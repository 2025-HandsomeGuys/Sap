// @tags: special-chunk, digging, entity, crystal, damage, loot, vfx
using UnityEngine;

/// <summary>
/// 수정 블록 — HP 관리 및 IDiggable 구현.
/// DiggableBlockBase가 HP·HitCooldown·IDamageStageable 브로드캐스트를 처리한다.
/// 이 클래스는 Settings 적용·VFX·드롭만 담당한다 (SRP).
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class CrystalBlockEntity : DiggableBlockBase
{
    private CrystalBlockVFX _vfx;
    private ILootDropper _dropper;

    protected override void Awake()
    {
        _vfx     = GetComponent<CrystalBlockVFX>();
        _dropper = GetComponent<ILootDropper>();
        base.Awake();
    }

    protected override void ApplySettings()
    {
        if (SpecialChunkSettingsLoader.Instance != null)
            maxHp = SpecialChunkSettingsLoader.Instance.Settings.entities.crystalBlock.maxHp;
    }

    protected override void OnHit() => _vfx?.PlayHit();

    protected override void OnDie(Vector3 center)
    {
        _dropper?.Drop(center);
        _vfx?.PlayDestroy(center);
        Debug.Log("[CrystalBlock] Destroyed.");
    }
}
