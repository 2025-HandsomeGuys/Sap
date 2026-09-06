// @tags: loot, drop, special-chunk, crystal, mineral, interface
using UnityEngine;

/// <summary>
/// 수정 블록 파괴 시 LootTable ScriptableObject 기반 광물 드롭.
///
/// SOLID:
///  - SRP: 드롭 실행만 담당.
///  - DIP: ILootDropper 계약 구현 → CrystalBlockEntity는 인터페이스만 알면 됨.
///  - OCP: LootTable 에셋 교체로 드롭 내용 변경 — 코드 수정 불필요.
/// </summary>
public class CrystalBlockDropper : UnityEngine.MonoBehaviour, ILootDropper
{
    [SerializeField] private LootTable lootTable;

    // ─── ILootDropper ────────────────────────────────────────────
    public void Drop(UnityEngine.Vector3 position)
    {
        if (lootTable == null)
        {
            UnityEngine.Debug.LogWarning("[CrystalBlockDropper] LootTable is not assigned.");
            return;
        }

        lootTable.ExecuteDrop(position);
    }
}
