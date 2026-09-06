# Plan: ChunkCoords — 청크 좌표 변환 표준화
@tags: coordinate, standardize, plan, ChunkCoords, chunk-placement

## 목표

청크좌표↔월드좌표 변환 공식이 여러 파일에 중복·분산되어 있어 매번 수작업 계산 오류가 발생한다.
`ChunkCoords` 정적 유틸리티 클래스를 만들어 **단일 진실 공급원(Single Source of Truth)** 으로 삼는다.

---

## 현재 문제

| 문제 | 발생 위치 |
|------|-----------|
| `coord.x * widthWorld` 중복 | `SpecialChunkManager`, `StandardChunkFactory` |
| `widthWorld` / `heightWorld` 파라미터가 4개 파일을 거쳐 전달 | `InfinityMapManager` → `ChunkDataProvider` → `SpecialChunkManager` |
| 프리팹 배치 시 "1청크 = 10" 수작업 계산 → 오류 (ZoneTrigger x=15 사례) | 프리팹 YAML 편집 시 |
| `worldSeed` 외에 `chunkWidthWorld`도 항상 같이 넘겨야 하는 불편함 | 여러 팩토리 |

---

## 변경 파일

| 파일 | 변경 종류 | 내용 |
|------|-----------|------|
| `Assets/Scripts/Gameplay/Terrain/Tiles/Core/ChunkCoords.cs` | **신규 생성** | 변환 유틸리티 정적 클래스 |
| `SpecialChunkManager.cs` | 수정 | `coord.x * widthWorld` → `ChunkCoords.ToWorld(coord)` / `widthWorld`, `heightWorld` 파라미터 제거 |
| `StandardChunkFactory.cs` | 수정 | `coord.x * ChunkWidthWorld` → `ChunkCoords.ToWorld(coord)` |
| `ChunkDataProvider.cs` | 수정 | `SpawnSpecialChunkIfPossible` 호출 시 widthWorld/heightWorld 인자 제거 |
| `SpawnSpecialChunkIfPossible` 시그니처 | 수정 | `widthWorld`, `heightWorld` 파라미터 제거 |

---

## 구현 단계

### 1. `ChunkCoords.cs` 생성

```csharp
using UnityEngine;

/// <summary>
/// 청크 좌표 ↔ 월드 좌표 변환 단일 진실 공급원.
/// 모든 청크 위치 계산은 이 클래스를 통한다.
///
/// 규칙:
///   - 청크 1칸 = WorldSize world units (현재 10f)
///   - 청크 피벗은 좌하단 (0,0) 기준
///   - WorldSize 변경 시 이 파일 하나만 수정
/// </summary>
public static class ChunkCoords
{
    /// <summary>청크 1칸의 월드 크기 (world units). 변경 시 이 상수만 수정.</summary>
    public const float WorldSize = 10f;

    /// <summary>청크 좌표 → 월드 위치 (좌하단 피벗)</summary>
    public static Vector3 ToWorld(Vector2Int coord)
        => new Vector3(coord.x * WorldSize, coord.y * WorldSize, 0f);

    /// <summary>월드 위치 → 청크 좌표 (floor)</summary>
    public static Vector2Int ToChunk(Vector3 worldPos)
        => new Vector2Int(
            Mathf.FloorToInt(worldPos.x / WorldSize),
            Mathf.FloorToInt(worldPos.y / WorldSize));

    /// <summary>청크 단위 오프셋 → 월드 오프셋 (멀티청크 서브 위치 계산용)</summary>
    public static Vector3 OffsetToWorld(Vector2Int offset)
        => new Vector3(offset.x * WorldSize, offset.y * WorldSize, 0f);
}
```

### 2. `SpecialChunkManager` 수정

**시그니처 변경:**
```csharp
// Before
public TerrainChunk SpawnSpecialChunkIfPossible(
    Vector2Int coord, TileType layerType, int worldSeed,
    Transform parent, float widthWorld, float heightWorld,
    out List<...> outSubChunks)

// After
public TerrainChunk SpawnSpecialChunkIfPossible(
    Vector2Int coord, TileType layerType, int worldSeed,
    Transform parent,
    out List<...> outSubChunks)
```

**내부 계산 변경:**
```csharp
// Before
Vector3 anchorPos = new Vector3(coord.x * widthWorld, coord.y * heightWorld, 0);
Vector3 subPos    = new Vector3(subCoord.x * widthWorld, subCoord.y * heightWorld, 0);

// After
Vector3 anchorPos = ChunkCoords.ToWorld(coord);
Vector3 subPos    = ChunkCoords.ToWorld(subCoord);
```

### 3. `StandardChunkFactory` 수정

```csharp
// Before
Vector3 worldPos = new Vector3(
    context.Coord.x * context.ChunkWidthWorld,
    context.Coord.y * context.ChunkHeightWorld, 0);

// After
Vector3 worldPos = ChunkCoords.ToWorld(context.Coord);
```

`ChunkGenerationContext`에서 `ChunkWidthWorld` / `ChunkHeightWorld` 필드는
다른 사용처가 없으면 함께 제거한다. (사용처 확인 후 결정)

### 4. `ChunkDataProvider` 수정

```csharp
// Before
var specialChunk = SpecialChunkManager.Instance.SpawnSpecialChunkIfPossible(
    coord, tileType, _worldSeed, parent, chunkWidthWorld, chunkHeightWorld, out subChunks);

// After
var specialChunk = SpecialChunkManager.Instance.SpawnSpecialChunkIfPossible(
    coord, tileType, _worldSeed, parent, out subChunks);
```

---

## 주의사항

- `ChunkWidthWorld` / `ChunkHeightWorld`가 `ChunkGenerationContext`에서 다른 용도로 쓰이면 유지한다.
  (예: 콜라이더 크기, 카메라 범위 계산 등) — 구현 전 grep으로 사용처 확인.
- `ChunkCoords.WorldSize = 10f`는 `TileDataManager.sourceWidth(1000) / pixelsPerUnit(100)`에서 유도된 값.
  만약 청크 크기가 변경될 경우 `TileDataManager`와 함께 이 상수도 동기화해야 한다.
- 오버로드(`widthWorld`, `heightWorld` 없는 버전)가 기존에 있으므로 제거 시 호환 유지 확인.
- SOLID: 이 클래스는 순수 유틸리티 (static) — MonoBehaviour 상속 없음, 싱글턴 아님.

---

## 기대 효과

- 프리팹 배치 시 "1청크 = ChunkCoords.WorldSize" 상수를 참조 → 수작업 계산 오류 제거
- `widthWorld`/`heightWorld` 파라미터 전달 체인 단순화
- 청크 크기 변경 시 `ChunkCoords.WorldSize` 한 줄만 수정

---

## 테스트 방법

1. 게임 실행 → `[StandardChunkFactory]` / `[SpecialChunkManager]` 로그에서 좌표가 기존과 동일한지 확인
2. 특수 청크 등장 위치가 변경 전후 동일한지 확인
3. `[ZoneEffectTrigger] OnEnable — bounds=(40, 10)` 유지 확인 (프리팹 수정 영향 없음)
