using NUnit.Framework;
using UnityEngine;
using Relic;

// 회귀 테스트: 청크 서브트리 전체를 '지형'으로 간주하면 청크 자식인 광물 렌더러까지
// 지형 톤으로 낚아채여, 이후 오브젝트 스캔이 _tracked 때문에 건너뛰고 광물이 안 보인다.
// (땅속 광물은 InfinityMapManager.DetachMineralsToWorld가 증명하듯 청크의 자식이다.)
public class XRayControllerSwapTests
{
    private Shader _shader;
    private Material _terrainMat, _backgroundMat, _objectMat;
    private GameObject _controllerGO;
    private XRayController _controller;

    [SetUp]
    public void SetUp()
    {
        _shader = Shader.Find("Custom/XRayFlat");
        Assert.IsNotNull(_shader, "Custom/XRayFlat 셰이더를 찾을 수 없다.");
        _terrainMat = new Material(_shader);
        _backgroundMat = new Material(_shader);
        _objectMat = new Material(_shader);

        _controllerGO = new GameObject("XRayControllerTest");
        _controller = _controllerGO.AddComponent<XRayController>();
        _controller.ConfigureFlat(
            new XRayFlatPalette(_terrainMat, _backgroundMat, _objectMat),
            Color.gray, Color.black, Color.cyan);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_controllerGO);
        Object.DestroyImmediate(_terrainMat);
        Object.DestroyImmediate(_backgroundMat);
        Object.DestroyImmediate(_objectMat);
    }

    /// <summary>청크 루트 + 광물 자식 구조를 흉내낸다.</summary>
    private static GameObject MakeChunkWithMineralChild(out SpriteRenderer chunkSR, out SpriteRenderer mineralSR)
    {
        var chunk = new GameObject("Chunk");
        chunkSR = chunk.AddComponent<SpriteRenderer>();

        var mineral = new GameObject("MINERAL_0_0");
        mineral.transform.SetParent(chunk.transform);
        mineralSR = mineral.AddComponent<SpriteRenderer>();
        mineralSR.enabled = false; // 땅속에 묻혀 렌더러가 꺼져 있는 상태

        return chunk;
    }

    [Test]
    public void 지형_스왑은_청크_루트_렌더러만_건드린다()
    {
        var chunk = MakeChunkWithMineralChild(out var chunkSR, out var mineralSR);
        try
        {
            _controller.SwapRenderers(chunk, XRayTone.Terrain, forceEnable: false, includeChildren: false);

            Assert.AreSame(_terrainMat, chunkSR.sharedMaterial, "청크 루트는 지형 톤이어야 한다.");
            Assert.AreNotSame(_terrainMat, mineralSR.sharedMaterial, "광물 자식은 지형 톤에 휩쓸리면 안 된다.");
        }
        finally { Object.DestroyImmediate(chunk); }
    }

    [Test]
    public void 지형_스왑_후에도_광물은_오브젝트_톤으로_스왑된다()
    {
        var chunk = MakeChunkWithMineralChild(out _, out var mineralSR);
        try
        {
            // 실제 호출 순서 재현: ScanWorld(지형)가 먼저, Scan(오브젝트)이 나중.
            _controller.SwapRenderers(chunk, XRayTone.Terrain, forceEnable: false, includeChildren: false);
            _controller.SwapRenderers(mineralSR.gameObject, XRayTone.Object, forceEnable: true, includeChildren: true);

            Assert.AreSame(_objectMat, mineralSR.sharedMaterial, "광물은 오브젝트 톤이어야 한다.");
            Assert.IsTrue(mineralSR.enabled, "묻힌 광물은 forceEnable로 켜져야 한다.");
        }
        finally { Object.DestroyImmediate(chunk); }
    }

    [Test]
    public void 원복하면_머티리얼과_enabled가_원래대로_돌아온다()
    {
        var chunk = MakeChunkWithMineralChild(out var chunkSR, out var mineralSR);
        var originalChunkMat = chunkSR.sharedMaterial;
        var originalMineralMat = mineralSR.sharedMaterial;
        try
        {
            _controller.SwapRenderers(chunk, XRayTone.Terrain, forceEnable: false, includeChildren: false);
            _controller.SwapRenderers(mineralSR.gameObject, XRayTone.Object, forceEnable: true, includeChildren: true);

            _controller.ForceOff();

            Assert.AreSame(originalChunkMat, chunkSR.sharedMaterial);
            Assert.AreSame(originalMineralMat, mineralSR.sharedMaterial);
            Assert.IsFalse(mineralSR.enabled, "묻혀 있던 광물은 다시 꺼져야 한다.");
        }
        finally { Object.DestroyImmediate(chunk); }
    }
}
