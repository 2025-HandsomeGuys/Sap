using NUnit.Framework;

public class AmbienceSelectorTests
{
    [Test]
    public void SurfaceMorning_PlaysCicadaAndBirds()
    {
        var set = AmbienceSelector.Select(true, TimeOfDay.Morning, TileType.Dirt);
        Assert.AreEqual(SfxKeys.AmbSurfaceDayCicada, set.Primary);
        Assert.AreEqual(SfxKeys.AmbSurfaceDayBirds, set.Secondary);
    }

    [Test]
    public void SurfaceAfternoon_PlaysNightInsectsOnly()
    {
        var set = AmbienceSelector.Select(true, TimeOfDay.Afternoon, TileType.Dirt);
        Assert.AreEqual(SfxKeys.AmbSurfaceNightInsects, set.Primary);
        Assert.IsNull(set.Secondary);
    }

    [Test]
    public void SurfaceIgnoresLayer()
    {
        // 지상은 층 개념이 없다 — TileType이 뭐든 결과가 같아야 한다
        var a = AmbienceSelector.Select(true, TimeOfDay.Morning, TileType.Dirt);
        var b = AmbienceSelector.Select(true, TimeOfDay.Morning, TileType.MagmaRock);
        Assert.AreEqual(a.Primary, b.Primary);
        Assert.AreEqual(a.Secondary, b.Secondary);
    }

    [Test]
    public void UndergroundLayer1_IsSilent()
    {
        var set = AmbienceSelector.Select(false, TimeOfDay.Morning, TileType.Dirt);
        Assert.IsNull(set.Primary);
        Assert.IsNull(set.Secondary);
    }

    [Test]
    public void UndergroundLayer2_PlaysWind()
    {
        var set = AmbienceSelector.Select(false, TimeOfDay.Morning, TileType.HardStone);
        Assert.AreEqual(SfxKeys.AmbLayerWind, set.Primary);
        Assert.IsNull(set.Secondary);
    }

    [Test]
    public void UndergroundDeepLayers_PlayCaveDrip()
    {
        foreach (var t in new[] { TileType.CoolStone, TileType.Ice, TileType.HotStone,
                                  TileType.MagmaRock, TileType.MeteoriteRock })
        {
            var set = AmbienceSelector.Select(false, TimeOfDay.Morning, t);
            Assert.AreEqual(SfxKeys.AmbCaveDrip, set.Primary, $"층 {t}");
            Assert.IsNull(set.Secondary, $"층 {t}");
        }
    }

    [Test]
    public void UndergroundIgnoresTimeOfDay()
    {
        var a = AmbienceSelector.Select(false, TimeOfDay.Morning, TileType.HardStone);
        var b = AmbienceSelector.Select(false, TimeOfDay.Afternoon, TileType.HardStone);
        Assert.AreEqual(a.Primary, b.Primary);
    }

    [Test]
    public void UnknownTileType_IsSilent()
    {
        var set = AmbienceSelector.Select(false, TimeOfDay.Morning, TileType.Empty);
        Assert.IsNull(set.Primary);
        Assert.IsNull(set.Secondary);
    }
}
