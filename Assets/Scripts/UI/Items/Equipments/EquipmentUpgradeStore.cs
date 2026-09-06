using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장비별 강화 레벨(+N)의 단일 소스. static — 씬에 컴포넌트를 두지 않아도 되도록
/// <see cref="DayEarningsLedger"/>와 같은 정적 저장소 패턴을 쓴다.
/// 세이브는 <see cref="SaveManager"/>가 <see cref="CaptureSaveData"/>/<see cref="ApplySaveData"/>로 왕복한다.
///
/// 스탯 반영: <see cref="EquipmentStatProvider"/>가 <see cref="OnChanged"/>를 구독하고
/// <see cref="BuildEffectiveModifiers"/>로 base + 누적 강화 보너스를 함께 밀어 넣는다.
///
/// 강화 테이블은 <see cref="EquipmentSO.upgradeLevels"/>(저작) 우선, 비어 있으면
/// <see cref="EquipmentUpgradeFormula.BuildTestTable"/>의 임시 테스트값을 쓴다(UI에 배지 표시).
/// </summary>
public static class EquipmentUpgradeStore
{
    /// <summary>강화 레벨이 바뀌면 발화(스탯 갱신·UI 갱신용).</summary>
    public static event Action OnChanged;

    private static readonly Dictionary<EquipmentID, int> _levels = new Dictionary<EquipmentID, int>();

    // 저작 테이블이 없는 장비의 폴백 테스트 테이블 캐시 (SO별 1회 생성).
    private static readonly Dictionary<EquipmentSO, List<EquipmentUpgradeLevel>> _testTableCache
        = new Dictionary<EquipmentSO, List<EquipmentUpgradeLevel>>();

    // ── 레벨 조회/세팅 ──
    public static int GetLevel(EquipmentID id) => _levels.TryGetValue(id, out int lv) ? lv : 0;
    public static int GetLevel(EquipmentSO eq) => eq == null ? 0 : GetLevel(eq.equipmentID);

    private static void SetLevel(EquipmentID id, int level)
    {
        if (level <= 0) _levels.Remove(id);
        else _levels[id] = level;
        OnChanged?.Invoke();
    }

    // ── 강화 테이블 ──
    /// <summary>저작 테이블이 없으면(폴백 임시값 사용) true. UI 배지·경고 로그용.</summary>
    public static bool IsUsingTestData(EquipmentSO eq)
        => eq == null || eq.upgradeLevels == null || eq.upgradeLevels.Count == 0;

    /// <summary>이 장비의 강화 테이블(저작 우선, 없으면 임시 테스트 테이블).</summary>
    public static IReadOnlyList<EquipmentUpgradeLevel> ResolveTable(EquipmentSO eq)
    {
        if (eq == null) return System.Array.Empty<EquipmentUpgradeLevel>();
        if (eq.upgradeLevels != null && eq.upgradeLevels.Count > 0) return eq.upgradeLevels;

        if (!_testTableCache.TryGetValue(eq, out var table) || table == null)
        {
            table = EquipmentUpgradeFormula.BuildTestTable(eq);
            _testTableCache[eq] = table;
        }
        return table;
    }

    /// <summary>이 장비의 최대 강화 레벨(= 테이블 단계 수).</summary>
    public static int MaxLevel(EquipmentSO eq) => ResolveTable(eq).Count;

    /// <summary>다음 강화 단계(현재 레벨 → +1). 만렙이면 null.</summary>
    public static EquipmentUpgradeLevel GetNextStep(EquipmentSO eq)
    {
        var table = ResolveTable(eq);
        int cur = GetLevel(eq);
        return (cur >= 0 && cur < table.Count) ? table[cur] : null;
    }

    /// <summary>강화 단계가 요구하는 재료 광물(materialId None이면 DB 첫 광물로 대체).</summary>
    public static MineralSO ResolveMaterial(EquipmentUpgradeLevel step)
    {
        var db = MineralDatabase.Instance;
        if (db == null) return null;

        if (step != null && step.materialId != MineralID.None)
            return db.GetMineralByID(step.materialId);

        // materialId 미지정(임시값) → DB의 첫 유효 광물을 '강화석'으로 쓴다.
        if (db.allMinerals != null)
            foreach (var m in db.allMinerals)
                if (m != null && m.mineralID != MineralID.None) return m;
        return null;
    }

    // ── 스탯 적용용 ──
    /// <summary>base statModifiers + 1..level 누적 bonusModifiers. StatProvider·미리보기 공용.</summary>
    public static List<EquipmentStatModData> BuildEffectiveModifiers(EquipmentSO eq, int level)
    {
        var result = new List<EquipmentStatModData>();
        if (eq == null) return result;

        if (eq.statModifiers != null) result.AddRange(eq.statModifiers);

        var table = ResolveTable(eq);
        int steps = Mathf.Clamp(level, 0, table.Count);
        for (int i = 0; i < steps; i++)
        {
            var mods = table[i]?.bonusModifiers;
            if (mods != null) result.AddRange(mods);
        }
        return result;
    }

    /// <summary>강화로 누적된 방어력 보너스(표시용).</summary>
    public static float EffectiveBonusDefense(EquipmentSO eq, int level)
    {
        var table = ResolveTable(eq);
        int steps = Mathf.Clamp(level, 0, table.Count);
        float sum = 0f;
        for (int i = 0; i < steps; i++) if (table[i] != null) sum += table[i].bonusDefense;
        return sum;
    }

    // ── 강화 실행 ──
    /// <summary>
    /// 현재 레벨 → +1 강화 시도. 골드+창고 광물 비용을 검증·차감한 뒤 레벨을 올린다.
    /// 재화가 부족하거나 만렙이면 false(변경 없음).
    /// </summary>
    public static bool TryUpgrade(EquipmentSO eq, PlayerStat stat, WarehouseManager warehouse)
    {
        if (eq == null || stat == null || warehouse == null) return false;

        var step = GetNextStep(eq);
        if (step == null) return false; // 만렙

        var material = ResolveMaterial(step);
        int haveMineral = (material != null) ? warehouse.GetMineralCount(material) : 0;

        // 검증 (차감 전에 둘 다 확인해 한쪽만 빠지는 일이 없게)
        if (stat.Gold < step.goldCost) return false;
        if (step.materialCount > 0 && (material == null || haveMineral < step.materialCount)) return false;

        // 차감
        if (step.goldCost > 0)
        {
            if (!stat.SpendGold(step.goldCost)) return false;
            DayEarningsLedger.Report(DayEarningsCategory.Upgrade, -step.goldCost);
        }
        if (step.materialCount > 0 && material != null)
            warehouse.RemoveMineral(material, step.materialCount);

        SetLevel(eq.equipmentID, GetLevel(eq) + 1);

        // 사운드는 여기서 울리지 않는다.
        // 강화음(SfxKeys.EquipEnhance)은 EquipmentUpgradeOverlayUI의 망치 타격 연출에 붙어 있다
        // (타격 1회 = 소리 1회). 여기서 3연타를 또 울리면 연출이 끝난 뒤 별개의 버스트가
        // 몰아서 터져 "강화음이 여러 번 난다"로 들린다. 유물 강화도 같은 연출을 타므로
        // UI 쪽에 두는 편이 장비/유물 양쪽에 일관되게 적용된다.

        return true;
    }

    // ── 세이브 왕복 ──
    public static EquipmentUpgradeSaveData CaptureSaveData()
    {
        var data = new EquipmentUpgradeSaveData();
        foreach (var kv in _levels)
            if (kv.Value > 0)
                data.entries.Add(new EquipmentUpgradeEntry { id = kv.Key.ToString(), level = kv.Value });
        return data;
    }

    public static void ApplySaveData(EquipmentUpgradeSaveData data)
    {
        _levels.Clear();
        if (data != null && data.entries != null)
        {
            foreach (var e in data.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id) || e.level <= 0) continue;
                if (Enum.TryParse(e.id, out EquipmentID id) && id != EquipmentID.None)
                    _levels[id] = e.level;
            }
        }
        OnChanged?.Invoke();
    }
}
