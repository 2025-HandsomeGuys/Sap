// @tags: coin, ui, chart, graph, animation, maskable-graphic, zoom, reveal
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Market;

namespace Coin.UI
{
    /// <summary>
    /// 실시간 라인 차트 + 베팅 공개 연출 (설계 §7.5).
    /// 평소엔 영역을 꽉 채워(전체 폭·높이) 살아있는 시세표처럼 보인다.
    /// 베팅하면 끝점(시세 tip)으로 **줌인** → **가속 드럼롤**: "띠… 띠… 띠, 띠," 간격이 점점 짧아지고
    /// 피치가 올라가며 끝점이 위/아래 한 걸음씩(Anticipate) → 막판 **버스트** "띠띠띠띠띠띠!" 기관총 연타
    /// → 숨죽이는 **정적** → (브레이킹) 잠깐 멈춰 뉴스 대기(NewsHold) → 스프링 오버슈트로 결과 확정(Reveal)
    /// → 1초 안에 평소 그래프로 **원상복귀**. 연출 스킵 없음.
    /// 빈 UI 오브젝트에 부착하면 RectTransform 영역에 라인을 그린다.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class CoinChartUI : MaskableGraphic
    {
        [Header("라인")]
        [SerializeField] private float lineThickness = 8f;
        [SerializeField] private int maxPoints = 40;
        [Tooltip("위아래 여백(영역 높이 비율). 곡선이 가장자리에 닿지 않게")]
        [SerializeField] private float verticalPadding = 0.08f;

        [Header("격자(그리드)")]
        [Tooltip("배경 격자. 줌 연출 시 라인과 같은 변환을 타고 벌어진다")]
        [SerializeField] private bool showGrid = true;
        [Tooltip("세로선(시간축) 칸 수")]
        [SerializeField] private int gridColumns = 8;
        [Tooltip("가로선(가격축) 칸 수")]
        [SerializeField] private int gridRows = 5;
        [Tooltip("은은한 배경 격자선 두께·색(알파 0이면 라인 숨김, 도트만)")]
        [SerializeField] private float gridThickness = 1f;
        [SerializeField] private Color gridColor = new Color(0.34f, 0.52f, 0.82f, 0.05f);
        [Tooltip("교차점 네온 픽셀 도트 — 레트로 게임 HUD 느낌. 그리드 성격은 이게 잡는다")]
        [SerializeField] private bool showGridDots = true;
        [SerializeField] private float gridDotSize = 3f;
        [SerializeField] private Color gridDotColor = new Color(0.30f, 0.78f, 0.92f, 0.22f);

        [Header("연출 타이밍(초)")]
        [SerializeField] private float zoomInDuration = 0.28f;
        [Tooltip("가속 드럼롤(빌드업) 첫 '띠' 간격(초) — 이후 띠마다 간격이 줄어 심박 몰아치듯 빨라진다")]
        [SerializeField] private float tickIntervalStart = 0.45f;
        [Tooltip("가속 드럼롤(빌드업) 마지막 '띠' 간격(초) — 여기서 버스트 연타로 넘어간다")]
        [SerializeField] private float tickIntervalEnd = 0.11f;
        [Tooltip("빌드업 '띠' 횟수 — 다 세면 버스트 연타 시작")]
        [SerializeField] private int tickCount = 7;
        [Tooltip("막판 버스트 '띠띠띠띠' 연타 횟수 — 빌드업 직후 기관총처럼 몰아친다. 0이면 버스트 없음")]
        [SerializeField] private int burstTickCount = 6;
        [Tooltip("버스트 연타 간격(초) — 낮을수록 미친 듯이 띠띠띠띠")]
        [SerializeField] private float burstTickInterval = 0.05f;
        [Tooltip("마지막 '띠' 후 결과 공개까지 숨죽이는 정적(초) — 드럼롤이 뚝 끊긴 무음의 정점")]
        [SerializeField] private float preRevealSilence = 0.26f;
        [Tooltip("값이 결과로 튀어 확정되는 시간(스프링)")]
        [SerializeField] private float revealDuration = 0.7f;
        [Tooltip("결과 이동 완료 후 평소 전체뷰 그래프로 원상복귀까지 총 시간(초) — 잠깐 머묾(Hold)+줌아웃 합")]
        [SerializeField] private float returnToIdleDuration = 1f;
        [Tooltip("뉴스 대기(브레이킹) 중 외부 공개 신호가 없을 때 자동 공개까지 최대 대기(안전망)")]
        [SerializeField] private float newsHoldMaxWait = 10f;

        [Header("줌 / 떨림")]
        [Tooltip("연출 시 끝점 주변 확대 배율(1=확대 없음)")]
        [SerializeField] private float zoomMax = 2.6f;
        [Tooltip("기대 단계 최대 떨림 폭(영역 높이 비율)")]
        [SerializeField] private float anticipateShake = 0.05f;
        [Tooltip("대기 중 끝점 미세 호흡 폭(영역 높이 비율). 0이면 정지")]
        [SerializeField] private float idleShimmer = 0.008f;

        private readonly List<float> _values = new List<float>();

        private enum Phase { Idle, ZoomIn, Anticipate, NewsHold, Reveal, Hold, ZoomOut }
        private Phase _phase = Phase.Idle;
        private bool Focusing => _phase != Phase.Idle;

        // 원상복귀(returnToIdleDuration) 배분: 결과에 잠깐 머문 뒤(40%) 줌아웃(60%).
        private float HoldDuration => returnToIdleDuration * 0.4f;
        private float ZoomOutDuration => returnToIdleDuration * 0.6f;

        // 공개 파라미터
        private float _revealFrom, _revealTo;
        private bool _revealExtreme;
        private bool _awaitNews;          // 흔들림 뒤 결과를 바로 공개하지 않고 멈춰(NewsHold) 뉴스를 먼저 띄우는 모드(브레이킹)
        private float _revealSwing = 1f;   // |가격 변동폭| 0~1 — 진동·오버슈트 강도 스케일
        private float _overshoot = 1.4f;   // 스프링 오버슈트 강도(승/극단일수록 큼)
        private float _pivotValue;         // 줌 피벗이 되는 값(베팅 시작 시세)

        // 연출 상태
        private float _phaseT;             // 현 페이즈 경과 초
        private float _zoom;               // 현재 줌 진행도(0=전체뷰, 1=끝점 포커스)
        private float _zoomStart;          // 줌인 시작 시점의 줌(연속 베팅 대비)
        private float _shakePixels;        // 끝점 시각 전용 흔들림(픽셀)
        private float _animClock;          // 노이즈용 누적 시간
        private float _tickTimer;          // 다음 '띠'까지 남은 시간
        private int _anticipateTicks;      // 발화한 '띠' 수
        private float _stepFrom, _stepTo;  // '띠' 스텝 이동 구간(픽셀 오프셋)
        private float _stepT;              // 현재 스텝 경과 초
        private float _idlePhase;          // 대기 호흡 시간
        private float _frozenLo, _frozenHi; // 연출 중 고정 세로 범위(끝점만 움직이게)
        private float _seedA, _seedB;       // 노이즈 시드 — Awake에서 초기화(직렬화 중 Random 호출 금지)

        // 도파민 연출(감쇠형 펀치) — '띠'/공개/착지 순간에 세팅되고 Update에서 지수 감쇠한다.
        private float _stepDur = 0.12f;    // 현재 스텝 이동 시간(버스트에선 짧게 — 드르륵 떨림)
        private float _dotPunch;           // 머리점 확대 펀치('띠'·착지 순간 커졌다 줄어든다)
        private float _zoomPunch;          // 줌 펀치 — 비트에 맞춰 화면이 살짝씩 튄다
        private float _flashAlpha;         // 전면 플래시 알파(버스트 스트로브·공개 릴리즈·착지 임팩트)
        private Color _flashColor = Color.white;

        public event Action OnAnticipatePeak;   // 흔들림 정점에서 멈춘 순간(브레이킹): 뉴스를 먼저 띄우라는 신호
        public event Action<bool, float> OnAnticipateTick; // ('띠' 스텝 위 방향?, 전체 진행도 0~1) — 시세 숫자 롤링 등 동기 연출용
        public event Action<float> OnRevealValue; // 결과 이동(Reveal) 중 매 프레임 현재 보간 가격 — 시세 텍스트가 그래프와 함께 움직이게
        public event Action OnRevealComplete;   // 결과 이동(Reveal) 완료
        public event Action OnZoomOutComplete;  // 줌아웃 후 전체뷰로 복귀한 순간(상장폐지 후 새 코인 교체 타이밍)

        // 차트는 표시 전용 — 클릭/레이캐스트 대상에서 제외.
        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            _seedA = UnityEngine.Random.value * 100f;
            _seedB = UnityEngine.Random.value * 100f;
        }

        /// <summary>정적 히스토리로 차트를 채운다(애니메이션 없음).</summary>
        public void SetHistory(IReadOnlyList<float> values)
        {
            _values.Clear();
            if (values != null)
            {
                int start = Mathf.Max(0, values.Count - maxPoints);
                for (int i = start; i < values.Count; i++) _values.Add(values[i]);
            }
            _phase = Phase.Idle;
            _zoom = 0f;
            _shakePixels = 0f;
            _idlePhase = 0f;
            SetVerticesDirty();
        }

        /// <summary>
        /// 마지막 캔들을 oldPrice→newPrice로 줌인하며 공개한다.
        /// swing(|변동폭| 0~1)이 클수록 진동·스프링 탄력이 커진다 — 작은 변동은 차분, 떡상/떡락은 격렬.
        /// awaitNews=true면 흔들림(Anticipate) 뒤 결과를 바로 공개하지 않고 **잠깐 멈춰서**(NewsHold) OnAnticipatePeak를
        /// 발동한다 — 이때 브레이킹 뉴스를 먼저 띄우고, 뉴스가 끝나면 TriggerReveal()로 결과 이동(떡상/떡락)을 시작한다.
        /// </summary>
        public void PlayReveal(float oldPrice, float newPrice, bool win, bool extreme, float swing, bool awaitNews)
        {
            _revealFrom = oldPrice;
            _revealTo = newPrice;
            _revealExtreme = extreme;
            _awaitNews = awaitNews;
            _revealSwing = Mathf.Clamp01(swing);
            _pivotValue = oldPrice;
            // 승/극단=탄력, 패=풀죽은 느낌 + 변동폭이 클수록 더 크게 튄다.
            _overshoot = (win || extreme)
                ? Mathf.Lerp(1.05f, 2.4f, _revealSwing)
                : Mathf.Lerp(0.6f, 0.95f, _revealSwing);
            // 차트 자체 페이즈 타이밍을 사용한다(별도 duration 인자 없음).

            // 보간될 새 점을 oldPrice(플랫)로 추가
            _values.Add(oldPrice);
            if (_values.Count > maxPoints) _values.RemoveAt(0);

            // 연출 동안 오버뷰 세로 범위 고정(끝점만 움직이게). 결과값 + 스프링 오버슈트 여유까지 포함해 잘림 방지.
            float overshootGuard = newPrice + (newPrice - oldPrice) * 0.4f;
            ComputeRange(out _frozenLo, out _frozenHi, includeExtra: overshootGuard);

            _phase = Phase.ZoomIn;
            _phaseT = 0f;
            _zoomStart = _zoom;
            _animClock = 0f;
            _tickTimer = 0f;
            _anticipateTicks = 0;
            _stepFrom = _stepTo = 0f;
            _stepT = 0f;
            _stepDur = 0.12f;
            _shakePixels = 0f;
            _dotPunch = _zoomPunch = _flashAlpha = 0f;
            SetVerticesDirty();
        }

        // ===================================================
        // 페이즈 머신
        // ===================================================
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _animClock += dt;
            float h = GetPixelAdjustedRect().height;

            // 펀치·플래시 감쇠 — 페이즈와 무관하게 자연 소멸.
            if (_dotPunch > 0.001f || _zoomPunch > 0.001f || _flashAlpha > 0.004f)
            {
                _dotPunch *= Mathf.Exp(-9f * dt);
                _zoomPunch *= Mathf.Exp(-10f * dt);
                _flashAlpha *= Mathf.Exp(-8f * dt);
                SetVerticesDirty();
            }

            switch (_phase)
            {
                case Phase.Idle:       StepIdle(dt, h);       break;
                case Phase.ZoomIn:     StepZoomIn(dt, h);     break;
                case Phase.Anticipate: StepAnticipate(dt, h); break;
                case Phase.NewsHold:   StepNewsHold(dt, h);   break;
                case Phase.Reveal:     StepReveal(dt, h);     break;
                case Phase.Hold:       StepHold(dt);          break;
                case Phase.ZoomOut:    StepZoomOut(dt);       break;
            }
        }

        private void StepIdle(float dt, float h)
        {
            bool dirty = false;
            if (_zoom > 0f)
            {
                _zoom = Mathf.MoveTowards(_zoom, 0f, dt / Mathf.Max(0.01f, ZoomOutDuration));
                dirty = true;
            }
            if (_values.Count >= 2 && idleShimmer > 0f)
            {
                _idlePhase += dt;
                _shakePixels = (Mathf.Sin(_idlePhase * 2.5f) + Mathf.Sin(_idlePhase * 6.3f) * 0.35f) * idleShimmer * h;
                dirty = true;
            }
            if (dirty) SetVerticesDirty();
        }

        private void StepZoomIn(float dt, float h)
        {
            _phaseT += dt;
            float k = Mathf.Clamp01(_phaseT / Mathf.Max(0.0001f, zoomInDuration));
            _zoom = Mathf.Lerp(_zoomStart, 1f, EaseOutCubic(k));
            _shakePixels = 0f; // 줌인은 조용히 — 움직임은 Anticipate의 '띠' 스텝부터
            SetVerticesDirty();
            if (k >= 1f) { _phase = Phase.Anticipate; _phaseT = 0f; }
        }

        // 기대(가속 드럼롤): "띠" 간격이 점점 짧아지고 피치가 올라간다 — 심박이 몰아치는 느낌.
        // 빌드업(tickCount)이 끝나면 곧장 버스트(burstTickCount) — 기관총처럼 "띠띠띠띠" 연타하며 피치가 더 치솟는다.
        // 소리와 끝점 스텝 이동이 같은 순간에 발화하고, 뒤로 갈수록 진폭도 커진다.
        // 다 세면 잠깐의 정적(preRevealSilence) — 드럼롤이 뚝 끊긴 무음의 정점 — 후 결과 공개.
        private void StepAnticipate(float dt, float h)
        {
            _phaseT += dt;
            _zoom = 1f;
            _values[_values.Count - 1] = _revealFrom; // 값은 고정, 움직임은 픽셀로만

            int buildup = Mathf.Max(1, tickCount);
            int total = buildup + Mathf.Max(0, burstTickCount);

            _tickTimer -= dt;
            if (_tickTimer <= 0f)
            {
                if (_anticipateTicks < total)
                {
                    bool inBurst = _anticipateTicks >= buildup;
                    // 피치·진폭: 빌드업은 1.0→1.35 상승 + 진폭 점증, 버스트는 1.35→1.6 + 폭주 진폭(랜덤 과대 스윙).
                    float pitch, ampScale;
                    if (inBurst)
                    {
                        float bp = total - buildup > 1 ? (_anticipateTicks - buildup) / (total - buildup - 1f) : 1f;
                        pitch = Mathf.Lerp(1.35f, 1.6f, bp);
                        ampScale = UnityEngine.Random.Range(1.4f, 2.3f); // 슬롯이 폭주하듯 크게 널뛴다
                        _flashColor = Color.white;                        // 비트에 맞춘 스트로브
                        _flashAlpha = Mathf.Max(_flashAlpha, Mathf.Lerp(0.05f, 0.13f, bp));
                    }
                    else
                    {
                        float p = buildup > 1 ? _anticipateTicks / (buildup - 1f) : 1f;
                        pitch = Mathf.Lerp(1f, 1.35f, p);
                        ampScale = Mathf.Lerp(0.4f, 1f, p);
                    }
                    CoinSfx.Play(CoinSfx.Tick, pitch); // "띠" — 스텝 이동·펀치와 동시 발화
                    _anticipateTicks++;
                    _tickTimer = _anticipateTicks < total
                        ? NextTickGap(buildup)
                        : Mathf.Max(0f, preRevealSilence); // 마지막 띠 뒤엔 숨죽이는 정적
                    // 위/아래 번갈아 스텝 + 머리점·줌 펀치(비트에 맞춰 화면이 함께 뛴다)
                    bool stepUp = _anticipateTicks % 2 == 1;
                    _stepFrom = _shakePixels;
                    _stepTo = (stepUp ? 1f : -1f) * ampScale * anticipateShake * h;
                    _stepT = 0f;
                    _stepDur = inBurst ? 0.045f : 0.12f;
                    _dotPunch = Mathf.Min(1.2f, _dotPunch + (inBurst ? 0.55f : 0.3f));
                    _zoomPunch = inBurst ? 0.05f : 0.03f;
                    OnAnticipateTick?.Invoke(stepUp, total > 1 ? (_anticipateTicks - 1f) / (total - 1f) : 1f);
                }
                else
                {
                    // 정적까지 소화 — 이제 공개.
                    if (_awaitNews)
                    {
                        // 드럼롤이 몰아치다 뚝 끊긴 정점에서 멈춘다 — 결과는 숨긴 채 뉴스를 먼저 띄우도록 신호를 보낸다.
                        _phase = Phase.NewsHold; _phaseT = 0f;
                        OnAnticipatePeak?.Invoke();
                    }
                    else BeginReveal();
                    return;
                }
            }

            // 목표까지 짧게 튀듯 이동 후 다음 띠까지 정지
            _stepT += dt;
            _shakePixels = Mathf.Lerp(_stepFrom, _stepTo, EaseOutCubic(Mathf.Clamp01(_stepT / _stepDur)));
            SetVerticesDirty();
        }

        // 결과 이동 시작 — 정적을 찢는 릴리즈 플래시 + 줌 펀치.
        private void BeginReveal()
        {
            _phase = Phase.Reveal;
            _phaseT = 0f;
            _flashColor = Color.white;
            _flashAlpha = Mathf.Max(_flashAlpha, 0.16f);
            _zoomPunch = 0.06f;
        }

        // 방금 발화한 띠(_anticipateTicks번째) 이후 다음 띠까지의 간격.
        // 빌드업: 지수 1.5 커브 — 초반엔 시작 간격에 가깝게 뜸을 들이다 후반에 급격히 몰아친다.
        // 버스트: 고정 초단타 간격 — 기관총 연타.
        private float NextTickGap(int buildup)
        {
            if (_anticipateTicks >= buildup)
                return Mathf.Max(0.02f, burstTickInterval);
            float g = buildup > 1 ? _anticipateTicks / (buildup - 1f) : 1f;
            return Mathf.Lerp(Mathf.Max(0.02f, tickIntervalStart), Mathf.Max(0.02f, tickIntervalEnd),
                              Mathf.Pow(g, 1.5f));
        }

        // 뉴스 대기: 결과를 숨긴 채(끝점=베팅 시작가) 확대 상태로 멈춰 긴장을 유지한다.
        // 멈춘 직후엔 급정거하듯 잔진동이 크게 일었다가 이내 숨죽인 미세 떨림으로 잦아든다("어, 왜 여기서 멈춰?").
        // TriggerReveal()이 오면 결과 이동(Reveal)을 시작. 신호가 없어도 안전망 시간 후 자동 공개한다.
        private void StepNewsHold(float dt, float h)
        {
            _phaseT += dt;
            _zoom = 1f;
            _values[_values.Count - 1] = _revealFrom;                     // 값 고정 — 아직 결과 미공개
            float settle = Mathf.Exp(-_phaseT * 6f);                      // 1 → 0 빠른 감쇠(급정거 후 정적)
            _shakePixels = OrganicShake() * anticipateShake * (0.22f + 1.0f * settle) * h;
            SetVerticesDirty();
            if (_phaseT >= Mathf.Max(0.1f, newsHoldMaxWait))              // 안전망: 영구 정지 방지
                BeginReveal();
        }

        /// <summary>NewsHold(뉴스 대기)에서 결과 이동(Reveal)을 시작한다. 브레이킹 뉴스가 그래프 공개 시점에 호출.</summary>
        public void TriggerReveal()
        {
            if (_phase == Phase.NewsHold) BeginReveal();
        }

        // 공개: old→new 스프링 오버슈트 + 떨림 감쇠
        private void StepReveal(float dt, float h)
        {
            _phaseT += dt;
            float k = Mathf.Clamp01(_phaseT / Mathf.Max(0.0001f, revealDuration));
            _zoom = 1f;
            _values[_values.Count - 1] = Mathf.LerpUnclamped(_revealFrom, _revealTo, EaseOutBack(k, _overshoot));
            OnRevealValue?.Invoke(_values[_values.Count - 1]); // 시세 텍스트도 그래프와 같은 프레임에 움직인다

            // 떨림은 자연스럽게 사그라들되, 변동폭이 클수록 더 크게 흔든다(작은 변동은 차분).
            float amp = anticipateShake * (1f - k) * Mathf.Lerp(0.4f, 1f, _revealSwing);
            if (_revealExtreme) amp += 0.045f * Mathf.Sin(k * 38f) * (1f - k); // 극단은 막판 진동 추가
            _shakePixels = OrganicShake() * amp * h;

            SetVerticesDirty();
            if (k >= 1f)
            {
                _values[_values.Count - 1] = _revealTo;
                _shakePixels = 0f;
                // 착지 임팩트 — 결과 색 전면 플래시 + 머리점 펀치(변동폭이 클수록 크게).
                _flashColor = _revealTo >= _revealFrom ? MarketTheme.Up : MarketTheme.Down;
                _flashAlpha = Mathf.Max(_flashAlpha, Mathf.Lerp(0.18f, 0.34f, _revealSwing));
                _dotPunch = 1.1f;
                _phase = Phase.Hold;
                _phaseT = 0f;
                _idlePhase = 0f;
                OnRevealComplete?.Invoke(); // 결과 이동 완료 — 결과 배너·피드백 발동
            }
        }

        private void StepHold(float dt)
        {
            _phaseT += dt;
            _zoom = 1f;
            float left = Mathf.Clamp01(1f - _phaseT / Mathf.Max(0.0001f, HoldDuration));
            _shakePixels = Mathf.Sin(_phaseT * 22f) * 2f * left; // 착지 후 미세 잔진동
            SetVerticesDirty();
            if (_phaseT >= HoldDuration) { _phase = Phase.ZoomOut; _phaseT = 0f; }
        }

        private void StepZoomOut(float dt)
        {
            _phaseT += dt;
            float k = Mathf.Clamp01(_phaseT / Mathf.Max(0.0001f, ZoomOutDuration));
            _zoom = 1f - EaseInOutCubic(k);
            _shakePixels = 0f;
            SetVerticesDirty();
            if (k >= 1f)
            {
                _zoom = 0f; _phase = Phase.Idle; _idlePhase = 0f;
                SetVerticesDirty();
                OnZoomOutComplete?.Invoke();
            }
        }

        // -1~1 유기적 흔들림(2옥타브 Perlin). 사인 단일파보다 자연스럽다.
        private float OrganicShake()
        {
            float a = (Mathf.PerlinNoise(_animClock * 15f, _seedA) - 0.5f) * 2f;
            float b = (Mathf.PerlinNoise(_animClock * 39f, _seedB) - 0.5f) * 2f;
            return Mathf.Clamp(a * 0.7f + b * 0.3f, -1f, 1f);
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private static float EaseInOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        private static float EaseOutBack(float t, float s)
        {
            float c3 = s + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + s * u * u;
        }

        private void ComputeRange(out float lo, out float hi, float includeExtra = float.NaN)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int i = 0; i < _values.Count; i++)
            {
                float v = _values[i];
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            if (!float.IsNaN(includeExtra)) { lo = Mathf.Min(lo, includeExtra); hi = Mathf.Max(hi, includeExtra); }
            if (lo > hi) { lo = 0f; hi = 1f; }
            float margin = (hi - lo) * 0.06f + 1e-3f;
            lo -= margin; hi += margin;
        }

        // ===================================================
        // 렌더
        // ===================================================
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = _values.Count;
            if (n < 2) return;

            Rect r = GetPixelAdjustedRect();

            // 전면 플래시(버스트 스트로브·공개 릴리즈·착지 임팩트) — 라인보다 먼저 그려 뒤에 깔린다.
            if (_flashAlpha > 0.004f)
            {
                Color fc = _flashColor;
                fc.a = Mathf.Min(0.5f, _flashAlpha);
                int fi = vh.currentVertCount;
                UIVertex fv = UIVertex.simpleVert; fv.color = fc;
                fv.position = new Vector2(r.xMin, r.yMin); vh.AddVert(fv);
                fv.position = new Vector2(r.xMin, r.yMax); vh.AddVert(fv);
                fv.position = new Vector2(r.xMax, r.yMax); vh.AddVert(fv);
                fv.position = new Vector2(r.xMax, r.yMin); vh.AddVert(fv);
                vh.AddTriangle(fi, fi + 1, fi + 2);
                vh.AddTriangle(fi + 2, fi + 3, fi);
            }

            float padY = lineThickness + r.height * Mathf.Clamp(verticalPadding, 0f, 0.45f);
            float yMin = r.yMin + padY, yMax = r.yMax - padY;
            float plotH = Mathf.Max(1f, yMax - yMin);

            float lo, hi;
            if (Focusing) { lo = _frozenLo; hi = _frozenHi; }
            else ComputeRange(out lo, out hi);
            float range = Mathf.Max(1e-4f, hi - lo);

            // 전체뷰 좌표 — 끝점은 머리점 반지름만큼 우단 안쪽으로(머리점이 프레임에 잘리지 않게).
            float xEnd = r.xMax - lineThickness * 2f;
            Vector2[] o = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Lerp(r.xMin, xEnd, i / (float)(n - 1));
                float y = yMin + plotH * Mathf.Clamp01((_values[i] - lo) / range);
                o[i] = new Vector2(x, y);
            }

            // 줌 변환 — 끝점(피벗) 주변을 확대하고 화면 중앙으로. _zoom으로 전체뷰↔포커스 블렌드.
            // 피벗 y는 베팅 시작 시세 기준 → 끝점이 그 자리에서 위/아래로 (확대되어) 움직이는 게 보인다.
            Vector2 c = r.center;
            float pivotY = yMin + plotH * Mathf.Clamp01((_pivotValue - lo) / range);
            Vector2 pivot = new Vector2(xEnd, pivotY);
            float z = Mathf.Clamp01(_zoom) + _zoomPunch; // 펀치만큼 순간 과줌 — 비트에 맞춰 화면이 튀는 맛

            // 격자무늬 — 라인 뒤에 깔아 시세표 느낌. 라인과 같은 줌 변환을 태워 확대 시 함께 벌어진다.
            if (showGrid) AddGrid(vh, r, c, pivot, z, xEnd, yMin, yMax);

            Vector2[] pts = new Vector2[n];
            for (int i = 0; i < n; i++)
                pts[i] = ApplyZoom(o[i], c, pivot, z);
            // 끝점에만 시각 전용 떨림
            pts[n - 1].y = Mathf.Clamp(pts[n - 1].y + _shakePixels, yMin, yMax);

            // 줌 변환이 좌표를 영역 밖으로 밀어내므로, 선 두께 절반만큼 안쪽 영역으로 클리핑해
            // 그려지는 사각 폴리곤이 RectTransform 프레임을 벗어나지 않게 한다.
            float half = lineThickness * 0.5f;
            float clipXMin = r.xMin + half, clipXMax = r.xMax - half;
            float clipYMin = r.yMin + half, clipYMax = r.yMax - half;

            // 세그먼트별 등락 색 + 최근일수록 선명한 네온 잔상
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                if (!ClipSegment(ref a, ref b, clipXMin, clipXMax, clipYMin, clipYMax)) continue;
                Color col = pts[i + 1].y >= pts[i].y ? MarketTheme.Up : MarketTheme.Down;
                float recency = (i + 1) / (float)(n - 1);
                col.a *= Mathf.Lerp(0.35f, 1f, recency);
                AddSegment(vh, a, b, col);
            }

            // 꼭짓점 이음새 — 영역 밖 꼭짓점은 건너뛴다
            for (int i = 1; i < n - 1; i++)
            {
                Vector2 p = pts[i];
                if (p.x < clipXMin || p.x > clipXMax || p.y < clipYMin || p.y > clipYMax) continue;
                Color col = pts[i].y >= pts[i - 1].y ? MarketTheme.Up : MarketTheme.Down;
                float recency = i / (float)(n - 1);
                col.a *= Mathf.Lerp(0.35f, 1f, recency);
                AddJoint(vh, p, col);
            }

            AddHeadDot(vh, pts[n - 1], r);
        }

        // 전체뷰 좌표 o를 끝점(pivot) 중심 줌 변환으로 옮긴다. z=0 전체뷰, z=1 포커스.
        // x는 o.x에만, y는 o.y에만 의존 → 수평/수직선이 기울지 않고 각각 벌어진다.
        private Vector2 ApplyZoom(Vector2 o, Vector2 c, Vector2 pivot, float z)
        {
            Vector2 focus = c + (o - pivot) * zoomMax;
            return Vector2.LerpUnclamped(o, focus, z);
        }

        // 배경 격자 — 은은한 실선 + 교차점 네온 픽셀 도트(레트로 게임 HUD). 라인과 같은 줌 변환을 태워
        // 확대 시 pivot(끝점) 기준으로 함께 벌어진다. 열 x·행 y 변환값을 미리 구해 라인·도트가 공유한다.
        private void AddGrid(VertexHelper vh, Rect r, Vector2 c, Vector2 pivot, float z,
                             float xEnd, float yMin, float yMax)
        {
            int nc = Mathf.Max(1, gridColumns) + 1;
            int nr = Mathf.Max(1, gridRows) + 1;
            Span<float> txs = stackalloc float[nc];
            Span<float> tys = stackalloc float[nr];
            for (int i = 0; i < nc; i++)
            {
                float ox = Mathf.Lerp(r.xMin, xEnd, i / (float)(nc - 1));
                txs[i] = ApplyZoom(new Vector2(ox, c.y), c, pivot, z).x;
            }
            for (int j = 0; j < nr; j++)
            {
                float oy = Mathf.Lerp(yMin, yMax, j / (float)(nr - 1));
                tys[j] = ApplyZoom(new Vector2(c.x, oy), c, pivot, z).y;
            }

            // 은은한 격자선(알파 낮음) — 배경에 거의 녹고, 구조는 도트가 잡는다.
            if (gridColor.a > 0.002f)
            {
                for (int i = 0; i < nc; i++)
                {
                    Vector2 a = new Vector2(txs[i], r.yMin), b = new Vector2(txs[i], r.yMax);
                    if (ClipSegment(ref a, ref b, r.xMin, r.xMax, r.yMin, r.yMax))
                        AddSegment(vh, a, b, gridColor, gridThickness);
                }
                for (int j = 0; j < nr; j++)
                {
                    Vector2 a = new Vector2(r.xMin, tys[j]), b = new Vector2(r.xMax, tys[j]);
                    if (ClipSegment(ref a, ref b, r.xMin, r.xMax, r.yMin, r.yMax))
                        AddSegment(vh, a, b, gridColor, gridThickness);
                }
            }

            // 교차점 픽셀 도트 — 화면 안에 든 것만 그린다. 이게 그리드의 '게임' 성격을 만든다.
            if (showGridDots && gridDotColor.a > 0.002f)
                for (int i = 0; i < nc; i++)
                {
                    if (txs[i] < r.xMin || txs[i] > r.xMax) continue;
                    for (int j = 0; j < nr; j++)
                    {
                        if (tys[j] < r.yMin || tys[j] > r.yMax) continue;
                        AddQuadDot(vh, new Vector2(txs[i], tys[j]), gridDotSize, gridDotColor, r);
                    }
                }
        }

        // 작은 사각 픽셀 도트(프레임 밖은 잘림). 그리드 교차점 강조용.
        private void AddQuadDot(VertexHelper vh, Vector2 p, float size, Color col, Rect r)
        {
            float h = size * 0.5f;
            float x0 = Mathf.Max(p.x - h, r.xMin), x1 = Mathf.Min(p.x + h, r.xMax);
            float y0 = Mathf.Max(p.y - h, r.yMin), y1 = Mathf.Min(p.y + h, r.yMax);
            if (x0 >= x1 || y0 >= y1) return;
            int idx = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = col;
            v.position = new Vector2(x0, y0); vh.AddVert(v);
            v.position = new Vector2(x0, y1); vh.AddVert(v);
            v.position = new Vector2(x1, y1); vh.AddVert(v);
            v.position = new Vector2(x1, y0); vh.AddVert(v);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        // 세그먼트를 클립 영역으로 잘라낸다(Liang-Barsky). 완전히 밖이면 false.
        private static bool ClipSegment(ref Vector2 a, ref Vector2 b,
            float xMin, float xMax, float yMin, float yMax)
        {
            float t0 = 0f, t1 = 1f;
            float dx = b.x - a.x, dy = b.y - a.y;
            if (!ClipEdge(-dx, a.x - xMin, ref t0, ref t1)) return false;
            if (!ClipEdge( dx, xMax - a.x, ref t0, ref t1)) return false;
            if (!ClipEdge(-dy, a.y - yMin, ref t0, ref t1)) return false;
            if (!ClipEdge( dy, yMax - a.y, ref t0, ref t1)) return false;
            Vector2 na = new Vector2(a.x + dx * t0, a.y + dy * t0);
            Vector2 nb = new Vector2(a.x + dx * t1, a.y + dy * t1);
            a = na; b = nb;
            return true;
        }

        private static bool ClipEdge(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f) return q >= 0f;
            float t = q / p;
            if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else        { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        private void AddHeadDot(VertexHelper vh, Vector2 p, Rect r)
        {
            Color c = (_phase == Phase.Reveal || _phase == Phase.Hold)
                ? (_revealTo >= _revealFrom ? MarketTheme.Up : MarketTheme.Down)
                : MarketTheme.AccentTeal;
            // 줌인할수록 머리점도 키워 강조 + '띠'/착지 순간 펀치로 커졌다 줄어든다
            float s = lineThickness * Mathf.Lerp(1.0f, 1.8f, Mathf.Clamp01(_zoom))
                      * (1f + 0.7f * Mathf.Min(1.2f, _dotPunch));
            // 동그란 점(삼각형 팬). 중심은 선 끝점에 그대로 두고, 프레임 밖으로 나가는
            // 테두리 정점만 안쪽으로 클램프해 잘라 그린다(중심을 옮기면 점이 선 끝에서 떨어져 보인다).
            const int seg = 24;
            int center = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = c;
            v.position = new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
            vh.AddVert(v);
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector2 rim = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s;
                rim.x = Mathf.Clamp(rim.x, r.xMin, r.xMax);
                rim.y = Mathf.Clamp(rim.y, r.yMin, r.yMax);
                v.position = rim; vh.AddVert(v);
            }
            for (int i = 0; i < seg; i++)
                vh.AddTriangle(center, center + 1 + i, center + 2 + i);
        }

        // 각 꼭짓점에 선 두께만 한 사각 패치를 깔아 구간 사이 이음새 틈을 메운다.
        private void AddJoint(VertexHelper vh, Vector2 p, Color c)
        {
            float h = lineThickness * 0.5f;
            int idx = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = c;

            v.position = p + new Vector2(-h, -h); vh.AddVert(v);
            v.position = p + new Vector2(-h,  h); vh.AddVert(v);
            v.position = p + new Vector2( h,  h); vh.AddVert(v);
            v.position = p + new Vector2( h, -h); vh.AddVert(v);

            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        private void AddSegment(VertexHelper vh, Vector2 a, Vector2 b, Color c)
            => AddSegment(vh, a, b, c, lineThickness);

        private void AddSegment(VertexHelper vh, Vector2 a, Vector2 b, Color c, float thickness)
        {
            Vector2 delta = b - a;
            Vector2 dir = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector2.right;
            Vector2 normal = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);

            int idx = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = c;

            v.position = a - normal; vh.AddVert(v);
            v.position = a + normal; vh.AddVert(v);
            v.position = b + normal; vh.AddVert(v);
            v.position = b - normal; vh.AddVert(v);

            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }
    }
}
