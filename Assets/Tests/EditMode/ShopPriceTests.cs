// @tags: test, editmode, shop, price, equipment, relic, economy, balance
using System.Collections.Generic;
using NUnit.Framework;
using Relic.Data;
using UnityEditor;

/// <summary>
/// 장비·유물 가격 사다리를 고정한다. 이 테스트가 곧 가격표 명세서다 —
/// 수치를 바꾸려면 먼저 Assets/Docs/economy/equipment-relic-price-design.md 를 고치고
/// 여기 기대값을 맞춘다.
///
/// 장비 효과(statModifiers)는 이번 범위가 아니라 검사하지 않는다(설계 §8).
///
/// 가격 조회 출처가 섞여 있다: 가격은 priceData.json(정답)에서 읽고,
/// isAvailable(판매 여부)·행 수는 여전히 ShopItemDatabase.asset에서 읽는다.
/// JSON 스키마(§3.2)에 isAvailable이 없기 때문이다 — 이번 이전은 "가격"만 대상이다.
/// 그래서 Equipment_AllTwentySevenAreOnSale / Relic_TwentySevenAreOnSale_AndTestRelicIsNot /
/// ShopDatabase_StillHasFiftyEightRows 세 테스트는 예외적으로 에셋을 계속 읽는다.
///
/// 네 번째 예외: Relic_TestRelicUpgradeCostsAreUntouched. TestStatRelic(4001)은
/// PriceDataExporter.ExportRelicUpgrades()(Assets/Scripts/Editor/PriceDataExporter.cs:107,
/// `if (so.id == RelicID.TestStatRelic) continue;`)가 JSON 추출에서 아예 제외하므로
/// relicUpgrades 절에 4001 항목이 없다 — JSON으로 옮기면 PriceDataTestSource의
/// Assert.IsNotNull에 걸려 항상 실패한다. 에셋을 읽는 것이 유일하게 가능한 선택이다.
/// </summary>
public class ShopPriceTests
{
    private const string ShopDbPath = "Assets/GameData/ShopData/ShopItemDatabase.asset";
    private const string RelicDir   = "Assets/GameData/Relics";

    // 설계 문서 §5 — 층별 장비 부위 가격 (= §6 유물 티어 구매가)
    private const int Layer1 = 2000;
    private const int Layer2 = 12000;
    private const int Layer3 = 46000;
    private const int Layer4 = 200000;

    // ─────────────────────────────────────────────────────────────
    // 기대 데이터 (설계 §5.1 / §6.1)
    // ─────────────────────────────────────────────────────────────

    /// <summary>세트 이름 → (부위 3개의 equipmentID, 부위당 가격)</summary>
    private static readonly (string set, int[] ids, int price)[] EquipmentSets =
    {
        ("Miner",     new[] { 3004, 3104, 3204 }, Layer1),
        ("Winter",    new[] { 3006, 3106, 3206 }, Layer2),
        ("Climber",   new[] { 3003, 3103, 3203 }, Layer2),
        ("Carrier",   new[] { 3005, 3105, 3205 }, Layer2),
        ("Heat",      new[] { 3007, 3107, 3207 }, Layer3),
        ("Engineer",  new[] { 3008, 3108, 3208 }, Layer3),
        ("Shovel",    new[] { 3010, 3110, 3210 }, Layer3),
        ("Scientist", new[] { 3009, 3109, 3209 }, Layer4),
        ("Marathon",  new[] { 3011, 3111, 3211 }, Layer4),
    };

    /// <summary>relicID → 구매가</summary>
    private static readonly (int id, int price)[] RelicPrices =
    {
        // 티어 1
        (4002, Layer2), (4003, Layer2), (4005, Layer2), (4012, Layer2),
        (4017, Layer2), (4023, Layer2), (4024, Layer2),
        // 티어 2
        (4006, Layer3), (4007, Layer3), (4008, Layer3), (4009, Layer3),
        (4013, Layer3), (4014, Layer3), (4015, Layer3), (4018, Layer3),
        (4021, Layer3), (4022, Layer3), (4027, Layer3), (4028, Layer3),
        // 티어 3
        (4004, Layer4), (4010, Layer4), (4011, Layer4), (4016, Layer4),
        (4019, Layer4), (4020, Layer4), (4025, Layer4), (4026, Layer4),
    };

    /// <summary>테스트용 유물. 상점에서 빼고 강화비도 손대지 않는다.</summary>
    private const int TestRelicId = 4001;

    // ─────────────────────────────────────────────────────────────
    // 로더
    // ─────────────────────────────────────────────────────────────

    private static ShopItemDatabase LoadShopDb()
    {
        var db = AssetDatabase.LoadAssetAtPath<ShopItemDatabase>(ShopDbPath);
        Assert.IsNotNull(db, $"상점 DB를 못 찾음: {ShopDbPath}");
        Assert.IsNotNull(db.shopItems, "shopItems가 null");
        return db;
    }

    private static ShopItemData FindEquipment(ShopItemDatabase db, int equipmentId)
    {
        var row = db.shopItems.Find(i =>
            i.itemType == ShopItemType.Equipment && (int)i.equipmentID == equipmentId);
        Assert.IsNotNull(row, $"equipmentID {equipmentId} 항목이 상점 DB에 없다");
        return row;
    }

    private static ShopItemData FindRelic(ShopItemDatabase db, int relicId)
    {
        var row = db.shopItems.Find(i =>
            i.itemType == ShopItemType.Relic && (int)i.relicID == relicId);
        Assert.IsNotNull(row, $"relicID {relicId} 항목이 상점 DB에 없다");
        return row;
    }

    private static Dictionary<int, RelicSO> LoadRelicAssets()
    {
        var map = new Dictionary<int, RelicSO>();
        string[] guids = AssetDatabase.FindAssets("t:RelicSO", new[] { RelicDir });
        foreach (string guid in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<RelicSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null) map[(int)so.id] = so;
        }
        Assert.Greater(map.Count, 0, $"{RelicDir}에 RelicSO 에셋이 없다");
        return map;
    }

    private static int EquipmentPrice(int equipmentId)
    {
        string name = ((EquipmentID)equipmentId).ToString();
        return PriceDataTestSource.ShopPrice(
            PriceDataTestSource.Load().shopEquipment, "shopEquipment", name);
    }

    private static int RelicPrice(int relicId)
    {
        string name = ((RelicID)relicId).ToString();
        return PriceDataTestSource.ShopPrice(
            PriceDataTestSource.Load().shopRelics, "shopRelics", name);
    }

    private static int[] RelicUpgradeGold(int relicId)
    {
        string name = ((RelicID)relicId).ToString();
        var data = PriceDataTestSource.Load();
        Assert.IsNotNull(data.relicUpgrades, "priceData.json에 relicUpgrades 절이 없다");
        var e = System.Array.Find(data.relicUpgrades, x => x.id == name);
        Assert.IsNotNull(e, $"priceData.json relicUpgrades에 '{name}' 없음");
        Assert.IsNotNull(e.gold, $"relicUpgrades '{name}': gold가 null");
        return e.gold;
    }

    // ─────────────────────────────────────────────────────────────
    // 장비 — 설계 §5
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Equipment_PricesMatchLayerLadder()
    {
        foreach (var (set, ids, price) in EquipmentSets)
            foreach (int id in ids)
                Assert.AreEqual(price, EquipmentPrice(id), $"{set} / equipmentID {id}");
    }

    [Test]
    public void Equipment_AllTwentySevenAreOnSale()
    {
        // 가격이 곧 잠금이다 — 해금 시스템을 만들지 않고 전부 노출한다 (설계 §3.1)
        var db = LoadShopDb();
        foreach (var (set, ids, _) in EquipmentSets)
            foreach (int id in ids)
                Assert.IsTrue(FindEquipment(db, id).isAvailable,
                    $"{set} / equipmentID {id}가 잠겨 있다");
    }

    [Test]
    public void Equipment_EachSetHasThreePiecesAtOnePrice()
    {
        // 층 안에서 필수/선택 가격을 구분하지 않는다 (설계 §3.2)
        foreach (var (set, ids, _) in EquipmentSets)
        {
            Assert.AreEqual(3, ids.Length, $"{set}: 부위가 3개가 아니다");
            int first = EquipmentPrice(ids[0]);
            foreach (int id in ids)
                Assert.AreEqual(first, EquipmentPrice(id),
                    $"{set}: 부위마다 가격이 다르다 (equipmentID {id})");
        }
    }

    [Test]
    public void Equipment_TotalCostIsOneMillionSevenHundredTwentyEightThousand()
    {
        int total = 0;
        foreach (var (_, ids, _) in EquipmentSets)
            foreach (int id in ids)
                total += EquipmentPrice(id);

        Assert.AreEqual(1_728_000, total, "장비 27종 총액 (설계 §5)");
    }

    // ─────────────────────────────────────────────────────────────
    // 유물 — 설계 §6
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Relic_PricesMatchTierLadder()
    {
        foreach (var (id, price) in RelicPrices)
            Assert.AreEqual(price, RelicPrice(id), $"relicID {id}");
    }

    [Test]
    public void Relic_TwentySevenAreOnSale_AndTestRelicIsNot()
    {
        var db = LoadShopDb();
        foreach (var (id, _) in RelicPrices)
            Assert.IsTrue(FindRelic(db, id).isAvailable, $"relicID {id}가 잠겨 있다");

        Assert.IsFalse(FindRelic(db, TestRelicId).isAvailable,
            "TestStatRelic(4001)은 테스트용이라 상점에서 빼야 한다");
    }

    [Test]
    public void Relic_UpgradeCostsAreHalfAndOneAndHalfOfPrice()
    {
        // 강화비 = 구매가의 50% / 150% (설계 §3.3)
        foreach (var (id, price) in RelicPrices)
        {
            int[] gold = RelicUpgradeGold(id);
            Assert.AreEqual(2, gold.Length,
                $"relicID {id}: upgradeCosts 길이는 maxLevel-1 = 2여야 한다");

            Assert.AreEqual(price / 2, gold[0], $"relicID {id}: Lv1→2");
            Assert.AreEqual(price * 3 / 2, gold[1], $"relicID {id}: Lv2→3");
        }
    }

    [Test]
    public void Relic_FullUpgradeEqualsOneEquipmentSetOfSameTier()
    {
        // 유물 하나 풀강 = 같은 층 장비 한 세트 (설계 §3.3)
        foreach (var (id, price) in RelicPrices)
        {
            int[] gold = RelicUpgradeGold(id);
            int fullCost = RelicPrice(id) + gold[0] + gold[1];
            Assert.AreEqual(price * 3, fullCost, $"relicID {id}: 풀강 총액은 구매가의 3배");
        }
    }

    [Test]
    public void Relic_TestRelicUpgradeCostsAreUntouched()
    {
        // 4001은 상점에서만 빼고 강화비는 원래 값(100/250)을 유지한다
        var assets = LoadRelicAssets();
        Assert.IsTrue(assets.ContainsKey(TestRelicId), "TestStatRelic 에셋이 없다");
        var so = assets[TestRelicId];
        Assert.AreEqual(100, so.upgradeCosts[0].gold);
        Assert.AreEqual(250, so.upgradeCosts[1].gold);
    }

    // ─────────────────────────────────────────────────────────────
    // 부수 피해 방지
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void ShopDatabase_StillHasFiftyEightRows()
    {
        // 항목을 추가·삭제하지 않는다. 값만 바꾼다.
        Assert.AreEqual(58, LoadShopDb().shopItems.Count);
    }

    [Test]
    public void Consumables_AreUntouched()
    {
        // 광물 가격 작업에서 정한 값이다 — 이번 작업이 건드리면 안 된다
        var data = PriceDataTestSource.Load();
        int frost = PriceDataTestSource.ShopPrice(data.shopItems, "shopItems", "FrostbiteResist");
        int burn  = PriceDataTestSource.ShopPrice(data.shopItems, "shopItems", "BurnResist");
        Assert.AreEqual(200, frost, "동상 저항 200G");
        Assert.AreEqual(15,  burn,  "화상 저항 15G");
    }
}
