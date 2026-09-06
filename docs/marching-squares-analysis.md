# 마칭스퀘어(Marching Squares) 적용 가능성 분석
@tags: marching-squares, collider, mesh, distance-field, pixel-based, TerrainCollider, TerrainModifier, refactoring, rendering, density

> **대상 시스템**: 현재 픽셀 기반 땅파기 시스템 (`Digger`, `TerrainModifier`, `TerrainVisualizer`, `TerrainCollider`)
> **작성일**: 2026-03-25

---

## 1. 현재 시스템 구조 요약

현재 땅파기 시스템은 **픽셀(비트맵) 기반**이다.

| 역할 | 구현 방식 |
|------|-----------|
| **데이터 저장** | `ChunkData.BasePixels` — `Color32[]` NativeArray |
| **파기 로직** | `TerrainModifier.Dig()` — 타원(ellipse) 범위 내 픽셀 alpha를 0으로 만듦 |
| **비주얼** | `TerrainVisualizer` — Chamfer 거리 변환 후 GPU 텍스처 업로드 |
| **콜라이더** | `TerrainCollider` — Moore-Neighbor Tracing + RDP 단순화 → `PolygonCollider2D` |
| **섬 제거** | `TerrainModifier.CheckFloatingIslands()` — BFS로 공중 픽셀 클러스터 제거 |

**핵심 특성**: 픽셀 하나하나가 "있음(solid)" / "없음(air)"의 이진 상태를 가짐. 연속적인 밀도(density) 값은 없음.

---

## 2. 마칭스퀘어(Marching Squares)란?

2D 등치선(contour) 추출 알고리즘. 격자(grid)의 각 셀 4개 꼭짓점에 **스칼라 밀도값(density)**이 있고, 임계값(threshold)을 기준으로 경계를 보간해 **부드러운 곡선 메시**를 생성한다.

```
Density grid 예시:
  0.9  0.8
  0.3  0.1
→ threshold=0.5 기준으로 좌변/상변에 보간점 생성 → 부드러운 경계선
```

주요 사용처: **Terraria 스타일** 보다 진보된 지형, **Noita**, **Lode Runner** 류의 연속 지형.

---

## 3. 적용 가능성 분석

### 3-A. 기술적 가능성

#### ✅ 가능한 부분
- 현재 `ChunkData`의 `PixelInfo` 배열을 **density 필드로 확장**하는 것은 구조적으로 가능하다.
- `TerrainModifier.Dig()`에서 픽셀을 삭제하는 대신 **density를 감소**시키도록 변경할 수 있다.
- `TerrainCollider`의 Moore-Neighbor Tracing을 **MS 기반 등치선 추출**로 교체 가능하다.
- Unity Job System 상에서 MS를 병렬로 돌릴 수 있다.

#### ❌ 어려운 부분

| 문제 | 설명 |
|------|------|
| **렌더링 패러다임 전환** | 현재는 픽셀 텍스처를 그대로 GPU에 올리는 방식. MS는 **메시(Mesh)** 기반 렌더링이 필요해 `SpriteRenderer`에서 `MeshRenderer`로 전환 필수 |
| **Chamfer 조명 시스템 폐기** | `TerrainVisualizer`의 2-pass Chamfer 거리 변환 기반 텍스처 경계 표현이 MS와 병존 불가 |
| **ColorMap/텍스처 매핑 재설계** | 현재 `BasePixels[index]`의 색상이 곧 비주얼인데, MS 메시 위에 텍스처를 UV 매핑으로 입히려면 완전히 다른 파이프라인 필요 |
| **청크 경계 연속성** | 인접 청크 간 density 값 공유가 필요. 현재는 픽셀 단위 `OverlapCircle` 방식이라 경계 처리가 비교적 단순함 |
| **파티클/이벤트 시스템** | `OnPixelDestroyed` 이벤트, `TileEventDispatcher` 등이 모두 **픽셀 인덱스** 기반. MS 전환 시 전부 재설계 필요 |
| **IndestructibleMask** | 현재 픽셀 단위 마스크가 density 필드에서 어떻게 동작해야 하는지 명확하지 않음 |

---

### 3-B. 하이브리드 접근 (부분 적용)

마칭스퀘어를 **콜라이더 생성에만** 적용하는 방식도 고려할 수 있다.

```
픽셀 데이터(이진) → 2×2 셀 블록으로 그룹화 → MS로 경계 추출 → PolygonCollider2D
```

- **장점**: 현재 렌더링 파이프라인 유지, 콜라이더만 더 부드러워짐
- **단점**: 이진 데이터에 MS를 적용하면 기존 Moore-Neighbor Tracing 대비 품질 차이가 거의 없음 (MS의 진가는 연속 density에서 나옴)

---

## 4. 장단점 비교

### 마칭스퀘어 도입 시 장점

| 장점 | 설명 |
|------|------|
| **부드러운 지형 경계** | 픽셀 계단 현상 없이 곡선 형태의 구멍 생성 가능 |
| **유기적인 땅파기 느낌** | Dig 반경이 클수록 자연스러운 동굴 형태 |
| **콜라이더 정밀도 향상** | 등치선 기반이라 적은 버텍스로 더 정확한 형태 |

### 마칭스퀘어 도입 시 단점 / 리스크

| 단점 | 설명 |
|------|------|
| **대규모 리팩토링 필요** | 렌더링·콜라이더·이벤트 시스템 전체를 교체해야 함 |
| **성능 불확실성** | 현재 시스템은 픽셀 레벨로 최적화됨. MS 메시는 청크당 동적 메시 생성 비용 발생 |
| **아트 파이프라인 재구축** | 픽셀 색상 → UV 텍스처 매핑 → 셰이더 방식으로 전환 필요 |
| **기존 기능 파손 위험** | `DiggableRock`, `IDiggable`, `IIndestructibleHit` 등 모든 연관 시스템 영향 |
| **개발 일정 리스크** | 최소 수개월의 추가 작업 예상 |

---

## 5. 결론 및 권고사항

### 결론

> **기술적으로 가능하지만, 현시점에서 도입은 권장하지 않는다.**

현재 시스템은 픽셀 기반으로 이미 잘 구조화되어 있고 (`TerrainModifier`, `TerrainVisualizer`, `TerrainCollider`가 분리됨), 마칭스퀘어 도입은 단순한 기능 추가가 아닌 **렌더링 패러다임 전체의 교체**를 의미한다.

### 권고: 언제 고려할 것인가?

| 상황 | 판단 |
|------|------|
| 현재 픽셀 계단 현상이 게임플레이에 실제로 문제가 되는가? | → 문제가 없다면 도입 불필요 |
| 아트 방향이 "픽셀 아트"인가? | → 픽셀 아트라면 MS는 오히려 방해 |
| 아트 방향이 "유기적 곡선 지형"인가? | → 그렇다면 **처음부터** MS 기반으로 설계했어야 했고, 지금 전환은 고비용 |
| 콜라이더만 개선하고 싶다면? | → RDP 허용 오차(`_colliderSimplifyTolerance`) 조정 또는 MS 하이브리드 시도 가능 |

### 현실적 대안

1. **단기**: `_colliderSimplifyTolerance` 값 튜닝으로 콜라이더 품질 개선 (리스크 제로)
2. **중기**: 현재 이진 픽셀 시스템 유지하되, `PixelInfo`에 soft erosion(가장자리 density 페이드) 추가로 시각적 계단 현상 완화
3. **장기**: 만약 게임의 아트 방향이 전환된다면, 그 시점에 MS 도입을 **신작 기준으로** 고려

---

*참고 파일*
- [`Digger.cs`](../Assets/Scripts/Gameplay/Terrain/Tiles/Digger.cs)
- [`TerrainModifier.cs`](../Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs)
- [`TerrainVisualizer.cs`](../Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainVisualizer.cs)
- [`TerrainCollider.cs`](../Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainCollider.cs)
