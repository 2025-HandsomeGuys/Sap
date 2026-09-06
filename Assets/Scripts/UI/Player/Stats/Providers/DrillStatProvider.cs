using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 드릴 시스템의 StatProvider.
/// 드릴의 업그레이드 레벨 등에 따라 배터리 용량, 회복량 등의 Modifier를 제공.
/// </summary>
public class DrillStatProvider : MonoBehaviour, IStatProvider
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
    /// 드릴 관련 Modifier를 설정. 드릴 업그레이드 시 호출.
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
        _modifiers.Add(new StatModifier(type, modType, value, ModifierSource.Drill));
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
