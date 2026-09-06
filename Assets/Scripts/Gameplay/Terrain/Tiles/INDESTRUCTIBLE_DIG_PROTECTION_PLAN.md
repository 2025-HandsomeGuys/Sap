# Indestructible 주변 지형 파기 차단 계획

## 1. 버그 원인 분석

### 현재 흐름

```
Digger.DigAt()
  └─ OverlapCircleAll(actualHitPos, effectiveRadius)   ← 검사
       └─ IIndestructibleHit 감지 시 → return (파기 중단) ✓
       └─ 미감지 시 → ModifyTerrain() 호출
            └─ TerrainModifier.ProcessDigPixels()
                 └─ IndestructibleMask[idx] != 0 → 픽셀 스킵 ✓
                 └─ 인접 일반 지형 픽셀 → 정상 파괴 ← 버그
```

### 핵심 원인: 검사 원 vs 실제 파기 타원 불일치

```
실제 파기 모양 (TerrainModifier.ProcessDigPixels 내부):
  - 비대칭 타원 (Asymmetric Ellipse)
  - 뒤쪽 (플레이어 방향): 반지름 effectiveRadius
  - 앞쪽 (파기 방향): 반지름 effectiveRadius * VerticalScale (= effectiveRadius * 1.5f)

현재 검사:
  - OverlapCircleAll(actualHitPos, effectiveRadius)
  - 단순 원 → 앞쪽 타원 돌출부를 커버하지 못함

결과:
  actualHitPos를 기준으로 effectiveRadius * 0.5f 만큼
  앞쪽에 있는 indestructible 콜라이더를 검사에서 놓침
  → 해당 영역의 일반 지형 픽셀이 파괴됨
```

### 버그 재현 조건 (구체적 예시)

```
[플레이어](0,0) ---effectiveRadius=2---> [파기중심](2,0)
                         [Indestructible](2.8,0) ← 콜라이더 존재
                                    ^ 검사원(반지름2) 밖, 타원(앞쪽반지름3) 안
                                      → 검사 미감지, 인접 지형 파괴
```

---

## 2. 다른 게임들의 접근법 (참고)

### Terraria (Tile-based LOS 차단)
- **방법**: 각 타일이 독립적인 솔리드 블록. indestructible 타일이 있으면 그 뒤 타일은 마우스가 닿아도 타겟팅 불가.
- **구현 방식**: 플레이어 → 마우스 위치까지 타일 단위 레이캐스트. 중간에 솔리드 타일이 있으면 차단.
- **우리 게임과 차이**: 픽셀 기반이라 타일 단위 레이캐스트 적용 어려움.

### Minecraft (AABB 볼륨 교차 차단)
- **방법**: 곡괭이의 히트박스가 블록의 AABB와 교차할 때만 파괴 가능.
- **구현 방식**: 도구 히트박스(구체/박스) ↔ 블록 AABB 교차 테스트. 불파괴 블록 AABB와 교차하면 모든 파기 차단.
- **우리 게임과의 연결**: 현재 OverlapCircleAll이 이와 유사한 개념이나, 검사 형태가 실제 파기 범위와 불일치.

### Noita (픽셀 인접 버퍼)
- **방법**: 보호된 픽셀 주변 N픽셀 범위도 함께 보호. 보호 픽셀 근처에 파괴 시도 시 무효화.
- **구현 방식**: IndestructibleMask 확장 — 마스크 픽셀의 N픽셀 인접 픽셀도 파괴 불가 처리.
- **장점**: 픽셀 레벨에서 완전한 보호. 어떤 방향에서 파도 indestructible 주변 지형 보호.
- **단점**: ProcessDigPixels 내 픽셀당 추가 검사 → 성능 부담.

### Deep Rock Galactic (물리 콜라이더 우선)
- **방법**: 채굴 드릴이 물리 레이어와 충돌. indestructible 물체는 특정 레이어에 있어서 드릴 관통 불가.
- **구현 방식**: Physics.SphereCastAll로 드릴 경로 전체 스캔. indestructible 레이어 감지 시 파기 취소.
- **우리 게임과의 연결**: CircleCast로 파기 경로 전체 스캔 — 가장 직접적으로 적용 가능.

---

## 3. 선택된 방향: Option B (물리 콜라이더 기반 차단)

> **조건**: 파기 원(effectiveRadius)이 indestructible의 PolygonCollider2D와 겹칠 때만 차단

이 조건을 만족하려면 **검사 형태가 실제 파기 타원을 정확히 커버**해야 한다.

---

## 4. 구현 방안 비교

### 방안 A: OverlapCircle 반지름 확대 (Quick Fix)

```csharp
// 변경 전
Physics2D.OverlapCircleAll(actualHitPos, effectiveRadius)

// 변경 후
float checkRadius = effectiveRadius * VERTICAL_SCALE; // 1.5f
Physics2D.OverlapCircleAll(actualHitPos, checkRadius)
```

- **장점**: 1줄 변경, 즉시 적용 가능
- **단점**: 앞쪽만 고려, 파기 타원의 측면·뒤쪽은 부정확. 약간 과도한 차단 가능성.
- **적합도**: 응급처치용

---

### 방안 B: Physics2D.CircleCastAll (권장)

```csharp
// 파기 경로 전체를 원 스윕으로 스캔
Vector2 digDirection = (actualHitPos - (Vector2)transform.position).normalized;
float sweepDistance = effectiveRadius * VERTICAL_SCALE; // 타원 앞쪽 끝까지

RaycastHit2D[] castHits = Physics2D.CircleCastAll(
    origin: (Vector2)transform.position,
    radius: effectiveRadius,
    direction: digDirection,
    distance: sweepDistance
);

foreach (var castHit in castHits)
{
    if (castHit.collider.TryGetComponent(out IIndestructibleHit indestructible))
    {
        indestructible.OnHitAttempt(actualHitPos, toolIndex);
        return;
    }
}
```

- **원리**: Deep Rock Galactic 방식. 플레이어 위치에서 파기 방향으로 원을 스윕.
  - 스윕 반지름 = effectiveRadius (파기 타원의 측면 폭)
  - 스윕 거리 = effectiveRadius * VerticalScale (파기 타원의 앞쪽 끝까지)
- **장점**: 파기 타원의 bounding volume을 실제로 커버. 방향 무관 정확도 높음.
- **단점**: OverlapCircleAll보다 연산량 약간 증가 (실용적으로는 무시 가능).
- **적합도**: 실제 구현 권장

---

### 방안 C: TerrainModifier 픽셀 인접 버퍼 (보조)

```csharp
// TerrainModifier.ProcessDigPixels() 내부, 픽셀 제거 직전에 추가
// IndestructibleMask 인접 픽셀도 보호
if (IsAdjacentToIndestructible(data, x, y)) continue;

// 헬퍼 함수
private bool IsAdjacentToIndestructible(ChunkData data, int x, int y)
{
    for (int dy = -1; dy <= 1; dy++)
    for (int dx = -1; dx <= 1; dx++)
    {
        if (dx == 0 && dy == 0) continue;
        int nx = x + dx, ny = y + dy;
        if (!data.IsValid(nx, ny)) continue;
        if (data.IndestructibleMask[data.ToIndex(nx, ny)] != 0) return true;
    }
    return false;
}
```

- **원리**: Noita 방식. indestructible 픽셀에 인접한 일반 지형 픽셀도 보호.
- **장점**: 어떤 각도·거리에서 파도 indestructible 주변 지형이 완벽히 보호됨. 방안 B가 놓치는 엣지 케이스까지 커버.
- **단점**: 픽셀당 8방향 IndestructibleMask 조회 추가. 파기 범위가 크면 성능 영향 가능.
  - 완화책: 청크에 IndestructibleMask 픽셀이 있는 경우에만 인접 체크 활성화 (`data.HasIndestructible` 플래그 추가)
- **적합도**: 방안 B의 보조로 함께 사용하면 이중 안전망

---

## 5. 최종 권장 구현 계획

### 1단계: Digger.cs — CircleCast로 검사 교체

**변경 위치**: `Digger.cs` `DigAt()` 메서드 (line 287~296)

```csharp
// [기존]
Collider2D[] indestructibleHits = Physics2D.OverlapCircleAll(actualHitPos, effectiveRadius);
foreach (var hit in indestructibleHits)
{
    if (hit.TryGetComponent(out IIndestructibleHit indestructible))
    {
        indestructible.OnHitAttempt(actualHitPos, toolIndex);
        return;
    }
}

// [변경 후]
// 파기 타원의 전체 범위를 CircleCast로 스캔
const float VERTICAL_SCALE = 1.5f; // TerrainModifier.VerticalScale과 동기화 필요
Vector2 digDirection = (actualHitPos - (Vector2)transform.position).normalized;
RaycastHit2D[] castHits = Physics2D.CircleCastAll(
    (Vector2)transform.position,
    effectiveRadius,
    digDirection,
    effectiveRadius * VERTICAL_SCALE
);
foreach (var castHit in castHits)
{
    if (castHit.collider != null &&
        castHit.collider.TryGetComponent(out IIndestructibleHit indestructible))
    {
        indestructible.OnHitAttempt(actualHitPos, toolIndex);
        return;
    }
}
```

> **주의**: `VERTICAL_SCALE`은 `TerrainModifier.VerticalScale`과 항상 동일한 값이어야 한다.
> 나중에 VerticalScale이 변경될 경우 두 곳 모두 수정 필요.
> 개선: `TerrainModifier`에 `public const float VERTICAL_SCALE = 1.5f;` 로 상수 분리 후 양측에서 참조.

---

### 2단계: TerrainModifier.cs — 픽셀 인접 보호 (선택적 보조)

**변경 위치**: `TerrainModifier.ProcessDigPixels()` (line 296 부근, IndestructibleMask 체크 직후)

```csharp
// [기존]
if (data.IndestructibleMask.IsCreated && data.IndestructibleMask[index] != 0) continue;

// [변경 후] — 1픽셀 인접 버퍼 추가
if (data.IndestructibleMask.IsCreated && data.IndestructibleMask[index] != 0) continue;
if (data.IndestructibleMask.IsCreated && IsAdjacentToIndestructible(data, x, y)) continue;
```

`IsAdjacentToIndestructible()` 헬퍼는 `TerrainModifier` 내 `private` 메서드로 추가.

> **성능 고려**: 청크에 indestructible 픽셀이 없으면 체크 스킵.
> `ChunkData`에 `public bool HasIndestructiblePixels` 플래그 추가 후, 2단계 체크를 이 플래그로 가드.

---

### 3단계: 동기화 리스크 제거 (선택)

`TerrainModifier`의 `VerticalScale`을 상수로 분리해 `Digger`와 공유:

```csharp
// TerrainModifier.cs
public const float VERTICAL_SCALE_DEFAULT = 1.5f;
public float VerticalScale { get; set; } = VERTICAL_SCALE_DEFAULT;
```

```csharp
// Digger.cs
float sweepDistance = effectiveRadius * TerrainModifier.VERTICAL_SCALE_DEFAULT;
```

---

## 6. 변경 파일 요약

| 파일 | 변경 내용 | 우선순위 |
|---|---|---|
| `Digger.cs` | OverlapCircleAll → CircleCastAll (DigAt 내부) | 필수 |
| `TerrainModifier.cs` | 픽셀 인접 보호 로직 추가, VERTICAL_SCALE 상수 분리 | 권장 |
| `ChunkData.cs` | `HasIndestructiblePixels` 플래그 추가 | 선택 (성능 최적화) |

---

## 7. 검증 시나리오

구현 후 아래 케이스를 수동 테스트:

1. indestructible 정면 타격 → 파기 차단 + 피드백 소리 ✓ (기존 동작 유지)
2. indestructible 바로 옆 지형 타격 (파기원이 콜라이더에 살짝 걸침) → 파기 차단
3. indestructible 완전히 옆 지형 타격 (파기원이 콜라이더와 전혀 겹치지 않음) → 파기 허용
4. indestructible 대각선 방향에서 파기 → 타원 앞쪽 돌출부가 콜라이더에 걸리는 경우 차단
5. 여러 indestructible이 가까이 있는 경우 → 가장 먼저 감지된 것에서 피드백 1회만 발생
