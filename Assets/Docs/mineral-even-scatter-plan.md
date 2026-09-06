# 광물 균등 배치 — 구현 계획

> 설계 문서: `Assets/Docs/mineral-even-scatter.md` (먼저 읽을 것)
> 선행 작업: `Assets/Docs/mineral-density-redesign.md` (개수 노브 — 이미 구현 완료)

**목표:** 광물 배치를 순수 균등난수에서 지터 격자(stratified sampling)로 바꿔, 청크 안 모든 광물이 종류 구분 없이 고르게 깔리게 한다.

**접근:** `GenerateMinerals`를 **집계 → 격자 → 배치** 3단계로 끊는다. rule별 독립 배치를 버리고 청크 전체가 하나의 격자를 나눠 쓴다. 3개 태스크로 나누되 **Task 1·2는 동작을 바꾸지 않고**, Task 3에서 한 번에 전환한다.

**기술 스택:** Unity 2D / C# / `JsonUtility` / NUnit(EditMode)

---

## 전역 제약 — 모든 태스크에 적용

- **버전 관리는 UVCS다.** `git add` / `git commit` 등 git 명령을 쓰지 않는다. "체크포인트"는 UVCS 체크인 시점 표시일 뿐이다.
- **Unity Test Runner 실행은 사람이 한다.** 에이전트는 테스트 파일 작성까지만 하고, 실행 결과를 게이트로 삼아 멈추지 않는다.
- **검증 조건을 바꾸지 않는다.** `IsWellSupported`(반경 5px 8이웃 전부 솔리드) / `IsInExcludedArea` / `IsGroundPixel`은 한 줄도 수정 대상이 아니다. 바뀌는 것은 "어느 좌표를 후보로 뽑는가"뿐이다.
- **개수 계산을 바꾸지 않는다.** `MineralDensity.ExpectedCount` / `.ProbabilisticRound`와 깊이 곡선(Rare `0→1`, Common `1→0.5`)은 그대로다.
- **난수는 반드시 청크 시드 `prng`(`System.Random`)를 쓴다.** `UnityEngine.Random`을 스폰 경로에 쓰면 청크 결정성이 깨진다.
- **rule마다 난수 2개를 무조건 소비한다.** 깊이 범위를 벗어난 rule에서 건너뛰면 난수 스트림이 깊이별로 어긋난다.
- **청크 로드 경로에 GC 할당을 넣지 않는다.** 배열은 정적 재사용 버퍼로 둔다 (`Assets/Docs/performance/chunk-load-gc.md`, `performance/README.md` §1.3·§3.5).
- 새 파일 최상단에는 프로젝트 관례대로 `// @tags: ...` 주석을 단다.

---

## 파일 구조

| 파일 | 상태 | 책임 |
|---|---|---|
| `Assets/Scripts/Gameplay/Terrain/Tiles/MineralScatter.cs` | **신규** | `ScatterGrid`(격자 계산·지터 점) + `MineralScatter.ShuffleInPlace`. Unity 비의존 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/MineralSpawnSettings.cs` | **신규** | 스폰 튜닝 값 묶음 (밀도·지터·로그) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/MineralGenerator.cs` | 수정 | 3단계 구조로 교체, 정적 버퍼 |
| `Assets/Scripts/_Core/Data/TileDataModels.cs` | 수정 | `TileDatabaseJson.mineralScatterJitter` |
| `Assets/Scripts/_Core/Managers/TileDataManager.cs` | 수정 | `MineralScatterJitter` 노출 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/Decorators/MineralDecorator.cs` | 수정 | `MineralSpawnSettings` 구성 |
| `Assets/StreamingAssets/tileData.json` | 수정 | `"mineralScatterJitter": 1.0` |
| `Assets/Tests/EditMode/MineralScatterTests.cs` | **신규** | 격자·지터·셔플 테스트 |

`ScatterGrid`를 별도 파일로 빼는 이유는 두 가지다. `MineralGenerator`는 `TerrainChunk`·`NativeArray`에 묶여 EditMode에서 못 돌리므로 계산만 분리해야 테스트가 되고(`MineralDensity`와 같은 패턴), 나중에 Poisson disk로 갈아탈 때 **교체 지점이 `PointAt` 하나로 좁혀진다**(설계 §2-3).

---

## Task 1: `ScatterGrid` + 셔플 (순수 로직)

**파일:**
- 생성: `Assets/Scripts/Gameplay/Terrain/Tiles/MineralScatter.cs`
- 생성: `Assets/Tests/EditMode/MineralScatterTests.cs`

**인터페이스:**
- 소비: 없음 (Unity 타입도 안 쓴다)
- 산출:
  - `static ScatterGrid ScatterGrid.Create(int count, int x0, int y0, int width, int height)`
  - `int ScatterGrid.CellCount { get; }` — `Cols * Rows`
  - `void ScatterGrid.PointAt(int cellIndex, double rollX, double rollY, float jitter, out int x, out int y)`
  - `static void MineralScatter.ShuffleInPlace(int[] buffer, int length, System.Random prng)`

아무 데도 연결하지 않으므로 **게임 동작은 완전히 동일하다.**

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/MineralScatterTests.cs`

```csharp
using NUnit.Framework;

public class MineralScatterTests
{
    // ─── ScatterGrid.Create ────────────────────────────────────────

    [Test]
    public void Grid_HasEnoughCellsForRequestedCount()
    {
        foreach (int count in new[] { 1, 2, 3, 5, 17, 170, 510, 9604 })
        {
            var g = ScatterGrid.Create(count, 10, 10, 980, 980);
            Assert.GreaterOrEqual(g.CellCount, count, $"count={count}");
        }
    }

    [Test]
    public void Grid_DoesNotOverAllocateCells()
    {
        // cols x rows는 count를 덮되 필요 이상으로 크면 안 된다.
        // rows = ceil(count/cols) 이므로 cols*rows < count + cols 가 성립해야 한다.
        foreach (int count in new[] { 3, 5, 17, 170, 510 })
        {
            var g = ScatterGrid.Create(count, 10, 10, 980, 980);
            Assert.Less(g.CellCount, count + g.Cols, $"count={count} cols={g.Cols} rows={g.Rows}");
        }
    }

    [Test]
    public void SquareRegion_GivesSquareGrid()
    {
        var g = ScatterGrid.Create(170, 10, 10, 980, 980);
        Assert.AreEqual(14, g.Cols);
        Assert.AreEqual(14, g.Rows);
    }

    [Test]
    public void WideRegion_GivesMoreColumnsThanRows()
    {
        var g = ScatterGrid.Create(100, 0, 0, 4000, 1000);
        Assert.Greater(g.Cols, g.Rows);
    }

    [Test]
    public void DegenerateInputs_GiveEmptyGrid()
    {
        Assert.AreEqual(0, ScatterGrid.Create(0, 0, 0, 980, 980).CellCount);
        Assert.AreEqual(0, ScatterGrid.Create(-5, 0, 0, 980, 980).CellCount);
        Assert.AreEqual(0, ScatterGrid.Create(10, 0, 0, 0, 980).CellCount);
        Assert.AreEqual(0, ScatterGrid.Create(10, 0, 0, 980, 0).CellCount);
    }

    [Test]
    public void TinyRegion_ClampsGridToPixelSize()
    {
        // 셀 너비가 1px 미만이 되면 안 된다. 이 경우 CellCount < count가 되며,
        // 호출측이 pointIndex >= CellCount로 중단한다(설계 §3-6).
        var g = ScatterGrid.Create(100, 0, 0, 5, 5);
        Assert.LessOrEqual(g.Cols, 5);
        Assert.LessOrEqual(g.Rows, 5);
    }

    // ─── ScatterGrid.PointAt ───────────────────────────────────────

    [Test]
    public void ZeroJitter_LandsOnCellCenter()
    {
        // 4x4 격자, 영역 0..400 → 셀 100px, 첫 셀 중심 = 50
        var g = ScatterGrid.Create(16, 0, 0, 400, 400);
        Assert.AreEqual(4, g.Cols);

        g.PointAt(0, 0.0, 0.0, 0f, out int x0, out int y0);
        Assert.AreEqual(50, x0);
        Assert.AreEqual(50, y0);

        // roll을 뭘 주든 jitter 0이면 중심이다
        g.PointAt(0, 0.99, 0.99, 0f, out int x1, out int y1);
        Assert.AreEqual(50, x1);
        Assert.AreEqual(50, y1);

        // 인덱스 5 = (col 1, row 1) → 중심 (150, 150)
        g.PointAt(5, 0.5, 0.5, 0f, out int x2, out int y2);
        Assert.AreEqual(150, x2);
        Assert.AreEqual(150, y2);
    }

    [Test]
    public void FullJitter_SpansTheWholeCell()
    {
        var g = ScatterGrid.Create(16, 0, 0, 400, 400);

        g.PointAt(0, 0.0, 0.0, 1f, out int lowX, out int lowY);
        g.PointAt(0, 1.0, 1.0, 1f, out int highX, out int highY);

        Assert.AreEqual(0, lowX);    // 셀 왼쪽 끝
        Assert.AreEqual(0, lowY);
        Assert.AreEqual(99, highX);  // 셀 오른쪽 끝(마지막 픽셀)
        Assert.AreEqual(99, highY);
    }

    [Test]
    public void EveryCellStaysInsideItsOwnColumnAndRow()
    {
        // 셀 배타성: 서로 다른 셀의 점은 서로 다른 셀 영역 안에 있어야 한다.
        // 이게 깨지면 "셀당 1개"가 무의미해지고 뭉침이 되살아난다.
        var g = ScatterGrid.Create(16, 10, 10, 400, 400);
        double cellW = 400.0 / g.Cols;
        double cellH = 400.0 / g.Rows;

        for (int cell = 0; cell < g.CellCount; cell++)
        {
            int cx = cell % g.Cols;
            int cy = cell / g.Cols;

            foreach (double roll in new[] { 0.0, 0.5, 1.0 })
            {
                g.PointAt(cell, roll, roll, 1f, out int x, out int y);

                Assert.GreaterOrEqual(x, 10 + cx * cellW - 1e-6, $"cell={cell}");
                Assert.Less(x, 10 + (cx + 1) * cellW, $"cell={cell}");
                Assert.GreaterOrEqual(y, 10 + cy * cellH - 1e-6, $"cell={cell}");
                Assert.Less(y, 10 + (cy + 1) * cellH, $"cell={cell}");
            }
        }
    }

    [Test]
    public void PointNeverEscapesTheRegion()
    {
        var g = ScatterGrid.Create(170, 10, 10, 980, 980);

        for (int cell = 0; cell < g.CellCount; cell++)
        {
            foreach (float jitter in new[] { 0f, 0.5f, 1f, 2f, -1f }) // 범위 밖 jitter도 클램프돼야 함
            {
                g.PointAt(cell, 0.0, 0.0, jitter, out int xLo, out int yLo);
                g.PointAt(cell, 1.0, 1.0, jitter, out int xHi, out int yHi);

                Assert.GreaterOrEqual(xLo, 10);
                Assert.GreaterOrEqual(yLo, 10);
                Assert.LessOrEqual(xHi, 10 + 980 - 1);
                Assert.LessOrEqual(yHi, 10 + 980 - 1);
            }
        }
    }

    [Test]
    public void OutOfRangeCellIndex_IsClampedNotCrashing()
    {
        var g = ScatterGrid.Create(16, 0, 0, 400, 400);
        Assert.DoesNotThrow(() => g.PointAt(-1, 0.5, 0.5, 1f, out _, out _));
        Assert.DoesNotThrow(() => g.PointAt(9999, 0.5, 0.5, 1f, out _, out _));
    }

    [Test]
    public void EmptyGrid_PointAtDoesNotCrash()
    {
        var g = ScatterGrid.Create(0, 7, 9, 980, 980);
        g.PointAt(0, 0.5, 0.5, 1f, out int x, out int y);
        Assert.AreEqual(7, x);
        Assert.AreEqual(9, y);
    }

    // ─── MineralScatter.ShuffleInPlace ─────────────────────────────

    [Test]
    public void Shuffle_KeepsEveryElementExactlyOnce()
    {
        const int N = 200;
        var buf = new int[N];
        for (int i = 0; i < N; i++) buf[i] = i;

        MineralScatter.ShuffleInPlace(buf, N, new System.Random(1234));

        var seen = new bool[N];
        foreach (int v in buf)
        {
            Assert.IsFalse(seen[v], $"{v} 중복");
            seen[v] = true;
        }
    }

    [Test]
    public void Shuffle_ActuallyReorders()
    {
        const int N = 200;
        var buf = new int[N];
        for (int i = 0; i < N; i++) buf[i] = i;

        MineralScatter.ShuffleInPlace(buf, N, new System.Random(1234));

        int samePosition = 0;
        for (int i = 0; i < N; i++) if (buf[i] == i) samePosition++;
        Assert.Less(samePosition, N / 4, "거의 안 섞였다");
    }

    [Test]
    public void Shuffle_IsDeterministicForSameSeed()
    {
        const int N = 50;
        var a = new int[N];
        var b = new int[N];
        for (int i = 0; i < N; i++) { a[i] = i; b[i] = i; }

        MineralScatter.ShuffleInPlace(a, N, new System.Random(777));
        MineralScatter.ShuffleInPlace(b, N, new System.Random(777));

        CollectionAssert.AreEqual(a, b);
    }

    [Test]
    public void Shuffle_OnlyTouchesTheRequestedPrefix()
    {
        var buf = new int[10];
        for (int i = 0; i < 10; i++) buf[i] = i;

        MineralScatter.ShuffleInPlace(buf, 5, new System.Random(42));

        // 뒤쪽 5개는 그대로여야 한다
        for (int i = 5; i < 10; i++) Assert.AreEqual(i, buf[i]);
    }

    [Test]
    public void Shuffle_HandlesBadInput()
    {
        Assert.DoesNotThrow(() => MineralScatter.ShuffleInPlace(null, 5, new System.Random(1)));
        Assert.DoesNotThrow(() => MineralScatter.ShuffleInPlace(new int[3], 999, new System.Random(1)));
        Assert.DoesNotThrow(() => MineralScatter.ShuffleInPlace(new int[3], 3, null));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Unity Test Runner (EditMode) → `MineralScatterTests`
기대: **컴파일 실패** — `ScatterGrid` / `MineralScatter` 타입이 없음.

- [ ] **Step 3: `MineralScatter.cs` 구현**

`Assets/Scripts/Gameplay/Terrain/Tiles/MineralScatter.cs` 신규 생성

```csharp
// @tags: mineral, scatter, distribution, grid, spawn, pure-logic
/// <summary>
/// 광물 균등 배치용 지터 격자(stratified sampling).
/// 영역을 목표 개수만큼의 셀로 나누고 셀당 점 하나를 뽑으면 구조적으로 뭉칠 수 없다.
/// Unity 비의존 순수 로직 — EditMode 테스트 대상. 난수는 전부 호출측이 주입한다.
/// 배경: Assets/Docs/mineral-even-scatter.md
/// </summary>
public struct ScatterGrid
{
    public int Cols;
    public int Rows;
    public int X0;
    public int Y0;
    public int Width;
    public int Height;

    public int CellCount => Cols * Rows;

    /// <summary>
    /// count개 이상을 담는 최소 격자. 셀이 정사각형에 가깝도록 영역 종횡비를 따라간다.
    /// count가 0 이하이거나 영역이 비면 CellCount 0인 격자를 준다.
    ///
    /// 주의: 영역이 아주 좁으면(셀 1px 미만) 클램프가 걸려 CellCount < count가 될 수 있다.
    /// 호출측이 pointIndex >= CellCount로 중단해야 한다.
    /// </summary>
    public static ScatterGrid Create(int count, int x0, int y0, int width, int height)
    {
        var g = new ScatterGrid
        {
            X0 = x0, Y0 = y0, Width = width, Height = height, Cols = 0, Rows = 0
        };
        if (count <= 0 || width <= 0 || height <= 0) return g;

        int cols = (int)System.Math.Ceiling(System.Math.Sqrt((double)count * width / height));
        if (cols < 1) cols = 1;
        if (cols > width) cols = width;        // 셀 너비 1px 미만 방지

        int rows = (count + cols - 1) / cols;  // ceil(count / cols)
        if (rows < 1) rows = 1;
        if (rows > height) rows = height;

        g.Cols = cols;
        g.Rows = rows;
        return g;
    }

    /// <summary>
    /// 셀 중심 기준으로 지터를 적용한 점. jitter 0 = 정확히 셀 중심, 1 = 셀 전체.
    /// rollX/rollY는 [0,1] 균등 난수. 결과는 항상 영역 안이며 해당 셀 밖으로 나가지 않는다.
    /// </summary>
    public void PointAt(int cellIndex, double rollX, double rollY, float jitter, out int x, out int y)
    {
        x = X0;
        y = Y0;
        if (Cols <= 0 || Rows <= 0) return;

        int cellCount = Cols * Rows;
        if (cellIndex < 0) cellIndex = 0;
        else if (cellIndex >= cellCount) cellIndex = cellCount - 1;

        int cx = cellIndex % Cols;
        int cy = cellIndex / Cols;

        double cellW = (double)Width / Cols;
        double cellH = (double)Height / Rows;
        double j = Clamp01(jitter);

        double px = X0 + (cx + 0.5) * cellW + (Clamp01(rollX) - 0.5) * cellW * j;
        double py = Y0 + (cy + 0.5) * cellH + (Clamp01(rollY) - 0.5) * cellH * j;

        x = ClampInt((int)System.Math.Floor(px), X0, X0 + Width - 1);
        y = ClampInt((int)System.Math.Floor(py), Y0, Y0 + Height - 1);
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    private static int ClampInt(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}

/// <summary>배치 보조 유틸. 셀 방문 순서를 섞는 데 쓴다.</summary>
public static class MineralScatter
{
    /// <summary>
    /// Fisher-Yates. buffer[0..length)만 제자리에서 섞고 뒤쪽은 건드리지 않는다.
    /// 정적 재사용 버퍼를 앞부분만 쓰기 위해 length를 따로 받는다.
    /// </summary>
    public static void ShuffleInPlace(int[] buffer, int length, System.Random prng)
    {
        if (buffer == null || prng == null) return;
        if (length > buffer.Length) length = buffer.Length;

        for (int i = length - 1; i > 0; i--)
        {
            int j = prng.Next(i + 1);
            int tmp = buffer[i];
            buffer[i] = buffer[j];
            buffer[j] = tmp;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Unity Test Runner (EditMode) → `MineralScatterTests`
기대: 15개 테스트 전부 PASS.

`SquareRegion_GivesSquareGrid`가 실패하면 `Create`의 반올림을 확인한다 — `count=170`이면 `sqrt(170)=13.04` → `ceil=14`, `rows=ceil(170/14)=13`… **이 경우 rows는 13이 나온다.** 테스트가 14를 기대하므로, 실제 값을 확인해 테스트 쪽 기대값을 실제 계산에 맞춘다(`Cols=14, Rows=13, CellCount=182 >= 170`). 격자가 정사각형에 "가깝다"는 것이 요구사항이지 정확히 같을 필요는 없다.

- [ ] **체크포인트** — UVCS 체크인: `광물 균등 배치: ScatterGrid + 셔플 순수 로직 + EditMode 테스트`

---

## Task 2: 설정 전달 경로 정비 (동작 무변경)

**파일:**
- 생성: `Assets/Scripts/Gameplay/Terrain/Tiles/MineralSpawnSettings.cs`
- 수정: `Assets/Scripts/_Core/Data/TileDataModels.cs` (`TileDatabaseJson`)
- 수정: `Assets/Scripts/_Core/Managers/TileDataManager.cs`
- 수정: `Assets/Scripts/Gameplay/Terrain/Tiles/MineralGenerator.cs` (시그니처만)
- 수정: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/Decorators/MineralDecorator.cs`
- 수정: `Assets/StreamingAssets/tileData.json`

**인터페이스:**
- 산출: `MineralSpawnSettings` 구조체 — `float DensityMultiplier`, `float ScatterJitter`, `bool LogSummary`, `static MineralSpawnSettings Default`
- 산출: `TileDataManager.MineralScatterJitter` (`float`, 0~1 클램프)
- 산출: `MineralGenerator.GenerateMinerals(TerrainChunk, List<MineralRuleJson>, Vector2Int, int, TileType, List<Rect>, MineralSpawnSettings)`

`GenerateMinerals`의 인자가 밀도·로그·지터까지 붙어 9개가 된다. 여기서 묶는다.
**지터 값은 아직 아무도 소비하지 않으므로 게임 동작은 완전히 동일하다.**

- [ ] **Step 1: `MineralSpawnSettings` 생성**

`Assets/Scripts/Gameplay/Terrain/Tiles/MineralSpawnSettings.cs`

```csharp
// @tags: mineral, spawn, settings, config
/// <summary>
/// 광물 스폰 튜닝 값 묶음. tileData.json에서 와서 MineralDecorator가 채운다.
/// 값이 늘어날 때마다 GenerateMinerals 시그니처가 길어지는 것을 막는다.
/// 배경: Assets/Docs/mineral-even-scatter.md §3-5
/// </summary>
public struct MineralSpawnSettings
{
    /// <summary>층 배율(mineralDensity) × 전역 배율(globalMineralDensity).</summary>
    public float DensityMultiplier;

    /// <summary>배치 지터 0~1. 0 = 격자 정중앙(인공적), 1 = 셀 전체(자연스러움).</summary>
    public float ScatterJitter;

    /// <summary>청크별 요청/배치 개수를 콘솔에 출력(튜닝용).</summary>
    public bool LogSummary;

    public static MineralSpawnSettings Default => new MineralSpawnSettings
    {
        DensityMultiplier = 1f,
        ScatterJitter = 1f,
        LogSummary = false,
    };
}
```

- [ ] **Step 2: `TileDatabaseJson`에 지터 필드 추가**

`TileDataModels.cs`의 `TileDatabaseJson`에서 `logMineralSpawn` 선언 바로 아래에 삽입한다.

```csharp
    // [New] 광물 배치 지터 0~1. 0 = 격자 정중앙, 1 = 셀 전체(기본).
    // 배경: Assets/Docs/mineral-even-scatter.md §3-4
    public float mineralScatterJitter = 1.0f;
```

- [ ] **Step 3: `TileDataManager`에 노출**

`GlobalMineralDensity` / `LogMineralSpawn` 프로퍼티 선언 아래에 추가한다.

```csharp
    /// <summary>tileData.json의 mineralScatterJitter. 광물 배치 지터(0~1).</summary>
    public float MineralScatterJitter { get; private set; } = 1.0f;
```

`LoadTileData()`의 `LogMineralSpawn = database.logMineralSpawn;` 바로 아래에 대입을 추가한다.

```csharp
            MineralScatterJitter = Mathf.Clamp01(database.mineralScatterJitter);
```

- [ ] **Step 4: `GenerateMinerals` 시그니처를 구조체로 교체**

`MineralGenerator.cs`의 선언 줄을 교체한다. **본문은 아직 건드리지 않는다.**

```csharp
    public static void GenerateMinerals(TerrainChunk chunk, List<MineralRuleJson> mineralRules, Vector2Int coord, int worldSeed, TileType tileType, List<Rect> excludedAreas, MineralSpawnSettings settings)
```

본문에서 옛 파라미터를 쓰던 두 곳을 바꾼다.

```csharp
        System.Text.StringBuilder log = settings.LogSummary ? new System.Text.StringBuilder() : null;
```

```csharp
            double n = MineralDensity.ExpectedCount(rule, currentDepth, settings.DensityMultiplier, prng.NextDouble());
```

로그 출력 줄의 `density={densityMultiplier:0.##}`도 `density={settings.DensityMultiplier:0.##}`로 바꾼다.

> **기본값을 주지 않는 이유**: `MineralSpawnSettings settings = default`로 두면 `DensityMultiplier`가 `0`이 되어 **광물이 하나도 안 나온다.** 호출측이 반드시 명시하게 강제한다. 호출부는 `MineralDecorator` 한 곳뿐이다.

- [ ] **Step 5: `MineralDecorator`에서 설정 구성**

`MineralDecorator.cs`의 `GenerateMinerals` 호출부와 그 위 밀도 계산을 아래로 교체한다.

```csharp
        // 층 배율 × 전역 배율. 개수 산출은 MineralDensity가, 깊이 곡선은 rule의 rarity가 결정한다.
        // 배경: Assets/Docs/mineral-density-redesign.md §3-2
        var settings = new MineralSpawnSettings
        {
            DensityMultiplier = tileData.mineralDensity * TileDataManager.Instance.GlobalMineralDensity,
            ScatterJitter     = TileDataManager.Instance.MineralScatterJitter,
            LogSummary        = TileDataManager.Instance.LogMineralSpawn,
        };

        // PreOccupiedAreas: ElevatorDecorator가 등록한 엘리베이터 점유 영역 전달 → 해당 영역에 광물 스폰 제외
        MineralGenerator.GenerateMinerals(chunk, tileData.minerals, context.Coord, context.WorldSeed,
                                          context.TargetTileType, context.PreOccupiedAreas, settings);
```

- [ ] **Step 6: `tileData.json`에 지터 추가**

최상위 `"logMineralSpawn": true,` 아래에 한 줄 추가한다.

```json
  "mineralScatterJitter": 1.0,
```

- [ ] **Step 7: 컴파일 + 동작 무변경 확인**

Unity에서 컴파일 에러 0을 확인한 뒤, 플레이 모드로 지상 청크를 몇 개 로드한다.
기대: 스폰 로그의 `요청`·`배치` 수가 이전과 같은 수준. 배치 위치도 **아직 그대로**다(지터 미사용).

- [ ] **체크포인트** — UVCS 체크인: `광물 스폰 설정을 MineralSpawnSettings로 묶고 지터 값 배선`

---

## Task 3: 3단계 균등 배치로 전환

**파일:**
- 수정: `Assets/Scripts/Gameplay/Terrain/Tiles/MineralGenerator.cs`

**인터페이스:**
- 소비: `ScatterGrid.Create` / `.CellCount` / `.PointAt`, `MineralScatter.ShuffleInPlace` (Task 1), `MineralSpawnSettings` (Task 2)
- 산출: 없음 (외부 시그니처 불변)

**여기서 배치 동작이 실제로 바뀐다.**

- [ ] **Step 1: 정적 버퍼와 상수 추가**

`MineralGenerator.cs`의 기존 `MINERAL_SUPPORT_MARGIN` 상수 선언 근처에 추가한다.

```csharp
    // 균등 배치용 정적 재사용 버퍼. MineralGenerator는 데코레이션 단계에서 메인 스레드로만
    // 호출되므로 정적 버퍼가 안전하다. 청크마다 new 하면 로드 경로에 GC가 쌓인다.
    // 배경: Assets/Docs/performance/chunk-load-gc.md
    private static int[] s_cellOrder = new int[1024];
    private static int[] s_requested = new int[16];
    private static bool s_limitWarningShown;

    // 셀 안에서 유효 위치를 찾는 시도 횟수. 셀이 작고(수십 px) 갓 생성된 청크는 거의 꽉 차 있으므로,
    // 여기서 다 실패하면 그 셀이 실제로 빈 공간이라는 뜻이다. 더 시도할 이유가 없다.
    private const int MAX_ATTEMPTS_PER_CELL = 6;
```

- [ ] **Step 2: `GenerateMinerals` 본문을 3단계로 교체**

`GenerateMinerals` 전체를 아래로 교체한다.

```csharp
    public static void GenerateMinerals(TerrainChunk chunk, List<MineralRuleJson> mineralRules, Vector2Int coord, int worldSeed, TileType tileType, List<Rect> excludedAreas, MineralSpawnSettings settings)
    {
        if (mineralRules == null || mineralRules.Count == 0 || chunk == null) return;
        // Phase 2(장식) 시점에 InitDistanceFieldJob이 pixelInfo를 읽는 중일 수 있으므로,
        // PixelInfo 쓰기 전 반드시 모든 잡을 완료해야 한다.
        chunk.EnsureJobsCompleted();

        System.Random prng = GetDeterministicRandom(coord, worldSeed);
        int currentDepth = Mathf.Abs(coord.y);

        // ── 1단계: 요청 개수 집계 ──────────────────────────────────────────
        // 격자 크기를 정하려면 청크 전체 개수를 먼저 알아야 한다.
        // rule마다 따로 배치하면 서로를 몰라서 겹치고 뭉친다(설계 §1-2).
        EnsureCapacity(ref s_requested, mineralRules.Count);

        int total = 0;
        for (int r = 0; r < mineralRules.Count; r++)
        {
            // [중요] 난수 2개는 rule마다 무조건 소비한다.
            // 깊이 범위를 벗어난 rule에서 건너뛰면 난수 스트림이 깊이별로 어긋나
            // 같은 시드에서도 배치가 요동친다.
            double n = MineralDensity.ExpectedCount(mineralRules[r], currentDepth,
                                                    settings.DensityMultiplier, prng.NextDouble());
            int requested = MineralDensity.ProbabilisticRound(n, prng.NextDouble());
            s_requested[r] = requested;
            total += requested;
        }

        if (total <= 0) return;

        // ── 2단계: 격자 생성 + 셀 순서 셔플 ────────────────────────────────
        int lo = MINERAL_VALIDATION_PADDING + MINERAL_SUPPORT_MARGIN;
        int regionW = chunk.width - lo * 2;
        int regionH = chunk.height - lo * 2;
        if (regionW <= 0 || regionH <= 0) return;

        total = ClampToPhysicalLimit(total, regionW, regionH, coord);

        ScatterGrid grid = ScatterGrid.Create(total, lo, lo, regionW, regionH);
        int cellCount = grid.CellCount;
        if (cellCount <= 0) return;

        EnsureCapacity(ref s_cellOrder, cellCount);
        for (int i = 0; i < cellCount; i++) s_cellOrder[i] = i;
        MineralScatter.ShuffleInPlace(s_cellOrder, cellCount, prng);

        // ── 3단계: 배치 ───────────────────────────────────────────────────
        // 셀 순서가 섞여 있으므로 각 rule은 자동으로 청크 전역에 흩어진다.
        // 셔플 한 번으로 "종류별 균등"과 "전체 균등"이 동시에 성립한다.
        System.Text.StringBuilder log = settings.LogSummary ? new System.Text.StringBuilder() : null;
        int totalRequested = 0;
        int totalPlaced = 0;
        int pointIndex = 0;

        for (int r = 0; r < mineralRules.Count; r++)
        {
            int requested = s_requested[r];
            if (requested <= 0) continue;

            var rule = mineralRules[r];
            int placed = 0;

            try
            {
                if (TryGetMineralData(rule.mineralType, out MineralSO mineralSO))
                {
                    for (int i = 0; i < requested && pointIndex < cellCount; i++)
                    {
                        int cell = s_cellOrder[pointIndex++];
                        if (TryFindPositionInCell(chunk, grid, cell, prng, settings.ScatterJitter,
                                                  excludedAreas, out int x, out int y))
                        {
                            SpawnAndCarveMineral(chunk, x, y, mineralSO);
                            placed++;
                        }
                        // 실패한 셀은 그냥 건너뛴다. 다른 곳에 다시 시도하면
                        // 남은 흙에 몰아넣게 되고, 그게 뭉침의 원인이다(설계 §4-2).
                    }
                }
            }
            catch (System.Exception e)
            {
                // rule 하나가 터져도 나머지 광물은 정상 생성돼야 한다
                Debug.LogError($"[MineralGenerator] Error generating mineral '{rule?.mineralType}': {e.Message}\n{e.StackTrace}");
            }

            if (log != null)
            {
                totalRequested += requested;
                totalPlaced += placed;
                log.Append("\n  ").Append(rule.mineralType).Append(' ')
                   .Append(placed).Append('/').Append(requested);
            }
        }

        if (log != null)
        {
            // 요청 대비 배치 비율이 낮으면 지형이 성겨서 IsWellSupported가 떨어뜨리고 있다는 뜻이다.
            // 배경: Assets/Docs/mineral-density-redesign.md §6-2
            int percent = (totalRequested > 0) ? Mathf.RoundToInt(100f * totalPlaced / totalRequested) : 0;
            Debug.Log($"[Minerals] chunk({coord.x},{coord.y}) {tileType} depth={currentDepth}" +
                      $" density={settings.DensityMultiplier:0.##} jitter={settings.ScatterJitter:0.##}" +
                      $" grid={grid.Cols}x{grid.Rows}" +
                      $" | 배치 {totalPlaced} / 요청 {totalRequested} ({percent}%){log}");
        }
    }
```

- [ ] **Step 3: `GetRandomValidPosition`을 `TryFindPositionInCell`로 교체**

기존 `GetRandomValidPosition` 메서드 전체를 삭제하고 아래로 대체한다.
`IsWellSupported` / `IsInExcludedArea` / `IsGroundPixel`은 **그대로 둔다.**

```csharp
    /// <summary>
    /// 한 셀 안에서 유효한 스폰 좌표를 찾는다. 청크 전역이 아니라 셀 안만 뒤지는 것이
    /// 균등 배치의 핵심이다 — 실패해도 다른 곳으로 새지 않는다.
    /// </summary>
    private static bool TryFindPositionInCell(TerrainChunk chunk, ScatterGrid grid, int cellIndex,
                                              System.Random prng, float jitter,
                                              List<Rect> excludedAreas, out int x, out int y)
    {
        x = 0;
        y = 0;

        for (int attempt = 0; attempt < MAX_ATTEMPTS_PER_CELL; attempt++)
        {
            // 첫 시도만 설정된 지터를 쓴다. jitter=0이면 재시도가 전부 같은 중심점이 되어
            // 무의미하므로, 재시도부터는 셀 전체(1.0)로 넓힌다.
            // 의도한 미감은 성공하는 경우에 유지되고, 실패할 때만 완화된다.
            float j = (attempt == 0) ? jitter : 1f;
            grid.PointAt(cellIndex, prng.NextDouble(), prng.NextDouble(), j, out x, out y);

            // 중심 + 이웃(상하좌우·대각)이 모두 솔리드여야 지지 검사를 통과해 얌전히 박혀 있는다.
            if (!IsWellSupported(chunk, x, y)) continue;

            // [엘리베이터 제외] 엘리베이터가 차지하는 픽셀 영역에는 광물 스폰 안 함
            if (excludedAreas != null && IsInExcludedArea(x, y, excludedAreas)) continue;

            return true;
        }

        return false;
    }
```

- [ ] **Step 4: 보조 메서드 2개 추가**

`TryFindPositionInCell` 아래에 추가한다.

```csharp
    /// <summary>
    /// 최소 간격(지지 마진 ×2) 기준으로 영역에 물리적으로 들어갈 수 있는 최대 개수로 자른다.
    /// 이보다 많이 요청되면 어차피 배치되지 않고 s_cellOrder만 무한정 커진다.
    /// 정상 경로가 아니라 잘못된 설정에 대한 방어이므로 경고는 세션당 1회만 낸다.
    /// </summary>
    private static int ClampToPhysicalLimit(int total, int regionW, int regionH, Vector2Int coord)
    {
        int spacing = MINERAL_SUPPORT_MARGIN * 2;
        int limit = Mathf.Max(1, (regionW / spacing) * (regionH / spacing));
        if (total <= limit) return total;

        if (!s_limitWarningShown)
        {
            s_limitWarningShown = true;
            Debug.LogWarning($"[MineralGenerator] chunk({coord.x},{coord.y}) 광물 요청 {total}개가 " +
                             $"물리적 한계 {limit}개를 넘어 잘렸습니다. " +
                             $"perChunk / mineralDensity / globalMineralDensity 값을 확인하세요. " +
                             $"(이 경고는 세션당 1회만 표시)");
        }
        return limit;
    }

    /// <summary>정적 버퍼를 필요한 크기까지만 키운다. 줄이지는 않는다.</summary>
    private static void EnsureCapacity(ref int[] buffer, int needed)
    {
        if (buffer == null || buffer.Length < needed)
            buffer = new int[Mathf.NextPowerOfTwo(needed)];
    }
```

- [ ] **Step 5: 컴파일 확인**

기대: 에러 0. `GetRandomValidPosition`이 사라졌으므로 남은 참조가 없는지 확인한다 —
Grep: `GetRandomValidPosition` (경로 `Assets/Scripts`) → 결과 0건이어야 한다.

- [ ] **Step 6: 균등 배치 육안 확인**

플레이 모드에서 **새 세이브**로 지상 청크를 넓게 판다.
기대: 뭉친 덩어리와 큰 빈 구멍이 사라지고 고르게 깔린다.

⚠ 광물 **위치는 이전과 완전히 다르다.** 난수 스트림이 바뀌었으므로 정상이다(설계 §4-1).

격자 규칙성이 눈에 띄면 `tileData.json`의 `mineralScatterJitter`를 확인한다 — `1.0`이어야 한다.

- [ ] **Step 7: 손실률 확인**

스폰 로그의 `%`를 이전 값과 비교한다.
`grid=14x13` 같은 격자 크기도 함께 찍히므로 셀 수가 요청 개수를 덮는지 바로 보인다.

기대: 일반 청크에서 크게 나빠지지 않는다. 갓 생성된 청크는 거의 꽉 차 있어 셀 대부분이 유효하다.
**크게 떨어지면**(예: 90% → 50%) 공동이 큰 청크에서 셀을 건너뛰고 있다는 뜻이다(설계 §4-2).
그 경우 설계 §8의 "셀 건너뜀 보상"을 재검토한다 — 다만 그건 뭉침을 부분적으로 되살리는 트레이드오프다.

- [ ] **Step 8: 지터 다이얼 확인**

`mineralScatterJitter`를 `0.0`으로 바꿔 플레이 → 광물이 격자 정중앙에 딱 맞춰 깔리는지 확인한다.
이게 보이면 지터 배선이 정상이라는 뜻이다. 확인 후 **`1.0`으로 되돌린다.**

- [ ] **체크포인트** — UVCS 체크인: `광물 배치를 지터 격자 기반 균등 배치로 전환`

---

## 완료 후

설계 문서 §5의 **청크 경계 이음매**는 그대로 남는다.
경계에서 광물이 붙거나 벌어지는 게 눈에 띄면 월드 좌표 고정 격자로 올리는 것을 검토한다.
다만 그건 `perChunk`(청크당 개수) 노브를 "셀 크기" 노브로 뒤집는 변경이라 별도 설계가 필요하다.

성능 관련 변화가 관측되면(청크 로드 시간·GC) `Assets/Docs/performance/README.md`에 기록한다.
이번 작업은 배치 알고리즘 교체가 목적이지 성능 최적화가 아니지만,
`GetRandomValidPosition`의 20회 거부 루프가 셀 내 6회로 줄어드는 부수 효과가 있다.

---

## 자체 점검 결과

| 설계 항목 | 담당 태스크 |
|---|---|
| §3-1 3단계 구조 (집계→격자→배치) | Task 3 Step 2 |
| §3-2 `ScatterGrid` + `ShuffleInPlace` | Task 1 |
| §3-3 셀 내 탐색, 재시도 시 지터 1.0 | Task 3 Step 3 |
| §3-4 `mineralScatterJitter` 파라미터 | Task 2 Step 2·3·6 |
| §3-5 `MineralSpawnSettings` 묶음 | Task 2 Step 1·4·5 |
| §3-6 정적 재사용 버퍼 + 상한 클램프 + `pointIndex >= CellCount` 중단 | Task 3 Step 1·2·4 |
| §4-1 난수 스트림 변경 고지 | Task 3 Step 6 |
| §4-2 공동 청크 총량 감소 | Task 3 Step 7 |
| §4-3 최소 간격 | Task 1 `EveryCellStaysInsideItsOwnColumnAndRow` 테스트 |
| §5 경계 이음매 (한계로 수용) | "완료 후" 절 |
| §7 검증 1~7 | Task 1 테스트(1~5), Task 3 Step 6~8(6~7) |
| §8 하지 않는 것 | 전역 제약 + "완료 후" 절 |

누락 없음. 태스크 간 이름·타입 일치 확인 완료
(`ScatterGrid.Create/CellCount/PointAt/Cols/Rows`, `MineralScatter.ShuffleInPlace`,
`MineralSpawnSettings.DensityMultiplier/ScatterJitter/LogSummary`, `TileDataManager.MineralScatterJitter`).
