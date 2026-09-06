# LargeStaticTerrainChunk 수정 계획
@tags: special-chunk, large-static, fix, plan, TerrainChunk, IChunkInitializer
> 작성일: 2026-03-13
> 우선순위 순 정렬

---

## Fix 1. SubChunkRegistry 언로드 시 정리 [우선순위: 긴급]

### 문제
언로드 시 `ActiveChunkRegistry`만 정리되고 `SubChunkRegistry._anchorMap`은 방치된다.
재방문 시 `TrySelect`의 exclusion zone 체크(`registry.Contains`)가 stale 항목에 걸려 스폰 실패.

**증상:** 처음 방문 성공 → 멀리 이동 → 돌아오면 빈 공간

### 수정 내용

**SubChunkRegistry.cs** — `UnregisterByAnchor` 메서드 추가
```csharp
/// <summary>앵커 언로드 시 해당 앵커의 모든 서브슬롯 예약을 해제한다.</summary>
public void UnregisterByAnchor(Vector2Int anchorCoord)
{
    var toRemove = new List<Vector2Int>();
    foreach (var kv in _anchorMap)
        if (kv.Value == anchorCoord) toRemove.Add(kv.Key);
    foreach (var k in toRemove)
    {
        _anchorMap.Remove(k);
        _instances.Remove(k);
    }
}
```

**InfinityMapManager.cs** — `UnloadDistantChunks`에서 LSTC 언로드 시 호출
```csharp
// Destroy(chunk.gameObject) 전에 추가
if (chunk is LargeStaticTerrainChunk)
    SpecialChunkManager.Instance?._registry.UnregisterByAnchor(coord);
```

단, `_registry` 필드가 현재 `private`이므로 `SpecialChunkManager`에 public 메서드 래퍼 추가 필요:
```csharp
// SpecialChunkManager.cs
public void UnregisterSubChunksForAnchor(Vector2Int anchorCoord)
    => _registry.UnregisterByAnchor(anchorCoord);
```

---

## Fix 2. 크기 설정 단일화 [우선순위: 높음]

### 문제
청크 크기가 두 곳에 분리 정의되어 있다.

| 위치 | 필드 | 역할 |
|------|------|------|
| 프리팹 컴포넌트 | `chunkGridWidth/Height` | `Width/Height` 프로퍼티 계산 (언로드 범위 판정) |
| SpecialChunkManager Inspector | `chunkSizeX/Y` | 서브슬롯 예약, exclusion zone 계산 |

둘이 불일치하면 서브슬롯 차단 범위와 스프라이트 커버 범위가 달라져 빈 공간 발생.

### 수정 방향

`SpecialChunkManager`가 크기 정보를 프리팹의 `LargeStaticTerrainChunk` 컴포넌트에서 직접 읽도록 변경.

**SpecialChunkManager.cs** — `SpecialChunkDef`에서 `chunkSizeX/Y` 제거 후 런타임 계산으로 대체
```csharp
// SpecialChunkDef에서 chunkSizeX/Y 필드 제거 (또는 deprecated 처리)

// GetSize 헬퍼 수정 (SpecialChunkSelector.cs)
private static Vector2Int GetSize(SpecialChunkManager.SpecialChunkDef def)
{
    if (def.prefab is LargeStaticTerrainChunk lstc)
        return new Vector2Int(lstc.chunkGridWidth, lstc.chunkGridHeight);
    return Vector2Int.one; // 일반 TerrainChunk 기반 특수 청크
}
```

이렇게 하면 프리팹의 `chunkGridWidth/Height`가 유일한 진실의 원천(Single Source of Truth)이 된다.

---

## Fix 3. IChunkInitializer 호출 순서 [우선순위: 중간]

### 문제
`SpecialChunkManager.SpawnSpecialChunkIfPossible`에서 `SetActive(false)` 이후에 `IChunkInitializer.Initialize`를 호출한다.

```csharp
anchorObj.SetActive(false);                           // 비활성화
foreach (var init in GetComponents<IChunkInitializer>())
    init.Initialize(parent);                          // 비활성 상태에서 호출
```

`Initialize` 내부에서 `OnEnable`, `Start` 등 Unity 이벤트에 의존하는 코드가 있으면 동작하지 않는다.

### 수정 내용

호출 순서 변경: `IChunkInitializer.Initialize` → `SetActive(false)` 순으로 바꾼다.

```csharp
// IChunkInitializer 먼저
foreach (var initializer in anchorObj.GetComponents<IChunkInitializer>())
    initializer.Initialize(parent);

// 그 다음 비활성화
anchorObj.SetActive(false);
```

---

## Fix 4. Phase 1에서 불필요한 SetActive(false) 제거 검토 [우선순위: 낮음]

### 문제
Phase 1에서 `SetActive(false)`를 명시적으로 하고, Phase 2에서 `SetActive(true)`로 되돌린다.
이 패턴의 목적이 불명확하고, Phase 2가 유일한 활성화 경로라는 취약성을 만든다.

`TerrainChunk`는 Phase 1 완료 시점에 픽셀 Job이 아직 실행 중이라 시각적으로 깜빡임 방지를 위해 비활성 상태로 두는 이유가 있다. 그러나 `LargeStaticTerrainChunk`는 Job이 없으므로 같은 이유가 적용되지 않는다.

### 수정 방향

`SpawnSpecialChunkIfPossible`에서 `LargeStaticTerrainChunk`인 경우 `SetActive(false)` 스킵:

```csharp
// LargeStaticTerrainChunk는 Job 없음 → 즉시 표시 가능
if (anchorChunk is not LargeStaticTerrainChunk)
    anchorObj.SetActive(false);
```

그렇게 하면 Phase 2의 `SetActive(true)`도 제거 가능. Phase 2에서 LSTC 분기 자체를 없앨 수 있다.

> **주의:** `IChunkInitializer.Initialize`가 활성 상태에 의존하는지 먼저 확인 후 적용.

---

## Fix 5. ActiveChunkRegistry의 fake-null 취약성 [우선순위: 낮음]

### 문제
`ActiveChunkRegistry.HasChunk`가 `Get(coord) != null`로 판정하는데, 인터페이스 레퍼런스라 파괴된 Unity 오브젝트를 null로 감지 못한다.

```csharp
// 현재
public bool HasChunk(Vector2Int coord) => Get(coord) != null;

// Get은 2D 배열에서 꺼낸 IChunk 레퍼런스를 그대로 반환
// 오브젝트가 Destroy됐어도 인터페이스 레퍼런스는 null이 아님
```

### 수정 방향

```csharp
public bool HasChunk(Vector2Int coord)
{
    var chunk = Get(coord);
    if (chunk == null) return false;
    var unityObj = chunk as UnityEngine.Object;
    if (unityObj != null && unityObj == null) // fake-null 감지
    {
        Remove(coord); // lazy 정리
        return false;
    }
    return true;
}
```

또는 `Get` 자체에서 lazy 정리:
```csharp
public IChunk Get(Vector2Int coord)
{
    // ...배열 접근...
    var chunk = _activeChunks[x, y];
    if (chunk == null) return null;
    var unityObj = chunk as UnityEngine.Object;
    if (unityObj != null && unityObj == null) // fake-null
    {
        _activeChunks[x, y] = null;
        _activeCoordinateSet.Remove(coord);
        _activeCoordinates.Remove(coord);
        return null;
    }
    return chunk;
}
```

---

## 수정 우선순위 요약

| # | 대상 | 파일 | 우선순위 | 현재 버그 유발 여부 |
|---|------|------|----------|-------------------|
| 1 | SubChunkRegistry 언로드 정리 | SubChunkRegistry, SpecialChunkManager, InfinityMapManager | **긴급** | ✅ 현재 스폰 실패 버그 |
| 2 | 크기 설정 단일화 | SpecialChunkSelector, SpecialChunkDef | **높음** | ⚠️ 설정 실수 시 빈 공간 |
| 3 | IChunkInitializer 호출 순서 | SpecialChunkManager | **중간** | ⚠️ 초기화 컴포넌트 있으면 잠재 버그 |
| 4 | SetActive 불필요 제거 | SpecialChunkManager, ChunkGenerationPipeline | **낮음** | ❌ 현재 문제 없음 |
| 5 | ActiveChunkRegistry fake-null | ActiveChunkRegistry | **낮음** | ❌ 현재 문제 없음 (언로드 수정으로 완화됨) |

---

## 수정 후 예상 흐름

```
언로드 시:
  Destroy(gameObject)
  ActiveChunkRegistry.Remove(anchorCoord)       ← 기존
  SubChunkRegistry.UnregisterByAnchor(anchorCoord) ← 추가 (Fix 1)

재방문 시:
  TrySelect(anchorCoord)
  → exclusionBuffer 순회
  → registry.Contains(subSlotCoord) = false    ← stale 항목 없음
  → 스폰 성공
```
