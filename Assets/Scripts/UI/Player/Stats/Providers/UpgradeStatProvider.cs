using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 업그레이드 시스템이 제공하는 Modifier를 PlayerStat에 전달하는 어댑터.
/// 기존 UpgradeManager의 해금 데이터를 읽어 StatModifier 리스트로 변환.
/// </summary>
public class UpgradeStatProvider : MonoBehaviour, IStatProvider
{
    private PlayerStat playerStat;
    private List<StatModifier> _cachedModifiers = new List<StatModifier>();
    private bool _isDirty = true;

    // 지금 이벤트를 물려 둔 매니저. Instance가 다른 인스턴스로 바뀌면 갈아탄다.
    private UpgradeManager _subscribedManager;

    // UpgradeEffectType → StatType 매핑
    private static readonly Dictionary<UpgradeEffectType, StatType> _typeMap = new Dictionary<UpgradeEffectType, StatType>
    {
        { UpgradeEffectType.MiningSpeedMultiplier,  StatType.MiningSpeed },
        { UpgradeEffectType.MiningRangeMultiplier,  StatType.MiningRange },
        { UpgradeEffectType.MiningRangeUp,          StatType.MiningRange },
        { UpgradeEffectType.MiningSpeedUp,          StatType.MiningSpeed },
        { UpgradeEffectType.MiningLevel,            StatType.MiningLevel },
        { UpgradeEffectType.MoveSpeedMultiplier,    StatType.MoveSpeed },
        { UpgradeEffectType.ClimbSpeedMultiplier,   StatType.WallClimbSpeed },
        { UpgradeEffectType.StaminaCostMultiplier,  StatType.StaminaCostPerSecond },
        { UpgradeEffectType.StaminaCostReduce,      StatType.StaminaCostReduce },
        { UpgradeEffectType.MineralExtraDropChance, StatType.MineralExtraDropChance },
        { UpgradeEffectType.RockMineralCountUp,     StatType.RockMineralCountUp },
        { UpgradeEffectType.MiningCooldownMultiplier, StatType.MiningCooldown },
        { UpgradeEffectType.DamageMultiplier,       StatType.Damage },
        { UpgradeEffectType.ToolRange,              StatType.ToolRange },
        { UpgradeEffectType.ToolChargeTimeReduce,   StatType.ToolChargeTimeReduce },
        { UpgradeEffectType.PickaxeDamageUp,        StatType.PickaxeDamageUp },
        { UpgradeEffectType.RareMineralChance,      StatType.RareMineralChance },
        { UpgradeEffectType.ShovelStaminaReduce,    StatType.ShovelStaminaReduce },
        { UpgradeEffectType.PickaxeStaminaReduce,   StatType.PickaxeStaminaReduce },
        { UpgradeEffectType.DrillBatteryCapacity,   StatType.DrillBatteryCapacity },
        { UpgradeEffectType.DrillBatteryRegen,      StatType.DrillBatteryRegen },
        { UpgradeEffectType.DrillDrainReduce,       StatType.DrillDrainReduce },
        { UpgradeEffectType.DrillRadiusUp,          StatType.DrillRadius },
        { UpgradeEffectType.CritChanceUp,           StatType.CritChanceUp },
        { UpgradeEffectType.DefenseUp,              StatType.Defense },
        { UpgradeEffectType.MaxStaminaUp,           StatType.MaxStamina },
        { UpgradeEffectType.FallDamageReduce,       StatType.FallDamageReduce },
        { UpgradeEffectType.JumpForceMultiplier,    StatType.JumpForce },
        { UpgradeEffectType.WallClimbSpeed,         StatType.WallClimbSpeed },
        { UpgradeEffectType.MoveSpeedUp,            StatType.MoveSpeed },
        { UpgradeEffectType.JumpForceUp,            StatType.JumpForce },
        { UpgradeEffectType.MineralSellBonus,       StatType.MineralSellBonus },
        { UpgradeEffectType.InventorySlotUp,        StatType.InventorySlotUp },
        { UpgradeEffectType.InventoryWeightUp,      StatType.InventoryWeightUp },
        { UpgradeEffectType.WarehouseCapacityUp,    StatType.WarehouseCapacityUp },
        { UpgradeEffectType.ConsumableSlotUnlock,   StatType.ConsumableSlotUnlock },
        { UpgradeEffectType.VisionRadiusUp,         StatType.VisionRadiusUp },
        { UpgradeEffectType.FlashlightRangeUp,      StatType.FlashlightRangeUp },
        { UpgradeEffectType.EnvironmentResistance,  StatType.EnvironmentResistance },
    };

    private void Start()
    {
        playerStat = GetComponentInParent<PlayerStat>();
        if (playerStat == null) playerStat = FindFirstObjectByType<PlayerStat>();

        if (playerStat != null) playerStat.RegisterProvider(this);

        Subscribe(UpgradeManager.Instance);

        _isDirty = true;
    }

    private void OnDestroy()
    {
        if (playerStat != null) playerStat.UnregisterProvider(this);

        Subscribe(null);
    }

    /// <summary>
    /// 구독 대상을 바꾼다. 구독해 둔 매니저를 직접 들고 있는 게 핵심이다 —
    /// UpgradeManager.Instance는 씬을 넘나들며 다른 인스턴스로 교체될 수 있고
    /// (트리 없는 인스턴스가 선점했다가 인계되는 경우 포함), 그때 예전 인스턴스의
    /// 이벤트만 붙들고 있으면 해금을 알리는 신호가 영영 안 오고 캐시가 굳는다.
    /// </summary>
    private void Subscribe(UpgradeManager target)
    {
        if (_subscribedManager == target) return;

        if (_subscribedManager != null)
            _subscribedManager.OnUpgradeStateChanged -= OnUpgradeChanged;

        _subscribedManager = target;

        if (_subscribedManager != null)
            _subscribedManager.OnUpgradeStateChanged += OnUpgradeChanged;
    }

    private void OnUpgradeChanged()
    {
        _isDirty = true;
        if (playerStat != null) playerStat.MarkDirty();
    }

    public IReadOnlyList<StatModifier> GetModifiers()
    {
        // 매니저가 갈렸으면 구독을 옮기고 무조건 다시 만든다. 예전 인스턴스로 만든 캐시는
        // 노드를 못 찾아 비어 있을 수 있는데, 그 상태로 굳으면 업그레이드가 통째로 사라진다.
        var current = UpgradeManager.Instance;
        if (_subscribedManager != current)
        {
            Subscribe(current);
            _isDirty = true;
        }

        if (_isDirty) RebuildModifiers();
        return _cachedModifiers;
    }

    private void RebuildModifiers()
    {
        _cachedModifiers.Clear();

        UpgradeManager upgradeManager = UpgradeManager.Instance;
        if (upgradeManager == null)
        {
            _isDirty = false;
            return;
        }

        var state = upgradeManager.GetState();
        if (state == null)
        {
            _isDirty = false;
            return;
        }

        // 해금된 모든 노드를 순회
        foreach (string nodeId in state.unlockedNodeIds)
        {
            var node = upgradeManager.GetNodeFromCache(nodeId);
            if (node == null || node.effect == null) continue;

            // 매핑에 없는 타입은 무시
            if (!_typeMap.TryGetValue(node.effect.type, out StatType statType)) continue;

            var modType = node.effect.isPercentage ? ModifierType.Percent : ModifierType.Flat;

            // 다단계 노드는 레벨 수만큼 같은 Modifier를 넣는다.
            // PlayerStat 공식이 (Base + ΣFlat) × ΠPercent라, 이러면 합연산은 ×레벨,
            // 곱연산은 거듭제곱이 되어 '노드를 레벨 수만큼 따로 산 것'과 정확히 같아진다.
            int level = Mathf.Max(1, state.GetLevel(nodeId));
            for (int i = 0; i < level; i++)
                _cachedModifiers.Add(new StatModifier(statType, modType, node.effect.value, ModifierSource.Upgrade));
        }

        _isDirty = false;
    }
}
