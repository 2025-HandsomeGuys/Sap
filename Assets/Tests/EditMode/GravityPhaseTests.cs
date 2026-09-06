using NUnit.Framework;

/// <summary>GravityPhases 순수 로직 — 순환 순서 + 위상별 gravityScale.</summary>
public class GravityPhaseTests
{
    [Test]
    public void Cycle_WeakensThenFlipsDirection()
    {
        // Normal(강한 아래) → LowNormal(약한 아래) → Inverted(강한 위) → LowInverted(약한 위).
        Assert.AreEqual(GravityPhase.Normal,      GravityPhases.Cycle[0]);
        Assert.AreEqual(GravityPhase.LowNormal,   GravityPhases.Cycle[1]);
        Assert.AreEqual(GravityPhase.Inverted,    GravityPhases.Cycle[2]);
        Assert.AreEqual(GravityPhase.LowInverted, GravityPhases.Cycle[3]);
    }

    [Test]
    public void NextIndex_WrapsAtEnd()
    {
        Assert.AreEqual(1, GravityPhases.NextIndex(0));
        Assert.AreEqual(2, GravityPhases.NextIndex(1));
        Assert.AreEqual(3, GravityPhases.NextIndex(2));
        Assert.AreEqual(0, GravityPhases.NextIndex(3)); // 끝 → 0으로 순환
    }

    [Test]
    public void IsInvertedDirection_TrueOnlyForUpwardPhases()
    {
        Assert.IsFalse(GravityPhases.IsInvertedDirection(GravityPhase.Normal));
        Assert.IsFalse(GravityPhases.IsInvertedDirection(GravityPhase.LowNormal));
        Assert.IsTrue(GravityPhases.IsInvertedDirection(GravityPhase.Inverted));
        Assert.IsTrue(GravityPhases.IsInvertedDirection(GravityPhase.LowInverted));
    }

    [Test]
    public void ScaleFor_Normal_ReturnsDefault()
    {
        Assert.AreEqual(3f, GravityPhases.ScaleFor(GravityPhase.Normal, 3f, 0.7f, 0.3f));
    }

    [Test]
    public void ScaleFor_LowNormal_ReturnsWeakDownward()
    {
        // 약한 아래 중력 = default × lowFactor.
        Assert.AreEqual(0.9f, GravityPhases.ScaleFor(GravityPhase.LowNormal, 3f, 0.7f, 0.3f), 1e-5f);
    }

    [Test]
    public void ScaleFor_Inverted_ReturnsNegativeScaledByMultiplier()
    {
        Assert.AreEqual(-1.4f, GravityPhases.ScaleFor(GravityPhase.Inverted, 2f, 0.7f, 0.3f), 1e-5f);
    }

    [Test]
    public void ScaleFor_LowInverted_ReturnsWeakUpward()
    {
        // 약한 위 중력 = -|default| × antiMult × lowFactor.
        Assert.AreEqual(-0.42f, GravityPhases.ScaleFor(GravityPhase.LowInverted, 2f, 0.7f, 0.3f), 1e-5f);
    }

    [Test]
    public void ScaleFor_Inverted_UsesAbsoluteOfDefault()
    {
        // 음수 기본값도 |값|×mult 후 부호 반전.
        Assert.AreEqual(-1.4f, GravityPhases.ScaleFor(GravityPhase.Inverted, -2f, 0.7f, 0.3f), 1e-5f);
    }
}
