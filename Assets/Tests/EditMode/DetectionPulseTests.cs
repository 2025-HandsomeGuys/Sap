using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class DetectionPulseTests
{
    // prefab=null 이면 GetSize가 chunkSizeX/Y(기본 1)로 폴백 → 단일 청크 앵커로 동작.
    private static SpecialChunkManager.SpecialChunkPool MakePool(TileType layer, float chance)
    {
        return new SpecialChunkManager.SpecialChunkPool
        {
            targetLayer = layer,
            chunks = new List<SpecialChunkManager.SpecialChunkDef>
            {
                new SpecialChunkManager.SpecialChunkDef
                {
                    prefab = null,
                    spawnChance = chance,
                    chunkType = SpecialChunkType.DungeonDoor,
                    chunkSizeX = 1, chunkSizeY = 1,
                }
            }
        };
    }

    [Test]
    public void TrySelect_IsDeterministic_ForSameCoord()
    {
        var pools = new List<SpecialChunkManager.SpecialChunkPool> { MakePool(TileType.Dirt, 100f) };
        var sel = new SpecialChunkSelector(pools, minChunkSpacing: 0, layerBoundarySpacing: 0, layerBoundaryYCoords: new int[0]);
        var reg = new SubChunkRegistry();

        var a = sel.TrySelect(new Vector2Int(3, -5), TileType.Dirt, 12345, reg);
        var b = sel.TrySelect(new Vector2Int(3, -5), TileType.Dirt, 12345, reg);

        Assert.IsTrue(a.HasValue, "chance 100% 정의는 항상 선택돼야 한다");
        Assert.AreEqual(a.Value.chunkType, b.Value.chunkType);
    }

    [Test]
    public void TrySelect_ReturnsNull_ForZeroChance()
    {
        var pools = new List<SpecialChunkManager.SpecialChunkPool> { MakePool(TileType.Dirt, 0f) };
        var sel = new SpecialChunkSelector(pools, 0, 0, new int[0]);
        var reg = new SubChunkRegistry();

        Assert.IsFalse(sel.TrySelect(new Vector2Int(3, -5), TileType.Dirt, 12345, reg).HasValue);
    }

    [Test]
    public void Store_Report_DedupesAndRoundTrips()
    {
        var store = new DetectedChunkStore();
        store.Report(new Vector2Int(1, -2), SpecialChunkType.DungeonDoor, false);
        store.Report(new Vector2Int(1, -2), SpecialChunkType.DungeonDoor, false); // 중복
        store.Report(new Vector2Int(5, -9), SpecialChunkType.AntiGravity, true);

        Assert.AreEqual(2, store.All.Count);

        var save = store.Capture();
        var store2 = new DetectedChunkStore();
        store2.Apply(save);

        Assert.AreEqual(2, store2.All.Count);
        Assert.IsTrue(store2.All[new Vector2Int(5, -9)].visited);
        Assert.AreEqual(SpecialChunkType.DungeonDoor, store2.All[new Vector2Int(1, -2)].type);
    }

    [Test]
    public void Store_OnChanged_FiresOnNewButNotOnUnchanged()
    {
        var store = new DetectedChunkStore();
        int fires = 0;
        store.OnChanged += () => fires++;

        store.Report(new Vector2Int(0, 0), SpecialChunkType.Mine, false); // +1
        store.Report(new Vector2Int(0, 0), SpecialChunkType.Mine, false); // 변화 없음 → 0
        Assert.AreEqual(1, fires);
    }
}
