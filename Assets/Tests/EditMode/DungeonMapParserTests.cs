using NUnit.Framework;
using Gameplay.Dungeon.Authoring;

public class DungeonMapParserTests
{
    private const string Valid =
        "# dungeon: cave_01\n" +
        "# cell: 1\n" +
        "[TILES]\n" +
        "WWW\n" +
        "W.W\n" +
        "WWW\n" +
        "[OBJECTS]\n" +
        "...\n" +
        ".E.\n" +
        "...\n";

    [Test]
    public void Parse_Valid_SetsDimensionsAndMeta()
    {
        var r = DungeonMapParser.Parse(Valid);
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(3, r.Data.Width);
        Assert.AreEqual(3, r.Data.Height);
        Assert.AreEqual("cave_01", r.Data.Name);
        Assert.AreEqual('W', r.Data.Tiles[0, 0]);
        Assert.AreEqual('.', r.Data.Tiles[1, 1]);
        Assert.AreEqual('E', r.Data.Objects[1, 1]);
        Assert.AreEqual('.', r.Data.Links[1, 1]); // LINKS 없으면 '.'
    }

    [Test]
    public void Parse_MismatchedBlockSize_Fails()
    {
        string bad = "[TILES]\nWWW\nWWW\n[OBJECTS]\n..\n..\n"; // width 3 vs 2
        var r = DungeonMapParser.Parse(bad);
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.Errors.Count > 0);
    }

    [Test]
    public void Parse_ShortRows_PaddedWithDots()
    {
        string s = "[TILES]\nWWW\nW\n[OBJECTS]\n...\n...\n";
        var r = DungeonMapParser.Parse(s);
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual('.', r.Data.Tiles[1, 2]); // 짧은 행은 '.'로 패딩
    }

    [Test]
    public void Parse_WithLinks_ParsesThirdLayer()
    {
        string s = "[TILES]\nWW\nWW\n[OBJECTS]\nP.\n.G\n[LINKS]\n1.\n.1\n";
        var r = DungeonMapParser.Parse(s);
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual('1', r.Data.Links[0, 0]);
        Assert.AreEqual('1', r.Data.Links[1, 1]);
    }

    [Test]
    public void Parse_MissingTilesSection_Fails()
    {
        var r = DungeonMapParser.Parse("[OBJECTS]\n..\n..\n");
        Assert.IsFalse(r.Success);
    }
}
