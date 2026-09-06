# 제트팩(Jetpack) 유물 구현 계획

> **에이전트 작업자용:** 태스크 단위 구현. 각 태스크는 독립 컴파일 가능 단위.
> **이 프로젝트는 UVCS를 쓰므로 git 커밋 스텝이 없다.** Unity 컴파일 / Test Runner / 에셋 생성은 **사용자가 직접** 수행한다.

**Goal:** 드릴 선택 시 파기를 대체해 드릴 배터리로 날아오르는 패시브 유물 `Jetpack(4020)`을 추가한다.

**Architecture:** 패시브 `RelicBehaviour`가 매 프레임 드릴 모드를 감시. 드릴 모드면 `PlayerMining.SuppressMining`으로 드릴을 정지시키고, 점프키 홀드 시 `IPlayerController.SetJetpackThrust`로 상승·배터리 소모. 게임 코드 훅 2개(suppress 플래그, 추진 훅)를 최소 침습으로 추가.

**Tech Stack:** Unity C#, `Relic.*` 네임스페이스, `[SerializeReference]` behaviour.

## Global Constraints

- 네임스페이스: `Relic`(behaviour), `Relic.Data`(RelicID).
- RelicID: `Jetpack = 4020` (4019 MinerDrone 다음).
- 레벨 파라미터: `ascendSpeedPerLevel = { 8f, 10f, 12f }`, `drainPerLevel = { 2.0f, 1.6f, 1.3f }`.
- `maxLevel = 3`.
- 추진 키: `Input.GetButton("Jump")`. 연료 판정: `GetDrillBatteryRatio() > 0`. 드레인: `RefillBattery(-Drain()*Time.deltaTime)`.
- 중력 복원은 **컨트롤러 자동**(HandleNormalMovement 339행이 매 프레임 `defaultGravity` 리셋) — 훅/relic에 명시적 복원 없음.
- 공유 파일(`RelicID.cs`, `RelicSliceAssetGenerator.cs`, `RelicDebugGranter.cs`)은 한 태스크씩.

---

### Task 1: 게임 훅 A — `PlayerMining.SuppressMining`

드릴을 완전 정지시키는 플래그. 제트팩이 드릴 모드일 때 on.

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerMining.cs`

**Interfaces:**
- Produces: `public bool PlayerMining.SuppressMining` — true면 현재 채굴 전략의 `HandleUpdate/FixedUpdate/LateUpdate`가 스킵되고 `GetCurrentDigParameters`가 `CanDig=false` 반환.

- [ ] **Step 1: 필드 추가**

`PlayerMining.cs`의 `private IMiningStrategy _currentStrategy;`(45행) 아래에 추가:

```csharp
    private IMiningStrategy _currentStrategy;

    // 제트팩 유물: 드릴 모드에서 드릴을 완전 정지시킨다(파기·대시·에임·드릴 배터리 소모 차단).
    // 전략 Handle* 호출만 스킵하고 SwitchStrategy 부기는 유지(제트팩 off 시 올바른 전략 복귀).
    public bool SuppressMining { get; set; }
```

- [ ] **Step 2: HandleUpdate 전략 호출 게이트 (139행)**

```csharp
        // 2. 전략에 따른 로직 수행
        if (!SuppressMining) _currentStrategy?.HandleUpdate();
```

- [ ] **Step 3: FixedUpdate 전략 호출 게이트 (176행)**

```csharp
    void FixedUpdate()
    {
        if (!SuppressMining) _currentStrategy?.HandleFixedUpdate();
    }
```

- [ ] **Step 4: LateUpdate 전략 호출 게이트 (210행)**

```csharp
        // 각 전략의 LateUpdate 처리
        if (!SuppressMining) _currentStrategy?.HandleLateUpdate();
```

- [ ] **Step 5: DigParameters 가드 (378행 근처)**

`GetCurrentDigParameters`에서 `_currentStrategy == null` 체크 줄 앞에 추가:

```csharp
        if (_currentStrategy == null) return new DigParameters { CanDig = false };

        if (SuppressMining) return new DigParameters { CanDig = false };

        var p = _currentStrategy.GetDigParameters(baseRadius, targetTileType);
```

- [ ] **Step 6: 컴파일 확인 (사용자)**

기존 유물·도구는 `SuppressMining` 기본 false라 동작 불변.

---

### Task 2: 게임 훅 B — `IPlayerController.SetJetpackThrust` + `PlayerController` 구현

**Files:**
- Modify: `Assets/Scripts/UI/Player/IPlayerController.cs`
- Modify: `Assets/Scripts/UI/Player/PlayerController.cs`

**Interfaces:**
- Produces: `void IPlayerController.SetJetpackThrust(bool active, float ascendSpeed)` — active면 컨트롤러가 중력 0 + Y를 ascendSpeed로 구동 + 상승캡 상향. false면 다음 프레임 자동 복원.

- [ ] **Step 1: 인터페이스에 시그니처 추가**

`IPlayerController.cs`의 `void SuperJump(float upVelocity);` 아래에 추가:

```csharp
    void SuperJump(float upVelocity);

    /// <summary>제트팩 추진(유물). active면 중력 0 + 상방 ascendSpeed로 구동. false면 자동 복원.
    /// PlayerController의 HandleNormalMovement에 통합되어 FixedUpdate 속도 확정에 반영된다.</summary>
    void SetJetpackThrust(bool active, float ascendSpeed);
```

- [ ] **Step 2: PlayerController 필드 추가**

`private float defaultGravity;`(76행) 아래에 추가:

```csharp
    private float defaultGravity;
    private bool  _jetpackActive;
    private float _jetpackAscend;
```

- [ ] **Step 3: SetJetpackThrust 메서드 추가**

`public void SuperJump(float upVelocity)`(518행) 메서드 앞(또는 뒤)에 추가:

```csharp
    // 유물: 제트팩 추진. HandleNormalMovement가 이 값을 읽어 중력/속도에 반영.
    public void SetJetpackThrust(bool active, float ascendSpeed)
    {
        _jetpackActive = active;
        _jetpackAscend = ascendSpeed;
    }
```

- [ ] **Step 4: 중력 0 적용 (339행 직후)**

`HandleNormalMovement`의 `rb.gravityScale = defaultGravity;`(339행) 다음 줄에 추가:

```csharp
        rb.gravityScale = defaultGravity;
        // 제트팩 추진 중엔 중력 무시(이 프레임만). 비활성 시 위 default 리셋으로 자동 복원.
        if (_jetpackActive) rb.gravityScale = 0f;
```

- [ ] **Step 5: 스냅 차단에 제트팩 조건 추가 (439행)**

`bool canSnap = ...` 줄 끝에 `&& !_jetpackActive` 추가:

```csharp
        bool canSnap = !isJumping && (isGrounded || _prevGrounded) && !isWalkingOffLedge && !isSlidingDown && !antiGravityOverride && !_jetpackActive;
```

- [ ] **Step 6: 상승 속도 구동 + 캡 상향 (479행 직후, maxUpSpeed 계산 전)**

`targetVelocity += windVelocity;`(479행) 다음에 추가(상승캡 상향이 484행 `maxUpSpeed` 계산 전에 오도록):

```csharp
        targetVelocity += windVelocity;

        // 제트팩: 상방 속도를 목표치로 구동하고, 상승 리미터(jumpForce×1.2)에 깎이지 않게 캡을 상향.
        if (_jetpackActive)
        {
            targetVelocity.y = _jetpackAscend;
            _superJumpCap = Mathf.Max(_superJumpCap, _jetpackAscend);
        }
```

- [ ] **Step 7: 컴파일 확인 (사용자)**

`_jetpackActive` 기본 false → 기존 이동 불변. 인터페이스 구현 누락 에러 없어야 함(PlayerController는 `IPlayerController` 직접 구현, 3행).

---

### Task 3: `JetpackRelic` behaviour 작성

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Behaviours/JetpackRelic.cs`

**Interfaces:**
- Consumes: `PlayerMining.SuppressMining`(Task 1), `IPlayerController.SetJetpackThrust`(Task 2), `ToolController.IsDrillMode`, `PlayerMining.GetDrillBatteryRatio`/`RefillBattery`, `IPlayerController.IsWallClimbing`.

- [ ] **Step 1: behaviour 클래스 작성**

`Behaviours/JetpackRelic.cs` 전체:

```csharp
using System;
using UnityEngine;

namespace Relic
{
    // 제트팩(패시브·드릴모드 한정): 드릴을 선택하면 파기가 비활성화되고, 점프키 홀드로
    // 드릴 배터리를 소모하며 상승한다(파라식 홀드 추진). 레벨↑ = 상승속도↑ + 연료효율↑.
    [Serializable]
    public class JetpackRelic : RelicBehaviour
    {
        [Tooltip("레벨별 상승 목표 속도(<jumpForce×1.2 권장)")]
        [SerializeField] private float[] ascendSpeedPerLevel = { 8f, 10f, 12f };

        [Tooltip("레벨별 초당 연료(드릴 배터리) 소모. 레벨↑ = 효율↑")]
        [SerializeField] private float[] drainPerLevel = { 2.0f, 1.6f, 1.3f };

        private bool _suppressing; // 우리가 드릴 suppress를 켰는지 추적

        private float Ascend() => ascendSpeedPerLevel[Mathf.Clamp(level - 1, 0, ascendSpeedPerLevel.Length - 1)];
        private float Drain()  => drainPerLevel[Mathf.Clamp(level - 1, 0, drainPerLevel.Length - 1)];

        public override void OnUpdate()
        {
            var tools  = ctx?.tools;
            var mining = ctx?.mining;
            var pc     = ctx?.controller;
            if (tools == null || mining == null || pc == null) { Cleanup(); return; }

            // UI/로딩 열림 중이거나 드릴 모드가 아니면 정지 상태로 되돌린다.
            bool uiBlocked = UIStateManager.Instance != null && UIStateManager.Instance.CurrentState != UIState.None;
            if (uiBlocked || !tools.IsDrillMode) { Cleanup(); return; }

            // 드릴 모드: 드릴을 완전 정지시킨다(파기 대신 비행).
            if (!_suppressing) { mining.SuppressMining = true; _suppressing = true; }

            // 추진: 연료 있고, 벽타기 아니고, 점프키 홀드일 때만.
            bool hasFuel = mining.GetDrillBatteryRatio() > 0f;
            bool thrust  = hasFuel && !pc.IsWallClimbing && Input.GetButton("Jump");
            if (thrust)
            {
                pc.SetJetpackThrust(true, Ascend());
                mining.RefillBattery(-Drain() * Time.deltaTime);
            }
            else
            {
                pc.SetJetpackThrust(false, 0f);
            }
        }

        public override void OnUnequip() => Cleanup();

        // 드릴 복귀 + 추진 off. 드릴모드 이탈/UI/해제 시 호출.
        private void Cleanup()
        {
            var mining = ctx?.mining;
            if (_suppressing && mining != null) mining.SuppressMining = false;
            _suppressing = false;
            ctx?.controller?.SetJetpackThrust(false, 0f);
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사용자)**

`ctx.tools`(ToolController)·`ctx.mining`(PlayerMining)·`ctx.controller`(IPlayerController) 모두 RelicContext 기존 필드. 신규 멤버(Task 1·2)만 참조.

---

### Task 4: RelicID + 생성기 등록

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Data/RelicID.cs`
- Modify: `Assets/Scripts/Editor/RelicSliceAssetGenerator.cs`

- [ ] **Step 1: RelicID 추가**

`MinerDrone = 4019,` 줄 다음에 추가:

```csharp
        MinerDrone    = 4019,   // 채굴 드론 (패시브: 플레이어 주변 로밍하며 벽 탐색→이동→파기. 공전 드론 DrillDrone의 원안형)
        Jetpack       = 4020,   // 제트팩 (패시브: 드릴 선택 시 파기 대체, 점프키 홀드로 드릴배터리 소모하며 비행)
        // 이후 유물은 4021+로 추가
```

- [ ] **Step 2: 생성기 등록**

`minerDrone` 생성 줄(46행) 다음에 추가:

```csharp
        var minerDrone = CreateRelic(RelicID.MinerDrone, "채굴 드론",     RelicType.Passive, new MinerDroneRelic()); // 로밍 탐색→이동→파기 동행 드론
        var jetpack    = CreateRelic(RelicID.Jetpack,    "제트팩",       RelicType.Passive, new JetpackRelic());    // 드릴 대체 비행(드릴배터리 소모)
```

- [ ] **Step 3: db.allRelics 리스트에 추가**

리스트 끝 `minerDrone` 다음에 `jetpack` 추가:

```csharp
        db.allRelics = new List<RelicSO> { stat, pigeon, magnet, invinc, anvil, plasma, gambler, steroid, drone, spider, genr, spring, gravFlip, dashBomb, furnace, lightning, toolSwap, blackMarket, minerDrone, jetpack };
```

- [ ] **Step 4: 에셋 생성 실행 (사용자)**

Unity에서 `Tools > Relic > Generate Slice Assets` → `Jetpack.asset` 생성 + DB 등록.

---

### Task 5: 디버그 키 (KeypadMinus)

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs` (`DefaultBindings`)

- [ ] **Step 1: 바인딩 추가**

`DefaultBindings()` 배열의 `KeypadPlus`(MinerDrone) 줄 다음에 추가:

```csharp
            new Binding { key = KeyCode.KeypadPlus,  relic = RelicID.MinerDrone,  slot = 0 },
            new Binding { key = KeyCode.KeypadMinus, relic = RelicID.Jetpack,     slot = 0 },
```

- [ ] **Step 2: 씬 인스턴스 폴백 주의 (사용자)**

씬의 `RelicDebugGranter`가 인스펙터 bindings를 쓰면 `KeypadMinus → Jetpack → slot 0`을 수동 추가하거나 bindings를 비워 재초기화.

---

### Task 6: 인게임 통합 검증 (사용자)

**Files:** 없음.

- [ ] **Step 1: 기본 비행**
  1. 지하에서 드릴 해금 후 마우스휠로 드릴 선택.
  2. 좌클릭/우클릭 → **파기 안 됨**(드릴 정지) 확인.
  3. 점프키 홀드 → 상승 + 드릴 배터리 게이지 감소 확인. 떼면 낙하.

- [ ] **Step 2: 연료 고갈 / 재충전**
  1. 배터리 0까지 비행 → 추진 정지(낙하).
  2. 발전기 유물/리젠으로 충전 후 재비행.

- [ ] **Step 3: 모드 전환 / 해제 클린업**
  1. 공중 비행 중 마우스휠로 곡괭이 전환 → 드릴 정상 복귀(파기 가능), 중력 정상 낙하.
  2. 디버그로 제트팩 해제 → 드릴 정상 복귀.

- [ ] **Step 4: 레벨 스케일**
  1. `KeypadMinus` 재입력으로 Lv2/Lv3 → 상승속도·연료 지속 증가 체감.

- [ ] **Step 5: 엣지**
  1. 벽타기 중 점프키 → 추진 안 걸리고 벽타기 정상.
  2. UI(인벤토리 등) 열고 점프키 홀드 → 추진·소모 없음.

---

## Self-Review 메모

- **Spec 커버리지**: 드릴모드 한정(T3 `IsDrillMode`), 드릴 정지(T1 `SuppressMining`), 홀드 추진(T3+T2), 연료공유(T3 `RefillBattery`/`GetDrillBatteryRatio`), 레벨 둘 다(T3 배열), 상승캡·스냅·중력(T2 Step4~6), 클린업(T3 `Cleanup`), 벽타기/UI 가드(T3), RelicID/생성기(T4), 디버그(T5), 검증(T6) — 전 항목 태스크 존재.
- **타입 일관성**: `SuppressMining`(bool prop), `SetJetpackThrust(bool,float)`, `Ascend()/Drain()`(float), `GetDrillBatteryRatio()`(float 0~1) — Task 간 시그니처 일치.
- **중력 복원**: 별도 복원 태스크 없음 — HandleNormalMovement 339행 자동 복원에 의존(설계 검증 완료).
- **UVCS/테스트**: git 커밋 스텝 없음. 컴파일·에셋 생성·검증은 사용자 수행. (순수 로직이 적어 EditMode 테스트 태스크는 생략 — 비행/물리는 인게임 검증이 유일 신뢰 경로.)
