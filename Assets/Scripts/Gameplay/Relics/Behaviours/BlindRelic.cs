using System;
using System.Collections;
using UnityEngine;
using Relic.Data;

namespace Relic
{
    // 맹인(패시브): 앞이 거의 보이지 않지만, 삽·곡괭이로 땅을 팔 때마다 그 자리에서 "파동"이 퍼져
    // 잠깐 주변을 환히 밝힌다. 파동이 잦아들면 다시 캄캄해진다 — 손끝 감각으로 파는 광부 컨셉.
    // 앞이 안 보이는 대신, 동작이 군더더기 없어져 스태미나 소모가 적고 차징·스윙이 빠르다.
    // 그 균형으로 한 번에 파는 범위는 작아진다.
    //
    //  · 시야: VisionRadiusUp 배율을 평상시엔 아주 낮게(near-blind) 깔아두고, OnDigSwing마다
    //    peak까지 튀었다가 pulseDuration에 걸쳐 평상시 값으로 감쇠한다. PlayerVisionOverlay가
    //    VisionRadiusUp를 매 프레임 읽으므로(finalVisionRadius *= GetFinalValue), 값만 흔들면 화면이 따라온다.
    //  · 시야 밖: darknessRest=1(완전 암흑) — 시야 범위 밖은 아무것도 안 보인다. PlayerVisionOverlay가 공간 마스킹 담당.
    //  · 음파 흑백: 장착 중 전역 _SonarAmount=1로 SonarRendererFeature(Custom/Sonar)가 화면을 청백 모노톤으로 합성.
    //    시야 마스킹은 오버레이가 하므로 보이는 픽셀만 흑백이 된다 — '음파로 스캔한 시야' 연출.
    //    ※ URP Renderer 에셋의 Renderer Features에 SonarRendererFeature를 1회 추가해야 동작(XRay와 동일).
    //  · 스태미나 소모 ↓ = StaminaCostPerSecond 배율<1 (PlayerController의 상시 드레인에 곱)
    //  · 차징/스윙 속도 ↑ = MiningSpeed 배율>1 (SapStrategy 차징 누적·PlayerStat 애니 속도 공용 knob)
    //  · 파는 범위 ↓ = ToolRange 배율<1 (대가 — Pickaxe/SapStrategy finalMultiplier에 곱)
    //  · 파동 링 VFX는 순수 연출(코드 생성 LineRenderer, 씬 세팅 불필요).
    //
    // 삽뿐 아니라 곡괭이 스윙에도 파동이 나간다(OnDigSwing은 모든 채굴 스윙 공용 훅). 맹인 특성상
    // 어떤 도구로든 파는 순간엔 주변이 보이는 편이 플레이가 자연스럽다.
    [Serializable]
    public class BlindRelic : RelicBehaviour
    {
        [Header("시야 (평상시 = near-blind, 파동 시 순간 확보)")]
        [SerializeField] private float[] visionRestPerLevel   = { 0.15f, 0.18f, 0.22f }; // 평상시 시야 배율(작을수록 캄캄) — 매우 좁게
        [SerializeField] private float[] visionPeakPerLevel   = { 1.30f, 1.50f, 1.70f }; // 파동 순간 시야 배율(>1 = 평소보다 더 밝게)
        [SerializeField] private float[] pulseDurationPerLevel = { 1.1f, 1.3f, 1.5f };   // 파동 밝기 감쇠 시간(초)

        [Header("바깥 어둠 (맹인 전용, 1=완전 암흑). 평상시 칠흑 → 파동 순간 밝아짐)")]
        [SerializeField] private float[] darknessRestPerLevel = { 1f, 1f, 1f };          // 평상시 시야 밖 = 완전 암흑(아무것도 안 보임)
        [SerializeField] private float[] darknessPeakPerLevel = { 0.45f, 0.40f, 0.35f }; // 파동 순간 어둠(옅게)

        [Header("음파 흑백 오버레이 (Custom/Sonar 풀스크린 패스)")]
        [SerializeField] private float sonarFadeSpeed = 3f; // 장착 시 흑백으로 스며드는 속도(전역 _SonarAmount 페이드)

        [Header("대가 스탯 배율(레벨별, Percent 곱연산)")]
        [SerializeField] private float[] staminaCostMulPerLevel = { 0.70f, 0.60f, 0.50f }; // 스태미나 소모 ↓
        [SerializeField] private float[] miningSpeedMulPerLevel = { 1.25f, 1.40f, 1.55f }; // 차징·스윙 속도 ↑
        [SerializeField] private float[] digRangeMulPerLevel    = { 0.70f, 0.66f, 0.62f }; // 파는 범위 ↓ (대가)

        [Header("파동 링 연출")]
        [SerializeField] private float ringMaxRadius = 6f;
        [SerializeField] private float ringWidth     = 0.16f;
        [SerializeField] private Color ringColor     = new Color(0.6f, 0.85f, 1f, 0.7f);

        // 파동 진행 시간. duration 이상이면 비활성(평상시 near-blind 유지).
        private float _pulseElapsed = float.MaxValue;

        // 바깥 어둠을 흔들 오버레이(지연 해석·캐시). null이면 어둠 제어는 no-op.
        // 지상 씬처럼 오버레이가 꺼져 있는 곳에서도 찾아내야 하므로 비활성 오브젝트까지 포함해 검색한다.
        private PlayerVisionOverlay _overlay;
        private PlayerVisionOverlay Overlay =>
            _overlay != null
                ? _overlay
                : (_overlay = UnityEngine.Object.FindFirstObjectByType<PlayerVisionOverlay>(FindObjectsInactive.Include));

        // 오버레이를 강제로 켜 두었는가. 씬 전환 등으로 오버레이가 사라지면 OnUpdate가 다시 붙인다.
        private bool _overlayForced;
        private float _overlayRetryTimer;
        private const float OverlayRetryInterval = 0.5f;

        private const string KVision  = "relic:Blind:vision";
        private const string KStamina = "relic:Blind:stamina";
        private const string KSpeed   = "relic:Blind:speed";
        private const string KRange   = "relic:Blind:range";

        // 음파 흑백 풀스크린(SonarRendererFeature)을 구동하는 전역 셰이더 값.
        private static readonly int SonarAmountID = Shader.PropertyToID("_SonarAmount");
        private float _sonarAmount;  // 현재 값(페이드 진행)
        private float _sonarTarget;  // 목표: 장착=1, 해제=0

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];
        private bool  PulseActive   => _pulseElapsed < Lv(pulseDurationPerLevel);

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            ApplyStaticStats();
            _pulseElapsed = float.MaxValue;
            ForceOverlayOn(); // 지상처럼 시야 오버레이가 꺼진 씬에서도 장착 즉시 켠다
            ApplyEnvelope(0f); // 장착 즉시 near-blind + 칠흑
            _sonarTarget = 1f; // 음파 흑백 페이드-인(OnUpdate에서 진행)
        }

        public override void OnLevelChanged(int lv)
        {
            base.OnLevelChanged(lv);
            ApplyStaticStats();
            if (!PulseActive) ApplyEnvelope(0f);
        }

        public override void OnUnequip()
        {
            var sp = ctx?.statProvider;
            if (sp != null)
            {
                sp.Clear(KVision);
                sp.Clear(KStamina);
                sp.Clear(KSpeed);
                sp.Clear(KRange);
            }
            Overlay?.ClearDarknessOverride();  // 바깥 어둠 원복
            Overlay?.SetForcedActive(false);   // 강제로 켰던 오버레이를 장착 전 상태로 되돌림
            _overlayForced = false;

            // 음파 흑백 즉시 해제(해제 후엔 OnUpdate가 더 안 불리므로 페이드 없이 끈다).
            _sonarTarget = 0f;
            _sonarAmount = 0f;
            Shader.SetGlobalFloat(SonarAmountID, 0f);
        }

        // 삽·곡괭이 스윙마다 파동 발생 → 순간 주변 밝힘 + 링 연출.
        public override void OnDigSwing()
        {
            _pulseElapsed = 0f;
            ApplyEnvelope(1f);   // 파동 최고조: 시야 peak + 어둠 옅게
            PlayRingVFX();
        }

        public override void OnUpdate()
        {
            // 오버레이가 아직 없거나(씬 로드 직후) 씬 전환으로 파괴됐으면 다시 붙인다.
            // 붙고 나면 Find는 더 안 돈다. 오버레이가 아예 없는 씬에서 매 프레임 Find를 돌지 않도록
            // 실패 시에는 OverlayRetryInterval 간격으로만 재시도한다.
            if (_overlay == null || !_overlayForced)
            {
                _overlayRetryTimer -= Time.deltaTime;
                if (_overlayRetryTimer <= 0f)
                {
                    _overlayRetryTimer = OverlayRetryInterval;
                    ForceOverlayOn();
                }
            }

            // 음파 흑백 페이드는 파동 여부와 무관하게 매 프레임 진행(장착 중 상시 on).
            if (_sonarAmount != _sonarTarget)
            {
                _sonarAmount = Mathf.MoveTowards(_sonarAmount, _sonarTarget, sonarFadeSpeed * Time.deltaTime);
                Shader.SetGlobalFloat(SonarAmountID, _sonarAmount);
            }

            if (!PulseActive) return; // 평상시엔 매 프레임 Set 하지 않음(마지막 Set 값 유지)

            _pulseElapsed += Time.deltaTime;
            float dur = Lv(pulseDurationPerLevel);

            if (_pulseElapsed >= dur)
            {
                ApplyEnvelope(0f);            // 파동 종료 → 다시 캄캄
                _pulseElapsed = float.MaxValue;
                return;
            }

            // e: 1(스윙 순간) → 0(파동 끝). easeOutQuad로 부드럽게 잦아든다.
            float k = _pulseElapsed / dur;
            float e = (1f - k) * (1f - k);
            ApplyEnvelope(e);
        }

        // ── 내부 ──

        // 시야 오버레이를 강제 활성. 지상 씬은 시야 제한이 필요 없어 오버레이가 꺼져 있는데,
        // 맹인 유물은 어디서든 캄캄해야 하므로 오브젝트·컴포넌트·캔버스를 전부 되살린다.
        // 오버레이가 아직 씬에 없으면 no-op — OnUpdate가 0.5초 간격으로 재시도한다.
        private void ForceOverlayOn()
        {
            var ov = Overlay;
            if (ov == null) return;

            ov.SetForcedActive(true);
            _overlayForced = true;

            // 씬 전환으로 새로 찾은 오버레이라면 어둠 값도 다시 얹어야 한다.
            if (!PulseActive) ApplyEnvelope(0f);
        }

        // 파동 엔벨로프(e: 0=평상시 near-blind/칠흑, 1=파동 최고조 밝음)에 맞춰 시야·어둠을 함께 구동.
        private void ApplyEnvelope(float e)
        {
            SetVision(Mathf.Lerp(Lv(visionRestPerLevel), Lv(visionPeakPerLevel), e));
            Overlay?.SetDarknessOverride(Mathf.Lerp(Lv(darknessRestPerLevel), Lv(darknessPeakPerLevel), e));
        }

        private void ApplyStaticStats()
        {
            var sp = ctx?.statProvider;
            if (sp == null) return;
            sp.Set(KStamina, StatType.StaminaCostPerSecond, ModifierType.Percent, Lv(staminaCostMulPerLevel));
            sp.Set(KSpeed,   StatType.MiningSpeed,          ModifierType.Percent, Lv(miningSpeedMulPerLevel));
            sp.Set(KRange,   StatType.ToolRange,            ModifierType.Percent, Lv(digRangeMulPerLevel));
        }

        private void SetVision(float mul)
        {
            ctx?.statProvider?.Set(KVision, StatType.VisionRadiusUp, ModifierType.Percent, mul);
        }

        private void PlayRingVFX()
        {
            if (ctx?.runner == null || ctx.player == null) return;
            ctx.runner.StartCoroutine(RingVFX(ctx.player.position, ringMaxRadius, Lv(pulseDurationPerLevel)));
        }

        private IEnumerator RingVFX(Vector2 center, float maxR, float dur)
        {
            var go = new GameObject("BlindPulseRing");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            const int seg = 48;
            lr.positionCount = seg;
            lr.widthMultiplier = ringWidth;
            lr.numCapVertices = 4;
            var mat = new Material(Shader.Find("Sprites/Default"));
            lr.material = mat;
            lr.sortingOrder = 115;

            float t = 0f;
            while (t < dur)
            {
                float k = t / dur;
                float r = Mathf.Lerp(0.2f, maxR, k);
                Color c = ringColor; c.a = ringColor.a * (1f - k);
                lr.startColor = c; lr.endColor = c;
                for (int i = 0; i < seg; i++)
                {
                    float a = (i / (float)seg) * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r, 0f));
                }
                t += Time.deltaTime;
                yield return null;
            }

            UnityEngine.Object.Destroy(mat);
            UnityEngine.Object.Destroy(go);
        }
    }
}
