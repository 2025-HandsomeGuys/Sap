using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Relic
{
    // mp3 유물의 타이밍 판정 등급. 값 순서는 "잘한 정도" 오름차순.
    public enum Mp3Judgement { Bad, Good, Perfect }

    // mp3 유물의 머리 위 비트 인디케이터. 프리팹 없이 코드로 생성(가로 트랙 + 판정선 + 판정 라벨).
    //
    // mp3 단독은 삽 일반 차징 그대로(떼는 순간=발사=판정), 도구스왑 동시 장착 시엔
    // 차징 없는 리듬 모드(누르는 순간=발사=판정)가 된다(Mp3Relic 주석 참고).
    // 어느 모드든 판정 시점이 존재하므로, 이 뷰는 삽을 든 동안 항상 켜져 있다.
    //
    // 구성:
    //   · 트랙 — 노트가 오른쪽에서 흘러와 판정선을 통과. 겹치는 순간이 온비트.
    //   · 판정선 — 비트마다 세로로 튀며 박자를 세고, 지금 치면 나올 등급을 색으로 노출.
    //   · 판정 구간 띠 — hitWindow를 x폭으로 환산해 "어디쯤에서 쳐야 하는지"를 직접 표시.
    //
    // 비트 클럭은 전역 연속(Time.time 기반). 입력에 따라 리셋하지 않는다 —
    //   리셋하면 판정선이 세던 박자와 노트가 어긋나고, 판정이 실력이 아니라 고정 절차가 된다.
    //
    // 좌표계: 노트 x = (비트까지 남은 비트수 / LeadBeats) * HalfW.
    //   판정 창(초)도 같은 비율(sec2x)로 환산되므로 띠 폭이 곧 실제 창이다.
    //
    // 링(원) 방식에서 선 방식으로 바꾼 이유: 원은 수축 후 곧바로 커져서 판정 창의 후반부(비트 직후)를
    //   크기만으로 읽을 수 없었다. 가로 트랙은 노트가 판정선을 '통과'하므로 전/후가 대칭으로 보인다.
    //
    // 표시 게이팅: 삽(toolIndex=1)일 때만 보인다. 곡괭이는 좌클릭 홀드 오토스윙(0.3초 주기)이라
    //   플레이어가 타이밍을 통제할 수 없어 mp3 대상에서 제외됐다(Mp3Relic 주석 참고).
    public class Mp3BeatIndicator : MonoBehaviour
    {
        private Transform _follow;
        private ToolController _tools;
        private float _bpm = 75f;
        private float _hitWindow = 0.13f;
        private float _startTime;

        private LineRenderer _judgeLine;  // 판정선(여기서 쳐야 한다) — 비트마다 펄스
        private LineRenderer _rail;       // 트랙 바닥선
        private LineRenderer _goodBand;   // Good 판정 구간 띠
        private LineRenderer _perfBand;   // Perfect 판정 구간 띠
        private LineRenderer[] _notes;    // 노트 렌더러 풀(패턴에 따라 보이는 개수가 달라진다)

        // 다가올 노트 시각(마디 시작부터의 '박' 단위 절대 좌표). 마디 단위로 미리 생성해 둔다.
        // 초가 아니라 박으로 들고 있어야 BPM을 바꿔도 이미 예약된 패턴이 그대로 따라온다.
        private readonly List<float> _onsets = new List<float>();
        private float _generatedUntilBeat;
        private float _lastPulsedOnset = float.NegativeInfinity;
        private TextMeshPro _label;       // Perfect/Good/Bad 팝업

        private const float HalfW = 0.9f;     // 트랙 반폭(월드 유닛, scale 적용 전)
        private const float LeadBeats = 1.5f; // 판정선→오른쪽 끝 = 몇 비트 뒤인지(=노트 접근 시간)
        // 판정선 x 위치(반폭 대비 비율). 태고의 달인처럼 왼쪽에 붙인다 —
        // 판정선 왼쪽은 '이미 지나간' 노트만 지나는 죽은 공간이라 판정 창 꼬리만큼만 남기면 된다.
        // 덕분에 같은 트랙 폭에서 노트 접근 구간(오른쪽)이 1.6배로 늘어 리듬 읽기가 쉬워진다.
        private const float JudgeXFrac = -0.6f;
        private const float NoteH = 0.22f;    // 노트 세로 길이
        private const float JudgeH = 0.3f;    // 판정선 세로 길이
        private const float BandH = 0.2f;     // 판정 구간 띠 두께
        // 렌더러 풀 크기. 한 마디 최대 6노트 × 보이는 구간(LeadBeats+여유)이면 충분하다.
        private const int NotePoolSize = 12;
        private const int ShovelToolIndex = 1;

        // hitWindow 중 Perfect로 인정할 비율. 0.4 → Lv1(0.12초) 기준 ±0.048초.
        private const float PerfectRatio = 0.4f;

        private readonly Color _railColor   = new Color(0.35f, 0.8f, 1f, 0.35f);
        private readonly Color _noteColor   = new Color(0.8f, 0.92f, 1f, 0.95f);
        private readonly Color _goodZone    = new Color(0.5f, 1f, 1f, 1f);
        private readonly Color _perfectZone = new Color(1f, 0.85f, 0.25f, 1f);
        private readonly Color _hitColor    = new Color(0.4f, 1f, 0.5f, 1f);
        private float _beatFlash;  // 비트 경계 펄스(0..1 감쇠)
        private float _hitFlash;   // 판정 성공 플래시(0..1 감쇠)

        // 삽엔 차징이 없어졌으므로 기존 가로 차징 바를 숨긴다. 파괴 시 원상복구용 상태도 보관.
        private MiningChargeBarUI _chargeBar;
        private bool _chargeBarWasEnabled;

        // 배치·크기. 매 프레임 반영되므로 플레이 중 Hierarchy에서 이 컴포넌트를 골라
        // 값을 드래그하면 즉시 움직인다 → 눈으로 보며 맞춘 뒤 Mp3.asset에 옮겨 적는 워크플로.
        //
        // Mp3Relic이 OnEquip에서 에셋 값으로 초기화하지만, 여기 값이 '살아있는' 소스다.
        // (RelicManager가 so.behaviour.Clone()으로 복제본을 쓰기 때문에 플레이 중 에셋 수정은
        //  이미 장착된 유물에 닿지 않는다. 그래서 런타임 튜닝은 반드시 이쪽에서 한다.)
        [Header("배치 — 플레이 중 조정 가능")]
        [Tooltip("인디케이터 전체 위치(머리뼈 기준)")]
        [SerializeField] private Vector3 headOffset = new Vector3(0f, 1.7f, 0f);
        [Tooltip("판정 글자(PERFECT/GOOD/BAD) 시작 위치 — 트랙 원점 기준 상대")]
        [SerializeField] private Vector3 labelOffset = new Vector3(0f, 0.55f, 0f);
        [Tooltip("인디케이터 크기 배율(x=가로, y=세로). 1 = 원래 크기")]
        [SerializeField] private Vector2 scale = new Vector2(0.8f, 0.55f);

        // 판정 라벨 연출
        private const float LabelLife = 0.4f;
        private const float LabelRise = 0.4f;
        private const float LabelFontSize = 3.5f;
        private float _labelTimer;
        private Color _labelColor;
        private float _labelScale = 1f;

        private float BeatInterval => 60f / Mathf.Max(1f, _bpm);
        private bool ShovelEquipped => _tools != null && _tools.currentToolIndex == ShovelToolIndex;

        public void Init(Transform follow, ToolController tools, float bpm, float hitWindow, Vector3 offset)
        {
            _follow = follow;
            _tools = tools;
            _bpm = bpm;
            _hitWindow = hitWindow;
            headOffset = offset;
            _startTime = Time.time;

            _chargeBar = FindFirstObjectByType<MiningChargeBarUI>();
            if (_chargeBar != null) _chargeBarWasEnabled = _chargeBar.enabled;
        }

        // 기존 가로 차징 바 억제. 삽은 리듬 모드라 차징 자체가 없으므로 바가 뜰 이유가 없다.
        // 곡괭이·드릴은 여전히 차징하므로 삽일 때만 끈다.
        // 컴포넌트만 끄면 barContainer가 마지막 상태로 남으므로 컨테이너도 함께 내린다.
        private void SuppressChargeBar(bool suppress)
        {
            if (_chargeBar == null) return;

            bool want = _chargeBarWasEnabled && !suppress;
            if (_chargeBar.enabled == want) return;

            _chargeBar.enabled = want;
            if (suppress && _chargeBar.barContainer != null)
                _chargeBar.barContainer.SetActive(false);
        }

        // mp3 해제 시 원래 차징 바를 되살린다. 껐던 것만 되돌리고, 원래 꺼져 있었다면 그대로 둔다.
        private void RestoreChargeBar()
        {
            if (_chargeBar == null) return;
            _chargeBar.enabled = _chargeBarWasEnabled;
            _chargeBar = null;
        }

        // 에셋(Mp3Relic)의 배치·크기 값을 초기값으로 심는다. 이후 튜닝은 이 컴포넌트에서 직접 한다.
        public void SetLayout(Vector3 label, Vector2 sizeScale)
        {
            labelOffset = label;
            scale = sizeScale;
        }

        public void SetTuning(float bpm, float hitWindow)
        {
            _bpm = bpm;
            _hitWindow = hitWindow;
        }

        private float CurrentBeat => (Time.time - _startTime) / BeatInterval;

        // 필요한 지점까지 마디를 미리 뽑아 둔다. 마디마다 패턴을 무작위로 골라 이어 붙인다.
        // 한 번 생성된 노트는 바뀌지 않으므로, 플레이어가 트랙에서 본 노트와 판정 대상이 항상 일치한다.
        private void EnsureOnsets(float untilBeat)
        {
            while (_generatedUntilBeat < untilBeat)
            {
                float[] pattern = Mp3BeatPatterns.Random();
                for (int i = 0; i < pattern.Length; i++)
                    _onsets.Add(_generatedUntilBeat + pattern[i]);

                _generatedUntilBeat += Mp3BeatPatterns.MeasureBeats;
            }

            // 지나간 노트 정리. 판정 창 후반부가 아직 열려 있을 수 있어 2박 여유를 둔다.
            float cutoff = CurrentBeat - 2f;
            int drop = 0;
            while (drop < _onsets.Count && _onsets[drop] < cutoff) drop++;
            if (drop > 0) _onsets.RemoveRange(0, drop);
        }

        // 현재 시각이 가장 가까운 '노트'에서 몇 초 떨어져 있는지.
        // 균등한 메트로놈이 아니라 패턴이 실제로 깔아 둔 노트를 기준으로 잰다.
        public float BeatDistanceSeconds()
        {
            float beatPos = CurrentBeat;
            EnsureOnsets(beatPos + LeadBeats + Mp3BeatPatterns.MeasureBeats);

            float bestBeats = float.MaxValue;
            for (int i = 0; i < _onsets.Count; i++)
            {
                float d = Mathf.Abs(_onsets[i] - beatPos);
                if (d < bestBeats) bestBeats = d;
            }

            if (bestBeats == float.MaxValue) return float.MaxValue;
            return bestBeats * BeatInterval;
        }

        public Mp3Judgement Judge()
        {
            float d = BeatDistanceSeconds();
            if (d <= _hitWindow * PerfectRatio) return Mp3Judgement.Perfect;
            if (d <= _hitWindow) return Mp3Judgement.Good;
            return Mp3Judgement.Bad;
        }

        // 스윙 결과를 머리 위에 띄운다. 연타 시 라벨 1개를 재사용하므로 최신 판정이 이전 것을 덮어쓴다.
        public void ShowJudgement(Mp3Judgement j)
        {
            switch (j)
            {
                case Mp3Judgement.Perfect:
                    SetLabel("PERFECT", _perfectZone, 1.15f);
                    _hitFlash = 1f;
                    break;
                case Mp3Judgement.Good:
                    SetLabel("GOOD", _goodZone, 1f);
                    _hitFlash = 0.55f;
                    break;
                default:
                    SetLabel("BAD", new Color(0.6f, 0.6f, 0.62f, 1f), 0.8f);
                    break;
            }
        }

        private void SetLabel(string text, Color c, float scaleMul)
        {
            if (_label == null) return;
            _label.text = text;
            _labelColor = c;
            _labelScale = scaleMul;
            _labelTimer = LabelLife;
        }

        private void Awake()
        {
            // sortingOrder: 띠(배경) < 레일 < 노트 < 판정선 < 라벨.
            _goodBand  = MakeLine("BeatGoodBand", BandH, 118);
            _perfBand  = MakeLine("BeatPerfectBand", BandH, 119);
            _rail      = MakeLine("BeatRail", 0.03f, 120);
            _judgeLine = MakeLine("BeatJudgeLine", 0.06f, 123);

            _notes = new LineRenderer[NotePoolSize];
            for (int i = 0; i < NotePoolSize; i++)
                _notes[i] = MakeLine("BeatNote" + i, 0.05f, 122);

            _label = MakeLabel();
        }

        private LineRenderer MakeLine(string objName, float width, int order)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.positionCount = 2;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.sortingOrder = order;
            lr.textureMode = LineTextureMode.Stretch;
            return lr;
        }

        private TextMeshPro MakeLabel()
        {
            var go = new GameObject("BeatJudgementLabel");
            go.transform.SetParent(transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = string.Empty;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = LabelFontSize;
            tmp.sortingOrder = 124;   // 트랙(118~123)보다 위 — 안 주면 지형 스프라이트에 가린다.
            tmp.rectTransform.sizeDelta = new Vector2(6f, 1.5f);
            tmp.rectTransform.localPosition = labelOffset;
            tmp.enabled = false;
            return tmp;
        }

        // 세로 막대(x 고정, y 대칭). 노트·판정선용.
        private void SetVertical(LineRenderer lr, float x, float half, Color c)
        {
            lr.startColor = c;
            lr.endColor = c;
            lr.SetPosition(0, new Vector3(x, -half, 0f));
            lr.SetPosition(1, new Vector3(x, half, 0f));
        }

        // 가로 막대(y=0, center 기준 좌우 halfWidth). 레일·판정 구간 띠용.
        private void SetHorizontal(LineRenderer lr, float center, float halfWidth, Color c)
        {
            lr.startColor = c;
            lr.endColor = c;
            lr.SetPosition(0, new Vector3(center - halfWidth, 0f, 0f));
            lr.SetPosition(1, new Vector3(center + halfWidth, 0f, 0f));
        }

        private void SetVisible(bool on)
        {
            _judgeLine.enabled = on;
            _rail.enabled = on;
            _goodBand.enabled = on;
            _perfBand.enabled = on;
            for (int i = 0; i < _notes.Length; i++) _notes[i].enabled = on;
            if (!on)
            {
                _label.enabled = false;
                _labelTimer = 0f;
            }
        }

        private void LateUpdate()
        {
            // 삽일 때만 이 UI가 파기 타이밍 표시를 담당하므로, 그때만 기존 차징 바를 억제한다.
            SuppressChargeBar(ShovelEquipped);

            // 삽이 아닐 땐 통째로 숨긴다(비트 클럭은 Time.time 기반이라 계속 흘러도 무방).
            if (!ShovelEquipped)
            {
                if (_judgeLine.enabled) SetVisible(false);
                return;
            }

            if (!_judgeLine.enabled) SetVisible(true);

            // 머리 위를 따라다니되 회전은 무시(부모로 붙이지 않고 월드 위치만 추적).
            if (_follow != null)
                transform.position = _follow.position + headOffset;

            // 크기는 루트 스케일로 적용. x/y를 따로 두어 트랙을 납작하게 만들 수 있다.
            // 인스펙터 값 변경을 즉시 반영하려고 매 프레임 대입한다(비용 무시 가능).
            transform.localScale = new Vector3(Mathf.Max(0.01f, scale.x), Mathf.Max(0.01f, scale.y), 1f);

            float interval = BeatInterval;
            float beatPos = CurrentBeat;
            EnsureOnsets(beatPos + LeadBeats + Mp3BeatPatterns.MeasureBeats);

            // 노트가 판정선을 지나는 순간 펄스. 균등 박이 아니라 패턴을 따라 뛴다.
            float passed = float.NegativeInfinity;
            for (int i = 0; i < _onsets.Count; i++)
                if (_onsets[i] <= beatPos && _onsets[i] > passed) passed = _onsets[i];

            if (passed > _lastPulsedOnset)
            {
                _lastPulsedOnset = passed;
                _beatFlash = 1f;
            }

            _beatFlash = Mathf.Max(0f, _beatFlash - Time.deltaTime * 4f);
            _hitFlash  = Mathf.Max(0f, _hitFlash  - Time.deltaTime * 2.5f);

            DrawJudgeLine();
            DrawTrack(interval, beatPos);
            UpdateLabel();
        }

        // 판정선 = 박자 카운터 겸 등급 표시. 비트마다 세로로 튀고,
        // 지금 치면 무슨 등급인지 색으로 그대로 노출한다.
        private void DrawJudgeLine()
        {
            Mp3Judgement zone = Judge();
            Color judgeCol = zone == Mp3Judgement.Perfect ? _perfectZone
                           : zone == Mp3Judgement.Good    ? _goodZone
                           : _railColor;
            if (zone == Mp3Judgement.Bad) judgeCol.a = 0.85f;
            if (_hitFlash > 0f) judgeCol = Color.Lerp(judgeCol, _hitColor, _hitFlash);
            SetVertical(_judgeLine, HalfW * JudgeXFrac, JudgeH * (1f + _beatFlash * 0.35f), judgeCol);
        }

        // 트랙·판정 구간 띠·노트.
        private void DrawTrack(float interval, float beatPos)
        {
            // 판정선은 왼쪽(JudgeXFrac), 노트는 판정선→오른쪽 끝 구간(span)을 LeadBeats에 걸쳐 이동.
            float judgeX = HalfW * JudgeXFrac;
            float span = HalfW - judgeX;

            // 판정 창(초) → 트랙 x폭. 노트 이동 속도(span/LeadBeats)와 같은 비율이어야
            // 띠에 겹친 순간 = 실제 판정 창 안이 성립한다.
            float sec2x = span / (LeadBeats * interval);
            float goodX = Mathf.Min(_hitWindow * sec2x, span);
            float perfX = Mathf.Min(_hitWindow * PerfectRatio * sec2x, span);

            SetHorizontal(_rail, 0f, HalfW, _railColor);

            // 판정 구간 띠 — "어디쯤 와야 치는지"를 직접 보여주는 표식. 판정선 중심.
            Color goodBandCol = _goodZone; goodBandCol.a = 0.22f;
            Color perfBandCol = _perfectZone; perfBandCol.a = 0.32f + _beatFlash * 0.25f;
            SetHorizontal(_goodBand, judgeX, goodX, goodBandCol);
            SetHorizontal(_perfBand, judgeX, perfX, perfBandCol);

            // 노트: 오른쪽 끝에서 다가와 판정선을 지나고, 왼쪽 남은 꼬리 구간에서 빠져나간다.
            // 이미 지나간 노트도 잠깐 남겨야 비트 직후의 판정 창 후반부가 보인다.
            int slot = 0;
            for (int i = 0; i < _onsets.Count && slot < _notes.Length; i++)
            {
                float x = judgeX + ((_onsets[i] - beatPos) / LeadBeats) * span;
                if (x > HalfW || x < -HalfW) continue;

                var lr = _notes[slot++];
                if (!lr.enabled) lr.enabled = true;

                // 판정선에 가까울수록 밝고 길게 — 접근감. 기준점은 트랙 중앙이 아니라 판정선.
                float near = 1f - Mathf.Clamp01(Mathf.Abs(x - judgeX) / span);
                Color c = _noteColor;
                c.a = 0.3f + 0.7f * near;
                SetVertical(lr, x, NoteH * (0.7f + 0.5f * near), c);
            }

            // 남는 풀은 끈다(패턴마다 보이는 노트 수가 다르다).
            for (; slot < _notes.Length; slot++)
                if (_notes[slot].enabled) _notes[slot].enabled = false;
        }

        private void UpdateLabel()
        {
            if (_labelTimer <= 0f)
            {
                if (_label.enabled) _label.enabled = false;
                return;
            }

            _labelTimer -= Time.deltaTime;
            if (!_label.enabled) _label.enabled = true;

            float k = 1f - Mathf.Clamp01(_labelTimer / LabelLife); // 0(등장) → 1(소멸)

            _label.rectTransform.localPosition = labelOffset + new Vector3(0f, k * LabelRise, 0f);

            // 등장 순간 살짝 크게 팝 → 원래 크기로.
            // 루트가 y만 눌린 비균등 스케일이라 그대로 두면 글자가 납작해진다 —
            // 라벨 로컬 y에 x/y 비율을 곱해 글자만 원래 비율로 되돌린다.
            float pop = Mathf.Lerp(1.25f, 1f, Mathf.Clamp01(k / 0.2f));
            float aspectFix = scale.y > 0.01f ? scale.x / scale.y : 1f;
            _label.rectTransform.localScale = new Vector3(1f, aspectFix, 1f) * (_labelScale * pop);

            // 후반 40%에서 페이드 아웃.
            Color c = _labelColor;
            c.a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            _label.color = c;
        }

        private void OnDestroy()
        {
            RestoreChargeBar();
            DestroyMat(_judgeLine);
            DestroyMat(_rail);
            DestroyMat(_goodBand);
            DestroyMat(_perfBand);
            if (_notes != null)
                for (int i = 0; i < _notes.Length; i++) DestroyMat(_notes[i]);
        }

        private void DestroyMat(LineRenderer lr)
        {
            if (lr != null && lr.material != null) Destroy(lr.material);
        }
    }
}
