using NUnit.Framework;
using UnityEngine;

/// <summary>
/// SubChunkRegistry 등록·조회·역산 테스트.
/// </summary>
public class SubChunkRegistryTests
{
    private SubChunkRegistry _registry;

    [SetUp]
    public void SetUp()
    {
        _registry = new SubChunkRegistry();
    }

    [Test]
    public void Contains_BeforeRegister_ReturnsFalse()
    {
        Assert.IsFalse(_registry.Contains(new Vector2Int(3, -3)));
    }

    [Test]
    public void Contains_AfterRegisterReserved_ReturnsTrue()
    {
        var sub = new Vector2Int(3, -3);
        var anchor = new Vector2Int(2, -3);
        _registry.RegisterReserved(sub, anchor);

        Assert.IsTrue(_registry.Contains(sub));
    }

    [Test]
    public void Contains_AnchorCoord_ReturnsFalse_WhenOnlySubRegistered()
    {
        // 앵커 자신은 서브 등록과 무관 — Contains는 서브 위치만 체크
        var sub = new Vector2Int(3, -3);
        var anchor = new Vector2Int(2, -3);
        _registry.RegisterReserved(sub, anchor);

        Assert.IsFalse(_registry.Contains(anchor));
    }

    [Test]
    public void TryGetAnchor_ReturnsRegisteredAnchorCoord()
    {
        var sub = new Vector2Int(4, -4);
        var anchor = new Vector2Int(2, -3);
        _registry.RegisterReserved(sub, anchor);

        bool found = _registry.TryGetAnchor(sub, out var result);

        Assert.IsTrue(found);
        Assert.AreEqual(anchor, result);
    }

    [Test]
    public void TryGetAnchor_UnregisteredCoord_ReturnsFalse()
    {
        bool found = _registry.TryGetAnchor(new Vector2Int(99, -99), out _);
        Assert.IsFalse(found);
    }

    [Test]
    public void TryGet_WithoutRegisterInstance_ReturnsFalse()
    {
        // RegisterReserved만 한 좌표는 인스턴스가 없으므로 TryGet은 false
        var sub = new Vector2Int(3, -3);
        _registry.RegisterReserved(sub, new Vector2Int(2, -3));

        bool found = _registry.TryGet(sub, out var chunk);

        Assert.IsFalse(found);
        Assert.IsNull(chunk);
    }

    [Test]
    public void RegisterReserved_MultipleSubCoords_AllContained()
    {
        var anchor = new Vector2Int(2, -3);
        var subs = new[]
        {
            new Vector2Int(3, -3),
            new Vector2Int(4, -3),
            new Vector2Int(2, -4),
            new Vector2Int(3, -4),
            new Vector2Int(4, -4),
        };

        foreach (var sub in subs)
            _registry.RegisterReserved(sub, anchor);

        foreach (var sub in subs)
            Assert.IsTrue(_registry.Contains(sub), $"서브좌표 {sub}가 Contains에서 false 반환됨");
    }
}
