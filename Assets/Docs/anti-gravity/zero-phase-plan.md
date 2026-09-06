# 무중력(Zero) 위상 재설계 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 무중력(Zero) 위상에서 점프 시 무한 상승하는 버그를 없애고, "위로 살짝 떠오르는 부유 + 미세 방향 보정 + 무중력→역중력 전환 시 부드러운 뒤집힘"을 구현한다.

**Architecture:** 수정은 사실상 `AntiGravityHandler.cs` 한 파일. `gravityScale = 0`은 유지하되 Zero 위상 동안 부력·drag·속도 클램프·입력 미세보정을 `FixedUpdate`에서 적용하고, flip을 `localScale.y` 즉시 반전에서 `rotation.z` 부드러운 Lerp로 교체한다. `PlayerController`·`AntiGravityZone`·`GravityPhase`는 수정하지 않는다.

**Tech Stack:** Unity 2D, C#, Rigidbody2D (`linearVelocity`/`linearDamping`/`gravityScale`).

## Global Constraints

- **버전 관리:** UVCS(Plastic). `git commit` 등 git 명령 사용 금지. 각 태스크 끝의 "체크인"은 사람이 UVCS로 수행 (에이전트는 커밋하지 않음).
- **테스트 실행:** Unity Test Runner / 플레이 검증은 **사람이 직접** 수행. 에이전트는 코드·체크리스트만 작성, `mcp__mcp-unity__run_tests` 등 호출 금지.
- **더티 플래그·중력 원복 패턴 유지:** `AntiGravityHandler`는 `[DefaultExecutionOrder(100)]`로 PlayerController(order 0)의 `gravityScale` 원복 뒤에 재적용된다. 이 순서 전제를 깨지 않는다.
- **입력 축:** 세로 입력은 `Input.GetAxisRaw("Vertical")` (PlayerController와 동일).
- 설계 원본: `Assets/Docs/anti-gravity/zero-phase-design.md`

---

## File Structure

- `Assets/Scripts/UI/Player/AntiGravityHandler.cs` — 수정 (전 태스크). Zero 물리 + flip 회전.
- `Assets/Scripts/UI/Player/PlayerController.cs` — 수정 없음 (읽기 확인만).
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/AntiGravityZone.cs` — 수정 없음.
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/GravityPhase.cs` — 수정 없음.

---

### Task 1: Zero 위상 부유 물리 (부력 + drag + 속도 클램프 + 미세 보정)

**Files:**
- Modify: `Assets/Scripts/UI/Player/AntiGravityHandler.cs`

**Interfaces:**
- Consumes: `Rigidbody2D.linearVelocity`, `Rigidbody2D.linearDamping`, `GravityPhase.Zero`, `Input.GetAxisRaw("Vertical")`.
- Produces: Zero 위상 진입 시 `_rb.linearDamping = floatDrag`, 이탈 시 원복. `FixedUpdate`에서 Zero일 때 부력·보정·클램프 적용. 이후 Task 2가 같은 클래스의 flip 부분을 교체.

- [ ] **Step 1: SerializeField 4종 + drag 원본 필드 추가**

[AntiGravityHandler.cs:18-19](../../Scripts/UI/Player/AntiGravityHandler.cs#L18) 의 기존 필드 바로 아래에 추가:

```csharp
    [SerializeField] private float antiGravityMultiplier = 0.7f;
    [SerializeField] private float transitionDuration = 0.25f;

    [Header("Zero(무중력) 위상 부유")]
    [Tooltip("무중력 중 위로 떠오르는 상승력")]
    [SerializeField] private float buoyancyForce = 2f;
    [Tooltip("무중력 진입 시 적용할 linearDamping (관성·점프 감쇠)")]
    [SerializeField] private float floatDrag = 1.5f;
    [Tooltip("무중력 중 세로 속도 상한 (화면 밖 이탈 방지)")]
    [SerializeField] private float floatMaxSpeed = 4f;
    [Tooltip("무중력 중 세로 입력에 실리는 미세 보정력")]
    [SerializeField] private float floatControlForce = 3f;
```

그리고 `_defaultGravity` 필드 근처([AntiGravityHandler.cs:29](../../Scripts/UI/Player/AntiGravityHandler.cs#L29))에 drag 원본 보관 필드 추가:

```csharp
    private float _defaultGravity;
    private float _defaultDrag;
```

- [ ] **Step 2: Awake에서 drag 원본 저장**

[AntiGravityHandler.cs:47](../../Scripts/UI/Player/AntiGravityHandler.cs#L47) `_defaultGravity = _rb.gravityScale;` 바로 아래에 추가:

```csharp
        _defaultGravity = _rb.gravityScale;
        _defaultDrag = _rb.linearDamping;
```

- [ ] **Step 3: 위상별 drag 세팅 — Zero는 floatDrag, 나머지는 원복**

`SetPhase()` switch의 각 case에서 drag를 조정한다.

Normal case ([AntiGravityHandler.cs:64-70](../../Scripts/UI/Player/AntiGravityHandler.cs#L64)) 에 `_rb.linearDamping = _defaultDrag;` 추가:

```csharp
            case GravityPhase.Normal:
                _overrideGravity = false;
                _rb.gravityScale = _defaultGravity;
                _rb.linearDamping = _defaultDrag;
                SetFlip(false);
                IsActive = false;
                ResetImpactTracking();
                break;
```

Zero case ([AntiGravityHandler.cs:71-78](../../Scripts/UI/Player/AntiGravityHandler.cs#L71)) 에 `_rb.linearDamping = floatDrag;` 추가:

```csharp
            case GravityPhase.Zero:
                _targetGravityScale = 0f;
                _overrideGravity = true;
                _rb.gravityScale = 0f;
                _rb.linearDamping = floatDrag;
                SetFlip(false);
                IsActive = false;
                ResetImpactTracking();
                break;
```

Inverted case ([AntiGravityHandler.cs:79-83](../../Scripts/UI/Player/AntiGravityHandler.cs#L79)) 에 `_rb.linearDamping = _defaultDrag;` 추가 (역중력은 감쇠 불필요):

```csharp
            case GravityPhase.Inverted:
                _rb.linearDamping = _defaultDrag;
                _targetGravityScale = _defaultGravity * -antiGravityMultiplier;
                _overrideGravity = true;
                _transition = StartCoroutine(TransitionIn());
                break;
```

- [ ] **Step 4: ExitZone에서 drag 원복**

[AntiGravityHandler.cs:93-94](../../Scripts/UI/Player/AntiGravityHandler.cs#L93) `_rb.gravityScale = _defaultGravity;` 아래에 추가:

```csharp
        if (_rb == null) return;
        _rb.gravityScale = _defaultGravity;
        _rb.linearDamping = _defaultDrag;
```

- [ ] **Step 5: FixedUpdate에 Zero 부유 로직 추가**

기존 `FixedUpdate()` ([AntiGravityHandler.cs:141-153](../../Scripts/UI/Player/AntiGravityHandler.cs#L141)) 의 `_overrideGravity` 재적용 블록 바로 아래, `if (!IsActive) return;` **위에** Zero 처리 블록을 삽입한다:

```csharp
    private void FixedUpdate()
    {
        // PlayerController가 같은 FixedUpdate에서 gravityScale을 원복하므로(실행 순서상 먼저),
        // 물리 적분 직전인 여기서 목표 중력으로 다시 덮어쓴다.
        if (_overrideGravity && _rb != null)
            _rb.gravityScale = _targetGravityScale;

        // Zero(무중력): 위로 살짝 떠오름(부력) + 세로 입력 미세 보정 + 세로 속도 클램프.
        // drag(linearDamping)는 SetPhase(Zero)에서 floatDrag로 올려 두었으므로
        // 점프 impulse·진입 관성이 여기 클램프와 함께 스르륵 감쇠한다.
        if (_currentPhase == GravityPhase.Zero && _rb != null)
        {
            _rb.AddForce(Vector2.up * buoyancyForce, ForceMode2D.Force);

            float v = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(v) > 0.01f)
                _rb.AddForce(Vector2.up * v * floatControlForce, ForceMode2D.Force);

            Vector2 vel = _rb.linearVelocity;
            vel.y = Mathf.Clamp(vel.y, -floatMaxSpeed, floatMaxSpeed);
            _rb.linearVelocity = vel;
        }

        if (!IsActive) return;
        if (_rb.linearVelocity.y > 0f)
            _peakUpwardSpeed = Mathf.Max(_peakUpwardSpeed, _rb.linearVelocity.y);
        if (_playerController != null && !_playerController.IsGrounded)
            _antiGravityAirTime += Time.fixedDeltaTime;
    }
```

- [ ] **Step 6: Unity 수동 검증 (사람이 수행)**

무중력 존이 있는 씬에서 플레이하고 확인:
- Normal → Zero 전환 시 플레이어가 **위로 천천히 떠오른다** (무한 가속 아님).
- Zero 중 점프하면 톡 붕 떴다가 **스르륵 멈춘다** (화면 밖으로 안 날아감).
- Zero 중 **아래 방향키(S)** 를 누르면 살짝 아래로 밀리고, 놓으면 다시 부력으로 떠오른다.
- Zero → Inverted 전환, Inverted → Normal 복귀, 존 밖 이탈 후 **점프·낙하가 정상 속도**로 돌아온다 (drag 원복 확인).
- 비플레이어 Rigidbody(돌·광물 등)는 기존과 동일하게 동작 (이 태스크가 안 건드림).

- [ ] **Step 7: UVCS 체크인 (사람이 수행)**

검증 통과 후 사람이 UVCS로 체크인. 메시지 예: `feat: Zero 위상 부유 물리(부력+drag+클램프+미세보정)`.

---

### Task 2: 무중력→역중력 부드러운 flip 회전

**Files:**
- Modify: `Assets/Scripts/UI/Player/AntiGravityHandler.cs`

**Interfaces:**
- Consumes: `transform.rotation`(z축), `transitionDuration`, `TransitionIn()` 코루틴.
- Produces: `SetFlip(bool)`이 `localScale.y` 대신 `rotation.z` 목표각(0°/180°)을 즉시 세팅하고, `TransitionIn()`이 회전을 Lerp로 보간. `PlayerController.Flip()`의 `localScale.x`와 충돌 없음.

**주의(사전 확인 완료):** `PlayerController`는 [Flip()](../../Scripts/UI/Player/PlayerController.cs#L481)에서 `localScale.x`만 조작하고 `rotation`은 절대 손대지 않는다. z축 회전 flip 안전.

- [ ] **Step 1: SetFlip을 rotation 즉시 세팅으로 교체**

기존 `SetFlip()` ([AntiGravityHandler.cs:121-126](../../Scripts/UI/Player/AntiGravityHandler.cs#L121)) 를 z축 회전 기반으로 교체:

```csharp
    // inverted=true → 180°(뒤집힘), false → 0°(정상). 즉시 세팅(전환 없음)용.
    private void SetFlip(bool inverted)
    {
        Vector3 e = transform.eulerAngles;
        e.z = inverted ? 180f : 0f;
        transform.eulerAngles = e;
    }
```

- [ ] **Step 2: TransitionIn에서 즉시 flip 대신 회전 보간 사용**

기존 `TransitionIn()` ([AntiGravityHandler.cs:100-119](../../Scripts/UI/Player/AntiGravityHandler.cs#L100)) 의 세로 속도 감쇠 루프와 병행해 회전을 돌린다. `SetFlip(true)` 호출을 회전 보간으로 대체:

```csharp
    private IEnumerator TransitionIn()
    {
        float elapsed = 0f;
        float startVelY = _rb.linearVelocity.y;
        float startZ = transform.eulerAngles.z;
        if (startZ > 180f) startZ -= 360f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / transitionDuration;
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, Mathf.Lerp(startVelY, 0f, t));

            Vector3 e = transform.eulerAngles;
            e.z = Mathf.LerpAngle(startZ, 180f, Mathf.Clamp01(t));
            transform.eulerAngles = e;
            yield return null;
        }

        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, 0f);
        _rb.gravityScale = _defaultGravity * -antiGravityMultiplier;
        SetFlip(true); // 회전 정확히 180°로 확정

        IsActive = true;
        _transition = null;
    }
```

- [ ] **Step 3: Normal 복귀 시 부드럽게 되돌리기(선택) — 즉시 유지**

`SetPhase(Normal)`·`ExitZone()`의 `SetFlip(false)`는 그대로 둔다 (Inverted→Normal은 낙하로 자연스럽게 이어지므로 즉시 0° 복귀로 충분, YAGNI). 별도 수정 없음.

- [ ] **Step 4: Unity 수동 검증 (사람이 수행)**

- Zero → Inverted 전환 시 플레이어 스프라이트가 **transitionDuration 동안 천천히 180° 회전**하며 뒤집힌다 (툭 끊기지 않음).
- Inverted → Normal 복귀 시 정상 방향(0°)으로 돌아온다.
- 좌우 이동 flip(`localScale.x`)이 회전과 **충돌 없이** 정상 동작한다.
- 회전 중/후 콜라이더·낙하 판정이 어긋나지 않는다.

- [ ] **Step 5: UVCS 체크인 (사람이 수행)**

검증 통과 후 사람이 UVCS로 체크인. 메시지 예: `feat: 무중력→역중력 부드러운 flip 회전`.

---

## Self-Review

**Spec coverage:**
- 설계 §1 (부력/drag/클램프/미세보정) → Task 1 Step 1~5 ✅
- 설계 §2 (Jump 미수정, 방향 확인) → 계획에서 Jump 미수정, Task 1 Step 6 검증에 반영 ✅
- 설계 §3 (rotation 부드러운 flip) → Task 2 전체 ✅
- 비목표(점프 약화·Attractor·비플레이어 부유) → 계획에 미포함 ✅

**Placeholder scan:** 모든 코드 스텝에 실제 코드 포함, TBD/TODO 없음 ✅

**Type consistency:** `_defaultDrag`, `floatDrag`, `buoyancyForce`, `floatMaxSpeed`, `floatControlForce`, `SetFlip(bool)`, `TransitionIn()` — 태스크 간 명칭 일치 ✅ (죽은 코드였던 `RotateFlip` 헬퍼는 제거, TransitionIn 인라인 회전으로 통일.)
