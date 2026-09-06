# 던전 ASCII 맵 파이프라인 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 던전(Tilemap) 레이아웃과 게임 오브젝트 배치를 텍스트 맵으로 저작하고, 에디터 임포터가 이를 Tilemap 프리팹에 자동 스탬프하는 파이프라인 + 던전 전용 함정 6종을 만든다.

**Architecture:** 순수 C# 파서(`DungeonMapParser`)가 텍스트를 검증된 3-레이어 그리드로 변환 → `DungeonTilesetSO`(심볼→타일/프리팹 매핑) → 에디터 툴(`DungeonMapImporter`)이 `Tilemap.SetTile` + 프리팹 Instantiate + 링크 배선을 수행. 함정은 `TerrainChunk`에 의존하지 않는 독립 `Collider2D` 기반 MonoBehaviour로 신규 작성.

**Tech Stack:** Unity 2D, Unity Tilemap (`UnityEngine.Tilemaps`), C#, NUnit(EditMode 테스트), 단일 `GameScripts.asmdef`(런타임) + `GameScripts.Editor.asmdef`(에디터) + `EditModeTests.asmdef`(테스트).

## Global Constraints

- 버전 관리는 **UVCS**. `git` 명령을 사용하지 않는다. 각 Task 끝의 "Commit" 스텝은 **UVCS 체크인(사용자 수행)** 또는 생략으로 대체한다. (자동 git commit 금지)
- Unity 테스트 실행은 **사람이 직접** 한다. Claude는 테스트 파일 작성·수정만 하고 `mcp__mcp-unity__run_tests` 등 실행 도구를 호출하지 않는다. 각 Task의 "테스트 실행" 스텝은 사용자에게 넘긴다.
- 더티 플래그·콜라이더 파이프라인 등 청크 시스템 아키텍처 제약(CLAUDE.md)은 이 기능과 무관(던전은 별도 Tilemap). 단, 신규 함정은 **`TerrainChunk`/`ChunkData`에 의존하지 않는다**.
- 플레이어 데미지는 `PlayerStat.ApplyHazardDamage(float amount)`로만 준다(무적처리는 `StartInvincibility()`). 플레이어 태그 = `"Player"`.
- 파서·SO 룩업·함정 사이클 계산 등 **순수 로직은 GameScripts 런타임 어셈블리에 두고 EditMode 테스트로 TDD**. MonoBehaviour 씬 통합/임포터는 Unity 에디터에서 수동 검증한다.
- 좌표 규칙: 텍스트 `(row, col)` → Tilemap cell `new Vector3Int(col, -row, 0)`. 좌상단 = `(0,0)`, 아랫줄일수록 y 감소(위쪽이 천장).
- 심볼은 인스펙터 편의상 `string`으로 입력받아 첫 글자(`[0]`)를 사용한다.

## 파일 구조

**신규(런타임, `Assets/Scripts/Gameplay/Dungeon/Authoring/`)**
- `DungeonMapData.cs` — 파싱 결과 데이터 컨테이너 + 파스result.
- `DungeonMapParser.cs` — 텍스트 → `DungeonMapData` 파싱/검증(순수 C#).
- `DungeonTilesetSO.cs` — 심볼→TileBase / 심볼→프리팹 매핑 ScriptableObject.

**신규(런타임 함정, `Assets/Scripts/Gameplay/Dungeon/Traps/`)**
- `ILinkTarget.cs` — 압력판이 제어하는 대상 인터페이스.
- `TrapCycle.cs` — 개폐가시/압쇄블록 시간→상태 계산 순수 구조체.
- `PressurePlate.cs`, `DungeonGate.cs`, `RetractingSpike.cs`, `DartTrap.cs`, `DartProjectile.cs`, `CrusherBlock.cs`, `CollapsingPlatform.cs`.

**신규(에디터, `Assets/Scripts/Editor/Dungeon/`)**
- `DungeonMapImporter.cs` — 파싱→스탬프 에디터 윈도우/메뉴.

**신규(테스트, `Assets/Tests/EditMode/`)**
- `DungeonMapParserTests.cs`, `DungeonTilesetLookupTests.cs`, `TrapCycleTests.cs`.

**신규(콘텐츠)**
- `Assets/GameData/Dungeon/DefaultDungeonTileset.asset` (SO 인스턴스, 에디터에서 생성).
- `Assets/DungeonMaps/sample_cave.txt` (샘플 맵).

---

## Phase 1 — 코어 파이프라인 (기존 오브젝트만으로 즉시 사용 가능)

### Task 1: DungeonMapParser (순수 C# 파서 + 검증)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/DungeonMapData.cs`
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/DungeonMapParser.cs`
- Test: `Assets/Tests/EditMode/DungeonMapParserTests.cs`

**Interfaces:**
- Produces:
  - `namespace Gameplay.Dungeon.Authoring`
  - `class DungeonMapData { string Name; float CellSize; int Width; int Height; char[,] Tiles; char[,] Objects; char[,] Links; }` — 그리드는 `[row, col]`, 빈 칸 = `'.'`. Links가 없으면 전부 `'.'`.
  - `class DungeonMapParseResult { bool Success; DungeonMapData Data; List<string> Errors; List<string> Warnings; }`
  - `static class DungeonMapParser { static DungeonMapParseResult Parse(string text); }`

- [ ] **Step 1: 데이터 컨테이너 작성**

`DungeonMapData.cs`:
```csharp
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring
{
    /// <summary>파싱된 던전 맵 한 장. 그리드는 [row, col] 인덱싱, 빈 칸은 '.'.</summary>
    public class DungeonMapData
    {
        public string Name = "dungeon";
        public float CellSize = 1f;
        public int Width;
        public int Height;
        public char[,] Tiles;   // [Height, Width]
        public char[,] Objects; // [Height, Width]
        public char[,] Links;   // [Height, Width] — LINKS 미존재 시 전부 '.'
    }

    public class DungeonMapParseResult
    {
        public bool Success;
        public DungeonMapData Data;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }
}
```

- [ ] **Step 2: 실패 테스트 작성**

`DungeonMapParserTests.cs`:
```csharp
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
```

- [ ] **Step 3: 테스트 실패 확인 (사용자)**

Unity Test Runner(EditMode)에서 `DungeonMapParserTests` 실행 → 컴파일 에러/전부 실패 확인.

- [ ] **Step 4: 파서 구현**

`DungeonMapParser.cs`:
```csharp
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring
{
    /// <summary>
    /// 던전 텍스트 맵을 파싱한다. 형식:
    ///   # key: value   메타(주석). dungeon, cell 인식.
    ///   [TILES] / [OBJECTS] / [LINKS] 블록. 각 블록은 '.'=빈칸인 문자 격자.
    /// 규칙: 세 블록의 행 수(Height)·열 수(Width)가 같아야 한다(짧은 행은 '.' 패딩).
    /// TILES, OBJECTS 는 필수. LINKS 는 선택(없으면 전부 '.').
    /// </summary>
    public static class DungeonMapParser
    {
        private enum Section { None, Tiles, Objects, Links }

        public static DungeonMapParseResult Parse(string text)
        {
            var result = new DungeonMapParseResult();
            if (string.IsNullOrEmpty(text))
            {
                result.Errors.Add("빈 입력");
                return result;
            }

            string name = "dungeon";
            float cell = 1f;
            var tiles = new List<string>();
            var objects = new List<string>();
            var links = new List<string>();
            List<string> current = null;

            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw;
                if (line.StartsWith("#"))
                {
                    ParseMeta(line, ref name, ref cell);
                    continue;
                }
                string trimmed = line.Trim();
                if (trimmed == "[TILES]") { current = tiles; continue; }
                if (trimmed == "[OBJECTS]") { current = objects; continue; }
                if (trimmed == "[LINKS]") { current = links; continue; }
                if (trimmed.Length == 0) continue; // 블록 사이 빈 줄 무시
                if (current == null)
                {
                    result.Errors.Add($"섹션 헤더 전에 내용이 나옴: '{line}'");
                    return result;
                }
                current.Add(line.TrimEnd());
            }

            if (tiles.Count == 0) { result.Errors.Add("[TILES] 섹션이 없거나 비어있음"); }
            if (objects.Count == 0) { result.Errors.Add("[OBJECTS] 섹션이 없거나 비어있음"); }
            if (result.Errors.Count > 0) return result;

            int height = tiles.Count;
            int width = MaxWidth(tiles);

            if (objects.Count != height)
            {
                result.Errors.Add($"[OBJECTS] 행 수({objects.Count})가 [TILES]({height})와 다름");
                return result;
            }
            if (MaxWidth(objects) != width)
            {
                result.Errors.Add($"[OBJECTS] 열 수({MaxWidth(objects)})가 [TILES]({width})와 다름");
                return result;
            }
            bool hasLinks = links.Count > 0;
            if (hasLinks && (links.Count != height || MaxWidth(links) != width))
            {
                result.Errors.Add($"[LINKS] 크기가 [TILES]({width}x{height})와 다름");
                return result;
            }

            var data = new DungeonMapData
            {
                Name = name, CellSize = cell, Width = width, Height = height,
                Tiles = ToGrid(tiles, height, width),
                Objects = ToGrid(objects, height, width),
                Links = hasLinks ? ToGrid(links, height, width) : FilledDots(height, width),
            };
            result.Data = data;
            result.Success = true;
            return result;
        }

        private static void ParseMeta(string line, ref string name, ref float cell)
        {
            // "# dungeon: cave_01" / "# cell: 1"
            string body = line.TrimStart('#').Trim();
            int colon = body.IndexOf(':');
            if (colon < 0) return;
            string key = body.Substring(0, colon).Trim().ToLowerInvariant();
            string val = body.Substring(colon + 1).Trim();
            if (key == "dungeon") name = val;
            else if (key == "cell" && float.TryParse(val, out float c)) cell = c;
        }

        private static int MaxWidth(List<string> rows)
        {
            int w = 0;
            foreach (var r in rows) if (r.Length > w) w = r.Length;
            return w;
        }

        private static char[,] ToGrid(List<string> rows, int height, int width)
        {
            var g = new char[height, width];
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                    g[r, c] = c < rows[r].Length ? rows[r][c] : '.';
            return g;
        }

        private static char[,] FilledDots(int height, int width)
        {
            var g = new char[height, width];
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                    g[r, c] = '.';
            return g;
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인 (사용자)**

Unity Test Runner(EditMode)에서 `DungeonMapParserTests` 전부 PASS 확인.

- [ ] **Step 6: 체크인**

UVCS로 `DungeonMapData.cs`, `DungeonMapParser.cs`, `DungeonMapParserTests.cs` 체크인(사용자).

---

### Task 2: DungeonTilesetSO (심볼→타일/프리팹 매핑)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/DungeonTilesetSO.cs`
- Test: `Assets/Tests/EditMode/DungeonTilesetLookupTests.cs`

**Interfaces:**
- Consumes: 없음.
- Produces:
  - `class DungeonTilesetSO : ScriptableObject`
  - `bool TryGetTile(char symbol, out TileBase tile)`
  - `bool TryGetObject(char symbol, out DungeonTilesetSO.ObjectEntry entry)`
  - `struct ObjectEntry { GameObject prefab; float zRotation; }`
  - `void BuildLookup()` (딕셔너리 재구성; 에디터/테스트에서 명시 호출)

- [ ] **Step 1: 실패 테스트 작성**

`DungeonTilesetLookupTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;

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
```

- [ ] **Step 2: 테스트 실패 확인 (사용자)** — 컴파일 에러/실패.

- [ ] **Step 3: SO 구현**

`DungeonTilesetSO.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Gameplay.Dungeon.Authoring
{
    /// <summary>던전 맵 심볼 → 타일/프리팹 매핑 테이블. 코드 수정 없이 인스펙터에서 확장.</summary>
    [CreateAssetMenu(menuName = "Dungeon/Dungeon Tileset", fileName = "DungeonTileset")]
    public class DungeonTilesetSO : ScriptableObject
    {
        [Serializable]
        public class TileMapping { public string symbol; public TileBase tile; }

        [Serializable]
        public class ObjectMapping { public string symbol; public GameObject prefab; public float zRotation; }

        public struct ObjectEntry { public GameObject prefab; public float zRotation; }

        public List<TileMapping> tileMappings = new List<TileMapping>();
        public List<ObjectMapping> objectMappings = new List<ObjectMapping>();

        private Dictionary<char, TileBase> _tiles;
        private Dictionary<char, ObjectEntry> _objects;

        private void OnEnable() => BuildLookup();

        public void BuildLookup()
        {
            _tiles = new Dictionary<char, TileBase>();
            _objects = new Dictionary<char, ObjectEntry>();
            foreach (var m in tileMappings)
            {
                if (string.IsNullOrEmpty(m.symbol) || m.tile == null) continue;
                _tiles[m.symbol[0]] = m.tile;
            }
            foreach (var m in objectMappings)
            {
                if (string.IsNullOrEmpty(m.symbol) || m.prefab == null) continue;
                _objects[m.symbol[0]] = new ObjectEntry { prefab = m.prefab, zRotation = m.zRotation };
            }
        }

        public bool TryGetTile(char symbol, out TileBase tile)
        {
            if (_tiles == null) BuildLookup();
            return _tiles.TryGetValue(symbol, out tile);
        }

        public bool TryGetObject(char symbol, out ObjectEntry entry)
        {
            if (_objects == null) BuildLookup();
            return _objects.TryGetValue(symbol, out entry);
        }
    }
}
```
> 테스트가 `so.objectMappings.Add(...)`와 `DungeonTilesetSO.ObjectMapping`을 쓰므로 클래스는 `public`, 필드도 `public`으로 유지한다. 테스트에서 `using Gameplay.Dungeon.Authoring;`를 추가해야 하면 추가.

- [ ] **Step 4: 테스트 네임스페이스 보정 후 통과 확인 (사용자)**

`DungeonTilesetLookupTests.cs` 상단에 `using Gameplay.Dungeon.Authoring;` 추가. EditMode 테스트 PASS 확인.

- [ ] **Step 5: 체크인** — SO + 테스트(사용자).

---

### Task 3: DungeonMapImporter (에디터 스탬프 툴 — 타일/오브젝트/ID)

**Files:**
- Create: `Assets/Scripts/Editor/Dungeon/DungeonMapImporter.cs`

**Interfaces:**
- Consumes: `DungeonMapParser.Parse`, `DungeonTilesetSO`, `DungeonMapData`.
- Produces: 에디터 윈도우 `Tools > Dungeon > Map Importer`. 대상 `Tilemap`·`Transform objectRoot`·`DungeonTilesetSO`·`TextAsset map`을 받아 스탬프.
- 중요: `DungeonRewardPickup.rewardId`(string, private)·`DungeonRock.rockId`(int, private)는 인스턴스별 유일해야 하므로 `SerializedObject`로 자동 부여.

- [ ] **Step 1: 임포터 윈도우 구현**

`DungeonMapImporter.cs`:
```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Gameplay.Dungeon.Authoring;

/// <summary>
/// 던전 텍스트 맵을 대상 Tilemap + objectRoot에 스탬프하는 에디터 툴.
/// 좌표: 텍스트(row,col) → tilemap cell (col, -row). 오브젝트는 셀 중심 월드좌표에 Instantiate.
/// reward/rock id는 인스턴스별 유일값 자동 부여.
/// </summary>
public class DungeonMapImporter : EditorWindow
{
    private Tilemap _tilemap;
    private Transform _objectRoot;
    private DungeonTilesetSO _tileset;
    private TextAsset _mapAsset;
    private bool _clearFirst = true;

    [MenuItem("Tools/Dungeon/Map Importer")]
    public static void Open() => GetWindow<DungeonMapImporter>("Dungeon Map Importer");

    private void OnGUI()
    {
        _tilemap = (Tilemap)EditorGUILayout.ObjectField("Target Tilemap", _tilemap, typeof(Tilemap), true);
        _objectRoot = (Transform)EditorGUILayout.ObjectField("Object Root", _objectRoot, typeof(Transform), true);
        _tileset = (DungeonTilesetSO)EditorGUILayout.ObjectField("Tileset", _tileset, typeof(DungeonTilesetSO), false);
        _mapAsset = (TextAsset)EditorGUILayout.ObjectField("Map (.txt)", _mapAsset, typeof(TextAsset), false);
        _clearFirst = EditorGUILayout.Toggle("Clear Before Import", _clearFirst);

        using (new EditorGUI.DisabledScope(_tilemap == null || _tileset == null || _mapAsset == null || _objectRoot == null))
            if (GUILayout.Button("Import"))
                Import();
    }

    private void Import()
    {
        var result = DungeonMapParser.Parse(_mapAsset.text);
        if (!result.Success)
        {
            EditorUtility.DisplayDialog("Import 실패", string.Join("\n", result.Errors), "확인");
            return;
        }
        _tileset.BuildLookup();
        var data = result.Data;

        Undo.RegisterFullObjectHierarchyUndo(_tilemap.gameObject, "Import Dungeon Tiles");
        if (_clearFirst) _tilemap.ClearAllTiles();

        int entryCount = 0;
        int rockAuto = 0, rewardAuto = 0;

        for (int row = 0; row < data.Height; row++)
        for (int col = 0; col < data.Width; col++)
        {
            var cellPos = new Vector3Int(col, -row, 0);

            char t = data.Tiles[row, col];
            if (t != '.' && _tileset.TryGetTile(t, out var tile))
                _tilemap.SetTile(cellPos, tile);
            else if (t != '.')
                Debug.LogWarning($"[DungeonImport] 미매핑 타일 심볼 '{t}' @({row},{col})");

            char o = data.Objects[row, col];
            if (o == '.') continue;
            if (!_tileset.TryGetObject(o, out var entry))
            {
                Debug.LogWarning($"[DungeonImport] 미매핑 오브젝트 심볼 '{o}' @({row},{col})");
                continue;
            }

            Vector3 world = _tilemap.GetCellCenterWorld(cellPos);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, _objectRoot);
            Undo.RegisterCreatedObjectUndo(go, "Import Dungeon Object");
            go.transform.position = world;
            go.transform.rotation = Quaternion.Euler(0, 0, entry.zRotation);

            if (o == 'E') entryCount++;
            AssignUniqueId(go, ref rockAuto, ref rewardAuto);
        }

        if (entryCount != 1)
            Debug.LogWarning($"[DungeonImport] DungeonEntryPoint(E)가 정확히 1개여야 하는데 {entryCount}개입니다.");

        Debug.Log($"[DungeonImport] 완료: {data.Name} ({data.Width}x{data.Height})");
        EditorSceneMarkDirty();
    }

    // reward/rock의 private 유일 id 자동 부여.
    private static void AssignUniqueId(GameObject go, ref int rockAuto, ref int rewardAuto)
    {
        var rock = go.GetComponent<DungeonRock>();
        if (rock != null)
        {
            var so = new SerializedObject(rock);
            so.FindProperty("rockId").intValue = rockAuto++;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        var reward = go.GetComponent<DungeonRewardPickup>();
        if (reward != null)
        {
            var so = new SerializedObject(reward);
            so.FindProperty("rewardId").stringValue = $"reward_{rewardAuto++}";
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void EditorSceneMarkDirty()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
```
> `DungeonMapImporter`는 `GameScripts.Editor.asmdef` 대상 폴더(`Assets/Scripts/Editor/`)에 있어야 하며, 해당 asmdef가 `GameScripts`를 참조해야 `DungeonRock`/`DungeonRewardPickup`/파서에 접근 가능하다. 참조가 없으면 asmdef에 추가.

- [ ] **Step 2: 컴파일 확인 (사용자)**

Unity로 전환해 컴파일 에러 없는지 확인. `Tools > Dungeon > Map Importer` 메뉴가 뜨는지 확인.

- [ ] **Step 3: 체크인** — 임포터(사용자).

---

### Task 4: 샘플 타일셋 에셋 + 샘플 맵 + 실제 임포트 검증

**Files:**
- Create(에디터에서): `Assets/GameData/Dungeon/DefaultDungeonTileset.asset`
- Create: `Assets/DungeonMaps/sample_cave.txt`

**Interfaces:** Consumes Task 1~3 산출물.

- [ ] **Step 1: 샘플 맵 텍스트 작성**

`Assets/DungeonMaps/sample_cave.txt`:
```
# dungeon: sample_cave
# cell: 1
[TILES]
WWWWWWWWWWWWWWWW
W..............W
W..WWWW........W
W.............rW
W......WWWW....W
W..............W
WWWWWWWWWWWWWWWW
[OBJECTS]
................
..E.........$..X
...............
...............
...............
...............
................
```
> 위 맵은 기존 오브젝트만 사용(E=입구, $=보상, X=출구, r=DungeonRock). 신규 함정은 Phase 2 이후 추가.

- [ ] **Step 2: 타일셋 SO 생성·매핑 (사용자, Unity)**

`Assets/GameData/Dungeon/`에서 우클릭 → Create → Dungeon → Dungeon Tileset. 이름 `DefaultDungeonTileset`.
tileMappings: `W` → `Wall`(`Assets/Sprites/World/SpeciaChunk/LegacyTiles/Wall.asset`), 필요 시 `R`/`D`/`I`/`M` 등 추가.
objectMappings: `E`→DungeonEntryPoint 프리팹(없으면 §Note 참고), `X`→DungeonExitInteractable 프리팹, `$`→DungeonRewardPickup 프리팹, `r`→DungeonRock 프리팹.
> Note: 현재 프로젝트에 개별 프리팹이 없으면 각 오브젝트를 프리팹화해서 매핑한다.

- [ ] **Step 3: 임포트 실행·검증 (사용자, Unity)**

던전 프리팹(또는 임시 씬)에 `Grid + Tilemap`과 빈 `ObjectRoot`를 두고, Map Importer로 `sample_cave.txt` 임포트 → 벽 타일이 찍히고 입구/보상/출구/바위가 올바른 셀에 배치되는지, reward/rock id가 유일하게 부여됐는지(인스펙터) 확인.

- [ ] **Step 4: 체크인** — 샘플 맵 + SO 에셋(사용자).

**→ Phase 1 완료 시점: 텍스트로 던전 레이아웃+기본 오브젝트 배치가 동작한다. 이후 Phase 2는 선택적으로 진행 가능.**

---

## Phase 2 — 던전 전용 함정 6종 + 링크 배선

### Task 5: 링크 인터페이스 + 함정 사이클 순수 로직 (TDD)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Traps/ILinkTarget.cs`
- Create: `Assets/Scripts/Gameplay/Dungeon/Traps/TrapCycle.cs`
- Test: `Assets/Tests/EditMode/TrapCycleTests.cs`

**Interfaces:**
- Produces:
  - `namespace Gameplay.Dungeon.Traps`
  - `interface ILinkTarget { void Activate(); void Deactivate(); }`
  - `struct TrapCycle { float period; float activeFraction; bool IsActive(float time); }` — 주기 `period` 중 앞 `activeFraction` 비율 동안 활성(개폐가시 튀어나옴/다트 발사창).
  - `struct PingPong01 { float period; float Value(float time); }` — 0→1→0 왕복(압쇄블록 위치 보간용).

- [ ] **Step 1: 실패 테스트 작성**

`TrapCycleTests.cs`:
```csharp
using NUnit.Framework;
using Gameplay.Dungeon.Traps;

public class TrapCycleTests
{
    [Test]
    public void TrapCycle_ActiveInFirstFraction()
    {
        var c = new TrapCycle { period = 2f, activeFraction = 0.5f };
        Assert.IsTrue(c.IsActive(0f));     // 0.0 구간
        Assert.IsTrue(c.IsActive(0.9f));   // 여전히 활성 구간(<1.0)
        Assert.IsFalse(c.IsActive(1.5f));  // 비활성 구간
        Assert.IsTrue(c.IsActive(2.0f));   // 다음 주기 시작 → 활성
    }

    [Test]
    public void PingPong_EndsMatchStart()
    {
        var p = new PingPong01 { period = 4f };
        Assert.AreEqual(0f, p.Value(0f), 1e-4f);
        Assert.AreEqual(1f, p.Value(2f), 1e-4f); // 절반에서 최대
        Assert.AreEqual(0f, p.Value(4f), 1e-4f); // 주기 끝 = 시작
    }
}
```

- [ ] **Step 2: 테스트 실패 확인 (사용자)**

- [ ] **Step 3: 구현**

`ILinkTarget.cs`:
```csharp
namespace Gameplay.Dungeon.Traps
{
    /// <summary>압력판(PressurePlate)이 제어하는 대상(문/다트/가시).</summary>
    public interface ILinkTarget
    {
        void Activate();
        void Deactivate();
    }
}
```

`TrapCycle.cs`:
```csharp
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>period 주기 중 앞 activeFraction 비율 동안 활성.</summary>
    public struct TrapCycle
    {
        public float period;
        public float activeFraction;

        public bool IsActive(float time)
        {
            if (period <= 0f) return true;
            float phase = Mathf.Repeat(time, period) / period; // [0,1)
            return phase < activeFraction;
        }
    }

    /// <summary>period 동안 0→1→0 삼각 왕복.</summary>
    public struct PingPong01
    {
        public float period;

        public float Value(float time)
        {
            if (period <= 0f) return 0f;
            return Mathf.PingPong(time / (period * 0.5f), 1f);
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인 (사용자)**

- [ ] **Step 5: 체크인** — 인터페이스 + 사이클 + 테스트(사용자).

---

### Task 6: PressurePlate + DungeonGate (링크 소스/타겟)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Traps/PressurePlate.cs`
- Create: `Assets/Scripts/Gameplay/Dungeon/Traps/DungeonGate.cs`

**Interfaces:**
- Consumes: `ILinkTarget`.
- Produces:
  - `class PressurePlate : MonoBehaviour { List<MonoBehaviour> targets; }` — 인스펙터에 `ILinkTarget` 구현 컴포넌트 목록(임포터가 배선). 플레이어 접촉 시 전 타겟 `Activate`, 이탈 시 `Deactivate`.
  - `class DungeonGate : MonoBehaviour, ILinkTarget` — Activate=열림(콜라이더 off + 렌더러 off/애니메이션), Deactivate=닫힘.

- [ ] **Step 1: DungeonGate 구현**

`DungeonGate.cs`:
```csharp
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>압력판에 연동되는 문. Activate=열림(통과 가능), Deactivate=닫힘.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class DungeonGate : MonoBehaviour, ILinkTarget
    {
        [SerializeField] private bool startOpen = false;
        private Collider2D _col;
        private SpriteRenderer[] _renderers;

        private void Awake()
        {
            _col = GetComponent<Collider2D>();
            _renderers = GetComponentsInChildren<SpriteRenderer>();
            SetOpen(startOpen);
        }

        public void Activate() => SetOpen(true);
        public void Deactivate() => SetOpen(false);

        private void SetOpen(bool open)
        {
            if (_col != null) _col.enabled = !open;
            foreach (var r in _renderers) r.enabled = !open;
        }
    }
}
```

- [ ] **Step 2: PressurePlate 구현**

`PressurePlate.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>플레이어가 밟으면 연동 타겟을 Activate, 이탈 시 Deactivate. targets는 임포터가 배선.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class PressurePlate : MonoBehaviour
    {
        [Tooltip("ILinkTarget 구현 컴포넌트(문/다트/가시). 임포터가 링크 그룹으로 채운다.")]
        public List<MonoBehaviour> targets = new List<MonoBehaviour>();
        [SerializeField] private string playerTag = "Player";

        private int _contacts;

        private void Reset()
        {
            var c = GetComponent<Collider2D>();
            if (c != null) c.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(playerTag)) return;
            _contacts++;
            if (_contacts == 1) foreach (var t in targets) (t as ILinkTarget)?.Activate();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!other.CompareTag(playerTag)) return;
            _contacts = Mathf.Max(0, _contacts - 1);
            if (_contacts == 0) foreach (var t in targets) (t as ILinkTarget)?.Deactivate();
        }
    }
}
```

- [ ] **Step 3: 컴파일 확인 (사용자)** — Unity 컴파일 에러 없음.

- [ ] **Step 4: 체크인** — plate + gate(사용자).

---

### Task 7: RetractingSpike (개폐식 가시 — 주기 + 링크)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Traps/RetractingSpike.cs`

**Interfaces:**
- Consumes: `TrapCycle`, `ILinkTarget`, `PlayerStat.ApplyHazardDamage`.
- Produces: `class RetractingSpike : MonoBehaviour, ILinkTarget` — `linkControlled=false`면 `TrapCycle`로 자동 개폐, true면 압력판 Activate 동안만 돌출. 돌출 상태에서 플레이어 접촉 시 데미지.

- [ ] **Step 1: 구현**

`RetractingSpike.cs`:
```csharp
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>바닥에서 주기적으로 튀어나오는 가시. 돌출 중 접촉 시 데미지.
    /// linkControlled=true면 주기 무시하고 압력판 Activate 동안만 돌출.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class RetractingSpike : MonoBehaviour, ILinkTarget
    {
        [SerializeField] private float period = 2f;
        [SerializeField] private float activeFraction = 0.4f;
        [SerializeField] private float damage = 20f;
        [SerializeField] private bool linkControlled = false;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private Transform visual; // 돌출/수납 시 보이기용(없어도 됨)

        private Collider2D _col;
        private bool _extended;
        private bool _linkActive;
        private TrapCycle _cycle;

        private void Awake()
        {
            _col = GetComponent<Collider2D>();
            _cycle = new TrapCycle { period = period, activeFraction = activeFraction };
        }

        public void Activate() => _linkActive = true;
        public void Deactivate() => _linkActive = false;

        private void Update()
        {
            bool extend = linkControlled ? _linkActive : _cycle.IsActive(Time.time);
            if (extend != _extended) SetExtended(extend);
        }

        private void SetExtended(bool on)
        {
            _extended = on;
            _col.enabled = on;
            if (visual != null) visual.gameObject.SetActive(on);
        }

        private void OnTriggerEnter2D(Collider2D other) => TryDamage(other);
        private void OnTriggerStay2D(Collider2D other) => TryDamage(other);

        private void TryDamage(Collider2D other)
        {
            if (!_extended || !other.CompareTag(playerTag)) return;
            var stat = other.GetComponent<PlayerStat>();
            if (stat == null) return;
            stat.ApplyHazardDamage(damage);
            stat.StartInvincibility();
        }
    }
}
```
> `ApplyHazardDamage` 내부에 무적/쿨다운이 이미 있으면 `StartInvincibility()` 중복 호출은 무해(짧게 재설정). 실제 동작은 Task 11 통합 검증에서 확인.

- [ ] **Step 2: 컴파일 확인 (사용자)**

- [ ] **Step 3: 체크인** (사용자).

---

### Task 8: DartTrap + DartProjectile (다트 발사기)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Traps/DartProjectile.cs`
- Create: `Assets/Scripts/Gameplay/Dungeon/Traps/DartTrap.cs`

**Interfaces:**
- Consumes: `TrapCycle`, `ILinkTarget`, `PlayerStat.ApplyHazardDamage`.
- Produces:
  - `class DartProjectile : MonoBehaviour { void Launch(Vector2 dir, float speed, float damage, float life); }`
  - `class DartTrap : MonoBehaviour, ILinkTarget` — `linkControlled=false`면 주기 발사, true면 Activate 시 1발. `zRotation`(임포터가 세팅) 기준 로컬 +X 방향으로 발사.

- [ ] **Step 1: DartProjectile 구현**

`DartProjectile.cs`:
```csharp
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>직선 투사체. 플레이어 접촉 시 데미지, 수명 후 소멸.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class DartProjectile : MonoBehaviour
    {
        private Vector2 _dir;
        private float _speed, _damage, _life, _age;
        private string _playerTag = "Player";

        public void Launch(Vector2 dir, float speed, float damage, float life)
        {
            _dir = dir.normalized; _speed = speed; _damage = damage; _life = life;
            var c = GetComponent<Collider2D>(); if (c != null) c.isTrigger = true;
        }

        private void Update()
        {
            transform.position += (Vector3)(_dir * (_speed * Time.deltaTime));
            _age += Time.deltaTime;
            if (_age >= _life) Destroy(gameObject);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.CompareTag(_playerTag))
            {
                var stat = other.GetComponent<PlayerStat>();
                if (stat != null) { stat.ApplyHazardDamage(_damage); stat.StartInvincibility(); }
                Destroy(gameObject);
            }
        }
    }
}
```

- [ ] **Step 2: DartTrap 구현**

`DartTrap.cs`:
```csharp
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>주기적으로(또는 압력판 Activate 시) 로컬 +X 방향 다트를 발사.</summary>
    public class DartTrap : MonoBehaviour, ILinkTarget
    {
        [SerializeField] private DartProjectile projectilePrefab;
        [SerializeField] private float period = 1.5f;
        [SerializeField] private float speed = 6f;
        [SerializeField] private float damage = 15f;
        [SerializeField] private float projectileLife = 4f;
        [SerializeField] private bool linkControlled = false;

        private float _timer;

        private void Update()
        {
            if (linkControlled) return;
            _timer += Time.deltaTime;
            if (_timer >= period) { _timer = 0f; Fire(); }
        }

        public void Activate() => Fire();
        public void Deactivate() { }

        private void Fire()
        {
            if (projectilePrefab == null) return;
            var dart = Instantiate(projectilePrefab, transform.position, transform.rotation);
            Vector2 dir = transform.right; // zRotation 반영된 로컬 +X
            dart.Launch(dir, speed, damage, projectileLife);
        }
    }
}
```

- [ ] **Step 3: 컴파일 확인 (사용자)**

- [ ] **Step 4: 체크인** (사용자).

---

### Task 9: CrusherBlock (압쇄 블록)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Traps/CrusherBlock.cs`

**Interfaces:**
- Consumes: `PingPong01`, `PlayerStat.ApplyHazardDamage`.
- Produces: `class CrusherBlock : MonoBehaviour` — 시작 위치와 `travel`(로컬 오프셋) 사이를 `period`로 왕복. 접촉 시 데미지.

- [ ] **Step 1: 구현**

`CrusherBlock.cs`:
```csharp
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>두 지점 사이를 왕복하는 압쇄 블록. 접촉 시 데미지.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class CrusherBlock : MonoBehaviour
    {
        [SerializeField] private Vector2 travel = new Vector2(0f, -3f); // 로컬 이동량
        [SerializeField] private float period = 2f;
        [SerializeField] private float damage = 40f;
        [SerializeField] private string playerTag = "Player";

        private Vector3 _start;
        private PingPong01 _pp;

        private void Awake()
        {
            _start = transform.position;
            _pp = new PingPong01 { period = period };
        }

        private void Update()
        {
            float t = _pp.Value(Time.time);
            transform.position = _start + (Vector3)(travel * t);
        }

        private void OnCollisionEnter2D(Collision2D c) => TryDamage(c.collider);
        private void OnTriggerEnter2D(Collider2D other) => TryDamage(other);

        private void TryDamage(Collider2D other)
        {
            if (!other.CompareTag(playerTag)) return;
            var stat = other.GetComponent<PlayerStat>();
            if (stat != null) { stat.ApplyHazardDamage(damage); stat.StartInvincibility(); }
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사용자)**

- [ ] **Step 3: 체크인** (사용자).

---

### Task 10: CollapsingPlatform (무너지는 발판)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Traps/CollapsingPlatform.cs`

**Interfaces:**
- Consumes: 없음(플레이어 접촉만).
- Produces: `class CollapsingPlatform : MonoBehaviour` — 플레이어가 위에 서면 `collapseDelay` 뒤 콜라이더/렌더러 off(낙하 느낌), `respawnDelay` 뒤 복구.

- [ ] **Step 1: 구현**

`CollapsingPlatform.cs`:
```csharp
using System.Collections;
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>밟으면 잠깐 뒤 사라졌다가 복구되는 발판.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class CollapsingPlatform : MonoBehaviour
    {
        [SerializeField] private float collapseDelay = 0.3f;
        [SerializeField] private float respawnDelay = 2f;
        [SerializeField] private string playerTag = "Player";

        private Collider2D _col;
        private SpriteRenderer _sr;
        private bool _triggered;

        private void Awake()
        {
            _col = GetComponent<Collider2D>();
            _sr = GetComponent<SpriteRenderer>();
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (_triggered || !c.collider.CompareTag(playerTag)) return;
            _triggered = true;
            StartCoroutine(CollapseRoutine());
        }

        private IEnumerator CollapseRoutine()
        {
            yield return new WaitForSeconds(collapseDelay);
            SetVisible(false);
            yield return new WaitForSeconds(respawnDelay);
            SetVisible(true);
            _triggered = false;
        }

        private void SetVisible(bool on)
        {
            if (_col != null) _col.enabled = on;
            if (_sr != null) _sr.enabled = on;
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사용자)**

- [ ] **Step 3: 체크인** (사용자).

---

### Task 11: 임포터 링크 배선 + 함정 심볼 매핑 + 통합 검증

**Files:**
- Modify: `Assets/Scripts/Editor/Dungeon/DungeonMapImporter.cs`

**Interfaces:**
- Consumes: `DungeonMapData.Links`, `ILinkTarget`, `PressurePlate`.
- Produces: 임포트 시 `[LINKS]` 그룹별로 소스(`PressurePlate`)의 `targets`에 같은 그룹 `ILinkTarget` 컴포넌트를 배선.

- [ ] **Step 1: 임포터에 링크 배선 추가**

`DungeonMapImporter.Import()`에서 오브젝트 배치 루프 중, 생성한 `GameObject`와 그 셀의 `data.Links[row,col]`을 함께 수집한 뒤 배치 루프 종료 후 배선한다. 아래 구조를 추가:

```csharp
// 필드 (Import 지역 변수):
// var placed = new List<(char linkId, GameObject go)>();

// 오브젝트 생성 직후:
char linkId = data.Links[row, col];
placed.Add((linkId, go));

// 배치 루프 종료 후:
WireLinks(placed);
```

그리고 메서드 추가:
```csharp
using System.Collections.Generic;
using System.Linq;
using Gameplay.Dungeon.Traps;

private static void WireLinks(List<(char linkId, GameObject go)> placed)
{
    // 그룹별로 소스(PressurePlate)와 타겟(ILinkTarget)을 나눠 배선.
    foreach (var group in placed.Where(p => p.linkId != '.').GroupBy(p => p.linkId))
    {
        var plates = group.Select(p => p.go.GetComponent<PressurePlate>()).Where(x => x != null).ToList();
        var targets = group.SelectMany(p => p.go.GetComponents<MonoBehaviour>())
                           .Where(m => m is ILinkTarget).Cast<MonoBehaviour>().ToList();
        if (plates.Count == 0) { Debug.LogWarning($"[DungeonImport] 링크 그룹 '{group.Key}'에 압력판(PressurePlate) 없음"); continue; }
        if (targets.Count == 0) { Debug.LogWarning($"[DungeonImport] 링크 그룹 '{group.Key}'에 타겟(ILinkTarget) 없음"); continue; }
        foreach (var plate in plates)
        {
            plate.targets = targets;
            EditorUtility.SetDirty(plate);
        }
    }
}
```
> `DungeonMapImporter` 상단 using에 `System.Collections.Generic`, `System.Linq`, `Gameplay.Dungeon.Traps` 추가. 압력판 자신이 타겟 리스트에 섞이지 않도록, PressurePlate는 `ILinkTarget`을 구현하지 않으므로 자동 제외된다(설계상 plate=소스, gate/spike/dart=타겟).

- [ ] **Step 2: 타일셋에 함정 심볼 매핑 추가 (사용자, Unity)**

`DefaultDungeonTileset`의 objectMappings에 함정 프리팹 추가. 심볼(주석 `#`·빈칸 `.`과 비충돌):
`_`→CollapsingPlatform, `!`→RetractingSpike, `>`→DartTrap(발사 방향은 매핑의 `zRotation`으로), `C`→CrusherBlock, `P`→PressurePlate, `G`→DungeonGate.
> 설계 §9의 "압쇄 블록 심볼은 `#` 대신 `C`" 결정을 여기서 확정 적용.

- [ ] **Step 3: 링크 포함 샘플 맵 작성**

`Assets/DungeonMaps/sample_traps.txt`:
```
# dungeon: sample_traps
# cell: 1
[TILES]
WWWWWWWWWWWW
W..........W
W..........W
W..........W
WWWW....WWWW
[OBJECTS]
............
.E...C....X.
.....!......
.P........G.
WWWW_...WWWW
[LINKS]
............
............
............
.1........1.
............
```
> `C`(압쇄), `!`(개폐가시), `P`/`G`(링크 그룹 1), `_`(무너지는 발판, TILES의 바닥줄 위 OBJECTS). TILES 바닥줄 `WWWW....WWWW`의 가운데 빈 곳에 발판 배치.

- [ ] **Step 4: 통합 검증 (사용자, Unity)**

`sample_traps.txt` 임포트 → 압력판 `P`를 밟으면 그룹 1의 문 `G`가 열리는지, 개폐가시 주기 동작, 압쇄 블록 왕복, 다트 발사, 무너지는 발판 붕괴/복구, 각 함정 접촉 시 `ApplyHazardDamage` 적용을 플레이 모드에서 확인.

- [ ] **Step 5: 체크인** — 임포터 수정 + 샘플 맵(사용자).

---

## Self-Review (작성자 점검 결과)

- **Spec coverage:** §2 파이프라인=Task1~4, §3 포맷=Task1, §4 매핑=Task2/4/11, §4 함정 6종=Task5~10, §5 링크=Task5/6/11, §6 임포터 동작=Task3/11, §7 검증=Task1(파서)+임포터 경고. 모든 spec 섹션에 대응 Task 존재.
- **Placeholder scan:** "적절한 에러처리" 류 없음. 모든 코드 스텝에 실제 코드 포함.
- **Type consistency:** `DungeonTilesetSO.TryGetTile/TryGetObject/ObjectEntry/ObjectMapping`, `TrapCycle.IsActive`, `PingPong01.Value`, `ILinkTarget.Activate/Deactivate`, `PressurePlate.targets`, `DungeonMapData.{Tiles,Objects,Links,Width,Height,Name,CellSize}` — Task 간 명칭 일치 확인.
- **알려진 통합 리스크(구현 중 확인):** ① `PlayerStat.ApplyHazardDamage`의 내부 무적/쿨다운 여부에 따라 `StartInvincibility()` 중복 호출 조정. ② 프로젝트에 던전 오브젝트 개별 프리팹이 없으면 프리팹화 필요(Task4 Step2). ③ `GameScripts.Editor.asmdef`가 `GameScripts` 참조하는지 확인(Task3 Note). ④ 청크용 콜라이더(플랫폼) 물리 레이어와 던전 플레이어 충돌 레이어 정합성은 씬 세팅에서 확인.
