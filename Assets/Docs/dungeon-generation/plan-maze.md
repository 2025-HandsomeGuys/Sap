# 던전 미로 배선 + 형태 마스크 구현 계획

> **작업자용:** 태스크 단위로 순서대로 진행한다. 각 태스크 끝에서 Unity 컴파일 에러 0을 확인하고 다음으로 넘어간다.

**목표:** 던전 생성기를 "가로 워커 + 밀봉된 FILL 방"에서 "형태 마스크 + 미로 배선 + 도달 불가 공간 메우기"로 바꾼다.

**아키텍처:** 격자 형태를 텍스트 마스크로 그리고(`S`/`X`/`.`/`#`), 그 위에 랜덤 DFS 스패닝 트리로 모든 칸을 잇고, 추가 연결로 순환로를 만든다. 조립 후 `E`/`X`를 바닥에 후처리로 찍고, `E`에서 플러드필해 닿지 않는 빈칸을 전부 벽으로 메운다.

**기술 스택:** C# / Unity EditMode 테스트(NUnit). 생성기는 `UnityEditor`를 참조하지 않는다.

**설계 문서:** `Assets/Docs/dungeon-generation/maze-and-shape-design.md`

## Global Constraints

- **git을 쓰지 않는다.** 이 프로젝트는 UVCS다. 커밋 단계는 없다 — 각 태스크는 "변경 파일 확인 + Unity 콘솔 컴파일 에러 0"으로 끝난다.
- **Unity 테스트 실행은 사람이 한다.** 테스트는 작성하고 그대로 다음 태스크로 넘어간다. 실행 결과를 게이트로 삼지 않는다.
- 생성 코드(`Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/**`)는 `UnityEditor` 네임스페이스를 쓰지 않는다.
- 방 크기 10×6, 개구부 위치 규약(좌우 = 행 2·3·4 / 상하 = 열 4·5·6, 그 자리에 확률 타일 금지)은 그대로다.
- 파일 맨 위에 `// @tags: ...` 주석을 단다(프로젝트 검색 규약).
- 격자 인덱싱은 전부 `[row, col]`, row 0이 맨 위.

---

## Task 1: 암반 칸 개념 도입 (`RoomRole.Fill` → `Rock`)

경로 밖 밀봉 방을 없애고 통짜 암반으로 바꾼다. **이 태스크만으로 화면의 "암반 속 빈 주머니"가 사라진다.**

**Files:**
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/RoomTypes.cs`
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonPathBuilder.cs`
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonRoomComposer.cs`
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonRoomFiller.cs`
- Modify: `Assets/Tests/EditMode/DungeonRoomFillerTests.cs`
- Modify: `Assets/Tests/EditMode/DungeonRoomComposerTests.cs`

**Interfaces:**
- Produces: `RoomRole.Rock`(암반 칸), `DungeonRoomFiller.Fill(ComposeResult, DungeonGenPresetSO, System.Random)` — `plan` 파라미터가 빠진다.

- [ ] **Step 1: `RoomTypes.cs`의 열거형 교체**

`RoomRole`을 이렇게 바꾼다.

```csharp
    /// <summary>
    /// 격자 한 칸의 역할. Start/End는 E·X가 놓일 자리 표시일 뿐 템플릿 선택에는 쓰지 않는다.
    /// Rock은 방이 아니라 통짜 암반 — 템플릿을 찍지 않고 벽으로 남긴다.
    /// </summary>
    public enum RoomRole
    {
        Normal,
        Start,
        End,
        Rock,
    }
```

- [ ] **Step 2: `DungeonPathBuilder.cs`에서 `Fill` 참조 치환**

`Build` 안의 초기화 루프와 워커 진행부 3곳이 `RoomRole.Fill`을 쓴다. 전부 `RoomRole.Rock`으로 바꾼다.

```csharp
                    grid[r, c] = new RoomPlan { Role = RoomRole.Rock, Opens = RoomOpen.None };
```
```csharp
                        if (grid[row, col].Role == RoomRole.Rock) grid[row, col].Role = RoomRole.Normal;
```

(Task 5에서 이 파일 전체가 미로 배선으로 교체된다. 여기서는 컴파일만 맞춘다.)

- [ ] **Step 3: `DungeonRoomComposer.cs` — 암반 칸은 템플릿을 찍지 않는다**

조립 루프를 이렇게 바꾼다.

```csharp
            for (int gr = 0; gr < gridH; gr++)
            for (int gc = 0; gc < gridW; gc++)
            {
                var cell = plan[gr, gc];

                // 암반 칸은 아무것도 안 한다 — 최종 격자가 이미 벽으로 초기화돼 있어 통짜로 남는다.
                if (cell.Role == RoomRole.Rock) continue;

                var template = Pick(cell.Role, cell.Opens, templates, rng, res.Warnings);

                if (template == null)
                    StampBlank(res, gr, gc, preset);
                else
                    StampTemplate(res, template, gr, gc, preset, rng);
            }
```

- [ ] **Step 4: `DungeonRoomFiller.cs` — FILL 방 보상 제외 규칙 제거**

암반 칸에는 방이 없으니 보상 후보도 생기지 않는다. `IsFillRoom`과 `plan` 파라미터를 지운다.

`Fill` 시그니처:
```csharp
        public static void Fill(ComposeResult res, DungeonGenPresetSO preset, System.Random rng)
        {
            if (res == null) throw new System.ArgumentNullException(nameof(res));
            if (preset == null) throw new System.ArgumentNullException(nameof(preset));
            if (rng == null) throw new System.ArgumentNullException(nameof(rng));

            int buried = FillTrapSlots(res, preset, rng);
            buried += FillRewardSlots(res, preset, rng);
            ...
```

`FillRewardSlots`에서 `plan` 파라미터와 아래 블록을 삭제한다.
```csharp
                // 삭제할 블록
                if (IsFillRoom(plan, r, c, preset))
                {
                    res.Objects[r, c] = None;
                    continue;
                }
```
`IsFillRoom` 메서드 전체도 삭제한다.

- [ ] **Step 5: `DungeonGenerator.cs` 호출부 수정**

```csharp
            DungeonRoomFiller.Fill(composed, preset, rng);
```

- [ ] **Step 6: 테스트 수정**

`DungeonRoomFillerTests.cs`에서 `OneRoom(...)` 헬퍼와 그 인자를 제거하고 호출을 전부 바꾼다.

```csharp
        DungeonRoomFiller.Fill(res, _preset, new System.Random(1));
```

`Reward_FillRoomCandidatesExcluded` 테스트는 삭제한다(규칙 자체가 없어졌다).

`DungeonRoomComposerTests.cs`에서 `RoomRole.Fill`을 쓰는 곳을 `RoomRole.Rock`으로 바꾸고, 암반 칸이 통짜 벽으로 남는지 확인하는 테스트를 추가한다.

```csharp
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
```

- [ ] **Step 7: 컴파일 확인** — Unity 콘솔 에러 0.

---

## Task 2: 형태 마스크 데이터와 파서

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonShapeMask.cs`
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonShapeParser.cs`
- Test: `Assets/Tests/EditMode/DungeonShapeParserTests.cs`

**Interfaces:**
- Produces: `DungeonShapeMask`(`Name`/`Weight`/`Width`/`Height`/`Cells`/`StartRow`/`StartCol`/`EndRow`/`EndCol`/`IsDungeon(row,col)`), `DungeonShapeParser.Parse(string text, string sourceName) → ShapeParseResult`(`Success`/`Masks`/`Errors`/`Warnings`)

- [ ] **Step 1: `DungeonShapeMask.cs` 작성**

```csharp
// @tags: dungeon, generation, shape, mask, data
namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 던전 격자의 형태 1종. Cells는 [row, col], row 0이 맨 위.
    /// 가로형·세로형·ㄱ자를 전부 이 한 장으로 표현한다 — 형태를 늘려도 코드는 안 바뀐다.
    /// </summary>
    public class DungeonShapeMask
    {
        public const char DungeonCell = '.';
        public const char RockCell = '#';
        public const char StartCell = 'S';
        public const char EndCell = 'X';

        public string Name = "";
        public int Weight = 1;

        public int Width;
        public int Height;
        public char[,] Cells;

        public int StartRow, StartCol;
        public int EndRow, EndCol;

        /// <summary>방이 놓이는 칸인가. 암반(#)과 격자 밖은 false.</summary>
        public bool IsDungeon(int row, int col)
        {
            if (row < 0 || row >= Height || col < 0 || col >= Width) return false;
            char c = Cells[row, col];
            return c == DungeonCell || c == StartCell || c == EndCell;
        }
    }
}
```

- [ ] **Step 2: `DungeonShapeParserTests.cs` 작성 (실패하는 테스트)**

```csharp
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
```

- [ ] **Step 3: `DungeonShapeParser.cs` 작성**

```csharp
// @tags: dungeon, generation, shape, mask, parser
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    public class ShapeParseResult
    {
        public bool Success;
        public readonly List<DungeonShapeMask> Masks = new List<DungeonShapeMask>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 형태 마스크 파일을 파싱한다. 파일 하나에 형태 여러 개, '---' 한 줄로 구분.
    ///   # shape: &lt;이름&gt;      경고 메시지용
    ///   # weight: N            형태 추첨 가중치 (생략 시 1)
    ///   격자: S=시작 X=끝 .=던전 칸 #=암반
    ///
    /// '#'은 주석 시작 문자이면서 암반 칸 문자이기도 하다. **'#' 뒤에 공백이 오면 주석**으로 본다 —
    /// 격자 행에는 공백이 들어갈 일이 없어 충돌하지 않는다.
    /// </summary>
    public static class DungeonShapeParser
    {
        public static ShapeParseResult Parse(string text, string sourceName)
        {
            var result = new ShapeParseResult();
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
                    var m = ParseBlock(block, sourceName, blockIndex, blockStartLine, result);
                    if (m != null) result.Masks.Add(m);
                    blockIndex++;
                }
                block.Clear();
                blockStartLine = i + 2;
            }

            if (result.Masks.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add($"[{sourceName}] 형태를 하나도 찾지 못했습니다.");

            result.Success = result.Errors.Count == 0;
            return result;
        }

        private static bool HasContent(List<string> block)
        {
            foreach (string l in block)
                if (l.Trim().Length > 0) return true;
            return false;
        }

        private static bool IsComment(string line)
        {
            string t = line.TrimStart();
            if (t.Length == 0 || t[0] != '#') return false;
            return t.Length == 1 || char.IsWhiteSpace(t[1]);
        }

        private static DungeonShapeMask ParseBlock(
            List<string> block, string sourceName, int blockIndex, int startLine, ShapeParseResult result)
        {
            string tag = $"[{sourceName} #{blockIndex} (line {startLine})]";

            var mask = new DungeonShapeMask();
            var rows = new List<string>();

            foreach (string raw in block)
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0) continue;

                if (IsComment(line))
                {
                    string body = line.TrimStart().TrimStart('#').Trim();
                    if (body.StartsWith("shape:"))
                        mask.Name = body.Substring("shape:".Length).Trim();
                    else if (body.StartsWith("weight:"))
                    {
                        if (int.TryParse(body.Substring("weight:".Length).Trim(), out int w) && w > 0)
                            mask.Weight = w;
                        else
                            result.Warnings.Add($"{tag} weight 값을 읽지 못해 1로 둡니다.");
                    }
                    continue;
                }

                rows.Add(line);
            }

            if (rows.Count == 0)
            {
                result.Errors.Add($"{tag} 격자가 비어 있습니다.");
                return null;
            }

            int width = rows[0].Length;
            for (int r = 0; r < rows.Count; r++)
                if (rows[r].Length != width)
                {
                    result.Errors.Add($"{tag} {r}행 길이가 {rows[r].Length}입니다 — 모든 행이 {width}이어야 합니다.");
                    return null;
                }

            mask.Width = width;
            mask.Height = rows.Count;
            mask.Cells = new char[mask.Height, mask.Width];

            int starts = 0, ends = 0;
            for (int r = 0; r < mask.Height; r++)
                for (int c = 0; c < mask.Width; c++)
                {
                    char ch = rows[r][c];
                    switch (ch)
                    {
                        case DungeonShapeMask.DungeonCell:
                        case DungeonShapeMask.RockCell:
                            break;
                        case DungeonShapeMask.StartCell:
                            starts++; mask.StartRow = r; mask.StartCol = c; break;
                        case DungeonShapeMask.EndCell:
                            ends++; mask.EndRow = r; mask.EndCol = c; break;
                        default:
                            result.Errors.Add($"{tag} ({r},{c})에 알 수 없는 문자 '{ch}' — S/X/./# 만 쓸 수 있습니다.");
                            return null;
                    }
                    mask.Cells[r, c] = ch;
                }

            if (starts != 1) { result.Errors.Add($"{tag} S가 {starts}개입니다 — 정확히 1개여야 합니다."); return null; }
            if (ends != 1) { result.Errors.Add($"{tag} X가 {ends}개입니다 — 정확히 1개여야 합니다."); return null; }

            if (!DemoteDisconnected(mask, tag, result)) return null;
            return mask;
        }

        /// <summary>
        /// S에서 4방향으로 닿는 칸만 남기고 나머지 던전 칸은 암반으로 강등한다.
        /// X에 못 닿으면 형태 자체가 틀린 것이므로 에러.
        /// </summary>
        private static bool DemoteDisconnected(DungeonShapeMask mask, string tag, ShapeParseResult result)
        {
            var reached = new bool[mask.Height, mask.Width];
            var queue = new Queue<(int row, int col)>();

            reached[mask.StartRow, mask.StartCol] = true;
            queue.Enqueue((mask.StartRow, mask.StartCol));

            int[] dr = { 0, 0, -1, 1 };
            int[] dc = { -1, 1, 0, 0 };

            while (queue.Count > 0)
            {
                var (r, c) = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    int nr = r + dr[i], nc = c + dc[i];
                    if (!mask.IsDungeon(nr, nc) || reached[nr, nc]) continue;
                    reached[nr, nc] = true;
                    queue.Enqueue((nr, nc));
                }
            }

            if (!reached[mask.EndRow, mask.EndCol])
            {
                result.Errors.Add($"{tag} S에서 X까지 이어지지 않습니다.");
                return false;
            }

            int demoted = 0;
            for (int r = 0; r < mask.Height; r++)
                for (int c = 0; c < mask.Width; c++)
                    if (mask.IsDungeon(r, c) && !reached[r, c])
                    {
                        mask.Cells[r, c] = DungeonShapeMask.RockCell;
                        demoted++;
                    }

            if (demoted > 0)
                result.Warnings.Add($"{tag} S와 이어지지 않은 칸 {demoted}개를 암반으로 바꿨습니다.");

            return true;
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인**

---

## Task 3: 형태 에셋 `basic_shapes.txt`

**Files:**
- Create: `Assets/DungeonMaps/Shapes/basic_shapes.txt`
- Test: `Assets/Tests/EditMode/DungeonShapeAssetTests.cs`

- [ ] **Step 1: `basic_shapes.txt` 작성**

```
# 던전 격자 형태. S=시작 X=끝 .=던전 칸 #=암반(던전 아님)
# 한 칸 = 방 하나(10x6 타일). 최종 타일 크기 = (가로칸*10+2) x (세로칸*6+2)
# 미로라 모든 칸이 방이 된다 — 칸 수를 늘리면 탐험 시간이 그대로 늘어난다.

# shape: 가로형
# weight: 3
S....
....X
---
# shape: 세로형
# weight: 2
S..
...
...
..X
---
# shape: ㄱ자
# weight: 2
S...
....
##..
##.X
---
# shape: 계단형
S.##
..##
....
##.X
```

칸 수는 각각 10 / 12 / 12 / 10이다. **미로라서 모든 칸이 방이 된다** — 지금(경로만 10칸)보다
체감이 훨씬 넓어지므로 작게 시작한다. 심심하면 칸을 늘린다(코드 변경 없음).

- [ ] **Step 2: 에셋 테스트 작성**

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonShapeAssetTests
{
    private const string AssetPath = "Assets/DungeonMaps/Shapes/basic_shapes.txt";

    private static ShapeParseResult Load()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
        Assert.IsNotNull(asset, $"{AssetPath} 를 찾지 못했습니다.");
        return DungeonShapeParser.Parse(asset.text, "basic_shapes");
    }

    [Test]
    public void BasicShapes_ParseWithoutErrorOrWarning()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));
        Assert.AreEqual(0, r.Warnings.Count, string.Join("\n", r.Warnings));
        Assert.GreaterOrEqual(r.Masks.Count, 3);
    }

    // 칸 수가 늘면 탐험 시간이 그대로 늘어난다. 눈에 띄게 커지면 의도한 것인지 확인하게 만든다.
    [Test]
    public void BasicShapes_StayWithinRoomBudget()
    {
        var r = Load();
        foreach (var m in r.Masks)
        {
            int rooms = 0;
            for (int row = 0; row < m.Height; row++)
                for (int col = 0; col < m.Width; col++)
                    if (m.IsDungeon(row, col)) rooms++;

            Assert.LessOrEqual(rooms, 14, $"'{m.Name}' 방 {rooms}칸 — 14칸을 넘으면 던전이 너무 커진다");
            Assert.GreaterOrEqual(rooms, 6, $"'{m.Name}' 방 {rooms}칸 — 너무 작다");
        }
    }
}
```

- [ ] **Step 3: Unity에서 에셋 임포트 확인** — `Assets/DungeonMaps/Shapes/` 폴더가 만들어지고 txt가 `TextAsset`으로 잡히는지.

---

## Task 4: 프리셋에 형태 필드 추가

기존 필드는 아직 지우지 않는다(Task 5에서 소비처와 함께 제거).

**Files:**
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonGenPresetSO.cs`
- Modify: `Assets/DungeonMaps/DungeonGenPreset.asset`
- Modify: `Assets/Tests/EditMode/DungeonGenPresetTests.cs`

**Interfaces:**
- Produces: `preset.shapeFiles`(`List<TextAsset>`), `preset.extraConnectionChance`(`float`)

- [ ] **Step 1: 필드 추가**

`roomTemplateFiles` 아래에 넣는다.

```csharp
        [Header("형태 마스크 파일")]
        [Tooltip("던전 격자의 모양. 매판 weight로 하나 뽑는다. 가로·세로·ㄱ자를 텍스트로 그린다.")]
        public List<TextAsset> shapeFiles = new List<TextAsset>();

        [Header("미로")]
        [Tooltip("모든 칸을 잇는 트리를 만든 뒤, 남은 벽을 이 확률로 더 뚫어 순환로를 만든다. " +
                 "0이면 갈림길 없는 나무 구조(막다른 길만 있는 미로), 1이면 격자가 통째로 뚫린다.")]
        [Range(0f, 1f)] public float extraConnectionChance = 0.25f;
```

- [ ] **Step 2: 프리셋 에셋에 값 기록**

`DungeonGenPreset.asset`의 `roomTemplateFiles` 블록 다음에 넣는다. `<basic_shapes.txt의 guid>`는
`Assets/DungeonMaps/Shapes/basic_shapes.txt.meta`에서 복사한다.

```yaml
  shapeFiles:
  - {fileID: 4900000, guid: <basic_shapes.txt의 guid>, type: 3}
  extraConnectionChance: 0.25
```

- [ ] **Step 3: 테스트 추가**

```csharp
    [Test]
    public void MazeDefaults_HaveExtraConnectionChance()
    {
        Assert.AreEqual(0.25f, _preset.extraConnectionChance, 0.0001f);
        Assert.IsNotNull(_preset.shapeFiles);
    }
```

- [ ] **Step 4: 컴파일 확인**

---

## Task 5: 미로 배선 (`DungeonPathBuilder` 교체)

**Files:**
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonPathBuilder.cs` (전면 교체)
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonGenerator.cs`
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonGenPresetSO.cs` (구 필드 제거)
- Modify: `Assets/DungeonMaps/DungeonGenPreset.asset` (구 필드 제거)
- Test: `Assets/Tests/EditMode/DungeonPathBuilderTests.cs` (전면 교체)

**Interfaces:**
- Consumes: `DungeonShapeMask`, `preset.extraConnectionChance`
- Produces: `DungeonPathBuilder.Build(DungeonShapeMask mask, float extraConnectionChance, System.Random rng) → RoomPlan[,]`

- [ ] **Step 1: 테스트 전면 교체**

```csharp
using NUnit.Framework;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonPathBuilderTests
{
    private static DungeonShapeMask Mask(params string[] rows)
    {
        var r = DungeonShapeParser.Parse(string.Join("\n", rows) + "\n", "test");
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        return r.Masks[0];
    }

    // 개구부는 반드시 양쪽에 대칭으로 선다. 한쪽만 서면 방 하나가 벽을 보고 서 있게 된다.
    private static void AssertSymmetric(RoomPlan[,] plan)
    {
        int h = plan.GetLength(0), w = plan.GetLength(1);
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                if (c + 1 < w)
                    Assert.AreEqual((plan[r, c].Opens & RoomOpen.R) != 0,
                                    (plan[r, c + 1].Opens & RoomOpen.L) != 0, $"({r},{c}) 좌우 비대칭");
                if (r + 1 < h)
                    Assert.AreEqual((plan[r, c].Opens & RoomOpen.D) != 0,
                                    (plan[r + 1, c].Opens & RoomOpen.U) != 0, $"({r},{c}) 상하 비대칭");
            }
    }

    // 개구부를 따라 S에서 모든 던전 칸에 닿는지.
    private static int CountReachable(RoomPlan[,] plan, DungeonShapeMask mask)
    {
        var seen = new bool[plan.GetLength(0), plan.GetLength(1)];
        var stack = new System.Collections.Generic.Stack<(int r, int c)>();
        seen[mask.StartRow, mask.StartCol] = true;
        stack.Push((mask.StartRow, mask.StartCol));
        int n = 0;

        while (stack.Count > 0)
        {
            var (r, c) = stack.Pop();
            n++;
            var o = plan[r, c].Opens;
            if ((o & RoomOpen.L) != 0 && !seen[r, c - 1]) { seen[r, c - 1] = true; stack.Push((r, c - 1)); }
            if ((o & RoomOpen.R) != 0 && !seen[r, c + 1]) { seen[r, c + 1] = true; stack.Push((r, c + 1)); }
            if ((o & RoomOpen.U) != 0 && !seen[r - 1, c]) { seen[r - 1, c] = true; stack.Push((r - 1, c)); }
            if ((o & RoomOpen.D) != 0 && !seen[r + 1, c]) { seen[r + 1, c] = true; stack.Push((r + 1, c)); }
        }
        return n;
    }

    private static int CountDungeonCells(DungeonShapeMask mask)
    {
        int n = 0;
        for (int r = 0; r < mask.Height; r++)
            for (int c = 0; c < mask.Width; c++)
                if (mask.IsDungeon(r, c)) n++;
        return n;
    }

    [Test]
    public void Build_EveryDungeonCellIsReachable()
    {
        var mask = Mask("S....", ".....", "##...", "....X");
        var plan = DungeonPathBuilder.Build(mask, 0.25f, new System.Random(7));

        Assert.AreEqual(CountDungeonCells(mask), CountReachable(plan, mask), "고립된 방이 있다");
    }

    [Test]
    public void Build_OpensAreSymmetric()
    {
        var mask = Mask("S....", ".....", "##...", "....X");
        AssertSymmetric(DungeonPathBuilder.Build(mask, 0.5f, new System.Random(3)));
    }

    [Test]
    public void Build_RockCellsHaveNoOpens()
    {
        var mask = Mask("S....", ".....", "##...", "....X");
        var plan = DungeonPathBuilder.Build(mask, 1f, new System.Random(3));

        for (int r = 0; r < mask.Height; r++)
            for (int c = 0; c < mask.Width; c++)
                if (!mask.IsDungeon(r, c))
                {
                    Assert.AreEqual(RoomRole.Rock, plan[r, c].Role, $"({r},{c})");
                    Assert.AreEqual(RoomOpen.None, plan[r, c].Opens, $"({r},{c})");
                }
    }

    // 추가 연결 0이면 스패닝 트리 = 간선 수가 (칸 수 - 1)이어야 한다.
    [Test]
    public void Build_NoExtraConnections_IsSpanningTree()
    {
        var mask = Mask("S....", ".....", ".....", "....X");
        var plan = DungeonPathBuilder.Build(mask, 0f, new System.Random(11));

        int edges = 0;
        for (int r = 0; r < mask.Height; r++)
            for (int c = 0; c < mask.Width; c++)
            {
                if ((plan[r, c].Opens & RoomOpen.R) != 0) edges++;
                if ((plan[r, c].Opens & RoomOpen.D) != 0) edges++;
            }

        Assert.AreEqual(CountDungeonCells(mask) - 1, edges, "사이클이 생겼다");
    }

    [Test]
    public void Build_ExtraConnections_AddCycles()
    {
        var mask = Mask("S....", ".....", ".....", "....X");
        var tree = DungeonPathBuilder.Build(mask, 0f, new System.Random(11));
        var looped = DungeonPathBuilder.Build(mask, 1f, new System.Random(11));

        Assert.Greater(CountEdges(looped), CountEdges(tree));
    }

    private static int CountEdges(RoomPlan[,] plan)
    {
        int edges = 0;
        for (int r = 0; r < plan.GetLength(0); r++)
            for (int c = 0; c < plan.GetLength(1); c++)
            {
                if ((plan[r, c].Opens & RoomOpen.R) != 0) edges++;
                if ((plan[r, c].Opens & RoomOpen.D) != 0) edges++;
            }
        return edges;
    }

    [Test]
    public void Build_StartAndEndRolesComeFromMask()
    {
        var mask = Mask("S..X");
        var plan = DungeonPathBuilder.Build(mask, 0f, new System.Random(1));

        Assert.AreEqual(RoomRole.Start, plan[0, 0].Role);
        Assert.AreEqual(RoomRole.End, plan[0, 3].Role);
        Assert.AreEqual(RoomRole.Normal, plan[0, 1].Role);
    }

    [Test]
    public void Build_SameSeed_SamePlan()
    {
        var mask = Mask("S....", ".....", "....X");
        var a = DungeonPathBuilder.Build(mask, 0.5f, new System.Random(42));
        var b = DungeonPathBuilder.Build(mask, 0.5f, new System.Random(42));

        for (int r = 0; r < mask.Height; r++)
            for (int c = 0; c < mask.Width; c++)
                Assert.AreEqual(a[r, c].Opens, b[r, c].Opens, $"({r},{c})");
    }
}
```

- [ ] **Step 2: `DungeonPathBuilder.cs` 전면 교체**

```csharp
// @tags: dungeon, generation, path, maze, grid, room
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>격자 한 칸의 계획. 어떤 역할의 칸이며 어느 면이 열려야 하는가.</summary>
    public struct RoomPlan
    {
        public RoomRole Role;
        public RoomOpen Opens;
    }

    /// <summary>
    /// 형태 마스크 위에 미로를 뚫는다.
    ///
    /// 1) 랜덤 DFS 스패닝 트리 — S에서 출발해 모든 던전 칸을 한 번씩 방문하며 지나온 면을 연다.
    ///    트리가 완성되면 **모든 칸이 반드시 S에서 도달 가능**하다. 뚫고 나서 검사하고 재시도하는
    ///    구조가 필요 없는 이유가 이것이다.
    /// 2) 추가 연결 — 남은 면을 extraConnectionChance로 더 뚫어 순환로와 갈림길을 만든다.
    ///
    /// 반환 배열은 [row, col] 인덱싱이며 row 0이 맨 위. 암반 칸은 Role=Rock, Opens=None으로 남는다.
    /// </summary>
    public static class DungeonPathBuilder
    {
        private static readonly int[] DirRow = { 0, 0, -1, 1 };
        private static readonly int[] DirCol = { -1, 1, 0, 0 };
        private static readonly RoomOpen[] DirOpen = { RoomOpen.L, RoomOpen.R, RoomOpen.U, RoomOpen.D };

        public static RoomPlan[,] Build(DungeonShapeMask mask, float extraConnectionChance, System.Random rng)
        {
            if (mask == null) throw new System.ArgumentNullException(nameof(mask));
            if (rng == null) throw new System.ArgumentNullException(nameof(rng));

            int h = mask.Height, w = mask.Width;
            var plan = new RoomPlan[h, w];

            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    RoomRole role = RoomRole.Rock;
                    if (mask.IsDungeon(r, c))
                    {
                        role = RoomRole.Normal;
                        if (r == mask.StartRow && c == mask.StartCol) role = RoomRole.Start;
                        else if (r == mask.EndRow && c == mask.EndCol) role = RoomRole.End;
                    }
                    plan[r, c] = new RoomPlan { Role = role, Opens = RoomOpen.None };
                }

            CarveSpanningTree(plan, mask, rng);
            AddExtraConnections(plan, mask, extraConnectionChance, rng);
            return plan;
        }

        // 반복 DFS. 재귀로 짜면 큰 마스크에서 스택이 깊어진다.
        private static void CarveSpanningTree(RoomPlan[,] plan, DungeonShapeMask mask, System.Random rng)
        {
            var visited = new bool[mask.Height, mask.Width];
            var stack = new Stack<(int row, int col)>();
            var candidates = new List<int>(4);

            visited[mask.StartRow, mask.StartCol] = true;
            stack.Push((mask.StartRow, mask.StartCol));

            while (stack.Count > 0)
            {
                var (r, c) = stack.Peek();

                candidates.Clear();
                for (int i = 0; i < 4; i++)
                {
                    int nr = r + DirRow[i], nc = c + DirCol[i];
                    if (mask.IsDungeon(nr, nc) && !visited[nr, nc]) candidates.Add(i);
                }

                if (candidates.Count == 0) { stack.Pop(); continue; }

                int dir = candidates[rng.Next(candidates.Count)];
                int tr = r + DirRow[dir], tc = c + DirCol[dir];

                Open(plan, r, c, tr, tc, DirOpen[dir]);
                visited[tr, tc] = true;
                stack.Push((tr, tc));
            }
        }

        // 오른쪽·아래 방향만 훑는다 — 왼쪽·위까지 보면 같은 면을 두 번 센다.
        private static void AddExtraConnections(
            RoomPlan[,] plan, DungeonShapeMask mask, float chance, System.Random rng)
        {
            if (chance <= 0f) return;

            var closed = new List<(int row, int col, int dir)>();
            for (int r = 0; r < mask.Height; r++)
                for (int c = 0; c < mask.Width; c++)
                {
                    if (!mask.IsDungeon(r, c)) continue;

                    if (mask.IsDungeon(r, c + 1) && (plan[r, c].Opens & RoomOpen.R) == 0)
                        closed.Add((r, c, 1));
                    if (mask.IsDungeon(r + 1, c) && (plan[r, c].Opens & RoomOpen.D) == 0)
                        closed.Add((r, c, 3));
                }

            Shuffle(closed, rng);

            foreach (var (r, c, dir) in closed)
            {
                if (rng.NextDouble() >= chance) continue;
                Open(plan, r, c, r + DirRow[dir], c + DirCol[dir], DirOpen[dir]);
            }
        }

        private static void Open(RoomPlan[,] plan, int r, int c, int tr, int tc, RoomOpen dir)
        {
            plan[r, c].Opens |= dir;
            plan[tr, tc].Opens |= RoomTypeUtil.Opposite(dir);
        }

        // 격자 순서대로 뚫으면 순환로가 항상 왼쪽 위에 몰린다.
        private static void Shuffle(List<(int row, int col, int dir)> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
```

- [ ] **Step 3: 프리셋에서 구 필드 제거**

`DungeonGenPresetSO.cs`에서 아래를 삭제한다.

```csharp
        [Header("격자 (방 단위)")]
        [Min(2)] public int gridWidth = 8;
        [Min(1)] public int gridHeight = 3;
        ...
        [Header("경로")]
        [Tooltip("매 스텝에서 오른쪽으로 이동할 확률...")]
        [Range(0f, 1f)] public float horizontalChance = 0.6f;

        [Header("루프")]
        [Tooltip("본선 옆에 갈라졌다 합류하는 2x2 곁길을...")]
        [Range(0f, 1f)] public float loopChance = 1f;
        [Tooltip("한 던전에 넣을 루프 최대 개수. 0이면 루프 없음.")]
        [Min(0)] public int maxLoops = 2;
```

`DungeonGenPreset.asset`에서도 `gridWidth`/`gridHeight`/`horizontalChance`/`loopChance`/`maxLoops` 줄을 지운다.

`DungeonGenPresetTests.Defaults_AreHorizontalDungeonSized`에서 `gridWidth`/`gridHeight` 단언 2줄을 지운다.

- [ ] **Step 4: `DungeonGenerator.cs`에 형태 로드와 마스크 추첨 추가**

`LoadTemplates` 아래에 붙인다.

```csharp
        private static List<DungeonShapeMask> LoadShapes(DungeonGenPresetSO preset, GenerationResult result)
        {
            var all = new List<DungeonShapeMask>();
            if (preset.shapeFiles == null) return all;

            foreach (var asset in preset.shapeFiles)
            {
                if (asset == null) continue;

                var parsed = DungeonShapeParser.Parse(asset.text, asset.name);
                result.Errors.AddRange(parsed.Errors);
                result.Warnings.AddRange(parsed.Warnings);
                if (parsed.Success) all.AddRange(parsed.Masks);
            }
            return all;
        }

        private static DungeonShapeMask PickShape(List<DungeonShapeMask> shapes, System.Random rng)
        {
            int total = 0;
            foreach (var s in shapes) total += s.Weight > 0 ? s.Weight : 1;

            int roll = rng.Next(total);
            foreach (var s in shapes)
            {
                roll -= s.Weight > 0 ? s.Weight : 1;
                if (roll < 0) return s;
            }
            return shapes[shapes.Count - 1];
        }
```

`Generate` 본문에서 템플릿 로드 직후에 형태를 읽고, 배선 호출을 바꾼다.

```csharp
            var shapes = LoadShapes(preset, result);
            if (result.Errors.Count > 0) return result;
            if (shapes.Count == 0)
            {
                result.Errors.Add("형태 마스크가 하나도 없습니다. 프리셋의 Shape Files를 확인하세요.");
                return result;
            }

            var rng = new System.Random(seed);
            var mask = PickShape(shapes, rng);
            var plan = DungeonPathBuilder.Build(mask, preset.extraConnectionChance, rng);
```

기존 `try/catch (ArgumentOutOfRangeException)` 블록은 삭제한다 — 격자 크기 검증이 파서로 옮겨갔다.

- [ ] **Step 5: `DungeonGeneratorTests.cs` 수정**

`SetUp`에서 격자 필드 대신 형태 마스크를 넣는다.

```csharp
    private TextAsset _shapeAsset;

    [SetUp]
    public void SetUp()
    {
        _roomAsset = new TextAsset(Rooms);
        _shapeAsset = new TextAsset("# shape: 테스트\nS.X\n");

        _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();
        _preset.name = "TestPreset";
        _preset.roomWidth = 4;
        _preset.roomHeight = 4;
        _preset.extraConnectionChance = 0f;
        _preset.roomTemplateFiles.Add(_roomAsset);
        _preset.shapeFiles.Add(_shapeAsset);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_preset);
        Object.DestroyImmediate(_roomAsset);
        Object.DestroyImmediate(_shapeAsset);
    }
```

`Generate_ProducesMapWithBorderSizeAndName`의 기대 크기를 마스크(3×1 칸)에 맞춘다.

```csharp
        Assert.AreEqual(3 * 4 + 2, r.Data.Width);
        Assert.AreEqual(1 * 4 + 2, r.Data.Height);
```

`Generate_NoTemplateFiles_Fails` 옆에 형태 없음 케이스를 추가한다.

```csharp
    [Test]
    public void Generate_NoShapeFiles_Fails()
    {
        _preset.shapeFiles.Clear();
        var r = DungeonGenerator.Generate(_preset, 1);
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.Errors.Count > 0);
    }
```

`Rooms` 상수의 방 3종은 Task 9까지 그대로 둔다(START/END 헤더 포함). Task 9에서 개구부 조합 표기로 바꾼다.

- [ ] **Step 6: 컴파일 확인**

---

## Task 6: `E`/`X` 후처리 배치 (`DungeonEntryPlacer`)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonEntryPlacer.cs`
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonRoomComposer.cs`
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonRoomTemplateParser.cs`
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonGenerator.cs`
- Test: `Assets/Tests/EditMode/DungeonEntryPlacerTests.cs`

**Interfaces:**
- Consumes: `ComposeResult`, `RoomPlan[,]`, `DungeonGenPresetSO`
- Produces: `DungeonEntryPlacer.Place(ComposeResult res, RoomPlan[,] plan, DungeonGenPresetSO preset, System.Random rng)`

- [ ] **Step 1: 조립기에서 Role 기반 선택 제거**

`DungeonRoomComposer.Pick`의 시그니처와 필터에서 `role`을 뺀다. 미로에서는 시작 칸의 개구부 조합을
미리 알 수 없어 전용 템플릿을 유지하면 15종 × 2를 더 그려야 한다.

```csharp
        // 개구부 조합 정확 일치 → 상위집합 → null(빈 방)
        private static DungeonRoomTemplate Pick(
            RoomOpen opens, IReadOnlyList<DungeonRoomTemplate> templates,
            System.Random rng, List<string> warnings)
        {
            var exact = new List<DungeonRoomTemplate>();
            var superset = new List<DungeonRoomTemplate>();

            foreach (var t in templates)
            {
                if (t == null) continue;
                if (t.Opens == opens) exact.Add(t);
                else if ((t.Opens & opens) == opens) superset.Add(t);
            }

            if (exact.Count > 0) return WeightedPick(exact, rng);

            string label = RoomTypeUtil.FormatOpens(opens);
            if (label.Length == 0) label = "(닫힌 방)";

            if (superset.Count > 0)
            {
                warnings.Add($"'{label}' 템플릿이 없어 상위집합 템플릿으로 대체했습니다.");
                return WeightedPick(superset, rng);
            }

            warnings.Add($"'{label}' 템플릿이 없어 빈 방으로 생성했습니다.");
            return null;
        }
```

호출부도 바꾼다.
```csharp
                var template = Pick(cell.Opens, templates, rng, res.Warnings);
```

- [ ] **Step 2: 템플릿 파서에서 ROLE 표기를 경고 후 무시**

`DungeonRoomTemplateParser`의 `# room:` 헤더 파싱에서 `START`/`END`/`FILL` 토큰을 만나면
`template.Role`을 세팅하는 대신 경고를 남기고 건너뛴다. 구 템플릿을 열었을 때 조용히 깨지지 않게 한다.

```csharp
                    // ROLE 표기는 폐지됐다(START/END는 후처리 배치, FILL은 암반). 남아 있으면 무시한다.
                    if (token == "START" || token == "END" || token == "FILL")
                    {
                        result.Warnings.Add($"{tag} '# room:'의 '{token}' 표기는 더 이상 쓰이지 않아 무시합니다.");
                        continue;
                    }
```

`DungeonRoomTemplate.Role` 필드와 `Describe()`의 role 접두사도 제거한다.

```csharp
        /// <summary>경고·에러 메시지용 표기. 예: "LR", "LRUD"</summary>
        public string Describe()
        {
            string opens = RoomTypeUtil.FormatOpens(Opens);
            return opens.Length > 0 ? opens : "(닫힌 방)";
        }
```

- [ ] **Step 3: 테스트 작성**

```csharp
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
```

- [ ] **Step 4: `DungeonEntryPlacer.cs` 작성**

```csharp
// @tags: dungeon, generation, entry, exit, placement
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 시작·끝 칸의 바닥에 E(입구)와 X(출구)를 찍는다.
    ///
    /// 전용 START/END 템플릿을 두지 않는 이유: 미로에서는 시작 칸의 개구부 조합을 미리 알 수 없어
    /// 'START LRUD'까지 15종 × 2를 그려야 한다. 대신 조립이 끝난 격자에서 바닥을 찾아 찍는다.
    /// </summary>
    public static class DungeonEntryPlacer
    {
        private const char None = '.';
        public const char Entry = 'E';
        public const char Exit = 'X';

        public static void Place(
            ComposeResult res, RoomPlan[,] plan, DungeonGenPresetSO preset, System.Random rng)
        {
            if (res == null) throw new System.ArgumentNullException(nameof(res));
            if (plan == null) throw new System.ArgumentNullException(nameof(plan));
            if (preset == null) throw new System.ArgumentNullException(nameof(preset));
            if (rng == null) throw new System.ArgumentNullException(nameof(rng));

            for (int gr = 0; gr < plan.GetLength(0); gr++)
            for (int gc = 0; gc < plan.GetLength(1); gc++)
            {
                if (plan[gr, gc].Role == RoomRole.Start) PlaceOne(res, preset, rng, gr, gc, Entry);
                else if (plan[gr, gc].Role == RoomRole.End) PlaceOne(res, preset, rng, gr, gc, Exit);
            }
        }

        private static void PlaceOne(
            ComposeResult res, DungeonGenPresetSO preset, System.Random rng, int gr, int gc, char symbol)
        {
            int rowFrom = gr * preset.roomHeight + 1;
            int rowTo = rowFrom + preset.roomHeight - 1;
            int colFrom = gc * preset.roomWidth + 1;
            int colTo = colFrom + preset.roomWidth - 1;

            var free = new List<(int row, int col)>();     // 슬롯도 없는 자리
            var occupied = new List<(int row, int col)>(); // 바닥이지만 슬롯이 있는 자리

            for (int r = rowFrom; r <= rowTo; r++)
            for (int c = colFrom; c <= colTo; c++)
            {
                if (!IsFloorSpot(res, preset, r, c)) continue;
                if (res.Objects[r, c] == None) free.Add((r, c));
                else occupied.Add((r, c));
            }

            var pool = free.Count > 0 ? free : occupied;
            if (pool.Count > 0)
            {
                var (pr, pc) = pool[rng.Next(pool.Count)];
                res.Objects[pr, pc] = symbol;
                return;
            }

            // 바닥이 하나도 없다. 방 맨 아랫줄 가운데를 바닥으로 만들고 그 위에 놓는다.
            int floorRow = rowTo;
            int mid = colFrom + preset.roomWidth / 2;
            res.Tiles[floorRow, mid] = preset.Wall;
            res.Tiles[floorRow - 1, mid] = preset.Empty;
            res.Objects[floorRow - 1, mid] = symbol;
            res.Warnings.Add($"방 ({gr},{gc})에 바닥이 없어 '{symbol}' 자리를 강제로 만들었습니다 — 템플릿을 확인하세요.");
        }

        private static bool IsFloorSpot(ComposeResult res, DungeonGenPresetSO preset, int r, int c)
        {
            if (res.Tiles[r, c] != preset.Empty) return false;
            if (r + 1 >= res.Height) return false;
            return res.Tiles[r + 1, c] == preset.Wall;
        }
    }
}
```

- [ ] **Step 5: `DungeonGenerator.cs` 파이프라인에 끼우기**

```csharp
            var composed = DungeonRoomComposer.Compose(plan, templates, preset, rng);
            DungeonEntryPlacer.Place(composed, plan, preset, rng);
            DungeonRoomFiller.Fill(composed, preset, rng);
```

- [ ] **Step 6: 컴파일 확인**

---

## Task 7: 도달 불가 공간 메우기 (`DungeonCavityFiller`)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonCavityFiller.cs`
- Modify: `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/DungeonGenerator.cs`
- Test: `Assets/Tests/EditMode/DungeonCavityFillerTests.cs`

**Interfaces:**
- Produces: `DungeonCavityFiller.Fill(ComposeResult res, DungeonGenPresetSO preset, out bool exitReachable) → int`(메운 칸 수)

- [ ] **Step 1: 테스트 작성**

```csharp
using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonCavityFillerTests
{
    private DungeonGenPresetSO _preset;

    [SetUp]
    public void SetUp() => _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_preset);

    private static ComposeResult Make(string[] tileRows, string[] objectRows)
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
                res.Objects[r, c] = objectRows[r][c];
                res.Links[r, c] = '.';
            }
        return res;
    }

    [Test]
    public void Fill_SealedCavity_BecomesWall()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W..WWW",
                    "W.WW.W",   // (2,4)는 벽으로 둘러싸인 공동
                    "WWWWWW" },
            new[] { "......",
                    "......",
                    ".E....",
                    "......" });

        int filled = DungeonCavityFiller.Fill(res, _preset, out _);

        Assert.AreEqual('W', res.Tiles[2, 4], "밀봉 공동이 메워지지 않았다");
        Assert.AreEqual(1, filled);
    }

    [Test]
    public void Fill_ReachableSpace_Untouched()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W....W",
                    "W....W",
                    "WWWWWW" },
            new[] { "......",
                    "......",
                    ".E....",
                    "......" });

        int filled = DungeonCavityFiller.Fill(res, _preset, out _);

        Assert.AreEqual(0, filled);
        for (int c = 1; c <= 4; c++)
            Assert.AreEqual('.', res.Tiles[1, c]);
    }

    [Test]
    public void Fill_ExitReachable_ReportsTrue()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W....W",
                    "WWWWWW" },
            new[] { "......",
                    ".E..X.",
                    "......" });

        DungeonCavityFiller.Fill(res, _preset, out bool reachable);
        Assert.IsTrue(reachable);
    }

    [Test]
    public void Fill_ExitSealedOff_ReportsFalse()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W.W..W",   // 가운데 벽으로 갈라짐
                    "WWWWWW" },
            new[] { "......",
                    ".E..X.",
                    "......" });

        DungeonCavityFiller.Fill(res, _preset, out bool reachable);
        Assert.IsFalse(reachable);
    }

    // 메운 칸에 오브젝트가 남아 있으면 벽 속에 프리팹이 박힌다.
    [Test]
    public void Fill_ClearsObjectsInFilledCells()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W.WW.W",
                    "W.WW.W",
                    "WWWWWW" },
            new[] { "......",
                    "......",
                    ".E..^.",
                    "......" });

        DungeonCavityFiller.Fill(res, _preset, out _);

        Assert.AreEqual('.', res.Objects[2, 4], "메운 칸의 오브젝트가 남아 있다");
    }
}
```

- [ ] **Step 2: `DungeonCavityFiller.cs` 작성**

```csharp
// @tags: dungeon, generation, cavity, flood, connectivity
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 입구(E)에서 빈 타일을 4방향 플러드필해, 닿지 않은 빈 타일을 전부 벽으로 메운다.
    ///
    /// 이 프로젝트의 던전 타일맵은 파괴할 수 없다. 그래서 벽으로 둘러싸인 공동은 영원히 못 가는
    /// 장식이 되고, 화면에서는 "암반 한가운데 뚫린 주머니"로 보인다. 확률 타일이 방 안에서 굴러
    /// 만든 공동까지 여기서 같이 사라진다.
    ///
    /// 부수 효과로 연결성 검증을 겸한다 — 출구에 닿지 않으면 그 맵은 실패다.
    ///
    /// 한계: 4방향 연결만 본다. "뚫려는 있지만 점프로 못 닿는" 공간은 잡지 못한다.
    /// </summary>
    public static class DungeonCavityFiller
    {
        /// <summary>메운 칸 수를 반환한다.</summary>
        public static int Fill(ComposeResult res, DungeonGenPresetSO preset, out bool exitReachable)
        {
            if (res == null) throw new System.ArgumentNullException(nameof(res));
            if (preset == null) throw new System.ArgumentNullException(nameof(preset));

            exitReachable = false;

            if (!TryFindObject(res, DungeonEntryPlacer.Entry, out int startRow, out int startCol))
                return 0;

            char wall = preset.Wall;
            var reached = new bool[res.Height, res.Width];
            var queue = new Queue<(int row, int col)>();

            reached[startRow, startCol] = true;
            queue.Enqueue((startRow, startCol));

            int[] dr = { 0, 0, -1, 1 };
            int[] dc = { -1, 1, 0, 0 };

            while (queue.Count > 0)
            {
                var (r, c) = queue.Dequeue();
                if (res.Objects[r, c] == DungeonEntryPlacer.Exit) exitReachable = true;

                for (int i = 0; i < 4; i++)
                {
                    int nr = r + dr[i], nc = c + dc[i];
                    if (nr < 0 || nr >= res.Height || nc < 0 || nc >= res.Width) continue;
                    if (reached[nr, nc] || res.Tiles[nr, nc] == wall) continue;

                    reached[nr, nc] = true;
                    queue.Enqueue((nr, nc));
                }
            }

            int filled = 0;
            for (int r = 0; r < res.Height; r++)
                for (int c = 0; c < res.Width; c++)
                {
                    if (res.Tiles[r, c] == wall || reached[r, c]) continue;

                    res.Tiles[r, c] = wall;
                    res.Objects[r, c] = '.'; // 벽 속에 프리팹이 박히지 않도록
                    filled++;
                }

            return filled;
        }

        private static bool TryFindObject(ComposeResult res, char symbol, out int row, out int col)
        {
            for (int r = 0; r < res.Height; r++)
                for (int c = 0; c < res.Width; c++)
                    if (res.Objects[r, c] == symbol) { row = r; col = c; return true; }

            row = col = -1;
            return false;
        }
    }
}
```

- [ ] **Step 3: `DungeonGenerator.cs` 파이프라인 완성**

```csharp
            var composed = DungeonRoomComposer.Compose(plan, templates, preset, rng);

            // E/X 배치가 먼저 — 공동 메우기의 플러드필 출발점이 E다.
            DungeonEntryPlacer.Place(composed, plan, preset, rng);

            int filled = DungeonCavityFiller.Fill(composed, preset, out bool exitReachable);
            if (!exitReachable)
            {
                result.Errors.Add("입구에서 출구까지 이어지지 않습니다. 방 템플릿의 개구부 위치 규약을 확인하세요.");
                return result;
            }
            if (filled > 0)
                result.Warnings.Add($"도달할 수 없는 빈칸 {filled}개를 암반으로 메웠습니다.");

            // 슬롯이 마지막 — 메워진 칸의 슬롯은 '벽에 묻힌 슬롯 삭제'가 알아서 지운다.
            DungeonRoomFiller.Fill(composed, preset, rng);

            result.Warnings.AddRange(composed.Warnings);
```

- [ ] **Step 4: 컴파일 확인**

---

## Task 8: 방 템플릿 15종 재작성

**Files:**
- Modify: `Assets/DungeonMaps/Templates/cave_rooms.txt` (전면 재작성)
- Modify: `Assets/Tests/EditMode/DungeonRoomTemplateAssetTests.cs`

- [ ] **Step 1: 헤더와 기존 방 손질**

파일 맨 위 주석을 이렇게 바꾼다.

```
# 동굴 던전 기본 방 세트. 방 크기 10x6.
# 개구부 규약: 좌우 = 행 2,3,4 / 상하 = 열 4,5,6. 개구부에는 확률 타일 금지.
# 확률 타일: 0=50% 벽, 1=25% 벽, 2=75% 벽
#
# 미로 배선이라 개구부 조합 15종이 전부 나올 수 있다. 빠지면 상위집합 폴백이 걸려
# 여분의 개구부(막힌 벽감)가 생긴다.
#
# 오브젝트 슬롯: ? 바닥 위험 / D 왼쪽 벽 다트 / d 오른쪽 벽 다트 / % 보상 후보
# 직접 배치: ^ 점프대 — **U가 열린 방은 반드시 하나 둔다**(없으면 위층으로 못 올라간다).
# E/X는 템플릿에 넣지 않는다 — 생성기가 시작·끝 방 바닥에 찍는다.
```

기존 `# room: START R` 방은 헤더를 `# room: R`로 바꾸고 `[OBJECTS]`의 `E`를 지운 뒤 슬롯을 넣는다.
기존 `# room: END L` 방은 헤더를 `# room: L`로 바꾸고 `X`를 지운다. 두 방의 `[TILES]`는 그대로 쓴다 —
START R은 왼쪽이 막히고 오른쪽만 열린 모양이라 그대로 `R` 타입이고, END L도 그대로 `L` 타입이다.

```
# room: R
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
..%....?..
..........
---
# room: L
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
..?....%..
..........
```

`R` 방의 `%`(row4,col2): 아래 row5col2='W' ✓. `?`(row4,col7): row5col7='W' ✓.
`L` 방의 `?`(row4,col2)·`%`(row4,col7): 같은 이유로 ✓.

- [ ] **Step 2: `U`가 열린 기존 방에 점프대 추가**

`UR`·`LU`·`UD`에는 이미 `^`가 있다. `LRU` 두 방에 없으므로 넣는다.

```
# room: LRU
[TILES]
WWWW...WWW
W..0...0.W
..........
..WW...W..
..........
WWWWWWWWWW
[OBJECTS]
..........
..........
.......%..
..........
...?.^.?..
..........
---
# room: LRU
# weight: 2
[TILES]
WWWW...WWW
W.0.....0W
..........
..W.....W.
....WWW...
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
.....%....
..?^....?.
..........
```

검산 — 첫 방 `^`(row4,col5): 타일 row4는 전부 `.`이고 row5가 전부 `W`라 ✓.
둘째 방은 row4의 col4~6이 `WWW` 발판이라 col5에 두면 타일이 벽이다. **col3**에 둔다 —
타일 row4col3='.' ✓, 받침 row5col3='W' ✓.

- [ ] **Step 3: 새 방 5종 추가**

파일 끝(FILL 방을 지운 자리)에 붙인다. **`# room: FILL` 두 방은 삭제한다.**

```
---
# room: U
[TILES]
WWWW...WWW
W........W
W........W
W........W
W........W
WWWWWWWWWW
[OBJECTS]
..........
..........
..........
..........
..%..^....
..........
---
# room: D
[TILES]
WWWWWWWWWW
W........W
W........W
W........W
W........W
WWWW...WWW
[OBJECTS]
..........
..........
..........
..........
..?....%..
..........
---
# room: LUD
[TILES]
WWWW...WWW
W........W
.........W
.........W
.........W
WWWW...WWW
[OBJECTS]
..........
..........
..........
..........
..?....^..
..........
---
# room: RUD
[TILES]
WWWW...WWW
W........W
W.........
W.........
W.........
WWWW...WWW
[OBJECTS]
..........
..........
..........
..........
..^....?..
..........
---
# room: LRUD
[TILES]
WWWW...WWW
W........W
..........
..WW..WW..
..........
WWWW...WWW
[OBJECTS]
..........
..........
..%.......
..........
..?....^..
..........
```

각 방 검산:
- `U` — 닫힌 면 L/R/D가 전부 확정 벽 ✓. `^`(row4,col5) 받침 row5col5='W' ✓. `%`(row4,col2) ✓.
- `D` — 닫힌 면 L/R/U 확정 벽 ✓. row5는 `WWWW...WWW`라 col2·col7이 받침 ✓.
- `LUD` — R면(col9) 전 행 `W` ✓. L 개구부 행 2·3·4의 col0이 `.` ✓. `^`(row4,col7) 받침 row5col7='W' ✓.
- `RUD` — L면(col0) 전 행 `W` ✓. `^`(row4,col2) 받침 row5col2='W' ✓.
- `LRUD` — 네 면 모두 개구부 규약 충족 ✓. `%`(row2,col2) 받침 row3col2='W' ✓. `^`(row4,col7) 받침 row5col7='W' ✓.

- [ ] **Step 4: 에셋 테스트 갱신**

`DungeonRoomTemplateAssetTests.cs`에서:

`CaveRooms_CoverEveryTypeThePathBuilderCanEmit`을 15종 전수로 바꾼다.

```csharp
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
```

`CaveRooms_StartHasEntry_EndHasExit`와 `CaveRooms_StartAndEndHaveNoTrapSlots`,
`CaveRooms_FillRoomsHaveNoObjects`는 삭제한다(역할 개념이 사라졌다).

`E`/`X`가 템플릿에 남아 있지 않은지, `U` 방에 점프대가 있는지 확인하는 테스트를 추가한다.

```csharp
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

    // 방 높이 6에 점프 도달이 2칸이라 맨바닥에서는 위 개구부에 닿지 않는다.
    // 이게 없으면 미로가 아래로만 흐르는 일방통행이 된다.
    [Test]
    public void CaveRooms_UpOpenRoomsHaveJumpPad()
    {
        var r = Load();
        foreach (var t in r.Templates)
        {
            if ((t.Opens & RoomOpen.U) == 0) continue;
            Assert.IsTrue(GridContains(t.Objects, '^'), $"[{t.Describe()}] 위가 열렸는데 점프대가 없다");
        }
    }
```

`CaveRooms_ObjectsAreKnownSlotOrMappedPrefab`과 `CaveRooms_SlotsSitOnValidSurfaces`,
`Preset_SlotSymbolsDoNotCollideWithTilesetObjects`, `Preset_SlotCandidatesAreMappedPrefabs`는 그대로 둔다.

- [ ] **Step 5: 컴파일 확인**

---

## Task 9: 마무리 — 남은 참조 정리와 문서 갱신

**Files:**
- Modify: `Assets/Tests/EditMode/DungeonGeneratorTests.cs`
- Modify: `Assets/Tests/EditMode/DungeonRoomTemplateParserTests.cs`
- Modify: `Assets/Docs/dungeon-generation/maze-and-shape-design.md`
- Modify: `c:\Users\onebe\wks\CLAUDE.md`

- [ ] **Step 1: `DungeonGeneratorTests`의 테스트용 방을 새 계약으로**

`Rooms` 상수에서 START/END 헤더를 없애고 `E`/`X`를 뺀다.

```csharp
    // 4x4 방 3종 — R / LR / L
    private const string Rooms =
        "# room: R\n" +
        "[TILES]\nWWWW\nW..W\nW...\nWWWW\n" +
        "[OBJECTS]\n....\n....\n....\n....\n" +
        "---\n" +
        "# room: LR\n" +
        "[TILES]\nWWWW\nW00W\n....\nWWWW\n" +
        "[OBJECTS]\n....\n....\n....\n....\n" +
        "---\n" +
        "# room: L\n" +
        "[TILES]\nWWWW\nW..W\n...W\nWWWW\n" +
        "[OBJECTS]\n....\n....\n....\n....\n";
```

`Generate_PlacesExactlyOneEntryAndOneExit`는 그대로 둔다 — 이제 `DungeonEntryPlacer`가 찍은 결과를 검증한다.

- [ ] **Step 2: 템플릿 파서 테스트에서 ROLE 케이스 조정**

`DungeonRoomTemplateParserTests`에서 `START`/`END`/`FILL` 헤더가 `Role`을 세팅하는지 확인하던 테스트를,
경고를 남기고 무시하는지 확인하도록 바꾼다.

```csharp
    [Test]
    public void Parse_LegacyRoleToken_IgnoredWithWarning()
    {
        var r = DungeonRoomTemplateParser.Parse(
            "# room: START R\n[TILES]\nWWWW\nW..W\nW...\nWWWW\n[OBJECTS]\n....\n....\n....\n....\n",
            4, 4, "test");

        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(RoomOpen.R, r.Templates[0].Opens);
        Assert.Greater(r.Warnings.Count, 0);
    }
```

- [ ] **Step 3: 설계 문서에 구현 완료 표시**

`maze-and-shape-design.md` §4의 세로 이동 규약을 실제 구현에 맞게 좁힌다.

```markdown
**`U`가 열린 방은 점프대(`^`)를 하나 이상 둔다.** 원래는 "발판 계단 또는 점프대"로 적었지만,
발판으로 오를 수 있는지는 테스트로 판정할 수 없어(점프 물리 시뮬레이션이 필요하다) 점프대로 통일했다.
`DungeonRoomTemplateAssetTests.CaveRooms_UpOpenRoomsHaveJumpPad`가 강제한다.
```

- [ ] **Step 4: 프로젝트 `CLAUDE.md` 핵심 파일 표에 한 줄 추가**

```markdown
| `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/` | 던전 절차 생성. 형태 마스크(`Shapes/*.txt`) → 미로 배선 → 방 조립 → E/X 배치 → 공동 메우기 → 슬롯 채우기. 에디터 창은 `Tools/Dungeon/Map Importer`. 설계: `Assets/Docs/dungeon-generation/maze-and-shape-design.md` |
```

- [ ] **Step 5: Unity에서 실제로 뽑아 확인**

`Tools/Dungeon/Map Importer` → Generate를 여러 시드로 돌려서 눈으로 본다.

1. 암반 속 빈 주머니가 없는가
2. 입구에서 출구까지 실제로 갈 수 있는가
3. 위층으로 올라가는 길이 막혀 있지 않은가(점프대 확인)
4. 형태가 판마다 바뀌는가(가로·세로·ㄱ자)
5. 던전이 지나치게 넓지 않은가 — 넓으면 `basic_shapes.txt`의 칸 수를 줄인다

---

## 자체 점검 결과

설계 문서와 대조해 확인한 것:

| 설계 항목 | 담당 태스크 |
|---|---|
| §2 형태 마스크 문법·파서 | Task 2, 3 |
| §3 미로 배선(트리 + 추가 연결) | Task 5 |
| §4 개구부 15종·START/END 폐지·FILL 폐지 | Task 1, 6, 8 |
| §4 세로 이동 규약 | Task 8 (테스트로 강제) |
| §5 파이프라인 순서 | Task 6, 7 |
| §5 E/X 배치 | Task 6 |
| §5 공동 메우기 + 연결성 판정 | Task 7 |
| §6 프리셋 필드 교체 | Task 4, 5 |
| §8 테스트 | 각 태스크에 포함 |

`RoomRole.Rock`(Task 1) → `DungeonPathBuilder.Build(mask, chance, rng)`(Task 5) →
`DungeonEntryPlacer.Place(res, plan, preset, rng)`(Task 6) →
`DungeonCavityFiller.Fill(res, preset, out exitReachable)`(Task 7) 순으로 이름과 시그니처가 이어진다.
