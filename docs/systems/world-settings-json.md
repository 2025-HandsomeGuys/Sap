# WorldSettings JSON 시스템 설계
@tags: world-settings, JSON, configuration, worldSettings, colliderUpdateInterval, system

> 작성일: 2026-03-12
> 목적: InfinityMapManager 및 관련 시스템의 Inspector 설정값을 JSON으로 분리하는 설계 문서

---

## 1. 목표

현재 Inspector에 분산된 수치 설정값들을 `StreamingAssets/worldSettings.json` 하나로 통합하여:

- **환경 분리**: 개발(디스크 저장 OFF, 낮은 뷰거리)과 릴리즈(디스크 저장 ON, 최적화 설정)를 파일로 관리
- **비개발자 접근성**: 디자이너/QA가 Unity를 열지 않고 수치 조정 가능
- **Inspector 단순화**: 레퍼런스 필드(Prefab, Transform, SO)만 남기고 수치는 전부 JSON으로

---

## 2. 설정 분류 기준

### JSON으로 이동할 설정

> 수치형 파라미터, 환경별로 다를 수 있는 값, 디자이너/QA가 조정할 수 있는 값

| 설정 | 현재 위치 | 이유 |
|------|-----------|------|
| `worldSeed` | `InfinityMapManager` public field | 시드를 외부에서 고정하거나 0(랜덤)으로 설정 가능하게 |
| `viewDistance` | `InfinityMapManager` public field | 개발 시 1, 릴리즈 시 조정 |
| `enableMemoryCache` | `InfinityMapManager` [Tooltip] | 테스트/릴리즈 환경 토글 |
| `enableDiskSave` | `InfinityMapManager` [Tooltip] | 테스트 시 꺼두고, 릴리즈 시 켜기 |
| `enableLayerBlending` | `InfinityMapManager` [Header] | 비주얼 품질 토글 |
| `blendingRatio` | `InfinityMapManager` [Range] | 경계 블렌딩 강도 튜닝 |
| `globalLightFalloff` | `InfinityMapManager` [Range] | 조명 감쇠 강도 튜닝 |
| `chunkSpawningBatchSize` | `InfinityMapManager` [SerializeField] | 성능 프로파일링용 튜닝 |
| `maxTimePerBatchMs` | `InfinityMapManager` [SerializeField] | 성능 프로파일링용 튜닝 |
| `maxTimePerFramePh2Ms` | `InfinityMapManager` [SerializeField] | 성능 프로파일링용 튜닝 |
| `TEXTURE_UPDATE_INTERVAL` | `InfinityMapManager` private const | 현재 0.05f 하드코딩 → 조정 가능하게 |
| `verticalScale` | `TerrainChunk` Prefab [SerializeField] | 파기 타원형 비율 튜닝 |
| `useIslandRemoval` | `TerrainChunk` Prefab [SerializeField] | 고립 픽셀 제거 알고리즘 토글 |
| `maxIslandSize` | `TerrainChunk` Prefab [SerializeField] | 고립 픽셀 제거 임계값 |
| `solidThickness` | `TerrainChunk` Prefab [SerializeField] | 경계 두께 렌더링 튜닝 |
| `textureThickness` | `TerrainChunk` Prefab [SerializeField] | 텍스처 경계 두께 튜닝 |
| `colliderUpdateInterval` | `TerrainChunk` Prefab [SerializeField] | 콜라이더 갱신 빈도 성능 튜닝 |

### Inspector에 남길 설정 (레퍼런스형)

> Unity Object 참조는 JSON으로 표현 불가 → Inspector 유지

| 설정 | 이유 |
|------|------|
| `chunkPrefab` (GameObject) | Unity 오브젝트 레퍼런스 |
| `player` (Transform) | 씬 레퍼런스 |
| `tileVisualSettings` (ScriptableObject) | SO 레퍼런스 |
| `_borderTexture`, `_secondaryBorderTexture` | Texture2D 레퍼런스 |
| `solidBorderColor` (Color32) | 선택적 — 색상은 Inspector가 더 직관적 |

### 코드 상수로 유지

> 알고리즘 내부 상수, 절대 바꾸지 않을 값

| 상수 | 위치 | 이유 |
|------|------|------|
| `COORD_UNINIT = int.MaxValue` | `InfinityMapManager` | 초기화 센티널, 의미 변경 불가 |
| `BOUNDARY_COST = 5` | `TerrainLightingCalculator` | 조명 알고리즘 내부 상수 |
| `MaxLightDistScaled = 255` | `TerrainLightingCalculator` | byte 최대값과 연동 |
| `MAX_COORD = 10000` | `InfinityMapManager.ShouldSkip` | 맵 절대 한계 |

---

## 3. JSON 파일 구조

**파일 위치:** `Assets/StreamingAssets/worldSettings.json`
(기존 `tileData.json`과 동일 경로 — 같은 로더 패턴 활용)

```json
{
  "world": {
    "seed": 0,
    "viewDistance": 1
  },
  "save": {
    "enableMemoryCache": true,
    "enableDiskSave": false
  },
  "rendering": {
    "enableLayerBlending": true,
    "blendingRatio": 0.4,
    "globalLightFalloff": 30.0,
    "textureUpdateInterval": 0.05
  },
  "performance": {
    "chunkSpawningBatchSize": 4,
    "maxTimePerBatchMs": 6,
    "maxTimePerFramePh2Ms": 5
  },
  "chunk": {
    "verticalScale": 1.5,
    "useIslandRemoval": true,
    "maxIslandSize": 50,
    "solidThickness": 1.6,
    "textureThickness": 4.0,
    "colliderUpdateInterval": 0.2
  }
}
```

> **`world.seed = 0`이면** 기존 동작 유지 — `Random.Range(1000, 99999)`로 랜덤 시드 생성.
> 고정 시드를 원하면 0이 아닌 값을 지정.

---

## 4. 새로 만들 파일

### 4-1. `WorldSettingsData.cs` — 데이터 모델

**위치:** `Assets/Scripts/_Core/Data/WorldSettingsData.cs`

```csharp
[System.Serializable]
public class WorldSettingsData
{
    public WorldSection world = new WorldSection();
    public SaveSection save = new SaveSection();
    public RenderingSection rendering = new RenderingSection();
    public PerformanceSection performance = new PerformanceSection();
    public ChunkSection chunk = new ChunkSection();

    [System.Serializable]
    public class WorldSection
    {
        public int seed = 0;
        public int viewDistance = 1;
    }

    [System.Serializable]
    public class SaveSection
    {
        public bool enableMemoryCache = true;
        public bool enableDiskSave = false;
    }

    [System.Serializable]
    public class RenderingSection
    {
        public bool enableLayerBlending = true;
        public float blendingRatio = 0.4f;
        public float globalLightFalloff = 30f;
        public float textureUpdateInterval = 0.05f;
    }

    [System.Serializable]
    public class PerformanceSection
    {
        public int chunkSpawningBatchSize = 4;
        public int maxTimePerBatchMs = 6;
        public int maxTimePerFramePh2Ms = 5;
    }

    [System.Serializable]
    public class ChunkSection
    {
        public float verticalScale = 1.5f;
        public bool useIslandRemoval = true;
        public int maxIslandSize = 50;
        public float solidThickness = 1.6f;
        public float textureThickness = 4.0f;
        public float colliderUpdateInterval = 0.2f;
    }
}
```

---

### 4-2. `WorldSettingsLoader.cs` — 로더

**위치:** `Assets/Scripts/_Core/Data/WorldSettingsLoader.cs`

- `TileDataManager`의 tileData.json 로딩 패턴과 동일하게 구현
- `Application.streamingAssetsPath` 기반으로 읽기
- JSON 파싱 실패 시 기본값(코드 내 default) 사용 → 게임이 항상 실행 가능해야 함
- 싱글톤으로 구현, `Awake()`에서 로드

```
흐름:
  Awake()
    → StreamingAssets/worldSettings.json 읽기
    → JsonUtility.FromJson<WorldSettingsData>()
    → 파싱 실패 시 new WorldSettingsData() (기본값)
    → Instance에 저장
```

---

## 5. 수정할 파일

### 5-1. `InfinityMapManager.cs`

**변경 전 (Inspector 필드):**
```csharp
[Header("설정")]
public int viewDistance = 1;

[Header("저장 설정")]
public bool enableMemoryCache = true;
public bool enableDiskSave = false;

[Header("지층 경계 블렌딩")]
public bool enableLayerBlending = true;
[Range(0.1f, 0.9f)] public float blendingRatio = 0.4f;

[Header("Global Lighting")]
[Range(10f, 128f)] public float globalLightFalloff = 30f;

[Header("Optimization")]
[SerializeField] private int chunkSpawningBatchSize = 4;
[SerializeField] private int maxTimePerBatchMs = 6;
[SerializeField] private int maxTimePerFramePh2Ms = 5;

public int worldSeed;
private const float TEXTURE_UPDATE_INTERVAL = 0.05f;
```

**변경 후:**
```csharp
// Inspector에서 제거 — JSON에서 로드
private int viewDistance;
private bool enableMemoryCache;
private bool enableDiskSave;
private bool enableLayerBlending;
private float blendingRatio;
private float globalLightFalloff;
private int chunkSpawningBatchSize;
private int maxTimePerBatchMs;
private int maxTimePerFramePh2Ms;
private float _textureUpdateInterval;  // const에서 variable로

// Inspector에 유지 (레퍼런스)
[Header("References (Inspector)")]
public GameObject chunkPrefab;
public Transform player;
public TileVisualSettings tileVisualSettings;
```

**`InitializeCoroutine()` 앞에 설정 로드 추가:**
```csharp
IEnumerator InitializeCoroutine()
{
    // 0. JSON 설정 로드 (WorldSettingsLoader 대기)
    yield return new WaitUntil(() => WorldSettingsLoader.Instance != null);
    ApplyWorldSettings(WorldSettingsLoader.Instance.Settings);

    // 이후 기존 로직 그대로...
    InitializeWorldSeed();
    // ...
}

private void ApplyWorldSettings(WorldSettingsData s)
{
    viewDistance            = s.world.viewDistance;
    worldSeed               = s.world.seed;
    enableMemoryCache       = s.save.enableMemoryCache;
    enableDiskSave          = s.save.enableDiskSave;
    enableLayerBlending     = s.rendering.enableLayerBlending;
    blendingRatio           = s.rendering.blendingRatio;
    globalLightFalloff      = s.rendering.globalLightFalloff;
    _textureUpdateInterval  = s.rendering.textureUpdateInterval;
    chunkSpawningBatchSize  = s.performance.chunkSpawningBatchSize;
    maxTimePerBatchMs       = s.performance.maxTimePerBatchMs;
    maxTimePerFramePh2Ms    = s.performance.maxTimePerFramePh2Ms;
}
```

---

### 5-2. `TerrainChunk.cs` (Prefab)

`chunk` 섹션 설정을 `WorldSettingsLoader`에서 읽어 초기화.
`ChunkSpawner`가 청크를 꺼낼 때(`_chunkPool.Get()`) 설정을 주입하는 방식 권장:

```csharp
// ChunkSpawner.cs에서 꺼낸 직후 적용
var chunkSettings = WorldSettingsLoader.Instance.Settings.chunk;
chunk.ApplySettings(chunkSettings);
```

`TerrainChunk`에 `ApplySettings()` 메서드 추가:
```csharp
public void ApplySettings(WorldSettingsData.ChunkSection s)
{
    verticalScale           = s.verticalScale;
    useIslandRemoval        = s.useIslandRemoval;
    maxIslandSize           = s.maxIslandSize;
    solidThickness          = s.solidThickness;
    _textureThickness       = s.textureThickness;
    colliderUpdateInterval  = s.colliderUpdateInterval;
}
```

> **주의:** 풀에서 꺼낼 때마다 적용하므로 Prefab의 Inspector 기본값은 fallback 역할을 하게 됨.
> Prefab의 [SerializeField]는 **제거하지 않고 유지** — JSON 로드 실패 시 안전망.

---

### 5-3. `TEXTURE_UPDATE_INTERVAL` 처리

현재 `private const float TEXTURE_UPDATE_INTERVAL = 0.05f;`인 상수를 인스턴스 변수로 변경:

```csharp
// 변경 전
private const float TEXTURE_UPDATE_INTERVAL = 0.05f;

// 변경 후
private float _textureUpdateInterval = 0.05f; // JSON 로드 후 덮어씀

// LateUpdate에서:
if ((Time.time - _lastTextureApplyTime) >= _textureUpdateInterval)
```

---

## 6. 실행 순서 (Scene 초기화)

```
씬 시작
  │
  ├─ WorldSettingsLoader.Awake()   ← 최우선 (Script Execution Order 설정 필요)
  │    └─ worldSettings.json 읽기 + 파싱
  │
  ├─ TileDataManager.Awake()       ← 기존과 동일
  │    └─ tileData.json 읽기
  │
  └─ InfinityMapManager.Awake()
       └─ InitializeCoroutine()
            ├─ WorldSettingsLoader 대기 (WaitUntil)
            ├─ ApplyWorldSettings()
            └─ 이하 기존 초기화 로직...
```

**Script Execution Order 설정 (Edit → Project Settings → Script Execution Order):**
```
WorldSettingsLoader: -200
TileDataManager:     -100
InfinityMapManager:  (default 0)
```

---

## 7. 환경별 설정 파일 전략

JSON 파일 하나로 환경을 구분하기 어려울 경우:

```
StreamingAssets/
├── worldSettings.json          ← 기본값 (릴리즈 설정)
├── worldSettings.dev.json      ← 개발 설정 (gitignore 추가 선택)
└── tileData.json
```

개발 빌드 시 `worldSettings.dev.json`을 우선 로드하는 로직을 `WorldSettingsLoader`에 추가:

```csharp
#if UNITY_EDITOR
  string devPath = Path.Combine(Application.streamingAssetsPath, "worldSettings.dev.json");
  if (File.Exists(devPath)) { /* dev 파일 로드 */ return; }
#endif
// 기본 파일 로드
```

---

## 8. 구현 순서 (작업 단계)

| 단계 | 작업 | 파일 |
|------|------|------|
| 1 | `worldSettings.json` 파일 생성 (기본값) | `StreamingAssets/worldSettings.json` |
| 2 | `WorldSettingsData.cs` 데이터 모델 작성 | `Assets/Scripts/_Core/Data/` |
| 3 | `WorldSettingsLoader.cs` 로더 작성 | `Assets/Scripts/_Core/Data/` |
| 4 | `InfinityMapManager.cs` Inspector 필드 → private, ApplyWorldSettings() 추가 | `InfinityMapManager.cs` |
| 5 | `TerrainChunk.cs` ApplySettings() 메서드 추가 | `TerrainChunk.cs` |
| 6 | `ChunkSpawner.cs` 청크 꺼낼 때 ApplySettings() 호출 추가 | `ChunkSpawner.cs` |
| 7 | Script Execution Order 설정 | Unity Editor |
| 8 | 에디터에서 플레이 테스트 | — |

---

## 9. 주의사항

- **Inspector 필드를 즉시 제거하지 말 것** → 먼저 JSON 로드 후 override하는 방식으로 단계적 전환
- **JsonUtility 한계**: `Dictionary`, `Color32` 직접 지원 안 됨 → 단순 섹션 구조로 우회
- **`solidBorderColor`(Color32)는 이번 범위에서 제외** → RGBA int 변환 필요해서 복잡도 증가
- **`enableMemoryCache = true`는 항상 유지** → JSON에서 false로 설정해도 안전하지 않음을 주석으로 명시
- **TerrainChunk Prefab 설정은 Pool 재사용 시 매번 적용** → 리셋 누락 방지
