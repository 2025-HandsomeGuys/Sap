using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonRoomComposerTests
{
    // 4x4 방. 양 끝 방은 사방이 막혀 있어 경계 개통 동작을 검증할 수 있다.
    private const string StartEndOnly =
        "# room: START R\n" +
        "[TILES]\nWWWW\nW..W\nW..W\nWWWW\n" +
        "[OBJECTS]\n....\n.E..\n....\n....\n" +
        "---\n" +
        "# room: END L\n" +
        "[TILES]\nWWWW\nW..W\nW..W\nWWWW\n" +
        "[OBJECTS]\n....\n..X.\n....\n....\n";

    // 확률 타일 '0'이 내부에 있는 LR 방
    private const string WithChanceTile =
        "# room: LR\n" +
        "[TILES]\nWWWW\n.00.\n....\nWWWW\n" +
        "[OBJECTS]\n....\n....\n....\n....\n";

    private DungeonGenPresetSO _preset;

    [SetUp]
    public void SetUp()
    {
        _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();
        _preset.roomWidth = 4;
        _preset.roomHeight = 4;
        _preset.forceCarveBoundaries = true;
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_preset);

    private List<DungeonRoomTemplate> Templates(params string[] texts)
    {
        var all = new List<DungeonRoomTemplate>();
        foreach (string t in texts)
        {
            var r = DungeonRoomTemplateParser.Parse(t, _preset.roomWidth, _preset.roomHeight, "test");
            Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
            all.AddRange(r.Templates);
        }
        return all;
    }

    // 한 줄짜리 경로 계획: START R → (LR …) → END L
    private static RoomPlan[,] Line(int len)
    {
        var g = new RoomPlan[1, len];
        for (int c = 0; c < len; c++)
            g[0, c] = new RoomPlan { Role = RoomRole.Normal, Opens = RoomOpen.L | RoomOpen.R };
        g[0, 0] = new RoomPlan { Role = RoomRole.Start, Opens = RoomOpen.R };
        g[0, len - 1] = new RoomPlan { Role = RoomRole.End, Opens = RoomOpen.L };
        return g;
    }

    [Test]
    public void Compose_SizeIncludesBorderRing()
    {
        var res = DungeonRoomComposer.Compose(Line(2), Templates(StartEndOnly), _preset, new System.Random(1));
        Assert.AreEqual(2 * 4 + 2, res.Width);   // 10
        Assert.AreEqual(1 * 4 + 2, res.Height);  // 6
    }

    [Test]
    public void Compose_BorderRingIsWall()
    {
        var res = DungeonRoomComposer.Compose(Line(2), Templates(StartEndOnly), _preset, new System.Random(1));
        for (int c = 0; c < res.Width; c++)
        {
            Assert.AreEqual('W', res.Tiles[0, c], $"윗변 ({0},{c})");
            Assert.AreEqual('W', res.Tiles[res.Height - 1, c], $"아랫변 ({res.Height - 1},{c})");
        }
        for (int r = 0; r < res.Height; r++)
        {
            Assert.AreEqual('W', res.Tiles[r, 0], $"왼변 ({r},0)");
            Assert.AreEqual('W', res.Tiles[r, res.Width - 1], $"오른변 ({r},{res.Width - 1})");
        }
    }

    [Test]
    public void Compose_StampsTemplatesAtRoomOffsets()
    {
        var res = DungeonRoomComposer.Compose(Line(2), Templates(StartEndOnly), _preset, new System.Random(1));
        // E는 방(0,0) 로컬(1,1) → 전역(0*4+1+1, 0*4+1+1) = (2,2)
        Assert.AreEqual('E', res.Objects[2, 2]);
        // X는 방(0,1) 로컬(1,2) → 전역(2, 1*4+2+1) = (2,7)
        Assert.AreEqual('X', res.Objects[2, 7]);
    }

    [Test]
    public void Compose_ForceCarve_OpensBoundaryBetweenPathRooms()
    {
        var res = DungeonRoomComposer.Compose(Line(2), Templates(StartEndOnly), _preset, new System.Random(1));
        // 방 0의 마지막 열 = 전역 열 4, 방 1의 첫 열 = 전역 열 5.
        // 개구부 규약 행(방 하단 위쪽 3행 → roomHeight 4에서는 로컬 0~2 → 전역 1~3) 중 가운데를 확인.
        Assert.AreEqual('.', res.Tiles[2, 4], "경계 왼쪽이 뚫려야 함");
        Assert.AreEqual('.', res.Tiles[2, 5], "경계 오른쪽이 뚫려야 함");
    }

    [Test]
    public void Compose_CarveDisabled_LeavesWalledTemplateSealed()
    {
        _preset.forceCarveBoundaries = false;
        var res = DungeonRoomComposer.Compose(Line(2), Templates(StartEndOnly), _preset, new System.Random(1));
        Assert.AreEqual('W', res.Tiles[2, 4]);
        Assert.AreEqual('W', res.Tiles[2, 5]);
    }

    [Test]
    public void Compose_ChanceTilesAreResolved()
    {
        var res = DungeonRoomComposer.Compose(Line(3), Templates(StartEndOnly, WithChanceTile), _preset, new System.Random(5));
        for (int r = 0; r < res.Height; r++)
            for (int c = 0; c < res.Width; c++)
                Assert.IsTrue(res.Tiles[r, c] == 'W' || res.Tiles[r, c] == '.',
                    $"({r},{c})에 미해석 심볼 '{res.Tiles[r, c]}'이 남음");
    }

    [Test]
    public void Compose_MissingTemplate_WarnsAndMakesBlankRoom()
    {
        // LR 템플릿이 없는 상태로 가운데 방을 요구
        var res = DungeonRoomComposer.Compose(Line(3), Templates(StartEndOnly), _preset, new System.Random(1));
        Assert.IsTrue(res.Warnings.Count > 0, "템플릿 부재 경고가 있어야 함");
        // 빈 방은 테두리만 벽, 내부는 빈칸. 방(0,1) 로컬(1,1) → 전역(2, 1*4+1+1) = (2,6)
        Assert.AreEqual('.', res.Tiles[2, 6]);
    }

    [Test]
    public void Compose_ObjectsAndLinksDefaultToDot()
    {
        var res = DungeonRoomComposer.Compose(Line(2), Templates(StartEndOnly), _preset, new System.Random(1));
        Assert.AreEqual('.', res.Objects[0, 0]);
        Assert.AreEqual('.', res.Links[0, 0]);
        Assert.AreEqual('.', res.Links[2, 2]);
    }

    [Test]
    public void Compose_SameSeed_SameResult()
    {
        var t = Templates(StartEndOnly, WithChanceTile);
        var a = DungeonRoomComposer.Compose(Line(4), t, _preset, new System.Random(42));
        var b = DungeonRoomComposer.Compose(Line(4), t, _preset, new System.Random(42));
        for (int r = 0; r < a.Height; r++)
            for (int c = 0; c < a.Width; c++)
                Assert.AreEqual(a.Tiles[r, c], b.Tiles[r, c], $"({r},{c})");
    }

    [Test]
    public void Compose_RockCell_StaysSolidWall()
    {
        var plan = new RoomPlan[1, 2];
        plan[0, 0] = new RoomPlan { Role = RoomRole.Normal, Opens = RoomOpen.R };
        plan[0, 1] = new RoomPlan { Role = RoomRole.Rock, Opens = RoomOpen.None };

        var res = DungeonRoomComposer.Compose(plan, Templates(StartEndOnly), _preset, new System.Random(1));

        // 오른쪽 칸(암반)의 방 영역이 전부 벽이어야 한다.
        for (int r = 1; r <= _preset.roomHeight; r++)
            for (int c = _preset.roomWidth + 1; c <= _preset.roomWidth * 2; c++)
                Assert.AreEqual('W', res.Tiles[r, c], $"암반 칸 ({r},{c})");
    }
}
