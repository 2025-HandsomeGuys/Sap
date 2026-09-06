using System;
using System.Collections.Generic;
using UnityEngine;

namespace Relic
{
    // 굴착 드론(패시브): 플레이어 주변을 공전하는 소형 드론이 주변 지형을 조금씩 자동 파괴.
    // Lv1 1기 / Lv2 효율↑(간격↓) / Lv3 2기 (CSV). 프리팹 없이 코드로 생성(LineRenderer 링).
    [Serializable]
    public class DrillDroneRelic : RelicBehaviour
    {
        [SerializeField] private int[]   countPerLevel        = { 1, 1, 2 };
        [SerializeField] private float[] carveIntervalPerLevel = { 0.5f, 0.32f, 0.32f }; // 파괴 간격(초)
        [SerializeField] private float droneRange  = 3f;    // 파괴 대상 탐지 사거리
        [SerializeField] private float carveRadius  = 0.5f;  // 1회 파괴 반경
        [SerializeField] private float hoverRadius  = 1.7f;  // 플레이어 공전 반경
        [SerializeField] private float orbitSpeed   = 70f;   // 공전 속도(deg/sec)
        [SerializeField] private float followLerp   = 5f;    // 목표 위치로 수렴 속도
        [SerializeField] private float visualRadius = 0.22f; // 드론 링 크기
        [SerializeField] private Color droneColor   = new Color(0.3f, 0.9f, 1f, 0.9f);

        [Header("이동음")]
        [SerializeField] private float moveSfxMinInterval = 2.2f;  // 비행 중 모터음 최소 간격(초)
        [SerializeField] private float moveSfxMaxInterval = 4.0f;  // 최대 간격(초)
        [SerializeField] private float moveSfxSpeed       = 0.5f;  // 이 속도(units/sec) 이상일 때만 낸다
        [SerializeField] private float moveSfxVolume      = 0.35f; // 패시브 상시음이라 작게

        private const float ProbeStepDeg = 47f; // 파괴 탐침 회전량

        private class Drone
        {
            public GameObject go;
            public Material mat;
            public float orbitPhase;   // 공전 위상(deg)
            public float probeAngle;   // 파괴 탐침 각(deg)
            public float nextCarve;    // 다음 파괴 시각
            public float nextMoveSfx;  // 다음 이동음 시각
        }

        private readonly List<Drone> _drones = new List<Drone>();
        private int _terrainMask;

        private float LvI(int[] a)   => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];
        private float LvF(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            _terrainMask = LayerMask.GetMask("Ground");
            if (_terrainMask == 0) _terrainMask = ~0;
            RebuildDrones();
        }

        public override void OnLevelChanged(int lv)
        {
            base.OnLevelChanged(lv);
            RebuildDrones();
        }

        public override void OnUnequip() => DestroyDrones();

        public override void OnUpdate()
        {
            if (ctx?.player == null || _drones.Count == 0) return;

            Vector2 center = ctx.player.position;
            float dt = Time.deltaTime;
            float interval = LvF(carveIntervalPerLevel);

            foreach (var d in _drones)
            {
                if (d.go == null) continue;

                // 공전 목표로 수렴
                d.orbitPhase += orbitSpeed * dt;
                Vector2 target = center + Dir(d.orbitPhase) * hoverRadius;
                Vector2 cur = d.go.transform.position;
                Vector2 next = Vector2.Lerp(cur, target, followLerp * dt);
                d.go.transform.position = next;

                TryPlayMoveSfx(d, cur, next, dt);

                // 주기적으로 주변 지형 파괴(탐침을 돌려가며)
                if (Time.time >= d.nextCarve)
                {
                    d.nextCarve = Time.time + interval;
                    d.probeAngle += ProbeStepDeg;
                    Vector2 pd = Dir(d.probeAngle);
                    Vector2 dpos = d.go.transform.position;
                    RaycastHit2D hit = Physics2D.Raycast(dpos, pd, droneRange, _terrainMask);
                    if (hit.collider != null)
                        ctx.CarveTerrain(hit.point + pd * 0.1f, carveRadius);
                }
            }
        }

        /// <summary>
        /// 실제로 움직이고 있을 때만 간헐적으로 모터음을 낸다.
        /// 상시 루프가 아니라 랜덤 간격 원샷이라 드론이 여럿이어도 겹쳐 울리지 않는다.
        /// </summary>
        private void TryPlayMoveSfx(Drone d, Vector2 from, Vector2 to, float dt)
        {
            if (dt <= 0f) return;
            if ((to - from).magnitude / dt < moveSfxSpeed) return;
            if (Time.time < d.nextMoveSfx) return;

            d.nextMoveSfx = Time.time + UnityEngine.Random.Range(moveSfxMinInterval, moveSfxMaxInterval);
            SoundManager.Instance?.PlaySFXAt(SfxKeys.DroneMove, to,
                                             UnityEngine.Random.Range(0.92f, 1.08f), moveSfxVolume);
        }

        private static Vector2 Dir(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private void RebuildDrones()
        {
            DestroyDrones();
            if (ctx?.player == null) return;

            int count = Mathf.Max(1, (int)LvI(countPerLevel));
            for (int i = 0; i < count; i++)
            {
                var d = CreateDrone();
                d.orbitPhase = (360f / count) * i; // 균등 배치
                d.probeAngle = 90f * i;
                // 드론끼리 이동음이 동시에 터지지 않게 시작 시각을 흩는다.
                d.nextMoveSfx = Time.time + UnityEngine.Random.Range(0.3f, moveSfxMaxInterval);
                _drones.Add(d);
            }
        }

        private Drone CreateDrone()
        {
            var go = new GameObject("RelicDrillDrone");
            go.transform.position = ctx.player.position;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            const int seg = 24;
            lr.positionCount = seg;
            lr.widthMultiplier = 0.06f;
            var mat = new Material(Shader.Find("Sprites/Default"));
            lr.material = mat;
            lr.startColor = droneColor;
            lr.endColor = droneColor;
            lr.sortingOrder = 110;
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * visualRadius, Mathf.Sin(a) * visualRadius, 0f));
            }

            return new Drone { go = go, mat = mat };
        }

        private void DestroyDrones()
        {
            foreach (var d in _drones)
            {
                if (d.go != null) UnityEngine.Object.Destroy(d.go);
                if (d.mat != null) UnityEngine.Object.Destroy(d.mat);
            }
            _drones.Clear();
        }
    }
}
