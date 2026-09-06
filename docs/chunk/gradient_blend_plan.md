# 지층 경계 블렌딩 — 노이즈 제거 & 순수 수평 그라데이션 전환 계획
@tags: blending, layer-boundary, gradient, noise, plan, TerrainBlender

> 작성일: 2026-03-17
> 목표: 픽셀 단위 점묘(Stippling) 노이즈 + 청크 단위 경계 물결 노이즈를 제거하고,
> 경계 청크 하단에서 단순한 선형(Lerp) 색상 보간만 사용하는 방식으로 전환한다.

---

## 1. 현재 시스템 요약

### 두 개의 독립적인 노이즈

| 이름 | 위치 | 역할 |
|------|------|------|
| 청크 단위 노이즈 | `TileDataManager.GetLayerNoise()` | 어느 청크가 "경계 청크"인지 결정. X에 따라 경계 Y가 물결 모양이 됨 |
| 픽셀 단위 노이즈 | `TerrainBlender.BlendLayerBoundary()` | 경계 청크 내부에서 픽셀별로 어느 층 색을 쓸지 점묘화 결정 |

### 패스 구조
```
CreateBlendedPixels()
  ├─ Pass 1 (BlendLayerBoundary)  : 노이즈 threshold 기반 픽셀 교체 (점묘)
  └─ Pass 2 (ApplySeparableBlur) : 분리형 박스 블러 (점묘 자국 부드럽게)
```

### 문제
- 결과물이 울퉁불퉁한 얼룩 패턴으로 보임
- 수평으로 깔끔하게 섞이는 자연스러운 그라데이션을 얻을 수 없음

---

## 2. 목표 시스템

- 청크 단위 노이즈 **제거** → 지층 경계가 정확한 수평선
- 픽셀 단위 노이즈 **제거** → 점묘 없음
- Pass 2 블러 **제거** → 불필요
- **대신**: 경계 청크 하단 `blendHeightRatio` 구역에서 y 위치 기반 `Color32.Lerp`만 사용

### 새 그라데이션 알고리즘
```
경계 청크의 blendHeight = height * blendHeightRatio 픽셀 구역 (y=0 ~ y=blendHeight-1):

  t = 1.0 - (y / blendHeight)       // y=0(하단): t=1.0 (아래 층 100%)
                                     // y=blendHeight(상단): t=0.0 (위 층 100%)

  for each pixel (x, y):
    if 현재 픽셀 투명(공기): 스킵
    if 아래 층 대응 픽셀 투명: 스킵

    otherY = height - 1 - y          // 아래 청크의 상단 행 대응
    result = Lerp(currentColor, belowColor, t)

    // PixelInfo (파기 시 재료 판정):
    //   t > 0.5 (y < blendHeight/2, 하단 절반) → 아래 층 재료
    //   t ≤ 0.5 (y ≥ blendHeight/2, 상단 절반) → 현재 층 재료
```

### 시각 결과
```
[현재 층 색]  ─────────────────────────
             (위 층 색 100%)
             ↓ 점진적으로 섞임
             ↓
             (아래 층 색 100%)
─────────────────────────[아래 층 색]
```

---

## 3. 변경 파일 목록 & 상세 작업

### 3-1. `TerrainBlender.cs` (핵심)

**제거:**
- `_noiseLUT` 배열, `LUT_SIZE`, `LUT_MASK` 상수
- `static TerrainBlender()` 생성자 (LUT 초기화)
- `GetFastNoise()` 메서드
- `BlendLayerBoundary()` 메서드 (점묘 패스)
- `_blurTemp` 배열
- `ApplySeparableBlur()` 메서드 (블러 패스)

**추가:**
```csharp
private static void ApplyGradientBlend(
    Color32[]           basePixels,
    Color32[]           otherPixels,
    byte[]              pixelInfo,
    byte[]              otherPixelInfo,
    int                 width,
    int                 height,
    BlendingProfileData profile)
{
    int blendHeight = Mathf.RoundToInt(height * profile.blendHeightRatio);

    for (int y = 0; y < blendHeight; y++)
    {
        float t = 1.0f - (float)y / blendHeight; // 1=하단(아래층), 0=상단(현재층)

        for (int x = 0; x < width; x++)
        {
            int idx    = y * width + x;
            int otherY = height - 1 - y;          // 아래 청크 상단 행과 대응
            int otherIdx = otherY * width + x;

            if (basePixels[idx].a == 0)  continue; // 공기(파진 공간) 스킵
            if (otherIdx < 0 || otherIdx >= otherPixels.Length) continue;
            if (otherPixels[otherIdx].a == 0) continue;

            basePixels[idx] = LerpColor(basePixels[idx], otherPixels[otherIdx], t);

            // PixelInfo: 하단 절반은 아래 층 재료 판정
            if (pixelInfo != null && otherPixelInfo != null
                && idx < pixelInfo.Length && otherIdx < otherPixelInfo.Length)
            {
                if (t > 0.5f)
                    pixelInfo[idx] = otherPixelInfo[otherIdx];
            }
        }
    }
}
```

**수정 (`CreateBlendedPixels`):**
- `coord` 매개변수 제거 (그라데이션에선 좌표 불필요)
- Pass 1 호출 → `ApplyGradientBlend()` 호출로 교체
- Pass 2 블러 호출 블록 전체 삭제

**유지:**
- `ShouldBlendLayer()` — 변경 없음
- `LerpColor()` — 그라데이션 보간에 재사용

---

### 3-2. `TileDataManager.cs`

**제거:**
- `layerNoiseScale` 필드 (line 27)
- `layerNoiseAmplitude` 필드 (line 28)
- `GetLayerNoise()` 메서드 (line 330~350)
- JSON 로딩 블록 (lines 166~169, `layerBoundaryNoise` 읽기)

**수정 (`GetTileTypeAtPosition`):**
```csharp
// 변경 전
public TileType GetTileTypeAtPosition(int xChunk, int yChunk)
{
    float noiseOffset = GetLayerNoise(xChunk, yChunk);
    float adjustedY   = yChunk + noiseOffset;
    int   roundedY    = Mathf.RoundToInt(adjustedY);
    return GetTileTypeAtDepth(roundedY);
}

// 변경 후
public TileType GetTileTypeAtPosition(int xChunk, int yChunk)
{
    return GetTileTypeAtDepth(yChunk);
}
```

> `GetTileTypeAtPosition` 시그니처는 유지 (호출 지점 다수가 두 인자를 사용하므로).
> xChunk 매개변수가 사용되지 않지만, 인터페이스 안정성을 위해 남겨둔다.

**수정 (`BuiltinDefaultProfile`):**
```csharp
// 변경 전
private static BlendingProfileData BuiltinDefaultProfile() => new BlendingProfileData
{
    name             = "__builtin_default",
    blendHeightRatio = 0.4f,
    noiseScale       = 0.02f,
    blurRadius       = 3,
    blurStrength     = 0.5f
};

// 변경 후
private static BlendingProfileData BuiltinDefaultProfile() => new BlendingProfileData
{
    name             = "__builtin_default",
    blendHeightRatio = 0.4f
};
```

---

### 3-3. `TileDataModels.cs` (`BlendingProfileData`)

```csharp
// 변경 전
[System.Serializable]
public class BlendingProfileData
{
    public string name;
    public float blendHeightRatio;
    public float noiseScale;     // ← 삭제
    public int   blurRadius;     // ← 삭제
    public float blurStrength;   // ← 삭제
}

// 변경 후
[System.Serializable]
public class BlendingProfileData
{
    public string name;
    public float blendHeightRatio;
}
```

> `LayerBoundaryNoiseConfig` 클래스와 `TileDatabaseJson.layerBoundaryNoise` 필드는
> JSON 역직렬화 호환성을 위해 **코드에서 일단 유지**. 단순히 읽기만 하고 무시.
> 추후 tileData.json 정리 시 함께 삭제 가능.

---

### 3-4. `ChunkDataProvider.cs` — 변경 없음 예정
`CreateBlendedPixels` 호출부에서 `coord`를 넘기고 있음.
→ `CreateBlendedPixels` 시그니처에서 `coord`를 제거하면 이 파일도 수정 필요.
→ 깔끔함을 위해 `coord` 제거를 권장하나, 호환성 유지를 위해 `coord`를 남기고 내부에서만 미사용으로 처리해도 됨.
**결정: `coord` 매개변수 제거, `ChunkDataProvider.cs` 호출부도 함께 수정.**

---

## 4. 변경하지 않는 것

| 항목 | 이유 |
|------|------|
| `ShouldBlendLayer()` 로직 | 경계 청크 감지 방식은 그대로 |
| `blendHeightRatio` 설정값 | 그라데이션 구역 크기 제어에 여전히 사용 |
| `GetStaminaReductionAtWorldY()` | `GetTileTypeAtDepth` 직접 호출, 영향 없음 |
| `tileData.json` 구조 | 역직렬화 오류 방지 위해 필드는 유지 (무시됨) |

---

## 5. 작업 순서

1. `TileDataModels.cs` — `BlendingProfileData`에서 `noiseScale`, `blurRadius`, `blurStrength` 제거
2. `TileDataManager.cs` — `GetLayerNoise`, `layerNoiseScale/Amplitude` 제거 + `GetTileTypeAtPosition` 단순화 + `BuiltinDefaultProfile` 정리
3. `TerrainBlender.cs` — LUT/노이즈/블러 코드 제거 + `ApplyGradientBlend` 추가 + `CreateBlendedPixels` 수정
4. `ChunkDataProvider.cs` — `CreateBlendedPixels` 호출에서 `coord` 인자 제거

---

## 6. 예상 결과

- 지층 경계가 수평으로 딱 잘린 깔끔한 그라데이션 밴드
- `blendHeightRatio = 0.4` 기준: 경계 청크 하단 400px 구간에서 두 층이 선형으로 혼합
- 물결 모양, 얼룩 패치 없음
- 성능 개선: LUT 초기화(512×512 랜덤 배열), 노이즈 샘플링, 박스 블러(두 패스) 모두 제거

---

## 7. 추후 확장 가능성

- `blendHeightRatio`를 레이어별로 다르게 설정하면 층마다 그라데이션 폭 조절 가능
- 선형 대신 S커브(Smoothstep)로 전환하려면 `ApplyGradientBlend` 내 `t` 계산만 수정:
  ```csharp
  // Smoothstep: t * t * (3 - 2*t)
  float tSmooth = t * t * (3.0f - 2.0f * t);
  ```
