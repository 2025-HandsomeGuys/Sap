# 텍스처 레이어 모서리 뾰족 아티팩트 분석
@tags: chamfer, distance-field, border-texture, corner-artifact, UV, visual, rendering, chunk-boundary, VisualUpdateJob, ChamferForwardPassJob

## 1. 현상

돌(암석) 지형에 texture layer(border texture)가 적용될 때,
두 모서리(edge)가 만나는 지점에서 텍스처 밴드가 **뾰족하게 튀어나오거나 안쪽으로 침범**하는 시각적 아티팩트가 발생한다.

```
정상적인 경우 (직선 edge):     문제 발생 (볼록 모서리):

  AIR  AIR  AIR               AIR  AIR  AIR
  ████ ████ ████               AIR  ████ ████
  ████ ████ ████               AIR  ████ ████
  [T]  [T]  [T]   ← 텍스처    [T]  [T] [T?] ← 대각선 방향으로 뾰족하게 연장
```

## 2. 관련 코드 위치

| 파일 | 역할 |
|------|------|
| `TerrainJobs.cs` | `VisualUpdateJob` — 거리 기반 텍스처 샘플링 |
| `TerrainJobs.cs` | `ChamferForwardPassJob` / `ChamferBackwardPassJob` — 거리 필드 계산 |
| `TerrainVisualizer.cs` | `UpdateVisualsArea()` — 전체 비주얼 파이프라인 |

## 3. 원인 분석

### 3-1. 렌더링 파이프라인 요약

```
픽셀 정보(baseData)
       ↓
[InitBFSJob] — 에어=0, 솔리드=maxDist 초기화
       ↓
[ChamferForwardPassJob]  ← 좌상→우하 스캔
[ChamferBackwardPassJob] ← 우하→좌상 스캔
       ↓
distanceField[i] = "이 솔리드 픽셀이 가장 가까운 에어 픽셀까지의 Chamfer 5-7 거리"
       ↓
[VisualUpdateJob]
  dist <= textureThicknessPx * 5  → borderTexture 샘플링
  V = dist / 5                    (깊이 방향 UV)
  U = globalX % borderWidth       (수평 방향 UV)
```

### 3-2. Chamfer 5-7 거리 필드의 특성

Chamfer 5-7은 수평/수직 이동 비용=5, 대각선 이동 비용=7로 거리를 근사한다.

- **직선 edge**: 에어에서 N픽셀 떨어진 솔리드 → `dist = N * 5`
- **볼록 모서리(convex corner, 솔리드가 튀어나온 귀퉁이)**:
  - 모서리 바로 안쪽 대각선 픽셀은 수평 edge거리 5 + 수직 edge거리 5 중 작은 쪽에서 전파
  - `dist = 7` (대각선 에어로부터) vs `dist = 5+5=10` (두 직선 경로)
  - 결과: **모서리 안쪽 대각 픽셀이 dist=7**로, 직선보다 얕게(더 얕은 V) 계산됨

- **오목 모서리(concave corner, 에어가 파고든 안쪽 귀퉁이)**:
  - 모서리 솔리드 픽셀은 두 방향의 에어에서 동시에 dist=5를 받음
  - 대각선 방향으로 dist 전파가 겹쳐서 모서리 근방 솔리드 픽셀들이 예상보다 작은 dist를 가짐

### 3-3. 뾰족함의 발생 메커니즘

#### 케이스 A — 오목 모서리(inside corner) 뾰족 침범

```
S S S
A S S    ← S(1,1)의 dist = min(5 from A(0,1), 5 from A(1,0)) = 5
A A S
```

모서리 픽셀 S(1,1)의 dist=5는 정상이지만, 그 위 픽셀 S(1,2):
- A(0,2)와 대각선 → dist = 7
- S(1,1)=5 에서 전파 → dist = 10
- **실제 dist = 7** → 텍스처 밴드가 예상보다 모서리 안쪽으로 파고듦

그 결과 오목 모서리 주변에서 텍스처 밴드가 대각선 방향으로 "뾰족하게" 내부 쪽으로 늘어난다.

```
dist 값 분포 예시 (textureThickness=4, threshold=20):

  S    S    S    S
  5    10   15   20      ← 좌측 직선 edge (정상 4픽셀 밴드)
  5    7    12   17      ← 오목 모서리 위 행 (대각선 7로 인해 1픽셀 추가 침범)
  A    5    10   15      ← 바닥 직선 edge (정상)
  A    A    A    A
```

#### 케이스 B — U 좌표 고정으로 인한 방향성 부재

현재 UV 매핑:
```csharp
int v = (int)(dist / 5);         // 깊이(에어로부터의 거리)
int u = globalX % borderWidth;   // 항상 월드 X 위치 기준
```

수평 edge에서는 U=X가 자연스럽지만, **수직 edge**에서는 같은 열(column)에 있는 픽셀이 모두 같은 U를 가진다. 모서리에서 edge가 수평→수직으로 꺾이는 순간, U가 갑자기 "멈춘" 것처럼 보이거나 텍스처가 부자연스럽게 반복된다.

## 4. 해결 방안 후보

### 방안 1 — Chamfer 거리 보정 (권장)

오목 모서리에서 대각선 전파된 픽셀의 dist를 보정한다.
인접 두 방향 모두 에어에 면한 픽셀(inside corner)을 감지하고,
`dist = min(dist_horizontal, dist_vertical)` 대신
`dist = sqrt(dist_h² + dist_v²)` 에 가까운 값으로 교정하는 후처리 패스 추가.

```
보정 아이디어:
  if (dist_x < threshold AND dist_y < threshold)
      dist = hypot(dist_x, dist_y)  // Chamfer 오차 교정
```

장점: UV 로직 변경 불필요
단점: 추가 패스 필요, 모서리 감지 로직 구현 복잡

---

### 방안 2 — 에어 방향 벡터 기반 UV 회전

각 솔리드 픽셀에 대해 "가장 가까운 에어 픽셀의 방향 벡터"를 계산하고,
그 방향에 따라 U 좌표를 수평/수직 중 맞는 축으로 선택한다.

```
edge_dir = normalize(air_pixel_pos - solid_pixel_pos)
if abs(edge_dir.x) > abs(edge_dir.y):
    u = globalX % borderWidth      // 수평 edge
else:
    u = globalY % borderHeight     // 수직 edge
```

장점: edge 방향에 따라 텍스처가 자연스럽게 매핑됨
단점: 방향 벡터 계산을 위한 별도 필드(방향 맵) 필요 → 메모리/연산 증가

---

### 방안 3 — 모서리 픽셀 알파 블렌딩(마스킹)

볼록 모서리 근처 픽셀은 텍스처 알파를 `dist / threshold`로 부드럽게 fade-out하여
뾰족하게 튀어나오는 부분을 시각적으로 무디게 만든다.

```csharp
float fade = (float)dist / (textureThicknessPx * 5);
Color32 result = LerpColor(borderCol, baseCol, fade);
```

장점: 구현 가장 단순
단점: 전체 텍스처 밴드의 선명도가 줄어들 수 있음

---

### 방안 4 — Min-of-axes 거리 (직선 edge 전용 제한)

현재 Chamfer 거리 대신, `min(수평 에어까지 거리, 수직 에어까지 거리)` 를 사용.
대각선 전파를 무시하면 모서리 뾰족 현상이 사라지지만,
실제 모서리 픽셀에서 텍스처가 예상보다 작게 적용될 수 있다.

## 5. 우선 구현 방향

**방안 1 (Chamfer 보정)** + **방안 3 (fade-out 블렌딩)** 조합이 현실적.

1. `ChamferBackwardPassJob` 완료 후 **CornerCorrectionJob** 추가:
   - 인접 4방향 거리 중 2방향 이상이 서로 다른 에지 방향에서 왔다면 보정
2. `VisualUpdateJob`에서 `dist / (textureThicknessPx * 5)` 비율로 경계부 fade 처리

## 6. 관련 파일

- `Assets/Scripts/_Core/Managers/TerrainJobs.cs` — Job 정의 (수정 필요)
- `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainVisualizer.cs` — 파이프라인 (CornerCorrectionJob 스케줄 추가)
- `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` — ChunkJobScheduler 통해 간접 연결
