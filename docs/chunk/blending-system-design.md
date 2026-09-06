# 블렌딩 시스템 리팩터링 설계서
@tags: blending, layer-boundary, stippling, blur, tile-type, BlendingProfileData, TerrainBlender, refactoring, stamina, JSON

> 작성일: 2026-03-16
> 대상 브랜치: master

---

## 1. 개요

### 목적

현재 층 경계 블렌딩 시스템의 세 가지 문제를 해결한다.
적용 범위는 **지형 층 경계 전용**이다. 특수 청크 경계 블렌딩은 이 시스템의 범위 밖이다.

| 문제 | 내용 |
|---|---|
| 시각적 거칠음 | 점묘화(stippling) 결과가 하드엣지처럼 보임. 소프트 블러 패스 추가 필요 |
| 파라미터 분산 | `blendingRatio`는 `InfinityMapManager`, `layerNoiseScale/Amplitude`는 `TileDataManager` Inspector에 흩어져 있어 관리 어려움 |
| 저항값 단절 | 블렌딩 구역 안에서 파기 시 저항값이 층 경계에서 급변함. 선형 보간 필요 |

### 최종 동작 목표

```
[ 층 경계 청크 — BasePixels 생성 시 1회 ]
  1. Stippling 패스  → 하단부 픽셀을 노이즈 임계값 기반으로 아래 층 색으로 교체
  2. Soft Blur 패스  → 블렌딩 구역 내 불투명 픽셀에 분리형 박스 블러 적용

[ 블렌딩 구역 안에서 파기 ]
  StaminaDigCostCalculator:
  - 파기 지점 worldY 기반으로 두 층 maxStaminaReduction 선형 보간
  - 구역 상단(현재 층) → 구역 하단(아래 층) 으로 저항값이 연속 변화

[ 파라미터 관리 ]
  BlendingProfileData (tileData.json):
  - 층별 optional 프로필, null 이면 전역 기본값 사용
  - Inspector 없이 JSON 편집만으로 튜닝 가능
```

---

## 2. 현재 시스템 분석

### 2.1 TerrainBlender.cs 동작

```
CreateBlendedPixels()
  └─ BlendLayerBoundary()
       ├─ 블렌딩 높이 = height * blendingRatio  (청크 하단부)
       ├─ 각 픽셀마다 LUT 노이즈 샘플
       ├─ Y위치 → threshold(0.2~0.8) 계산
       └─ noise > threshold → 픽셀을 아래 층 색으로 교체
                              + pixelInfo[idx] = belowTileTypeId
```

교체된 픽셀과 원본 픽셀 사이에 색상 단차가 그대로 남아 경계가 거칠어 보인다.

### 2.2 파라미터 현황

| 파라미터 | 현재 위치 | 문제 |
|---|---|---|
| `blendingRatio` | `InfinityMapManager` Inspector | 전역 값, 층별 조정 불가 |
| `layerNoiseScale` | `TileDataManager` private | Inspector 노출 없음 |
| `layerNoiseAmplitude` | `TileDataManager` private | 同上 |
| 블러 파라미터 | 없음 | 미구현 |

### 2.3 저항값 현황

`StaminaDigCostCalculator`는 `pixelInfo` 바이트 → `TileType` → `maxStaminaReduction`으로 조회한다.
Stippling으로 교체된 픽셀은 아래 층 TileType이 기록되지만, 교체되지 않은 픽셀은 원본 TileType 그대로여서 동일 블렌딩 구역 안에서도 저항값이 랜덤하게 달라진다. 블렌딩 구역 전체에 걸쳐 연속적인 저항 변화가 없다.

---

## 3. 데이터 구조 설계

### 3.1 BlendingProfileData (신규)

`TileDataModels.cs`에 추가.

```csharp
[Serializable]
public class BlendingProfileData
{
    public string name;              // 프로필 식별자 (tileData.json 내 고유)

    // Stippling
    public float blendHeightRatio;   // 블렌딩 구역 높이 비율 (0~1). 예: 0.4 = 청크 하단 40%
    public float noiseScale;         // 점묘화 노이즈 샘플 스케일. 작을수록 덩어리가 커짐

    // Soft Blur
    public int   blurRadius;         // 박스 블러 반경 (픽셀). 0 = 블러 없음
    public float blurStrength;       // 블러 적용 강도 (0~1). 0 = 원본 유지, 1 = 완전 블러
}
```

### 3.2 tileData.json 스키마 변경

#### TileDatabaseJson

```csharp
[Serializable]
public class TileDatabaseJson
{
    // 기존 필드 유지
    public int terrainWidth;
    public int terrainDepth;
    public List<TileDataJson> tiles;
    public ElevatorConfig elevatorConfig;
    public LayerBoundaryNoiseConfig layerBoundaryNoise;  // 경계 흔들기 노이즈 (유지)

    // [NEW]
    public List<BlendingProfileData> blendingProfiles;
    public string defaultBlendingProfileName;  // null 이면 빌트인 기본값 사용
}
```

#### TileDataJson

```csharp
[Serializable]
public class TileDataJson
{
    // 기존 필드 유지
    public string tileType;
    public float  maxStaminaReduction;
    public int    startDepth;
    public int    tier;
    public float  hardness;
    public List<MineralRuleJson> minerals;

    // [NEW] 이 층의 하단 경계에 사용할 블렌딩 프로필 이름
    // null 또는 빈 문자열 → defaultBlendingProfileName 사용
    public string blendingProfileName;
}
```

> **주의:** `blendingProfileName`은 해당 층의 **하단 경계**에 적용된다.
> `Dirt` → `Ice` 경계 블렌딩 프로필은 `Dirt` 타일에 설정한다.
> 가장 깊은 층(`MeteoriteRock`)은 아래가 없으므로 필드가 있어도 무시된다.

#### tileData.json 예시

```json
{
  "terrainDepth": 36,
  "defaultBlendingProfileName": "default",
  "blendingProfiles": [
    {
      "name": "default",
      "blendHeightRatio": 0.40,
      "noiseScale": 0.02,
      "blurRadius": 3,
      "blurStrength": 0.5
    },
    {
      "name": "heavy",
      "blendHeightRatio": 0.55,
      "noiseScale": 0.025,
      "blurRadius": 5,
      "blurStrength": 0.7
    }
  ],
  "tiles": [
    { "tileType": "Dirt",          "startDepth": 0,   "maxStaminaReduction": 0.1, "blendingProfileName": "default" },
    { "tileType": "Ice",           "startDepth": -20, "maxStaminaReduction": 0.5, "blendingProfileName": "default" },
    { "tileType": "MagmaRock",     "startDepth": -40, "maxStaminaReduction": 1.0, "blendingProfileName": "heavy"   },
    { "tileType": "MeteoriteRock", "startDepth": -60, "maxStaminaReduction": 2.0 }
  ]
}
```

---

## 4. TileDataManager 확장

### 4.1 블렌딩 프로필 로드 및 조회

```csharp
// TileDataManager 내부 필드
private Dictionary<string, BlendingProfileData> _blendingProfiles = new();
private BlendingProfileData _defaultProfile;

// LoadFromJson() 내부에서 로드
foreach (var p in database.blendingProfiles ?? new List<BlendingProfileData>())
    _blendingProfiles[p.name] = p;

_defaultProfile = (!string.IsNullOrEmpty(database.defaultBlendingProfileName)
    && _blendingProfiles.TryGetValue(database.defaultBlendingProfileName, out var def))
    ? def
    : BuiltinDefaultProfile();

// 조회 API
public BlendingProfileData GetBlendingProfile(string name)
{
    if (string.IsNullOrEmpty(name)) return _defaultProfile;
    return _blendingProfiles.TryGetValue(name, out var p) ? p : _defaultProfile;
}

public BlendingProfileData GetBlendingProfileForLayer(TileType tileType)
{
    var data = GetData(tileType);
    return GetBlendingProfile(data?.blendingProfileName);
}
```

빌트인 기본값 (tileData.json에 프로필이 없을 때 fallback):

```csharp
private static BlendingProfileData BuiltinDefaultProfile() => new BlendingProfileData
{
    name             = "__builtin_default",
    blendHeightRatio = 0.4f,
    noiseScale       = 0.02f,
    blurRadius       = 3,
    blurStrength     = 0.5f
};
```

### 4.2 블렌딩 구역 저항값 보간 API

```csharp
/// <summary>
/// 파기 지점 worldY 기반으로 스태미나 감소값을 반환한다.
/// 블렌딩 구역 안이면 두 층 maxStaminaReduction 을 선형 보간한다.
/// </summary>
/// <param name="worldY">파기 지점의 월드 Y (유닛)</param>
/// <param name="chunkHeightWorld">청크 높이 (유닛). InfinityMapManager.chunkHeightWorld</param>
public float GetStaminaReductionAtWorldY(float worldY, float chunkHeightWorld)
{
    int   chunkY = Mathf.FloorToInt(worldY / chunkHeightWorld);
    float subY   = (worldY - chunkY * chunkHeightWorld) / chunkHeightWorld; // 0=하단, 1=상단

    TileType currentType = GetTileTypeAtDepth(chunkY);
    TileType typeBelow   = GetTileTypeAtDepth(chunkY - 1);

    // 같은 층이면 일반 조회
    if (currentType == typeBelow)
        return GetData(currentType)?.maxStaminaReduction ?? 0f;

    // 블렌딩 구역 판정: 청크 하단 blendHeightRatio 이내
    BlendingProfileData profile = GetBlendingProfileForLayer(currentType);
    if (subY < profile.blendHeightRatio)
    {
        // t = 0 → 하단 끝 (아래 층 저항), t = 1 → 블렌딩 구역 상단 (현재 층 저항)
        float t            = subY / profile.blendHeightRatio;
        float staminaAbove = GetData(currentType)?.maxStaminaReduction ?? 0f;
        float staminaBelow = GetData(typeBelow)?.maxStaminaReduction   ?? 0f;
        return Mathf.Lerp(staminaBelow, staminaAbove, t);
    }

    return GetData(currentType)?.maxStaminaReduction ?? 0f;
}
```

---

## 5. TerrainBlender 리팩터링

### 5.1 변경 방향

| 항목 | 변경 전 | 변경 후 |
|---|---|---|
| 파라미터 | `float blendingRatio` 직접 수신 | `BlendingProfileData profile` 수신 |
| 블러 | 없음 | `ApplySeparableBlur()` 패스 추가 |
| 적용 범위 | 층 경계 전용 | 층 경계 전용 (범위 변경 없음) |

### 5.2 메서드 시그니처

```csharp
// 기존 진입점 — float blendingRatio → BlendingProfileData profile 로 교체
public static Color32[] CreateBlendedPixels(
    Color32[]           sourcePixels,
    Color32[]           otherPixels,
    byte[]              pixelInfo,
    byte[]              otherPixelInfo,
    Vector2Int          coord,
    TileType            currentType,
    TileType            typeBelow,
    int                 width,
    int                 height,
    BlendingProfileData profile);       // ← 변경
```

### 5.3 패스 실행 순서

```
CreateBlendedPixels() 호출
  │
  ├─ 1. Stippling 패스  (기존 BlendLayerBoundary 로직, noiseScale 파라미터화)
  │       노이즈 임계값 기반 픽셀 교체 + pixelInfo 교체
  │
  └─ 2. Soft Blur 패스  (신규, blurRadius > 0 일 때만 실행)
          블렌딩 구역 내 불투명 픽셀에 분리형 박스 블러 적용
          결과를 sourcePixels 배열에 직접 기록 → BasePixels 에 굽힘
```

### 5.4 소프트 블러 — 분리형 박스 블러 (Separable Box Blur)

수평 → 수직 2패스로 나눠 처리한다.

```
연산 수 비교 (블렌딩 구역 400×1000px, 반경 3):
  단순 박스 블러:    400 × 1000 × 7×7 ≈ 19,600,000
  분리형 박스 블러:  400 × 1000 × 7×2 ≈  5,600,000  (약 3.5배 빠름)
```

알고리즘 개요:

```
blendZone: y = [startY, endY)    (청크 하단 blendHeightRatio 비율)

Pass 1 — 수평 블러 (pixels → temp):
  for y in blendZone:
    for x in [0, width):
      if pixel[y,x].a == 0: continue   ← 투명(공기) 픽셀 스킵
      avg = avg(pixel[y, clamp(x-r, 0, width-1) .. clamp(x+r, 0, width-1)])
      temp[y,x] = Lerp(pixel[y,x], avg, blurStrength)

Pass 2 — 수직 블러 (temp → pixels):
  for y in blendZone:
    for x in [0, width):
      if pixel[y,x].a == 0: continue
      avg = avg(temp[clamp(y-r, startY, endY-1) .. clamp(y+r, startY, endY-1), x])
                                         ↑ blendZone 범위 내로 클램프 (구역 밖 픽셀 오염 방지)
      pixel[y,x] = Lerp(pixel[y,x], avg, blurStrength)
```

> **경계 처리:** 수직 패스에서 샘플 윈도우를 `[startY, endY)` 안으로 클램프한다.
> 이를 통해 블러가 블렌딩 구역 밖 픽셀에 영향을 주지 않는다.

---

## 6. ChunkDataProvider 변경

`_blendingRatio` 필드를 제거하고 `BlendingProfileData` 조회 방식으로 교체한다.

```csharp
// 변경 전
groundPixels = TerrainBlender.CreateBlendedPixels(..., _blendingRatio);

// 변경 후
BlendingProfileData profile =
    TileDataManager.Instance.GetBlendingProfileForLayer(context.TileType);
groundPixels = TerrainBlender.CreateBlendedPixels(..., profile);
```

`InfinityMapManager`의 `blendingRatio` Inspector 필드도 함께 제거한다 (JSON으로 이동).

---

## 7. StaminaDigCostCalculator 변경

파기 지점의 worldY를 받는 오버로드를 추가한다.
기존 `TryCalculate(TileType, ...)` 오버로드는 제거하지 않고 유지한다 (블렌딩 구역 외 호출자에서 계속 사용).

```csharp
// [NEW] 월드 위치 기반 오버로드
public bool TryCalculate(Vector2 worldPos, float chunkHeightWorld, out float reduction)
{
    reduction = TileDataManager.Instance
        .GetStaminaReductionAtWorldY(worldPos.y, chunkHeightWorld);

    if (reduction <= 0f) return true;

    if (UpgradeManager.Instance != null)
        reduction = UpgradeManager.Instance
            .GetStatValue(UpgradeEffectType.StaminaCostMultiplier, reduction);

    return StaminaManager.Instance.HasEnough(reduction);
}
```

`Digger.cs`에서 파기 지점 `actualHitPos`가 이미 확보되어 있으므로 해당 위치에서 새 오버로드를 호출한다.

---

## 8. IChunkInitializer 확장 (순서 제어)

ChunkEdgeBlender를 위해 추가하지 않는다. 향후 다음 시나리오에서 필요해질 수 있어 인터페이스 확장만 추가한다:

- BasePixels 의존 관계가 있는 새 Initializer 추가 시
  (예: 공동 형태에 맞춰 암석을 자동 배치하는 Initializer)
- SpriteCavityInitializer 이후에 픽셀을 후처리해야 하는 경우

```csharp
public interface IChunkInitializer
{
    /// <summary>낮을수록 먼저 실행. 기본값 0.</summary>
    int InitializationOrder { get; }
    void Initialize(Transform parent);
}
```

기존 구현체 기본값:

| 클래스 | InitializationOrder |
|---|---|
| SpriteCavityInitializer | 0 |
| IndestructibleOverlayInit | 0 |
| (이후 추가 구현체) | 명시 권장 |

`SpecialChunkManager` 호출부에 정렬 추가:

```csharp
var initializers = anchorObj.GetComponentsInChildren<IChunkInitializer>();
System.Array.Sort(initializers,
    (a, b) => a.InitializationOrder.CompareTo(b.InitializationOrder));
foreach (var init in initializers)
    init.Initialize(parent);
```

정렬 비용: 요소 2~3개 배열, 사실상 0.

---

## 9. 파이프라인 통합 요약

```
Phase 1 — Spawn (ChunkDataProvider)
  일반 청크 + 특수 청크 공통:
    GetBlendingProfileForLayer(TileType) → profile
    TerrainBlender.CreateBlendedPixels(..., profile)
      ├─ Stippling 패스
      └─ Soft Blur 패스
    결과 → context.GroundPixels → ChunkData.BasePixels

Phase 2 — Decorate (IChunkInitializer)
  InitializationOrder 오름차순 정렬 후 실행
  블렌딩 관련 작업 없음

Phase 3 — Finalize
  변경 없음 (경계 텍스처 렌더링, 조명 BFS)
```

---

## 10. 구현 파일 목록

### 변경되는 파일

| 파일 | 변경 내용 |
|---|---|
| `TileDataModels.cs` | `BlendingProfileData` 추가, `TileDatabaseJson`·`TileDataJson` 필드 추가 |
| `TileDataManager.cs` | 프로필 로드/조회 메서드, `GetStaminaReductionAtWorldY()` 추가 |
| `TerrainBlender.cs` | `float blendingRatio` → `BlendingProfileData profile`, `ApplySeparableBlur()` 추가 |
| `ChunkDataProvider.cs` | `_blendingRatio` 제거, 프로필 조회 방식으로 교체 |
| `InfinityMapManager.cs` | `blendingRatio` Inspector 필드 제거 |
| `StaminaDigCostCalculator.cs` | worldPos 기반 오버로드 추가 |
| `IChunkInitializer.cs` | `int InitializationOrder { get; }` 추가 |
| `SpecialChunkManager.cs` | IChunkInitializer 정렬 후 실행 |
| `SpriteCavityInitializer.cs` | `InitializationOrder => 0` 구현 |
| `IndestructibleOverlayInit.cs` | `InitializationOrder => 0` 구현 |
| `tileData.json` | `blendingProfiles` 배열, `defaultBlendingProfileName`, 각 tile `blendingProfileName` 추가 |

### 새로 생성되는 파일

없음.

---

## 11. 주의사항

### 블렌딩 구역 밖 픽셀 보호
Soft Blur의 수직 패스 샘플 윈도우를 블렌딩 구역 `[startY, endY)` 안으로 반드시 클램프한다.
클램프 누락 시 블렌딩 구역 위쪽의 선명한 픽셀이 번지는 아티팩트 발생.

### 저항값 보간과 비주얼 구역의 일치
`GetStaminaReductionAtWorldY()`의 보간 범위는 `BlendingProfileData.blendHeightRatio`를 기준으로 한다.
비주얼 블렌딩 구역과 저항 변화 구간이 항상 동일하다.

### blurRadius = 0 처리
`blurRadius == 0`이면 Blur 패스 전체를 건너뛴다. 기존 점묘화 전용 동작을 원할 때 사용.

### blendingProfileName 미설정 시 동작
`TileDataJson.blendingProfileName`이 null 또는 빈 문자열이면 `defaultBlendingProfileName`을 사용한다.
`defaultBlendingProfileName`도 없거나 잘못된 이름이면 빌트인 기본값(`blendHeightRatio=0.4, blurRadius=3, blurStrength=0.5`)으로 fallback한다.
