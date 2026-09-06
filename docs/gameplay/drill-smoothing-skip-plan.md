# 드릴 ImmediateDig 스무딩 Skip 구현 계획
@tags: drill, smoothing, ImmediateDig, FixedUpdate, erosion, TerrainModifier, plan

> **작성일**: 2026-03-27
> **관련 문서**: [`terrain-smoothing-chaikin-laplacian.md`](./terrain-smoothing-chaikin-laplacian.md)

---

## 1. 문제 요약

드릴 대시 중 `ImmediateDig`가 **매 FixedUpdate (~50Hz)** 마다 호출되면서
`RemoveNarrowProtrusions` + `ErodeEdges`가 과도하게 실행돼 지형이 예상보다 많이 깎인다.

**근본 원인**: `ImmediateDig`는 "즉시 장애물 제거"가 목적인데, 스무딩까지 담당하고 있다.
**해결 원칙**: 스무딩은 0.4초 주기의 `RequestDig`만 담당하게 한다.

---

## 2. 콜 체인 전체 구조

```
[ImmediateDig 경로] FixedUpdate마다 (~50Hz)
Digger.ImmediateDig(worldPos)
  └─ ITerrainManager.ModifyTerrain(worldPos, digRadius, toolIndex=3)
       └─ InfinityMapManager.ModifyTerrain(...)
            └─ TerrainChunk.Dig(..., toolIndex=3)
                 ├─ RemoveNarrowProtrusions  ← 50Hz마다 실행 (문제)
                 ├─ ErodeEdges(1.0f)         ← 50Hz마다 실행 (문제)
                 └─ CheckFloatingIslands     ← 필요 (고립 픽셀 방지)

[RequestDig 경로] 0.4초마다
Digger.RequestDig → TryDig → DigRoutine → DigAt
  └─ ITerrainManager.ModifyTerrain(pos, radius, toolIndex=3)
       └─ TerrainChunk.Dig(...)
            ├─ RemoveNarrowProtrusions  ← 0.4초마다, 정상
            ├─ ErodeEdges(1.0f)         ← 0.4초마다, 정상
            └─ CheckFloatingIslands

[SapStrategy / PickaxeStrategy 경로] (ITerrainModifiable 경유, 변경 없음)
chunk.ModifyTerrain(worldPos, radius, toolIndex)
  └─ TerrainChunk.Dig(...) — 항상 스무딩 적용
```

**toolIndex만으로 두 경로를 구분할 수 없으므로** (`RequestDig`도 toolIndex=3),
`applySmoothing: bool` 파라미터를 `ITerrainManager → InfinityMapManager → TerrainChunk.Dig`
체인에 추가해 `ImmediateDig`에서만 `false`를 전달한다.

---

## 3. 변경 파일 목록

| # | 파일 | 변경 내용 |
|---|---|---|
| 1 | `ITerrainManager.cs` | `ModifyTerrain`에 `bool applySmoothing = true` 추가 |
| 2 | `InfinityMapManager.cs` | 파라미터 수신 → `tChunk.Dig(..., applySmoothing)` 전달 |
| 3 | `StaticChunkTerrainManager.cs` | 파라미터 수신 → `chunk.Dig(..., applySmoothing)` 전달 |
| 4 | `TerrainChunk.cs` | `Dig()`에 파라미터 추가, 스무딩 블록을 `if (applySmoothing)`으로 감싸기 |
| 5 | `Digger.cs` | `ImmediateDig()`에서 `applySmoothing: false`로 호출 |

**변경 없는 파일**:
- `ITerrainModifiable.cs` — `SapStrategy`/`PickaxeStrategy` 경로, 항상 스무딩 적용
- `TerrainChunk.ModifyTerrain()` (ITerrainModifiable 구현) — `Dig()` default true 사용
- `TerrainModifier.cs` — 로직 자체는 그대로

---

## 4. 파일별 변경 상세

### 4-1. `ITerrainManager.cs`

```csharp
// 변경 전
void ModifyTerrain(Vector2 worldPos, float radius, int toolIndex);

// 변경 후
void ModifyTerrain(Vector2 worldPos, float radius, int toolIndex, bool applySmoothing = true);
```

> **주의**: C# 인터페이스의 default 파라미터는 인터페이스 타입으로 호출할 때만 기본값이 적용된다.
> 구현체(InfinityMapManager, StaticChunkTerrainManager)에도 동일한 default 값을 선언해야
> 구현체 타입으로 직접 호출하는 곳에서도 생략 가능하다.

---

### 4-2. `InfinityMapManager.cs` (`:933`)

```csharp
// 변경 전
public void ModifyTerrain(Vector2 targetWorldPos, float radius, int toolIndex)
{
    ...
    tChunk.Dig(targetWorldPos, radius, toolIndex);
    ...
}

// 변경 후
public void ModifyTerrain(Vector2 targetWorldPos, float radius, int toolIndex, bool applySmoothing = true)
{
    ...
    tChunk.Dig(targetWorldPos, radius, toolIndex, applySmoothing);
    ...
}
```

`anyModified` 판정, 이벤트 발생 등 나머지 로직은 그대로 유지.

---

### 4-3. `StaticChunkTerrainManager.cs` (`:100`)

```csharp
// 변경 전
public void ModifyTerrain(Vector2 worldPos, float radius, int toolIndex)
{
    ...
    chunk.Dig(worldPos, radius, toolIndex);
    ...
}

// 변경 후
public void ModifyTerrain(Vector2 worldPos, float radius, int toolIndex, bool applySmoothing = true)
{
    ...
    chunk.Dig(worldPos, radius, toolIndex, applySmoothing);
    ...
}
```

---

### 4-4. `TerrainChunk.cs` — 핵심 변경

**`Dig()` 시그니처** (`:554`)

```csharp
// 변경 전
public void Dig(Vector2 mouseWorldPos, float radius, int toolIndex)

// 변경 후
public void Dig(Vector2 mouseWorldPos, float radius, int toolIndex, bool applySmoothing = true)
```

**`Dig()` 본문** — 스무딩 블록 + 섬 감지 모두 `if (applySmoothing)`으로 감싸기

```csharp
if (result.WasModified)
{
    // ↓ 변경: applySmoothing일 때만 실행
    if (applySmoothing)
    {
        _modifier.RemoveNarrowProtrusions(
            _data, result.ClampedMinX, result.ClampedMinY,
            result.ClampedMaxX, result.ClampedMaxY,
            narrowThreshold: 6
        );

        _modifier.ErodeEdges(
            _data, result.ClampedMinX, result.ClampedMinY,
            result.ClampedMaxX, result.ClampedMaxY,
            intensity: 1.0f
        );

        if (useIslandRemoval)
        {
            _modifier.CheckFloatingIslandsInArea(
                _data, result.ClampedMinX, result.ClampedMinY,
                result.ClampedMaxX, result.ClampedMaxY,
                PixelToWorldPos, debrisCallback
            );
        }
    }

    _data.CurrentPixels.CopyFrom(_data.BasePixels);

    // 이하 바위 노출, DirtyRect, MarkNeighborsDirty 등 변경 없음
    ...
}
```

#### CheckFloatingIslandsInArea도 skip하는 이유

| 항목 | 수치 |
|---|---|
| `Array.Clear` 비용 (40KB 청크 기준) | ~4μs/call |
| 50Hz × 경계 2청크 | ~400μs/s = 프레임 예산의 0.04% |

비용 자체는 무시 가능하지만, `ImmediateDig`의 파기 패턴상 섬이 발생할 가능성이 낮다.

- `ImmediateDig`는 **전방 연속 지형**을 직접 제거 → 섬 구조 자체가 생기지 않음
- 섬은 여러 구멍이 맞닿아 흙 기둥이 분리될 때 발생하는데, 이는 스무딩(`RemoveNarrowProtrusions`)이 담당
- 만약 섬이 생기더라도 **0.4초 후 `RequestDig`의 `CheckFloatingIslands`** 가 정리함

혹시 고립 픽셀이 관찰되면 다음처럼 쉽게 분리해 복구할 수 있다:

```csharp
if (applySmoothing)
{
    _modifier.RemoveNarrowProtrusions(...);
    _modifier.ErodeEdges(...);
}
// 섬 감지는 항상
if (useIslandRemoval)
    _modifier.CheckFloatingIslandsInArea(...);
```

**`TerrainChunk.ModifyTerrain()` (ITerrainModifiable)** — 변경 없음

```csharp
// 그대로 유지. Dig()의 default true를 사용하게 됨.
public void ModifyTerrain(Vector2 worldPos, float radius, int toolIndex)
{
    Dig(worldPos, radius, toolIndex); // applySmoothing = true (default)
}
```

---

### 4-5. `Digger.cs` — `ImmediateDig()` (`:120`)

```csharp
// 변경 전
public void ImmediateDig(Vector2 worldPos)
{
    ...
    mapManager.ModifyTerrain(worldPos, digRadius, 3);
}

// 변경 후
public void ImmediateDig(Vector2 worldPos)
{
    ...
    mapManager.ModifyTerrain(worldPos, digRadius, 3, applySmoothing: false);
}
```

`RequestDig → DigAt`에서의 `mapManager.ModifyTerrain(actualHitPos, effectiveRadius, toolIndex)` 호출은
`applySmoothing` 인자를 생략 → default `true` 적용 → **변경 없음**.

---

## 5. 스무딩 분담 결과

| 호출 경로 | 빈도 | RemoveNarrowProtrusions | ErodeEdges | CheckFloatingIslands |
|---|---|---|---|---|
| `ImmediateDig` | ~50Hz | ❌ skip | ❌ skip | ❌ skip |
| `RequestDig` (드릴 대시) | 0.4s | ✅ | ✅ | ✅ |
| `RequestDig` (일반 채굴) | 쿨다운마다 | ✅ | ✅ | ✅ |
| `SapStrategy.ModifyTerrain` | 타격마다 | ✅ | ✅ | ✅ |

---

## 6. 주의사항

### C# 인터페이스 default 파라미터

```csharp
// 인터페이스 타입으로 호출할 때
ITerrainManager mgr = ...;
mgr.ModifyTerrain(pos, r, tool);  // → applySmoothing = true (인터페이스 default)

// 구현체 타입으로 직접 호출할 때
InfinityMapManager mgr = ...;
mgr.ModifyTerrain(pos, r, tool);  // → applySmoothing = true (구현체 default)
// ↑ 구현체에도 동일한 default 선언이 있어야 함
```

인터페이스와 구현체 모두 `= true` default를 명시해야 기존 호출 코드를 건드리지 않아도 된다.

### `Explode()`는 변경 대상 아님

`Explode`는 `DrillStrategy`가 호출하지 않으며, 원형 폭발은 스무딩이 항상 필요하다.
`TerrainChunk.Explode()` 내부 스무딩 블록은 그대로 유지한다.

### 향후 새로운 `ITerrainManager` 구현체 추가 시

`ModifyTerrain(Vector2, float, int, bool applySmoothing = true)` 시그니처를 반드시 구현해야 한다.
미구현 시 컴파일 에러로 즉시 감지되므로 런타임 위험은 없다.

---

## 7. 검증 방법

1. **드릴 대시 중 Console 로그 확인**
   `TerrainModifier.ErodeEdges()` 첫 줄에 임시 로그 추가 후 플레이.
   대시 중 `~50건/초 → 약 2~3건/초`로 줄어들면 성공.

2. **스무딩 품질 확인**
   일반 채굴(픽아웃 등)에서 모서리가 여전히 둥글게 깎이는지 확인.

3. **고립 픽셀 확인**
   드릴 대시 후 텍스처에 독립적인 점 형태 픽셀이 남지 않는지 확인.
   (`CheckFloatingIslands`가 항상 실행되므로 정상이면 없어야 함)

---

*참고 파일*
- [`ITerrainManager.cs`](../../Assets/Scripts/_Core/Data/ITerrainManager.cs)
- [`InfinityMapManager.cs`](../../Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs)
- [`StaticChunkTerrainManager.cs`](../../Assets/Scripts/Gameplay/Terrain/Tiles/StaticChunkTerrainManager.cs)
- [`TerrainChunk.cs`](../../Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs)
- [`Digger.cs`](../../Assets/Scripts/Gameplay/Terrain/Tiles/Digger.cs)
- [`terrain-smoothing-chaikin-laplacian.md`](./terrain-smoothing-chaikin-laplacian.md)
