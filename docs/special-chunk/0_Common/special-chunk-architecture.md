# 특수 청크 시스템 아키텍처
@tags: special-chunk, architecture, IChunkInitializer, SpecialChunkFactory, SpecialChunkManager, lifecycle, SpawnSpecialChunk

> 마지막 업데이트: 2026-05-09
> 대상: 특수 청크 시스템 구조를 이해하고 싶은 작업자
> 코드 기반 정리 (실제 코드 동작 기준)

---

## 1. 컴포넌트 책임 분리 (SRP)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `SpecialChunkManager` | SpecialChunkManager.cs | 퍼사드. 외부 API 노출, 선택·레지스트리에 위임 |
| `SpecialChunkSelector` | Core/SpecialChunkSelector.cs | 확률 선택 + 간격·경계 제약 판별 |
| `SubChunkRegistry` | Core/SubChunkRegistry.cs | 서브청크 좌표 등록·조회·해제 (상태만) |
| `SpecialChunkFootprint` | Core/SpecialChunkFootprint.cs | 풋프린트·서브좌표·앵커 역산 (순수 수학) |
| `SpriteCavityInitializer` | Behaviours/SpriteCavityInitializer.cs | 스프라이트 픽셀 → TerrainChunk BasePixels 주입 |
| `TrashWallGenerator` | Core/TrashWallGenerator.cs | 블록 패턴 배치 (Instantiate 방식) |
| `IndestructibleOverlayInit` | Behaviours/IndestructibleOverlayInit.cs | IndestructibleMask 마킹 |
| `LargeStaticTerrainChunk` | Chunk/LargeStaticTerrainChunk.cs | 파기 불가 정적 대형 청크 |
| `TerrainChunk` | Chunk/TerrainChunk.cs | 파기 가능 청크 (일반 + 특수 공용) |

---

## 2. 특수 청크 타입 분류

```
특수 청크
├─ 패턴 A: TerrainChunk + SpriteCavityInitializer (루트)
│           → 파기 가능. 스프라이트 투명 픽셀 = 공동.
│           → NeedsDelayedActivation=true, IChunkInitializer 루트에 있음
│           → SetActive(false) 안 함 (조건 불충족)
│
├─ 패턴 B: LargeStaticTerrainChunk (루트)
│           → 파기 불가, 픽셀 데이터 없음, 메모리 절약.
│           → NeedsDelayedActivation=false
│           → SetActive(false) 안 함 (NeedsDelayedActivation=false)
│
├─ 패턴 C: TerrainChunk + SpriteCavityInitializer + IndestructibleOverlayInit (자식)
│           → 파기 가능 지형 + 파기 불가 영역 공존. IndestructibleMask 사용.
│           → 패턴 A와 SetActive 동작 동일
│
└─ 패턴 D: LargeStaticTerrainChunk (앵커) + 자식 TerrainChunk (서브청크)
            → 대형 정적 배경 + 일부 파기 가능 칸의 하이브리드.
            → 서브청크가 SubChunkRegistry.Register로 등록되어 파기·조명·세이브 활성화.
```

---

## 3. SetActive(false) 조건 (중요)

`SpecialChunkManager.SpawnSpecialChunkIfPossible` 내부:

```csharp
if (anchorChunk.NeedsDelayedActivation && anchorObj.GetComponent<IChunkInitializer>() == null)
    anchorObj.SetActive(false);
```

| 청크 타입 | NeedsDelayedActivation | 루트에 IChunkInitializer | SetActive(false)? |
|-----------|------------------------|--------------------------|-------------------|
| TerrainChunk + SpriteCavityInitializer | true | 있음 | **안 함** |
| TerrainChunk (IChunkInitializer 없음) | true | 없음 | 함 (비정상 구성) |
| LargeStaticTerrainChunk | false | 없음 | **안 함** |

→ 현재 정상 구성에서는 **특수 청크에 SetActive(false)가 호출되지 않는다.**

---

## 4. IChunkInitializer 실행 순서

```csharp
// SpecialChunkManager.SpawnSpecialChunkIfPossible 내부
var initializers = anchorObj.GetComponentsInChildren<IChunkInitializer>();
System.Array.Sort(initializers, (a, b) => a.InitializationOrder.CompareTo(b.InitializationOrder));
foreach (var initializer in initializers)
    initializer.Initialize(parent);
```

- `GetComponentsInChildren`: **루트 포함** 자식까지 탐색
- `InitializationOrder` 오름차순 정렬 후 실행

패턴 C 실행 순서 (SpriteCavityInitializer.Order=0 < IndestructibleOverlayInit.Order=1 기준):

```
[1] SpriteCavityInitializer.Initialize()   → 투명 px = air, 불투명 px = 지형 색
[2] IndestructibleOverlayInit.Initialize() → 오버레이 불투명 px를 BasePixels에 복원
                                             + IndestructibleMask[idx] = 1
```

순서가 바뀌면 공동이 오버레이를 덮어 마스크 무효.

---

## 5. 스폰 파이프라인 전체 흐름

```
InfinityMapManager.Update()
  └─ UpdateChunks() → BuildLoadQueue() → loadingRunner.UpdateLoadQueue()
                                      └─ StartLoadingIfNeeded() → ProcessChunkQueue() [코루틴]

ProcessChunkQueue() [ChunkLoadingRunner]
  │
  ├─ PHASE 1: Spawn & Initialize
  │   for each coord in batch:
  │     ChunkGenerationPipeline.ExecutePhase1_SpawnAndInitialize(coord)
  │       └─ ChunkDataProvider.GetContext(coord)
  │             ├─ [1-A] SubChunkRegistry에 등록된 서브청크? → 인스턴스 반환
  │             ├─ [Save] wasNormalChunk 저장 데이터 있음? → 저장 데이터 반환
  │             ├─ [1-B] SpecialChunkManager.SpawnSpecialChunkIfPossible()
  │             │           SpecialChunkSelector.TrySelect() (확률 + 간격 + 경계 체크)
  │             │           당첨 → Instantiate(prefab)
  │             │                  IChunkInitializer 정렬 후 Initialize()
  │             │                  조건부 SetActive(false) ← 현재는 안 함
  │             │                  멀티청크 → SubChunkRegistry 등록
  │             ├─ [1-A2] IsSubChunkCoord? → context.IsBlocked = true
  │             └─ [일반] GenerateNewData() → 지형 픽셀 생성
  │           ChunkSpawner.SpawnChunk(context)
  │             └─ IsSpecialChunk? → SpecialChunkFactory (인스턴스 재사용, provider/player 주입)
  │                                → StandardChunkFactory (풀 재사용 또는 신규 Instantiate + Reuse_Step1_Prepare)
  │           ActiveChunkRegistry.Add(coord, chunk)
  │           (SubChunkInstances 있으면 즉시 레지스트리 등록 + Reuse_Step2_Finalize)
  │
  ├─ PHASE 1 Job 대기
  │   while (!p1JobsFinished):
  │     for each tc: if tc.IsJobRunning() → 대기
  │   ※ 특수 청크는 Reuse_Step1_Prepare 미호출 → ScheduleInitJobOnly 미호출
  │     → IsJobRunning() = false → 즉시 통과
  │
  ├─ PHASE 2: Finalize & Decorate
  │   for each chunk:
  │     ExecutePhase2_DecoratorSteps(coord, chunk)
  │       ├─ tChunk.Reuse_Step2_Finalize()
  │       │     → gameObject.SetActive(true)  (이미 active면 그대로)
  │       │     → UpdateCollider()            (콜라이더 즉시 생성)
  │       │     → IsColliderDirty = false
  │       └─ isSpecialPrebuilt = (IChunkInitializer 컴포넌트가 있으면 true)
  │             true  → DecorateChunk_Phase2 스킵 (자식 보존, 랜덤 암석·광물 없음)
  │             false → DecorateChunk_Phase2 실행 (랜덤 암석·광물·엘리베이터 배치)
  │
  ├─ PHASE 2.5: Visual Job 예약
  │   for each tc: tc.FinishVisualsAfterInit()
  │     → UpdateVisualsFull(skipInit=true) → Chamfer + Visual Job 예약 (비블로킹)
  │
  └─ PHASE 3: Lighting
      for each chunk: ExecutePhase3_UpdateLighting()
        → UpdateBoundaryLighting(true, true, true, true)
        → InfinityMapManager.MarkChunkDirty() → ProcessDirtyChunksAsync (LateUpdate)
      Phase 3 Job 완료 대기 (비블로킹)
```

---

## 6. SpecialChunkSelector 선택 로직

```
TrySelect(coord, layerType, worldSeed, registry)
  │
  ├─ TrySelectRaw() — 순수 확률 체크
  │     layerType으로 pool 찾기
  │     hash = CoordHash(coord, worldSeed)  = (x*73856093)^(y*19349663)^(seed*83492791)
  │     PRNG(hash) → 각 SpecialChunkDef.spawnChance로 롤 (0~100)
  │     깊이 제한(minDepth/maxDepth) 체크
  │     → 당첨 def 반환 또는 null
  │
  ├─ footprint 계산 (앵커 + 크기)
  ├─ exclusion zone = footprint + minChunkSpacing 확장
  │
  └─ exclusion zone 내 충돌 체크:
       SubChunkRegistry에 이미 등록된 좌표 → null
       이웃 앵커 후보: CoordHash 비교 → 더 높은 해시 우선 (결정론적 충돌 해결)
       → 최종 def 반환
```

**결정론적 보장**: 같은 `worldSeed`와 `coord`면 항상 같은 결과.

---

## 7. SubChunkRegistry 구조

```
_instances  : Dict<coord, TerrainChunk>     — 실제 인스턴스 (파기·조명·세이브)
_anchorMap  : Dict<coord, anchorCoord>      — 모든 예약 좌표 → 앵커 역산
_reverseMap : Dict<anchorCoord, List<coord>> — 앵커 → 서브좌표 목록 (역인덱스)
```

- `Register`: 인스턴스 + 좌표 등록 (파기 가능 서브청크)
- `RegisterReserved`: 좌표만 등록 (정적 영역, 일반 청크 생성 차단용)
- `UnregisterByAnchor`: 앵커 언로드 시 O(k)로 서브슬롯 일괄 해제

---

## 8. 멀티청크 좌표 규칙

```
앵커: coord = (ax, ay)  ← 좌상단
서브청크: (ax+dx, ay+dy)  where dx > 0, dy < 0

예: anchor=(2,-3), size=(3,2)
  → 점유 좌표: (2,-3) (3,-3) (4,-3)
               (2,-4) (3,-4) (4,-4)

Y축: 지하 = 음수 (아래 방향이 음수 Y)
```

자식 TerrainChunk의 로컬 좌표:
- `ChunkCoords.WorldSize = 10` (1000px ÷ 100PPU = 10 units)
- 서브청크 (+1, 0) → `localPosition = (10, 0)`
- 서브청크 (+2, -1) → `localPosition = (20, -10)`

---

## 9. Phase 2에서 자식 보존 조건

`isSpecialPrebuilt = (tChunk.GetComponent<IChunkInitializer>() != null)`

| 조건 | 결과 |
|------|------|
| 루트에 `IChunkInitializer` 있음 | `DecorateChunk_Phase2` 스킵 → 프리팹 자식 그대로 보존 |
| 루트에 `IChunkInitializer` 없음 | `DecorateChunk_Phase2` 실행 → 랜덤 암석·광물 배치 |

`DecorateChunk_Phase2` 내부의 자식 파괴 로직:
```csharp
if (child.name.StartsWith("ROCK_"))  → 풀 반납
else if (child.name.StartsWith("MINERAL_")) → 풀 반납
else → Destroy(child)   ← 이름 규칙 없는 자식은 파괴됨
```

→ 특수 청크 자식은 `ROCK_`, `MINERAL_` 접두사를 **사용하지 말 것**.

---

## 10. 언로드 시 주의사항

앵커 청크가 언로드될 때 반드시:
```csharp
SpecialChunkManager.Instance.UnregisterSubChunksForAnchor(anchorCoord);
```
미호출 시 SubChunkRegistry에 stale 예약이 남아 재방문 시 서브 좌표 스폰 실패.

---

## 관련 문서

| 문서 | 내용 |
|------|------|
| `special-chunk-editor-setup-guide.md` | 패턴 A/B/C/D 에디터 설정 |
| `large-static-hybrid-diggable.md` | 패턴 D 상세 (하이브리드) |
| `compressedtrashwall-prefab-setup.md` | CompressedTrashWall 프리팹 구성 |
| `compressedtrashwall-spawn-flow.md` | CompressedTrashWall 스폰 흐름 |
| `special-chunk-settings-json.md` | specialChunkSettings.json 설정 |
