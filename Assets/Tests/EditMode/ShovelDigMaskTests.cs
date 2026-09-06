// @tags: test, shovel, dig, mask, terrain
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

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
        ShovelDigMask.SetBits(FullBits(4, 4), 4, 4, Vector2.one);
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
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, Vector2.one);
        Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var s));

        // 중심에서 좌우 ±49.5px 는 안쪽, ±50.5px 는 바깥
        Assert.IsTrue(s.Contains(0f, 0f), "중심");
        Assert.IsTrue(s.Contains(49.5f, 0f), "우측 경계 안쪽");
        Assert.IsTrue(s.Contains(-49.5f, 0f), "좌측 경계 안쪽");
        Assert.IsFalse(s.Contains(50.5f, 0f), "우측 경계 바깥");
        Assert.IsFalse(s.Contains(-50.5f, 0f), "좌측 경계 바깥");
        Assert.IsFalse(s.Contains(0f, 50.5f), "위쪽 경계 바깥");
        Assert.IsFalse(s.Contains(0f, -50.5f), "아래쪽 경계 바깥");
    }

    [Test]
    public void 배율2면_파이는_영역이_2배()
    {
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, new Vector2(2f, 2f));
        Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var s));

        // 배율 1이면 바깥이던 지점(±50.5)이 배율 2에서는 안쪽
        Assert.IsTrue(s.Contains(99f, 0f), "배율 2 → ±100px 까지 안쪽");
        Assert.IsFalse(s.Contains(101f, 0f), "배율 2 → ±100px 넘으면 바깥");
    }

    [Test]
    public void 차징반경이_커지면_영역도_비례해_커진다()
    {
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, Vector2.one);

        Assert.IsTrue(ShovelDigMask.TryGetSampler(25f, out var half));
        Assert.IsFalse(half.Contains(30f, 0f), "radiusPx 25 → 반폭 25px");

        Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var full));
        Assert.IsTrue(full.Contains(30f, 0f), "radiusPx 50 → 반폭 50px");
    }

    [Test]
    public void 세로로_긴_마스크는_세로로_길게_판다()
    {
        // 가로 20, 세로 100. 가로폭이 지름 기준이므로 radiusPx=10 → 마스크 1px = 지형 1px
        ShovelDigMask.SetBits(FullBits(20, 100), 20, 100, Vector2.one);
        Assert.IsTrue(ShovelDigMask.TryGetSampler(10f, out var s));

        Assert.IsFalse(s.Contains(15f, 0f), "가로는 ±10px 까지만");
        Assert.IsTrue(s.Contains(0f, 45f), "세로는 ±50px 까지");
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

        ShovelDigMask.SetBits(bits, 10, 10, Vector2.one);
        Assert.IsTrue(ShovelDigMask.TryGetSampler(5f, out var s));

        // 마스크 중심이 (5,5). 좌하단 사분면(localX<0, localY<0)만 파여야 한다.
        Assert.IsTrue(s.Contains(-2f, -2f), "좌하단 = 불투명");
        Assert.IsFalse(s.Contains(2f, 2f), "우상단 = 투명");
        Assert.IsFalse(s.Contains(2f, -2f), "우하단 = 투명");
        Assert.IsFalse(s.Contains(-2f, 2f), "좌상단 = 투명");
    }

    [Test]
    public void 음수좌표가_0번_인덱스로_말려들어가지_않는다()
    {
        // (int) 캐스팅은 -0.5 를 0 으로 만든다. FloorToInt 를 써야 왼쪽 바깥이 바깥으로 판정된다.
        // 좌하단 1px 만 불투명한 마스크로 이 실수를 잡는다.
        var bits = new bool[100];
        bits[0] = true; // (u=0, v=0)

        ShovelDigMask.SetBits(bits, 10, 10, Vector2.one);
        Assert.IsTrue(ShovelDigMask.TryGetSampler(5f, out var s));

        // 마스크 왼쪽 바깥(u = -0.x) — (int) 캐스팅이면 u=0 이 되어 true 로 새어나온다
        Assert.IsFalse(s.Contains(-5.5f, -5.5f), "왼쪽·아래 바깥은 바깥이어야 한다");
        Assert.IsTrue(s.Contains(-4.5f, -4.5f), "좌하단 첫 픽셀은 안쪽");
    }

    [Test]
    public void 순회반경은_실측_최대반경()
    {
        // 100x100 전부 불투명, radiusPx=50 → 마스크 1px = 지형 1px
        // 실측 최대거리(모서리 픽셀 중심) = sqrt(49.5²+49.5²) = 70.0036
        // BoundsRadiusPx = 50 * (1*2*70.0036/100) = 70.0036
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, Vector2.one);
        Assert.AreEqual(70.0036f, ShovelDigMask.BoundsRadiusPx(50f), 0.05f);
    }

    [Test]
    public void 순회반경은_세로로_긴_마스크를_안_자른다()
    {
        // 20x100, radiusPx=10 → 마스크 1px = 지형 1px
        // 실측 최대거리 = sqrt(9.5²+49.5²) = 50.4034 → BoundsRadiusPx = 10*(1*2*50.4034/20) = 50.4034
        ShovelDigMask.SetBits(FullBits(20, 100), 20, 100, Vector2.one);
        Assert.AreEqual(50.4034f, ShovelDigMask.BoundsRadiusPx(10f), 0.05f);
    }

    [Test]
    public void 순회반경은_배율에_비례한다()
    {
        // 실측 최대거리 70.0036, scale 2 → BoundsRadiusPx = 50*(2*2*70.0036/100) = 140.0071
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, new Vector2(2f, 2f));
        Assert.AreEqual(140.0071f, ShovelDigMask.BoundsRadiusPx(50f), 0.05f);
    }

    [Test]
    public void 반경0이면_샘플러를_못_얻는다()
    {
        // radiusPx 0 은 0 나눗셈이 된다. 폴백으로 떨어뜨린다.
        ShovelDigMask.SetBits(FullBits(10, 10), 10, 10, Vector2.one);
        Assert.IsFalse(ShovelDigMask.TryGetSampler(0f, out _));
    }

    // ==========================================================================
    //  Set(Texture2D, Vector2) — 베이크와 실패 폴백
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
            ShovelDigMask.Set(tex, Vector2.one);
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
        ShovelDigMask.SetBits(FullBits(4, 4), 4, 4, Vector2.one);
        ShovelDigMask.Set(null, Vector2.one);
        Assert.IsFalse(ShovelDigMask.IsActive, "마스크를 비우면 타원으로 돌아가야 한다");
    }

    [Test]
    public void Set_배율0이면_비활성()
    {
        var tex = MakeTex(16, 16, 255);
        try
        {
            LogAssert.Expect(LogType.Warning, new Regex("ShovelDigMask.*배율"));
            ShovelDigMask.Set(tex, Vector2.zero);
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
            ShovelDigMask.Set(tex, new Vector2(-1f, -1f));
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
            ShovelDigMask.Set(tex, Vector2.one);
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
            ShovelDigMask.Set(tex, Vector2.one);
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
            ShovelDigMask.Set(tex, Vector2.one);
            Assert.IsTrue(ShovelDigMask.IsActive);
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_같은조합_반복호출시_로그가_한번만()
    {
        var tex = MakeTex(16, 16, 255);
        try
        {
            int before = ShovelDigMask.LogEmitCount;

            ShovelDigMask.Set(tex, Vector2.one);
            Assert.AreEqual(before + 1, ShovelDigMask.LogEmitCount, "첫 적용은 로그 1줄");

            ShovelDigMask.Set(tex, Vector2.one);
            ShovelDigMask.Set(tex, Vector2.one);
            ShovelDigMask.Set(tex, Vector2.one);
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
            ShovelDigMask.Set(tex, Vector2.one);
            int after1 = ShovelDigMask.LogEmitCount;

            ShovelDigMask.Set(tex, new Vector2(2f, 2f));   // 배율이 바뀜
            Assert.AreEqual(after1 + 1, ShovelDigMask.LogEmitCount, "배율이 바뀌면 다시 찍힘");

            // 마스크 미설정은 정상 상태라 로그를 안 남긴다
            ShovelDigMask.Set(null, new Vector2(2f, 2f));
            Assert.AreEqual(after1 + 1, ShovelDigMask.LogEmitCount, "마스크를 비우면 로그 없음");
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void Set_배율이_바뀌면_다시_적용된다()
    {
        var tex = MakeTex(100, 100, 255);
        try
        {
            ShovelDigMask.Set(tex, Vector2.one);
            Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var s1));
            Assert.IsFalse(s1.Contains(60f, 0f), "배율 1 → 반폭 50px");

            ShovelDigMask.Set(tex, new Vector2(2f, 2f));
            Assert.IsTrue(ShovelDigMask.TryGetSampler(50f, out var s2));
            Assert.IsTrue(s2.Contains(60f, 0f), "배율 2 → 반폭 100px");
        }
        finally { Object.DestroyImmediate(tex); }
    }

    [Test]
    public void ExtentMultiplier_비활성이면_0()
    {
        Assert.AreEqual(0f, ShovelDigMask.ExtentMultiplier, 0.0001f);
    }

    [Test]
    public void ExtentMultiplier_정사각마스크_배율1()
    {
        // 100x100, scale 1 → 실측 최대거리 70.0036 → 1*2*70.0036/100 = 1.40007
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, Vector2.one);
        Assert.AreEqual(1.40007f, ShovelDigMask.ExtentMultiplier, 0.001f);
    }

    [Test]
    public void ExtentMultiplier_세로로_긴_마스크는_2를_넘는다()
    {
        // 20x100, scale 1 → 실측 최대거리 50.4034 → 1*2*50.4034/20 = 5.04034
        // 매니저 기본값 2.0f보다 크다 = 경계 잘림이 실제로 발생하는 조건
        ShovelDigMask.SetBits(FullBits(20, 100), 20, 100, Vector2.one);
        Assert.AreEqual(5.04034f, ShovelDigMask.ExtentMultiplier, 0.001f);
        Assert.Greater(ShovelDigMask.ExtentMultiplier, 2f);
    }

    [Test]
    public void ExtentMultiplier_배율에_비례한다()
    {
        // 실측 최대거리 70.0036, scale 3 → 3*2*70.0036/100 = 4.20022
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, new Vector2(3f, 3f));
        Assert.AreEqual(4.20022f, ShovelDigMask.ExtentMultiplier, 0.001f);
    }

    [Test]
    public void BoundsRadiusPx는_ExtentMultiplier와_일관된다()
    {
        ShovelDigMask.SetBits(FullBits(20, 100), 20, 100, new Vector2(2f, 2f));
        float r = 37f;
        Assert.AreEqual(r * ShovelDigMask.ExtentMultiplier, ShovelDigMask.BoundsRadiusPx(r), 0.001f);
    }

    [Test]
    public void ExtentMultiplier는_여백을_실측으로_잘라낸다()
    {
        // 100x100인데 가운데 2x2만 불투명. 반대각선(70.7)이 아니라 실측(≈1.06)이 나와야 한다.
        var bits = new bool[100 * 100];
        for (int v = 49; v <= 50; v++)
            for (int u = 49; u <= 50; u++)
                bits[v * 100 + u] = true;

        ShovelDigMask.SetBits(bits, 100, 100, Vector2.one);

        // 중심(50,50) → 픽셀(50,50) 중심(50.5,50.5) 거리 = sqrt(0.5²+0.5²) = 0.7071
        // ExtentMultiplier = 1 * 2 * 0.7071 / 100 = 0.01414
        Assert.AreEqual(0.01414f, ShovelDigMask.ExtentMultiplier, 0.0005f);
    }
}