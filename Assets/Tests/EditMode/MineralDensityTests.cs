using NUnit.Framework;

public class MineralDensityTests
{
    private static MineralRuleJson Rule(float lo, float hi, int minD, int maxD, bool rare)
    {
        return new MineralRuleJson
        {
            mineralType = "Coal",
            minDepth = minD,
            maxDepth = maxD,
            rarity = rare ? "Rare" : "Common",
            perChunk = new[] { lo, hi }
        };
    }

    // ─── DepthFactor ───────────────────────────────────────────────

    [Test]
    public void RareFactor_RisesFromZeroToOne()
    {
        Assert.AreEqual(0.0, MineralDensity.DepthFactor(0, 0, 20, true), 1e-9);
        Assert.AreEqual(0.5, MineralDensity.DepthFactor(10, 0, 20, true), 1e-9);
        Assert.AreEqual(1.0, MineralDensity.DepthFactor(20, 0, 20, true), 1e-9);
    }

    [Test]
    public void CommonFactor_FallsFromOneToHalf()
    {
        Assert.AreEqual(1.00, MineralDensity.DepthFactor(0, 0, 20, false), 1e-9);
        Assert.AreEqual(0.75, MineralDensity.DepthFactor(10, 0, 20, false), 1e-9);
        Assert.AreEqual(0.50, MineralDensity.DepthFactor(20, 0, 20, false), 1e-9);
    }

    [Test]
    public void DegenerateRange_TreatedAsFullDepth()
    {
        // minDepth == maxDepth면 t=1로 고정한다 (0으로 나누지 않는다)
        Assert.AreEqual(1.0, MineralDensity.DepthFactor(5, 5, 5, true), 1e-9);
        Assert.AreEqual(0.5, MineralDensity.DepthFactor(5, 5, 5, false), 1e-9);
    }

    [Test]
    public void DepthFactor_ClampsOutsideRange()
    {
        Assert.AreEqual(0.0, MineralDensity.DepthFactor(-5, 0, 20, true), 1e-9);
        Assert.AreEqual(1.0, MineralDensity.DepthFactor(99, 0, 20, true), 1e-9);
    }

    // ─── ExpectedCount ─────────────────────────────────────────────

    [Test]
    public void LerpRoll_WalksThePerChunkRange()
    {
        // Common, 층 최상단(depthFactor = 1.0), 배율 1.0 → perChunk 값이 그대로 나온다
        var rule = Rule(10f, 20f, 0, 20, rare: false);
        Assert.AreEqual(10.0, MineralDensity.ExpectedCount(rule, 0, 1.0, 0.0), 1e-9);
        Assert.AreEqual(15.0, MineralDensity.ExpectedCount(rule, 0, 1.0, 0.5), 1e-9);
        Assert.AreEqual(20.0, MineralDensity.ExpectedCount(rule, 0, 1.0, 1.0), 1e-9);
    }

    [Test]
    public void DepthFactorAndMultiplier_BothApply()
    {
        var rule = Rule(10f, 10f, 0, 20, rare: false);
        // 층 최하단 Common → depthFactor 0.5, 배율 2.0 → 10 * 0.5 * 2.0 = 10
        Assert.AreEqual(10.0, MineralDensity.ExpectedCount(rule, 20, 2.0, 0.5), 1e-9);
        // 배율만 3배 → 10 * 1.0 * 3.0 = 30
        Assert.AreEqual(30.0, MineralDensity.ExpectedCount(rule, 0, 3.0, 0.5), 1e-9);
    }

    [Test]
    public void OutOfDepthRange_IsZero()
    {
        var rule = Rule(10f, 10f, 20, 39, rare: false);
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(rule, 19, 1.0, 0.5), 1e-9);
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(rule, 40, 1.0, 0.5), 1e-9);
    }

    [Test]
    public void MissingPerChunk_IsZeroNotDefaultOne()
    {
        // 설정을 빠뜨린 광물이 "조금씩 나오는" 상태를 만들면 원인 추적이 어렵다.
        // 누락은 0개로 확실히 드러나야 한다. 경고는 TileDataManager 로드 시점에 뜬다.
        var noArray = Rule(1f, 1f, 0, 20, false);
        noArray.perChunk = null;
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(noArray, 0, 1.0, 0.5), 1e-9);

        var tooShort = Rule(1f, 1f, 0, 20, false);
        tooShort.perChunk = new[] { 5f };
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(tooShort, 0, 1.0, 0.5), 1e-9);

        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(null, 0, 1.0, 0.5), 1e-9);
    }

    [Test]
    public void ZeroMultiplier_TurnsMineralsOff()
    {
        var rule = Rule(10f, 20f, 0, 20, rare: false);
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(rule, 0, 0.0, 0.5), 1e-9);
    }

    [Test]
    public void NegativeValues_ClampToZero()
    {
        var rule = Rule(-5f, -5f, 0, 20, rare: false);
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(rule, 0, 1.0, 0.5), 1e-9);
    }

    // ─── ProbabilisticRound ────────────────────────────────────────

    [Test]
    public void WholeNumber_RoundsExactly()
    {
        Assert.AreEqual(3, MineralDensity.ProbabilisticRound(3.0, 0.0));
        Assert.AreEqual(3, MineralDensity.ProbabilisticRound(3.0, 0.999));
    }

    [Test]
    public void Fraction_SplitsOnTheRoll()
    {
        // n = 3.4 → roll < 0.4면 4개, 아니면 3개
        Assert.AreEqual(4, MineralDensity.ProbabilisticRound(3.4, 0.0));
        Assert.AreEqual(4, MineralDensity.ProbabilisticRound(3.4, 0.399));
        Assert.AreEqual(3, MineralDensity.ProbabilisticRound(3.4, 0.4));
        Assert.AreEqual(3, MineralDensity.ProbabilisticRound(3.4, 0.999));
    }

    [Test]
    public void SubOneValue_IsRepresentable()
    {
        // [0, 1] 같은 희소 광물: 기대 0.48개 → 48% 확률로 1개
        Assert.AreEqual(1, MineralDensity.ProbabilisticRound(0.48, 0.47));
        Assert.AreEqual(0, MineralDensity.ProbabilisticRound(0.48, 0.49));
    }

    [Test]
    public void NonPositive_IsZero()
    {
        Assert.AreEqual(0, MineralDensity.ProbabilisticRound(0.0, 0.0));
        Assert.AreEqual(0, MineralDensity.ProbabilisticRound(-1.5, 0.0));
    }

    [Test]
    public void ExpectedValue_ConvergesToN()
    {
        // 확률 반올림의 기대값은 정확히 n이어야 한다
        var prng = new System.Random(12345);
        const double N = 3.4;
        const int ITERATIONS = 100000;

        long total = 0;
        for (int i = 0; i < ITERATIONS; i++)
            total += MineralDensity.ProbabilisticRound(N, prng.NextDouble());

        double mean = (double)total / ITERATIONS;
        Assert.AreEqual(N, mean, 0.02, $"평균 {mean}");
    }
}
