using NUnit.Framework;
using Relic;

public class ActiveRelicStateTests
{
    [Test]
    public void Instant_GoesStraightToCooldown()
    {
        var s = new ActiveRelicState();
        Assert.IsTrue(s.TryActivate(duration: 0f, cooldown: 2f));
        Assert.AreEqual(RelicPhase.Cooldown, s.Phase);
    }

    [Test]
    public void Cooldown_BlocksReactivation()
    {
        var s = new ActiveRelicState();
        s.TryActivate(0f, 2f);
        Assert.IsFalse(s.TryActivate(0f, 2f), "쿨타임 중 재발동 차단");
    }

    [Test]
    public void Cooldown_ExpiresToReady()
    {
        var s = new ActiveRelicState();
        s.TryActivate(0f, 1f);
        var r = s.Tick(1.0f);
        Assert.IsTrue(r.becameReady);
        Assert.AreEqual(RelicPhase.Ready, s.Phase);
        Assert.IsTrue(s.TryActivate(0f, 1f), "쿨타임 후 재발동 가능");
    }

    [Test]
    public void Duration_TicksThenEndsThenCooldown()
    {
        var s = new ActiveRelicState();
        s.TryActivate(duration: 1f, cooldown: 2f);
        Assert.AreEqual(RelicPhase.Active, s.Phase);

        var mid = s.Tick(0.5f);
        Assert.IsTrue(mid.activeTick);
        Assert.AreEqual(0.5f, mid.activeElapsed, 1e-4f);

        var end = s.Tick(0.5f);
        Assert.IsTrue(end.activeEnded);
        Assert.AreEqual(RelicPhase.Cooldown, s.Phase);
    }
}
