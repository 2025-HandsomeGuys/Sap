# Linked Chunk System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 멀티타일 구조물을 독립 TerrainChunk 앵커들의 링크로 구현한다. 앵커 로드 여부와 무관하게 각 피스가 결정론적 역산으로 자기 역할을 판단한다.

**Architecture:** LinkedChunkRegistry가 앵커 스폰 시 피스 좌표를 캐싱한다. ChunkDataProvider는 캐시를 우선 조회하고, 캐시 미스(앵커가 로드 범위 밖 등)에는 SpecialChunkSelector의 역산으로 폴백한다. 피스는 IChunkInitializer 없는 일반 TerrainChunk로 동작하여 기존 파이프라인을 그대로 탄다.

**Tech Stack:** Unity 2D, C#, 기존 특수청크 파이프라인 (SpecialChunkManager, ChunkDataProvider, StandardChunkFactory)

**설계 문서:** `Assets/Docs/linked-chunk-system-design.md`

---

## 파일 목록

| 역할 | 파일 |
|------|------|
| **신규** 링크 피스 캐시 레지스트리 | `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Core/LinkedChunkRegistry.cs` |
| **신규** 레지스트리 단위 테스트 | `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Tests/LinkedChunkRegistryTests.cs` |
| **수정** 역산 메서드 추가 + exclusion zone | `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Core/SpecialChunkSelector.cs` |
| **수정** LinkedPiece 구조체, 스폰·언로드·API | `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs` |
| **수정** LinkedPiecePrefab 필드 추가 | `Assets/Scripts/Gameplay/Terrain/Tiles/Generation/Pipeline/ChunkGenerationContext.cs` |
| **수정** 링크 피스 전용 프리팹 처리 | `Assets/Scripts/Gameplay/Terrain/Tiles/Generation/Pipeline/StandardChunkFactory.cs` |
| **수정** GetContext 캐시·역산 경로 추가 | `Assets/Scripts/Gameplay/Terrain/Tiles/Generation/Pipeline/ChunkDataProvider.cs` |

---

## Task 1: LinkedChunkRegistry

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Core/LinkedChunkRegistry.cs`
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Tests/LinkedChunkRegistryTests.cs`

- [ ] **Step 1: LinkedChunkRegistry 파일 생성**

```csharp
// @tags: special-chunk, chunk, registry, linked-piece
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 링크 피스 좌표→(프리팹, 앵커) 매핑 캐시.
/// 앵커 스폰 시 채워지며, 비어있어도 SpecialChunkSelector 역산으로 시스템이 동작한다.
/// SubChunkRegistry와 동일한 패턴, 역인덱스(_reverseMap)로 UnregisterByAnchor O(k) 보장.
/// </summary>
public class LinkedChunkRegistry
{
    private readonly Dictionary<Vector2Int, (MonoBehaviour prefab, Vector2Int anchor)> _links
        = new Dictionary<Vector2Int, (MonoBehaviour prefab, Vector2Int anchor)>();

    private readonly Dictionary<Vector2Int, List<Vector2Int>> _reverseMap
        = new Dictionary<Vector2Int, List<Vector2Int>>();

    public void Register(Vector2Int coord, MonoBehaviour prefab, Vector2Int anchor)
    {
        _links[coord] = (prefab, anchor);
        if (!_reverseMap.TryGetValue(anchor, out var list))
        {
            list = new List<Vector2Int>();
            _reverseMap[anchor] = list;
        }
        if (!list.Contains(coord))
            list.Add(coord);
    }

    public bool TryGet(Vector2Int coord, out MonoBehaviour prefab, out Vector2Int anchor)
    {
        if (_links.TryGetValue(coord, out var entry))
        {
            prefab = entry.prefab;
            anchor = entry.anchor;
            return true;
        }
        prefab = null;
        anchor = default;
        return false;
    }

    public bool Contains(Vector2Int coord) => _links.ContainsKey(coord);

    public void UnregisterByAnchor(Vector2Int anchor)
    {
        if (!_reverseMap.TryGetValue(anchor, out var coords)) return;
        foreach (var coord in coords)
            _links.Remove(coord);
        _reverseMap.Remove(anchor);
    }
}
```

- [ ] **Step 2: EditMode 테스트 파일 생성**

```csharp
using NUnit.Framework;
using UnityEngine;

public class LinkedChunkRegistryTests
{
    private LinkedChunkRegistry _registry;
    private Vector2Int _anchor;
    private Vector2Int _pieceB;
    private Vector2Int _pieceC;

    [SetUp]
    public void SetUp()
    {
        _registry = new LinkedChunkRegistry();
        _anchor = new Vector2Int(0, 0);
        _pieceB = new Vector2Int(1, 0);
        _pieceC = new Vector2Int(2, 0);
    }

    [Test]
    public void Register_ThenTryGet_ReturnsRegisteredData()
    {
        var prefabGO = new GameObject("TestPrefab");
        var mb = prefabGO.AddComponent<MeshRenderer>() as MonoBehaviour;
        _registry.Register(_pieceB, mb, _anchor);

        bool found = _registry.TryGet(_pieceB, out var outPrefab, out var outAnchor);

        Assert.IsTrue(found);
        Assert.AreEqual(mb, outPrefab);
        Assert.AreEqual(_anchor, outAnchor);
        Object.DestroyImmediate(prefabGO);
    }

    [Test]
    public void TryGet_UnregisteredCoord_ReturnsFalse()
    {
        bool found = _registry.TryGet(_pieceB, out _, out _);
        Assert.IsFalse(found);
    }

    [Test]
    public void Contains_RegisteredCoord_ReturnsTrue()
    {
        _registry.Register(_pieceB, null, _anchor);
        Assert.IsTrue(_registry.Contains(_pieceB));
    }

    [Test]
    public void UnregisterByAnchor_RemovesAllLinkedPieces()
    {
        _registry.Register(_pieceB, null, _anchor);
        _registry.Register(_pieceC, null, _anchor);

        _registry.UnregisterByAnchor(_anchor);

        Assert.IsFalse(_registry.Contains(_pieceB));
        Assert.IsFalse(_registry.Contains(_pieceC));
    }

    [Test]
    public void UnregisterByAnchor_NonExistentAnchor_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _registry.UnregisterByAnchor(new Vector2Int(99, 99)));
    }

    [Test]
    public void Register_SameCoordTwice_OverwritesPrefab()
    {
        var go1 = new GameObject("P1");
        var go2 = new GameObject("P2");
        var mb1 = go1.AddComponent<MeshRenderer>() as MonoBehaviour;
        var mb2 = go2.AddComponent<MeshRenderer>() as MonoBehaviour;

        _registry.Register(_pieceB, mb1, _anchor);
        _registry.Register(_pieceB, mb2, _anchor);

        _registry.TryGet(_pieceB, out var result, out _);
        Assert.AreEqual(mb2, result);

        Object.DestroyImmediate(go1);
        Object.DestroyImmediate(go2);
    }
}
```

- [ ] **Step 3: Unity Test Runner에서 LinkedChunkRegistryTests 실행**

Unity Editor → Window → General → Test Runner → EditMode 탭 → `LinkedChunkRegistryTests` 실행.
예상: 5개 테스트 모두 PASS.

- [ ] **Step 4: 커밋**

```
feat: add LinkedChunkRegistry — linked piece coord cache
```

---

## Task 2: SpecialChunkSelector — 역산 메서드 + exclusion zone

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Core/SpecialChunkSelector.cs`

- [ ] **Step 1: `SpecialChunkDef`의 `linkedPieces` 필드 접근을 위해 SpecialChunkManager.LinkedPiece 구조체가 필요함을 확인**

Task 3에서 `LinkedPiece` 구조체와 `SpecialChunkDef.linkedPieces`를 추가한다. Task 2는 해당 필드가 있다고 가정하고 작성하며, Task 3 완료 후 컴파일 확인한다.

- [ ] **Step 2: `IsLinkedPieceByPool` 메서드 추가**

`SpecialChunkSelector.cs` 의 `IsSubChunkByPool` 메서드 아래에 추가:

```csharp
/// <summary>
/// 해당 좌표가 어떤 앵커의 링크 피스인지 풀 역산으로 판별한다.
/// 앵커 로드 여부와 무관하게 동작 (결정론적).
/// </summary>
public bool IsLinkedPieceByPool(Vector2Int coord, TileType layerType, int worldSeed)
{
    foreach (var pool in _pools)
    {
        if (pool.targetLayer != layerType || pool.chunks == null) continue;
        foreach (var def in pool.chunks)
        {
            if (def.linkedPieces == null || def.linkedPieces.Length == 0) continue;
            foreach (var piece in def.linkedPieces)
            {
                var potentialAnchor = coord - piece.offset;
                var defAtAnchor = TrySelectRaw(potentialAnchor, layerType, worldSeed);
                if (defAtAnchor != null && defAtAnchor.Value.prefab == def.prefab)
                    return true;
            }
        }
    }
    return false;
}
```

- [ ] **Step 3: `TryGetLinkedAnchorByPool` 메서드 추가**

`IsLinkedPieceByPool` 바로 아래에 추가:

```csharp
/// <summary>
/// 역산으로 앵커 좌표와 이 피스에 지정된 프리팹을 반환한다.
/// TryGetAnchorByPool의 링크 피스 버전.
/// </summary>
public bool TryGetLinkedAnchorByPool(
    Vector2Int coord, TileType layerType, int worldSeed,
    out Vector2Int anchorCoord, out MonoBehaviour prefab)
{
    anchorCoord = default;
    prefab = null;

    foreach (var pool in _pools)
    {
        if (pool.targetLayer != layerType || pool.chunks == null) continue;
        foreach (var def in pool.chunks)
        {
            if (def.linkedPieces == null || def.linkedPieces.Length == 0) continue;
            foreach (var piece in def.linkedPieces)
            {
                var potentialAnchor = coord - piece.offset;
                var defAtAnchor = TrySelectRaw(potentialAnchor, layerType, worldSeed);
                if (defAtAnchor != null && defAtAnchor.Value.prefab == def.prefab)
                {
                    anchorCoord = potentialAnchor;
                    prefab = piece.prefab;
                    return true;
                }
            }
        }
    }
    return false;
}
```

- [ ] **Step 4: `TrySelect` exclusion zone에 링크 피스 차단 추가**

`TrySelect` 메서드의 exclusion zone 루프에서 `registry.Contains(checkCoord)` 블록 바로 아래에 추가:

```csharp
// 기존 코드 (변경 없음)
if (registry.Contains(checkCoord))
{
    registry.TryGetAnchor(checkCoord, out var blockerAnchor);
    return null;
}

// [추가] 링크 피스 좌표 — 다른 앵커가 점유 예정
if (IsLinkedPieceByPool(checkCoord, layerType, worldSeed)) return null;

// 기존 케이스 1, 2 이하 동일
```

- [ ] **Step 5: 커밋 (Task 3 완료 후 컴파일 확인 뒤 함께 커밋)**

Task 3와 함께:
```
feat: add IsLinkedPieceByPool, TryGetLinkedAnchorByPool to SpecialChunkSelector
feat: block linked piece coords in TrySelect exclusion zone
```

---

## Task 3: SpecialChunkManager — 구조체·필드·스폰·API

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs`

- [ ] **Step 1: `LinkedPiece` 구조체를 `SpecialChunkDef` 바로 위에 추가**

`SpecialChunkDef` 구조체 정의 위쪽 (약 34번째 줄 부근):

```csharp
[System.Serializable]
public struct LinkedPiece
{
    [Tooltip("앵커 기준 상대 좌표. 예: (1,0) = 오른쪽 1칸, (0,-1) = 아래 1칸")]
    public Vector2Int offset;
    [Tooltip("해당 위치에 스폰할 TerrainChunk 프리팹. null이면 기본 청크 프리팹 사용.")]
    public MonoBehaviour prefab;
}
```

- [ ] **Step 2: `SpecialChunkDef`에 `linkedPieces` 필드 추가**

`SpecialChunkDef` 구조체 내 마지막 필드(`chunkSizeY`) 아래:

```csharp
[Tooltip("연결된 피스 목록. 비어있으면 단일 청크 앵커로 동작.")]
public LinkedPiece[] linkedPieces;
```

- [ ] **Step 3: `_linkedRegistry` 필드 추가 및 Awake 초기화**

`#region Private State` 안에 `_registry` 필드 아래:

```csharp
private LinkedChunkRegistry _linkedRegistry;
```

`Awake()` 에서 `_registry = new SubChunkRegistry();` 바로 아래:

```csharp
_linkedRegistry = new LinkedChunkRegistry();
```

- [ ] **Step 4: `UnregisterSubChunksForAnchor` 수정**

기존:
```csharp
public void UnregisterSubChunksForAnchor(Vector2Int anchorCoord)
    => _registry.UnregisterByAnchor(anchorCoord);
```

변경:
```csharp
public void UnregisterSubChunksForAnchor(Vector2Int anchorCoord)
{
    _registry.UnregisterByAnchor(anchorCoord);
    _linkedRegistry.UnregisterByAnchor(anchorCoord);
}
```

- [ ] **Step 5: `IsSubChunkCoord` 수정 — 링크 피스 포함**

기존:
```csharp
public bool IsSubChunkCoord(Vector2Int coord, TileType layerType, int worldSeed)
{
    if (_registry.Contains(coord)) return true;
    return _selector.IsSubChunkByPool(coord, layerType, worldSeed, _registry);
}
```

변경:
```csharp
public bool IsSubChunkCoord(Vector2Int coord, TileType layerType, int worldSeed)
{
    if (_registry.Contains(coord)) return true;
    if (_linkedRegistry.Contains(coord)) return true;
    if (_selector.IsSubChunkByPool(coord, layerType, worldSeed, _registry)) return true;
    return _selector.IsLinkedPieceByPool(coord, layerType, worldSeed);
}
```

- [ ] **Step 6: `SpawnSpecialChunkIfPossible`에 링크 피스 등록 추가**

기존의 `sizeX > 1 || sizeY > 1` 블록(약 282번째 줄) **이전**에 삽입:

```csharp
// 링크 피스 등록 (캐시 채우기 — 앵커가 로드됐을 때만 실행)
if (def.linkedPieces != null)
{
    foreach (var piece in def.linkedPieces)
    {
        if (piece.prefab == null) continue;
        _linkedRegistry.Register(coord + piece.offset, piece.prefab, coord);
    }
}
```

- [ ] **Step 7: Public API 2개 추가 — `#region Public API — Registry 조회` 끝에**

```csharp
/// <summary>
/// 캐시에서 링크 피스 프리팹을 반환한다. 캐시 미스 시 false.
/// </summary>
public bool TryGetLinkedPiece(Vector2Int coord, out MonoBehaviour prefab, out Vector2Int anchor)
    => _linkedRegistry.TryGet(coord, out prefab, out anchor);

/// <summary>
/// 캐시 우선 조회 → 캐시 미스 시 결정론적 역산.
/// ChunkDataProvider가 링크 피스 스폰 경로에서 호출한다.
/// </summary>
public bool TryGetLinkedPiecePrefab(
    Vector2Int coord, TileType layerType, int worldSeed, out MonoBehaviour prefab)
{
    if (_linkedRegistry.TryGet(coord, out prefab, out _)) return true;
    return _selector.TryGetLinkedAnchorByPool(coord, layerType, worldSeed, out _, out prefab);
}
```

- [ ] **Step 8: Unity 컴파일 확인 후 Task 2와 함께 커밋**

Unity Console에 컴파일 에러 없음 확인.

```
feat: add LinkedPiece struct, _linkedRegistry, linked piece spawn/unregister to SpecialChunkManager
```

---

## Task 4: ChunkGenerationContext + StandardChunkFactory

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Generation/Pipeline/ChunkGenerationContext.cs`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Generation/Pipeline/StandardChunkFactory.cs`

- [ ] **Step 1: `ChunkGenerationContext`에 `LinkedPiecePrefab` 필드 추가**

`// Flags` 블록 바로 위:

```csharp
// 링크 피스 전용 프리팹 (null이면 기본 chunk prefab 또는 풀 재사용)
public GameObject LinkedPiecePrefab;
```

- [ ] **Step 2: `StandardChunkFactory.CreateChunk` 수정**

기존 풀 조회 블록:
```csharp
if (_chunkPool.HasAvailableChunks())
{
    chunk = _chunkPool.Get();
}
else
{
    GameObject obj = UnityEngine.Object.Instantiate(_chunkPrefab, _parentTransform);
    chunk = obj.GetComponent<TerrainChunk>();
    chunk.FirstTimeInit(context.SourceWidth, context.SourceHeight);
}
```

변경 (링크 피스 전용 프리팹 분기 추가):
```csharp
if (context.LinkedPiecePrefab != null && context.LinkedPiecePrefab != _chunkPrefab)
{
    // 링크 피스 전용 프리팹 — 풀 미사용, 직접 인스턴스화
    GameObject obj = UnityEngine.Object.Instantiate(context.LinkedPiecePrefab, _parentTransform);
    chunk = obj.GetComponent<TerrainChunk>();
    chunk.FirstTimeInit(context.SourceWidth, context.SourceHeight);
}
else if (_chunkPool.HasAvailableChunks())
{
    chunk = _chunkPool.Get();
}
else
{
    GameObject obj = UnityEngine.Object.Instantiate(_chunkPrefab, _parentTransform);
    chunk = obj.GetComponent<TerrainChunk>();
    chunk.FirstTimeInit(context.SourceWidth, context.SourceHeight);
}
```

- [ ] **Step 3: 컴파일 확인 후 커밋**

```
feat: add LinkedPiecePrefab to ChunkGenerationContext, handle in StandardChunkFactory
```

---

## Task 5: ChunkDataProvider — GetContext 링크 경로 추가

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Generation/Pipeline/ChunkDataProvider.cs`

- [ ] **Step 1: 1-A 블록(SubChunkRegistry 조회) 바로 다음에 1-A-LINK 추가**

기존 1-A 블록 끝 (`return context;` 이후):

```csharp
// [신규] 1-A-LINK. 링크 피스 캐시·역산 경로 (앵커 로드 여부 무관)
// 캐시(LinkedChunkRegistry) 우선, 미스 시 SpecialChunkSelector 결정론적 역산.
// SpawnSpecialChunkIfPossible보다 먼저 실행해야 링크 피스 좌표에 다른 특수청크가 스폰되는 것을 막는다.
if (SpecialChunkManager.Instance != null)
{
    // TileType이 아직 결정되지 않았으므로 먼저 결정
    var linkedTileType = TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y);

    if (SpecialChunkManager.Instance.TryGetLinkedPiecePrefab(
        coord, linkedTileType, _worldSeed, out var linkedPrefab))
    {
        context.TileType = linkedTileType;
        if (linkedPrefab != null)
            context.LinkedPiecePrefab = linkedPrefab.gameObject;

        // 저장 데이터 복원 (파진 픽셀 포함)
        if (_persistenceSystem.TryGetChunkData(coord, out var linkSaved))
            context.SavedData = linkSaved;

        LoadVisualData(context, context.TileType);

        if (!context.IsRestoredFromSave)
            GenerateNewData(context);

        return context;
    }
}
```

- [ ] **Step 2: 기존 `context.TileType` 결정 라인이 중복되지 않는지 확인**

1-A-LINK 안에서만 `linkedTileType`을 결정하고 `context.TileType`에 할당한다. 이 블록이 early return되지 않으면 이후 기존 `context.TileType = TileDataManager...` 라인이 정상 실행된다. 중복 없음.

- [ ] **Step 3: Unity 컴파일 확인**

Console에 에러 없음.

- [ ] **Step 4: 커밋**

```
feat: add linked piece cache+reverse path to ChunkDataProvider.GetContext
```

---

## Task 6: Inspector 설정 및 수동 검증

**Files:** Unity Inspector (코드 수정 없음)

- [ ] **Step 1: Inspector에서 테스트용 링크 구조물 정의**

`SpecialChunkManager` 컴포넌트 → pools → 원하는 레이어 pool 선택 → chunks 리스트에 새 항목 추가:
- `prefab`: 앵커로 쓸 TerrainChunk 프리팹
- `spawnChance`: 테스트용으로 100
- `linkedPieces`: 배열 크기 1
  - `offset`: (1, 0)
  - `prefab`: 오른쪽 피스 프리팹 (없으면 동일 프리팹)

- [ ] **Step 2: 오른쪽에서 접근 시 정상 스폰 확인**

플레이어를 앵커 오른쪽(피스 C쪽)에 스폰.  
예상: 피스 C가 링크 피스로 스폰됨 (일반 청크와 동일하게 보임).  
앵커 A가 로드 범위 밖이어도 C가 정상 스폰됨을 확인.

- [ ] **Step 3: 앵커부터 접근 시 캐시 경로 확인**

플레이어를 앵커 A 위치에 스폰.  
예상: A 스폰 → LinkedChunkRegistry에 C 등록 → C가 1-A-LINK 캐시 경로로 스폰.  
Unity Console에서 `[LinkedChunk]` 로그 없음(정상 경로).

- [ ] **Step 4: 저장·복원 검증**

피스 C 위치를 일부 파기 → 세이브(게임 종료) → 재시작.  
예상: 파진 픽셀이 복원됨.

- [ ] **Step 5: 언로드·재진입 검증**

플레이어를 구조물에서 멀리 이동 (양쪽 모두 언로드) → 다시 C쪽에서 접근.  
예상: C가 링크 피스로 정상 재스폰. 일반 청크로 스폰되지 않음.

- [ ] **Step 6: 다른 특수청크 exclusion zone 검증**

링크 피스 C 인접 좌표에 다른 특수청크가 스폰되지 않음을 확인.  
(spawnChance 100짜리 특수청크가 있다면 C 옆에 생기지 않아야 함)

- [ ] **Step 7: 최종 커밋**

```
test: verify linked chunk system — spawn, save/restore, exclusion zone
```
