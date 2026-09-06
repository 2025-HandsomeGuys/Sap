# 텔레메트리 Phase 1 (로컬 JSONL) 구현 계획

> **에이전트 작업자용:** 이 계획은 `superpowers:subagent-driven-development` 또는
> `superpowers:executing-plans` 로 태스크 단위 실행을 전제로 작성되었다.
> 각 단계는 체크박스(`- [ ]`)로 추적한다.

**목표:** 플레이어 행동 이벤트를 로컬 JSONL 파일로 수집하는 텔레메트리 시스템을 구축한다. 서버 전송은 범위 외.

**아키텍처:** `Telemetry.Log(name, payload)` 정적 진입점 하나로 게임플레이 코드가 호출하고,
공통 필드 주입(`TelemetrySession`) → 메모리 버퍼(`TelemetryBuffer`) → JSONL 파일(`TelemetryFileSink`) 순으로 흐른다.
`TelemetryRunner`(MonoBehaviour)가 주기 플러시·heartbeat·종료 훅을 담당한다.
순수 C# 3개(Session/Buffer/FileSink)는 Unity 의존이 없어 EditMode 테스트가 가능하다.

**기술 스택:** Unity 6000.3.2f1, C#, NUnit(EditMode), `System.IO`, `PlayerPrefs`

**설계 문서:** `Assets/Docs/telemetry/design.md`

---

## Global Constraints

- **버전 관리는 UVCS**다. `git` 명령을 쓰지 않는다. 각 태스크 끝의 "체크포인트"는 **사람이 직접 UVCS에 체크인**한다.
- **Unity Test Runner 실행은 사람이 직접 한다.** 구현자는 테스트를 작성만 하고, 실행 결과를 기다리지 않고 다음 태스크로 진행한다.
- 신규 파일 첫 줄에 `// @tags: ...` 주석을 넣는다 (프로젝트 파일 검색 규약).
- 코드 주석·로그는 한국어, 식별자는 영어.
- 네임스페이스를 쓰지 않는다 — `_Core/Managers` 하위 기존 코드(`DayEarningsLedger`, `TileDataManager`)는 전역 네임스페이스다. 동일하게 맞춘다.
- 이벤트 이름은 **snake_case 문자열 상수**로 `TelemetryEvents` 정적 클래스에 모은다. 리터럴을 호출부에 흩뿌리지 않는다.
- JSON 키는 `design.md` §5의 Supabase 컬럼명과 1:1로 맞춘다: `anon_id`, `session_id`, `seq`, `event`, `build_type`, `version`, `playtime`, `day`, `region`, `depth`, `payload`.
- 텔레메트리 코드는 **절대 예외를 밖으로 던지지 않는다.** 파일 I/O 실패는 삼켜서 로그만 남긴다. 텔레메트리 때문에 게임이 죽으면 안 된다.
- 파일 총 용량 상한: **50MB**.
- 플러시 주기: **60초**.

---

## 파일 구조

**신규 — 런타임 (`Assets/Scripts/_Core/Telemetry/`)**

| 파일 | 책임 |
|---|---|
| `TelemetryEvents.cs` | 이벤트 이름 상수 모음 |
| `TelemetryPayload.cs` | 이벤트별 필드를 JSON 조각으로 누적하는 빌더 |
| `TelemetrySession.cs` | anonId·sessionId·seq·공통 컨텍스트(day/region/depth/playtime) 소유 |
| `TelemetryBuffer.cs` | 완성된 JSON 라인을 메모리에 모으고 플러시 시점 판단 |
| `TelemetryFileSink.cs` | 세션별 JSONL 파일 append, 용량 상한 정리 |
| `Telemetry.cs` | 정적 진입점. 활성화 게이트 + 위 조각들 배선 |
| `TelemetryRunner.cs` | MonoBehaviour. 60초 플러시, heartbeat, 종료 훅, 예외 캡처 |

**신규 — 에디터 (`Assets/Scripts/Editor/`)**

| 파일 | 책임 |
|---|---|
| `TelemetryEditorMenu.cs` | 로그 폴더 열기 / 로그 전체 삭제 메뉴 |

**신규 — 테스트 (`Assets/Tests/EditMode/`)**

`TelemetryPayloadTests.cs` · `TelemetrySessionTests.cs` · `TelemetryBufferTests.cs` · `TelemetryFileSinkTests.cs`

**수정 — 계측 삽입 지점**

| 파일 | 삽입 내용 |
|---|---|
| `_Core/Managers/SettlementManager.cs` | depth 컨텍스트 갱신, `dive_start` / `dive_end` / `depth_milestone` / `region_first_enter` |
| `UI/Player/Stats/PlayerStat.cs` | `OnStaminaDepleted` 구독 지점 마련(이벤트는 이미 존재) |
| `UI/Player/EncumbranceController.cs` | `encumbered_enter` |
| `_Core/Managers/EmergencyEscapeReport.cs` | `emergency_escape` |
| `Gameplay/Environment/BedInteractable.cs` | `day_settled` 스냅샷 |
| `UI/Shop/ShopManager.cs` | `shop_transaction` |
| `UI/Upgrade/UpgradeManager.cs` | `upgrade_purchased` / `upgrade_blocked` |
| `Coin/Core/CoinGameManager.cs` | `coin_bet` |
| `Systems/Guide/GuideManager.cs` | `guide_shown` |

---

## Task 1: TelemetryPayload — JSON 조각 빌더

**Files:**
- Create: `Assets/Scripts/_Core/Telemetry/TelemetryPayload.cs`
- Test: `Assets/Tests/EditMode/TelemetryPayloadTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `TelemetryPayload.New()` → `TelemetryPayload`
  - `Add(string key, string value)` / `Add(string key, int value)` / `Add(string key, float value)` / `Add(string key, bool value)` → `TelemetryPayload` (체이닝)
  - `ToJsonObject()` → `string` — 항상 `{...}` 형태. 비었으면 `{}`
  - `static string EscapeJson(string raw)` → `string` — 따옴표 없이 이스케이프된 본문만

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/TelemetryPayloadTests.cs`:

```csharp
using NUnit.Framework;

/// <summary>TelemetryPayload — JSON 조각 생성·이스케이프·타입별 포맷 검증.</summary>
public class TelemetryPayloadTests
{
    [Test]
    public void Empty_ReturnsEmptyObject()
    {
        Assert.AreEqual("{}", TelemetryPayload.New().ToJsonObject());
    }

    [Test]
    public void Add_ProducesKeyValuePairs()
    {
        string json = TelemetryPayload.New()
            .Add("depth", 120)
            .Add("region", "Stone")
            .ToJsonObject();

        Assert.AreEqual("{\"depth\":120,\"region\":\"Stone\"}", json);
    }

    [Test]
    public void Add_BoolIsLowercase()
    {
        Assert.AreEqual("{\"ok\":true,\"bad\":false}",
            TelemetryPayload.New().Add("ok", true).Add("bad", false).ToJsonObject());
    }

    [Test]
    public void Add_FloatUsesInvariantCultureAndTwoDecimals()
    {
        // 한국어 로케일에서도 소수점이 쉼표가 되면 JSON이 깨진다
        Assert.AreEqual("{\"d\":12.34}",
            TelemetryPayload.New().Add("d", 12.339f).ToJsonObject());
    }

    [Test]
    public void Add_NullStringBecomesEmptyString()
    {
        Assert.AreEqual("{\"s\":\"\"}", TelemetryPayload.New().Add("s", (string)null).ToJsonObject());
    }

    [Test]
    public void EscapeJson_HandlesQuotesBackslashesAndControlChars()
    {
        Assert.AreEqual("a\\\"b", TelemetryPayload.EscapeJson("a\"b"));
        Assert.AreEqual("a\\\\b", TelemetryPayload.EscapeJson("a\\b"));
        Assert.AreEqual("a\\nb", TelemetryPayload.EscapeJson("a\nb"));
        Assert.AreEqual("a\\tb", TelemetryPayload.EscapeJson("a\tb"));
    }

    [Test]
    public void EscapeJson_StripsNewlinesFromStackTrace()
    {
        // error 이벤트의 스택트레이스가 JSONL 한 줄을 깨뜨리면 안 된다
        string json = TelemetryPayload.New().Add("trace", "line1\nline2").ToJsonObject();
        Assert.IsFalse(json.Contains("\n"), "직렬화 결과에 실제 개행이 남으면 JSONL이 깨진다");
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Unity Test Runner(EditMode)에서 `TelemetryPayloadTests` 실행 — 컴파일 에러(`TelemetryPayload` 없음)로 실패해야 한다.
**실행은 사람이 수행한다.** 구현자는 다음 단계로 진행한다.

- [ ] **Step 3: 구현**

`Assets/Scripts/_Core/Telemetry/TelemetryPayload.cs`:

```csharp
// @tags: telemetry, payload, json, builder, serialization

using System.Globalization;
using System.Text;

/// <summary>
/// 이벤트별 필드를 JSON 오브젝트 문자열로 누적하는 빌더.
/// 호출 시점에 바로 문자열로 굳혀 Dictionary 박싱·할당을 피한다(설계 §4 성능).
/// 개행은 전부 이스케이프되므로 JSONL 한 줄이 깨지지 않는다.
/// </summary>
public sealed class TelemetryPayload
{
    private readonly StringBuilder _sb = new StringBuilder(128);

    public static TelemetryPayload New() => new TelemetryPayload();

    public TelemetryPayload Add(string key, string value)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":\"")
           .Append(EscapeJson(value ?? string.Empty)).Append('"');
        return this;
    }

    public TelemetryPayload Add(string key, int value)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":")
           .Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public TelemetryPayload Add(string key, float value)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":")
           .Append(value.ToString("0.##", CultureInfo.InvariantCulture));
        return this;
    }

    public TelemetryPayload Add(string key, bool value)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":")
           .Append(value ? "true" : "false");
        return this;
    }

    /// <summary>항상 중괄호로 감싼 JSON 오브젝트를 돌려준다. 비어 있으면 "{}".</summary>
    public string ToJsonObject() => "{" + _sb + "}";

    /// <summary>따옴표를 제외한 JSON 문자열 본문 이스케이프.</summary>
    public static string EscapeJson(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length + 8);
        foreach (char c in raw)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    // 그 외 제어문자는 버린다(스택트레이스에 섞여 들어올 수 있다)
                    if (c >= ' ') sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private void Separate()
    {
        if (_sb.Length > 0) _sb.Append(',');
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Unity Test Runner(EditMode)에서 `TelemetryPayloadTests` 실행 — 7개 모두 통과해야 한다. **사람이 실행한다.**

- [ ] **Step 5: 체크포인트**

UVCS에 체크인. 메시지: `feat: 텔레메트리 JSON 페이로드 빌더 추가`

---

## Task 2: TelemetrySession — 공통 필드 소유

**Files:**
- Create: `Assets/Scripts/_Core/Telemetry/TelemetrySession.cs`
- Test: `Assets/Tests/EditMode/TelemetrySessionTests.cs`

**Interfaces:**
- Consumes: `TelemetryPayload.EscapeJson(string)` (Task 1)
- Produces:
  - `new TelemetrySession(string anonId, string sessionId, string buildType, string version)`
  - 컨텍스트 프로퍼티(get/set): `int Day` · `string Region` · `float Depth` · `float PlaytimeTotal`
  - `string BuildLine(string eventName, string payloadJsonObject)` → JSONL 한 줄. 호출마다 `seq` 1씩 증가
  - `int Seq { get; }` — 현재까지 발행한 이벤트 수
  - `static string LoadOrCreateAnonId()` — `PlayerPrefs`에 `telemetry.anonId` 키로 GUID 영속화

`Region` 기본값은 `""`, `Day` 기본값은 `0`이다. 게임이 아직 설정하지 않은 상태를 그대로 기록한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/TelemetrySessionTests.cs`:

```csharp
using NUnit.Framework;

/// <summary>TelemetrySession — 공통 필드 주입·seq 증가·JSON 라인 형식 검증.</summary>
public class TelemetrySessionTests
{
    private TelemetrySession NewSession() =>
        new TelemetrySession("anon-1", "sess-1", "ea", "0.1.0");

    [Test]
    public void BuildLine_ContainsAllCommonFields()
    {
        var s = NewSession();
        s.Day = 3;
        s.Region = "Stone";
        s.Depth = 120.5f;
        s.PlaytimeTotal = 3600f;

        string line = s.BuildLine("dive_end", "{\"result\":\"return\"}");

        StringAssert.Contains("\"anon_id\":\"anon-1\"", line);
        StringAssert.Contains("\"session_id\":\"sess-1\"", line);
        StringAssert.Contains("\"event\":\"dive_end\"", line);
        StringAssert.Contains("\"build_type\":\"ea\"", line);
        StringAssert.Contains("\"version\":\"0.1.0\"", line);
        StringAssert.Contains("\"day\":3", line);
        StringAssert.Contains("\"region\":\"Stone\"", line);
        StringAssert.Contains("\"depth\":120.5", line);
        StringAssert.Contains("\"playtime\":3600", line);
        StringAssert.Contains("\"payload\":{\"result\":\"return\"}", line);
    }

    [Test]
    public void BuildLine_SeqIncrementsFromZero()
    {
        var s = NewSession();
        StringAssert.Contains("\"seq\":0", s.BuildLine("a", "{}"));
        StringAssert.Contains("\"seq\":1", s.BuildLine("b", "{}"));
        StringAssert.Contains("\"seq\":2", s.BuildLine("c", "{}"));
        Assert.AreEqual(3, s.Seq);
    }

    [Test]
    public void BuildLine_HasNoNewline()
    {
        // JSONL은 한 이벤트가 정확히 한 줄이어야 한다
        Assert.IsFalse(NewSession().BuildLine("a", "{}").Contains("\n"));
    }

    [Test]
    public void BuildLine_EmptyPayloadIsValidJson()
    {
        StringAssert.Contains("\"payload\":{}", NewSession().BuildLine("heartbeat", "{}"));
    }

    [Test]
    public void BuildLine_EscapesRegionName()
    {
        var s = NewSession();
        s.Region = "a\"b";
        StringAssert.Contains("\"region\":\"a\\\"b\"", s.BuildLine("a", "{}"));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

EditMode에서 `TelemetrySessionTests` 실행 — 컴파일 에러로 실패. **사람이 실행한다.**

- [ ] **Step 3: 구현**

`Assets/Scripts/_Core/Telemetry/TelemetrySession.cs`:

```csharp
// @tags: telemetry, session, context, anon-id, common-fields

using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// 한 세션의 공통 필드를 소유하고, 이벤트 하나를 JSONL 한 줄로 조립한다.
/// 키 이름은 Phase 2 Supabase 테이블 컬럼과 1:1 대응한다(설계 §5).
/// Unity 의존은 PlayerPrefs 하나뿐이라 EditMode 테스트가 가능하다.
/// </summary>
public sealed class TelemetrySession
{
    private const string AnonIdKey = "telemetry.anonId";

    private readonly string _anonId;
    private readonly string _sessionId;
    private readonly string _buildType;
    private readonly string _version;

    private int _seq;

    /// <summary>현재 일차. 게임이 아직 설정하지 않았으면 0.</summary>
    public int Day { get; set; }
    /// <summary>현재 지역(TileType 이름). 미설정이면 빈 문자열.</summary>
    public string Region { get; set; } = string.Empty;
    /// <summary>현재 깊이(m).</summary>
    public float Depth { get; set; }
    /// <summary>누적 플레이 시간(초).</summary>
    public float PlaytimeTotal { get; set; }

    public int Seq => _seq;

    public TelemetrySession(string anonId, string sessionId, string buildType, string version)
    {
        _anonId = anonId ?? string.Empty;
        _sessionId = sessionId ?? string.Empty;
        _buildType = buildType ?? string.Empty;
        _version = version ?? string.Empty;
    }

    /// <summary>이벤트 하나를 JSONL 한 줄로 조립한다. 호출마다 seq가 1 증가한다.</summary>
    public string BuildLine(string eventName, string payloadJsonObject)
    {
        var sb = new StringBuilder(256);
        sb.Append('{');
        Str(sb, "anon_id", _anonId);        sb.Append(',');
        Str(sb, "session_id", _sessionId);  sb.Append(',');
        Num(sb, "seq", _seq);               sb.Append(',');
        Str(sb, "event", eventName);        sb.Append(',');
        Str(sb, "build_type", _buildType);  sb.Append(',');
        Str(sb, "version", _version);       sb.Append(',');
        Num(sb, "playtime", Mathf.RoundToInt(PlaytimeTotal)); sb.Append(',');
        Num(sb, "day", Day);                sb.Append(',');
        Str(sb, "region", Region);          sb.Append(',');

        sb.Append("\"depth\":").Append(Depth.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"payload\":").Append(string.IsNullOrEmpty(payloadJsonObject) ? "{}" : payloadJsonObject);
        sb.Append('}');

        _seq++;
        return sb.ToString();
    }

    /// <summary>익명 식별자를 PlayerPrefs에서 읽고, 없으면 새로 만들어 저장한다.</summary>
    public static string LoadOrCreateAnonId()
    {
        string id = PlayerPrefs.GetString(AnonIdKey, string.Empty);
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(AnonIdKey, id);
            PlayerPrefs.Save();
        }
        return id;
    }

    private static void Str(StringBuilder sb, string key, string value)
    {
        sb.Append('"').Append(key).Append("\":\"")
          .Append(TelemetryPayload.EscapeJson(value ?? string.Empty)).Append('"');
    }

    private static void Num(StringBuilder sb, string key, int value)
    {
        sb.Append('"').Append(key).Append("\":")
          .Append(value.ToString(CultureInfo.InvariantCulture));
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

EditMode에서 `TelemetrySessionTests` 5개 통과 확인. **사람이 실행한다.**

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `feat: 텔레메트리 세션 공통 필드 조립 추가`

---

## Task 3: TelemetryBuffer — 메모리 버퍼

**Files:**
- Create: `Assets/Scripts/_Core/Telemetry/TelemetryBuffer.cs`
- Test: `Assets/Tests/EditMode/TelemetryBufferTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `new TelemetryBuffer(int maxLines = 512)`
  - `void Add(string line)`
  - `int Count { get; }`
  - `bool IsFull { get; }` — `Count >= maxLines`
  - `string[] Drain()` — 현재 내용을 반환하고 버퍼를 비운다. 비어 있으면 길이 0 배열
  - `int DroppedCount { get; }` — 상한 초과로 버린 줄 수

버퍼 상한이 필요한 이유: 플러시가 어떤 이유로 멈춰도 메모리가 무한히 늘지 않게 하기 위함이다.
상한 도달 시 **오래된 것이 아니라 새 것을 버린다** — 세션 초반 이벤트(`session_start` 등)가 분석에 더 중요하기 때문이다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/TelemetryBufferTests.cs`:

```csharp
using NUnit.Framework;

/// <summary>TelemetryBuffer — 누적·드레인·상한 초과 처리 검증.</summary>
public class TelemetryBufferTests
{
    [Test]
    public void Drain_ReturnsLinesInOrderAndClears()
    {
        var buf = new TelemetryBuffer();
        buf.Add("a");
        buf.Add("b");

        var drained = buf.Drain();

        Assert.AreEqual(new[] { "a", "b" }, drained);
        Assert.AreEqual(0, buf.Count);
    }

    [Test]
    public void Drain_OnEmptyReturnsEmptyArray()
    {
        var buf = new TelemetryBuffer();
        Assert.AreEqual(0, buf.Drain().Length);
    }

    [Test]
    public void Add_IgnoresNullAndEmpty()
    {
        var buf = new TelemetryBuffer();
        buf.Add(null);
        buf.Add("");
        Assert.AreEqual(0, buf.Count);
    }

    [Test]
    public void Add_BeyondCapacity_DropsNewestAndCounts()
    {
        var buf = new TelemetryBuffer(maxLines: 2);
        buf.Add("a");
        buf.Add("b");
        buf.Add("c");   // 버려짐

        Assert.IsTrue(buf.IsFull);
        Assert.AreEqual(1, buf.DroppedCount);
        // 초반 이벤트가 살아남아야 한다
        Assert.AreEqual(new[] { "a", "b" }, buf.Drain());
    }

    [Test]
    public void Drain_ResetsFullState()
    {
        var buf = new TelemetryBuffer(maxLines: 1);
        buf.Add("a");
        buf.Drain();

        Assert.IsFalse(buf.IsFull);
        buf.Add("b");
        Assert.AreEqual(1, buf.Count);
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

EditMode에서 `TelemetryBufferTests` 실행 — 컴파일 에러로 실패. **사람이 실행한다.**

- [ ] **Step 3: 구현**

`Assets/Scripts/_Core/Telemetry/TelemetryBuffer.cs`:

```csharp
// @tags: telemetry, buffer, queue, flush

using System.Collections.Generic;

/// <summary>
/// 완성된 JSONL 라인을 플러시 전까지 모아 두는 메모리 버퍼.
/// 상한을 두어 플러시가 멈춰도 메모리가 무한정 늘지 않게 한다.
/// 상한 도달 시 새 줄을 버린다 — 세션 초반 이벤트가 분석에 더 중요하기 때문.
/// </summary>
public sealed class TelemetryBuffer
{
    private readonly List<string> _lines;
    private readonly int _maxLines;

    public int Count => _lines.Count;
    public bool IsFull => _lines.Count >= _maxLines;
    /// <summary>상한 초과로 버려진 줄 수(누적).</summary>
    public int DroppedCount { get; private set; }

    public TelemetryBuffer(int maxLines = 512)
    {
        _maxLines = maxLines < 1 ? 1 : maxLines;
        _lines = new List<string>(_maxLines);
    }

    public void Add(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        if (IsFull)
        {
            DroppedCount++;
            return;
        }
        _lines.Add(line);
    }

    /// <summary>현재 내용을 반환하고 버퍼를 비운다.</summary>
    public string[] Drain()
    {
        if (_lines.Count == 0) return System.Array.Empty<string>();

        var result = _lines.ToArray();
        _lines.Clear();
        return result;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

EditMode에서 `TelemetryBufferTests` 5개 통과 확인. **사람이 실행한다.**

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `feat: 텔레메트리 메모리 버퍼 추가`

---

## Task 4: TelemetryFileSink — JSONL 파일 기록

**Files:**
- Create: `Assets/Scripts/_Core/Telemetry/TelemetryFileSink.cs`
- Test: `Assets/Tests/EditMode/TelemetryFileSinkTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `new TelemetryFileSink(string directory, string fileName, long maxTotalBytes = 50L * 1024 * 1024)`
  - `void Append(string[] lines)` — 디렉토리가 없으면 만들고 파일에 append. 실패해도 예외를 던지지 않음
  - `string FilePath { get; }`
  - `void EnforceQuota()` — 디렉토리 총합이 상한을 넘으면 **오래된 파일부터**(수정 시각 기준) 삭제. 현재 세션 파일은 삭제하지 않음
  - `static string BuildFileName(System.DateTime startedAt, string sessionId)` → `yyyyMMdd-HHmmss_<sessionId 앞 8자>.jsonl`

테스트는 `Path.GetTempPath()` 아래 임시 디렉토리를 쓰고 `[TearDown]`에서 지운다.
`Application.persistentDataPath`는 `Telemetry`(Task 5)가 주입한다 — 그래야 이 클래스가 순수 C#으로 남는다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/TelemetryFileSinkTests.cs`:

```csharp
using System;
using System.IO;
using NUnit.Framework;

/// <summary>TelemetryFileSink — 파일 append·파일명 규칙·용량 상한 정리 검증.</summary>
public class TelemetryFileSinkTests
{
    private string _dir;

    [SetUp]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "telemetry_test_" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public void Append_CreatesDirectoryAndWritesOneLinePerEvent()
    {
        var sink = new TelemetryFileSink(_dir, "s.jsonl");
        sink.Append(new[] { "{\"a\":1}", "{\"b\":2}" });

        string[] lines = File.ReadAllLines(sink.FilePath);
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual("{\"a\":1}", lines[0]);
        Assert.AreEqual("{\"b\":2}", lines[1]);
    }

    [Test]
    public void Append_AppendsRatherThanOverwrites()
    {
        var sink = new TelemetryFileSink(_dir, "s.jsonl");
        sink.Append(new[] { "1" });
        sink.Append(new[] { "2" });

        Assert.AreEqual(2, File.ReadAllLines(sink.FilePath).Length);
    }

    [Test]
    public void Append_EmptyOrNull_DoesNotCreateFile()
    {
        var sink = new TelemetryFileSink(_dir, "s.jsonl");
        sink.Append(null);
        sink.Append(Array.Empty<string>());

        Assert.IsFalse(File.Exists(sink.FilePath));
    }

    [Test]
    public void BuildFileName_UsesTimestampAndShortSessionId()
    {
        string name = TelemetryFileSink.BuildFileName(
            new DateTime(2026, 7, 25, 14, 30, 5), "abcdef0123456789");

        Assert.AreEqual("20260725-143005_abcdef01.jsonl", name);
    }

    [Test]
    public void EnforceQuota_DeletesOldestFilesButKeepsCurrentSession()
    {
        Directory.CreateDirectory(_dir);
        // 각 1KB짜리 오래된 파일 3개
        for (int i = 0; i < 3; i++)
        {
            string p = Path.Combine(_dir, $"old{i}.jsonl");
            File.WriteAllText(p, new string('x', 1024));
            File.SetLastWriteTimeUtc(p, new DateTime(2020, 1, 1).AddDays(i));
        }

        var sink = new TelemetryFileSink(_dir, "current.jsonl", maxTotalBytes: 2048);
        sink.Append(new[] { new string('y', 500) });

        sink.EnforceQuota();

        Assert.IsTrue(File.Exists(sink.FilePath), "현재 세션 파일은 지우면 안 된다");
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "old0.jsonl")), "가장 오래된 파일부터 지워야 한다");

        long total = 0;
        foreach (var f in Directory.GetFiles(_dir)) total += new FileInfo(f).Length;
        Assert.LessOrEqual(total, 2048);
    }

    [Test]
    public void Append_ToUnwritablePath_DoesNotThrow()
    {
        // 텔레메트리 실패가 게임을 죽이면 안 된다
        var sink = new TelemetryFileSink("\0invalid", "s.jsonl");
        Assert.DoesNotThrow(() => sink.Append(new[] { "{}" }));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

EditMode에서 `TelemetryFileSinkTests` 실행 — 컴파일 에러로 실패. **사람이 실행한다.**

- [ ] **Step 3: 구현**

`Assets/Scripts/_Core/Telemetry/TelemetryFileSink.cs`:

```csharp
// @tags: telemetry, file, jsonl, sink, quota, persistence

using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// 텔레메트리 라인을 세션별 JSONL 파일에 append한다.
/// Phase 2에서 업로더가 이 디렉토리를 스캔해 '아직 안 보낸 파일'을 그대로 큐로 쓴다(설계 §5).
///
/// 모든 I/O 실패는 삼키고 로그만 남긴다 — 텔레메트리 때문에 게임이 죽으면 안 된다.
/// </summary>
public sealed class TelemetryFileSink
{
    private readonly string _directory;
    private readonly long _maxTotalBytes;

    public string FilePath { get; }

    public TelemetryFileSink(string directory, string fileName, long maxTotalBytes = 50L * 1024 * 1024)
    {
        _directory = directory;
        _maxTotalBytes = maxTotalBytes;
        FilePath = Path.Combine(directory, fileName);
    }

    /// <summary>파일명 규칙: yyyyMMdd-HHmmss_&lt;sessionId 앞 8자&gt;.jsonl</summary>
    public static string BuildFileName(DateTime startedAt, string sessionId)
    {
        string shortId = string.IsNullOrEmpty(sessionId)
            ? "unknown"
            : sessionId.Substring(0, Math.Min(8, sessionId.Length));

        return startedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_" + shortId + ".jsonl";
    }

    public void Append(string[] lines)
    {
        if (lines == null || lines.Length == 0) return;

        try
        {
            Directory.CreateDirectory(_directory);
            File.AppendAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Telemetry] 파일 기록 실패: {e.Message}");
        }
    }

    /// <summary>디렉토리 총합이 상한을 넘으면 오래된 파일부터 지운다. 현재 세션 파일은 남긴다.</summary>
    public void EnforceQuota()
    {
        try
        {
            if (!Directory.Exists(_directory)) return;

            var files = new DirectoryInfo(_directory).GetFiles("*.jsonl");
            long total = 0;
            foreach (var f in files) total += f.Length;
            if (total <= _maxTotalBytes) return;

            Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));

            foreach (var f in files)
            {
                if (total <= _maxTotalBytes) break;
                if (string.Equals(f.FullName, Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase)) continue;

                long size = f.Length;
                f.Delete();
                total -= size;
                Debug.Log($"[Telemetry] 용량 상한 초과 — 오래된 로그 삭제: {f.Name}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Telemetry] 용량 정리 실패: {e.Message}");
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

EditMode에서 `TelemetryFileSinkTests` 6개 통과 확인. **사람이 실행한다.**

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `feat: 텔레메트리 JSONL 파일 싱크 추가`

---

## Task 5: Telemetry 진입점 + TelemetryRunner + 세션 축 이벤트

이 태스크가 끝나면 **게임을 실행했을 때 실제로 파일이 쌓인다.** 파이프라인 검증 지점이다.

**Files:**
- Create: `Assets/Scripts/_Core/Telemetry/TelemetryEvents.cs`
- Create: `Assets/Scripts/_Core/Telemetry/Telemetry.cs`
- Create: `Assets/Scripts/_Core/Telemetry/TelemetryRunner.cs`

**Interfaces:**
- Consumes: `TelemetryPayload`(T1), `TelemetrySession`(T2), `TelemetryBuffer`(T3), `TelemetryFileSink`(T4)
- Produces:
  - `Telemetry.Log(string eventName, TelemetryPayload payload = null)`
  - `Telemetry.Context` → `TelemetrySession` (null 가능 — 초기화 전)
  - `Telemetry.IsEnabled { get; set; }`
  - `Telemetry.Init()` / `Telemetry.Flush()` / `Telemetry.Shutdown()`
  - `Telemetry.EchoToConsole { get; set; }` — 에디터 콘솔 출력 토글
  - `Telemetry.LogDirectory` → `string`
  - `TelemetryEvents.SessionStart` = `"session_start"` 등 상수

- [ ] **Step 1: 이벤트 이름 상수 작성**

`Assets/Scripts/_Core/Telemetry/TelemetryEvents.cs`:

```csharp
// @tags: telemetry, events, constants, names

/// <summary>텔레메트리 이벤트 이름 상수(설계 §3). 호출부에 문자열 리터럴을 흩뿌리지 않는다.</summary>
public static class TelemetryEvents
{
    // 세션·이탈
    public const string SessionStart = "session_start";
    public const string SessionEnd   = "session_end";
    public const string Heartbeat    = "heartbeat";

    // 안정성
    public const string Error = "error";

    // 지역 진행
    public const string DiveStart        = "dive_start";
    public const string DiveEnd          = "dive_end";
    public const string RegionFirstEnter = "region_first_enter";
    public const string DepthMilestone   = "depth_milestone";

    // 스태미나·실패
    public const string StaminaDepleted  = "stamina_depleted";
    public const string EmergencyEscape  = "emergency_escape";
    public const string EncumberedEnter  = "encumbered_enter";

    // 경제
    public const string DaySettled      = "day_settled";
    public const string ShopTransaction = "shop_transaction";
    public const string UpgradePurchased = "upgrade_purchased";
    public const string UpgradeBlocked   = "upgrade_blocked";

    // 마켓
    public const string CoinBet        = "coin_bet";
    public const string CoinDaySummary = "coin_day_summary";
    public const string StockTrade     = "stock_trade";
    public const string MarketVisit    = "market_visit";

    // 가이드
    public const string GuideShown   = "guide_shown";
    public const string GuideSkipped = "guide_skipped";
}
```

- [ ] **Step 2: 정적 진입점 작성**

`Assets/Scripts/_Core/Telemetry/Telemetry.cs`:

```csharp
// @tags: telemetry, static, entry-point, log, facade

using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 텔레메트리 정적 진입점 (DayEarningsLedger.Report 패턴).
/// 게임플레이 코드는 Telemetry.Log() 한 줄만 알면 되고, 버퍼·파일 기록은 뒤에서 처리한다.
///
/// Phase 1은 로컬 JSONL 기록까지만 한다. 원격 전송은 Phase 2에서
/// 이 디렉토리를 스캔하는 별도 소비자(TelemetryUploader)로 추가된다 — 이 클래스는 바뀌지 않는다.
/// </summary>
public static class Telemetry
{
    private const string FolderName = "telemetry";

    private static TelemetrySession _session;
    private static TelemetryBuffer _buffer;
    private static TelemetryFileSink _sink;
    private static bool _initialized;

    /// <summary>
    /// 수집 활성화 게이트. Phase 2에서 옵트아웃 토글이 이 값을 끈다(설계 §6).
    /// 꺼져 있으면 Log()가 즉시 반환한다.
    /// </summary>
    public static bool IsEnabled { get; set; } = true;

    /// <summary>에디터에서 이벤트를 콘솔에도 출력한다(기본 꺼짐).</summary>
    public static bool EchoToConsole { get; set; }

    /// <summary>현재 세션 컨텍스트. 초기화 전에는 null.</summary>
    public static TelemetrySession Context => _session;

    public static string LogDirectory => Path.Combine(Application.persistentDataPath, FolderName);

    public static void Init()
    {
        if (_initialized) return;

        DateTime now = DateTime.Now;
        string sessionId = Guid.NewGuid().ToString("N");

        _session = new TelemetrySession(
            TelemetrySession.LoadOrCreateAnonId(),
            sessionId,
            BuildType(),
            Application.version);

        _buffer = new TelemetryBuffer();
        _sink = new TelemetryFileSink(LogDirectory, TelemetryFileSink.BuildFileName(now, sessionId));
        _initialized = true;

        _sink.EnforceQuota();
        Debug.Log($"[Telemetry] 시작 — {_sink.FilePath}");
    }

    /// <summary>이벤트 기록. payload가 null이면 빈 오브젝트로 기록된다.</summary>
    public static void Log(string eventName, TelemetryPayload payload = null)
    {
        if (!IsEnabled || !_initialized || string.IsNullOrEmpty(eventName)) return;

        try
        {
            string line = _session.BuildLine(eventName, payload?.ToJsonObject() ?? "{}");
            _buffer.Add(line);

            if (EchoToConsole) Debug.Log($"[Telemetry] {line}");
            if (_buffer.IsFull) Flush();
        }
        catch (Exception e)
        {
            // 텔레메트리 예외가 게임플레이로 새어나가면 안 된다
            Debug.LogWarning($"[Telemetry] 기록 실패 ({eventName}): {e.Message}");
        }
    }

    /// <summary>버퍼를 파일로 내린다.</summary>
    public static void Flush()
    {
        if (!_initialized) return;
        _sink.Append(_buffer.Drain());
    }

    /// <summary>세션 종료 — 마지막 플러시 후 비활성화한다.</summary>
    public static void Shutdown()
    {
        if (!_initialized) return;
        Flush();
        _initialized = false;
    }

    private static string BuildType()
    {
#if DEMO_BUILD
        return "demo";
#else
        return "ea";
#endif
    }
}
```

- [ ] **Step 3: 러너 작성**

`Assets/Scripts/_Core/Telemetry/TelemetryRunner.cs`:

```csharp
// @tags: telemetry, runner, monobehaviour, heartbeat, flush, bootstrap, error

using UnityEngine;

/// <summary>
/// 텔레메트리 수명주기 담당 MonoBehaviour.
/// 게임 시작 시 자동 생성되어(RuntimeInitializeOnLoadMethod) 씬 전환을 넘어 살아남는다.
/// 씬에 오브젝트를 배치할 필요가 없다.
///
/// 담당: 60초 주기 플러시, heartbeat, 누적 플레이타임 갱신, 예외 캡처, 종료 훅.
/// </summary>
public sealed class TelemetryRunner : MonoBehaviour
{
    private const float FlushInterval = 60f;
    private const float HeartbeatInterval = 60f;

    private float _flushTimer;
    private float _heartbeatTimer;
    private string _lastScreen = "boot";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("[TelemetryRunner]");
        go.AddComponent<TelemetryRunner>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        Telemetry.Init();
        Application.logMessageReceived += OnLogMessage;

        Telemetry.Log(TelemetryEvents.SessionStart, TelemetryPayload.New()
            .Add("os", SystemInfo.operatingSystem)
            .Add("cpu", SystemInfo.processorType)
            .Add("gpu", SystemInfo.graphicsDeviceName)
            .Add("ram_mb", SystemInfo.systemMemorySize)
            .Add("screen", $"{Screen.width}x{Screen.height}")
            .Add("language", Application.systemLanguage.ToString()));
    }

    private void Update()
    {
        if (Telemetry.Context == null) return;

        float dt = Time.unscaledDeltaTime;
        Telemetry.Context.PlaytimeTotal += dt;

        _flushTimer += dt;
        if (_flushTimer >= FlushInterval)
        {
            _flushTimer = 0f;
            Telemetry.Flush();
        }

        _heartbeatTimer += dt;
        if (_heartbeatTimer >= HeartbeatInterval)
        {
            _heartbeatTimer = 0f;
            _lastScreen = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Telemetry.Log(TelemetryEvents.Heartbeat, TelemetryPayload.New().Add("screen", _lastScreen));
        }
    }

    /// <summary>예외·에러 로그를 error 이벤트로 남긴다(설계 §3.6).</summary>
    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;

        Telemetry.Log(TelemetryEvents.Error, TelemetryPayload.New()
            .Add("type", type.ToString())
            .Add("message", Truncate(condition, 300))
            .Add("trace", Truncate(stackTrace, 1000)));
    }

    private void OnApplicationQuit()
    {
        Application.logMessageReceived -= OnLogMessage;

        Telemetry.Log(TelemetryEvents.SessionEnd, TelemetryPayload.New()
            .Add("screen", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            .Add("session_seconds", Telemetry.Context != null ? (int)Telemetry.Context.PlaytimeTotal : 0));

        Telemetry.Shutdown();
    }

    /// <summary>모바일·창 최소화 대비 — 포커스를 잃으면 즉시 플러시한다.</summary>
    private void OnApplicationPause(bool paused)
    {
        if (paused) Telemetry.Flush();
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= max ? s : s.Substring(0, max);
    }
}
```

- [ ] **Step 4: 동작 확인**

Unity 에디터에서 Play 진입 → 10초 후 정지.
콘솔에 `[Telemetry] 시작 — <경로>` 가 찍히고, 해당 경로에 `.jsonl` 파일이 생기며
`session_start` 와 `session_end` 두 줄이 들어 있어야 한다.
**사람이 확인한다.**

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `feat: 텔레메트리 진입점·러너·세션 이벤트 추가`

---

## Task 6: 에디터 지원 메뉴

**Files:**
- Create: `Assets/Scripts/Editor/TelemetryEditorMenu.cs`

**Interfaces:**
- Consumes: `Telemetry.LogDirectory`, `Telemetry.EchoToConsole`, `Telemetry.Flush()` (Task 5)
- Produces: 없음 (에디터 전용)

- [ ] **Step 1: 구현**

`Assets/Scripts/Editor/TelemetryEditorMenu.cs`:

```csharp
// @tags: telemetry, editor, menu, tool, log, folder

using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 텔레메트리 로그 확인용 에디터 메뉴.
/// 로컬 전용(Phase 1)에서는 파일을 직접 열어보는 것이 유일한 확인 수단이라 필요하다.
/// </summary>
public static class TelemetryEditorMenu
{
    private const string EchoMenu = "Tools/Telemetry/콘솔에 이벤트 출력";

    [MenuItem("Tools/Telemetry/로그 폴더 열기")]
    private static void OpenFolder()
    {
        string dir = Telemetry.LogDirectory;
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir);
    }

    [MenuItem("Tools/Telemetry/지금 플러시")]
    private static void FlushNow()
    {
        Telemetry.Flush();
        Debug.Log("[Telemetry] 수동 플러시 완료");
    }

    [MenuItem("Tools/Telemetry/로그 전체 삭제")]
    private static void ClearLogs()
    {
        string dir = Telemetry.LogDirectory;
        if (!Directory.Exists(dir))
        {
            Debug.Log("[Telemetry] 삭제할 로그가 없습니다.");
            return;
        }

        if (!EditorUtility.DisplayDialog("텔레메트리 로그 삭제",
                $"{dir}\n\n안의 모든 .jsonl 파일을 지웁니다. 계속할까요?", "삭제", "취소"))
            return;

        foreach (string f in Directory.GetFiles(dir, "*.jsonl")) File.Delete(f);
        Debug.Log("[Telemetry] 로그 전체 삭제 완료");
    }

    [MenuItem(EchoMenu)]
    private static void ToggleEcho()
    {
        Telemetry.EchoToConsole = !Telemetry.EchoToConsole;
        Menu.SetChecked(EchoMenu, Telemetry.EchoToConsole);
        Debug.Log($"[Telemetry] 콘솔 출력 {(Telemetry.EchoToConsole ? "켬" : "끔")}");
    }

    [MenuItem(EchoMenu, true)]
    private static bool ToggleEchoValidate()
    {
        Menu.SetChecked(EchoMenu, Telemetry.EchoToConsole);
        return true;
    }
}
```

- [ ] **Step 2: 동작 확인**

Unity 메뉴 `Tools > Telemetry` 에 항목 4개가 보이고, "로그 폴더 열기"가 탐색기를 여는지 확인. **사람이 확인한다.**

- [ ] **Step 3: 체크포인트**

UVCS 체크인: `feat: 텔레메트리 에디터 메뉴 추가`

---

## Task 7: 루프 축 계측 — dive / region / depth

**Files:**
- Modify: `Assets/Scripts/_Core/Managers/SettlementManager.cs`

`SettlementManager`가 이미 탐험 추적(`StartTracking` / `StopTracking` / `MaxDepth` / `MinedMinerals`)을 하고 있으므로,
루프 축 이벤트 4개를 전부 이 파일에서 낼 수 있다.

**Interfaces:**
- Consumes: `Telemetry.Log`, `Telemetry.Context`, `TelemetryPayload`, `TelemetryEvents` (Task 5)
- Produces: 없음

**참고할 기존 코드**
- `StartTracking(Transform player, float initialY = 0f)` — 하강 시작
- `StopTracking()` — 귀환. `MaxDepth`·`TimeUnderground`·`MinedMinerals` 확정
- `Update()` 안에서 `currentDepth = Mathf.Max(0, _initialPlayerY - _playerTransform.position.y)` 계산 중
- `TileDataManager.Instance.GetTileTypeAtDepth(int y)` → 현재 지역(`TileType`)

- [ ] **Step 1: 컨텍스트 갱신 + 마일스톤·지역 최초 진입 추가**

`SettlementManager`에 필드와 메서드를 추가한다:

```csharp
    // ─── 텔레메트리 ───
    // 100m 단위 최초 도달 기록용. 세션 내에서만 유지한다(재시작 시 다시 찍혀도 무방 — 분석은 최초값만 본다).
    private int _lastDepthMilestone;
    private string _lastReportedRegion = string.Empty;
    private static readonly System.Collections.Generic.HashSet<string> _visitedRegions
        = new System.Collections.Generic.HashSet<string>();

    /// <summary>매 프레임 깊이·지역을 텔레메트리 컨텍스트에 반영하고, 경계를 넘으면 이벤트를 낸다.</summary>
    private void UpdateTelemetryContext(float currentDepth)
    {
        if (Telemetry.Context == null) return;

        Telemetry.Context.Depth = currentDepth;

        // 지역 판정 — 플레이어 Y를 그대로 넘긴다(GetTileTypeAtDepth는 월드 Y 기준)
        string region = _lastReportedRegion;
        if (TileDataManager.Instance != null && _playerTransform != null)
        {
            region = TileDataManager.Instance
                .GetTileTypeAtDepth(Mathf.RoundToInt(_playerTransform.position.y)).ToString();
        }

        if (region != _lastReportedRegion)
        {
            _lastReportedRegion = region;
            Telemetry.Context.Region = region;

            // 지역 최초 진입 — 도달 시점(playtime)이 콘텐츠 분량 검증의 핵심 지표(설계 §3.1)
            if (!string.IsNullOrEmpty(region) && _visitedRegions.Add(region))
            {
                Telemetry.Log(TelemetryEvents.RegionFirstEnter, TelemetryPayload.New()
                    .Add("region", region)
                    .Add("depth", currentDepth));
            }
        }

        // 100m 단위 최초 도달
        int milestone = Mathf.FloorToInt(currentDepth / 100f) * 100;
        if (milestone > _lastDepthMilestone)
        {
            _lastDepthMilestone = milestone;
            Telemetry.Log(TelemetryEvents.DepthMilestone, TelemetryPayload.New()
                .Add("milestone", milestone)
                .Add("region", region));
        }
    }
```

- [ ] **Step 2: Update에서 호출 연결**

`Update()` 안의 깊이 계산 직후(기존 `if (currentDepth > MaxDepth)` 블록 옆)에 한 줄을 추가한다:

```csharp
            float currentDepth = Mathf.Max(0, _initialPlayerY - _playerTransform.position.y);
            if (currentDepth > MaxDepth)
            {
                MaxDepth = currentDepth;
            }

            UpdateTelemetryContext(currentDepth);   // ← 추가
```

- [ ] **Step 3: dive_start 발행**

`StartTracking(Transform player, float initialY = 0f)` 본문 끝에 추가한다:

```csharp
        _lastDepthMilestone = 0;
        _lastReportedRegion = string.Empty;

        var stat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        Telemetry.Log(TelemetryEvents.DiveStart, TelemetryPayload.New()
            .Add("time_of_day", DayCycleManager.Instance != null
                ? DayCycleManager.Instance.CurrentTime.ToString() : "Unknown")
            .Add("stamina", stat != null ? stat.CurrentStamina : 0f)
            .Add("gold", stat != null ? stat.Gold : 0));
```

- [ ] **Step 4: dive_end 발행**

`StopTracking()` 본문에서 `IsDataPending`을 세운 뒤에 추가한다:

```csharp
        int mineralCount = 0;
        foreach (var kv in MinedMinerals) mineralCount += kv.Value;

        var statEnd = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        Telemetry.Log(TelemetryEvents.DiveEnd, TelemetryPayload.New()
            .Add("result", "return")            // 긴급탈출은 emergency_escape 이벤트로 별도 기록된다
            .Add("max_depth", MaxDepth)
            .Add("seconds", TimeUnderground)
            .Add("mineral_kinds", MinedMinerals.Count)
            .Add("mineral_count", mineralCount)
            .Add("stamina_left", statEnd != null ? statEnd.CurrentStamina : 0f));
```

- [ ] **Step 5: 동작 확인**

지하로 내려갔다 귀환한 뒤 로그 파일에 `dive_start` → `depth_milestone` → `region_first_enter` → `dive_end` 순서로
이벤트가 남는지 확인한다. **사람이 확인한다.**

- [ ] **Step 6: 체크포인트**

UVCS 체크인: `feat: 루프 축 텔레메트리 계측 추가`

---

## Task 8: 실패 축 계측 — 스태미나 · 긴급탈출 · 과적

**Files:**
- Modify: `Assets/Scripts/UI/Player/EncumbranceController.cs`
- Modify: `Assets/Scripts/_Core/Managers/EmergencyEscapeReport.cs`
- Modify: `Assets/Scripts/UI/Player/StaminaManager.cs`

**Interfaces:**
- Consumes: `Telemetry.Log`, `TelemetryPayload`, `TelemetryEvents` (Task 5)
- Produces: 없음

**참고할 기존 코드**
- `PlayerStat.OnStaminaDepleted` — 이미 존재하는 이벤트. `StaminaManager`가 `playerStats`를 들고 있으므로 여기서 구독한다
- `EncumbranceController.OnEncumbranceChanged?.Invoke(IsEncumbered)` — 상태 전이 지점 (약 127행)
- `EmergencyEscapeReport.Record(lost, kept)` — 페널티 확정 지점. `LostTotal`/`KeptTotal`이 여기서 확정된다

- [ ] **Step 1: 과적 진입 계측**

`EncumbranceController`의 `OnEncumbranceChanged?.Invoke(IsEncumbered);` **직전**에 추가한다:

```csharp
        // 과적 진입만 기록한다 — 해제는 분석에 쓰이지 않아 노이즈만 늘린다(설계 §3.2)
        if (IsEncumbered)
        {
            Telemetry.Log(TelemetryEvents.EncumberedEnter, TelemetryPayload.New()
                .Add("weight", TotalWeight)
                .Add("threshold", EncumbranceThreshold)
                .Add("over", TotalWeight - EncumbranceThreshold));
        }
```

- [ ] **Step 2: 긴급탈출 계측**

`EmergencyEscapeReport.Record(...)` 본문 맨 끝(`HasPending = LostTotal > 0;` 다음)에 추가한다:

```csharp
        Telemetry.Log(TelemetryEvents.EmergencyEscape, TelemetryPayload.New()
            .Add("lost_count", LostTotal)
            .Add("kept_count", KeptTotal)
            .Add("lost_kinds", _lost.Count)
            .Add("kept_kinds", _kept.Count));
```

- [ ] **Step 3: 스태미나 소진 계측**

`StaminaManager`에 구독을 추가한다. 기존 `playerStats` 필드를 사용한다.

```csharp
    private void OnEnable()
    {
        if (playerStats != null) playerStats.OnStaminaDepleted += OnStaminaDepletedForTelemetry;
    }

    private void OnDisable()
    {
        if (playerStats != null) playerStats.OnStaminaDepleted -= OnStaminaDepletedForTelemetry;
    }

    /// <summary>스태미나 소진 지점 기록 — 일차별 곡선이 학습 여부를 보여준다(설계 §3.2).</summary>
    private void OnStaminaDepletedForTelemetry()
    {
        var encumbrance = FindFirstObjectByType<EncumbranceController>(FindObjectsInactive.Include);

        Telemetry.Log(TelemetryEvents.StaminaDepleted, TelemetryPayload.New()
            .Add("weight", encumbrance != null ? encumbrance.TotalWeight : 0f)
            .Add("encumbered", encumbrance != null && encumbrance.IsEncumbered)
            .Add("injury", Injury)
            .Add("burn", Burn)
            .Add("frostbite", Frostbite));
    }
```

**주의:** `StaminaManager`에 이미 `OnEnable`/`OnDisable`이 있으면 새로 만들지 말고 기존 메서드 안에 구독 두 줄만 넣는다.
`playerStats`가 `Awake`에서 세팅된다면 `OnEnable`이 먼저 돌 수 있으므로, null이면 `Start()`에서 한 번 더 시도한다.

- [ ] **Step 4: 동작 확인**

지하에서 스태미나를 모두 소진하고, 과적 상태로 만들고, 일시정지 메뉴에서 긴급탈출을 실행한다.
로그에 `stamina_depleted` · `encumbered_enter` · `emergency_escape` 세 이벤트가 남는지 확인한다. **사람이 확인한다.**

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `feat: 실패 축 텔레메트리 계측 추가`

---

## Task 9: 경제 축 계측 — 정산 · 상점 · 업그레이드

**Files:**
- Modify: `Assets/Scripts/Gameplay/Environment/BedInteractable.cs`
- Modify: `Assets/Scripts/UI/Shop/ShopManager.cs`
- Modify: `Assets/Scripts/UI/Upgrade/UpgradeManager.cs`

**Interfaces:**
- Consumes: `Telemetry.Log`, `Telemetry.Context`, `TelemetryPayload`, `TelemetryEvents` (Task 5)
- Produces: 없음

**참고할 기존 코드**
- `BedInteractable.SleepRoutine()` — `DayEarningsReport report = DayEarningsLedger.BuildReport(endedDay, goldNow);` 직후가 스냅샷 지점
- `ShopManager.SellItem(InterfaceInventoryItem itemToSell, int amount, bool fromWarehouse, int slotIndex)` → `bool`
- `ShopManager.BuyItem(ShopItemData itemData, int quantity)` → `bool`
- `UpgradeManager.UnlockNode(UpgradeNodeSO node)` → `bool` (177행)

- [ ] **Step 1: day_settled 스냅샷 발행**

`BedInteractable.SleepRoutine()` 안, `DayEarningsLedger.ResetForNewDay(goldNow);` **직전**에 추가한다:

```csharp
        // 하루 스냅샷 — N일차 골드·깊이 커브의 원천 데이터(설계 §3.3)
        var settlement = SettlementManager.Instance;
        Telemetry.Log(TelemetryEvents.DaySettled, TelemetryPayload.New()
            .Add("ended_day", endedDay)
            .Add("gold_start", goldNow - report.total)
            .Add("gold_end", goldNow)
            .Add("mineral_sale", report.mineralSale)
            .Add("stock", report.stock)
            .Add("coin", report.coin)
            .Add("shop_purchase", report.shopPurchase)
            .Add("upgrade", report.upgrade)
            .Add("other", report.other)
            .Add("max_depth", settlement != null ? settlement.MaxDepth : 0f));

        // 다음 날 컨텍스트 반영
        if (Telemetry.Context != null) Telemetry.Context.Day = endedDay + 1;
```

- [ ] **Step 2: shop_transaction 발행 (판매)**

`ShopManager.SellItem(...)`은 판매 경로가 **창고 / 인벤토리 두 갈래**이고 각각 `return true;`로 끝난다.
두 곳 모두에 넣는다. 대금 변수는 이미 계산되어 있는 `totalGold`이며,
바로 위에서 `playerStats.AddGold(totalGold)` 와 `DayEarningsLedger.Report(...)`가 호출된다.
그 두 줄 **바로 다음**에 추가한다:

```csharp
                    Telemetry.Log(TelemetryEvents.ShopTransaction, TelemetryPayload.New()
                        .Add("kind", "sell")
                        .Add("item", mineral.mineralID.ToString())
                        .Add("amount", amount)
                        .Add("gold", totalGold)
                        .Add("from_warehouse", fromWarehouse));
```

- [ ] **Step 3: shop_transaction 발행 (구매)**

`ShopManager.BuyItem(ShopItemData itemData, int quantity = 1)`에서 구매가 확정되어 `true`를 반환하기 직전에 추가한다.
대금 변수는 이미 계산되어 있는 `totalPrice`다(`int totalPrice = itemData.price * quantity;`).

```csharp
        Telemetry.Log(TelemetryEvents.ShopTransaction, TelemetryPayload.New()
            .Add("kind", "buy")
            .Add("item", itemData.itemType == ShopItemType.Item
                ? itemData.itemID.ToString() : itemData.equipmentID.ToString())
            .Add("amount", quantity)
            .Add("gold", -totalPrice));
```

지출을 **음수로** 기록하는 것은 `DayEarningsLedger.Report`의 부호 규약과 맞추기 위함이다.

- [ ] **Step 4: 업그레이드 성공 발행**

`UpgradeManager.UnlockNode(UpgradeNodeSO node)`의 마지막 `return true;` 직전
(`OnUpgradeStateChanged?.Invoke();` 다음)에 추가한다. 비용은 `node.cost`다.

```csharp
        Telemetry.Log(TelemetryEvents.UpgradePurchased, TelemetryPayload.New()
            .Add("node", node.nodeId)
            .Add("cost", node.cost));
```

- [ ] **Step 5: 업그레이드 차단 발행**

`UnlockNode`의 첫 줄은 `if (!CanUnlock(node)) return false;` 이며, 이 검사는 골드 부족과
선행 조건 미충족을 함께 판정한다. 따라서 사유를 골드 기준으로 나눠 기록한다.
이 early return을 아래로 교체한다:

```csharp
        if (!CanUnlock(node))
        {
            // 사고 싶었지만 못 산 것 = 욕구. 부족 금액 분포가 골드 커브를 정량화한다(설계 §3.3)
            if (node != null)
            {
                PlayerStat blockedStat = FindFirstObjectByType<PlayerStat>();
                int gold = blockedStat != null ? blockedStat.Gold : 0;
                Telemetry.Log(TelemetryEvents.UpgradeBlocked, TelemetryPayload.New()
                    .Add("node", node.nodeId)
                    .Add("cost", node.cost)
                    .Add("gold", gold)
                    .Add("short_by", Mathf.Max(0, node.cost - gold))
                    .Add("reason", gold < node.cost ? "gold" : "requirement"));
            }
            return false;
        }
```

`short_by`가 0이면서 `reason`이 `requirement`인 건은 "돈은 있는데 선행 조건이 안 풀린" 경우다.
이 둘을 섞으면 골드 커브 분석이 오염되므로 반드시 구분해서 남긴다.

- [ ] **Step 6: 동작 확인**

상점에서 광물을 팔고 아이템을 사고, 업그레이드를 하나 사고, 살 수 없는 노드를 클릭한 뒤 침대에서 잔다.
로그에 `shop_transaction` 2건 · `upgrade_purchased` · `upgrade_blocked` · `day_settled` 가 남는지 확인한다. **사람이 확인한다.**

- [ ] **Step 7: 체크포인트**

UVCS 체크인: `feat: 경제 축 텔레메트리 계측 추가`

---

## Task 10: 마켓 축 · 가이드 축 계측

**Files:**
- Modify: `Assets/Scripts/Coin/Core/CoinGameManager.cs`
- Modify: `Assets/Scripts/Market/MarketSceneController.cs`
- Modify: `Assets/Scripts/Systems/Guide/GuideManager.cs`

**Interfaces:**
- Consumes: `Telemetry.Log`, `TelemetryPayload`, `TelemetryEvents` (Task 5)
- Produces: 없음

**참고할 기존 코드**
- `CoinGameManager.TryBet(BetDirection dir, int stake, out CoinRoundResult result)` → `bool` (278행)
- `GuideManager.Show(string guideId, bool force = true)` (238행) — `GuideOverlayUI.Show(guide)` 호출 직전이 삽입 지점
- `MarketSceneController` — 씬 진입/이탈 지점

- [ ] **Step 1: coin_bet 발행**

`CoinGameManager.TryBet(...)`에서 `ApplyDelta(result.delta);` 와 `RoundsToday++;` 가 실행된 뒤,
`true`를 반환하기 직전에 추가한다.
설계 §3.4의 목적은 **실측 EV가 설계값과 맞는지 검증**하는 것이므로, 스테이크와 최종 손익을 함께 남긴다.
`result.delta`는 fee·출금 한도·소프트 청산이 모두 적용된 **최종 값**이어야 하므로 반드시 이 위치에서 읽는다.

```csharp
            Telemetry.Log(TelemetryEvents.CoinBet, TelemetryPayload.New()
                .Add("slot", LockedSlotId)
                .Add("dir", dir.ToString())
                .Add("stake", stake)
                .Add("delta", result.delta)
                .Add("fee", result.fee)
                .Add("liquidated", result.liquidated)
                .Add("leverage", CurrentLeverage)
                .Add("streak", CurrentStreak)
                .Add("rounds_today", RoundsToday));
```

`CurrentLeverage`가 `float`이면 `Add(string, float)` 오버로드가, `int`면 `Add(string, int)`가 자동 선택된다.
`result.liquidated`가 `bool`임은 설계 §마켓 규칙에 정의되어 있다.

- [ ] **Step 2: market_visit 발행**

`MarketSceneController`의 씬 진입 지점(`Start` 또는 초기화 메서드)에 추가한다:

```csharp
        _visitStartTime = Time.unscaledTime;
        Telemetry.Log(TelemetryEvents.MarketVisit, TelemetryPayload.New().Add("phase", "enter"));
```

이탈 지점(씬을 닫는 메서드)에 추가한다:

```csharp
        Telemetry.Log(TelemetryEvents.MarketVisit, TelemetryPayload.New()
            .Add("phase", "exit")
            .Add("seconds", Time.unscaledTime - _visitStartTime));
```

필드를 클래스에 추가한다:

```csharp
    private float _visitStartTime;   // 마켓 체류 시간 측정용
```

- [ ] **Step 3: guide_shown 발행**

`GuideManager.Show(string guideId, bool force = true)`의 `GuideOverlayUI.Show(guide);` **직전**에 추가한다:

```csharp
        Telemetry.Log(TelemetryEvents.GuideShown, TelemetryPayload.New()
            .Add("guide_id", guideId)
            .Add("first_time", !HasSeen(guideId)));
```

- [ ] **Step 4: 동작 확인**

마켓에 들어가 코인을 한 판 베팅하고 나온 뒤, 가이드를 하나 띄운다.
로그에 `market_visit`(enter/exit) · `coin_bet` · `guide_shown` 이 남는지 확인한다. **사람이 확인한다.**

- [ ] **Step 5: 체크포인트**

UVCS 체크인: `feat: 마켓·가이드 축 텔레메트리 계측 추가`

---

## 범위 외 (Phase 2)

아래는 이 계획에 포함되지 않는다. `design.md` §8 Phase 2 참조.

- 원격 업로드(`TelemetryUploader`), Supabase 테이블, 재시도 백오프
- 프라이버시 고지 팝업 · 옵트아웃 토글 (`Telemetry.IsEnabled` 게이트만 Task 5에서 준비됨)
- 분석 쿼리 작성
- `stock_trade` · `coin_day_summary` · `guide_skipped` — 이벤트 상수는 `TelemetryEvents`에 정의해 두었으나
  삽입은 하지 않는다. 각각의 삽입 지점(주식 체결부, 코인 일일 정산, 가이드 스킵 버튼)을 먼저 확정해야 한다.

## 알려진 제약

- `_visitedRegions`는 정적 `HashSet`이라 **세션 내에서만** 최초 진입을 판정한다.
  게임을 재시작하면 이미 가 본 지역도 다시 `region_first_enter`로 찍힌다.
  분석 시 `anon_id`별 최초 1건만 취하면 되므로 의도적으로 단순하게 둔다.
- `day` 컨텍스트는 `BedInteractable`(Task 9)이 갱신한다. 그 전까지 발생한 이벤트의 `day`는
  세이브 로드 시점 값이 아니라 `0`일 수 있다. 세이브 로드 직후 `Telemetry.Context.Day`를 채우는 작업은
  Phase 2에서 `SaveManager` 로드 경로에 추가한다.
