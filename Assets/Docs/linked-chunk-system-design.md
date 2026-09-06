# 링크 피스 시스템 설계 문서

**작성일**: 2026-05-27  
**상태**: 설계 완료, 구현 대기

---

## 목표

단일 청크 앵커들을 연결해 멀티타일 구조물을 구성한다.  
기존 멀티청크(`sizeX/Y > 1`) 방식의 위치 오류 버그를 우회하고,  
Minecraft 방식의 결정론적 역산으로 로드 순서·앵커 로드 여부에 무관하게 동작한다.

---

## 핵심 원칙

- 링크 피스는 `IChunkInitializer` 없는 독립 TerrainChunk
- **각 피스는 앵커가 로드되지 않아도 스스로 스폰 가능** (앵커 종속 없음)
- `LinkedChunkRegistry`는 캐시(성능 최적화)일 뿐, 스폰 정확성에 필수 아님
- 결정론적 역산이 항상 올바른 메인 경로
- 구조물 방향 고정 (회전 미지원), 2D 격자 지원

### 설계 배경 — 앵커 종속 방식의 한계

로드 범위가 4청크이고 구조물이 3~4칸이면 앵커가 로드 범위 밖에 있는 상황이 일반적으로 발생한다.  
앵커 스폰을 전제로 하는 방식(B 원안)은 이 경우 피스를 영구 차단하는 버그를 유발한다.  
따라서 역산을 메인 경로로, 레지스트리를 캐시로 설계한다.

---

## 섹션 1 — 데이터 구조

### LinkedPiece 구조체 (신규)

```csharp
[System.Serializable]
public struct LinkedPiece
{
    public Vector2Int offset;    // 앵커 기준 상대 좌표 (예: (1,0), (0,-1), (1,-1))
    public MonoBehaviour prefab; // 해당 위치에 강제 스폰할 프리팹
}
```

### SpecialChunkDef 확장

`SpecialChunkManager.SpecialChunkDef`에 필드 추가:

```csharp
public LinkedPiece[] linkedPieces; // 비어있으면 기존 단일 청크 앵커로 동작
```

Inspector에서 앵커 정의에 링크 피스 목록만 추가하면 된다.  
기존 단일 청크 앵커는 `linkedPieces`를 비워두면 동작 변화 없음.

**프리팹 재사용 규칙**:
- 피스 내용이 동일하면 같은 프리팹을 여러 offset에 지정 가능
- 일반 지형처럼 보여도 되는 피스는 기본 chunk prefab 재사용 가능

---

## 섹션 2 — 신규 컴포넌트

### LinkedChunkRegistry (캐시 역할)

`SubChunkRegistry`와 동일한 패턴. `SpecialChunkManager`가 나란히 보유.  
앵커가 로드됐을 때 채워지는 캐시이며, 비어있어도 시스템은 정상 동작한다.

```csharp
public class LinkedChunkRegistry
{
    private Dictionary<Vector2Int, (MonoBehaviour prefab, Vector2Int anchor)> _links;

    public void Register(Vector2Int coord, MonoBehaviour prefab, Vector2Int anchor);
    public bool TryGet(Vector2Int coord, out MonoBehaviour prefab, out Vector2Int anchor);
    public bool Contains(Vector2Int coord);
    public void UnregisterByAnchor(Vector2Int anchor);
}
```

### SpecialChunkSelector — 메서드 2개 추가

```csharp
// 해당 좌표가 어떤 앵커의 링크 피스인지 역산
public bool IsLinkedPieceByPool(
    Vector2Int coord, TileType layerType, int worldSeed);

// 앵커 좌표와 사용할 프리팹까지 역산
public bool TryGetLinkedAnchorByPool(
    Vector2Int coord, TileType layerType, int worldSeed,
    out Vector2Int anchorCoord, out MonoBehaviour prefab);
```

**역산 로직**:
1. 풀에서 `linkedPieces`가 있는 def만 순회 (비용 최소화)
2. 각 `linkedPiece.offset`을 역으로 적용 → 앵커 후보 좌표 계산
3. 후보 좌표에 `TrySelectRaw` → 당첨 + offset 일치이면 링크 피스 확정

---

## 섹션 3 — 수정되는 기존 흐름

### 3-1. ChunkDataProvider.GetContext() — 핵심 변경

```
1-A (캐시 경로, 빠름):
  LinkedChunkRegistry.TryGet(coord) → hit
    → context에 프리팹 세팅
    → savedData 조회 (파진 픽셀 복원용)
    → LoadVisualData()
    → return

1-A miss → 역산 경로 (앵커 로드 여부 무관):
  TryGetLinkedAnchorByPool(coord) → hit
    → context에 프리팹 세팅 (앵커 없이도 스폰 가능)
    → savedData 조회
    → LoadVisualData()
    → return

역산도 miss → 기존 흐름 계속
  (SubChunkRegistry, IsBlocked, SpawnSpecialChunk, GenerateNewData ...)
```

**savedData 조회** (1-A 및 역산 경로 공통):
```csharp
if (_persistenceSystem.TryGetChunkData(coord, out var saved) && saved.hasChanges)
    context.SavedData = saved;
```

### 3-2. SpecialChunkManager.SpawnSpecialChunkIfPossible()

앵커 스폰 완료 직후 `linkedPieces`를 캐시에 등록 (이미 ActiveChunkRegistry에 있는 피스는 스킵):

```
앵커 Instantiate → IChunkInitializer.Initialize() → OnSpawned()
→ def.linkedPieces 순회
  → !ActiveChunkRegistry.HasChunk(anchorCoord + piece.offset) 이면
    → _linkedRegistry.Register(anchorCoord + piece.offset, piece.prefab, anchorCoord)
```

앵커가 로드 범위 밖이어서 스폰되지 않더라도 링크 피스는 역산 경로로 정상 스폰된다.

### 3-3. SpecialChunkManager.UnregisterSubChunksForAnchor()

```csharp
public void UnregisterSubChunksForAnchor(Vector2Int anchorCoord)
{
    _registry.UnregisterByAnchor(anchorCoord);       // 기존
    _linkedRegistry.UnregisterByAnchor(anchorCoord); // 추가 (캐시 정리)
}
```

### 3-4. IsSubChunkCoord — 링크 피스 차단 추가

기존 `IsSubChunkByPool` 역산 이후, 링크 피스 역산도 포함:

```
IsSubChunkByPool(coord) → true → IsBlocked = true
                        → false
IsLinkedPieceByPool(coord) → true → IsBlocked = true
                           → false → 기존 흐름
```

링크 피스 좌표에 일반 청크가 스폰되는 것을 막는다.

### 3-5. SpecialChunkSelector.TrySelect() — exclusion zone

```csharp
// 기존
if (registry.Contains(checkCoord)) return null;

// 변경
if (registry.Contains(checkCoord) || linkedRegistry.Contains(checkCoord)) return null;
```

캐시에 없는 링크 피스 좌표는 `IsLinkedPieceByPool`로 추가 확인:

```csharp
if (IsLinkedPieceByPool(checkCoord, layerType, worldSeed)) return null;
```

---

## 섹션 4 — 에러 케이스 대응

| 케이스 | 발생 조건 | 대응 |
|--------|-----------|------|
| 앵커가 로드 범위 밖 | 로드 범위 4칸, 구조물 3~4칸 | 역산 경로로 피스 독립 스폰 — **문제없음** |
| 플레이어가 중간 피스 B부터 접근 | A-B-C 구조 | B 역산 → 앵커 A 발견 → B 스폰. A가 나중에 로드되면 캐시 채움 |
| 플레이어가 C쪽에서 먼저 접근 | 반대편 접근 | C 역산 → 앵커 A 발견 → C 스폰. B도 동일 |
| A 언로드, 피스들 활성 상태 | A가 범위 밖으로 나감 | 피스들은 독립 TC — 정상 작동 유지. 캐시만 정리됨 |
| 파진 픽셀 미복원 | savedData 미전달 | 1-A 및 역산 경로 모두 savedData 조회로 해결 |
| 언로드 타이밍 중 Phase 1↔2 사이 A 언로드 | 레이스 컨디션 | 피스는 이미 ActiveChunkRegistry에 등록됨. Phase 2 정상 완료 |
| 저장 후 재로드, 앵커 범위 밖 | wasNormalChunk=false로 저장 | 역산 경로로 피스 독립 복원 |

---

## 섹션 5 — 체크리스트 확인 결과

`Assets/Docs/terrain-feature-checklist.md` 기준:

| 항목 | 상태 |
|------|------|
| IsSubChunkCoord 역산 | IsLinkedPieceByPool 추가 (3-4) |
| PrependSubChunkAnchors | 링크 피스는 앵커 우선 불필요 — 역산으로 독립 스폰 |
| ActiveChunkRegistry 정합성 | 독립 TC이므로 기존 흐름 그대로 |
| ChunkPool 반환 | IChunkInitializer 없으므로 기존 풀 반납 정상 |
| wasNormalChunk 플래그 | IChunkInitializer 없으면 wasNormalChunk=true → 일반 저장 경로 |
| RestoreSavedPixels | 1-A 및 역산 경로 savedData 조회로 대응 |
| UpdateBoundaryLighting | 독립 TC이므로 기존 흐름 그대로 |
| TryUpdateCollider | 기존 흐름 그대로 |
| IChunkInitializer 유무 | 없음 (의도적) → Phase 2 데코레이터 정상 실행 |
| Exclusion Zone | 3-5에서 역산 포함 |
| DetachMineralsToWorld | 기존 흐름 그대로 |
| MarkDirty 체인 | 기존 흐름 그대로 |

---

## 섹션 6 — 범위 외

- 구조물 회전 지원
- 링크 피스에 `IChunkInitializer` 지원
- 링크 피스 간 `PreOccupiedAreas` 공유 (경계 장식 겹침 방지)
- A→B→C 연쇄 등록 (체인 방식)

---

## 수정 파일 목록

| 파일 | 변경 종류 |
|------|-----------|
| `SpecialChunkManager.cs` | `LinkedPiece` 구조체 추가, `SpecialChunkDef` 확장, `_linkedRegistry` 필드 추가, `SpawnSpecialChunkIfPossible` 수정, `UnregisterSubChunksForAnchor` 수정 |
| `SpecialChunkSelector.cs` | `IsLinkedPieceByPool`, `TryGetLinkedAnchorByPool` 추가, `TrySelect` exclusion zone 수정 |
| `ChunkDataProvider.cs` | `GetContext` 1-A 캐시 경로 + 역산 메인 경로 추가 |
| `LinkedChunkRegistry.cs` | **신규 파일** |
