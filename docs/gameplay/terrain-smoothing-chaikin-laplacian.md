# 땅파기 구멍 스무딩: 픽셀 침식(Edge Erosion)
@tags: smoothing, erosion, pixel, TerrainModifier, edge-erosion, collider, chamfer, dig, rendering

> **대상 파일**: `TerrainModifier.cs`, `TerrainChunk.cs`, `TerrainCollider.cs`
> **최초 작성**: 2026-03-25 / **최종 수정**: 2026-05-20

---

## 1. 문제 정의

파기 후 `BasePixels`의 구멍 가장자리가 픽셀 격자 모양 그대로 남아 **계단·뾰족한 모서리**처럼 보인다.

```
파기 직후 (계단형)        픽셀 침식 1회 후 (모서리 깎임)

████░████             ████░████
████░████     →       ███░░░███
████░░░░░             ███░░░░░░
████░░░░░             ████░░░░░
```

### 왜 콜라이더 스무딩만으로는 안 되나

| 대상 | 효과 |
|---|---|
| `PolygonCollider2D` 경로 좌표 스무딩 | 물리 충돌 형태만 변경, 텍스처는 그대로 |
| **`BasePixels` 픽셀 침식** | 텍스처 자체가 부드럽게 깎임 (원하는 것) |

---

## 2. 구현된 스무딩 메서드 (실제 코드 기준)

### 2-A. `RemoveNarrowProtrusions` — 좁은 돌기 제거

`TerrainModifier.cs:518`

여러 원형 구멍이 이어붙을 때 생기는 **뾰족한 흙 돌기**를 제거한다.

```csharp
public void RemoveNarrowProtrusions(ChunkData data, int minX, int minY, int maxX, int maxY, int narrowThreshold = 6)
```

**판별 로직**: 4방향(수평·수직·대각×2)으로 각각 solid 폭을 측정해,
가장 좁은 방향의 폭 ≤ `narrowThreshold`이면 제거.

```
widthH  = 수평 폭
widthV  = 수직 폭
widthD1 = 대각(↗↙) 폭
widthD2 = 대각(↘↖) 폭
minWidth = min(widthH, widthV, widthD1, widthD2)
minWidth ≤ narrowThreshold → 제거
```

### 2-B. `ErodeEdges` — CA 이웃 카운팅 침식

`TerrainModifier.cs:600`

```csharp
public void ErodeEdges(ChunkData data, int minX, int minY, int maxX, int maxY, int airThreshold = 5)
```

**파라미터**:
- `airThreshold` (기본: 5): 8방향 이웃 중 빈 픽셀 수가 이 값 이상이면 제거.
  - 3 = 공격적, 5 = 보통, 7 = 보수적

**판별 로직 (Cellular Automata)**:
```
8방향 이웃 빈 픽셀 수 >= airThreshold → 제거
```

방향 조합 조건(AND) 없이 **고립도**만 판별하므로 어느 방향으로 노출되든 균일하게 깎임.  
경계 밖 좌표는 `IsAirForErosion()`에서 solid로 취급 → 경계 픽셀 과침식 방지.

---

## 3. 실제 호출 순서 (TerrainChunk.cs 기준)

`TerrainChunk.Dig()` (`:571`) — `applySmoothing` 파라미터로 스무딩 전체를 on/off한다:

```csharp
public void Dig(Vector2 mouseWorldPos, float radius, int toolIndex, bool applySmoothing = true)

var result = _modifier.Dig(data, ...);

if (result.WasModified)
{
    if (applySmoothing)
    {
        // 1. 좁은 돌기 제거 — 뾰족한 흙 기둥 제거
        _modifier.RemoveNarrowProtrusions(
            _data, result.ClampedMinX, result.ClampedMinY,
            result.ClampedMaxX, result.ClampedMaxY,
            narrowThreshold: 6
        );

        // 2. 모서리 침식 — 남은 돌출 픽셀 제거
        _modifier.ErodeEdges(
            _data, result.ClampedMinX, result.ClampedMinY,
            result.ClampedMaxX, result.ClampedMaxY,
            intensity: 1.0f
        );

        // 3. 섬 제거 — 침식 후 고립 픽셀도 제거
        _modifier.CheckFloatingIslandsInArea(_data, ...);
    }

    _data.CurrentPixels.CopyFrom(_data.BasePixels);
}
```

`TerrainChunk.Explode()` (`:667`)는 `applySmoothing` 파라미터 없이 항상 세 단계 모두 적용한다.

> [!IMPORTANT]
> 실제 순서: **`RemoveNarrowProtrusions` → `ErodeEdges` → `CheckFloatingIslands`**
> (초기 설계 문서의 순서와 다름. `RemoveNarrowProtrusions`가 먼저임)

---

## 4. 드릴 과침식 문제 (✅ 해결됨 — 2026-05-20)

### 현상 (과거)

드릴 대시 중 90도 직각 지형이 생기거나, 반대로 구멍이 예상보다 훨씬 크게 파임.

### 원인: ErodeEdges 과호출

`DrillStrategy.HandleFixedUpdate()` (`:231`)에서 **매 FixedUpdate (~50Hz)** 마다 `ImmediateDig`가 호출되어,
결과적으로 같은 위치에 **50+ 회/초의 침식**이 반복 적용되었다.

### 해결: `applySmoothing: false`

`Digger.ImmediateDig()` (`:133`)가 스무딩 없이 지형만 파도록 수정되었다:

```csharp
// Digger.cs:153
mapManager.ModifyTerrain(worldPos, digRadius, 3, applySmoothing: false);
```

`applySmoothing: false`이면 `TerrainChunk.Dig()` 내부에서 `RemoveNarrowProtrusions`, `ErodeEdges`, `CheckFloatingIslandsInArea` 세 단계를 모두 skip한다.
0.4초마다 호출되는 `RequestDig` 경로는 `applySmoothing: true`(기본값)로 동작해 스무딩을 정상 적용한다.

### 두 파기 경로의 차이 (현재)

| 경로 | 호출 위치 | 반경 | 빈도 | 스무딩 |
|---|---|---|---|---|
| `ImmediateDig` | `playerPos + dir * digRadius` | `digRadius` (전체) | FixedUpdate (~50Hz) | ❌ skip |
| `RequestDig` → `DigAt` | `playerPos + dir * effectiveRadius` | `digRadius * 0.5` | 0.4초마다 | ✅ 적용 |

---

## 5. 문제 진단 방법

### A. ErodeEdges 호출 횟수 카운팅

`applySmoothing: true` 경로(RequestDig)가 예상보다 많이 호출되는지 확인:

```csharp
public void ErodeEdges(ChunkData data, int minX, int minY, int maxX, int maxY, float intensity = 1.0f)
{
    // [DEBUG] 호출 횟수 확인
    Debug.Log($"[ErodeEdges] called. Frame={Time.frameCount}");
    ...
}
```

드릴 대시 중 1초에 수 건 이상이면 `applySmoothing` 분기 조건을 재확인.

### B. ErodeEdges만 임시 비활성화

`TerrainChunk.Dig()`의 `if (applySmoothing)` 블록 안에서 `ErodeEdges` 줄 주석 처리:

```csharp
// _modifier.ErodeEdges(_data, ...);   // [TEST] 비활성화
```

90도 지형이 사라지면 → ErodeEdges 자체 로직이 문제.
90도 지형이 유지되면 → `applySmoothing: true` 호출 자체가 너무 잦음.

### C. RemoveNarrowProtrusions 단독 테스트

`narrowThreshold`를 0으로 낮춰 비활성화에 가깝게:

```csharp
_modifier.RemoveNarrowProtrusions(_data, ..., narrowThreshold: 0);
```

결과가 달라지면 → 돌기 제거가 과도하게 동작 중.

---

## 6. 콜라이더 추가 스무딩 (선택)

픽셀 침식과 **병행** 가능. 콜라이더 폴리곤을 추가로 부드럽게 만들 때 사용.

`TerrainCollider.SimplifyAndCollectPath()` 내 RDP 이후에 삽입:

### Chaikin's Algorithm (선분 1/4·3/4 분할)

```csharp
private void ApplyChaikin(List<Vector2> points, int iterations = 1)
{
    if (points.Count < 3) return;
    var buf = new List<Vector2>(points.Count * 2);
    for (int iter = 0; iter < iterations; iter++)
    {
        int n = points.Count;
        buf.Clear();
        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = points[i];
            Vector2 p1 = points[(i + 1) % n];
            buf.Add(0.75f * p0 + 0.25f * p1);
            buf.Add(0.25f * p0 + 0.75f * p1);
        }
        points.Clear();
        points.AddRange(buf);
    }
}
```

### Laplacian Smoothing (이웃 평균)

```csharp
private void ApplyLaplacian(List<Vector2> points, int iterations = 3, float lambda = 0.4f)
{
    if (points.Count < 3) return;
    int n = points.Count;
    var temp = new Vector2[n];
    for (int iter = 0; iter < iterations; iter++)
    {
        for (int i = 0; i < n; i++)
        {
            Vector2 avg = (points[(i - 1 + n) % n] + points[(i + 1) % n]) * 0.5f;
            temp[i] = Vector2.Lerp(points[i], avg, lambda);
        }
        for (int i = 0; i < n; i++) points[i] = temp[i];
    }
}
```

---

## 7. 비교 요약

| 방법 | 적용 대상 | 시각 변화 | 콜라이더 변화 | 비용 |
|---|---|---|---|---|
| **`RemoveNarrowProtrusions`** | `BasePixels` | ✅ | ✅ 자동 반영 | 파기 영역 4방향 스캔 |
| **`ErodeEdges`** (픽셀 침식) | `BasePixels` | ✅ | ✅ 자동 반영 | 파기 영역만 순회 |
| Chaikin (콜라이더) | 폴리곤 경로 | ❌ 없음 | ✅ 부드러워짐 | 점 수 2배 |
| Laplacian (콜라이더) | 폴리곤 경로 | ❌ 없음 | ✅ 부드러워짐 | 점 수 유지 |

---

## 8. 파라미터 튜닝 가이드

| 설정 | 값 범위 | 현재값 | 권장 |
|---|---|---|---|
| `ErodeEdges.airThreshold` | 1~8 | **5** | 4~6 (낮을수록 강하게 깎임) |
| `RemoveNarrowProtrusions.narrowThreshold` | 0~10 | **6** | 4~6 |
| Chaikin iterations | 1~2 | - | **1** |
| Laplacian iterations | 2~5 | - | **3** |
| Laplacian lambda | 0.3~0.6 | - | **0.4** |

---

*참고 파일*
- [`TerrainModifier.cs`](../../Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs)
- [`TerrainChunk.cs`](../../Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs)
- [`DrillStrategy.cs`](../../Assets/Scripts/UI/Player/Strategies/DrillStrategy.cs)
- [`TerrainCollider.cs`](../../Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainCollider.cs)
- [`marching-squares-analysis.md`](../marching-squares-analysis.md)
