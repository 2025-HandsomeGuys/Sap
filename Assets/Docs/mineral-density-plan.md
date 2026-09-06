# 광물 스폰 밀도 파라미터 재설계 — 구현 계획

> 설계 문서: `Assets/Docs/mineral-density-redesign.md` (먼저 읽을 것)

**목표:** `MineralRuleJson`에서 청크 스폰용 필드와 돌 드랍용 필드를 분리하고, 청크 밀도를 `perChunk` 한 노브 + 층·전역 배율로 조절 가능하게 만든다.

**접근:** 6개 태스크로 나누되 **매 태스크 종료 시점마다 컴파일이 되고 게임 동작이 유지된다.**
신·구 필드를 잠시 병기했다가 마지막에 구 필드를 걷어내는 순서다. 중간에 "광물이 하나도 안 나오는" 구간을 만들지 않는다.

**기술 스택:** Unity 2D / C# / `JsonUtility` / NUnit(EditMode)

---

## 전역 제약 — 모든 태스크에 적용

- **버전 관리는 UVCS다.** `git add` / `git commit` 등 git 명령을 쓰지 않는다. 각 태스크 끝의 "체크포인트"는 UVCS 체크인 시점 표시일 뿐이다.
- **Unity Test Runner 실행은 사람이 한다.** 에이전트는 테스트 파일 작성까지만 하고, 실행 결과를 게이트로 삼아 멈추지 않는다. 실행할 테스트 이름은 각 태스크에 명시돼 있다.
- **배치 알고리즘을 건드리지 않는다.** `GetRandomValidPosition` / `IsWellSupported` / `SpawnAndCarveMineral` / `MarkPixelAsMineral`은 이번 작업에서 한 줄도 수정 대상이 아니다.
- **깊이 곡선을 바꾸지 않는다.** Rare `0→1`, Common `1→0.5`를 그대로 보존한다.
- **밸런스 값을 바꾸지 않는다.** §4 마이그레이션 표는 기존 기대값의 무손실 환산이다. Dirt층 편중(다른 층의 4~6배)은 의도적으로 남긴다.
- **난수는 반드시 청크 시드 `prng`(`System.Random`)를 쓴다.** `UnityEngine.Random`을 스폰 경로에 쓰면 청크 결정성이 깨진다.
- 새 파일 최상단에는 프로젝트 관례대로 `// @tags: ...` 주석을 단다.

---

## 파일 구조

| 파일 | 상태 | 책임 |
|---|---|---|
| `Assets/Scripts/Gameplay/Terrain/Tiles/MineralDensity.cs` | **신규** | 스폰 개수 계산 순수 로직. Unity 비의존, 난수 주입식 |
| `Assets/Scripts/_Core/Data/TileDataModels.cs` | 수정 | 스키마 — 신규 필드 추가 → 구 필드 제거 |
| `Assets/Scripts/_Core/Managers/TileDataManager.cs` | 수정 | `GlobalMineralDensity` 노출, 로드 시 규칙 검증 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/MineralGenerator.cs` | 수정 | `attempts` 루프 제거, `MineralDensity`로 개수 산출 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/Decorators/MineralDecorator.cs` | 수정 | 층·전역 배율 전달 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/DiggableRock.cs` | 수정 | `rockDropWeight` / `RockDropMin` / `RockDropMax`로 치환 |
| `Assets/StreamingAssets/tileData.json` | 수정 | 29개 rule 마이그레이션 |
| `Assets/Tests/EditMode/MineralDensityTests.cs` | **신규** | 계산 로직 테스트 |
| `Assets/Tests/EditMode/TileDataMineralRuleTests.cs` | **신규** | JSON 29개 rule 스키마 무결성 테스트 |

`MineralDensity`를 별도 파일로 빼는 이유: `MineralGenerator`는 `TerrainChunk`·`NativeArray`에 묶여 있어 EditMode에서 못 돌린다. 계산만 분리하면 순수 함수로 테스트할 수 있다 (`AmbienceSelector`·`MineralUpgradeLadder`와 같은 패턴).

---

## Task 1: 스키마에 신규 필드 추가 (구 필드 유지)

**파일:**
- 수정: `Assets/Scripts/_Core/Data/TileDataModels.cs:41-58` (`MineralRuleJson`)
- 수정: `Assets/Scripts/_Core/Data/TileDataModels.cs:14-39` (`TileDataJson`)
- 수정: `Assets/Scripts/_Core/Data/TileDataModels.cs:74-86` (`TileDatabaseJson`)
- 수정: `Assets/Scripts/_Core/Managers/TileDataManager.cs` (`LoadTileData` 내부)

**인터페이스:**
- 산출: `MineralRuleJson.perChunk` (`float[]`), `.rockDropWeight` (`float`), `.rockDropCount` (`int[]`), `.RockDropMin` / `.RockDropMax` (`int`)
- 산출: `TileDataJson.mineralDensity` (`float`, 기본 `1f`)
- 산출: `TileDatabaseJson.globalMineralDensity` (`float`, 기본 `1f`)
- 산출: `TileDataManager.GlobalMineralDensity` (`float` 읽기 전용 프로퍼티)

이 태스크는 필드만 추가한다. **아직 아무도 읽지 않으므로 게임 동작은 완전히 동일하다.**

- [ ] **Step 1: `MineralRuleJson`에 신규 필드 추가**

`TileDataModels.cs`의 `MineralRuleJson` 전체를 아래로 교체한다. 구 필드 4개(`chance`/`minCount`/`maxCount`/`attemptsPerChunk`)는 Task 6까지 그대로 둔다.

```csharp
[System.Serializable]
public class MineralRuleJson
{
    public string mineralType; // Enum name as string (e.g. "Coal")
    public int minDepth;       // 적용 깊이 범위 (양쪽 시스템 공유)
    public int maxDepth;

    // [Fix] JsonUtility는 enum을 숫자로만 인식 → string으로 받아서 직접 파싱
    // JSON에는 "rarity": "Common" 또는 "Rare" 로 작성
    public string rarity;      // "Common" or "Rare" (양쪽 시스템 공유)

    public bool IsRare => string.Equals(rarity, "Rare", System.StringComparison.OrdinalIgnoreCase);

    // ─── 청크 지형 스폰 전용 (MineralGenerator) ───────────────────────
    // [min, max] 청크당 스폰 개수 범위. 깊이 보정·밀도 배율 적용 전 기준값.
    // 소수 허용 — [0, 1]처럼 1개 미만의 희소 광물을 표현한다.
    public float[] perChunk;

    // ─── 돌 깨기 드랍 전용 (DiggableRock) ─────────────────────────────
    // 드랍 광물 선택 가중치. 깊이에 따라 Lerp(w, 1-w, t)로 뒤집힌다.
    public float rockDropWeight;
    // [min, max] 돌 1개당 드랍 개수 범위.
    public int[] rockDropCount;

    public int RockDropMin => (rockDropCount != null && rockDropCount.Length > 0) ? rockDropCount[0] : 1;
    public int RockDropMax => (rockDropCount != null && rockDropCount.Length > 1) ? rockDropCount[1] : RockDropMin;

    // ─── [Deprecated] Task 6에서 제거. 아래 4개는 두 시스템이 뒤섞여 쓰던 필드다. ───
    public float chance;
    public int minCount;
    public int maxCount;
    public int attemptsPerChunk = 1;
}
```

- [ ] **Step 2: `TileDataJson`에 층 밀도 배율 추가**

`TileDataJson`의 `public List<MineralRuleJson> minerals;` 줄 바로 위에 삽입한다.

```csharp
    // [New] 이 층의 광물 스폰 밀도 배율. 1.0 = 기준. 층 간 밀도 편차 조정용.
    // 배경: Assets/Docs/mineral-density-redesign.md §2-3
    public float mineralDensity = 1.0f;
```

- [ ] **Step 3: `TileDatabaseJson`에 전역 밀도 배율 추가**

`TileDatabaseJson`의 `public List<TileDataJson> tiles;` 줄 바로 아래에 삽입한다.

```csharp
    // [New] 게임 전체 광물 스폰 밀도 배율. 층 배율(mineralDensity)과 곱해진다.
    public float globalMineralDensity = 1.0f;
```

- [ ] **Step 4: `TileDataManager`에 `GlobalMineralDensity` 노출**

`TileDataManager.cs`의 `public string tileDataJsonPath = "tileData.json";` 아래에 프로퍼티를 추가한다.

```csharp
    /// <summary>tileData.json의 globalMineralDensity. 광물 스폰 밀도 전역 배율.</summary>
    public float GlobalMineralDensity { get; private set; } = 1.0f;
```

`LoadTileData()` 안, `terrainWidth = database.terrainWidth;` 바로 다음 줄에 대입을 추가한다.

```csharp
            // 음수·NaN 방지. 0은 "광물 전부 끄기"로 유효하므로 허용한다.
            GlobalMineralDensity = (database.globalMineralDensity >= 0f) ? database.globalMineralDensity : 1.0f;
```

- [ ] **Step 5: 컴파일 확인**

Unity 에디터로 전환해 컴파일 에러가 없는지 확인한다.
기대: 에러 0. 신규 필드를 아직 아무도 안 읽으므로 경고도 없어야 한다.

- [ ] **Step 6: 동작 무변경 확인**

플레이 모드로 지상에서 몇 청크 파본다.
기대: 광물이 이전과 동일하게 나온다 (아직 구 경로가 그대로 동작 중).

- [ ] **체크포인트** — UVCS 체크인: `광물 스키마: perChunk / rockDrop* 필드 추가 (구 필드 병존)`

---

## Task 2: `MineralDensity` 순수 계산기 신설

**파일:**
- 생성: `Assets/Scripts/Gameplay/Terrain/Tiles/MineralDensity.cs`
- 생성: `Assets/Tests/EditMode/MineralDensityTests.cs`

**인터페이스:**
- 소비: `MineralRuleJson.perChunk` / `.minDepth` / `.maxDepth` / `.IsRare` (Task 1)
- 산출:
  - `static double MineralDensity.DepthFactor(int depth, int minDepth, int maxDepth, bool isRare)`
  - `static bool MineralDensity.HasValidRange(float[] perChunk)`
  - `static double MineralDensity.ExpectedCount(MineralRuleJson rule, int depth, double densityMultiplier, double lerpRoll)`
  - `static int MineralDensity.ProbabilisticRound(double n, double roundRoll)`

난수를 인자로 주입받는 설계다. `System.Random`을 내부에서 만들지 않으므로 테스트에서 경계값을 직접 찍을 수 있고, 호출측이 청크 시드를 그대로 쓸 수 있다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/MineralDensityTests.cs`

```csharp
using NUnit.Framework;

public class MineralDensityTests
{
    private static MineralRuleJson Rule(float lo, float hi, int minD, int maxD, bool rare)
    {
        return new MineralRuleJson
        {
            mineralType = "Coal",
            minDepth = minD,
            maxDepth = maxD,
            rarity = rare ? "Rare" : "Common",
            perChunk = new[] { lo, hi }
        };
    }

    // ─── DepthFactor ───────────────────────────────────────────────

    [Test]
    public void RareFactor_RisesFromZeroToOne()
    {
        Assert.AreEqual(0.0, MineralDensity.DepthFactor(0, 0, 20, true), 1e-9);
        Assert.AreEqual(0.5, MineralDensity.DepthFactor(10, 0, 20, true), 1e-9);
        Assert.AreEqual(1.0, MineralDensity.DepthFactor(20, 0, 20, true), 1e-9);
    }

    [Test]
    public void CommonFactor_FallsFromOneToHalf()
    {
        Assert.AreEqual(1.00, MineralDensity.DepthFactor(0, 0, 20, false), 1e-9);
        Assert.AreEqual(0.75, MineralDensity.DepthFactor(10, 0, 20, false), 1e-9);
        Assert.AreEqual(0.50, MineralDensity.DepthFactor(20, 0, 20, false), 1e-9);
    }

    [Test]
    public void DegenerateRange_TreatedAsFullDepth()
    {
        // minDepth == maxDepth면 t=1로 고정한다 (0으로 나누지 않는다)
        Assert.AreEqual(1.0, MineralDensity.DepthFactor(5, 5, 5, true), 1e-9);
        Assert.AreEqual(0.5, MineralDensity.DepthFactor(5, 5, 5, false), 1e-9);
    }

    [Test]
    public void DepthFactor_ClampsOutsideRange()
    {
        Assert.AreEqual(0.0, MineralDensity.DepthFactor(-5, 0, 20, true), 1e-9);
        Assert.AreEqual(1.0, MineralDensity.DepthFactor(99, 0, 20, true), 1e-9);
    }

    // ─── ExpectedCount ─────────────────────────────────────────────

    [Test]
    public void LerpRoll_WalksThePerChunkRange()
    {
        // Common, 층 최상단(depthFactor = 1.0), 배율 1.0 → perChunk 값이 그대로 나온다
        var rule = Rule(10f, 20f, 0, 20, rare: false);
        Assert.AreEqual(10.0, MineralDensity.ExpectedCount(rule, 0, 1.0, 0.0), 1e-9);
        Assert.AreEqual(15.0, MineralDensity.ExpectedCount(rule, 0, 1.0, 0.5), 1e-9);
        Assert.AreEqual(20.0, MineralDensity.ExpectedCount(rule, 0, 1.0, 1.0), 1e-9);
    }

    [Test]
    public void DepthFactorAndMultiplier_BothApply()
    {
        var rule = Rule(10f, 10f, 0, 20, rare: false);
        // 층 최하단 Common → depthFactor 0.5, 배율 2.0 → 10 * 0.5 * 2.0 = 10
        Assert.AreEqual(10.0, MineralDensity.ExpectedCount(rule, 20, 2.0, 0.5), 1e-9);
        // 배율만 3배 → 10 * 1.0 * 3.0 = 30
        Assert.AreEqual(30.0, MineralDensity.ExpectedCount(rule, 0, 3.0, 0.5), 1e-9);
    }

    [Test]
    public void OutOfDepthRange_IsZero()
    {
        var rule = Rule(10f, 10f, 20, 39, rare: false);
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(rule, 19, 1.0, 0.5), 1e-9);
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(rule, 40, 1.0, 0.5), 1e-9);
    }

    [Test]
    public void MissingPerChunk_IsZeroNotDefaultOne()
    {
        // 설정을 빠뜨린 광물이 "조금씩 나오는" 상태를 만들면 원인 추적이 어렵다.
        // 누락은 0개로 확실히 드러나야 한다. 경고는 TileDataManager 로드 시점에 뜬다.
        var noArray = Rule(1f, 1f, 0, 20, false);
        noArray.perChunk = null;
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(noArray, 0, 1.0, 0.5), 1e-9);

        var tooShort = Rule(1f, 1f, 0, 20, false);
        tooShort.perChunk = new[] { 5f };
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(tooShort, 0, 1.0, 0.5), 1e-9);

        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(null, 0, 1.0, 0.5), 1e-9);
    }

    [Test]
    public void ZeroMultiplier_TurnsMineralsOff()
    {
        var rule = Rule(10f, 20f, 0, 20, rare: false);
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(rule, 0, 0.0, 0.5), 1e-9);
    }

    [Test]
    public void NegativeValues_ClampToZero()
    {
        var rule = Rule(-5f, -5f, 0, 20, rare: false);
        Assert.AreEqual(0.0, MineralDensity.ExpectedCount(rule, 0, 1.0, 0.5), 1e-9);
    }

    // ─── ProbabilisticRound ────────────────────────────────────────

    [Test]
    public void WholeNumber_RoundsExactly()
    {
        Assert.AreEqual(3, MineralDensity.ProbabilisticRound(3.0, 0.0));
        Assert.AreEqual(3, MineralDensity.ProbabilisticRound(3.0, 0.999));
    }

    [Test]
    public void Fraction_SplitsOnTheRoll()
    {
        // n = 3.4 → roll < 0.4면 4개, 아니면 3개
        Assert.AreEqual(4, MineralDensity.ProbabilisticRound(3.4, 0.0));
        Assert.AreEqual(4, MineralDensity.ProbabilisticRound(3.4, 0.399));
        Assert.AreEqual(3, MineralDensity.ProbabilisticRound(3.4, 0.4));
        Assert.AreEqual(3, MineralDensity.ProbabilisticRound(3.4, 0.999));
    }

    [Test]
    public void SubOneValue_IsRepresentable()
    {
        // [0, 1] 같은 희소 광물: 기대 0.48개 → 48% 확률로 1개
        Assert.AreEqual(1, MineralDensity.ProbabilisticRound(0.48, 0.47));
        Assert.AreEqual(0, MineralDensity.ProbabilisticRound(0.48, 0.49));
    }

    [Test]
    public void NonPositive_IsZero()
    {
        Assert.AreEqual(0, MineralDensity.ProbabilisticRound(0.0, 0.0));
        Assert.AreEqual(0, MineralDensity.ProbabilisticRound(-1.5, 0.0));
    }

    [Test]
    public void ExpectedValue_ConvergesToN()
    {
        // 확률 반올림의 기대값은 정확히 n이어야 한다
        var prng = new System.Random(12345);
        const double N = 3.4;
        const int ITERATIONS = 100000;

        long total = 0;
        for (int i = 0; i < ITERATIONS; i++)
            total += MineralDensity.ProbabilisticRound(N, prng.NextDouble());

        double mean = (double)total / ITERATIONS;
        Assert.AreEqual(N, mean, 0.02, $"평균 {mean}");
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Unity Test Runner (EditMode) → `MineralDensityTests`
기대: **컴파일 실패** — `MineralDensity` 타입이 없음.

- [ ] **Step 3: `MineralDensity` 구현**

`Assets/Scripts/Gameplay/Terrain/Tiles/MineralDensity.cs` 신규 생성

```csharp
// @tags: mineral, density, spawn, generation, pure-logic
/// <summary>
/// 광물 청크 스폰 개수 계산. Unity 비의존 순수 로직 — EditMode 테스트 대상.
/// 난수는 전부 호출측이 주입한다(청크 결정성 보존 + 테스트 가능성).
/// 배경: Assets/Docs/mineral-density-redesign.md
/// </summary>
public static class MineralDensity
{
    /// <summary>
    /// 깊이 보정 계수. Rare는 0→1(깊을수록 많이), Common은 1→0.5(깊을수록 적게).
    /// 기존 MineralGenerator의 곡선을 그대로 옮긴 것이다.
    /// </summary>
    public static double DepthFactor(int depth, int minDepth, int maxDepth, bool isRare)
    {
        double t = (maxDepth > minDepth)
            ? Clamp01((double)(depth - minDepth) / (maxDepth - minDepth))
            : 1.0;

        return isRare ? t : 1.0 - 0.5 * t;
    }

    /// <summary>perChunk 범위가 쓸 수 있는 형태인지. TileDataManager 로드 검증에서도 쓴다.</summary>
    public static bool HasValidRange(float[] perChunk)
        => perChunk != null && perChunk.Length >= 2;

    /// <summary>
    /// 이 청크에서 스폰을 요청할 개수(실수). 깊이 범위 밖이거나 perChunk가 누락이면 0.
    /// lerpRoll은 [0,1) 균등 난수.
    /// </summary>
    public static double ExpectedCount(MineralRuleJson rule, int depth, double densityMultiplier, double lerpRoll)
    {
        if (rule == null) return 0.0;
        if (depth < rule.minDepth || depth > rule.maxDepth) return 0.0;

        // 누락에 기본값을 주지 않는다 — 설정 안 한 광물이 조용히 나오면 추적이 어렵다.
        if (!HasValidRange(rule.perChunk)) return 0.0;
        if (densityMultiplier <= 0.0) return 0.0;

        double lo = rule.perChunk[0];
        double hi = rule.perChunk[1];
        double n = lo + (hi - lo) * Clamp01(lerpRoll);

        n *= DepthFactor(depth, rule.minDepth, rule.maxDepth, rule.IsRare);
        n *= densityMultiplier;

        return n > 0.0 ? n : 0.0;
    }

    /// <summary>
    /// 소수부를 확률로 반올림. 기대값은 정확히 n이다.
    /// 예: 3.4 → 40% 확률로 4개, 60% 확률로 3개.
    /// 이게 있어야 perChunk [0, 1] 같은 1개 미만 희소 광물을 표현할 수 있다.
    /// roundRoll은 [0,1) 균등 난수.
    /// </summary>
    public static int ProbabilisticRound(double n, double roundRoll)
    {
        if (n <= 0.0) return 0;

        int floor = (int)System.Math.Floor(n);
        double frac = n - floor;

        return roundRoll < frac ? floor + 1 : floor;
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
}
```

- [ ] **Step 4: 테스트 통과 확인**

Unity Test Runner (EditMode) → `MineralDensityTests`
기대: 15개 테스트 전부 PASS.

- [ ] **체크포인트** — UVCS 체크인: `광물 밀도 계산 순수 로직 MineralDensity 추가 + EditMode 테스트`

---

## Task 3: `tileData.json` 마이그레이션 (신·구 필드 병기)

**파일:**
- 수정: `Assets/StreamingAssets/tileData.json`
- 생성: `Assets/Tests/EditMode/TileDataMineralRuleTests.cs`

**인터페이스:**
- 소비: Task 1의 스키마 필드명
- 산출: 29개 rule 전부가 `perChunk`(길이 2) / `rockDropWeight` / `rockDropCount`(길이 2)를 가진 JSON

**이 태스크는 게임 동작을 바꾸지 않는다.** 신규 필드를 아직 아무도 읽지 않기 때문이다. 값 환산의 근거는 설계 문서 §4다.

- [ ] **Step 1: Dirt층 `minerals` 배열 교체**

`tileData.json`의 `"tileType": "Dirt"` 블록 안 `"minerals": [ ... ]`를 아래로 교체한다.

```json
      "minerals": [
        { "mineralType": "ScrapMetal", "chance": 0.9, "minDepth": 0, "maxDepth": 19, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 6,
          "perChunk": [12, 20], "rockDropWeight": 0.9, "rockDropCount": [2, 4] },
        { "mineralType": "GarbageBag", "chance": 0.9, "minDepth": 0, "maxDepth": 19, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 12,
          "perChunk": [26, 39], "rockDropWeight": 0.9, "rockDropCount": [2, 4] },
        { "mineralType": "PETBottle", "chance": 0.9, "minDepth": 0, "maxDepth": 19, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 12,
          "perChunk": [26, 39], "rockDropWeight": 0.9, "rockDropCount": [2, 4] },
        { "mineralType": "Coal", "chance": 0.8, "minDepth": 0, "maxDepth": 19, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 12,
          "perChunk": [23, 35], "rockDropWeight": 0.8, "rockDropCount": [2, 4] },
        { "mineralType": "Copper", "chance": 0.6, "minDepth": 0, "maxDepth": 19, "minCount": 1, "maxCount": 8, "rarity": "Rare", "attemptsPerChunk": 12,
          "perChunk": [20, 45], "rockDropWeight": 0.6, "rockDropCount": [1, 8] },
        { "mineralType": "Iron", "chance": 0.4, "minDepth": 0, "maxDepth": 19, "minCount": 1, "maxCount": 8, "rarity": "Rare", "attemptsPerChunk": 12,
          "perChunk": [12, 32], "rockDropWeight": 0.4, "rockDropCount": [1, 8] }
      ]
```

- [ ] **Step 2: Ice층 `minerals` 배열 교체**

```json
      "minerals": [
        { "mineralType": "Meteorite", "chance": 0.5, "minDepth": 20, "maxDepth": 39, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Fossil", "chance": 0.5, "minDepth": 20, "maxDepth": 39, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Silver", "chance": 0.5, "minDepth": 20, "maxDepth": 39, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Sapphire", "chance": 0.5, "minDepth": 20, "maxDepth": 39, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Emerald", "chance": 0.5, "minDepth": 20, "maxDepth": 39, "minCount": 1, "maxCount": 2, "rarity": "Rare", "attemptsPerChunk": 4,
          "perChunk": [1, 5], "rockDropWeight": 0.5, "rockDropCount": [1, 2] },
        { "mineralType": "Topaz", "chance": 0.5, "minDepth": 20, "maxDepth": 39, "minCount": 1, "maxCount": 2, "rarity": "Rare", "attemptsPerChunk": 4,
          "perChunk": [1, 5], "rockDropWeight": 0.5, "rockDropCount": [1, 2] },
        { "mineralType": "Copper", "chance": 0.08, "minDepth": 20, "maxDepth": 29, "minCount": 1, "maxCount": 2, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [0, 1], "rockDropWeight": 0.08, "rockDropCount": [1, 2] },
        { "mineralType": "Iron", "chance": 0.08, "minDepth": 20, "maxDepth": 29, "minCount": 1, "maxCount": 2, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [0, 1], "rockDropWeight": 0.08, "rockDropCount": [1, 2] }
      ]
```

- [ ] **Step 3: MagmaRock층 `minerals` 배열 교체**

```json
      "minerals": [
        { "mineralType": "Obsidian", "chance": 0.5, "minDepth": 40, "maxDepth": 59, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Quartz", "chance": 0.5, "minDepth": 40, "maxDepth": 59, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Gold", "chance": 0.5, "minDepth": 40, "maxDepth": 59, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Ruby", "chance": 0.5, "minDepth": 40, "maxDepth": 59, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Diamond", "chance": 0.5, "minDepth": 40, "maxDepth": 59, "minCount": 1, "maxCount": 2, "rarity": "Rare", "attemptsPerChunk": 4,
          "perChunk": [1, 5], "rockDropWeight": 0.5, "rockDropCount": [1, 2] },
        { "mineralType": "LavaStone", "chance": 0.5, "minDepth": 40, "maxDepth": 59, "minCount": 1, "maxCount": 2, "rarity": "Rare", "attemptsPerChunk": 4,
          "perChunk": [1, 5], "rockDropWeight": 0.5, "rockDropCount": [1, 2] },
        { "mineralType": "Emerald", "chance": 0.08, "minDepth": 40, "maxDepth": 49, "minCount": 1, "maxCount": 2, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [0, 1], "rockDropWeight": 0.08, "rockDropCount": [1, 2] },
        { "mineralType": "Topaz", "chance": 0.08, "minDepth": 40, "maxDepth": 49, "minCount": 1, "maxCount": 2, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [0, 1], "rockDropWeight": 0.08, "rockDropCount": [1, 2] }
      ]
```

- [ ] **Step 4: MeteoriteRock층 `minerals` 배열 교체**

```json
      "minerals": [
        { "mineralType": "Mithril", "chance": 0.5, "minDepth": 60, "maxDepth": 79, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Gravitonium", "chance": 0.5, "minDepth": 60, "maxDepth": 79, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "Uranium", "chance": 0.5, "minDepth": 60, "maxDepth": 79, "minCount": 2, "maxCount": 4, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [3, 9], "rockDropWeight": 0.5, "rockDropCount": [2, 4] },
        { "mineralType": "VoidStone", "chance": 0.5, "minDepth": 60, "maxDepth": 79, "minCount": 1, "maxCount": 2, "rarity": "Rare", "attemptsPerChunk": 4,
          "perChunk": [1, 5], "rockDropWeight": 0.5, "rockDropCount": [1, 2] },
        { "mineralType": "StarFragment", "chance": 0.5, "minDepth": 60, "maxDepth": 79, "minCount": 1, "maxCount": 2, "rarity": "Rare", "attemptsPerChunk": 4,
          "perChunk": [1, 5], "rockDropWeight": 0.5, "rockDropCount": [1, 2] },
        { "mineralType": "Diamond", "chance": 0.08, "minDepth": 60, "maxDepth": 69, "minCount": 1, "maxCount": 2, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [0, 1], "rockDropWeight": 0.08, "rockDropCount": [1, 2] },
        { "mineralType": "LavaStone", "chance": 0.08, "minDepth": 60, "maxDepth": 69, "minCount": 1, "maxCount": 2, "rarity": "Common", "attemptsPerChunk": 4,
          "perChunk": [0, 1], "rockDropWeight": 0.08, "rockDropCount": [1, 2] }
      ]
```

- [ ] **Step 5: 무결성 테스트 작성**

29개 rule을 손으로 고쳤으므로 오타·누락을 잡는 테스트가 필요하다.
`Assets/Tests/EditMode/TileDataMineralRuleTests.cs`

```csharp
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// tileData.json의 광물 규칙 스키마 무결성. 29개 rule을 손으로 마이그레이션했기 때문에
/// 오타·누락을 컴파일이 아니라 테스트로 잡아야 한다.
/// </summary>
public class TileDataMineralRuleTests
{
    private static TileDatabaseJson LoadDatabase()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "tileData.json");
        Assert.IsTrue(File.Exists(path), $"tileData.json 없음: {path}");
        var db = JsonUtility.FromJson<TileDatabaseJson>(File.ReadAllText(path));
        Assert.IsNotNull(db, "tileData.json 파싱 실패");
        Assert.IsNotNull(db.tiles, "tiles 배열 없음");
        return db;
    }

    [Test]
    public void EveryRule_HasSpawnAndDropFields()
    {
        var db = LoadDatabase();
        int ruleCount = 0;

        foreach (var tile in db.tiles)
        {
            if (tile?.minerals == null) continue;
            foreach (var rule in tile.minerals)
            {
                ruleCount++;
                string where = $"{tile.tileType}/{rule.mineralType}";

                Assert.IsTrue(MineralDensity.HasValidRange(rule.perChunk), $"{where}: perChunk 누락/길이부족");
                Assert.GreaterOrEqual(rule.perChunk[1], rule.perChunk[0], $"{where}: perChunk max < min");
                Assert.GreaterOrEqual(rule.perChunk[0], 0f, $"{where}: perChunk 음수");

                Assert.IsNotNull(rule.rockDropCount, $"{where}: rockDropCount 누락");
                Assert.AreEqual(2, rule.rockDropCount.Length, $"{where}: rockDropCount 길이는 2여야 함");
                Assert.GreaterOrEqual(rule.rockDropCount[1], rule.rockDropCount[0], $"{where}: rockDropCount max < min");
                Assert.GreaterOrEqual(rule.rockDropCount[0], 1, $"{where}: rockDropCount min은 1 이상");

                Assert.GreaterOrEqual(rule.rockDropWeight, 0f, $"{where}: rockDropWeight 음수");
                Assert.LessOrEqual(rule.rockDropWeight, 1f, $"{where}: rockDropWeight는 0~1");
            }
        }

        Assert.AreEqual(29, ruleCount, "광물 규칙 개수가 예상과 다르다 — 규칙을 추가/삭제했다면 이 숫자도 갱신할 것");
    }

    [Test]
    public void EveryRule_HasParsableRarityAndSaneDepthRange()
    {
        var db = LoadDatabase();

        foreach (var tile in db.tiles)
        {
            if (tile?.minerals == null) continue;
            foreach (var rule in tile.minerals)
            {
                string where = $"{tile.tileType}/{rule.mineralType}";
                // rarity 오타는 조용히 Common으로 처리돼 버린다 — 명시적으로 검사한다
                bool known = rule.rarity == "Common" || rule.rarity == "Rare";
                Assert.IsTrue(known, $"{where}: rarity가 'Common'/'Rare'가 아님 → '{rule.rarity}'");
                Assert.GreaterOrEqual(rule.maxDepth, rule.minDepth, $"{where}: maxDepth < minDepth");
            }
        }
    }

    [Test]
    public void DensityMultipliers_AreNonNegative()
    {
        var db = LoadDatabase();
        Assert.GreaterOrEqual(db.globalMineralDensity, 0f, "globalMineralDensity 음수");
        foreach (var tile in db.tiles)
            Assert.GreaterOrEqual(tile.mineralDensity, 0f, $"{tile.tileType}: mineralDensity 음수");
    }
}
```

- [ ] **Step 6: 테스트 실행**

Unity Test Runner (EditMode) → `TileDataMineralRuleTests`
기대: 3개 전부 PASS. 실패하면 해당 층/광물 이름이 메시지에 찍히므로 그 rule을 고친다.

- [ ] **Step 7: 동작 무변경 확인**

플레이 모드로 지상 몇 청크 파본다.
기대: 광물 양이 이전과 동일. (아직 구 경로가 동작 중이므로 달라지면 JSON 편집 중 구 필드를 건드린 것이다)

- [ ] **체크포인트** — UVCS 체크인: `tileData.json 29개 rule에 perChunk / rockDrop* 병기`

---

## Task 4: `MineralGenerator`를 `perChunk` 기반으로 전환

**파일:**
- 수정: `Assets/Scripts/Gameplay/Terrain/Tiles/MineralGenerator.cs:37-101`
- 수정: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/Decorators/MineralDecorator.cs:11-19`

**인터페이스:**
- 소비: `MineralDensity.ExpectedCount` / `.ProbabilisticRound` (Task 2), `MineralRuleJson.perChunk` (Task 1), `TileDataManager.GlobalMineralDensity` (Task 1)
- 산출: `MineralGenerator.GenerateMinerals(TerrainChunk, List<MineralRuleJson>, Vector2Int, int, TileType, List<Rect>, float)` — 마지막 `densityMultiplier` 파라미터는 기본값 `1f`

**여기서 처음으로 실제 스폰 경로가 바뀐다.**

- [ ] **Step 1: `GenerateMinerals` 교체**

`MineralGenerator.cs`의 `GenerateMinerals`와 `TrySpawnMineralsFromRule` 두 메서드(37~101행)를 아래 하나로 교체한다.

```csharp
    public static void GenerateMinerals(TerrainChunk chunk, List<MineralRuleJson> mineralRules, Vector2Int coord, int worldSeed, TileType tileType, List<Rect> excludedAreas = null, float densityMultiplier = 1f)
    {
        if (mineralRules == null || mineralRules.Count == 0 || chunk == null) return;
        // Phase 2(장식) 시점에 InitDistanceFieldJob이 pixelInfo를 읽는 중일 수 있으므로,
        // PixelInfo 쓰기 전 반드시 모든 잡을 완료해야 한다.
        chunk.EnsureJobsCompleted();

        System.Random prng = GetDeterministicRandom(coord, worldSeed);
        int currentDepth = Mathf.Abs(coord.y);

        foreach (var rule in mineralRules)
        {
            // [중요] 난수 2개는 rule마다 무조건 소비한다.
            // 깊이 범위를 벗어난 rule에서 건너뛰면 난수 스트림이 깊이별로 어긋나
            // 같은 시드에서도 배치가 요동친다.
            double n = MineralDensity.ExpectedCount(rule, currentDepth, densityMultiplier, prng.NextDouble());
            int count = MineralDensity.ProbabilisticRound(n, prng.NextDouble());

            if (count <= 0) continue;

            try
            {
                if (!TryGetMineralData(rule.mineralType, out MineralSO mineralSO)) continue;

                for (int i = 0; i < count; i++)
                {
                    if (GetRandomValidPosition(chunk, prng, out int x, out int y, excludedAreas))
                        SpawnAndCarveMineral(chunk, x, y, mineralSO);
                }
            }
            catch (System.Exception e)
            {
                // rule 하나가 터져도 나머지 광물은 정상 생성돼야 한다
                Debug.LogError($"[MineralGenerator] Error generating mineral '{rule?.mineralType}': {e.Message}\n{e.StackTrace}");
            }
        }
    }
```

`GetDeterministicRandom` 이하 나머지 메서드는 **전부 그대로 둔다.**

- [ ] **Step 2: `MineralDecorator`에서 배율 전달**

`MineralDecorator.cs`의 `Decorate` 본문을 아래로 교체한다.

```csharp
    public void Decorate(TerrainChunk chunk, DecorationContext context)
    {
        if (TileDataManager.Instance == null) return;

        TileDataJson tileData = TileDataManager.Instance.GetData(context.TargetTileType);
        if (tileData == null || tileData.minerals == null || tileData.minerals.Count == 0) return;

        // 층 배율 × 전역 배율. 개수 산출은 MineralDensity가, 깊이 곡선은 rule의 rarity가 결정한다.
        // 배경: Assets/Docs/mineral-density-redesign.md §3-2
        float density = tileData.mineralDensity * TileDataManager.Instance.GlobalMineralDensity;

        // PreOccupiedAreas: ElevatorDecorator가 등록한 엘리베이터 점유 영역 전달 → 해당 영역에 광물 스폰 제외
        MineralGenerator.GenerateMinerals(chunk, tileData.minerals, context.Coord, context.WorldSeed,
                                          context.TargetTileType, context.PreOccupiedAreas, density);
    }
```

- [ ] **Step 3: 컴파일 확인**

기대: 에러 0.

- [ ] **Step 4: 스폰 양 육안 확인**

플레이 모드에서 **새 세이브**로 시작해 지상(Dirt) 청크 몇 개를 파본다.
기대: 마이그레이션 전 스크린샷과 비슷한 밀도 (청크당 약 100개 규모).

⚠ 난수 스트림 소비 순서가 바뀌었으므로 **광물 위치는 달라진다. 이건 정상이다** (설계 문서 §6-1). 확인할 것은 위치가 아니라 **양**이다.

- [ ] **Step 5: 전역 배율 동작 확인**

`tileData.json` 최상위에 `"globalMineralDensity": 2.0`을 임시로 넣고 플레이 → 광물이 약 2배로 늘어나는지 확인한다.
확인 후 **값을 지우거나 `1.0`으로 되돌린다.**

- [ ] **체크포인트** — UVCS 체크인: `MineralGenerator를 perChunk + 밀도 배율 기반으로 전환`

---

## Task 5: `DiggableRock`를 전용 필드로 전환

**파일:**
- 수정: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/DiggableRock.cs:550-578`

**인터페이스:**
- 소비: `MineralRuleJson.rockDropWeight` / `.RockDropMin` / `.RockDropMax` (Task 1)

로직은 손대지 않는 **순수 이름 치환**이다. `rockDropWeight`는 구 `chance`와 값이 같고, `RockDropMin/Max`는 구 `minCount/maxCount`와 값이 같으므로 **드랍 결과가 완전히 동일해야 한다.**

- [ ] **Step 1: 가중치 계산 치환**

`DiggableRock.cs`의 550~562행 주석과 가중치 루프를 아래로 교체한다.

```csharp
        // 깊이 기반 가중치로 1종류 선택 (rockDropWeight=표면 가중치, 1-rockDropWeight=깊이 가중치)
        // 예: Copper 0.6 → 표면 60%, 깊이 40% / Iron 0.4 → 표면 40%, 깊이 60%
        float totalWeight = 0f;
        var weights = new float[eligibleRares.Count];
        for (int i = 0; i < eligibleRares.Count; i++)
        {
            var r = eligibleRares[i];
            float rt = (r.maxDepth > r.minDepth)
                ? Mathf.Clamp01((float)(depth - r.minDepth) / (r.maxDepth - r.minDepth))
                : 1f;
            weights[i] = Mathf.Lerp(r.rockDropWeight, 1f - r.rockDropWeight, rt);
            totalWeight += weights[i];
        }
```

- [ ] **Step 2: 드랍 개수 계산 치환**

573~578행을 아래로 교체한다.

```csharp
        // 드롭 수량은 깊이에 따라 선형 증가 (minDepth → RockDropMin, maxDepth → RockDropMax)
        float t = (picked.maxDepth > picked.minDepth)
            ? Mathf.Clamp01((float)(depth - picked.minDepth) / (picked.maxDepth - picked.minDepth))
            : 1f;
        int scaledMaxCount = Mathf.Max(picked.RockDropMin, Mathf.RoundToInt(Mathf.Lerp(picked.RockDropMin, picked.RockDropMax, t)));
        int count = Random.Range(picked.RockDropMin, scaledMaxCount + 1);
```

- [ ] **Step 3: 컴파일 확인**

기대: 에러 0.

- [ ] **Step 4: 돌 드랍 육안 확인**

플레이 모드에서 Dirt층 `DiggableRock`을 5개쯤 캐본다.
기대: 나오는 광물 종류와 개수가 이전과 같은 범위 (Dirt 얕은 곳이면 Copper/Iron이 1~8개 범위).

`DiggableRock`은 `UnityEngine.Random`을 쓰므로 시드 재현이 안 된다. 정확한 값 비교가 아니라 **범위가 이상하지 않은지**를 본다. 1개만 나오거나 수십 개가 쏟아지면 치환이 잘못된 것이다.

- [ ] **체크포인트** — UVCS 체크인: `DiggableRock을 rockDropWeight / rockDropCount로 전환`

---

## Task 6: 구 필드 제거 + 로드 검증 로그

**파일:**
- 수정: `Assets/Scripts/_Core/Data/TileDataModels.cs` (`MineralRuleJson`의 Deprecated 블록)
- 수정: `Assets/StreamingAssets/tileData.json` (29개 rule에서 키 4개 삭제)
- 수정: `Assets/Scripts/_Core/Managers/TileDataManager.cs` (`LoadTileData`)

**인터페이스:**
- 산출: `MineralRuleJson`에 `chance` / `minCount` / `maxCount` / `attemptsPerChunk` 없음

- [ ] **Step 1: 구 필드 참조가 0인지 확인**

Grep으로 확인한다: `\.chance|\.minCount|\.maxCount|\.attemptsPerChunk` (경로 `Assets/Scripts`)

기대: `MineralRuleJson`과 무관한 것만 남는다 —
`LootTable.cs:49,52` (`LootEntry`), `SpecialChunkSettingsData.cs:36`, `DungeonRoomComposer.cs:153`.
**`MineralGenerator` / `DiggableRock`에 하나라도 남아 있으면 Task 4·5가 덜 끝난 것이다. 여기서 멈추고 되돌아간다.**

- [ ] **Step 2: `MineralRuleJson`에서 Deprecated 블록 삭제**

Task 1에서 남겨둔 아래 4줄과 그 위 주석 줄을 통째로 지운다.

```csharp
    // ─── [Deprecated] Task 6에서 제거. 아래 4개는 두 시스템이 뒤섞여 쓰던 필드다. ───
    public float chance;
    public int minCount;
    public int maxCount;
    public int attemptsPerChunk = 1;
```

- [ ] **Step 3: `tileData.json`에서 구 키 4개 삭제**

29개 rule 각각에서 `"chance"`, `"minCount"`, `"maxCount"`, `"attemptsPerChunk"` 키를 지운다.
남는 rule의 형태는 정확히 아래와 같아야 한다(Dirt/Coal 예시).

```json
        { "mineralType": "Coal", "minDepth": 0, "maxDepth": 19, "rarity": "Common",
          "perChunk": [23, 35], "rockDropWeight": 0.8, "rockDropCount": [2, 4] },
```

`JsonUtility`는 클래스에 없는 JSON 키를 조용히 무시하므로 지우지 않아도 동작은 하지만, 남겨두면 **다음 사람이 다시 그 값을 만지게 된다.** 반드시 지운다.

- [ ] **Step 4: 로드 시 규칙 검증 추가**

`TileDataManager.cs`에 메서드를 추가한다.

```csharp
    /// <summary>
    /// 광물 규칙의 필수 필드 누락을 로드 시 1회 리포트한다.
    /// 런타임에 매 청크마다 경고를 뿜으면 로그가 범람하므로 여기서만 검사한다.
    /// 누락된 rule은 스폰 0개로 조용히 넘어가기 때문에(설계 §6-3) 이 로그가 유일한 단서다.
    /// </summary>
    private static void ValidateMineralRules(List<TileDataJson> tiles)
    {
        if (tiles == null) return;

        var problems = new List<string>();
        foreach (var tile in tiles)
        {
            if (tile?.minerals == null) continue;
            foreach (var rule in tile.minerals)
            {
                if (rule == null) { problems.Add($"{tile.tileType}: null 규칙 항목"); continue; }

                if (!MineralDensity.HasValidRange(rule.perChunk))
                    problems.Add($"{tile.tileType}/{rule.mineralType}: perChunk 누락 또는 길이<2 → 청크 스폰 0개");

                if (rule.rockDropCount == null || rule.rockDropCount.Length < 2)
                    problems.Add($"{tile.tileType}/{rule.mineralType}: rockDropCount 누락 → 돌 드랍 개수가 폴백값(1)으로 처리됨");
            }
        }

        if (problems.Count > 0)
            Debug.LogWarning($"[TileDataManager] 광물 규칙 {problems.Count}건 확인 필요:\n - " + string.Join("\n - ", problems));
    }
```

`LoadTileData()`의 `_sortedTileData.Sort(...)` 호출 바로 다음 줄에 호출을 추가한다.

```csharp
            ValidateMineralRules(database.tiles);
```

- [ ] **Step 5: 검증 로그가 실제로 뜨는지 확인**

`tileData.json`에서 Dirt/Coal의 `"perChunk": [23, 35],`를 임시로 지우고 플레이한다.
기대: 콘솔에 `[TileDataManager] 광물 규칙 1건 확인 필요: - Dirt/Coal: perChunk 누락...` 경고.

확인 후 **지운 줄을 반드시 되돌린다.**

- [ ] **Step 6: 전체 테스트 실행**

Unity Test Runner (EditMode) 전체 실행.
기대: `MineralDensityTests` 15개 + `TileDataMineralRuleTests` 3개 PASS, 기존 테스트 회귀 없음.
특히 `MineralUpgradeLadderTests`는 `mineralType`/`IsRare`만 쓰므로 그대로 통과해야 한다.

- [ ] **Step 7: 최종 육안 확인**

새 세이브로 Dirt → Ice → MagmaRock까지 내려가며 각 층 광물이 나오는지, 돌 드랍이 정상인지 본다.

- [ ] **체크포인트** — UVCS 체크인: `광물 규칙 구 필드 제거 + 로드 검증 로그 추가`

---

## 완료 후 — 밸런스 조정 (별도 작업)

여기까지가 **구조 변경**이다. 설계 문서 §2-3의 층별 밀도 편차는 그대로 남아 있다.

| 층 | 층 최상단 | 층 최하단 |
|---|---|---|
| Dirt | 약 110개 | 약 109개 |
| Ice / MagmaRock | 약 25개 | 약 18개 |
| MeteoriteRock | 약 19개 | 약 15개 |

이제 이걸 조절하는 방법은 두 가지다.

1. **층 배율** — `tileData.json`의 각 층에 `"mineralDensity": 0.25` 식으로 넣으면 그 층 전체가 스케일된다. Dirt만 낮추려면 이게 제일 간단하다.
2. **개별 `perChunk`** — 특정 광물만 조절할 때.

전체를 한 번에 늘리려면 최상위 `"globalMineralDensity"`를 올린다.

**구조 변경과 밸런스 변경은 반드시 별도 체크인으로 나눈다.** 한 번에 하면 결과가 달라졌을 때 원인이 어느 쪽인지 가릴 수 없다.

---

## 자체 점검 결과

설계 문서 대비 커버리지를 확인했다.

| 설계 항목 | 담당 태스크 |
|---|---|
| §3-1 스키마 분리 (`perChunk`/`rockDropWeight`/`rockDropCount`) | Task 1, Task 6 |
| §3-1 `rarity`/`minDepth`/`maxDepth` 공유 유지 | Task 1 (건드리지 않음) |
| §3-2 층·전역 밀도 배율 | Task 1(정의) + Task 4(적용) |
| §3-3 개수 계산식 + `attempts` 루프 제거 | Task 2, Task 4 |
| §3-4 `ProbabilisticRound` (청크 시드 사용) | Task 2, Task 4 |
| §3-5 `DiggableRock` 치환 + `RockDropMin/Max` 프로퍼티 | Task 1(프로퍼티), Task 5(치환) |
| §4 마이그레이션 표 29개 rule | Task 3 |
| §6-1 난수 스트림 변경 고지 | Task 4 Step 4 |
| §6-3 `perChunk` 누락 시 0개 + 로드 검증 | Task 2(0개), Task 6(로그) |
| §6-4 밸런스 미변경 | 전역 제약 + "완료 후" 절 |
| §7 검증 1~5 | Task 2(1·2·3), Task 5 Step 4(4), Task 4 Step 4(5) |
| §8 하지 않는 것 | 전역 제약 |

누락 없음. 태스크 간 타입·이름 일치 확인 완료 (`MineralDensity.ExpectedCount` / `.ProbabilisticRound` / `.HasValidRange`, `RockDropMin` / `RockDropMax`, `GlobalMineralDensity`).
