// @tags: price, apply, json, override, economy, pure-logic
using System;
using System.Collections.Generic;
using Relic.Data;

/// <summary>PriceApplier가 값을 써넣을 대상 묶음. 어느 것이든 null일 수 있다.</summary>
public class PriceApplyTargets
{
    public MineralPriceDatabase mineralPrices;
    public MineralDatabase      minerals;
    public ShopItemDatabase     shop;
    public RelicDatabase        relics;
    public UpgradeTreeSO        upgradeTree;
}

/// <summary>
/// priceData.json 의 값을 ScriptableObject 필드에 써넣는다.
///
/// MonoBehaviour에서 분리한 이유는 EditMode에서 합성 SO로 테스트하기 위해서다.
/// 파일 I/O와 인스펙터 참조는 PriceDataLoader가 담당한다.
///
/// 규칙 (설계 §3.2):
///   - JSON에 없는 id는 건드리지 않는다 (에셋 값 유지)
///   - 모르는 id는 경고만 남기고 넘어간다 (예외를 던지지 않는다)
///
/// 설계: Assets/Docs/economy/price-data-json-design.md §5
/// </summary>
public static class PriceApplier
{
    public static PriceApplyResult Apply(PriceData data, PriceApplyTargets targets)
    {
        var result = new PriceApplyResult();
        if (data == null || targets == null) return result;

        ApplyMinerals(data.minerals, targets, result);
        ApplyShop(data.shopItems,     ShopItemType.Item,      targets, result);
        ApplyShop(data.shopEquipment, ShopItemType.Equipment, targets, result);
        ApplyShop(data.shopRelics,    ShopItemType.Relic,     targets, result);
        ApplyRelicUpgrades(data.relicUpgrades, targets, result);
        ApplyNodes(data.upgradeNodes, targets, result);

        return result;
    }

    private static void ApplyMinerals(MineralPriceEntry[] entries, PriceApplyTargets t, PriceApplyResult r)
    {
        if (entries == null || entries.Length == 0) return;

        var priceOverrides = new Dictionary<MineralID, int>();

        foreach (var e in entries)
        {
            if (!Enum.TryParse(e.id, out MineralID id) || id == MineralID.None)
            {
                r.warnings.Add($"minerals: 알 수 없는 id '{e.id}'");
                continue;
            }

            priceOverrides[id] = e.price;

            if (t.minerals == null || t.minerals.allMinerals == null)
            {
                r.warnings.Add($"minerals: MineralDatabase 참조가 없어 '{e.id}' 무게를 적용 못 함");
                continue;
            }

            // GetMineralByID를 쓰지 않는 이유: MineralDatabase는 OnEnable에서 딕셔너리를 굽는데
            // 그 뒤에 allMinerals가 채워진 인스턴스(테스트의 CreateInstance 등)에서는 딕셔너리가
            // 비어 있어도 _initialized가 true라 재구축을 건너뛴다. 리스트를 직접 본다.
            var so = t.minerals.allMinerals.Find(m => m != null && m.mineralID == id);
            if (so == null)
            {
                r.warnings.Add($"minerals: MineralDatabase에 '{e.id}' 없음");
                continue;
            }

            so.weight = e.weight;
            r.applied++;
        }

        if (priceOverrides.Count == 0) return;

        if (t.mineralPrices == null)
        {
            r.warnings.Add("minerals: MineralPriceDatabase 참조가 없어 가격을 적용 못 함");
            return;
        }
        t.mineralPrices.ApplyPriceOverrides(priceOverrides);
    }

    private static void ApplyShop(IdPriceEntry[] entries, ShopItemType kind, PriceApplyTargets t, PriceApplyResult r)
    {
        if (entries == null || entries.Length == 0) return;

        if (t.shop == null || t.shop.shopItems == null)
        {
            r.warnings.Add($"{kind}: ShopItemDatabase 참조가 없어 적용 못 함");
            return;
        }

        foreach (var e in entries)
        {
            ShopItemData row = null;

            switch (kind)
            {
                case ShopItemType.Item:
                    if (!Enum.TryParse(e.id, out ItemID itemId)) { r.warnings.Add($"shopItems: 알 수 없는 id '{e.id}'"); continue; }
                    row = t.shop.shopItems.Find(i => i.itemType == kind && i.itemID == itemId);
                    break;

                case ShopItemType.Equipment:
                    if (!Enum.TryParse(e.id, out EquipmentID eqId)) { r.warnings.Add($"shopEquipment: 알 수 없는 id '{e.id}'"); continue; }
                    row = t.shop.shopItems.Find(i => i.itemType == kind && i.equipmentID == eqId);
                    break;

                case ShopItemType.Relic:
                    if (!Enum.TryParse(e.id, out RelicID relicId)) { r.warnings.Add($"shopRelics: 알 수 없는 id '{e.id}'"); continue; }
                    row = t.shop.shopItems.Find(i => i.itemType == kind && i.relicID == relicId);
                    break;
            }

            if (row == null)
            {
                r.warnings.Add($"{kind}: 상점 목록에 '{e.id}' 항목 없음");
                continue;
            }

            row.price = e.price;
            r.applied++;
        }
    }

    private static void ApplyRelicUpgrades(RelicUpgradeEntry[] entries, PriceApplyTargets t, PriceApplyResult r)
    {
        if (entries == null || entries.Length == 0) return;

        if (t.relics == null || t.relics.allRelics == null)
        {
            r.warnings.Add("relicUpgrades: RelicDatabase 참조가 없어 적용 못 함");
            return;
        }

        foreach (var e in entries)
        {
            if (!Enum.TryParse(e.id, out RelicID id) || id == RelicID.None)
            {
                r.warnings.Add($"relicUpgrades: 알 수 없는 id '{e.id}'");
                continue;
            }

            // GetRelicByID를 쓰지 않는 이유는 ApplyMinerals의 주석과 같다 — OnEnable 이후에
            // allRelics가 채워진 인스턴스에서 딕셔너리가 비어 있을 수 있다.
            var so = t.relics.allRelics.Find(x => x != null && x.id == id);
            if (so == null)
            {
                r.warnings.Add($"relicUpgrades: RelicDatabase에 '{e.id}' 없음");
                continue;
            }

            if (so.upgradeCosts == null || e.gold == null || e.gold.Length != so.upgradeCosts.Length)
            {
                int want = so.upgradeCosts != null ? so.upgradeCosts.Length : 0;
                int got  = e.gold != null ? e.gold.Length : 0;
                r.warnings.Add($"relicUpgrades: '{e.id}' gold 길이 불일치 (에셋 {want}, JSON {got}) — 건너뜀");
                continue;
            }

            for (int i = 0; i < e.gold.Length; i++)
                so.upgradeCosts[i].gold = e.gold[i];

            r.applied++;
        }
    }

    private static void ApplyNodes(NodeCostEntry[] entries, PriceApplyTargets t, PriceApplyResult r)
    {
        if (entries == null || entries.Length == 0) return;

        if (t.upgradeTree == null)
        {
            r.warnings.Add("upgradeNodes: UpgradeTreeSO 참조가 없어 적용 못 함");
            return;
        }

        foreach (var e in entries)
        {
            var node = t.upgradeTree.GetNodeById(e.id);
            if (node == null)
            {
                r.warnings.Add($"upgradeNodes: 트리에 '{e.id}' 노드 없음");
                continue;
            }

            node.cost = e.cost;
            r.applied++;
        }
    }
}
