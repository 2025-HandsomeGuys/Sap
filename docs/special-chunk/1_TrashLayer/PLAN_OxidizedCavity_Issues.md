# Plan: OxidizedCavity 특수 청크 2개 버그 수정
@tags: special-chunk, oxidized-cavity, bug, plan, fix

## 목표
1. `[TerrainCollider] PolygonCollider2D is null!` 에러 제거
2. 멀티 서브청크 좌표에 일반 청크가 먼저 생성되어 겹치는 중복 스폰 방지

---

## 이슈 1 — PolygonCollider2D null 에러

### 원인
`TerrainChunk.Awake()`에서 `GetComponent<PolygonCollider2D>()`를 호출하지만,
`[RequireComponent(typeof(PolygonCollider2D))]`가 주석 처리되어 있어
프리팹에 컴포넌트가 없으면 `_polyCollider = null` → `TerrainCollider` 생성 시 예외.

### 수정 방법
`TerrainChunk.Awake()` 내 `_polyCollider = GetComponent<>()` 직후에 방어 코드 추가:

```csharp
_polyCollider = GetComponent<PolygonCollider2D>();
if (_polyCollider == null)
{
    _polyCollider = gameObject.AddComponent<PolygonCollider2D>();
    Debug.LogWarning($"[TerrainChunk] PolygonCollider2D 누락 → 자동 추가: {name}");
}
```

### 변경 파일
- `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` — Awake() 방어 코드 1줄

---

## 이슈 2 — 멀티 청크 중복 스폰 (Overlap)

### 원인
파이프라인이 서브청크 좌표(예: `3,-2`)를 앵커 좌표(`1,-2`)보다 먼저 처리하면:
- `TryGetRegisteredSubChunk(3,-2)` → false (앵커가 아직 스폰 안 됨)
- `SpawnSpecialChunkIfPossible(3,-2)` → null (앵커 좌표가 아님)
- → **일반 청크 생성**

이후 앵커가 처리되면 `3,-2`에 서브청크를 Instantiate → 두 객체 중복.

### 해결 전략 — 사전 서브좌표 차단

`SpecialChunkManager`에 **역방향 판정 메서드** `IsSubChunkCoord` 추가:
- 주어진 좌표를 `잠재적 앵커 - offset`으로 역산해, 그 잠재 앵커에서 동일 def가 스폰될지 확인
- 해당하면 `ChunkDataProvider`에서 일반 청크 생성 차단 → `IsBlocked = true` context 반환
- 파이프라인이 null 반환 (에러 로그 없이)

---

## 변경 파일

| 파일 | 변경 내용 |
|------|-----------|
| `TerrainChunk.cs` | Awake() — PolygonCollider2D 자동 추가 방어 코드 |
| `SpecialChunkManager.cs` | `IsSubChunkCoord(coord, layerType, worldSeed)` 메서드 추가 |
| `ChunkGenerationContext.cs` | `IsBlocked` 플래그 추가 |
| `ChunkDataProvider.cs` | `TryGetRegisteredSubChunk` 체크 직후 `IsSubChunkCoord` 체크 추가 |
| `ChunkGenerationPipeline.cs` | `context.IsBlocked` 이면 조용히 null 반환 |

---

## 구현 단계

1. `ChunkGenerationContext.cs` — `public bool IsBlocked;` 필드 추가
2. `SpecialChunkManager.cs` — `IsSubChunkCoord` 메서드 구현
3. `ChunkDataProvider.cs` — `IsSubChunkCoord` 호출 및 차단 로직 삽입
4. `ChunkGenerationPipeline.cs` — `IsBlocked` 처리 (silent null return)
5. `TerrainChunk.cs` — Awake() 방어 코드 추가

---

## IsSubChunkCoord 알고리즘

```csharp
public bool IsSubChunkCoord(Vector2Int coord, TileType layerType, int worldSeed)
{
    // 이미 등록된 서브청크면 true (TryGetRegisteredSubChunk와 중복이지만 안전 중복)
    if (_subChunkRegistry.ContainsKey(coord)) return true;

    // layerType 풀 탐색
    foreach (var pool in pools)
    {
        if (pool.targetLayer != layerType) continue;
        foreach (var chunkDef in pool.chunks)
        {
            if (chunkDef.subChunks == null || chunkDef.subChunks.Count == 0) continue;
            foreach (var subDef in chunkDef.subChunks)
            {
                // 역산: coord가 이 offset의 서브라면, 앵커는 coord - offset
                Vector2Int potentialAnchor = coord - subDef.offset;
                var defAtAnchor = GetSpecialChunkDef(potentialAnchor, layerType, worldSeed);
                // 같은 프리팹이 앵커로 선택된다면 → 이 coord는 서브청크 자리
                if (defAtAnchor != null && defAtAnchor.Value.prefab == chunkDef.prefab)
                    return true;
            }
        }
    }
    return false;
}
```

---

## 주의사항

- `IsSubChunkCoord`는 결정론적(deterministic) — 같은 seed·좌표면 항상 동일한 결과
- `GetSpecialChunkDef`를 내부에서 재호출하므로 성능 비용 있음 (서브청크 offset 수 × pool 크기). 실사용 범위(수 개의 offset)에서는 무시 가능
- `IsBlocked = true` 반환 시 해당 좌표는 registry에 추가되지 않음 → 앵커가 스폰되면 sub-chunk가 정상 등록됨
- SOLID 위반 없음: `ChunkDataProvider`는 `SpecialChunkManager` 인터페이스를 통해 질의 (기존 패턴 유지)

---

## 테스트 방법
1. 플레이 모드에서 OxidizedCavity 앵커보다 서브청크 좌표 쪽에서 접근
2. Console에 `[TerrainCollider] PolygonCollider2D is null!` 에러 없음 확인
3. 서브청크 좌표에 일반 청크 + 특수 청크 동시 존재 없음 확인 (Hierarchy 검사)
