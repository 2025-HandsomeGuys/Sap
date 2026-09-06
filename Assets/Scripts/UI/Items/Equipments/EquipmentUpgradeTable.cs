using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장비 강화 한 단계(Lv N → N+1)의 비용과 스탯 증가치.
/// <see cref="EquipmentSO.upgradeLevels"/>에 레벨 순서대로 담는다(index 0 = 1→2 강화).
///
/// 비워두면 <see cref="EquipmentUpgradeFormula"/>가 <b>알아볼 수 있는 임시 테스트 테이블</b>을 만들어 쓴다.
/// (레벨디자인 확정 전 임시값 — UI에 "⚠ 임시 테스트 값" 배지가 뜨고, 비용이 1111·2222… 반복숫자다.)
/// </summary>
[System.Serializable]
public class EquipmentUpgradeLevel
{
    [Tooltip("이 단계에서 '추가'되는 스탯 수정치(누적). 장착 시 base statModifiers와 함께 적용된다")]
    public List<EquipmentStatModData> bonusModifiers = new List<EquipmentStatModData>();

    [Tooltip("이 단계에서 추가되는 방어력(표시용). 현재 방어력은 스탯 파이프라인에 연결돼 있지 않아 표시만 된다")]
    public float bonusDefense;

    [Header("비용 (골드 + 창고 광물)")]
    public int goldCost;
    [Tooltip("필요 광물. None이면 저장소가 광물 DB의 첫 광물을 '강화석'으로 대체한다(임시)")]
    public MineralID materialId = MineralID.None;
    public int materialCount;
}

/// <summary>
/// 장비 강화의 <b>임시 폴백 밸런스</b>. 실제 밸런스는 각 <see cref="EquipmentSO.upgradeLevels"/>를 채워
/// 대체할 예정이며(레벨디자인), 그 전까지 이 공식이 만든 값이 쓰인다.
///
/// 임시임을 한눈에 알 수 있게: 골드 비용은 <b>1111의 배수</b>(1111, 2222…), 광물 수는 레벨 수와 같고,
/// 스탯 보너스는 레벨당 딱 떨어지는 신호값(<see cref="TestBonusPerLevel"/>)이다.
/// UI는 폴백을 쓰는 장비에 "⚠ 임시 테스트 값" 배지를 띄운다(<see cref="EquipmentUpgradeStore.IsUsingTestData"/>).
/// </summary>
public static class EquipmentUpgradeFormula
{
    /// <summary>임시 폴백 최대 강화 레벨.</summary>
    public const int TestMaxLevel = 5;

    /// <summary>임시 골드 비용 계단 — level * 이 값 (1111, 2222…).</summary>
    public const int TestGoldStep = 1111;

    /// <summary>임시 스탯 보너스 — 이 장비가 원래 수정하는 각 스탯에 레벨당 이만큼 Flat 추가.</summary>
    public const float TestBonusPerLevel = 5f;

    /// <summary>임시 방어력 보너스 — 레벨당.</summary>
    public const float TestDefensePerLevel = 1f;

    /// <summary>이 장비용 임시 강화 테이블 생성(1→2 … MaxLevel-1→MaxLevel).</summary>
    public static List<EquipmentUpgradeLevel> BuildTestTable(EquipmentSO eq)
    {
        var table = new List<EquipmentUpgradeLevel>();
        for (int lv = 1; lv < TestMaxLevel; lv++)
        {
            var step = new EquipmentUpgradeLevel
            {
                goldCost = lv * TestGoldStep,       // 1111, 2222, 3333, 4444
                materialId = MineralID.None,         // 저장소가 DB 첫 광물로 대체
                materialCount = lv,                  // 1, 2, 3, 4
                bonusDefense = TestDefensePerLevel,
                bonusModifiers = new List<EquipmentStatModData>()
            };

            // 이 장비가 원래 수정하는 스탯마다 레벨당 신호값을 Flat으로 얹는다.
            if (eq != null && eq.statModifiers != null)
            {
                foreach (var m in eq.statModifiers)
                {
                    if (m == null) continue;
                    step.bonusModifiers.Add(new EquipmentStatModData
                    {
                        statType = m.statType,
                        modifierType = ModifierType.Flat,
                        value = TestBonusPerLevel
                    });
                }
            }
            table.Add(step);
        }
        return table;
    }

    // ── 유물 통일 비용(골드 + 광물) — 유물은 자체 upgradeCosts(ItemID)를 쓰지 않고 이 공식으로 통일한다 ──

    /// <summary>유물 Lv currentLevel → currentLevel+1 강화의 임시 골드 비용(2222 배수).</summary>
    public static int RelicGoldCost(int currentLevel) => System.Math.Max(1, currentLevel) * 2222;

    /// <summary>유물 강화의 임시 광물 수.</summary>
    public static int RelicMaterialCount(int currentLevel) => System.Math.Max(1, currentLevel);
}
