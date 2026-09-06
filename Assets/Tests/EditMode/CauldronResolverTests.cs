using NUnit.Framework;
using System.Collections.Generic;

public class CauldronResolverTests
{
    private static MineralRuleJson R(string t, string r) => new MineralRuleJson { mineralType = t, rarity = r };
    private static TileDataJson Tile(string n, params MineralRuleJson[] m)
        => new TileDataJson { tileType = n, minerals = new List<MineralRuleJson>(m) };

    private MineralUpgradeLadder MakeLadder() => new MineralUpgradeLadder(new List<TileDataJson>
    {
        Tile("Dirt", R("Coal","Common"), R("Copper","Rare"), R("Iron","Rare")),
        Tile("Ice",  R("Silver","Common"), R("Emerald","Rare")),
        Tile("Magma",R("Gold","Common"), R("Diamond","Rare")),
        Tile("Meteor",R("Mithril","Common"), R("StarFragment","Rare")),
    });

    private CauldronResolver MakeResolver()
    {
        var probs = new CauldronProbabilities { greatSuccess = 0.15, success = 0.45, fail = 0.25, greatFail = 0.15 };
        return new CauldronResolver(MakeLadder(), probs, explosiveMin: 3, explosiveMax: 6);
    }

    // 결정론적 결과를 위해 NextDouble을 제어할 수 없으므로, 경계 분포를 통계로 검증.
    [Test]
    public void Resolve_Distribution_MatchesProbabilities()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(12345);
        var counts = new Dictionary<CauldronOutcome, int>
        {
            { CauldronOutcome.GreatSuccess, 0 }, { CauldronOutcome.Success, 0 },
            { CauldronOutcome.Fail, 0 }, { CauldronOutcome.GreatFail, 0 },
        };
        const int N = 100000;
        for (int i = 0; i < N; i++)
            counts[resolver.Resolve(MineralID.Coal, rng).outcome]++;

        Assert.That(counts[CauldronOutcome.GreatSuccess] / (double)N, Is.EqualTo(0.15).Within(0.02));
        Assert.That(counts[CauldronOutcome.Success]      / (double)N, Is.EqualTo(0.45).Within(0.02));
        Assert.That(counts[CauldronOutcome.Fail]         / (double)N, Is.EqualTo(0.25).Within(0.02));
        Assert.That(counts[CauldronOutcome.GreatFail]    / (double)N, Is.EqualTo(0.15).Within(0.02));
    }

    [Test]
    public void Resolve_Success_GivesMineralReward_OneRungUp()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(1);
        // Coal(rung0) 성공 결과는 항상 rung1(Copper/Iron) 또는 rung2(대성공). 광물 보상이어야.
        for (int i = 0; i < 1000; i++)
        {
            var res = resolver.Resolve(MineralID.Coal, rng);
            if (res.outcome == CauldronOutcome.Success)
            {
                Assert.AreEqual(CauldronRewardType.Mineral, res.rewardType);
                Assert.AreNotEqual(MineralID.None, res.resultMineral);
            }
        }
    }

    [Test]
    public void Resolve_Fail_GivesAsh()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(2);
        for (int i = 0; i < 1000; i++)
        {
            var res = resolver.Resolve(MineralID.Coal, rng);
            if (res.outcome == CauldronOutcome.Fail)
                Assert.AreEqual(CauldronRewardType.Ash, res.rewardType);
        }
    }

    [Test]
    public void Resolve_GreatFail_GivesExplosionWithinRange()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(3);
        for (int i = 0; i < 1000; i++)
        {
            var res = resolver.Resolve(MineralID.Coal, rng);
            if (res.outcome == CauldronOutcome.GreatFail)
            {
                Assert.AreEqual(CauldronRewardType.Explosion, res.rewardType);
                Assert.That(res.explosiveCount, Is.InRange(3, 6));
            }
        }
    }

    [Test]
    public void Resolve_TopMineral_Success_FlagsEasterEgg()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(4);
        for (int i = 0; i < 2000; i++)
        {
            var res = resolver.Resolve(MineralID.StarFragment, rng);
            if (res.outcome == CauldronOutcome.Success || res.outcome == CauldronOutcome.GreatSuccess)
                Assert.AreEqual(CauldronRewardType.EasterEgg, res.rewardType);
        }
    }
}
