# 반중력 구간 3단계 위상 + 배경 예고 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (권장) 또는 superpowers:executing-plans 로 task 단위 실행. Steps는 체크박스(`- [ ]`)로 추적.

**Goal:** 반중력 특수청크를 정상→무중력→역중력 3단계로 확장하고, 전용 배경 오브젝트가 다음 위상 색을 가속 점멸해 전환을 예고한다.

**Architecture:** 테스트 가능한 순수 로직(`GravityPhases` 정적 헬퍼: 순환 순서 + 위상별 gravityScale)을 분리한다. `AntiGravityZone`(MonoBehaviour)은 위상 순환 코루틴 + 2개 이벤트(`OnPhaseChanged`, `OnPhaseWarning`)를 발화하고, `AntiGravityHandler`(플레이어)와 `AntiGravityBackground`(배경)가 이를 구독한다.

**Tech Stack:** Unity 2D, C#, NUnit (EditMode), UVCS.

## Global Constraints

- 버전 관리: **UVCS** — git 명령 사용 금지. "체크인" 단계는 사람이 UVCS로 수행.
- 테스트 실행: **사람이 직접** Unity Test Runner에서 수행. Claude/실행자는 테스트 실행 도구를 호출하지 않는다.
- 더티 플래그 등 청크 규약은 이 작업과 무관(지형 픽셀 수정 없음).
- 모든 신규 코드 파일 상단에 `// @tags: ...` 주석 유지.
- namespace 없음(기존 `AntiGravityZone`/`AntiGravityHandler`가 글로벌 네임스페이스).
- 배경은 전역 `BackgroundManager`가 아니라 특수청크 프리팹 내 **전용 배경 오브젝트**에만 적용.

---

### Task 1: GravityPhase enum + GravityPhases 순수 헬퍼

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/GravityPhase.cs`
- Test: `Assets/Tests/EditMode/GravityPhaseTests.cs`

**Interfaces:**
- Produces:
  - `enum GravityPhase { Normal, Zero, Inverted }`
  - `static GravityPhase GravityPhases.Next(GravityPhase phase)`
  - `static float GravityPhases.ScaleFor(GravityPhase phase, float defaultScale, float multiplier)`

- [ ] **Step 1: 실패하는 테스트 작성**

Create `Assets/Tests/EditMode/GravityPhaseTests.cs`:

```csharp
using NUnit.Framework;

/// <summary>GravityPhases 순수 로직 — 순환 순서 + 위상별 gravityScale.</summary>
public class GravityPhaseTests
{
    [Test]
    public void Next_CyclesNormalZeroInvertedNormal()
    {
        Assert.AreEqual(GravityPhase.Zero,     GravityPhases.Next(GravityPhase.Normal));
        Assert.AreEqual(GravityPhase.Inverted, GravityPhases.Next(GravityPhase.Zero));
        Assert.AreEqual(GravityPhase.Normal,   GravityPhases.Next(GravityPhase.Inverted));
    }

    [Test]
    public void ScaleFor_Normal_ReturnsDefault()
    {
        Assert.AreEqual(3f, GravityPhases.ScaleFor(GravityPhase.Normal, 3f, 0.7f));
    }

    [Test]
    public void ScaleFor_Zero_ReturnsZero()
    {
        Assert.AreEqual(0f, GravityPhases.ScaleFor(GravityPhase.Zero, 3f, 0.7f));
    }

    [Test]
    public void ScaleFor_Inverted_ReturnsNegativeScaledByMultiplier()
    {
        Assert.AreEqual(-1.4f, GravityPhases.ScaleFor(GravityPhase.Inverted, 2f, 0.7f), 1e-5f);
    }

    [Test]
    public void ScaleFor_Inverted_UsesAbsoluteOfDefault()
    {
        // 음수 기본값도 |값|×mult 후 부호 반전.
        Assert.AreEqual(-1.4f, GravityPhases.ScaleFor(GravityPhase.Inverted, -2f, 0.7f), 1e-5f);
    }
}
```

- [ ] **Step 2: 사람이 테스트 실행 → 실패 확인**

Unity Test Runner(EditMode) 실행. 예상: `GravityPhase`/`GravityPhases` 미정의로 컴파일 실패.

- [ ] **Step 3: 최소 구현 작성**

Create `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/GravityPhase.cs`:

```csharp
// @tags: gravity, phase, enum, anti-gravity, special-chunk
using UnityEngine;

/// <summary>반중력 구간의 중력 위상. 정상 → 무중력 → 역중력 순환.</summary>
public enum GravityPhase { Normal, Zero, Inverted }

/// <summary>GravityPhase 순수 로직 — 순환 순서 및 위상별 gravityScale 계산.</summary>
public static class GravityPhases
{
    /// <summary>순환 다음 위상. Normal → Zero → Inverted → Normal.</summary>
    public static GravityPhase Next(GravityPhase phase)
    {
        switch (phase)
        {
            case GravityPhase.Normal:   return GravityPhase.Zero;
            case GravityPhase.Zero:     return GravityPhase.Inverted;
            case GravityPhase.Inverted: return GravityPhase.Normal;
            default:                    return GravityPhase.Normal;
        }
    }

    /// <summary>위상별 적용 gravityScale.
    /// Normal=defaultScale, Zero=0, Inverted=-|defaultScale|×multiplier.</summary>
    public static float ScaleFor(GravityPhase phase, float defaultScale, float multiplier)
    {
        switch (phase)
        {
            case GravityPhase.Zero:     return 0f;
            case GravityPhase.Inverted: return -Mathf.Abs(defaultScale) * multiplier;
            default:                    return defaultScale;
        }
    }
}
```

- [ ] **Step 4: 사람이 테스트 실행 → 통과 확인**

Unity Test Runner(EditMode). 예상: 5개 테스트 PASS.

- [ ] **Step 5: 체크인 (사람, UVCS)**

`GravityPhase.cs`, `GravityPhaseTests.cs` 체크인. 메시지 예: `feat: GravityPhase 3단계 위상 enum + 순수 헬퍼`.

---

### Task 2: AntiGravityZone 3단계 위상 + 이벤트

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/AntiGravityZone.cs` (전체 교체)

**Interfaces:**
- Consumes: `GravityPhase`, `GravityPhases.Next`, `GravityPhases.ScaleFor` (Task 1)
- Consumes: `AntiGravityHandler.SetPhase(GravityPhase)`, `AntiGravityHandler.ExitZone()` (Task 3 — 시그니처 선반영)
- Produces:
  - `event Action<GravityPhase> AntiGravityZone.OnPhaseChanged`
  - `event Action<GravityPhase, float> AntiGravityZone.OnPhaseWarning` (다음 위상, 남은 leadTime초)

- [ ] **Step 1: AntiGravityZone.cs 전체 교체**

기존 2단계 토글(`Activate/Deactivate`, `OnPhaseChanged(bool)`)을 아래로 교체:

```csharp
// @tags: zone, anti-gravity, trigger, special-chunk, chunk, rigidbody
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 반중력 청크 트리거 존. 정상 → 무중력 → 역중력 순으로 위상을 순환한다.
/// - 플레이어: AntiGravityHandler.SetPhase / ExitZone 호출
/// - 비플레이어 Rigidbody2D: gravityScale 직접 변경 (원본 보관 후 복원)
/// - OnPhaseChanged: 전환 순간 / OnPhaseWarning: 전환 leadTime초 전 (배경 예고용)
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AntiGravityZone : MonoBehaviour
{
    [Header("Phase Durations (초)")]
    [SerializeField] private float normalDuration  = 4f;
    [SerializeField] private float zeroDuration     = 3f;
    [SerializeField] private float invertedDuration = 3f;

    [Header("Warning")]
    [Tooltip("전환 몇 초 전부터 배경 예고를 시작할지")]
    [SerializeField] private float warningLeadTime = 1.5f;

    [Header("Physics")]
    [Tooltip("역중력 강도 배수")]
    [SerializeField] private float antiGravityMultiplier = 0.7f;

    /// <summary>전환 순간. 새 위상 전달.</summary>
    public event Action<GravityPhase> OnPhaseChanged;
    /// <summary>전환 leadTime초 전. 곧 올 위상과 남은 시간 전달.</summary>
    public event Action<GravityPhase, float> OnPhaseWarning;

    private GravityPhase _phase = GravityPhase.Normal;
    private AntiGravityHandler _playerHandler;
    private readonly Dictionary<Rigidbody2D, float> _savedGravityScales = new();
    private readonly HashSet<Rigidbody2D> _insideObjects = new();

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger)
        {
            Debug.LogWarning("[AntiGravityZone] Collider2D가 Trigger가 아닙니다. 자동으로 isTrigger = true 설정.");
            col.isTrigger = true;
        }
    }

    private void OnEnable() => StartCoroutine(CycleCo());

    private void OnDisable()
    {
        StopAllCoroutines();
        RestoreAll();
        _insideObjects.Clear();
        _playerHandler = null;
        _phase = GravityPhase.Normal;
    }

    private float DurationOf(GravityPhase phase)
    {
        switch (phase)
        {
            case GravityPhase.Zero:     return zeroDuration;
            case GravityPhase.Inverted: return invertedDuration;
            default:                    return normalDuration;
        }
    }

    private IEnumerator CycleCo()
    {
        while (true)
        {
            ApplyToAll();
            OnPhaseChanged?.Invoke(_phase);

            float duration = DurationOf(_phase);
            float lead = Mathf.Min(warningLeadTime, duration);
            float beforeWarning = duration - lead;
            if (beforeWarning > 0f) yield return new WaitForSeconds(beforeWarning);

            GravityPhase next = GravityPhases.Next(_phase);
            OnPhaseWarning?.Invoke(next, lead);
            if (lead > 0f) yield return new WaitForSeconds(lead);

            _phase = next;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var handler = other.GetComponent<AntiGravityHandler>();
        if (handler != null)
        {
            _playerHandler = handler;
            handler.SetPhase(_phase);
            return;
        }

        var rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        _insideObjects.Add(rb);
        ApplyPhase(rb, _phase);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var handler = other.GetComponent<AntiGravityHandler>();
        if (handler != null)
        {
            _playerHandler = null;
            handler.ExitZone();
            return;
        }

        var rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        _insideObjects.Remove(rb);
        RestoreGravity(rb);
    }

    private void ApplyToAll()
    {
        if (_playerHandler != null) _playerHandler.SetPhase(_phase);
        foreach (var rb in _insideObjects)
            if (rb != null) ApplyPhase(rb, _phase);
    }

    private void RestoreAll()
    {
        if (_playerHandler != null) _playerHandler.ExitZone();
        foreach (var rb in _insideObjects)
            if (rb != null) RestoreGravity(rb);
        _savedGravityScales.Clear();
    }

    private void ApplyPhase(Rigidbody2D rb, GravityPhase phase)
    {
        if (!_savedGravityScales.ContainsKey(rb))
            _savedGravityScales[rb] = rb.gravityScale;

        float original = _savedGravityScales[rb];
        if (phase == GravityPhase.Normal)
        {
            rb.gravityScale = original;
            return;
        }
        float baseScale = Mathf.Approximately(original, 0f) ? 1f : original;
        rb.gravityScale = GravityPhases.ScaleFor(phase, baseScale, antiGravityMultiplier);
    }

    private void RestoreGravity(Rigidbody2D rb)
    {
        if (_savedGravityScales.TryGetValue(rb, out float saved))
        {
            rb.gravityScale = saved;
            _savedGravityScales.Remove(rb);
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사람, Unity 콘솔)**

Task 3 미완료 시 `AntiGravityHandler.SetPhase/ExitZone` 미정의로 컴파일 에러가 날 수 있다. Task 3과 함께 체크인하거나, Task 3을 먼저 완료한 뒤 본 task를 마무리한다. 예상: Task 3 완료 후 에러 0건.

- [ ] **Step 3: 체크인 (사람, UVCS)**

`AntiGravityZone.cs` 체크인 (Task 3과 묶어도 됨). 메시지 예: `feat: AntiGravityZone 3단계 위상 순환 + 예고 이벤트`.

---

### Task 3: AntiGravityHandler 위상 일반화 (SetPhase / ExitZone)

**Files:**
- Modify: `Assets/Scripts/UI/Player/AntiGravityHandler.cs` (전체 교체)

**Interfaces:**
- Consumes: `GravityPhase` (Task 1)
- Produces:
  - `void AntiGravityHandler.SetPhase(GravityPhase phase)`
  - `void AntiGravityHandler.ExitZone()`
  - `bool AntiGravityHandler.IsGravityInverted` (기존 유지 — `PlayerController`가 점프 방향에 사용; Inverted 단계 및 전환 중에만 true)

- [ ] **Step 1: AntiGravityHandler.cs 전체 교체**

```csharp
// @tags: player, anti-gravity, gravity, handler, phase
using System.Collections;
using UnityEngine;

/// <summary>
/// 반중력 구역 진입 시 플레이어 중력 위상 전환·스프라이트 flip 처리.
/// AntiGravityZone이 SetPhase(phase)/ExitZone()을 호출한다.
/// PlayerController는 IsGravityInverted만 읽어 점프 방향을 결정한다.
/// </summary>
public class AntiGravityHandler : MonoBehaviour
{
    [SerializeField] private float antiGravityMultiplier = 0.7f;
    [SerializeField] private float transitionDuration = 0.25f;

    public bool IsActive { get; private set; }            // 역중력 완전 활성
    public bool IsGravityInverted => IsActive || _transition != null;

    private Rigidbody2D _rb;
    private float _defaultGravity;
    private Coroutine _transition;
    private GravityPhase _currentPhase = GravityPhase.Normal;

    private PlayerController _playerController;
    private StaminaManager _staminaManager;
    private PlayerStat _playerStat;
    private Animator _animator;
    private float _peakUpwardSpeed;
    private float _antiGravityAirTime;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _defaultGravity = _rb.gravityScale;
        _playerController = GetComponent<PlayerController>();
        _staminaManager   = GetComponent<StaminaManager>();
        _playerStat       = GetComponent<PlayerStat>();
        _animator         = GetComponent<Animator>();
    }

    /// <summary>존이 현재 위상을 전달. 같은 위상 재호출은 무시(idempotent).</summary>
    public void SetPhase(GravityPhase phase)
    {
        if (_rb == null) return;
        if (phase == _currentPhase) return;
        _currentPhase = phase;
        StopTransition();

        switch (phase)
        {
            case GravityPhase.Normal:
                _rb.gravityScale = _defaultGravity;
                SetFlip(false);
                IsActive = false;
                ResetImpactTracking();
                break;
            case GravityPhase.Zero:
                _rb.gravityScale = 0f;
                SetFlip(false);
                IsActive = false;
                ResetImpactTracking();
                break;
            case GravityPhase.Inverted:
                _transition = StartCoroutine(TransitionIn());
                break;
        }
    }

    /// <summary>존 이탈/언로드 시 호출. 즉시 정상 복귀.</summary>
    public void ExitZone()
    {
        _currentPhase = GravityPhase.Normal;
        StopTransition();
        if (_rb == null) return;
        _rb.gravityScale = _defaultGravity;
        SetFlip(false);
        IsActive = false;
        ResetImpactTracking();
    }

    private IEnumerator TransitionIn()
    {
        float elapsed = 0f;
        float startVelY = _rb.linearVelocity.y;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / transitionDuration;
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, Mathf.Lerp(startVelY, 0f, t));
            yield return null;
        }

        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, 0f);
        _rb.gravityScale = _defaultGravity * -antiGravityMultiplier;
        SetFlip(true);

        IsActive = true;
        _transition = null;
    }

    private void SetFlip(bool inverted)
    {
        Vector3 s = transform.localScale;
        s.y = inverted ? -Mathf.Abs(s.y) : Mathf.Abs(s.y);
        transform.localScale = s;
    }

    private void ResetImpactTracking()
    {
        _peakUpwardSpeed = 0f;
        _antiGravityAirTime = 0f;
    }

    private void StopTransition()
    {
        if (_transition == null) return;
        StopCoroutine(_transition);
        _transition = null;
    }

    private void FixedUpdate()
    {
        if (!IsActive) return;
        if (_rb.linearVelocity.y > 0f)
            _peakUpwardSpeed = Mathf.Max(_peakUpwardSpeed, _rb.linearVelocity.y);
        if (_playerController != null && !_playerController.IsGrounded)
            _antiGravityAirTime += Time.fixedDeltaTime;
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        if (!IsActive) return;
        foreach (var contact in col.contacts)
        {
            if (contact.normal.y < -0.5f)
            {
                ApplyCeilingImpact();
                break;
            }
        }
    }

    private void ApplyCeilingImpact()
    {
        float speed = _peakUpwardSpeed;
        float airTime = _antiGravityAirTime;
        _peakUpwardSpeed = 0f;
        _antiGravityAirTime = 0f;

        if (_playerController == null) return;

        bool isHard = speed >= _playerController.hardLandSpeedThreshold
                   && airTime >= _playerController.hardLandTimeThreshold;

        if (_animator != null)
            _animator.SetTrigger(isHard ? "HardLand" : "WeakLand");

        if (isHard)
            StartCoroutine(HardLandRoutine());

        if (speed > _playerController.fallDamageThreshold && _staminaManager != null)
        {
            float excess  = speed - _playerController.fallDamageThreshold;
            float damage  = excess * _playerController.fallDamageMultiplier;
            float reduce  = _playerStat != null
                ? Mathf.Clamp01(_playerStat.GetFinalValue(StatType.FallDamageReduce))
                : 0f;
            damage *= (1f - reduce);

            if (damage > 0f)
            {
                _staminaManager.AddInjury(damage);
                HitFlashUI.Instance?.Flash(0.5f, 0.2f);
                _playerStat?.StartInvincibility();
            }
        }
    }

    private IEnumerator HardLandRoutine()
    {
        _playerController.isHardLanding = true;
        _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        yield return new WaitForSeconds(_playerController.hardLandRecoverTime);
        _playerController.isHardLanding = false;
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사람, Unity 콘솔)**

Task 2 + Task 3 함께 컴파일. 예상: 에러 0건. `PlayerController`의 `_antiGravityHandler.IsGravityInverted` 참조는 변경 없이 그대로 동작.

- [ ] **Step 3: PlayMode 확인 (사람)**

플레이어가 존 진입 후 3단계 순환 체감:
- 정상: 평소 중력
- 무중력: 둥둥 뜸(천천히 상승, 천장/바닥에서 정지)
- 역중력: 위로 떨어지고 스프라이트 뒤집힘, 천장 충돌 시 스태미나 데미지
- 존 이탈 시 즉시 정상 복귀(스프라이트·중력)

- [ ] **Step 4: 체크인 (사람, UVCS)**

`AntiGravityHandler.cs` (+ Task 2 `AntiGravityZone.cs`) 체크인. 메시지 예: `feat: AntiGravityHandler 3단계 위상(SetPhase/ExitZone)`.

---

### Task 4: AntiGravityBackground — 위상 이미지 교체 + 가속 점멸 예고

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/AntiGravityBackground.cs`

**Interfaces:**
- Consumes: `GravityPhase` (Task 1), `AntiGravityZone.OnPhaseChanged`, `AntiGravityZone.OnPhaseWarning` (Task 2)

- [ ] **Step 1: AntiGravityBackground.cs 작성**

```csharp
// @tags: anti-gravity, background, visual, phase, blink, special-chunk
using System.Collections;
using UnityEngine;

/// <summary>
/// 반중력 특수청크 전용 배경 오브젝트. AntiGravityZone 위상에 맞춰 스프라이트를 교체하고,
/// 전환 직전 다음 위상 스프라이트를 가속 점멸하여 예고한다. (전역 BackgroundManager와 무관)
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class AntiGravityBackground : MonoBehaviour
{
    [Tooltip("미지정 시 부모에서 AntiGravityZone 탐색")]
    [SerializeField] private AntiGravityZone zone;

    [Tooltip("index = (int)GravityPhase — 0:정상 1:무중력 2:역중력")]
    [SerializeField] private Sprite[] phaseSprites = new Sprite[3];

    [Header("Blink (전환 예고)")]
    [Tooltip("점멸 시작 간격(느림)")]
    [SerializeField] private float blinkStartInterval = 0.4f;
    [Tooltip("점멸 종료 간격(전환 직전, 빠름)")]
    [SerializeField] private float blinkEndInterval = 0.08f;

    private SpriteRenderer _sr;
    private GravityPhase _currentPhase = GravityPhase.Normal;
    private Coroutine _blink;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (zone == null) zone = GetComponentInParent<AntiGravityZone>();
    }

    private void OnEnable()
    {
        if (zone == null) return;
        zone.OnPhaseChanged += HandlePhaseChanged;
        zone.OnPhaseWarning += HandlePhaseWarning;
    }

    private void OnDisable()
    {
        if (zone == null) return;
        zone.OnPhaseChanged -= HandlePhaseChanged;
        zone.OnPhaseWarning -= HandlePhaseWarning;
        StopBlink();
    }

    private void HandlePhaseChanged(GravityPhase phase)
    {
        StopBlink();
        _currentPhase = phase;
        ApplySprite(phase);
    }

    private void HandlePhaseWarning(GravityPhase next, float leadTime)
    {
        StopBlink();
        _blink = StartCoroutine(BlinkCo(next, leadTime));
    }

    private IEnumerator BlinkCo(GravityPhase next, float leadTime)
    {
        Sprite baseSprite = SpriteFor(_currentPhase);
        Sprite nextSprite = SpriteFor(next);
        float elapsed = 0f;
        bool showNext = false;

        while (elapsed < leadTime)
        {
            showNext = !showNext;
            _sr.sprite = showNext ? nextSprite : baseSprite;

            float t = leadTime > 0f ? Mathf.Clamp01(elapsed / leadTime) : 1f;
            float interval = Mathf.Lerp(blinkStartInterval, blinkEndInterval, t);
            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }

        _sr.sprite = baseSprite; // 곧 OnPhaseChanged가 다음 위상으로 확정.
        _blink = null;
    }

    private void StopBlink()
    {
        if (_blink == null) return;
        StopCoroutine(_blink);
        _blink = null;
    }

    private void ApplySprite(GravityPhase phase)
    {
        var s = SpriteFor(phase);
        if (s != null) _sr.sprite = s;
    }

    private Sprite SpriteFor(GravityPhase phase)
    {
        int i = (int)phase;
        return (phaseSprites != null && i >= 0 && i < phaseSprites.Length) ? phaseSprites[i] : null;
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사람, Unity 콘솔)**

예상: 에러 0건.

- [ ] **Step 3: 프리팹 셋업 (사람, Unity 에디터)**

반중력 특수청크 프리팹의 전용 배경 오브젝트에:
1. `AntiGravityBackground` 컴포넌트 부착 (SpriteRenderer 필요).
2. `zone` 필드에 해당 청크의 `AntiGravityZone` 연결 (미지정 시 부모 자동 탐색).
3. `phaseSprites`에 3개 스프라이트 할당: index 0=정상, 1=무중력, 2=역중력.
4. `blinkStartInterval`/`blinkEndInterval` 기본값(0.4/0.08) 확인.

- [ ] **Step 4: PlayMode 확인 (사람)**

- 각 위상에서 배경이 해당 이미지로 표시되는지
- 전환 `warningLeadTime`(기본 1.5초) 전부터 다음 위상 이미지가 점점 빠르게 점멸하는지
- 전환 순간 점멸이 멈추고 다음 이미지로 확정되는지

- [ ] **Step 5: 체크인 (사람, UVCS)**

`AntiGravityBackground.cs` + 프리팹 변경 체크인. 메시지 예: `feat: AntiGravityBackground 위상 이미지 + 가속 점멸 예고`.

---

## 검증 매핑 (spec → task)

| Spec 항목 | Task |
|-----------|------|
| §1 3단계 위상 모델(enum, 순환) | Task 1, 2 |
| §1 단계별 지속시간 SerializeField | Task 2 |
| §2 OnPhaseChanged / OnPhaseWarning | Task 2 |
| §3 플레이어 SetPhase(Normal/Zero/Inverted) | Task 3 |
| §3 비플레이어 Rigidbody 위상 적용/복원 | Task 2 (ApplyPhase/RestoreGravity) |
| §3 IsGravityInverted 의미 유지 | Task 3 |
| §4 배경 이미지 교체 + 가속 점멸 예고 | Task 4 |
| §5 엣지(이탈/언로드/점멸 중 전환) | Task 2 (OnDisable), Task 3 (ExitZone), Task 4 (StopBlink) |
| §6 EditMode 순수 로직 테스트 | Task 1 |
| §6 PlayMode 사람 검증 | Task 3, 4 |

## 비고

- 데미지 함정은 기존 `MagmaFloorDamage`/`FallingHazardBase` 재사용 — 신규 코드 없음(범위 외).
- `.cs.private.0` 백업 파일은 손대지 않음.
