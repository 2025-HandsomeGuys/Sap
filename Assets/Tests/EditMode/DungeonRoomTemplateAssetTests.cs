using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Gameplay.Dungeon.Authoring;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonRoomTemplateAssetTests
{
    private const string AssetPath = "Assets/DungeonMaps/Templates/cave_rooms.txt";
    private const string PresetPath = "Assets/DungeonMaps/DungeonGenPreset.asset";
    private const string TilesetPath = "Assets/GameData/Dungeon/DefaultDungeonTileset.asset";
    private const int RoomW = 10;
    private const int RoomH = 6;

    private static RoomTemplateParseResult Load()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
        Assert.IsNotNull(asset, $"{AssetPath} 를 찾지 못했습니다.");
        return DungeonRoomTemplateParser.Parse(asset.text, RoomW, RoomH, "cave_rooms");
    }

    private static DungeonGenPresetSO LoadPreset()
    {
        var preset = AssetDatabase.LoadAssetAtPath<DungeonGenPresetSO>(PresetPath);
        Assert.IsNotNull(preset, $"{PresetPath} 를 찾지 못했습니다.");
        preset.BuildLookup();
        return preset;
    }

    private static DungeonTilesetSO LoadTileset()
    {
        var tileset = AssetDatabase.LoadAssetAtPath<DungeonTilesetSO>(TilesetPath);
        Assert.IsNotNull(tileset, $"{TilesetPath} 를 찾지 못했습니다.");
        tileset.BuildLookup();
        return tileset;
    }

    [Test]
    public void CaveRooms_ParsesAtRoomSize()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));
        Assert.GreaterOrEqual(r.Templates.Count, 9);
    }

    // 미로 배선은 개구부 조합 15종이 전부 나올 수 있다. 빠지면 상위집합 폴백이 걸려
    // 여분의 개구부(막힌 벽감)가 생긴다.
    [Test]
    public void CaveRooms_CoverAllFifteenOpenCombinations()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));

        for (int bits = 1; bits <= 15; bits++)
        {
            var opens = (RoomOpen)bits;
            bool found = r.Templates.Exists(t => t.Opens == opens);
            Assert.IsTrue(found, $"'{RoomTypeUtil.FormatOpens(opens)}' 템플릿이 없습니다.");
        }
    }

    [Test]
    public void CaveRooms_OpeningsFollowConventionPositions()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));

        foreach (var t in r.Templates)
        {
            string who = t.Describe();

            if ((t.Opens & RoomOpen.L) != 0)
                for (int row = 2; row <= 4; row++)
                    Assert.AreEqual('.', t.Tiles[row, 0], $"[{who}] 왼쪽 개구부 행 {row}가 막혀 있음");

            if ((t.Opens & RoomOpen.R) != 0)
                for (int row = 2; row <= 4; row++)
                    Assert.AreEqual('.', t.Tiles[row, RoomW - 1], $"[{who}] 오른쪽 개구부 행 {row}가 막혀 있음");

            if ((t.Opens & RoomOpen.U) != 0)
                for (int col = 4; col <= 6; col++)
                    Assert.AreEqual('.', t.Tiles[0, col], $"[{who}] 위쪽 개구부 열 {col}이 막혀 있음");

            if ((t.Opens & RoomOpen.D) != 0)
                for (int col = 4; col <= 6; col++)
                    Assert.AreEqual('.', t.Tiles[RoomH - 1, col], $"[{who}] 아래쪽 개구부 열 {col}이 막혀 있음");
        }
    }

    // 닫힌 면은 확정 벽이어야 한다. '.'은 물론이고 확률 타일('0'/'1'/'2')도 안 된다 —
    // 굴림 결과에 따라 뚫려서 옆 방으로 새는 구멍이 생긴다.
    [Test]
    public void CaveRooms_ClosedEdgesAreSolidWall()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));

        foreach (var t in r.Templates)
        {
            string who = t.Describe();

            if ((t.Opens & RoomOpen.L) == 0)
                for (int row = 0; row < RoomH; row++)
                    Assert.AreEqual('W', t.Tiles[row, 0], $"[{who}] 닫힌 왼쪽 면 행 {row}");

            if ((t.Opens & RoomOpen.R) == 0)
                for (int row = 0; row < RoomH; row++)
                    Assert.AreEqual('W', t.Tiles[row, RoomW - 1], $"[{who}] 닫힌 오른쪽 면 행 {row}");

            if ((t.Opens & RoomOpen.U) == 0)
                for (int col = 0; col < RoomW; col++)
                    Assert.AreEqual('W', t.Tiles[0, col], $"[{who}] 닫힌 위쪽 면 열 {col}");

            if ((t.Opens & RoomOpen.D) == 0)
                for (int col = 0; col < RoomW; col++)
                    Assert.AreEqual('W', t.Tiles[RoomH - 1, col], $"[{who}] 닫힌 아래쪽 면 열 {col}");
        }
    }

    // E/X는 생성기가 찍는다. 템플릿에 남아 있으면 맵에 입구가 둘이 된다.
    [Test]
    public void CaveRooms_ContainNoEntryOrExit()
    {
        var r = Load();
        foreach (var t in r.Templates)
            for (int row = 0; row < RoomH; row++)
                for (int col = 0; col < RoomW; col++)
                {
                    char o = t.Objects[row, col];
                    Assert.AreNotEqual('E', o, $"[{t.Describe()}] ({row},{col})");
                    Assert.AreNotEqual('X', o, $"[{t.Describe()}] ({row},{col})");
                }
    }

    // 'U'가 열린 방은 발판 사다리로 실제로 오를 수 있어야 한다.
    //
    // 기하: 플레이어는 바닥인 행 4에 서고, 수직 점프 도달은 2칸이다(행 R에서 행 R-2).
    //   행 3에 발판(W)이 있으면 행 4에서 그 옆으로 뛰어올라 발판 위(행 2)에 선다.
    //   행 1에 발판(W)이 있으면 행 2에서 뛰어올라 발판 위(행 0 = U 개구부)에 선다.
    // 발판은 개구부 열(4~6) 안에 있어야 그 통로로 이어진다.
    //
    // 예전 규칙은 "점프대('^')가 있는가"였는데, 점프대는 약 2.94칸까지만 올라
    // 바닥(행 4)에서 천장(행 0)까지 4칸을 절대 못 넘는다(측정치 출처: Assets/DungeonMaps/flame_shaft.txt).
    // 존재 검사라 테스트는 통과했지만 생성된 맵은 조용히 클리어 불가였다.
    [Test]
    public void CaveRooms_UpOpenRoomsAreClimbable()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));

        foreach (var t in r.Templates)
        {
            if ((t.Opens & RoomOpen.U) == 0) continue;

            string who = t.Describe();
            Assert.IsTrue(HasWallInOpeningColumns(t, 3),
                $"[{who}] 위가 열렸는데 행 3(열 4~6)에 발판이 없다 — 바닥 행 4에서 행 2로 못 오른다");
            Assert.IsTrue(HasWallInOpeningColumns(t, 1),
                $"[{who}] 위가 열렸는데 행 1(열 4~6)에 발판이 없다 — 행 2에서 행 0(U 개구부)으로 못 오른다");
        }
    }

    // 상하 개구부의 규약 열(4~6) 안에 확정 벽이 하나라도 있는가.
    private static bool HasWallInOpeningColumns(DungeonRoomTemplate t, int row)
    {
        for (int col = 4; col <= 6; col++)
            if (t.Tiles[row, col] == 'W') return true;
        return false;
    }

    // 템플릿 [OBJECTS]에 들어갈 수 있는 건 (1) 생성기가 해석하는 슬롯이거나 (2) 타일셋에 매핑된
    // 실제 오브젝트뿐이다. 그 외 심볼은 그대로 스탬프되어 임포터가 "미매핑 오브젝트 심볼" 경고를 쏟는다.
    [Test]
    public void CaveRooms_ObjectsAreKnownSlotOrMappedPrefab()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));
        var preset = LoadPreset();
        var tileset = LoadTileset();

        foreach (var t in r.Templates)
            for (int row = 0; row < RoomH; row++)
                for (int col = 0; col < RoomW; col++)
                {
                    char o = t.Objects[row, col];
                    if (o == '.') continue;

                    bool known = o == preset.RewardSlot
                              || preset.TryGetObjectSlot(o, out _)
                              || tileset.TryGetObject(o, out _);
                    Assert.IsTrue(known, $"[{t.Describe()}] ({row},{col})에 슬롯도 프리팹도 아닌 심볼 '{o}'");
                }
    }

    // 슬롯 심볼이 타일셋 오브젝트 심볼과 겹치면, 템플릿이 직접 놓은 그 오브젝트가 슬롯으로 잡혀
    // 굴림에 덮인다. 실제로 '^'(점프대)를 다트 슬롯으로 쓰다가 점프대가 통째로 사라진 적이 있다.
    [Test]
    public void Preset_SlotSymbolsDoNotCollideWithTilesetObjects()
    {
        var preset = LoadPreset();
        var tileset = LoadTileset();

        foreach (var slot in preset.objectSlots)
        {
            Assert.IsFalse(string.IsNullOrEmpty(slot.symbol), "슬롯 심볼이 비어 있습니다.");
            char s = slot.symbol[0];
            Assert.IsFalse(tileset.TryGetObject(s, out _),
                $"슬롯 심볼 '{s}'가 타일셋 오브젝트와 겹칩니다.");
        }

        Assert.IsFalse(tileset.TryGetObject(preset.RewardSlot, out _),
            $"보상 슬롯 심볼 '{preset.RewardSlot}'가 타일셋 오브젝트와 겹칩니다.");
    }

    // 슬롯 후보로 뽑히는 심볼은 전부 실제 프리팹이어야 한다. 오타면 임포트 때 조용히 경고만 남는다.
    [Test]
    public void Preset_SlotCandidatesAreMappedPrefabs()
    {
        var preset = LoadPreset();
        var tileset = LoadTileset();

        foreach (var slot in preset.objectSlots)
            foreach (var c in slot.candidates)
            {
                Assert.IsFalse(string.IsNullOrEmpty(c.symbol), $"슬롯 '{slot.symbol}'에 빈 후보");
                Assert.IsTrue(tileset.TryGetObject(c.symbol[0], out _),
                    $"슬롯 '{slot.symbol}'의 후보 '{c.symbol}'가 타일셋에 없습니다.");
            }

        Assert.IsTrue(tileset.TryGetObject(preset.Reward, out _),
            $"보상 심볼 '{preset.Reward}'가 타일셋에 없습니다.");
    }

    // 슬롯은 놓을 자리가 정해져 있다. 바닥 함정·보상은 바로 아래가 '확정 벽'이어야 하고
    // (확률 타일 위에 놓으면 굴림에 따라 발판이 사라져 공중에 뜬다), 다트는 붙을 벽이 있어야 한다.
    [Test]
    public void CaveRooms_SlotsSitOnValidSurfaces()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));

        foreach (var t in r.Templates)
            for (int row = 0; row < RoomH; row++)
                for (int col = 0; col < RoomW; col++)
                {
                    char o = t.Objects[row, col];
                    if (o == '.' || o == 'E' || o == 'X') continue;

                    string who = $"[{t.Describe()}] ({row},{col}) '{o}'";
                    Assert.AreEqual('.', t.Tiles[row, col], $"{who} 자리가 빈칸이 아님");

                    // 바닥에 놓이는 것들 — 위험 슬롯·보상·점프대는 받침이 있어야 공중에 뜨지 않는다.
                    if (o == '?' || o == '%' || o == '^')
                    {
                        Assert.Less(row + 1, RoomH, $"{who} 은 방 맨 아랫줄에 둘 수 없다 — 받침이 아래 방 지형에 달린다");
                        Assert.AreEqual('W', t.Tiles[row + 1, col], $"{who} 바로 아래가 확정 벽이 아님");
                    }
                    else if (o == 'D') // 왼쪽 벽에 붙는 다트 자리
                    {
                        Assert.Greater(col, 0, $"{who} 왼쪽에 벽이 없음");
                        Assert.AreEqual('W', t.Tiles[row, col - 1], $"{who} 왼쪽이 확정 벽이 아님");
                    }
                    else if (o == 'd') // 오른쪽 벽에 붙는 다트 자리
                    {
                        Assert.Less(col + 1, RoomW, $"{who} 오른쪽에 벽이 없음");
                        Assert.AreEqual('W', t.Tiles[row, col + 1], $"{who} 오른쪽이 확정 벽이 아님");
                    }
                }
    }
}
