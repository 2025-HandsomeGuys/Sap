// @tags: special-chunk, digging, entity, damage, loot, vfx
using UnityEngine;

/// <summary>
/// 압축 쓰레기 벽 — HP 관리 및 IDiggable 구현.
/// DiggableBlockBase가 HP·HitCooldown·IDamageStageable 브로드캐스트를 처리한다.
/// 이 클래스는 Settings 적용·VFX·드롭만 담당한다 (SRP).
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class TrashWallEntity : DiggableBlockBase
{
    private TrashWallVFX _vfx;
    private TrashWallDropper _dropper;

    protected override void Awake()
    {
        _vfx     = GetComponent<TrashWallVFX>();
        _dropper = GetComponent<TrashWallDropper>();
        base.Awake();
    }

    protected override void ApplySettings()
    {
        if (SpecialChunkSettingsLoader.Instance != null)
            maxHp = SpecialChunkSettingsLoader.Instance.Settings.entities.trashWall.maxHp;
    }

    protected override void OnHit() => _vfx?.PlayHit();

    protected override void OnDie(Vector3 center)
    {
        _dropper?.Drop(center);
        _vfx?.PlayDestroy(center);
        Debug.Log("[TrashWall] Destroyed.");
    }
}
