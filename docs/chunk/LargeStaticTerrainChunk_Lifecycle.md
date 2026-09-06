# LargeStaticTerrainChunk 생명주기 분석
@tags: special-chunk, large-static, lifecycle, InfinityMapManager, SpecialChunkManager, SubChunkRegistry
> 작성일: 2026-03-13
> 대상 파일: InfinityMapManager, SpecialChunkManager, ChunkGenerationPipeline, SubChunkRegistry

---

## 1. 개요

`LargeStaticTerrainChunk`는 픽셀 데이터가 없는 **정적 스프라이트 전용** 특수 청크다.
일반 `TerrainChunk`와 동일한 `IChunk` 인터페이스를 구현하지만, 내부 처리 경로가 전혀 다르다.

```
IChunk
├── TerrainChunk         → 픽셀 배열, ChunkData, Job, 저장/로드
└── LargeStaticTerrainChunk → SpriteRenderer + PolygonCollider2D만 존재, 데이터 없음
```

---

## 2. 로딩 전체 흐름

```
Update()
└── HandlePlayerChunkTracking()        플레이어가 새 청크 좌표로 이동 시
    └── UpdateChunks()
        ├── UnloadDistantChunks()
        ├── BuildLoadQueue()           viewDistance 범위 좌표 목록 생성
        ├── _loadingRunner.UpdateLoadQueue(queue)
        ├── PrependSubChunkAnchors(queue)   ← 서브슬롯 감지 → 앵커를 큐 앞으로
        └── _loadingRunner.StartLoadingIfNeeded()
            └── ProcessChunkQueue() [코루틴]
                ├── Phase 1: SpawnAndInitialize
                ├── (Job 대기 - TerrainChunk 전용, LSTC는 즉시 통과)
                ├── Phase 2: FinalizeAndDecorate
                └── Phase 3: UpdateLighting
```

---

## 3. Phase별 LargeStaticTerrainChunk 처리

### Phase 1 — Spawn (스폰 및 등록)

```
ChunkGenerationPipeline.ExecutePhase1_SpawnAndInitialize(coord)
└── ChunkDataProvider.GetContext(coord)
    ├── [1-A] TryGetRegisteredSubChunk   → 이미 등록된 서브청크면 해당 인스턴스 반환
    ├── [1-B] SpawnSpecialChunkIfPossible
    │   ├── SpecialChunkSelector.TrySelect()   확률+제약 검사
    │   ├── Instantiate(prefab)                프리팹 인스턴스화
    │   ├── LargeStaticTerrainChunk.Initialize(coord)
    │   ├── anchorObj.SetActive(false)         ← 여기서 비활성화
    │   ├── IChunkInitializer.Initialize(parent) (비활성 상태에서 호출)
    │   └── RegisterReserved(subCoord, anchorCoord) per 서브슬롯
    └── context.SpecialChunkInstance = LargeStaticTerrainChunk 반환
└── ChunkSpawner.SpawnChunk(context)
    └── SpecialChunkFactory.CreateChunk(context)
        └── context.SpecialChunkInstance 그대로 반환 (주입 로직 없음)
└── _chunkRegistry.Add(coord, chunk)   ActiveChunkRegistry에 등록
    ※ SetActive(false) 상태로 등록됨
```

**Phase 1 완료 시점의 상태:**
- `ActiveChunkRegistry`: coord → LSTC 등록됨
- `SubChunkRegistry._anchorMap`: 서브슬롯 coord들 → anchorCoord 등록됨
- GameObject: `SetActive(false)` → 화면에 안 보임

---

### Phase 1.5 — Job 대기 (통과)

```csharp
// ChunkLoadingRunner.cs:186-204
while (!p1JobsFinished) {
    var tc = pair.Value as TerrainChunk;
    if (tc != null && tc.IsJobRunning()) { ... }  // LSTC는 TerrainChunk가 아니므로 즉시 통과
}
```

`LargeStaticTerrainChunk`는 Job이 없으므로 대기 없이 Phase 2로 진행.

---

### Phase 2 — Finalize (활성화)

```csharp
// ChunkGenerationPipeline.cs:120-124
if (chunk is LargeStaticTerrainChunk staticChunk)
{
    chunk.gameObject.SetActive(true);   ← 여기서 비로소 화면에 표시됨
    return;                             ← 장식물(암석/광물/엘리베이터) 전혀 없음
}
```

**Phase 2에서 하는 일이 오직 `SetActive(true)` 하나다.**
TerrainChunk와 달리 `Reuse_Step2_Finalize()`, `DecorateChunk_Phase2()` 호출 없음.

---

### Phase 3 — 조명 (스킵)

```csharp
// ChunkGenerationPipeline.cs:140-143
if (chunk.gameObject.activeSelf && chunk is TerrainChunk tChunk)
{
    tChunk.UpdateBoundaryLighting(...);
}
// LargeStaticTerrainChunk → TerrainChunk 아님 → 완전 스킵
```

조명 계산 없음. 스프라이트 자체에 미리 그려진 조명을 사용해야 함.

---

## 4. 언로딩 전체 흐름

```
UnloadDistantChunks()
└── for each coord in ActiveChunkRegistry
    └── IsChunkOutOfRange(coord)
        └── [LSTC 전용] IsFootprintOutOfRange(coord, sx, sy)
            sx = chunk.Width / 1000    ← LargeStaticTerrainChunk.chunkGridWidth
            sy = chunk.Height / 1000   ← LargeStaticTerrainChunk.chunkGridHeight
            footprint 전체가 viewDistance 초과할 때만 unload (일부 걸치면 유지)
    └── if out of range:
        ├── (enableMemoryCache && TerrainChunk) → 저장  ← LSTC는 저장 없음
        ├── (TerrainChunk) → _chunkPool.Return()
        ├── (그 외 = LSTC) → Destroy(chunk.gameObject)
        └── _chunkRegistry.Remove(coord)
```

---

## 5. 서브슬롯(SubChunkRegistry)과의 관계

`chunkSizeX > 1`이면 앵커 스폰 시 인접 좌표들을 서브슬롯으로 예약한다.

```
앵커 coord=(-4,-2), chunkSizeX=2 → 서브슬롯 (-3,-2) 등록
SubChunkRegistry._anchorMap[(-3,-2)] = (-4,-2)
```

서브슬롯 좌표로 청크 로딩이 시도될 경우:

```
ChunkDataProvider.GetContext((-3,-2))
└── [1-A2] IsSubChunkCoord → IsBlocked = true → return
ChunkGenerationPipeline: context.IsBlocked → return null   (일반 지형 생성 안 됨)
```

서브슬롯은 `ActiveChunkRegistry`에 절대 등록되지 않는다.
`LargeStaticTerrainChunk`의 스프라이트가 그 영역을 시각적으로 덮어야 한다.

---

## 6. PrependSubChunkAnchors 메커니즘

플레이어가 앵커 밖 서브슬롯에 먼저 도달하는 경우를 처리한다.

```
BuildLoadQueue() → 서브슬롯 (-3,-2)가 viewDistance 안에 들어옴
PrependSubChunkAnchors(queue)
└── IsSubChunkCoord((-3,-2)) → true
└── TryGetSubChunkAnchorCoord((-3,-2)) → anchorCoord = (-4,-2)
└── (-4,-2)가 registry에 없고 queue에도 없으면 → queue 맨 앞에 삽입
ProcessChunkQueue: (-4,-2) 먼저 처리 → LSTC 스폰 → (-3,-2)는 IsBlocked로 skip
```

앵커가 viewDistance 밖에 있어도 서브슬롯이 보이면 앵커를 먼저 로드한다.

---

## 7. 저장/로드 비교

| 항목 | TerrainChunk | LargeStaticTerrainChunk |
|------|-------------|------------------------|
| 픽셀 데이터 저장 | ✅ `WorldPersistenceSystem` | ❌ 없음 |
| 메모리 캐시 | ✅ `enableMemoryCache` | ❌ 없음 |
| 디스크 저장 | ✅ F5 저장 | ❌ 없음 |
| 재로드 시 복원 | ✅ `ChunkSaveData` | ❌ 매번 새로 Instantiate |
| 오브젝트 풀 | ✅ `ChunkPool` | ❌ Destroy/Instantiate |

LSTC는 언로드 시 `Destroy`, 재방문 시 `Instantiate`가 반복된다.
**시드가 고정이므로 항상 동일한 위치에 스폰된다.**

---

## 8. 알려진 문제점

### ⚠️ 문제 1: SubChunkRegistry 미정리 (현재 미해결)

언로드 시 `SubChunkRegistry._anchorMap` 항목이 제거되지 않는다.

```
1회차: (-4,-2) 스폰 → RegisterReserved((-3,-2), (-4,-2)) → _anchorMap에 등록
       이동 → 언로드 → Destroy + ActiveChunkRegistry.Remove
       ⚠️ _anchorMap[(-3,-2)] = (-4,-2) 는 남아있음

2회차: TrySelect((-4,-2)) 호출
       → exclusionBuffer에 어떤 coord X가 있고
       → registry.Contains(X) = true (stale entry)
       → return null → 스폰 실패
```

**증상:** 처음 방문 시 스폰 성공, 멀리 갔다가 돌아오면 스폰 실패.
**진단:** `[EXCL_BLOCK]` 키워드로 Console 검색.

**픽스 방향:**
```csharp
// SubChunkRegistry에 추가 필요
public void UnregisterByAnchor(Vector2Int anchorCoord)
{
    var toRemove = _anchorMap.Where(kv => kv.Value == anchorCoord)
                             .Select(kv => kv.Key).ToList();
    foreach (var k in toRemove) { _anchorMap.Remove(k); _instances.Remove(k); }
}

// InfinityMapManager.UnloadDistantChunks 에서 호출
if (chunk is LargeStaticTerrainChunk)
    SpecialChunkManager.Instance?._registry.UnregisterByAnchor(coord);
```

---

### ✅ 문제 2: IChunk fake-null 체크 오류 (수정됨)

`if (chunk != null)` → 인터페이스 레퍼런스라 Unity fake-null 감지 불가.
→ `var unityObj = chunk as UnityEngine.Object; if (unityObj != null)` 로 수정됨.
`_chunkRegistry.Remove(coord)`는 null 여부와 관계없이 항상 실행되도록 수정됨.

---

## 9. 전체 상태 다이어그램

```
[Instantiate + Initialize]
        │
        ▼
SetActive(false) ──── Phase 1 완료, ActiveChunkRegistry 등록
        │
        ▼
SetActive(true)  ──── Phase 2 완료, 화면에 표시
        │
  [플레이어 이동]
        │
        ▼
IsFootprintOutOfRange? ──No──→ 유지
        │Yes
        ▼
Destroy(gameObject)
ActiveChunkRegistry.Remove
⚠️ SubChunkRegistry._anchorMap 미정리 ← 버그
```

---

## 10. Phase 2가 호출되지 않으면?

`SetActive(false)` 상태로 `ActiveChunkRegistry`에 등록은 되어 있지만 보이지 않는다.
`BuildLoadQueue`는 `hasChunk=True`이므로 다시 로드 시도를 하지 않는다.
→ **한 번 Phase 2가 누락되면 그 세션 동안 영구히 안 보인다.**

Phase 2 누락 가능 경로:
- `Phase2_FinalizeAndDecorate`에서 예외 발생 → `OnPhase2_FinalizeError` → `_registry.Remove` 후 루프 계속
- 단, 이 경우 `ActiveChunkRegistry`에서도 제거되므로 다음 프레임에 재시도됨

---

*이 문서는 코드 정적 분석 기반이며, 런타임 동작과 다를 수 있습니다.*
