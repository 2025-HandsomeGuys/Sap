using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 장비 강화 검증:
///  (1) 장비를 장착하면 실제로 스탯이 오른다 (EquipmentStatProvider → PlayerStat).
///  (2) 강화(+N)하면 그 수치만큼 스탯이 더 오른다 (EquipmentUpgradeStore 누적 보너스).
///  (3) 강화 테이블/누적 계산이 맞다.
///
/// (1)(2)는 실제 게임에서 쓰는 컴포넌트(EquipmentStatProvider·EquipmentInventory·PlayerStat)를
/// 그대로 붙여 검증한다. EditMode라 Start()가 자동 실행되지 않으므로 리플렉션으로 한 번 호출한다.
/// </summary>
public class EquipmentUpgradeTests
{
    private readonly List<Object> _created = new List<Object>();

    [SetUp]
    public void Setup() => EquipmentUpgradeStore.ApplySaveData(null); // 강화 레벨 전역 상태 초기화

    [TearDown]
    public void TearDown()
    {
        EquipmentUpgradeStore.ApplySaveData(null);
        foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
        _created.Clear();
    }

    // ── 헬퍼 ──
    private EquipmentSO MakeEquipment(EquipmentID id, EquipmentType type, StatType stat, float baseValue)
    {
        var eq = ScriptableObject.CreateInstance<EquipmentSO>();
        eq.equipmentID = id;
        eq.equipmentType = type;
        eq.stackable = false;
        eq.maxStackSize = 1;
        eq.statModifiers = new List<EquipmentStatModData>
        {
            new EquipmentStatModData { statType = stat, modifierType = ModifierType.Flat, value = baseValue }
        };
        _created.Add(eq);
        return eq;
    }

    private T AddComp<T>(GameObject go) where T : Component => go.AddComponent<T>();

    // EditMode에선 Start()가 자동 실행되지 않으므로 직접 호출(같은 GO의 PlayerStat/EquipmentInventory를 찾아 등록·구독).
    private static void InvokeStart(MonoBehaviour mb)
    {
        var start = mb.GetType().GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(start, "EquipmentStatProvider.Start 를 찾지 못했습니다.");
        start.Invoke(mb, null);
    }

    // ===================================================
    // (3) 순수 누적 계산
    // ===================================================
    [Test]
    public void BuildEffectiveModifiers_Level0_ReturnsBaseOnly()
    {
        var eq = MakeEquipment(EquipmentID.LeatherHelmet, EquipmentType.Head, StatType.MiningSpeed, 2f);

        var mods = EquipmentUpgradeStore.BuildEffectiveModifiers(eq, 0);

        Assert.AreEqual(1, mods.Count);
        Assert.AreEqual(StatType.MiningSpeed, mods[0].statType);
        Assert.AreEqual(2f, mods[0].value, 0.001f);
    }

    [Test]
    public void BuildEffectiveModifiers_Level1_AddsTestBonus()
    {
        var eq = MakeEquipment(EquipmentID.LeatherHelmet, EquipmentType.Head, StatType.MiningSpeed, 2f);

        // 저작 테이블이 비어 있어 임시 테스트 테이블(레벨당 Flat +TestBonusPerLevel)이 쓰인다.
        var mods = EquipmentUpgradeStore.BuildEffectiveModifiers(eq, 1);

        float sum = 0f;
        foreach (var m in mods) sum += m.value;
        Assert.AreEqual(2f + EquipmentUpgradeFormula.TestBonusPerLevel, sum, 0.001f);
    }

    [Test]
    public void TestTable_MaxLevel_MatchesFormula()
    {
        var eq = MakeEquipment(EquipmentID.IronHelmet, EquipmentType.Head, StatType.MoveSpeed, 1f);
        Assert.IsTrue(EquipmentUpgradeStore.IsUsingTestData(eq));
        Assert.AreEqual(EquipmentUpgradeFormula.TestMaxLevel - 1, EquipmentUpgradeStore.MaxLevel(eq));
    }

    // ===================================================
    // (1)(2) 실제 컴포넌트 경로: 장착 → 스탯↑, 강화 → 스탯 더↑
    // ===================================================
    [Test]
    public void Equipping_RaisesFinalStat_AndUpgrading_RaisesMore()
    {
        var go = new GameObject("PlayerRig");
        _created.Add(go);

        var stat = AddComp<PlayerStat>(go);
        var inv = AddComp<EquipmentInventory>(go);
        var provider = AddComp<EquipmentStatProvider>(go);

        stat.SetBaseValue(StatType.MoveSpeed, 5f);
        InvokeStart(provider); // 등록 + 이벤트 구독

        // 장착 전
        Assert.AreEqual(5f, stat.GetFinalValue(StatType.MoveSpeed), 0.001f, "장착 전 기준값이어야 한다");

        // 장착 (+2 Flat) → OnInventoryChanged → provider dirty
        var helmet = MakeEquipment(EquipmentID.LeatherHelmet, EquipmentType.Head, StatType.MoveSpeed, 2f);
        int added = inv.AddItem(helmet);
        Assert.AreEqual(1, added, "장비가 슬롯에 들어가야 한다");
        Assert.AreEqual(7f, stat.GetFinalValue(StatType.MoveSpeed), 0.001f, "장착하면 +2 올라야 한다");

        // 강화 +1 (임시 테스트 테이블 = 레벨당 +TestBonusPerLevel) → OnChanged → provider dirty
        EquipmentUpgradeStore.ApplySaveData(new EquipmentUpgradeSaveData
        {
            entries = new List<EquipmentUpgradeEntry>
            {
                new EquipmentUpgradeEntry { id = EquipmentID.LeatherHelmet.ToString(), level = 1 }
            }
        });

        float expected = 5f + 2f + EquipmentUpgradeFormula.TestBonusPerLevel;
        Assert.AreEqual(expected, stat.GetFinalValue(StatType.MoveSpeed), 0.001f,
            "강화하면 강화 수치만큼 더 올라야 한다");
    }

    [Test]
    public void SaveData_RoundTrips()
    {
        EquipmentUpgradeStore.ApplySaveData(new EquipmentUpgradeSaveData
        {
            entries = new List<EquipmentUpgradeEntry>
            {
                new EquipmentUpgradeEntry { id = EquipmentID.MinerBoots.ToString(), level = 3 }
            }
        });
        Assert.AreEqual(3, EquipmentUpgradeStore.GetLevel(EquipmentID.MinerBoots));

        var captured = EquipmentUpgradeStore.CaptureSaveData();
        EquipmentUpgradeStore.ApplySaveData(null);
        Assert.AreEqual(0, EquipmentUpgradeStore.GetLevel(EquipmentID.MinerBoots));

        EquipmentUpgradeStore.ApplySaveData(captured);
        Assert.AreEqual(3, EquipmentUpgradeStore.GetLevel(EquipmentID.MinerBoots));
    }
}
