using NUnit.Framework;
using System.Collections.Generic;

/// <summary>
/// MineralRockSection.Resolve() — 광물별 오버라이드 적중 / 미적중(default 폴백) 검증.
/// 순수 로직만 테스트 (MonoBehaviour 없음).
/// </summary>
public class MineralRockSettingsTests
{
    private SpecialChunkSettingsData.MineralRockSection MakeSection()
    {
        return new SpecialChunkSettingsData.MineralRockSection
        {
            defaultMaxHp   = 8f,
            defaultMinDrop = 2,
            defaultMaxDrop = 4,
            overrides = new List<SpecialChunkSettingsData.MineralRockEntry>
            {
                new SpecialChunkSettingsData.MineralRockEntry
                    { mineralID = "Copper", maxHp = 6f, minDrop = 2, maxDrop = 4 },
                new SpecialChunkSettingsData.MineralRockEntry
                    { mineralID = "Iron",   maxHp = 10f, minDrop = 1, maxDrop = 3 },
            }
        };
    }

    [Test]
    public void Resolve_KnownMineral_ReturnsOverride()
    {
        var r = MakeSection().Resolve("Iron");
        Assert.AreEqual(10f, r.maxHp);
        Assert.AreEqual(1, r.minDrop);
        Assert.AreEqual(3, r.maxDrop);
    }

    [Test]
    public void Resolve_UnknownMineral_ReturnsDefaults()
    {
        var r = MakeSection().Resolve("Gold");
        Assert.AreEqual(8f, r.maxHp);
        Assert.AreEqual(2, r.minDrop);
        Assert.AreEqual(4, r.maxDrop);
    }

    [Test]
    public void Resolve_EmptyId_ReturnsDefaults()
    {
        var r = MakeSection().Resolve("");
        Assert.AreEqual(8f, r.maxHp);
    }

    [Test]
    public void Resolve_CachesDictionary_SecondCallSameResult()
    {
        var section = MakeSection();
        var first  = section.Resolve("Copper");
        var second = section.Resolve("Copper");
        Assert.AreEqual(first.maxHp, second.maxHp);
        Assert.AreEqual(6f, second.maxHp);
    }
}
