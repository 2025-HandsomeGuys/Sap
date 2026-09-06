// @tags: test, editmode, stat, defense, resist, hazard, damage

using NUnit.Framework;

/// <summary>
/// HazardMitigation — 방어력·속성 저항의 소프트캡 감산 검증.
/// 이 곡선이 곧 밸런스 명세다: K 값을 바꾸려면 여기 기대값부터 고친다.
/// </summary>
public class HazardMitigationTests
{
    [Test]
    public void NoStat_LeavesAmountUntouched()
    {
        Assert.AreEqual(10f, HazardMitigation.Apply(10f, 0f, HazardMitigation.DefenseK), 0.0001f);
    }

    [Test]
    public void NegativeStat_LeavesAmountUntouched()
    {
        Assert.AreEqual(10f, HazardMitigation.Apply(10f, -5f, HazardMitigation.DefenseK), 0.0001f);
    }

    [Test]
    public void StatEqualToK_HalvesAmount()
    {
        Assert.AreEqual(5f, HazardMitigation.Apply(10f, HazardMitigation.DefenseK, HazardMitigation.DefenseK), 0.0001f);
        Assert.AreEqual(5f, HazardMitigation.Apply(10f, HazardMitigation.ResistK, HazardMitigation.ResistK), 0.0001f);
    }

    /// <summary>방한/방열 포션 값이 20이고 ResistK도 20이라, 포션 하나 = 해당 속성 피해 절반.</summary>
    [Test]
    public void ResistPotionValue_HalvesElementalDamage()
    {
        const float PotionResist = 20f; // FrostbiteResist.asset / BurnResist.asset
        Assert.AreEqual(0.5f, HazardMitigation.Apply(1f, PotionResist, HazardMitigation.ResistK), 0.0001f);
    }

    /// <summary>폴백 강화 테이블은 레벨당 방어력 +1 — 3부위를 +4까지 올리면 12.</summary>
    [Test]
    public void FullyUpgradedTestSet_ReducesInjuryByAboutFortyFivePercent()
    {
        float defense = 3f * (EquipmentUpgradeFormula.TestMaxLevel - 1) * EquipmentUpgradeFormula.TestDefensePerLevel;
        Assert.AreEqual(12f, defense, 0.0001f);

        float taken = HazardMitigation.Apply(100f, defense, HazardMitigation.DefenseK);
        Assert.AreEqual(45.45f, taken, 0.01f);
    }

    /// <summary>소프트캡이라 방어력을 아무리 쌓아도 피해가 0이 되지 않는다(지속 피해 무력화 방지).</summary>
    [Test]
    public void HugeStat_NeverReachesZero()
    {
        float taken = HazardMitigation.Apply(10f, 100000f, HazardMitigation.DefenseK);
        Assert.Greater(taken, 0f);
        Assert.Less(taken, 0.01f);
    }

    /// <summary>회복(음수)에는 감산을 걸지 않는다 — 방어력이 높다고 덜 회복되면 안 된다.</summary>
    [Test]
    public void NegativeAmount_IsNotMitigated()
    {
        Assert.AreEqual(-10f, HazardMitigation.Apply(-10f, 50f, HazardMitigation.DefenseK), 0.0001f);
    }
}
