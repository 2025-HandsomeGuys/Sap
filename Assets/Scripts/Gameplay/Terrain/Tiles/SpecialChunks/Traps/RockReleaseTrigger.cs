// @tags: special-chunk, rock, trap, trigger, digging
using UnityEngine;

/// <summary>
/// 플레이어가 밟으면 바위 주변 땅을 파낸 뒤 바위를 굴려 내려보내는 트리거.
/// 천장 붕괴 없이 경사면을 굴러내려오는 시나리오 전용.
/// RollingRockTrap과 달리 OpenCeiling 없이 Activate()만 호출한다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class RockReleaseTrigger : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("발동시킬 바위 엔티티.")]
    public RollingRockEntity rockEntity;

    [Header("초기 발굴")]
    [Tooltip("바위 CircleCollider 반경 대비 파낼 반경 배수. 1이면 바위 크기와 동일, 2면 두 배.")]
    public float digRadiusMultiplier = 2f;

    private bool _triggered;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_triggered || !other.CompareTag("Player")) return;
        _triggered = true;

        DigsAroundRock();
        rockEntity?.Activate();
    }

    private void DigsAroundRock()
    {
        if (rockEntity == null) return;

        ITerrainManager terrain =
            Object.FindFirstObjectByType<StaticChunkTerrainManager>() as ITerrainManager
            ?? Object.FindFirstObjectByType<InfinityMapManager>() as ITerrainManager;

        if (terrain == null)
        {
            Debug.LogWarning("[RockReleaseTrigger] ITerrainManager를 찾을 수 없습니다.");
            return;
        }

        var col = rockEntity.GetComponent<CircleCollider2D>();
        float radius = col != null ? col.radius * digRadiusMultiplier : rockEntity.clearRadius * digRadiusMultiplier;

        terrain.ExplodeTerrain(rockEntity.transform.position, radius);
        Debug.Log($"[RockReleaseTrigger] 바위 주변 발굴 — 반경 {radius:F2}");
    }
}
