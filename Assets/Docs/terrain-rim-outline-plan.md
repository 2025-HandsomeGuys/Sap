# 지형 최외곽 테두리(rim) 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (권장) 또는 superpowers:executing-plans 로 task 단위 실행. 스텝은 체크박스(`- [ ]`) 로 추적한다.

**Goal:** 땅을 팠을 때 공기와 맞닿는 최외곽 2픽셀을 균일 두께의 짙은 단색 라인으로 렌더한다.

**Architecture:** half-res 거리장으로는 얇은 최외곽 라인을 표현할 수 없다(2×2 계단·패리티 흔들림). 따라서 rim만 `VisualUpdateJob` 안에서 **full-res `baseData` 를 유클리드 원판(13탭)으로 직접 스캔**해 판정한다. 새 Burst 잡·새 버퍼·새 잡 의존성 없음. 청크 가장자리 2px는 BoundarySync 된 거리장으로 폴백한다.

**Tech Stack:** Unity 2D, C#, Burst, `IJobParallelFor`, `NativeArray`, NUnit EditMode

**설계 문서:** `Assets/Docs/terrain-rim-outline.md` — 상수 유도(게이트 `5t+7` 상한, 폴백 `t*5` 전파표)가 전부 여기 있다. 상수를 바꾸려면 반드시 먼저 읽을 것.

## Global Constraints

- **버전 관리는 UVCS.** `git` 명령을 쓰지 않는다. 체크인은 사람이 직접 한다 — Claude는 체크인하지 않는다.
- **Unity 테스트 실행은 사람이 한다.** Claude는 테스트 파일 작성·수정만 하고 Test Runner 를 호출하지 않는다. 테스트 통과를 다음 task 의 게이트로 삼지 않는다.
- **`TerrainChunk.cs.private.0` 은 절대 편집하지 않는다.** 확장자가 `.cs` 가 아니라 Unity 컴파일 대상이 아닌 잔재 파일이다. 실제 파일은 `TerrainChunk.cs`.
- **런타임 설정은 static 패턴.** non-serialized 필드는 `Instantiate` 시 프리팹에서 복사되지 않는다 (CLAUDE.md 아키텍처 제약 §4).
- rim 두께 기본값 `t = 2` (px), 기본색 `#241009`. `rimThicknessPx: 0` 이면 rim 완전 비활성 = 기존 동작.
- 파생 상수: `rimR2 = t²`, `rimGateDist = (t+2)*10`, `rimEdgeFallbackDist = t*5`.

### 🚨 이 프로젝트에서 이미 대가를 치른 함정 두 개

**1. Burst 스테일 커널 — 이 계획이 정확히 그 트리거다.**
Burst 잡 struct 의 **필드를 바꾸면** Burst 가 옛 커널을 계속 쓸 수 있다. **Unity 재시작으로도 안 풀린다.**
증상은 "소스상 불가능한 예외"(가드가 있는데 터짐) 또는 "새 필드가 그냥 무시됨"이다.
이 프로젝트는 여기에 2시간을 날렸고, 해법으로 **잡 struct 의 타입명을 바꿔** 캐시 엔트리를 새로 만들었다
(`InitBFSJob` → `InitDistanceFieldJob`).

→ 이 계획은 `VisualUpdateJob` 에 필드 5개를 추가한다. **그러므로 struct 이름도 함께 바꾼다** (Task 1 Step 1).
이건 선택이 아니라 함정 회피 절차다.

**2. EditMode 테스트는 job safety 를 증명하지 못한다.**
`IJobParallelForExtensions.Run(n)` 은 메인 스레드 순차 실행이라 **parallel-for 인덱스 제약을 적용하지 않는다.**
`[NativeDisableParallelForRestriction]` 을 빠뜨려도 **EditMode 는 초록이고 Play 에서만 터진다.**

→ Task 1 의 테스트 6개가 전부 통과해도 그건 **rim 판정 로직의 증거일 뿐 job safety 의 증거가 아니다.**
Play 모드 확인(Task 3 Step 3)을 건너뛰지 말 것.

---

## File Structure

| 파일 | 책임 | Task |
|---|---|---|
| `Assets/Scripts/_Core/Managers/TerrainJobs.cs` | `VisualUpdateJob` 에 rim 필드 + `IsRimDisc()` + Execute 분기 | 1 |
| `Assets/Tests/EditMode/VisualUpdateJobRimTests.cs` | rim 판정 로직 EditMode 테스트 (신규) | 1 |
| `Assets/Scripts/_Core/Data/WorldSettingsData.cs` | `ChunkSection` 에 `rimThicknessPx` / `rimColor` 필드 | 2 |
| `Assets/StreamingAssets/worldSettings.json` | 두 값의 실제 설정치 | 2 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` | static `s_rimThicknessPx` / `s_rimColor` + `SetRimSettings()` | 2 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs` | `ApplySettings()` 에서 hex 파싱 후 static 주입 | 2 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs` | `ScheduleVisualJob()` 에서 static 읽어 잡 필드 세팅 | 3 |

`TerrainVisualizer.cs` 는 **건드리지 않는다.** `ScheduleVisualJob` 이 `TerrainChunk` static 을 직접 읽는 기존 패턴(`debugDistanceField`)을 그대로 따르므로 시그니처 변경이 없다.

Task 1 은 Task 2·3 없이 독립적으로 테스트된다(잡 필드를 테스트에서 직접 세팅). Task 3 이 완료돼야 게임에서 보인다.

---

## Task 1: 잡 개명 + rim 판정 로직

**Files:**
- Modify: `Assets/Scripts/_Core/Managers/TerrainJobs.cs` (`VisualUpdateJob` → `TerrainVisualJob`, 19~194행)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs` (170행 — 개명 반영만)
- Create: `Assets/Tests/EditMode/TerrainVisualJobRimTests.cs`

**Interfaces:**
- Consumes: 없음 (기존 필드 `baseData`, `pixelInfo`, `distanceField`, `outputTexture`, `width`, `height`)
- Produces:
  - 잡 타입명 **`TerrainJobs.TerrainVisualJob`** (구 `VisualUpdateJob`) — Task 3 이 이 이름으로 참조한다
  - public 필드 5개 — Task 3 이 이 이름으로 세팅한다:
    - `public int rimThicknessPx` (0 = 비활성)
    - `public int rimR2`
    - `public int rimGateDist`
    - `public int rimEdgeFallbackDist`
    - `public Color32 rimColor`

---

- [ ] **Step 1: 잡 struct 개명 — `VisualUpdateJob` → `TerrainVisualJob`**

**필드를 추가하기 전에 먼저 개명한다.** Burst 스테일 커널 회피 절차다 (Global Constraints 참고).
필드만 추가하고 이름을 그대로 두면, Burst 가 옛 커널을 재사용해 **새 rim 필드가 조용히 무시될 수 있고 Unity 재시작으로도 안 풀린다.**

`TerrainJobs.cs`:

```csharp
    // ============================================================================================================
    //  TERRAIN VISUAL JOB (Parallel) - 베이스 지형 + 테두리 + rim 렌더
    //  (구 VisualUpdateJob — rim 필드 추가 시 Burst 스테일 커널을 피하려고 개명했다.
    //   배경: Burst 는 잡 struct 의 필드가 바뀌어도 옛 커널을 재사용할 수 있고 재시작으로 안 풀린다.
    //   InitBFSJob → InitDistanceFieldJob 개명과 같은 이유.)
    // ============================================================================================================

    [BurstCompile]
    public struct TerrainVisualJob : IJobParallelFor
```

`ChunkJobScheduler.cs` 170행의 참조도 함께 바꾼다:

```csharp
        var job = new TerrainJobs.TerrainVisualJob
```

이 시점에서 컴파일이 통과해야 한다. 동작은 이전과 100% 동일하다 (이름만 바뀜).

- [ ] **Step 2: 개명만으로 게임이 그대로인지 확인 (사람)**

플레이해서 땅을 파본다. 테두리 렌더가 **개명 전과 완전히 동일**해야 한다.
여기서 뭔가 달라지면 개명 과정에서 실수한 것이므로 다음으로 넘어가지 말 것.

- [ ] **Step 3: 실패하는 테스트 작성**

`Assets/Tests/EditMode/TerrainVisualJobRimTests.cs` 를 새로 만든다.

`TerrainJobsRectScopeTests.cs` 의 패턴을 따른다 — 잡 구조체를 직접 만들고 `.Run(n)` 으로 돌린 뒤 `NativeArray` 를 검사한다.

> ⚠ **이 테스트들은 rim 판정 로직만 검증한다. job safety 는 검증하지 못한다.**
> `.Run(n)` 은 메인 스레드 순차 실행이라 parallel-for 인덱스 제약을 적용하지 않는다.
> 6개가 전부 초록이어도 Play 모드에서 `InvalidOperationException` 이 날 수 있다 (Task 3 Step 3).

테스트 설계 메모 (구현자용):
- `textureThicknessPx = 0` 으로 둔다 → 솔리드 픽셀의 `dist(=100)` 가 `0*5=0` 보다 크므로 borderTexture 분기를 타지 않는다. rim 아니면 `baseCol` 이 그대로 나온다 → rim 판정만 격리해서 볼 수 있다.
- `distanceField` 는 기본 `0` 으로 채운다 → 게이트(`rawDist <= rimGateDist`)가 항상 열린다. 게이트 자체는 `Gate_Blocks...` 테스트에서 따로 본다.
- **`DiscIsEuclidean_NotChebyshev` 가 이 설계의 핵심 테스트다.** 5×5 정사각형(체비셰프) 구현이면 (2,2) 오프셋이 rim 이 되어 45° 경사에서 두께가 2.8px 로 벌어진다. 유클리드 원판이면 `8 > 4` 라 rim 이 아니다.

```csharp
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;   // IJobParallelFor.Run(n) 확장 메서드
using UnityEngine;

/// <summary>
/// TerrainVisualJob 의 rim(최외곽 테두리) 판정 검증.
///
/// ⚠ 이 테스트는 rim 판정 로직만 본다. job safety 는 검증하지 못한다 —
///    .Run(n) 은 메인 스레드 순차 실행이라 parallel-for 인덱스 제약을 적용하지 않는다.
///
/// 설계: Assets/Docs/terrain-rim-outline.md
/// </summary>
public class TerrainVisualJobRimTests
{
    private const int W = 16;
    private const int H = 16;
    private const int T = 2;                       // rimThicknessPx
    private const int GATE = (T + 2) * 10;         // 40
    private const int EDGE_FALLBACK = T * 5;       // 10
    private const int SOLID_DIST = 100;            // 솔리드 픽셀의 기본 거리값

    private static readonly Color32 RIM   = new Color32(255, 0, 255, 255);
    private static readonly Color32 SOLID = new Color32(80, 60, 40, 255);
    private static readonly Color32 AIR   = new Color32(0, 0, 0, 0);

    /// <summary>전부 솔리드인 baseData. 호출자가 공기를 뚫는다.</summary>
    private static NativeArray<Color32> MakeSolidBase()
    {
        var a = new NativeArray<Color32>(W * H, Allocator.Temp);
        for (int i = 0; i < a.Length; i++) a[i] = SOLID;
        return a;
    }

    /// <summary>모든 픽셀 dist = value. 0이면 게이트가 항상 열린다.</summary>
    private static NativeArray<ushort> MakeDist(ushort value)
    {
        var a = new NativeArray<ushort>(W * H, Allocator.Temp);
        for (int i = 0; i < a.Length; i++) a[i] = value;
        return a;
    }

    /// <summary>rim 이 켜진 잡을 rect 전체(0,0)~(W,H)에 대해 실행하고 출력 텍스처를 돌려준다.</summary>
    private static NativeArray<Color32> RunJob(
        NativeArray<Color32> baseData,
        NativeArray<byte> pixelInfo,
        NativeArray<ushort> dist,
        int rimThicknessPx)
    {
        var output = new NativeArray<Color32>(W * H, Allocator.Temp);

        // borderData 는 길이 1짜리 더미. textureThicknessPx=0 이라 실제로 샘플되지 않는다.
        var borderData = new NativeArray<Color32>(1, Allocator.Temp);
        var emptySecondary = new NativeArray<Color32>(0, Allocator.Temp);

        new TerrainJobs.TerrainVisualJob
        {
            baseData = baseData,
            distanceField = dist,
            borderData = borderData,
            pixelInfo = pixelInfo,
            secondaryBorderData = emptySecondary,
            secondaryTileId = 0,
            secondaryBorderWidth = 0,
            secondaryBorderHeight = 0,
            outputTexture = output,
            width = W,
            height = H,
            textureThicknessPx = 0,          // borderTexture 분기 비활성 → rim 만 격리
            borderWidth = 1,
            borderHeight = 1,
            chunkOffsetX = 0,
            chunkOffsetY = 0,
            debugDistanceField = false,
            rectMinX = 0,
            rectMinY = 0,
            rectWidth = W,

            rimThicknessPx = rimThicknessPx,
            rimR2 = rimThicknessPx * rimThicknessPx,
            rimGateDist = (rimThicknessPx + 2) * 10,
            rimEdgeFallbackDist = rimThicknessPx * 5,
            rimColor = RIM,
        }.Run(W * H);

        borderData.Dispose();
        emptySecondary.Dispose();
        return output;
    }

    private static bool IsRim(NativeArray<Color32> output, int x, int y)
    {
        Color32 c = output[y * W + x];
        return c.r == RIM.r && c.g == RIM.g && c.b == RIM.b && c.a == RIM.a;
    }

    [Test]
    public void FlatSurface_RimIsExactlyTwoPixelsThick()
    {
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(0);

        // y >= 8 은 공기, y < 8 은 솔리드 → 표면은 y=7
        for (int y = 8; y < H; y++)
        for (int x = 0; x < W; x++)
            baseData[y * W + x] = AIR;

        var output = RunJob(baseData, pixelInfo, dist, T);

        // 내부 열(x=2..13)만 검사 — 가장자리 2px는 폴백 경로라 별도 테스트
        for (int x = T; x < W - T; x++)
        {
            Assert.IsTrue (IsRim(output, x, 7), $"x={x}, y=7 (공기까지 1px) 은 rim 이어야 한다");
            Assert.IsTrue (IsRim(output, x, 6), $"x={x}, y=6 (공기까지 2px) 은 rim 이어야 한다");
            Assert.IsFalse(IsRim(output, x, 5), $"x={x}, y=5 (공기까지 3px) 은 rim 이 아니어야 한다");
        }

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void DiscIsEuclidean_NotChebyshev()
    {
        // 이 테스트가 체비셰프(5×5 정사각) 구현을 잡아낸다.
        // 정사각형이면 (2,2) 오프셋도 rim 이 되어 45° 경사에서 두께가 2.8px 로 벌어진다.
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(0);

        baseData[8 * W + 8] = AIR;   // 공기 픽셀 딱 하나

        var output = RunJob(baseData, pixelInfo, dist, T);

        Assert.IsTrue (IsRim(output, 10,  8), "(2,0) 오프셋: 4 <= 4 → rim");
        Assert.IsTrue (IsRim(output,  9,  9), "(1,1) 오프셋: 2 <= 4 → rim");
        Assert.IsFalse(IsRim(output, 10, 10), "(2,2) 오프셋: 8 > 4 → rim 아님 (체비셰프면 여기서 실패한다)");
        Assert.IsFalse(IsRim(output, 11,  8), "(3,0) 오프셋: 9 > 4 → rim 아님");

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void IndestructiblePixel_IsRimSource_EvenThoughAlphaIsOpaque()
    {
        // 파괴 불가 픽셀은 BasePixels 에서 alpha != 0 을 유지한다 (BFS 빛 차단 목적).
        // alpha 만 검사하는 구현이면 이 테스트가 실패한다.
        var baseData  = MakeSolidBase();                              // 공기 없음
        var pixelInfo = new NativeArray<byte>(W * H, Allocator.Temp); // 전부 0
        var dist      = MakeDist(0);

        pixelInfo[8 * W + 8] = 128;   // indestructible 비트

        var output = RunJob(baseData, pixelInfo, dist, T);

        Assert.IsTrue (IsRim(output, 10, 8), "파괴 불가 픽셀에서 2px 이내 → rim");
        Assert.IsFalse(IsRim(output, 11, 8), "파괴 불가 픽셀에서 3px → rim 아님");

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void ChunkEdge_UsesDistanceFieldFallback_NotDiscScan()
    {
        // 가장자리 t픽셀 이내는 원판이 OOB 로 나가므로 distanceField 로 판정한다.
        // baseData 에는 공기가 전혀 없다 → 원판 스캔이었다면 rim 이 하나도 안 나온다.
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(SOLID_DIST);   // 기본은 게이트 밖

        dist[3 * W + 0] = EDGE_FALLBACK;        // (0,3): 10 <= 10 → rim
        dist[3 * W + 1] = EDGE_FALLBACK + 10;   // (1,3): 20 >  10 → rim 아님 (게이트 40 은 통과)

        var output = RunJob(baseData, pixelInfo, dist, T);

        Assert.IsTrue (IsRim(output, 0, 3), "가장자리 픽셀 dist=10 → 폴백으로 rim");
        Assert.IsFalse(IsRim(output, 1, 3), "가장자리 픽셀 dist=20 → 폴백 임계값 초과 → rim 아님");

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void Gate_BlocksDiscScan_WhenDistanceIsLarge()
    {
        // 게이트가 실제로 작동하는지. 실제 거리장에서는 이런 상황이 생길 수 없다
        // (rim 픽셀의 업샘플 dist 상한 = 5t+7 = 17 << 40, 설계 §3-4 유도).
        // 순수하게 게이트 메커니즘만 검증하는 테스트다.
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(0);

        baseData[8 * W + 8] = AIR;
        dist[8 * W + 10] = GATE + 1;   // (10,8) 은 공기에서 2px 지만 게이트를 못 넘는다

        var output = RunJob(baseData, pixelInfo, dist, T);

        Assert.IsFalse(IsRim(output, 10, 8), "게이트를 못 넘으면 원판 스캔을 하지 않는다");
        Assert.IsTrue (IsRim(output,  6, 8), "게이트 안쪽(dist=0)의 같은 거리 픽셀은 정상적으로 rim");

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void RimThicknessZero_DisablesRimEntirely()
    {
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(0);

        baseData[8 * W + 8] = AIR;

        var output = RunJob(baseData, pixelInfo, dist, 0);   // rimThicknessPx = 0

        for (int i = 0; i < output.Length; i++)
        {
            Color32 c = output[i];
            bool isRimColor = c.r == RIM.r && c.g == RIM.g && c.b == RIM.b && c.a == RIM.a;
            Assert.IsFalse(isRimColor, $"rimThicknessPx=0 이면 rim 픽셀이 하나도 없어야 한다 (idx={i})");
        }

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }
}
```

- [ ] **Step 4: 테스트가 컴파일되지 않는 것을 확인**

Unity 에디터로 돌아가 컴파일 에러를 확인한다.
기대: `TerrainVisualJob` 에 `rimThicknessPx`, `rimR2`, `rimGateDist`, `rimEdgeFallbackDist`, `rimColor` 필드가 없다는 CS0117 에러.

(테스트 실행은 사람이 한다. 컴파일 에러 확인만으로 충분하다.)

- [ ] **Step 5: TerrainVisualJob 에 rim 필드 추가**

`TerrainJobs.cs` 의 `TerrainVisualJob` 에서 `debugDistanceField` 선언(51행) **바로 아래**에 추가한다:

```csharp
        // Debug: DistanceField 등고선 시각화. true 이면 솔리드 픽셀을 거리 기반 회색조 밴드로 출력.
        public bool debugDistanceField;

        // [rim] 공기·파괴불가 픽셀과 맞닿는 최외곽 단색 라인. 0 이면 비활성.
        // half-res 거리장으로는 얇은 라인을 균일하게 그릴 수 없어 full-res baseData 를 직접 스캔한다.
        // 파생 상수는 ChunkJobScheduler 가 계산해 넣는다. 설계: Assets/Docs/terrain-rim-outline.md
        public int     rimThicknessPx;       // t
        public int     rimR2;                // t² — 원판 판정용
        public int     rimGateDist;          // (t+2)*10 — full-res 스캔 게이트
        public int     rimEdgeFallbackDist;  // t*5 — 청크 가장자리 폴백 임계값
        public Color32 rimColor;
```

- [ ] **Step 6: IsRimDisc 메서드 추가**

`LogBorderInvalid` 선언(58~60행) **바로 아래**, `Execute` **앞**에 추가한다:

```csharp
        /// <summary>
        /// 반경 rimThicknessPx 유클리드 원판 안에 공기 또는 파괴불가 픽셀이 있으면 true.
        ///
        /// 원판(dx²+dy² ≤ t²)이어야 경사와 무관하게 수직 두께가 t 로 유지된다.
        /// 5×5 정사각형(체비셰프)으로 바꾸면 45° 경사에서 두께가 2.8px 로 벌어진다.
        ///
        /// 호출 시점에 OOB 는 불가능하다 — Execute 의 isEdge 분기가 가장자리를 이미 걸러낸다.
        /// </summary>
        private bool IsRimDisc(int px, int py)
        {
            int t = rimThicknessPx;
            for (int dy = -t; dy <= t; dy++)
            for (int dx = -t; dx <= t; dx++)
            {
                if (dx * dx + dy * dy > rimR2) continue;

                int ni = (py + dy) * width + (px + dx);
                if (baseData[ni].a == 0) return true;                              // 공기

                // 파괴 불가 픽셀은 BasePixels 에서 alpha != 0 을 유지한다(BFS 빛 차단).
                // 따라서 alpha 만 봐서는 안 되고 이 비트를 따로 검사해야 한다.
                if (pixelInfo.Length > 0 && (pixelInfo[ni] & 128) != 0) return true;
            }
            return false;
        }
```

- [ ] **Step 7: Execute 에 rim 분기 삽입**

`Execute` 안, `debugDistanceField` 블록(79~86행) **직후**, 기존 `ushort dist = distanceField[index];`(92행) **앞**에 삽입한다.

`bestOrtho` 보정보다 앞이어야 한다 — 그 보정은 rim 판정을 흔들기만 한다.

```csharp
            // Debug: 등고선 시각화 — 25 dist 단위로 밝기 교대 (Case A/B 판별용)
            if (debugDistanceField)
            {
                ushort d = distanceField[index];
                int band = (d / 25) % 2;
                byte g = band == 0 ? (byte)80 : (byte)200;
                outputTexture[index] = new Color32(g, g, g, 255);
                return;
            }

            // [rim] 최외곽 라인 — borderTexture 보다 우선한다.
            // 게이트: 업샘플 dist 는 실제 거리를 최대 +7 까지만 과대평가하고 rim 후보의 실제
            // 거리는 최대 5t 이므로, rim 픽셀의 dist 는 5t+7 이하다. (t+2)*10 이면 충분히 안전.
            // 설계·유도: Assets/Docs/terrain-rim-outline.md §3-4
            if (rimThicknessPx > 0)
            {
                ushort rawDist = distanceField[index];
                if (rawDist <= rimGateDist)
                {
                    int t = rimThicknessPx;
                    bool isEdge = px < t || px >= width - t || py < t || py >= height - t;

                    // 가장자리는 원판이 청크 밖으로 나가므로 BoundarySync 된 거리장으로 폴백 (§4)
                    bool isRim = isEdge ? (rawDist <= rimEdgeFallbackDist)
                                        : IsRimDisc(px, py);

                    if (isRim)
                    {
                        outputTexture[index] = rimColor;
                        return;
                    }
                }
            }

            // 2. Logic: Draw borders based on distance from air
            ushort dist = distanceField[index];
```

- [ ] **Step 8: 컴파일 확인**

Unity 에디터에서 컴파일 에러가 없는지 확인한다. Burst 컴파일 경고도 없어야 한다.

`IsRimDisc` 는 `[BurstCompile]` 구조체의 인스턴스 메서드이고 `NativeArray` 읽기만 하므로 Burst 가 인라인한다.

- [ ] **Step 9: 테스트 실행 (사람)**

Unity Test Runner → EditMode → `TerrainVisualJobRimTests` 6개.
기대: 전부 PASS.

실패하면 실패한 테스트 이름을 알려줄 것. 진단 힌트:
- `DiscIsEuclidean_NotChebyshev` 실패 → 원판 조건(`dx*dx + dy*dy > rimR2`)이 잘못됐다
- `IndestructiblePixel_...` 실패 → `pixelInfo & 128` 검사를 빠뜨렸다
- **전부 실패하거나 rim 필드가 통째로 무시되는 것처럼 보이면 → Burst 스테일 커널을 의심하라.** Step 1 개명이 제대로 됐는지 확인할 것.

- [ ] **Step 10: UVCS 체크인 (사람)**

변경 파일: `TerrainJobs.cs`, `ChunkJobScheduler.cs`(개명 반영), `TerrainVisualJobRimTests.cs`

---

## Task 2: 설정 파라미터 (worldSettings.json → static)

**Files:**
- Modify: `Assets/Scripts/_Core/Data/WorldSettingsData.cs` (`ChunkSection`, 52~63행)
- Modify: `Assets/StreamingAssets/worldSettings.json` (`chunk` 섹션)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` (static 선언부 ~83행, `SetDefaultColliderUpdateInterval` 근처)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs` (`ApplySettings()`, ~323행)

**Interfaces:**
- Consumes: 없음
- Produces: Task 3 이 읽는 두 static getter —
  - `public static int TerrainChunk.RimThicknessPx { get; }`
  - `public static Color32 TerrainChunk.RimColor { get; }`

---

- [ ] **Step 1: WorldSettingsData 에 필드 추가**

클래스명이 `ChunkSettings` 가 아니라 **`ChunkSection`** 이다. `colliderSimplifyTolerance` 아래에 추가한다:

```csharp
    [System.Serializable]
    public class ChunkSection
    {
        public float verticalScale         = 1.5f;
        public bool  useIslandRemoval      = true;
        public int   maxIslandSize         = 50;
        public float solidThickness        = 1.6f;
        public float textureThickness      = 4.0f;
        public float colliderUpdateInterval= 0.2f;
        // RDP 콜라이더 단순화 허용오차(월드 좌표, PPU 의존). 클수록 정점·물리 프록시 감소.
        public float colliderSimplifyTolerance = 0.005f;

        /// <summary>공기·파괴불가 픽셀과 맞닿는 최외곽 rim 라인 두께(px). 0 이면 비활성.
        /// 설계: Assets/Docs/terrain-rim-outline.md</summary>
        public int rimThicknessPx = 2;

        /// <summary>rim 색(hex).
        /// ⚠ JsonUtility 는 hex 문자열을 Color32 로 역직렬화하지 못하므로 반드시 string 이어야 한다.
        /// 파싱은 InfinityMapManager.ApplySettings 에서 ColorUtility.TryParseHtmlString 으로 한다.</summary>
        public string rimColor = "#241009";
    }
```

- [ ] **Step 2: worldSettings.json 에 값 추가**

`chunk` 섹션 마지막 항목 뒤에 쉼표를 붙이고 두 줄을 추가한다:

```json
  "chunk": {
    "verticalScale": 1.5,
    "useIslandRemoval": true,
    "maxIslandSize": 50,
    "solidThickness": 1.6,
    "textureThickness": 4.0,
    "colliderUpdateInterval": 0.2,
    "colliderSimplifyTolerance": 0.1,
    "rimThicknessPx": 2,
    "rimColor": "#241009"
  }
```

- [ ] **Step 3: TerrainChunk 에 static 추가**

**`TerrainChunk.cs` 를 수정한다. `TerrainChunk.cs.private.0` 이 아니다.**

`private static float s_colliderUpdateInterval = 0.2f;` (83행) 아래에 추가한다:

```csharp
    private static float s_colliderUpdateInterval = 0.2f;

    /// <summary>
    /// [rim] 공기·파괴불가 픽셀과 맞닿는 최외곽 라인 두께(px). 0 이면 비활성.
    /// worldSettings.json 의 chunk.rimThicknessPx / chunk.rimColor 로 덮어쓴다.
    /// non-serialized 필드는 Instantiate 시 프리팹에서 복사되지 않으므로 static 이어야 한다.
    /// 설계: Assets/Docs/terrain-rim-outline.md
    /// </summary>
    private static int     s_rimThicknessPx = 2;
    private static Color32 s_rimColor       = new Color32(0x24, 0x10, 0x09, 255);

    public static int     RimThicknessPx => s_rimThicknessPx;
    public static Color32 RimColor       => s_rimColor;

    public static void SetRimSettings(int thicknessPx, Color32 color)
    {
        s_rimThicknessPx = Mathf.Max(0, thicknessPx);
        s_rimColor       = color;
    }
```

- [ ] **Step 4: InfinityMapManager.ApplySettings 에서 주입**

`TerrainCollider.SetDefaultSimplifyTolerance(...)` (324행) 아래에 추가한다:

```csharp
        TerrainChunk.SetDefaultColliderUpdateInterval(s.chunk.colliderUpdateInterval);
        TerrainCollider.SetDefaultSimplifyTolerance(s.chunk.colliderSimplifyTolerance);

        // [rim] 최외곽 테두리 (Assets/Docs/terrain-rim-outline.md)
        // JsonUtility 는 hex → Color32 변환을 못 하므로 string 으로 받아 여기서 파싱한다.
        if (!ColorUtility.TryParseHtmlString(s.chunk.rimColor, out Color rimColor))
        {
            Debug.LogWarning($"[InfinityMapManager] chunk.rimColor 파싱 실패: '{s.chunk.rimColor}' — 기본색 사용");
            rimColor = new Color32(0x24, 0x10, 0x09, 255);
        }
        TerrainChunk.SetRimSettings(s.chunk.rimThicknessPx, rimColor);
```

`ColorUtility.TryParseHtmlString` 은 `Color` 를 내놓고 `Color → Color32` 암묵 변환이 있으므로 그대로 넘기면 된다.

- [ ] **Step 5: 컴파일 + 로드 확인**

Unity 에디터에서 컴파일 에러가 없는지 확인한다.

플레이 모드로 들어가 Console 에 `chunk.rimColor 파싱 실패` 경고가 **뜨지 않는지** 본다. 뜨면 json 의 hex 형식(`#RRGGBB`)이 틀린 것이다.

- [ ] **Step 6: UVCS 체크인 (사람)**

변경 파일: `WorldSettingsData.cs`, `worldSettings.json`, `TerrainChunk.cs`, `InfinityMapManager.cs`

이 시점까지는 **게임 화면에 아무 변화가 없다.** 설정이 static 까지만 도달했고 잡에 연결되지 않았다. Task 3 에서 연결된다.

---

## Task 3: ChunkJobScheduler 연결 + 인게임 검증

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs` (`ScheduleVisualJob()`, 162~199행)

**Interfaces:**
- Consumes:
  - Task 1: `VisualUpdateJob.rimThicknessPx` / `rimR2` / `rimGateDist` / `rimEdgeFallbackDist` / `rimColor`
  - Task 2: `TerrainChunk.RimThicknessPx` / `TerrainChunk.RimColor`
- Produces: 없음 (최종 배선)

---

- [ ] **Step 1: ScheduleVisualJob 에서 static 읽어 잡 필드 세팅**

`ScheduleVisualJob` 의 잡 이니셜라이저에서 `debugDistanceField = TerrainChunk.DebugDistanceField,` (198행) **바로 아래**에 추가한다.

`TerrainVisualizer` 를 거치지 않는다 — `debugDistanceField` 가 이미 static 을 직접 읽는 선례다. 덕분에 `ScheduleVisualJob` 시그니처도, 호출부 3곳도 그대로 둔다.

```csharp
            debugDistanceField = TerrainChunk.DebugDistanceField,

            // [rim] TerrainChunk static 직접 읽기 (debugDistanceField 와 동일 패턴).
            // 파생 상수는 여기서 계산한다 — 유도는 Assets/Docs/terrain-rim-outline.md §3-4, §4
            rimThicknessPx      = TerrainChunk.RimThicknessPx,
            rimR2               = TerrainChunk.RimThicknessPx * TerrainChunk.RimThicknessPx,
            rimGateDist         = (TerrainChunk.RimThicknessPx + 2) * 10,
            rimEdgeFallbackDist = TerrainChunk.RimThicknessPx * 5,
            rimColor            = TerrainChunk.RimColor,
```

- [ ] **Step 2: 형광색으로 두께 육안 검증**

`worldSettings.json` 의 `rimColor` 를 임시로 형광색으로 바꾼다:

```json
    "rimColor": "#FF00FF"
```

플레이해서 땅을 판 뒤 **화면을 확대**해 확인한다:

1. **평지** — 마젠타 라인이 정확히 2px 인가
2. **45° 경사** — 수직 방향 두께가 여전히 2px 인가 (여기서 톱니가 남으면 원판이 아니라 정사각형으로 구현된 것)
3. **오목 코너 / 볼록 코너** — 두께가 유지되는가
4. **청크 seam** — 라인이 **끊기지 않는가** (약간 두꺼워지는 것은 설계상 허용, §4)
5. **땅속에 묻힌 바위**(카빙 오브젝트) — rim 이 **그려지지 않아야 한다.**
   공기와 안 맞닿았으니까. 여기에 마젠타 라인이 보이면 `IsRimDisc` 가 `pixelInfo & 128` 을
   소스로 쓰고 있는 것이다 (그 비트는 파괴 불가가 아니라 카빙 오브젝트 실루엣 마커 — 설계 §3-3).
   묻힌 바위 경계는 종전대로 borderTexture 그라데이션이어야 한다.
6. **파괴 불가 오버레이**(특수청크) 옆을 파보기 — 새로 생긴 공기 쪽에 rim 이 붙는가.
   오버레이 자체는 솔리드 픽셀이므로 별도 처리 없이 자연히 붙어야 정상이다.

- [ ] **Step 3: Play 모드 job safety 확인 — EditMode 그린은 증거가 아니다**

**Task 1 의 테스트 6개가 전부 통과했어도 이 스텝을 건너뛰면 안 된다.**
`.Run(n)` 은 메인 스레드 순차 실행이라 parallel-for 인덱스 제약을 적용하지 않는다.
어트리뷰트가 틀렸어도 EditMode 는 초록이고 **Play 에서만 터진다.**

Play 모드에서 땅을 파면서 Console 에 `InvalidOperationException` 이 나오는지 본다.
특히 `baseData` / `pixelInfo` 관련 "index out of restricted range" 계열 메시지.

- **안 나오면** 예상대로다. `[ReadOnly]` 배열은 `IJobParallelFor` 에서 임의 인덱스 읽기가 허용되고,
  기존 코드도 이미 `baseData[index]`(rect-local `i` 와 다른 인덱스)로 읽고 있다.
- **나오면** `TerrainVisualJob` 의 `baseData` / `pixelInfo` 선언에 `[NativeDisableParallelForRestriction]` 을 붙인다
  (`distanceField` 가 이미 그렇게 돼 있다 — 같은 패턴):

```csharp
        [ReadOnly] [NativeDisableParallelForRestriction] public NativeArray<Color32> baseData;
        [ReadOnly] [NativeDisableParallelForRestriction] public NativeArray<byte> pixelInfo;
```

- [ ] **Step 4: 드릴 연속 파기로 성능 확인**

드릴로 연속 파기하면서 프레임 저하가 없는지 본다.

rim 은 게이트를 통과한 표면 근처 픽셀만 13탭을 돌므로 측정 가능한 비용이 나오면 안 된다.
저하가 보이면 `[LAGDIAG]` 로그(`ChunkJobScheduler.CompleteAllJobs`)의 `vis=` 항목을 rim 도입 전후로 비교할 것.
(직전 최적화 후 기준치는 `vis=0.0` — 계측에 안 잡히는 수준이었다.)

- [ ] **Step 5: rimThicknessPx: 0 회귀 확인**

`worldSettings.json` 에서 `"rimThicknessPx": 0` 으로 두고 플레이한다.
기대: rim 이 전혀 없고 **기존 borderTexture 렌더가 도입 전과 동일**하다.

- [ ] **Step 6: 최종 색으로 되돌리기**

```json
    "rimThicknessPx": 2,
    "rimColor": "#241009"
```

실제 지형 팔레트에 맞는 색은 여기서 눈으로 보고 조정한다. `#241009` 는 시작점일 뿐이다.

- [ ] **Step 7: UVCS 체크인 (사람)**

변경 파일: `ChunkJobScheduler.cs`, `worldSettings.json`

---

## 완료 후 남는 것

- **청크 seam 2px strip** — 폴백 경로라 계단이 남는다. 설계상 의도된 한계다(§4). 실제 플레이에서 명확히 거슬릴 때만 재검토하고, 그때도 이웃 `BasePixels` 를 넘기는 비용(seam 근처를 팔 때마다 이웃 VisualJob 4개에 동기화 포인트)을 먼저 측정할 것.
- **`enableRound1Preview: true`** 로 켜면 seam rim 이 1프레임 튄다 (Round 1 Visual 은 BoundarySync 이전). 기본값 `false` 라 현재는 무해.
