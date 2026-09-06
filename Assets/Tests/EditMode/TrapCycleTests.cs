using NUnit.Framework;
using Gameplay.Dungeon.Traps;

public class TrapCycleTests
{
    [Test]
    public void TrapCycle_ActiveInFirstFraction()
    {
        var c = new TrapCycle { period = 2f, activeFraction = 0.5f };
        Assert.IsTrue(c.IsActive(0f));     // 0.0 구간
        Assert.IsTrue(c.IsActive(0.9f));   // 여전히 활성 구간(<1.0)
        Assert.IsFalse(c.IsActive(1.5f));  // 비활성 구간
        Assert.IsTrue(c.IsActive(2.0f));   // 다음 주기 시작 → 활성
    }

    [Test]
    public void TrapCycle_NonPositivePeriod_AlwaysActive()
    {
        var zero = new TrapCycle { period = 0f, activeFraction = 0.5f };
        Assert.IsTrue(zero.IsActive(0f));
        Assert.IsTrue(zero.IsActive(3.7f));

        var neg = new TrapCycle { period = -1f, activeFraction = 0.5f };
        Assert.IsTrue(neg.IsActive(3.7f));
    }
}
