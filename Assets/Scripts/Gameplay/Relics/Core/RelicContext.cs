using UnityEngine;

namespace Relic
{
    // behavior가 플레이어/월드에 접근하는 유일 창구. RelicManager가 셋업 시 채운다.
    public class RelicContext
    {
        public Transform      player;
        public PlayerStat     stat;
        public PlayerMining   mining;
        public ToolController tools;
        public RelicStatProvider statProvider;
        public RelicManager   runner;   // 코루틴 대행 + 스폰
        public ITerrainManager terrain; // 지형 파괴 경로 (모루·스테로이드·플라즈마 공용)
        public IPlayerController controller; // 접지 판정·슈퍼 점프 (고물 스프링)

        // 임의 좌표/반경의 지형을 원형으로 파괴한다(§2-A 공용 헬퍼).
        // 내부적으로 기존 파기 경로(ExplodeTerrain)를 호출 → IndestructibleMask 자동 존중.
        public void CarveTerrain(Vector2 worldPos, float radius)
        {
            if (radius <= 0f) return;
            // Awake 시점에 지형 매니저가 아직 없었을 수 있으므로 최초 사용 시 지연 해석.
            if (terrain == null)
            {
                terrain = (ITerrainManager)Object.FindFirstObjectByType<StaticChunkTerrainManager>()
                       ?? (ITerrainManager)Object.FindFirstObjectByType<InfinityMapManager>();
            }
            terrain?.ExplodeTerrain(worldPos, radius);
        }
    }
}
