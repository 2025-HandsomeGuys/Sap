using NUnit.Framework;
using UnityEngine;

/// <summary>DungeonStateStore — 인스턴스별 격리·중복방지·Capture/Apply 왕복 검증.</summary>
public class DungeonStateStoreTests
{
    [SetUp]
    public void Setup() => DungeonStateStore.Clear();

    [Test]
    public void MarkRockBroken_UsesCurrentInstance()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(3, -5));
        DungeonStateStore.MarkRockBroken(7);
        Assert.IsTrue(DungeonStateStore.IsRockBroken(new Vector2Int(3, -5), 7));
        Assert.IsFalse(DungeonStateStore.IsRockBroken(new Vector2Int(3, -5), 8));
    }

    [Test]
    public void Reward_IsIsolatedPerInstance()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(1, 1));
        DungeonStateStore.MarkRewardCollected("chest");
        Assert.IsTrue(DungeonStateStore.IsRewardCollected(new Vector2Int(1, 1), "chest"));
        Assert.IsFalse(DungeonStateStore.IsRewardCollected(new Vector2Int(2, 2), "chest"));
    }

    [Test]
    public void MarkRockBroken_NoDuplicates()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(0, 0));
        DungeonStateStore.MarkRockBroken(1);
        DungeonStateStore.MarkRockBroken(1);
        var data = DungeonStateStore.Capture();
        Assert.AreEqual(1, data.entries[0].brokenRockIds.Count);
    }

    [Test]
    public void CaptureThenApply_RoundTrips()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(4, -2));
        DungeonStateStore.MarkRockBroken(9);
        DungeonStateStore.MarkRewardCollected("gold_1");
        var data = DungeonStateStore.Capture();

        DungeonStateStore.Clear();
        Assert.IsFalse(DungeonStateStore.IsRockBroken(new Vector2Int(4, -2), 9));

        DungeonStateStore.Apply(data);
        Assert.IsTrue(DungeonStateStore.IsRockBroken(new Vector2Int(4, -2), 9));
        Assert.IsTrue(DungeonStateStore.IsRewardCollected(new Vector2Int(4, -2), "gold_1"));
    }

    [Test]
    public void Apply_NullData_ClearsState()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(0, 0));
        DungeonStateStore.MarkRockBroken(1);
        DungeonStateStore.Apply(null);
        Assert.IsFalse(DungeonStateStore.IsRockBroken(new Vector2Int(0, 0), 1));
    }
}
