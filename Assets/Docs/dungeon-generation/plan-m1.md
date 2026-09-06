# 던전 절차적 생성 M1 구현 계획

> **에이전트 작업자용:** 이 계획은 `superpowers:subagent-driven-development` 또는 `superpowers:executing-plans`로 태스크 단위 실행한다. 스텝은 체크박스(`- [ ]`)로 추적한다.

**목표:** 방 템플릿 txt를 격자로 조립해 가로형 던전 맵을 생성하고, 에디터 창에서 시드를 바꿔가며 ASCII로 훑어본 뒤 씬에 스탬프하는 루프를 완성한다.

**아키텍처:** 순수 C# 생성기(`Gameplay.Dungeon.Authoring.Generation`)가 프리셋+시드를 받아 기존 `DungeonMapData`를 만든다. 에디터 창은 생성기를 호출만 하고, 출력 txt는 기존 `DungeonMapParser`/`DungeonMapImporter`가 그대로 읽는다. M1은 지형 뼈대 + 확률 타일까지만이고 함정·보상·링크·검증은 M2/M3.

**기술 스택:** Unity 2D, C#, NUnit(EditMode), 기존 Tilemap 스탬프 파이프라인.

**설계 문서:** [design.md](design.md) — 이 계획은 설계 §1~§6, §10~§13(M1 범위)을 구현한다.

## Global Constraints

- **버전 관리는 UVCS.** `git add`/`git commit` 등 git 명령을 실행하지 않는다. 각 태스크 끝의 체크포인트는 "Unity 컴파일 에러 없음 확인"이며, 커밋은 사람이 UVCS로 한다.
- **테스트는 작성만 한다.** Unity Test Runner 실행(`mcp__mcp-unity__run_tests` 포함)은 호출하지 않는다. 사람이 직접 돌린다.
- **생성기 코드에 `UnityEditor` 참조 금지.** `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/` 아래 파일은 `using UnityEditor`를 쓰지 않는다. 나중에 런타임 생성으로 전환할 때 그대로 살리기 위함.
- **`UnityEngine.Random` 사용 금지.** 난수는 `System.Random` 인스턴스를 인자로 주입받는다. 같은 시드 → 같은 맵이 보장되어야 한다.
- **Unity는 `char` 필드를 직렬화하지 않는다.** ScriptableObject에 심볼을 둘 때는 `string`으로 선언하고 `[0]`으로 꺼내 쓴다 (기존 `DungeonTilesetSO`와 동일한 방식).
- **격자 인덱싱은 `[row, col]`**, 행 0이 맨 위. 기존 `DungeonMapData`와 동일.
- **주석은 한국어.** 파일 상단에 `// @tags: ...` 검색 태그 주석을 단다 (프로젝트 파일 검색 규약).
- **네임스페이스:** 생성기는 `Gameplay.Dungeon.Authoring.Generation`. 테스트 클래스는 네임스페이스 없이 `Assets/Tests/EditMode/` 직하 (기존 `DungeonMapParserTests.cs`와 동일).

---

## 파일 구성

### 신규 — `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/`

| 파일 | 책임 |
|---|---|
| `RoomTypes.cs` | `RoomOpen` 플래그 / `RoomRole` 열거형 / 문자열 파싱·포맷 유틸 |
| `DungeonRoomTemplate.cs` | 방 템플릿 1개의 데이터 컨테이너 |
| `DungeonRoomTemplateParser.cs` | 템플릿 txt → `List<DungeonRoomTemplate>` |
| `DungeonGenPresetSO.cs` | 생성 파라미터 ScriptableObject |
| `DungeonPathBuilder.cs` | 경로 뚫기 → `RoomPlan[,]` |
| `DungeonRoomComposer.cs` | 템플릿 선택 + 확률 타일 + 조립 + 외곽벽 + 경계 개통 |
| `DungeonGenerator.cs` | 오케스트레이터. 프리셋+시드 → `DungeonMapData` |
| `DungeonMapWriter.cs` | `DungeonMapData` → 기존 포맷 txt 문자열 |

### 수정

| 파일 | 내용 |
|---|---|
| `Assets/Scripts/Editor/Dungeon/DungeonMapImporter.cs` | Generate 섹션 추가. 기존 Import 경로는 그대로 |

### 신규 데이터

| 파일 | 내용 |
|---|---|
| `Assets/DungeonMaps/Templates/cave_rooms.txt` | 최소 방 템플릿 세트 (약 12개) |
| `Assets/DungeonMaps/Generated/` | 생성 결과 출력 폴더 (런타임 생성) |

### 신규 테스트 — `Assets/Tests/EditMode/`

| 파일 | 대상 |
|---|---|
| `DungeonRoomTypesTests.cs` | Task 1 |
| `DungeonRoomTemplateParserTests.cs` | Task 2 |
| `DungeonGenPresetTests.cs` | Task 3 |
| `DungeonPathBuilderTests.cs` | Task 4 |
| `DungeonRoomComposerTests.cs` | Task 5 |
| `DungeonGeneratorTests.cs` | Task 6 |

---

## Task 1: 방 타입 열거형과 템플릿 데이터

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/RoomTypes.cs`
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonRoomTemplate.cs`
- Test: `Assets/Tests/EditMode/DungeonRoomTypesTests.cs`

**Interfaces:**
- Consumes: 없음 (첫 태스크)
- Produces:
  - `enum RoomOpen { None=0, L=1, R=2, U=4, D=8 }` ([Flags])
  - `enum RoomRole { Normal, Start, End, Fill }`
  - `static bool RoomTypeUtil.TryParseOpens(string s, out RoomOpen opens)`
  - `static string RoomTypeUtil.FormatOpens(RoomOpen opens)` — 항상 `L R U D` 순
  - `static RoomOpen RoomTypeUtil.Opposite(RoomOpen dir)`
  - `class DungeonRoomTemplate { RoomRole Role; RoomOpen Opens; int Weight; int Width; int Height; char[,] Tiles; char[,] Objects; char[,] Links; }`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/DungeonRoomTypesTests.cs`:

```csharp
using NUnit.Framework;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonRoomTypesTests
{
    [Test]
    public void TryParseOpens_Combination_ReturnsFlags()
    {
        Assert.IsTrue(RoomTypeUtil.TryParseOpens("LR", out var o));
        Assert.AreEqual(RoomOpen.L | RoomOpen.R, o);
    }

    [Test]
    public void TryParseOpens_IsCaseInsensitiveAndIgnoresSpaces()
    {
        Assert.IsTrue(RoomTypeUtil.TryParseOpens(" l d ", out var o));
        Assert.AreEqual(RoomOpen.L | RoomOpen.D, o);
    }

    [Test]
    public void TryParseOpens_Empty_ReturnsNone()
    {
        Assert.IsTrue(RoomTypeUtil.TryParseOpens("", out var o));
        Assert.AreEqual(RoomOpen.None, o);
    }

    [Test]
    public void TryParseOpens_UnknownChar_Fails()
    {
        Assert.IsFalse(RoomTypeUtil.TryParseOpens("LX", out _));
    }

    [Test]
    public void FormatOpens_AlwaysLRUDOrder()
    {
        Assert.AreEqual("LRUD", RoomTypeUtil.FormatOpens(RoomOpen.D | RoomOpen.U | RoomOpen.R | RoomOpen.L));
        Assert.AreEqual("", RoomTypeUtil.FormatOpens(RoomOpen.None));
    }

    [Test]
    public void Opposite_SwapsDirections()
    {
        Assert.AreEqual(RoomOpen.R, RoomTypeUtil.Opposite(RoomOpen.L));
        Assert.AreEqual(RoomOpen.L, RoomTypeUtil.Opposite(RoomOpen.R));
        Assert.AreEqual(RoomOpen.D, RoomTypeUtil.Opposite(RoomOpen.U));
        Assert.AreEqual(RoomOpen.U, RoomTypeUtil.Opposite(RoomOpen.D));
        Assert.AreEqual(RoomOpen.None, RoomTypeUtil.Opposite(RoomOpen.None));
    }
}
```

- [ ] **Step 2: `RoomTypes.cs` 작성**

```csharp
// @tags: dungeon, generation, room, type, enum
using System;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>방의 열린 면(개구부) 집합. 가로형 던전에서 인접 방과 이어지는 방향.</summary>
    [Flags]
    public enum RoomOpen
    {
        None = 0,
        L = 1,
        R = 2,
        U = 4,
        D = 8,
    }

    /// <summary>방의 역할. Start/End는 E·X를 직접 품은 전용 템플릿, Fill은 경로 밖 방.</summary>
    public enum RoomRole
    {
        Normal,
        Start,
        End,
        Fill,
    }

    public static class RoomTypeUtil
    {
        /// <summary>"LR", " l d " 같은 문자열을 플래그로. 대소문자·공백 무시. 빈 문자열은 None.</summary>
        public static bool TryParseOpens(string s, out RoomOpen opens)
        {
            opens = RoomOpen.None;
            if (s == null) return false;

            foreach (char raw in s)
            {
                if (char.IsWhiteSpace(raw)) continue;
                switch (char.ToUpperInvariant(raw))
                {
                    case 'L': opens |= RoomOpen.L; break;
                    case 'R': opens |= RoomOpen.R; break;
                    case 'U': opens |= RoomOpen.U; break;
                    case 'D': opens |= RoomOpen.D; break;
                    default: opens = RoomOpen.None; return false;
                }
            }
            return true;
        }

        /// <summary>플래그를 항상 L→R→U→D 순의 문자열로. 경고 메시지·비교에 쓴다.</summary>
        public static string FormatOpens(RoomOpen opens)
        {
            var sb = new System.Text.StringBuilder(4);
            if ((opens & RoomOpen.L) != 0) sb.Append('L');
            if ((opens & RoomOpen.R) != 0) sb.Append('R');
            if ((opens & RoomOpen.U) != 0) sb.Append('U');
            if ((opens & RoomOpen.D) != 0) sb.Append('D');
            return sb.ToString();
        }

        /// <summary>단일 방향의 반대. 경로가 A→B로 이동할 때 B에 뚫을 면을 구한다.</summary>
        public static RoomOpen Opposite(RoomOpen dir)
        {
            switch (dir)
            {
                case RoomOpen.L: return RoomOpen.R;
                case RoomOpen.R: return RoomOpen.L;
                case RoomOpen.U: return RoomOpen.D;
                case RoomOpen.D: return RoomOpen.U;
                default: return RoomOpen.None;
            }
        }
    }
}
```

- [ ] **Step 3: `DungeonRoomTemplate.cs` 작성**

```csharp
// @tags: dungeon, generation, room, template, data
namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 방 템플릿 1개. 격자는 [row, col] 인덱싱, 빈 칸은 '.'.
    /// 크기는 프리셋의 roomWidth/roomHeight와 정확히 일치해야 한다(파서가 검증).
    /// </summary>
    public class DungeonRoomTemplate
    {
        public RoomRole Role = RoomRole.Normal;
        public RoomOpen Opens = RoomOpen.None;
        public int Weight = 1;

        public int Width;
        public int Height;
        public char[,] Tiles;   // [Height, Width]
        public char[,] Objects; // [Height, Width]
        public char[,] Links;   // [Height, Width] — LINKS 미존재 시 전부 '.'

        /// <summary>경고·에러 메시지용 표기. 예: "START R", "LR", "FILL"</summary>
        public string Describe()
        {
            string role = Role == RoomRole.Normal ? "" : Role.ToString().ToUpperInvariant() + " ";
            return (role + RoomTypeUtil.FormatOpens(Opens)).Trim();
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인**

Unity 에디터로 돌아가 컴파일 에러가 없는지 확인한다. Console에 에러 0건이어야 한다.
테스트 실행은 사람이 한다 — Test Runner > EditMode > `DungeonRoomTypesTests` 6건 통과 예상.

- [ ] **Step 5: 체크포인트**

컴파일 통과 후 다음 태스크로. (커밋은 UVCS로 사람이)

---

## Task 2: 방 템플릿 파서

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonRoomTemplateParser.cs`
- Test: `Assets/Tests/EditMode/DungeonRoomTemplateParserTests.cs`

**Interfaces:**
- Consumes: `DungeonRoomTemplate`, `RoomOpen`, `RoomRole`, `RoomTypeUtil.TryParseOpens` (Task 1)
- Produces:
  - `class RoomTemplateParseResult { bool Success; List<DungeonRoomTemplate> Templates; List<string> Errors; List<string> Warnings; }`
  - `static RoomTemplateParseResult DungeonRoomTemplateParser.Parse(string text, int roomWidth, int roomHeight, string sourceName)`

**포맷 요약** (설계 §4): `---` 한 줄로 방 구분. 각 방은 `# room: <ROLE?> <OPENS?>` 헤더 + 선택적 `# weight: N` + `[TILES]`/`[OBJECTS]` 필수 + `[LINKS]` 선택.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/DungeonRoomTemplateParserTests.cs`:

```csharp
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

    [Test]
    public void Parse_HeaderRoleAndOpens()
    {
        var r = DungeonRoomTemplateParser.Parse(TwoRooms, 4, 3, "test");
        Assert.AreEqual(RoomRole.Start, r.Templates[0].Role);
        Assert.AreEqual(RoomOpen.R, r.Templates[0].Opens);
        Assert.AreEqual(RoomRole.Normal, r.Templates[1].Role);
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

    [Test]
    public void Parse_FillRoleWithoutOpens()
    {
        string s =
            "# room: FILL\n" +
            "[TILES]\nWWWW\nWWWW\nWWWW\n" +
            "[OBJECTS]\n....\n....\n....\n";
        var r = DungeonRoomTemplateParser.Parse(s, 4, 3, "test");
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(RoomRole.Fill, r.Templates[0].Role);
        Assert.AreEqual(RoomOpen.None, r.Templates[0].Opens);
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
```

- [ ] **Step 2: `DungeonRoomTemplateParser.cs` 작성**

```csharp
// @tags: dungeon, generation, room, template, parser
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    public class RoomTemplateParseResult
    {
        public bool Success;
        public readonly List<DungeonRoomTemplate> Templates = new List<DungeonRoomTemplate>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 방 템플릿 파일을 파싱한다. 파일 하나에 방 여러 개, '---' 한 줄로 구분.
    ///   # room: &lt;ROLE?&gt; &lt;OPENS?&gt;   ROLE = START|END|FILL (생략 시 Normal), OPENS = L/R/U/D 조합
    ///   # weight: N                        같은 타입끼리의 추첨 가중치 (생략 시 1)
    ///   [TILES] / [OBJECTS] 필수, [LINKS] 선택
    /// 모든 방은 roomWidth x roomHeight와 **정확히** 일치해야 한다(짧은 행도 에러).
    /// 짧은 행을 '.'로 패딩하면 방 가장자리가 조용히 뚫려 의도치 않은 개구부가 생긴다.
    /// </summary>
    public static class DungeonRoomTemplateParser
    {
        public static RoomTemplateParseResult Parse(string text, int roomWidth, int roomHeight, string sourceName)
        {
            var result = new RoomTemplateParseResult();
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Errors.Add($"[{sourceName}] 빈 입력");
                return result;
            }

            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            var block = new List<string>();
            int blockStartLine = 1;
            int blockIndex = 0;

            for (int i = 0; i <= lines.Length; i++)
            {
                bool isSeparator = i == lines.Length || lines[i].TrimEnd().StartsWith("---");
                if (!isSeparator) { block.Add(lines[i]); continue; }

                if (HasContent(block))
                {
                    var t = ParseBlock(block, roomWidth, roomHeight, sourceName, blockIndex, blockStartLine, result);
                    if (t != null) result.Templates.Add(t);
                    blockIndex++;
                }
                block.Clear();
                blockStartLine = i + 2;
            }

            if (result.Templates.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add($"[{sourceName}] 방 템플릿을 하나도 찾지 못했습니다.");

            result.Success = result.Errors.Count == 0;
            return result;
        }

        private static bool HasContent(List<string> block)
        {
            foreach (string l in block)
                if (l.Trim().Length > 0) return true;
            return false;
        }

        // 방 하나(블록) 파싱. 실패 시 result.Errors에 쌓고 null 반환.
        private static DungeonRoomTemplate ParseBlock(
            List<string> block, int roomWidth, int roomHeight,
            string sourceName, int blockIndex, int startLine, RoomTemplateParseResult result)
        {
            string tag = $"[{sourceName} #{blockIndex} (line {startLine})]";

            var template = new DungeonRoomTemplate();
            bool sawRoomHeader = false;

            var tiles = new List<string>();
            var objects = new List<string>();
            var links = new List<string>();
            List<string> current = null;

            foreach (string raw in block)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;

                if (trimmed.StartsWith("#"))
                {
                    if (!ParseMeta(trimmed, template, ref sawRoomHeader, tag, result)) return null;
                    continue;
                }
                if (trimmed == "[TILES]") { current = tiles; continue; }
                if (trimmed == "[OBJECTS]") { current = objects; continue; }
                if (trimmed == "[LINKS]") { current = links; continue; }

                if (current == null)
                {
                    result.Errors.Add($"{tag} 섹션 헤더 전에 내용이 나옴: '{raw}'");
                    return null;
                }
                current.Add(raw.TrimEnd());
            }

            if (!sawRoomHeader) { result.Errors.Add($"{tag} '# room:' 헤더가 없습니다."); return null; }
            if (tiles.Count == 0) { result.Errors.Add($"{tag} [TILES] 섹션이 없거나 비었습니다."); return null; }
            if (objects.Count == 0) { result.Errors.Add($"{tag} [OBJECTS] 섹션이 없거나 비었습니다."); return null; }

            if (!CheckSize(tiles, "[TILES]", roomWidth, roomHeight, tag, result)) return null;
            if (!CheckSize(objects, "[OBJECTS]", roomWidth, roomHeight, tag, result)) return null;
            if (links.Count > 0 && !CheckSize(links, "[LINKS]", roomWidth, roomHeight, tag, result)) return null;

            template.Width = roomWidth;
            template.Height = roomHeight;
            template.Tiles = ToGrid(tiles, roomHeight, roomWidth);
            template.Objects = ToGrid(objects, roomHeight, roomWidth);
            template.Links = links.Count > 0 ? ToGrid(links, roomHeight, roomWidth) : FilledDots(roomHeight, roomWidth);
            return template;
        }

        // "# room: START R" / "# weight: 3" 처리. 알 수 없는 키는 주석으로 무시.
        private static bool ParseMeta(
            string line, DungeonRoomTemplate template, ref bool sawRoomHeader,
            string tag, RoomTemplateParseResult result)
        {
            string body = line.TrimStart('#').Trim();
            int colon = body.IndexOf(':');
            if (colon < 0) return true; // 순수 주석

            string key = body.Substring(0, colon).Trim().ToLowerInvariant();
            string val = body.Substring(colon + 1).Trim();

            if (key == "room")
            {
                sawRoomHeader = true;
                return ParseRoomHeader(val, template, tag, result);
            }
            if (key == "weight")
            {
                if (!int.TryParse(val, out int w) || w <= 0)
                {
                    result.Errors.Add($"{tag} weight는 1 이상의 정수여야 합니다: '{val}'");
                    return false;
                }
                template.Weight = w;
            }
            return true;
        }

        // "START R" / "LR" / "FILL" → Role + Opens
        private static bool ParseRoomHeader(string val, DungeonRoomTemplate template, string tag, RoomTemplateParseResult result)
        {
            string[] parts = val.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            string opensText = "";

            foreach (string p in parts)
            {
                switch (p.ToUpperInvariant())
                {
                    case "START": template.Role = RoomRole.Start; break;
                    case "END": template.Role = RoomRole.End; break;
                    case "FILL": template.Role = RoomRole.Fill; break;
                    case "NORMAL": template.Role = RoomRole.Normal; break;
                    default: opensText += p; break;
                }
            }

            if (!RoomTypeUtil.TryParseOpens(opensText, out var opens))
            {
                result.Errors.Add($"{tag} 알 수 없는 방 타입 표기: '{val}' (ROLE=START|END|FILL, OPENS=L/R/U/D 조합)");
                return false;
            }
            template.Opens = opens;
            return true;
        }

        private static bool CheckSize(
            List<string> rows, string section, int roomWidth, int roomHeight,
            string tag, RoomTemplateParseResult result)
        {
            if (rows.Count != roomHeight)
            {
                result.Errors.Add($"{tag} {section} 행 수가 {rows.Count}인데 방 높이는 {roomHeight}입니다.");
                return false;
            }
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Length != roomWidth)
                {
                    result.Errors.Add($"{tag} {section} {r}행 길이가 {rows[r].Length}인데 방 너비는 {roomWidth}입니다.");
                    return false;
                }
            }
            return true;
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

- [ ] **Step 3: 컴파일 확인**

Unity Console 에러 0건. 테스트는 사람이 실행 — `DungeonRoomTemplateParserTests` 11건 통과 예상.

- [ ] **Step 4: 체크포인트**

---

## Task 3: 생성 프리셋 ScriptableObject

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonGenPresetSO.cs`
- Test: `Assets/Tests/EditMode/DungeonGenPresetTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `class DungeonGenPresetSO : ScriptableObject`
    - `int gridWidth, gridHeight, roomWidth, roomHeight`
    - `List<TextAsset> roomTemplateFiles`
    - `float horizontalChance`
    - `string wallSymbol, emptySymbol` + `char Wall`, `char Empty` 프로퍼티
    - `List<ChanceTile> chanceTiles`
    - `bool forceCarveBoundaries`
    - `void BuildLookup()`
    - `bool TryGetChanceTile(char symbol, out ChanceTile tile)`
  - `class DungeonGenPresetSO.ChanceTile { string symbol; float chance; string onHit; string onMiss; }`

**주의:** Unity는 `char` 필드를 직렬화하지 않는다. 심볼은 전부 `string`으로 선언하고 `[0]`으로 꺼낸다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/DungeonGenPresetTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonGenPresetTests
{
    private DungeonGenPresetSO _preset;

    [SetUp]
    public void SetUp() => _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_preset);

    [Test]
    public void Defaults_AreHorizontalDungeonSized()
    {
        Assert.AreEqual(4, _preset.gridWidth);
        Assert.AreEqual(3, _preset.gridHeight);
        Assert.AreEqual(10, _preset.roomWidth);
        Assert.AreEqual(6, _preset.roomHeight);
        Assert.IsTrue(_preset.forceCarveBoundaries);
    }

    [Test]
    public void SymbolProperties_ReadFirstCharOfString()
    {
        Assert.AreEqual('W', _preset.Wall);
        Assert.AreEqual('.', _preset.Empty);
    }

    [Test]
    public void SymbolProperties_EmptyStringFallsBack()
    {
        _preset.wallSymbol = "";
        _preset.emptySymbol = null;
        Assert.AreEqual('W', _preset.Wall);
        Assert.AreEqual('.', _preset.Empty);
    }

    [Test]
    public void TryGetChanceTile_DefaultTableHasZeroOneTwo()
    {
        Assert.IsTrue(_preset.TryGetChanceTile('0', out var t0));
        Assert.AreEqual(0.50f, t0.chance, 0.0001f);
        Assert.AreEqual("W", t0.onHit);
        Assert.AreEqual(".", t0.onMiss);

        Assert.IsTrue(_preset.TryGetChanceTile('1', out var t1));
        Assert.AreEqual(0.25f, t1.chance, 0.0001f);

        Assert.IsTrue(_preset.TryGetChanceTile('2', out var t2));
        Assert.AreEqual(0.75f, t2.chance, 0.0001f);
    }

    [Test]
    public void TryGetChanceTile_UnregisteredSymbol_ReturnsFalse()
    {
        Assert.IsFalse(_preset.TryGetChanceTile('W', out _));
        Assert.IsFalse(_preset.TryGetChanceTile('.', out _));
    }

    [Test]
    public void BuildLookup_PicksUpRuntimeEdits()
    {
        _preset.chanceTiles.Add(new DungeonGenPresetSO.ChanceTile
        {
            symbol = "9", chance = 0.1f, onHit = "W", onMiss = "."
        });
        _preset.BuildLookup();
        Assert.IsTrue(_preset.TryGetChanceTile('9', out var t));
        Assert.AreEqual(0.1f, t.chance, 0.0001f);
    }
}
```

- [ ] **Step 2: `DungeonGenPresetSO.cs` 작성**

```csharp
// @tags: dungeon, generation, preset, scriptableobject, config
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 던전 생성 파라미터. 확률 타일 같은 "문법"을 코드가 아닌 데이터로 두어
    /// 심볼을 추가·변경할 때 코드 수정이 필요 없게 한다.
    /// Unity는 char를 직렬화하지 않으므로 심볼은 전부 string으로 선언한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon/Generation Preset", fileName = "DungeonGenPreset")]
    public class DungeonGenPresetSO : ScriptableObject
    {
        /// <summary>확률 타일 1종. chance 확률로 onHit, 아니면 onMiss로 치환된다.</summary>
        [Serializable]
        public class ChanceTile
        {
            public string symbol = "0";
            [Range(0f, 1f)] public float chance = 0.5f;
            public string onHit = "W";
            public string onMiss = ".";
        }

        [Header("격자 (방 단위)")]
        [Min(2)] public int gridWidth = 4;   // 1이면 START와 END가 같은 셀이 된다
        [Min(1)] public int gridHeight = 3;

        [Header("방 하나 크기 (타일)")]
        [Min(3)] public int roomWidth = 10;
        [Min(3)] public int roomHeight = 6;

        [Header("방 템플릿 파일")]
        public List<TextAsset> roomTemplateFiles = new List<TextAsset>();

        [Header("경로")]
        [Tooltip("매 스텝에서 오른쪽으로 이동할 확률. 낮을수록 위아래로 구불거린다.")]
        [Range(0f, 1f)] public float horizontalChance = 0.6f;

        [Header("기본 심볼")]
        public string wallSymbol = "W";
        public string emptySymbol = ".";

        [Header("확률 타일")]
        public List<ChanceTile> chanceTiles = new List<ChanceTile>
        {
            new ChanceTile { symbol = "0", chance = 0.50f, onHit = "W", onMiss = "." },
            new ChanceTile { symbol = "1", chance = 0.25f, onHit = "W", onMiss = "." },
            new ChanceTile { symbol = "2", chance = 0.75f, onHit = "W", onMiss = "." },
        };

        [Header("안전망")]
        [Tooltip("인접한 경로 방 사이 경계에 통로를 강제로 뚫는다. 템플릿이 다듬어지면 꺼도 된다.")]
        public bool forceCarveBoundaries = true;

        public char Wall => FirstChar(wallSymbol, 'W');
        public char Empty => FirstChar(emptySymbol, '.');

        private Dictionary<char, ChanceTile> _chanceLookup;

        private void OnEnable() => BuildLookup();
        private void OnValidate() => BuildLookup();

        public void BuildLookup()
        {
            _chanceLookup = new Dictionary<char, ChanceTile>();
            if (chanceTiles == null) return;
            foreach (var c in chanceTiles)
            {
                if (c == null || string.IsNullOrEmpty(c.symbol)) continue;
                _chanceLookup[c.symbol[0]] = c;
            }
        }

        public bool TryGetChanceTile(char symbol, out ChanceTile tile)
        {
            if (_chanceLookup == null) BuildLookup();
            return _chanceLookup.TryGetValue(symbol, out tile);
        }

        private static char FirstChar(string s, char fallback)
            => string.IsNullOrEmpty(s) ? fallback : s[0];
    }
}
```

- [ ] **Step 3: 컴파일 확인**

Unity Console 에러 0건. 테스트는 사람이 실행 — `DungeonGenPresetTests` 6건 통과 예상.

- [ ] **Step 4: 체크포인트**

---

## Task 4: 경로 생성기

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonPathBuilder.cs`
- Test: `Assets/Tests/EditMode/DungeonPathBuilderTests.cs`

**Interfaces:**
- Consumes: `RoomRole`, `RoomOpen` (Task 1)
- Produces:
  - `struct RoomPlan { RoomRole Role; RoomOpen Opens; }`
  - `static RoomPlan[,] DungeonPathBuilder.Build(int gridWidth, int gridHeight, float horizontalChance, System.Random rng)` — 반환 배열은 `[gridHeight, gridWidth]` 인덱싱

**알고리즘** (설계 §5): 왼쪽 열 랜덤 행에서 START(오른쪽만 열림) → 매 스텝 `horizontalChance`로 오른쪽/세로 이동 → 오른쪽 끝 열 도달 시 END. 세로 이동은 격자 안이고 미방문인 쪽만. 미방문 셀은 전부 FILL.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/DungeonPathBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonPathBuilderTests
{
    private static RoomPlan[,] Build(int w, int h, float hc, int seed)
        => DungeonPathBuilder.Build(w, h, hc, new System.Random(seed));

    private static (int row, int col) Find(RoomPlan[,] g, RoomRole role)
    {
        for (int r = 0; r < g.GetLength(0); r++)
            for (int c = 0; c < g.GetLength(1); c++)
                if (g[r, c].Role == role) return (r, c);
        return (-1, -1);
    }

    [Test]
    public void Build_StartInLeftColumn_EndInRightColumn()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var g = Build(4, 3, 0.6f, seed);
            var start = Find(g, RoomRole.Start);
            var end = Find(g, RoomRole.End);
            Assert.AreEqual(0, start.col, $"seed {seed}: START는 맨 왼쪽 열이어야 함");
            Assert.AreEqual(3, end.col, $"seed {seed}: END는 맨 오른쪽 열이어야 함");
        }
    }

    [Test]
    public void Build_StartOpensRightOnly_EndOpensLeftOnly()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var g = Build(4, 3, 0.6f, seed);
            var s = Find(g, RoomRole.Start);
            var e = Find(g, RoomRole.End);
            Assert.AreEqual(RoomOpen.R, g[s.row, s.col].Opens, $"seed {seed}");
            Assert.AreEqual(RoomOpen.L, g[e.row, e.col].Opens, $"seed {seed}");
        }
    }

    [Test]
    public void Build_OpensAreSymmetricBetweenNeighbours()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var g = Build(5, 4, 0.6f, seed);
            int h = g.GetLength(0), w = g.GetLength(1);
            for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                bool right = (g[r, c].Opens & RoomOpen.R) != 0;
                if (right)
                {
                    Assert.Less(c + 1, w, $"seed {seed}: ({r},{c}) R이 격자 밖을 가리킴");
                    Assert.AreNotEqual(0, (int)(g[r, c + 1].Opens & RoomOpen.L), $"seed {seed}: ({r},{c}) R ↔ L 비대칭");
                }
                bool down = (g[r, c].Opens & RoomOpen.D) != 0;
                if (down)
                {
                    Assert.Less(r + 1, h, $"seed {seed}: ({r},{c}) D가 격자 밖을 가리킴");
                    Assert.AreNotEqual(0, (int)(g[r + 1, c].Opens & RoomOpen.U), $"seed {seed}: ({r},{c}) D ↔ U 비대칭");
                }
            }
        }
    }

    [Test]
    public void Build_PathConnectsStartToEnd()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var g = Build(5, 4, 0.6f, seed);
            var s = Find(g, RoomRole.Start);
            var e = Find(g, RoomRole.End);

            int h = g.GetLength(0), w = g.GetLength(1);
            var seen = new bool[h, w];
            var stack = new Stack<(int r, int c)>();
            stack.Push(s);
            seen[s.row, s.col] = true;

            while (stack.Count > 0)
            {
                var (r, c) = stack.Pop();
                var o = g[r, c].Opens;
                TryPush(stack, seen, g, h, w, r, c - 1, (o & RoomOpen.L) != 0);
                TryPush(stack, seen, g, h, w, r, c + 1, (o & RoomOpen.R) != 0);
                TryPush(stack, seen, g, h, w, r - 1, c, (o & RoomOpen.U) != 0);
                TryPush(stack, seen, g, h, w, r + 1, c, (o & RoomOpen.D) != 0);
            }
            Assert.IsTrue(seen[e.row, e.col], $"seed {seed}: START에서 END로 이어지지 않음");
        }
    }

    private static void TryPush(Stack<(int, int)> stack, bool[,] seen, RoomPlan[,] g, int h, int w, int r, int c, bool open)
    {
        if (!open || r < 0 || r >= h || c < 0 || c >= w || seen[r, c]) return;
        seen[r, c] = true;
        stack.Push((r, c));
    }

    [Test]
    public void Build_UnvisitedCellsAreFillWithNoOpens()
    {
        var g = Build(4, 3, 1f, 123); // 항상 오른쪽 → 한 줄만 경로
        int fill = 0;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 4; c++)
                if (g[r, c].Role == RoomRole.Fill)
                {
                    fill++;
                    Assert.AreEqual(RoomOpen.None, g[r, c].Opens);
                }
        Assert.AreEqual(8, fill); // 12칸 중 4칸이 경로
    }

    [Test]
    public void Build_SameSeed_SameResult()
    {
        var a = Build(5, 4, 0.6f, 777);
        var b = Build(5, 4, 0.6f, 777);
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 5; c++)
            {
                Assert.AreEqual(a[r, c].Role, b[r, c].Role, $"({r},{c}) Role");
                Assert.AreEqual(a[r, c].Opens, b[r, c].Opens, $"({r},{c}) Opens");
            }
    }

    [Test]
    public void Build_GridWidthLessThanTwo_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => Build(1, 3, 0.6f, 0));
    }

    [Test]
    public void Build_GridHeightLessThanOne_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => Build(4, 0, 0.6f, 0));
    }

    [Test]
    public void Build_NullRng_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => DungeonPathBuilder.Build(4, 3, 0.6f, null));
    }
}
```

- [ ] **Step 2: `DungeonPathBuilder.cs` 작성**

```csharp
// @tags: dungeon, generation, path, grid, room
namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>격자 한 칸의 계획. 어떤 역할의 방이며 어느 면이 열려야 하는가.</summary>
    public struct RoomPlan
    {
        public RoomRole Role;
        public RoomOpen Opens;
    }

    /// <summary>
    /// 가로형 던전의 경로를 뚫는다. 왼쪽 열에서 시작해 오른쪽 끝 열에 닿으면 끝.
    /// 지형을 만들기 전에 "길"부터 정하는 스팰렁키 방식.
    /// 반환 배열은 [row, col] 인덱싱이며 row 0이 맨 위.
    ///
    /// START는 항상 R만, END는 항상 L만 열린다 — 시작·끝에서 세로 이동을 허용하면
    /// START RD / START RU / END LU … 전용 템플릿을 전부 그려야 해서 초기 작업량이 커진다.
    /// </summary>
    public static class DungeonPathBuilder
    {
        public static RoomPlan[,] Build(int gridWidth, int gridHeight, float horizontalChance, System.Random rng)
        {
            if (gridWidth < 2)
                throw new System.ArgumentOutOfRangeException(nameof(gridWidth), "gridWidth는 2 이상이어야 합니다 (1이면 START와 END가 같은 셀).");
            if (gridHeight < 1)
                throw new System.ArgumentOutOfRangeException(nameof(gridHeight), "gridHeight는 1 이상이어야 합니다.");
            if (rng == null)
                throw new System.ArgumentNullException(nameof(rng));

            var grid = new RoomPlan[gridHeight, gridWidth];
            for (int r = 0; r < gridHeight; r++)
                for (int c = 0; c < gridWidth; c++)
                    grid[r, c] = new RoomPlan { Role = RoomRole.Fill, Opens = RoomOpen.None };

            var visited = new bool[gridHeight, gridWidth];

            int row = rng.Next(gridHeight);
            int col = 0;
            visited[row, col] = true;
            grid[row, col].Role = RoomRole.Start;

            // 첫 스텝은 무조건 오른쪽. 여기서 세로로 움직이면 START에 U/D가 붙어
            // 'START U' / 'START D' 전용 템플릿이 필요해진다(설계 §5: START는 항상 R만).
            bool firstStep = true;

            while (col < gridWidth - 1)
            {
                bool goRight = firstStep || rng.NextDouble() < horizontalChance;
                firstStep = false;

                if (!goRight)
                {
                    bool canUp = row > 0 && !visited[row - 1, col];
                    bool canDown = row < gridHeight - 1 && !visited[row + 1, col];

                    if (!canUp && !canDown)
                    {
                        goRight = true; // 세로로 갈 데가 없으면 강제 전진
                    }
                    else
                    {
                        bool up = canUp && (!canDown || rng.Next(2) == 0);
                        RoomOpen dir = up ? RoomOpen.U : RoomOpen.D;

                        grid[row, col].Opens |= dir;
                        row += up ? -1 : 1;
                        grid[row, col].Opens |= RoomTypeUtil.Opposite(dir);

                        visited[row, col] = true;
                        if (grid[row, col].Role == RoomRole.Fill) grid[row, col].Role = RoomRole.Normal;
                    }
                }

                if (goRight)
                {
                    grid[row, col].Opens |= RoomOpen.R;
                    col++;
                    grid[row, col].Opens |= RoomOpen.L;

                    visited[row, col] = true;
                    if (grid[row, col].Role == RoomRole.Fill) grid[row, col].Role = RoomRole.Normal;
                }
            }

            grid[row, col].Role = RoomRole.End;
            return grid;
        }
    }
}
```

- [ ] **Step 3: 컴파일 확인**

Unity Console 에러 0건. 테스트는 사람이 실행 — `DungeonPathBuilderTests` 9건 통과 예상.

- [ ] **Step 4: 체크포인트**

---

## Task 5: 방 조립기

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonRoomComposer.cs`
- Test: `Assets/Tests/EditMode/DungeonRoomComposerTests.cs`

**Interfaces:**
- Consumes: `RoomPlan` (Task 4), `DungeonRoomTemplate` (Task 1), `DungeonGenPresetSO` (Task 3)
- Produces:
  - `class ComposeResult { int Width; int Height; char[,] Tiles; char[,] Objects; char[,] Links; List<string> Warnings; }`
  - `static ComposeResult DungeonRoomComposer.Compose(RoomPlan[,] plan, IReadOnlyList<DungeonRoomTemplate> templates, DungeonGenPresetSO preset, System.Random rng)`

**책임** (설계 §6): 템플릿 선택(정확 일치 → 상위집합 → 빈 방) · 확률 타일 굴림 · 외곽 벽 링 · 좌표 변환 · 경계 강제 개통.

**좌표 변환:** 방 `(gr, gc)`의 로컬 `(r, c)` → 전역 `(gr*roomHeight + r + 1, gc*roomWidth + c + 1)`. `+1`은 외곽 벽 링.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/DungeonRoomComposerTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: `DungeonRoomComposer.cs` 작성**

```csharp
// @tags: dungeon, generation, compose, room, template, stamp, carve
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>조립된 맵 격자. 인덱싱은 [row, col], row 0이 맨 위.</summary>
    public class ComposeResult
    {
        public int Width;
        public int Height;
        public char[,] Tiles;
        public char[,] Objects;
        public char[,] Links;
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 방 타입 격자에 실제 템플릿을 찍어 하나의 큰 격자로 만든다.
    /// 템플릿 선택은 (Role, Opens) 정확 일치 → Opens 상위집합 → 빈 방 순으로 폴백한다.
    /// 상위집합 폴백에서 생기는 여분의 개구부는 막힌 방으로 이어지는 벽감이 되므로 치명적이지 않다.
    /// </summary>
    public static class DungeonRoomComposer
    {
        public static ComposeResult Compose(
            RoomPlan[,] plan,
            IReadOnlyList<DungeonRoomTemplate> templates,
            DungeonGenPresetSO preset,
            System.Random rng)
        {
            if (plan == null) throw new System.ArgumentNullException(nameof(plan));
            if (templates == null) throw new System.ArgumentNullException(nameof(templates));
            if (preset == null) throw new System.ArgumentNullException(nameof(preset));
            if (rng == null) throw new System.ArgumentNullException(nameof(rng));

            int gridH = plan.GetLength(0);
            int gridW = plan.GetLength(1);
            int rw = preset.roomWidth;
            int rh = preset.roomHeight;

            var res = new ComposeResult
            {
                Width = gridW * rw + 2,
                Height = gridH * rh + 2,
            };
            res.Tiles = Filled(res.Height, res.Width, preset.Wall);
            res.Objects = Filled(res.Height, res.Width, '.');
            res.Links = Filled(res.Height, res.Width, '.');

            for (int gr = 0; gr < gridH; gr++)
            for (int gc = 0; gc < gridW; gc++)
            {
                var cell = plan[gr, gc];
                var template = Pick(cell.Role, cell.Opens, templates, rng, res.Warnings);

                if (template == null)
                    StampBlank(res, gr, gc, preset);
                else
                    StampTemplate(res, template, gr, gc, preset, rng);
            }

            if (preset.forceCarveBoundaries)
                CarveBoundaries(res, plan, preset);

            return res;
        }

        // (Role, Opens) 정확 일치 → Opens 상위집합 → null(빈 방)
        private static DungeonRoomTemplate Pick(
            RoomRole role, RoomOpen opens,
            IReadOnlyList<DungeonRoomTemplate> templates,
            System.Random rng, List<string> warnings)
        {
            var exact = new List<DungeonRoomTemplate>();
            var superset = new List<DungeonRoomTemplate>();

            foreach (var t in templates)
            {
                if (t == null || t.Role != role) continue;
                if (t.Opens == opens) exact.Add(t);
                else if ((t.Opens & opens) == opens) superset.Add(t);
            }

            if (exact.Count > 0) return WeightedPick(exact, rng);

            string label = $"{role} {RoomTypeUtil.FormatOpens(opens)}".Trim();
            if (superset.Count > 0)
            {
                warnings.Add($"'{label}' 템플릿이 없어 상위집합 템플릿으로 대체했습니다.");
                return WeightedPick(superset, rng);
            }

            warnings.Add($"'{label}' 템플릿이 없어 빈 방으로 생성했습니다.");
            return null;
        }

        private static DungeonRoomTemplate WeightedPick(List<DungeonRoomTemplate> list, System.Random rng)
        {
            int total = 0;
            foreach (var t in list) total += t.Weight > 0 ? t.Weight : 1;

            int roll = rng.Next(total);
            foreach (var t in list)
            {
                roll -= t.Weight > 0 ? t.Weight : 1;
                if (roll < 0) return t;
            }
            return list[list.Count - 1];
        }

        private static void StampTemplate(
            ComposeResult res, DungeonRoomTemplate t, int gr, int gc,
            DungeonGenPresetSO preset, System.Random rng)
        {
            int rw = preset.roomWidth;
            int rh = preset.roomHeight;

            for (int r = 0; r < rh; r++)
            for (int c = 0; c < rw; c++)
            {
                int gy = gr * rh + r + 1;
                int gx = gc * rw + c + 1;

                res.Tiles[gy, gx] = ResolveChanceTile(t.Tiles[r, c], preset, rng);
                res.Objects[gy, gx] = t.Objects[r, c];
                res.Links[gy, gx] = t.Links[r, c];
            }
        }

        // 템플릿이 없을 때의 폴백 방: 테두리만 벽, 내부는 빈칸. 경계 개통이 통로를 뚫는다.
        private static void StampBlank(ComposeResult res, int gr, int gc, DungeonGenPresetSO preset)
        {
            int rw = preset.roomWidth;
            int rh = preset.roomHeight;

            for (int r = 0; r < rh; r++)
            for (int c = 0; c < rw; c++)
            {
                int gy = gr * rh + r + 1;
                int gx = gc * rw + c + 1;

                bool border = r == 0 || r == rh - 1 || c == 0 || c == rw - 1;
                res.Tiles[gy, gx] = border ? preset.Wall : preset.Empty;
                res.Objects[gy, gx] = '.';
                res.Links[gy, gx] = '.';
            }
        }

        // 확률 심볼이면 굴려서 치환, 아니면 그대로.
        private static char ResolveChanceTile(char symbol, DungeonGenPresetSO preset, System.Random rng)
        {
            if (!preset.TryGetChanceTile(symbol, out var ct)) return symbol;

            string outcome = rng.NextDouble() < ct.chance ? ct.onHit : ct.onMiss;
            return string.IsNullOrEmpty(outcome) ? preset.Empty : outcome[0];
        }

        /// <summary>
        /// 인접한 경로 방 사이 경계에 통로를 강제로 뚫는다(설계 §4 개구부 위치 규약).
        /// 좌우는 방 하단 기준 위쪽 3행, 상하는 방 가운데 3열. 경계 양쪽 각 1칸씩 뚫는다.
        /// </summary>
        private static void CarveBoundaries(ComposeResult res, RoomPlan[,] plan, DungeonGenPresetSO preset)
        {
            int gridH = plan.GetLength(0);
            int gridW = plan.GetLength(1);
            int rw = preset.roomWidth;
            int rh = preset.roomHeight;
            char empty = preset.Empty;

            int rowFrom = System.Math.Max(0, rh - 4);
            int rowTo = System.Math.Max(0, rh - 2);
            int colFrom = System.Math.Max(0, rw / 2 - 1);
            int colTo = System.Math.Min(rw - 1, rw / 2 + 1);

            for (int gr = 0; gr < gridH; gr++)
            for (int gc = 0; gc < gridW; gc++)
            {
                var opens = plan[gr, gc].Opens;

                if ((opens & RoomOpen.R) != 0 && gc + 1 < gridW)
                {
                    int boundaryCol = gc * rw + rw; // 왼쪽 방의 마지막 열(전역)
                    for (int r = rowFrom; r <= rowTo; r++)
                    {
                        int gy = gr * rh + r + 1;
                        res.Tiles[gy, boundaryCol] = empty;
                        res.Tiles[gy, boundaryCol + 1] = empty;
                    }
                }

                if ((opens & RoomOpen.D) != 0 && gr + 1 < gridH)
                {
                    int boundaryRow = gr * rh + rh; // 위쪽 방의 마지막 행(전역)
                    for (int c = colFrom; c <= colTo; c++)
                    {
                        int gx = gc * rw + c + 1;
                        res.Tiles[boundaryRow, gx] = empty;
                        res.Tiles[boundaryRow + 1, gx] = empty;
                    }
                }
            }
        }

        private static char[,] Filled(int height, int width, char value)
        {
            var g = new char[height, width];
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                    g[r, c] = value;
            return g;
        }
    }
}
```

- [ ] **Step 3: 컴파일 확인**

Unity Console 에러 0건. 테스트는 사람이 실행 — `DungeonRoomComposerTests` 9건 통과 예상.

- [ ] **Step 4: 체크포인트**

---

## Task 6: 오케스트레이터와 맵 라이터

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonGenerator.cs`
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonMapWriter.cs`
- Test: `Assets/Tests/EditMode/DungeonGeneratorTests.cs`

**Interfaces:**
- Consumes: `DungeonGenPresetSO` (Task 3), `DungeonRoomTemplateParser` (Task 2), `DungeonPathBuilder` (Task 4), `DungeonRoomComposer` (Task 5), 기존 `DungeonMapData`
- Produces:
  - `class GenerationResult { bool Success; int Seed; DungeonMapData Data; List<string> Warnings; List<string> Errors; }`
  - `static GenerationResult DungeonGenerator.Generate(DungeonGenPresetSO preset, int seed)`
  - `static string DungeonMapWriter.Write(DungeonMapData data, string presetName, int seed)`

**주의:** `UnityEngine.TextAsset`은 써도 된다(`UnityEditor`만 금지). `DungeonMapData`는 기존 `Gameplay.Dungeon.Authoring` 네임스페이스에 있으므로 `using Gameplay.Dungeon.Authoring;`이 필요하다 — 생성기는 `.Generation` 하위 네임스페이스라 자동으로 보이지 않는다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/DungeonGeneratorTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonGeneratorTests
{
    // 4x4 방 3종 — START/END/LR
    private const string Rooms =
        "# room: START R\n" +
        "[TILES]\nWWWW\nW..W\nW...\nWWWW\n" +
        "[OBJECTS]\n....\n....\n.E..\n....\n" +
        "---\n" +
        "# room: LR\n" +
        "[TILES]\nWWWW\nW00W\n....\nWWWW\n" +
        "[OBJECTS]\n....\n....\n....\n....\n" +
        "---\n" +
        "# room: END L\n" +
        "[TILES]\nWWWW\nW..W\n...W\nWWWW\n" +
        "[OBJECTS]\n....\n....\n..X.\n....\n";

    private DungeonGenPresetSO _preset;
    private TextAsset _roomAsset;

    [SetUp]
    public void SetUp()
    {
        _roomAsset = new TextAsset(Rooms);
        _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();
        _preset.name = "TestPreset";
        _preset.roomWidth = 4;
        _preset.roomHeight = 4;
        _preset.gridWidth = 3;
        _preset.gridHeight = 1;
        _preset.horizontalChance = 1f; // 한 줄 경로로 고정
        _preset.roomTemplateFiles.Add(_roomAsset);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_preset);
        Object.DestroyImmediate(_roomAsset);
    }

    [Test]
    public void Generate_NullPreset_Fails()
    {
        var r = DungeonGenerator.Generate(null, 1);
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.Errors.Count > 0);
    }

    [Test]
    public void Generate_NoTemplateFiles_Fails()
    {
        _preset.roomTemplateFiles.Clear();
        var r = DungeonGenerator.Generate(_preset, 1);
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.Errors.Count > 0);
    }

    [Test]
    public void Generate_BadTemplateSize_Fails()
    {
        _preset.roomWidth = 5; // 템플릿은 4칸
        var r = DungeonGenerator.Generate(_preset, 1);
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void Generate_ProducesMapWithBorderSizeAndName()
    {
        var r = DungeonGenerator.Generate(_preset, 12345);
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(3 * 4 + 2, r.Data.Width);
        Assert.AreEqual(1 * 4 + 2, r.Data.Height);
        Assert.AreEqual("gen_12345", r.Data.Name);
        Assert.AreEqual(12345, r.Seed);
    }

    [Test]
    public void Generate_PlacesExactlyOneEntryAndOneExit()
    {
        var r = DungeonGenerator.Generate(_preset, 7);
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));

        int e = 0, x = 0;
        for (int row = 0; row < r.Data.Height; row++)
            for (int col = 0; col < r.Data.Width; col++)
            {
                if (r.Data.Objects[row, col] == 'E') e++;
                if (r.Data.Objects[row, col] == 'X') x++;
            }
        Assert.AreEqual(1, e, "E는 정확히 1개");
        Assert.AreEqual(1, x, "X는 정확히 1개");
    }

    [Test]
    public void Generate_SameSeed_SameMap()
    {
        var a = DungeonGenerator.Generate(_preset, 999);
        var b = DungeonGenerator.Generate(_preset, 999);
        Assert.AreEqual(DungeonMapWriter.Write(a.Data, "p", 999),
                        DungeonMapWriter.Write(b.Data, "p", 999));
    }

    [Test]
    public void Write_RoundTripsThroughExistingParser()
    {
        var gen = DungeonGenerator.Generate(_preset, 55);
        Assert.IsTrue(gen.Success, string.Join("; ", gen.Errors));

        string text = DungeonMapWriter.Write(gen.Data, "TestPreset", 55);
        var parsed = DungeonMapParser.Parse(text);

        Assert.IsTrue(parsed.Success, string.Join("; ", parsed.Errors));
        Assert.AreEqual(gen.Data.Width, parsed.Data.Width);
        Assert.AreEqual(gen.Data.Height, parsed.Data.Height);
        Assert.AreEqual("gen_55", parsed.Data.Name);

        for (int r = 0; r < gen.Data.Height; r++)
            for (int c = 0; c < gen.Data.Width; c++)
            {
                Assert.AreEqual(gen.Data.Tiles[r, c], parsed.Data.Tiles[r, c], $"Tiles({r},{c})");
                Assert.AreEqual(gen.Data.Objects[r, c], parsed.Data.Objects[r, c], $"Objects({r},{c})");
                Assert.AreEqual(gen.Data.Links[r, c], parsed.Data.Links[r, c], $"Links({r},{c})");
            }
    }

    [Test]
    public void Write_IncludesSeedAndPresetMeta()
    {
        var gen = DungeonGenerator.Generate(_preset, 55);
        string text = DungeonMapWriter.Write(gen.Data, "TestPreset", 55);
        StringAssert.Contains("# seed: 55", text);
        StringAssert.Contains("# preset: TestPreset", text);
        StringAssert.Contains("[TILES]", text);
        StringAssert.Contains("[OBJECTS]", text);
        StringAssert.Contains("[LINKS]", text);
    }
}
```

- [ ] **Step 2: `DungeonMapWriter.cs` 작성**

```csharp
// @tags: dungeon, generation, writer, serialize, txt
using System.Text;
using Gameplay.Dungeon.Authoring;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// DungeonMapData를 기존 DungeonMapParser가 읽을 수 있는 txt로 직렬화한다.
    /// preset/seed 메타는 파서가 무시하지만, "이 맵 어디서 나왔지"를 추적하려고 남긴다.
    /// </summary>
    public static class DungeonMapWriter
    {
        public static string Write(DungeonMapData data, string presetName, int seed)
        {
            if (data == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append("# dungeon: ").Append(data.Name).Append('\n');
            sb.Append("# cell: ").Append(data.CellSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("# size: ").Append(data.Width).Append('x').Append(data.Height).Append('\n');
            sb.Append("# preset: ").Append(presetName ?? "").Append('\n');
            sb.Append("# seed: ").Append(seed).Append('\n');

            AppendBlock(sb, "[TILES]", data.Tiles, data.Width, data.Height);
            AppendBlock(sb, "[OBJECTS]", data.Objects, data.Width, data.Height);
            AppendBlock(sb, "[LINKS]", data.Links, data.Width, data.Height);
            return sb.ToString();
        }

        private static void AppendBlock(StringBuilder sb, string header, char[,] grid, int width, int height)
        {
            sb.Append(header).Append('\n');
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                    sb.Append(grid[r, c]);
                sb.Append('\n');
            }
        }
    }
}
```

- [ ] **Step 3: `DungeonGenerator.cs` 작성**

```csharp
// @tags: dungeon, generation, generator, orchestrator, seed
using System.Collections.Generic;
using Gameplay.Dungeon.Authoring;

namespace Gameplay.Dungeon.Authoring.Generation
{
    public class GenerationResult
    {
        public bool Success;
        public int Seed;
        public DungeonMapData Data;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
    }

    /// <summary>
    /// 프리셋 + 시드 → DungeonMapData. 파이프라인 전체가 System.Random 인스턴스 하나를 공유하므로
    /// 같은 시드는 항상 같은 맵을 만든다. 단계 순서를 바꾸면 같은 시드라도 결과가 달라진다.
    /// UnityEditor에 의존하지 않는다 — 나중에 런타임 생성으로 옮길 때 그대로 쓰기 위함.
    /// </summary>
    public static class DungeonGenerator
    {
        public static GenerationResult Generate(DungeonGenPresetSO preset, int seed)
        {
            var result = new GenerationResult { Seed = seed };

            if (preset == null)
            {
                result.Errors.Add("프리셋이 비어 있습니다.");
                return result;
            }

            var templates = LoadTemplates(preset, result);
            if (result.Errors.Count > 0) return result;
            if (templates.Count == 0)
            {
                result.Errors.Add("방 템플릿이 하나도 없습니다. 프리셋의 Room Template Files를 확인하세요.");
                return result;
            }

            var rng = new System.Random(seed);

            RoomPlan[,] plan;
            try
            {
                plan = DungeonPathBuilder.Build(preset.gridWidth, preset.gridHeight, preset.horizontalChance, rng);
            }
            catch (System.ArgumentOutOfRangeException ex)
            {
                result.Errors.Add($"격자 크기가 잘못되었습니다: {ex.Message}");
                return result;
            }

            var composed = DungeonRoomComposer.Compose(plan, templates, preset, rng);
            result.Warnings.AddRange(composed.Warnings);

            result.Data = new DungeonMapData
            {
                Name = $"gen_{seed}",
                CellSize = 1f,
                Width = composed.Width,
                Height = composed.Height,
                Tiles = composed.Tiles,
                Objects = composed.Objects,
                Links = composed.Links,
            };
            result.Success = true;
            return result;
        }

        private static List<DungeonRoomTemplate> LoadTemplates(DungeonGenPresetSO preset, GenerationResult result)
        {
            var all = new List<DungeonRoomTemplate>();
            if (preset.roomTemplateFiles == null) return all;

            foreach (var asset in preset.roomTemplateFiles)
            {
                if (asset == null) continue;

                var parsed = DungeonRoomTemplateParser.Parse(
                    asset.text, preset.roomWidth, preset.roomHeight, asset.name);

                result.Errors.AddRange(parsed.Errors);
                result.Warnings.AddRange(parsed.Warnings);
                if (parsed.Success) all.AddRange(parsed.Templates);
            }
            return all;
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인**

Unity Console 에러 0건. 테스트는 사람이 실행 — `DungeonGeneratorTests` 8건 통과 예상.
`Write_RoundTripsThroughExistingParser`가 통과하면 생성 결과가 기존 임포터로 그대로 들어간다는 뜻이다. 이게 이 태스크의 핵심 검증이다.

- [ ] **Step 5: 체크포인트**

---

## Task 7: 최소 방 템플릿 세트와 프리셋 에셋

**Files:**
- Create: `Assets/DungeonMaps/Templates/cave_rooms.txt`
- Create: `Assets/DungeonMaps/DungeonGenPreset_Cave.asset` (Unity 메뉴로 생성 — 수동)
- Test: `Assets/Tests/EditMode/DungeonRoomTemplateAssetTests.cs`

**Interfaces:**
- Consumes: `DungeonRoomTemplateParser`, `RoomOpen`, `RoomRole` (Task 1~2)
- Produces: 방 10×6 기준 템플릿 12개. 경로 생성기가 낼 수 있는 모든 타입을 덮는다.

**경로 생성기가 낼 수 있는 타입은 정확히 9종이다.** 경로상의 각 Normal 방은 "들어온 면 1 + 나가는 면 1"로 항상 개구부가 2개다(같은 셀을 두 번 지나지 않으므로). 가능한 조합은 `LR` `LU` `LD` `UR` `UD` `RD` 6종 + `START R` + `END L` + `FILL`.

**M1 템플릿에는 슬롯 심볼(`?`/`^`/`%`)을 넣지 않는다.** M1은 슬롯을 해석하지 않으므로 그대로 스탬프되어 임포터가 "미매핑 오브젝트 심볼" 경고를 쏟는다. `E`/`X` 외의 오브젝트는 M2에서 추가한다.

- [ ] **Step 1: `Assets/DungeonMaps/Templates/cave_rooms.txt` 작성**

개구부 규약(설계 §4) — 방 10×6에서 **좌우 개구부는 행 2·3·4**(열 0 또는 9), **상하 개구부는 열 4·5·6**(행 0 또는 5). 개구부 자리에는 확률 타일을 쓰지 않는다.

```
# 동굴 던전 기본 방 세트. 방 크기 10x6.
# 개구부 규약: 좌우 = 행 2,3,4 / 상하 = 열 4,5,6. 개구부에는 확률 타일 금지.
# 확률 타일: 0=50% 벽, 1=25% 벽, 2=75% 벽

# room: START R
[TILES]
WWWWWWWWWW
W........W
W.........
W...0.....
W..WWW....
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
..E.......
..........
---
# room: END L
[TILES]
WWWWWWWWWW
W........W
.........W
.....0...W
....WWW..W
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
.......X..
..........
---
# room: LR
# weight: 3
[TILES]
WWWWWWWWWW
W..000...W
..0....0..
..........
..WWW.WW..
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
---
# room: LR
# weight: 2
[TILES]
WWWWWWWWWW
W...00...W
..........
...0..0...
....WW....
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
---
# room: LR
[TILES]
WWWWWWWWWW
W.0....0.W
..........
..WW..WW..
..........
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
---
# room: LD
[TILES]
WWWWWWWWWW
W........W
.........W
....0....W
.........W
WWWW...WWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
---
# room: RD
[TILES]
WWWWWWWWWW
W........W
W.........
W....0....
W.........
WWWW...WWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
---
# room: UR
[TILES]
WWWW...WWW
W........W
W.........
W..0......
W.........
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
---
# room: LU
[TILES]
WWWW...WWW
W........W
.........W
......0..W
.........W
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
---
# room: UD
[TILES]
WWWW...WWW
W........W
W...0.0..W
W........W
W........W
WWWW...WWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
---
# room: FILL
[TILES]
WWWWWWWWWW
WWW0000WWW
WW00..00WW
WW0....0WW
WWW0..0WWW
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
---
# room: FILL
[TILES]
WWWWWWWWWW
WWWWWWWWWW
WW2222222W
WW2222222W
WWWWWWWWWW
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
..........
..........
```

- [ ] **Step 2: 템플릿 에셋 검증 테스트 작성**

`Assets/Tests/EditMode/DungeonRoomTemplateAssetTests.cs`. 손으로 그린 격자는 행 길이가 한 칸 어긋나기 쉬우므로 자동 검증한다.

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonRoomTemplateAssetTests
{
    private const string AssetPath = "Assets/DungeonMaps/Templates/cave_rooms.txt";
    private const int RoomW = 10;
    private const int RoomH = 6;

    private static RoomTemplateParseResult Load()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
        Assert.IsNotNull(asset, $"{AssetPath} 를 찾지 못했습니다.");
        return DungeonRoomTemplateParser.Parse(asset.text, RoomW, RoomH, "cave_rooms");
    }

    [Test]
    public void CaveRooms_ParsesAtRoomSize()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));
        Assert.GreaterOrEqual(r.Templates.Count, 9);
    }

    [Test]
    public void CaveRooms_CoverEveryTypeThePathBuilderCanEmit()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));

        var required = new List<(RoomRole role, RoomOpen opens)>
        {
            (RoomRole.Start, RoomOpen.R),
            (RoomRole.End,   RoomOpen.L),
            (RoomRole.Normal, RoomOpen.L | RoomOpen.R),
            (RoomRole.Normal, RoomOpen.L | RoomOpen.U),
            (RoomRole.Normal, RoomOpen.L | RoomOpen.D),
            (RoomRole.Normal, RoomOpen.U | RoomOpen.R),
            (RoomRole.Normal, RoomOpen.U | RoomOpen.D),
            (RoomRole.Normal, RoomOpen.R | RoomOpen.D),
            (RoomRole.Fill,   RoomOpen.None),
        };

        foreach (var (role, opens) in required)
        {
            bool found = r.Templates.Exists(t => t.Role == role && t.Opens == opens);
            Assert.IsTrue(found, $"'{role} {RoomTypeUtil.FormatOpens(opens)}' 템플릿이 없습니다.");
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

    [Test]
    public void CaveRooms_ClosedEdgesAreNotAccidentallyOpen()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));

        foreach (var t in r.Templates)
        {
            string who = t.Describe();

            if ((t.Opens & RoomOpen.L) == 0)
                for (int row = 0; row < RoomH; row++)
                    Assert.AreNotEqual('.', t.Tiles[row, 0], $"[{who}] 닫힌 왼쪽 면 행 {row}이 뚫려 있음");

            if ((t.Opens & RoomOpen.R) == 0)
                for (int row = 0; row < RoomH; row++)
                    Assert.AreNotEqual('.', t.Tiles[row, RoomW - 1], $"[{who}] 닫힌 오른쪽 면 행 {row}이 뚫려 있음");
        }
    }

    [Test]
    public void CaveRooms_StartHasEntry_EndHasExit()
    {
        var r = Load();
        var start = r.Templates.Find(t => t.Role == RoomRole.Start);
        var end = r.Templates.Find(t => t.Role == RoomRole.End);
        Assert.IsNotNull(start);
        Assert.IsNotNull(end);
        Assert.IsTrue(Contains(start.Objects, 'E'), "START 방에 E가 없습니다.");
        Assert.IsTrue(Contains(end.Objects, 'X'), "END 방에 X가 없습니다.");
    }

    private static bool Contains(char[,] grid, char symbol)
    {
        for (int r = 0; r < grid.GetLength(0); r++)
            for (int c = 0; c < grid.GetLength(1); c++)
                if (grid[r, c] == symbol) return true;
        return false;
    }
}
```

- [ ] **Step 3: 프리셋 에셋 생성 (Unity 수동 작업)**

1. Project 창에서 `Assets/DungeonMaps/` 우클릭 → **Create > Dungeon > Generation Preset**
2. 이름을 `DungeonGenPreset_Cave`로
3. 인스펙터에서 설정:
   - Grid Width `4`, Grid Height `3`
   - Room Width `10`, Room Height `6`
   - Room Template Files → `+` → `cave_rooms.txt` 드래그
   - 나머지는 기본값

- [ ] **Step 4: 컴파일·검증**

Unity Console 에러 0건. 테스트는 사람이 실행 — `DungeonRoomTemplateAssetTests` 5건 통과 예상.
이 테스트가 실패하면 템플릿 격자의 행 길이나 개구부 위치가 어긋난 것이다. 실패 메시지가 어느 방의 어느 행인지 알려준다.

- [ ] **Step 5: 체크포인트**

---

## Task 8: 에디터 Generate 섹션

**Files:**
- Modify: `Assets/Scripts/Editor/Dungeon/DungeonMapImporter.cs`

**Interfaces:**
- Consumes: `DungeonGenPresetSO`, `DungeonGenerator`, `GenerationResult`, `DungeonMapWriter` (Task 3·6)
- Produces: 에디터 전용. 다른 태스크가 의존하지 않는다.

**목표 흐름:** 주사위로 시드를 굴려 ASCII 프리뷰로 골격을 훑고, 괜찮으면 `Save & Stamp`로 씬에 실제 타일을 찍는다. `Generate`는 디스크를 건드리지 않아 반복이 빠르다.

- [ ] **Step 1: 스탬프 경로를 재사용 가능하게 분리**

기존 `Import()`은 파싱과 스탬프가 한 덩어리라 생성 결과를 찍을 수 없다. 스탬프 부분을 `StampMap(DungeonMapData)`으로 뽑아낸다.

`Import()`을 아래로 교체한다:

```csharp
    private void Import()
    {
        var result = DungeonMapParser.Parse(_mapAsset.text);
        if (!result.Success)
        {
            EditorUtility.DisplayDialog("Import 실패", string.Join("\n", result.Errors), "확인");
            return;
        }
        StampMap(result.Data);
    }

    /// <summary>파싱된 맵을 Map Root 아래에 스탬프한다. 텍스트 임포트와 생성 결과가 공유하는 경로.</summary>
    private void StampMap(DungeonMapData data)
    {
        _tileset.BuildLookup();
```

그리고 **기존 `Import()`의 나머지 본문**(`Undo.RegisterFullObjectHierarchyUndo(...)` 줄부터 `EditorSceneMarkDirty();` 까지)을 그대로 `StampMap` 안으로 옮긴다. 본문은 이미 지역 변수 `data`만 참조하므로 다른 수정이 필요 없다. 옮긴 뒤 `StampMap` 위쪽에 있던 `var data = result.Data;` 줄은 삭제한다.

- [ ] **Step 2: using과 필드 추가**

파일 상단 using에 두 줄을 추가한다:

```csharp
using System.IO;
using Gameplay.Dungeon.Authoring.Generation;
```

클래스 필드 선언부(`private bool _clearFirst = true;` 아래)에 추가한다:

```csharp
    // --- Generate 섹션 ---
    private const string GeneratedFolder = "Assets/DungeonMaps/Generated";

    private DungeonGenPresetSO _preset;
    private int _seed = 12345;
    private DungeonMapData _generated;
    private string _previewText = "";
    private readonly List<string> _genErrors = new List<string>();
    private readonly List<string> _genWarnings = new List<string>();
    private Vector2 _previewScroll;
    private GUIStyle _monoStyle;

    /// <summary>ASCII 프리뷰는 등폭 폰트여야 격자가 어긋나 보이지 않는다.</summary>
    private GUIStyle MonoStyle
    {
        get
        {
            if (_monoStyle == null)
            {
                _monoStyle = new GUIStyle(EditorStyles.label) { wordWrap = false, richText = false };
                var mono = Font.CreateDynamicFontFromOSFont("Consolas", 11);
                if (mono != null) _monoStyle.font = mono;
            }
            return _monoStyle;
        }
    }
```

- [ ] **Step 3: `OnGUI`를 두 섹션으로 분리**

기존 `OnGUI()` 전체를 아래로 교체한다. 기존 내용은 `DrawImportSection()`으로 그대로 들어간다.

```csharp
    private void OnGUI()
    {
        DrawGenerateSection();

        EditorGUILayout.Space(10);
        var line = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(line, new Color(0.35f, 0.35f, 0.35f));
        EditorGUILayout.Space(10);

        DrawImportSection();
    }

    private void DrawImportSection()
    {
        EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Map Root: 맵 전체를 담을 빈 오브젝트. 이 아래의 Tilemap을 찾아 타일을 칠하고,\n" +
            "'Objects' 컨테이너에 오브젝트를 배치합니다. Tilemap이 없으면 자동 생성(콜라이더/스케일은 직접 설정).",
            MessageType.Info);

        _mapRoot = (Transform)EditorGUILayout.ObjectField("Map Root", _mapRoot, typeof(Transform), true);
        _tileset = (DungeonTilesetSO)EditorGUILayout.ObjectField("Tileset", _tileset, typeof(DungeonTilesetSO), false);
        _mapAsset = (TextAsset)EditorGUILayout.ObjectField("Map (.txt)", _mapAsset, typeof(TextAsset), false);
        _clearFirst = EditorGUILayout.Toggle("Clear Before Import", _clearFirst);

        using (new EditorGUI.DisabledScope(_mapRoot == null || _tileset == null || _mapAsset == null))
            if (GUILayout.Button("Import"))
                Import();
    }
```

- [ ] **Step 4: Generate 섹션 구현**

`DrawImportSection()` 아래에 추가한다.

```csharp
    private void DrawGenerateSection()
    {
        EditorGUILayout.LabelField("Generate", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "프리셋과 시드로 던전 맵을 생성합니다. Generate는 디스크를 건드리지 않고 프리뷰만 갱신합니다.\n" +
            "Save & Stamp는 아래 Import 섹션의 Map Root·Tileset 설정을 그대로 사용합니다.",
            MessageType.Info);

        _preset = (DungeonGenPresetSO)EditorGUILayout.ObjectField("Preset", _preset, typeof(DungeonGenPresetSO), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            _seed = EditorGUILayout.IntField("Seed", _seed);
            using (new EditorGUI.DisabledScope(_preset == null))
                if (GUILayout.Button("Random", GUILayout.Width(70)))
                {
                    _seed = new System.Random().Next(1, int.MaxValue);
                    DoGenerate();
                }
        }

        using (new EditorGUI.DisabledScope(_preset == null))
            if (GUILayout.Button("Generate"))
                DoGenerate();

        if (_genErrors.Count > 0)
            EditorGUILayout.HelpBox("에러:\n" + string.Join("\n", _genErrors), MessageType.Error);
        if (_genWarnings.Count > 0)
            EditorGUILayout.HelpBox($"경고 {_genWarnings.Count}건:\n" + string.Join("\n", _genWarnings), MessageType.Warning);

        if (!string.IsNullOrEmpty(_previewText))
        {
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(240));
            float h = MonoStyle.CalcSize(new GUIContent(_previewText)).y;
            EditorGUILayout.SelectableLabel(_previewText, MonoStyle, GUILayout.ExpandWidth(true), GUILayout.Height(h));
            EditorGUILayout.EndScrollView();
        }

        using (new EditorGUI.DisabledScope(_generated == null))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save Only"))
                SaveGenerated();

            using (new EditorGUI.DisabledScope(_mapRoot == null || _tileset == null))
                if (GUILayout.Button("Save & Stamp"))
                {
                    if (SaveGenerated() != null) StampMap(_generated);
                }
        }
    }

    private void DoGenerate()
    {
        _genErrors.Clear();
        _genWarnings.Clear();
        _generated = null;
        _previewText = "";

        if (_preset == null) return;

        var result = DungeonGenerator.Generate(_preset, _seed);
        _genErrors.AddRange(result.Errors);
        _genWarnings.AddRange(result.Warnings);
        if (!result.Success) return;

        _generated = result.Data;
        _previewText = BuildPreview(result.Data);
    }

    // 오브젝트가 있는 칸은 오브젝트 심볼로, 나머지는 타일 심볼로 그린다.
    private static string BuildPreview(DungeonMapData d)
    {
        var sb = new System.Text.StringBuilder(d.Height * (d.Width + 1));
        for (int r = 0; r < d.Height; r++)
        {
            for (int c = 0; c < d.Width; c++)
                sb.Append(d.Objects[r, c] != '.' ? d.Objects[r, c] : d.Tiles[r, c]);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>생성 결과를 txt로 저장하고 에셋 경로를 반환한다. 실패 시 null.</summary>
    private string SaveGenerated()
    {
        if (_generated == null) return null;

        if (!Directory.Exists(GeneratedFolder))
        {
            Directory.CreateDirectory(GeneratedFolder);
            AssetDatabase.Refresh();
        }

        string path = $"{GeneratedFolder}/gen_{_seed}.txt";
        string text = DungeonMapWriter.Write(_generated, _preset != null ? _preset.name : "", _seed);

        File.WriteAllText(path, text);
        AssetDatabase.ImportAsset(path);

        Debug.Log($"[DungeonGen] 저장: {path} ({_generated.Width}x{_generated.Height})");
        return path;
    }
```

- [ ] **Step 5: 수동 확인**

1. Unity 컴파일 에러 0건
2. **Tools > Dungeon > Map Importer** 열기
3. Preset에 `DungeonGenPreset_Cave` 지정 → `Generate` → 프리뷰에 격자가 뜨는지 확인
   - `E`가 왼쪽에, `X`가 오른쪽에 하나씩 보여야 한다
   - 바깥 테두리가 전부 `W`여야 한다
   - `0`/`1`/`2` 같은 미해석 심볼이 남아 있으면 안 된다
4. `Random`을 여러 번 눌러 맵이 매번 달라지는지 확인
5. 같은 시드를 다시 입력하고 `Generate` → 같은 맵이 나오는지 확인
6. Map Root·Tileset을 지정하고 `Save & Stamp` → 씬에 타일이 찍히고 `Assets/DungeonMaps/Generated/gen_<seed>.txt`가 생기는지 확인

- [ ] **Step 6: 체크포인트**

여기까지가 M1이다. 이 시점에 "맵이 어떤 느낌인지" 판단할 수 있다. 템플릿과 확률값을 조정하는 것은 코드가 아니라 데이터 작업이다.

---

## M1 이후 확인할 것

M1을 돌려보고 판단해야 하는 항목들. 지금 결정하지 않는다.

- **세로 이동 구간을 오를 수단이 없다.** 경로가 위로 꺾이면(`LU`/`UR`/`UD`) 플레이어가 올라갈 방법이 필요하다. M1 템플릿에는 사다리가 없다. 프리뷰를 보고 (a) 사다리 심볼을 타일셋에 추가할지 (b) `horizontalChance`를 높여 세로 이동을 줄일지 (c) 위로 가는 경로를 금지할지 정한다.
- **방 크기 10×6이 카메라(화면 높이 5유닛)에 맞는지.** 세로가 답답하면 `roomHeight`를 줄인다. 단, 템플릿을 전부 다시 그려야 한다.
- **격자 크기.** 기본 4×3(42×20 타일)에서 시작해 늘려본다.
- **M2 진입 조건:** 지형 골격이 마음에 들면 슬롯(`?`/`^`/`%`)과 링크 재배정으로 넘어간다.
