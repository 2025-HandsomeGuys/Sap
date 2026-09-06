using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장비 시스템이 제공하는 Modifier를 PlayerStat에 전달하는 어댑터.
/// 장착 중인 장비의 능력치를 StatModifier로 변환.
///
/// 두 갈래를 모은다:
///  - <see cref="EquipmentSO.statModifiers"/> + 강화 단계의 bonusModifiers (그대로 전달)
///  - <see cref="EquipmentSO.defense"/> + 강화 단계의 bonusDefense (<see cref="StatType.Defense"/>로 변환)
/// 실제 감산은 <see cref="StaminaManager.AddInjury"/>가 <see cref="HazardMitigation"/>으로 수행한다.
/// </summary>
public class EquipmentStatProvider : MonoBehaviour, IStatProvider
{
    private PlayerStat playerStat;
    private EquipmentInventory equipmentInventory;
    private List<StatModifier> _cachedModifiers = new List<StatModifier>();
    private bool _isDirty = true;

    private void Start()
    {
        playerStat = GetComponentInParent<PlayerStat>();
        if (playerStat == null) playerStat = FindFirstObjectByType<PlayerStat>();

        equipmentInventory = GetComponentInParent<EquipmentInventory>();
        if (equipmentInventory == null) equipmentInventory = FindFirstObjectByType<EquipmentInventory>();

        if (playerStat != null) playerStat.RegisterProvider(this);

        // 장비 변경 이벤트 구독
        if (equipmentInventory != null)
        {
            equipmentInventory.OnInventoryChanged += OnEquipmentChanged;
        }

        // 강화(+N) 레벨 변경도 스탯에 반영해야 한다(작업대 강화 즉시 적용).
        EquipmentUpgradeStore.OnChanged += OnEquipmentChanged;

        _isDirty = true;
    }

    private void OnDestroy()
    {
        if (playerStat != null) playerStat.UnregisterProvider(this);
        if (equipmentInventory != null)
        {
            equipmentInventory.OnInventoryChanged -= OnEquipmentChanged;
        }
        EquipmentUpgradeStore.OnChanged -= OnEquipmentChanged;
    }

    private void OnEquipmentChanged()
    {
        _isDirty = true;
        if (playerStat != null) playerStat.MarkDirty();
    }

    public IReadOnlyList<StatModifier> GetModifiers()
    {
        if (_isDirty) RebuildModifiers();
        return _cachedModifiers;
    }

    private void RebuildModifiers()
    {
        _cachedModifiers.Clear();

        if (equipmentInventory == null)
        {
            _isDirty = false;
            return;
        }

        // 장착 중인 모든 장비를 순회
        foreach (var slot in equipmentInventory.ReadonlyItems)
        {
            if (slot.item == null) continue;
            if (slot.item is EquipmentSO equipSO)
            {
                // base statModifiers + 강화(+N) 누적 보너스를 함께 적용한다.
                // (EquipmentUpgradeStore가 저작 테이블 또는 임시 테스트 테이블로 누적치를 계산)
                int level = EquipmentUpgradeStore.GetLevel(equipSO);
                var effective = EquipmentUpgradeStore.BuildEffectiveModifiers(equipSO, level);
                foreach (var modData in effective)
                {
                    if (modData == null) continue;
                    _cachedModifiers.Add(new StatModifier(
                        modData.statType,
                        modData.modifierType,
                        modData.value,
                        ModifierSource.Equipment
                    ));
                }

                // 방어력은 statModifiers가 아니라 EquipmentSO.defense + 강화 단계의 bonusDefense에
                // 따로 담겨 있다. StatType.Defense로 옮겨야 StaminaManager.AddInjury의 감산이 먹는다
                // — 이 줄이 없으면 방어력은 툴팁에 뜨기만 하고 아무 일도 하지 않는다.
                float defense = equipSO.defense + EquipmentUpgradeStore.EffectiveBonusDefense(equipSO, level);
                if (!Mathf.Approximately(defense, 0f))
                {
                    _cachedModifiers.Add(new StatModifier(
                        StatType.Defense,
                        ModifierType.Flat,
                        defense,
                        ModifierSource.Equipment
                    ));
                }
            }
        }

        _isDirty = false;
    }
}
