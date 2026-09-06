using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring;

public class DungeonTilesetLookupTests
{
    [Test]
    public void FirstCharOfSymbolString_IsUsedAsKey()
    {
        var so = ScriptableObject.CreateInstance<DungeonTilesetSO>();
        so.objectMappings.Add(new DungeonTilesetSO.ObjectMapping { symbol = "E", prefab = new GameObject("Entry"), zRotation = 90f });
        so.BuildLookup();

        Assert.IsTrue(so.TryGetObject('E', out var entry));
        Assert.AreEqual(90f, entry.zRotation);
        Assert.IsFalse(so.TryGetObject('X', out _));
    }

    [Test]
    public void UnmappedTile_ReturnsFalse()
    {
        var so = ScriptableObject.CreateInstance<DungeonTilesetSO>();
        so.BuildLookup();
        Assert.IsFalse(so.TryGetTile('W', out _));
    }
}
