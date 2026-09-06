# 드릴 "파면서 앞으로 가는 느낌" 부재 원인 분석
@tags: drill, movement, feel, analysis, velocity, physics, DrillStrategy

> 작성일: 2026-03-12
> 관련 파일: `DrillStrategy.cs`, `TerrainChunk.cs`, `PlayerController.cs`

---

## 1. 증상 요약

드릴 대시(RMB + LMB 홀드) 중에:
- 플레이어가 앞으로 이동하지 않거나 매우 느리게 움직임
- 파기가 한 위치에서만 반복되는 느낌
- "뚫고 들어가는" 감각이 없고, 벽 앞에 붙어서 제자리 진동하는 느낌

---

## 2. 원인 분석 — 파기-이동 파이프라인 전체 흐름

```
[DrillStrategy.HandleUpdate()]          — Update() 루프
  └─ PerformDig()
       └─ chunk.ModifyTerrain()
            └─ _modifier.Dig()
                 └─ data.IsColliderDirty = true      ← 픽셀 제거 완료
                                                          ↑
                                               여기까지는 즉시 반영됨

[TerrainChunk.Update()] or [LateUpdate()]
  └─ ShouldUpdateCollider()
       └─ (Time.time - _lastColliderUpdateTime) >= colliderUpdateInterval(0.2s)
            └─ [0.2초 이상 지났을 때만] _colliderManager.UpdateCollider()
                                         ↑
                              물리 콜라이더 갱신 (200ms 지연)

[PlayerController.HandleNormalMovement()] — FixedUpdate()
  └─ isDashing == true → velocity 줄 건너뜀
       ↑ (DrillStrategy.HandleFixedUpdate()에서 직접 설정)

[DrillStrategy.HandleFixedUpdate()]       — FixedUpdate()
  └─ rb.linearVelocity = direction * _dashSpeed
       └─ 단, 지형 PolygonCollider2D가 아직 갱신 안 됐으면 → 물리 충돌로 이동 막힘
```

---

## 3. 원인 1 (핵심): 콜라이더 갱신 200ms 지연

### 코드 근거

```csharp
// TerrainChunk.cs:85
private float colliderUpdateInterval = 0.2f;

// TerrainChunk.cs:238-244
if (_data.IsColliderDirty && ShouldUpdateCollider())
{
    _colliderManager.UpdateCollider(); // ← 0.2초에 한 번만 실행
    _data.IsColliderDirty = false;
    _lastColliderUpdateTime = Time.time;
}
```

### 발생 과정

| 시간 | 이벤트 |
|------|--------|
| t=0.000 | 대시 시작. `PerformDig()` → 픽셀 제거, `IsColliderDirty = true` |
| t=0.000 | `HandleFixedUpdate()` → `rb.linearVelocity = direction * 15` 설정 |
| t=0.000 | 물리 계산: PolygonCollider2D는 아직 old shape → 플레이어 충돌로 이동 차단 |
| t=0.080 | `PerformDig()` 재호출 → **동일 위치**에서 다시 파기 (픽셀이 이미 없어서 효과 없음) |
| t=0.160 | `PerformDig()` 재호출 → 동일 |
| t=0.200 | 드디어 `UpdateCollider()` 실행 → 물리 벽 사라짐 |
| t=0.200 | 플레이어가 갑자기 `15 * 0.2 = 3 units` 앞으로 튀어나감 |
| t=0.200 | 다음 지형 벽에 막힘. 사이클 반복 |

**결과:** 0.2초 동안 제자리 → 순간 이동 → 0.2초 제자리. 끊기는 움직임처럼 보임.
드릴 `_digInterval = 0.08s`이지만 물리 콜라이더 갱신은 `0.2s` → **파기 2~3회가 물리적으로 무의미**해짐.

---

## 4. 원인 2 (구조): 파기 위치(lookAhead)가 너무 짧음

### 코드 근거

```csharp
// DrillStrategy.PerformDig()
Vector2 digCenter = playerPos + direction * _digRadius; // _digRadius = 0.6f
```

### 문제

`_digRadius = 0.6f` = PPU 10 기준 **6 pixels**만 앞.
플레이어 `bodyCollider`의 실제 반경을 `r_player`라 하면:

```
파기 원 중심까지 거리: 0.6 units
파기 원 도달 범위:    0.6 + 0.6 = 1.2 units (중심에서 끝까지)

플레이어 몸 반경이 ~0.8 units이라면:
  → 파기 원이 플레이어 몸 안/바로 표면에서 시작
  → 앞쪽 지형이 충분히 제거되지 않음
  → 플레이어가 통과할 터널 너비 = 직경 1.2 units
  → 플레이어 몸 직경 > 1.2 units이면 물리 통과 불가
```

**결과:** 드릴이 플레이어 발 아래나 몸통 표면에서 파서, 이동 경로가 확보되지 않음.

---

## 5. 원인 3 (물리): 대시 중 중력 미처리

### 코드 근거

```csharp
// PlayerController.HandleNormalMovement() — FixedUpdate마다 실행
void HandleNormalMovement()
{
    rb.gravityScale = defaultGravity; // ← isDashing 여부 무관하게 항상 중력 적용
    if (!isDashing)
        rb.linearVelocity = new Vector2(...);
}

// DrillStrategy.HandleFixedUpdate()
rb.linearVelocity = direction * _dashSpeed;
// ↑ 이 프레임은 올바른 방향으로 설정되지만...
// 다음 FixedUpdate 전까지 물리 엔진이 gravityScale만큼 y 속도를 낮춤
```

### 발생 과정 (위쪽 방향 드릴 예시)

```
FixedUpdate N:
  HandleNormalMovement: rb.gravityScale = 9.81 (중력 켜짐)
  DrillFixedUpdate:     rb.linearVelocity = (0, 15) [위쪽]

FixedUpdate N+1:
  물리 적분:            velocity.y -= gravity * deltaTime ≒ 15 - 9.81*0.02 ≒ 14.8
  HandleNormalMovement: rb.gravityScale = 9.81 (여전히)
  DrillFixedUpdate:     rb.linearVelocity = (0, 15) [재설정, OK]
```

위 방향은 매 프레임 재설정으로 어느 정도 유지되지만:
- **대각선 아래 방향** → 중력이 더해져 의도보다 빠르게 낙하
- **대각선 위 방향** → 중력과 싸워 실제 속도 감소
- **수평 방향** → 영향 없음

**결과:** 방향에 따라 체감 속도가 다르고, 특히 위쪽 지형을 뚫는 드릴이 매우 무겁게 느껴짐.

---

## 6. 원인 4 (구조): HandleNormalMovement와 DrillFixedUpdate의 실행 순서 보장 없음

### 코드 근거

```csharp
// PlayerController.FixedUpdate() → HandleNormalMovement()
// PlayerMining.FixedUpdate()    → DrillStrategy.HandleFixedUpdate()
```

Unity에서 복수의 MonoBehaviour가 동일한 FixedUpdate를 가질 때 실행 순서는 **Script Execution Order** 설정에 의존한다. 기본값이면 사실상 **불규칙**.

만약 `PlayerController.FixedUpdate()`가 `PlayerMining.FixedUpdate()` 후에 실행되면:
```
DrillFixedUpdate:       rb.linearVelocity = direction * 15  [드릴 속도 설정]
HandleNormalMovement:   rb.gravityScale = defaultGravity    [이건 항상 실행됨]
  → isDashing == true이므로 velocity는 건드리지 않음 → OK
```

만약 반대 순서면:
```
HandleNormalMovement:   isDashing → velocity 건드리지 않음 → OK
DrillFixedUpdate:       rb.linearVelocity = direction * 15
```

이 경우는 큰 문제 없다. 하지만 `HandleNormalMovement()`에서 `rb.gravityScale`은 **항상** 설정되므로, 실행 순서 무관하게 중력 문제(원인 3)는 남는다.

---

## 7. 원인별 심각도 정리

| 번호 | 원인 | 체감 영향 | 심각도 |
|------|------|-----------|--------|
| 1 | 콜라이더 갱신 200ms 지연 | 제자리 → 순간이동 반복 | ★★★★★ |
| 2 | lookAhead 거리 부족 | 이동 경로 미확보 | ★★★★☆ |
| 3 | 대시 중 중력 미처리 | 방향별 체감 다름 | ★★★☆☆ |
| 4 | FixedUpdate 실행 순서 | 프레임간 속도 불일치 | ★★☆☆☆ |

---

## 8. 해결 방안

### 해결 1: 콜라이더 즉시 갱신 (원인 1 해결 — 가장 중요)

**방법 A: `PerformDig()`에서 hit한 청크의 콜라이더를 즉시 갱신**

`TerrainChunk`에 `ForceUpdateCollider()` 메서드를 추가하고, `PerformDig()` 후 호출한다.

```csharp
// TerrainChunk.cs에 추가
public void ForceUpdateCollider()
{
    if (_colliderManager == null || _data == null) return;
    _colliderManager.UpdateCollider();
    _data.IsColliderDirty = false;
    _lastColliderUpdateTime = Time.time;
}
```

```csharp
// DrillStrategy.PerformDig() 내부
if (hit.TryGetComponent(out TerrainChunk chunk))
{
    chunk.ModifyTerrain(digCenter, _digRadius, 3);
    chunk.ForceUpdateCollider(); // ← 콜라이더 즉시 반영
}
```

이후 `Physics2D.SyncTransforms()`를 호출해 물리 엔진에 즉시 반영:

```csharp
Physics2D.SyncTransforms(); // 물리 엔진과 Transform 강제 동기화
```

> **주의:** `UpdateCollider()`는 1000×1000 픽셀 전체 스캔 + Moore-Neighbor 추적을 수행한다. 드릴 0.08s마다 호출하면 **프레임 드롭**이 생긴다. 아래 "부분 갱신" 방안을 병행해야 한다.

**방법 B (권장): 드릴 대시 중 colliderUpdateInterval을 0.05s로 낮춤**

`TerrainChunk`에서 `colliderUpdateInterval`을 외부에서 설정할 수 있게 한다.

```csharp
// TerrainChunk.cs에 추가
public float ColliderUpdateInterval
{
    get => colliderUpdateInterval;
    set => colliderUpdateInterval = Mathf.Max(0f, value);
}
```

```csharp
// DrillStrategy.cs — StartDrillDash()
private void StartDrillDash()
{
    _isDrillDashing = true;
    _digTimer = _digInterval;

    // 근처 청크의 콜라이더 갱신 주기를 드릴용으로 단축
    SetNearbyChunksColliderInterval(0.05f);
    ...
}

private void StopDrillDash()
{
    _isDrillDashing = false;

    // 콜라이더 갱신 주기 복원
    SetNearbyChunksColliderInterval(0.2f);
    ...
}

private void SetNearbyChunksColliderInterval(float interval)
{
    var mgr = InfinityMapManager.Instance;
    if (mgr == null) return;
    Vector2Int playerChunk = mgr.WorldToChunkCoord(_context.transform.position);
    for (int dx = -1; dx <= 1; dx++)
    for (int dy = -1; dy <= 1; dy++)
    {
        var chunk = mgr.GetChunk(new Vector2Int(playerChunk.x + dx, playerChunk.y + dy));
        if (chunk != null) chunk.ColliderUpdateInterval = interval;
    }
}
```

---

### 해결 2: lookAhead 거리 확장 (원인 2 해결)

플레이어 `bodyCollider`의 실제 반경을 기준으로 파기 위치를 계산한다.

```csharp
// DrillStrategy.PerformDig() — 파기 위치 계산 개선
private float GetPlayerRadius()
{
    var col = _context.controller.bodyCollider;
    if (col is CircleCollider2D circle) return circle.radius;
    if (col is CapsuleCollider2D capsule) return capsule.size.x * 0.5f;
    return 0.5f; // 폴백
}

private void PerformDig()
{
    ...
    float playerRadius = GetPlayerRadius();
    // 파기 중심 = 플레이어 앞 (플레이어 반경 + 드릴 반경 + 여유 0.2)
    float lookAhead = playerRadius + _digRadius + 0.2f;
    Vector2 digCenter = playerPos + direction * lookAhead;
    ...
}
```

**수치 예시 (playerRadius = 0.5f, _digRadius = 0.6f):**

| 항목 | 현재 | 개선 후 |
|------|------|---------|
| lookAhead | 0.6 | 0.5 + 0.6 + 0.2 = 1.3 |
| 파기 원 끝까지 | 1.2 | 1.9 |
| 플레이어 통과 여유 | 없음 | 0.4 units |

또한 `_digRadius`를 플레이어 반경보다 크게 설정해야 터널이 충분히 넓다:

```csharp
// Enter() 시 플레이어 크기에 맞게 동적 설정
public void Enter(PlayerMining context)
{
    _context = context;
    _digRadius = Mathf.Max(GetPlayerRadius() + 0.1f, 0.6f); // 플레이어보다 약간 넓게
    ...
}
```

---

### 해결 3: 대시 중 중력 비활성화 (원인 3 해결)

`DrillStrategy`에서 직접 `rb.gravityScale`을 제어한다.

```csharp
private float _savedGravityScale;
private Rigidbody2D _rb; // Enter()에서 캐싱

public void Enter(PlayerMining context)
{
    _context = context;
    _rb = context.controller.GetComponent<Rigidbody2D>();
    ...
}

private void StartDrillDash()
{
    _isDrillDashing = true;
    _savedGravityScale = _rb.gravityScale;
    _rb.gravityScale = 0f; // 중력 차단 → 어느 방향으로든 균일한 속도
    ...
}

private void StopDrillDash()
{
    _isDrillDashing = false;
    if (_rb != null) _rb.gravityScale = _savedGravityScale; // 중력 복원
    ...
}
```

---

### 해결 4: `HandleFixedUpdate()` Rigidbody 캐싱 (원인 4 + 성능 개선)

현재 매 `FixedUpdate()`마다 `GetComponent<Rigidbody2D>()` 호출 중. Enter() 시 캐싱.

```csharp
// 현재 (매 FixedUpdate마다 GetComponent 호출)
Rigidbody2D rb = _context.controller.GetComponent<Rigidbody2D>();

// 개선 (Enter()에서 한 번만 캐싱)
private Rigidbody2D _rb; // 필드로 선언

public void Enter(PlayerMining context)
{
    _context = context;
    _rb = context.controller.GetComponent<Rigidbody2D>(); // 캐싱
    ...
}

public void HandleFixedUpdate()
{
    if (_rb == null || !_isDrillDashing) return;
    Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
    Vector2 direction = (mousePos - _context.transform.position).normalized;
    _rb.linearVelocity = direction * _dashSpeed;
}
```

---

## 9. 개선 후 예상 동작 흐름

```
[대시 시작]
  ├─ rb.gravityScale = 0              (중력 제거)
  ├─ 근처 청크 colliderInterval = 0.05s (빠른 콜라이더 갱신)
  └─ _digTimer = _digInterval         (즉시 첫 파기)

[매 0.08초 PerformDig()]
  ├─ lookAhead = playerRadius + digRadius + 0.2  (충분히 앞을 파기)
  ├─ chunk.ModifyTerrain() → 픽셀 제거
  └─ (0.05s마다 콜라이더 갱신) → 물리 벽 제거

[매 FixedUpdate]
  └─ rb.linearVelocity = direction * dashSpeed
       └─ 콜라이더가 빠르게 갱신되므로 → 플레이어가 실제로 앞으로 이동

[대시 종료]
  ├─ rb.gravityScale 복원
  └─ 근처 청크 colliderInterval = 0.2s (원복)
```

---

## 10. 변경 파일 및 우선순위

| 우선순위 | 파일 | 변경 내용 |
|---------|------|---------|
| 1 (필수) | `TerrainChunk.cs` | `ColliderUpdateInterval` 프로퍼티 추가, `ForceUpdateCollider()` 추가 |
| 1 (필수) | `DrillStrategy.cs` | `_rb` 캐싱, `gravityScale = 0` 대시 중, `lookAhead` 거리 개선, 콜라이더 주기 단축 호출 |
| 2 (권장) | `DrillStrategy.cs` | `GetPlayerRadius()` → 동적 `_digRadius` 계산 |
| 3 (선택) | `InfinityMapManager.cs` | `WorldToChunkCoord()` public 확인 또는 추가 |

---

## 11. 추가 고려사항

### 성능: 잦은 UpdateCollider()의 비용
`UpdateCollider()`는 1000×1000 픽셀 전체 스캔이다. 0.05초마다 호출하면 초당 20회 → **프레임 드롭 가능성 있음**.

완전한 해결책은 **부분 청크 콜라이더 갱신** (파기된 영역 주변만 재계산)이지만, 구현 복잡도가 높다.

현실적 타협안:
- 대시 중 `colliderInterval = 0.08s` (드릴 `_digInterval`과 동일)
- 대시 중이 아닐 때 `0.2s` 유지
- 이렇게 하면 파기 직후 콜라이더가 갱신되어 다음 이동에 반영됨

### isDashing 플래그와 점프 충돌
드릴 대시 중 `isDashing = true`이므로 `HandleNormalMovement()`에서 일반 이동이 차단된다. 중력도 `gravityScale = 0`으로 끄면 대시 중 점프가 완전히 무력화된다. 이는 의도된 동작(드릴 중엔 자유 이동)이면 문제없다.
