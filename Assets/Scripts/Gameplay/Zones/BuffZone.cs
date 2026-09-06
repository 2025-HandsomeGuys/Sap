// @tags: zone, buff, debuff, stamina, player, trigger
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 존 진입 시 플레이어 스탯에 배율/고정값 버프/디버프를 적용하고,
/// 이탈 시 원복하는 IZoneEffect 구현체.
///
/// [SOLID]
///   SRP: 스탯 배율 적용/원복만 담당. 감지는 ZoneEffectTrigger가 담당.
///   OCP: 새 StatType은 Inspector에서 buffs 목록에 추가하는 것만으로 확장.
///   DIP: PlayerStat에 직접 값을 쓰지 않고, 플레이어의 BuffStatProvider에 위임.
///        BuffStatProvider가 없으면 LogWarning 후 무시(Graceful Degradation).
///
/// 사용법: ZoneEffectTrigger와 동일 GameObject에 부착.
///         buffs 목록에 원하는 StatType, ModifierType, value 설정.
/// </summary>
public class BuffZone : MonoBehaviour, IZoneEffect
{
    [Serializable]
    public struct BuffEntry
    {
        [Tooltip("적용할 스탯 종류")]
        public StatType statType;

        [Tooltip("Flat=고정값 합산 / Percent=배율 곱산 (1.2 = 20% 증가, 0.8 = 20% 감소)")]
        public ModifierType modifierType;

        [Tooltip("수정치 값")]
        public float value;
    }

    [Header("버프 설정")]
    [Tooltip("이 존에 진입하면 적용될 스탯 수정치 목록")]
    public List<BuffEntry> buffs = new List<BuffEntry>();

    // 각 플레이어마다 고유 buffId를 관리 (다중 플레이어 대비)
    // 단일 플레이어 게임이므로 instanceId로 충분
    private string GetBuffId(PlayerStat player)
        => $"BuffZone_{gameObject.GetInstanceID()}_{player.GetInstanceID()}";

    public void OnEnter(PlayerStat player)
    {
        var provider = player.GetComponent<BuffStatProvider>();
        if (provider == null)
        {
            Debug.LogWarning($"[BuffZone] '{player.name}'에 BuffStatProvider가 없습니다. 버프를 적용할 수 없습니다.");
            return;
        }

        string buffId = GetBuffId(player);

        foreach (var entry in buffs)
        {
            provider.AddBuff(buffId, entry.statType, entry.modifierType, entry.value, duration: 0f);
        }

        Debug.Log($"[BuffZone] '{player.name}' 진입 — {buffs.Count}개 버프 적용 (buffId={buffId})");
    }

    public void OnExit(PlayerStat player)
    {
        var provider = player.GetComponent<BuffStatProvider>();
        if (provider == null) return;

        string buffId = GetBuffId(player);
        provider.RemoveBuff(buffId);

        Debug.Log($"[BuffZone] '{player.name}' 이탈 — 버프 제거 (buffId={buffId})");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.25f);
        Gizmos.DrawCube(transform.position, col.bounds.size);
    }
#endif
}
