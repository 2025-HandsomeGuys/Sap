// @tags: compass, locator, test, editmode
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class ActiveAnchorLocatorTests
{
    static ActiveAnchorSource SourceFrom(Dictionary<Vector2Int, SpecialChunkType> map)
    {
        return () => map;
    }

    [Test]
    public void ReturnsFalse_WhenNoAnchors()
    {
        var locator = new ActiveAnchorLocator(
            8, SourceFrom(new Dictionary<Vector2Int, SpecialChunkType>()));

        bool found = locator.TryFindNearest(Vector2Int.zero, out _);

        Assert.IsFalse(found);
    }

    [Test]
    public void FindsNearest_AmongMultiple()
    {
        var map = new Dictionary<Vector2Int, SpecialChunkType>
        {
            { new Vector2Int(3, 0), SpecialChunkType.Mine },        // 거리 3
            { new Vector2Int(1, 1), SpecialChunkType.DungeonDoor }, // 거리 ~1.41 (가장 가까움)
        };
        var locator = new ActiveAnchorLocator(8, SourceFrom(map));

        bool found = locator.TryFindNearest(Vector2Int.zero, out var target);

        Assert.IsTrue(found);
        Assert.AreEqual(new Vector2Int(1, 1), target.coord);
        Assert.AreEqual(SpecialChunkType.DungeonDoor, target.type);
    }

    [Test]
    public void RespectsMaxRadius()
    {
        var map = new Dictionary<Vector2Int, SpecialChunkType>
        {
            { new Vector2Int(10, 0), SpecialChunkType.Mine },
        };
        var locator = new ActiveAnchorLocator(4, SourceFrom(map));

        bool found = locator.TryFindNearest(Vector2Int.zero, out _);

        Assert.IsFalse(found); // 반경 4 밖
    }

    [Test]
    public void UnlimitedRadius_WhenMaxRadiusZero()
    {
        var map = new Dictionary<Vector2Int, SpecialChunkType>
        {
            { new Vector2Int(500, 0), SpecialChunkType.Mine },
        };
        var locator = new ActiveAnchorLocator(0, SourceFrom(map)); // 0 = 무제한

        bool found = locator.TryFindNearest(Vector2Int.zero, out var target);

        Assert.IsTrue(found);
        Assert.AreEqual(new Vector2Int(500, 0), target.coord);
    }

    [Test]
    public void Filter_SkipsUnwantedTypes()
    {
        var map = new Dictionary<Vector2Int, SpecialChunkType>
        {
            { new Vector2Int(1, 0), SpecialChunkType.Mine },
            { new Vector2Int(3, 0), SpecialChunkType.DungeonDoor },
        };
        // DungeonDoor만 원함 → 더 가까운 Mine은 무시하고 (3,0) 채택
        var locator = new ActiveAnchorLocator(
            8, SourceFrom(map), t => t == SpecialChunkType.DungeonDoor);

        bool found = locator.TryFindNearest(Vector2Int.zero, out var target);

        Assert.IsTrue(found);
        Assert.AreEqual(new Vector2Int(3, 0), target.coord);
    }

    [Test]
    public void WorldPos_IsChunkCenter()
    {
        var map = new Dictionary<Vector2Int, SpecialChunkType>
        {
            { new Vector2Int(2, 3), SpecialChunkType.Mine },
        };
        var locator = new ActiveAnchorLocator(8, SourceFrom(map));

        locator.TryFindNearest(Vector2Int.zero, out var target);

        // ToWorld(2,3) = (20,30), 중심 = +5,+5 = (25,35)
        Assert.AreEqual(25f, target.worldPos.x, 0.001f);
        Assert.AreEqual(35f, target.worldPos.y, 0.001f);
    }

    [Test]
    public void NullSource_ReturnsFalse()
    {
        var locator = new ActiveAnchorLocator(8, () => null);

        bool found = locator.TryFindNearest(Vector2Int.zero, out _);

        Assert.IsFalse(found);
    }
}
