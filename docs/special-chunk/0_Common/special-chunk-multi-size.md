# 특수 청크 멀티 사이즈 지형 생성 구조
@tags: special-chunk, multi-size, multi-chunk, sub-chunk, SubChunkRegistry, coordinate

> 일반 청크 크기(1000×1000px)를 초과하는 특수 청크가 어떻게 지형을 생성하고 관리되는지 설명합니다.

---

## 1. 핵심 개념

일반 청크는 항상 1000×1000px = 청크 좌표 1칸을 차지합니다.
**멀티 사이즈 특수 청크**는 이 경계를 무시하고 **단 하나의 프리팹 GameObject**가 여러 청크 슬롯에 걸쳐 공간을 점유합니다.

```
일반 청크 (1000×1000px):        멀티 청크 (2000×1000px, sizeX=2):
┌───────┐                        ┌───────────────┐
│ (0,0) │                        │   앵커(0,0)   │  ← GameObject 1개
└───────┘                        │ (서브: (1,0)) │    픽셀 2000px 폭
                                 └───────────────┘
```

---

## 2. 사이즈 결정 방법

### SpecialChunkDef 구조체 (`SpecialChunkManager.cs:31`)

```csharp
public struct SpecialChunkDef
{
    public TerrainChunk prefab;
    public float spawnChance;
    public SpecialChunkType chunkType;
    public int minDepth;
    public int maxDepth;
    public int chunkSizeX;  // ← 점유 청크 가로 수 (기본 1)
    public int chunkSizeY;  // ← 점유 청크 세로 수 (기본 1)

    public int SizeX => Mathf.Max(1, chunkSizeX);
    public int SizeY => Mathf.Max(1, chunkSizeY);
}
```

- `chunkSizeX=2, chunkSizeY=1` → 2000×1000px 청크 (가로 2칸)
- `chunkSizeX=2, chunkSizeY=2` → 2000×2000px 청크 (4칸 점유)

### SpecialChunkSelector의 크기 계산 (`SpecialChunkSelector.cs:183`)

```csharp
private static Vector2Int GetSize(SpecialChunkDef def)
{
    if (def.prefab == null) return Vector2Int.one;
    return new Vector2Int(
        Mathf.Max(1, def.prefab.width / 1000),   // prefab 픽셀 폭 ÷ 1000
        Mathf.Max(1, def.prefab.height / 1000)   // prefab 픽셀 높이 ÷ 1000
    );
}
```

---

## 3. 풋프린트(Footprint) 계산

### 좌표 규칙

- **앵커(Anchor)**: 특수 청크의 좌상단 청크 좌표 (가장 작은 dx=0, dy=0)
- **서브청크(SubChunk)**: 앵커 기준 +X 방향(오른쪽), -Y 방향(아래)으로 확장
- Y축: 지하 방향이 **음수** (e.g. 지하 2층 = y=-2)

### SpecialChunkFootprint.Build() (`SpecialChunkFootprint.cs:21`)

```csharp
public static void Build(Vector2Int anchor, Vector2Int size, HashSet<Vector2Int> result)
{
    for (int dx = 0; dx < size.x; dx++)
        for (int dy = 0; dy > -size.y; dy--)
            result.Add(anchor + new Vector2Int(dx, dy));
}
```

**예시:** `anchor=(2,-3), size=(3,2)`
```
점유 좌표: {(2,-3), (3,-3), (4,-3), (2,-4), (3,-4), (4,-4)}

     X=2   X=3   X=4
Y=-3 [앵커] [서브] [서브]
Y=-4 [서브] [서브] [서브]
```

---

## 4. 스폰 흐름 (SpawnSpecialChunkIfPossible)

`SpecialChunkManager.SpawnSpecialChunkIfPossible()` (`SpecialChunkManager.cs:196`) 실행 순서:

### Step 1: 앵커 프리팹 선택

```
SpecialChunkSelector.TrySelect(coord, layerType, worldSeed, registry)
→ 시드 기반 결정론적 RNG로 확률 판정
→ 당첨 시 SpecialChunkDef 반환, 꽝 시 null
```

### Step 2: 앵커 GameObject 생성

```csharp
GameObject anchorObj = Instantiate(def.prefab.gameObject, anchorPos, Quaternion.identity, parent);
TerrainChunk anchorChunk = anchorObj.GetComponent<TerrainChunk>();
anchorChunk.isStaticSpecialChunk = true;

// 멀티 사이즈이면 TerrainChunk의 내부 버퍼를 재초기화
int pixelW = def.SizeX * 1000;  // e.g. 2 × 1000 = 2000px
int pixelH = def.SizeY * 1000;
if (pixelW != 1000 || pixelH != 1000)
    anchorChunk.ReinitializeWithSize(pixelW, pixelH);
```

- 단 하나의 프리팹이 `2000×2000px` 크기의 텍스처와 콜라이더를 가짐
- `ReinitializeWithSize()`가 내부 Color32[] 배열 및 Texture2D를 실제 크기로 재할당

### Step 3: 서브 위치 예약 등록 (인스턴스 없이)

```csharp
for (int dx = 0; dx < sizeX; dx++)
    for (int dy = 0; dy > -sizeY; dy--)
    {
        if (dx == 0 && dy == 0) continue;  // 앵커 자신 skip
        Vector2Int subCoord = coord + new Vector2Int(dx, dy);
        _registry.RegisterReserved(subCoord, coord);  // 좌표만 예약, 인스턴스 없음
    }
```

- `RegisterReserved()`는 `_anchorMap`에만 기록 → TerrainChunk 인스턴스를 생성하지 않음
- 이후 파이프라인이 서브 좌표에 도달하면 "이미 처리됨"으로 건너뜀

---

## 5. 생성 파이프라인에서의 처리

청크 좌표별로 `ChunkDataProvider.GetContext()` → `ChunkGenerationPipeline.ExecutePhase1()` 순서로 진행됩니다.

### 앵커 좌표 도달 시 (Phase 1)

```
GetContext(anchorCoord)
  → 1-A: TryGetRegisteredSubChunk() → null (앵커 자신은 서브가 아님)
  → 1-B: SpawnSpecialChunkIfPossible() → 앵커 TerrainChunk 반환
  → context.SpecialChunkInstance = anchorChunk
  → context.SubChunkInstances = null (대형 앵커 방식에서는 비어있음)
  → RETURN

ExecutePhase1:
  → SpecialChunkFactory.CreateChunk(context) → anchorChunk 반환
  → _registry.Add(anchorCoord, anchorChunk)
```

### 서브 좌표 도달 시 (Phase 1)

```
GetContext(subCoord)
  → 1-A: TryGetRegisteredSubChunk() → null (인스턴스 없이 예약만 됨)
  → 1-B: SpawnSpecialChunkIfPossible() → null (서브 위치는 앵커 못 됨, footprint 충돌)
  → 1-A2: IsSubChunkCoord() → true (_registry.Contains() 또는 풀 탐색으로 판정)
  → context.IsBlocked = true
  → RETURN

ExecutePhase1:
  → context.IsBlocked == true → return null (청크 생성 없이 완전 skip)
```

### Phase 2: 데코레이션

```csharp
if (chunk.isStaticSpecialChunk)
{
    chunk.gameObject.SetActive(true);
    chunk.UpdateBoundaryLighting(true, true, true, true);
    return;  // ← 암석/광물/엘리베이터 데코레이션 없음
}
```

특수 청크는 프리팹이 이미 완성된 지형을 포함하므로 데코레이션 단계를 건너뜁니다.

---

## 6. 로드 큐 앵커 우선순위 보장

서브 좌표가 뷰 범위 안에 있어도 앵커가 범위 밖일 수 있습니다.
이를 방지하기 위해 `PrependSubChunkAnchors()` (`InfinityMapManager.cs:646`)가 동작합니다.

```
BuildLoadQueue() → 뷰 범위의 미로드 좌표 목록 생성
        ↓
PrependSubChunkAnchors()
  For each coord in queue:
    IsSubChunkCoord(coord) == true?
      → TryGetSubChunkAnchorCoord() 로 앵커 역산
      → 앵커가 큐에 없고 레지스트리에도 없으면 → 큐 맨 앞에 삽입
        ↓
최종 큐: [앵커, 앵커, ..., 서브들, 일반 청크들]
```

앵커가 항상 서브보다 먼저 로드되어 `RegisterReserved()`가 선행됩니다.

---

## 7. 언로드 범위 판정

### IsChunkOutOfRange() (`InfinityMapManager.cs:575`)

| 청크 종류 | 판정 방식 |
|-----------|-----------|
| 서브청크 | 앵커 좌표를 역산 → 앵커의 footprint 기준 판정 |
| 앵커 (isStaticSpecialChunk) | 자신의 footprint 기준 판정 |
| 일반 청크 | 단순 거리 비교 |

### IsFootprintOutOfRange() (`InfinityMapManager.cs:612`)

```csharp
private bool IsFootprintOutOfRange(Vector2Int anchor, int sizeX, int sizeY)
{
    // footprint 직사각형에서 플레이어와 가장 가까운 점을 구해 거리 판정
    int nearestX = Mathf.Clamp(lastChunkCoord.x, anchor.x, anchor.x + sizeX - 1);
    int nearestY = Mathf.Clamp(lastChunkCoord.y, anchor.y - sizeY + 1, anchor.y);
    return Mathf.Abs(nearestX - lastChunkCoord.x) > viewDistance ||
           Mathf.Abs(nearestY - lastChunkCoord.y) > viewDistance;
}
```

→ footprint의 **어느 한 점이라도 뷰 범위 안에 있으면** 언로드하지 않습니다.

---

## 8. 충돌 해결 (이웃 특수 청크와의 간격 보장)

`SpecialChunkSelector.TrySelect()` (`SpecialChunkSelector.cs:34`)는 3단계로 충돌을 방지합니다.

```
1. footprint 계산 (anchor 기준 sizeX × sizeY 직사각형)

2. exclusion zone = footprint ± minChunkSpacing (기본 2칸 확장)
   exclusionZone.ExceptWith(footprint)  ← 자신의 footprint는 제외

3. exclusion zone의 각 좌표 검사:
   a. _registry.Contains(checkCoord) → 이미 점유됨 → REJECT
   b. TrySelectRaw(checkCoord) != null (이웃 앵커 후보):
      → CoordHash 비교: neighborHash > myHash → REJECT (상대 우선)
   c. TryGetAnchorByPool(checkCoord) (이웃 대형 청크의 서브 위치):
      → 앵커 CoordHash 비교 → REJECT 여부 결정
```

결정론적 해시 `(x * 73856093) ^ (y * 19349663) ^ (seed * 83492791)` 로 우선순위를 결정해 같은 시드에서 항상 동일한 특수 청크가 배치됩니다.

---

## 9. 전체 흐름 요약 (2×1 특수 청크 예시)

```
anchor=(5,-6), size=(2,1) → 픽셀 2000×1000px

[선택]
  SpecialChunkSelector.TrySelect((5,-6)) → ScrapExplosion 당첨

[스폰 — Phase 1 for (5,-6)]
  Instantiate(prefab) at worldPos(5,-6)
  anchorChunk.ReinitializeWithSize(2000, 1000)
  RegisterReserved((6,-6), anchorCoord=(5,-6))   ← 서브 좌표 예약
  registry.Add((5,-6), anchorChunk)

[Phase 1 for (6,-6)]
  GetContext: IsSubChunkCoord((6,-6)) == true
  context.IsBlocked = true
  ExecutePhase1 → return null (skip)

[Phase 2 for (5,-6)]
  isStaticSpecialChunk == true
  → SetActive(true)
  → UpdateBoundaryLighting()
  → DONE (데코레이션 없음)

[언로드]
  IsChunkOutOfRange((6,-6)):
    TryGetAnchorCoord((6,-6)) → (5,-6)
    IsFootprintOutOfRange((5,-6), sizeX=2, sizeY=1)
    → footprint={(5,-6),(6,-6)} 중 가장 가까운 점 기준 판정
```

---

## 10. 관련 파일 참조

| 파일 | 역할 |
|------|------|
| `SpecialChunkManager.cs` | 스폰 오케스트레이터 (퍼사드) |
| `SpecialChunkFootprint.cs` | 풋프린트 좌표 계산 (순수 수학) |
| `SubChunkRegistry.cs` | 서브 위치 예약 등록 및 앵커 역산 |
| `SpecialChunkSelector.cs` | 확률 선택 + 충돌 해결 |
| `ChunkDataProvider.cs` | 파이프라인 컨텍스트 조립 (1-A/1-B/1-A2 분기) |
| `ChunkGenerationPipeline.cs` | Phase 1/2/3 실행 |
| `SpecialChunkFactory.cs` | 특수 청크 팩토리 (context에서 인스턴스 추출) |
| `InfinityMapManager.cs` | 로드 큐 관리, 언로드 범위 판정 |
