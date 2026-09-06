using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonEntryPlacerTests
{
    private DungeonGenPresetSO _preset;

    [SetUp]
    public void SetUp()
    {
        _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();
        _preset.roomWidth = 4;
        _preset.roomHeight = 4;
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_preset);

    // 방 2칸(4x4)짜리 격자. 타일은 지정한 대로, 오브젝트는 전부 비어 있다.
    private static ComposeResult Make(params string[] tileRows)
    {
        int h = tileRows.Length, w = tileRows[0].Length;
        var res = new ComposeResult
        {
            Width = w, Height = h,
            Tiles = new char[h, w], Objects = new char[h, w], Links = new char[h, w],
        };
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                res.Tiles[r, c] = tileRows[r][c];
                res.Objects[r, c] = '.';
                res.Links[r, c] = '.';
            }
        return res;
    }

    private static RoomPlan[,] TwoRooms()
    {
        var plan = new RoomPlan[1, 2];
        plan[0, 0] = new RoomPlan { Role = RoomRole.Start, Opens = RoomOpen.R };
        plan[0, 1] = new RoomPlan { Role = RoomRole.End, Opens = RoomOpen.L };
        return plan;
    }

    private static int Count(ComposeResult res, char symbol)
    {
        int n = 0;
        for (int r = 0; r < res.Height; r++)
            for (int c = 0; c < res.Width; c++)
                if (res.Objects[r, c] == symbol) n++;
        return n;
    }

    // 10칸 폭 = 외곽 링 1 + 방 4 + 방 4 + 외곽 링 1
    private static ComposeResult OpenRooms() => Make(
        "WWWWWWWWWW",
        "W........W",
        "W........W",
        "W........W",
        "W........W",
        "WWWWWWWWWW");

    [Test]
    public void Place_PutsExactlyOneEntryAndOneExit()
    {
        var res = OpenRooms();
        DungeonEntryPlacer.Place(res, TwoRooms(), _preset, new System.Random(1));

        Assert.AreEqual(1, Count(res, 'E'));
        Assert.AreEqual(1, Count(res, 'X'));
    }

    [Test]
    public void Place_StandsOnFloor()
    {
        var res = OpenRooms();
        DungeonEntryPlacer.Place(res, TwoRooms(), _preset, new System.Random(1));

        for (int r = 0; r < res.Height; r++)
            for (int c = 0; c < res.Width; c++)
                if (res.Objects[r, c] == 'E' || res.Objects[r, c] == 'X')
                {
                    Assert.AreEqual('.', res.Tiles[r, c], $"({r},{c}) 빈칸이 아님");
                    Assert.AreEqual('W', res.Tiles[r + 1, c], $"({r},{c}) 아래가 바닥이 아님");
                }
    }

    [Test]
    public void Place_EntryGoesInStartRoom_ExitInEndRoom()
    {
        var res = OpenRooms();
        DungeonEntryPlacer.Place(res, TwoRooms(), _preset, new System.Random(1));

        for (int r = 0; r < res.Height; r++)
            for (int c = 0; c < res.Width; c++)
            {
                if (res.Objects[r, c] == 'E') Assert.LessOrEqual(c, 4, "E가 시작 방 밖에 있다");
                if (res.Objects[r, c] == 'X') Assert.Greater(c, 4, "X가 끝 방 밖에 있다");
            }
    }

    // 슬롯을 덮어 없애지 않는다.
    [Test]
    public void Place_AvoidsSlotCells()
    {
        var res = OpenRooms();
        for (int c = 1; c <= 4; c++) res.Objects[4, c] = '?'; // 시작 방 바닥을 슬롯으로 메움
        res.Objects[4, 2] = '.';                              // 한 칸만 비워둠

        DungeonEntryPlacer.Place(res, TwoRooms(), _preset, new System.Random(1));

        Assert.AreEqual('E', res.Objects[4, 2]);
        Assert.AreEqual(1, Count(res, 'E'));
    }

    // 바닥이 아예 없으면 실패시키지 않고 뚫어서라도 놓는다 — E 없는 맵은 임포트가 무의미하다.
    [Test]
    public void Place_NoFloorAtAll_CarvesOneAndWarns()
    {
        var res = Make(
            "WWWWWWWWWW",
            "W........W",
            "W........W",
            "W........W",
            "W........W",
            "W........W");   // 맨 아랫줄이 빈칸이라 받침이 없다

        DungeonEntryPlacer.Place(res, TwoRooms(), _preset, new System.Random(1));

        Assert.AreEqual(1, Count(res, 'E'));
        Assert.Greater(res.Warnings.Count, 0);
    }
}
