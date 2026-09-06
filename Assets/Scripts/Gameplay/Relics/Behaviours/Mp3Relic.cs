using System;
using UnityEngine;

namespace Relic
{
    // mp3(패시브): 삽을 들면 머리 위에 비트 인디케이터가 표시되고, 비트에 맞춰 삽을 발사하면
    // 그 스윙의 파기 범위가 커진다. 판정 등급(Perfect/Good/Bad)이 머리 위에 뜨고 보정도 차등 적용된다.
    //
    // 모드 2종 — 도구스왑(ToolSwap) 동시 장착 여부로 갈린다:
    //   · 차징 모드(기본): 삽의 차징 방식 그대로 — 좌클릭 홀드로 차징, '떼는 순간'이 발사이자
    //     판정 시점. 풀차지를 원하는 만큼 유지할 수 있으므로 차지해 두고 비트에 맞춰 뗀다.
    //     연타 방지 쿨다운은 따로 없다 — 차징 시간(발사마다 다시 차지)이 그 역할을 한다.
    //   · 리듬 모드(도구스왑 장착 중): 차징 없이 좌클릭 '누르는 순간' 즉시 풀차지 발사·판정.
    //     스왑된 삽은 돌 파기 담당이라 차징 없는 속타 리듬이 어울린다. 연타 방지는
    //     swingCooldownBeats 최소 간격이 담당(차징이 없으므로 쿨다운이 유일한 억제).
    //   스왑 상태(ToolCapabilities.SwapTerrainRock)는 OnUpdate에서 폴링해 전환한다 —
    //   ToolSwap의 장착/해제 순서와 무관하게 항상 일치시키기 위해.
    //
    // 게이팅 ① 삽 전용: 곡괭이는 좌클릭 '홀드' 오토스윙(PickaxeStrategy, _attackCooldown 0.3초)이라
    //   플레이어가 스윙 타이밍을 통제할 수 없다. 0.3초 주기와 비트 간격(75BPM=0.8초)이 어긋나
    //   실력과 무관하게 Perfect/Good/Bad가 결정론적으로 반복될 뿐이라 판정 대상에서 제외했다.
    //   삽은 발사 1회 = 판정 1회.
    //
    // 게이팅 ② 스윙 순간 판정: 삽·곡괭이 스윙만 PlayerMining.RaiseDigSwing()을 발행하므로
    //   (드릴 대시는 발행 안 함) OnDigSwing 도달 = 멜리 스윙. 여기서 툴이 삽인지 한 번 더 거른다.
    //   RaiseDigSwing은 GetCurrentDigParameters '직전'에 호출되므로(SapStrategy/PickaxeStrategy)
    //   등급 set→consume이 같은 스윙에 정확히 대응한다.
    [Serializable]
    public class Mp3Relic : RelicBehaviour
    {
        private const int ShovelToolIndex = 1;

        [SerializeField] private float bpm = 75f;   // 느린 템포(0.8초 간격) — 차징 후 타이밍 맞출 여유를 준다
        // 판정별 파기 반경 배수. 기준(1.0) = 차징 방식의 풀차지 한 방.
        // Perfect만 1을 넘고 Good은 살짝 손해 — 박자를 '맞히면 이득'이 아니라
        // '정확히 맞혀야 본전 이상'이 되어 대충 쳐도 되는 구간이 없다.
        [SerializeField] private float perfectMultiplier = 1.1f;
        [SerializeField] private float goodMultiplier    = 0.8f;
        [SerializeField] private float badPenalty        = 0.2f;

        // 레벨은 위력이 아니라 '판정 관대함'을 키운다(레벨↑ = 창 넓어짐).
        [SerializeField] private float[] hitWindowPerLevel = { 0.12f, 0.15f, 0.18f };
        // [리듬 모드 전용] 스윙 최소 간격(비트 단위). 연타로 Bad를 흘리다 Perfect가 얻어걸리는 걸 막는다.
        // 비트 간격에 비례시켜 BPM을 바꿔도 체감이 유지된다. 0.5 = 한 비트에 최대 2번.
        // 1.0(한 비트당 1회)은 크게 빗나간 클릭이 다음 비트까지 잡아먹어 실수가 2박 손해가 된다.
        [SerializeField] private float swingCooldownBeats = 0.5f;
        // 인디케이터 배치. headOffset = 묶음 전체(머리 기준), labelOffset은 그 안에서의 상대 위치.
        // 판정선·트랙은 항상 묶음 원점(0,0)에 고정된다 — 판정선이 트랙에서 벗어나면 안 되기 때문.
        [SerializeField] private Vector3 headOffset  = new Vector3(0f, 1.7f, 0f);
        [SerializeField] private Vector3 labelOffset = new Vector3(0f, 0.55f, 0f);  // 판정 글자 시작 위치
        [SerializeField] private Vector2 indicatorScale = new Vector2(0.8f, 0.55f); // 크기 배율(x=가로, y=세로)

        private Mp3BeatIndicator _indicator;
        private Mp3Judgement _swingGrade = Mp3Judgement.Bad;
        private int _judgeFrame = -1;   // 이 프레임에 이미 판정을 확정했는지 (아래 JudgeThisFrame 참고)
        private bool _rhythmOn;         // 현재 리듬 모드를 켜뒀는지 (스왑 상태 폴링의 엣지 검출용)

        private float LvWindow() => hitWindowPerLevel[Mathf.Clamp(level - 1, 0, hitWindowPerLevel.Length - 1)];

        // 스윙 쿨다운(초). 패턴의 최소 간격(셋잇단 1/3박)보다 크면 '보이는데 못 치는' 노트가 생기므로
        // 그 80% 아래로 강제로 조인다 — 연타 억제는 유지하면서 어떤 패턴도 완주 가능하게 남긴다.
        private float SwingCooldownSeconds()
        {
            float maxBeats = Mp3BeatPatterns.MinGapBeats * 0.8f;
            float beats = Mathf.Min(Mathf.Max(0f, swingCooldownBeats), maxBeats);
            return (60f / Mathf.Max(1f, bpm)) * beats;
        }

        // 발사 프레임의 판정을 1회만 확정한다(같은 프레임 재호출은 no-op).
        //
        // 발사 프레임(차징 모드=Up, 리듬 모드=Down)에는 두 소비자가 등급을 읽는다:
        //   ① SapStrategy.FireMining(스윙·돌 파기) — RaiseDigSwing→OnDigSwing 경유
        //   ② Digger.TryDig(지형 파기) — GetCurrentDigParameters→ModifyDigParameters 경유
        // 둘의 Update 실행 순서는 프로젝트에 지정돼 있지 않아, Digger가 먼저 돌면
        // OnDigSwing이 아직 안 불린 상태라 '직전 스윙'의 등급으로 지형이 파인다.
        // 그래서 두 경로 모두 여기로 들어와 먼저 도착한 쪽이 판정을 확정하고,
        // 나중 쪽은 같은 값을 재사용한다 — 순서와 무관하게 '한 발사 = 한 판정'.
        private void JudgeThisFrame()
        {
            if (_judgeFrame == Time.frameCount) return;
            _judgeFrame = Time.frameCount;
            _swingGrade = _indicator.Judge();
        }

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            SpawnIndicator();
        }

        public override void OnLevelChanged(int lv)
        {
            level = lv;
            if (_indicator != null) _indicator.SetTuning(bpm, LvWindow());
        }

        public override void OnUnequip()
        {
            SetRhythm(false);   // mp3가 빠지면 삽은 무조건 일반 차징으로 복귀
            if (_indicator != null) UnityEngine.Object.Destroy(_indicator.gameObject);
            _indicator = null;
            _swingGrade = Mp3Judgement.Bad;
        }

        public override void OnUpdate()
        {
            // 도구스왑 상태와 리듬 모드를 동기화한다(엣지에서만 세팅).
            // 이벤트 대신 폴링인 이유: ToolSwap과 mp3는 서로를 모르는 독립 슬롯이라
            // 장착/해제 순서가 보장되지 않고, 스왑 플래그(ToolCapabilities.SwapTerrainRock)는
            // 구독 지점이 없는 정적 값이기 때문. 매프레임 bool 비교라 비용은 무시 가능.
            SetRhythm(ToolCapabilities.SwapTerrainRock);
        }

        private void SetRhythm(bool on)
        {
            if (_rhythmOn == on) return;
            _rhythmOn = on;
            ctx?.mining?.SetSapRhythmMode(on, on ? SwingCooldownSeconds() : 0f);
        }

        public override void OnDigSwing()
        {
            // 삽 발사 순간만 판정. 곡괭이·드릴 스윙은 표시도 보정도 없다(위 게이팅 ① 참고).
            if (_indicator == null || !IsShovelEquipped())
            {
                _swingGrade = Mp3Judgement.Bad;
                return;
            }

            JudgeThisFrame();   // Digger가 먼저 확정했으면 그 값을 그대로 쓴다
            _indicator.ShowJudgement(_swingGrade);
        }

        // 주의: 등급은 여기서 소모되지 않는다(위 주석 참고). OnDigSwing이 매 스윙 새로 세팅한다.
        public override void ModifyDigParameters(ref DigParameters p)
        {
            // 삽이 아닐 땐 관여하지 않는다. OnDigSwing은 곡괭이 스윙에도 불려 _swingGrade를
            // Bad로 초기화하므로, 이 가드가 없으면 곡괭이 파기에 badPenalty가 잘못 걸린다.
            if (!IsShovelEquipped()) return;

            // 발사 프레임이면 여기서도 판정을 확정할 수 있다 — Digger의 지형 파기가
            // SapStrategy(OnDigSwing)보다 먼저 도는 실행 순서를 방어한다(위 JudgeThisFrame 참고).
            // 발사 입력은 모드마다 다르다: 차징 모드=뗄 때(Up), 리듬 모드=누를 때(Down).
            // 발사 프레임이 아니면(DigRangePreview 등) 기존 등급을 그대로 쓴다.
            bool fireInput = _rhythmOn ? Input.GetMouseButtonDown(0) : Input.GetMouseButtonUp(0);
            if (_indicator != null && fireInput) JudgeThisFrame();

            // 등급은 '소모'하지 않고 다음 스윙까지 유지한다.
            // GetCurrentDigParameters는 한 번의 스윙에도 여러 번 불린다
            // (SapStrategy.FireMining, Digger의 실제 파기, DigRangePreview 등).
            // 첫 호출에서 등급을 리셋하면 정작 지형을 파는 Digger 호출이 Bad를 받아
            // Perfect를 쳐도 badPenalty로 파이는 버그가 난다.
            //
            // 배수는 판정별로 직접 지정한다(유도식 없음) — 1.0 = 풀차지 한 방 기준.
            // Bad도 그냥 통과시키지 않고 깎는다: 차징을 다 해놓고도 박자를 놓치면
            // 손해를 보게 해, 박자를 무시한 플레이가 mp3 미장착보다 나빠지게 만든다.
            float boost;
            if (_swingGrade == Mp3Judgement.Perfect)   boost = perfectMultiplier;
            else if (_swingGrade == Mp3Judgement.Good) boost = goodMultiplier;
            else                                       boost = badPenalty;

            p.RadiusMultiplier *= boost;
        }

        private bool IsShovelEquipped()
            => ctx != null && ctx.tools != null && ctx.tools.currentToolIndex == ShovelToolIndex;

        private void SpawnIndicator()
        {
            if (_indicator != null || ctx == null) return;

            Transform follow = (ctx.mining != null && ctx.mining.headBone != null)
                ? ctx.mining.headBone
                : ctx.player;
            if (follow == null) return;

            var go = new GameObject("Mp3BeatIndicator");
            _indicator = go.AddComponent<Mp3BeatIndicator>();
            _indicator.Init(follow, ctx.tools, bpm, LvWindow(), headOffset);
            _indicator.SetLayout(labelOffset, indicatorScale);
        }
    }
}
