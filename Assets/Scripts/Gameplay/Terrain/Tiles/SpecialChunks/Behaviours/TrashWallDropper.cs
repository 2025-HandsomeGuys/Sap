// @tags: loot, drop, special-chunk, mineral, digging
using UnityEngine;

/// <summary>
/// 압축 쓰레기 벽 파괴 시 광물 드롭을 담당하는 컴포넌트.
///
/// SOLID — SRP: 드롭 로직만. HP 판정·비주얼·VFX와 완전 분리.
/// MineralDatabase / MineralItemController 패턴은 기존 ScrapExplosion·DiggableRock과 동일.
/// </summary>
public class TrashWallDropper : MonoBehaviour
{
    [Header("확정 드롭 — 고철")]
    [Tooltip("드롭할 고철 최솟값")]
    public int scrapMin = 3;
    [Tooltip("드롭할 고철 최댓값")]
    public int scrapMax = 5;

    [Header("확률 드롭")]
    [Range(0f, 1f)]
    [Tooltip("구리 드롭 확률 (0~1). 기획: 10~20%")]
    public float copperChance = 0.15f;

    [Range(0f, 1f)]
    [Tooltip("철 드롭 확률 (0~1). 기획: 10~20%")]
    public float ironChance = 0.10f;

    [Tooltip("드롭 위치 랜덤 오프셋 반경 (x축)")]
    public float dropScatterX = 0.3f;

    [Tooltip("드롭 위치 랜덤 오프셋 상단 범위 (y축)")]
    public float dropScatterY = 0.2f;

    private void Awake()
    {
        // JSON 설정 적용
        if (SpecialChunkSettingsLoader.Instance != null)
        {
            var s = SpecialChunkSettingsLoader.Instance.Settings.drops.trashWall;
            scrapMin     = s.scrapMin;
            scrapMax     = s.scrapMax;
            copperChance = s.copperChance;
            ironChance   = s.ironChance;
            dropScatterX = s.scatterX;
            dropScatterY = s.scatterY;
        }
    }

    /// <summary>파괴 지점에서 광물을 드롭한다. TrashWallEntity.Die()가 호출.</summary>
    public void Drop(Vector3 center)
    {
        // 확정: 고철 다수
        int scrapCount = Random.Range(scrapMin, scrapMax + 1);
        for (int i = 0; i < scrapCount; i++)
            TryDrop(MineralID.ScrapMetal, center);

        // 확률: 구리
        if (Random.value < copperChance)
            TryDrop(MineralID.Copper, center);

        // 확률: 철
        if (Random.value < ironChance)
            TryDrop(MineralID.Iron, center);
    }

    // ─── 내부 ────────────────────────────────────────────────────
    private void TryDrop(MineralID id, Vector3 center)
    {
        if (MineralDatabase.Instance == null)
        {
            Debug.LogWarning("[TrashWallDropper] MineralDatabase.Instance is null.");
            return;
        }

        MineralSO so = MineralDatabase.Instance.GetMineralByID(id);
        if (so == null || so.mineralPrefab == null)
        {
            Debug.LogWarning($"[TrashWallDropper] MineralSO or prefab missing for {id}.");
            return;
        }

        Vector3 dropPos = new Vector3(
            center.x + Random.Range(-dropScatterX, dropScatterX),
            center.y + Random.Range(0f, dropScatterY),
            -1f
        );

        GameObject spawned = Instantiate(so.mineralPrefab, dropPos, Quaternion.identity);
        if (spawned.TryGetComponent<MineralItemController>(out var ctrl))
            ctrl.mineralData = so;

        Debug.Log($"[TrashWallDropper] Dropped {id} at {dropPos}");
    }
}
