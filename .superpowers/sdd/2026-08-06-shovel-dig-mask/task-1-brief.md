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

