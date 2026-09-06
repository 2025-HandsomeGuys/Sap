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

