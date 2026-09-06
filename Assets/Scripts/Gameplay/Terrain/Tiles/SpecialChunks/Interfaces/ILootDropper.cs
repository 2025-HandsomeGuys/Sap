// @tags: interface, loot, drop, special-chunk, mineral
/// <summary>
/// 파괴 시 아이템을 드롭하는 컴포넌트 계약.
///
/// SOLID:
///  - SRP: 드롭 책임만 분리 (HP 엔티티와 무관).
///  - DIP: TrashWallEntity, CrystalBlockEntity 등은 이 인터페이스만 알면 됨.
///  - OCP: 새 드롭 방식(LootTable, 하드코딩 등)은 이 계약을 구현하여 확장.
/// </summary>
public interface ILootDropper
{
    /// <param name="position">아이템이 생성될 월드 좌표</param>
    void Drop(UnityEngine.Vector3 position);
}
