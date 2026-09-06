# CompressedTrashWall 배치 전체 흐름
@tags: special-chunk, compressedtrashwall, spawn, flow, SpecialChunkManager, prefab, neighbor-chunk, border

> CompressedTrashWall 특수 청크 프리팹이 SpecialChunkManager를 통해
> 일반 청크 옆에 배치되기까지의 전 과정을 코드 레벨로 정리한 문서.

---

## 0. 전제 조건 (인스펙터 설정 요약)

```
CompressedTrashWallChunk 프리팹 루트
├─ TerrainChunk               ← IChunk 구현체, 픽셀 파기 가능
├─ SpriteCavityInitializer    ← IChunkInitializer 구현체, 스프라이트 픽셀 주입
└─ CompressedTrashBag (자식)  ← CompressedTrashWallEntity 등 게임 오브젝트

SpecialChunkManager Inspector
└─ Pools[targetLayer = Dirt]
   └─ SpecialChunkDef
       prefab      = CompressedTrashWallChunk
       spawnChance = (원하는 확률)
       chunkType   = CompressedTrashWall
       chunkSizeX  = 1, chunkSizeY = 1
```

---

## 1. 매 프레임 진입점 — InfinityMapManager.UpdateChunks()

```
[매 프레임] InfinityMapManager.Update()
    │
    └─ UpdateChunks()
        ├─ BuildLoadQueue()            ← 플레이어 주변 viewDistance 범위 순회
        │   for x in [-vd, +vd]:
        │     for y in [-vd, +vd]:
        │       targetCoord = lastChunkCoord + (x, y)
        │       if ShouldSkipChunkGeneration(targetCoord) → skip
        │       if _chunkRegistry.HasChunk(targetCoord)  → skip
        │       else → queue.Add(targetCoord)
        │
        └─ _loadingRunner.UpdateLoadQueue(queue)
            → SortLoadQueueByDistance()   ← 플레이어에서 가까운 순
            → StartLoadingIfNeeded()      ← 코루틴 시작
```

### ShouldSkipChunkGeneration 조건

| 조건 | 동작 |
|------|------|
| `coord.y > 0` | skip (지표 위) |
| `abs(coord.x) > terrainWidth` | skip (맵 가로 경계 초과) |

> 이 단계에서는 특수 청크 여부를 체크하지 않는다.
> 특수/일반 분기는 Phase 1 내부 ChunkDataProvider에서 결정된다.

---

## 2. Phase 1 — Spawn & Initialize

```
ChunkLoadingRunner.ProcessChunkQueue() [코루틴]
    │
    for coord in 배치(batch):
        ChunkGenerationPipeline.ExecutePhase1_SpawnAndInitialize(coord)
```

### 2-1. ChunkDataProvider.GetContext(coord, parent)

특수 청크 여부를 결정하는 핵심 분기점.

```
GetContext(coord, parent)
│
├─ [1-A] SpecialChunkManager.TryGetRegisteredSubChunk(coord)
│         → 이미 앵커 스폰 시 SubChunkRegistry에 등록된 서브 청크 인스턴스가 있으면
│           context.SpecialChunkInstance = 그 인스턴스, return
│
├─ [1-B] SpecialChunkManager.SpawnSpecialChunkIfPossible(coord, tileType, seed, parent)
│         → SpecialChunkSelector.TrySelect() 결과에 따라:
│              꽝  → null 반환
│              당첨 → 아래 흐름 →
│                   Instantiate(prefab, ChunkCoords.ToWorld(coord), ...)
│                   anchorObj.name = "Special_{x}_{y}"
│                   anchorChunk = anchorObj.GetComponent<IChunk>()   ← TerrainChunk
│                   foreach IChunkInitializer → initializer.Initialize(parent)
│                       ↑ SpriteCavityInitializer.Initialize() 실행
│                   // SetActive(false) 조건:
│                   // NeedsDelayedActivation && GetComponent<IChunkInitializer>() == null
│                   // SpriteCavityInitializer가 루트에 있으므로 SetActive(false) 안 함
│                   (sizeX=1, sizeY=1이므로 SubChunkRegistry 예약 없음)
│                   return anchorChunk
│         → 당첨이면 context.SpecialChunkInstance = anchorChunk, return
│         → 꽝이면 계속
│
├─ [1-A2] SpecialChunkManager.IsSubChunkCoord(coord, ...)
│          → true면 context.IsBlocked = true, return (일반 청크 생성 차단)
│
└─ [일반] GenerateNewData()
          → GetGroundPixels(tileType): 지층 색상 픽셀 배열 생성
          → GetBorderPixels(tileType): 경계 픽셀 로드
          → context.GroundPixels, BorderPixels 등 채움
```

### 2-2. SpriteCavityInitializer.Initialize() 상세 (1-B 내부)

```
SpriteCavityInitializer.Initialize(parent)
    │
    ├─ TerrainChunk chunk = GetComponent<TerrainChunk>()
    ├─ Texture2D tex = sourceSprite.texture    ← 반드시 Read/Write Enabled
    ├─ Color[] spritePixels = tex.GetPixels(rect.x, rect.y, sw, sh)
    │
    ├─ var data = chunk.GetData()             ← ChunkData 접근
    │
    └─ for each pixel (x, y):
           if alpha < 10 → data.BasePixels[idx] = (0,0,0,0)   ← 공기(공동)
           else          → data.BasePixels[idx] = spriteColor  ← 지형(파기 가능)
       data.CurrentPixels.CopyFrom(data.BasePixels)
       chunk.isTextureDirty = true
       chunk.isDirty = true
```

결과: TerrainChunk 픽셀 데이터가 스프라이트 형상과 일치. 투명 영역은 공동.

### 2-3. SpecialChunkSelector.TrySelect() 상세 (1-B 내부)

```
TrySelect(coord, layerType, seed, registry)
    │
    ├─ TrySelectRaw(coord, layerType, seed)
    │   → layerType에 맞는 pool 검색
    │   → CoordHash(coord, seed) = (x*73856093)^(y*19349663)^(seed*83492791)
    │   → PRNG(hash) 로 각 SpecialChunkDef.spawnChance 로 롤
    │   → 당첨 def 반환 또는 null
    │
    ├─ IsNearLayerBoundary() → 경계 근처면 null
    │
    ├─ exclusion zone 계산 (footprint + minChunkSpacing 확장)
    │   → 이미 등록된 다른 특수 청크가 exclusion 안에 있으면 null
    │   → 이웃에도 당첨 후보가 있으면 CoordHash 해시 비교 → 높은 쪽 우선
    │
    └─ 최종 당첨 def 반환
```

### 2-4. ChunkSpawner.SpawnChunk(context)

```
SpawnChunk(context)
    │
    ├─ context.IsSpecialChunk == true
    │   → SpecialChunkFactory.CreateChunk(context)
    │       → tChunk.SetChunkProvider(_chunkProvider)   ← IChunkProvider 주입
    │       → tChunk.player = _player                   ← 플레이어 Transform 주입
    │       → return context.SpecialChunkInstance        ← 이미 Instantiate된 인스턴스
    │
    └─ context.IsSpecialChunk == false
        → StandardChunkFactory.CreateChunk(context)
            → ChunkPool에서 재사용 또는 신규 Instantiate
            → Reuse_Step1_Prepare(groundPixels, ...): 픽셀 데이터 초기화
```

> **핵심:** SpecialChunkFactory는 Instantiate를 다시 하지 않는다.
> 이미 1-B 단계(SpawnSpecialChunkIfPossible)에서 생성된 인스턴스를 그대로 반환.

### 2-5. 레지스트리 등록

```
ChunkGenerationPipeline.ExecutePhase1_SpawnAndInitialize()
    │
    └─ _registry.Add(coord, chunk)  ← ActiveChunkRegistry에 등록
       이후 BuildLoadQueue에서 HasChunk(coord) == true → 중복 생성 방지
```

---

## 3. Phase 1 Job 완료 대기

```
ChunkLoadingRunner.ProcessChunkQueue()
    │
    └─ while (!p1JobsFinished):
           foreach result:
               if tc.IsJobRunning() → 아직 안 끝남
           yield return null (다음 프레임)
```

> SpriteCavityInitializer를 가진 CompressedTrashWall은 SetActive(false) 상태가 아니므로
> `Reuse_Step1_Prepare` 미호출 → `ScheduleInitJobOnly` 미호출 → `IsJobRunning()=false`
> → Phase 1 대기 루프가 즉시 통과된다.

---

## 4. Phase 2 — Finalize & Decorate

```
ChunkGenerationPipeline.ExecutePhase2_FinalizeAndDecorate(coord, chunk)
    │
    ├─ chunk is TerrainChunk tChunk
    │   │
    │   ├─ tChunk.Reuse_Step2_Finalize()
    │   │   → gameObject.SetActive(true)   ← 이미 active이므로 실질적으로 no-op
    │   │   → UpdateCollider() 즉시 실행  ← 동기 호출 (Job 없음)
    │   │
    │   ├─ isSpecialPrebuilt = tChunk.GetComponent<IChunkInitializer>() != null
    │   │                              ↑ SpriteCavityInitializer 존재 → true
    │   │
    │   └─ if (!isSpecialPrebuilt)
    │           _spawner.DecorateChunk_Phase2(tChunk, coord)  ← 이 경우 실행 안 됨!
    │
    └─ (일반 청크일 때만 DecorateChunk_Phase2 실행)
```

### DecorateChunk_Phase2 스킵의 의미

| 항목 | 스킵됨 (특수 청크) | 실행됨 (일반 청크) |
|------|-------------------|-------------------|
| 기존 자식 파괴 | ✅ 스킵 — CompressedTrashBag 자식 보존됨 | 실행 |
| ElevatorDecorator | ✅ 스킵 | 조건부 실행 |
| MineralDecorator | ✅ 스킵 | 실행 |
| RockDecorator | ✅ 스킵 | 실행 |

> 자식 파괴 코드:
> ```csharp
> if (child.name.StartsWith("ROCK_"))  → 풀 반납
> else if (child.name.StartsWith("MINERAL_")) → 풀 반납
> else → Destroy(child)   ← CompressedTrashBag이 여기에 해당
> ```
> SpriteCavityInitializer가 없었다면 CompressedTrashBag이 Destroy되었을 것임.

---

## 4.5. Phase 2.5 — Visual Job 예약

```
ChunkLoadingRunner (Phase 2 완료 후)
    │
    └─ for each tc: tc.FinishVisualsAfterInit()
           → _visualizer.UpdateVisualsFull(ChunkX, ChunkY, null, skipInit: true)
           → Chamfer + Visual Job 예약 (비블로킹)
           → 실제 텍스처 GPU 업로드는 LateUpdate ProcessDirtyChunksAsync에서 완료
```

> 이 단계가 있어야 지형 색상이 실제로 화면에 렌더링됨.
> Phase 2의 `UpdateCollider()`는 픽셀 데이터만 읽어 콜라이더를 생성하므로
> 텍스처 업로드와 분리되어 있음.

---

## 5. Phase 3 — Update Lighting

```
ChunkGenerationPipeline.ExecutePhase3_UpdateLighting(chunk)
    │
    └─ if chunk.gameObject.activeSelf && chunk is TerrainChunk tChunk:
           tChunk.UpdateBoundaryLighting(true, true, true, true)
```

> SetActive(true) 이후 BFS 라이팅 계산 실행.

---

## 6. 인접 일반 청크와의 관계

```
플레이어 @ 청크 (2, -1) 기준, viewDistance=1

로드 큐: (1,-2) (2,-2) (3,-2)
         (1,-1) [2,-1] (3,-1)    ← [2,-1] = CompressedTrashWall 특수 청크
         (1, 0) (2, 0) (3, 0)

(2,-1) 처리 시 → 1-B에서 특수 청크 당첨 → Instantiate, IChunkInitializer.Initialize()
(1,-1) 처리 시 → 1-B 꽝, 1-A2 미해당 → 일반 StandardChunkFactory → 정상 배치
(3,-1) 처리 시 → 동일 (일반 청크)

각 청크의 월드 위치 (ChunkCoords.ToWorld = coord * 10f, 피벗 좌하단):
(1,-1) → (10, -10)   |  (2,-1) → (20, -10)   |  (3,-1) → (30, -10)
└─ 10×10 유닛 영역    └─ 10×10 유닛 영역        └─ 10×10 유닛 영역
   [10,20] × [-10,0]    [20,30] × [-10,0]          [30,40] × [-10,0]

인접하지만 겹치지 않음 — x=20, x=30 에서 정확히 맞닿음
```

---

## 7. 전체 시퀀스 다이어그램 (요약)

```
InfinityMapManager
    UpdateChunks()
        BuildLoadQueue() → [coords]
        ↓
ChunkLoadingRunner
    ProcessChunkQueue() [코루틴]
        ↓ for each coord in batch
ChunkGenerationPipeline
    ExecutePhase1_SpawnAndInitialize(coord)
        ↓
ChunkDataProvider
    GetContext(coord, parent)
        ↓ 1-B 당첨
SpecialChunkManager
    SpawnSpecialChunkIfPossible()
        ↓
SpecialChunkSelector
    TrySelect() → def (당첨)
        ↓
Instantiate(def.prefab, ToWorld(coord))  ← 씬에 배치 (SetActive=true 유지)
        ↓
SpriteCavityInitializer.Initialize()     ← 픽셀 주입 (공동 형성)
        ↓
// SetActive(false) 안 함 — IChunkInitializer가 루트에 있으므로 조건 불충족
        ↓ context 반환
ChunkSpawner
    SpawnChunk() → SpecialChunkFactory.CreateChunk()
        ↓ 인스턴스 재사용, provider/player 주입, border 데이터 적용
ActiveChunkRegistry
    Add(coord, chunk)                    ← 등록 완료
        ↓
[Phase 1 Job 대기] ← ScheduleInitJobOnly 미호출 → IsJobRunning=false → 즉시 통과
        ↓
ChunkGenerationPipeline
    ExecutePhase2_DecoratorSteps()
        ↓
TerrainChunk.Reuse_Step2_Finalize()     ← SetActive(true) (이미 활성), UpdateCollider()
        ↓
IChunkInitializer 존재 → DecorateChunk_Phase2 스킵
                         (자식 보존, 랜덤 암석·광물 없음)
        ↓
tc.FinishVisualsAfterInit()             ← Chamfer + Visual Job 예약 (비블로킹)
        ↓
ExecutePhase3_UpdateLighting()          ← BFS 라이팅 + MarkChunkDirty
        ↓
완료 — CompressedTrashWallChunk가 일반 청크 옆에 정상 배치됨
```

---

## 8. 주요 불변식 (잘못되면 버그)

| 불변식 | 위반 시 증상 |
|--------|------------|
| `SpriteCavityInitializer.sourceSprite` 텍스처 Read/Write ON | `Initialize()` 실패, 픽셀 주입 안 됨 → 흰 블록 |
| 스프라이트 Pivot = Bottom Left | 청크가 grid 기준점에서 벗어나 반칸 어긋남 |
| `TerrainChunk.pixelsPerUnit` = 100 | 렌더 크기가 10배 또는 0.1배 → 인접 청크 침범 |
| `IChunkInitializer` 컴포넌트 루트에 존재 | 없으면 Phase 2가 자식을 Destroy → CompressedTrashBag 소멸 |
| `SpecialChunkDef.prefab` = IChunk 구현체 있는 프리팹 | `GetComponent<IChunk>() == null` → Destroy + 로그 에러 |
| `chunkSizeX = 1, chunkSizeY = 1` | 다르면 SubChunkRegistry 예약 발생 → 인접 슬롯 일반 청크 생성 차단 |

---

## 9. 런타임 로그 체크포인트

정상 동작 시 아래 로그가 순서대로 출력된다.

```
[SpecialChunkManager] SpawnSpecialChunkIfPossible 호출: coord=(2,-1), layer=Dirt
[SpecialChunkSelector] (2,-1) prefab=CompressedTrashWallChunk roll=12.3 chance=15 → 당첨
[SpecialChunkManager] (2,-1) → 선택됨: CompressedTrashWallChunk
[SpecialChunkManager] CHUNKSIZE Spawn | coord=(2,-1) | prefab=CompressedTrashWallChunk | anchorPos=(20,-10,0)
[SpriteCavityInitializer] 스프라이트 픽셀 주입 완료 (2,-1) 1000x1000
[TRACE][ChunkDataProvider] (2,-1) → 1-B 특수청크 스폰 성공: Special_2_-1
```

이상 시 확인 포인트:
- `→ 풀 없음` → Inspector에서 targetLayer 확인
- `→ prefab null` → SpecialChunkDef.prefab 미할당
- `Texture '...' Read/Write가 비활성화` → Import Settings 수정
- `Phase2 skipped`가 안 뜨고 `DecorateChunk_Phase2` 실행됨 → IChunkInitializer 컴포넌트 미부착
