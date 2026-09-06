using NUnit.Framework;

public class StaminaBarLayoutTests
{
    // 회귀: MaxStamina가 이미 감소된 값인데 바가 감소분을 한 번 더 뺐다.
    // 그 결과 current가 (감소분만큼) 과도하게 클램프돼, 벽타기로 그만큼 쓸 때까지 바가 안 줄었다.
    [Test]
    public void ReductionIsNotSubtractedTwice()
    {
        // 기준 100, 채굴 감소 30 → MaxStamina 70, 현재 70(클램프된 직후)
        var s = StaminaBarLayout.Compute(70f, 70f, 1f, 0f, 0f, 0f, 0f, 30f);

        Assert.AreEqual(100f, s.total, 0.001f);
        Assert.AreEqual(70f, s.current, 0.001f);   // 40f로 잘리면 버그
        Assert.AreEqual(0f, s.recoverable, 0.001f);
        Assert.AreEqual(30f, s.digging, 0.001f);
    }

    [Test]
    public void ClimbingConsumptionShrinksFillImmediately()
    {
        var before = StaminaBarLayout.Compute(70f, 70f, 1f, 0f, 0f, 0f, 0f, 30f);
        var after  = StaminaBarLayout.Compute(70f, 69f, 1f, 0f, 0f, 0f, 0f, 30f);

        Assert.Less(after.current / after.total, before.current / before.total);
        Assert.AreEqual(1f, after.recoverable, 0.001f);
    }

    [Test]
    public void SegmentsAlwaysSumToTotal()
    {
        var s = StaminaBarLayout.Compute(55f, 30f, 1f, 10f, 5f, 20f, 3f, 7f);
        float sum = s.current + s.recoverable + s.injury + s.burn + s.frostbite + s.radiation + s.digging;
        Assert.AreEqual(s.total, sum, 0.001f);
    }

    // 곱연산 업그레이드(예: 최대 스태미나 +10%)가 걸리면 Flat 감소도 그 배율을 먹는다.
    [Test]
    public void PercentMultiplierScalesLostSegment()
    {
        // 기준 100 + 감소 -30 → (100-30) × 1.1 = 77
        var s = StaminaBarLayout.Compute(77f, 77f, 1.1f, 0f, 0f, 0f, 0f, 30f);

        Assert.AreEqual(110f, s.total, 0.001f);   // 감소가 없었다면 100 × 1.1
        Assert.AreEqual(33f, s.digging, 0.001f);
    }

    [Test]
    public void OverReductionClampsToZeroWithoutNegativeSegments()
    {
        // 감소가 기준값을 넘겨 MaxStamina가 음수가 된 상황
        var s = StaminaBarLayout.Compute(-20f, 10f, 1f, 120f, 0f, 0f, 0f, 0f);

        Assert.AreEqual(0f, s.current, 0.001f);
        Assert.AreEqual(0f, s.recoverable, 0.001f);
        Assert.AreEqual(120f, s.total, 0.001f);
    }
}
