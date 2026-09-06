using System;
using UnityEngine;

namespace Relic
{
    // 모루(패시브): 착지 순간 발밑 지형을 원형으로 파괴한다.
    // 파괴 반경은 "낙하 충격"(착지 직전 최고 낙하속도)에 로그 비례 — 높이 떨어질수록 커지되
    // 로그 곡선이라 초반엔 빠르게, 이후엔 완만하게 늘어 하드 클램프 없이 자연 상한을 가진다.
    //   · fallSpeedThreshold 미만(짧은 점프·낮은 하강)은 아예 파지 않는다.
    //   · 소지 무게(EncumbranceController.TotalWeight)는 부차적 보너스(적재 상한이 있어 유계).
    // 레벨업으로 곡선 진폭(radiusScalePerLevel)이 커진다. 낙하속도는 프레임워크를 건드리지 않고
    // 플레이어 Rigidbody2D.linearVelocity.y를 매 프레임 자체 추적해 구한다.
    // 파괴 순간 임팩트 지점에 충격파 링을 표시(무적 유물처럼 코드 생성, 프리팹 불필요).
    [Serializable]
    public class AnvilRelic : RelicBehaviour
    {
        [SerializeField] private float[] radiusScalePerLevel = { 0.5f, 0.65f, 0.8f }; // 로그 곡선 진폭(레벨별)
        [SerializeField] private float   fallSpeedThreshold  = 12f;   // 이 낙하속도 미만이면 파지 않음(짧은 점프 무시)
        [SerializeField] private float   logSteepness        = 0.5f;  // 로그 입력 계수(클수록 초반 급상승)
        [SerializeField] private float   radiusPerWeight     = 0.015f;// 무게 1당 추가 반경(부차적)
        [SerializeField] private float   minCarveRadius      = 0.5f;  // 이 반경 미만이면 스킵(자잘한 파괴 방지)
        [SerializeField] private float   verticalOffset      = 0.4f;  // 발밑으로 내려 파는 오프셋
        [SerializeField] private float   landingCooldown     = 0.3f;  // 연속 착지 파괴 최소 간격(초)

        // 충격파 링 비주얼 파라미터
        private const int   RingSegments = 48;
        private const float RingWidth    = 0.09f;
        private const float RingDuration = 0.35f;   // 링 전체 지속(초)

        private EncumbranceController _encumbrance; // 무게 소스(런타임 캐시)
        private Rigidbody2D          _rb;           // 낙하속도 추적용(런타임 캐시)
        private float _peakFall;                    // 이번 체공 중 최고 낙하속도(양수)
        private float _nextCarveTime;               // 다음 파괴 허용 시각

        private GameObject   _ring;                 // 단위원 링(월드 배치, 미부모)
        private LineRenderer _lr;
        private Material     _ringMat;
        private float        _ringTimer;            // 남은 링 시간
        private float        _ringMaxRadius;        // 이번 임팩트 반경

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            ResolveRefs();
        }

        // 낙하 추적 + 링 애니메이션. RelicManager가 매 프레임 호출.
        public override void OnUpdate()
        {
            // ── 낙하 충격 추적(항상) ──
            if (_rb == null) ResolveRefs();
            if (_rb != null)
            {
                float vy = _rb.linearVelocity.y;
                if (vy < 0f) _peakFall = Mathf.Max(_peakFall, -vy); // 하강 중 최고 낙하속도 기록
            }

            // ── 링 확장·페이드(1회성 충격파) ──
            if (_ring == null || _ringTimer <= 0f) return;

            _ringTimer -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(_ringTimer / RingDuration); // 0→1 진행도
            float r = Mathf.Lerp(_ringMaxRadius * 0.4f, _ringMaxRadius * 1.15f, t);
            _ring.transform.localScale = Vector3.one * r;

            float a = Mathf.Lerp(0.9f, 0f, t);
            var c = _lr.startColor; c.a = a;
            _lr.startColor = c; _lr.endColor = c;

            if (_ringTimer <= 0f) _ring.SetActive(false);
        }

        public override void OnLanded()
        {
            if (ctx?.player == null) return;

            float fall = _peakFall;
            _peakFall = 0f;   // 다음 체공을 위해 리셋 (읽은 뒤 즉시)

            if (Time.time < _nextCarveTime) return;          // 착지 쿨다운(연타 방지)

            float excess = fall - fallSpeedThreshold;
            if (excess <= 0f) return;                        // 짧은 점프·낮은 하강은 무시

            // 로그 곡선: 초반 급상승 → 완만화. 입력(fall)이 maxFallSpeed로 유계라 자연 상한.
            float weight = CurrentWeight();
            float radius = Lv(radiusScalePerLevel) * Mathf.Log(1f + excess * logSteepness)
                         + weight * radiusPerWeight;
            if (radius < minCarveRadius) return;             // 너무 작으면 스킵

            _nextCarveTime = Time.time + landingCooldown;

            // 발밑을 원형 파괴. ExplodeTerrain 경로라 IndestructibleMask는 자동 존중된다.
            Vector2 pos = (Vector2)ctx.player.position + Vector2.down * verticalOffset;
            ctx.CarveTerrain(pos, radius);

            ShowRing(pos, radius);   // 임팩트 링 표시
        }

        public override void OnUnequip()
        {
            if (_ring != null) UnityEngine.Object.Destroy(_ring);
            if (_ringMat != null) UnityEngine.Object.Destroy(_ringMat);
            _ring = null; _lr = null; _ringMat = null;
        }

        private void ShowRing(Vector2 pos, float radius)
        {
            EnsureRing();
            if (_ring == null) return;

            _ring.transform.position = pos;
            _ringMaxRadius = radius;
            _ringTimer = RingDuration;
            _ring.SetActive(true);

            var c = _lr.startColor; c.a = 0.9f;
            _lr.startColor = c; _lr.endColor = c;
            _ring.transform.localScale = Vector3.one * (radius * 0.4f);
        }

        private void EnsureRing()
        {
            if (_ring != null) return;

            // 부모 없이 월드에 배치(충격파는 임팩트 지점에 고정). 단위원(radius 1)을 localScale로 키운다.
            _ring = new GameObject("RelicAnvilRing");

            _lr = _ring.AddComponent<LineRenderer>();
            _lr.useWorldSpace = false;
            _lr.loop = true;
            _lr.positionCount = RingSegments;
            _lr.widthMultiplier = RingWidth;
            _lr.numCapVertices = 4;
            _ringMat = new Material(Shader.Find("Sprites/Default"));
            _lr.material = _ringMat;

            var color = new Color(1f, 0.55f, 0.15f, 0.9f); // 임팩트 오렌지
            _lr.startColor = color;
            _lr.endColor = color;
            _lr.sortingOrder = 100;

            for (int i = 0; i < RingSegments; i++)
            {
                float ang = (i / (float)RingSegments) * Mathf.PI * 2f;
                _lr.SetPosition(i, new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f)); // 단위원
            }

            _ring.SetActive(false);
        }

        private float CurrentWeight()
        {
            if (_encumbrance == null) ResolveRefs();
            return _encumbrance != null ? _encumbrance.TotalWeight : 0f;
        }

        private void ResolveRefs()
        {
            if (ctx?.player == null) return;
            if (_encumbrance == null)
                _encumbrance = ctx.player.GetComponentInParent<EncumbranceController>()
                            ?? UnityEngine.Object.FindFirstObjectByType<EncumbranceController>();
            if (_rb == null)
                _rb = ctx.player.GetComponentInParent<Rigidbody2D>();
        }
    }
}
