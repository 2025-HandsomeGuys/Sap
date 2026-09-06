using NUnit.Framework;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonShapeParserTests
{
    [Test]
    public void Parse_SingleShape_ReadsGridAndAnchors()
    {
        var r = DungeonShapeParser.Parse("# shape: 가로형\nS..X\n", "test");

        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(1, r.Masks.Count);

        var m = r.Masks[0];
        Assert.AreEqual("가로형", m.Name);
        Assert.AreEqual(1, m.Weight);
        Assert.AreEqual(4, m.Width);
        Assert.AreEqual(1, m.Height);
        Assert.AreEqual(0, m.StartCol);
        Assert.AreEqual(3, m.EndCol);
        Assert.IsTrue(m.IsDungeon(0, 1));
    }

    [Test]
    public void Parse_MultipleShapes_SplitOnSeparator()
    {
        string text = "# shape: a\nS.X\n---\n# shape: b\n# weight: 3\nS\n.\nX\n";
        var r = DungeonShapeParser.Parse(text, "test");

        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(2, r.Masks.Count);
        Assert.AreEqual(3, r.Masks[1].Weight);
        Assert.AreEqual(1, r.Masks[1].Width);
        Assert.AreEqual(3, r.Masks[1].Height);
    }

    [Test]
    public void Parse_RockCellsAreNotDungeon()
    {
        var r = DungeonShapeParser.Parse("S..\n.##\n..X\n", "test");

        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.IsFalse(r.Masks[0].IsDungeon(1, 1));
        Assert.IsTrue(r.Masks[0].IsDungeon(1, 0));
    }

    // 짧은 행을 조용히 패딩하면 던전이 의도치 않게 잘린다.
    [Test]
    public void Parse_RaggedRows_Fails()
    {
        var r = DungeonShapeParser.Parse("S..X\nS..\n", "test");
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void Parse_MissingStartOrEnd_Fails()
    {
        Assert.IsFalse(DungeonShapeParser.Parse("....\n", "test").Success, "S와 X가 없음");
        Assert.IsFalse(DungeonShapeParser.Parse("S...\n", "test").Success, "X가 없음");
        Assert.IsFalse(DungeonShapeParser.Parse("S.SX\n", "test").Success, "S가 둘");
    }

    [Test]
    public void Parse_UnknownCharacter_Fails()
    {
        var r = DungeonShapeParser.Parse("S.?X\n", "test");
        Assert.IsFalse(r.Success);
    }

    // 형태 자체가 틀린 것이므로 에러다.
    [Test]
    public void Parse_StartCannotReachEnd_Fails()
    {
        var r = DungeonShapeParser.Parse("S#X\n", "test");
        Assert.IsFalse(r.Success);
    }

    // 손으로 그리다 한 칸 삐져나오는 실수는 흔하다 — 막지 말고 암반으로 강등한다.
    [Test]
    public void Parse_DisconnectedDungeonCell_DemotedWithWarning()
    {
        var r = DungeonShapeParser.Parse("S.X\n###\n..#\n", "test");

        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(1, r.Warnings.Count, string.Join("; ", r.Warnings));
        Assert.IsFalse(r.Masks[0].IsDungeon(2, 0), "고립 칸은 암반으로 강등");
        Assert.IsFalse(r.Masks[0].IsDungeon(2, 1));
    }
}
