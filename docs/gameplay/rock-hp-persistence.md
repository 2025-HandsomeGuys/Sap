# 암석 HP 영속성 & 회복 시스템 구현 계획
@tags: rock, DiggableRock, HP, persistence, recovery, save, plan

> 작성일: 2026-03-20
> 상태: 확정 (구현 준비 완료)

---

## 목표 동작

| 항목 | 동작 |
|------|------|
| HP 저장 | 청크 언로드 시 현재 HP 저장 |
| HP 복원 | 청크 재로드 시 저장된 HP 그대로 복원 |
| HP 회복 | 마지막 데미지로부터 N초 경과 시 초당 선형 회복 |
| 회복 조건 | 청크가 **로드된 상태**에서만 대기 시간 카운트·회복 진행 |
| isRevealed 저장 | 명시적으로 저장·복원 |
| 시간 기준 | `Time.time` (세션 내 게임 시간) |
| 디스크 저장 | 불필요 (메모리 캐시만) |

---

## DirtPatch 시스템 정리 (선행 작업)

### 현황: 완전한 데드 코드

코드를 분석한 결과 DirtPatch 시스템은 **파이프라인에 연결되지 않은 상태**임.

| 코드 | 상태 |
|------|------|
| `RockSpawner.SpawnDirtPatch()` | 정의만 있고 **호출하는 곳 없음** |
| `GenerateRocks(dirtPatchPrefab:)` | 파라미터 받지만 **실제로 사용 안 함** |
| `DiggableRock.associatedDirtPatch` | 항상 null |
| `DirtPatch.cs`, `HiddenRock.cs` | 클래스 존재하지만 스폰 안 됨 |
| `SapStrategy`의 IDirtDiggable 감지 | 대상 오브젝트 없으므로 무효 |

### 정리 내용

DirtPatch를 현재 사용하지 않으므로 아래 코드들을 제거·정리한다.

**제거 대상:**
- `RockSpawner.SpawnDirtPatch()` 메서드
- `RockSpawner._dirtPatchPools` Dictionary
- `RockSpawner.ReturnDirtPatchToPool()` 메서드
- `GenerateRocks()`의 `dirtPatchPrefab` 파라미터
- `RockDecorator`에서 `dirtPatchPrefab: visualData.dirtPatchPrefab` 전달 부분
- `TileVisualSettings.RockSpriteSet.dirtPatchPrefab` 필드
- `DiggableRock.associatedDirtPatch` 필드
- `DiggableRock.DestroyRock()`에서 DirtPatch 반납 블록
- `DiggableRock.Reveal()` 메서드 (DirtPatch에서만 호출됨)
- `DirtPatch.cs` 파일
- `HiddenRock.cs` 파일
- `SapStrategy`에서 `IDirtDiggable` 감지 블록
- `IDirtDiggable` 인터페이스 파일

**isRevealed에 대한 영향:**
DirtPatch가 없으므로 암석은 항상 `RevealInTerrain()` 경로로 노출됨.
저장 시 `isRevealed`는 실질적으로 항상 `true` (노출된 암석만 SpawnedRocks에 남아있음).
명시적 저장을 유지하면 향후 DirtPatch 재도입 시에도 대응 가능.

---

## 현재 버그 분석

### 버그: HP가 재로드 시 초기화됨

**원인 1: RockSaveEntry에 HP 없음**
```
RockSaveEntry { boundsX, boundsY, boundsW, boundsH, spriteSetIndex }
← savedHp, lastDamageTime, isRevealed 없음
```

**원인 2: Start()에 HP 초기화 코드 중복 존재**
```csharp
// DiggableRock.Start()
_currentHp = MaxHp;  ← 이게 RestoreState() 이후 실행되면 덮어씀
```

`OnEnable()`에도 동일한 초기화가 있어서 중복. `Start()`의 것을 제거해야 함.

**타이밍 문제 (신규 Instantiate 케이스):**
```
Instantiate() → Awake() → OnEnable() [MaxHp 초기화]
              → RestoreRocks() [RestoreState() → savedHp 설정]
              → Start() [_currentHp = MaxHp 덮어씀 ← 버그]
```

→ `Start()`의 `_currentHp = MaxHp`를 제거하면 해결.

---

## 수정 대상 파일

| 파일 | 수정 내용 |
|------|-----------|
| `_Core/Data/ChunkSaveData.cs` | `RockSaveEntry`에 필드 3개 추가 |
| `Decoration/DiggableRock.cs` | HP 회복 로직, RestoreState(), getter, Start() 수정 |
| `Core/WorldPersistenceSystem.cs` | 저장 시 새 필드 채우기 |
| `Decoration/TerrainDecorator.cs` | `RestoreRocks()`에서 `RestoreState()` 호출 |
| `Decoration/RockSpawner.cs` | DirtPatch 관련 코드 제거 |
| `Decoration/Decorators/RockDecorator.cs` | dirtPatchPrefab 파라미터 제거 |
| `_Core/Data/TileVisualSettings.cs` | `dirtPatchPrefab` 필드 제거 |
| `UI/Items/DirtPatch.cs` | 파일 삭제 |
| `UI/Items/HiddenRock.cs` | 파일 삭제 |
| `Player/Strategies/SapStrategy.cs` | IDirtDiggable 감지 블록 제거 |
| `Gameplay/Terrain/Tiles/Digger.cs` | IDirtDiggable 관련 코드 제거 (있으면) |
| `IDirtDiggable.cs` (인터페이스) | 파일 삭제 |

---

## 구현 상세

### STEP 1: DirtPatch 코드 정리

위 제거 대상 목록 참고. 인터페이스 제거 전에 구현체 참조를 모두 제거할 것.

---

### STEP 2: RockSaveEntry 필드 추가

**파일:** `Assets/Scripts/_Core/Data/ChunkSaveData.cs`

```csharp
[System.Serializable]
public class RockSaveEntry
{
    public int boundsX;
    public int boundsY;
    public int boundsW;
    public int boundsH;
    public int spriteSetIndex;

    // ── 신규 ──
    public float savedHp;         // 언로드 시점의 현재 HP
    public float lastDamageTime;  // 언로드 시점의 Time.time (마지막 데미지 기준)
    public bool  isRevealed;      // 노출된 상태였는지
}
```

---

### STEP 3: DiggableRock.cs 수정

#### 3-1. 새 필드·설정값 추가

```csharp
[Header("HP Recovery")]
[Tooltip("마지막 데미지 후 회복이 시작되기까지 대기 시간 (초)")]
public float recoveryDelay = 10f;

[Tooltip("회복 시작 후 초당 HP 회복량")]
public float recoveryRate = 1f;   // MaxHp=5 기준 → 5초면 완전 회복

private float _lastDamageTime = float.NegativeInfinity;
```

#### 3-2. 저장용 Public Getter 추가

```csharp
public float CurrentHp      => _currentHp;
public float LastDamageTime => _lastDamageTime;
public bool  IsRevealed     => _isRevealed;
```

#### 3-3. Start()에서 HP 초기화 제거

```csharp
private void Start()
{
    InitTexture();
    // ← _currentHp = MaxHp 제거 (OnEnable에서 처리, 중복 + 타이밍 버그)

    if (selfRegister) { /* 기존 유지 */ }

    if (!preExposed && !_isRevealed)
        RevealInTerrain();
    // _isRevealed=true이면 RestoreState()에서 이미 콜라이더 활성화 → collider.enabled 체크로 스킵
}
```

#### 3-4. OnEnable()에 _lastDamageTime 초기화 추가

```csharp
private void OnEnable()
{
    if (_stageListeners != null)
    {
        _currentHp = MaxHp;
        _lastDamageTime = float.NegativeInfinity;  // ← 추가
        foreach (var s in _stageListeners) s.OnHpRatioChanged(1f);
    }

    if (!preExposed && !_isRevealed)
    {
        if (_polyCollider != null) _polyCollider.enabled = false;
        if (_spriteRenderer != null) _spriteRenderer.enabled = false;
    }
}
```

*`_lastDamageTime = -∞` 시 `Time.time - (-∞) = ∞ > recoveryDelay`이지만,
`_currentHp >= MaxHp` 가드가 있어서 Update에서 회복 루프 진입하지 않음. 문제없음.*

#### 3-5. Dig()에서 _lastDamageTime 갱신

```csharp
public void Dig(Vector2 worldPos, float damage, int toolIndex)
{
    if (toolIndex != 2 && toolIndex != 3) return;

    _currentHp -= damage;
    _lastDamageTime = Time.time;  // ← 추가

    /* 기존 비주얼·파괴 로직 유지 */
}
```

#### 3-6. Update() 추가 — HP 선형 회복

```csharp
private void Update()
{
    if (!_isRevealed) return;           // 미노출 암석은 회복 대상 아님
    if (_currentHp >= MaxHp) return;    // 이미 최대 HP

    // 마지막 데미지 후 recoveryDelay가 지나야 회복 시작
    if (Time.time - _lastDamageTime < recoveryDelay) return;

    _currentHp = Mathf.Min(_currentHp + recoveryRate * Time.deltaTime, MaxHp);

    float ratio = _currentHp / MaxHp;
    foreach (var listener in _stageListeners)
        listener.OnHpRatioChanged(ratio);
}
```

#### 3-7. RestoreState() 메서드 추가

```csharp
/// <summary>
/// 청크 재로드 시 TerrainDecorator.RestoreRocks()에서 호출.
/// OnEnable()의 초기화를 덮어쓰고 저장된 상태를 복원한다.
/// Start() 이전/이후 모두 안전하게 동작 (Start()의 HP 초기화가 제거되어 있으므로).
/// </summary>
public void RestoreState(float savedHp, float savedLastDamageTime, bool savedIsRevealed)
{
    _currentHp      = savedHp;
    _lastDamageTime = savedLastDamageTime;
    _isRevealed     = savedIsRevealed;

    // 비주얼 동기화 (크랙 단계 복원)
    float ratio = Mathf.Max(_currentHp, 0f) / MaxHp;
    if (_stageListeners != null)
        foreach (var s in _stageListeners) s.OnHpRatioChanged(ratio);

    // 노출 상태면 콜라이더·렌더러 활성화
    // Start()의 RevealInTerrain()이 collider.enabled 체크로 중복 실행 스킵
    if (savedIsRevealed)
    {
        if (_polyCollider != null)   _polyCollider.enabled   = true;
        if (_spriteRenderer != null) _spriteRenderer.enabled = true;
    }
}
```

---

### STEP 4: WorldPersistenceSystem.cs 수정

`SaveChunkDataToMemory()` 내 RockSaveEntry 생성 부분:

```csharp
rockEntries[validCount++] = new RockSaveEntry
{
    boundsX        = dr.PixelBoundsInChunk.x,
    boundsY        = dr.PixelBoundsInChunk.y,
    boundsW        = dr.PixelBoundsInChunk.width,
    boundsH        = dr.PixelBoundsInChunk.height,
    spriteSetIndex = dr.spriteSetIndex,

    // ── 신규 ──
    savedHp        = dr.CurrentHp,
    lastDamageTime = dr.LastDamageTime,
    isRevealed     = dr.IsRevealed,
};
```

---

### STEP 5: TerrainDecorator.RestoreRocks() 수정

```csharp
GameObject rockObj = RockSpawner.SpawnRockObject(chunk, rock, tileType);

DiggableRock dr = rockObj.GetComponent<DiggableRock>();
dr.PixelBoundsInChunk = new RectInt(entry.boundsX, entry.boundsY, entry.boundsW, entry.boundsH);
chunk.AddSpawnedRock(dr);
chunk.AddRockBound(rock.bounds);

// ── 신규: 저장된 HP·회복타이머·노출 상태 복원 ──
dr.RestoreState(entry.savedHp, entry.lastDamageTime, entry.isRevealed);
```

---

## 타이밍 검증

### 풀 재사용 (대부분의 경우)

```
SetActive(true)
  └─ OnEnable()     → _currentHp=MaxHp, _lastDamageTime=-∞
RestoreRocks() [동기, 같은 프레임]
  └─ RestoreState() → _currentHp=savedHp, _lastDamageTime=saved, 콜라이더 활성화
Start() 재실행 없음 (이미 실행됨)
```
✅ 안전

### 신규 Instantiate

```
Instantiate()
  └─ Awake()        → 컴포넌트 캐시
  └─ OnEnable()     → _currentHp=MaxHp, _lastDamageTime=-∞
RestoreRocks() [동기, 같은 프레임]
  └─ RestoreState() → _currentHp=savedHp, _lastDamageTime=saved
다음 프레임
  └─ Start()        → InitTexture() [HP 건드리지 않음 ← 핵심 수정]
                    → if (!_isRevealed) RevealInTerrain()
                       └─ _isRevealed=true면 collider.enabled 체크로 스킵
```
✅ 안전 (`Start()`의 `_currentHp = MaxHp` 제거가 전제)

---

## 회복 동작 예시

```
설정: MaxHp=5, recoveryDelay=10s, recoveryRate=1/s

T= 0  플레이어가 캠 → HP=3, lastDamageTime=0
T= 5  청크 언로드 → RockSaveEntry { savedHp=3, lastDamageTime=0, isRevealed=true }
T= 8  청크 재로드 → RestoreState(3, 0, true) 복원
      Time.time(8) - lastDamageTime(0) = 8초 < 10초 → 아직 회복 안 함
T=10  Time.time(10) - lastDamageTime(0) = 10초 ≥ 10초 → 회복 시작
T=12  HP = 3 + 1×2 = 5 (완전 회복)

T=15  청크 언로드 → savedHp=5, lastDamageTime=0 (회복 완료 상태)
T=20  재로드 → HP=5, 회복 불필요
```

---

## Inspector 권장 기본값

| 필드 | 기본값 | 비고 |
|------|--------|------|
| `recoveryDelay` | `10f` | 10초 비전투 후 회복 시작 |
| `recoveryRate`  | `1f`  | MaxHp=5 기준 5초면 완전 회복 |

두 값 모두 지층별 암석 프리팹에서 다르게 조정 가능.
