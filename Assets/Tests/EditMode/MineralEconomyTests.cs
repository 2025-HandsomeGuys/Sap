// @tags: test, editmode, mineral, economy, balance, pure-logic
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// MineralEconomy 계산식의 단위 테스트. 합성 데이터만 쓰고 에셋을 읽지 않는다.
/// 실제 데이터와의 대조는 MineralBalanceTests가 담당한다.
/// </summary>
public class MineralEconomyTests
{
    private static List<MineralEconomyEntry> Sample()
    {
        // perChunk 비중 1:3 → 가중평균은 비싼 쪽으로 끌린다
        return new List<MineralEconomyEntry>
        {
            new MineralEconomyEntry(MineralID.ScrapMetal, 10f, 0.5f, 1f),
            new MineralEconomyEntry(MineralID.Iron,       20f, 1.5f, 3f),
        };
    }

    [Test]
    public void TotalPerChunk_SumsAllEntries()
    {
        Assert.AreEqual(4f, MineralEconomy.TotalPerChunk(Sample()), 0.0001f);
    }

    [Test]
    public void WeightedAveragePrice_WeightsByPerChunk()
    {
        // (10*1 + 20*3) / 4 = 17.5
        Assert.AreEqual(17.5f, MineralEconomy.WeightedAveragePrice(Sample()), 0.0001f);
    }

    [Test]
    public void WeightedAverageWeight_WeightsByPerChunk()
    {
        // (0.5*1 + 1.5*3) / 4 = 1.25
        Assert.AreEqual(1.25f, MineralEconomy.WeightedAverageWeight(Sample()), 0.0001f);
    }

    [Test]
    public void ItemsPerTrip_IsBagDividedByAverageWeight()
    {
        // 40 / 1.25 = 32
        Assert.AreEqual(32f, MineralEconomy.ItemsPerTrip(40f, Sample()), 0.0001f);
    }

    [Test]
    public void RevenuePerTrip_IsItemsTimesAveragePrice()
    {
        // 32 * 17.5 = 560
        Assert.AreEqual(560f, MineralEconomy.RevenuePerTrip(40f, Sample()), 0.001f);
    }

    [Test]
    public void AverageValuePerWeight_IsAvgPriceOverAvgWeight()
    {
        // 17.5 / 1.25 = 14
        Assert.AreEqual(14f, MineralEconomy.AverageValuePerWeight(Sample()), 0.0001f);
    }

    [Test]
    public void MaxValuePerWeight_PicksTheBestEntry()
    {
        // 고철 10/0.5 = 20, 철 20/1.5 = 13.33 → 20
        Assert.AreEqual(20f, MineralEconomy.MaxValuePerWeight(Sample()), 0.0001f);
    }

    [Test]
    public void PickBias_IsMaxOverAverage()
    {
        // 20 / 14 = 1.42857
        Assert.AreEqual(20f / 14f, MineralEconomy.PickBias(Sample()), 0.0001f);
    }

    [Test]
    public void EmptyList_ReturnsZero_AndDoesNotDivideByZero()
    {
        var empty = new List<MineralEconomyEntry>();
        Assert.AreEqual(0f, MineralEconomy.TotalPerChunk(empty));
        Assert.AreEqual(0f, MineralEconomy.WeightedAveragePrice(empty));
        Assert.AreEqual(0f, MineralEconomy.WeightedAverageWeight(empty));
        Assert.AreEqual(0f, MineralEconomy.AverageValuePerWeight(empty));
        Assert.AreEqual(0f, MineralEconomy.MaxValuePerWeight(empty));
        Assert.AreEqual(0f, MineralEconomy.PickBias(empty));
        Assert.AreEqual(0f, MineralEconomy.ItemsPerTrip(40f, empty));
        Assert.AreEqual(0f, MineralEconomy.RevenuePerTrip(40f, empty));
    }

    [Test]
    public void ZeroWeightEntry_IsIgnoredByValuePerWeight()
    {
        var list = new List<MineralEconomyEntry>
        {
            new MineralEconomyEntry(MineralID.Coal, 100f, 0f, 1f),
            new MineralEconomyEntry(MineralID.Iron,  20f, 2f, 1f),
        };
        // 무게 0짜리는 ValuePerWeight 0으로 처리되어 최대값에 끼지 않는다
        Assert.AreEqual(10f, MineralEconomy.MaxValuePerWeight(list), 0.0001f);
    }

    [Test]
    public void NullList_ReturnsZero()
    {
        Assert.AreEqual(0f, MineralEconomy.WeightedAveragePrice(null));
        Assert.AreEqual(0f, MineralEconomy.RevenuePerTrip(40f, null));
    }
}
