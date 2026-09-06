using NUnit.Framework;
using Relic;

public class RelicStatProviderTests
{
    [Test]
    public void Set_AddsModifierWithRelicSource()
    {
        var p = new RelicStatProvider();
        p.Set("relic:test", StatType.MiningRange, ModifierType.Percent, 1.2f);

        var mods = p.GetModifiers();
        Assert.AreEqual(1, mods.Count);
        Assert.AreEqual(StatType.MiningRange, mods[0].statType);
        Assert.AreEqual(ModifierSource.Relic, mods[0].source);
        Assert.AreEqual(1.2f, mods[0].value, 1e-4f);
    }

    [Test]
    public void Set_SameKey_UpsertsInsteadOfDuplicating()
    {
        var p = new RelicStatProvider();
        p.Set("relic:test", StatType.MiningRange, ModifierType.Percent, 1.1f); // Lv1
        p.Set("relic:test", StatType.MiningRange, ModifierType.Percent, 1.3f); // Lv2 강화

        var mods = p.GetModifiers();
        Assert.AreEqual(1, mods.Count, "레벨업은 중복이 아니라 갱신");
        Assert.AreEqual(1.3f, mods[0].value, 1e-4f);
    }

    [Test]
    public void Clear_RemovesModifier()
    {
        var p = new RelicStatProvider();
        p.Set("relic:test", StatType.MoveSpeed, ModifierType.Flat, 5f);
        p.Clear("relic:test");
        Assert.AreEqual(0, p.GetModifiers().Count);
    }
}
