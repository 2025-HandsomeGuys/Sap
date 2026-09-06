# 미니맵 Update에서 발생하는 Job Sync 스파이크 분석
@tags: border-sync, BoundarySyncJob, neighbor-chunk, chunk-boundary, job-pipeline, ProcessDirtyChunksAsync, chamfer, distance-field, minimap, performance, job-sync, EnsureJobsCompleted, Round2

## 프로파일러 증상

```
PlayerLoop (49.41ms)
 └─ Update.ScriptRunBehaviourUpdate
     └─ UndergroundMinimap.Update()         43.26ms
         └─ JobHandle.Complete              43.22ms
             └─ WaitForJobGroupID           43.20ms
                 ├─ Idle                    40.33ms  ← 메인 스레드 대기
                 ├─ TerrainJobs:Init...      1.66ms  ← 워커 스레드
                 └─ TerrainJobs:Vis...       1.00ms  ← 워커 스레드
```

---

## 현재 Job System 구조

### 청크별 잡 파이프라인 (ChunkJobScheduler)

지형 하나가 파였을 때 실행되는 잡 의존성 체인:

```
InitBFSJob (IJobParallelFor)
  → BoundarySyncJob (IJob)        ← 8방향 이웃 initHandle에 의존
      → ChamferForwardPassJob (IJob)
          → ChamferBackwardPassJob (IJob)   ← _lightingJobHandle
              → VisualUpdateJob (IJobParallelFor)  ← _visualsJobHandle
```

각 잡이 사용하는 NativeArray:

| NativeArray      | InitBFS | BoundarySync | ChamferFwd | ChamferBwd | VisualUpdate |
|------------------|---------|--------------|------------|------------|--------------|
| BasePixels       | R       |              |            |            | R            |
| DistanceField    | W       | R(이웃)/W(자기) | W          | W          | R            |
| PixelInfo        | R       |              |            |            | R            |
| GetRawTexture()  |         |              |            |            | W            |

### ProcessDirtyChunksAsync 6단계 파이프라인 (InfinityMapManager)

```
Step 1: SnapshotScheduleInitJobs()     — 전체 dirty 청크 Init 잡 스케줄 → yield 1프레임
Step 2: SnapshotScheduleRound1()       — Round 1 Visual 잡 스케줄
Step 3: wait loop                      — IsVisualJobCompleted() 폴링 (비블로킹)
Step 4: SnapshotScheduleRound2()       — 이웃 경계 동기화 후 Round 2 스케줄
Step 5: wait loop                      — Round 2 완료 폴링
Step 6: SnapshotFinalizeAndApply()     — ApplyTexture() 직접 호출
```

이 코루틴은 LateUpdate에서 진행되며, 각 yield 사이 프레임이 넘어간다.

---

## 근본 원인: IsTransparent() → EnsureJobsCompleted() 체인

### 호출 경로

```
UndergroundMinimap.Update()
  └─ DigPathTracker.ExploreArea()          ← updateInterval마다 호출 (기본 0.1초)
      └─ [for each cell in radius 3 circle]
          └─ chunk.IsTransparent(px, py)   ← 최대 ~29개 청크 셀
              └─ EnsureJobsCompleted()     ← TerrainChunk.cs:429
                  └─ _visualizer.CompleteJobs()
                      └─ _jobScheduler.CompleteAllJobs()
                          ├─ _initJobHandle.Complete()
                          ├─ _boundarySyncHandle.Complete()
                          ├─ _forwardJobHandle.Complete()
                          ├─ _lightingJobHandle.Complete()
                          └─ _visualsJobHandle.Complete()
```

### 왜 40ms가 낭비되는가

`Update()`는 `LateUpdate()`보다 먼저 실행된다. 따라서:

1. 직전 프레임의 LateUpdate에서 ProcessDirtyChunksAsync가 Init + Visual 잡을 스케줄했지만, 아직 **배치 대기 상태(dispatched 안 됨)**인 잡이 남아있을 수 있다.
2. 현재 프레임 Update에서 `ExploreArea` → `IsTransparent` → `CompleteAllJobs()` 호출.
3. Unity Job System이 배치 큐를 flush해서 워커 스레드에 보냄 + 완료를 기다림.
4. Init → BoundarySync → ChamferFwd → ChamferBwd → Visual 의존성 체인이 순차 실행.
5. 메인 스레드는 이 전체 체인이 끝날 때까지 대기 → **Idle 40ms**.

실제 잡 실행은 1.66ms + 1.00ms이지만, **배치 미발송(dispatch 지연) + 의존성 체인 대기**가 40ms를 만든다.

### 왜 미니맵이 여기에 관여하는가

`ExploreArea(radius=3)`는 최대 29개 셀을 순회하며, 각 셀마다 해당 청크의 `IsTransparent`를 호출한다. 미니맵은 픽셀 투명도만 알면 되는데, 내부적으로 **모든 terrain 잡을 동기 완료**시키는 `CompleteAllJobs()`가 실행된다.

---

## 현재 구조의 문제점 요약

| 문제 | 위치 | 영향 |
|------|------|------|
| `IsTransparent()`가 `CompleteAllJobs()` 호출 | TerrainChunk.cs:429 | 전체 잡 파이프라인 강제 동기화 |
| `ExploreArea()`가 Update()에서 매 0.1초마다 실행 | UndergroundMinimap.cs:87 | 드릴 사용 중 지속적 스파이크 |
| BasePixels 읽기에 Visual 잡까지 완료 대기 | CompleteAllJobs() 과잉 | Init 완료면 충분한데 5개 잡 모두 대기 |

---

## 개선 방향

### 방향 A: IsTransparent에서 CompleteAllJobs → CompleteInit으로 교체

BasePixels는 잡에서 `[ReadOnly]`로만 읽힌다. 따라서 InitBFSJob이 완료된 후라면 메인 스레드에서 BasePixels를 읽어도 안전하다. Visual 잡까지 완료시킬 필요가 없다.

```csharp
// TerrainChunk.cs IsTransparent() 내부
// 변경 전
EnsureJobsCompleted();  // CompleteAllJobs() 호출

// 변경 후 (Init/Lighting 완료만 보장)
CompleteLighting();     // Init + BoundarySync + ChamferFwd + ChamferBwd 완료
                        // VisualUpdateJob은 대기 안 함
```

단, BasePixels를 쓰는 경로(TerrainModifier.Dig 등)는 여전히 `CompleteAllJobs`가 필요할 수 있으니 해당 호출부를 별도로 유지할 것.

### 방향 B: DigPathTracker에 별도 캐시 유지 (권장)

미니맵이 NativeArray에 전혀 접근하지 않도록 한다. 지형 파기 이벤트(`OnTerrainModified`) 때 "이 셀은 공기"라는 정보를 HashSet에 별도로 기록.

```csharp
// DigPathTracker에 추가
private readonly HashSet<Vector2Int> _airCells = new HashSet<Vector2Int>();

void OnTerrainModified(Vector2 worldPos)
{
    Vector2Int cell = WorldToCell(worldPos);
    _airCells.Add(cell);           // 파진 = 공기
    _diggedCells.Add(cell);
    IsDirty = true;
}

// ExploreArea에서 IsTransparent 대신 _airCells 조회
```

`ExploreArea`의 목적은 "기존에 공기였던 공간"을 밝히는 것이므로, `OnTerrainModified` 이벤트가 발화되는 시점에 캐싱하면 NativeArray 접근이 불필요해진다.

### 방향 C: ExploreArea를 LateUpdate로 이동

ProcessDirtyChunksAsync가 LateUpdate 후반부에서 잡을 스케줄하므로, ExploreArea를 LateUpdate 맨 앞에 실행하면 잡 완료 상태에서 안전하게 읽을 수 있다. 단, `_timer` 로직 조정 필요.

---

## 관련 파일

| 파일 | 관련 |
|------|------|
| `Assets/Scripts/UI/Player/UndergroundMinimap.cs` | ExploreArea 호출, Render |
| `Assets/Scripts/Gameplay/Terrain/DigPathTracker.cs` | ExploreArea 구현 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` | IsTransparent, EnsureJobsCompleted |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainVisualizer.cs` | CompleteJobs, CompleteLighting |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs` | CompleteAllJobs, CompleteLighting |
| `Assets/Scripts/_Core/Managers/TerrainJobs.cs` | InitBFSJob, VisualUpdateJob, Chamfer 잡 정의 |
| `docs/LightingBFS_Optimization.md` | 이전 최적화 작업 이력 |
