# Job 파이프라인 낭비 제거 — 구현 계획

> **작업자 안내:** 이 계획은 태스크 단위로 실행한다. 각 스텝은 체크박스(`- [ ]`)로 추적한다.
> 설계 근거는 `Assets/Docs/job-pipeline-waste-removal.md`를 **먼저 읽을 것.**

**목표:** dirty 청크 1개당 사이클 잡 스케줄 16 → 11, 워크아이템 ~5.5M → ~50k로 줄인다. 렌더 결과는 동일하게 유지한다.

**접근:** ① Job struct들을 dirty rect 크기로 dispatch → ② `ChunkJobScheduler`의 복합 메서드를 잡 1:1 메서드로 분리 → ③ Round1/Round2가 각자 필요한 잡만 고르게 재조립.

**기술 스택:** Unity 2D / C# / Burst / Unity Job System (`IJob`, `IJobParallelFor`) / NUnit EditMode

---

## Global Constraints

- **버전 관리는 UVCS(Unity Version Control)다. `git` 명령어를 절대 사용하지 않는다.** 체크인은 사람이 수행한다.
- **Unity Test Runner 실행은 사람이 한다.** Claude는 테스트 파일을 작성만 하고 `mcp__mcp-unity__run_tests` 등을 호출하지 않는다.
  테스트 통과를 다음 태스크의 게이트로 삼지 않는다 — 작성 후 그대로 진행한다.
- 더티 플래그는 반드시 `ChunkData.MarkDirty()` / `MarkRenderDirty()` 메서드로 세팅한다 (플래그 3개 직접 나열 금지).
- `TryUpdateCollider()`를 `ApplyTexture()` 안으로 되돌리지 않는다 (콜라이더/비주얼 분리 — `collider-visual-decoupling.md`).
- `InfinityMapManager`는 partial class다. `InfinityMapManager.cs`와 `InfinityMapManager.Data.cs` 양쪽을 확인한다.
- **설계 문서 §9 "건드리면 안 되는 기존 동작"을 위반하지 않는다.** 특히 `ScheduleBoundarySync` 호출 조건을 바꾸지 않는다.

### 상수 (설계 문서에서 그대로 옮김)

| 이름 | 값 | 위치 |
|---|---|---|
| `EXT_MARGIN` | 50 | `ChunkJobScheduler` |
| `SYNC_DEPTH` (스케줄러) | 50 (full px) | `ChunkJobScheduler.ScheduleBoundarySync` |
| `SYNC_DEPTH` (잡 내부) | 25 (half cells) | `TerrainJobs.BoundarySyncJob` |
| `PROP_MARGIN` | 50 | `ChunkJobScheduler.ScheduleBoundarySync` |
| `JOB_BATCH_SIZE` | 64 | `ChunkJobScheduler` |
| `maxDist` | 255 | 전 잡 공통 |

---

## 파일 구조

| 파일 | 책임 | 이번 변경 |
|---|---|---|
| `Assets/Scripts/_Core/Managers/TerrainJobs.cs` | Burst job struct 정의 | 4개 잡을 rect-local dispatch로 전환 (Task 2) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs` | JobHandle·의존성·스케줄 | 복합 메서드 → 잡 1:1 메서드 분리 (Task 3) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainVisualizer.cs` | 스케줄러 조합 + GPU 업로드 | Round1/Round2 Pass 메서드 신설 (Task 4) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` | 청크 MonoBehaviour | Do*VisualUpdate* 4개 → Pass 2개 (Task 4, 6) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs` | LateUpdate 파이프라인 | 스냅샷 튜플 + Round1/Round2 재조립 (Task 5) |
| `Assets/Scripts/_Core/Data/WorldSettingsData.cs` | JSON 데이터 모델 | `enableRound1Preview` 추가 (Task 5) |
| `Assets/StreamingAssets/worldSettings.json` | 런타임 설정 | 동상 (Task 5) |
| `Assets/Tests/EditMode/EditModeTests.asmdef` | 테스트 어셈블리 | Collections/Burst/Mathematics 참조 추가 (Task 1) |
| `Assets/Tests/EditMode/ChamferConvergenceTests.cs` | **신규** — §3.1 논증 증명 | Task 1 |
| `Assets/Tests/EditMode/TerrainJobsRectScopeTests.cs` | **신규** — rect 스코프 dispatch 검증 | Task 2 |

---

## Task 1: Chamfer 수렴 증명 테스트

**이 태스크가 이 계획 전체의 전제를 검증한다.** 설계 §3.1의 "Round2 Init 제거 가능" 논증이 틀렸다면
여기서 드러나고, 그러면 Task 5를 진행하면 안 된다.

**Files:**
- Modify: `Assets/Tests/EditMode/EditModeTests.asmdef`
- Create: `Assets/Tests/EditMode/ChamferConvergenceTests.cs`

**Interfaces:**
- Consumes: `TerrainJobs.ChamferForwardPassJob`, `TerrainJobs.ChamferBackwardPassJob` (현행 그대로, 변경 없음)
- Produces: 없음 (검증 전용)

---

- [ ] **Step 1: 테스트 어셈블리에 Job 패키지 참조 추가**

현재 `EditModeTests.asmdef`는 `GameScripts`만 참조해서 `NativeArray`를 쓸 수 없다.
어셈블리 참조는 전이(transitive)되지 않으므로 직접 추가해야 한다.

`Assets/Tests/EditMode/EditModeTests.asmdef` 전체를 아래로 교체:

```json
{
    "name": "EditModeTests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "GameScripts",
        "Unity.Collections",
        "Unity.Burst",
        "Unity.Mathematics"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: 수렴 증명 테스트 작성**

핵심 주장: **"Round1이 남긴 DF(참값의 상한)에서 시작한 min-전파"** 와
**"maxDist로 리셋하고 다시 깐 min-전파"** 는 같은 결과에 도달한다.

`Assets/Tests/EditMode/ChamferConvergenceTests.cs` 신규 작성:

```csharp
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;   // ⚠ 필수 — .Run()은 IJobExtensions / IJobParallelForExtensions의 확장 메서드다
using UnityEngine;

/// <summary>
/// 설계 §3.1 증명 — Round2의 InitBFSJob(리셋)을 제거해도 Chamfer 결과가 동일한지.
///
/// Chamfer 2패스는 min-전파다. 시작값이 참값의 상한(upper bound)이면
/// 리셋 여부와 무관하게 같은 고정점으로 수렴해야 한다.
/// 이 테스트가 실패하면 Task 5(Round2 Init 제거)를 진행하면 안 된다.
///
/// 배경: Assets/Docs/job-pipeline-waste-removal.md
/// </summary>
public class ChamferConvergenceTests
{
    private const int W = 16;
    private const int H = 16;
    private const int MAXD = 255;

    [Test]
    public void Chamfer_FromConvergedUpperBound_EqualsFromMaxDistReset()
    {
        // 청크 내부에 공기 한 점 (파낸 구멍)
        var air = new bool[W * H];
        air[5 * H + 5] = true;

        var baseData = MakeBase(air);
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp); // 잡은 Length>0일 때만 읽는다

        // ── Round1: maxDist 리셋 → chamfer. 결과 = "이웃 영향 없는 자기 청크만의 DF" (= 참값의 상한)
        var dfRound1 = MakeResetField(air);
        RunChamfer(dfRound1, baseData, pixelInfo);

        // ── Path B (개선안): Round1 결과 위에 경계 시드를 min-merge → chamfer. Init 리셋 없음.
        var dfB = new NativeArray<ushort>(W * H, Allocator.Temp);
        dfRound1.CopyTo(dfB);
        ApplyBoundarySeed(dfB);
        RunChamfer(dfB, baseData, pixelInfo);

        // ── Path C (현행): maxDist 리셋 → 경계 시드 → chamfer.
        var dfC = MakeResetField(air);
        ApplyBoundarySeed(dfC);
        RunChamfer(dfC, baseData, pixelInfo);

        for (int i = 0; i < W * H; i++)
            Assert.AreEqual(dfC[i], dfB[i], $"불일치 index={i} (x={i % W}, y={i / W})");

        dfRound1.Dispose(); dfB.Dispose(); dfC.Dispose();
        baseData.Dispose(); pixelInfo.Dispose();
    }

    [Test]
    public void Chamfer_FromConvergedUpperBound_EqualsReset_WhenNeighborSeedIsHigher()
    {
        // 경계 시드가 자기 DF보다 "큰" 경우 — min-merge라 무시되어야 한다.
        var air = new bool[W * H];
        air[0 * H + 0] = true;   // 좌하단 모서리에 공기 → 왼쪽 경계 근처 DF가 이미 작다
        var baseData = MakeBase(air);
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);

        var dfRound1 = MakeResetField(air);
        RunChamfer(dfRound1, baseData, pixelInfo);

        var dfB = new NativeArray<ushort>(W * H, Allocator.Temp);
        dfRound1.CopyTo(dfB);
        ApplyHighBoundarySeed(dfB);
        RunChamfer(dfB, baseData, pixelInfo);

        var dfC = MakeResetField(air);
        ApplyHighBoundarySeed(dfC);
        RunChamfer(dfC, baseData, pixelInfo);

        for (int i = 0; i < W * H; i++)
            Assert.AreEqual(dfC[i], dfB[i], $"불일치 index={i} (x={i % W}, y={i / W})");

        dfRound1.Dispose(); dfB.Dispose(); dfC.Dispose();
        baseData.Dispose(); pixelInfo.Dispose();
    }

    // ────────────────────────────────────────────────────────────────

    private static NativeArray<Color32> MakeBase(bool[] air)
    {
        var a = new NativeArray<Color32>(W * H, Allocator.Temp);
        for (int i = 0; i < W * H; i++)
            a[i] = air[i] ? new Color32(0, 0, 0, 0) : new Color32(0, 0, 0, 255);
        return a;
    }

    /// InitBFSJob의 dirty 분기와 동일: 공기→0, 솔리드→maxDist
    private static NativeArray<ushort> MakeResetField(bool[] air)
    {
        var df = new NativeArray<ushort>(W * H, Allocator.Temp);
        for (int i = 0; i < W * H; i++)
            df[i] = air[i] ? (ushort)0 : (ushort)MAXD;
        return df;
    }

    /// BoundarySyncJob의 좌측 엣지 로직과 동일한 min-merge (이웃 거리가 낮은 경우)
    private static void ApplyBoundarySeed(NativeArray<ushort> df)
    {
        for (int y = 0; y < H; y++)
            for (int k = 0; k < 4; k++)
            {
                int idx = y * W + k;
                ushort candidate = (ushort)(5 + k * 5);
                if (candidate < df[idx]) df[idx] = candidate;
            }
    }

    /// 이웃 거리가 이미 큰 경우 — min-merge에서 채택되지 않아야 한다
    private static void ApplyHighBoundarySeed(NativeArray<ushort> df)
    {
        for (int y = 0; y < H; y++)
            for (int k = 0; k < 4; k++)
            {
                int idx = y * W + k;
                ushort candidate = (ushort)(200 + k * 5);
                if (candidate < df[idx]) df[idx] = candidate;
            }
    }

    private static void RunChamfer(NativeArray<ushort> df, NativeArray<Color32> baseData, NativeArray<byte> info)
    {
        new TerrainJobs.ChamferForwardPassJob
        {
            distanceField = df, baseData = baseData, pixelInfo = info,
            width = W, height = H, maxDist = MAXD,
            extMinX = 0, extMinY = 0, extMaxX = W, extMaxY = H
        }.Run();

        new TerrainJobs.ChamferBackwardPassJob
        {
            distanceField = df, baseData = baseData, pixelInfo = info,
            width = W, height = H, maxDist = MAXD,
            extMinX = 0, extMinY = 0, extMaxX = W, extMaxY = H
        }.Run();
    }
}
```

- [ ] **Step 3: 컴파일 확인**

Unity 에디터로 전환해 컴파일 에러가 없는지 확인한다.
`NativeArray` / `TerrainJobs` 심볼을 못 찾으면 Step 1의 asmdef가 반영되지 않은 것이다 (에디터 재시작).

- [ ] **Step 4: 사람이 테스트 실행 — 이 태스크만은 게이트다**

Unity Test Runner (EditMode) → `ChamferConvergenceTests` 2개 실행.

**두 테스트가 통과해야 Task 5를 진행할 수 있다.**
실패하면 §3.1의 논증이 틀렸다는 뜻이므로 설계로 되돌아간다 (Task 2~4는 §3.1과 무관하므로 그대로 진행 가능).

- [ ] **Step 5: 체크포인트**

변경 파일: `EditModeTests.asmdef`, `ChamferConvergenceTests.cs`
UVCS 체크인은 사람이 수행한다. **git 명령어를 쓰지 않는다.**

---

## Task 2: `TerrainJobs` — rect-local dispatch

4개 `IJobParallelFor`를 dirty rect 넓이만큼만 dispatch한다. 결과 픽셀은 동일하다.

**Files:**
- Modify: `Assets/Scripts/_Core/Managers/TerrainJobs.cs`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs` (호출부 필드명 맞추기)
- Create: `Assets/Tests/EditMode/TerrainJobsRectScopeTests.cs`

**Interfaces:**
- Produces (Task 3이 이 필드명으로 스케줄한다):
  - `InitBFSJob`: `extMinX, extMinY, extWidth` (+ 기존 `dirtyMinX/Y/MaxX/MaxY`, `width`, `maxDist`)
  - `DownsampleMaskJob`: `rectMinX, rectMinY, rectWidth` (+ 기존 `fullWidth`, `fullHeight`, `halfWidth`)
  - `UpsampleDistanceJob`: `rectMinX, rectMinY, rectWidth` (+ 기존 `fullWidth`, `halfWidth`, `maxDist`)
  - `VisualUpdateJob`: `rectMinX, rectMinY, rectWidth` (기존 `visMinX/visMinY/visMaxX/visMaxY` **제거**)
- 스케줄 개수는 항상 `rectWidth * rectHeight`.

### ⚠ 이 태스크의 유일한 함정

지금은 **job index == array index**라서 `distanceField` / `fullField` / `baseHalf` / `pixelInfoHalf`에
안전 속성이 없어도 통과했다. rect-local로 바꾸면 **job index ≠ array index**가 되므로
Unity Job Safety System이 `IndexOutOfRangeException` 또는
"job is not allowed to write to NativeArray at index N" 을 던진다.

→ **쓰기 대상 NativeArray 전부에 `[NativeDisableParallelForRestriction]`을 붙여야 한다.**
(`VisualUpdateJob.outputTexture`에는 이미 붙어 있다.)

---

- [ ] **Step 1: 실패하는 테스트 작성 (rect 밖은 안 건드린다)**

`Assets/Tests/EditMode/TerrainJobsRectScopeTests.cs` 신규 작성:

```csharp
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;   // ⚠ 필수 — .Run(n)은 IJobParallelForExtensions의 확장 메서드다
using UnityEngine;

/// <summary>
/// rect-local dispatch 검증 — 잡이 rect 안만 쓰고 rect 밖은 건드리지 않는지.
/// 배경: Assets/Docs/job-pipeline-waste-removal.md §5.4
/// </summary>
public class TerrainJobsRectScopeTests
{
    private const int FULL_W = 16;
    private const int FULL_H = 16;
    private const int HALF_W = 8;
    private const int HALF_H = 8;
    private const int MAXD = 255;
    private const ushort SENTINEL = 12345;

    [Test]
    public void DownsampleMaskJob_WritesOnlyInsideRect()
    {
        var baseData  = new NativeArray<Color32>(FULL_W * FULL_H, Allocator.Temp);
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var baseHalf  = new NativeArray<Color32>(HALF_W * HALF_H, Allocator.Temp);
        var infoHalf  = new NativeArray<byte>(HALF_W * HALF_H, Allocator.Temp);

        // 전부 솔리드, 단 full (4,4)만 공기 → half (2,2)가 공기가 되어야 한다
        for (int i = 0; i < baseData.Length; i++) baseData[i] = new Color32(0, 0, 0, 255);
        baseData[4 * FULL_W + 4] = new Color32(0, 0, 0, 0);

        // half 전체를 구분 가능한 값으로 미리 채움 → rect 밖이 보존되는지 확인용
        for (int i = 0; i < baseHalf.Length; i++) baseHalf[i] = new Color32(9, 9, 9, 99);

        // half rect = (1,1)~(4,4)
        new TerrainJobs.DownsampleMaskJob
        {
            baseData = baseData, pixelInfo = pixelInfo,
            baseHalf = baseHalf, pixelInfoHalf = infoHalf,
            fullWidth = FULL_W, fullHeight = FULL_H, halfWidth = HALF_W,
            rectMinX = 1, rectMinY = 1, rectWidth = 3
        }.Run(3 * 3);

        // rect 안: half(2,2)는 공기(alpha 0)
        Assert.AreEqual(0, baseHalf[2 * HALF_W + 2].a, "rect 안 half(2,2)는 공기여야 한다");
        // rect 안: half(1,1)은 솔리드(alpha 255)
        Assert.AreEqual(255, baseHalf[1 * HALF_W + 1].a, "rect 안 half(1,1)은 솔리드여야 한다");
        // rect 밖: 미리 채운 sentinel이 그대로
        Assert.AreEqual(99, baseHalf[0 * HALF_W + 0].a, "rect 밖 half(0,0)은 보존돼야 한다");
        Assert.AreEqual(99, baseHalf[7 * HALF_W + 7].a, "rect 밖 half(7,7)은 보존돼야 한다");

        baseData.Dispose(); pixelInfo.Dispose(); baseHalf.Dispose(); infoHalf.Dispose();
    }

    [Test]
    public void InitBFSJob_ResetsDirtyRect_PreservesSolidOutsideDirty_SkipsOutsideExt()
    {
        var baseData  = new NativeArray<Color32>(HALF_W * HALF_H, Allocator.Temp);
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var df        = new NativeArray<ushort>(HALF_W * HALF_H, Allocator.Temp);

        for (int i = 0; i < baseData.Length; i++) baseData[i] = new Color32(0, 0, 0, 255);
        baseData[3 * HALF_W + 3] = new Color32(0, 0, 0, 0); // (3,3) 공기

        for (int i = 0; i < df.Length; i++) df[i] = SENTINEL;

        // ext = (1,1)~(6,6), dirty = (2,2)~(5,5)
        new TerrainJobs.InitBFSJob
        {
            baseData = baseData, pixelInfo = pixelInfo, distanceField = df,
            maxDist = MAXD,
            dirtyMinX = 2, dirtyMinY = 2, dirtyMaxX = 5, dirtyMaxY = 5,
            extMinX = 1, extMinY = 1, extWidth = 5,
            width = HALF_W
        }.Run(5 * 5);

        // dirty 안 솔리드 → maxDist로 리셋
        Assert.AreEqual(MAXD, df[2 * HALF_W + 2], "dirty 안 솔리드는 maxDist로 리셋");
        // dirty 안 공기 → 0
        Assert.AreEqual(0, df[3 * HALF_W + 3], "dirty 안 공기는 0");
        // ext 안 / dirty 밖 솔리드 → 기존 값(sentinel) 보존
        Assert.AreEqual(SENTINEL, df[1 * HALF_W + 1], "ext 안·dirty 밖 솔리드는 기존 값 보존");
        // ext 밖 → 아예 안 건드림
        Assert.AreEqual(SENTINEL, df[0 * HALF_W + 0], "ext 밖은 dispatch되지 않아야 한다");
        Assert.AreEqual(SENTINEL, df[7 * HALF_W + 7], "ext 밖은 dispatch되지 않아야 한다");

        baseData.Dispose(); pixelInfo.Dispose(); df.Dispose();
    }

    [Test]
    public void UpsampleDistanceJob_DoublesHalfValue_OnlyInsideRect()
    {
        var halfField = new NativeArray<ushort>(HALF_W * HALF_H, Allocator.Temp);
        var fullField = new NativeArray<ushort>(FULL_W * FULL_H, Allocator.Temp);

        for (int i = 0; i < halfField.Length; i++) halfField[i] = 10;
        for (int i = 0; i < fullField.Length; i++) fullField[i] = SENTINEL;

        // full rect = (4,4)~(8,8)
        new TerrainJobs.UpsampleDistanceJob
        {
            halfField = halfField, fullField = fullField,
            fullWidth = FULL_W, halfWidth = HALF_W, maxDist = MAXD,
            rectMinX = 4, rectMinY = 4, rectWidth = 4
        }.Run(4 * 4);

        Assert.AreEqual(20, fullField[4 * FULL_W + 4], "rect 안은 half 값 ×2");
        Assert.AreEqual(20, fullField[7 * FULL_W + 7], "rect 안은 half 값 ×2");
        Assert.AreEqual(SENTINEL, fullField[3 * FULL_W + 3], "rect 밖은 보존");
        Assert.AreEqual(SENTINEL, fullField[8 * FULL_W + 8], "rect 밖은 보존");

        halfField.Dispose(); fullField.Dispose();
    }

    [Test]
    public void UpsampleDistanceJob_ClampsToMaxDist()
    {
        var halfField = new NativeArray<ushort>(HALF_W * HALF_H, Allocator.Temp);
        var fullField = new NativeArray<ushort>(FULL_W * FULL_H, Allocator.Temp);

        for (int i = 0; i < halfField.Length; i++) halfField[i] = 200; // ×2 = 400 > 255

        new TerrainJobs.UpsampleDistanceJob
        {
            halfField = halfField, fullField = fullField,
            fullWidth = FULL_W, halfWidth = HALF_W, maxDist = MAXD,
            rectMinX = 0, rectMinY = 0, rectWidth = FULL_W
        }.Run(FULL_W * FULL_H);

        Assert.AreEqual(MAXD, fullField[0], "maxDist로 클램프돼야 한다");

        halfField.Dispose(); fullField.Dispose();
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

Unity 에디터에서 컴파일 에러가 나야 정상이다.
기대: `'DownsampleMaskJob' does not contain a definition for 'rectMinX'` 등
(아직 필드를 추가하지 않았으므로).

- [ ] **Step 3: `DownsampleMaskJob`을 rect-local로 변경**

`Assets/Scripts/_Core/Managers/TerrainJobs.cs`의 `DownsampleMaskJob` 전체를 교체:

```csharp
    [BurstCompile]
    public struct DownsampleMaskJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> baseData;
        [ReadOnly] public NativeArray<byte> pixelInfo;

        // [rect-local] job index != array index → 안전 검증 해제 필수
        [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<Color32> baseHalf;
        [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<byte> pixelInfoHalf;

        public int fullWidth, fullHeight;
        public int halfWidth;

        // [rect-local] dispatch 도메인 (half 좌표). Schedule(rectWidth * rectHeight).
        public int rectMinX, rectMinY, rectWidth;

        public void Execute(int i)
        {
            int hx = rectMinX + i % rectWidth;
            int hy = rectMinY + i / rectWidth;
            int hIndex = hy * halfWidth + hx;

            int x0 = hx << 1; // hx*2
            int y0 = hy << 1; // hy*2

            bool anyAir = false;
            bool anyIndestr = false;
            bool hasInfo = pixelInfo.Length > 0;

            for (int dy = 0; dy < 2; dy++)
            {
                int fy = y0 + dy;
                if (fy >= fullHeight) continue;
                int rowBase = fy * fullWidth;
                for (int dx = 0; dx < 2; dx++)
                {
                    int fx = x0 + dx;
                    if (fx >= fullWidth) continue;
                    int fi = rowBase + fx;
                    if (baseData[fi].a == 0) anyAir = true;
                    if (hasInfo && (pixelInfo[fi] & 128) != 0) anyIndestr = true;
                }
            }

            // 하나라도 air면 air(alpha 0), 아니면 solid(alpha 255). RGB는 미사용(Init/Chamfer는 alpha만 체크).
            baseHalf[hIndex] = anyAir ? new Color32(0, 0, 0, 0) : new Color32(0, 0, 0, 255);
            // Init/Chamfer는 pixelInfo의 &128(indestructible)만 읽으므로 그 비트만 전달.
            pixelInfoHalf[hIndex] = (byte)(anyIndestr ? 128 : 0);
        }
    }
```

- [ ] **Step 4: `UpsampleDistanceJob`을 rect-local로 변경**

같은 파일의 `UpsampleDistanceJob` 전체를 교체:

```csharp
    [BurstCompile]
    public struct UpsampleDistanceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ushort> halfField;

        // [rect-local] job index != array index → 안전 검증 해제 필수
        [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<ushort> fullField;

        public int fullWidth;
        public int halfWidth;
        public int maxDist;

        // [rect-local] dispatch 도메인 (full 좌표). Schedule(rectWidth * rectHeight).
        public int rectMinX, rectMinY, rectWidth;

        public void Execute(int i)
        {
            int x = rectMinX + i % rectWidth;
            int y = rectMinY + i / rectWidth;
            int index = y * fullWidth + x;

            int hx = x >> 1; // x / 2
            int hy = y >> 1; // y / 2
            int d = halfField[hy * halfWidth + hx] * 2; // half 1스텝 = full 2픽셀 → ×2
            fullField[index] = (ushort)(d < maxDist ? d : maxDist);
        }
    }
```

- [ ] **Step 5: `InitBFSJob`을 rect-local로 변경**

같은 파일의 `InitBFSJob` 전체를 교체:

```csharp
    [BurstCompile]
    public struct InitBFSJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> baseData;
        [ReadOnly] public NativeArray<byte> pixelInfo;

        // [rect-local] job index != array index → 안전 검증 해제 필수
        [NativeDisableParallelForRestriction] public NativeArray<ushort> distanceField;

        public int maxDist;

        // Dirty rect: 실제 변경된 영역 (maxDist로 리셋할 대상)
        public int dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY;

        // [rect-local] dispatch 도메인 = extended rect. Schedule(extWidth * extHeight).
        // ext = dirty ± EXT_MARGIN. 2패스가 이 범위 밖 픽셀의 기존 값을 읽어 전파한다.
        public int extMinX, extMinY, extWidth;

        public int width;

        public void Execute(int i)
        {
            int x = extMinX + i % extWidth;
            int y = extMinY + i / extWidth;
            int index = y * width + x;

            bool isSeed = baseData[index].a == 0
                || (pixelInfo.Length > 0 && (pixelInfo[index] & 128) != 0);

            bool inDirty = x >= dirtyMinX && x < dirtyMaxX && y >= dirtyMinY && y < dirtyMaxY;

            if (inDirty)
            {
                // Dirty 픽셀: 에어→0, 솔리드→maxDist (2패스가 다시 채움)
                distanceField[index] = isSeed ? (ushort)0 : (ushort)maxDist;
            }
            else if (isSeed)
            {
                // Extended 영역 (Dirty 바깥): 에어는 0 보장, 솔리드는 기존 값 유지
                // 2패스는 이 기존 값을 읽어 Dirty 안으로 전파한다
                distanceField[index] = 0;
            }
        }
    }
```

- [ ] **Step 6: `VisualUpdateJob`을 rect-local로 변경**

같은 파일의 `VisualUpdateJob`에서 **필드 선언과 `Execute` 서두만** 바꾼다. 나머지 본문 로직은 그대로다.

기존 필드 선언 중 아래 부분을 찾아서:

```csharp
        // [Dirty-rect 스코핑] 이 사각형([visMin, visMax)) 밖 픽셀은 baseData·distanceField가
        // 바뀌지 않았으므로 텍스처를 다시 쓰지 않고 스킵한다(이전 값 유지 → GPU 텍스처는 영속).
        // ScheduleVisualJob이 _lastExt*(방금 거리장이 갱신된 범위)로 세팅한다.
        // 전체 렌더(청크 생성·RefreshVisuals)에서는 _lastExt*가 풀 청크라 종전과 동일하게 동작.
        public int visMinX, visMinY, visMaxX, visMaxY;
```

아래로 교체:

```csharp
        // [rect-local] dispatch 도메인 (full 좌표). Schedule(rectWidth * rectHeight).
        // 이 rect == 방금 거리장이 갱신된 범위(_lastExt) == 텍스처가 바뀔 수 있는 정확한 영역.
        // rect 밖 픽셀은 dispatch되지 않으므로 GPU 텍스처의 이전 값이 그대로 유지된다.
        public int rectMinX, rectMinY, rectWidth;
```

그리고 `Execute` 서두 — 기존:

```csharp
        public void Execute(int index)
        {
            // [Dirty-rect 스코핑] 변경 영역(_lastExt) 밖은 스킵 — 텍스처는 이전 값을 유지한다.
            int px = index % width;
            int py = index / width;
            if (px < visMinX || px >= visMaxX || py < visMinY || py >= visMaxY)
                return;

            Color32 baseCol = baseData[index];
```

아래로 교체:

```csharp
        public void Execute(int i)
        {
            // [rect-local] i는 rect 로컬 인덱스. early-return 가드 불필요 — dispatch 도메인이 곧 rect다.
            int px = rectMinX + i % rectWidth;
            int py = rectMinY + i / rectWidth;
            int index = py * width + px;

            Color32 baseCol = baseData[index];
```

- [ ] **Step 7: `VisualUpdateJob` 본문의 좌표 재계산·인덱스 가드 정리**

같은 `Execute` 본문 안에서 **3곳**을 고친다. `px`/`py`가 이미 계산돼 있으므로 재계산을 제거한다.

**(a) 오목 모서리 보정 블록** — 기존:

```csharp
            {
                int x0 = index % width;
                int y0 = index / width;
                int bestOrtho = int.MaxValue;
```

교체:

```csharp
            {
                int x0 = px;
                int y0 = py;
                int bestOrtho = int.MaxValue;
```

**(b) 테두리 UV 블록** — 기존:

```csharp
                if (borderWidth > 0 && borderHeight > 0 && borderData.Length > 0)
                {
                    int x = index % width;
                    int y = index / width;
                    int v = math.min((int)(dist / 5), borderHeight - 1);
```

교체:

```csharp
                if (borderWidth > 0 && borderHeight > 0 && borderData.Length > 0)
                {
                    int x = px;
                    int y = py;
                    int v = math.min((int)(dist / 5), borderHeight - 1);
```

**(c) 경고 로그 가드** — 기존:

```csharp
                else
                {
                    if (index == 0)
                    {
                        int bLen = borderData.Length;
                        LogBorderInvalid(borderWidth, borderHeight, bLen);
                    }
                }
```

교체 (rect 로컬 인덱스 기준 — 청크당 1회만 찍는 게 목적이므로 어느 픽셀이든 무방):

```csharp
                else
                {
                    if (i == 0)
                    {
                        int bLen = borderData.Length;
                        LogBorderInvalid(borderWidth, borderHeight, bLen);
                    }
                }
```

- [ ] **Step 8: `ChunkJobScheduler`의 기존 호출부를 새 필드명에 맞춘다**

Task 3에서 이 파일을 크게 재구성하므로 **(b)~(e)의 편집 일부는 Task 3에서 다시 지워진다.**
그래도 지금 해야 한다 — Task 2가 단독으로 컴파일·동작해야 하기 때문이다.
`ChunkJobScheduler.cs`에서 4개 잡 초기화 지점의 필드명·Schedule 개수를 바꾼다.

**(a) `ScheduleVisualJob`** — 기존 필드 지정 끝부분:

```csharp
            debugDistanceField = TerrainChunk.DebugDistanceField,

            // [Dirty-rect 스코핑] ...
            visMinX = _lastExtMinX,
            visMinY = _lastExtMinY,
            visMaxX = _lastExtMaxX,
            visMaxY = _lastExtMaxY
        };

        // [half-res] VisualJob은 full DistanceField를 읽는다 → Upsample(full DF writer) 완료 후 실행.
        _visualsJobHandle = job.Schedule(data.TotalPixels, JOB_BATCH_SIZE, _upsampleHandle);
```

교체:

```csharp
            debugDistanceField = TerrainChunk.DebugDistanceField,

            // [rect-local] 방금 거리장이 갱신된 범위(_lastExt)만 dispatch.
            rectMinX = _lastExtMinX,
            rectMinY = _lastExtMinY,
            rectWidth = _lastExtMaxX - _lastExtMinX
        };

        int visW = _lastExtMaxX - _lastExtMinX;
        int visH = _lastExtMaxY - _lastExtMinY;
        if (visW <= 0 || visH <= 0) return; // 빈 rect — 핸들은 그대로 둔다 (설계 §5.4 주의 3)

        // [half-res] VisualJob은 full DistanceField를 읽는다 → Upsample(full DF writer) 완료 후 실행.
        _visualsJobHandle = job.Schedule(visW * visH, JOB_BATCH_SIZE, _upsampleHandle);
```

**(b) `ScheduleInitLighting`의 `DownsampleMaskJob`** — 기존:

```csharp
        var downsampleJob = new TerrainJobs.DownsampleMaskJob
        {
            baseData = data.BasePixels,
            pixelInfo = data.PixelInfo,
            baseHalf = data.BasePixelsHalf,
            pixelInfoHalf = data.PixelInfoHalf,
            fullWidth = w, fullHeight = h,
            halfWidth = data.HalfWidth
        };
        _downsampleHandle = downsampleJob.Schedule(data.HalfTotalPixels, 64, prevAll);
```

교체:

```csharp
        int dsMinX = extMinX / 2, dsMinY = extMinY / 2;
        int dsMaxX = (extMaxX + 1) / 2, dsMaxY = (extMaxY + 1) / 2;
        int dsW = dsMaxX - dsMinX, dsH = dsMaxY - dsMinY;

        var downsampleJob = new TerrainJobs.DownsampleMaskJob
        {
            baseData = data.BasePixels,
            pixelInfo = data.PixelInfo,
            baseHalf = data.BasePixelsHalf,
            pixelInfoHalf = data.PixelInfoHalf,
            fullWidth = w, fullHeight = h,
            halfWidth = data.HalfWidth,
            rectMinX = dsMinX, rectMinY = dsMinY, rectWidth = dsW
        };
        if (dsW > 0 && dsH > 0)
            _downsampleHandle = downsampleJob.Schedule(dsW * dsH, 64, prevAll);
```

**(c) `ScheduleInitLighting`의 `InitBFSJob`** — 기존:

```csharp
        var initJob = new TerrainJobs.InitBFSJob
        {
            baseData     = data.BasePixelsHalf,
            distanceField = data.DistanceFieldHalf,
            pixelInfo    = data.PixelInfoHalf,
            maxDist      = 255,
            dirtyMinX = minX / 2,        dirtyMinY = minY / 2,
            dirtyMaxX = (maxX + 1) / 2,  dirtyMaxY = (maxY + 1) / 2,
            extMinX = extMinX / 2,       extMinY = extMinY / 2,
            extMaxX = (extMaxX + 1) / 2, extMaxY = (extMaxY + 1) / 2,
            width = data.HalfWidth
        };

        // Init은 halfDF writer. Downsample(halfBase writer) + prevAll(halfDF reader/writer) 후 실행.
        JobHandle initDeps = JobHandle.CombineDependencies(_downsampleHandle, prevAll);
        _initJobHandle = initJob.Schedule(data.HalfTotalPixels, 64, initDeps);
```

교체 (`extMaxX`/`extMaxY` 필드 제거, `extWidth` 사용):

```csharp
        var initJob = new TerrainJobs.InitBFSJob
        {
            baseData     = data.BasePixelsHalf,
            distanceField = data.DistanceFieldHalf,
            pixelInfo    = data.PixelInfoHalf,
            maxDist      = 255,
            dirtyMinX = minX / 2,        dirtyMinY = minY / 2,
            dirtyMaxX = (maxX + 1) / 2,  dirtyMaxY = (maxY + 1) / 2,
            extMinX = dsMinX, extMinY = dsMinY, extWidth = dsW,
            width = data.HalfWidth
        };

        // Init은 halfDF writer. Downsample(halfBase writer) + prevAll(halfDF reader/writer) 후 실행.
        JobHandle initDeps = JobHandle.CombineDependencies(_downsampleHandle, prevAll);
        if (dsW > 0 && dsH > 0)
            _initJobHandle = initJob.Schedule(dsW * dsH, 64, initDeps);
```

**(d) `ScheduleChamferPasses`의 `DownsampleMaskJob`** — 기존:

```csharp
        var dsJob = new TerrainJobs.DownsampleMaskJob
        {
            baseData = data.BasePixels, pixelInfo = data.PixelInfo,
            baseHalf = data.BasePixelsHalf, pixelInfoHalf = data.PixelInfoHalf,
            fullWidth = data.Width, fullHeight = data.Height, halfWidth = data.HalfWidth
        };
        JobHandle dsDep = JobHandle.CombineDependencies(_initJobHandle, _lightingJobHandle, _upsampleHandle);
        _downsampleHandle = dsJob.Schedule(data.HalfTotalPixels, 64, dsDep);
```

교체:

```csharp
        int cdsMinX = _lastExtMinX / 2, cdsMinY = _lastExtMinY / 2;
        int cdsMaxX = (_lastExtMaxX + 1) / 2, cdsMaxY = (_lastExtMaxY + 1) / 2;
        int cdsW = cdsMaxX - cdsMinX, cdsH = cdsMaxY - cdsMinY;

        var dsJob = new TerrainJobs.DownsampleMaskJob
        {
            baseData = data.BasePixels, pixelInfo = data.PixelInfo,
            baseHalf = data.BasePixelsHalf, pixelInfoHalf = data.PixelInfoHalf,
            fullWidth = data.Width, fullHeight = data.Height, halfWidth = data.HalfWidth,
            rectMinX = cdsMinX, rectMinY = cdsMinY, rectWidth = cdsW
        };
        JobHandle dsDep = JobHandle.CombineDependencies(_initJobHandle, _lightingJobHandle, _upsampleHandle);
        if (cdsW > 0 && cdsH > 0)
            _downsampleHandle = dsJob.Schedule(cdsW * cdsH, 64, dsDep);
```

**(e) `ScheduleChamferPasses`의 `UpsampleDistanceJob`** — 기존:

```csharp
        var upsampleJob = new TerrainJobs.UpsampleDistanceJob
        {
            halfField = data.DistanceFieldHalf,
            fullField = data.DistanceField,
            fullWidth = data.Width,
            halfWidth = data.HalfWidth,
            maxDist = 255,
            visMinX = _lastExtMinX, visMinY = _lastExtMinY, // vis rect는 full 좌표
            visMaxX = _lastExtMaxX, visMaxY = _lastExtMaxY
        };
        // Upsample: halfDF reader + fullDF writer. bwd(halfDF writer) + 이전 Visual(fullDF reader) 후.
        JobHandle upDeps = JobHandle.CombineDependencies(_lightingJobHandle, _visualsJobHandle);
        _upsampleHandle = upsampleJob.Schedule(data.TotalPixels, JOB_BATCH_SIZE, upDeps);
```

교체:

```csharp
        int upW = _lastExtMaxX - _lastExtMinX;
        int upH = _lastExtMaxY - _lastExtMinY;

        var upsampleJob = new TerrainJobs.UpsampleDistanceJob
        {
            halfField = data.DistanceFieldHalf,
            fullField = data.DistanceField,
            fullWidth = data.Width,
            halfWidth = data.HalfWidth,
            maxDist = 255,
            rectMinX = _lastExtMinX, rectMinY = _lastExtMinY, rectWidth = upW
        };
        // Upsample: halfDF reader + fullDF writer. bwd(halfDF writer) + 이전 Visual(fullDF reader) 후.
        JobHandle upDeps = JobHandle.CombineDependencies(_lightingJobHandle, _visualsJobHandle);
        if (upW > 0 && upH > 0)
            _upsampleHandle = upsampleJob.Schedule(upW * upH, JOB_BATCH_SIZE, upDeps);
```

- [ ] **Step 9: 컴파일 확인 + 사람이 테스트 실행**

컴파일 에러 0을 확인한다.
Unity Test Runner (EditMode) → `TerrainJobsRectScopeTests` 4개.
**게이트가 아니다** — 실행을 요청하고, 결과와 무관하게 다음 스텝으로 진행한다.

- [ ] **Step 10: 인게임 확인**

Play 모드에서 지형을 파본다. **이 시점에서 파이프라인 구조는 아직 안 바뀌었으므로 렌더 결과가 완전히 동일해야 한다.**
테두리가 깨지거나 파낸 구멍이 안 보이면 rect 매핑이 틀린 것이다.

`InfinityMapManager.LagDiag = true`로 켜고 `[LAGDIAG] CompleteAll` 로그의 수치를 기록해둔다 (Task 5 전후 비교용).

- [ ] **Step 11: 체크포인트**

변경: `TerrainJobs.cs`, `ChunkJobScheduler.cs`, `TerrainJobsRectScopeTests.cs`
UVCS 체크인은 사람이 수행한다.

---

## Task 3: `ChunkJobScheduler` — 잡 1:1 스케줄 메서드 분리

`ScheduleInitLighting`(= rect세팅 + DS + Init)과 `ScheduleChamferPasses`(= DS + Chamfer + Upsample)가
여러 잡을 묶고 있다. 잡마다 1:1 메서드로 쪼개고, **조합은 호출자가 정하게** 한다.

**동작은 바뀌지 않는다.** 기존 복합 메서드는 새 메서드들을 묶는 래퍼로 남긴다.

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs`

**Interfaces:**
- Consumes: Task 2의 `TerrainJobs` 필드명 (`rectMinX/rectMinY/rectWidth`, `extMinX/extMinY/extWidth`)
- Produces (Task 4가 호출한다):
  - `void SetDirtyRect(ChunkData data, int minX, int minY, int maxX, int maxY)` — `_lastDirty*` + `_lastExt*` 세팅
  - `void ScheduleDownsample(ChunkData data)`
  - `void ScheduleInit(ChunkData data)`
  - `void ScheduleChamferPasses(ChunkData data)` — **fwd + bwd 만** (DS·Upsample 제거)
  - `void ScheduleUpsample(ChunkData data)`
  - `void ScheduleVisualJob(ChunkData data, Texture2D mainTexture, int texThick, int chunkX, int chunkY)` — 기존 시그니처 유지
  - `bool TryScheduleInitLighting(ChunkData data, int minX, int minY, int maxX, int maxY)` — 기존 유지
  - `JobHandle ScheduleBoundarySync(ChunkData data, BoundaryNeighborSet n)` — **변경 없음** (설계 §9.1)

---

- [ ] **Step 1: dirty rect 필드 추가**

`ChunkJobScheduler.cs`의 CONFIGURATION 섹션에서 기존:

```csharp
    private const int JOB_BATCH_SIZE = 64;

    // ScheduleInitLighting에서 저장 → ScheduleChamferPasses에서 재사용
    private int _lastExtMinX, _lastExtMinY, _lastExtMaxX, _lastExtMaxY;
```

아래로 교체:

```csharp
    private const int JOB_BATCH_SIZE = 64;
    private const int EXT_MARGIN = 50;

    // SetDirtyRect가 세팅 → 이후 모든 Schedule* 메서드가 읽는다.
    // _lastExt* 는 ScheduleBoundarySync 가 경계 strip 방향으로 확장한다(의도된 동작).
    private int _lastDirtyMinX, _lastDirtyMinY, _lastDirtyMaxX, _lastDirtyMaxY;
    private int _lastExtMinX, _lastExtMinY, _lastExtMaxX, _lastExtMaxY;
```

- [ ] **Step 2: `SetDirtyRect` + 의존성 헬퍼 추가**

`ScheduleVisualJob` 바로 위(CONSTRUCTION 섹션 아래)에 추가:

```csharp
    // ============================================================================================================
    //  RECT STATE
    // ============================================================================================================

    /// <summary>
    /// dirty rect와 그 확장 rect(ext = dirty ± EXT_MARGIN)를 세팅한다.
    /// 이후 스케줄되는 모든 잡이 이 rect를 dispatch 도메인으로 쓴다.
    ///
    /// ⚠ 각 라운드 진입 시 반드시 먼저 호출할 것.
    ///   _lastExt*는 필드이고 ScheduleBoundarySync가 이를 변형하며,
    ///   같은 청크에 대해 ProcessDirtyChunksAsync 코루틴이 동시 실행될 수 있다.
    ///   (배경: Assets/Docs/job-pipeline-waste-removal.md §5.2)
    /// </summary>
    public void SetDirtyRect(ChunkData data, int minX, int minY, int maxX, int maxY)
    {
        int w = data.Width;
        int h = data.Height;

        _lastDirtyMinX = Mathf.Clamp(minX, 0, w);
        _lastDirtyMinY = Mathf.Clamp(minY, 0, h);
        _lastDirtyMaxX = Mathf.Clamp(maxX, 0, w);
        _lastDirtyMaxY = Mathf.Clamp(maxY, 0, h);

        _lastExtMinX = Mathf.Max(_lastDirtyMinX - EXT_MARGIN, 0);
        _lastExtMinY = Mathf.Max(_lastDirtyMinY - EXT_MARGIN, 0);
        _lastExtMaxX = Mathf.Min(_lastDirtyMaxX + EXT_MARGIN, w);
        _lastExtMaxY = Mathf.Min(_lastDirtyMaxY + EXT_MARGIN, h);
    }

    /// <summary>
    /// 이 청크의 모든 pending 핸들 합성 (over-depend).
    /// 청크 내 파이프라인은 어차피 직렬이라 병렬성 손실이 없고, 의존성 누락으로 인한
    /// InvalidOperationException 을 원천 차단한다.
    /// </summary>
    private JobHandle AllPrevHandles()
    {
        JobHandle h = JobHandle.CombineDependencies(
            JobHandle.CombineDependencies(_initJobHandle, _forwardJobHandle, _lightingJobHandle),
            JobHandle.CombineDependencies(_boundarySyncHandle, _upsampleHandle, _externalDistanceFieldReaders));
        return JobHandle.CombineDependencies(h, _downsampleHandle, _visualsJobHandle);
    }

    /// <summary>half 좌표 ext rect. 반환 false면 빈 rect이므로 스케줄하지 않는다.</summary>
    private bool HalfExtRect(out int minX, out int minY, out int rw, out int rh)
    {
        minX = _lastExtMinX / 2;
        minY = _lastExtMinY / 2;
        rw = (_lastExtMaxX + 1) / 2 - minX;
        rh = (_lastExtMaxY + 1) / 2 - minY;
        return rw > 0 && rh > 0;
    }

    /// <summary>full 좌표 ext rect. 반환 false면 빈 rect이므로 스케줄하지 않는다.</summary>
    private bool FullExtRect(out int minX, out int minY, out int rw, out int rh)
    {
        minX = _lastExtMinX;
        minY = _lastExtMinY;
        rw = _lastExtMaxX - minX;
        rh = _lastExtMaxY - minY;
        return rw > 0 && rh > 0;
    }
```

- [ ] **Step 3: `ScheduleDownsample` / `ScheduleInit` 신설**

같은 파일에 추가:

```csharp
    // ============================================================================================================
    //  JOB SCHEDULING: 1:1 (조합은 호출자가 정한다)
    // ============================================================================================================

    /// <summary>
    /// full BasePixels/PixelInfo → half 다운샘플. Init·Chamfer가 half 격자에서 읽는다.
    /// 각 라운드 진입 시 1회 호출한다 — 라운드 사이에 플레이어가 다시 팔 수 있으므로
    /// 항상 라이브 BasePixels 를 다시 읽어야 stale halfBase 를 피한다.
    /// </summary>
    public void ScheduleDownsample(ChunkData data)
    {
        if (!HalfExtRect(out int minX, out int minY, out int rw, out int rh)) return;

        var job = new TerrainJobs.DownsampleMaskJob
        {
            baseData = data.BasePixels,
            pixelInfo = data.PixelInfo,
            baseHalf = data.BasePixelsHalf,
            pixelInfoHalf = data.PixelInfoHalf,
            fullWidth = data.Width,
            fullHeight = data.Height,
            halfWidth = data.HalfWidth,
            rectMinX = minX, rectMinY = minY, rectWidth = rw
        };
        _downsampleHandle = job.Schedule(rw * rh, 64, AllPrevHandles());
    }

    /// <summary>
    /// dirty rect의 거리장을 리셋(솔리드→maxDist, 에어→0)하고 ext 영역의 시드를 보정한다.
    /// ScheduleDownsample 이후에 호출할 것 — BasePixelsHalf 를 읽는다.
    /// </summary>
    public void ScheduleInit(ChunkData data)
    {
        if (!HalfExtRect(out int minX, out int minY, out int rw, out int rh)) return;

        var job = new TerrainJobs.InitDistanceFieldJob
        {
            baseData = data.BasePixelsHalf,
            pixelInfo = data.PixelInfoHalf,
            distanceField = data.DistanceFieldHalf,
            maxDist = 255,
            dirtyMinX = _lastDirtyMinX / 2,
            dirtyMinY = _lastDirtyMinY / 2,
            dirtyMaxX = (_lastDirtyMaxX + 1) / 2,
            dirtyMaxY = (_lastDirtyMaxY + 1) / 2,
            extMinX = minX, extMinY = minY, extWidth = rw,
            width = data.HalfWidth
        };
        _initJobHandle = job.Schedule(rw * rh, 64,
            JobHandle.CombineDependencies(_downsampleHandle, AllPrevHandles()));

        _externalDistanceFieldReaders = default; // Init 가 prior external readers 를 포섭함
    }

    /// <summary>half 거리장 → full 거리장(×2). VisualJob 직전에만 필요하다.</summary>
    public void ScheduleUpsample(ChunkData data)
    {
        if (!FullExtRect(out int minX, out int minY, out int rw, out int rh)) return;

        var job = new TerrainJobs.UpsampleDistanceJob
        {
            halfField = data.DistanceFieldHalf,
            fullField = data.DistanceField,
            fullWidth = data.Width,
            halfWidth = data.HalfWidth,
            maxDist = 255,
            rectMinX = minX, rectMinY = minY, rectWidth = rw
        };
        // Upsample: halfDF reader + fullDF writer. bwd(halfDF writer) + 이전 Visual(fullDF reader) 후.
        JobHandle deps = JobHandle.CombineDependencies(_lightingJobHandle, _visualsJobHandle);
        _upsampleHandle = job.Schedule(rw * rh, JOB_BATCH_SIZE, deps);
    }
```

- [ ] **Step 4: `ScheduleChamferPasses`를 fwd+bwd 전용으로 축소**

기존 `ScheduleChamferPasses` 전체(DS + fwd + bwd + Upsample)를 아래로 교체:

```csharp
    /// <summary>
    /// [2패스 Chamfer] Chamfer forward/backward 만 스케줄한다.
    /// DS(ScheduleDownsample)·Upsample(ScheduleUpsample)은 호출자가 별도로 스케줄한다.
    ///
    /// ext rect는 이 시점의 _lastExt* — ScheduleBoundarySync 가 호출됐다면 strip 확장이 반영돼 있다.
    /// </summary>
    public void ScheduleChamferPasses(ChunkData data)
    {
        if (!HalfExtRect(out int minX, out int minY, out int rw, out int rh)) return;

        int maxX = minX + rw;
        int maxY = minY + rh;

        JobHandle dep = AllPrevHandles();

        var fwd = new TerrainJobs.ChamferForwardPassJob
        {
            distanceField = data.DistanceFieldHalf,
            baseData  = data.BasePixelsHalf,
            pixelInfo = data.PixelInfoHalf,
            width = data.HalfWidth, height = data.HalfHeight, maxDist = 255,
            extMinX = minX, extMinY = minY, extMaxX = maxX, extMaxY = maxY
        };
        _forwardJobHandle = fwd.Schedule(dep);

        var bwd = new TerrainJobs.ChamferBackwardPassJob
        {
            distanceField = data.DistanceFieldHalf,
            baseData  = data.BasePixelsHalf,
            pixelInfo = data.PixelInfoHalf,
            width = data.HalfWidth, height = data.HalfHeight, maxDist = 255,
            extMinX = minX, extMinY = minY, extMaxX = maxX, extMaxY = maxY
        };
        _lightingJobHandle = bwd.Schedule(_forwardJobHandle);

        _externalDistanceFieldReaders = default; // Chamfer 가 prior external readers 를 포섭함
    }
```

- [ ] **Step 5: `ScheduleInitLighting`을 래퍼로 축소**

기존 `ScheduleInitLighting` 전체를 아래로 교체:

```csharp
    /// <summary>
    /// [래퍼] SetDirtyRect + Downsample + Init.
    /// 단발 전체 갱신 경로(RefreshVisuals 등)와 LateUpdate Step1 이 사용한다.
    /// </summary>
    public void ScheduleInitLighting(ChunkData data, int minX = 0, int minY = 0, int maxX = -1, int maxY = -1)
    {
        if (maxX < 0) maxX = data.Width;
        if (maxY < 0) maxY = data.Height;

        SetDirtyRect(data, minX, minY, maxX, maxY);
        ScheduleDownsample(data);
        ScheduleInit(data);
    }
```

- [ ] **Step 6: `RecalcExtRange` 제거 → `SetDirtyRect`로 대체**

기존 `RecalcExtRange` 메서드 전체를 **삭제한다** (`SetDirtyRect`가 대체한다 — dirty rect까지 같이 저장하므로 상위 호환).

```csharp
    // 삭제 대상:
    // public void RecalcExtRange(ChunkData data, int minX, int minY, int maxX, int maxY) { ... }
```

`TerrainVisualizer.UpdateVisualsArea`가 이걸 호출하고 있으므로 Step 7에서 같이 고친다.

- [ ] **Step 7: `ScheduleFullVisualUpdate`에 Upsample 추가**

`ScheduleChamferPasses`에서 Upsample을 뺐으므로, 이 래퍼가 명시적으로 불러야 한다. 기존:

```csharp
    public void ScheduleFullVisualUpdate(ChunkData data, Texture2D mainTexture, int texThick, int chunkX, int chunkY)
    {
        ScheduleInitLighting(data);
        ScheduleChamferPasses(data);
        ScheduleVisualJob(data, mainTexture, texThick, chunkX, chunkY);
    }
```

교체:

```csharp
    public void ScheduleFullVisualUpdate(ChunkData data, Texture2D mainTexture, int texThick, int chunkX, int chunkY)
    {
        ScheduleInitLighting(data);   // SetDirtyRect + DS + Init
        ScheduleChamferPasses(data);
        ScheduleUpsample(data);
        ScheduleVisualJob(data, mainTexture, texThick, chunkX, chunkY);
    }
```

- [ ] **Step 8: `TerrainVisualizer.UpdateVisualsArea` 수선 (컴파일 통과용)**

`RecalcExtRange` 삭제로 깨진다. `TerrainVisualizer.cs`의 `UpdateVisualsArea` 본문에서 기존:

```csharp
        // skipInit=true: LateUpdate 선스케줄 경로 — Init 생략, ext range만 재계산
        // (ext range 재계산 필수: RefreshVisuals 등이 full-chunk 범위로 덮어썼을 수 있음)
        if (!skipInit)
            _jobScheduler.ScheduleInitLighting(_data, minX, minY, maxX, maxY);
        else
            _jobScheduler.RecalcExtRange(_data, minX, minY, maxX, maxY);

        // onPreChamfer가 BoundarySync Job을 스케줄 → ScheduleChamferPasses가 자동으로 의존성에 포함
        onPreChamfer?.Invoke();

        _jobScheduler.ScheduleChamferPasses(_data);

        int texPx = Mathf.Clamp(Mathf.RoundToInt(TextureThickness * PixelsPerUnit), 1, 50);
        _jobScheduler.ScheduleVisualJob(_data, _mainTexture, texPx, chunkX, chunkY);
```

교체:

```csharp
        if (!skipInit)
        {
            _jobScheduler.ScheduleInitLighting(_data, minX, minY, maxX, maxY); // SetDirtyRect + DS + Init
        }
        else
        {
            _jobScheduler.SetDirtyRect(_data, minX, minY, maxX, maxY);
            _jobScheduler.ScheduleDownsample(_data);
        }

        // onPreChamfer가 BoundarySync Job을 스케줄 → ScheduleChamferPasses가 자동으로 의존성에 포함
        onPreChamfer?.Invoke();

        _jobScheduler.ScheduleChamferPasses(_data);
        _jobScheduler.ScheduleUpsample(_data);

        int texPx = Mathf.Clamp(Mathf.RoundToInt(TextureThickness * PixelsPerUnit), 1, 50);
        _jobScheduler.ScheduleVisualJob(_data, _mainTexture, texPx, chunkX, chunkY);
```

- [ ] **Step 9: 컴파일 + 인게임 확인**

**이 태스크도 동작 변화가 없어야 한다.** Play 모드에서 파기·경계 파기·청크 로드를 확인한다.
결과가 Task 2 이후와 동일해야 한다.

- [ ] **Step 10: 체크포인트**

변경: `ChunkJobScheduler.cs`, `TerrainVisualizer.cs`

---

## Task 4: Round1 / Round2 Pass 메서드 신설

`TerrainVisualizer`와 `TerrainChunk`에 두 개의 Pass 메서드를 추가한다.
**기존 `Do*VisualUpdate*` 4개는 아직 지우지 않는다** (Task 5에서 호출부를 바꾼 뒤 Task 6에서 제거).

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainVisualizer.cs`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs`

**Interfaces:**
- Consumes: Task 3의 `SetDirtyRect` / `ScheduleDownsample` / `ScheduleInit` / `ScheduleChamferPasses` / `ScheduleUpsample` / `ScheduleVisualJob`
- Produces (Task 5가 호출한다):
  - `TerrainChunk.ScheduleDistancePass(RectInt rect, bool hasRect, bool skipInit, bool withPreview)`
  - `TerrainChunk.ScheduleSyncAndVisualPass(RectInt rect, bool hasRect)`
  - `TerrainChunk.ScheduleInitOnlyIfReady(RectInt rect)` → **`bool` 반환으로 시그니처 변경**

---

- [ ] **Step 1: `TerrainVisualizer`에 Pass 메서드 2개 추가**

`TerrainVisualizer.cs`의 `UpdateVisualsArea` 아래에 추가:

```csharp
    // ============================================================================================================
    //  LATEUPDATE 파이프라인 전용 PASS (Assets/Docs/job-pipeline-waste-removal.md §4)
    // ============================================================================================================

    /// <summary>
    /// [Round 1] 자기 청크의 거리장만 확정한다. 이웃이 Round 2에서 읽을 값이다.
    /// BoundarySync 없음 → _lastExt 의 strip 확장도 일어나지 않는다(§3.3.1).
    ///
    /// skipInit: Step 1에서 Init이 이미 스케줄됐으면 true.
    /// withPreview: true면 Upsample + Visual 을 추가해 조기 텍스처 프리뷰를 만든다(§3.4).
    /// </summary>
    public void ScheduleDistancePass(int minX, int minY, int maxX, int maxY,
                                     int chunkX, int chunkY, bool skipInit, bool withPreview)
    {
        _jobScheduler.SetDirtyRect(_data, minX, minY, maxX, maxY);
        _jobScheduler.ScheduleDownsample(_data);

        if (!skipInit)
            _jobScheduler.ScheduleInit(_data);

        _jobScheduler.ScheduleChamferPasses(_data);

        if (!withPreview) return;

        _jobScheduler.ScheduleUpsample(_data);
        int texPx = Mathf.Clamp(Mathf.RoundToInt(TextureThickness * PixelsPerUnit), 1, 50);
        _jobScheduler.ScheduleVisualJob(_data, _mainTexture, texPx, chunkX, chunkY);
        _data.IsVisualDirty = true;
    }

    /// <summary>
    /// [Round 2] 이웃 거리장을 동기화하고 최종 텍스처까지 만든다.
    /// onPreChamfer 가 BoundarySync Job 을 스케줄하며, 그 안에서 _lastExt 가 strip 으로 확장된다.
    ///
    /// ⚠ SetDirtyRect 를 반드시 먼저 호출한다 — Round 2 는 더 이상 ScheduleInitLighting 을
    ///    거치지 않으므로 _lastExt 를 재확립할 다른 주체가 없다(§5.2).
    /// </summary>
    public void ScheduleSyncAndVisualPass(int minX, int minY, int maxX, int maxY,
                                          int chunkX, int chunkY, System.Action onPreChamfer)
    {
        _jobScheduler.SetDirtyRect(_data, minX, minY, maxX, maxY);
        _jobScheduler.ScheduleDownsample(_data);

        onPreChamfer?.Invoke();   // BoundarySync — 여기서 _lastExt 가 확장된다

        _jobScheduler.ScheduleChamferPasses(_data);
        _jobScheduler.ScheduleUpsample(_data);

        int texPx = Mathf.Clamp(Mathf.RoundToInt(TextureThickness * PixelsPerUnit), 1, 50);
        _jobScheduler.ScheduleVisualJob(_data, _mainTexture, texPx, chunkX, chunkY);
        _data.IsVisualDirty = true;
    }
```

- [ ] **Step 2: `TerrainVisualizer.TryScheduleInitArea` 확인**

이미 `bool`을 반환한다. 변경 불필요 — 아래와 같은지 확인만 한다:

```csharp
    public bool TryScheduleInitArea(int minX, int minY, int maxX, int maxY)
    {
        minX = Mathf.Clamp(minX, 0, _data.Width);
        maxX = Mathf.Clamp(maxX, 0, _data.Width);
        minY = Mathf.Clamp(minY, 0, _data.Height);
        maxY = Mathf.Clamp(maxY, 0, _data.Height);
        return _jobScheduler.TryScheduleInitLighting(_data, minX, minY, maxX, maxY);
    }
```

- [ ] **Step 3: `TerrainChunk`에 Pass 메서드 2개 추가 + `ScheduleInitOnlyIfReady` 반환값 변경**

`TerrainChunk.cs`에서 기존:

```csharp
    /// <summary>
    /// [Non-blocking Pre-schedule] LateUpdate 시작 시 전용.
    /// 이전 잡이 완료된 경우에만 Init 스케줄 → RefreshVisuals 실행 중이면 건너뜀.
    /// </summary>
    public void ScheduleInitOnlyIfReady(RectInt rect)
    {
        _visualizer?.TryScheduleInitArea(rect.xMin, rect.yMin, rect.xMax, rect.yMax);
    }
```

아래로 교체 (`bool` 반환 + Pass 메서드 2개 추가):

```csharp
    /// <summary>
    /// [Non-blocking Pre-schedule] LateUpdate Step 1 전용.
    /// 이전 Init 잡이 완료된 경우에만 스케줄한다.
    /// </summary>
    /// <returns>Init 잡이 실제로 스케줄됐으면 true. false면 Round 1이 대신 수행해야 한다.</returns>
    public bool ScheduleInitOnlyIfReady(RectInt rect)
    {
        if (_visualizer == null) return false;
        return _visualizer.TryScheduleInitArea(rect.xMin, rect.yMin, rect.xMax, rect.yMax);
    }

    /// <summary>
    /// [Round 1] 자기 청크 거리장만 확정. BoundarySync·Upsample·Visual 없음.
    /// (배경: Assets/Docs/job-pipeline-waste-removal.md §4)
    /// </summary>
    public void ScheduleDistancePass(RectInt rect, bool hasRect, bool skipInit, bool withPreview)
    {
        if (_visualizer == null) return;

        int minX = hasRect ? rect.xMin : 0;
        int minY = hasRect ? rect.yMin : 0;
        int maxX = hasRect ? rect.xMax : _data.Width;
        int maxY = hasRect ? rect.yMax : _data.Height;

        _visualizer.ScheduleDistancePass(minX, minY, maxX, maxY, ChunkX, ChunkY, skipInit, withPreview);
    }

    /// <summary>
    /// [Round 2] BoundarySync → Chamfer → Upsample → Visual.
    /// </summary>
    public void ScheduleSyncAndVisualPass(RectInt rect, bool hasRect)
    {
        if (_visualizer == null) return;

        int minX = hasRect ? rect.xMin : 0;
        int minY = hasRect ? rect.yMin : 0;
        int maxX = hasRect ? rect.xMax : _data.Width;
        int maxY = hasRect ? rect.yMax : _data.Height;

        _visualizer.ScheduleSyncAndVisualPass(minX, minY, maxX, maxY, ChunkX, ChunkY,
                                              SyncBoundaryDistanceWithNeighbors);
    }
```

- [ ] **Step 4: 컴파일 확인**

`ScheduleInitOnlyIfReady`의 반환값을 무시하는 기존 호출부(`InfinityMapManager.SnapshotScheduleInitJobs`)는
C#에서 반환값 무시가 허용되므로 컴파일된다. 동작도 그대로다.

- [ ] **Step 5: 체크포인트**

변경: `TerrainVisualizer.cs`, `TerrainChunk.cs`
**아직 파이프라인은 옛 경로를 쓴다.** 인게임 동작에 변화가 없어야 한다.

---

## Task 5: `InfinityMapManager` 파이프라인 재조립 (⚡ 동작 변경 지점)

여기서 실제로 잡이 줄어든다. **Task 1의 두 테스트가 통과한 뒤에만 진행한다.**

**Files:**
- Modify: `Assets/Scripts/_Core/Data/WorldSettingsData.cs`
- Modify: `Assets/StreamingAssets/worldSettings.json`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs`

**Interfaces:**
- Consumes: Task 4의 `TerrainChunk.ScheduleDistancePass` / `ScheduleSyncAndVisualPass` / `ScheduleInitOnlyIfReady(→bool)`
- Produces: 없음 (최상위)

---

- [ ] **Step 1: `WorldSettingsData`에 프리뷰 플래그 추가**

`Assets/Scripts/_Core/Data/WorldSettingsData.cs`의 `RenderingSection` — 기존:

```csharp
    public class RenderingSection
    {
        public bool  enableLayerBlending  = true;
        public float globalLightFalloff   = 30f;
        public float textureUpdateInterval= 0f;
    }
```

교체:

```csharp
    public class RenderingSection
    {
        public bool  enableLayerBlending  = true;
        public float globalLightFalloff   = 30f;
        public float textureUpdateInterval= 0f;

        /// <summary>
        /// [A/B 안전장치] Round 1에서도 Upsample + Visual 을 돌려 텍스처를 조기 프리뷰한다.
        /// 기본 false — 프리뷰는 Round 2가 즉시 덮어쓰므로 낭비다(설계 §3.4).
        /// 파기 반응성이 이상하게 느껴지면 true 로 켜서 A/B 비교하고, 검증 후 이 필드를 제거한다.
        /// </summary>
        public bool  enableRound1Preview  = false;
    }
```

- [ ] **Step 2: `worldSettings.json`에 추가**

`Assets/StreamingAssets/worldSettings.json`의 `rendering` 블록 — 기존:

```json
  "rendering": {
    "enableLayerBlending": true,
    "globalLightFalloff": 30.0,
    "textureUpdateInterval": 0.05
  },
```

교체:

```json
  "rendering": {
    "enableLayerBlending": true,
    "globalLightFalloff": 30.0,
    "textureUpdateInterval": 0.05,
    "enableRound1Preview": false
  },
```

- [ ] **Step 3: `InfinityMapManager`에 플래그 필드 + 로드 추가**

`InfinityMapManager.cs`의 텍스처 쓰로틀링 필드 옆 — 기존:

```csharp
    private float _lastTextureApplyTime = -999f;
    private float _textureUpdateInterval = 0f; // JSON 로드 후 덮어써짐
```

교체:

```csharp
    private float _lastTextureApplyTime = -999f;
    private float _textureUpdateInterval = 0f; // JSON 로드 후 덮어써짐

    // [A/B 안전장치] Round 1에서도 Visual 을 돌려 조기 프리뷰를 만들지 여부.
    // static 이 아니다 — 파이프라인의 주인인 이 매니저만 알면 되고, 각 Pass 호출에 인자로 내려간다.
    // (배경: Assets/Docs/job-pipeline-waste-removal.md §3.4, §5.5)
    private bool _enableRound1Preview = false;
```

그리고 `ApplyWorldSettings` 안 — 기존:

```csharp
        _textureUpdateInterval   = s.rendering.textureUpdateInterval;
```

교체:

```csharp
        _textureUpdateInterval   = s.rendering.textureUpdateInterval;
        _enableRound1Preview     = s.rendering.enableRound1Preview;
```

- [ ] **Step 4: 스냅샷 튜플에 `initScheduled` 추가**

`InfinityMapManager.cs`의 스냅샷 풀 — 기존:

```csharp
    // 스냅샷 List 풀링 — dirty 프레임마다 new List<> 하던 것을 재사용
    private readonly Stack<List<(TerrainChunk, RectInt, bool)>> _snapshotPool
        = new Stack<List<(TerrainChunk, RectInt, bool)>>();
```

교체:

```csharp
    // 스냅샷 List 풀링 — dirty 프레임마다 new List<> 하던 것을 재사용
    // 튜플: (청크, dirty rect, rect 유무, Step1에서 Init이 실제로 스케줄됐는지)
    private readonly Stack<List<(TerrainChunk, RectInt, bool, bool)>> _snapshotPool
        = new Stack<List<(TerrainChunk, RectInt, bool, bool)>>();
```

그리고 `RentSnapshot` / `ReturnSnapshot` — 기존:

```csharp
    private List<(TerrainChunk, RectInt, bool)> RentSnapshot(int capacity)
    {
        var list = _snapshotPool.Count > 0 ? _snapshotPool.Pop() : new List<(TerrainChunk, RectInt, bool)>(capacity);
        list.Clear();
        return list;
    }

    private void ReturnSnapshot(List<(TerrainChunk, RectInt, bool)> snapshot) => _snapshotPool.Push(snapshot);
```

교체:

```csharp
    private List<(TerrainChunk, RectInt, bool, bool)> RentSnapshot(int capacity)
    {
        var list = _snapshotPool.Count > 0
            ? _snapshotPool.Pop()
            : new List<(TerrainChunk, RectInt, bool, bool)>(capacity);
        list.Clear();
        return list;
    }

    private void ReturnSnapshot(List<(TerrainChunk, RectInt, bool, bool)> snapshot) => _snapshotPool.Push(snapshot);
```

- [ ] **Step 5: `LateUpdate`의 스냅샷 채우기 수정**

기존:

```csharp
            var snapshot = RentSnapshot(_dirtyChunksOrdered.Count);
            foreach (var chunk in _dirtyChunksOrdered)
            {
                if (chunk == null || !chunk.gameObject.activeSelf) continue;
                bool hasRect = _dirtyRects.TryGetValue(chunk, out RectInt rect);
                snapshot.Add((chunk, rect, hasRect));
            }
```

교체 (`initScheduled`는 Step 1에서 채워지므로 여기선 false):

```csharp
            var snapshot = RentSnapshot(_dirtyChunksOrdered.Count);
            foreach (var chunk in _dirtyChunksOrdered)
            {
                if (chunk == null || !chunk.gameObject.activeSelf) continue;
                bool hasRect = _dirtyRects.TryGetValue(chunk, out RectInt rect);
                snapshot.Add((chunk, rect, hasRect, false)); // initScheduled는 Step 1에서 확정
            }
```

- [ ] **Step 6: `ProcessDirtyChunksAsync` 시그니처 변경**

기존:

```csharp
    private IEnumerator ProcessDirtyChunksAsync(List<(TerrainChunk chunk, RectInt rect, bool hasRect)> snapshot)
```

교체:

```csharp
    private IEnumerator ProcessDirtyChunksAsync(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
```

본문(6단계 호출 순서)은 그대로 둔다.

- [ ] **Step 7: `SnapshotScheduleInitJobs` — Init 스케줄 여부를 기록**

기존:

```csharp
    /// <summary>Step 1: 스냅샷 내 모든 청크에 Init 잡 스케줄.</summary>
    private void SnapshotScheduleInitJobs(List<(TerrainChunk chunk, RectInt rect, bool hasRect)> snapshot)
    {
        foreach (var (chunk, rect, hasRect) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            if (hasRect) chunk.ScheduleInitOnlyIfReady(rect);
        }
    }
```

교체 (튜플은 값 타입이라 `foreach`로는 못 고친다 — 인덱스 루프로 재대입):

```csharp
    /// <summary>
    /// Step 1: Init 잡 선스케줄. 이전 Init이 아직 돌고 있으면 스킵된다.
    /// 스킵 여부를 스냅샷에 기록해 Round 1이 대신 수행하게 한다 — Init은 사이클당 정확히 1회.
    /// (배경: Assets/Docs/job-pipeline-waste-removal.md §3.2)
    /// </summary>
    private void SnapshotScheduleInitJobs(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        for (int i = 0; i < snapshot.Count; i++)
        {
            var (chunk, rect, hasRect, _) = snapshot[i];
            if (chunk == null || !chunk.gameObject.activeSelf) continue;

            // hasRect=false(전체 갱신)는 여기서 스케줄하지 않는다 → Round 1이 full-chunk Init 수행
            bool scheduled = hasRect && chunk.ScheduleInitOnlyIfReady(rect);
            snapshot[i] = (chunk, rect, hasRect, scheduled);
        }
    }
```

- [ ] **Step 8: `SnapshotScheduleRound1` — 거리장 패스만 수행**

기존:

```csharp
    /// <summary>Step 2: Round 1 — Init 완료 가정 하에 Chamfer + Visual 스케줄.</summary>
    private void SnapshotScheduleRound1(List<(TerrainChunk chunk, RectInt rect, bool hasRect)> snapshot)
    {
        foreach (var (chunk, rect, hasRect) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            if (hasRect) chunk.DoVisualUpdateSkipInit(rect);
            else         chunk.DoFullVisualUpdate();
        }
    }
```

교체:

```csharp
    /// <summary>
    /// Step 2: Round 1 — 자기 청크 거리장만 확정한다. 이웃이 Round 2에서 읽을 값이다.
    /// BoundarySync·Upsample·Visual 없음(§3.3, §3.4).
    /// Step 1에서 Init이 스킵됐으면(initScheduled=false) 여기서 대신 수행한다.
    /// </summary>
    private void SnapshotScheduleRound1(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        foreach (var (chunk, rect, hasRect, initScheduled) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            chunk.ScheduleDistancePass(rect, hasRect,
                                       skipInit: initScheduled,
                                       withPreview: _enableRound1Preview);
        }
    }
```

- [ ] **Step 9: `SnapshotScheduleRound2` — 동기화 + 비주얼**

기존:

```csharp
    /// <summary>Step 4: Round 2 — 이웃 DistanceField 동기화 후 Visual 재스케줄.</summary>
    private void SnapshotScheduleRound2(List<(TerrainChunk chunk, RectInt rect, bool hasRect)> snapshot)
    {
        foreach (var (chunk, rect, hasRect) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            if (hasRect) chunk.DoVisualUpdate(rect);
            else         chunk.DoFullVisualUpdate();
        }
    }
```

교체:

```csharp
    /// <summary>
    /// Step 4: Round 2 — 이웃 거리장 동기화(BoundarySync) 후 Chamfer → Upsample → Visual.
    /// Init은 하지 않는다 — Round 1이 남긴 DF는 참값의 상한이고 Chamfer는 min-전파이므로
    /// 리셋 없이도 같은 고정점에 수렴한다(§3.1, ChamferConvergenceTests가 증명).
    /// </summary>
    private void SnapshotScheduleRound2(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        foreach (var (chunk, rect, hasRect, _) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            chunk.ScheduleSyncAndVisualPass(rect, hasRect);
        }
    }
```

- [ ] **Step 10: 나머지 Snapshot 헬퍼 3개의 시그니처 정리**

`SnapshotAreAllJobsDone`, `SnapshotCompleteLighting`, `SnapshotFinalizeAndApply` 세 개의
파라미터 타입을 `(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)`로 바꾸고,
`foreach` 구조분해도 4개 항목으로 맞춘다.

```csharp
    private bool SnapshotAreAllJobsDone(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        foreach (var (chunk, _, _, _) in snapshot)
        {
            if (chunk != null && chunk.gameObject.activeSelf && chunk.IsJobRunning())
                return false;
        }
        return true;
    }
```

```csharp
    private void SnapshotCompleteLighting(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        var __sw = LagDiag ? System.Diagnostics.Stopwatch.StartNew() : null;
        foreach (var (chunk, _, _, _) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            chunk.CompleteLighting();
        }
        if (__sw != null) { __sw.Stop(); double ms = __sw.Elapsed.TotalMilliseconds;
            if (ms > 3.0) Debug.Log($"[LAGDIAG] pipeline CompleteLighting {ms:F1}ms chunks={snapshot.Count} frame={Time.frameCount}"); }
    }
```

```csharp
    private void SnapshotFinalizeAndApply(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        var __sw = LagDiag ? System.Diagnostics.Stopwatch.StartNew() : null;
        foreach (var (chunk, _, _, _) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            chunk.EnsureJobsCompleted();
            chunk.ApplyTexture();
        }
        if (__sw != null) { __sw.Stop(); double ms = __sw.Elapsed.TotalMilliseconds;
            if (ms > 3.0) Debug.Log($"[LAGDIAG] pipeline FinalizeAndApply {ms:F1}ms chunks={snapshot.Count} frame={Time.frameCount}"); }
    }
```

- [ ] **Step 11: 컴파일 확인**

- [ ] **Step 12: 인게임 회귀 확인 (설계 §7)**

Play 모드에서 **6가지 전부** 확인한다. 하나라도 깨지면 진행하지 않는다.

1. **청크 중앙 파기** — 구멍과 테두리가 정상.
2. **청크 경계를 가로질러 파기** — 경계 꺾임(kink), 직선 스트라이프 테두리가 없는지. **가장 위험한 지점.**
3. **코너(대각) 방향 파기** — 두 경계에 동시에 닿는 경우.
4. **드릴 연속 파기** — 텍스처가 뒤처지거나, 파낸 구멍의 테두리가 엉뚱한 위치에 그려지거나, 갱신이 누락되지 않는지.
   `SetDirtyRect` 누락 버그가 여기서 드러난다.
5. **특수청크 재로드 후 테두리** — 저장 → 언로드 → 재로드.
6. **청크 신규 로드** — 경계 테두리가 이웃과 이어지는지.

- [ ] **Step 13: 성능 측정**

`InfinityMapManager.LagDiag = true` → 드릴로 경계를 파면서 `[LAGDIAG] CompleteAll` / `[LAGDIAG] pipeline` 로그 수집.
Task 2 Step 10에서 기록한 수치와 비교한다.

- [ ] **Step 14: 프리뷰 A/B**

`worldSettings.json`의 `enableRound1Preview`를 `true`로 바꿔 Play → 파기 반응성이 눈에 띄게 다른지 본다.
차이가 없으면 `false`로 되돌린다 (기본값).

- [ ] **Step 15: 체크포인트**

변경: `WorldSettingsData.cs`, `worldSettings.json`, `InfinityMapManager.cs`

---

## Task 6: 죽은 코드 제거

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs`

---

- [ ] **Step 1: 남은 호출부 확인**

아래 4개 메서드를 부르는 곳이 정말 없는지 먼저 검색한다:

- `DoVisualUpdate`
- `DoVisualUpdateSkipInit`
- `DoFullVisualUpdate`
- `DoFullVisualUpdateSkipInit`

`ScheduleInitOnly`(비-IfReady 버전)도 호출부가 없으면 같이 제거 대상이다.

**호출부가 하나라도 남아 있으면 제거하지 말고 보고한다.**

- [ ] **Step 2: `TerrainChunk`에서 죽은 메서드 제거**

**실제 결과 (계획 정정):** `DoVisualUpdate`는 **죽은 코드가 아니었다.**
`RollingRockTrap.cs:105` 가 바위가 지나간 자리를 즉시 갱신하려고 단발로 호출한다
(`TerrainCarver` / `PixelFloorCollapser` 가 `Visualizer.UpdateVisualsArea` 를 쓰는 것과 같은 부류).
→ **남긴다.** 대신 "매니저에서 호출"이라던 XML 주석을 실제 용도로 정정했다.

실제로 삭제한 것 (호출부 0건 확인):
```csharp
    public void DoVisualUpdateSkipInit(RectInt dirtyRect) { ... }
    public void DoFullVisualUpdate() { ... }
    public void DoFullVisualUpdateSkipInit() { ... }
    public void ScheduleInitOnly(RectInt rect) { ... }   // ScheduleInitOnlyIfReady 와 혼동 주의 — 후자는 살아 있다
```

**남겨야 하는 것 (LateUpdate 파이프라인 밖의 단발 전체 갱신 경로 — 설계 §5.2 주의):**
- `RefreshVisuals()`
- `FinishVisualsAfterInit()`
- `ScheduleInitJobOnly()`
- `LoadChunkData()`
- `InitializeAfterGeneration()`
- `ApplyTexture()`, `TryUpdateCollider()`, `ForceUpdateCollider()`

- [ ] **Step 3: 컴파일 + 인게임 재확인**

Task 5 Step 12의 6가지를 다시 한 번 훑는다 (특히 특수청크 재로드 · 청크 신규 로드 —
이 경로들이 `RefreshVisuals` / `FinishVisualsAfterInit` 를 쓰므로).

- [ ] **Step 4: 설계 문서에 완료 표시**

`Assets/Docs/job-pipeline-waste-removal.md` 상단에 실측 결과를 한 줄 추가한다:

```markdown
> **구현 완료 (YYYY-MM-DD).** LagDiag 실측: CompleteAll <이전>ms → <이후>ms.
> enableRound1Preview 기본값 false 로 확정 / 유지 필요.
```

- [ ] **Step 5: 체크포인트**

변경: `TerrainChunk.cs`, `job-pipeline-waste-removal.md`

---

## 남은 과제 (이번 범위 밖 — 설계 §8)

1. **JobHandle 8개 수동 의존성 관리** → 리소스 단위 의존성 추적기로 자동화.
   이번에 `AllPrevHandles()`로 한 곳에 모았으므로 다음 리팩토링의 진입점이 생겼다.
2. **`_lastExt*` 숨은 가변 상태** → rect를 명시적 값 객체로 전달.
   이번에 `SetDirtyRect`로 진입점을 하나로 좁혔다.
3. **`BasePixelsHalf` / `PixelInfoHalf` 버퍼 제거** → Init/Chamfer가 full `BasePixels`를 inline 2×2 샘플.
   `DownsampleMaskJob` + NativeArray 2개(청크당 ~1.25MB) 제거.
4. **이름과 실체 불일치** → `_lightingJobHandle`(실제 ChamferBackward),
   `TerrainLightingCalculator`(실제 BoundarySync 스케줄러).
   (`InitBFSJob` → `InitDistanceFieldJob` 개명은 Task 2에서 완료 — 아래 참고)
5. **`LagDiag` 임시 계측 코드 제거** (Task 5 Step 13의 측정이 끝난 뒤).
6. **`enableRound1Preview` 플래그 제거** (A/B 검증이 끝난 뒤).
