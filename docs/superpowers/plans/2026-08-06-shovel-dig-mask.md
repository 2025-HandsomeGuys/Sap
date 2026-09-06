# 삽 파기 모양 마스크 이미지화 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 삽(toolIndex=1)이 파는 구멍 모양을 PNG 마스크 이미지로 정의할 수 있게 하고, 마스크가 없거나 잘못됐으면 기존 회전 타원으로 자동 폴백한다.

**Architecture:** 마스크 상태·베이크·샘플링을 `ShovelDigMask` static 클래스 하나로 분리한다. `TerrainModifier.ProcessDigPixels`는 기존 타원 판정을 그대로 둔 채 `if (마스크 유효) → 마스크 / else → 타원` 분기만 추가한다. 인스펙터(`PlayerMining`)는 `ShovelDigMask.Set()`을 호출할 뿐 계산에 관여하지 않는다.

**Tech Stack:** Unity 2D, C#, NUnit (EditMode), `Texture2D.GetPixels32`

## Global Constraints

- **버전 관리는 UVCS다. `git add`/`git commit` 등 git 명령어를 쓰지 않는다.** 각 Task 끝의 "커밋"은 사람이 UVCS에서 직접 체크인한다. 계획에 git 명령을 넣지 않는다. (CLAUDE.md)
- **Unity Test Runner 실행은 사람이 직접 한다.** Claude는 테스트 파일을 작성·수정만 하고 `mcp__mcp-unity__run_tests` 등 실행 도구를 호출하지 않는다. 테스트 통과를 다음 Task의 게이트로 요구하지 않고, 작성 후 그대로 진행한다. (CLAUDE.md)
- **더티 플래그는 `ChunkData` 메서드로만 세팅한다.** `data.MarkDirty()` / `data.MarkRenderDirty()`. 플래그 3개를 직접 나열하지 않는다. (CLAUDE.md §3) — 이 계획에서는 기존 `TerrainModifier.Dig`의 `data.MarkDirty()` 호출을 그대로 두므로 새로 추가할 일은 없다.
- **알파 임계값은 10.** `TerrainCarver.ALPHA_THRESHOLD`와 같은 값을 쓴다. 마스크 픽셀의 알파가 이 값 **초과**면 불투명(= 파임)으로 본다.
- **기본 동작 불변.** 마스크 미설정이 기본값이고, 그 상태에서 파기 결과가 지금과 픽셀 단위로 동일해야 한다.
- 신규 스크립트 파일 첫 줄에 `// @tags: ...` 주석을 단다 (프로젝트 검색 관례).

**설계 문서:** `docs/superpowers/specs/2026-08-06-shovel-dig-mask-design.md`

---

## File Structure

| 파일 | 책임 |
|---|---|
| **Create** `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs` | 마스크 상태 보관(static), Texture2D → `bool[]` 베이크, 실패 사유 로그+중복 억제, 좌표 샘플링, 순회 반경 계산. 이 기능의 모든 판단이 여기 모인다 |
| **Create** `Assets/Tests/EditMode/ShovelDigMaskTests.cs` | 위 클래스의 EditMode 테스트 |
| **Modify** `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs` | `ProcessDigPixels`에 마스크 분기 추가, `Dig`의 순회 범위(margin) 분기 |
| **Modify** `Assets/Scripts/UI/Player/PlayerMining.cs` | 인스펙터 필드 2개 + `Start`/`OnValidate` 배선 |
| **Modify** `Assets/Scripts/UI/Player/Strategies/SapStrategy.cs` | 기존 `LogDigs` 진단 줄에 `shape=` 항목 1개 추가 |

`ShovelDigMask`를 별도 파일로 뺀 이유: 설계 문서는 static을 `TerrainModifier`에 두는 것으로 적었으나, `TerrainModifier`는 이미 780줄에 파기·섬 제거·침식·돌기 제거를 다 갖고 있다. 마스크 상태와 베이크 로직을 거기 더하면 순수 계산만 따로 테스트할 수 없다. 분리하면 `TerrainModifier`의 변경이 분기 2곳으로 끝나고, 마스크 수학 전체가 `ChunkData` 없이 단위 테스트된다.

---

## Task 1: `ShovelDigMask` 코어 — 샘플링과 순회 반경

마스크 **수학**만 만든다. Texture2D 베이크(Task 2)는 아직 없고, 테스트가 `bool[]`을 직접 주입한다.

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs`
- Test: `Assets/Tests/EditMode/ShovelDigMaskTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `static bool ShovelDigMask.IsActive { get; }`
  - `static void ShovelDigMask.SetBits(bool[] bits, int w, int h, float scale)` — 테스트·Task 2가 쓰는 주입 지점
  - `static void ShovelDigMask.Clear()`
  - `static float ShovelDigMask.BoundsRadiusPx(float radiusPx)`
  - `static bool ShovelDigMask.TryGetSampler(float radiusPx, out ShovelDigMask.Sampler sampler)`
  - `struct ShovelDigMask.Sampler` with `bool Contains(float localX, float localY)`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/ShovelDigMaskTests.cs`:

```csharp
// @tags: test, shovel, dig, mask, terrain
using NUnit.Framework;
using UnityEngine;

public class ShovelDigMaskTests
{
    // static 상태라 테스트 간 누수가 생긴다. 매번 비우고 시작한다.
    [SetUp]
    public void SetUp() => ShovelDigMask.Clear();

    [TearDown]
    public void TearDown() => ShovelDigMask.Clear();

    /// <summary>w×h 전부 불투명한 마스크</summary>
    private static bool[] FullBits(int w, int h)
    {
        var bits = new bool[w * h];
        for (int i = 0; i < bits.Length; i++) bits[i] = true;
        return bits;
    }

    [Test]
    public void Clear_상태_비활성()
    {
        ShovelDigMask.SetBits(FullBits(4, 4), 4, 4, 1f);
        Assert.IsTrue(ShovelDigMask.IsActive);

        ShovelDigMask.Clear();
        Assert.IsFalse(ShovelDigMask.IsActive);
    }

    [Test]
    public void 비활성이면_샘플러를_못_얻는다()
    {
        Assert.IsFalse(ShovelDigMask.TryGetSampler(50f, out _));
    }

    [Test]
    public void 꽉찬_정사각마스크_배율1_가로폭이_파기지름과_같다()
    {
        // 마스크 100x100, radiusPx=50 → 파기 지름 100px → 마스크 1px = 지형 1px
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, 1f);
        Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var s));

        // 중심에서 좌우 ±49.5px 는 안쪽, ±50.5px 는 바깥
        Assert.IsTrue(s.Contains(0f, 0f),      "중심");
        Assert.IsTrue(s.Contains(49.5f, 0f),   "우측 경계 안쪽");
        Assert.IsTrue(s.Contains(-49.5f, 0f),  "좌측 경계 안쪽");
        Assert.IsFalse(s.Contains(50.5f, 0f),  "우측 경계 바깥");
        Assert.IsFalse(s.Contains(-50.5f, 0f), "좌측 경계 바깥");
        Assert.IsFalse(s.Contains(0f, 50.5f),  "위쪽 경계 바깥");
        Assert.IsFalse(s.Contains(0f, -50.5f), "아래쪽 경계 바깥");
    }

    [Test]
    public void 배율2면_파이는_영역이_2배()
    {
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, 2f);
        Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var s));

        // 배율 1이면 바깥이던 지점(±50.5)이 배율 2에서는 안쪽
        Assert.IsTrue(s.Contains(99f, 0f),    "배율 2 → ±100px 까지 안쪽");
        Assert.IsFalse(s.Contains(101f, 0f),  "배율 2 → ±100px 넘으면 바깥");
    }

    [Test]
    public void 차징반경이_커지면_영역도_비례해_커진다()
    {
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, 1f);

        Assert.IsTrue(ShovelDigMask.TryGetSampler(25f, out var half));
        Assert.IsFalse(half.Contains(30f, 0f), "radiusPx 25 → 반폭 25px");

        Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var full));
        Assert.IsTrue(full.Contains(30f, 0f), "radiusPx 50 → 반폭 50px");
    }

    [Test]
    public void 세로로_긴_마스크는_세로로_길게_판다()
    {
        // 가로 20, 세로 100. 가로폭이 지름 기준이므로 radiusPx=10 → 마스크 1px = 지형 1px
        ShovelDigMask.SetBits(FullBits(20, 100), 20, 100, 1f);
        Assert.IsTrue(ShovelDigMask.TryGetSampler(10f, out var s));

        Assert.IsFalse(s.Contains(15f, 0f), "가로는 ±10px 까지만");
        Assert.IsTrue(s.Contains(0f, 45f),  "세로는 ±50px 까지");
        Assert.IsFalse(s.Contains(0f, 55f), "세로 ±50px 바깥");
    }

    [Test]
    public void 마스크_구멍은_안_파인다()
    {
        // 10x10 중 좌하단 5x5 만 불투명
        var bits = new bool[100];
        for (int v = 0; v < 5; v++)
            for (int u = 0; u < 5; u++)
                bits[v * 10 + u] = true;

        ShovelDigMask.SetBits(bits, 10, 10, 1f);
        Assert.IsTrue(ShovelDigMask.TryGetSampler(5f, out var s));

        // 마스크 중심이 (5,5). 좌하단 사분면(localX<0, localY<0)만 파여야 한다.
        Assert.IsTrue(s.Contains(-2f, -2f),  "좌하단 = 불투명");
        Assert.IsFalse(s.Contains(2f, 2f),   "우상단 = 투명");
        Assert.IsFalse(s.Contains(2f, -2f),  "우하단 = 투명");
        Assert.IsFalse(s.Contains(-2f, 2f),  "좌상단 = 투명");
    }

    [Test]
    public void 음수좌표가_0번_인덱스로_말려들어가지_않는다()
    {
        // (int) 캐스팅은 -0.5 를 0 으로 만든다. FloorToInt 를 써야 왼쪽 바깥이 바깥으로 판정된다.
        // 좌하단 1px 만 불투명한 마스크로 이 실수를 잡는다.
        var bits = new bool[100];
        bits[0] = true; // (u=0, v=0)

        ShovelDigMask.SetBits(bits, 10, 10, 1f);
        Assert.IsTrue(ShovelDigMask.TryGetSampler(5f, out var s));

        // 마스크 왼쪽 바깥(u = -0.x) — (int) 캐스팅이면 u=0 이 되어 true 로 새어나온다
        Assert.IsFalse(s.Contains(-5.5f, -5.5f), "왼쪽·아래 바깥은 바깥이어야 한다");
        Assert.IsTrue(s.Contains(-4.5f, -4.5f),  "좌하단 첫 픽셀은 안쪽");
    }

    [Test]
    public void 순회반경은_회전_외접반경()
    {
        // 100x100 마스크, radiusPx=50 → 마스크 1px = 지형 1px
        // 회전해도 안 잘리려면 반대각선 sqrt(100²+100²)/2 = 70.71 이상
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, 1f);
        Assert.AreEqual(70.71f, ShovelDigMask.BoundsRadiusPx(50f), 0.05f);
    }

    [Test]
    public void 순회반경은_세로로_긴_마스크를_안_자른다()
    {
        // 20x100, radiusPx=10 → 마스크 1px = 지형 1px. 반대각선 sqrt(20²+100²)/2 = 50.99
        ShovelDigMask.SetBits(FullBits(20, 100), 20, 100, 1f);
        Assert.AreEqual(50.99f, ShovelDigMask.BoundsRadiusPx(10f), 0.05f);
    }

    [Test]
    public void 순회반경은_배율에_비례한다()
    {
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, 2f);
        Assert.AreEqual(141.42f, ShovelDigMask.BoundsRadiusPx(50f), 0.05f);
    }

    [Test]
    public void 반경0이면_샘플러를_못_얻는다()
    {
        // radiusPx 0 은 0 나눗셈이 된다. 폴백으로 떨어뜨린다.
        ShovelDigMask.SetBits(FullBits(10, 10), 10, 10, 1f);
        Assert.IsFalse(ShovelDigMask.TryGetSampler(0f, out _));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Unity Test Runner (EditMode) 에서 `ShovelDigMaskTests` 실행 — **사람이 직접**.
예상: `ShovelDigMask` 타입이 없어 컴파일 에러.

이 확인을 다음 단계의 게이트로 삼지 않는다. 바로 Step 3으로 진행한다.

- [ ] **Step 3: `ShovelDigMask` 구현**

`Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs` 생성:

```csharp
// @tags: shovel, dig, mask, terrain, shape, static
using UnityEngine;

/// <summary>
/// 삽(toolIndex=1)이 파는 구멍 모양을 이미지 마스크로 정의한다.
///
/// static인 이유: 마스크가 필요한 지점은 <see cref="TerrainModifier.ProcessDigPixels"/>인데,
/// 거기까지 가려면 SapStrategy → TerrainChunk.Dig → TerrainModifier.Dig 2단계의 시그니처를
/// 뚫어야 한다. TerrainChunk.Dig는 드릴·곡괭이·StaticChunkTerrainManager 등 여러 곳이 부른다.
/// s_colliderUpdateInterval·s_maxIslandSize와 같은 패턴이다(CLAUDE.md §4).
/// 마스크는 전역 1개이므로 청크마다 사본을 가질 이유도 없다.
///
/// 마스크가 없거나 유효하지 않으면 <see cref="IsActive"/>가 false가 되고,
/// TerrainModifier는 기존 회전 타원 판정으로 그대로 떨어진다.
/// </summary>
public static class ShovelDigMask
{
    // 알파가 이 값을 '초과'하면 불투명. TerrainCarver.ALPHA_THRESHOLD와 같은 값이어야 한다.
    public const byte AlphaThreshold = 10;

    private static bool[] s_bits;   // 길이 = w*h. true = 파인다. null = 비활성
    private static int s_width;
    private static int s_height;
    private static float s_scale = 1f;

    /// <summary>마스크 경로가 살아 있는가. false면 호출측은 기존 타원을 쓴다.</summary>
    public static bool IsActive => s_bits != null;

    /// <summary>베이크된 비트맵을 직접 심는다. Texture2D 경로는 Set()이 이걸 부른다.</summary>
    public static void SetBits(bool[] bits, int w, int h, float scale)
    {
        if (bits == null || w <= 0 || h <= 0 || bits.Length != w * h || scale <= 0f)
        {
            Clear();
            return;
        }

        s_bits = bits;
        s_width = w;
        s_height = h;
        s_scale = scale;
    }

    /// <summary>마스크를 끈다. 이후 파기는 기존 타원 모양으로 돌아간다.</summary>
    public static void Clear()
    {
        s_bits = null;
        s_width = 0;
        s_height = 0;
        s_scale = 1f;
    }

    /// <summary>
    /// 마스크 픽셀 1개가 지형 픽셀 몇 개에 해당하는가.
    /// 기준: 마스크 가로폭 = 파기 지름(radiusPx * 2). 배율 1에 꽉 찬 원을 넣으면 기존 원형과 같은 크기다.
    /// </summary>
    private static float PixelsPerMaskPixel(float radiusPx)
        => (radiusPx * 2f / s_width) * s_scale;

    /// <summary>
    /// 픽셀 순회 범위로 쓸 반경(지형 픽셀). 마스크가 어느 각도로 회전해도 안 잘리도록
    /// 마스크 사각형의 외접원(반대각선) 크기를 준다.
    /// 넉넉해도 실제 제거는 Sampler.Contains가 결정하므로 결과가 틀리지 않는다. 모자라면 잘린다.
    /// </summary>
    public static float BoundsRadiusPx(float radiusPx)
    {
        if (!IsActive) return 0f;

        float pxPerMaskPx = PixelsPerMaskPixel(radiusPx);
        float halfDiagonal = Mathf.Sqrt(s_width * (float)s_width + s_height * (float)s_height) * 0.5f;
        return pxPerMaskPx * halfDiagonal;
    }

    /// <summary>
    /// 픽셀 루프 밖에서 1회 얻어 루프 안에서 재사용하는 샘플러.
    /// 나눗셈과 null 체크를 루프 밖으로 빼기 위한 것 — struct라 할당이 없다.
    /// </summary>
    public struct Sampler
    {
        internal bool[] Bits;
        internal int Width;
        internal int Height;
        internal float Inv;        // 1 / pxPerMaskPx

        /// <summary>
        /// 파기 중심 기준 회전 좌표(localX = 파는 방향, localY = 그 왼쪽)가 마스크 불투명 영역 안인가.
        /// 마스크 이미지는 삽이 +X(오른쪽)를 향하는 그림으로 그린다.
        /// </summary>
        public bool Contains(float localX, float localY)
        {
            // FloorToInt 필수. (int) 캐스팅은 0 방향으로 자르므로 -0.5 가 0 이 되어
            // 마스크 왼쪽·아래 바깥이 0번 열/행으로 말려들어간다.
            int u = Mathf.FloorToInt(localX * Inv + Width * 0.5f);
            int v = Mathf.FloorToInt(localY * Inv + Height * 0.5f);

            if (u < 0 || u >= Width || v < 0 || v >= Height) return false;
            return Bits[v * Width + u];
        }
    }

    /// <summary>
    /// 이번 파기에 쓸 샘플러를 얻는다. 마스크가 비활성이거나 반경이 0 이하면 false —
    /// 호출측은 기존 타원 판정으로 떨어져야 한다.
    /// </summary>
    public static bool TryGetSampler(float radiusPx, out Sampler sampler)
    {
        sampler = default;
        if (!IsActive) return false;

        float pxPerMaskPx = PixelsPerMaskPixel(radiusPx);
        if (pxPerMaskPx <= 0f) return false;   // radiusPx 0 = 0 나눗셈 방지

        sampler.Bits = s_bits;
        sampler.Width = s_width;
        sampler.Height = s_height;
        sampler.Inv = 1f / pxPerMaskPx;
        return true;
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인**

Unity Test Runner (EditMode) 에서 `ShovelDigMaskTests` 실행 — **사람이 직접**. 예상: 12개 전부 PASS.

- [ ] **Step 5: 체크인**

UVCS에서 `ShovelDigMask.cs` + `ShovelDigMaskTests.cs`를 체크인한다 — **사람이 직접**.
설명 예시: `feat: 삽 파기 마스크 좌표 샘플링 코어 추가`

---

## Task 2: Texture2D 베이크와 실패 사유 로그

인스펙터에서 받은 `Texture2D`를 `bool[]`로 굽고, 실패하면 **왜** 실패했는지 로그를 남긴다.

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs`
- Test: `Assets/Tests/EditMode/ShovelDigMaskTests.cs` (테스트 추가)

**Interfaces:**
- Consumes: Task 1의 `SetBits`, `Clear`, `IsActive`
- Produces:
  - `static void ShovelDigMask.Set(Texture2D tex, float scale)`
  - `static int ShovelDigMask.LogEmitCount { get; }` — 중복 억제 검증용

- [ ] **Step 1: 실패하는 테스트 작성**

`ShovelDigMaskTests.cs` **끝(마지막 `}` 직전)** 에 아래를 추가한다. Task 1의 `SetUp`/`TearDown`/`FullBits`가 그대로 적용된다.

```csharp
    // ==========================================================================
    //  Set(Texture2D, float) — 베이크와 실패 폴백
    // ==========================================================================

    /// <summary>런타임 생성 텍스처는 isReadable=true다. alpha 값으로 채운다.</summary>
    private static Texture2D MakeTex(int w, int h, byte alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, alpha);
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    [Test]
    public void Set_정상텍스처_활성화()
    {
        var tex = MakeTex(64, 64, 255);
        try
        {
            ShovelDigMask.Set(tex, 1f);
            Assert.IsTrue(ShovelDigMask.IsActive);

            // 64x64 꽉 찬 마스크, radiusPx=32 → 마스크 1px = 지형 1px
            Assert.IsTrue(ShovelDigMask.TryGetSampler(32f, out var s));
            Assert.IsTrue(s.Contains(0f, 0f));
            Assert.IsFalse(s.Contains(40f, 0f));
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_null이면_비활성()
    {
        ShovelDigMask.SetBits(FullBits(4, 4), 4, 4, 1f);
        ShovelDigMask.Set(null, 1f);
        Assert.IsFalse(ShovelDigMask.IsActive, "마스크를 비우면 타원으로 돌아가야 한다");
    }

    [Test]
    public void Set_배율0이면_비활성()
    {
        var tex = MakeTex(16, 16, 255);
        try
        {
            LogAssert.Expect(LogType.Warning, new Regex("ShovelDigMask.*배율"));
            ShovelDigMask.Set(tex, 0f);
            Assert.IsFalse(ShovelDigMask.IsActive);
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_배율음수면_비활성()
    {
        var tex = MakeTex(16, 16, 255);
        try
        {
            LogAssert.Expect(LogType.Warning, new Regex("ShovelDigMask.*배율"));
            ShovelDigMask.Set(tex, -1f);
            Assert.IsFalse(ShovelDigMask.IsActive);
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_전부투명하면_비활성()
    {
        var tex = MakeTex(16, 16, 0);
        try
        {
            LogAssert.Expect(LogType.Warning, new Regex("ShovelDigMask.*알파"));
            ShovelDigMask.Set(tex, 1f);
            Assert.IsFalse(ShovelDigMask.IsActive, "다 투명한 이미지 = 아무것도 안 파임 → 타원 폴백");
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_알파가_임계값_이하면_투명취급()
    {
        // AlphaThreshold=10. 알파 10은 '초과'가 아니므로 투명이다.
        var tex = MakeTex(16, 16, ShovelDigMask.AlphaThreshold);
        try
        {
            LogAssert.Expect(LogType.Warning, new Regex("ShovelDigMask.*알파"));
            ShovelDigMask.Set(tex, 1f);
            Assert.IsFalse(ShovelDigMask.IsActive);
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_알파가_임계값_초과면_불투명취급()
    {
        var tex = MakeTex(16, 16, (byte)(ShovelDigMask.AlphaThreshold + 1));
        try
        {
            ShovelDigMask.Set(tex, 1f);
            Assert.IsTrue(ShovelDigMask.IsActive);
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_같은조합_반복호출시_로그가_한번만()
    {
        // OnValidate는 인스펙터를 만질 때마다 불린다. 같은 (텍스처, 배율, 결과)면 로그를 건너뛴다.
        //
        // LogAssert로는 이걸 못 잡는다 — Unity는 예상 못 한 LogType.Log/Warning으로 테스트를
        // 실패시키지 않으므로 NoUnexpectedReceived()가 중복 Debug.Log를 통과시킨다.
        // 그래서 실제로 콘솔에 나간 횟수(LogEmitCount)를 센다.
        var tex = MakeTex(16, 16, 255);
        try
        {
            int before = ShovelDigMask.LogEmitCount;

            ShovelDigMask.Set(tex, 1f);
            Assert.AreEqual(before + 1, ShovelDigMask.LogEmitCount, "첫 적용은 로그 1줄");

            ShovelDigMask.Set(tex, 1f);
            ShovelDigMask.Set(tex, 1f);
            ShovelDigMask.Set(tex, 1f);
            Assert.AreEqual(before + 1, ShovelDigMask.LogEmitCount, "같은 조합은 더 안 찍힘");

            Assert.IsTrue(ShovelDigMask.IsActive);
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_조합이_바뀌면_로그가_다시_찍힌다()
    {
        var tex = MakeTex(16, 16, 255);
        try
        {
            ShovelDigMask.Set(tex, 1f);
            int after1 = ShovelDigMask.LogEmitCount;

            ShovelDigMask.Set(tex, 2f);   // 배율이 바뀜
            Assert.AreEqual(after1 + 1, ShovelDigMask.LogEmitCount, "배율이 바뀌면 다시 찍힘");

            LogAssert.Expect(LogType.Log, new Regex("ShovelDigMask.*미설정"));
            ShovelDigMask.Set(null, 2f);  // 마스크가 비워짐
            Assert.AreEqual(after1 + 2, ShovelDigMask.LogEmitCount, "마스크를 비우면 다시 찍힘");
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_배율이_바뀌면_다시_적용된다()
    {
        var tex = MakeTex(100, 100, 255);
        try
        {
            ShovelDigMask.Set(tex, 1f);
            Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var s1));
            Assert.IsFalse(s1.Contains(60f, 0f), "배율 1 → 반폭 50px");

            ShovelDigMask.Set(tex, 2f);
            Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var s2));
            Assert.IsTrue(s2.Contains(60f, 0f), "배율 2 → 반폭 100px");
        }
        finally { Object.DestroyImmediate(tex); }
    }
```

테스트 파일 맨 위 `using` 3줄을 아래로 교체한다 (`LogAssert`·`Regex` 사용):

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Unity Test Runner (EditMode) 실행 — **사람이 직접**. 예상: `ShovelDigMask.Set` 이 없어 컴파일 에러.
게이트로 삼지 않고 바로 Step 3으로 진행한다.

- [ ] **Step 3: `Set` 구현**

`ShovelDigMask.cs`의 `Clear()` 메서드 **바로 아래**에 추가한다:

```csharp
    // ============================================================================
    //  Texture2D 베이크
    //
    //  로그는 여기서만 찍는다. 파기 루프에서는 절대 찍지 않는다 —
    //  삽질 1회당 1줄이면 콘솔이 도배되고, 실패 사유는 전부 이 시점에 판정 가능하다.
    // ============================================================================

    // 로그 중복 억제용. OnValidate는 인스펙터를 만질 때마다 불리므로
    // 마지막으로 로그를 찍은 조합을 기억했다가 같으면 건너뛴다.
    private static int s_lastLoggedTexId;
    private static float s_lastLoggedScale;
    private static bool s_lastLoggedOk;
    private static bool s_hasLogged;

    /// <summary>
    /// 실제로 콘솔에 나간 로그 줄 수(누적). 중복 억제가 동작하는지 테스트에서 확인하는 용도다.
    /// Unity의 LogAssert는 예상 못 한 Log/Warning으로 테스트를 실패시키지 않아
    /// 중복 Debug.Log를 잡아내지 못한다.
    /// </summary>
    public static int LogEmitCount { get; private set; }

    /// <summary>
    /// 인스펙터의 마스크 텍스처를 굽는다. 실패하면 사유를 남기고 비활성(= 타원 폴백)으로 둔다.
    /// </summary>
    public static void Set(Texture2D tex, float scale)
    {
        int texId = tex != null ? tex.GetInstanceID() : 0;

        if (tex == null)
        {
            Clear();
            LogOnce(texId, scale, false, LogType.Log,
                "[ShovelDigMask] 마스크 미설정 → 기존 타원 모양 사용");
            return;
        }

        if (scale <= 0f)
        {
            Clear();
            LogOnce(texId, scale, false, LogType.Warning,
                $"[ShovelDigMask] '{tex.name}' 배율이 {scale} → 타원 폴백. " +
                "shovelDigMaskScale은 0보다 커야 함");
            return;
        }

        if (!tex.isReadable)
        {
            Clear();
            LogOnce(texId, scale, false, LogType.Error,
                $"[ShovelDigMask] '{tex.name}' Read/Write Enabled가 꺼져 있음 → 타원 폴백. " +
                "Import Settings에서 켤 것");
            return;
        }

        int w = tex.width;
        int h = tex.height;
        Color32[] pixels = tex.GetPixels32();

        var bits = new bool[w * h];
        int opaque = 0;
        for (int i = 0; i < bits.Length; i++)
        {
            bool on = pixels[i].a > AlphaThreshold;
            bits[i] = on;
            if (on) opaque++;
        }

        if (opaque == 0)
        {
            Clear();
            LogOnce(texId, scale, false, LogType.Warning,
                $"[ShovelDigMask] '{tex.name}' {w}x{h}에 알파>{AlphaThreshold} 픽셀이 " +
                "하나도 없음 → 타원 폴백");
            return;
        }

        SetBits(bits, w, h, scale);

        // 성공도 남긴다. 이게 없으면 "로그가 안 뜨는 것"과 "정상"을 구분할 수 없다.
        float percent = opaque * 100f / bits.Length;
        LogOnce(texId, scale, true, LogType.Log,
            $"[ShovelDigMask] '{tex.name}' {w}x{h} 적용. " +
            $"불투명 {opaque}px ({percent:F1}%), 배율 {scale}");
    }

    /// <summary>
    /// (텍스처, 배율, 성공여부) 조합이 직전과 같으면 건너뛴다.
    /// 조합이 바뀌면(이미지 교체·배율 변경·Read/Write를 켬) 다시 찍힌다.
    /// </summary>
    private static void LogOnce(int texId, float scale, bool ok, LogType level, string message)
    {
        if (s_hasLogged &&
            s_lastLoggedTexId == texId &&
            s_lastLoggedScale == scale &&
            s_lastLoggedOk == ok)
        {
            return;
        }

        s_hasLogged = true;
        s_lastLoggedTexId = texId;
        s_lastLoggedScale = scale;
        s_lastLoggedOk = ok;
        LogEmitCount++;

        switch (level)
        {
            case LogType.Error:   Debug.LogError(message);   break;
            case LogType.Warning: Debug.LogWarning(message); break;
            default:              Debug.Log(message);        break;
        }
    }
```

`Clear()`는 로그 억제 상태를 건드리지 않는다 — `Set`이 실패해서 `Clear()`를 부른 직후
같은 `Set`이 `LogOnce`를 호출해야 하기 때문이다. 다만 테스트가 `[SetUp]`에서 `Clear()`를
부르므로, 억제 상태도 같이 비워야 테스트 간 간섭이 없다. `Clear()` 본문 끝에 아래 3줄을 더한다:

```csharp
        // 테스트·씬 전환에서 로그 억제 상태가 남아 첫 로그가 삼켜지는 것을 막는다
        s_hasLogged = false;
        s_lastLoggedTexId = 0;
        s_lastLoggedOk = false;
```

⚠ 이 때문에 `Set` 안에서 `Clear()`를 부른 뒤 `LogOnce`를 부르는 순서가 **중요하다**.
순서를 뒤집으면(로그 먼저, Clear 나중) 억제 상태가 지워져 매번 로그가 찍힌다.
현재 코드는 `Clear()` → `LogOnce()` 순이라 맞다.

- [ ] **Step 4: 테스트가 통과하는지 확인**

Unity Test Runner (EditMode) 실행 — **사람이 직접**. 예상: Task 1의 12개 + 이번 10개 = 22개 PASS.

- [ ] **Step 5: 체크인**

UVCS 체크인 — **사람이 직접**. 설명 예시: `feat: 삽 마스크 Texture2D 베이크 + 실패 사유 로그`

---

## Task 3: `TerrainModifier` 분기 연결

기존 타원 코드를 **그대로 두고** 마스크 분기를 옆에 추가한다.

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs:173-196` (`Dig`의 bounds 계산)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs:316-389` (`ProcessDigPixels`)

**Interfaces:**
- Consumes: Task 1의 `ShovelDigMask.IsActive`, `BoundsRadiusPx`, `TryGetSampler`, `Sampler.Contains`
- Produces: 없음 (기존 시그니처 유지 — `TerrainChunk.Dig`·호출부 변경 없음)

- [ ] **Step 1: 삽 도구 상수와 돌 반경 비율 상수 추가**

`TerrainModifier.cs`의 상수 블록(18~24행 부근, `ROCK_DIG_THRESHOLD` 아래)에 2줄 추가:

```csharp
    private const int SHOVEL_TOOL_INDEX = 1;             // 삽. MiningStaminaTuning.Shovel과 같은 값
    // ROCK_DIG_THRESHOLD(0.04)는 '거리 제곱 / 반경 제곱' 비율이다.
    // 마스크 경로에는 sqrRadius가 없으므로 거리 비율로 환산해 쓴다. sqrt(0.04) = 0.2
    private const float ROCK_DIG_RADIUS_RATIO = 0.2f;
```

- [ ] **Step 2: `Dig`의 순회 범위(margin)를 분기**

`TerrainModifier.cs` 176~180행. 아래 기존 코드를

```csharp
        // Calculate bounds
        float frontScale = Mathf.Max(1f, VerticalScale);
        int maxRadiusPx = Mathf.CeilToInt(radiusPx * frontScale);
        int margin = maxRadiusPx + DIG_MARGIN_EXTRA;
```

이렇게 교체한다:

```csharp
        // Calculate bounds
        // 마스크는 세로로 길거나 회전하면 타원 기준 margin을 벗어나 잘린다.
        // 마스크 경로에서는 회전 외접원 반경을 쓴다. 넉넉해도 실제 제거는 마스크 판정이 정하므로
        // 결과가 틀리지 않는다 — 모자라면 잘린다.
        bool useShovelMask = (toolIndex == SHOVEL_TOOL_INDEX) && ShovelDigMask.IsActive;
        int maxRadiusPx = useShovelMask
            ? Mathf.CeilToInt(ShovelDigMask.BoundsRadiusPx(radiusPx))
            : Mathf.CeilToInt(radiusPx * Mathf.Max(1f, VerticalScale));
        int margin = maxRadiusPx + DIG_MARGIN_EXTRA;
```

- [ ] **Step 3: `ProcessDigPixels`에 마스크 분기 추가**

`TerrainModifier.cs` 316~389행의 `ProcessDigPixels` 전체를 아래로 교체한다.
**타원 계산 부분은 원본 그대로**이고, 그 앞뒤로 마스크 분기만 감쌌다.

```csharp
    private bool ProcessDigPixels(ChunkData data, int minX, int minY, int maxX, int maxY,
                                   int centerPx, int centerPy, float radiusPx, float angle, int toolIndex,
                                   ParticleSpawnCallback particleCallback, PixelToWorldCallback pixelToWorld)
    {
        float cos = Mathf.Cos(-angle);
        float sin = Mathf.Sin(-angle);
        float sqrRadius = radiusPx * radiusPx;

        // 마스크 분기. 샘플러를 못 얻으면(마스크 없음·반경 0) 아래 타원 판정으로 그대로 떨어진다.
        bool useMask = (toolIndex == SHOVEL_TOOL_INDEX)
                       && ShovelDigMask.TryGetSampler(radiusPx, out var maskSampler);
        // 돌(pixelType 2)은 마스크 경로에서도 중심 근처에서만 파인다 — 타원 경로의 규칙과 같다.
        float rockLimitPx = radiusPx * ROCK_DIG_RADIUS_RATIO;
        float sqrRockLimit = rockLimitPx * rockLimitPx;

        bool pixelChanged = false;

        for (int y = minY; y < maxY; y++)
        {
            float dy = y - centerPy;

            for (int x = minX; x < maxX; x++)
            {
                int index = data.ToIndex(x, y);

                // Skip if already air
                if (data.BasePixels[index].a == 0) continue;

                // 파기 불가 픽셀 스킵 (IndestructibleOverlay)
                if (data.IndestructibleMask.IsCreated && data.IndestructibleMask[index] != 0) continue;

                // indestructible 인접 픽셀 보호 (1픽셀 버퍼)
                if (data.HasIndestructiblePixels && IsAdjacentToIndestructible(data, x, y)) continue;

                // Transform to local dig space (rotated ellipse)
                // CenterPx is ALREADY the geometric center of the dig hole!
                float dx = x - centerPx;

                // Rotation only. Translation is already done.
                float localX = dx * cos - dy * sin;
                float localY = dx * sin + dy * cos;

                byte pixelType = data.PixelInfo[index]; // 1=Dirt, 2=Rock

                if (useMask)
                {
                    // --- 마스크 판정 ---
                    // VerticalScale(전방 늘림)은 걸지 않는다. 모양은 이미지가 정의한다.
                    if (!maskSampler.Contains(localX, localY)) continue;

                    // Shovel can't dig rock efficiently
                    if (pixelType == 2)
                    {
                        float distSqrFromCenter = (localX * localX) + (localY * localY);
                        if (distSqrFromCenter > sqrRockLimit) continue;
                    }
                }
                else
                {
                    // --- 기존 타원 판정 (원본 그대로) ---
                    // Apply vertical scaling (ellipse)
                    float currentScale = (localX >= 0) ? VerticalScale : 1.0f;
                    float localXScaled = localX / currentScale;
                    float distSqr = (localXScaled * localXScaled) + (localY * localY);

                    if (distSqr > sqrRadius) continue;

                    // Shovel can't dig rock efficiently
                    if (pixelType == 2 && toolIndex == SHOVEL_TOOL_INDEX)
                    {
                        // Only dig at center (very small area)
                        if (distSqr > sqrRadius * ROCK_DIG_THRESHOLD) continue;
                    }
                }

                // Save debris color for particle
                Color32 debrisColor = data.BasePixels[index];

                // Remove pixel
                data.BasePixels[index] = new Color32(0, 0, 0, 0);
                data.PixelInfo[index] = 0;
                pixelChanged = true;

                // Spawn debris particle via callback + fire destroy event
                if (pixelToWorld != null)
                {
                    Vector2 worldPos = pixelToWorld(x, y);
                    particleCallback?.Invoke(worldPos, debrisColor);
                    OnPixelDestroyed?.Invoke(worldPos, debrisColor);
                }
            }
        }

        return pixelChanged;
    }
```

⚠ 원본과 달라진 점 두 가지를 확인할 것:
1. 타원 판정이 `if (distSqr <= sqrRadius) { ... }` 감싸기에서 `if (distSqr > sqrRadius) continue;` 로 뒤집혔다. 픽셀 제거 코드를 두 분기가 공유하기 위한 것으로, 동작은 같다.
2. `byte pixelType`을 읽는 위치가 분기 앞으로 올라왔다. 값은 같다.

- [ ] **Step 4: 컴파일과 회귀 확인**

**사람이 직접:**
1. Unity 콘솔에 컴파일 에러가 없는지 확인
2. Unity Test Runner EditMode 전체 실행 — 기존 테스트가 전부 그대로 PASS 하는지 (특히 `TerrainJobsRectScopeTests`, `ChamferConvergenceTests`)
3. **마스크를 넣지 않은 상태로** 플레이 → 삽으로 파기. 구멍 모양이 지금과 똑같아야 한다. 곡괭이·드릴도 확인

- [ ] **Step 5: 체크인**

UVCS 체크인 — **사람이 직접**. 설명 예시: `feat: TerrainModifier에 삽 마스크 분기 추가 (타원 폴백 유지)`

---

## Task 4: 인스펙터 배선과 진단 로그

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerMining.cs:16-23` (필드 추가), `PlayerMining.cs:142-155` (`Start`)
- Modify: `Assets/Scripts/UI/Player/Strategies/SapStrategy.cs:254-259` (`LogDigs` 진단 줄)

**Interfaces:**
- Consumes: Task 2의 `ShovelDigMask.Set(Texture2D, float)`, Task 1의 `ShovelDigMask.IsActive`
- Produces: `PlayerMining.shovelDigMask`, `PlayerMining.shovelDigMaskScale` (인스펙터 노출)

- [ ] **Step 1: `PlayerMining`에 인스펙터 필드 추가**

`PlayerMining.cs` 23행 `public int wallClimbToolIndex = 0;` **바로 아래**에 추가:

```csharp

    [Header("삽 파기 모양 (비우면 기존 타원)")]
    [Tooltip("삽이 파는 구멍 모양 이미지. Import Settings에서 Read/Write Enabled 필수.\n" +
             "삽이 +X(오른쪽)를 향하는 그림으로 그릴 것 — 마우스 방향으로 회전한다.\n" +
             "알파 10 초과 픽셀이 파인다. 비우면 기존 회전 타원으로 판다.")]
    public Texture2D shovelDigMask;

    [Tooltip("마스크 크기 배율. 1 = 마스크 가로폭이 파기 지름과 같다(꽉 찬 원을 넣으면 기존 원형과 동일).")]
    public float shovelDigMaskScale = 1f;
```

- [ ] **Step 2: `Start`에서 마스크 적용 + `OnValidate` 추가**

`PlayerMining.cs`의 `Start()` 안, 154행 `_emptyStrategy = new EmptyStrategy();` **바로 아래**에 추가:

```csharp

        // 삽 파기 모양 마스크. 실패 사유는 ShovelDigMask.Set이 콘솔에 남긴다.
        ShovelDigMask.Set(shovelDigMask, shovelDigMaskScale);
```

그리고 `Start()` 메서드가 끝나는 `}` **바로 뒤**에 새 메서드를 추가:

```csharp

    /// <summary>
    /// 인스펙터에서 마스크·배율을 만지면 즉시 반영한다. 이미지 모양을 눈으로 맞춰보는 게
    /// 이 기능의 주 용도라, 플레이를 껐다 켜야 반영되면 쓸모가 없다.
    /// 로그 도배는 ShovelDigMask.Set이 (텍스처, 배율, 결과) 조합으로 억제한다.
    /// </summary>
    private void OnValidate()
    {
        ShovelDigMask.Set(shovelDigMask, shovelDigMaskScale);
    }
```

`PlayerMining`에는 현재 `OnValidate`가 없다(확인함). 새로 만드는 게 맞다.

- [ ] **Step 3: `SapStrategy`의 진단 줄에 `shape=` 추가**

`SapStrategy.cs` 254~259행. 아래 기존 블록을

```csharp
        if (MiningStaminaTuning.LogDigs)
        {
            Debug.Log($"[SapDig] f{Time.frameCount} charging={_isCharging} attacking={_isAttacking} " +
                      $"chargeTimer={_currentChargeTimer:F3} ratio={GetChargeRatio():F3} " +
                      $"canDig={p.CanDig} tool={p.ToolIndex} radius={(p.CanDig ? p.RadiusMultiplier : 0f):F4}");
        }
```

이렇게 교체한다 (`shape=` 항목 하나만 추가):

```csharp
        if (MiningStaminaTuning.LogDigs)
        {
            // shape: 마스크가 실제로 먹었는지. "삽인데 왜 이미지 모양이 안 나오지"를 여기서 본다.
            // 기본 off라 평상시엔 안 찍힌다. 실패 사유 자체는 ShovelDigMask.Set이 세팅 시점에 남긴다.
            string shape = (p.ToolIndex == MiningStaminaTuning.Shovel && ShovelDigMask.IsActive)
                ? "mask" : "ellipse";
            Debug.Log($"[SapDig] f{Time.frameCount} charging={_isCharging} attacking={_isAttacking} " +
                      $"chargeTimer={_currentChargeTimer:F3} ratio={GetChargeRatio():F3} " +
                      $"canDig={p.CanDig} tool={p.ToolIndex} shape={shape} " +
                      $"radius={(p.CanDig ? p.RadiusMultiplier : 0f):F4}");
        }
```

- [ ] **Step 4: 수동 확인**

**사람이 직접**, 플레이어 프리팹의 `PlayerMining`에서:

| 확인 | 기대 |
|---|---|
| 마스크 비움 → 삽질 | 지금과 똑같은 타원 구멍 + 콘솔 `마스크 미설정 → 기존 타원` |
| Read/Write 꺼진 PNG 지정 | `LogError`에 이미지 이름 + `Import Settings에서 켤 것` / 타원으로 파짐 |
| `shovelDigMaskScale`에 0 입력 | `LogWarning` 배율 사유 / 타원 |
| 전부 투명한 PNG 지정 | `LogWarning` 알파 사유 / 타원 |
| 정상 PNG 지정 | `Log` 1줄에 크기·불투명 px·%·배율 / 이미지 모양대로 파임 |
| 인스펙터에서 같은 값 반복 클릭 | 로그가 더 안 늘어남 |
| 플레이 중 배율 슬라이더 조절 | 로그 갱신 + 다음 삽질부터 크기 반영 |
| 마우스를 위/왼쪽/아래로 두고 삽질 | 마스크가 그 방향으로 회전 (+X가 파는 방향) |
| 청크 경계를 걸쳐 삽질 | 마스크가 잘리지 않고 이어짐 |
| 불괴 픽셀(오버레이) 위 삽질 | 보호됨 |
| F8 패널에서 `LogDigs` on → 삽질 | `[SapDig] ... shape=mask ...` |

- [ ] **Step 5: 체크인**

UVCS 체크인 — **사람이 직접**. 설명 예시: `feat: 삽 파기 마스크 인스펙터 배선 + SapDig 진단에 shape 추가`

---

## 완료 후

- 성능 최적화 작업이 아니므로 `Assets/Docs/performance/` 기록 대상은 아니다.
- 마스크와 `DigRangePreview`(차징 중 예고 이미지)는 **연결되지 않는다.** 테스트 시 같은 이미지를 `DigRangePreview.indicatorImage`에도 수동으로 꽂아야 예고와 실제가 맞는다. 자동 동기화가 필요해지면 별건으로 다룬다 — 설계 문서 §6 참조.
