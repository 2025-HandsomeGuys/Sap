# 버그 리포트 시스템 구현 계획

> **작업자 안내:** 태스크 단위로 순서대로 진행한다. 체크박스(`- [ ]`)로 진행을 추적한다.

**목표:** 게임 중 F12 한 번으로 스크린샷 + 그 시점의 게임 상태 + 최근 로그 + 세이브 스냅샷을 로컬 폴더에 저장한다.

**아키텍처:** 캡처(스크린샷·상태)를 오버레이보다 **먼저** 끝내 스냅샷을 불변으로 확정한 뒤, 메모 입력 오버레이를 띄우고, 저장 시 파일 4개를 쓴다. 6개 파일이 각각 하나의 책임만 갖는다 — 수집기는 UI를 모르고, UI는 파일 쓰기를 모르고, 라이터는 게임 상태를 모른다.

**설계 문서:** [bug-report-system.md](bug-report-system.md)

**기술 스택:** Unity 6000.3.2f1, 레거시 `Input`, TextMeshPro, `CodeUI` 코드 UI 키트, `JsonUtility`, `System.IO`

## 전역 제약

- **커밋 단계 없음.** 이 프로젝트는 UVCS를 쓴다. `git` 명령을 실행하지 않는다.
- **테스트는 작성만 한다.** EditMode 테스트 파일은 만들되 `mcp__mcp-unity__run_tests` 등으로 실행하지 않는다. 실행은 사람이 Unity Test Runner로 한다. 테스트 통과를 다음 태스크의 게이트로 삼지 않는다.
- 모든 신규 파일은 `Assets/Scripts/Utils/BugReport/` 아래에 둔다.
- 모든 신규 파일 첫 줄에 프로젝트 관례대로 `// @tags: ...` 주석을 단다.
- 전 파일을 `#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT` 로 감싼다.
- 기존 파일은 **한 개도 수정하지 않는다.** 수집은 전부 기존 public API 읽기로만 한다.
- `Object.FindObjectOfType`은 Unity 6에서 obsolete다. `Object.FindFirstObjectByType<T>()`를 쓴다.
- 청크 좌표 변환은 반드시 `ChunkCoords.ToChunk(worldPos)`를 쓴다. `position.x / 10f`를 직접 쓰지 않는다.

---

## 파일 구조

| 파일 | 책임 |
|---|---|
| `BugReportData.cs` | 직렬화 구조체. 로직 없음 |
| `BugReportLogBuffer.cs` | 로그 링버퍼. Unity 로그 콜백 구독 |
| `BugReportWriter.cs` | 폴더 생성 + 파일 4개 쓰기. 게임 상태를 모름 |
| `BugReportCollector.cs` | 싱글톤에서 상태 수집 → `BugReportData` |
| `BugReportOverlayUI.cs` | 메모 입력 오버레이 (코드 생성) |
| `BugReportSystem.cs` | 진입점. 핫키 + 캡처 코루틴 오케스트레이션 |
| `Assets/Tests/EditMode/BugReportLogBufferTests.cs` | 링버퍼 EditMode 테스트 |
| `Assets/Tests/EditMode/BugReportWriterTests.cs` | 파일 쓰기 EditMode 테스트 |

의존 방향: `System` → `Collector`/`Overlay`/`Writer` → `Data`. 역방향 참조 없음.

---

### Task 1: BugReportData — 직렬화 구조체

**Files:**
- Create: `Assets/Scripts/Utils/BugReport/BugReportData.cs`

**Interfaces:**
- Produces: `BugReportData` (필드: `memo`, `meta`, `player`, `world`, `ui`, `extra`), `BugReportMeta`, `BugReportPlayer`, `BugReportWorld`, `BugReportLogEntry`

`JsonUtility`는 `Dictionary`를 직렬화하지 못한다. 확장 컨텍스트는 `List<string>`에 `"key=value"` 형태로 담는다.
좌표는 `Vector2Int`가 아니라 `string`으로 담는다 — 사람이 JSON을 눈으로 읽는 것이 이 파일의 유일한 용도이기 때문이다.

- [ ] **Step 1: 파일 작성**

```csharp
// @tags: bug-report, qa, serialization, data
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.Collections.Generic;

namespace BugReport
{
    /// <summary>report.json 최상위 구조. 로직 없는 순수 데이터 컨테이너.</summary>
    [Serializable]
    public class BugReportData
    {
        public string memo = "";
        public BugReportMeta meta = new BugReportMeta();
        public BugReportPlayer player = new BugReportPlayer();
        public BugReportWorld world = new BugReportWorld();
        public string ui = Unavailable;
        /// <summary>확장 컨텍스트. "key=value" 문자열 목록 (JsonUtility가 Dictionary를 못 다룸).</summary>
        public List<string> extra = new List<string>();

        /// <summary>수집 실패한 필드에 넣는 표식. 리포트 자체는 계속 저장된다.</summary>
        public const string Unavailable = "<unavailable>";
    }

    [Serializable]
    public class BugReportMeta
    {
        public string localTime = "";
        public string appVersion = "";
        public string unityVersion = "";
        public string platform = "";
        public string sceneName = BugReportData.Unavailable;
        public string resolution = "";
        public float realtimeSinceStartup;
        public long totalMemoryMB;
        public int sessionErrorCount;
        public int sessionExceptionCount;
    }

    [Serializable]
    public class BugReportPlayer
    {
        public string worldPos = BugReportData.Unavailable;
        public string chunkCoord = BugReportData.Unavailable;
        public string velocity = BugReportData.Unavailable;
        public string stamina = BugReportData.Unavailable;
        public string gold = BugReportData.Unavailable;
        public string miningLevel = BugReportData.Unavailable;
        public string weight = BugReportData.Unavailable;
    }

    [Serializable]
    public class BugReportWorld
    {
        public string layer = BugReportData.Unavailable;
        public string day = BugReportData.Unavailable;
        public string timeOfDay = BugReportData.Unavailable;
        public string inDungeon = BugReportData.Unavailable;
        public int loadedChunkCount;
        public List<string> loadedChunks = new List<string>();
    }

    /// <summary>로그 링버퍼 한 줄.</summary>
    public struct BugReportLogEntry
    {
        public string condition;
        public string stackTrace;
        public UnityEngine.LogType type;
        public float time;
    }
}
#endif
```

- [ ] **Step 2: 컴파일 확인**

Unity 에디터로 포커스를 옮겨 컴파일을 통과하는지 확인한다. 콘솔에 에러가 없어야 한다.

---

### Task 2: BugReportLogBuffer — 로그 링버퍼

**Files:**
- Create: `Assets/Scripts/Utils/BugReport/BugReportLogBuffer.cs`
- Test: `Assets/Tests/EditMode/BugReportLogBufferTests.cs`

**Interfaces:**
- Consumes: `BugReportLogEntry` (Task 1)
- Produces:
  - `BugReportLogBuffer.Install()` — 로그 콜백 구독. 중복 호출 안전
  - `BugReportLogBuffer.Add(string condition, string stackTrace, LogType type, float time)` — 테스트용 직접 주입
  - `BugReportLogBuffer.Snapshot()` → `List<BugReportLogEntry>` (오래된 것 → 최신 순)
  - `BugReportLogBuffer.ErrorCount` / `ExceptionCount` (int)
  - `BugReportLogBuffer.Clear()`
  - `BugReportLogBuffer.Capacity` (const int = 300)

스택 트레이스는 `Error`/`Exception`/`Assert`만 저장한다. 전부 저장하면 로그 파일이 수 MB로 불어난다.
고정 크기 배열 + 쓰기 인덱스. 로그 한 줄마다 리스트를 늘리지 않는다.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
// @tags: bug-report, test, editmode, log-buffer
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using NUnit.Framework;
using UnityEngine;
using BugReport;

public class BugReportLogBufferTests
{
    [SetUp]
    public void SetUp() => BugReportLogBuffer.Clear();

    [Test]
    public void Snapshot_ReturnsEntriesInInsertionOrder()
    {
        BugReportLogBuffer.Add("first", "", LogType.Log, 1f);
        BugReportLogBuffer.Add("second", "", LogType.Log, 2f);

        var snap = BugReportLogBuffer.Snapshot();

        Assert.AreEqual(2, snap.Count);
        Assert.AreEqual("first", snap[0].condition);
        Assert.AreEqual("second", snap[1].condition);
    }

    [Test]
    public void Snapshot_WhenOverCapacity_KeepsNewestAndDropsOldest()
    {
        int over = BugReportLogBuffer.Capacity + 5;
        for (int i = 0; i < over; i++)
            BugReportLogBuffer.Add($"msg{i}", "", LogType.Log, i);

        var snap = BugReportLogBuffer.Snapshot();

        Assert.AreEqual(BugReportLogBuffer.Capacity, snap.Count);
        Assert.AreEqual("msg5", snap[0].condition, "가장 오래된 5줄이 밀려나야 한다");
        Assert.AreEqual($"msg{over - 1}", snap[snap.Count - 1].condition);
    }

    [Test]
    public void Add_StoresStackTraceOnlyForErrorLevels()
    {
        BugReportLogBuffer.Add("info", "STACK", LogType.Log, 1f);
        BugReportLogBuffer.Add("bad", "STACK", LogType.Error, 2f);

        var snap = BugReportLogBuffer.Snapshot();

        Assert.AreEqual("", snap[0].stackTrace, "일반 로그는 스택을 버린다");
        Assert.AreEqual("STACK", snap[1].stackTrace);
    }

    [Test]
    public void Counts_TrackErrorsAndExceptionsSeparately()
    {
        BugReportLogBuffer.Add("a", "", LogType.Error, 1f);
        BugReportLogBuffer.Add("b", "", LogType.Exception, 2f);
        BugReportLogBuffer.Add("c", "", LogType.Warning, 3f);

        Assert.AreEqual(1, BugReportLogBuffer.ErrorCount);
        Assert.AreEqual(1, BugReportLogBuffer.ExceptionCount);
    }

    [Test]
    public void Counts_SurviveRingBufferOverflow()
    {
        BugReportLogBuffer.Add("early", "", LogType.Error, 0f);
        for (int i = 0; i < BugReportLogBuffer.Capacity + 10; i++)
            BugReportLogBuffer.Add($"msg{i}", "", LogType.Log, i);

        Assert.AreEqual(1, BugReportLogBuffer.ErrorCount,
            "버퍼에서 밀려나도 세션 누적 카운트는 남아야 한다");
    }
}
#endif
```

- [ ] **Step 2: 구현 작성**

```csharp
// @tags: bug-report, qa, log, ring-buffer
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System.Collections.Generic;
using UnityEngine;

namespace BugReport
{
    /// <summary>
    /// 최근 로그 N줄을 담는 링버퍼. Application.logMessageReceived를 구독한다.
    /// 세션 누적 Error/Exception 카운트는 버퍼에서 밀려나도 유지된다 —
    /// "이 리포트 이전에 이미 예외가 있었다"가 중요한 단서이기 때문.
    /// </summary>
    public static class BugReportLogBuffer
    {
        public const int Capacity = 300;

        private static readonly BugReportLogEntry[] s_ring = new BugReportLogEntry[Capacity];
        private static int s_writeIndex;   // 다음에 쓸 위치
        private static int s_count;        // 채워진 개수 (최대 Capacity)
        private static bool s_installed;

        public static int ErrorCount { get; private set; }
        public static int ExceptionCount { get; private set; }

        /// <summary>로그 콜백 구독. 중복 호출해도 한 번만 걸린다.</summary>
        public static void Install()
        {
            if (s_installed) return;
            s_installed = true;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
            => Add(condition, stackTrace, type, Time.realtimeSinceStartup);

        /// <summary>버퍼에 한 줄 추가. 테스트에서 직접 호출한다.</summary>
        public static void Add(string condition, string stackTrace, LogType type, float time)
        {
            // 스택은 에러 계열만 보관 — 전부 담으면 로그 파일이 수 MB가 된다.
            bool keepStack = type == LogType.Error
                          || type == LogType.Exception
                          || type == LogType.Assert;

            s_ring[s_writeIndex] = new BugReportLogEntry
            {
                condition = condition ?? "",
                stackTrace = keepStack ? (stackTrace ?? "") : "",
                type = type,
                time = time,
            };

            s_writeIndex = (s_writeIndex + 1) % Capacity;
            if (s_count < Capacity) s_count++;

            if (type == LogType.Error) ErrorCount++;
            else if (type == LogType.Exception) ExceptionCount++;
        }

        /// <summary>오래된 것 → 최신 순으로 복사해 반환.</summary>
        public static List<BugReportLogEntry> Snapshot()
        {
            var result = new List<BugReportLogEntry>(s_count);
            int start = (s_writeIndex - s_count + Capacity) % Capacity;
            for (int i = 0; i < s_count; i++)
                result.Add(s_ring[(start + i) % Capacity]);
            return result;
        }

        public static void Clear()
        {
            s_writeIndex = 0;
            s_count = 0;
            ErrorCount = 0;
            ExceptionCount = 0;
        }
    }
}
#endif
```

- [ ] **Step 3: 컴파일 확인**

Unity 콘솔에 에러가 없는지 확인한다. 테스트 실행은 사람이 한다 — 여기서 기다리지 않는다.

---

### Task 3: BugReportWriter — 파일 쓰기

**Files:**
- Create: `Assets/Scripts/Utils/BugReport/BugReportWriter.cs`
- Test: `Assets/Tests/EditMode/BugReportWriterTests.cs`

**Interfaces:**
- Consumes: `BugReportData` (Task 1), `BugReportLogBuffer.Snapshot()` (Task 2)
- Produces:
  - `BugReportWriter.FolderName(DateTime when)` → `string` (예: `"2026-08-10_142301"`)
  - `BugReportWriter.RootFolder` → `string` (`persistentDataPath/BugReports`)
  - `BugReportWriter.Write(BugReportData data, byte[] pngBytes, string saveJson, string rootOverride, DateTime when)` → `BugReportWriteResult`
  - `BugReportWriteResult` (필드: `bool success`, `string folderPath`, `string error`)

`rootOverride`는 테스트가 임시 폴더에 쓰기 위한 것이다. `null`이면 `RootFolder`를 쓴다.
게임 상태를 전혀 모른다 — 이미 만들어진 데이터와 바이트 배열만 받는다.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
// @tags: bug-report, test, editmode, file-io
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.IO;
using NUnit.Framework;
using BugReport;

public class BugReportWriterTests
{
    private string _tempRoot;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BugReportTests_" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
    }

    [Test]
    public void FolderName_UsesSortableTimestamp()
    {
        var when = new DateTime(2026, 8, 10, 14, 23, 1);
        Assert.AreEqual("2026-08-10_142301", BugReportWriter.FolderName(when));
    }

    [Test]
    public void Write_CreatesAllFourFiles()
    {
        var data = new BugReportData { memo = "테스트 메모" };
        var when = new DateTime(2026, 8, 10, 14, 23, 1);

        var result = BugReportWriter.Write(data, new byte[] { 1, 2, 3 }, "{}", _tempRoot, when);

        Assert.IsTrue(result.success, result.error);
        string dir = Path.Combine(_tempRoot, "2026-08-10_142301");
        Assert.IsTrue(File.Exists(Path.Combine(dir, "screenshot.png")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "report.json")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "log.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "save.json")));
    }

    [Test]
    public void Write_PreservesKoreanMemoInJson()
    {
        var data = new BugReportData { memo = "벽이 안 막힘" };
        var when = new DateTime(2026, 8, 10, 14, 23, 1);

        BugReportWriter.Write(data, new byte[] { 1 }, "{}", _tempRoot, when);

        string json = File.ReadAllText(
            Path.Combine(_tempRoot, "2026-08-10_142301", "report.json"),
            System.Text.Encoding.UTF8);
        StringAssert.Contains("벽이 안 막힘", json);
    }

    [Test]
    public void Write_WithNullScreenshot_StillWritesOtherFiles()
    {
        var data = new BugReportData();
        var when = new DateTime(2026, 8, 10, 14, 23, 1);

        var result = BugReportWriter.Write(data, null, "{}", _tempRoot, when);

        Assert.IsTrue(result.success, "스크린샷 실패가 리포트 전체를 막으면 안 된다");
        string dir = Path.Combine(_tempRoot, "2026-08-10_142301");
        Assert.IsFalse(File.Exists(Path.Combine(dir, "screenshot.png")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "report.json")));
    }

    [Test]
    public void Write_TwiceInSameSecond_DoesNotOverwrite()
    {
        var when = new DateTime(2026, 8, 10, 14, 23, 1);

        var first = BugReportWriter.Write(new BugReportData { memo = "A" }, null, "{}", _tempRoot, when);
        var second = BugReportWriter.Write(new BugReportData { memo = "B" }, null, "{}", _tempRoot, when);

        Assert.IsTrue(second.success, second.error);
        Assert.AreNotEqual(first.folderPath, second.folderPath,
            "같은 초에 두 번 눌러도 앞 리포트를 덮으면 안 된다");
    }
}
#endif
```

- [ ] **Step 2: 구현 작성**

```csharp
// @tags: bug-report, qa, file-io, writer
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BugReport
{
    public struct BugReportWriteResult
    {
        public bool success;
        public string folderPath;
        public string error;
    }

    /// <summary>
    /// 리포트 폴더 생성 + 파일 쓰기만 담당. 게임 상태를 전혀 모른다.
    /// </summary>
    public static class BugReportWriter
    {
        public static string RootFolder =>
            Path.Combine(Application.persistentDataPath, "BugReports");

        /// <summary>정렬 가능한 타임스탬프 폴더명.</summary>
        public static string FolderName(DateTime when) => when.ToString("yyyy-MM-dd_HHmmss");

        public static BugReportWriteResult Write(
            BugReportData data, byte[] pngBytes, string saveJson,
            string rootOverride, DateTime when)
        {
            string root = string.IsNullOrEmpty(rootOverride) ? RootFolder : rootOverride;

            try
            {
                string dir = Path.Combine(root, FolderName(when));

                // 같은 초에 두 번 눌러도 앞 리포트를 덮지 않는다.
                if (Directory.Exists(dir))
                {
                    int suffix = 2;
                    string candidate;
                    do { candidate = $"{dir}_{suffix++}"; } while (Directory.Exists(candidate));
                    dir = candidate;
                }
                Directory.CreateDirectory(dir);

                // 스크린샷 실패가 리포트 전체를 막으면 안 된다 — 있으면 쓰고 없으면 넘어간다.
                if (pngBytes != null && pngBytes.Length > 0)
                    File.WriteAllBytes(Path.Combine(dir, "screenshot.png"), pngBytes);

                File.WriteAllText(Path.Combine(dir, "report.json"),
                    JsonUtility.ToJson(data, true), Encoding.UTF8);

                File.WriteAllText(Path.Combine(dir, "log.txt"),
                    FormatLog(), Encoding.UTF8);

                File.WriteAllText(Path.Combine(dir, "save.json"),
                    string.IsNullOrEmpty(saveJson) ? "{}" : saveJson, Encoding.UTF8);

                return new BugReportWriteResult { success = true, folderPath = dir };
            }
            catch (Exception e)
            {
                return new BugReportWriteResult { success = false, error = e.Message };
            }
        }

        private static string FormatLog()
        {
            var sb = new StringBuilder(8192);
            foreach (var entry in BugReportLogBuffer.Snapshot())
            {
                sb.Append('[').Append(entry.time.ToString("F2")).Append("] ")
                  .Append(entry.type).Append(": ").AppendLine(entry.condition);
                if (!string.IsNullOrEmpty(entry.stackTrace))
                    sb.AppendLine(entry.stackTrace);
            }
            return sb.ToString();
        }
    }
}
#endif
```

- [ ] **Step 3: 컴파일 확인**

---

### Task 4: BugReportCollector — 상태 수집

**Files:**
- Create: `Assets/Scripts/Utils/BugReport/BugReportCollector.cs`

**Interfaces:**
- Consumes: `BugReportData` 등 (Task 1), `BugReportLogBuffer.ErrorCount`/`ExceptionCount` (Task 2)
- Produces:
  - `BugReportCollector.Collect()` → `BugReportData`
  - `BugReportCollector.CollectSaveJson()` → `string`
  - `BugReportCollector.ExtraContext` (`static event Action<Dictionary<string, string>>`)

**항목별 개별 try-catch가 이 파일의 핵심이다.** 매니저 하나가 null이거나 예외를 던져도 그 필드만 `<unavailable>`로 남고 나머지는 정상 수집된다. 버그 리포트가 버그 때문에 실패하면 안 된다.

- [ ] **Step 1: 구현 작성**

```csharp
// @tags: bug-report, qa, state, collector
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BugReport
{
    /// <summary>
    /// 기존 싱글톤에서 상태를 읽어 BugReportData를 만든다. 기존 파일을 수정하지 않는다.
    ///
    /// ⚠ 모든 항목은 개별 try-catch로 감싼다. 매니저 하나가 죽어도
    ///   그 필드만 <unavailable>로 남고 리포트는 저장돼야 한다.
    /// </summary>
    public static class BugReportCollector
    {
        /// <summary>
        /// 확장 지점. 새 시스템이 자기 값을 리포트에 넣고 싶으면 한 줄 구독하면 된다.
        /// 인터페이스 구현도, 이 파일 수정도 필요 없다.
        /// </summary>
        public static event Action<Dictionary<string, string>> ExtraContext;

        public static BugReportData Collect()
        {
            var data = new BugReportData();
            CollectMeta(data.meta);
            CollectPlayer(data.player);
            CollectWorld(data.world);
            Try(() => data.ui = UIStateManager.Instance.CurrentState.ToString());
            CollectExtra(data.extra);
            return data;
        }

        /// <summary>세이브 스냅샷. report.json에 끼워 넣으면 이스케이프로 읽을 수 없게 되므로 별도 파일로 뺀다.</summary>
        public static string CollectSaveJson()
        {
            try
            {
                var pd = SaveManager.Instance != null ? SaveManager.Instance.playerData : null;
                return pd != null ? JsonUtility.ToJson(pd, true) : "{}";
            }
            catch (Exception e) { return "{ \"error\": \"" + e.Message + "\" }"; }
        }

        private static void CollectMeta(BugReportMeta m)
        {
            m.localTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            m.appVersion = Application.version;
            m.unityVersion = Application.unityVersion;
            m.platform = Application.platform.ToString();
            m.resolution = $"{Screen.width}x{Screen.height} (fullscreen={Screen.fullScreen})";
            m.realtimeSinceStartup = Time.realtimeSinceStartup;
            m.totalMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
            m.sessionErrorCount = BugReportLogBuffer.ErrorCount;
            m.sessionExceptionCount = BugReportLogBuffer.ExceptionCount;
            Try(() => m.sceneName = SceneManager.GetActiveScene().name);
        }

        private static void CollectPlayer(BugReportPlayer p)
        {
            Transform player = null;
            Try(() =>
            {
                var stat = UnityEngine.Object.FindFirstObjectByType<PlayerStat>();
                if (stat == null) return;
                player = stat.transform;
                p.gold = stat.Gold.ToString();
                p.stamina = stat.CurrentStamina.ToString("F1");
                p.miningLevel = stat.MiningLevel.ToString();
            });

            Try(() =>
            {
                if (player == null) return;
                Vector3 pos = player.position;
                p.worldPos = $"{pos.x:F2}, {pos.y:F2}";
                // 좌표 변환은 반드시 ChunkCoords를 통한다 (단일 진실 공급원).
                Vector2Int c = ChunkCoords.ToChunk(pos);
                p.chunkCoord = $"{c.x}, {c.y}";
            });

            Try(() =>
            {
                if (player == null) return;
                var rb = player.GetComponent<Rigidbody2D>();
                if (rb != null) p.velocity = $"{rb.linearVelocity.x:F2}, {rb.linearVelocity.y:F2}";
            });

            Try(() =>
            {
                var enc = UnityEngine.Object.FindFirstObjectByType<EncumbranceController>();
                if (enc != null)
                    p.weight = $"{enc.TotalWeight:F1} / 임계 {enc.EncumbranceThreshold:F1}";
            });
        }

        private static void CollectWorld(BugReportWorld w)
        {
            Try(() => w.inDungeon = DungeonOverlayController.IsInDungeon.ToString());

            Try(() =>
            {
                var dc = UnityEngine.Object.FindFirstObjectByType<DayCycleManager>();
                if (dc == null) return;
                w.day = dc.CurrentDay.ToString();
                w.timeOfDay = dc.CurrentTime.ToString();
            });

            Try(() =>
            {
                var stat = UnityEngine.Object.FindFirstObjectByType<PlayerStat>();
                if (stat == null || TileDataManager.Instance == null) return;
                // FootstepPlayer.CurrentLayer()와 같은 방식 — 깊이로 층을 얻는다.
                int chunkY = ChunkCoords.ToChunk(stat.transform.position).y;
                w.layer = TileDataManager.Instance.GetTileTypeAtDepth(chunkY).ToString();
            });

            Try(() =>
            {
                var map = InfinityMapManager.Instance;
                if (map == null) return;
                foreach (var coord in map.GetActiveChunkCoords())
                    w.loadedChunks.Add($"{coord.x},{coord.y}");
                w.loadedChunkCount = w.loadedChunks.Count;
            });
        }

        private static void CollectExtra(List<string> into)
        {
            var handler = ExtraContext;
            if (handler == null) return;

            // 구독자가 던져도 리포트를 깨뜨리지 못하게 한다.
            Try(() =>
            {
                var bag = new Dictionary<string, string>();
                handler(bag);
                foreach (var kv in bag) into.Add($"{kv.Key}={kv.Value}");
            });
        }

        /// <summary>한 항목 실패가 리포트 전체를 막지 않게 하는 래퍼.</summary>
        private static void Try(Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogWarning($"[BugReport] 수집 실패: {e.Message}"); }
        }
    }
}
#endif
```

- [ ] **Step 2: 타입 이름 검증**

다음이 실제로 존재하고 접근 가능한지 컴파일로 확인한다. 하나라도 다르면 **그 항목만** 고친다 (`Try` 블록 단위로 격리돼 있다):

- `PlayerStat.Gold` / `.CurrentStamina` / `.MiningLevel`
- `EncumbranceController.TotalWeight` / `.EncumbranceThreshold`
- `DayCycleManager.CurrentDay` / `.CurrentTime`
- `TileDataManager.Instance.GetTileTypeAtDepth(int)`
- `InfinityMapManager.Instance.GetActiveChunkCoords()`
- `UIStateManager.Instance.CurrentState`
- `DungeonOverlayController.IsInDungeon`
- `SaveManager.Instance.playerData`

`PlayerStat` 등이 네임스페이스 안에 있다면 `using`을 추가한다.

- [ ] **Step 3: 컴파일 확인**

---

### Task 5: BugReportOverlayUI — 메모 입력 오버레이

**Files:**
- Create: `Assets/Scripts/Utils/BugReport/BugReportOverlayUI.cs`

**Interfaces:**
- Consumes: `CodeUI` 키트 (`Assets/Scripts/UI/Core/CodeUIKit.cs`)
- Produces: `BugReportOverlayUI.Show(Texture2D screenshot, Action<string> onSubmit, Action onCancel)`

`CodeUI`에는 InputField 헬퍼가 없으므로 여기서 직접 만든다. `TMP_InputField`는 뷰포트와 텍스트 컴포넌트를 손으로 연결해야 동작한다.

**IMGUI를 쓰지 않는 이유:** 빌드에서 한글 IME 입력이 깨진다. 메모를 한글로 쓸 것이므로 `TMP_InputField`가 필수다.

- [ ] **Step 1: 구현 작성**

```csharp
// @tags: bug-report, qa, ui, overlay, code-generated
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BugReport
{
    /// <summary>
    /// 메모 입력 오버레이. CodeUI 키트로 전부 코드 생성 — 씬 세팅 불필요.
    /// 스크린샷과 상태는 이 오버레이가 뜨기 전에 이미 확정돼 있다.
    /// </summary>
    public class BugReportOverlayUI : MonoBehaviour
    {
        private TMP_InputField _input;
        private Action<string> _onSubmit;
        private Action _onCancel;
        private bool _closed;

        public static BugReportOverlayUI Show(
            Texture2D screenshot, Action<string> onSubmit, Action onCancel)
        {
            CodeUI.EnsureEventSystem();

            var root = new GameObject("BugReportOverlay",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;   // 다른 모든 UI 위

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var ui = root.AddComponent<BugReportOverlayUI>();
            ui._onSubmit = onSubmit;
            ui._onCancel = onCancel;
            ui.Build(root.transform, screenshot);
            return ui;
        }

        private void Build(Transform parent, Texture2D screenshot)
        {
            // 암막 — 뒤쪽 클릭을 전부 먹는다.
            var dim = CodeUI.CreateImage(parent, "Dim", new Color(0f, 0f, 0f, 0.75f));
            CodeUI.StretchFull(dim.rectTransform);

            var panel = CodeUI.CreateImage(parent, "Panel", CodeUI.PanelBg);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(900f, 620f);

            var title = CodeUI.CreateText(panel.transform, "Title", 34f, FontStyles.Bold);
            title.text = "버그 리포트";
            title.color = CodeUI.LabelColor;
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(30f, -80f); trt.offsetMax = new Vector2(-30f, -20f);

            // 스크린샷 미리보기 — 무엇이 찍혔는지 보고 메모를 쓴다.
            var preview = CodeUI.CreateImage(panel.transform, "Preview", Color.white);
            var vrt = preview.rectTransform;
            vrt.anchorMin = new Vector2(0f, 1f); vrt.anchorMax = new Vector2(1f, 1f);
            vrt.pivot = new Vector2(0.5f, 1f);
            vrt.offsetMin = new Vector2(30f, -420f); vrt.offsetMax = new Vector2(-30f, -90f);
            preview.preserveAspect = true;
            if (screenshot != null)
                preview.sprite = Sprite.Create(screenshot,
                    new Rect(0, 0, screenshot.width, screenshot.height), new Vector2(0.5f, 0.5f));

            BuildInput(panel.transform);

            var save = CodeUI.CreateTextButton(panel.transform, "Save",
                CodeUI.PositiveColor, Color.white, 26f, () => Close(true));
            var srt = save.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 0f);
            srt.pivot = new Vector2(1f, 0f);
            srt.anchoredPosition = new Vector2(-30f, 25f);
            srt.sizeDelta = new Vector2(180f, 60f);
            save.GetComponentInChildren<TextMeshProUGUI>().text = "저장 (Enter)";

            var cancel = CodeUI.CreateTextButton(panel.transform, "Cancel",
                CodeUI.NeutralBg, Color.white, 26f, () => Close(false));
            var crt = cancel.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-230f, 25f);
            crt.sizeDelta = new Vector2(180f, 60f);
            cancel.GetComponentInChildren<TextMeshProUGUI>().text = "취소 (ESC)";
        }

        /// <summary>
        /// TMP_InputField를 코드로 조립. 뷰포트·텍스트 컴포넌트를 손으로 연결해야 동작한다.
        /// </summary>
        private void BuildInput(Transform panel)
        {
            var box = CodeUI.CreateImage(panel, "InputBox", CodeUI.BoxBg);
            var brt = box.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(30f, 100f); brt.offsetMax = new Vector2(-30f, 190f);

            var viewport = CodeUI.CreateRect(box.transform, "TextArea");
            CodeUI.StretchFull(viewport);
            viewport.offsetMin = new Vector2(14f, 8f);
            viewport.offsetMax = new Vector2(-14f, -8f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = CodeUI.CreateText(viewport, "Text", 26f, FontStyles.Normal);
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.TopLeft;
            CodeUI.StretchFull(text.rectTransform);

            var placeholder = CodeUI.CreateText(viewport, "Placeholder", 26f, FontStyles.Italic);
            placeholder.text = "무슨 일이 있었나요? (예: 벽이 안 막힘)";
            placeholder.color = CodeUI.MutedColor;
            placeholder.alignment = TextAlignmentOptions.TopLeft;
            CodeUI.StretchFull(placeholder.rectTransform);

            _input = box.gameObject.AddComponent<TMP_InputField>();
            _input.textViewport = viewport;
            _input.textComponent = text;
            _input.placeholder = placeholder;
            _input.lineType = TMP_InputField.LineType.MultiLineNewline;
            _input.characterLimit = 500;
            _input.ActivateInputField();
        }

        private void Update()
        {
            if (_closed) return;

            // Enter 저장 / ESC 취소. 프로젝트 UI 확인 키는 Space지만
            // 텍스트 입력 중에는 Space가 공백 문자라 여기만 예외다.
            if (Input.GetKeyDown(KeyCode.Escape)) Close(false);
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                    Close(true);
            }
        }

        private void Close(bool submit)
        {
            if (_closed) return;
            _closed = true;

            string memo = _input != null ? _input.text : "";
            var onSubmit = _onSubmit;
            var onCancel = _onCancel;

            Destroy(gameObject);

            if (submit) onSubmit?.Invoke(memo);
            else onCancel?.Invoke();
        }
    }
}
#endif
```

- [ ] **Step 2: `CodeUI` 시그니처 대조**

`Assets/Scripts/UI/Core/CodeUIKit.cs`를 열어 실제 시그니처와 맞는지 확인하고 호출부를 맞춘다:

- `CodeUI.CreateImage(Transform, string, Color, ...)` — 뒤에 optional 파라미터가 있다
- `CodeUI.CreateText(Transform, string, float, FontStyles)`
- `CodeUI.CreateTextButton(Transform, string, Color bg, Color fg, float fontSize, Action onClick, ...)`
- `CodeUI.CreateRect(Transform, string)` → `RectTransform`
- `CodeUI.StretchFull(RectTransform)`
- `CodeUI.EnsureEventSystem()`

- [ ] **Step 3: 컴파일 확인**

---

### Task 6: BugReportSystem — 진입점

**Files:**
- Create: `Assets/Scripts/Utils/BugReport/BugReportSystem.cs`

**Interfaces:**
- Consumes: 앞선 다섯 파일 전부
- Produces: `BugReportSystem.Capture()` — 코드에서 직접 리포트를 띄우고 싶을 때

`SoundManager`와 같은 `[RuntimeInitializeOnLoadMethod]` 패턴 — **씬 배치 불필요**.

**⚠ 이 태스크의 핵심은 순서다.** 스크린샷과 상태 수집이 오버레이보다 **먼저** 끝나야 한다. 뒤집히면 리포트에 자기 UI가 찍히고, 일시정지 이후의 값이 수집되어 "버그가 난 그 시점"이 아니게 된다.

- [ ] **Step 1: 구현 작성**

```csharp
// @tags: bug-report, qa, entry-point, hotkey, screenshot
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.Collections;
using UnityEngine;

namespace BugReport
{
    /// <summary>
    /// 버그 리포트 진입점. RuntimeInitializeOnLoadMethod로 자동 생성 — 씬 배치 불필요.
    /// 로그 버퍼가 씬 전환에서 끊기면 안 되므로 DontDestroyOnLoad로 유지된다.
    /// </summary>
    public class BugReportSystem : MonoBehaviour
    {
        public const KeyCode HotKey = KeyCode.F12;

        private static BugReportSystem s_instance;
        private bool _busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (s_instance != null) return;

            BugReportLogBuffer.Install();   // 첫 프레임 로그부터 잡는다

            var go = new GameObject("[BugReportSystem]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<BugReportSystem>();
        }

        /// <summary>코드에서 직접 리포트를 띄우고 싶을 때.</summary>
        public static void Capture()
        {
            if (s_instance == null || s_instance._busy) return;
            s_instance.StartCoroutine(s_instance.CaptureRoutine());
        }

        private void Update()
        {
            if (!_busy && Input.GetKeyDown(HotKey)) Capture();
        }

        private IEnumerator CaptureRoutine()
        {
            _busy = true;

            // ── 1. 스크린샷: 오버레이를 띄우기 전에 찍는다.
            //    WaitForEndOfFrame 없이 호출하면 렌더가 끝나지 않아 실패한다.
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            byte[] png = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                png = shot.EncodeToPNG();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BugReport] 스크린샷 실패: {e.Message}");
            }

            // ── 2. 상태 수집: 여전히 오버레이 전. 여기서 값이 확정된다.
            BugReportData data = BugReportCollector.Collect();
            string saveJson = BugReportCollector.CollectSaveJson();
            DateTime when = DateTime.Now;

            // ── 3. 일시정지. PauseOverlayUI와 같은 방식 —
            //    1f로 하드코딩하지 않는다. 일시정지 메뉴 위에서 F12를 눌렀을 때
            //    게임이 멋대로 재개되면 안 된다.
            float prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            // 메모 입력 중 M(지도)·I(인벤토리) 같은 전역 단축키가 발동하지 않게 막는다.
            // Input.GetKeyDown 핸들러는 입력 포커스와 무관하게 돌기 때문이다.
            object prevUiState = null;
            try
            {
                if (UIStateManager.Instance != null)
                {
                    prevUiState = UIStateManager.Instance.CurrentState;
                    UIStateManager.Instance.SetState(UIState.Popup);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[BugReport] UI 상태 차단 실패: {e.Message}"); }

            // ── 4. 오버레이.
            BugReportOverlayUI.Show(shot,
                memo => Finish(data, png, saveJson, when, shot, prevTimeScale, prevUiState, memo),
                () => Finish(data, null, null, when, shot, prevTimeScale, prevUiState, null));
        }

        private void Finish(BugReportData data, byte[] png, string saveJson, DateTime when,
                            Texture2D shot, float prevTimeScale, object prevUiState, string memo)
        {
            Restore(prevTimeScale, prevUiState);

            if (memo != null)   // null이면 취소
            {
                data.memo = memo;
                var result = BugReportWriter.Write(data, png, saveJson, null, when);

                if (result.success) Debug.Log($"[BugReport] 저장됨: {result.folderPath}");
                else Debug.LogError($"[BugReport] 저장 실패: {result.error}");
            }

            if (shot != null) Destroy(shot);   // CaptureScreenshotAsTexture는 수동 해제
            _busy = false;
        }

        private void Restore(float prevTimeScale, object prevUiState)
        {
            Time.timeScale = prevTimeScale;
            try
            {
                if (UIStateManager.Instance != null && prevUiState is UIState state)
                    UIStateManager.Instance.SetState(state);
            }
            catch (Exception e) { Debug.LogWarning($"[BugReport] UI 상태 복원 실패: {e.Message}"); }
        }
    }
}
#endif
```

- [ ] **Step 2: `UIState.Popup` 부수효과 확인**

`Assets/Scripts/UI/NPC/UIStateManager.cs`를 열어 `SetState(UIState.Popup)` 진입/이탈이 무엇을 건드리는지 확인한다. 플레이어 입력 차단·커서 표시는 이 용도에 맞다. 카메라 이동이나 플레이어 위치를 바꾸는 부수효과가 있다면 **이 방식을 버리고** 오버레이가 살아있는 동안 `true`인 `BugReportSystem.IsOpen` static 플래그로 대체하고, 필요 지점에서만 이 플래그를 확인하게 한다.

- [ ] **Step 3: 컴파일 확인**

---

### Task 7: 수동 검증

**Files:** 없음 (플레이 모드 확인)

자동 테스트로는 잡히지 않는 것들이다. 순서대로 확인한다.

- [ ] **Step 1: 기본 동작**

플레이 모드 진입 → 지하로 이동 → F12.

- 게임이 멈추고 오버레이가 뜬다
- **미리보기에 버그 리포트 UI가 찍혀 있지 않다** ← 순서가 맞다는 증거
- 한글 메모가 입력된다 (IME 확인)
- Enter → 콘솔에 `[BugReport] 저장됨: ...` 경로가 찍힌다

- [ ] **Step 2: 저장 결과 확인**

`%USERPROFILE%\AppData\LocalLow\<회사>\<게임>\BugReports\<타임스탬프>\` 를 연다.

- 파일 4개가 있다
- `screenshot.png`가 실제 게임 화면이다
- `report.json`의 `player.chunkCoord`가 실제 있던 청크와 맞다
- `report.json`의 `world.loadedChunks`가 비어있지 않다
- 한글 메모가 깨지지 않았다
- `log.txt`에 최근 로그가 있다

- [ ] **Step 3: 취소 경로**

F12 → ESC. 게임이 재개되고 폴더가 **생기지 않는다.**

- [ ] **Step 4: 일시정지 위에서 호출**

일시정지 메뉴를 연 상태에서 F12 → 저장 → **일시정지가 유지된다.** 게임이 재개되면 `prevTimeScale` 복원이 깨진 것이다.

- [ ] **Step 5: 매니저 없는 씬**

타이틀·마켓 씬처럼 `InfinityMapManager`가 없는 곳에서 F12.

리포트가 **정상 저장되고** 지형 관련 필드만 `<unavailable>`이어야 한다. 저장이 실패하면 `Try` 격리가 깨진 것이다.

- [ ] **Step 6: 연속 호출**

F12 저장 직후 다시 F12. 같은 초에 두 번 눌러도 앞 리포트가 덮이지 않고 `_2` 접미사 폴더가 생긴다.

---

## 완료 후

- `CLAUDE.md` 핵심 파일 표에 `BugReportSystem.cs` 한 줄 추가
- 팀에 전달: **F12 = 버그 리포트**, 폴더 경로, 공유할 때는 폴더를 통째로 압축할 것
