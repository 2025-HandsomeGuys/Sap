using NUnit.Framework;

public class SurfaceSceneRegistryTests
{
    /// <summary>
    /// 회귀 테스트: 실제 지상 씬 이름은 "DemoUpground"다.
    /// 이게 빠져 있어서 정산씬에서만 밤 앰비언스가 들리고
    /// 지상 씬으로 넘어가는 순간(=BGM 시작) 앰비언스가 꺼졌다.
    /// </summary>
    [Test]
    public void DemoUpground_IsSurface()
    {
        Assert.IsTrue(SurfaceSceneRegistry.IsSurface("DemoUpground"));
    }

    [Test]
    public void SettlementScene_IsSurface()
    {
        Assert.IsTrue(SurfaceSceneRegistry.IsSurface("SettlementScene"));
    }

    [Test]
    public void UndergroundAndDungeon_AreNotSurface()
    {
        Assert.IsFalse(SurfaceSceneRegistry.IsSurface("DemoUnderground"));
        Assert.IsFalse(SurfaceSceneRegistry.IsSurface("Dungeon1"));
    }

    [Test]
    public void NullOrEmpty_IsNotSurface()
    {
        Assert.IsFalse(SurfaceSceneRegistry.IsSurface(null));
        Assert.IsFalse(SurfaceSceneRegistry.IsSurface(""));
    }

    [Test]
    public void ExtraList_AddsToCanonicalList()
    {
        Assert.IsTrue(SurfaceSceneRegistry.IsSurface("MyNewSurface", new[] { "MyNewSurface" }));
    }

    /// <summary>
    /// 인스펙터·프리팹에 직렬화된 낡은 목록은 '대체'가 아니라 '추가'여야 한다.
    /// FanalPlayer.prefab의 FootstepPlayer가 실제로 옛 목록을 물고 있었다.
    /// </summary>
    [Test]
    public void StaleExtraList_DoesNotShadowCanonicalList()
    {
        var stale = new[] { "UpgroundScene", "SettlementScene" };
        Assert.IsTrue(SurfaceSceneRegistry.IsSurface("DemoUpground", stale));
    }

    [Test]
    public void NullExtraList_FallsBackToCanonicalList()
    {
        Assert.IsTrue(SurfaceSceneRegistry.IsSurface("DemoUpground", null));
        Assert.IsFalse(SurfaceSceneRegistry.IsSurface("DemoUnderground", null));
    }
}
