using NUnit.Framework;
using UnityEngine;

/// <summary>DungeonSaveData JsonUtility 왕복 직렬화 검증 (순수 DTO).</summary>
public class DungeonSaveDataTests
{
    [Test]
    public void JsonUtility_RoundTrips_Entries()
    {
        var data = new DungeonSaveData();
        var e = new DungeonInstanceEntry { x = 2, y = -3 };
        e.brokenRockIds.Add(5);
        e.collectedRewardIds.Add("chest");
        data.entries.Add(e);

        string json = JsonUtility.ToJson(data);
        var restored = JsonUtility.FromJson<DungeonSaveData>(json);

        Assert.AreEqual(1, restored.entries.Count);
        Assert.AreEqual(2, restored.entries[0].x);
        Assert.AreEqual(-3, restored.entries[0].y);
        Assert.AreEqual(5, restored.entries[0].brokenRockIds[0]);
        Assert.AreEqual("chest", restored.entries[0].collectedRewardIds[0]);
    }
}
