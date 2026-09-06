# SpecialChunk Settings JSON 시스템 설계
@tags: special-chunk, settings, JSON, configuration, SpecialChunkManager, worldSettings

> 작성일: 2026-03-12
> 목적: SpecialChunk 시스템 전반의 설정값을 `specialChunkSettings.json`으로 분리하는 설계 문서
> 연관 문서: `world-settings-json.md` (worldSettings.json 시스템과 동일한 로더 패턴 사용)

---

## 1. 목표

SpecialChunk 시스템에 흩어진 수치들을 `StreamingAssets/specialChunkSettings.json`으로 통합하여:

- **밸런싱 효율화**: 트랩 데미지/딜레이/확률을 Unity 재시작 없이 조정
- **타입별 분리**: `ScrapExplosion`, `CollapseFloor` 등 각 청크 타입의 파라미터를 명확하게 구분
- **const 하드코딩 제거**: `ScrapExplosion.cs`처럼 `private const`로 박힌 수치들을 외부로 노출

---

## 2. 설정 분류

### JSON으로 이동할 설정

#### 2-1. 스폰 제어 (SpecialChunkManager)

| 필드 | 현재 타입 | 기본값 | 설명 |
|------|-----------|--------|------|
| `minChunkSpacing` | `int` [SerializeField] | `2` | 특수 청크 간 최소 청크 단위 간격 |
| `layerBoundarySpacing` | `int` [SerializeField] | `1` | 레이어 경계에서의 여백 |
| `spawnChances` | `SpecialChunkDef.spawnChance` float | 타입마다 다름 | 타입별 스폰 확률 (0~100%) |

> **`spawnChances` 처리 전략**: Prefab 레퍼런스는 Inspector에 유지하고,
> `SpecialChunkType` enum을 키로 각 def의 `spawnChance`를 JSON 값으로 override.
> → Inspector에서 설정한 확률은 JSON이 없을 때만 fallback으로 사용.

#### 2-2. 트랩 메카닉 (Trap Mechanics)

**CollapseFloor:**
| 필드 | 기본값 | 설명 |
|------|--------|------|
| `collapseDelay` | `0.2f` | 플레이어 접촉 후 붕괴까지 대기 시간 (초) |
| `floorThicknessPx` | `200` | 제거할 바닥 픽셀 두께 |

**DelayedBlast:**
| 필드 | 기본값 | 설명 |
|------|--------|------|
| `blastDelay` | `2.0f` | 광물 채취 후 폭발까지 대기 시간 (초) |
| `blastRadius` | `0.3f` | 지형 파괴 반경 (유닛) |
| `damageRadius` | `1.5f` | 플레이어 데미지 범위 (유닛) |
| `staminaDamage` | `20.0f` | 폭발 스태미나 피해량 |

**RollingRock (RollingRockEntity):**
| 필드 | 기본값 | 설명 |
|------|--------|------|
| `launchSpeed` | `8.0f` | 초기 발사 속도 (유닛/초) |
| `clearInterval` | `0.15f` | 지형 제거 거리 임계값 (유닛) |
| `clearRadius` | `0.4f` | 지형 제거 반경 (유닛) |
| `staminaDamage` | `30.0f` | 충돌 시 스태미나 피해량 |
| `knockbackForce` | `12.0f` | 충돌 시 넉백 크기 |

**StalactiteTrap:**
| 필드 | 기본값 | 설명 |
|------|--------|------|
| `detectionRange` | `3.0f` | 플레이어 감지 레이캐스트 거리 (유닛) |
| `staminaDamage` | `20.0f` | 낙하 충돌 스태미나 피해량 |
| `slowMultiplier` | `0.6f` | 이동속도 감소 배율 (0.6 = 40% 감소) |
| `slowDuration` | `2.0f` | 이동속도 감소 지속 시간 (초) |
| `impactVibrationRadius` | `2.0f` | 충격 진동 전파 반경 (유닛) |

**ScrapExplosion (현재 전부 `private const`):**
| 상수명 | 기본값 | 설명 |
|--------|--------|------|
| `explosionRadius` | `3.0f` | 폭발 충격 반경 (유닛) |
| `staminaDamage` | `30.0f` | 폭발 스태미나 피해량 |
| `maxHp` | `30.0f` | 쓰레기 벽 최대 HP |
| `scrapMin` | `3` | 고철 최소 드롭 수 |
| `scrapMax` | `6` | 고철 최대 드롭 수 |
| `copperChance` | `0.20f` | 구리 드롭 확률 |
| `ironChance` | `0.15f` | 철 드롭 확률 |

#### 2-3. 엔티티 HP (Entity HP)

| 엔티티 | 필드 | 기본값 |
|--------|------|--------|
| `SnowmanEntity` | `maxHp` | `5.0f` |
| `CableEntity` | `maxHp` | `3.0f` |
| `TrashWallEntity` | `maxHp` | `30.0f` |
| `CrystalBlockEntity` | `maxHp` | `25.0f` |

#### 2-4. 드롭 설정 (Drop Settings)

**TrashWallDropper:**
| 필드 | 기본값 | 설명 |
|------|--------|------|
| `scrapMin` | `3` | 최소 드롭 수 |
| `scrapMax` | `5` | 최대 드롭 수 |
| `copperChance` | `0.15f` | 구리 드롭 확률 |
| `leadChance` | `0.10f` | 납 드롭 확률 |
| `scatterX` | `0.3f` | 드롭 X축 산란 범위 |
| `scatterY` | `0.2f` | 드롭 Y축 산란 범위 |

#### 2-5. 환경 효과 (Zones)

**OxidizedZone:**
| 필드 | 기본값 | 설명 |
|------|--------|------|
| `penaltyPerTick` | `5.0f` | 틱당 최대 스태미나 영구 감소량 |
| `tickInterval` | `1.0f` | 페널티 적용 간격 (초) |
| `minMaxStamina` | `20.0f` | 최대 스태미나 하한선 |

#### 2-6. 물리/비주얼 (Physics & Visuals)

| 설정 | 위치 | 기본값 | 설명 |
|------|------|--------|------|
| `icicleImpactRadius` | `IcicleHazard` | `2.0f` | 고드름 충격 진동 전파 반경 |
| `vibrationDefaultRadius` | `VibrationManager` | `3.0f` | 기본 진동 전파 반경 |
| `damageStagedThresholds` | `DamageStagedVisuals` | `[0.66, 0.33]` | HP 비율별 스프라이트 전환 기준 |

---

### Inspector에 남길 설정 (레퍼런스형)

> Unity Object 참조 — JSON으로 표현 불가

| 설정 | 파일 | 이유 |
|------|------|------|
| `prefab` (TerrainChunk) | `SpecialChunkDef` | Unity Object 레퍼런스 |
| `rockEntity`, `ceilingChunk` | `RollingRockTrap` | 씬 레퍼런스 |
| `effectComponents` | `CollapseFloor` | MonoBehaviour 리스트 |
| `explosionVFXPrefab`, `gasParticlePrefab` | `DelayedBlast` | Prefab 레퍼런스 |
| `warningVFX`, `collapseVFX` | `VFXCollapseEffect` | GameObject 레퍼런스 |
| `warningSound`, `collapseSound` | `AudioCollapseEffect` | AudioClip 레퍼런스 |
| `hitParticle`, `sparkParticle`, `shardParticle` | 각 VFX 클래스 | ParticleSystem 레퍼런스 |
| `hitSFX`, `destroySFX`, `triggerSFX` | 각 클래스 | AudioClip 레퍼런스 |
| `stages` (Sprite[]) | `DamageStagedVisuals` | Sprite 레퍼런스 |
| `lootTable` | `CrystalBlockDropper` | ScriptableObject 레퍼런스 |
| `playerLayer`, `destroyItemLayer` | `DelayedBlast`, `StalactiteTrap` | LayerMask (int로 직렬화 가능하나 Inspector가 더 안전) |
| `connectedTarget` | `CableEntity` | Transform 레퍼런스 |
| `blockPrefab` | `TrashWallGenerator` | Prefab 레퍼런스 |

### 코드 상수 유지

| 상수 | 파일 | 이유 |
|------|------|------|
| `HIT_COOLDOWN = 0.2f` | Snowman/Cable/TrashWall/Crystal | 히트 중복 방지 — 프레임레이트 의존적 |
| `hp 대화 기준 0.75/0.5/0.25` | `SnowmanEntity` | 내러티브 트리거, 밸런스 무관 |
| `73856093` 등 hash 상수 | `SpecialChunkSelector` | 결정론적 해시 알고리즘 상수 |
| `COORD_UNINIT`, `MAX_COORD` | 공통 | 경계 센티널 |
| `toolIndex == 2` 체크 | TrashWall/Crystal | 도구 인덱스 정의는 별도 시스템 |

---

## 3. JSON 파일 구조

**파일 위치:** `Assets/StreamingAssets/specialChunkSettings.json`

```json
{
  "spawning": {
    "minChunkSpacing": 2,
    "layerBoundarySpacing": 1,
    "spawnChances": {
      "ScrapExplosion": 15.0,
      "DelayedBlast": 20.0,
      "CollapseFloor": 25.0,
      "GuideLine": 30.0,
      "Pitfall": 20.0,
      "DropSpike": 15.0
    }
  },
  "traps": {
    "collapseFloor": {
      "collapseDelay": 0.2,
      "floorThicknessPx": 200
    },
    "delayedBlast": {
      "blastDelay": 2.0,
      "blastRadius": 0.3,
      "damageRadius": 1.5,
      "staminaDamage": 20.0
    },
    "rollingRock": {
      "launchSpeed": 8.0,
      "clearInterval": 0.15,
      "clearRadius": 0.4,
      "staminaDamage": 30.0,
      "knockbackForce": 12.0
    },
    "stalactite": {
      "detectionRange": 3.0,
      "staminaDamage": 20.0,
      "slowMultiplier": 0.6,
      "slowDuration": 2.0,
      "impactVibrationRadius": 2.0
    },
    "scrapExplosion": {
      "explosionRadius": 3.0,
      "staminaDamage": 30.0,
      "maxHp": 30.0,
      "scrapMin": 3,
      "scrapMax": 6,
      "copperChance": 0.20,
      "ironChance": 0.15
    }
  },
  "entities": {
    "snowman":      { "maxHp": 5.0  },
    "cable":        { "maxHp": 3.0  },
    "trashWall":    { "maxHp": 30.0 },
    "crystalBlock": { "maxHp": 25.0 }
  },
  "drops": {
    "trashWall": {
      "scrapMin": 3,
      "scrapMax": 5,
      "copperChance": 0.15,
      "leadChance": 0.10,
      "scatterX": 0.3,
      "scatterY": 0.2
    }
  },
  "zones": {
    "oxidized": {
      "penaltyPerTick": 5.0,
      "tickInterval": 1.0,
      "minMaxStamina": 20.0
    }
  },
  "physics": {
    "icicleImpactRadius": 2.0,
    "vibrationDefaultRadius": 3.0,
    "damageStagedThresholds": [ 0.66, 0.33 ]
  }
}
```

---

## 4. 새로 만들 파일

### 4-1. `SpecialChunkSettingsData.cs` — 데이터 모델

**위치:** `Assets/Scripts/_Core/Data/SpecialChunkSettingsData.cs`

```csharp
[System.Serializable]
public class SpecialChunkSettingsData
{
    public SpawningSection spawning   = new SpawningSection();
    public TrapsSection    traps      = new TrapsSection();
    public EntitiesSection entities   = new EntitiesSection();
    public DropsSection    drops      = new DropsSection();
    public ZonesSection    zones      = new ZonesSection();
    public PhysicsSection  physics    = new PhysicsSection();

    // --- Spawning ---
    [System.Serializable]
    public class SpawningSection
    {
        public int minChunkSpacing      = 2;
        public int layerBoundarySpacing = 1;
        // key: SpecialChunkType 이름(string), value: 확률(float 0~100)
        // JsonUtility는 Dictionary 미지원 → List로 대체
        public List<SpawnChanceEntry> spawnChances = new List<SpawnChanceEntry>();
    }
    [System.Serializable]
    public class SpawnChanceEntry
    {
        public string chunkTypeName; // SpecialChunkType.ToString()과 매칭
        public float  chance;        // 0 ~ 100
    }

    // --- Traps ---
    [System.Serializable]
    public class TrapsSection
    {
        public CollapseFloorData  collapseFloor  = new CollapseFloorData();
        public DelayedBlastData   delayedBlast   = new DelayedBlastData();
        public RollingRockData    rollingRock    = new RollingRockData();
        public StalactiteData     stalactite     = new StalactiteData();
        public ScrapExplosionData scrapExplosion = new ScrapExplosionData();
    }
    [System.Serializable] public class CollapseFloorData
    {
        public float collapseDelay    = 0.2f;
        public int   floorThicknessPx = 200;
    }
    [System.Serializable] public class DelayedBlastData
    {
        public float blastDelay    = 2.0f;
        public float blastRadius   = 0.3f;
        public float damageRadius  = 1.5f;
        public float staminaDamage = 20.0f;
    }
    [System.Serializable] public class RollingRockData
    {
        public float launchSpeed    = 8.0f;
        public float clearInterval  = 0.15f;
        public float clearRadius    = 0.4f;
        public float staminaDamage  = 30.0f;
        public float knockbackForce = 12.0f;
    }
    [System.Serializable] public class StalactiteData
    {
        public float detectionRange        = 3.0f;
        public float staminaDamage         = 20.0f;
        public float slowMultiplier        = 0.6f;
        public float slowDuration          = 2.0f;
        public float impactVibrationRadius = 2.0f;
    }
    [System.Serializable] public class ScrapExplosionData
    {
        public float explosionRadius = 3.0f;
        public float staminaDamage   = 30.0f;
        public float maxHp           = 30.0f;
        public int   scrapMin        = 3;
        public int   scrapMax        = 6;
        public float copperChance    = 0.20f;
        public float ironChance      = 0.15f;
    }

    // --- Entities ---
    [System.Serializable]
    public class EntitiesSection
    {
        public EntityHpData snowman      = new EntityHpData { maxHp = 5f  };
        public EntityHpData cable        = new EntityHpData { maxHp = 3f  };
        public EntityHpData trashWall    = new EntityHpData { maxHp = 30f };
        public EntityHpData crystalBlock = new EntityHpData { maxHp = 25f };
    }
    [System.Serializable] public class EntityHpData { public float maxHp; }

    // --- Drops ---
    [System.Serializable]
    public class DropsSection
    {
        public TrashWallDropData trashWall = new TrashWallDropData();
    }
    [System.Serializable] public class TrashWallDropData
    {
        public int   scrapMin     = 3;
        public int   scrapMax     = 5;
        public float copperChance = 0.15f;
        public float leadChance   = 0.10f;
        public float scatterX     = 0.3f;
        public float scatterY     = 0.2f;
    }

    // --- Zones ---
    [System.Serializable]
    public class ZonesSection
    {
        public OxidizedZoneData oxidized = new OxidizedZoneData();
    }
    [System.Serializable] public class OxidizedZoneData
    {
        public float penaltyPerTick  = 5.0f;
        public float tickInterval    = 1.0f;
        public float minMaxStamina   = 20.0f;
    }

    // --- Physics / Visuals ---
    [System.Serializable]
    public class PhysicsSection
    {
        public float  icicleImpactRadius    = 2.0f;
        public float  vibrationDefaultRadius= 3.0f;
        public float[] damageStagedThresholds = new[] { 0.66f, 0.33f };
    }
}
```

> **`JsonUtility` Dictionary 미지원 문제**: `spawnChances`는 `List<SpawnChanceEntry>`로 직렬화.
> 런타임 로드 시 `Dictionary<string, float>`로 변환해 O(1) 조회.

---

### 4-2. `SpecialChunkSettingsLoader.cs` — 로더

**위치:** `Assets/Scripts/_Core/Data/SpecialChunkSettingsLoader.cs`

`WorldSettingsLoader`와 동일한 싱글톤 패턴.

```
Awake()
  → StreamingAssets/specialChunkSettings.json 읽기
  → JsonUtility.FromJson<SpecialChunkSettingsData>()
  → 파싱 실패 시 new SpecialChunkSettingsData() (기본값)
  → spawnChances List → Dictionary 변환
  → Instance에 저장
```

**Script Execution Order 추가:**
```
SpecialChunkSettingsLoader: -150   (WorldSettingsLoader:-200 이후, SpecialChunkManager 이전)
```

---

## 5. 수정할 파일

### 5-1. SpecialChunkManager.cs — 스폰 설정 + spawnChance override

```csharp
void Start()
{
    // ... 기존 초기화 ...
    ApplySpecialChunkSettings();
}

private void ApplySpecialChunkSettings()
{
    if (SpecialChunkSettingsLoader.Instance == null) return;
    var s = SpecialChunkSettingsLoader.Instance.Settings;

    // 스폰 간격
    minChunkSpacing      = s.spawning.minChunkSpacing;
    layerBoundarySpacing = s.spawning.layerBoundarySpacing;

    // spawnChance override: SpecialChunkType 이름으로 매칭
    var chanceMap = s.spawning.GetSpawnChanceDict(); // List → Dict 변환
    foreach (var pool in pools)
        foreach (var def in pool.chunks)
        {
            string key = def.chunkType.ToString();
            if (chanceMap.TryGetValue(key, out float chance))
                def.spawnChance = chance;
        }
}
```

### 5-2. 트랩/엔티티 컴포넌트들 — ApplySettings 패턴

각 컴포넌트의 `Start()` 또는 `Awake()`에 아래 패턴 적용:

```csharp
// CollapseFloor.cs 예시
void Start()
{
    if (SpecialChunkSettingsLoader.Instance != null)
    {
        var s = SpecialChunkSettingsLoader.Instance.Settings.traps.collapseFloor;
        collapseDelay     = s.collapseDelay;
        floorThicknessPx  = s.floorThicknessPx;
    }
    // ... 기존 로직 ...
}
```

**ScrapExplosion.cs — const → 필드 변환 필요:**

```csharp
// 변경 전
private const float ExplosionRadius = 3f;
private const float StaminaDamage   = 30f;

// 변경 후
private float _explosionRadius = 3f;   // JSON override 가능
private float _staminaDamage   = 30f;

void Awake()
{
    if (SpecialChunkSettingsLoader.Instance != null)
    {
        var s = SpecialChunkSettingsLoader.Instance.Settings.traps.scrapExplosion;
        _explosionRadius = s.explosionRadius;
        _staminaDamage   = s.staminaDamage;
        // ... 나머지 필드 ...
    }
}
```

### 5-3. 수정 파일 목록

| 파일 | 변경 내용 |
|------|-----------|
| `SpecialChunkManager.cs` | `ApplySpecialChunkSettings()` 추가, minChunkSpacing/layerBoundarySpacing JSON에서 로드 |
| `CollapseFloor.cs` | `Start()`에서 JSON 설정 적용 |
| `DelayedBlast.cs` | `Awake()`에서 JSON 설정 적용 |
| `RollingRockEntity.cs` | `Awake()`에서 JSON 설정 적용 |
| `StalactiteTrap.cs` | `Awake()`에서 JSON 설정 적용 |
| `ScrapExplosion.cs` | `const` → `private field` 변환 후 `Awake()`에서 적용 |
| `SnowmanEntity.cs` | `maxHp` JSON에서 로드 |
| `CableEntity.cs` | `maxHp` JSON에서 로드 |
| `TrashWallEntity.cs` | `maxHp` JSON에서 로드 |
| `CrystalBlockEntity.cs` | `maxHp` JSON에서 로드 |
| `TrashWallDropper.cs` | 드롭 수치 JSON에서 로드 |
| `OxidizedZone.cs` | 페널티 수치 JSON에서 로드 |
| `IcicleHazard.cs` | `impactRadius` JSON에서 로드 |
| `VibrationManager.cs` | `defaultImpactRadius` JSON에서 로드 |
| `DamageStagedVisuals.cs` | HP 기준 `0.66/0.33` → JSON `damageStagedThresholds` 배열로 |

---

## 6. 실행 순서

```
씬 시작
  │
  ├─ WorldSettingsLoader.Awake()             (Order: -200)
  ├─ SpecialChunkSettingsLoader.Awake()      (Order: -150)
  │    └─ specialChunkSettings.json 읽기 + 파싱
  │
  ├─ SpecialChunkManager.Start()
  │    └─ ApplySpecialChunkSettings()        ← JSON override
  │
  ├─ CollapseFloor/DelayedBlast/etc.Start()
  │    └─ 각자 loader에서 설정 읽기
  │
  └─ InfinityMapManager.InitializeCoroutine()
```

---

## 7. 구현 순서 (작업 단계)

| 단계 | 작업 | 파일 |
|------|------|------|
| 1 | `specialChunkSettings.json` 파일 생성 (기본값) | `StreamingAssets/` |
| 2 | `SpecialChunkSettingsData.cs` 작성 | `Assets/Scripts/_Core/Data/` |
| 3 | `SpecialChunkSettingsLoader.cs` 작성 | `Assets/Scripts/_Core/Data/` |
| 4 | Script Execution Order 등록 (-150) | Unity Editor |
| 5 | `SpecialChunkManager.cs` ApplySettings 추가 | 스폰 설정 + spawnChance |
| 6 | 트랩 4종 수정 (Collapse/DelayedBlast/RollingRock/Stalactite) | 각 Traps 파일 |
| 7 | `ScrapExplosion.cs` const → field 전환 | ScrapExplosion.cs |
| 8 | 엔티티 4종 maxHp 수정 | Snowman/Cable/TrashWall/Crystal |
| 9 | 드롭/존/물리 수정 (Dropper/OxidizedZone/Vibration/Icicle/DamageStaged) | 각 파일 |
| 10 | 에디터 플레이 테스트 | — |

---

## 8. worldSettings.json과의 분리 이유

| 항목 | worldSettings.json | specialChunkSettings.json |
|------|-------------------|--------------------------|
| 담당 시스템 | 청크 생성 인프라 | 트랩/던전 콘텐츠 밸런싱 |
| 수정 담당자 | 프로그래머 (성능 튜닝) | 디자이너 (게임플레이 밸런싱) |
| 변경 빈도 | 낮음 | 높음 (밸런스 패치마다) |
| 파일 크기 | 소형 | 중형 (타입별 섹션) |

두 파일을 합치면 한 파일이 너무 커지고, 담당 도메인이 달라 책임 분리 원칙에도 어긋남.

---

## 9. 주의사항

- **DamageStagedVisuals thresholds 배열**: `JsonUtility`는 `float[]` 직렬화 지원 — 배열 크기가 정확히 2개여야 함. 길이 체크 후 기본값 fallback 필요
- **ScrapExplosion const 제거**: `const`를 `private field`로 바꾸면 기존 컴파일 최적화가 사라지지만 규모상 무시 가능
- **spawnChance List→Dict 변환**: `SpecialChunkSettingsLoader.Awake()`에서 한 번만 수행, 이후 캐시된 Dict 사용
- **Inspector [SerializeField] 즉시 제거 금지**: 단계적 전환 — JSON 로드 후 override하고, JSON 없으면 Inspector 값 그대로 사용
- **풀에서 꺼낸 컴포넌트 재적용**: 오브젝트 풀링 시 컴포넌트가 재사용될 때 JSON 설정이 다시 적용되는지 확인 필요 (특히 `TrashWallEntity`, `CrystalBlockEntity`)
