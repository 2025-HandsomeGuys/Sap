using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 인벤토리 시스템의 StatProvider.
/// 인벤토리 업그레이드에 따른 슬롯 추가, 무게 한도 증가 등의 Modifier를 관리.
/// </summary>
public class InventoryStatProvider : MonoBehaviour, IStatProvider
{
    private PlayerStat playerStat;
    private readonly List<StatModifier> _modifiers = new List<StatModifier>();

    private void Start()
    {
        playerStat = GetComponentInParent<PlayerStat>();
        if (playerStat == null) playerStat = FindFirstObjectByType<PlayerStat>();

        if (playerStat != null) playerStat.RegisterProvider(this);
    }

    private void OnDestroy()
    {
        if (playerStat != null) playerStat.UnregisterProvider(this);
    }

    /// <summary>
    /// 인벤토리 관련 Modifier를 설정
    /// </summary>
    public void SetModifiers(List<StatModifier> newModifiers)
    {
        _modifiers.Clear();
        if (newModifiers != null) _modifiers.AddRange(newModifiers);
        if (playerStat != null) playerStat.MarkDirty();
    }

    /// <summary>
    /// 단일 Modifier 추가
    /// </summary>
    public void AddModifier(StatType type, ModifierType modType, float value)
    {
        _modifiers.Add(new StatModifier(type, modType, value, ModifierSource.Inventory));
        if (playerStat != null) playerStat.MarkDirty();
    }

    /// <summary>
    /// 모든 Modifier 초기화
    /// </summary>
    public void ClearModifiers()
    {
        _modifiers.Clear();
        if (playerStat != null) playerStat.MarkDirty();
    }

    public IReadOnlyList<StatModifier> GetModifiers()
    {
        return _modifiers;
    }
}
