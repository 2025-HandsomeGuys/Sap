# 청크 경계선 랙 최적화 계획
@tags: chunk-loading, optimization, ChunkLoadingRunner, async, pipeline, performance, JobHandle, chunk-boundary, lag

> 작성일: 2026-03-23
> 최종 수정: 2026-03-24 (JobHandle.Complete idle 54.7% 원인 분석 및 해결)
> 대상 파일: ChunkLoadingRunner, ChunkLoadingScheduler, InfinityMapManager, ChunkGenerationPipeline, 관련 파일

---

## 1. 현재 흐름 요약

```
[Update] HandlePlayerChunkTracking()
    → 청크 좌표 변경 감지
    → UpdateChunks()
        → UnloadDistantChunks()          ← 동기 실행
        → BuildLoadQueue()               ← 동기 실행
        → _loadingRunner.UpdateLoadQueue()
        → StartLoadingIfNeeded()

[Coroutine] ProcessChunkQueue()
    → Phase 1: SpawnAndInitialize (배치 처리, 시간 예산)
    → Job 완료 대기 (프레임 양보)
    → Phase 2: FinalizeAndDecorate (시간 예산)
    → Phase 3: UpdateLighting
    → Job 완료 대기 (프레임 양보)
```

---

## 2. 랙 원인 분석

### [원인 A] 활성화된 Debug.Log — 체감 랙 1위

> **현황 (2026-03-24): 대부분 해결됨. 잔여 1건.**

~~현재 청크 로드/언로드 경로에 주석 처리되지 않은 `Debug.Log`가 다수 존재한다.~~

아래 파일들은 청크 로딩 경로 로그가 전부 주석처리 완료되었다:

| 파일 | 상태 |
|---|---|
| `ChunkGenerationPipeline.cs` | ✅ 주석처리 완료 |
| `StandardChunkFactory.cs` | ✅ 주석처리 완료 |
| `ChunkDataProvider.cs` | ✅ 주석처리 완료 |
| `InfinityMapManager.cs` (로딩/언로드 경로) | ✅ 주석처리 완료 |

**해결 (2026-03-24)**: `ShouldSkipChunkGeneration()` 내 4개 TRACE 로그 제거 완료.

---

### [원인 B] 경계 통과 프레임에 동기 언로드 실행

`Update()` → `UpdateChunks()` → `UnloadDistantChunks()`가 **같은 프레임에 동기 실행**된다.

```csharp
// InfinityMapManager.cs
void UpdateChunks()
{
    UnloadDistantChunks();   // ← 이 프레임에 모두 처리됨
    ...
}
```

`UnloadDistantChunks()` 내부에서 `SaveChunkDataToMemory()`가 호출된다.
저장 대상은 수정된 청크의 픽셀 배열(1000×1000 = 1,000,000 픽셀 = 4MB)이다.
`viewDistance=1`에서 경계 통과 시 최대 3개 청크가 동시에 언로드될 수 있다.

**문제**: 최대 3×4MB = 12MB 분량의 배열 처리가 단일 프레임에 몰린다.

**수정**: 언로드도 코루틴으로 분산 처리한다. 언로드 큐를 두고 프레임당 N개씩 처리한다.

```
[새 구조]
UpdateChunks()
    → 언로드 대상 좌표를 _unloadQueue에 추가 (동기, 가벼움)
    → BuildLoadQueue / StartLoadingIfNeeded

[Coroutine] ProcessUnloadQueue()
    → 매 프레임 _unloadQueue에서 1~2개씩 꺼내 SaveChunkDataToMemory + 반납
```

---

### [원인 C] CreateBatch의 O(n) List.RemoveAt(0)

```csharp
// ChunkLoadingScheduler.cs
public List<T> CreateBatch<T>(List<T> queue, int maxSize)
{
    for (int i = 0; i < count; i++)
    {
        batch.Add(queue[0]);
        queue.RemoveAt(0);  // ← 매번 전체 List 앞당기기 O(n)
    }
}
```

`batchSize=4`, 큐 9개 기준으로는 미미하지만, `viewDistance`가 커질수록 O(n×batchSize)로 증가한다.
또한 `_loadQueue.Insert(0, coord)` (`PrependToQueue`, `UpdateLoadQueue`) 도 O(n)이다.

**수정**: `_loadQueue`를 `List<Vector2Int>` 대신 인덱스 커서 방식으로 변경한다.

```csharp
// 변경 전: RemoveAt(0) 반복
// 변경 후: 읽기 인덱스(_readHead)만 전진, 코루틴 종료 시 Clear()
private int _readHead = 0;

// CreateBatch 대신:
for (int i = 0; i < batchSize && _readHead < _loadQueue.Count; i++, _readHead++)
    batch.Add(_loadQueue[_readHead]);
```

앞에 삽입해야 하는 앵커 좌표는 별도 `_priorityQueue` (작은 List)로 관리해 먼저 소비한다.

---

### [원인 D] UpdateLoadQueue의 List.Contains() — O(n²)

```csharp
// ChunkLoadingRunner.cs
foreach (var coord in _loadQueue)
{
    if (!newQueue.Contains(coord) && ...)  // ← newQueue가 List면 O(n) per item
        pendingAnchors.Add(coord);
}
```

`newQueue`가 `List<Vector2Int>`이므로 `Contains`가 O(n)이고,
`_loadQueue`의 모든 항목을 순회하면 전체 O(n²)가 된다.

**수정**: `newQueue`를 `HashSet<Vector2Int>`로 변환한 후 Contains 호출한다.

```csharp
var newQueueSet = new HashSet<Vector2Int>(newQueue);
foreach (var coord in _loadQueue)
{
    if (!newQueueSet.Contains(coord) && ...)
        pendingAnchors.Add(coord);
}
```

---

### [원인 E] 예측 로딩 없음 — 구조적 원인

현재는 플레이어가 청크 경계선을 **완전히 넘은 후** 로드가 시작된다.

```
플레이어 이동: ... → [청크A] → (경계) → [청크B]
로딩 시작:                               ↑ 이 순간에야 시작
```

청크 하나를 완전히 로드하는 데 여러 프레임이 걸리므로,
새 청크에 도착했을 때는 아직 이웃 청크들이 존재하지 않아 빈 공간이나 팝-인이 발생한다.

**수정**: 플레이어가 청크 경계에서 특정 거리 이내(예: 청크 크기의 30%)에 들어오면 미리 이웃 청크를 로드 큐에 넣는다.

```csharp
// 예시: HandlePlayerChunkTracking() 내부 추가
float thresholdRatio = 0.35f;  // 청크 크기의 35% 이내 진입 시 사전 로드
Vector2 inChunkOffset = new Vector2(
    player.position.x % chunkWidthWorld,
    player.position.y % chunkHeightWorld
);

// X 방향 오른쪽 경계 근접 시
if (inChunkOffset.x > chunkWidthWorld * (1f - thresholdRatio))
    PreloadChunk(new Vector2Int(lastChunkCoord.x + 1, lastChunkCoord.y));
// X 방향 왼쪽 경계 근접 시
if (inChunkOffset.x < chunkWidthWorld * thresholdRatio)
    PreloadChunk(new Vector2Int(lastChunkCoord.x - 1, lastChunkCoord.y));
// Y 방향도 동일하게...
```

`PreloadChunk(coord)`: 아직 레지스트리에 없으면 `_loadQueue`에 추가 + `StartLoadingIfNeeded()` 호출.

---

### [원인 F] 코루틴 재진입 불가로 인한 처리 지연

`_isLoadingRoutineRunning = true` 동안 새 경계 통과가 발생하면,
`StartLoadingIfNeeded()`는 새 코루틴을 시작하지 못한다.

```csharp
public void StartLoadingIfNeeded()
{
    if (_loadQueue.Count > 0 && !_isLoadingRoutineRunning)  // 실행 중이면 skip
        StartCoroutine(ProcessChunkQueue());
}
```

`UpdateLoadQueue()`로 큐는 갱신되지만, 이미 실행 중인 코루틴은 갱신된 큐를 처리한다.
문제는 코루틴이 현재 배치의 Phase 1 → Phase 2 → Phase 3 사이클을 마친 뒤에야 큐의 새 항목을 처리하기 때문에,
**두 번 연속 빠르게 경계를 통과하면 두 번째 세트의 청크는 첫 번째 세트가 완전히 처리될 때까지 대기**한다.

**수정**: Phase 1 Spawn 후 Phase 2/3를 별도 코루틴으로 분리해,
Spawn이 완료된 청크는 즉시 Phase 2 코루틴으로 넘기고 Phase 1은 계속 다음 청크를 처리하도록 한다.
또는 배치 사이에 큐를 재검사해 새 항목이 있으면 현재 배치 종료 후 즉시 처리한다.

---

### [원인 G] SortLoadQueueByDistance의 람다 GC 할당

```csharp
_loadQueue.Sort((a, b) =>
{
    Vector2 posA = new Vector2(a.x * ChunkCoords.WorldSize, ...);  // new 할당
    Vector2 posB = new Vector2(b.x * ChunkCoords.WorldSize, ...);  // new 할당
    return (posA - playerPos2D).sqrMagnitude.CompareTo(...);
});
```

정렬 시 비교 횟수만큼 `Vector2`가 힙에 할당된다 (값 타입이지만 람다 내부에서 박싱 가능성 있음).
비교 횟수는 약 O(n log n)이므로, n=9이면 약 30회 할당.

**수정**: 거리를 사전에 `float[]`로 계산해두고 인덱스 기반으로 정렬한다.
또는 `IComparer<Vector2Int>` 구조체를 캐싱해서 람다 클로저 할당을 없앤다.

---

### [원인 H] LateUpdate에서 모든 dirty 청크를 동기 처리

```csharp
// InfinityMapManager.cs LateUpdate
foreach (var chunk in _dirtyChunksOrdered)
    chunk.EnsureJobsCompleted();   // ← 잡 강제 완료 (블로킹)
```

LateUpdate에서 `EnsureJobsCompleted()`를 호출하면 해당 Job이 완료될 때까지 메인 스레드가 블로킹된다.
파기(Dig) 직후라면 단일 청크이므로 빠르지만,
**청크 로딩 중 `RevealExposedRocks()` 같은 추가 dirty 등록과 겹치면** 여러 청크의 Job 완료를 기다려야 한다.

**현황**: 이미 Round 1/Round 2 병렬 패턴으로 최적화되어 있음.
`EnsureJobsCompleted()`가 일괄 완료 대기이므로 각 청크의 Job이 병렬 실행 후 한 번에 완료된다. → **현재는 비교적 양호함.**

---

## 3. 우선순위별 수정 계획

### P0 — 즉시 수정 (1~2시간, 효과 확실)

#### P0-1. 활성 Debug.Log 전체 비활성화

> **상태 (2026-03-24 확인): 대부분 완료 ✅ — 잔여 1건 🔴**

**완료된 파일**:
- `ChunkGenerationPipeline.cs` — 청크 로딩 경로 로그 전부 주석처리됨 (LogWarning/LogError만 유지)
- `StandardChunkFactory.cs` — 전부 주석처리됨
- `ChunkDataProvider.cs` — 전부 주석처리됨 (LogError만 유지)
- `InfinityMapManager.cs` — 로딩 경로 대부분 주석처리됨

**완료 (2026-03-24)**:

`InfinityMapManager.cs`의 `ShouldSkipChunkGeneration()` 내 4개 TRACE 로그 제거 완료.

---

#### P0-2. CreateBatch O(n) 제거

> **상태 (2026-03-24): 완료 ✅**

`ChunkLoadingRunner.cs`에 `_readHead` 커서 방식 적용됨. `CreateBatch`는 `ChunkLoadingScheduler`에서 제거되고 `ChunkLoadingRunner` 내부 코루틴으로 통합됨.

---

#### P0-3. UpdateLoadQueue의 Contains → HashSet

> **상태 (2026-03-24): 완료 ✅**

`ChunkLoadingRunner.cs`에 `HashSet<Vector2Int>` 적용됨.

---

### P1 — 중기 수정 (반나절, 핵심 구조 개선)

#### P1-1. 예측 로딩 (Predictive Pre-loading)

> **상태 (2026-03-24): 완료 ✅**
> - `HandlePlayerChunkTracking()`에서 `TryPreloadNeighborChunks()` 호출
> - `PRELOAD_THRESHOLD = 0.35f` 상수로 관리
> - `_pendingUnloadSet` 체크로 언로드 예정 청크는 사전 로드 대상에서 제외

**대상 파일**: `InfinityMapManager.cs`

`HandlePlayerChunkTracking()` 내에 경계 근접 감지 로직 추가.
청크 크기의 `preloadThreshold`% 이내 진입 시 인접 청크를 사전에 로드 큐에 넣는다.

```csharp
[SerializeField] private float _preloadThreshold = 0.35f; // JSON에서 설정 가능

private void HandlePlayerChunkTracking()
{
    Vector2Int currentChunkCoord = CalculatePlayerChunkCoord();

    if (currentChunkCoord != lastChunkCoord)
    {
        lastChunkCoord = currentChunkCoord;
        UpdateChunks();
    }

    TryPreloadNeighborChunks();  // ← 추가
}

private void TryPreloadNeighborChunks()
{
    if (player == null) return;

    float cx = player.position.x / chunkWidthWorld;
    float cy = player.position.y / chunkHeightWorld;
    float fx = cx - Mathf.Floor(cx);  // 청크 내 x 비율 [0,1]
    float fy = cy - Mathf.Floor(cy);  // 청크 내 y 비율 [0,1]

    var preloadCoords = new List<Vector2Int>();

    if (fx > 1f - _preloadThreshold)
        preloadCoords.Add(new Vector2Int(lastChunkCoord.x + 1, lastChunkCoord.y));
    if (fx < _preloadThreshold)
        preloadCoords.Add(new Vector2Int(lastChunkCoord.x - 1, lastChunkCoord.y));
    if (fy > 1f - _preloadThreshold)
        preloadCoords.Add(new Vector2Int(lastChunkCoord.x, lastChunkCoord.y + 1));
    if (fy < _preloadThreshold)
        preloadCoords.Add(new Vector2Int(lastChunkCoord.x, lastChunkCoord.y - 1));

    bool added = false;
    foreach (var coord in preloadCoords)
    {
        if (!_chunkRegistry.HasChunk(coord) && !ShouldSkipChunkGeneration(coord))
        {
            _loadingRunner.PrependToQueue(new[] { coord });
            added = true;
        }
    }

    if (added)
        _loadingRunner.StartLoadingIfNeeded();
}
```

---

#### P1-2. 언로드 비동기화

> **상태 (2026-03-24): 완료 ✅**
> - `UnloadDistantChunks()`: 대상 좌표를 `_unloadQueue`에 추가만 하고 즉시 반환
> - `ProcessUnloadQueue()` 코루틴: 프레임당 4ms 예산으로 분산 처리
> - `DoUnloadChunk()`: 실제 언로드 로직. 처리 시점에 `IsChunkOutOfRange` 재확인으로 방향 전환 대응
> - `_pendingUnloadSet`으로 중복 큐잉 방지

**대상 파일**: `InfinityMapManager.cs`

```csharp
// 언로드 큐 추가
private readonly Queue<Vector2Int> _unloadQueue = new Queue<Vector2Int>();
private bool _isUnloadRoutineRunning = false;

// UnloadDistantChunks: 큐에만 넣고 즉시 반환
private void UnloadDistantChunks()
{
    var activeCoords = _chunkRegistry.GetActiveCoordinatesCopy();
    foreach (var coord in activeCoords)
    {
        if (IsChunkOutOfRange(coord))
            _unloadQueue.Enqueue(coord);
    }

    if (!_isUnloadRoutineRunning)
        StartCoroutine(ProcessUnloadQueue());
}

// 실제 언로드: 프레임 분산
private IEnumerator ProcessUnloadQueue()
{
    _isUnloadRoutineRunning = true;
    var sw = new System.Diagnostics.Stopwatch();

    while (_unloadQueue.Count > 0)
    {
        sw.Restart();
        while (_unloadQueue.Count > 0 && sw.ElapsedMilliseconds < 4)
        {
            var coord = _unloadQueue.Dequeue();
            // 이미 언로드됐으면 skip
            if (!_chunkRegistry.HasChunk(coord)) continue;

            DoUnloadChunk(coord);  // 기존 언로드 로직
        }
        yield return null;
    }

    _isUnloadRoutineRunning = false;
}
```

주의: 언로드 큐에 들어간 좌표가 다시 로드 큐에 들어오는 경우(플레이어 방향 전환) 처리 필요.
→ `_unloadQueue`에서 해당 좌표를 제거하거나, `DoUnloadChunk` 시작 시 `IsChunkOutOfRange(coord)`를 재확인한다.

---

#### P1-3. GetActiveCoordinatesCopy() 할당 감소

> **상태 (2026-03-24): 완료 ✅**
> - `ActiveChunkRegistry.CopyActiveCoordinatesTo(List<Vector2Int>)` 추가
> - `InfinityMapManager._tempCoordBuffer` (capacity 64) 필드 추가
> - `UnloadDistantChunks()`에서 `GetActiveCoordinatesCopy()` 대신 버퍼 재사용으로 변경

**대상 파일**: `InfinityMapManager.cs`, `ActiveChunkRegistry.cs`

```csharp
// 현재: 언로드마다 새 리스트 복사
public List<Vector2Int> GetActiveCoordinatesCopy()
    => new List<Vector2Int>(_activeCoordinates);

// 개선: 재사용 가능한 버퍼 전달
public void CopyActiveCoordinatesTo(List<Vector2Int> buffer)
{
    buffer.Clear();
    buffer.AddRange(_activeCoordinates);
}

// 호출 측 (InfinityMapManager 필드로 캐싱)
private readonly List<Vector2Int> _tempCoordBuffer = new List<Vector2Int>(64);

private void UnloadDistantChunks()
{
    _chunkRegistry.CopyActiveCoordinatesTo(_tempCoordBuffer);
    foreach (var coord in _tempCoordBuffer)
    { ... }
}
```

---

### P2 — 장기 개선 (구조적 변경, 선택적)

#### P2-1. BuildLoadQueue 캐싱

> **상태 (2026-03-24): 완료 ✅**
> - `InfinityMapManager._buildQueueBuffer` 필드 추가 (capacity 32)
> - `BuildLoadQueue()`에서 `new List<>()` 제거, 버퍼 재사용으로 변경

#### P2-2. ChunkGenerationPipeline / ChunkLoadingScheduler 캐싱

> **상태 (2026-03-24): 완료 ✅**
> - `ChunkLoadingRunner._scheduler`, `_pipeline` 필드 추가
> - `Initialize()`에서 한 번만 생성, `ProcessChunkQueue()`에서 재사용

#### P2-3. SortLoadQueueByDistance IComparer 캐싱

> **상태 (2026-03-24): 완료 ✅**
> - `ChunkLoadingRunner.DistanceComparer` 구조체 추가 (힙 할당 없음)
> - `_distanceComparer` 필드로 캐싱, 람다 클로저 GC 제거

#### P2-4. Phase 2 장식 분리 (스파이크 방지)

> **상태 (2026-03-24): 완료 ✅**
> - `ChunkSpawner.DecorateChunk_Phase2_Steps()` 추가 — 데코레이터마다 yield
> - `ChunkGenerationPipeline.ExecutePhase2_DecoratorSteps()` 추가 — IEnumerable 스텝 반환
> - `ChunkLoadingRunner` Phase 2 루프: 청크 단위 + 데코레이터 단위 양쪽에서 `ShouldYieldFrame()` 체크
> - 기존 `ExecutePhase2_FinalizeAndDecorate()`는 레거시 호환용으로 유지 (내부에서 Steps 사용)

---

## 4. worldSettings.json 조정 권고값

수정 전후 비교를 위한 설정값 가이드:

| 파라미터 | 현재 기본값 | 권고값 | 설명 |
|---|---|---|---|
| `chunkSpawningBatchSize` | 4 | 2~3 | 한 배치당 청크 수 줄이면 프레임 분산 |
| `maxTimePerBatchMs` | 6 | 4~5 | 배치 시간 예산 줄이면 프레임 영향 감소 |
| `maxTimePerFramePh2Ms` | 5 | 4 | Phase 2 시간 예산 |
| `textureUpdateInterval` | 0.05 | 0.05~0.1 | GPU 업로드 간격, 로딩 중 0.1로 올리면 이득 |
| `preloadThreshold` *(신규)* | — | 0.3~0.4 | 청크 크기 비율, 경계 근접 사전 로드 트리거 |

---

## 5. 수정 전후 프로파일링 포인트

Unity Profiler에서 다음 항목을 확인한다.

- `HandlePlayerChunkTracking` 스파이크 타이밍 vs. 청크 경계 좌표
- `UnloadDistantChunks` GC.Alloc 양 (목표: 0 또는 매우 소량)
- `Debug.Log` 관련 `UnityEngine.Debug:Log` 호출 스택 비중
- `ProcessChunkQueue (Coroutine)` 내 각 Phase 실행 시간
- LateUpdate `EnsureJobsCompleted` 대기 시간

---

### [원인 I] JobHandle.Complete idle — WaitForJobGroupID 54~65% (2026-03-24 신규 발견)

프로파일러에서 `PlayerLoop / UpdateScene`이 82ms를 점유하고,
`JobHandle.Complete → WaitForJobGroupID`에서 idle이 65.7% → 54.7%로 측정됨.
메인 스레드가 워커의 Job 완료를 기다리며 놀고 있는 상태.

**원인 체인 (3단계)**:

| 단계 | 위치 | 블로킹 내용 |
|---|---|---|
| I-1 | `Reuse_Step1_Initialize` → `RefreshVisuals` → `WaitForInit()` | 신규 청크 Init Job을 예약 직후 Complete() 대기 (~54ms) |
| I-2 | Phase 1b `SyncBoundaryDistanceWithNeighbors` → `CompleteLighting()` | 같은 배치 이웃의 Chamfer Job 대기 |
| I-3 | Phase 2 `MineralGenerator` / `TerrainCarver` → `EnsureJobsCompleted()` | Phase 1b에서 예약된 Chamfer+Visual Job 대기 + BasePixels 데이터 레이스 |

**해결 (2026-03-24 적용)**:

```
[새 ProcessChunkQueue 흐름]

Phase 1  : ScheduleInitJobOnly()        ← Init만 예약, 즉시 반환 (비블로킹)
Phase 1  : wait loop (IsJobRunning)     ← 비블로킹 폴링, init 완료 대기
Phase 2  : 데코레이터 (TerrainCarver, MineralGenerator 등)
           └ Init 완료 상태, Chamfer 미예약 → EnsureJobsCompleted() 불필요
Phase 2.5: FinishVisualsAfterInit()     ← Chamfer+Visual 예약 (onPreChamfer=null, 비블로킹)
Phase 3  : UpdateBoundaryLighting       → 이웃 MarkChunkDirty (비동기)
           + 신규 청크 자신도 MarkChunkDirty → ProcessDirtyChunksAsync가 SyncBoundary 처리
Phase 3  : wait loop (IsJobRunning)     ← Chamfer+Visual 완료 대기 (비블로킹)

[ProcessDirtyChunksAsync - LateUpdate, 1프레임 후]
  Step 1: ScheduleInitOnlyIfReady (비블로킹)
  yield return null
  Step 2: DoVisualUpdate with SyncBoundary → neighbor.CompleteLighting() 즉시 반환
          (1프레임 후이므로 Chamfer 이미 완료)
```

**변경 파일**:
- `ChunkLoadingRunner.cs`: Phase 1b 제거 → Phase 2.5로 이동
- `TerrainChunk.cs`: `FinishVisualsAfterInit()` onPreChamfer=null; `Reuse_Step1_Initialize` → `ScheduleInitJobOnly()`; `GetNeighborDistance` `EnsureJobsCompleted` → `CompleteLighting`
- `ChunkGenerationPipeline.cs`: `ExecutePhase3_UpdateLighting`에 신규 청크 `MarkChunkDirty` 추가
- `MineralGenerator.cs`: `EnsureJobsCompleted()` 제거 (Phase 2 시점엔 Job 미예약)
- `TerrainLightingCalculator.cs`: `TriggerNeighborRefresh` → `MarkChunkDirty` (이전 세션 적용)

---

## 6. 수정 순서 권고

```
1. P0-1: Debug.Log 비활성화          → ✅ 완료 (2026-03-24)
2. P0-2: CreateBatch O(n) 제거       → ✅ 완료 (2026-03-24)
3. P0-3: Contains → HashSet          → ✅ 완료 (2026-03-24)
4. 프로파일링으로 잔여 스파이크 측정
5. P1-1: 예측 로딩                   → ✅ 완료 (2026-03-24)
6. P1-2: 언로드 비동기화             → ✅ 완료 (2026-03-24)
7. P1-3: 버퍼 재사용                 → ✅ 완료 (2026-03-24)
8. P2-1: BuildLoadQueue 버퍼 캐싱      → ✅ 완료 (2026-03-24)
9. P2-2: Scheduler/Pipeline 캐싱      → ✅ 완료 (2026-03-24)
10. P2-3: IComparer 구조체 캐싱        → ✅ 완료 (2026-03-24)
11. P2-4: Phase 2 데코레이터 분리      → ✅ 완료 (2026-03-24)
12. 원인 I: JobHandle.Complete idle 제거 → ✅ 완료 (2026-03-24)
```
