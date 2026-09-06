using UnityEngine;

/// <summary>
/// TargetedRollingHoleEntity 전용 릴리스 트리거.
/// 플레이어가 밟으면 트리거가 발동하여 연결된 바위를 굴러가게 합니다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TargetedRockReleaseTrigger : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("발동시킬 커스텀 바위 엔티티.")]
    public TargetedRollingHoleEntity rockEntity;

    [Header("초기 발굴 옵션")]
    [Tooltip("바위가 굴러가기 시작할 때 바위 주변 지형도 파낼지 여부")]
    public bool digInitialHole = true;

    [Tooltip("초기 발굴 시 구멍 크기 배수 (기본 반경의 몇 배로 팔 것인지)")]
    public float digRadiusMultiplier = 2f;

    private bool _triggered;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"[TEST_ROCK] [TargetedRockReleaseTrigger] OnTriggerEnter2D called by: {other.gameObject.name}, Tag: {other.tag}");

        if (_triggered) 
        {
            Debug.Log("[TEST_ROCK] [TargetedRockReleaseTrigger] Already triggered. Ignoring.");
            return;
        }

        if (!other.CompareTag("Player"))
        {
            Debug.Log($"[TEST_ROCK] [TargetedRockReleaseTrigger] Not a Player (Tag is {other.tag}). Ignoring.");
            return;
        }

        _triggered = true;
        Debug.Log("[TEST_ROCK] [TargetedRockReleaseTrigger] Trigger activated! Proceeding to dig and activate rock.");

        if (digInitialHole)
        {
            DigsAroundRock();
        }
        
        if (rockEntity != null)
        {
            Debug.Log("[TEST_ROCK] [TargetedRockReleaseTrigger] Calling rockEntity.Activate()...");
            rockEntity.Activate();
        }
        else
        {
            Debug.LogError("[TEST_ROCK] [TargetedRockReleaseTrigger] rockEntity is null!");
        }
    }

    private void DigsAroundRock()
    {
        if (rockEntity == null) return;

        ITerrainManager terrain =
            Object.FindFirstObjectByType<StaticChunkTerrainManager>() as ITerrainManager
            ?? Object.FindFirstObjectByType<InfinityMapManager>() as ITerrainManager;

        if (terrain == null)
        {
            Debug.LogWarning("[TEST_ROCK] [TargetedRockReleaseTrigger] ITerrainManager를 찾을 수 없습니다.");
            return;
        }

        float radius = rockEntity.clearRadius * digRadiusMultiplier;
        Vector2 pos = rockEntity.transform.position;

        terrain.ExplodeTerrain(pos, radius);
        Debug.Log($"[TEST_ROCK] [TargetedRockReleaseTrigger] 바위 주변 전역 지형 발굴 — 반경 {radius:F2}");
    }
}
