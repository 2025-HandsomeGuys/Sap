using NUnit.Framework;
using Relic;

public class HourglassRelicTests
{
    private static HourglassRelic AtLevel(int lv)
    {
        var r = new HourglassRelic();
        r.OnEquip(null, lv); // ctx 미사용 유물 — null 안전
        return r;
    }

    [Test]
    public void Scale_MatchesLevelReduction()
    {
        Assert.AreEqual(0.70f, AtLevel(1).GetCooldownScale(), 1e-5f);
        Assert.AreEqual(0.60f, AtLevel(2).GetCooldownScale(), 1e-5f);
        Assert.AreEqual(0.50f, AtLevel(3).GetCooldownScale(), 1e-5f);
    }

    [Test]
    public void Scale_ClampsOutOfRangeLevel()
    {
        Assert.AreEqual(0.70f, AtLevel(0).GetCooldownScale(), 1e-5f, "레벨 하한 클램프");
        Assert.AreEqual(0.50f, AtLevel(99).GetCooldownScale(), 1e-5f, "레벨 상한 클램프");
    }

    [Test]
    public void DefaultBehaviour_HasNeutralScale()
    {
        // 훅 기본값 검증 — 다른 유물은 쿨타임에 영향 없어야 함.
        var plain = new DoubleJumpRelic();
        Assert.AreEqual(1f, plain.GetCooldownScale(), 1e-5f);
    }
}
