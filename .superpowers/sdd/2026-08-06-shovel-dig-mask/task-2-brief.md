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

