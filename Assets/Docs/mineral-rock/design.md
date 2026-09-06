# 발광 광물돌 (Mineral Rock) 설계

작성일: 2026-06-28

## 목표

코어키퍼의 광물돌처럼, **특정 광물 전용 + 어두운 동굴에서 파티클로 자체발광**하는 채굴 가능한 돌을 만든다.
기존 `DiggableRock`(HP·노출·콜라이더·복원·파괴)을 **재사용**하고, 광물돌 고유 동작은 별도 컴포넌트로 합성한다.

## 확정 결정사항

| 항목 | 결정 |
|------|------|
| 발광 트리거 | **항상 발광** (Additive/Unlit 스파클 → 어두운 배경에서 자연 도드라짐). 광량 감지 로직 없음 |
| 정체성/드롭 | **특정 광물 전용** (1 광물돌 = 1 광물 확정 드롭) |
| 발광색 | **각 광물돌 프리팹에 직접 설정** (`MineralRockGlow.glowColor` SerializeField). MineralSO 무수정 |
| 스폰 방식 | **특수청크에 프리팹 직접 배치** (절차적 스폰 없음, 사용자가 수동 배치) |
| 아키텍처 | **A안: DiggableRock 재사용 + 컴포넌트 합성** (DRY) |
| HP·드롭 개수 | **specialChunkSettings.json에서 광물별 조정** (재빌드 불필요) |

## 아키텍처

### 컴포넌트 구성 (광물돌 프리팹)

| 컴포넌트 | 책임 | 신규/기존 |
|---|---|---|
| `DiggableRock` | HP·노출·콜라이더·복원·파괴 | 기존 (드롭 훅 1곳 수정) |
| `DamageStagedVisuals` | 균열 단계 비주얼 | 기존 |
| `RockBreakVFX` | 파괴 조각 비산 | 기존 |
| **`MineralRock`** | 정체성(어떤 광물) + 드롭 오버라이드 + JSON 설정 적용 | **신규** |
| **`MineralRockGlow`** | 항상 발광 스파클 파티클 구동 | **신규** |

### 드롭 오버라이드 훅 (DiggableRock 수정)

신규 인터페이스 (`_Core/Interfaces/IRockDropOverride.cs`):

```csharp
public interface IRockDropOverride
{
    /// <summary>드롭을 처리했으면 true. true면 DiggableRock의 기본 랜덤 드롭을 건너뛴다.</summary>
    bool TryDropOnDestroy(Vector3 rockCenter);
}
```

`DiggableRock.DestroyRock()` 내부, 기존 `DropRareMinerals()` 호출부를 다음으로 교체:

```csharp
var dropOverride = GetComponent<IRockDropOverride>();
if (dropOverride == null || !dropOverride.TryDropOnDestroy(_spriteRenderer.bounds.center))
    DropRareMinerals();   // 오버라이드 없으면 기존 동작 100% 유지
```

광물 인스턴스화 로직(`Instantiate(prefab) + MineralLifetime`)은 신규 static 헬퍼로 추출해
`DiggableRock.TryDropMineral`과 `MineralRock` 양쪽이 공유한다 (DRY):

```csharp
// MineralDropHelper.cs (static)
public static class MineralDropHelper
{
    public static void Drop(MineralSO so, Vector3 worldPos);          // null 가드 + 경고 포함
    public static void Drop(MineralID id, Vector3 worldPos);          // MineralDatabase 조회 후 위 호출
}
```

### MineralRock 컴포넌트

```
[DefaultExecutionOrder(-140)]   // 로더(-150) 다음, DiggableRock.OnEnable 전에 실행
public class MineralRock : MonoBehaviour, IRockDropOverride
```

- 필드: `[SerializeField] MineralSO ore;`
- 런타임 캐시: `int _minDrop, _maxDrop;`
- (발광색은 `MineralRockGlow`가 자체 보유 — MineralRock은 정체성·드롭만 담당)
- `Awake()`:
  1. `SpecialChunkSettingsLoader.Instance.Settings.mineralRock`에서 `ore.mineralID`로 설정 조회 (없으면 default)
  2. `GetComponent<DiggableRock>().MaxHp = entry.maxHp;` — DiggableRock.OnEnable이 이후 `_currentHp = MaxHp`로 초기화하므로 순서 안전
  3. `_minDrop = entry.minDrop; _maxDrop = entry.maxDrop;`
  - 로더/설정 null 이면 default 값으로 폴백
- `TryDropOnDestroy(center)`:
  - `ore == null` → `false` 반환 (기본 드롭으로 폴백)
  - `int count = Random.Range(_minDrop, _maxDrop + 1);`
  - `count`회 `MineralDropHelper.Drop(ore, center + 소량 산란)` 호출 → `true` 반환

### MineralRockGlow 컴포넌트

```csharp
public class MineralRockGlow : MonoBehaviour
```

- `[SerializeField] ParticleSystem sparkleSystem;` (자식, 프리팹에서 에디터 설정)
- `[SerializeField] Color glowColor = new Color(0.4f, 0.9f, 1f, 1f);` (**광물돌별 직접 설정** — 발광색 단일 소스)
- `Start()`: `main.startColor = glowColor`
- **가시성 동기화**: `SpriteRenderer.enabled == true`(실제로 보일 때)만 emission ON. 안 보이면 OFF.
  - `IsRevealed`가 아니라 `enabled`를 쓰는 이유: `preExposed=true` 잼바위는 `RevealInTerrain()`을 거치지 않아 `_isRevealed`가 false로 남지만 렌더러는 켜져 있다. `enabled` 기준이면 preExposed/파서노출/복원 세 경로 모두 일관 동작
  - 매뉴얼 배치(`preExposed=true`, 땅에서 살짝 튀어나온 형태)면 렌더러 켜짐 → 즉시 ON
  - `Update()`에서 `enabled` 변화 감지해 `sparkleSystem` Play/Stop 토글 (cheap 비교)
- `OnEnable/OnDisable`: 풀 안전을 위해 Stop/리셋
- 코어키퍼 트윙클 파라미터 (파티클 시스템 에디터 설정):
  - Emission rate ≈ 5/s
  - Shape: Sprite (돌 스프라이트 전체에 산란) 또는 Box(돌 바운즈)
  - Start Size 0.04~0.1, Start Lifetime 0.4~0.8
  - Color over Lifetime: alpha 0→1→0 (깜빡임)
  - Size over Lifetime: 0→1→0 (페이드)
  - Renderer Material: **Additive/Unlit** (자체발광)

### MineralSO

수정하지 않는다. 발광색은 각 광물돌 프리팹의 `MineralRockGlow.glowColor`에 직접 설정한다.

## JSON 조정 (specialChunkSettings.json)

### 신설 섹션

```json
"mineralRock": {
  "defaultMaxHp": 8.0,
  "defaultMinDrop": 2,
  "defaultMaxDrop": 4,
  "overrides": [
    { "mineralID": "Copper", "maxHp": 6.0,  "minDrop": 2, "maxDrop": 4 },
    { "mineralID": "Iron",   "maxHp": 10.0, "minDrop": 1, "maxDrop": 3 }
  ]
}
```

### SpecialChunkSettingsData.cs 대응 클래스

```csharp
public MineralRockSection mineralRock = new MineralRockSection();

[System.Serializable]
public class MineralRockSection
{
    public float defaultMaxHp   = 8f;
    public int   defaultMinDrop = 2;
    public int   defaultMaxDrop = 4;
    public List<MineralRockEntry> overrides = new List<MineralRockEntry>();

    private Dictionary<string, MineralRockEntry> _dict;
    // spawnChances와 동일 패턴: List → Dictionary 1회 변환
    public MineralRockEntry Get(string mineralID) { /* dict 조회, 없으면 null */ }
}

[System.Serializable]
public class MineralRockEntry
{
    public string mineralID;
    public float  maxHp;
    public int    minDrop;
    public int    maxDrop;
}
```

`MineralRock.Awake()`에서 `mineralRock.Get(ore.mineralID.ToString())` 결과가 null이면
`defaultMaxHp/defaultMinDrop/defaultMaxDrop`을 사용한다.

## 프리팹 배치 (특수청크, 직접 배치)

CLAUDE.md 제약 준수:

- **제약 #9**: `preExposed = true`, `minExposedPixels = 0`, **Rigidbody2D 없음**
- **제약 #8**: Project 창에서 특수청크 프리팹을 **Prefab Edit 모드로 열어** 자식 배치
  (씬 인스턴스에 드래그 후 Apply 금지 — local position 틀어짐)
  - 청크 1칸 = 10유닛, 자식 local position 유효 범위 X(0~10), Y(0~10)

### 셋업 산출물

- Additive 파티클 머티리얼 1개 (`Assets/Material/` 또는 `Assets/Prefabs/VFX/`)
- 광물돌 프리팹 (DiggableRock + DamageStagedVisuals + RockBreakVFX + MineralRock + MineralRockGlow + 자식 ParticleSystem)
- 에디터 셋업 가이드 (`Assets/Docs/mineral-rock/prefab-setup.md`)

## 파일 변경 요약

**신규**
- `Assets/Scripts/_Core/Interfaces/IRockDropOverride.cs`
- `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralRock.cs`
- `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralRockGlow.cs`
- `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralDropHelper.cs`
- Additive 파티클 머티리얼 / 광물돌 프리팹 / `prefab-setup.md`

**수정**
- `DiggableRock.cs` — DestroyRock 드롭 훅 + TryDropMineral을 MineralDropHelper로 위임
- `SpecialChunkSettingsData.cs` — `MineralRockSection`/`MineralRockEntry` 추가
- `Assets/StreamingAssets/specialChunkSettings.json` — `mineralRock` 섹션 추가

## 테스트

- **EditMode** (테스트 파일만 작성, 실행은 사람이 직접 — CLAUDE.md):
  - `MineralRock.TryDropOnDestroy`가 `[minDrop, maxDrop]` 범위 개수만큼 인스턴스화 + `true` 반환
  - `IRockDropOverride` 미부착 시 DiggableRock 기존 랜덤 드롭 유지
  - `MineralRockSection.Get()` — 오버라이드 적중/미적중(default 폴백) 분기
- **수동 플레이**: 어두운 동굴에서 발광 가시성, 채굴 시 확정 드롭 개수, JSON 값 변경 반영

## 비고 / 추후

- 절차적 스폰(깊이별 자동 배치)은 이번 범위 외 — 별도 작업으로 분리
- 실제 조명용 URP 2D Point Light 부착은 선택적 폴리시 (이번 범위 외, 파티클 발광이 1차)
