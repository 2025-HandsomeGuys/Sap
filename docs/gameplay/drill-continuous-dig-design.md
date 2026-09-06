# 드릴 연속 파기 구현 설계서
@tags: drill, continuous-dig, ImmediateDig, DrillStrategy, design, ProcessDirtyChunksAsync

> 작성일: 2026-03-12
> 대상 파일: `Assets/Scripts/UI/Player/Strategies/DrillStrategy.cs`

---

## 1. 문제 정의

드릴(RMB + LMB 홀드)로 대시 중일 때 지형이 **단 한 번만** 파진다.

---

## 2. 근본 원인 분석

### 2-1. 채굴 흐름 전체 구조

```
입력
 └─ MouseDigInputHandler.IsDigRequested()
       └─ Input.GetMouseButtonDown(0)   ← 한 프레임만 true
             └─ Digger.Update()
                   └─ TryDig() → DigAt() → mapManager.ModifyTerrain()
```

`GetMouseButtonDown(0)`은 **버튼을 처음 누르는 단 한 프레임**에만 `true`를 반환한다.
드릴이 대시하는 동안 매 프레임 파기를 원하지만, `Digger.cs`는 이 일회성 이벤트에만 반응하므로 최초 클릭 한 번만 동작한다.

### 2-2. DrillStrategy의 현재 동작

```csharp
// HandleUpdate() — 현재
if (isAiming && Input.GetMouseButton(0) && _currentBattery > 0)
{
    if (!_isDrillDashing) StartDrillDash();   // 대시는 시작됨
    // ← 파기 로직 없음!
}

// HandleFixedUpdate() — 현재
if (_isDrillDashing)
{
    rb.linearVelocity = direction * _dashSpeed; // 이동만 처리
    // ← 파기 로직 없음!
}
```

`StartDrillDash()`는 물리 이동(`rb.linearVelocity`)만 처리하고, 지형 파기를 전혀 호출하지 않는다.

### 2-3. 다른 전략과의 비교

| 전략 | 파기 방식 | 파기 호출 위치 |
|------|----------|--------------|
| SapStrategy | 차징 후 클릭 1회 | `FireMining()` → `OverlapCircleAll` + `ModifyTerrain` |
| PickaxeStrategy | 콤보 클릭마다 1회 | `ExecuteAttack()` → `PerformDig()` → `OverlapCircleAll` + `ModifyTerrain` |
| **DrillStrategy** | 없음 (버그) | 없음 |

SapStrategy와 PickaxeStrategy는 전략 내부에서 직접 `OverlapCircleAll` → `TerrainChunk.ModifyTerrain()`을 호출한다. DrillStrategy도 동일한 방식을 따라야 한다.

---

## 3. 해결 방향 검토

### 방안 A: MouseDigInputHandler를 `GetMouseButton`으로 변경

```csharp
// MouseDigInputHandler.cs
return Input.GetMouseButton(0); // GetMouseButtonDown → GetMouseButton
```

**장점:** 변경이 단순
**단점 (채택 불가):**
- 삽(SapStrategy)도 매 프레임 `Digger.TryDig()`가 실행됨 → `digCooldown`이 막지만, SapStrategy가 아닌 Digger가 파기 담당자가 됨 (책임 혼재)
- PickaxeStrategy는 `GetDigParameters()`에서 `CanDigTerrain = false`를 반환하므로 실제 지형 파기는 막히지만, `_playerAnimator.SetTrigger("Mine")`이 `TryDig()` 안에서 매 0.1초마다 발동 → 애니메이션 깨짐
- 드릴이 없는 상태에서 LMB 홀드 시 다른 도구도 연속 파기가 돼버림 → 게임플레이 밸런스 붕괴

### 방안 B: DrillStrategy 내부에 연속 파기 루프 추가 ✅ 권장

SapStrategy / PickaxeStrategy와 동일한 방식으로, **DrillStrategy 자신이 파기를 직접 처리**한다.

**장점:**
- Strategy Pattern 원칙에 완전히 부합 (단일 책임)
- Digger.cs, MouseDigInputHandler.cs 수정 불필요
- 드릴 고유의 타이밍/범위/비용 로직을 독립적으로 제어 가능

**단점:** 없음

---

## 4. 권장 구현 상세

### 4-1. 핵심 아이디어

대시 중(`_isDrillDashing == true`)일 때 매 `_digInterval`초마다 플레이어 전방 지형을 파낸다.

```
HandleUpdate() 매 프레임
 └─ _isDrillDashing ?
       └─ _digTimer += deltaTime
             └─ _digTimer >= _digInterval ?
                   └─ PerformDig()   ← 지형/암석 파기
                   └─ _digTimer = 0f
```

### 4-2. 추가할 필드

```csharp
// Configuration (생성자 파라미터로 받거나 인스펙터에서 조정 가능하도록)
private float _digInterval = 0.08f;   // 파기 주기 (초) — 값이 작을수록 빠른 드릴
private float _digRadius   = 0.6f;    // 드릴 파기 반경 (삽보다 좁은 원통형 이미지)

// State
private float _digTimer = 0f;
```

**`_digInterval` 값 선택 근거:**

| 값 | 느낌 | 초당 파기 횟수 |
|----|------|--------------|
| 0.05s | 초고속 드릴, 과함 | 20회 |
| **0.08s** | **자연스러운 드릴감, 권장** | **12.5회** |
| 0.12s | 약간 느린 드릴 | 8.3회 |
| 0.2s | 곡괭이 수준, 드릴답지 않음 | 5회 |

`Digger.cs`의 기본 `digCooldown`이 `0.1f`인 것을 참고하면, 드릴은 그것보다 약간 빠른 `0.08f`가 적절하다.

### 4-3. StartDrillDash() 수정

대시 시작 즉시 첫 파기가 발동되도록 타이머를 `_digInterval`로 초기화한다.
(0으로 초기화하면 첫 파기까지 `_digInterval`만큼 대기해야 해서 반응이 늦게 느껴짐)

```csharp
private void StartDrillDash()
{
    _isDrillDashing = true;
    _digTimer = _digInterval; // ← 추가: 즉시 첫 파기 발동
    _context.playerAnimator.SetBool("DrillDash", true);
    _context.controller.isDashing = true;
    _context.controller.isMiningAction = true;
}
```

### 4-4. HandleUpdate() 수정

```csharp
if (_isDrillDashing)
{
    // 배터리 소모 (기존)
    _currentBattery -= Time.deltaTime;
    if (_currentBattery <= 0)
    {
        _currentBattery = 0;
        StopDrillDash();
        return;
    }

    // 연속 파기 (추가)
    _digTimer += Time.deltaTime;
    if (_digTimer >= _digInterval)
    {
        PerformDig();
        _digTimer = 0f;
    }
}
```

### 4-5. PerformDig() 구현

SapStrategy/PickaxeStrategy의 패턴을 그대로 따른다.

```csharp
private void PerformDig()
{
    if (_context == null) return;

    Vector2 mousePos  = Camera.main.ScreenToWorldPoint(Input.mousePosition);
    Vector2 playerPos = _context.transform.position;
    Vector2 direction = (mousePos - playerPos).normalized;

    // 드릴 팁 위치 = 플레이어 앞쪽
    Vector2 digCenter = playerPos + direction * _digRadius;

    DigParameters p = GetDigParameters(1.0f, TileType.Dirt);
    if (!p.CanDig) return;

    float effectiveRadius = _digRadius * p.RadiusMultiplier;

    Collider2D[] hits = Physics2D.OverlapCircleAll(digCenter, effectiveRadius);

    bool hitSomething = false;
    foreach (var hit in hits)
    {
        // 1. IDirtDiggable (흙 패치) 우선
        if (hit.TryGetComponent(out IDirtDiggable dirtPatch))
        {
            dirtPatch.DigDirt();
            hitSomething = true;
            break;
        }
    }

    if (!hitSomething)
    {
        foreach (var hit in hits)
        {
            // 2. IDiggable (DiggableRock)
            if (p.CanDigRock && hit.TryGetComponent(out IDiggable diggable))
            {
                _context.playerStats?.UseStamina(0.3f); // 암석 파기 스태미나
                diggable.Dig(digCenter, effectiveRadius, p.ToolIndex);
                hitSomething = true;
                break;
            }

            // 3. TerrainChunk (지형)
            if (p.CanDigTerrain && hit.TryGetComponent(out TerrainChunk chunk))
            {
                chunk.ModifyTerrain(digCenter, effectiveRadius, p.ToolIndex);
                hitSomething = true;
            }
        }
    }
}
```

### 4-6. GetDigParameters() — Digger.cs 간섭 차단

`Digger.cs`는 `GetMouseButtonDown(0)` 발생 시 `GetCurrentDigParameters()`를 호출해 파기 여부를 결정한다.
드릴 대시 중에는 DrillStrategy가 파기를 전담하므로, **Digger.cs가 중복으로 파지 않도록** 대시 중에는 `CanDig = false`를 반환한다.

```csharp
public DigParameters GetDigParameters(float baseRadius, TileType targetTileType)
{
    if (_currentBattery <= 0)
        return new DigParameters { CanDig = false };

    // 대시 중에는 DrillStrategy가 직접 파기 담당
    // Digger.cs (MouseDigInputHandler)가 중복 파기하는 것을 방지
    if (_isDrillDashing)
        return new DigParameters { CanDig = false };

    // 대시 중이 아닐 때(RMB 없이 LMB만 클릭) → Digger.cs에 위임
    return new DigParameters
    {
        CanDig           = true,
        RadiusMultiplier = 1.0f,
        ToolIndex        = 3,
        CanDigTerrain    = true,
        CanDigRock       = true
    };
}
```

> **왜 이 처리가 필요한가?**
> 대시 시작 프레임에 `GetMouseButtonDown(0)`이 `true`이므로, `Digger.cs`도 동시에 `TryDig()`를 호출한다.
> `_isDrillDashing` 플래그로 차단하지 않으면 같은 프레임에 이중 파기가 발생한다.

---

## 5. StopDrillDash() 수정

대시 종료 시 타이머 초기화.

```csharp
private void StopDrillDash()
{
    _isDrillDashing = false;
    _digTimer = 0f; // ← 추가
    if (_context != null)
    {
        _context.playerAnimator.SetBool("DrillDash", false);
        _context.controller.isDashing = false;
        _context.controller.isMiningAction = false;
    }
}
```

---

## 6. 최종 변경 요약

| 항목 | 변경 내용 |
|------|---------|
| **추가 필드** | `_digInterval = 0.08f`, `_digRadius = 0.6f`, `_digTimer = 0f` |
| **StartDrillDash()** | `_digTimer = _digInterval` 추가 (즉시 첫 파기) |
| **HandleUpdate()** | 대시 중 `_digTimer` 누적 → `PerformDig()` 호출 |
| **PerformDig()** | 신규 추가 — OverlapCircleAll 기반 파기 로직 |
| **StopDrillDash()** | `_digTimer = 0f` 초기화 추가 |
| **GetDigParameters()** | `_isDrillDashing` 중 `CanDig = false` 반환 추가 |

**수정 불필요:**
- `Digger.cs` — 변경 없음
- `MouseDigInputHandler.cs` — 변경 없음
- `PlayerMining.cs` — 변경 없음
- `IMiningStrategy.cs` — 변경 없음

---

## 7. 엣지 케이스 및 고려사항

### 7-1. 스태미나가 0일 때

`PerformDig()` 안에서 `playerStats.UseStamina()`를 호출하기 전에 잔량 체크를 해야 한다.
스태미나가 0이면 파기를 건너뛰되 대시는 계속 유지 (배터리와 스태미나는 독립 자원).
또는 스태미나 0 시 대시도 멈추는 규칙을 원한다면 `StopDrillDash()`를 호출.

```csharp
// PerformDig() 상단
float staminaCost = 0.2f;
if (_context.playerStats != null && !_context.playerStats.CanUseStamina(staminaCost))
    return; // 스태미나 부족 → 이번 틱은 파기 생략
_context.playerStats.UseStamina(staminaCost);
```

### 7-2. `_digRadius`와 `Digger.digRadius`의 불일치

현재 `Digger.cs`는 `PlayerStat.MiningRange`를 기반으로 `digRadius`를 설정한다.
`DrillStrategy._digRadius`는 고정값(0.6f)이라 업그레이드 시스템과 연동되지 않는다.

**단기 해결:** 고정값 유지 (간단)
**장기 해결:** `Enter(PlayerMining context)` 시 `Digger` 참조를 얻어 `_digRadius = digger.digRadius`로 동기화

```csharp
public void Enter(PlayerMining context)
{
    _context = context;
    // 업그레이드 반영: Digger의 digRadius 동기화
    var digger = context.GetComponent<Digger>();
    if (digger != null) _digRadius = digger.digRadius;
    else _digRadius = 0.6f; // 폴백
    ...
}
```

### 7-3. 드릴 팁 위치 정밀도

현재 설계는 `playerPos + direction * _digRadius`로 계산하는데, 이는 **플레이어 중심**으로부터의 거리다.
실제 드릴 스프라이트 끝부분과 일치하지 않을 수 있다.
시각적 정밀도가 중요하다면 드릴 끝 `Transform`을 별도로 두고 그 위치를 사용하는 방법도 있다.

### 7-4. 대각선 파기와 터널 모양

`OverlapCircleAll`은 원형 영역을 파내므로 대각선 방향으로 드릴할 때 터널이 계단형이 될 수 있다.
이는 다른 도구들도 동일하게 겪는 문제이며, 현 시점에서 별도 대응 불필요.

### 7-5. `_digInterval` 인스펙터 노출 여부

현재 `DrillStrategy`는 순수 C# 클래스여서 인스펙터에서 값을 바꿀 수 없다.
밸런싱 과정에서 자주 조정이 필요하다면, `PlayerMining.cs`에 `[SerializeField] float drillDigInterval = 0.08f;`를 추가하고 생성자 파라미터로 전달하는 방식이 편하다.

```csharp
// PlayerMining.cs
[Header("드릴 설정")]
public float maxBattery = 5.0f;
public float dashSpeed = 15.0f;
public float drillDigInterval = 0.08f; // ← 추가
public float drillDigRadius   = 0.6f;  // ← 추가

// Start()
_drillStrategy = new DrillStrategy(maxBattery, dashSpeed, drillDigInterval, drillDigRadius);
```

---

## 8. 전체 수정 후 DrillStrategy 동작 흐름

```
[플레이어 RMB 홀드]
  └─ isAiming = true

[플레이어 LMB 홀드 + 배터리 > 0]
  └─ StartDrillDash()
       ├─ _isDrillDashing = true
       ├─ _digTimer = _digInterval   ← 즉시 파기 준비
       └─ 물리: rb.linearVelocity = direction * dashSpeed

[매 프레임 HandleUpdate()]
  ├─ _currentBattery -= deltaTime
  ├─ _digTimer += deltaTime
  └─ _digTimer >= _digInterval ?
       └─ PerformDig()
            ├─ OverlapCircleAll(digCenter, effectiveRadius)
            ├─ DirtPatch 감지 → DigDirt()
            ├─ DiggableRock 감지 → Dig()
            └─ TerrainChunk 감지 → ModifyTerrain()
       └─ _digTimer = 0f

[LMB 뗌 OR 배터리 = 0 OR RMB 뗌]
  └─ StopDrillDash()
       ├─ _isDrillDashing = false
       └─ _digTimer = 0f
```

---

## 9. 구현 우선순위

1. **필수 (버그 수정):** `HandleUpdate()`에 타이머 로직 + `PerformDig()` 추가
2. **권장 (아키텍처 정합성):** `GetDigParameters()`에서 대시 중 `CanDig = false` 반환
3. **선택 (밸런싱):** `PlayerMining.cs`에 `drillDigInterval`, `drillDigRadius` 인스펙터 필드 추가
4. **선택 (업그레이드 연동):** `Enter()` 시 `Digger.digRadius` 동기화
