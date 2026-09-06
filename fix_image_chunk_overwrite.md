# Fix: ImageChunkOverrider 위치에 특수청크 스폰 방지

## 문제 요약

`ImageChunkOverrider`가 0,0 청크의 **TerrainChunk 픽셀 데이터는 정상적으로 덮어씀**.
그러나 그 이전에 생성 파이프라인(`ChunkDataProvider` → `SpecialChunkManager.SpawnSpecialChunkIfPossible`)이
0,0에 특수청크 프리팹을 Instantiate하고, 해당 프리팹의 **자식 indestructible GameObject들이 씬에 그대로 남는** 문제.

## 근본 원인

`SpecialChunkSelector.TrySelect()`는 다른 특수청크와의 간격(`minChunkSpacing`)만 체크하고,
**"이 좌표는 이미지로 덮어쓸 예정이므로 특수청크 스폰 금지"** 라는 개념이 없다.

## 수정 방향

`SpecialChunkManager`에 **보호 좌표 집합(protected coords)** 을 추가하고,
`ImageChunkOverrider.Awake()`에서 `targetChunkCoord`를 등록.
스폰 시도 시 해당 좌표이면 즉시 `null` 반환.

---

## 수정 1: `SpecialChunkManager.cs` (order -50 지정 포함)

### 변경할 내용

**클래스 선언 위에 `[DefaultExecutionOrder(-50)]` 추가**:

```csharp
[DefaultExecutionOrder(-50)]
public class SpecialChunkManager : MonoBehaviour
```

**필드 추가** (선언부, `Awake` 전에 초기화되도록 field initializer 사용):

```csharp
// 특수청크 스폰을 차단할 보호 좌표 집합
private readonly HashSet<Vector2Int> _protectedCoords = new HashSet<Vector2Int>();
```

**Public API 추가** (`#region Public API — 스폰` 위에 별도 region 또는 기존 region 내):

```csharp
/// <summary>
/// 해당 좌표에 특수청크 스폰을 차단한다. (예: ImageChunkOverrider 사용 위치)
/// </summary>
public void RegisterProtectedCoord(Vector2Int coord) => _protectedCoords.Add(coord);

/// <summary>
/// 보호 좌표 등록을 해제한다.
/// </summary>
public void UnregisterProtectedCoord(Vector2Int coord) => _protectedCoords.Remove(coord);
```

**`SpawnSpecialChunkIfPossible()` 가장 앞에 조기 반환 추가**:

```csharp
public IChunk SpawnSpecialChunkIfPossible(
    Vector2Int coord, TileType layerType, int worldSeed,
    Transform parent,
    out List<(Vector2Int subCoord, IChunk subChunk)> outSubChunks)
{
    outSubChunks = null;

    // ★ 보호 좌표 차단
    if (_protectedCoords.Contains(coord))
        return null;

    // 1. 앵커 프리팹 선택
    // ... (기존 코드 유지)
}
```

---

## 수정 2: `ImageChunkOverrider.cs`

### 변경할 내용

**클래스 선언 위에 `[DefaultExecutionOrder(-40)]` 추가**:

```csharp
[DefaultExecutionOrder(-40)]
public class ImageChunkOverrider : MonoBehaviour
```

**`Awake()` 추가** (`Start()` 앞에):

```csharp
private void Awake()
{
    // SpecialChunkManager는 order -50으로 먼저 실행되므로 Instance 보장됨
    SpecialChunkManager.Instance.RegisterProtectedCoord(targetChunkCoord);
}

private void OnDestroy()
{
    if (SpecialChunkManager.Instance != null)
        SpecialChunkManager.Instance.UnregisterProtectedCoord(targetChunkCoord);
}
```

`Start()`는 기존대로 `StartCoroutine(OverrideRoutine())` 유지.

---

## 실행 순서 분석 결과

### 현재 Execution Order 상태

| 클래스 | Order | 비고 |
|--------|-------|------|
| `WorldSettingsLoader` | -200 | `[DefaultExecutionOrder(-200)]` 선언됨 |
| `SpecialChunkSettingsLoader` | -150 | `[DefaultExecutionOrder(-150)]` 선언됨 |
| `SpecialChunkManager` | **0** | 어트리뷰트 없음 — 미지정 |
| `ImageChunkOverrider` | **0** | 어트리뷰트 없음 — 미지정 |
| `InfinityMapManager` | **0** | 어트리뷰트 없음 — 미지정 |

### 문제가 되는 타이밍

`InfinityMapManager.Awake()`는 `StartCoroutine(InitializeCoroutine())`을 호출한다.
`InitializeCoroutine()`은 `TileDataManager`가 이미 준비된 경우 yield 없이 동기 실행으로 다음 경로를 통과한다:

```
InitializeCoroutine() [동기 실행]
  └─ UpdateChunks()
       └─ StartLoadingIfNeeded()
            └─ StartCoroutine(ProcessChunkQueue())
                 └─ ExecutePhase1_SpawnAndInitialize(coord)  ← yield 전에 실행
                      └─ SpawnSpecialChunkIfPossible(0,0)   ← 여기서 특수청크 스폰
```

`ProcessChunkQueue()`의 첫 번째 unconditional yield는 배치 전체 처리 후(`yield return scheduler.YieldAndRestart()`)이므로,
배치 크기(기본 4개)만큼의 청크가 **Awake 단계에서 동기로** 스폰된다.

세 클래스가 모두 order 0이라 `ImageChunkOverrider.Awake()`가 `InfinityMapManager.Awake()`보다
**먼저 실행된다는 보장이 없다.** → 보호 좌표 등록 전에 0,0이 이미 스폰될 수 있음.

### 해결: Execution Order 명시적 지정

`[DefaultExecutionOrder]` 어트리뷰트로 순서를 고정한다.

| 클래스 | 지정할 Order | 이유 |
|--------|-------------|------|
| `SpecialChunkManager` | **-50** | `Instance` 및 `_protectedCoords`가 `ImageChunkOverrider.Awake()` 전에 준비 |
| `ImageChunkOverrider` | **-40** | `InfinityMapManager`(order 0) 보다 먼저 보호 좌표 등록 |
| `InfinityMapManager` | 변경 없음 (0) | 청크 생성은 항상 마지막에 시작 |

### 보장된 Awake 실행 순서

```
1. SpecialChunkManager.Awake()  [order -50]
   → Instance 설정, _protectedCoords 준비

2. ImageChunkOverrider.Awake()  [order -40]
   → RegisterProtectedCoord(0,0) 호출

3. InfinityMapManager.Awake()   [order 0]
   → InitializeCoroutine() 시작
   → SpawnSpecialChunkIfPossible(0,0) 시 _protectedCoords에 0,0 이미 등록됨 → null 반환
```

---

## 변경 파일 목록

| 파일 | 변경 내용 |
|------|-----------|
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs` | `[DefaultExecutionOrder(-50)]` 추가, `_protectedCoords` 필드 추가, `RegisterProtectedCoord` / `UnregisterProtectedCoord` 메서드 추가, `SpawnSpecialChunkIfPossible` 앞에 차단 로직 추가 |
| `Assets/Scripts/UI/Interaction/Scene/ImageChunkOverrider.cs` | `[DefaultExecutionOrder(-40)]` 추가, `Awake()` 추가 (보호 좌표 등록), `OnDestroy()` 추가 (등록 해제) |

---

## 변경하지 않는 것

- `SpecialChunkSelector` — 선택 로직 변경 없음. 스폰 직전(`SpawnSpecialChunkIfPossible`) 차단이 더 단순하고 명확.
- `ChunkDataProvider` — 변경 없음.
- 데코레이션(돌/광물) 파이프라인 — TerrainChunk 픽셀은 `ImageChunkOverrider`가 정상 덮어씌우므로 별도 처리 불필요.
