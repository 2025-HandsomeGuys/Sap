using System;
using UnityEngine;

namespace Relic
{
    // 플라즈마 커터(액티브): 발동 시 마우스 방향으로 레이저 빔을 duration 동안 발사.
    // 빔이 닿는 지형을 계속 녹여(파괴) 터널을 뚫는다. 사용 중 이동속도 감소.
    // 경로를 막는 DiggableRock도 누적 데미지로 녹인다 — 터널이 돌 앞에서 끊기지 않게 하는 게 목적이라
    // dps를 돌 HP(2.5~7.5) 대비 넉넉히 잡아 지속시간의 일부만 소모하고 지나가도록 했다.
    // Lv1 3초 / Lv2 5초 / Lv3 7초 (CSV). 쿨타임 1분.
    [Serializable]
    public class PlasmaCutterRelic : RelicBehaviour
    {
        [SerializeField] private float[] durationPerLevel = { 3f, 5f, 7f };
        [SerializeField] private float   cooldown = 60f;

        [SerializeField] private float beamLength   = 12f;   // 최대 사거리
        [SerializeField] private float carveLength    = 1.0f; // 파괴 캡슐 길이(빔 방향) — 네모+둥근끝 슬롯
        [SerializeField] private float carveInterval = 0.05f; // 파괴 간격(초) — 성능 스로틀
        [SerializeField] private float[] rockDpsPerLevel = { 6f, 9f, 12f }; // 돌(DiggableRock) 초당 데미지
        [SerializeField] private float moveSlowMultiplier = 0.5f; // 사용 중 이속 배율(<1)
        [SerializeField] private float turnSmoothTime = 0.6f; // 방향전환 관성(드릴 0.4보다 무겁게). 클수록 천천히 돌아감

        [SerializeField] private Color beamColor = new Color(1f, 0.25f, 0.1f, 0.9f);
        [SerializeField] private float beamWidth = 0.35f;     // 빔 비주얼 전체 폭 = 파기 터널 폭의 기준

        // 파기 반경을 빔 폭에서 파생 → 파는 터널이 보이는 레이저선 크기에 맞는다.
        // 0.5면 터널 폭 = 빔 폭 정확히 일치. 0.55면 살짝 여유(빔이 터널 안에 확실히 들어옴).
        private const float CarveToBeamRatio = 0.55f;
        private float CarveRadius => beamWidth * CarveToBeamRatio;

        private const string SlowKey = "relic:plasma:slow";

        private GameObject   _beam;
        private LineRenderer _lr;
        private Material     _mat;
        private int          _terrainMask;
        private float        _nextCarve;

        // 돌 데미지용 프레임 델타. Time.deltaTime을 직접 쓰지 않고 elapsed 차분으로 뽑아
        // RelicManager가 어떤 시계(scaled/unscaled)를 쓰든 그대로 따라가게 한다.
        private float _prevElapsed;

        // 방향전환 관성: 드릴처럼 각도를 SmoothDampAngle로 서서히 따라가게 한다.
        private float _smoothedAngle;
        private float _angleVelocity;

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        // 이미 만들어진 유물 에셋에는 rockDpsPerLevel이 직렬화돼 있지 않아 빈 배열로 역직렬화될 수 있다.
        // 그대로 Lv()에 넘기면 Clamp(x,0,-1) → a[-1]로 매 프레임 예외가 난다.
        // 인스펙터에서 값을 채우기 전까지는 Lv2 기준값으로 동작하게 해 조용한 무동작을 피한다.
        private const float RockDpsFallback = 9f;
        private float RockDps => (rockDpsPerLevel != null && rockDpsPerLevel.Length > 0)
            ? Lv(rockDpsPerLevel)
            : RockDpsFallback;

        public override float GetDuration() => Lv(durationPerLevel);
        public override float GetCooldown() => cooldown;

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            _terrainMask = LayerMask.GetMask("Ground");
            if (_terrainMask == 0) _terrainMask = ~0; // "Ground" 레이어 없으면 전체
        }

        public override void OnActivate()
        {
            EnsureBeam();
            if (_beam != null) _beam.SetActive(true);
            _nextCarve = 0f;
            _prevElapsed = 0f;

            // 방향 관성 초기화: 시작 순간엔 현재 마우스 방향으로 스냅(과거 각도에서 휩쓸며 시작하지 않게).
            Vector2 d0 = ctx?.player != null ? AimDir(ctx.player.position) : Vector2.right;
            _smoothedAngle = Mathf.Atan2(d0.y, d0.x) * Mathf.Rad2Deg;
            _angleVelocity = 0f;

            // 사용 중 이속 감소
            ctx?.statProvider?.Set(SlowKey, StatType.MoveSpeed, ModifierType.Percent, moveSlowMultiplier);
        }

        public override void OnActiveUpdate(float elapsed)
        {
            if (_beam == null || ctx?.player == null) return;

            float dt = Mathf.Max(0f, elapsed - _prevElapsed);
            _prevElapsed = elapsed;

            Vector2 origin = ctx.player.position;

            // 방향전환 관성: 목표(마우스) 각도로 SmoothDampAngle 보간 → 드릴처럼 무겁게 회전.
            Vector2 targetDir = AimDir(origin);
            float targetAngle = Mathf.Atan2(targetDir.y, targetDir.x) * Mathf.Rad2Deg;
            _smoothedAngle = Mathf.SmoothDampAngle(_smoothedAngle, targetAngle, ref _angleVelocity, turnSmoothTime);
            Vector2 dir = new Vector2(Mathf.Cos(_smoothedAngle * Mathf.Deg2Rad), Mathf.Sin(_smoothedAngle * Mathf.Deg2Rad));

            // 지형과 충돌 지점 탐색
            RaycastHit2D hit = Physics2D.Raycast(origin, dir, beamLength, _terrainMask);
            Vector2 end = hit.collider != null ? hit.point : origin + dir * beamLength;

            // 빔 비주얼(월드 좌표)
            _lr.SetPosition(0, origin);
            _lr.SetPosition(1, end);

            // 빔이 맞은 게 돌인지 판정. 프레임 간 캐시하면 안 된다 —
            // 파괴 시 DestroyRock()이 Destroy(gameObject)를 부르므로 stale 참조가 된다.
            DiggableRock rock = hit.collider != null ? hit.collider.GetComponent<DiggableRock>() : null;

            // 돌 녹이기: carveInterval 스로틀을 태우지 않고 프레임 단위로 준다.
            // 스로틀을 태우면 균열 비주얼(OnHpRatioChanged)이 뚝뚝 끊겨 보인다.
            if (rock != null && dt > 0f)
                rock.Dig(hit.point, RockDps * dt, DiggableRock.ToolIndexRelicBeam);

            // 접촉 지형 녹이기(스로틀). 원 하나가 아니라 빔 방향 캡슐(네모+둥근끝)로 파 레이저 슬롯 모양.
            // ExplodeTerrain 경로라 IndestructibleMask 자동 존중.
            if (hit.collider != null && Time.time >= _nextCarve)
            {
                _nextCarve = Time.time + carveInterval;
                Vector2 a = hit.point - dir * 0.15f;         // 살짝 뒤(기존 터널과 매끄럽게 연결)

                // 돌에 맞았으면 접점까지만 판다. 앞으로 파고들면 돌 뒤 지형이 미리 파여 유령 터널이 생긴다.
                // 반대로 carve를 통째로 끄면 안 된다 — raycast는 폭 없는 선이라
                // 빔이 돌 가장자리를 스칠 때 빔 폭 안의 주변 지형이 안 파이고 진행이 막힌다.
                Vector2 b = rock != null ? hit.point : hit.point + dir * carveLength;
                CarveCapsule(a, b, CarveRadius);
            }
        }

        // a→b 선분을 따라 원을 겹쳐 스탬프 = 스타디움(네모+양끝 둥근) 모양 파괴.
        private void CarveCapsule(Vector2 a, Vector2 b, float radius)
        {
            float dist = Vector2.Distance(a, b);
            float step = Mathf.Max(0.15f, radius * 0.7f);
            int n = Mathf.CeilToInt(dist / step);
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = n == 0 ? a : Vector2.Lerp(a, b, i / (float)n);
                ctx.CarveTerrain(p, radius);
            }
        }

        public override void OnActiveEnd()
        {
            if (_beam != null) _beam.SetActive(false);
            ctx?.statProvider?.Clear(SlowKey);
        }

        public override void OnUnequip()
        {
            ctx?.statProvider?.Clear(SlowKey);
            if (_beam != null) UnityEngine.Object.Destroy(_beam);
            if (_mat != null) UnityEngine.Object.Destroy(_mat);
            _beam = null; _lr = null; _mat = null;
        }

        // 수평 기준 아래로 이 각도 이상은 겨냥 불가 → 발밑 바닥을 파고 빠지는 현상 방지(드릴과 동일 20°).
        private const float MaxDownAngleDeg = 20f;

        private Vector2 AimDir(Vector2 origin)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 m = cam.ScreenToWorldPoint(Input.mousePosition);
                Vector2 d = (Vector2)m - origin;
                if (d.sqrMagnitude <= 0.0001f) return Vector2.right; // 마우스가 플레이어 위 → 폴백

                // 하향 클램프: y가 -|x|*tan(maxDown)보다 낮으면 그 한계로 올림(드릴 차징과 동일).
                float minY = -Mathf.Abs(d.x) * Mathf.Tan(MaxDownAngleDeg * Mathf.Deg2Rad);
                if (d.y < minY) d.y = minY;

                // 클램프로 벡터가 거의 소멸(거의 수직 아래 조준)한 경우 수평 방향 유지.
                if (d.sqrMagnitude <= 0.0001f) d = new Vector2(d.x >= 0f ? 1f : -1f, 0f);
                return d.normalized;
            }
            return Vector2.right; // 폴백
        }

        private void EnsureBeam()
        {
            if (_beam != null) return;

            _beam = new GameObject("RelicPlasmaBeam");
            _lr = _beam.AddComponent<LineRenderer>();
            _lr.useWorldSpace = true;
            _lr.positionCount = 2;
            _lr.widthMultiplier = beamWidth;
            _lr.numCapVertices = 4;
            _mat = new Material(Shader.Find("Sprites/Default"));
            _lr.material = _mat;
            _lr.startColor = beamColor;
            _lr.endColor = beamColor;
            _lr.sortingOrder = 120;

            _beam.SetActive(false);
        }
    }
}
