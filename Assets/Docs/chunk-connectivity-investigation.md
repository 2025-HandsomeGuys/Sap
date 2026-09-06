# 청크 간 연결 시스템 조사 계획
@tags: border-sync, neighbor-chunk, chunk-boundary, BoundarySyncJob, BoundarySync, distance-field, cross-chunk, collider, visual-border, MarkChunkDirty, ProcessDirtyChunksAsync, Round2, dig-propagation, TerrainCollider, GetPixelAlpha, GetNeighborDistance

> **목적:** 청크 경계 연결 관련 오류 원인을 파악하기 위해 코드를 체계적으로 읽는 계획.
> 구현 변경 없이 **이해** 에 집중.

---

## 청크 간 연결의 3가지 레이어

```
┌─────────────────────────────────────────────────────┐
│  A. 비주얼 경계 (Border Texture)                     │
│     타일 타입 전환 시 경계 이음새 텍스처              │
├─────────────────────────────────────────────────────┤
│  B. 조명/거리 필드 동기화 (BoundarySync)             │
│     이웃 청크 DistanceField 값을 읽어 경계 픽셀 보정 │
├─────────────────────────────────────────────────────┤
│  C. 콜라이더 (Cross-chunk pixel query)               │
│     경계 픽셀의 채움 여부를 이웃 청크에서 직접 조회  │
└─────────────────────────────────────────────────────┘
```

---

## Task 1: BoundarySync 잡 파이프라인 읽기

**목표:** 이웃 청크 DistanceField를 경계에 복사하는 핵심 로직 이해

**읽을 파일 (순서대로):**

### 1-A. `BoundarySyncJob` 구현
파일: `Assets/Scripts/_Core/Managers/TerrainJobs.cs`
- `struct BoundarySyncJob : IJob` (line ~255)
- 어떤 이웃 NativeArray를 [ReadOnly]로 받는지
- `isSkyAbove` 플래그가 어떻게 경계 처리에 영향을 주는지
- 경계 픽셀에 어떤 값을 기록하는지 (`distanceField[idx] = ?`)

**확인할 것:**
- 4방향(N/S/E/W)만 처리? 아니면 8방향(대각선 포함)?
- 이웃이 없을 때 fallback 값은?

### 1-B. `ChunkJobScheduler.ScheduleBoundarySync()`
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs`
- line ~233 `ScheduleBoundarySync()`
- `BoundaryNeighborSet` 구조체 (line ~37): 4방향 NativeArray 슬롯
- `_externalDistanceFieldReaders` 합산 로직 (line ~182)
- `RegisterExternalDistanceFieldReader()` (line ~189)

**확인할 것:**
- 이웃이 로드 안 됐을 때 length-0 placeholder로 대체되는 로직
- 완료 핸들(`_boundarySyncHandle`)이 다음 Chamfer 잡의 deps에 어떻게 합산되는지

### 1-C. `TerrainLightingCalculator.ScheduleBoundarySyncJob()`
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainLightingCalculator.cs`
- line 23 `ScheduleBoundarySyncJob()`
- 4방향 이웃 청크를 어떻게 조회하는지 (`ChunkProvider.GetChunk`)
- 이웃의 `GetLightingHandle()` 을 deps에 합산하는 방식
- `isSkyAbove` 조건 (`Y==0` 청크 처리, line ~49)
- BoundarySync 완료 후 이웃에게 `RegisterExternalDistanceFieldReader()` 등록

**확인할 것:**
- 이웃이 `null`일 때 placeholder NativeArray 사용 경로
- `CompleteLighting()`을 강제 완료시키는 시점 있는지

---

## Task 2: InfinityMapManager Round 2 파이프라인 읽기

**목표:** ProcessDirtyChunksAsync 에서 경계 동기화가 언제/어떻게 트리거되는지 이해

**읽을 파일:**

### 2-A. `ProcessDirtyChunksAsync` 전체 흐름
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs`
- `SnapshotScheduleInitJobs()` — Init 잡 스케줄 (Step 1)
- `SnapshotScheduleRound1()` — Round 1 Visual 잡 (Step 2)
- `SnapshotCompleteLighting()` — Round 1 완료 대기 + 조명 완료 (Step 3)
- `SnapshotScheduleRound2()` — **이웃 경계 동기화 후** Round 2 (Step 4)

**확인할 것:**
- `SnapshotScheduleRound2()` 에서 `ScheduleBoundarySync` 호출 방식
- Round 2 Visual 잡이 Round 1과 무엇이 다른지 (같은 잡? deps만 다른지?)
- `SnapshotFinalizeAndApply()` 에서 텍스처 업로드 순서

### 2-B. `MarkChunkDirty()` 이웃 전파
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs`
- `MarkChunkDirty(chunk, rect)` 구현
- 파기된 청크만 dirty 마킹? 이웃 청크도 함께 dirty 마킹?
- `_dirtyColliderChunks` 집합과 `_dirtyVisualChunks` 의 관계

**확인할 것:**
- 경계 픽셀 파기 시 이웃 청크가 자동으로 dirty 대기열에 들어가는지
- 이웃이 로드 안 된 상태에서 경계 파기 시 처리 방식

---

## Task 3: 비주얼 경계 텍스처 (Border Texture) 읽기

**목표:** 타일 타입 전환 이음새 텍스처가 어떻게 생성·적용되는지 이해

### 3-A. Border 데이터 로드 경로
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/Generation/Pipeline/ChunkDataProvider.cs`
- line ~117 `GetBorderPixels(type)`
- `SecondaryBorderPixels` 로드 조건 (line ~185): 어떤 조건에서 두 번째 border가 필요한지
- `context.BorderPixels` → `initData.BorderPixels` → `ChunkData.BorderData` 로 이어지는 흐름

### 3-B. Border 픽셀 렌더링
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs`
- line ~91 Visual 잡 스케줄 (`borderData`, `secondaryBorderData`, `borderWidth/Height`)
- `solidBorderColor` 가 무엇인지
- Border 텍스처가 실제 지형 픽셀과 어떻게 합성되는지

파일: `Assets/Scripts/_Core/Managers/TerrainJobs.cs`
- Visual 잡 내부에서 border pixel lookup 로직 (UV 매핑 방식)

**확인할 것:**
- Border 텍스처는 청크 간 경계를 시각적으로 가리는 역할인지, 아니면 조명과 연동되는지
- 파기 후 border 픽셀이 재계산되는지 (border는 생성 시 1회만?)

---

## Task 4: 크로스-청크 픽셀 조회 경로 읽기

**목표:** 경계 픽셀에서 이웃 청크 데이터를 참조하는 모든 경로 파악

### 4-A. `GetNeighborDistance()` — 조명 크로스-청크 조회
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs`
- line ~361 `GetNeighborDistance(x, y)`
- 경계 범위 초과 시 neighborCoord 계산 방식
- `neighbor.CompleteLighting()` 강제 완료 호출 시점
- `neighbor == null` 일 때 반환값 `0` 의 의미 (air로 취급)

### 4-B. `GetPixelAlpha()` — 콜라이더 크로스-청크 조회
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs`
- line ~431 크로스-청크 GetPixelAlpha 구현
- 이웃이 없을 때 `return true` (air) fallback

### 4-C. `TerrainCollider` 경계 픽셀 처리
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainCollider.cs`
- Moore-neighbor 8방향 순회 (line ~209~)
- 청크 경계 픽셀에서 `GetPixelAlpha()` 크로스-청크 호출 여부
- 경계 픽셀에서 이웃이 없을 때 콜라이더 형태

**확인할 것:**
- 이웃 청크 로드/언로드 시 경계 콜라이더가 즉시 재생성되는가?
- `_dirtyColliderChunks` 에 이웃 청크가 추가되는 시점

---

## Task 5: 파기 시 이웃 청크 갱신 흐름 읽기

**목표:** 픽셀 파기 이벤트가 이웃 청크 비주얼/콜라이더에 어떻게 전파되는지

### 5-A. `TerrainModifier` 파기 로직
파일: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs`
- `Dig()` 또는 이에 해당하는 메서드
- 경계 픽셀 파기 시 이웃 청크에 `MarkChunkDirty` 호출 여부
- 이웃 청크 좌표 계산 방식

### 5-B. `InfinityMapManager.MarkChunkDirty()` 이웃 전파 확인
(Task 2-B에서 읽은 내용 + 아래 확인)
- 경계 근처 파기 rect가 이웃 청크 dirty 대기열에 들어가는지
- 이웃 dirty 마킹이 비주얼만인지 콜라이더도 포함인지

---

## Task 6: 오류 현상 기록 및 원인 후보 정리

조사 완료 후 아래 표를 채운다:

| 레이어 | 오류 현상 | 예상 원인 파일/함수 | 확인 방법 |
|--------|-----------|---------------------|-----------|
| 비주얼 경계 | | | |
| 조명 경계 | | | |
| 콜라이더 경계 | | | |
| 파기 전파 | | | |

---

## 읽기 순서 요약

```
Task 1: BoundarySyncJob (TerrainJobs.cs)
         └─ ChunkJobScheduler.ScheduleBoundarySync()
              └─ TerrainLightingCalculator.ScheduleBoundarySyncJob()

Task 2: InfinityMapManager.ProcessDirtyChunksAsync()
         └─ SnapshotScheduleRound2() (이웃 동기화 핵심)
         └─ MarkChunkDirty() (전파 범위)

Task 3: ChunkDataProvider → ChunkJobScheduler (Border 텍스처)
         └─ TerrainJobs Visual 잡 (border pixel 합성)

Task 4: TerrainChunk.GetNeighborDistance() (조명)
         TerrainChunk.GetPixelAlpha() (콜라이더)
         TerrainCollider (경계 폴리곤)

Task 5: TerrainModifier → MarkChunkDirty (이웃 전파)
```

---

## 핵심 질문 (읽으면서 답해야 할 것)

1. **파기 → 이웃 전파 경로:** 경계 픽셀 파기 시 이웃 청크가 자동으로 Visual/Collider dirty 대기열에 들어가는가?
2. **BoundarySync 트리거 시점:** Round 2에서만 실행? 아니면 청크 init 시에도?
3. **이웃 미로드 시 fallback:** 이웃 청크가 없을 때 각 레이어(조명/콜라이더/비주얼)의 fallback 값은?
4. **Border 텍스처 갱신:** 파기 후 border 픽셀이 재계산되는가? 생성 시 1회 고정인가?
5. **콜라이더 경계:** `TerrainCollider`가 경계 8방향 체크에서 이웃 청크를 실제로 참조하는가?
