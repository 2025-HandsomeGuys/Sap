// @tags: test, editmode, mineral, economy, balance, price, weight, regression
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode 테스트용 priceData.json 로더. 게임 런타임 경로(PriceDataLoader)와 별개로
/// 파일을 직접 읽는다 — 테스트는 "JSON에 적힌 값"을 검증하지 로더 동작을 검증하지 않는다
/// (로더는 PriceApplierTests 담당).
/// </summary>
public static class PriceDataTestSource
{
    private static PriceData _cached;

    public static PriceData Load()
    {
        if (_cached != null) return _cached;

        string path = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "priceData.json");
        NUnit.Framework.Assert.IsTrue(System.IO.File.Exists(path), $"priceData.json 없음: {path}");

        _cached = UnityEngine.JsonUtility.FromJson<PriceData>(System.IO.File.ReadAllText(path));
        NUnit.Framework.Assert.IsNotNull(_cached, "priceData.json 파싱 실패");
        return _cached;
    }

    public static int MineralPrice(string id)  => Find(id).price;
    public static float MineralWeight(string id) => Find(id).weight;

    private static MineralPriceEntry Find(string id)
    {
        var data = Load();
        NUnit.Framework.Assert.IsNotNull(data.minerals, "priceData.json에 minerals 절이 없다");
        var e = System.Array.Find(data.minerals, m => m.id == id);
        NUnit.Framework.Assert.IsNotNull(e, $"priceData.json에 광물 '{id}' 없음");
        return e;
    }

    public static int ShopPrice(IdPriceEntry[] section, string sectionName, string id)
    {
        NUnit.Framework.Assert.IsNotNull(section, $"priceData.json에 {sectionName} 절이 없다");
        var e = System.Array.Find(section, x => x.id == id);
        NUnit.Framework.Assert.IsNotNull(e, $"priceData.json {sectionName}에 '{id}' 없음");
        return e.price;
    }
}

/// <summary>
/// priceData.json(가격·무게)과 tileData.json(산출량)을 읽어 설계 목표치와 대조한다.
/// 이 테스트가 곧 밸런스 명세서다 — 수치를 바꾸려면 먼저
/// Assets/Docs/economy/mineral-price-design.md 를 고치고 여기 기대값을 맞춘다.
///
/// 3·4층은 아직 재조정 전이라 검사하지 않는다(설계 문서 §8).
/// </summary>
public class MineralBalanceTests
{
    // 설계 문서 §3.1 — 층별 가방 용량
    private const float Layer1Bag = 40f;
    private const float Layer2Bag = 70f;

    // 설계 문서 §3.3 — 선별 편향 상한
    private const float MaxPickBias = 1.20f;

    // ─────────────────────────────────────────────────────────────
    // 로더
    // ─────────────────────────────────────────────────────────────

    private static TileDatabaseJson LoadTileDb()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "tileData.json");
        Assert.IsTrue(File.Exists(path), $"tileData.json 없음: {path}");
        var db = JsonUtility.FromJson<TileDatabaseJson>(File.ReadAllText(path));
        Assert.IsNotNull(db?.tiles, "tileData.json 파싱 실패");
        return db;
    }

    /// <summary>tileType(예: "Dirt")의 광물 규칙을 경제 계산 항목으로 변환한다.</summary>
    private static List<MineralEconomyEntry> BuildLayer(string tileType)
    {
        var tileDb = LoadTileDb();

        TileDataJson tile = tileDb.tiles.Find(t => t != null && t.tileType == tileType);
        Assert.IsNotNull(tile, $"tileData.json에 tileType={tileType} 없음");
        Assert.IsNotNull(tile.minerals, $"{tileType}에 minerals 배열 없음");

        var entries = new List<MineralEconomyEntry>();
        foreach (var rule in tile.minerals)
        {
            Assert.IsTrue(System.Enum.TryParse(rule.mineralType, out MineralID id),
                $"{tileType}/{rule.mineralType}: MineralID 파싱 실패");

            float mid = (rule.perChunk[0] + rule.perChunk[1]) * 0.5f;
            entries.Add(new MineralEconomyEntry(
                id,
                PriceDataTestSource.MineralPrice(rule.mineralType),
                PriceDataTestSource.MineralWeight(rule.mineralType),
                mid));
        }
        return entries;
    }

    // ─────────────────────────────────────────────────────────────
    // 1층 (Dirt) — 설계 문서 §4
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Layer1_WeightedAveragePrice_MatchesDesign()
    {
        Assert.AreEqual(20.55f, MineralEconomy.WeightedAveragePrice(BuildLayer("Dirt")), 0.5f);
    }

    [Test]
    public void Layer1_WeightedAverageWeight_MatchesDesign()
    {
        Assert.AreEqual(0.5744f, MineralEconomy.WeightedAverageWeight(BuildLayer("Dirt")), 0.01f);
    }

    [Test]
    public void Layer1_ItemsPerTrip_IsAbout70()
    {
        Assert.AreEqual(69.6f, MineralEconomy.ItemsPerTrip(Layer1Bag, BuildLayer("Dirt")), 1.5f);
    }

    [Test]
    public void Layer1_RevenuePerTrip_MatchesDesign()
    {
        Assert.AreEqual(1430f, MineralEconomy.RevenuePerTrip(Layer1Bag, BuildLayer("Dirt")), 60f);
    }

    [Test]
    public void Layer1_PickBias_StaysUnderLimit()
    {
        float bias = MineralEconomy.PickBias(BuildLayer("Dirt"));
        Assert.LessOrEqual(bias, MaxPickBias,
            $"1층 선별 편향 {bias:F3} — 최고 효율 광물의 무게를 올려야 한다 (설계 §3.3)");
    }

    [Test]
    public void Layer1_PerChunkTotal_IsUnchanged()
    {
        // 1층 산출량은 의도된 값이라 이번 작업에서 건드리지 않는다 (설계 §4)
        Assert.AreEqual(249.5f, MineralEconomy.TotalPerChunk(BuildLayer("Dirt")), 1f);
    }

    // ─────────────────────────────────────────────────────────────
    // 2층 (Ice) — 설계 문서 §5
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Layer2_WeightedAveragePrice_MatchesDesign()
    {
        Assert.AreEqual(95.03f, MineralEconomy.WeightedAveragePrice(BuildLayer("Ice")), 1.5f);
    }

    [Test]
    public void Layer2_WeightedAverageWeight_MatchesDesign()
    {
        Assert.AreEqual(1.0733f, MineralEconomy.WeightedAverageWeight(BuildLayer("Ice")), 0.02f);
    }

    [Test]
    public void Layer2_ItemsPerTrip_IsAbout65()
    {
        Assert.AreEqual(65.2f, MineralEconomy.ItemsPerTrip(Layer2Bag, BuildLayer("Ice")), 1.5f);
    }

    [Test]
    public void Layer2_RevenuePerTrip_MatchesDesign()
    {
        Assert.AreEqual(6198f, MineralEconomy.RevenuePerTrip(Layer2Bag, BuildLayer("Ice")), 150f);
    }

    [Test]
    public void Layer2_PickBias_StaysUnderLimit()
    {
        float bias = MineralEconomy.PickBias(BuildLayer("Ice"));
        Assert.LessOrEqual(bias, MaxPickBias,
            $"2층 선별 편향 {bias:F3} — Rare 광물의 무게를 올려야 한다 (설계 §3.3)");
    }

    [Test]
    public void Layer2_PerChunkTotal_IsRaisedTo150()
    {
        Assert.AreEqual(150f, MineralEconomy.TotalPerChunk(BuildLayer("Ice")), 2f);
    }

    [Test]
    public void Layer2_RareMineralsAreTheTwoMostExpensive()
    {
        // Rare인데 저가인 광물이 있으면 만나서 손해가 된다 (설계 §5.1)
        var tileDb = LoadTileDb();
        TileDataJson tile = tileDb.tiles.Find(t => t != null && t.tileType == "Ice");

        int cheapestRare = int.MaxValue;
        int dearestCommon = 0;
        foreach (var rule in tile.minerals)
        {
            int price = PriceDataTestSource.MineralPrice(rule.mineralType);
            if (rule.IsRare) cheapestRare = Mathf.Min(cheapestRare, price);
            else dearestCommon = Mathf.Max(dearestCommon, price);
        }

        Assert.Greater(cheapestRare, dearestCommon,
            $"2층 최저가 Rare({cheapestRare}) <= 최고가 Common({dearestCommon}) — Rare는 그 층 최상위여야 한다");
    }

    // ─────────────────────────────────────────────────────────────
    // 층 간 관계 — 설계 문서 §3.2 / §3.5
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void EnteringLayer2WithoutBackpack_StillBeatsLayer1FullBag()
    {
        // 배낭 I 없이 2층에 내려가도 손해가 아니어야 한다. 이 배수가 1 아래로 떨어지면 설계가 깨진 것.
        float layer1Full = MineralEconomy.RevenuePerTrip(Layer1Bag, BuildLayer("Dirt"));
        float layer2NoBag = MineralEconomy.RevenuePerTrip(Layer1Bag, BuildLayer("Ice"));

        Assert.GreaterOrEqual(layer2NoBag / layer1Full, 2.0f,
            $"2층 진입 매출 {layer2NoBag:F0} vs 1층 만재 {layer1Full:F0} — 2.0배 이상이어야 한다 (설계 §3.2)");
    }

    [Test]
    public void LayerRevenueRatio_IsAboutFourAndAHalf()
    {
        float layer1 = MineralEconomy.RevenuePerTrip(Layer1Bag, BuildLayer("Dirt"));
        float layer2 = MineralEconomy.RevenuePerTrip(Layer2Bag, BuildLayer("Ice"));
        float ratio = layer2 / layer1;

        Assert.GreaterOrEqual(ratio, 4.0f, $"층 배수 {ratio:F2} — 지수 곡선이 너무 완만하다");
        Assert.LessOrEqual(ratio, 5.0f, $"층 배수 {ratio:F2} — 지수 곡선이 너무 가파르다");
    }
}
