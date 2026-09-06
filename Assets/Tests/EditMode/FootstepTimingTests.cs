using NUnit.Framework;

public class FootstepTimingTests
{
    [Test]
    public void FasterSpeed_GivesShorterInterval()
    {
        float slow = FootstepTiming.IntervalFor(2f);
        float fast = FootstepTiming.IntervalFor(6f);
        Assert.Less(fast, slow);
    }

    [Test]
    public void VeryFastSpeed_ClampsToMin()
    {
        Assert.AreEqual(FootstepTiming.MinInterval, FootstepTiming.IntervalFor(999f), 0.0001f);
    }

    [Test]
    public void VerySlowSpeed_ClampsToMax()
    {
        Assert.AreEqual(FootstepTiming.MaxInterval, FootstepTiming.IntervalFor(0.01f), 0.0001f);
    }

    [Test]
    public void ZeroSpeed_DoesNotDivideByZero()
    {
        float v = FootstepTiming.IntervalFor(0f);
        Assert.IsFalse(float.IsNaN(v));
        Assert.IsFalse(float.IsInfinity(v));
        Assert.AreEqual(FootstepTiming.MaxInterval, v, 0.0001f);
    }

    [Test]
    public void NegativeSpeed_UsesMagnitude()
    {
        // 왼쪽으로 걸어도 오른쪽과 같은 간격이어야 한다
        Assert.AreEqual(FootstepTiming.IntervalFor(4f), FootstepTiming.IntervalFor(-4f), 0.0001f);
    }

    [Test]
    public void ResultAlwaysWithinBounds()
    {
        for (float s = 0f; s < 30f; s += 0.37f)
        {
            float v = FootstepTiming.IntervalFor(s);
            Assert.GreaterOrEqual(v, FootstepTiming.MinInterval);
            Assert.LessOrEqual(v, FootstepTiming.MaxInterval);
        }
    }
}
