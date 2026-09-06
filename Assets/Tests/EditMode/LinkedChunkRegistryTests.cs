using NUnit.Framework;
using UnityEngine;

public class LinkedChunkRegistryTests
{
    private LinkedChunkRegistry _registry;
    private Vector2Int _anchor;
    private Vector2Int _pieceB;
    private Vector2Int _pieceC;

    // MeshRenderer는 MonoBehaviour가 아니므로 테스트용 최소 MonoBehaviour 사용
    private class TestMono : MonoBehaviour { }

    [SetUp]
    public void SetUp()
    {
        _registry = new LinkedChunkRegistry();
        _anchor = new Vector2Int(0, 0);
        _pieceB = new Vector2Int(1, 0);
        _pieceC = new Vector2Int(2, 0);
    }

    [Test]
    public void Register_ThenTryGet_ReturnsRegisteredData()
    {
        var go = new GameObject("TestPrefab");
        var mb = go.AddComponent<TestMono>();
        _registry.Register(_pieceB, mb, _anchor);

        bool found = _registry.TryGet(_pieceB, out var outPrefab, out var outAnchor);

        Assert.IsTrue(found);
        Assert.AreEqual(mb, outPrefab);
        Assert.AreEqual(_anchor, outAnchor);
        Object.DestroyImmediate(go);
    }

    [Test]
    public void TryGet_UnregisteredCoord_ReturnsFalse()
    {
        bool found = _registry.TryGet(_pieceB, out _, out _);
        Assert.IsFalse(found);
    }

    [Test]
    public void Contains_RegisteredCoord_ReturnsTrue()
    {
        _registry.Register(_pieceB, null, _anchor);
        Assert.IsTrue(_registry.Contains(_pieceB));
    }

    [Test]
    public void UnregisterByAnchor_RemovesAllLinkedPieces()
    {
        _registry.Register(_pieceB, null, _anchor);
        _registry.Register(_pieceC, null, _anchor);

        _registry.UnregisterByAnchor(_anchor);

        Assert.IsFalse(_registry.Contains(_pieceB));
        Assert.IsFalse(_registry.Contains(_pieceC));
    }

    [Test]
    public void UnregisterByAnchor_NonExistentAnchor_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _registry.UnregisterByAnchor(new Vector2Int(99, 99)));
    }

    [Test]
    public void Register_SameCoordTwice_OverwritesPrefab()
    {
        var go1 = new GameObject("P1");
        var go2 = new GameObject("P2");
        var mb1 = go1.AddComponent<TestMono>();
        var mb2 = go2.AddComponent<TestMono>();

        _registry.Register(_pieceB, mb1, _anchor);
        _registry.Register(_pieceB, mb2, _anchor);

        _registry.TryGet(_pieceB, out var result, out _);
        Assert.AreEqual(mb2, result);

        Object.DestroyImmediate(go1);
        Object.DestroyImmediate(go2);
    }
}
