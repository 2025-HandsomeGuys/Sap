using System;
using UnityEngine;

namespace Relic
{
    // 엘리베이터 신호기(패시브·상시): 반경 내 엘리베이터를 '직접 가보지 않아도' 지도에 띄운다.
    //
    // 원래 엘리베이터 마커는 상호작용 범위까지 걸어가야만 남는다
    // (PlayerInteractor → MapMarkerRegistry.Discover, IMapElevator 규칙).
    // 이 유물은 그 발견 조건만 대신 채워 준다 — 마커·스프라이트·지도 렌더는 기존 것을 그대로 쓴다.
    //
    // 청크를 로드하지 않고도 찾을 수 있는 이유: 엘리베이터 배치가 결정론이다.
    // 정류장 Y는 ElevatorManager.layers(카탈로그), X는 층마다 하나뿐이고 ElevatorStopLayout이 정해서
    // ShouldSpawnElevator(x, y)만으로 존재 여부가 나오고, 월드 좌표는
    // CalculateElevatorPosition이 로드된 실물과 같은 값을 준다(스폰 위치 단일 원천).
    // 탐지파동(DetectionPulseRelic)이 특수청크를 예측 탐지하는 것과 같은 발상이다.
    [Serializable]
    public class ElevatorTrackerRelic : RelicBehaviour
    {
        [Tooltip("레벨별 탐지 반경(청크). 정류장은 Y로 10칸 간격이라 레벨이 오르면 아래 층 승강장까지 잡힌다")]
        [SerializeField] private int[] radiusPerLevel = { 8, 14, 20 };

        [Tooltip("훑는 주기(초). 마커는 중복 등록이 무시되므로 촘촘할 필요가 없다")]
        [SerializeField] private float scanInterval = 0.5f;

        private float _nextScan;

        private int Radius() => radiusPerLevel[Mathf.Clamp(level - 1, 0, radiusPerLevel.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            _nextScan = 0f;   // 장착 즉시 한 번 훑는다
        }

        public override void OnUpdate()
        {
            if (ctx?.player == null) return;
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + Mathf.Max(0.1f, scanInterval);
            Scan();
        }

        private void Scan()
        {
            var em = ElevatorManager.Instance;
            if (em == null || em.layers == null) return;   // 지상 씬 등 엘리베이터가 없는 곳

            Vector2Int origin = ChunkCoords.ToChunk(ctx.player.position);
            int r = Radius();

            // Y는 정류장 깊이만 훑는다 — 그 사이 청크에는 엘리베이터가 아예 없다.
            for (int i = 0; i < em.layers.Count; i++)
            {
                var layer = em.layers[i];
                if (layer == null) continue;

                int y = layer.startDepth;
                if (Mathf.Abs(y - origin.y) > r) continue;

                for (int x = origin.x - r; x <= origin.x + r; x++)
                {
                    if (!em.ShouldSpawnElevator(x, y)) continue;
                    MapMarkerRegistry.Discover(em.CalculateElevatorPosition(x, y), MapMarkerKind.Elevator);
                }
            }
        }
    }
}
