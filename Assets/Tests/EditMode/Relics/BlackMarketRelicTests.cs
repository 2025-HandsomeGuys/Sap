using NUnit.Framework;
using Relic;

public class BlackMarketRelicTests
{
    [Test]
    public void ComputeGold_Lv1_Floors80Percent()
    {
        // raw 1000 * 0.8 = 800
        Assert.AreEqual(800, BlackMarketRelic.ComputeGold(1000, 0.8f));
    }

    [Test]
    public void ComputeGold_FullRate_Unchanged()
    {
        Assert.AreEqual(1000, BlackMarketRelic.ComputeGold(1000, 1.0f));
    }

    [Test]
    public void ComputeGold_FractionFloored()
    {
        // 333 * 0.8 = 266.4 → 266
        Assert.AreEqual(266, BlackMarketRelic.ComputeGold(333, 0.8f));
    }

    [Test]
    public void ComputeGold_Zero_ReturnsZero()
    {
        Assert.AreEqual(0, BlackMarketRelic.ComputeGold(0, 0.8f));
    }
}
