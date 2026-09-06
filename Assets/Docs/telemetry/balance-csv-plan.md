# 밸런스판 CSV 구현 계획

> **에이전트 작업자용:** 이 계획은 `superpowers:subagent-driven-development` 또는
> `superpowers:executing-plans` 로 태스크 단위 실행을 전제로 작성되었다.
> 각 단계는 체크박스(`- [ ]`)로 추적한다.

**Goal:** 이미 흐르고 있는 텔레메트리 JSONL에 회차 식별자와 밸런싱 KPI를 보강하고,
이를 `days.csv` / `dives.csv` 두 표로 굽는 파이썬 변환기를 만든다.

**Architecture:** 수집 파이프라인(`Telemetry` / `TelemetryBuffer` / `TelemetryFileSink` / `TelemetryRunner`)은
수정하지 않는다. 작업은 ① `PlayerData` + `TelemetrySession`에 `run_id`/`is_fixture` 공통 필드 추가,
② 기존 `dive_end` / `day_settled` 호출부에 `.Add()` 확충(신규 순수 C# `RegionDwellTracker` 포함),
③ `Tools/telemetry/export_csv.py` 변환기 — 세 갈래다.

**Tech Stack:** Unity 6000.3.2f1, C#, NUnit(EditMode), Python 3 표준 라이브러리만

**설계 문서:** [`Assets/Docs/telemetry/balance-csv-design.md`](balance-csv-design.md)
**상위 설계:** [`Assets/Docs/telemetry/design.md`](design.md)

---

## Global Constraints

- **버전 관리는 UVCS다.** `git` 명령을 쓰지 않는다. 각 태스크 끝의 "체크포인트"는 **사람이 직접 UVCS에 체크인**한다.
- **Unity Test Runner 실행은 사람이 직접 한다.** 구현자는 테스트를 작성만 하고, 실행 결과를 기다리지 않고 다음 태스크로 진행한다.
- 신규 파일 첫 줄에 `// @tags: ...` 주석을 넣는다 (프로젝트 파일 검색 규약).
- 코드 주석·로그는 한국어, 식별자는 영어.
- **네임스페이스를 쓰지 않는다** — `_Core/Telemetry` 하위 기존 코드는 전역 네임스페이스다. 동일하게 맞춘다.
- JSON 키는 snake_case. Phase 2 Supabase 컬럼명과 1:1 대응(`design.md` §5).
- **텔레메트리 코드는 절대 예외를 밖으로 던지지 않는다.** 실패는 삼키고 `Debug.LogWarning`만 남긴다.
- **수집 파이프라인 4개 파일(`Telemetry.cs` / `TelemetryBuffer.cs` / `TelemetryFileSink.cs` / `TelemetryRunner.cs`)은 이 계획에서 수정하지 않는다.**
- 파이썬은 **표준 라이브러리만** 사용한다 (`json` / `csv` / `argparse` / `pathlib` / `sys`). pandas 금지.
- 게임 동작 변화는 0이어야 한다. 특히 Task 6의 `DigResult.WasModified` 의미는 불변.

---

## 파일 구조

**신규 — 런타임**

| 파일 | 책임 |
|---|---|
| `Assets/Scripts/_Core/Telemetry/RegionDwellTracker.cs` | 층별 체류 시간 누적 (순수 C#) |

**신규 — 테스트**

| 파일 | 책임 |
|---|---|
| `Assets/Tests/EditMode/RegionDwellTrackerTests.cs` | 전환·누적·리셋·스냅샷 |
| `Assets/Tests/EditMode/TerrainDigPixelCountTests.cs` | `RemovedPixels` 불변식 + `WasModified` 회귀 가드 |

**신규 — 도구 (Unity `Assets/` 밖)**

| 파일 | 책임 |
|---|---|
| `Tools/telemetry/export_csv.py` | JSONL → `days.csv` / `dives.csv` |
| `Tools/telemetry/README.md` | 사용법 |

**수정**

| 파일 | 내용 |
|---|---|
| `Assets/Scripts/_Core/Telemetry/TelemetryPayload.cs` | `AddRaw()` — 중첩 JSON 오브젝트 삽입 |
| `Assets/Scripts/_Core/Telemetry/TelemetrySession.cs` | `RunId` / `IsFixture` 공통 필드 |
| `Assets/Tests/EditMode/TelemetrySessionTests.cs` | 위 필드 테스트 추가 |
| `Assets/Tests/EditMode/TelemetryPayloadTests.cs` | `AddRaw` 테스트 추가 |
| `Assets/Scripts/UI/Player/PlayerData.cs` | `runId` / `isFixture` 필드 |
| `Assets/Scripts/_Core/Managers/SaveManager.cs` | 뉴게임 발급 · 로드 백필 · `MarkRunAsFixture()` |
| `Assets/Scripts/Utils/DebugConsole/QAFixtureCommands.cs` | `fx load` 시 픽스처 마킹 |
| `Assets/Scripts/Utils/DebugConsole/DebugCommands.cs` | `gold`/`day`/`stamina` 시 픽스처 마킹 |
| `Assets/Scripts/_Core/Managers/SettlementManager.cs` | 층별 체류·파기 픽셀 누적, `dive_end` 필드 확충 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs` | `DigResult.RemovedPixels` |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` | 파기 픽셀을 `SettlementManager`에 전달 |
| `Assets/Scripts/Gameplay/Environment/BedInteractable.cs` | `day_settled` 필드 확충 |

---

## Task 1: `TelemetryPayload.AddRaw` — 중첩 JSON 오브젝트

`region_seconds`와 `minerals`는 `{"Dirt":124.5}` 형태의 **중첩 오브젝트**다.
현재 `TelemetryPayload`에는 문자열/숫자/불리언만 있어서, 오브젝트를 넣으면 따옴표로 감싸져 문자열이 된다.

**Files:**
- Modify: `Assets/Scripts/_Core/Telemetry/TelemetryPayload.cs`
- Test: `Assets/Tests/EditMode/TelemetryPayloadTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces: `TelemetryPayload AddRaw(string key, string rawJson)` — `rawJson`을 이스케이프 없이 그대로 값 자리에 넣는다. `null`/빈 문자열이면 `{}`를 넣는다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/TelemetryPayloadTests.cs` 끝에 추가한다.

```csharp
    [Test]
    public void AddRaw_중첩_오브젝트를_따옴표_없이_넣는다()
    {
        string json = TelemetryPayload.New()
            .Add("result", "return")
            .AddRaw("region_seconds", "{\"Dirt\":12.5,\"Stone\":3}")
            .ToJsonObject();

        Assert.AreEqual("{\"result\":\"return\",\"region_seconds\":{\"Dirt\":12.5,\"Stone\":3}}", json);
    }

    [Test]
    public void AddRaw_null이면_빈_오브젝트를_넣는다()
    {
        string json = TelemetryPayload.New().AddRaw("minerals", null).ToJsonObject();

        Assert.AreEqual("{\"minerals\":{}}", json);
    }
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Unity Test Runner(EditMode)에서 `TelemetryPayloadTests` 실행.
Expected: 컴파일 에러 — `TelemetryPayload`에 `AddRaw` 정의 없음.

> 실행은 사람이 한다. 구현자는 컴파일 에러를 확인만 하고 다음 단계로 간다.

- [ ] **Step 3: 최소 구현**

`TelemetryPayload.cs`의 `Add(string key, bool value)` 메서드 **바로 뒤**에 추가한다.

```csharp
    /// <summary>
    /// 이미 완성된 JSON 조각(오브젝트·배열)을 값 자리에 그대로 넣는다.
    /// 이스케이프하지 않으므로 호출자가 올바른 JSON을 보장해야 한다 —
    /// 중첩 오브젝트(region_seconds, minerals) 전용이다.
    /// </summary>
    public TelemetryPayload AddRaw(string key, string rawJson)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":")
           .Append(string.IsNullOrEmpty(rawJson) ? "{}" : rawJson);
        return this;
    }
```

- [ ] **Step 4: 테스트 통과 확인**

Unity Test Runner(EditMode) → `TelemetryPayloadTests` 전체 PASS 기대.

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `telemetry: TelemetryPayload.AddRaw 추가 (중첩 JSON 오브젝트)`

---

## Task 2: `TelemetrySession`에 `run_id` / `is_fixture` 공통 필드

**Files:**
- Modify: `Assets/Scripts/_Core/Telemetry/TelemetrySession.cs`
- Test: `Assets/Tests/EditMode/TelemetrySessionTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `TelemetrySession.RunId { get; set; }` — 기본값 `string.Empty`
  - `TelemetrySession.IsFixture { get; set; }` — 기본값 `false`
  - `BuildLine`이 내는 JSON에 `"run_id":"..."` 와 `"is_fixture":true|false` 포함.
    위치는 `session_id` 바로 뒤(공통 축 필드끼리 모아 둔다).

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/TelemetrySessionTests.cs` 끝에 추가한다.

```csharp
    [Test]
    public void BuildLine_run_id와_is_fixture를_공통_필드로_싣는다()
    {
        var session = new TelemetrySession("anon1", "sess1", "ea", "1.0.0");
        session.RunId = "run-abc";
        session.IsFixture = true;

        string line = session.BuildLine("dive_end", "{}");

        StringAssert.Contains("\"run_id\":\"run-abc\"", line);
        StringAssert.Contains("\"is_fixture\":true", line);
    }

    [Test]
    public void BuildLine_미설정이면_run_id는_빈문자열이고_is_fixture는_false다()
    {
        var session = new TelemetrySession("anon1", "sess1", "ea", "1.0.0");

        string line = session.BuildLine("session_start", "{}");

        StringAssert.Contains("\"run_id\":\"\"", line);
        StringAssert.Contains("\"is_fixture\":false", line);
    }
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Expected: 컴파일 에러 — `RunId` / `IsFixture` 정의 없음.

- [ ] **Step 3: 최소 구현**

`TelemetrySession.cs`의 `PlaytimeTotal` 프로퍼티 **바로 뒤**에 추가한다.

```csharp
    /// <summary>회차 식별자. 뉴게임마다 새로 발급되어 세이브에 영속한다. 미설정이면 빈 문자열.</summary>
    public string RunId { get; set; } = string.Empty;
    /// <summary>QA 픽스처·디버그 명령이 손댄 회차인지. true면 밸런스 분석에서 제외한다.</summary>
    public bool IsFixture { get; set; }
```

같은 파일 `BuildLine`에서 `session_id` 줄 바로 아래에 두 줄을 끼워 넣는다.

```csharp
        Str(sb, "session_id", _sessionId);  sb.Append(',');
        Str(sb, "run_id", RunId);           sb.Append(',');
        Bool(sb, "is_fixture", IsFixture);  sb.Append(',');
        Num(sb, "seq", _seq);               sb.Append(',');
```

파일 끝 `Num` 헬퍼 **바로 뒤**에 `Bool` 헬퍼를 추가한다.

```csharp
    private static void Bool(StringBuilder sb, string key, bool value)
    {
        sb.Append('"').Append(key).Append("\":")
          .Append(value ? "true" : "false");
    }
```

- [ ] **Step 4: 테스트 통과 확인**

Unity Test Runner(EditMode) → `TelemetrySessionTests` 전체 PASS 기대.
**기존 테스트도 함께 본다** — 라인 구조가 바뀌었으므로 전체 라인을 문자열 비교하던 케이스가 있으면 기대값을 갱신한다.

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `telemetry: run_id / is_fixture 공통 필드 추가`

---

## Task 3: `PlayerData`에 회차 필드 + 발급·백필

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerData.cs`
- Modify: `Assets/Scripts/_Core/Managers/SaveManager.cs` (`NewGame()` ~152, `Load()` 끝 ~615)

**Interfaces:**
- Consumes: `TelemetrySession.RunId` / `.IsFixture` (Task 2), `Telemetry.Context`
- Produces:
  - `PlayerData.runId` (string) / `PlayerData.isFixture` (bool)
  - `SaveManager.SyncTelemetryRun()` — `playerData`의 회차 정보를 `Telemetry.Context`에 반영. `public`.

`PlayerData`는 `JsonUtility`로 직렬화되므로 **구버전 세이브는 `runId`가 빈 문자열, `isFixture`가 false로 로드된다.**
`Load()`에서 빈 값을 발견하면 그 자리에서 발급하고 저장해 흡수한다.

- [ ] **Step 1: `PlayerData`에 필드 추가**

`Assets/Scripts/UI/Player/PlayerData.cs`의 `[Header("Gold")] public int gold;` **바로 뒤**에 추가한다.

```csharp
    [Header("Telemetry")]
    // 회차(playthrough) 식별자. 뉴게임마다 GUID 발급 → 밸런스 CSV의 기본키.
    // 구버전 세이브는 빈 문자열로 로드되고 SaveManager.Load()에서 발급된다.
    public string runId;
    // QA 픽스처·디버그 명령이 한 번이라도 손댄 회차. 한 번 서면 되돌리지 않는다.
    public bool isFixture;
```

- [ ] **Step 2: `SaveManager.NewGame()`에서 발급**

`Assets/Scripts/_Core/Managers/SaveManager.cs`의 `NewGame()` 안,
`PlayerData data = new PlayerData(playerSO); playerData = data;` **바로 뒤**에 추가한다.

```csharp
        // 회차 식별자 발급 — 밸런스 CSV가 이 값으로 플레이스루를 가른다(balance-csv-design.md §4)
        data.runId = System.Guid.NewGuid().ToString("N");
        data.isFixture = false;
        SyncTelemetryRun();
```

- [ ] **Step 3: `SaveManager.Load()`에서 백필 + 동기화**

같은 파일 `Load()`의 마지막 줄 `DungeonStateStore.Apply(data.dungeonSave);` **바로 뒤**에 추가한다.

```csharp
        // 회차 식별자 백필 — 구버전 세이브는 runId가 비어 있다. 그 자리에서 발급해 흡수한다.
        if (string.IsNullOrEmpty(playerData.runId))
        {
            playerData.runId = System.Guid.NewGuid().ToString("N");
            Save();
            Debug.Log($"[SaveManager] 구버전 세이브에 회차 식별자 발급: {playerData.runId}");
        }
        SyncTelemetryRun();
```

> `playerData`가 `data`로 대입되는 지점이 `Load()` 안에 이미 있다.
> 위 코드는 `playerData`를 쓰므로 대입 이후에 놓여야 한다 — `Load()`의 **맨 끝**이 그 조건을 만족한다.

- [ ] **Step 4: `SyncTelemetryRun()` 구현**

같은 파일 `Load()` 메서드 **바로 뒤**(클래스 닫는 중괄호 앞)에 추가한다.

```csharp
    /// <summary>
    /// 현재 세이브의 회차 정보를 텔레메트리 컨텍스트에 반영한다.
    /// 이후 기록되는 모든 이벤트가 run_id·is_fixture를 달고 나간다.
    /// Telemetry가 아직 Init 전이면 Context가 null이므로 조용히 넘어간다.
    /// </summary>
    public void SyncTelemetryRun()
    {
        if (Telemetry.Context == null || playerData == null) return;
        Telemetry.Context.RunId = playerData.runId ?? string.Empty;
        Telemetry.Context.IsFixture = playerData.isFixture;
    }
```

- [ ] **Step 5: 컴파일 확인**

Unity 에디터로 돌아가 컴파일 에러가 없는지 확인한다.
Expected: 에러 없음.

- [ ] **Step 6: 수동 확인**

에디터에서 뉴게임 → 침대에서 자기 → `Tools/Telemetry/로그 폴더 열기`로 JSONL을 열어
`day_settled` 라인에 비어 있지 않은 `"run_id"`가 있는지 눈으로 확인한다.

- [ ] **Step 7: 체크포인트**

UVCS 체크인: `telemetry: PlayerData에 runId/isFixture 추가 + 발급·백필`

---

## Task 4: 디버그 조작 시 픽스처 마킹

`fx load`는 디스크의 세이브 파일을 갈아끼운 뒤 씬을 재시작한다.
메모리에 플래그를 세워 봐야 재시작에 날아가므로, **파일에 직접 써야 한다.**
파일 경로 지식은 `SaveManager`가 갖고 있으므로 헬퍼를 거기에 둔다.

**Files:**
- Modify: `Assets/Scripts/_Core/Managers/SaveManager.cs`
- Modify: `Assets/Scripts/Utils/DebugConsole/QAFixtureCommands.cs` (`Load()` ~102)
- Modify: `Assets/Scripts/Utils/DebugConsole/DebugCommands.cs`

**Interfaces:**
- Consumes: `PlayerData.isFixture` (Task 3), `SaveManager.SyncTelemetryRun()` (Task 3)
- Produces:
  - `SaveManager.MarkRunAsFixture()` — 메모리의 `playerData.isFixture = true` + 저장 + 텔레메트리 동기화
  - `SaveManager.MarkSlotFileAsFixture(int slot)` — **static**. 지정 슬롯의 JSON 파일을 열어 `isFixture`만 true로 바꿔 다시 쓴다

- [ ] **Step 1: `SaveManager`에 마킹 헬퍼 두 개 추가**

`SyncTelemetryRun()` **바로 뒤**에 추가한다.

```csharp
    /// <summary>
    /// 현재 회차를 '디버그 조작됨'으로 마킹한다. 한 번 서면 되돌리지 않는다 —
    /// 조작된 회차는 이후 일차의 곡선도 신뢰할 수 없다(balance-csv-design.md §4).
    /// </summary>
    public void MarkRunAsFixture()
    {
        if (playerData == null || playerData.isFixture) return;
        playerData.isFixture = true;
        Save();
        SyncTelemetryRun();
        Debug.Log("[SaveManager] 이 회차를 디버그 조작됨(isFixture)으로 마킹했다.");
    }

    /// <summary>
    /// 지정 슬롯의 세이브 파일을 열어 isFixture만 true로 바꿔 다시 쓴다.
    /// fx load처럼 '파일을 갈아끼운 뒤 씬을 재시작'하는 경로용 — 메모리에는 세울 수 없다.
    /// 실패는 삼킨다. 마킹 실패로 QA 흐름이 끊기면 안 된다.
    /// </summary>
    public static void MarkSlotFileAsFixture(int slot)
    {
        try
        {
            string slotPath = Path.Combine(Application.persistentDataPath, $"playerData_{slot}.json");
            if (!File.Exists(slotPath)) return;

            PlayerData data = JsonUtility.FromJson<PlayerData>(File.ReadAllText(slotPath));
            if (data == null || data.isFixture) return;

            data.isFixture = true;
            File.WriteAllText(slotPath, JsonUtility.ToJson(data, true));
            Debug.Log($"[SaveManager] 슬롯 {slot} 세이브를 디버그 조작됨(isFixture)으로 마킹했다.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 픽스처 마킹 실패: {e.Message}");
        }
    }
```

`SaveManager.cs` 상단 `using`에 `System`(`Exception`용)이 없으면 추가한다.
`System.IO`와 `UnityEngine`은 이미 있다(`path` 프로퍼티와 `Debug` 사용).

- [ ] **Step 2: `fx load`에 마킹 삽입**

`Assets/Scripts/Utils/DebugConsole/QAFixtureCommands.cs`의 `Load()`에서
`string error = QAFixtureStore.Restore(name, slot);` 의 에러 체크 **바로 뒤**,
`sm.CurrentSlotIndex = slot;` **앞**에 추가한다.

```csharp
            // 픽스처로 복원된 회차는 밸런스 분석에서 제외한다(balance-csv-design.md §4).
            // 씬이 재시작되므로 메모리가 아니라 방금 갈아끼운 파일에 직접 쓴다.
            SaveManager.MarkSlotFileAsFixture(slot);
```

- [ ] **Step 3: `gold` / `day` / `stamina` 명령에 마킹 삽입**

`Assets/Scripts/Utils/DebugConsole/DebugCommands.cs`에서 `gold` / `day` / `stamina`
명령의 **핸들러 함수 안, 실제로 값을 바꾼 뒤** 각각 한 줄을 넣는다.

```csharp
            SaveManager.Instance?.MarkRunAsFixture();
```

`time` / `pos` / `help` / `clear`에는 **넣지 않는다.** 밸런스 수치를 바꾸지 않으며,
배속까지 마킹하면 QA 플레이 대부분이 걸러져 플래그가 무의미해진다.

- [ ] **Step 4: 컴파일 확인**

Expected: 에러 없음.

- [ ] **Step 5: 수동 확인**

에디터 플레이 → `` ` `` 로 콘솔 열기 → `gold 1000` → 침대에서 자기 →
JSONL의 `day_settled` 라인에 `"is_fixture":true` 확인.

- [ ] **Step 6: 체크포인트**

UVCS 체크인: `telemetry: 디버그 조작 시 회차를 isFixture로 마킹`

---

## Task 5: `RegionDwellTracker` — 층별 체류 시간 (순수 C#)

`SettlementManager.Update()`는 매 프레임 도는 코드다. Dictionary 조회를 프레임마다 하지 않도록
현재 구간은 필드에 누적하고 **전환 시에만** Dictionary로 flush한다.
MonoBehaviour 밖으로 빼서 EditMode 테스트도 가능하게 한다.

**Files:**
- Create: `Assets/Scripts/_Core/Telemetry/RegionDwellTracker.cs`
- Test: `Assets/Tests/EditMode/RegionDwellTrackerTests.cs`

**Interfaces:**
- Consumes: 없음 (순수 C#, Unity 의존 없음)
- Produces:
  - `void Tick(float deltaTime)` — 현재 지역에 누적
  - `void SwitchTo(string region)` — 같은 지역이면 no-op, 다르면 현재 구간을 flush하고 전환
  - `string ToPayloadObject()` — `{"Dirt":12.5,"Stone":3}`. 호출 시점의 현재 구간까지 포함한 **스냅샷**이며, 호출 후에도 계속 누적된다
  - `void Reset()` — 전부 비운다

**불변식:** 현재 지역의 누적값은 `SwitchTo`/`ToPayloadObject` 전까지 Dictionary가 아니라 `_pending` 필드에 있다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/RegionDwellTrackerTests.cs` 를 새로 만든다.

```csharp
// @tags: test, telemetry, region, dwell, editmode

using NUnit.Framework;

public class RegionDwellTrackerTests
{
    [Test]
    public void 지역_전환_없이_누적하면_한_지역에_모인다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(1.5f);
        t.Tick(1.0f);

        Assert.AreEqual("{\"Dirt\":2.5}", t.ToPayloadObject());
    }

    [Test]
    public void 전환하면_이전_지역_누적이_보존된다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.SwitchTo("Stone");
        t.Tick(3f);

        Assert.AreEqual("{\"Dirt\":2,\"Stone\":3}", t.ToPayloadObject());
    }

    [Test]
    public void 같은_지역으로_전환하면_누적이_끊기지_않는다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.SwitchTo("Dirt");
        t.Tick(3f);

        Assert.AreEqual("{\"Dirt\":5}", t.ToPayloadObject());
    }

    [Test]
    public void 되돌아온_지역은_기존_누적에_더해진다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.SwitchTo("Stone");
        t.Tick(1f);
        t.SwitchTo("Dirt");
        t.Tick(4f);

        Assert.AreEqual("{\"Dirt\":6,\"Stone\":1}", t.ToPayloadObject());
    }

    [Test]
    public void 스냅샷_후에도_계속_누적된다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.ToPayloadObject();
        t.Tick(3f);

        Assert.AreEqual("{\"Dirt\":5}", t.ToPayloadObject());
    }

    [Test]
    public void Reset하면_비어_있다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.Reset();

        Assert.AreEqual("{}", t.ToPayloadObject());
    }

    [Test]
    public void 지역_설정_전_Tick은_버려진다()
    {
        var t = new RegionDwellTracker();
        t.Tick(5f);

        Assert.AreEqual("{}", t.ToPayloadObject());
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Expected: 컴파일 에러 — `RegionDwellTracker` 타입 없음.

- [ ] **Step 3: 최소 구현**

`Assets/Scripts/_Core/Telemetry/RegionDwellTracker.cs` 를 새로 만든다.

```csharp
// @tags: telemetry, region, dwell, timer, pure-logic

using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// 층(지역)별 체류 시간을 누적한다. 잠수 1회 단위로 쓰인다(balance-csv-design.md §6).
///
/// 매 프레임 도는 코드에서 Dictionary를 만지지 않도록, 현재 구간은 _pending 필드에 모으고
/// 지역이 바뀔 때만 Dictionary로 옮긴다. 프레임당 비용은 float += 하나다.
///
/// Unity 의존이 없으므로 EditMode 테스트가 가능하다.
/// </summary>
public sealed class RegionDwellTracker
{
    private readonly Dictionary<string, float> _seconds = new Dictionary<string, float>();
    private string _current = string.Empty;
    private float _pending;

    /// <summary>현재 지역에 누적한다. 지역이 설정되기 전 호출은 버려진다.</summary>
    public void Tick(float deltaTime)
    {
        if (string.IsNullOrEmpty(_current) || deltaTime <= 0f) return;
        _pending += deltaTime;
    }

    /// <summary>지역 전환. 같은 지역이면 아무것도 하지 않는다(누적이 끊기지 않는다).</summary>
    public void SwitchTo(string region)
    {
        if (region == _current) return;
        Flush();
        _current = region ?? string.Empty;
    }

    /// <summary>
    /// 현재까지의 누적을 JSON 오브젝트 문자열로 낸다.
    /// 현재 구간을 flush한 스냅샷이며, 호출 후에도 계속 누적할 수 있다.
    /// </summary>
    public string ToPayloadObject()
    {
        Flush();

        var sb = new StringBuilder(64);
        sb.Append('{');
        bool first = true;
        foreach (var kv in _seconds)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(TelemetryPayload.EscapeJson(kv.Key)).Append("\":")
              .Append(kv.Value.ToString("0.##", CultureInfo.InvariantCulture));
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>전부 비운다. 잠수 시작 시 호출한다.</summary>
    public void Reset()
    {
        _seconds.Clear();
        _current = string.Empty;
        _pending = 0f;
    }

    /// <summary>현재 구간의 누적을 Dictionary로 옮긴다.</summary>
    private void Flush()
    {
        if (_pending <= 0f || string.IsNullOrEmpty(_current)) { _pending = 0f; return; }

        _seconds.TryGetValue(_current, out float prev);
        _seconds[_current] = prev + _pending;
        _pending = 0f;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Unity Test Runner(EditMode) → `RegionDwellTrackerTests` 7건 전부 PASS 기대.

> `Dictionary` 순회 순서는 삽입 순서가 보장되지 않지만, .NET의 `Dictionary`는 삭제가 없는 한
> 실질적으로 삽입 순서로 순회한다. 위 테스트는 그 전제 위에 있다.
> **테스트가 순서 때문에 실패하면 구현이 아니라 테스트를 고친다** — 키별로 파싱해 비교하도록 바꾼다.

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `telemetry: RegionDwellTracker 추가 (층별 체류 시간)`

---

## Task 6: 파기 픽셀 카운트 — `DigResult.RemovedPixels`

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs`
  (`Dig()` ~202, `DigResult` ~313, `ProcessDigPixels()` ~325)

**Interfaces:**
- Consumes: 없음
- Produces: `TerrainModifier.DigResult.RemovedPixels` (int) — 이번 파기로 실제 제거된 픽셀 수

**⚠ 동작 보존:** `WasModified`의 의미와 소비처를 바꾸지 않는다.
`RemovedPixels > 0`으로 치환하지 말 것. 기존 필드는 그대로 두고 필드만 추가한다.
`Explode()`가 만드는 `DigResult`는 이 필드를 세우지 않는다(기본값 0) — 폭발은 플레이어 파기가 아니다.

- [ ] **Step 1: `DigResult`에 필드 추가**

`TerrainModifier.cs`의 `DigResult` 구조체를 다음으로 바꾼다.

```csharp
    public struct DigResult
    {
        public bool WasModified;
        public int RawMinX, RawMaxX, RawMinY, RawMaxY;      // Raw bounds (may exceed chunk)
        public int ClampedMinX, ClampedMaxX, ClampedMinY, ClampedMaxY;  // Clamped to chunk
        public float RadiusPx;
        /// <summary>
        /// 이번 파기로 실제 제거된 픽셀 수. 밸런스 텔레메트리용(balance-csv-design.md §7).
        /// Explode() 경로는 세우지 않는다 — 폭발은 플레이어가 판 것이 아니다.
        /// </summary>
        public int RemovedPixels;
    }
```

- [ ] **Step 2: `ProcessDigPixels` 반환 타입을 int로 변경**

시그니처를 바꾼다.

```csharp
    private int ProcessDigPixels(ChunkData data, int minX, int minY, int maxX, int maxY,
                                   int centerPx, int centerPy, float radiusPx, float angle, int toolIndex,
                                   ParticleSpawnCallback particleCallback, PixelToWorldCallback pixelToWorld)
```

메서드 안 `bool pixelChanged = false;` 를 다음으로 바꾼다.

```csharp
        int removed = 0;
```

픽셀 제거 블록의 `pixelChanged = true;` 를 다음으로 바꾼다.

```csharp
                removed++;
```

메서드 끝 `return pixelChanged;` 를 다음으로 바꾼다.

```csharp
        return removed;
```

- [ ] **Step 3: `Dig()` 호출부를 맞춘다**

`Dig()` 안의 호출과 결과 조립을 다음으로 바꾼다.

```csharp
        // Process pixels (Pass true geometric center)
        int removedPixels = ProcessDigPixels(data, minX, minY, maxX, maxY, 
                                         centerPx, centerPy, 
                                         radiusPx, angle, toolIndex,
                                         particleCallback, pixelToWorld);
        bool modified = removedPixels > 0;
        
        DigResult result = new DigResult
        {
            WasModified = modified,
            RawMinX = rawMinX,
            RawMaxX = rawMaxX,
            RawMinY = rawMinY,
            RawMaxY = rawMaxY,
            ClampedMinX = minX,
            ClampedMaxX = maxX,
            ClampedMinY = minY,
            ClampedMaxY = maxY,
            RadiusPx = radiusPx,
            RemovedPixels = removedPixels
        };
```

> `modified`의 값은 이전과 정확히 같다 — 기존 `pixelChanged`도 "픽셀을 하나라도 지웠는가"였다.
> 아래 `if (modified) data.MarkDirty();` 는 그대로 둔다.

- [ ] **Step 4: 컴파일 확인**

Expected: 에러 없음. `ProcessDigPixels`의 호출부는 `Dig()` 한 곳뿐이다.

- [ ] **Step 5: 불변식 테스트 작성**

`Assets/Tests/EditMode/TerrainDigPixelCountTests.cs` 를 새로 만든다.
`ChunkDataBorderDataTests`와 같은 패턴(`new ChunkData(w,h)` → `Dispose()`)을 따른다.

기하 계산을 하드코딩하지 않는다 — **"보고한 수 == 실제로 지워진 수"** 라는 불변식만 검증한다.

```csharp
// @tags: test, terrain, dig, pixel-count, telemetry, editmode

using NUnit.Framework;
using UnityEngine;

public class TerrainDigPixelCountTests
{
    private const int Size = 64;
    private const float Ppu = 100f;

    /// <summary>월드 좌표 → 픽셀 좌표. PPU만 곱하는 단순 변환.</summary>
    private static Vector2Int ToPixel(Vector2 world)
        => new Vector2Int(Mathf.RoundToInt(world.x * Ppu), Mathf.RoundToInt(world.y * Ppu));

    private static ChunkData MakeSolidChunk()
    {
        var data = new ChunkData(Size, Size);
        for (int i = 0; i < Size * Size; i++)
        {
            data.BasePixels[i] = new Color32(120, 90, 60, 255);
            data.PixelInfo[i] = 1; // 1 = Dirt
        }
        return data;
    }

    private static int CountTransparent(ChunkData data)
    {
        int n = 0;
        for (int i = 0; i < Size * Size; i++)
            if (data.BasePixels[i].a == 0) n++;
        return n;
    }

    [Test]
    public void RemovedPixels가_실제로_지워진_픽셀_수와_같다()
    {
        var data = MakeSolidChunk();
        // VerticalScale=1 → 타원이 아니라 원. 기하가 단순해져 판정이 흔들리지 않는다.
        var modifier = new TerrainModifier(Ppu, 1f);

        // player (0.32,0.32) → 픽셀 (32,32) / mouse (0.42,0.32) → 픽셀 (42,32)
        // 파기 중심은 반경 5px 원으로 청크 안쪽에 완전히 들어온다.
        var result = modifier.Dig(
            data,
            new Vector2(0.42f, 0.32f),
            new Vector2(0.32f, 0.32f),
            radius: 0.05f,
            toolIndex: 0,
            ToPixel);

        Assert.IsTrue(result.WasModified, "고체 지형을 팠는데 WasModified가 false다");
        Assert.Greater(result.RemovedPixels, 0);
        Assert.AreEqual(CountTransparent(data), result.RemovedPixels);

        data.Dispose();
    }

    [Test]
    public void 이미_비어_있으면_RemovedPixels는_0이고_WasModified도_false다()
    {
        var data = new ChunkData(Size, Size); // 전부 투명(알파 0)으로 시작
        var modifier = new TerrainModifier(Ppu, 1f);

        var result = modifier.Dig(
            data,
            new Vector2(0.42f, 0.32f),
            new Vector2(0.32f, 0.32f),
            radius: 0.05f,
            toolIndex: 0,
            ToPixel);

        Assert.AreEqual(0, result.RemovedPixels);
        Assert.IsFalse(result.WasModified, "빈 공간을 팠는데 WasModified가 true다 — 기존 동작이 깨졌다");

        data.Dispose();
    }
}
```

- [ ] **Step 6: 테스트 실행 확인**

Unity Test Runner(EditMode) → `TerrainDigPixelCountTests` 2건 PASS 기대.

> 두 번째 테스트가 **기존 동작 보존의 회귀 가드**다. `WasModified`의 의미가 바뀌면 여기서 걸린다.

- [ ] **Step 7: 체크포인트**

UVCS 체크인: `telemetry: DigResult.RemovedPixels 추가 (파기 픽셀 카운트)`

---

## Task 7: `SettlementManager` — 층별 체류·파기 픽셀 수집 + `dive_end` 확충

**Files:**
- Modify: `Assets/Scripts/_Core/Managers/SettlementManager.cs`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` (`Dig()` ~706)

**Interfaces:**
- Consumes: `RegionDwellTracker` (Task 5), `DigResult.RemovedPixels` (Task 6), `TelemetryPayload.AddRaw` (Task 1)
- Produces:
  - `SettlementManager.AddDugPixels(int count)` — 이번 잠수의 파기 픽셀 누적. `_isTracking`이 false면 무시
  - `dive_end` 이벤트에 `region_seconds` / `pixels_dug` / `stamina_pct` / `weight_ratio` / `minerals` 추가

- [ ] **Step 1: 필드와 누적 API 추가**

`SettlementManager.cs`의 텔레메트리 필드 블록(`private static readonly HashSet<string> _visitedRegions ...` 아래)에 추가한다.

```csharp
    // 층별 체류 시간·파기 픽셀. 잠수마다 리셋된다(balance-csv-design.md §6·§7).
    private readonly RegionDwellTracker _dwell = new RegionDwellTracker();
    private int _dugPixels;
```

`AddMinedMineral` 메서드 **바로 뒤**에 추가한다.

```csharp
    /// <summary>
    /// 이번 잠수의 파기 픽셀을 누적한다. TerrainChunk.Dig가 호출한다.
    /// 추적 중이 아니면(지상·정산 후) 무시한다.
    /// </summary>
    public void AddDugPixels(int count)
    {
        if (!_isTracking || count <= 0) return;
        _dugPixels += count;
    }
```

- [ ] **Step 2: `Update`와 지역 전환에 배선**

`Update()`의 `TimeUnderground += Time.deltaTime;` **바로 뒤**에 추가한다.

```csharp
        _dwell.Tick(Time.deltaTime);
```

`UpdateTelemetryContext()`의 지역 전환 분기 안, `Telemetry.Context.Region = region;` **바로 뒤**에 추가한다.

```csharp
            _dwell.SwitchTo(region);
```

- [ ] **Step 3: `StartTracking`에서 리셋**

`StartTracking()`의 `_lastReportedRegion = string.Empty;` **바로 뒤**에 추가한다.

```csharp
        _dwell.Reset();
        _dugPixels = 0;
```

- [ ] **Step 4: `LogDiveEnd` 확충**

`LogDiveEnd(string result)` 전체를 다음으로 바꾼다.

```csharp
    /// <summary>잠수 종료 이벤트. result는 "return"(정상 귀환) 또는 "escape"(긴급 탈출).</summary>
    private void LogDiveEnd(string result)
    {
        int mineralCount = 0;
        foreach (var kv in MinedMinerals) mineralCount += kv.Value;

        var stat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        var enc = FindFirstObjectByType<EncumbranceController>(FindObjectsInactive.Include);

        float maxStamina = stat != null ? stat.MaxStamina : 0f;
        float staminaPct = (stat != null && maxStamina > 0f) ? stat.CurrentStamina / maxStamina : 0f;

        float threshold = enc != null ? enc.EncumbranceThreshold : 0f;
        float weightRatio = (enc != null && threshold > 0f) ? enc.TotalWeight / threshold : 0f;

        Telemetry.Log(TelemetryEvents.DiveEnd, TelemetryPayload.New()
            .Add("result", result)
            .Add("max_depth", MaxDepth)
            .Add("seconds", TimeUnderground)
            .Add("mineral_kinds", MinedMinerals.Count)
            .Add("mineral_count", mineralCount)
            .Add("stamina_left", stat != null ? stat.CurrentStamina : 0f)
            .Add("stamina_pct", staminaPct)
            .Add("weight_ratio", weightRatio)
            .Add("pixels_dug", _dugPixels)
            .AddRaw("region_seconds", _dwell.ToPayloadObject())
            .AddRaw("minerals", BuildMineralsObject()));
    }

    /// <summary>캔 광물 구성을 JSON 오브젝트로. 예: {"Iron":12,"Gold":3}</summary>
    private string BuildMineralsObject()
    {
        var sb = new System.Text.StringBuilder(64);
        sb.Append('{');
        bool first = true;
        foreach (var kv in MinedMinerals)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(TelemetryPayload.EscapeJson(kv.Key.ToString())).Append("\":")
              .Append(kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append('}');
        return sb.ToString();
    }
```

> `AbortTracking()`은 `LogDiveEnd("escape")`를 **누적값 리셋 전에** 호출한다 — 기존 순서가 이미 맞다.
> `_dugPixels`/`_dwell`도 그 뒤 `StartTracking()`에서 리셋되므로 별도 처리가 필요 없다.

- [ ] **Step 5: `TerrainChunk`에서 파기 픽셀 전달**

`Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs`의 `Dig()`에서
`if (result.WasModified)` **바로 앞**에 추가한다.

```csharp
        // 밸런스 텔레메트리 — 플레이어가 판 픽셀만 센다(폭발·섬 제거는 이 경로가 아니다).
        if (result.RemovedPixels > 0)
            SettlementManager.Instance?.AddDugPixels(result.RemovedPixels);

```

- [ ] **Step 6: 컴파일 확인**

Expected: 에러 없음.

- [ ] **Step 7: 수동 확인**

에디터에서 지하로 내려가 지형을 좀 파고 층을 두 개 이상 지난 뒤 지상으로 귀환한다.
JSONL의 `dive_end` 라인에서 확인:
- `"pixels_dug"` 가 0보다 큼
- `"region_seconds"` 가 `{"Dirt":...,"Stone":...}` 형태의 **오브젝트**(따옴표로 감싼 문자열이 아님)
- `"minerals"` 가 오브젝트
- `"stamina_pct"` 가 0~1 범위

- [ ] **Step 8: 체크포인트**

UVCS 체크인: `telemetry: dive_end에 층별 체류·파기 픽셀·귀환 상태 추가`

---

## Task 8: `day_settled` 확충

**Files:**
- Modify: `Assets/Scripts/Gameplay/Environment/BedInteractable.cs` (`SleepRoutine()` ~106)

**Interfaces:**
- Consumes: 없음 (기존 `Telemetry.Log` 호출부 확장)
- Produces: `day_settled` 이벤트에 `mining_level` / `unlocked_nodes` / `warehouse_count` / `stock_value` / `stock_cost` 추가

- [ ] **Step 1: 헬퍼 두 개 추가**

`BedInteractable.cs`의 `SleepRoutine()` **바로 뒤**에 추가한다.

```csharp
    /// <summary>해금된 업그레이드 노드 수. 세이브 데이터가 없으면 0.</summary>
    private static int CountUnlockedNodes()
    {
        var sm = GameManager.Instance?.saveManager;
        var state = sm?.playerData?.upgradeTreeState;
        return state?.unlockedNodeIds?.Count ?? 0;
    }

    /// <summary>창고 보관 아이템 총 개수(종류 수가 아니라 수량 합).</summary>
    private static int CountWarehouseItems()
    {
        var wd = GameManager.Instance?.saveManager?.playerData?.warehouseData;
        if (wd == null) return 0;

        int total = 0;
        if (wd.mineralInventory?.slots != null)
            foreach (var s in wd.mineralInventory.slots) total += s.quantity;
        if (wd.itemInventory?.slots != null)
            foreach (var s in wd.itemInventory.slots) total += s.quantity;
        return total;
    }
```

`MineralInventoryData` / `ItemInventoryData`는 둘 다 `List<...SlotData> slots` 를 갖고,
슬롯의 수량 필드는 둘 다 `quantity` 다
([MineralInventoryData.cs:5-15](../../Scripts/UI/Inventory/MineralInventoryData.cs#L5-L15) ·
[ItemInventoryData.cs:5-15](../../Scripts/UI/Inventory/ItemInventoryData.cs#L5-L15)).
`SaveManager.playerData` 는 `public PlayerData playerData { get; private set; }` 이라 읽기만 가능하다 — 여기서는 읽기만 한다.

- [ ] **Step 2: `day_settled` payload 확충**

`SleepRoutine()`의 `Telemetry.Log(TelemetryEvents.DaySettled, ...)` 블록에서
마지막 줄 `.Add("max_depth", settlement != null ? settlement.MaxDepth : 0f));` 를 다음으로 바꾼다.

```csharp
            .Add("max_depth", settlement != null ? settlement.MaxDepth : 0f)
            .Add("mining_level", stat != null ? stat.MiningLevel : 0)
            .Add("unlocked_nodes", CountUnlockedNodes())
            .Add("warehouse_count", CountWarehouseItems())
            .Add("stock_value", portfolioValue)
            .Add("stock_cost", portfolioCost));
```

같은 메서드에서 `var settlement = SettlementManager.Instance;` **바로 뒤**에 추가한다.

```csharp
        // 주식 평가액·원가 — 주식이 아직 초기화되지 않았으면 0
        var sgm = StockGameManager.Instance;
        var portfolio = (sgm != null && sgm.IsInitialized) ? sgm.PortfolioManager : null;
        int portfolioValue = portfolio != null ? (int)portfolio.GetTotalValue() : 0;
        int portfolioCost  = portfolio != null ? (int)portfolio.GetTotalCost()  : 0;
```

`PortfolioManager`는 싱글톤이 아니라 **`StockGameManager.Instance.PortfolioManager` 프로퍼티**로 얻는 평범한 클래스다
([StockGameManager.cs:31](../../Scripts/Stock/Core/StockGameManager.cs#L31)).
`GetTotalValue()` / `GetTotalCost()` 는 `long`을 반환하므로 `int` 캐스팅이 필요하다
([PortfolioManager.cs:65](../../Scripts/Stock/Systems/PortfolioManager.cs#L65)).
`BedInteractable.cs` 상단에 `using Stock.Core;` 가 이미 있어 네임스페이스는 해결되어 있고,
같은 메서드가 바로 위에서 `StockGameManager.Instance.IsInitialized` 를 이미 쓰고 있다.

- [ ] **Step 3: 컴파일 확인**

Expected: 에러 없음.

- [ ] **Step 4: 수동 확인**

에디터에서 업그레이드를 하나 사고, 창고에 광물을 넣고, 침대에서 잔다.
JSONL의 `day_settled` 라인에서 `mining_level` / `unlocked_nodes` / `warehouse_count` /
`stock_value` / `stock_cost` 가 실제 게임 상태와 맞는지 확인한다.

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `telemetry: day_settled에 진행도·창고·주식 스냅샷 추가`

---

## Task 9: 파이썬 변환기 — `export_csv.py`

**Files:**
- Create: `Tools/telemetry/export_csv.py` (프로젝트 루트 기준, `Assets/`와 형제)
- Create: `Tools/telemetry/README.md`

**Interfaces:**
- Consumes: Task 2·7·8이 만든 JSONL 스키마
- Produces: `days.csv` / `dives.csv`

**동작:**
1. 입력 디렉토리의 모든 `*.jsonl`을 읽는다. 깨진 줄은 건너뛰고 개수만 stderr에 보고
2. `event`로 분류 — `day_settled` → `days.csv`, `dive_end` → `dives.csv`
3. 기본적으로 `is_fixture == true` 행은 제외. `--include-fixture`로 포함
4. 중첩 오브젝트를 컬럼으로 펼친다 — `region_seconds` → `sec_*`, `minerals` → `min_*`
5. `run_id`, `day` 순 정렬. 없는 필드는 빈칸

- [ ] **Step 1: 스크립트 작성**

`Tools/telemetry/export_csv.py` 를 새로 만든다.

```python
#!/usr/bin/env python3
"""텔레메트리 JSONL을 밸런스판 CSV(days.csv / dives.csv)로 굽는다.

설계: Assets/Docs/telemetry/balance-csv-design.md

사용법:
    python export_csv.py <telemetry_dir> -o <out_dir> [--include-fixture]

표준 라이브러리만 쓴다 — 아무 데서나 돌아야 한다.
"""

import argparse
import csv
import json
import sys
from pathlib import Path

# 공통 필드 — 모든 이벤트가 갖는다
COMMON = ["run_id", "is_fixture", "anon_id", "session_id", "seq",
          "build_type", "version", "playtime", "day", "region", "depth"]

# payload에서 끌어올릴 스칼라 필드 (표별)
DAY_FIELDS = ["ended_day", "gold_start", "gold_end", "mineral_sale", "stock", "coin",
              "shop_purchase", "upgrade", "other", "max_depth",
              "mining_level", "unlocked_nodes", "warehouse_count",
              "stock_value", "stock_cost"]

DIVE_FIELDS = ["result", "max_depth", "seconds", "mineral_kinds", "mineral_count",
               "stamina_left", "stamina_pct", "weight_ratio", "pixels_dug"]

# 중첩 오브젝트 → 컬럼 접두어
DIVE_NESTED = {"region_seconds": "sec_", "minerals": "min_"}


def read_events(directory):
    """디렉토리의 모든 *.jsonl을 읽어 이벤트 dict를 yield한다."""
    files = sorted(Path(directory).glob("*.jsonl"))
    if not files:
        print(f"경고: {directory} 에 *.jsonl 파일이 없다", file=sys.stderr)

    broken = 0
    for path in files:
        with path.open("r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    yield json.loads(line)
                except json.JSONDecodeError:
                    # 크래시로 잘린 마지막 줄이 흔하다 — 세고 넘어간다
                    broken += 1

    if broken:
        print(f"경고: 파싱 실패한 줄 {broken}개를 건너뛰었다", file=sys.stderr)


def flatten(event, scalar_fields, nested_map):
    """이벤트 하나를 평평한 dict로. 중첩 오브젝트는 접두어 붙여 펼친다."""
    row = {k: event.get(k, "") for k in COMMON}
    payload = event.get("payload") or {}

    for k in scalar_fields:
        row[k] = payload.get(k, "")

    for key, prefix in (nested_map or {}).items():
        obj = payload.get(key) or {}
        if isinstance(obj, dict):
            for sub, val in obj.items():
                row[prefix + sub] = val

    return row


def write_csv(path, rows, scalar_fields, nested_map):
    """행을 CSV로 쓴다. 컬럼 집합은 전체 행을 훑어 결정한다(2-pass)."""
    if not rows:
        print(f"건너뜀: {path.name} — 해당 이벤트가 없다", file=sys.stderr)
        return

    nested_cols = set()
    for row in rows:
        for k in row:
            if any(k.startswith(p) for p in (nested_map or {}).values()):
                nested_cols.add(k)

    header = COMMON + scalar_fields + sorted(nested_cols)

    rows.sort(key=lambda r: (str(r.get("run_id", "")), _as_int(r.get("day"))))

    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=header, extrasaction="ignore")
        writer.writeheader()
        for row in rows:
            writer.writerow({k: row.get(k, "") for k in header})

    print(f"{path} — {len(rows)}행, {len(header)}컬럼")


def _as_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def main():
    parser = argparse.ArgumentParser(description="텔레메트리 JSONL → 밸런스 CSV")
    parser.add_argument("telemetry_dir", help="JSONL이 있는 디렉토리")
    parser.add_argument("-o", "--out", default=".", help="CSV 출력 디렉토리 (기본: 현재 디렉토리)")
    parser.add_argument("--include-fixture", action="store_true",
                        help="QA 픽스처·디버그 조작 회차도 포함한다 (기본: 제외)")
    args = parser.parse_args()

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    days, dives, skipped = [], [], 0

    for event in read_events(args.telemetry_dir):
        if not args.include_fixture and event.get("is_fixture") is True:
            skipped += 1
            continue

        name = event.get("event")
        if name == "day_settled":
            days.append(flatten(event, DAY_FIELDS, None))
        elif name == "dive_end":
            dives.append(flatten(event, DIVE_FIELDS, DIVE_NESTED))

    if skipped:
        print(f"제외: 픽스처 회차 이벤트 {skipped}건 (--include-fixture로 포함 가능)", file=sys.stderr)

    write_csv(out_dir / "days.csv", days, DAY_FIELDS, None)
    write_csv(out_dir / "dives.csv", dives, DIVE_FIELDS, DIVE_NESTED)


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: 샘플 JSONL로 검증**

`Tools/telemetry/` 에서 임시 파일을 만들어 돌린다.

```bash
mkdir -p /tmp/tsample
cat > /tmp/tsample/sample.jsonl <<'EOF'
{"anon_id":"a1","session_id":"s1","run_id":"r1","is_fixture":false,"seq":0,"event":"day_settled","build_type":"ea","version":"1.0","playtime":600,"day":1,"region":"Dirt","depth":0,"payload":{"ended_day":1,"gold_start":100,"gold_end":450,"mineral_sale":400,"stock":0,"coin":-50,"shop_purchase":0,"upgrade":0,"other":0,"max_depth":120.5,"mining_level":0,"unlocked_nodes":2,"warehouse_count":14,"stock_value":0,"stock_cost":0}}
{"anon_id":"a1","session_id":"s1","run_id":"r1","is_fixture":false,"seq":1,"event":"dive_end","build_type":"ea","version":"1.0","playtime":580,"day":1,"region":"Stone","depth":120.5,"payload":{"result":"return","max_depth":120.5,"seconds":420,"mineral_kinds":2,"mineral_count":15,"stamina_left":12.5,"stamina_pct":0.12,"weight_ratio":0.98,"pixels_dug":48210,"region_seconds":{"Dirt":180,"Stone":240},"minerals":{"Iron":12,"Gold":3}}}
{"anon_id":"a1","session_id":"s2","run_id":"r2","is_fixture":true,"seq":0,"event":"day_settled","build_type":"ea","version":"1.0","playtime":10,"day":9,"region":"Dirt","depth":0,"payload":{"ended_day":9,"gold_end":999999}}
{"broken json
EOF
python Tools/telemetry/export_csv.py /tmp/tsample -o /tmp/tsample/out
```

Expected (stderr/stdout):
- `경고: 파싱 실패한 줄 1개를 건너뛰었다`
- `제외: 픽스처 회차 이벤트 1건`
- `days.csv — 1행` / `dives.csv — 1행`

`/tmp/tsample/out/dives.csv` 를 열어 확인:
- `sec_Dirt` = 180, `sec_Stone` = 240
- `min_Iron` = 12, `min_Gold` = 3
- `run_id` = r1

`--include-fixture` 를 붙여 다시 돌리면 `days.csv — 2행` 이 되어야 한다.

- [ ] **Step 3: README 작성**

`Tools/telemetry/README.md` 를 새로 만든다.

```markdown
# 밸런스판 CSV 변환기

텔레메트리 JSONL을 `days.csv` / `dives.csv` 로 굽는다.
설계: `Assets/Docs/telemetry/balance-csv-design.md`

## 사용법

```
python export_csv.py <telemetry_dir> -o <out_dir> [--include-fixture]
```

`<telemetry_dir>` 는 게임의 `persistentDataPath/telemetry/` 다.
Unity 에디터에서 `Tools/Telemetry` 메뉴로 폴더를 열 수 있다.

Windows 기본 경로:
`%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\telemetry`

## 출력

| 파일 | 한 줄 |
|---|---|
| `days.csv` | 한 회차의 N일차 정산 1건 (`day_settled`) |
| `dives.csv` | 잠수 1회 (`dive_end`) |

두 표는 `run_id` + `day` 로 조인된다.
`dives.csv` 를 `run_id, day` 로 group-by 하면 하루 요약이 나온다.

## 픽스처 제외

QA 픽스처(`fx load`)나 디버그 명령(`gold`/`day`/`stamina`)이 손댄 회차는
`is_fixture=true` 로 표시되어 **기본적으로 제외**된다.
포함하려면 `--include-fixture`.

## 파생 컬럼

시간당 광물·긴급탈출률·파산 임계값 같은 파생값은 CSV에 넣지 않는다.
정의가 계속 바뀌므로 엑셀/노트북에서 계산한다.

예) 긴급탈출률 = `dives.csv` 에서 `result == "escape"` 비율을 `day` 로 group-by
```

- [ ] **Step 4: 체크포인트**

UVCS 체크인: `tools: 텔레메트리 JSONL → 밸런스 CSV 변환기 추가`

---

## Task 10: 전체 통합 확인 + 문서 갱신

**Files:**
- Modify: `Assets/Docs/telemetry/design.md` (§3 스키마 표에 신규 필드 반영)
- Modify: `CLAUDE.md` (핵심 파일 표에 한 줄)

- [ ] **Step 1: 엔드투엔드 플레이 확인**

에디터에서 **뉴게임**으로 시작해 다음을 한 번에 통과한다.

1. 지하로 내려가 층을 두 개 이상 지나며 지형을 판다
2. 광물을 몇 개 캔다
3. 지상으로 귀환한다 (`dive_end` 발생)
4. 상점에서 광물을 팔고 업그레이드를 하나 산다
5. 침대에서 잔다 (`day_settled` 발생)
6. `Tools/Telemetry` 메뉴로 로그 폴더를 연다

- [ ] **Step 2: CSV로 구워 확인**

```
python Tools/telemetry/export_csv.py "<persistentDataPath>/telemetry" -o ./out
```

`out/days.csv` 와 `out/dives.csv` 를 열어 확인:
- 두 파일 모두 `run_id` 컬럼이 **비어 있지 않고 서로 같다**
- `is_fixture` 가 `False`
- `dives.csv` 에 `sec_*` 컬럼이 2개 이상, `pixels_dug` 가 0보다 큼
- `days.csv` 의 `gold_end` 가 실제 보유 골드와 일치

- [ ] **Step 3: `design.md` 스키마 표 갱신**

`Assets/Docs/telemetry/design.md` §3.0 공통 페이로드 표에 두 행을 추가한다.

```markdown
| `run_id` | 회차(플레이스루) 식별자. 뉴게임마다 발급되어 세이브에 영속 |
| `is_fixture` | QA 픽스처·디버그 명령이 손댄 회차인지 (분석 시 제외) |
```

§3.1 `dive_end` 행의 고유 필드를 다음으로 바꾼다.

```markdown
| `dive_end` | 결과(귀환/긴급탈출), 최대 깊이, 소요 시간, 획득 광물(구성), 스태미나 잔량·비율, 무게 비율, 파기 픽셀, 층별 체류 시간 | 귀환 처리부 |
```

§3.3 `day_settled` 행의 고유 필드를 다음으로 바꾼다.

```markdown
| `day_settled` **(스냅샷)** | 시작/종료 골드, 카테고리별 증감, 창고 재고, 채광 레벨, 해금 노드 수, 주식 평가액·원가, 당일 최대 깊이 | `DayEarningsLedger` 소비 시점 |
```

§5 Supabase 스키마의 `create table events` 에 두 컬럼을 추가한다.

```sql
  run_id      text,
  is_fixture  boolean     default false,
```

- [ ] **Step 4: `CLAUDE.md` 핵심 파일 표에 한 줄 추가**

`Assets/Scripts/Utils/DebugConsole/` 행 **바로 뒤**에 추가한다.

```markdown
| `Assets/Scripts/_Core/Telemetry/` | 플레이 텔레메트리(로컬 JSONL). `Telemetry.Log(name, payload)` 한 줄로 기록하고 버퍼·파일은 뒤에서 처리. 공통 필드에 `run_id`(회차)·`is_fixture`(디버그 조작 여부)가 실린다 — 밸런스 CSV가 이 둘로 회차를 가르고 조작된 판을 걸러낸다. 밸런스판은 `Tools/telemetry/export_csv.py`로 `days.csv`/`dives.csv`를 굽는다. 설계: `Assets/Docs/telemetry/design.md` + `balance-csv-design.md` |
```

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `docs: 밸런스 CSV 반영 (design.md 스키마 · CLAUDE.md)`

---

## 실행 순서 메모

- Task 1·2는 서로 독립이다. 3은 2에 의존한다.
- Task 5·6은 서로 독립이고 1~4와도 독립이다.
- Task 7은 1·5·6에, Task 8은 독립, Task 9는 2·7·8의 스키마에 의존한다.
- **Task 9의 파이썬 스크립트는 Task 7·8이 끝나기 전에 써도 된다** — 없는 필드는 빈칸으로 나온다.
  다만 Step 2의 샘플 검증은 통과해야 한다.
