## Task 5: 청크 선택 반경 — 마스크가 경계에서 잘리지 않게

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs` (`ExtentMultiplier` 추가)
- Modify: `Assets/Scripts/UI/Player/Strategies/SapStrategy.cs` (`PerformSapDig` 청크 검색 반경) ← **진짜 수정 지점**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs:1027` (방어적)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/StaticChunkTerrainManager.cs:103` (방어적)
- Test: `Assets/Tests/EditMode/ShovelDigMaskTests.cs` (테스트 추가)

### 배경 — 왜 필요한가, 그리고 어디가 진짜 문제인가

파기는 여러 청크에 걸칠 수 있고, 각 청크가 자기 좌표계로 같은 마스크를 샘플링해 이음매를 맞춘다.
그런데 **"어느 청크에게 파기를 시킬지" 고르는 반경**이 마스크 크기와 무관하게 고정돼 있으면,
마스크가 그 반경을 넘어가는 청크는 호출조차 되지 않아 **구멍이 청크 경계에서 잘린다.**

삽의 청크 선택 지점은 `SapStrategy.PerformSapDig`의 이 줄이다:

```csharp
Collider2D[] hits = Physics2D.OverlapCircleAll(digCenter, radius);
```

⚠ **`Digger.DigAt` → `ITerrainManager.ModifyTerrain` 경로는 삽과 무관하다.**
`Digger.Update`(`Digger.cs:97`)가 `currentTool == 1`이면 즉시 return하므로 삽은 거기 도달하지 않는다.
매니저 2곳의 `maxScale = 2.0f` 수정은 **방어적 조치**이지 현재 삽 경로의 수정이 아니다.
`Digger.cs:324`의 `sweepDistance`도 같은 이유로 **이번 범위에서 건드리지 않는다** (삽이 도달 불가 = 죽은 코드가 된다).

### 기존 동작 보존이 최우선

`Physics2D.OverlapCircleAll(digCenter, radius)`의 결과는 **지형 청크와 돌(`IDiggable`) 양쪽**에 쓰인다.
반경을 무조건 넓히면 삽이 더 먼 돌까지 때리게 되어 게임플레이가 바뀐다.

따라서:
- **돌 검색 반경은 절대 바꾸지 않는다.** 기존 `radius` 그대로.
- **지형 청크 검색 반경만** 마스크가 활성일 때 넓힌다.
- 마스크가 비활성이면 두 반경이 같으므로 **같은 배열을 재사용**한다 → 추가 쿼리도, 동작 변화도 없다.

---

- [ ] **Step 1: `ShovelDigMask.ExtentMultiplier` 추가 + 테스트**

`ShovelDigMask.cs`의 `BoundsRadiusPx`를 아래로 **교체**한다. 새 프로퍼티를 추가하고
기존 메서드는 그것을 쓰도록 바꾸는 것이다 (반환값은 수학적으로 완전히 동일).

```csharp
    /// <summary>
    /// 파기 반경 1단위당 마스크가 뻗는 최대 배율. `BoundsRadiusPx(r) == r * ExtentMultiplier`이며
    /// radiusPx와 무관한 상수라, 월드 단위로 일하는 호출부(청크 검색 반경)가 그대로 쓸 수 있다.
    ///
    /// 값의 유래: pxPerMaskPx = (r*2/w)*scale, 회전 외접반경 = sqrt(w²+h²)/2
    ///          → BoundsRadiusPx = r * scale * sqrt(w²+h²) / w
    ///
    /// 비활성이면 0 — 호출부는 Mathf.Max로 기존 값과 비교하므로 0이 안전한 중립값이다.
    /// </summary>
    public static float ExtentMultiplier
    {
        get
        {
            if (!IsActive) return 0f;
            float diagonal = Mathf.Sqrt(s_width * (float)s_width + s_height * (float)s_height);
            return s_scale * diagonal / s_width;
        }
    }

    /// <summary>
    /// 픽셀 순회 범위로 쓸 반경(지형 픽셀). 마스크가 어느 각도로 회전해도 안 잘리도록
    /// 마스크 사각형의 외접원(반대각선) 크기를 준다.
    /// 넉넉해도 실제 제거는 Sampler.Contains가 결정하므로 결과가 틀리지 않는다. 모자라면 잘린다.
    /// </summary>
    public static float BoundsRadiusPx(float radiusPx)
    {
        if (!IsActive) return 0f;
        return radiusPx * ExtentMultiplier;
    }
```

⚠ `PixelsPerMaskPixel` private 메서드가 `BoundsRadiusPx`에서만 쓰였다면 `TryGetSampler`도 쓰고 있는지
확인하라. `TryGetSampler`가 쓰고 있으면 **지우지 마라.**

`ShovelDigMaskTests.cs` 끝(마지막 `}` 직전)에 테스트를 추가한다:

```csharp
    [Test]
    public void ExtentMultiplier_비활성이면_0()
    {
        Assert.AreEqual(0f, ShovelDigMask.ExtentMultiplier, 0.0001f);
    }

    [Test]
    public void ExtentMultiplier_정사각마스크_배율1()
    {
        // 100x100, scale 1 → sqrt(2) ≈ 1.4142
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, 1f);
        Assert.AreEqual(1.4142f, ShovelDigMask.ExtentMultiplier, 0.001f);
    }

    [Test]
    public void ExtentMultiplier_세로로_긴_마스크는_2를_넘는다()
    {
        // 20x100, scale 1 → sqrt(20²+100²)/20 = 101.98/20 = 5.099
        // 매니저 기본값 2.0f보다 크다 = 경계 잘림이 실제로 발생하는 조건
        ShovelDigMask.SetBits(FullBits(20, 100), 20, 100, 1f);
        Assert.AreEqual(5.099f, ShovelDigMask.ExtentMultiplier, 0.001f);
        Assert.Greater(ShovelDigMask.ExtentMultiplier, 2f);
    }

    [Test]
    public void ExtentMultiplier_배율에_비례한다()
    {
        ShovelDigMask.SetBits(FullBits(100, 100), 100, 100, 3f);
        Assert.AreEqual(4.2426f, ShovelDigMask.ExtentMultiplier, 0.001f);
    }

    [Test]
    public void BoundsRadiusPx는_ExtentMultiplier와_일관된다()
    {
        ShovelDigMask.SetBits(FullBits(20, 100), 20, 100, 2f);
        float r = 37f;
        Assert.AreEqual(r * ShovelDigMask.ExtentMultiplier, ShovelDigMask.BoundsRadiusPx(r), 0.001f);
    }
```

- [ ] **Step 2: `SapStrategy.PerformSapDig`의 청크 검색 반경 분리** ← 핵심

`SapStrategy.cs`에서 아래 기존 줄을

```csharp
        Collider2D[] hits = Physics2D.OverlapCircleAll(digCenter, radius);
```

이렇게 교체한다:

```csharp
        // 돌(IDiggable) 검색 반경은 그대로 둔다 — 넓히면 삽이 더 먼 돌을 때리게 되어 게임플레이가 바뀐다.
        Collider2D[] hits = Physics2D.OverlapCircleAll(digCenter, radius);

        // 지형 청크만 따로 넓힌다. 마스크가 radius를 넘어 뻗으면 그 너머 청크가
        // 아예 호출되지 않아 구멍이 청크 경계에서 잘린다.
        // 마스크 비활성이면 반경이 같으므로 같은 배열을 재사용한다 → 추가 쿼리 없음, 동작 변화 없음.
        float terrainSearchRadius = radius * Mathf.Max(1f, ShovelDigMask.ExtentMultiplier);
        Collider2D[] terrainHits = (terrainSearchRadius > radius)
            ? Physics2D.OverlapCircleAll(digCenter, terrainSearchRadius)
            : hits;
```

그 다음, 기존의 단일 `foreach (var hitCollider in hits)` 루프를 **두 개로 쪼갠다.**

기존 루프는 이 형태다 (돌 처리와 지형 처리가 한 루프 안에 있다):

```csharp
        foreach (var hitCollider in hits)
        {
            IDiggable diggable = hitCollider.GetComponent<IDiggable>();
            if (diggable != null && p.CanDigRock)
            {
                ... 돌 처리 (스태미나, damage, diggable.Dig, hitAnyRock) ...
            }

            TerrainChunk chunk = hitCollider.GetComponent<TerrainChunk>();
            if (chunk != null && p.CanDigTerrain)
            {
                if (chunk.Dig(digCenter, radius, p.ToolIndex))
                    hitAnyTerrain = true;
            }
        }
```

이것을 아래처럼 바꾼다. **돌 처리 블록의 내용은 한 글자도 바꾸지 말고 그대로 옮겨라.**

```csharp
        // 돌 처리 — 기존 반경(hits) 그대로
        foreach (var hitCollider in hits)
        {
            IDiggable diggable = hitCollider.GetComponent<IDiggable>();
            if (diggable != null && p.CanDigRock)
            {
                ... 기존 돌 처리 블록을 그대로 여기에 ...
            }
        }

        // 지형 처리 — 마스크가 활성이면 넓힌 반경(terrainHits)
        foreach (var hitCollider in terrainHits)
        {
            TerrainChunk chunk = hitCollider.GetComponent<TerrainChunk>();
            if (chunk != null && p.CanDigTerrain)
            {
                // ModifyTerrain(void) 대신 Dig(bool) — 콜라이더에 닿았다고 흙이 깎인 건 아니다.
                // 이미 파인 공간이나 불괴 픽셀을 치면 false → 아래 비용 블록이 통째로 건너뛴다.
                if (chunk.Dig(digCenter, radius, p.ToolIndex))
                    hitAnyTerrain = true;
            }
        }
```

⚠ `chunk.Dig`에 넘기는 인자는 **`radius` 그대로**다. `terrainSearchRadius`를 넘기면 파기 크기 자체가
커져버린다 — 넓힌 것은 "어느 청크를 부를지"이지 "얼마나 팔지"가 아니다.

⚠ 루프를 쪼개면서 `hitAnyRock` / `hitAnyTerrain` 변수 선언이 루프보다 위에 있는지 확인하라.
그 아래의 스태미나 비용 블록(`if (hitAnyRock) ...`, `if (hitAnyTerrain && !p.IgnoreStaminaCost) ...`)은
**건드리지 마라.**

- [ ] **Step 3: 매니저 2곳 (방어적)**

삽은 현재 이 경로를 타지 않지만, 나중에 배선이 바뀌면 재현 조건이 까다로운 경계 잘림 버그가 된다.
`toolIndex`로 가드해 삽이 아닌 도구에는 영향이 0이 되게 한다.

`InfinityMapManager.cs:1027`의

```csharp
        float maxScale = 2.0f;
```

를 아래로 교체:

```csharp
        // 삽 마스크가 radius*2를 넘어 뻗으면 그 너머 청크가 호출되지 않아 경계에서 잘린다.
        // 삽이 아닌 도구는 영향 0. (현재 삽은 SapStrategy가 직접 chunk.Dig를 부르므로 이 경로를
        //  타지 않지만, 배선이 바뀌었을 때 조용히 깨지지 않도록 방어해둔다.)
        float maxScale = (toolIndex == 1) ? Mathf.Max(2.0f, ShovelDigMask.ExtentMultiplier) : 2.0f;
```

`StaticChunkTerrainManager.cs:103`의 `float maxScale = 2.0f;`도 **똑같이** 교체한다.
(매니저가 둘이다. 하나만 고치면 정적 청크 씬에서만 재현되는 버그가 된다.)

- [ ] **Step 4: 확인 (사람이 직접)**

1. Unity 콘솔 컴파일 에러 없음
2. EditMode 테스트 — 기존 22개 + 신규 5개 = 27개
3. **마스크 없이** 삽·곡괭이·드릴 파기 → 지금과 동일 (특히 삽이 때리는 돌의 사거리가 안 변했는지)
4. 세로로 긴 마스크(예: 20x100) 또는 배율 2 이상 → 청크 경계를 걸쳐 파기 → 잘림 없음

- [ ] **Step 5: 체크인**

UVCS에서 사람이 직접. 설명 예시: `fix: 삽 마스크가 청크 경계에서 잘리지 않게 지형 검색 반경 확장`
