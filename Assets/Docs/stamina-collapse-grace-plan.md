# 탈진 / 쓰러짐 유예 시스템 — 구현 계획

@tags: stamina, exhaustion, collapse, plan, implementation

설계 문서: [stamina-collapse-grace-design.md](stamina-collapse-grace-design.md)

**목표:** 현재 스태미나 0은 "탈진"(행동 제한)으로, MaxStamina 0은 "5초 유예 후 쓰러짐"으로 분리한다.

**접근:** 판정 로직(히스테리시스·유예 카운트다운)을 MonoBehaviour 밖 순수 C#으로 빼서 EditMode로 검증하고,
MonoBehaviour들은 그 결과를 소비만 한다. 사망 트리거는 `PlayerStat.OnCollapse` 이벤트 하나로 좁힌다.

---

## 프로젝트 제약 (모든 태스크에 적용)

- **버전 관리는 UVCS.** `git add`/`git commit` 등 git 명령을 쓰지 않는다. 태스크 끝에 커밋 단계는 없다.
- **Unity Test Runner는 사람이 직접 돌린다.** Claude는 테스트 파일을 작성만 하고 실행하지 않으며,
  테스트 통과를 다음 태스크의 진행 조건으로 걸지 않는다.
- **효과음은 `SfxKeys` 상수 → `SoundManager` 경로로만 추가한다** (CLAUDE.md §14).
  인스펙터에 `AudioClip`을 직접 물리지 않는다.
- 새 `.cs` 파일은 파일 첫 줄에 `// @tags: ...` 주석을 단다 (프로젝트 검색 규약).
- 테스트 메서드명은 영문 PascalCase, 주석은 한국어 — 기존 `AmbienceSelectorTests` 스타일을 따른다.

---

## 파일 구조

| 파일 | 책임 |
|---|---|
| `Assets/Scripts/UI/Player/Stats/StaminaVitals.cs` (신규) | 순수 로직 — 탈진 히스테리시스(`StaminaExhaustion`), 유예 카운트다운(`CollapseGraceTimer`) |
| `Assets/Tests/EditMode/StaminaVitalsTests.cs` (신규) | 위 두 타입의 EditMode 테스트 |
| `PlayerStat.cs` | 상태 보유 — `IsExhausted`, `OnCollapse`, 최대치 변경 시 클램프 |
| `StaminaManager.cs` | 유예 타이머 구동 + 상태 감소 API의 클램프 + 탈진 진입 SFX |
| `GameOverHandler.cs` | 사망 구독 대상 교체 |
| `PlayerController.cs` | 탈진 시 벽타기 차단·강제 해제 |
| `PlayerMining.cs` | 탈진 시 파기 차단 (단일 choke point) |
| `PickaxeStrategy.cs` | 탈진 시 헛스윙 방지 |
| `HeartbeatSfx.cs` | 유예 중 최고 피치 고정 |
| `StaminaBarUI.cs` | 상태 감소 이중 차감 제거 |
| `SfxKeys.cs` | 탈진 진입 키 추가 |

---

## Task 1: 순수 로직 + EditMode 테스트

**Files:**
- Create: `Assets/Scripts/UI/Player/Stats/StaminaVitals.cs`
- Create: `Assets/Tests/EditMode/StaminaVitalsTests.cs`

**Produces (이후 태스크가 쓰는 것):**
- `static bool StaminaExhaustion.Evaluate(bool wasExhausted, float current, float max, float recoverRatio)`
- `const float StaminaExhaustion.DefaultRecoverRatio = 0.15f`
- `class CollapseGraceTimer` — `Tick(float maxStamina, float deltaTime) → bool`, `Grace{get;set;}`, `IsCollapsing`, `Remaining`, `HasFired`, `Reset()`

- [ ] **Step 1: 테스트 파일 작성**

`Assets/Tests/EditMode/StaminaVitalsTests.cs`:

```csharp
using NUnit.Framework;

public class StaminaVitalsTests
{
    // ── 탈진 히스테리시스 ──

    [Test]
    public void NotExhausted_WhenStaminaRemains()
    {
        Assert.IsFalse(StaminaExhaustion.Evaluate(false, 50f, 100f, 0.15f));
    }

    [Test]
    public void Exhausted_WhenCurrentHitsZero()
    {
        Assert.IsTrue(StaminaExhaustion.Evaluate(false, 0f, 100f, 0.15f));
    }

    [Test]
    public void StaysExhausted_BelowRecoverThreshold()
    {
        // 회복이 20/s라 0 찍은 다음 프레임에 풀리면 깜빡인다 — 임계치까지는 유지해야 한다
        Assert.IsTrue(StaminaExhaustion.Evaluate(true, 5f, 100f, 0.15f));
    }

    [Test]
    public void Recovers_AtRecoverThreshold()
    {
        Assert.IsFalse(StaminaExhaustion.Evaluate(true, 15f, 100f, 0.15f));
    }

    [Test]
    public void AlwaysExhausted_WhenMaxIsZero()
    {
        // 최대치 0 = 쓰러지는 중. 유예 시간 동안 파기·벽타기를 허용하면 안 된다
        Assert.IsTrue(StaminaExhaustion.Evaluate(false, 0f, 0f, 0.15f));
    }

    // ── 쓰러짐 유예 타이머 ──

    [Test]
    public void NeverFires_WhileMaxRemains()
    {
        var timer = new CollapseGraceTimer(5f);
        for (int i = 0; i < 100; i++)
            Assert.IsFalse(timer.Tick(10f, 0.1f));
    }

    [Test]
    public void FiresExactlyOnce_AfterGraceElapsed()
    {
        var timer = new CollapseGraceTimer(5f);
        int fired = 0;
        for (int i = 0; i < 100; i++)
            if (timer.Tick(0f, 0.1f)) fired++;

        Assert.AreEqual(1, fired);
    }

    [Test]
    public void ResetsGrace_WhenMaxRecoversInTime()
    {
        var timer = new CollapseGraceTimer(5f);
        for (int i = 0; i < 30; i++) timer.Tick(0f, 0.1f);  // 3초 경과
        timer.Tick(10f, 0.1f);                              // 회복 아이템 사용

        for (int i = 0; i < 30; i++)                        // 다시 3초 — 아직 안 죽어야 한다
            Assert.IsFalse(timer.Tick(0f, 0.1f));
    }

    [Test]
    public void StaysFired_EvenIfMaxRecoversAfterwards()
    {
        // 이미 게임오버 연출이 시작됐으므로 되돌리지 않는다
        var timer = new CollapseGraceTimer(5f);
        for (int i = 0; i < 60; i++) timer.Tick(0f, 0.1f);
        Assert.IsTrue(timer.HasFired);

        timer.Tick(100f, 0.1f);
        Assert.IsTrue(timer.HasFired);
    }

    [Test]
    public void RemainingCountsDown()
    {
        var timer = new CollapseGraceTimer(5f);
        timer.Tick(0f, 2f);
        Assert.AreEqual(3f, timer.Remaining, 0.001f);
    }

    [Test]
    public void IsCollapsing_OnlyDuringCountdown()
    {
        var timer = new CollapseGraceTimer(5f);
        Assert.IsFalse(timer.IsCollapsing);

        timer.Tick(0f, 1f);
        Assert.IsTrue(timer.IsCollapsing);
    }
}
```

- [ ] **Step 2: 구현 작성**

`Assets/Scripts/UI/Player/Stats/StaminaVitals.cs`:

```csharp
// @tags: stamina, exhaustion, collapse, grace, pure-logic
using UnityEngine;

/// <summary>
/// 탈진(현재 스태미나 0) 판정 — 순수 로직.
///
/// 히스테리시스가 핵심이다. 회복 속도가 20/s라 0을 찍자마자 다음 프레임에 탈진이 풀리고
/// 다시 소모해 0이 되는 깜빡임(핑퐁)이 생긴다. 그래서 진입은 0, 해제는 MaxStamina의 일정 비율.
/// </summary>
public static class StaminaExhaustion
{
    /// <summary>탈진 해제 임계치 기본값 — MaxStamina 대비 비율.</summary>
    public const float DefaultRecoverRatio = 0.15f;

    /// <param name="wasExhausted">직전 판정 결과</param>
    /// <returns>이번 판정 결과</returns>
    public static bool Evaluate(bool wasExhausted, float current, float max, float recoverRatio)
    {
        // 최대치 0 = 쓰러지는 중(유예 시간). 이 동안 파기·벽타기를 허용하지 않는다.
        if (max <= 0f) return true;

        if (!wasExhausted) return current <= 0f;

        return current < max * Mathf.Clamp01(recoverRatio);
    }
}

/// <summary>
/// MaxStamina가 0이 된 뒤 실제로 쓰러지기까지의 유예 타이머 — 순수 로직.
/// 유예 안에 MaxStamina가 회복되면(회복 아이템) 리셋되어 살아난다.
/// </summary>
public class CollapseGraceTimer
{
    private float _elapsed;
    private bool _fired;

    /// <summary>유예 시간(초). 인스펙터 값을 매 프레임 반영할 수 있게 열어 둔다.</summary>
    public float Grace { get; set; }

    public CollapseGraceTimer(float grace)
    {
        Grace = grace;
    }

    /// <summary>카운트다운이 진행 중인가 — UI·심장음 연출이 읽는다.</summary>
    public bool IsCollapsing => !_fired && _elapsed > 0f;

    /// <summary>쓰러지기까지 남은 시간(초).</summary>
    public float Remaining => Mathf.Max(0f, Grace - _elapsed);

    /// <summary>쓰러짐이 이미 확정됐는가.</summary>
    public bool HasFired => _fired;

    /// <returns>쓰러짐이 확정된 그 프레임에만 true. 그 뒤로는 계속 false.</returns>
    public bool Tick(float maxStamina, float deltaTime)
    {
        if (maxStamina > 0f)
        {
            if (!_fired) _elapsed = 0f;   // 회복 — 유예 리셋
            return false;
        }

        if (_fired) return false;

        _elapsed += deltaTime;
        if (_elapsed < Grace) return false;

        _fired = true;
        return true;
    }

    public void Reset()
    {
        _elapsed = 0f;
        _fired = false;
    }
}
```

- [ ] **Step 3: Unity 컴파일 확인**

Unity 에디터로 전환해 콘솔에 컴파일 에러가 없는지 본다.
(테스트 실행은 사람이 원할 때 Test Runner에서 직접 한다.)

---

## Task 2: PlayerStat — 탈진 상태 · 쓰러짐 이벤트 · 클램프

**Files:**
- Modify: `Assets/Scripts/UI/Player/Stats/PlayerStat.cs`

**Consumes:** `StaminaExhaustion.Evaluate`, `StaminaExhaustion.DefaultRecoverRatio`
**Produces:** `PlayerStat.IsExhausted`, `PlayerStat.OnCollapse`, `PlayerStat.RaiseCollapse()`

- [ ] **Step 1: 이벤트와 탈진 필드 추가**

`public event Action OnStaminaDepleted;` (15줄) 아래에 추가:

```csharp
    /// <summary>
    /// 쓰러짐 확정 — MaxStamina가 0인 채로 유예 시간이 다 지났다.
    /// 게임오버는 이 이벤트만 구독한다. 발화는 StaminaManager가 유예 타이머로 판단한다.
    ///
    /// OnStaminaDepleted(현재 스태미나 0)와 혼동하지 말 것 — 그쪽은 이제 사망이 아니라
    /// '탈진 진입' 신호다.
    /// </summary>
    public event Action OnCollapse;
```

`invincibilityDuration` 필드(25줄) 아래에 추가:

```csharp
    [Header("탈진")]
    [Tooltip("탈진이 풀리는 회복 임계치 — MaxStamina 대비 비율. 0이면 0을 벗어나는 즉시 풀려 깜빡인다.")]
    [SerializeField, Range(0f, 0.5f)]
    private float exhaustRecoverRatio = StaminaExhaustion.DefaultRecoverRatio;

    private bool _isExhausted;

    /// <summary>
    /// 탈진 — 현재 스태미나를 다 써서 파기·벽타기를 할 수 없는 상태.
    /// 죽지 않는다. 사망은 MaxStamina 0 + 유예 경과(OnCollapse)로만 일어난다.
    /// </summary>
    public bool IsExhausted => _isExhausted;
```

- [ ] **Step 2: 탈진 갱신 헬퍼와 발화 메서드 추가**

`RecoverStamina` 메서드(244줄 부근) 아래에 추가:

```csharp
    /// <summary>현재 스태미나가 바뀔 때마다 탈진 여부를 다시 판정한다.</summary>
    private void RefreshExhaustion()
    {
        _isExhausted = StaminaExhaustion.Evaluate(_isExhausted, currentStamina, MaxStamina, exhaustRecoverRatio);
    }

    /// <summary>
    /// 쓰러짐 발화. 유예 타이머를 굴리는 StaminaManager만 호출한다.
    /// </summary>
    public void RaiseCollapse()
    {
        OnCollapse?.Invoke();
    }
```

- [ ] **Step 3: 스태미나가 바뀌는 지점마다 탈진 갱신**

`UseStamina`를 교체한다. `RefreshExhaustion()`이 이벤트 발화보다 **앞에** 와야
구독자가 `IsExhausted`를 읽을 때 최신값을 본다.

```csharp
    public void UseStamina(float amount)
    {
        float before = currentStamina;
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        RefreshExhaustion();
        if (before > 0f && currentStamina <= 0f)
            OnStaminaDepleted?.Invoke();
    }
```

`RecoverStamina`:

```csharp
    public void RecoverStamina(float amount)
    {
        currentStamina = Mathf.Min(MaxStamina, currentStamina + amount);
        RefreshExhaustion();
    }
```

`CurrentStamina` 세터(146~150줄):

```csharp
    public float CurrentStamina
    {
        get => currentStamina;
        set { currentStamina = Mathf.Clamp(value, 0f, MaxStamina); RefreshExhaustion(); }
    }
```

`ClampCurrentStamina`:

```csharp
    public void ClampCurrentStamina()
    {
        float max = MaxStamina;
        if (currentStamina > max)
        {
            float before = currentStamina;
            currentStamina = Mathf.Max(0f, max);
            RefreshExhaustion();
            if (before > 0f && currentStamina <= 0f)
                OnStaminaDepleted?.Invoke();
        }
        else
        {
            RefreshExhaustion();
        }
    }
```

- [ ] **Step 4: 최대치가 줄어드는 경로에 클램프 연결**

`SetBaseValue`(94줄):

```csharp
    public void SetBaseValue(StatType type, float value)
    {
        baseValues.Set(type, value);
        MarkDirty();

        // 최대치를 낮추면 currentStamina가 새 상한 위에 남는다.
        // 그러면 StaminaBar가 표시값을 상한으로 잘라버려 '바가 안 줄어드는' 것처럼 보인다.
        if (type == StatType.MaxStamina) ClampCurrentStamina();
    }
```

`FromData`의 마지막 `MarkDirty();`(330줄) 뒤에 추가:

```csharp
        // 세이브의 maxStamina가 저장 당시보다 낮게 조정됐을 수 있다 — 상한 밖 값을 남기지 않는다.
        ClampCurrentStamina();
```

- [ ] **Step 5: Unity 컴파일 확인**

콘솔 에러 없음 확인.

---

## Task 3: StaminaManager — 유예 타이머 · 상태 감소 클램프 · 탈진 SFX

**Files:**
- Modify: `Assets/Scripts/UI/Player/StaminaManager.cs`
- Modify: `Assets/Scripts/_Core/Managers/SfxKeys.cs`

**Consumes:** `CollapseGraceTimer`, `PlayerStat.RaiseCollapse()`, `PlayerStat.IsExhausted`
**Produces:** `StaminaManager.IsCollapsing`, `StaminaManager.CollapseRemaining`, `SfxKeys.PlayerExhausted`

- [ ] **Step 1: SFX 키 추가**

`SfxKeys.cs`의 `PlayerHurt`(24줄) 다음 줄에 추가:

```csharp
    public const string PlayerExhausted = "player_exhausted"; // 현재 스태미나 소진 — 행동 불가 진입
```

음원은 나중에 `Assets/Audio/SFX/player_exhausted.wav`로 넣고
`Tools/Sound/Rescan SFX Folder`를 돌리면 등록된다. 클립이 없으면 조용히 무음이라 지금 없어도 된다.

- [ ] **Step 2: 유예 타이머 필드 추가**

`staminaRegenRate` 필드(19줄) 아래에 추가:

```csharp
    [Header("쓰러짐 유예")]
    [Tooltip("MaxStamina가 0이 된 뒤 실제로 쓰러지기까지의 유예 시간(초). 이 안에 회복하면 산다.")]
    public float collapseGraceSeconds = 5f;
```

`private float lastStaminaValue;`(35줄) 아래에 추가:

```csharp
    private readonly CollapseGraceTimer _collapse = new CollapseGraceTimer(5f);
    private bool _wasExhausted;

    /// <summary>쓰러짐 카운트다운 진행 중 — 심장음·UI 연출이 읽는다.</summary>
    public bool IsCollapsing => _collapse.IsCollapsing;

    /// <summary>쓰러지기까지 남은 시간(초).</summary>
    public float CollapseRemaining => _collapse.Remaining;
```

- [ ] **Step 3: Update에 유예 판정과 탈진 SFX 추가**

`Update()`(110~116줄)를 교체한다:

```csharp
    void Update()
    {
        if (playerStats.CurrentStamina < lastStaminaValue)
            TryStartRegeneration();

        lastStaminaValue = playerStats.CurrentStamina;

        // 쓰러짐 유예 — MaxStamina가 0인 상태가 유예 시간 내내 지속돼야 죽는다.
        //
        // 이벤트가 아니라 상태를 폴링하는 이유: MaxStamina가 0이 되는 경로가 여럿인데
        // (AddInjury/AddBurn/AddFrostbite/AddDiggingReduction/SetBaseValue/FromData)
        // 그중 하나라도 알림을 빠뜨리면 그대로 '안 죽는 버그'가 된다. 폴링은 경로와 무관하다.
        _collapse.Grace = collapseGraceSeconds;
        if (_collapse.Tick(playerStats.MaxStamina, Time.deltaTime))
            playerStats.RaiseCollapse();

        // 탈진 진입음 — 진입 프레임에 1회만. 탈진 중 파기·벽타기 시도에는 소리를 내지 않는다(연타 소음).
        bool exhausted = playerStats.IsExhausted;
        if (exhausted && !_wasExhausted && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.PlayerExhausted);
        _wasExhausted = exhausted;
    }
```

- [ ] **Step 4: 상태 감소 API에 클램프 추가**

`AddInjury` / `AddBurn` / `AddFrostbite`(139~147줄)를 교체한다.
[stamina-max-reduction-plan.md](stamina-max-reduction-plan.md)에 원래 하기로 되어 있었으나 빠져 있던 부분이다.

```csharp
    public void AddInjury(float amount)
    {
        if (DamageBlocked) return;
        injury += amount;
        playerStats.MarkDirty();
        playerStats.ClampCurrentStamina();
    }

    public void AddBurn(float amount)
    {
        if (DamageBlocked) return;
        burn += amount;
        playerStats.MarkDirty();
        playerStats.ClampCurrentStamina();
    }

    public void AddFrostbite(float amount)
    {
        if (DamageBlocked) return;
        frostbite += amount;
        playerStats.MarkDirty();
        playerStats.ClampCurrentStamina();
        FrostbiteOverlayUI.Instance?.UpdateFrostbite(frostbite, playerStats.GetBaseValue(StatType.MaxStamina));
    }
```

- [ ] **Step 5: 전체 회복 경로에서 유예 타이머 리셋**

`_collapse`는 한 번 발화하면(`HasFired`) 다시 발화하지 않는다.
플레이어 오브젝트가 씬 전환 후에도 살아남는 경우 두 번째 죽음이 즉시 처리되어 버리므로,
전체 회복 지점에서 명시적으로 리셋한다. (지상 복귀 시 `ExploreExitController`가 이 둘을 호출한다.)

`RecoverStatus`의 마지막 줄 뒤, `ResetDiggingReduction`의 마지막 줄 뒤에 각각 추가:

```csharp
        _collapse.Reset();
```

- [ ] **Step 6: Unity 컴파일 확인**

---

## Task 4: GameOverHandler — 사망 트리거 교체

**Files:**
- Modify: `Assets/Scripts/Gameplay/GameOverHandler.cs`

**Consumes:** `PlayerStat.OnCollapse`

- [ ] **Step 1: 구독 대상 교체**

`OnEnable`/`OnDisable`(83~93줄):

```csharp
    private void OnEnable()
    {
        if (playerStat != null)
            playerStat.OnCollapse += TriggerGameOver;
    }

    private void OnDisable()
    {
        if (playerStat != null)
            playerStat.OnCollapse -= TriggerGameOver;
    }
```

- [ ] **Step 2: 클래스 주석 갱신**

8줄의 `<see cref="PlayerStat.OnStaminaDepleted"/>를 받아` 를 다음으로 바꾼다:

```
/// <see cref="PlayerStat.OnCollapse"/>(MaxStamina 0 + 유예 경과)를 받아 게임오버 연출을 띄우고
/// 지상으로 돌려보낸다. 현재 스태미나가 0이 되는 것(탈진)으로는 죽지 않는다.
```

106줄의 `// 스태미나 0으로 죽는 경우 심장 루프가 울리는 중이다` 는 그대로 둔다 — 여전히 맞다.

- [ ] **Step 3: Unity 컴파일 확인**

---

## Task 5: PlayerController — 탈진 시 벽타기 차단 · 강제 해제

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerController.cs`

**Consumes:** `PlayerStat.IsExhausted`

- [ ] **Step 1: 해제 로직을 메서드로 추출**

`HandleWallClimbing()`(427줄) 바로 위에 추가:

```csharp
    /// <summary>벽타기 종료 — 중력은 HandleNormalMovement가 매 FixedUpdate 복원하므로 여기서 건드리지 않는다.</summary>
    private void StopWallClimbing()
    {
        if (!isWallClimbing) return;
        isWallClimbing = false;
        if (anim != null) anim.SetTrigger("ClimbExit");
    }
```

기존 자동 해제(271~275줄)를 이 메서드 호출로 바꾼다:

```csharp
        if (isWallClimbing && !isOverlappingWall && !isOverlappingLadder)
        {
            StopWallClimbing();
        }
```

- [ ] **Step 2: 탈진 중 벽타기 진입 차단**

Shift 토글의 진입 분기(218줄)를 교체한다:

```csharp
                    else if ((isOverlappingWall || isOverlappingLadder)
                             && !(_playerStat != null && _playerStat.IsExhausted))
```

- [ ] **Step 3: 타는 중 탈진하면 강제 해제**

`HandleWallClimbing()` 끝의 소모 부분(454~455줄)을 교체한다:

```csharp
        if (_playerStat != null)
        {
            _playerStat.UseStamina(_playerStat.StaminaCostPerSecond * Time.fixedDeltaTime);

            // 힘이 다 빠지면 벽에서 떨어진다. 사다리도 동일 — 위 소모 로직이 벽/사다리를 구분하지 않는다.
            if (_playerStat.IsExhausted) StopWallClimbing();
        }
```

- [ ] **Step 4: Unity 컴파일 확인**

---

## Task 6: 파기 차단 — 단일 choke point

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerMining.cs:500-517`
- Modify: `Assets/Scripts/UI/Player/Strategies/PickaxeStrategy.cs:102-118`

**Consumes:** `PlayerStat.IsExhausted`

- [ ] **Step 1: GetCurrentDigParameters에 탈진 게이트 추가**

`PlayerMining.GetCurrentDigParameters`의 마지막 `return p;`(516줄) 앞에 추가:

```csharp
        // 탈진 — 스태미나를 쓰는 파기만 막는다.
        // 유물 후처리(ApplyDigModifiers) 뒤에 두는 이유: 드릴은 DrillStrategy가
        // IgnoreStaminaCost = true를 세팅해 배터리로 파므로 탈진에 걸리면 안 된다.
        if (p.CanDig && !p.IgnoreStaminaCost && playerStats != null && playerStats.IsExhausted)
            p.CanDig = false;
```

이 한 지점이 삽·곡괭이·드릴 전 경로를 덮는다. 호출부(`Digger`, `PickaxeStrategy`)는
이미 `CanDig`를 검사하고 있으므로 따로 손대지 않는다.

- [ ] **Step 2: 곡괭이 헛스윙 방지**

`PickaxeStrategy.HandleUpdate`의 공격 입력 처리(103줄 `if (Input.GetMouseButton(0))`) 안,
`if (_currentPenaltyTimer > 0f) return;` 바로 다음 줄에 추가:

```csharp
            // 탈진 — 휘두를 힘이 없다. 여기서 막지 않으면 PerformDig가 CanDig를 확인하기 전에
            // 이미 스윙 모션과 dig_swing 사운드를 내보내 헛스윙이 된다.
            if (_context.playerStats != null && _context.playerStats.IsExhausted) return;
```

- [ ] **Step 3: Unity 컴파일 확인**

---

## Task 7: HeartbeatSfx — 유예 중 최고 피치

**Files:**
- Modify: `Assets/Scripts/Audio/HeartbeatSfx.cs:52-53`

**Consumes:** `StaminaManager.IsCollapsing`

- [ ] **Step 1: 유예 분기 추가**

`float max = stats.MaxStamina;` / `if (max <= 0f) return;`(52~53줄)를 교체한다:

```csharp
        float max = stats.MaxStamina;

        // 쓰러짐 유예 중(MaxStamina 0) — 5초 안에 회복해야 산다는 유일한 청각 신호다.
        // 아래 비율 계산은 max로 나누므로 여기서 따로 처리한다.
        if (staminaManager.IsCollapsing)
        {
            if (!_active)
            {
                sm.Loop(LoopHandle, SfxKeys.PlayerHeartbeat);
                _active = true;
            }
            sm.SetLoopPitch(LoopHandle, pitchAtZero);
            return;
        }

        if (max <= 0f) return;
```

- [ ] **Step 2: Unity 컴파일 확인**

---

## Task 8: StaminaBarUI — 상태 감소 이중 차감 제거

**Files:**
- Modify: `Assets/Scripts/UI/Player/StaminaBarUI.cs:91`

- [ ] **Step 1: 감소 전 최대치를 기준값에서 읽기**

91줄을 교체한다:

```csharp
        // GetBaseValue를 쓰는 이유: stat.MaxStamina(최종값)에는 StaminaManager가 주입한
        // -(injury+burn+frostbite+digging) Flat 감소가 이미 반영돼 있다. 그 값에서 아래처럼
        // totalReduction을 또 빼면 이중 차감이 되어 사용 가능치가 실제보다 작게 표시된다.
        // 같은 '감소 전 최대치'를 StaminaManager의 동상 오버레이 호출부도 GetBaseValue로 가져온다.
        float originalMax = stat.GetBaseValue(StatType.MaxStamina);
```

- [ ] **Step 2: Unity 컴파일 확인 후 인게임 확인**

`StaminaTestHelper`의 `TestAddInjury`를 눌러 부상을 준 다음,
바의 초록 구간 + 부상 구간 합이 전체 너비를 채우고 초록 구간이 실제 잔량과 맞는지 본다.

---

## Task 9: 문서 갱신

**Files:**
- Modify: `Assets/Docs/stamina-max-reduction-plan.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: 기존 설계 문서에 변경 사실 반영**

`stamina-max-reduction-plan.md` 최상단(제목 아래)에 추가:

```markdown
> ⚠ **2026-08-05 갱신** — MaxStamina가 0이 되면 즉시 사망하던 동작이 바뀌었다.
> 이제 5초 유예 후에도 0이면 쓰러지며, 현재 스태미나가 0이 되는 것(탈진)으로는 죽지 않는다.
> 자세한 내용은 [stamina-collapse-grace-design.md](stamina-collapse-grace-design.md).
```

같은 문서 §"벽타기 현재 스테미나 소모" 절 끝에 한 줄 추가:

```markdown
현재 스태미나가 0이 되면 탈진 상태가 되어 벽에서 떨어지고, 회복 임계치까지 차야 다시 탈 수 있다.
```

- [ ] **Step 2: CLAUDE.md 핵심 파일 표에 한 줄 추가**

`| Assets/Docs/ | 설계 결정 문서 디렉토리 |` 위쪽 적당한 위치에:

```markdown
| `Assets/Scripts/UI/Player/Stats/StaminaVitals.cs` | 탈진 히스테리시스·쓰러짐 유예 순수 로직. 현재 스태미나 0=탈진(행동 제한), MaxStamina 0+5초=사망. EditMode 테스트 있음 |
```

`## 설계 결정 문서` 목록에도 추가:

```markdown
- `Assets/Docs/stamina-collapse-grace-design.md` — 탈진/쓰러짐 유예 설계. 사망 조건이 `OnStaminaDepleted` → `OnCollapse`로 옮겨간 배경
```

---

## 사람이 직접 확인할 것 (Claude가 못 하는 부분)

1. **Unity Test Runner** — EditMode에서 `StaminaVitalsTests` 실행
2. **인게임 시나리오**
   - 벽 타다 스태미나 소진 → 떨어지고, 잠깐은 다시 못 붙는지
   - 탈진 중 곡괭이·삽이 안 나가고 드릴은 되는지
   - `StaminaTestHelper.TestAddInjury`로 최대치를 0까지 깎고 5초 버티면 죽는지
   - 그 5초 안에 회복 아이템을 쓰면 사는지
   - F8 진단 패널에서 최대 스태미나를 낮췄을 때 바가 즉시 따라 내려오는지
3. **음원** — `Assets/Audio/SFX/player_exhausted.wav` 추가 후 `Tools/Sound/Rescan SFX Folder`
