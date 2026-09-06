# 청크 좌표 계산 및 겹침 원인 분석
@tags: coordinate, special-chunk, overlap, chunk-registry, PPU, pivot, SubChunkRegistry, chunk-placement, collision

---

## 1. 좌표 계산 공식

```
청크 좌표 (cx, cy)  →  월드 위치 (cx × 10, cy × 10)
월드 위치 (wx, wy)  →  청크 좌표 (floor(wx/10), floor(wy/10))

상수: WorldSize = 10f (= 1000px / 100 PPU)
피벗: 좌하단 (0, 0) 기준
```

### 청크가 차지하는 월드 영역

```
좌표 (cx, cy) 청크 →  x: [cx×10, cx×10+10]
                       y: [cy×10, cy×10+10]

예) 좌표 (2, -1)  →  x: [20, 30],  y: [-10, 0]
   좌표 (1, -1)  →  x: [10, 20],  y: [-10, 0]
   ↑ 두 청크는 x=20 에서 정확히 맞닿음 (겹침 없음이 정상)
```

---

## 2. 청크 배치 흐름

```
[매 프레임] InfinityMapManager.UpdateChunks()
    │
    ├─ BuildLoadQueue()          ← 플레이어 주변 viewDistance 범위 순회
    │   └─ ShouldSkipChunkGeneration(coord)  ← 생성 제외 조건 체크
    │
    └─ ChunkLoadingRunner.UpdateLoadQueue()  ← 큐에 추가
        └─ PrependSubChunkAnchors()          ← 멀티청크 앵커 우선 배치
```

### BuildLoadQueue 순회 범위

```csharp
for (x = -viewDistance; x <= viewDistance; x++)
  for (y = -viewDistance; y <= viewDistance; y++)
      targetCoord = lastChunkCoord + (x, y)
      if (ShouldSkip || hasChunk) continue
      queue.Add(targetCoord)
```

### ShouldSkipChunkGeneration 조건 (이 조건에 해당하면 일반 청크 생성 안 함)

| 조건 | 이유 |
|------|------|
| `coord.y > 0` | 지표 위는 생성 안 함 |
| `abs(coord.x) > terrainWidth` | 맵 가로 경계 초과 |
| (추가 조건 가능) | |

> **주의:** `ShouldSkipChunkGeneration` 단계에서는 특수 청크 여부를 체크하지 않는다.
> 특수 청크 결정은 더 아래 `ChunkDataProvider.GetContext()`에서 일어난다.

---

## 3. 특수 청크 vs 일반 청크 결정 지점

`ChunkDataProvider.GetContext(coord)`에서 아래 순서로 판단한다.

```
GetContext(coord)
│
├─ 1-A. SubChunkRegistry에 등록된 서브청크인가?
│        YES → context.SpecialChunkInstance = 등록된 인스턴스, 즉시 return
│
├─ 1-B. SpawnSpecialChunkIfPossible() 시도
│        당첨 → context.SpecialChunkInstance = 새 특수청크 인스턴스, 즉시 return
│        꽝   → 계속
│
├─ 1-A2. IsSubChunkCoord()? (멀티청크 서브 위치)
│         YES → context.IsBlocked = true, return  → Phase1에서 null 반환(생성 없음)
│
└─ 일반 청크 데이터 생성 (GroundPixels, BorderPixels 등)
```

### 핵심

- 특수 청크가 배치된 좌표에는 **일반 청크가 생성되지 않는다** (1-B에서 조기 return)
- 단, 이 보호는 **레지스트리 등록이 완료된 이후**에만 확실하다

---

## 4. 특수 청크 인스턴스화 위치와 타이밍

```
ChunkDataProvider.GetContext(coord)
└─ SpawnSpecialChunkIfPossible(coord, ...)
    └─ Instantiate(prefab, ChunkCoords.ToWorld(coord), ...)
        ↑ 이 시점에 씬에 배치됨

아직 _chunkRegistry에 등록되지 않은 상태!
↓
Phase 1: SpawnChunk() → SpecialChunkFactory.CreateChunk() 반환
↓
_registry.Add(coord, chunk)  ← 여기서 비로소 등록됨
```

---

## 5. 겹침이 발생할 수 있는 원인 목록

### 원인 A: PPU 불일치 (가장 흔한 원인)

TerrainChunk의 `pixelsPerUnit` Inspector 값이 잘못 설정된 경우.

```
정상: pixelsPerUnit = 100  →  1000px / 100 = 10 유닛 (WorldSize와 일치)
오류: pixelsPerUnit = 10   →  1000px / 10  = 100 유닛 (10배 크게 렌더링!)
```

- 특수 청크 프리팹의 TerrainChunk Inspector에서 `Pixels Per Unit` 값을 **반드시 100** 으로 확인

### 원인 B: 스프라이트 피벗 오류

청크 루트에 **별도 SpriteRenderer** 컴포넌트가 추가된 경우, 스프라이트 피벗이 Center이면 청크가 grid 기준점에서 절반만큼 이동한다.

```
ToWorld((2,-1)) = (20, -10)  ← 청크 좌하단 기준 월드 위치

스프라이트 pivot = Center (기본값)
→ 스프라이트 중심이 (20, -10)에 놓임
→ 스프라이트가 x: [15, 25], y: [-15, -5] 를 차지
→ 인접 청크 영역(x<20 구간)까지 침범!

스프라이트 pivot = Bottom Left (올바름)
→ 스프라이트 좌하단이 (20, -10)에 놓임
→ 스프라이트가 x: [20, 30], y: [-10, 0] 를 차지 ✓
```

- Sprite Editor에서 pivot → **Bottom Left** (또는 Custom x=0, y=0)

### 원인 C: TerrainChunk 픽셀 데이터 미초기화

`SpecialChunkFactory.CreateChunk()`는 `Reuse_Step1_Prepare()`를 호출하지 않는다.
따라서 특수 청크 TerrainChunk의 초기 픽셀은 **흰색 전체 솔리드** 상태.

- PolygonCollider2D가 흰색 픽셀 기반으로 생성 → 청크 전체를 꽉 채우는 충돌체
- 인접 일반 청크도 자기 영역에 꽉 찬 충돌체 보유 → **경계선에서 충돌체가 맞닿아 겹쳐 보임**
- 해결: `SpriteCavityInitializer`가 스프라이트 픽셀을 TerrainChunk에 주입해 올바른 모양 생성

### 원인 D: 레지스트리 미등록 타이밍 (멀티청크 케이스)

멀티청크(sizeX > 1)의 경우 앵커 좌표만 레지스트리에 등록되고
서브좌표는 `SubChunkRegistry.RegisterReserved()`로만 관리됨.

```
앵커 (2,-1) 레지스트리 등록 완료
서브 (3,-1) SubChunkRegistry에 예약됨

하지만 BuildLoadQueue가 (3,-1)을 큐에 넣은 후
GetContext에서 IsSubChunkCoord 체크 → 차단됨
↓
만약 IsSubChunkCoord가 false를 반환하면 일반 청크도 생성 → 겹침!
```

- `SpecialChunkSelector.IsSubChunkByPool()`의 `TrySelect`가 동일 조건으로 당첨 결과를 내야 함
- worldSeed, layerType 불일치 시 오탐 발생 가능

### 원인 E: IChunkInitializer 없는 TerrainChunk 특수청크의 Phase 2

`IChunkInitializer`가 없는 TerrainChunk 기반 특수 청크는
Phase 2 `DecorateChunk_Phase2`에서 **자식이 파괴되고 랜덤 암석/광물이 추가**된다.

- 시각적으로는 일반 청크와 똑같아 보임 → "겹쳐 보임" 착시 가능

---

## 6. 정상 동작 시 시각화

```
좌표 기준 (viewDistance=1, 플레이어 @ 청크 (0,0))

로드되는 청크 좌표:
(-1,+1) (0,+1) (+1,+1)   ← y>0이므로 ShouldSkip=true, 생성 안 됨
(-1, 0) (0, 0) (+1, 0)
(-1,-1) (0,-1) (+1,-1)

월드 위치:
(-10,0)  (0,0)  (10,0)
(-10,-10)(0,-10)(10,-10)

[각 청크: 10×10 유닛, 피벗 좌하단]
(-10,-10)────(0,-10)────(10,-10)
     │    [-1,-1]  │   [0,-1]   │   [1,-1]   │
(-10,0) ─────(0, 0)────(10, 0)
```

---

## 7. 겹침 확인 체크리스트

**프리팹 Inspector 확인:**
- [ ] `TerrainChunk.pixelsPerUnit` = **100**
- [ ] 루트 SpriteRenderer의 스프라이트 pivot = **Bottom Left**
- [ ] `SpriteCavityInitializer.sourceSprite` 텍스처 크기 = 1000×1000

**런타임 로그 확인:**
- [ ] `[TRACE][BuildLoadQueue] (cx,cy) skip=false hasChunk=false` → 정상 큐 진입
- [ ] `[TRACE][ChunkDataProvider] (cx,cy) → 1-B 특수청크 스폰 성공` → 특수청크 배치됨
- [ ] 같은 좌표에 `StandardChunkFactory` 로그가 없어야 함

**씬 Hierarchy 확인:**
- [ ] `Special_X_Y` 와 `Chunk_X_Y` 가 **같은 (X,Y)에 동시 존재하지 않음**
