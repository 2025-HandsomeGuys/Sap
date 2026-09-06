using NUnit.Framework;

public class FootstepSurfaceSelectorTests
{
    [Test]
    public void Surface_UsesGrassPair()
    {
        var p = FootstepSurfaceSelector.Select(true, TileType.Dirt);
        Assert.AreEqual(SfxKeys.StepGrass, p.A);
        Assert.AreEqual(SfxKeys.StepGrassB, p.B);
    }

    [Test]
    public void SurfaceIgnoresLayer()
    {
        // 지상은 층 개념이 없다 — TileType이 뭐든 결과가 같아야 한다
        var a = FootstepSurfaceSelector.Select(true, TileType.Dirt);
        var b = FootstepSurfaceSelector.Select(true, TileType.HardStone);
        Assert.AreEqual(a.A, b.A);
        Assert.AreEqual(a.B, b.B);
    }

    [Test]
    public void UndergroundLayer2_UsesHardStonePair()
    {
        var p = FootstepSurfaceSelector.Select(false, TileType.HardStone);
        Assert.AreEqual(SfxKeys.StepHardStone, p.A);
        Assert.AreEqual(SfxKeys.StepHardStoneB, p.B);
    }

    [Test]
    public void UndergroundOtherLayers_FallBackToDirt()
    {
        foreach (var t in new[] { TileType.Dirt, TileType.CoolStone, TileType.Ice,
                                  TileType.HotStone, TileType.MagmaRock, TileType.MeteoriteRock })
        {
            var p = FootstepSurfaceSelector.Select(false, t);
            Assert.AreEqual(SfxKeys.StepDirt, p.A, $"층 {t}");
            Assert.AreEqual(SfxKeys.StepDirtB, p.B, $"층 {t}");
        }
    }

    [Test]
    public void UnknownLayer_FallsBackToDirt()
    {
        var p = FootstepSurfaceSelector.Select(false, TileType.Empty);
        Assert.AreEqual(SfxKeys.StepDirt, p.A);
    }

    [Test]
    public void PairKeysAreNeverNull()
    {
        foreach (bool surface in new[] { true, false })
        foreach (var t in new[] { TileType.Dirt, TileType.HardStone, TileType.Ice, TileType.Empty })
        {
            var p = FootstepSurfaceSelector.Select(surface, t);
            Assert.IsNotNull(p.A);
            Assert.IsNotNull(p.B);
        }
    }
}
