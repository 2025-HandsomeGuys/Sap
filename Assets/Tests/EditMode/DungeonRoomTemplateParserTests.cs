using NUnit.Framework;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonRoomTemplateParserTests
{
    // 방 크기 4x3 짜리 최소 템플릿 두 개
    private const string TwoRooms =
        "# room: START R\n" +
        "[TILES]\n" +
        "WWWW\n" +
        "W...\n" +
        "WWWW\n" +
        "[OBJECTS]\n" +
        "....\n" +
        ".E..\n" +
        "....\n" +
        "---\n" +
        "# room: LR\n" +
        "# weight: 3\n" +
        "[TILES]\n" +
        "WWWW\n" +
        "..0.\n" +
        "WWWW\n" +
        "[OBJECTS]\n" +
        "....\n" +
        "....\n" +
        "....\n";

    [Test]
    public void Parse_TwoRooms_ReturnsBoth()
    {
        var r = DungeonRoomTemplateParser.Parse(TwoRooms, 4, 3, "test");
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(2, r.Templates.Count);
    }

    // Role은 폐지됐다(START는 후처리 배치) — 경고 후 무시되고 Opens만 남는다.
    [Test]
    public void Parse_HeaderRoleTokenIgnored_OpensParsed()
    {
        var r = DungeonRoomTemplateParser.Parse(TwoRooms, 4, 3, "test");
        Assert.AreEqual(RoomOpen.R, r.Templates[0].Opens);
        Assert.Greater(r.Warnings.Count, 0, "START 토큰 경고가 있어야 함");
        Assert.AreEqual(RoomOpen.L | RoomOpen.R, r.Templates[1].Opens);
    }

    [Test]
    public void Parse_WeightDefaultsToOne()
    {
        var r = DungeonRoomTemplateParser.Parse(TwoRooms, 4, 3, "test");
        Assert.AreEqual(1, r.Templates[0].Weight);
        Assert.AreEqual(3, r.Templates[1].Weight);
    }

    [Test]
    public void Parse_GridsAreRowColIndexed()
    {
        var r = DungeonRoomTemplateParser.Parse(TwoRooms, 4, 3, "test");
        var start = r.Templates[0];
        Assert.AreEqual('W', start.Tiles[0, 0]);
        Assert.AreEqual('.', start.Tiles[1, 1]);
        Assert.AreEqual('E', start.Objects[1, 1]);
        Assert.AreEqual('.', start.Links[1, 1]); // LINKS 없으면 '.'
    }

    // FILL 토큰도 폐지됐다(암반은 RoomPlan.Role로 판정) — 경고 후 무시되고 Opens=None만 남는다.
    [Test]
    public void Parse_FillRoleTokenIgnored_OpensNone()
    {
        string s =
            "# room: FILL\n" +
            "[TILES]\nWWWW\nWWWW\nWWWW\n" +
            "[OBJECTS]\n....\n....\n....\n";
        var r = DungeonRoomTemplateParser.Parse(s, 4, 3, "test");
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(RoomOpen.None, r.Templates[0].Opens);
        Assert.Greater(r.Warnings.Count, 0, "FILL 토큰 경고가 있어야 함");
    }

    [Test]
    public void Parse_WrongRoomSize_Fails()
    {
        string s =
            "# room: LR\n" +
            "[TILES]\nWWWWW\nWWWWW\nWWWWW\n" +   // 5칸인데 4칸을 기대
            "[OBJECTS]\n.....\n.....\n.....\n";
        var r = DungeonRoomTemplateParser.Parse(s, 4, 3, "test");
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.Errors.Count > 0);
    }

    [Test]
    public void Parse_ShortRow_Fails()
    {
        string s =
            "# room: LR\n" +
            "[TILES]\nWWWW\nWW\nWWWW\n" +           // 2칸짜리 행 — 패딩하지 않고 에러
            "[OBJECTS]\n....\n....\n....\n";
        var r = DungeonRoomTemplateParser.Parse(s, 4, 3, "test");
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void Parse_MissingRoomHeader_Fails()
    {
        string s = "[TILES]\nWWWW\nWWWW\nWWWW\n[OBJECTS]\n....\n....\n....\n";
        var r = DungeonRoomTemplateParser.Parse(s, 4, 3, "test");
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void Parse_UnknownOpensChar_Fails()
    {
        string s =
            "# room: LX\n" +
            "[TILES]\nWWWW\nWWWW\nWWWW\n[OBJECTS]\n....\n....\n....\n";
        var r = DungeonRoomTemplateParser.Parse(s, 4, 3, "test");
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void Parse_WithLinks_ParsesThirdLayer()
    {
        string s =
            "# room: LR\n" +
            "[TILES]\nWWWW\nWWWW\nWWWW\n" +
            "[OBJECTS]\nP...\n...G\n....\n" +
            "[LINKS]\n1...\n...1\n....\n";
        var r = DungeonRoomTemplateParser.Parse(s, 4, 3, "test");
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual('1', r.Templates[0].Links[0, 0]);
        Assert.AreEqual('1', r.Templates[0].Links[1, 3]);
    }

    [Test]
    public void Parse_EmptyText_Fails()
    {
        var r = DungeonRoomTemplateParser.Parse("", 4, 3, "test");
        Assert.IsFalse(r.Success);
    }
}
