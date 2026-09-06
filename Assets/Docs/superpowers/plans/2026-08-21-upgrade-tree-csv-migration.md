# 업그레이드 트리 CSV 마이그레이션 구현 계획 (1단계)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 업그레이드 트리의 원본을 `UpgradeTreeGenerator.cs`의 C# 리터럴에서 `UpgradeTree.csv`로 옮기고, 에셋·`priceData.json`·파이썬 도구가 모두 그 하나를 읽게 만든다. **게임 동작은 바뀌지 않는다** (가격 불일치 정리 제외).

**Architecture:** CSV 리더를 런타임 어셈블리(`GameScripts`)에 두고, 에디터 생성기·가격 익스포터가 그것을 소비한다. 파이썬 도구는 C# 소스 정규식 파싱을 그만두고 같은 CSV를 읽는다. 파서는 기존 `CsvParser`(RFC 4180)를 재사용한다.

**Tech Stack:** Unity 2D / C# / NUnit(EditMode) / Python 3 표준 라이브러리

**Spec:** `Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md`

## Global Constraints

- **버전 관리는 UVCS다. `git` 명령을 쓰지 않는다** (`git add` / `git commit` / `git diff` 전부 금지). 체크포인트는 UVCS에서 사람이 체크인한다.
- **Unity 테스트는 Claude가 실행하지 않는다.** `mcp__mcp-unity__run_tests` 등을 호출하지 않는다. 테스트는 작성하고, 실행·확인은 사람이 Test Runner에서 한다. **테스트 실행을 다음 태스크의 게이트로 삼지 않는다.**
- **파이썬은 표준 라이브러리만 쓴다** — `Tools/telemetry/export_csv.py` / `fit_upgrade_prices.py` / `check_upgrade_tree.py`와 같은 방침.
- **새 CSV 파서를 만들지 않는다.** `Assets/Scripts/_Core/Data/Excel/CsvParser.cs`를 쓴다 (RFC 4180 준수, BOM 제거 포함).
- **`effectType`은 `UpgradeEffectType` enum 이름 문자열**로 적고 `System.Enum.TryParse`로 읽는다 — `tileData.json` / `priceData.json`이 이미 쓰는 규약.
- **마이그레이션 기준값은 C#/에셋 계보다** (면허 I = 1,280G). `priceData.json`의 700G 계보는 스테일이므로 버린다. 스펙 §2.1.
- **1단계에서 값을 새로 짓지 않는다.** C# 리터럴의 값을 그대로 옮긴다. 가격·문구·좌표를 "더 좋게" 고치지 않는다.
- 노드는 **50개**다 (에셋은 46개 — 차이 4개는 스펙 §2.2).

---

## File Structure

| 파일 | 책임 |
|---|---|
| `Assets/Scripts/_Core/Data/UpgradeNodeData.cs` (신규) | 노드 1행의 순수 데이터. `GameScripts`로 이사 — Editor에 있으면 테스트가 못 본다 |
| `Assets/Scripts/_Core/Data/UpgradeTreeCsvReader.cs` (신규) | CSV → `List<UpgradeNodeData>` 변환 + 오류 수집. Unity 에디터 의존 없음 |
| `Assets/GameData/UpgradeData/UpgradeTree.csv` (신규) | **단일 원본** |
| `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` (수정) | `GetUpgradePlanData()` 삭제 → 리더 호출. 에셋 생성 로직은 그대로 |
| `Assets/Scripts/Editor/PriceDataExporter.cs` (수정) | `ExportNodes()`가 에셋 대신 CSV에서 읽음 |
| `Tools/telemetry/migrate_tree_to_csv.py` (신규, 일회성) | C# 리터럴 → CSV 추출 + 왕복 대조 |
| `Tools/telemetry/upgrade_tree_csv.py` (신규) | 파이썬용 CSV 리더. `check_upgrade_tree.py`가 쓴다 |
| `Tools/telemetry/check_upgrade_tree.py` (수정) | C# 정규식 파서 삭제 → 위 리더 사용 |
| `Assets/Tests/EditMode/UpgradeTreeCsvTests.cs` (신규) | 리더 단위 테스트 |
| `Assets/Tests/EditMode/UpgradeTreeCostTests.cs` (수정) | 스펙 §9.1 `Ignore` / §9.2 유지 |

### 어셈블리 제약 (중요)

`Assets/Tests/EditMode/EditModeTests.asmdef`는 `GameScripts`만 참조하고 **`GameScripts.Editor`는 참조하지 않는다.**
따라서 리더를 `Assets/Scripts/Editor/`에 두면 `UpgradeTreeCsvTests`에서 컴파일 에러가 난다.
**반드시 `Assets/Scripts/_Core/Data/`(= `GameScripts`)에 둔다.**

---

## Task 1: CSV 리더

**Files:**
- Create: `Assets/Scripts/_Core/Data/UpgradeNodeData.cs`
- Create: `Assets/Scripts/_Core/Data/UpgradeTreeCsvReader.cs`
- Test: `Assets/Tests/EditMode/UpgradeTreeCsvTests.cs`
- Modify: `Assets/Scripts/Editor/UpgradeTreeGenerator.cs:804-832` (기존 `UpgradeNodeData` 클래스 삭제)

**Interfaces:**
- Consumes: `CsvParser.Parse(string) / ParseText(string)` → `List<Dictionary<string,string>>`
- Produces:
  - `class UpgradeNodeData` — 필드 `nodeId`(string) `displayNameKey`(string) `descriptionKey`(string) `tier`(int) `cost`(int) `uiPosition`(Vector2) `parentIds`(string[]) `effectType`(UpgradeEffectType) `effectValue`(float) `isPercentage`(bool) `locked`(bool)
  - `static List<UpgradeNodeData> UpgradeTreeCsvReader.Read(string filePath, List<string> errors = null)`
  - `static List<UpgradeNodeData> UpgradeTreeCsvReader.ReadText(string csvText, List<string> errors = null)`
  - `const string UpgradeTreeCsvReader.DefaultPath = "Assets/GameData/UpgradeData/UpgradeTree.csv"`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/Tests/EditMode/UpgradeTreeCsvTests.cs`:

```csharp
// @tags: test, editmode, upgrade, tree, csv, reader
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// UpgradeTreeCsvReader 단위 테스트.
/// 파일이 아니라 문자열을 먹여서 검사한다 — 실제 CSV의 내용 변화와 무관하게
/// 파싱 규칙만 고정하기 위해서다.
/// </summary>
public class UpgradeTreeCsvTests
{
    private const string Header =
        "nodeId,tier,effectType,effectValue,isPercentage,parentIds,cost,uiX,uiY,displayNameKey,descriptionKey,locked\n";

    [Test]
    public void Read_ParsesBasicRow()
    {
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "MiningRange_T0_01,0,MiningRangeMultiplier,1.12,true,,60,0,-350,넓은 삽날 I,범위가 넓어집니다.,false\n");

        Assert.AreEqual(1, rows.Count);
        var n = rows[0];
        Assert.AreEqual("MiningRange_T0_01", n.nodeId);
        Assert.AreEqual(0, n.tier);
        Assert.AreEqual(UpgradeEffectType.MiningRangeMultiplier, n.effectType);
        Assert.AreEqual(1.12f, n.effectValue, 0.0001f);
        Assert.IsTrue(n.isPercentage);
        Assert.AreEqual(60, n.cost);
        Assert.AreEqual(new Vector2(0f, -350f), n.uiPosition);
        Assert.AreEqual("넓은 삽날 I", n.displayNameKey);
        Assert.IsFalse(n.locked);
    }

    [Test]
    public void Read_BlankParents_MeansRootNode()
    {
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "A,0,MaxStaminaUp,10,false,,60,0,0,이름,설명,false\n");

        Assert.IsNotNull(rows[0].parentIds, "parentIds는 null이 아니라 빈 배열이어야 한다");
        Assert.AreEqual(0, rows[0].parentIds.Length);
    }

    [Test]
    public void Read_SplitsParentIdsOnSemicolon()
    {
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "C,0,MaxStaminaUp,10,false,A;B,60,0,0,이름,설명,false\n");

        CollectionAssert.AreEqual(new[] { "A", "B" }, rows[0].parentIds);
    }

    [Test]
    public void Read_QuotedDescriptionWithComma_KeepsComma()
    {
        // 실제 데이터에 있는 행이다 — EnvironmentResistance_T2_01의 설명에 쉼표가 들어간다.
        // 인용을 못 다루면 컬럼이 한 칸씩 밀려 조용히 망가진다.
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "E,2,EnvironmentResistance,30,false,,60,0,0,유해 차폐 기어,\"극한 온도, 독소 가스 등 저항 +30%\",false\n");

        Assert.AreEqual("극한 온도, 독소 가스 등 저항 +30%", rows[0].descriptionKey);
        Assert.AreEqual(30f, rows[0].effectValue, 0.0001f);
    }

    [Test]
    public void Read_UnknownEffectType_IsSkippedAndReported()
    {
        var errors = new List<string>();
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "Bad,0,ThisTypeDoesNotExist,1,false,,60,0,0,이름,설명,false\n", errors);

        Assert.AreEqual(0, rows.Count, "모르는 effectType 행은 버린다");
        Assert.AreEqual(1, errors.Count);
        StringAssert.Contains("Bad", errors[0]);
    }

    [Test]
    public void Read_DuplicateNodeId_IsSkippedAndReported()
    {
        var errors = new List<string>();
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "A,0,MaxStaminaUp,10,false,,60,0,0,이름,설명,false\n" +
            "A,0,MaxStaminaUp,20,false,,70,0,0,이름2,설명2,false\n", errors);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(1, errors.Count);
    }

    [Test]
    public void Read_LockedColumn_IsParsed()
    {
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "A,0,MaxStaminaUp,10,false,,60,0,0,이름,설명,TRUE\n");

        Assert.IsTrue(rows[0].locked, "대소문자와 무관하게 읽어야 한다");
    }

    [Test]
    public void Read_MalformedNumber_IsSkippedAndReported()
    {
        var errors = new List<string>();
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "A,0,MaxStaminaUp,열,false,,60,0,0,이름,설명,false\n", errors);

        Assert.AreEqual(0, rows.Count);
        Assert.AreEqual(1, errors.Count);
    }
}
```

- [ ] **Step 2: 사람에게 실행을 요청한다 (게이트 아님)**

Unity Test Runner > EditMode > `UpgradeTreeCsvTests`.
기대: **컴파일 실패** (`UpgradeTreeCsvReader` 없음).
Claude는 실행하지 않는다. 확인을 기다리지 말고 Step 3으로 간다.

- [ ] **Step 3: `UpgradeNodeData`를 `GameScripts`로 옮긴다**

`Assets/Scripts/Editor/UpgradeTreeGenerator.cs` 파일 **끝의 `public class UpgradeNodeData { ... }` 블록(804~832행)을 삭제**하고, 아래를 `Assets/Scripts/_Core/Data/UpgradeNodeData.cs`로 만든다.

```csharp
// @tags: upgrade, tree, node, data, csv
using UnityEngine;

/// <summary>
/// 업그레이드 노드 1행의 순수 데이터. UpgradeTree.csv 한 줄에 대응한다.
///
/// GameScripts(런타임) 어셈블리에 둔다 — Editor 어셈블리에 있으면
/// EditModeTests.asmdef가 참조하지 못해 테스트를 쓸 수 없다.
/// 런타임 코드는 이 타입을 쓰지 않지만, 그건 배치 이유가 아니라 결과일 뿐이다.
/// </summary>
public class UpgradeNodeData
{
    public string nodeId;
    public string displayNameKey;
    public string descriptionKey;
    public int tier;
    public int cost;
    public Vector2 uiPosition;
    public string[] parentIds;
    public UpgradeEffectType effectType;
    public float effectValue;
    public bool isPercentage;

    /// <summary>
    /// true면 합성기(synth_tree.py)가 이 행의 값을 덮어쓰지 않는다.
    /// 사람이 손으로 확정한 고정점. 1단계에서는 전 행 false다.
    /// </summary>
    public bool locked;

    public UpgradeNodeData(
        string id, string name, string desc, int t, int c, Vector2 pos,
        string[] parents, UpgradeEffectType effType, float effVal, bool isPct,
        bool isLocked = false)
    {
        nodeId = id;
        displayNameKey = name;
        descriptionKey = desc;
        tier = t;
        cost = c;
        uiPosition = pos;
        parentIds = parents;
        effectType = effType;
        effectValue = effVal;
        isPercentage = isPct;
        locked = isLocked;
    }
}
```

- [ ] **Step 4: 리더를 구현한다**

`Assets/Scripts/_Core/Data/UpgradeTreeCsvReader.cs`:

```csharp
// @tags: upgrade, tree, csv, reader, data, generator
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// UpgradeTree.csv를 읽어 UpgradeNodeData 목록으로 만든다.
///
/// 이 CSV가 업그레이드 트리의 단일 원본이다. 에디터 생성기·가격 익스포터·
/// 파이썬 도구가 모두 여기서 읽는다.
/// 설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §4
///
/// 잘못된 행은 버리고 errors에 사유를 남긴다 — 한 행이 깨졌다고 트리 전체를
/// 못 읽으면 생성기가 아무것도 못 하고, 무엇이 잘못됐는지도 안 보인다.
/// </summary>
public static class UpgradeTreeCsvReader
{
    public const string DefaultPath = "Assets/GameData/UpgradeData/UpgradeTree.csv";

    public static List<UpgradeNodeData> Read(string filePath, List<string> errors = null)
        => FromRows(CsvParser.Parse(filePath), errors);

    public static List<UpgradeNodeData> ReadText(string csvText, List<string> errors = null)
        => FromRows(CsvParser.ParseText(csvText), errors);

    private static List<UpgradeNodeData> FromRows(
        List<Dictionary<string, string>> rows, List<string> errors)
    {
        var list = new List<UpgradeNodeData>();
        var seen = new HashSet<string>();

        foreach (var row in rows)
        {
            string id = Get(row, "nodeId").Trim();
            if (string.IsNullOrEmpty(id)) continue;

            if (!seen.Add(id))
            {
                Err(errors, $"{id}: nodeId 중복 — 뒤의 행을 버린다");
                continue;
            }

            string typeText = Get(row, "effectType").Trim();
            if (!System.Enum.TryParse(typeText, out UpgradeEffectType effType))
            {
                Err(errors, $"{id}: 모르는 effectType '{typeText}'");
                continue;
            }

            if (!TryInt(row, "tier", out int tier) ||
                !TryInt(row, "cost", out int cost) ||
                !TryFloat(row, "effectValue", out float effVal) ||
                !TryFloat(row, "uiX", out float uiX) ||
                !TryFloat(row, "uiY", out float uiY))
            {
                Err(errors, $"{id}: 숫자 칸을 읽을 수 없다 (tier/cost/effectValue/uiX/uiY)");
                continue;
            }

            list.Add(new UpgradeNodeData(
                id,
                Get(row, "displayNameKey"),
                Get(row, "descriptionKey"),
                tier,
                cost,
                new Vector2(uiX, uiY),
                ParseParents(Get(row, "parentIds")),
                effType,
                effVal,
                ParseBool(Get(row, "isPercentage")),
                ParseBool(Get(row, "locked"))));
        }

        return list;
    }

    private static string Get(Dictionary<string, string> row, string key)
        => row.TryGetValue(key, out string v) ? v : "";

    private static void Err(List<string> errors, string message)
    {
        errors?.Add(message);
        Debug.LogWarning($"[UpgradeTreeCsvReader] {message}");
    }

    /// <summary>
    /// 선행 노드는 세미콜론으로 나눈다 — 쉼표는 CSV 구분자라 쓸 수 없다.
    /// 빈 칸은 null이 아니라 빈 배열이다. 생성기가 Length로 루트 노드를 판정하므로
    /// null을 돌려주면 NullReference 대신 조용한 분기 차이가 생긴다.
    /// </summary>
    private static string[] ParseParents(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new string[0];

        var parts = text.Split(';');
        var list = new List<string>(parts.Length);
        foreach (string p in parts)
        {
            string t = p.Trim();
            if (t.Length > 0) list.Add(t);
        }
        return list.ToArray();
    }

    private static bool ParseBool(string text)
        => !string.IsNullOrWhiteSpace(text) &&
           text.Trim().Equals("true", System.StringComparison.OrdinalIgnoreCase);

    // 숫자는 반드시 InvariantCulture로 읽는다. CurrentCulture로 읽으면
    // 소수점 규칙이 다른 로케일에서 1.12가 112가 되거나 파싱이 실패한다.
    private static bool TryInt(Dictionary<string, string> row, string key, out int value)
        => int.TryParse(Get(row, key).Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out value);

    private static bool TryFloat(Dictionary<string, string> row, string key, out float value)
        => float.TryParse(Get(row, key).Trim(), NumberStyles.Float,
                          CultureInfo.InvariantCulture, out value);
}
```

- [ ] **Step 5: 사람에게 확인을 요청한다 (게이트 아님)**

Unity Test Runner > EditMode > `UpgradeTreeCsvTests` 8개.
기대: 전부 PASS. `UpgradeTreeGenerator.cs`는 아직 `GetUpgradePlanData()`를 쓰므로 컴파일은 통과한다 (`UpgradeNodeData`가 같은 이름으로 `GameScripts`에 있으므로 그대로 보인다).

- [ ] **Step 6: 체크포인트**

UVCS에 체크인한다 (git 아님). 대상:
`UpgradeNodeData.cs` / `UpgradeTreeCsvReader.cs` / `UpgradeTreeCsvTests.cs` / `UpgradeTreeGenerator.cs`
메시지: `feat: 업그레이드 트리 CSV 리더 추가 (UpgradeNodeData를 GameScripts로 이동)`

---

## Task 2: C# 리터럴 → CSV 추출과 왕복 대조

**Files:**
- Create: `Tools/telemetry/migrate_tree_to_csv.py`
- Create: `Assets/GameData/UpgradeData/UpgradeTree.csv` (스크립트 출력)

**Interfaces:**
- Consumes: `Assets/Scripts/Editor/UpgradeTreeGenerator.cs`의 `GetUpgradePlanData()` 리터럴
- Produces: `UpgradeTree.csv` — 헤더 `nodeId,tier,effectType,effectValue,isPercentage,parentIds,cost,uiX,uiY,displayNameKey,descriptionKey,locked`, 50행

**왜 파이썬으로 뽑나:** C#끼리 옮기면 옮긴 결과가 맞는지 확인할 방법이 없다. 밖에서 원본과 산출물을 각각 파싱해 대조하면 "50개 전 필드가 같다"를 기계가 판정한다.

- [ ] **Step 1: 추출·대조 스크립트를 쓴다**

`Tools/telemetry/migrate_tree_to_csv.py`:

```python
#!/usr/bin/env python3
"""UpgradeTreeGenerator.cs의 C# 리터럴을 UpgradeTree.csv로 옮긴다 (일회성).

왜 있나
-------
트리 원본을 C# 코드에서 데이터로 내리는 마이그레이션이다. 손으로 옮기면
50행 × 11필드 = 550칸을 눈으로 검사해야 하고, 한 칸만 틀려도 조용히 넘어간다.

두 가지 모드
------------
    python Tools/telemetry/migrate_tree_to_csv.py            # CSV 생성
    python Tools/telemetry/migrate_tree_to_csv.py --verify   # C#과 CSV 대조

--verify가 "일치 50 / 불일치 0"을 찍어야 마이그레이션이 끝난 것이다.

설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §4.3
표준 라이브러리만 쓴다.
"""

import argparse
import csv
import re
import sys
from pathlib import Path

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

SRC = "Assets/Scripts/Editor/UpgradeTreeGenerator.cs"
OUT = "Assets/GameData/UpgradeData/UpgradeTree.csv"

FIELDS = ["nodeId", "tier", "effectType", "effectValue", "isPercentage",
          "parentIds", "cost", "uiX", "uiY", "displayNameKey",
          "descriptionKey", "locked"]

# new UpgradeNodeData(
#     "id", "name", "desc", tier, cost, new Vector2(x, y),
#     new string[] { "p1", "p2" }, UpgradeEffectType.X, 1.12f, true
# )
NODE_RE = re.compile(
    r'new\s+UpgradeNodeData\(\s*'
    r'"([^"]*)"\s*,\s*'            # 1 nodeId
    r'"([^"]*)"\s*,\s*'            # 2 displayNameKey
    r'"([^"]*)"\s*,\s*'            # 3 descriptionKey
    r'(-?\d+)\s*,\s*'              # 4 tier
    r'(-?\d+)\s*,\s*'              # 5 cost
    r'new\s+Vector2\(\s*(-?[\d.]+)f?\s*,\s*(-?[\d.]+)f?\s*\)\s*,\s*'  # 6,7 uiX uiY
    r'new\s+string\[\]\s*\{([^}]*)\}\s*,\s*'   # 8 parents
    r'UpgradeEffectType\.(\w+)\s*,\s*'         # 9 effectType
    r'(-?[\d.]+)f?\s*,\s*'                     # 10 effectValue
    r'(true|false)',                           # 11 isPercentage
    re.DOTALL)

PARENT_RE = re.compile(r'"([^"]*)"')


def parse_cs(path):
    text = Path(path).read_text(encoding="utf-8")
    rows = []
    for m in NODE_RE.finditer(text):
        # 주석 처리된 줄은 건너뛴다
        line_start = text.rfind("\n", 0, m.start()) + 1
        if text[line_start:m.start()].lstrip().startswith("//"):
            continue
        parents = PARENT_RE.findall(m.group(8))
        rows.append({
            "nodeId":         m.group(1),
            "displayNameKey": m.group(2),
            "descriptionKey": m.group(3),
            "tier":           m.group(4),
            "cost":           m.group(5),
            "uiX":            trim_num(m.group(6)),
            "uiY":            trim_num(m.group(7)),
            "parentIds":      ";".join(parents),
            "effectType":     m.group(9),
            "effectValue":    trim_num(m.group(10)),
            "isPercentage":   m.group(11),
            "locked":         "false",
        })
    return rows


def trim_num(s):
    """'0.0' -> '0', '-350.0' -> '-350'. 소수점이 의미 있는 값은 그대로 둔다."""
    if "." in s:
        s = s.rstrip("0").rstrip(".")
    return s or "0"


def parse_csv(path):
    with open(path, encoding="utf-8", newline="") as f:
        return [dict(r) for r in csv.DictReader(f)]


def write_csv(rows, path):
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    # newline="" + QUOTE_MINIMAL: 쉼표가 든 설명만 인용된다 (RFC 4180)
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS, quoting=csv.QUOTE_MINIMAL)
        w.writeheader()
        for r in rows:
            w.writerow({k: r[k] for k in FIELDS})


def verify(cs_rows, csv_rows):
    cs = {r["nodeId"]: r for r in cs_rows}
    cv = {r["nodeId"]: r for r in csv_rows}
    bad = 0

    for nid in sorted(set(cs) | set(cv)):
        if nid not in cs:
            print(f"  [CSV에만] {nid}"); bad += 1; continue
        if nid not in cv:
            print(f"  [C#에만]  {nid}"); bad += 1; continue
        for k in FIELDS:
            a, b = cs[nid][k], cv[nid][k]
            if norm(k, a) != norm(k, b):
                print(f"  [불일치] {nid}.{k}: C#={a!r} CSV={b!r}"); bad += 1

    print(f"\nC# {len(cs)}개 / CSV {len(cv)}개 / 불일치 {bad}건")
    return bad == 0


def norm(key, value):
    """숫자 칸은 값으로 비교한다 — '60'과 '60.0'은 같다."""
    if key in ("tier", "cost", "effectValue", "uiX", "uiY"):
        try:
            return float(value)
        except ValueError:
            return value
    return value


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=SRC)
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--verify", action="store_true", help="생성하지 않고 대조만")
    a = ap.parse_args()

    cs_rows = parse_cs(a.src)
    print(f"C#에서 노드 {len(cs_rows)}개를 읽었다.")
    if len(cs_rows) == 0:
        print("한 개도 못 읽었다 — 정규식이 리터럴 형식과 안 맞는다.")
        return 1

    if a.verify:
        if not Path(a.out).exists():
            print(f"CSV가 없다: {a.out}")
            return 1
        return 0 if verify(cs_rows, parse_csv(a.out)) else 1

    write_csv(cs_rows, a.out)
    print(f"저장: {a.out}")
    print("이어서 --verify 로 대조할 것.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 2: 추출한다**

```
python Tools/telemetry/migrate_tree_to_csv.py
```
기대: `C#에서 노드 50개를 읽었다.` / `저장: Assets/GameData/UpgradeData/UpgradeTree.csv`

**50이 아니면 멈춘다.** 정규식이 리터럴 형태를 놓친 것이므로, CSV를 쓰기 전에 고친다.

- [ ] **Step 3: 왕복 대조한다**

```
python Tools/telemetry/migrate_tree_to_csv.py --verify
```
기대: `C# 50개 / CSV 50개 / 불일치 0건`

**불일치가 1건이라도 있으면 다음 태스크로 넘어가지 않는다.** 이 대조가 마이그레이션의 유일한 안전장치다.

- [ ] **Step 4: 쉼표 행을 눈으로 확인한다**

```
grep "EnvironmentResistance_T2_01" Assets/GameData/UpgradeData/UpgradeTree.csv
```
기대: 설명 칸이 `"극한 온도, 독소 가스 등 ..."`처럼 **따옴표로 감싸져** 있을 것.
감싸져 있지 않으면 컬럼이 밀린 것이다 — `--verify`가 잡았어야 하므로 대조 로직부터 의심한다.

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `migrate_tree_to_csv.py` / `UpgradeTree.csv`
메시지: `feat: 업그레이드 트리 50노드를 UpgradeTree.csv로 추출 (C# 대조 일치)`

---

## Task 3: 테스트 정리 (스펙 §9)

**Files:**
- Modify: `Assets/Tests/EditMode/UpgradeTreeCostTests.cs`

**왜 지금 하나:** Task 4에서 에셋이 46 → 50개가 되면 `NodeCount_IsFortySix`가 빨간불이 된다. 재생성 **전에** 정리해야 "내가 깬 것"과 "원래 깨져 있던 것"이 섞이지 않는다.

**Interfaces:**
- Consumes: 없음
- Produces: 없음 (테스트만 수정)

- [ ] **Step 1: 수치 테스트 6개에 `[Ignore]`를 붙인다**

각 `[Test]` **아래**에 아래 형태로 한 줄씩 추가한다. **단언은 지우지 않는다.**

```csharp
    [Test]
    [Ignore("총액·경로 기대값은 플레이하며 조정하는 값이라 고정 숫자로 못 지킨다. " +
            "2026-08-18 승격 이후 이미 방치돼 실패 중이었다. " +
            "오토플레이 리듬 검사로 대체 예정 — specs/2026-08-21-upgrade-tree-autogen-design.md §8/§9.1")]
    public void Tier0_TotalCost_MatchesLayer1Budget()
```

대상 6개:
1. `NodeCount_IsFortySix`
2. `Tier0_TotalCost_MatchesLayer1Budget`
3. `Tier1_TotalCost_MatchesLayer2Budget`
4. `MiningLicenses_HaveExplicitCosts`
5. `Tier0_MinimumPathToLicense_CostsFortyOneTwenty`
6. `Tier0_EveryDiveAffordsExactlyOneNode`

`Tier1_TotalCost_MatchesLayer2Budget`은 **지금 통과 중인데도** 끈다. 사유 문구에 그 사실을 적는다:

```csharp
    [Ignore("2층 총액도 플레이하며 조정할 값이다. 지금은 통과 중이지만 1층만 풀고 " +
            "2층을 묶어두면 합성기가 T1에 닿을 때 같은 교착이 반복된다. §9.1")]
```

- [ ] **Step 2: `Backpack_MovedToTier1_AndCostsFiveThousand`에서 가격 단언만 뺀다**

이 테스트는 살린다. 티어 이동은 구조이고 가격은 수치다.

```csharp
    [Test]
    public void Backpack_MovedToTier1_AndCostsFiveThousand()
    {
        var nodes = LoadNodes();
        Assert.IsTrue(nodes.ContainsKey("InventoryWeight_T1_01"), "배낭 I이 T1에 없다");
        Assert.AreEqual(1, nodes["InventoryWeight_T1_01"].tier);
        // 가격 단언(5000)은 뺐다 — 수치는 §9.1로 넘어갔고, 여기서 지키는 것은 배치다.
        Assert.IsFalse(nodes.ContainsKey("InventoryWeight_T0_01"), "배낭 I이 T0에 아직 남아 있다");
    }
```

- [ ] **Step 3: CSV ↔ JSON id 정합 검사를 추가한다**

`Assets/Tests/EditMode/PriceDataIntegrationTests.cs` 안에 아래 테스트를 추가한다.
(`PriceDataTestSource`는 `MineralBalanceTests.cs`에 있는 기존 로더다.)

```csharp
    [Test]
    public void UpgradeNodeIds_InPriceJson_AllExistInCsv()
    {
        // 2026-08-21: JSON에 'Facility_Workbench_T0'가 있는데 트리에는 'Facility_Workbench_T1'이라
        // 런타임에 경고만 남기고 작업대 가격이 통째로 적용되지 않고 있었다.
        // 이 검사가 없으면 오타 하나가 조용히 가격을 무력화한다.
        var csv = UpgradeTreeCsvReader.Read(UpgradeTreeCsvReader.DefaultPath);
        var ids = new HashSet<string>();
        foreach (var n in csv) ids.Add(n.nodeId);
        Assert.Greater(ids.Count, 0, "UpgradeTree.csv를 못 읽었다");

        var data = PriceDataTestSource.Load();
        Assert.IsNotNull(data.upgradeNodes, "priceData.json에 upgradeNodes 절이 없다");

        var missing = new List<string>();
        foreach (var e in data.upgradeNodes)
            if (!ids.Contains(e.id)) missing.Add(e.id);

        CollectionAssert.IsEmpty(missing,
            "priceData.json의 이 id들이 UpgradeTree.csv에 없다: " + string.Join(", ", missing));
    }
```

필요하면 파일 상단에 `using System.Collections.Generic;`을 추가한다.

- [ ] **Step 4: 사람에게 확인을 요청한다 (게이트 아님)**

Unity Test Runner > EditMode.
기대: 수치 6개가 **Ignored(회색)**, 구조 테스트는 통과.
`UpgradeNodeIds_InPriceJson_AllExistInCsv`는 **아직 실패한다** — Task 4에서 JSON을 다시 굽기 전이라 `Facility_Workbench_T0`가 남아 있다. 정상이다.

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `UpgradeTreeCostTests.cs` / `PriceDataIntegrationTests.cs`
메시지: `test: 트리 수치 테스트 Ignore 처리, CSV-JSON id 정합 검사 추가`

---

## Task 4: 생성기가 CSV를 읽게 한다

**Files:**
- Modify: `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` (`GetUpgradePlanData()` 삭제, 320행 부근 호출부 교체)

**Interfaces:**
- Consumes: `UpgradeTreeCsvReader.Read(string, List<string>)` (Task 1)
- Produces: 없음 (에셋 생성 동작은 그대로)

- [ ] **Step 1: 호출부를 바꾼다**

`GenerateUpgradeTree()` 안의 이 부분을 —

```csharp
        // 1. 기획 명세 데이터 가져오기
        List<UpgradeNodeData> planData = GetUpgradePlanData();
        _log += $"총 {planData.Count}개의 업그레이드 노드 기획 데이터를 로드했습니다.\n";
```

이렇게 바꾼다:

```csharp
        // 1. 기획 명세 데이터 — 원본은 UpgradeTree.csv다 (C# 리터럴이 아니다).
        //    설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §4
        var csvErrors = new List<string>();
        List<UpgradeNodeData> planData =
            UpgradeTreeCsvReader.Read(UpgradeTreeCsvReader.DefaultPath, csvErrors);

        foreach (string e in csvErrors) _log += $"  [CSV 오류] {e}\n";

        if (planData.Count == 0)
        {
            _log += $"\n★ 중단 — {UpgradeTreeCsvReader.DefaultPath} 에서 노드를 하나도 못 읽었다.\n" +
                    "  에셋을 건드리지 않았다. 파일 경로와 헤더를 확인할 것.\n";
            return;
        }

        _log += $"총 {planData.Count}개의 노드를 CSV에서 로드했습니다.\n";
```

**빈 목록에서 반드시 `return`해야 한다.** 그냥 진행하면 Pass 4(고아 정리)가 "기획에 없는 에셋"으로 판단해 **노드 에셋 50개를 전부 지운다.**

- [ ] **Step 2: `GetUpgradePlanData()`를 통째로 지운다**

`private List<UpgradeNodeData> GetUpgradePlanData()`의 여는 중괄호부터 `return list;` 다음 닫는 중괄호까지 (약 320~800행). 위의 기획 주석 블록도 함께 지운다 — 그 내용은 스펙 문서와 CSV로 옮겨갔다.

`UpgradeNodeData` 클래스는 Task 1에서 이미 옮겼으므로 이 파일에 남아 있지 않아야 한다.

- [ ] **Step 3: 사람에게 재생성을 요청한다**

Unity에서 `Tools > Upgrade > Upgrade Tree Generator` > `업그레이드 트리 자동 생성 및 링킹 실행`.

**로그에서 확인할 것:**

| 항목 | 기대 |
|---|---|
| 로드 개수 | `총 50개의 노드를 CSV에서 로드했습니다.` |
| CSV 오류 | 0건 |
| 생성 | **4** (`Facility_Map_T0` / `MapExplore_T0_01` / `MapExplore_T1_01` / `InventoryWeightSmall_T0_01`) |
| 수정 | 0 |
| 삭제 | **0** |
| 그대로 | 46 |

**"수정"이 0이 아니면 CSV가 C#과 어긋난 것이다.** Task 2의 `--verify`로 돌아간다.
**"삭제"가 0이 아니면 즉시 멈춘다.** 되돌릴 수 있게 UVCS에서 되돌리고 원인을 찾는다.

- [ ] **Step 4: 에셋 수를 확인한다**

```
ls Assets/GameData/UpgradeData/Node/*.asset | wc -l
```
기대: `50`

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `UpgradeTreeGenerator.cs` + 새 노드/효과 에셋 8개(노드 4 + 효과 4)
메시지: `refactor: 트리 생성기가 C# 리터럴 대신 UpgradeTree.csv를 읽는다`

---

## Task 5: 가격 익스포터를 CSV로 돌리고 JSON을 다시 굽는다

**Files:**
- Modify: `Assets/Scripts/Editor/PriceDataExporter.cs:196-211` (`ExportNodes()`)
- Modify: `Assets/StreamingAssets/priceData.json` (툴이 재생성)

**Interfaces:**
- Consumes: `UpgradeTreeCsvReader.Read(string, List<string>)`
- Produces: `priceData.json`의 `upgradeNodes` 50개

**왜 에셋이 아니라 CSV에서 읽나:** 에셋에서 읽으면 "생성기를 안 돌린 상태"에서 export할 때 낡은 값이 JSON으로 굳는다. 스펙 §2.1의 3중 불일치가 정확히 그렇게 생겼다.

- [ ] **Step 1: `ExportNodes()`를 바꾼다**

```csharp
    /// <summary>
    /// 노드 비용을 UpgradeTree.csv에서 읽는다 — 노드 에셋이 아니다.
    ///
    /// 에셋에서 읽으면 생성기를 안 돌린 상태에서 export했을 때 낡은 값이 JSON에 굳고,
    /// 그 JSON이 런타임에 에셋을 덮어써서 원본과 영구히 갈라진다.
    /// 2026-08-21 시점에 실제로 그 상태였다(50개 중 48개 불일치).
    /// 설계: specs/2026-08-21-upgrade-tree-autogen-design.md §5.1
    /// </summary>
    private static NodeCostEntry[] ExportNodes()
    {
        var errors = new List<string>();
        var nodes = UpgradeTreeCsvReader.Read(UpgradeTreeCsvReader.DefaultPath, errors);

        foreach (string e in errors)
            Debug.LogWarning($"[PriceDataExporter] CSV: {e}");

        if (nodes.Count == 0)
        {
            Debug.LogError($"[PriceDataExporter] {UpgradeTreeCsvReader.DefaultPath} 에서 " +
                           "노드를 못 읽었다. upgradeNodes 절이 비게 된다.");
            return new NodeCostEntry[0];
        }

        var list = new List<NodeCostEntry>(nodes.Count);
        foreach (var n in nodes)
            list.Add(new NodeCostEntry { id = n.nodeId, cost = n.cost });

        // 가격 오름차순 — 트리 진행 순서를 눈으로 훑기 위한 정렬. 같은 값이면 id 순.
        list.Sort((a, b) => a.cost != b.cost ? a.cost.CompareTo(b.cost) : string.CompareOrdinal(a.id, b.id));
        return list.ToArray();
    }
```

파일 상단 주석의 `노드 비용은 UpgradeTreeGenerator.cs 가 아니라 생성된 노드 에셋에서 읽는다.` 한 줄도 `노드 비용은 UpgradeTree.csv에서 읽는다.`로 고친다.

- [ ] **Step 2: 사람에게 export를 요청한다**

Unity에서 `Tools > Economy > Export Prices to JSON`.
확인 대화상자가 뜬다 — **"덮어쓴다"를 누른다.** 지금은 JSON이 스테일이므로 덮어쓰는 것이 맞다.

로그 기대: `... / 노드 50`

- [ ] **Step 3: JSON을 확인한다**

```
python -c "import json;d=json.load(open('Assets/StreamingAssets/priceData.json',encoding='utf-8'));n=d['upgradeNodes'];print(len(n));print([x for x in n if x['id'].startswith('Facility')])"
```

기대:
- 개수 `50`
- `Facility_Workbench_T0`가 **없고** `Facility_Workbench_T1`이 있을 것

- [ ] **Step 4: 면허 가격이 복원됐는지 본다**

```
python -c "import json;d=json.load(open('Assets/StreamingAssets/priceData.json',encoding='utf-8'));print([x for x in d['upgradeNodes'] if 'MiningLevel' in x['id']])"
```
기대: `MiningLevel_T0_Final`의 cost가 **1280** (700이 아니다).

⚠ 이 시점부터 **게임이 실제로 어려워진다.** 그동안 절반 가격으로 돌고 있었다(스펙 §12).
버그 수정이지만 체감은 밸런스 변경이므로, 플레이해보고 필요하면 CSV의 `cost` 칸을 조정한다.

- [ ] **Step 5: 사람에게 테스트 확인을 요청한다 (게이트 아님)**

Unity Test Runner > EditMode.
기대: Task 3에서 추가한 `UpgradeNodeIds_InPriceJson_AllExistInCsv`가 이제 **통과**.

- [ ] **Step 6: 체크포인트**

UVCS 체크인: `PriceDataExporter.cs` / `priceData.json`
메시지: `fix: 노드 가격 JSON을 CSV에서 재생성 (48건 불일치·Workbench id 오타 해소)`

---

## Task 6: 파이썬 도구를 CSV로 옮기고 C# 파서를 지운다

**Files:**
- Create: `Tools/telemetry/upgrade_tree_csv.py`
- Modify: `Tools/telemetry/check_upgrade_tree.py` (C# 정규식 파서 삭제)

**Interfaces:**
- Consumes: `Assets/GameData/UpgradeData/UpgradeTree.csv`
- Produces:
  - `upgrade_tree_csv.load(path=DEFAULT_PATH) -> list[dict]` — 각 dict는 키 `nodeId`(str) `tier`(int) `cost`(int) `effectType`(str) `effectValue`(float) `isPercentage`(bool) `parentIds`(list[str]) `uiX`(float) `uiY`(float) `displayNameKey`(str) `descriptionKey`(str) `locked`(bool)
  - `upgrade_tree_csv.DEFAULT_PATH` (str)

- [ ] **Step 1: 파이썬 리더를 쓴다**

`Tools/telemetry/upgrade_tree_csv.py`:

```python
#!/usr/bin/env python3
"""UpgradeTree.csv를 읽는다. 파이썬 밸런스 도구의 공통 입구다.

이전에는 check_upgrade_tree.py가 UpgradeTreeGenerator.cs를 정규식으로 파싱했다.
원본이 CSV로 내려왔으므로 그 파서는 필요 없다.

설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §5.2
표준 라이브러리만 쓴다.
"""

import csv
from pathlib import Path

DEFAULT_PATH = "Assets/GameData/UpgradeData/UpgradeTree.csv"


def load(path=DEFAULT_PATH):
    """CSV를 읽어 dict 목록으로 돌려준다. 타입은 여기서 확정한다."""
    p = Path(path)
    if not p.exists():
        raise FileNotFoundError(f"UpgradeTree.csv가 없다: {path}")

    rows = []
    with open(p, encoding="utf-8", newline="") as f:
        for r in csv.DictReader(f):
            nid = (r.get("nodeId") or "").strip()
            if not nid:
                continue
            rows.append({
                "nodeId":         nid,
                "tier":           int(r["tier"]),
                "cost":           int(r["cost"]),
                "effectType":     (r.get("effectType") or "").strip(),
                "effectValue":    float(r["effectValue"]),
                "isPercentage":   _bool(r.get("isPercentage")),
                "parentIds":      _parents(r.get("parentIds")),
                "uiX":            float(r["uiX"]),
                "uiY":            float(r["uiY"]),
                "displayNameKey": r.get("displayNameKey") or "",
                "descriptionKey": r.get("descriptionKey") or "",
                "locked":         _bool(r.get("locked")),
            })
    return rows


def _bool(s):
    return (s or "").strip().lower() == "true"


def _parents(s):
    # 세미콜론 구분 — 쉼표는 CSV 구분자라 못 쓴다 (C# 리더와 같은 규약)
    return [p.strip() for p in (s or "").split(";") if p.strip()]
```

- [ ] **Step 2: 리더를 확인한다**

```
python -c "import sys;sys.path.insert(0,'Tools/telemetry');import upgrade_tree_csv as u;r=u.load();print(len(r));print(r[0])"
```
기대: `50`과 첫 행 dict. `parentIds`가 리스트일 것.

- [ ] **Step 3: `check_upgrade_tree.py`를 CSV로 돌린다**

`Node` 클래스의 **속성 이름(`id` / `name` / `tier` / `cost` / `pos` / `parents` / `effect`)은
그대로 둔다.** 하위 로직(`ancestors` / `topo_order` / `simulate` / `structural_checks`)이
전부 이 이름을 쓰므로, 생성자만 바꾸면 나머지는 손댈 필요가 없다.

**3-1.** 상단 `DEFAULT_SRC`와 import를 바꾼다:

```python
import upgrade_tree_csv

DEFAULT_SRC = upgrade_tree_csv.DEFAULT_PATH
DEFAULT_TESTS = "Assets/Tests/EditMode/UpgradeTreeCostTests.cs"
```

`Tools/telemetry/`를 import 경로에 넣기 위해, 다른 import 위에 다음을 둔다:

```python
sys.path.insert(0, str(Path(__file__).parent))
```

**3-2.** `NODE_RE` 정규식과 그 위의 설명 주석 2줄(42~56행)을 **통째로 지운다.**

**3-3.** `Node.__init__`을 CSV 행을 받도록 바꾼다:

```python
class Node:
    __slots__ = ("id", "name", "tier", "cost", "pos", "parents", "effect")

    def __init__(self, row):
        self.id = row["nodeId"]
        self.name = row["displayNameKey"]
        self.tier = row["tier"]
        self.cost = row["cost"]
        self.pos = (row["uiX"], row["uiY"])
        self.parents = row["parentIds"]
        self.effect = row["effectType"]

    @property
    def is_facility(self):
        return self.id.startswith("Facility_")
```

**3-4.** `parse()`를 바꾼다:

```python
def parse(src):
    nodes = [Node(r) for r in upgrade_tree_csv.load(src)]
    if not nodes:
        sys.exit("노드를 하나도 못 읽었다 — %s를 확인할 것" % src)
    return {n.id: n for n in nodes}
```

**3-5.** 모듈 docstring에서 아래 문단을 —

> 이 스크립트는 C# 기획 명세(GetUpgradePlanData)를 직접 파싱하므로
> Unity도, 에셋 재생성도 필요 없다. 고치고 바로 돌려보면 된다.

이렇게 고친다:

> 이 스크립트는 UpgradeTree.csv(트리의 단일 원본)를 읽으므로
> Unity도, 에셋 재생성도 필요 없다. CSV를 고치고 바로 돌려보면 된다.

`--suggest`의 사다리 재적합 로직과 `simulate` / `structural_checks`는 **건드리지 않는다.**
입력 출처만 바뀐다.

- [ ] **Step 3-b: 기대값이 Ignore됐다는 사실을 표시한다**

`read_test_expectations()`는 `UpgradeTreeCostTests.cs`에서 `Assert.AreEqual` 상수를 긁어온다.
Task 3에서 단언을 지우지 않고 `[Ignore]`만 붙였으므로 **이 함수는 계속 옛 기대값을 읽어
"기대 4,120 / 실제 8,340" 같은 줄을 찍는다.** 이제 그 숫자는 아무것도 지키지 않으므로,
그대로 두면 진단 결과를 오해하게 된다.

함수 끝의 `return exp` 앞에 다음을 넣는다:

```python
    # 2026-08-21: 이 기대값들은 전부 [Ignore] 상태다(§9.1). 숫자는 참고용으로 남기되
    # 라벨에 표시해서, 진단 출력이 "지켜지고 있는 값"처럼 읽히지 않게 한다.
    if "[Ignore(" in text:
        exp = {f"{k} (Ignore됨)": v for k, v in exp.items()}
```

- [ ] **Step 4: 진단과 제안을 둘 다 돌린다**

```
python Tools/telemetry/check_upgrade_tree.py
python Tools/telemetry/check_upgrade_tree.py --suggest
```

기대: 둘 다 예외 없이 끝나고, 노드 수가 **50**으로 찍힌다.
필수 노드 집합(면허 조상)이 이전 실행과 같은지 눈으로 비교한다 — 데이터가 안 바뀌었으므로 같아야 한다.

- [ ] **Step 5: 지워진 줄 수를 확인한다**

```
wc -l Tools/telemetry/check_upgrade_tree.py Tools/telemetry/upgrade_tree_csv.py
```
정규식 파서를 지운 만큼 `check_upgrade_tree.py`가 짧아졌을 것이다. 스펙 §5.2가 약속한 "코드가 준다"가 실제로 일어났는지 본다. 안 줄었으면 파서 잔재가 남아 있다.

- [ ] **Step 6: 체크포인트**

UVCS 체크인: `upgrade_tree_csv.py` / `check_upgrade_tree.py`
메시지: `refactor: 파이썬 도구가 C# 소스 대신 UpgradeTree.csv를 읽는다`

---

## 1단계 완료 조건

- [ ] `UpgradeTree.csv` 50행이 단일 원본이고, C#·JSON·파이썬이 모두 그것을 읽는다
- [ ] `migrate_tree_to_csv.py --verify` 불일치 0건
- [ ] 노드 에셋 50개, 생성기 로그의 "삭제" 0
- [ ] `priceData.json` `upgradeNodes` 50개, `Facility_Workbench_T0` 없음, 면허 I = 1,280
- [ ] EditMode: 수치 6개 Ignored, 구조 테스트 + `UpgradeTreeCsvTests` 8개 + id 정합 1개 통과
- [ ] `check_upgrade_tree.py`가 CSV로 돌고 C# 정규식 파서가 없다

**여기서 멈추고 플레이한다.** 면허가 700G → 1,280G로 돌아왔으므로 체감이 달라진다.
CSV의 `cost` 칸을 고치고 생성기 + export를 다시 돌리는 것이 이제 편집 한 번이면 된다 —
이 단계가 벌어온 것이 그것이다.

## 다음 단계 (별도 계획)

2~4단계(런 모델 · 한계효용 합성기 · 오토플레이 검증)는 **1단계를 써본 피드백을 받은 뒤**
별도 계획으로 작성한다. 스펙 §6~§8이 대상이다.
