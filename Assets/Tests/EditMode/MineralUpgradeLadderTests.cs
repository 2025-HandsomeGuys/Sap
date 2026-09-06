using NUnit.Framework;
using System.Collections.Generic;

/// <summary>
/// MineralUpgradeLadder — tileData 파생 8칸 사다리 + 승급 해석.
/// 순수 로직만 테스트 (MonoBehaviour 없음). 실제 tileData.json 4개 층 구조를 축약 재현.
/// </summary>
public class MineralUpgradeLadderTests
{
    // 실제 tileData.json 구조를 그대로 재현 (bleed-over 포함).
    private static MineralRuleJson R(string type, string rarity)
        => new MineralRuleJson { mineralType = type, rarity = rarity };

    private static TileDataJson Tile(string name, params MineralRuleJson[] minerals)
        => new TileDataJson { tileType = name, minerals = new List<MineralRuleJson>(minerals) };

    private MineralUpgradeLadder MakeLadder()
    {
        var tiles = new List<TileDataJson>
        {
            Tile("Dirt",
                R("ScrapMetal","Common"), R("GarbageBag","Common"), R("PETBottle","Common"), R("Coal","Common"),
                R("Copper","Rare"), R("Iron","Rare")),
            Tile("Ice",
                R("Meteorite","Common"), R("Fossil","Common"), R("Silver","Common"), R("Sapphire","Common"),
                R("Emerald","Rare"), R("Topaz","Rare"),
                R("Copper","Common"), R("Iron","Common")), // bleed-over
            Tile("MagmaRock",
                R("Obsidian","Common"), R("Quartz","Common"), R("Gold","Common"), R("Ruby","Common"),
                R("Diamond","Rare"), R("LavaStone","Rare"),
                R("Emerald","Common"), R("Topaz","Common")), // bleed-over
            Tile("MeteoriteRock",
                R("Mithril","Common"), R("Gravitonium","Common"), R("Uranium","Common"),
                R("VoidStone","Rare"), R("StarFragment","Rare"),
                R("Diamond","Common"), R("LavaStone","Common")), // bleed-over
        };
        return new MineralUpgradeLadder(tiles);
    }

    [Test]
    public void RungCount_FourLayers_IsEight()
    {
        Assert.AreEqual(8, MakeLadder().RungCount);
    }

    [Test]
    public void RungOf_CommonMineral_ReturnsLayerCommonRung()
    {
        Assert.AreEqual(0, MakeLadder().RungOf(MineralID.Coal));      // Dirt-Common
        Assert.AreEqual(2, MakeLadder().RungOf(MineralID.Meteorite)); // Ice-Common
    }

    [Test]
    public void RungOf_RareMineral_ReturnsLayerRareRung()
    {
        Assert.AreEqual(1, MakeLadder().RungOf(MineralID.Copper));  // Dirt-Rare
        Assert.AreEqual(3, MakeLadder().RungOf(MineralID.Emerald)); // Ice-Rare
    }

    [Test]
    public void RungOf_BleedOverMineral_UsesShallowestFirstAppearance()
    {
        // Copper는 Ice에서 Common으로 재등장하지만 Dirt-Rare(rung1)가 우선.
        Assert.AreEqual(1, MakeLadder().RungOf(MineralID.Copper));
        Assert.AreEqual(3, MakeLadder().RungOf(MineralID.Emerald));
        Assert.AreEqual(5, MakeLadder().RungOf(MineralID.Diamond));
    }

    [Test]
    public void Resolve_Success_OneStepUp_ResultInNextRung()
    {
        var ladder = MakeLadder();
        var result = ladder.Resolve(MineralID.Coal, 1, new System.Random(0), out bool clamped);
        Assert.IsFalse(clamped);
        CollectionAssert.Contains((System.Collections.ICollection)ladder.Rung(1), result); // {Copper, Iron}
    }

    [Test]
    public void Resolve_GreatSuccess_TwoStepsUp_ResultInRungPlusTwo()
    {
        var ladder = MakeLadder();
        var result = ladder.Resolve(MineralID.Coal, 2, new System.Random(0), out bool clamped);
        Assert.IsFalse(clamped);
        CollectionAssert.Contains((System.Collections.ICollection)ladder.Rung(2), result); // Ice-Common
    }

    [Test]
    public void Resolve_TopRung_Clamps()
    {
        var ladder = MakeLadder();
        var result = ladder.Resolve(MineralID.StarFragment, 1, new System.Random(0), out bool clamped);
        Assert.IsTrue(clamped);
        CollectionAssert.Contains((System.Collections.ICollection)ladder.Rung(7), result);
    }

    [Test]
    public void Resolve_UnknownMineral_ReturnsNone()
    {
        var result = MakeLadder().Resolve(MineralID.None, 1, new System.Random(0), out bool clamped);
        Assert.AreEqual(MineralID.None, result);
        Assert.IsFalse(clamped);
    }
}
