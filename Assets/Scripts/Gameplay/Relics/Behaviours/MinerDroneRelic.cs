using System;
using System.Collections.Generic;
using UnityEngine;

namespace Relic
{
    // 채굴 드론(패시브): 플레이어 주변을 "돌아다니며" 팔 만한 지형(벽면)을 찾아가 같이 파주는 동행 드론.
    // 굴착 드론(DrillDrone, 고정 공전)과 달리 목표 탐색·이동·파기의 행동 로직(상태기계)을 가진다.
    //
    // 목표 선택 = A(주변 정리) + B(조준 방향 보조) 혼합:
    //  · roamAngle을 돌려가며 플레이어 주변 벽을 탐색(A, 로밍)
    //  · aimBias 확률로 마우스 조준 방향의 벽을 우선 탐색(B, 플레이어가 파는 쪽을 같이 파줌)
    //
    // 상태기계(드론 1기): Seeking(따라다니며 탐색) → Moving(목표로 비행) → Digging(잠깐 파기) → Seeking …
    // Lv1 1기 / Lv2 파기 간격↓(효율↑) / Lv3 2기. 프리팹 없이 코드 생성(LineRenderer 링).
    [Serializable]
    public class MinerDroneRelic : RelicBehaviour
    {
        [SerializeField] private int[]   countPerLevel         = { 1, 1, 2 };
        [SerializeField] private float[] carveIntervalPerLevel = { 0.14f, 0.10f, 0.10f }; // Digging 중 파기 간격(초)
        [SerializeField] private float roamRadius   = 4f;    // 플레이어 기준 벽 탐색/활동 반경
        [SerializeField] private float hoverRadius   = 1.8f;  // Seeking 중 대기 공전 반경
        [SerializeField] private float carveRadius   = 0.5f;  // 1회 파괴 반경
        [SerializeField] private float moveSpeed     = 6f;    // 목표로 비행 속도(units/sec)
        [SerializeField] private float standoff      = 0.45f; // 벽에서 이만큼 떨어져 대기하며 판다
        [SerializeField] private float seekInterval  = 0.35f; // 목표 재탐색 간격(초)
        [SerializeField] private float digTime       = 0.5f;  // 한 목표에서 파는 시간(초)
        [Range(0f, 1f)]
        [SerializeField] private float aimBias       = 0.5f;  // 탐색을 조준 방향으로 편향할 확률(B 비중)
        [SerializeField] private float roamSpin      = 90f;   // 로밍 각속도(deg/sec)
        [SerializeField] private float visualRadius  = 0.22f; // 드론 링 크기
        [SerializeField] private Color droneColor    = new Color(0.5f, 1f, 0.55f, 0.9f); // 채굴 드론=초록(공전 드론=시안과 구분)

        [Header("이동음")]
        [SerializeField] private float moveSfxMinInterval = 2.2f;  // 비행 중 모터음 최소 간격(초)
        [SerializeField] private float moveSfxMaxInterval = 4.0f;  // 최대 간격(초)
        [SerializeField] private float moveSfxSpeed       = 0.5f;  // 이 속도(units/sec) 이상일 때만 낸다
        [SerializeField] private float moveSfxVolume      = 0.35f; // 패시브 상시음이라 작게

        private enum State { Seeking, Moving, Digging }

        private class Drone
        {
            public GameObject go;
            public Material mat;
            public State state;
            public Vector2 targetPoint;   // 파려는 벽면 지점
            public Vector2 targetDir;     // 플레이어→벽 방향(파고드는 방향)
            public float roamAngle;       // 로밍 위상(deg)
            public float bob;             // 부유 연출 위상
            public float nextSeek;        // 다음 탐색 시각
            public float nextCarve;       // 다음 파기 시각
            public float digUntil;        // Digging 종료 시각
            public float nextMoveSfx;     // 다음 이동음 시각
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
            float carveInterval = LvF(carveIntervalPerLevel);

            foreach (var d in _drones)
            {
                if (d.go == null) continue;
                Vector2 pos = d.go.transform.position;
                d.bob += dt * 6f;

                switch (d.state)
                {
                    case State.Seeking:
                    {
                        // 플레이어 주위를 부드럽게 공전하며 대기(로밍 느낌).
                        d.roamAngle += roamSpin * dt;
                        Vector2 hover = center + Dir(d.roamAngle) * hoverRadius
                                      + Vector2.up * (Mathf.Sin(d.bob) * 0.06f);
                        d.go.transform.position = Vector2.Lerp(pos, hover, 5f * dt);

                        // 주기적으로 팔 벽을 탐색 → 찾으면 Moving.
                        if (Time.time >= d.nextSeek)
                        {
                            d.nextSeek = Time.time + seekInterval;
                            if (TryFindTarget(center, d, out d.targetPoint, out d.targetDir))
                                d.state = State.Moving;
                        }
                        break;
                    }

                    case State.Moving:
                    {
                        // 벽 앞 standoff 지점까지 비행.
                        Vector2 goal = d.targetPoint - d.targetDir * standoff;
                        d.go.transform.position = Vector2.MoveTowards(pos, goal, moveSpeed * dt);

                        // 너무 멀어졌거나(플레이어가 이동) 도달하면 상태 전이.
                        if (Vector2.Distance(center, d.targetPoint) > roamRadius * 1.5f)
                        {
                            d.state = State.Seeking; // 목표가 활동 반경 밖 → 포기
                        }
                        else if (Vector2.Distance(d.go.transform.position, goal) < 0.15f)
                        {
                            d.state = State.Digging;
                            d.digUntil = Time.time + digTime;
                            d.nextCarve = 0f;
                        }
                        break;
                    }

                    case State.Digging:
                    {
                        // 벽 앞에 머무르며 파고든다(파괴로 벽이 물러나면 다음 목표로).
                        Vector2 goal = d.targetPoint - d.targetDir * standoff;
                        d.go.transform.position = Vector2.MoveTowards(pos, goal, moveSpeed * dt);

                        if (Time.time >= d.nextCarve)
                        {
                            d.nextCarve = Time.time + carveInterval;
                            // ExplodeTerrain 경로라 IndestructibleMask 자동 존중.
                            ctx.CarveTerrain(d.targetPoint + d.targetDir * 0.1f, carveRadius);
                        }

                        if (Time.time >= d.digUntil)
                        {
                            d.nextSeek = 0f; // 즉시 다음 목표 탐색 허용
                            d.state = State.Seeking;
                        }
                        break;
                    }
                }

                // 상태와 무관하게 "실제로 이동했는지"로 판정한다 — 벽 앞에 붙어 파는 중엔 안 울린다.
                TryPlayMoveSfx(d, pos, d.go.transform.position, dt);
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

        // A(로밍) + B(조준) 혼합으로 플레이어 주변의 팔 벽면을 하나 찾는다.
        private bool TryFindTarget(Vector2 center, Drone d, out Vector2 point, out Vector2 dir)
        {
            // 탐색 방향 선택: aimBias 확률로 마우스 조준 방향(B), 아니면 로밍 각(A)에 지터.
            Vector2 probe;
            if (UnityEngine.Random.value < aimBias && TryAimDir(center, out var aim))
                probe = Rotate(aim, UnityEngine.Random.Range(-20f, 20f));
            else
                probe = Dir(d.roamAngle + UnityEngine.Random.Range(-35f, 35f));

            // 플레이어에서 그 방향으로 쏴 벽면(Ground)을 찾는다.
            RaycastHit2D hit = Physics2D.Raycast(center, probe, roamRadius, _terrainMask);
            if (hit.collider != null)
            {
                point = hit.point;
                dir = probe;
                return true;
            }
            point = center; dir = probe;
            return false;
        }

        private bool TryAimDir(Vector2 origin, out Vector2 dir)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 m = cam.ScreenToWorldPoint(Input.mousePosition);
                Vector2 dd = (Vector2)m - origin;
                if (dd.sqrMagnitude > 0.04f) { dir = dd.normalized; return true; }
            }
            dir = Vector2.right;
            return false;
        }

        private static Vector2 Dir(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
        }

        private void RebuildDrones()
        {
            DestroyDrones();
            if (ctx?.player == null) return;

            int count = Mathf.Max(1, (int)LvI(countPerLevel));
            for (int i = 0; i < count; i++)
            {
                var d = CreateDrone();
                d.roamAngle = (360f / count) * i; // 균등 분산 시작
                d.state = State.Seeking;
                // 드론끼리 이동음이 동시에 터지지 않게 시작 시각을 흩는다.
                d.nextMoveSfx = Time.time + UnityEngine.Random.Range(0.3f, moveSfxMaxInterval);
                _drones.Add(d);
            }
        }

        private Drone CreateDrone()
        {
            var go = new GameObject("RelicMinerDrone");
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
