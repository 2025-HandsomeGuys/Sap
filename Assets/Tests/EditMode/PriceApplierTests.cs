// @tags: test, editmode, price, json, loader, economy
using System.Collections.Generic;
using NUnit.Framework;
using Relic.Data;
using UnityEngine;

/// <summary>
/// PriceApplier의 적용 로직 검증. 합성 ScriptableObject를 만들어 쓰고,
/// 프로젝트의 실제 에셋은 건드리지 않는다.
///
/// 설계: Assets/Docs/economy/price-data-json-design.md §5, §7.2
/// </summary>
public class PriceApplierTests
{
    private readonly List<Object> _created = new List<Object>();

    private T Make<T>() where T : ScriptableObject
    {
        var so = ScriptableObject.CreateInstance<T>();
        _created.Add(so);
        return so;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var o in _created) Object.DestroyImmediate(o);
        _created.Clear();
    }

    /// <summary>광물 2종·상점 3종·유물강화 1종·노드 1개를 가진 최소 타깃.</summary>
    private PriceApplyTargets MakeTargets(
        out MineralSO coal, out MineralSO iron,
        out ShopItemDatabase shop, out RelicSO magnet, out UpgradeNodeSO node)
    {
        coal = Make<MineralSO>();
        coal.mineralID = MineralID.Coal;
        coal.weight = 1f;

        iron = Make<MineralSO>();
        iron.mineralID = MineralID.Iron;
        iron.weight = 1f;

        // InitializeDictionary()를 부르지 않는다 — CreateInstance 시점의 OnEnable이 이미
        // 빈 딕셔너리를 굽고 _initialized를 true로 만들어 재구축이 안 된다.
        // PriceApplier는 그래서 allMinerals 리스트를 직접 본다.
        var mineralDb = Make<MineralDatabase>();
        mineralDb.allMinerals = new List<MineralSO> { coal, iron };

        var priceDb = Make<MineralPriceDatabase>();
        priceDb.ApplyPriceOverrides(new Dictionary<MineralID, int>
        {
            { MineralID.Coal, 1 },
            { MineralID.Iron, 1 },
        });

        shop = Make<ShopItemDatabase>();
        shop.shopItems = new List<ShopItemData>
        {
            new ShopItemData { itemType = ShopItemType.Item,      itemID = ItemID.FrostbiteResist, price = 1 },
            new ShopItemData { itemType = ShopItemType.Equipment, equipmentID = EquipmentID.MinerHelmet, price = 1 },
            new ShopItemData { itemType = ShopItemType.Relic,     relicID = RelicID.Magnet, price = 1 },
        };

        magnet = Make<RelicSO>();
        magnet.id = RelicID.Magnet;
        magnet.maxLevel = 3;
        magnet.upgradeCosts = new[]
        {
            new RelicUpgradeCost { gold = 1 },
            new RelicUpgradeCost { gold = 1 },
        };
        var relicDb = Make<RelicDatabase>();
        relicDb.allRelics = new List<RelicSO> { magnet };

        node = Make<UpgradeNodeSO>();
        node.nodeId = "MiningSpeed_T0_01";
        node.cost = 1;
        var tree = Make<UpgradeTreeSO>();
        tree.allNodes = new List<UpgradeNodeSO> { node };

        return new PriceApplyTargets
        {
            mineralPrices = priceDb,
            minerals      = mineralDb,
            shop          = shop,
            relics        = relicDb,
            upgradeTree   = tree,
        };
    }

    private static PriceData FullData()
    {
        return new PriceData
        {
            minerals = new[]
            {
                new MineralPriceEntry { id = "Coal", price = 18, weight = 0.5f },
                new MineralPriceEntry { id = "Iron", price = 50, weight = 1.2f },
            },
            shopItems     = new[] { new IdPriceEntry { id = "FrostbiteResist", price = 200 } },
            shopEquipment = new[] { new EquipmentPriceEntry { id = "MinerHelmet", price = 2000, layer = "L0_Dirt" } },
            shopRelics    = new[] { new IdPriceEntry { id = "Magnet",          price = 12000 } },
            relicUpgrades = new[] { new RelicUpgradeEntry { id = "Magnet", gold = new[] { 6000, 18000 } } },
            upgradeNodes  = new[] { new NodeCostEntry { id = "MiningSpeed_T0_01", cost = 700 } },
        };
    }

    [Test]
    public void Apply_WritesMineralWeightAndPrice()
    {
        var t = MakeTargets(out var coal, out var iron, out _, out _, out _);
        PriceApplier.Apply(FullData(), t);

        Assert.AreEqual(0.5f, coal.weight, 0.0001f);
        Assert.AreEqual(1.2f, iron.weight, 0.0001f);
        Assert.AreEqual(18, t.mineralPrices.GetPrice(MineralID.Coal));
        Assert.AreEqual(50, t.mineralPrices.GetPrice(MineralID.Iron));
    }

    [Test]
    public void Apply_WritesShopPricesForAllThreeKinds()
    {
        var t = MakeTargets(out _, out _, out var shop, out _, out _);
        PriceApplier.Apply(FullData(), t);

        Assert.AreEqual(200,   shop.shopItems.Find(i => i.itemType == ShopItemType.Item).price);
        Assert.AreEqual(2000,  shop.shopItems.Find(i => i.itemType == ShopItemType.Equipment).price);
        Assert.AreEqual(12000, shop.shopItems.Find(i => i.itemType == ShopItemType.Relic).price);
    }

    [Test]
    public void Apply_WritesRelicUpgradeGold()
    {
        var t = MakeTargets(out _, out _, out _, out var magnet, out _);
        PriceApplier.Apply(FullData(), t);

        Assert.AreEqual(6000,  magnet.upgradeCosts[0].gold);
        Assert.AreEqual(18000, magnet.upgradeCosts[1].gold);
    }

    [Test]
    public void Apply_WritesUpgradeNodeCost()
    {
        var t = MakeTargets(out _, out _, out _, out _, out var node);
        PriceApplier.Apply(FullData(), t);

        Assert.AreEqual(700, node.cost);
    }

    [Test]
    public void Apply_LeavesAbsentIdsUntouched()
    {
        // JSON에 Iron이 없으면 Iron은 에셋 값 그대로여야 한다
        var t = MakeTargets(out var coal, out var iron, out _, out _, out _);
        var data = new PriceData
        {
            minerals = new[] { new MineralPriceEntry { id = "Coal", price = 18, weight = 0.5f } },
        };

        PriceApplier.Apply(data, t);

        Assert.AreEqual(0.5f, coal.weight, 0.0001f);
        Assert.AreEqual(1f,   iron.weight, 0.0001f, "JSON에 없는 광물은 건드리면 안 된다");
        Assert.AreEqual(1,    t.mineralPrices.GetPrice(MineralID.Iron));
    }

    [Test]
    public void Apply_UnknownIdWarnsButDoesNotThrow()
    {
        var t = MakeTargets(out _, out _, out _, out _, out _);
        var data = new PriceData
        {
            minerals     = new[] { new MineralPriceEntry { id = "NotAMineral", price = 5, weight = 5f } },
            upgradeNodes = new[] { new NodeCostEntry { id = "NoSuchNode", cost = 5 } },
        };

        PriceApplyResult result = null;
        Assert.DoesNotThrow(() => result = PriceApplier.Apply(data, t));
        Assert.AreEqual(2, result.warnings.Count, "모르는 id 2건이 경고로 남아야 한다");
        Assert.AreEqual(0, result.applied);
    }

    [Test]
    public void Apply_NullDataIsSafe()
    {
        var t = MakeTargets(out _, out _, out _, out _, out _);
        PriceApplyResult result = null;
        Assert.DoesNotThrow(() => result = PriceApplier.Apply(null, t));
        Assert.AreEqual(0, result.applied);
    }

    [Test]
    public void Apply_NullSectionsAreSafe()
    {
        // JsonUtility는 JSON에 없는 배열 필드를 null로 남긴다
        var t = MakeTargets(out _, out _, out _, out _, out _);
        Assert.DoesNotThrow(() => PriceApplier.Apply(new PriceData(), t));
    }

    [Test]
    public void Apply_ShortRelicGoldArrayWarnsAndSkips()
    {
        var t = MakeTargets(out _, out _, out _, out var magnet, out _);
        var data = new PriceData
        {
            relicUpgrades = new[] { new RelicUpgradeEntry { id = "Magnet", gold = new[] { 6000 } } },
        };

        PriceApplyResult result = null;
        Assert.DoesNotThrow(() => result = PriceApplier.Apply(data, t));
        Assert.AreEqual(1, magnet.upgradeCosts[0].gold, "길이가 안 맞으면 아무것도 쓰지 않는다");
        Assert.AreEqual(1, magnet.upgradeCosts[1].gold);
        Assert.AreEqual(1, result.warnings.Count);
    }

    [Test]
    public void Apply_MissingTargetsAreSafe()
    {
        // 인스펙터 참조를 안 물린 채 실행돼도 예외가 나면 안 된다
        var empty = new PriceApplyTargets();
        PriceApplyResult result = null;
        Assert.DoesNotThrow(() => result = PriceApplier.Apply(FullData(), empty));
        Assert.Greater(result.warnings.Count, 0);
    }

    [Test]
    public void Apply_CountsAppliedEntries()
    {
        var t = MakeTargets(out _, out _, out _, out _, out _);
        var result = PriceApplier.Apply(FullData(), t);

        // 광물 2 + 소모품 1 + 장비 1 + 유물 1 + 유물강화 1 + 노드 1 = 7
        Assert.AreEqual(7, result.applied);
        Assert.AreEqual(0, result.warnings.Count);
    }
}
