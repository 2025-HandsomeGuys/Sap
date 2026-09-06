# LargeStaticTerrainChunk + 파기 가능 서브청크 하이브리드 설계
@tags: special-chunk, large-static, hybrid, diggable, sub-chunk, design, IChunkInitializer

> 마지막 업데이트: 2026-03-21
> 대상: 4000×1000px 이상 대형 특수 청크에 파기 가능 영역을 추가하려는 작업자

---

> **관련 문서:**
> - `docs/special-chunk/special-chunk-editor-setup-guide.md` — 패턴 A/B/C 에디터 설정 가이드
> - `docs/chunk/LargeStaticTerrainChunk_Lifecycle.md` — LargeStaticTerrainChunk 생명주기

---

## 1. 배경

`LargeStaticTerrainChunk`는 1000px를 초과하는 대형 청크(예: 4000×1000)를 위한 컴포넌트로,
ChunkData 픽셀 배열을 갖지 않아 메모리와 연산을 절약한다.
대신 파기를 지원하지 않는다.

파기 가능한 영역이 일부 필요한 경우, **자식 `TerrainChunk`를 서브청크로 등록**하는 하이브리드 구성을 사용한다.

---

## 2. 동작 원리

### 기존 흐름 (수정 전)

`SpawnSpecialChunkIfPossible`에서 멀티청크 서브 좌표를 **좌표만 예약** (`RegisterReserved`)하여
일반 TerrainChunk 생성을 차단만 했다.
→ 자식 TerrainChunk가 프리팹에 있어도 레지스트리에 등록되지 않아 파기·조명·세이브 불가.

### 수정 후 흐름

`SpawnSpecialChunkIfPossible`에서 서브 좌표 루프 시 해당 좌표에 자식 `TerrainChunk`가 있으면:

1. `SubChunkRegistry.Register(subCoord, terrainChunk, anchorCoord)` — 인스턴스로 등록
2. `outSubChunks` 목록에 추가

`ChunkGenerationPipeline.ExecutePhase1_SpawnAndInitialize`가 `SubChunkInstances`를 받아:
- `_chunkRegistry.Add(subCoord, subChunk)` — 메인 레지스트리 등록
- `SetActive(true)`
- `UpdateBoundaryLighting()` 호출

자식 `IChunkInitializer`(SpriteCavityInitializer 등)는 `SpawnSpecialChunkIfPossible` 내
`GetComponentsInChildren<IChunkInitializer>()` 탐색에 이미 포함되므로 **별도 초기화 불필요**.

### 서브청크 좌표 자동 계산

```
TerrainChunk.ChunkX = Mathf.FloorToInt(transform.position.x / ChunkCoords.WorldSize)
TerrainChunk.ChunkY = Mathf.FloorToInt(transform.position.y / ChunkCoords.WorldSize)
```

자식 GameObject의 `localPosition`이 올바른 청크 단위(10 units = 1청크)에 맞으면
`Coord`가 자동으로 서브청크 좌표로 계산된다.

---

## 3. 코드 변경 내역

**파일:** `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs`

`SpawnSpecialChunkIfPossible`의 서브 좌표 등록 루프 수정:

```csharp
// 자식 TerrainChunk를 Coord 기준으로 인덱싱 (파기 가능 서브청크 지원)
var childChunkMap = new Dictionary<Vector2Int, TerrainChunk>();
foreach (var tc in anchorObj.GetComponentsInChildren<TerrainChunk>())
    childChunkMap[tc.Coord] = tc;

for (int dx = 0; dx < sizeX; dx++)
{
    for (int dy = 0; dy > -sizeY; dy--)
    {
        if (dx == 0 && dy == 0) continue;

        Vector2Int subCoord = coord + new Vector2Int(dx, dy);

        if (childChunkMap.TryGetValue(subCoord, out TerrainChunk childTc))
        {
            // 실제 TerrainChunk 인스턴스로 등록 → 파기·조명·세이브 활성화
            _registry.Register(subCoord, childTc, coord);
            outSubChunks ??= new List<(Vector2Int, IChunk)>();
            outSubChunks.Add((subCoord, childTc));
        }
        else
        {
            // 정적 영역: 좌표만 예약 (일반 TerrainChunk 스폰 차단)
            _registry.RegisterReserved(subCoord, coord);
        }
    }
}
```

---

## 4. 프리팹 설정

### 4-1. localPosition 규칙

`ChunkCoords.WorldSize = 10` (1000px ÷ PPU 100 = 10 units = 1청크)

| 청크 위치 | localPosition |
|-----------|---------------|
| 앵커 (0,0) — LargeStatic 루트 자체 | (0, 0) |
| 서브청크 (+1, 0) — 오른쪽 1칸 | (10, 0) |
| 서브청크 (+2, 0) — 오른쪽 2칸 | (20, 0) |
| 서브청크 (+3, 0) — 오른쪽 3칸 | (30, 0) |

### 4-2. 프리팹 구조 예시 (4×1 광산, 2칸 파기 가능)

```
MineChunk (루트 GameObject)
├─ [컴포넌트] LargeStaticTerrainChunk  (chunkGridWidth=4, chunkGridHeight=1)
├─ [컴포넌트] SpriteRenderer           ← 광산 전체 배경 스프라이트
├─ [컴포넌트] PolygonCollider2D        ← 정적 영역 충돌
│
├─ DiggableSection_1 (자식, localPosition=(10, 0))   ← 서브청크 (+1,0) 파기 가능
│   ├─ [컴포넌트] TerrainChunk
│   ├─ [컴포넌트] SpriteCavityInitializer
│   │     └─ Source Sprite: 해당 칸 공동 스프라이트 (1000×1000px)
│   └─ [컴포넌트] SpriteRenderer
│
├─ DiggableSection_2 (자식, localPosition=(20, 0))   ← 서브청크 (+2,0) 파기 가능
│   └─ ...
│
│   (서브청크 (+3,0)은 자식 없음 → RegisterReserved, 정적 처리)
│
└─ IndestructibleOverlay (자식, 선택)                ← 패턴 C 오버레이 추가 가능
    ├─ [컴포넌트] SpriteRenderer
    ├─ [컴포넌트] PolygonCollider2D
    └─ [컴포넌트] IndestructibleOverlayInit
```

### 4-3. 파기 가능 자식 TerrainChunk 컴포넌트 설정

파기 가능 영역의 성격에 따라 패턴을 선택한다.

| 원하는 동작 | 추가 컴포넌트 |
|------------|--------------|
| 공동 있음, 전부 파기 가능 | `SpriteCavityInitializer` (패턴 A) |
| 공동 안에 파기 불가 구조물 | `SpriteCavityInitializer` + 자식 `IndestructibleOverlayInit` (패턴 C) |

> 자식 TerrainChunk의 `SpriteRenderer`는 루트 LargeStatic의 배경 스프라이트와 렌더링이 겹칠 수 있다.
> `Order in Layer` 조정으로 의도한 레이어 순서를 맞출 것.

---

## 5. 정적 vs 파기 가능 칸 결정

| 칸 | 처리 방식 | 조건 |
|----|----------|------|
| 자식 TerrainChunk 있음 | `Register` → 메인 레지스트리 등록, 파기·조명·세이브 정상 작동 | localPosition이 청크 단위에 정확히 맞아야 함 |
| 자식 TerrainChunk 없음 | `RegisterReserved` → 좌표만 예약, 일반 지형 생성 차단 | 기존 LargeStatic 동작과 동일 |

---

## 6. 주의사항

- 자식 TerrainChunk의 `localPosition`이 `ChunkCoords.WorldSize(10)` 배수에서 벗어나면
  `Coord`가 잘못 계산되어 서브청크 등록이 누락된다.
  배치 후 Play Mode에서 `tc.Coord`를 확인할 것.
- 자식 TerrainChunk의 `SpriteRenderer Filter Mode = Point`, `Compression = None`, `Read/Write Enabled` 체크.
- `SpriteCavityInitializer`의 Source Sprite 크기는 **1000×1000px** (TerrainChunk 1칸 크기).
- 자식 TerrainChunk가 앵커 좌표(0,0)와 같은 위치(`localPosition = (0,0)`)에 있으면
  루프에서 `dx==0 && dy==0` skip 조건에 걸려 등록되지 않는다.
  파기 가능 영역은 반드시 서브 좌표(오프셋 1 이상)에 배치할 것.
- 앵커(루트)가 언로드되면 `UnregisterSubChunksForAnchor`가 자식 TerrainChunk 등록도 함께 해제한다.
