// @tags: rock, fragment, tier, test, editmode
using NUnit.Framework;
using UnityEngine;

public class RockFragmentPoolTests
{
    // --- DefaultRule ---

    [Test]
    public void DefaultRule_Large_Has8CountAndMixedWeights()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large);
        Assert.AreEqual(8, rule.totalCount);
        Assert.AreEqual(0.6f, rule.weightLarge,  0.001f);
        Assert.AreEqual(0.3f, rule.weightMedium, 0.001f);
        Assert.AreEqual(0.1f, rule.weightSmall,  0.001f);
    }

    [Test]
    public void DefaultRule_Small_OnlyDrawsSmall()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Small);
        Assert.AreEqual(4, rule.totalCount);
        Assert.AreEqual(0f,  rule.weightLarge,  0.001f);
        Assert.AreEqual(0f,  rule.weightMedium, 0.001f);
        Assert.AreEqual(1f,  rule.weightSmall,  0.001f);
    }

    // --- ResolveCount ---

    [Test]
    public void ResolveCount_LargeRock_ScalesWithSize()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large); // totalCount 8, 0.7~1.3
        Assert.AreEqual(6,  RockFragmentPool.ResolveCount(rule, 0.5f));
        Assert.AreEqual(8,  RockFragmentPool.ResolveCount(rule, 1.0f));
        Assert.AreEqual(10, RockFragmentPool.ResolveCount(rule, 1.5f));
    }

    [Test]
    public void ResolveCount_SmallRock_ScalesWithSize()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Small); // totalCount 4
        Assert.AreEqual(3, RockFragmentPool.ResolveCount(rule, 0.5f));
        Assert.AreEqual(4, RockFragmentPool.ResolveCount(rule, 1.0f));
        Assert.AreEqual(5, RockFragmentPool.ResolveCount(rule, 1.5f));
    }

    [Test]
    public void ResolveCount_ClampsScaleOutsideExpectedRange()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large);
        Assert.AreEqual(6,  RockFragmentPool.ResolveCount(rule, 0.1f), "0.5 미만은 0.5로 클램프");
        Assert.AreEqual(10, RockFragmentPool.ResolveCount(rule, 9f),   "1.5 초과는 1.5로 클램프");
    }

    [Test]
    public void ResolveCount_NeverReturnsZero()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Small);
        rule.totalCount = 0;
        Assert.AreEqual(1, RockFragmentPool.ResolveCount(rule, 0.5f));
    }

    // --- PickTier ---

    [Test]
    public void PickTier_LargeRule_MapsRollToTierBands()
    {
        // Large 규칙 가중치: L 0.6 / M 0.3 / S 0.1 → 누적 경계 0.6, 0.9, 1.0
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large);
        Assert.AreEqual(RockSizeTier.Large,  RockFragmentPool.PickTier(rule, 0.0f));
        Assert.AreEqual(RockSizeTier.Large,  RockFragmentPool.PickTier(rule, 0.59f));
        Assert.AreEqual(RockSizeTier.Medium, RockFragmentPool.PickTier(rule, 0.61f));
        Assert.AreEqual(RockSizeTier.Medium, RockFragmentPool.PickTier(rule, 0.89f));
        Assert.AreEqual(RockSizeTier.Small,  RockFragmentPool.PickTier(rule, 0.95f));
    }

    [Test]
    public void PickTier_SmallRule_AlwaysSmall()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Small);
        Assert.AreEqual(RockSizeTier.Small, RockFragmentPool.PickTier(rule, 0.0f));
        Assert.AreEqual(RockSizeTier.Small, RockFragmentPool.PickTier(rule, 0.5f));
        Assert.AreEqual(RockSizeTier.Small, RockFragmentPool.PickTier(rule, 0.999f));
    }

    [Test]
    public void PickTier_NonNormalizedWeights_AreNormalized()
    {
        // 가중치 합이 10 → 6/3/1 비율. 경계는 0.6, 0.9
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large);
        rule.weightLarge  = 6f;
        rule.weightMedium = 3f;
        rule.weightSmall  = 1f;
        Assert.AreEqual(RockSizeTier.Large,  RockFragmentPool.PickTier(rule, 0.5f));
        Assert.AreEqual(RockSizeTier.Medium, RockFragmentPool.PickTier(rule, 0.7f));
        Assert.AreEqual(RockSizeTier.Small,  RockFragmentPool.PickTier(rule, 0.95f));
    }

    [Test]
    public void PickTier_AllWeightsZero_FallsBackToBrokenTier()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Medium);
        rule.weightLarge = rule.weightMedium = rule.weightSmall = 0f;
        Assert.AreEqual(RockSizeTier.Medium, RockFragmentPool.PickTier(rule, 0.5f));
    }
}
