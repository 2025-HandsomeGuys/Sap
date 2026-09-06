# Terrain Chunk 배치 시스템 심층 분석
@tags: chunk-loading, placement, InfinityMapManager, ChunkLoadingRunner, special-chunk, lifecycle, pooling, async

> 작성일: 2026-03-14
> 대상: `InfinityMapManager` 및 연관 서브시스템 전체
> 목적: Large Static Terrain Chunk가 InfinityMapManager를 통해 배치되는 과정 정리

---

## 목차

1. [전체 아키텍처 개요](#1-전체-아키텍처-개요)
2. [시스템 초기화 흐름](#2-시스템-초기화-흐름)
3. [핵심 서브시스템](#3-핵심-서브시스템)
4. [청크 로드/언로드 흐름](#4-청크-로드언로드-흐름)
5. [비동기 배치 로딩: ChunkLoadingRunner](#5-비동기-배치-로딩-chunkloadingrunner)
6. [3단계 생성 파이프라인](#6-3단계-생성-파이프라인)
7. [청크 데이터 수집: ChunkDataProvider](#7-청크-데이터-수집-chunkdataprovider)
8. [청크 생성 팩토리: ChunkSpawner](#8-청크-생성-팩토리-chunkspawner)
9. [LateUpdate: 더티 청크 처리](#9-lateupdate-더티-청크-처리)
10. [좌표 시스템](#10-좌표-시스템)
11. [메모리 관리 전략](#11-메모리-관리-전략)
12. [성능 최적화 기법](#12-성능-최적화-기법)
13. [전체 흐름 시퀀스 다이어그램](#13-전체-흐름-시퀀스-다이어그램)

---

## 1. 전체 아키텍처 개요

`InfinityMapManager`는 **파사드(Facade)** 역할을 하며, 독립적인 서브시스템들을 조율한다.

```
InfinityMapManager (파사드 · 싱글톤)
│
├─ GridCoordinateSystem      좌표 변환 (청크 ↔ 배열 ↔ 월드)
├─ ChunkPool                 청크 객체 재사용 풀 (최대 32개)
├─ ActiveChunkRegistry       활성 청크 O(1) 조회 레지스트리
├─ WorldPersistenceSystem    저장/로드 (메모리 캐시 + 디스크)
│
├─ ChunkLoadingRunner        비동기 배치 로딩 코루틴 (MonoBehaviour)
│   └─ ChunkGenerationPipeline   3단계 파이프라인
│       ├─ ChunkDataProvider     데이터 수집 (컨텍스트 구성)
│       └─ ChunkSpawner          청크 생성 팩토리
│           ├─ StandardChunkFactory  일반 청크
│           └─ SpecialChunkFactory   특수 청크 (던전/함정)
│
└─ 장식자들 (Decorators)
    ├─ ElevatorDecorator
    ├─ MineralDecorator
    └─ RockDecorator
```

### 적용된 디자인 패턴

| 패턴 | 적용 위치 |
|------|-----------|
| Facade | `InfinityMapManager` — 전체 시스템 진입점 |
| Factory | `StandardChunkFactory`, `SpecialChunkFactory` |
| Object Pool | `ChunkPool`, `RockSpawner` |
| Registry | `ActiveChunkRegistry` (O(1) 조회) |
| Pipeline | `ChunkGenerationPipeline` (Spawn → Decorate → Finalize) |
| Strategy | `IMiningStrategy` (채굴 도구 전략) |
| Observer | `TileEventDispatcher` + `ITileDestroyListener` |

---

## 2. 시스템 초기화 흐름

### 2.1 진입점: Awake() → InitializeCoroutine()

게임 시작 시 `Awake()`에서 싱글톤을 설정하고 `InitializeCoroutine()`을 코루틴으로 시작한다.

```
Awake()
├─ Instance 싱글톤 설정
└─ StartCoroutine(InitializeCoroutine())
```

### 2.2 InitializeCoroutine() 10단계 순서

| 단계 | 대상 | 내용 |
|------|------|------|
| ① | `InitializeWorldSeed()` | 월드 시드 설정 (결정론적 생성 보장) |
| ② | `InitializeChunkSettings()` | 청크 크기 계산 (1000px → 10 world units) |
| ③ | `tileVisualSettings` 검증 | TileVisualSettings ScriptableObject 로드 |
| ④ | `TileDataManager.Instance` 대기 | 최대 5초 대기 (비동기 초기화 완료 보장) |
| ⑤ | `GridCoordinateSystem` 초기화 | 좌표 변환 시스템 생성 |
| ⑥ | `ChunkPool` 초기화 | 청크 객체 풀 생성 |
| ⑦ | `ActiveChunkRegistry` 초기화 | O(1) 조회 레지스트리 생성 |
| ⑧ | `WorldPersistenceSystem` 초기화 | 저장/로드 시스템 생성 |
| ⑨ | `ChunkLoadingRunner` 추가 | `AddComponent`로 MonoBehaviour 생성 |
| ⑩ | `ChunkDataProvider` + `ChunkSpawner` 초기화 | 생성 파이프라인 완성 |

**초기화 코드 핵심부:**

```csharp
// ⑤~⑧: 서브시스템 생성
_gridSystem         = new GridCoordinateSystem(terrainWidth, terrainDepth);
_chunkPool          = new ChunkPool();
_chunkRegistry      = new ActiveChunkRegistry(_gridSystem);
_persistenceSystem  = new WorldPersistenceSystem(_gridSystem);
_persistenceSystem.SetActiveChunkRegistry(_chunkRegistry);

// ⑨: 비동기 로딩 컴포넌트
_loadingRunner = gameObject.AddComponent<ChunkLoadingRunner>();
_loadingRunner.Initialize(
    _chunkRegistry, _chunkSpawner, _chunkPool, _chunkDataProvider,
    _gridSystem, player,
    chunkSpawningBatchSize,  // 기본: 4
    maxTimePerBatchMs,       // 기본: 6ms
    maxTimePerFramePh2Ms     // 기본: 5ms
);
```

초기화 완료 후 **`UpdateChunks()`를 즉시 호출**하여 초기 청크를 로드하고, `_isInitialized = true`를 설정한다.

---

## 3. 핵심 서브시스템

### 3.1 GridCoordinateSystem: 좌표 변환

**역할:** 청크 좌표(grid) ↔ 배열 인덱스(array) 변환

```
청크 좌표    →  배열 인덱스
  x → x + terrainWidth          (좌측 경계 0 기준)
  y → -y                        (Y 반전: 아래로 갈수록 증가)

예) 청크 (-50, -10)  →  배열 (0, 10)
    청크 (  0,   0)  →  배열 (50, 0)
    청크 ( 50, -36)  →  배열 (100, 36)
```

| 메서드 | 역할 |
|--------|------|
| `ToArrayX(chunkX)` | 청크X → 배열X |
| `ToArrayY(chunkY)` | 청크Y → 배열Y (Y 반전) |
| `ToWorldCoord(arrayX, arrayY)` | 배열 인덱스 → 청크 좌표 |
| `IsValidArrayIndex(x, y)` | 배열 경계 범위 체크 |

---

### 3.2 ChunkPool: 객체 풀

**역할:** 비활성 청크를 재사용하여 `Instantiate`/`Destroy` 오버헤드 제거

```
풀 상태:
  [비활성 청크 Queue] ← Return()  |  Get() →  [활성화된 청크]

- MAX_POOL_SIZE = 32 (초과 시 Destroy)
- Return() 시: gameObject.SetActive(false) + Enqueue
- Get()   시: Dequeue + 호출자가 SetActive(true) 처리
```

---

### 3.3 ActiveChunkRegistry: O(1) 청크 조회

**역할:** 활성 청크를 빠르게 조회·관리

**내부 자료구조:**

```csharp
IChunk[,]              _activeChunks          // 2D 배열 → O(1) 좌표 조회
List<Vector2Int>       _activeCoordinates      // 순서 유지 (순회용)
HashSet<Vector2Int>    _activeCoordinateSet    // O(1) 존재 여부 확인
```

**Fake-null 처리:** Unity에서 파괴된 오브젝트는 C# null 체크를 통과하지만 UnityEngine.Object 비교에서 false가 된다. 레지스트리는 `Get()` 호출 시 이를 감지하여 해당 항목을 lazy하게 제거한다.

```csharp
// Get() 내부
var unityObj = chunk as UnityEngine.Object;
if (unityObj != null && unityObj == null)   // Unity fake-null 감지
{
    _activeChunks[x, y] = null;
    _activeCoordinateSet.Remove(coord);
    _activeCoordinates.Remove(coord);
    return null;
}
```

---

### 3.4 WorldPersistenceSystem: 저장/로드

**역할:** 청크 픽셀 데이터의 메모리 캐시 및 디스크 I/O 관리

```
메모리 캐시: Dictionary<Vector2Int, ChunkSaveData>
디스크 경로: Application.persistentDataPath/worldData.bin (바이너리)

ChunkSaveData:
  Color32[] modifiedPixels   현재 픽셀 상태
  byte[]    pixelInfo        타일 타입 ID 맵
  bool      hasChanges       수정 여부
```

**청크 생명주기와 저장:**

```
언로드 시:
  enableMemoryCache == true → SaveChunkDataToMemory(coord, chunk)

재로드 시:
  TryGetChunkData(coord) 성공 → 캐시에서 픽셀 복원
  실패 → 절차적 새로 생성

수동 저장 (F5):
  SaveAllDataToDisk() → worldData.bin 쓰기
```

---

## 4. 청크 로드/언로드 흐름

### 4.1 플레이어 추적

`Update()`마다 `HandlePlayerChunkTracking()`이 플레이어 위치를 청크 좌표로 변환하고, 이전 좌표와 다르면 `UpdateChunks()`를 트리거한다.

```csharp
// 플레이어 월드 위치 → 청크 좌표
int x = Mathf.FloorToInt(player.position.x / chunkWidthWorld);
int y = Mathf.FloorToInt(player.position.y / chunkHeightWorld);
// chunkWidthWorld = 1000px / 100ppu = 10 world units
```

### 4.2 UpdateChunks() 흐름

```
UpdateChunks()
├─ 1. UnloadDistantChunks()       범위 밖 청크 언로드
├─ 2. BuildLoadQueue()            로드할 청크 목록 생성
├─ 3. _loadingRunner.UpdateLoadQueue(queue)  큐 업데이트 + 거리순 정렬
├─ 4. PrependSubChunkAnchors(queue)          멀티청크 앵커 우선 삽입
└─ 5. _loadingRunner.StartLoadingIfNeeded()  로딩 코루틴 시작
```

### 4.3 UnloadDistantChunks(): 언로드 조건

`viewDistance` (기본값: 1)를 초과하는 청크를 언로드한다.

```csharp
// 일반 청크: 플레이어 기준 거리
bool outOfRange = Mathf.Abs(coord.x - lastChunkCoord.x) > viewDistance
               || Mathf.Abs(coord.y - lastChunkCoord.y) > viewDistance;

// 멀티청크(서브청크): 앵커의 footprint 기반 범위 판정
```

**언로드 처리:**

```
언로드 대상 청크
├─ enableMemoryCache == true  →  SaveChunkDataToMemory() (언로드 전 저장)
├─ TerrainChunk               →  ChunkPool.Return() (비활성화 + 풀 반납)
└─ 특수 청크 / IChunk         →  Destroy()
```

### 4.4 BuildLoadQueue(): 로드 큐 생성

`viewDistance` 범위 내의 모든 좌표 중 아직 로드되지 않은 좌표를 큐에 추가한다.

**스킵 조건:**

| 조건 | 이유 |
|------|------|
| `coord.y > 0` | 지표면 위는 생성 불필요 |
| 월드 경계 초과 | `terrainWidth` / `terrainDepth` 초과 |
| 이미 레지스트리에 존재 | 중복 로드 방지 |
| 이미 등록된 서브청크 자리 | 앵커가 처리 |

### 4.5 PrependSubChunkAnchors(): 앵커 우선 처리

멀티청크 특수 청크는 **앵커 청크를 서브청크보다 먼저** 로드해야 한다. 큐에서 서브청크 좌표를 감지하면, 해당 앵커를 역산하여 큐 맨 앞에 삽입한다.

```
큐: [서브청크A, 서브청크B, 일반청크C, ...]
→ 앵커 발견: 앵커X
→ 수정 후: [앵커X, 서브청크A, 서브청크B, 일반청크C, ...]
```

---

## 5. 비동기 배치 로딩: ChunkLoadingRunner

`ChunkLoadingRunner`는 `MonoBehaviour` 코루틴으로 동작하며, 한 프레임에 모든 청크를 처리하지 않고 **배치 단위**로 나누어 처리한다.

### 5.1 UpdateLoadQueue(): 큐 갱신 + 정렬

플레이어가 이동할 때마다 새로운 큐를 받아 **거리순 정렬**을 수행한다.

```csharp
_loadQueue.Sort((a, b) => {
    // 플레이어에서 가까운 청크를 먼저 처리
    return SqrDistance(a, player).CompareTo(SqrDistance(b, player));
});
```

**진행 중인 앵커 보존:** 큐 교체 시 현재 로딩 중인 앵커 청크는 새 큐 앞에 유지하여 **레이스 컨디션을 방지**한다.

### 5.2 PrependToQueue(): 앵커 삽입

```csharp
// 앵커를 큐의 최앞 순서로 삽입
foreach (var coord in anchorCoords)
    _loadQueue.Insert(insertIndex++, coord);
```

### 5.3 ProcessChunkQueue(): 로딩 코루틴 구조

```
ProcessChunkQueue() [코루틴]
│
├─ Phase 1 배치 루프
│   ├─ CreateBatch(_loadQueue, batchSize)   배치 추출 (기본 4개)
│   ├─ pipeline.ExecutePhase1_SpawnAndInitialize(coord)  청크 스폰
│   ├─ scheduler.ShouldBreakBatch()  시간 초과 시 yield return null (프레임 양보)
│   └─ yield return null (배치 완료 후 1프레임 대기)
│
├─ Phase 1 Job 완료 대기
│   └─ while (!allJobsFinished) { yield return null }
│        [Unity Jobs가 모두 완료될 때까지 프레임마다 체크]
│
├─ Phase 2 루프 (장식물 스폰)
│   ├─ pipeline.ExecutePhase2_FinalizeAndDecorate(coord, chunk)
│   └─ scheduler.ShouldYieldFrame()  시간 초과 시 yield return null
│
└─ Phase 3 루프 (조명 업데이트)
    ├─ pipeline.ExecutePhase3_UpdateLighting(chunk)
    └─ while (!allJobsFinished) { yield return null }
```

**시간 제한:**

| Phase | 설정 | 기본값 |
|-------|------|--------|
| Phase 1 배치 | `maxTimePerBatchMs` | 6ms |
| Phase 2 프레임 | `maxTimePerFramePh2Ms` | 5ms |

---

## 6. 3단계 생성 파이프라인

`ChunkGenerationPipeline`이 3개의 Phase를 순차적으로 실행한다.

### Phase 1: ExecutePhase1_SpawnAndInitialize()

```
1. 중복 체크: _registry.HasChunk(coord) → 이미 존재하면 null 반환
2. 컨텍스트 수집: _provider.GetContext(coord, _parent)
   └─ 특수청크/서브청크/저장된 청크/새 청크 여부 판단
3. 차단 확인: context.IsBlocked == true → 서브청크 자리, 생성 스킵
4. 청크 스폰: _spawner.SpawnChunk(context)
   ├─ 특수청크: SpecialChunkFactory.CreateChunk()
   └─ 일반청크: StandardChunkFactory.CreateChunk()
5. 레지스트리 등록: _registry.Add(coord, chunk)
6. 멀티청크: 서브청크도 레지스트리에 등록 + UpdateBoundaryLighting()
```

### Phase 2: ExecutePhase2_FinalizeAndDecorate()

```
1. tChunk.Reuse_Step2_Finalize()    재사용 청크 정리
2. _spawner.DecorateChunk_Phase2()  장식물 스폰
   ├─ 기존 자식 오브젝트 정리 (풀 반납)
   ├─ ElevatorDecorator.Decorate()  엘리베이터 공간 예약
   ├─ MineralDecorator.Decorate()   광물 배치
   └─ RockDecorator.Decorate()      암석 배치
```

**왜 Phase 1과 Phase 2를 분리했나?**
Phase 1에서 청크 픽셀 생성 Unity Job이 실행된다. Job이 완료되기 전에 장식물을 스폰하면 광물/암석이 잘못된 픽셀 위에 배치된다. Phase 1 Job이 완전히 끝난 뒤 Phase 2를 실행함으로써 정합성을 보장한다.

### Phase 3: ExecutePhase3_UpdateLighting()

```
chunk.UpdateBoundaryLighting(top: true, bottom: true, left: true, right: true)
→ BFS 기반 라이팅 계산 Job 스케줄링
→ 완료 대기 후 시각 업데이트
```

---

## 7. 청크 데이터 수집: ChunkDataProvider

`GetContext()`는 청크 생성에 필요한 모든 정보를 수집하여 `ChunkGenerationContext`를 반환한다.

### 우선순위 결정 트리

```
GetContext(coord)
│
├─ 1순위: 이미 등록된 서브청크 (SubChunkRegistry에서 조회)
│         → context.SpecialChunkInstance 설정 후 즉시 반환
│
├─ 2순위: 앵커 스페셜 청크 시도 (SpecialChunkSelector로 결정론적 판단)
│         성공 시 → 멀티청크 인스턴스 생성 + SubChunkInstances 설정
│
├─ 3순위: 서브청크 자리 차단
│         → context.IsBlocked = true 후 반환 (생성 스킵)
│
├─ 4순위: 저장된 청크 복원
│         TryGetChunkData(coord) 성공 시
│         → context.SavedData 설정 (픽셀/pixelInfo 포함)
│
└─ 5순위: 새로운 절차적 청크 생성
          GenerateNewData() 실행
```

### GenerateNewData(): 절차적 데이터 생성

```
1. PixelInfo 배열 초기화 (byte[], 타일 ID 맵)
2. TileDataManager.GetGroundPixels(TileType) → 베이스 픽셀 로드
3. enableLayerBlending == true 이면:
   a. TerrainBlender.ShouldBlendLayer()로 경계 청크 판단
   b. 아래 층 픽셀 로드
   c. TerrainBlender.CreateBlendedPixels() → Perlin Noise 기반 혼합
   d. SecondaryBorderPixels 설정 (경계 텍스처)
4. BorderPixels 로드 (경계 렌더링용)
5. context.GroundPixels = 최종 픽셀 배열
```

**지층 블렌딩 설정:**

```
layerNoiseScale     (0.08)  청크 단위 파동 빈도
layerNoiseAmplitude (2.5)   청크 단위 파동 높이
blendingRatio       (0.4)   혼합 영역 비율 (0~1)
```

---

## 8. 청크 생성 팩토리: ChunkSpawner

### 8.1 SpawnChunk(): 팩토리 위임

```csharp
context.IsSpecialChunk
    ? _specialFactory.CreateChunk(context)
    : _standardFactory.CreateChunk(context)
```

### 8.2 StandardChunkFactory: 일반 청크 생성

```
1. 풀 재사용 시도:
   ChunkPool.HasAvailableChunks()
   └─ true  → Pool.Get() + SetActive(true)
   └─ false → Instantiate(chunkPrefab) + FirstTimeInit()

2. 월드 위치 설정:
   transform.position = ChunkCoords.ToWorld(coord)
   // (coord.x * 10f, coord.y * 10f, 0)

3. 이름 설정:
   gameObject.name = $"Chunk_{coord.x}_{coord.y}"

4. 데이터 초기화 (InitializeChunkData):
   context.IsRestoredFromSave
   ├─ true  → Pixels = savedData.modifiedPixels (복원)
   └─ false → Pixels = context.GroundPixels (새 생성)
   chunk.Reuse_Step1_Prepare(initData)
```

### 8.3 DecorateChunk_Phase2(): 장식물 스폰

```
1. 기존 자식 정리:
   ROCK_*    → RockSpawner.ReturnToPool()
   MINERAL_* → MineralGenerator.ReturnToPool()
   기타       → Destroy()

2. DecorationContext 구성:
   { coord, worldSeed, tileType, hasBeenModified }

3. 데코레이터 순차 실행 (OCP 원칙):
   ElevatorDecorator  → 엘리베이터 샤프트 픽셀 제거 + 오브젝트 스폰
   MineralDecorator   → 광물 오브젝트 배치
   RockDecorator      → 암석 오브젝트 풀링 + 배치
```

---

## 9. LateUpdate: 더티 청크 처리

파기 등으로 픽셀이 변경된 청크는 `MarkChunkDirty(chunk, rect)`로 등록되고, `LateUpdate()`에서 처리된다.

### 2라운드 업데이트 정책

지층 경계 처리의 정확성을 위해 **같은 프레임에 두 번** 시각 업데이트를 실행한다.

```
LateUpdate()
│
├─ 라운드 1:
│   foreach 더티 청크:
│     chunk.DoVisualUpdate(dirtyRect)  부분 or 전체 업데이트
│     chunk.EnsureJobsCompleted()      Job 완료 대기
│
├─ 라운드 2 (이웃 청크 DistanceField 반영 후 재처리):
│   foreach 더티 청크:
│     chunk.DoVisualUpdate(dirtyRect)
│     chunk.EnsureJobsCompleted()
│
├─ 특수 청크 직접 처리:
│   레지스트리 밖 청크 → chunk.ApplyTexture()
│
└─ GPU 텍스처 업로드 (쓰로틀링):
    if (Time.time - _lastApplyTime >= 0.05s)
      foreach 활성 청크:
        if (tc.isTextureDirty) tc.ApplyTexture()
      _lastApplyTime = Time.time
```

**더티 Rect Union 처리:**

```csharp
// 동일 청크에 여러 번 마킹되면 Rect를 합산
if (_dirtyRects.ContainsKey(chunk))
    _dirtyRects[chunk] = Union(existing, newRect);
else
{
    _dirtyRects[chunk] = newRect;
    _dirtyChunksOrdered.Add(chunk);
}
```

---

## 10. 좌표 시스템

### 10.1 좌표계 정의

```
픽셀 좌표    청크 내부 픽셀 단위 (0 ~ 999)
청크 좌표    그리드 단위 정수 (예: (0, -2))
월드 좌표    Unity world units (청크 좌표 × 10f)
배열 인덱스  GridCoordinateSystem 내부 2D 배열 인덱스

PPU = 100 (Pixels Per Unit), 청크 크기 = 1000px = 10 units
```

### 10.2 7층 청크 Y 좌표

| 층 | Y 청크 좌표 | TileType | 이름 |
|----|------------|----------|------|
| 0 | 0 | Dirt | 무른땅 |
| 1 | -2 | HardStone | 단단한땅 |
| 2 | -4 | CoolStone | 서늘한땅 |
| 3 | -6 | Ice | 빙하기땅 |
| 4 | -8 | HotStone | 더운땅 |
| 5 | -10 | MagmaRock | 마그마땅 |
| 6 | -12 | MeteoriteRock | 최종땅 |

### 10.3 ChunkCoords 유틸

```csharp
ChunkCoords.WorldSize = 10f  // 청크 1칸 = 10 world units

ToWorld(Vector2Int coord)    → Vector3(coord.x * 10f, coord.y * 10f, 0)
ToChunk(Vector3 worldPos)    → Vector2Int(Floor(x / 10f), Floor(y / 10f))
```

---

## 11. 메모리 관리 전략

### 11.1 청크 생명주기

```
[미존재]
   ↓ BuildLoadQueue() + PrependSubChunkAnchors()
[로드 큐에 추가]
   ↓ ChunkLoadingRunner.ProcessChunkQueue()
[Phase 1: 스폰 + 픽셀 초기화]
   ↓ Phase 1 Job 완료 대기
[Phase 2: 장식물 스폰]
   ↓ Phase 3: 조명 업데이트
[활성 (ActiveChunkRegistry에 등록)]
   ↓ IsChunkOutOfRange() == true
[언로드 대기]
   ↓ UnloadDistantChunks()
[메모리 캐시에 저장 (enableMemoryCache)]
   ↓ ChunkPool.Return() or Destroy()
[풀 대기 or 파괴]
```

### 11.2 enableMemoryCache vs enableDiskSave

| 설정 | 효과 |
|------|------|
| `enableMemoryCache = true` | 언로드 시 `Dictionary<Vector2Int, ChunkSaveData>`에 보존 → 재방문 시 즉시 복원 |
| `enableDiskSave = true` | F5 또는 종료 시 `worldData.bin`에 영구 저장 |
| 둘 다 false | 언로드 = 파기, 재방문 시 절차적 재생성 (테스트용) |

### 11.3 메모리 캐시 구조

```
Dictionary<Vector2Int, ChunkSaveData>
    Key   = 청크 좌표
    Value = { modifiedPixels: Color32[1000*1000]
              pixelInfo:      byte[1000*1000]
              hasChanges:     bool }

청크 1개 당 약: 4MB (Color32) + 1MB (byte) = 5MB
viewDistance=1 → 최대 9개 활성 청크 → 약 45MB (활성)
```

---

## 12. 성능 최적화 기법

| 기법 | 구현 위치 | 효과 |
|------|-----------|------|
| **청크 풀링** | `ChunkPool` (최대 32개) | `Instantiate`/`Destroy` GC 압력 제거 |
| **O(1) 레지스트리** | `ActiveChunkRegistry` (2D 배열 + HashSet) | 좌표 기반 즉시 청크 조회 |
| **배치 분할** | `ChunkLoadingScheduler` (4개/6ms) | Phase 1을 여러 프레임에 분산 |
| **시간 기반 yield** | `scheduler.ShouldBreakBatch()` | 프레임 드롭 방지 |
| **거리순 정렬** | `SortLoadQueueByDistance()` | 플레이어 근처 청크 먼저 표시 |
| **Job 완료 대기** | Phase 1→2 사이 `while (!finished) yield` | 광물 강제 렉 방지 |
| **텍스처 쓰로틀링** | `LateUpdate` 0.05초 인터벌 | GPU 업로드 최대 20fps 제한 |
| **더티 Rect 컬링** | `MarkChunkDirty(rect)` Rect Union | 불필요한 전체 업데이트 감소 |
| **2라운드 LateUpdate** | `LateUpdate` 라운드1 + 라운드2 | 지층 경계 렌더링 정확도 |
| **Fake-null Lazy 정리** | `ActiveChunkRegistry.Get()` | 파괴된 청크 참조 누수 방지 |

---

## 13. 전체 흐름 시퀀스 다이어그램

### 13.1 초기화 ~ 첫 청크 로드

```
Game Start
    │
    ▼
InfinityMapManager.Awake()
    │ StartCoroutine(InitializeCoroutine())
    ▼
[10단계 초기화]
    │ GridSystem / Pool / Registry / Persistence / LoadingRunner 생성
    ▼
UpdateChunks()   ← 첫 청크 로드 트리거
    │
    ├─ UnloadDistantChunks()    (최초 실행 시 아무 것도 없음)
    ├─ BuildLoadQueue()         viewDistance 범위 좌표 수집
    ├─ UpdateLoadQueue()        거리순 정렬
    ├─ PrependSubChunkAnchors() 멀티청크 앵커 앞에 삽입
    └─ StartLoadingIfNeeded()   ProcessChunkQueue 코루틴 시작
```

### 13.2 청크 1개 생성 상세 (일반 청크, 신규)

```
ProcessChunkQueue [코루틴, Frame N]
    │
    ├─ Phase 1
    │   │
    │   ├─ ChunkDataProvider.GetContext(coord)
    │   │   └─ GenerateNewData()
    │   │       ├─ TileDataManager.GetGroundPixels(TileType)
    │   │       └─ TerrainBlender.CreateBlendedPixels() [지층 경계 시]
    │   │
    │   ├─ ChunkSpawner.SpawnChunk(context)
    │   │   └─ StandardChunkFactory.CreateChunk()
    │   │       ├─ ChunkPool.Get() or Instantiate(chunkPrefab)
    │   │       ├─ transform.position = ChunkCoords.ToWorld(coord)
    │   │       └─ chunk.Reuse_Step1_Prepare(initData)
    │   │           └─ [Unity Job 스케줄링: 픽셀 데이터 초기화]
    │   │
    │   └─ ActiveChunkRegistry.Add(coord, chunk)
    │
    │ yield return null   [Frame N+1]
    │
    ├─ Phase 1 Job 완료 대기
    │   └─ while (chunk.IsJobRunning()) { yield return null }
    │
    ├─ Phase 2  [Frame N+K]
    │   ├─ chunk.Reuse_Step2_Finalize()
    │   └─ ChunkSpawner.DecorateChunk_Phase2()
    │       ├─ ElevatorDecorator.Decorate()
    │       ├─ MineralDecorator.Decorate()
    │       └─ RockDecorator.Decorate()
    │
    └─ Phase 3
        └─ chunk.UpdateBoundaryLighting(true, true, true, true)
            └─ [BFS 라이팅 Job 스케줄링]
```

### 13.3 청크 1개 생성 상세 (캐시에서 복원)

```
ProcessChunkQueue
    │
    ├─ Phase 1
    │   │
    │   ├─ ChunkDataProvider.GetContext(coord)
    │   │   └─ _persistenceSystem.TryGetChunkData(coord) → 성공
    │   │       context.SavedData = { modifiedPixels, pixelInfo }
    │   │       context.IsRestoredFromSave = true
    │   │
    │   └─ StandardChunkFactory.CreateChunk()
    │       └─ initData.Pixels = savedData.modifiedPixels  ← 캐시에서 픽셀 복원
    │
    └─ Phase 2
        └─ DecorateChunk_Phase2()
            └─ context.hasBeenModified == true
               → 엘리베이터 스폰만 (픽셀 제거 스킵: skipPixelClear = true)
```

### 13.4 플레이어 이동 → 청크 갱신

```
Player 이동
    │
    ▼
Update() → HandlePlayerChunkTracking()
    │ 청크 좌표 변경 감지
    ▼
UpdateChunks()
    ├─ UnloadDistantChunks()
    │   └─ viewDistance 초과 청크:
    │       SaveChunkDataToMemory() → ChunkPool.Return()
    │
    └─ BuildLoadQueue() → 새 청크 큐 → LoadingRunner
```

---

## 요약

Large Static Terrain Chunk의 배치 과정을 한 문장으로 정리하면:

> **플레이어가 청크 경계를 넘는 순간** `UpdateChunks()`가 트리거되어, `ChunkLoadingRunner`가 **플레이어에서 가까운 순서로 배치(4개/6ms)** 청크를 로드하며, 각 청크는 **저장 데이터 복원 → 절차적 생성** 우선순위로 픽셀 데이터를 결정하고, **Phase 1(스폰)→Phase 2(장식)→Phase 3(조명)** 3단계를 거쳐 Unity Job과 코루틴으로 **프레임 드롭 없이** 화면에 배치된다.

---

*생성: AI Assistant (Claude) / 2026-03-14*
