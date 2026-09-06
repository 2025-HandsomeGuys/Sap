# LightingBFS 최적화 작업 기록
@tags: border-sync, neighbor-chunk, chunk-boundary, BoundarySyncJob, SyncBoundaryDistance, MarkNeighborDirty, chamfer, distance-field, BFS, optimization, Round2, ProcessDirtyChunksAsync, dig-propagation, strip-rect, SYNC_DEPTH

## 개요

프로파일러에서 발견된 3개 병목 함수를 분석하고 단계적으로 최적화.

| 함수 | 최적화 전 | 원인 |
|---|---|---|
| `InfinityMapManager.LateUpdate()` | ~38.5ms | 청크 순차 처리 + 이중 완료 대기 |
| `JobHandle.Complete` | ~38.2ms | 메인 스레드 블로킹 |
| `TerrainJobs.LightingBFSJob (Burst)` | ~28.1ms | 랜덤 메모리 접근 + 큐 오버헤드 |

---

## 1차 최적화 (구조적 개선)

### Fix 1 — `MarkNeighborDirty` 스트립 rect

**파일:** `TerrainChunk.cs`, `InfinityMapManager.cs`

**문제:**
경계 근처를 팔 때 이웃 청크 전체(1000×1000 = 100만 px)를 dirty로 마크 → 이웃 청크에서 전체 BFS 재실행.

```
전체 BFS:  1,000,000px → ~28ms
스트립 BFS:    50,000px → ~1-2ms (약 20배 감소)
```

**변경 내용:**
`MarkNeighborsDirty`에서 full chunk rect 대신 인접 경계 스트립(50px)만 전달.

```
crossedLeft   → 왼쪽 이웃의 오른쪽 50px 스트립: RectInt(width-50, 0, 50, height)
crossedRight  → 오른쪽 이웃의 왼쪽 50px 스트립: RectInt(0, 0, 50, height)
crossedTop    → 위쪽 이웃의 아래쪽 50px 스트립: RectInt(0, 0, width, 50)
crossedBottom → 아래쪽 이웃의 위쪽 50px 스트립: RectInt(0, height-50, width, 50)
코너          → 50×50px 패치
```

`InfinityMapManager.MarkNeighborDirty` 시그니처 변경:
```csharp
// 이전
public void MarkNeighborDirty(int cx, int cy)
// 이후
public void MarkNeighborDirty(int cx, int cy, RectInt dirtyRect)
```

---

### Fix 2 — LateUpdate 배치 완료

**파일:** `InfinityMapManager.cs`

**문제:**
Round 1 / Round 2에서 각 청크마다 `EnsureJobsCompleted()` 호출 → BFS가 순차 실행됨.

```
이전: [chunkA BFS 완료] → [chunkB BFS 완료] = N × BFS_time
이후: [chunkA BFS ...]
      [chunkB BFS ...] ← 병렬  = max(BFS_time)
```

**변경 내용:**
- Round 1 루프 내 `EnsureJobsCompleted()` 제거
- Round 1 종료 후 모든 청크 `CompleteLighting()` 일괄 대기
- Round 2 루프 내 `EnsureJobsCompleted()` 제거
- Round 2 종료 후 모든 청크 `EnsureJobsCompleted()` 일괄 대기

```csharp
// Round 1: 스케줄만
foreach (chunk) chunk.DoVisualUpdate(rect);

// 일괄 완료 (BFS만)
foreach (chunk) chunk.CompleteLighting();

// Round 2: 스케줄만
foreach (chunk) chunk.DoVisualUpdate(rect);

// 일괄 완료 (전체)
foreach (chunk) chunk.EnsureJobsCompleted();
```

---

### Fix 3 — SyncBoundaryDistanceWithNeighbors 완료 대기 축소

**파일:** `TerrainLightingCalculator.cs`

**문제:**
이웃 청크 `DistanceField` 읽기 전 `EnsureJobsCompleted()` (BFS + Visual 대기) 호출.
DistanceField는 BFS 완료 시점에 이미 확정되므로 Visual Job 대기는 불필요.

**변경 내용:**

```csharp
// 이전
neighbor.EnsureJobsCompleted(); // BFS + Visual 대기
// 이후
neighbor.CompleteLighting();    // BFS만 대기
```

---

## 2차 최적화 (알고리즘 교체)

### BFS → 2-패스 Chamfer 거리 변환

**파일:** `TerrainJobs.cs`, `ChunkJobScheduler.cs`, `TerrainVisualizer.cs`, `TerrainLightingCalculator.cs`, `TerrainChunk.cs`

#### 문제 분석

`LightingBFSJob`의 근본적 한계:

| 문제 | 설명 |
|---|---|
| 랜덤 메모리 접근 | BFS는 큐 순서로 픽셀 방문 → 2MB DistanceField에 캐시 미스 폭발 |
| NativeQueue 오버헤드 | enqueue/dequeue 수십만~수백만 회 반복 |
| 픽셀 재처리 | 더 짧은 거리 발견 시 같은 픽셀을 여러 번 큐에 삽입 가능 |

#### 알고리즘 원리

**Chamfer 5-7 2-패스 거리 변환** (Borgefors, 1986):

**Forward Pass** (좌상 → 우하):
각 픽셀에서 이미 처리된 이웃의 최소 거리 전파.
```
체크 방향: 좌(cost 5), 위(cost 5), 좌상(cost 7), 우상(cost 7)
```

**Backward Pass** (우하 → 좌상):
반대 방향 이웃의 최소 거리 전파.
```
체크 방향: 우(cost 5), 아래(cost 5), 우하(cost 7), 좌하(cost 7)
```

두 패스 합산 = BFS와 **수학적으로 동일한 Chamfer 5-7 거리 필드**.
단, 순차 스캔라인 방식이므로 각 픽셀은 패스당 정확히 1회 처리.

#### 성능 비교

| | BFS | 2-패스 Chamfer |
|---|---|---|
| 메모리 접근 패턴 | 랜덤 (큐 순서) | 순차 스캔라인 |
| 큐 | NativeQueue 필요 | 불필요 |
| 픽셀당 처리 횟수 | 1회 이상 (재삽입 가능) | 패스당 정확히 1회 |
| 1000×1000 예상 시간 | ~28ms | ~2ms |

#### 파이프라인 변경

```
이전: InitBFSJob → (main: SyncBoundary + Enqueue) → LightingBFSJob → VisualUpdateJob
이후: InitBFSJob → (main: SyncBoundary) → ChamferForwardPassJob → ChamferBackwardPassJob → VisualUpdateJob
```

#### SyncBoundaryDistanceWithNeighbors 단순화

BFS에서는 경계값을 큐에 삽입해야 전파 가능했지만,
2-패스에서는 `distanceField`에 값만 써 두면 패스가 자동 전파.

```csharp
// 이전: distanceField 기록 + 큐 삽입
data.DistanceField[myIdx] = newDist;
bfsQueue.Enqueue(myIdx);

// 이후: distanceField 기록만
data.DistanceField[myIdx] = newDist;
```

`NativeQueue<int>` 완전 제거 → `Allocator.Persistent` 할당/해제 오버헤드도 사라짐.

#### 시그니처 변경

```csharp
// TerrainLightingCalculator
// 이전
public void SyncBoundaryDistanceWithNeighbors(NativeQueue<int> bfsQueue)
// 이후
public void SyncBoundaryDistanceWithNeighbors()

// TerrainVisualizer
// 이전
public void UpdateVisualsArea(..., System.Action<NativeQueue<int>> onPreBFS = null)
// 이후
public void UpdateVisualsArea(..., System.Action onPreChamfer = null)

// ChunkJobScheduler
// 이전
public void ScheduleLightingBFS(ChunkData data)
// 이후
public void ScheduleChamferPasses(ChunkData data)
```

---

## 수정 파일 목록

| 파일 | 변경 내용 |
|---|---|
| `TerrainJobs.cs` | `LightingBFSJob` 제거, `InitBFSJob` queue 필드 제거, `ChamferForwardPassJob` / `ChamferBackwardPassJob` 추가 |
| `ChunkJobScheduler.cs` | `_bfsQueue` 제거, `_forwardJobHandle` 추가, `ScheduleLightingBFS` → `ScheduleChamferPasses`, ext 범위 필드 저장 |
| `TerrainVisualizer.cs` | `onPreBFS: Action<NativeQueue<int>>` → `onPreChamfer: Action`, `ScheduleLightingBFS` → `ScheduleChamferPasses` |
| `TerrainLightingCalculator.cs` | `bfsQueue` 파라미터 제거, `Enqueue` 호출 제거, `EnsureJobsCompleted` → `CompleteLighting` |
| `TerrainChunk.cs` | `SyncBoundaryDistanceWithNeighbors` 시그니처 변경, `MarkNeighborsDirty` 스트립 rect 적용 |
| `InfinityMapManager.cs` | `MarkNeighborDirty` 시그니처 변경, LateUpdate 배치 완료 구조 변경 |

---

## 주의사항

- **2-패스 Chamfer의 정확도**: Forward/Backward 두 패스로 정확한 Chamfer 5-7 거리 보장. 단, 청크 경계 연결은 `SyncBoundaryDistanceWithNeighbors` + Round 2 재처리로 보완.
- **Extended Area**: `InitBFSJob`의 `EXT_MARGIN = 50`은 유지. 2-패스가 이 영역 바깥 픽셀의 기존 값을 읽어 Dirty rect 안으로 전파함.
- **스트립 rect 크기**: `STRIP = 50` (= `EXT_MARGIN`)으로 설정. BFS 전파 최대 깊이(maxDist/5 = 51px)와 일치.
