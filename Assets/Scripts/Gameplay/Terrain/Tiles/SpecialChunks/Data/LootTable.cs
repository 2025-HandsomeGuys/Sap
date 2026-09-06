// @tags: loot, data-container, mineral, special-chunk, drop, scriptable-object
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspector에서 드롭 항목·확률·수량을 설정하는 ScriptableObject 루트 테이블.
///
/// SOLID:
///  - SRP: 드롭 항목 정의 + 실행만 담당.
///  - OCP: LootEntry 추가로 확장 — CrystalBlockDropper 코드 불변.
///  - DIP: CrystalBlockDropper는 이 에셋에만 의존, MineralDatabase 직접 참조 불필요.
/// </summary>
[CreateAssetMenu(fileName = "LootTable_New", menuName = "Sap/LootTable")]
public class LootTable : ScriptableObject
{
    [System.Serializable]
    public struct LootEntry
    {
        [Tooltip("드롭할 광물 ID")]
        public MineralID mineralId;

        [Tooltip("드롭 확률 (0~1)")]
        [Range(0f, 1f)]
        public float chance;

        [Tooltip("최소 드롭 수량")]
        public int minCount;

        [Tooltip("최대 드롭 수량")]
        public int maxCount;
    }

    [Tooltip("드롭 항목 목록")]
    public List<LootEntry> entries;

    [Tooltip("드롭 위치 x축 랜덤 오프셋 반경")]
    public float scatterX = 0.3f;

    [Tooltip("드롭 위치 y축 상단 오프셋 범위")]
    public float scatterY = 0.2f;

    /// <summary>position 근처에 확률에 따라 광물을 스폰한다.</summary>
    public void ExecuteDrop(Vector3 position)
    {
        if (entries == null) return;

        foreach (var entry in entries)
        {
            int count = Random.Range(entry.minCount, entry.maxCount + 1);
            for (int i = 0; i < count; i++)
            {
                if (Random.value > entry.chance) continue;
                TrySpawn(entry.mineralId, position);
            }
        }
    }

    // ─── 내부 ────────────────────────────────────────────────────
    private void TrySpawn(MineralID id, Vector3 center)
    {
        if (MineralDatabase.Instance == null)
        {
            Debug.LogWarning("[LootTable] MineralDatabase.Instance is null.");
            return;
        }

        MineralSO so = MineralDatabase.Instance.GetMineralByID(id);
        if (so == null || so.mineralPrefab == null)
        {
            Debug.LogWarning($"[LootTable] MineralSO or prefab missing for {id}.");
            return;
        }

        Vector3 dropPos = new Vector3(
            center.x + Random.Range(-scatterX, scatterX),
            center.y + Random.Range(0f, scatterY),
            -1f
        );

        GameObject spawned = Instantiate(so.mineralPrefab, dropPos, Quaternion.identity);
        if (spawned.TryGetComponent<MineralItemController>(out var ctrl))
            ctrl.mineralData = so;
    }
}
