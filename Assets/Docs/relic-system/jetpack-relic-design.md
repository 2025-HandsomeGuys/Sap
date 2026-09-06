# 제트팩(Jetpack) 유물 설계

`adding-a-relic.md` 가이드 기반. RelicID `Jetpack = 4020` (4019 MinerDrone 다음).

## 1. 정체성

드릴을 든 채로는 파는 대신 **난다**. 드릴 도구를 선택하면 드릴 파기가 비활성화되고,
점프키를 홀드해 드릴 배터리를 연료로 소모하며 상승한다(오버워치 파라식 홀드 추진).

- 타입: **패시브** (상시 `OnUpdate` 감시). 단, **드릴 도구 선택 시에만** 활성.
- 조작: **홀드 추진** — 점프키 홀드 시 상승, 떼면 낙하. 연료 0이면 추진 정지.
- 연료: **드릴 배터리 공유** — `ctx.mining.RefillBattery(음수)` 드레인, `GetDrillBatteryRatio()` 잔량 판정.
- 레벨 스케일(둘 다): `ascendSpeedPerLevel`(상승속도↑) + `drainPerLevel`(초당 소모↓).
- `maxLevel = 3`.

## 2. 동작 흐름 (`OnUpdate`, 패시브)

매 프레임:

1. **가드**: `UIStateManager` 열림/로딩 중이면 추진 off + suppress 해제 후 return.
2. **드릴 모드 판정**: `ctx.tools.IsDrillMode`.
   - **드릴 모드 아님** → suppress 해제(`SuppressMining=false`) + 추진 off(`SetJetpackThrust(false, 0)`) + return.
     (공중에서 드릴→다른 도구로 전환 시 드릴/중력 정상 복귀 보장)
   - **드릴 모드** → `SuppressMining=true` (드릴 완전 정지).
3. **추진 입력**: `Input.GetButton("Jump")` && 배터리 잔량 > 0 && `!ctx.controller.IsWallClimbing`.
   - 추진 O: `ctx.controller.SetJetpackThrust(true, AscendSpeed())` + `ctx.mining.RefillBattery(-Drain() * Time.deltaTime)`.
   - 추진 X: `ctx.controller.SetJetpackThrust(false, 0)`.

**정리(cleanup) 지점**:
- 드릴 모드 이탈(2번) / UI 가드(1번) / `OnUnequip`에서 반드시 `SuppressMining=false` + `SetJetpackThrust(false, 0)`.

## 3. 필요 게임 코드 훅 (2개)

### 3-A. `PlayerMining.SuppressMining` (bool)
드릴을 완전 정지시키는 플래그. 제트팩이 on/off.

- 게이트: `_currentStrategy?.HandleUpdate()`(139행) / `HandleFixedUpdate()`(176행) / `HandleLateUpdate()`(210행)
  → `if (!SuppressMining) _currentStrategy?.HandleX();`
- `GetCurrentDigParameters`(372행): `if (SuppressMining) return new DigParameters { CanDig = false };`
- **`SwitchStrategy` 부기(149~168행)는 건드리지 않음** — 제트팩 off 시 올바른 전략 복귀 추적 유지.
- 효과: 파기·대시·에임·드릴 배터리 소모 전부 멈춤. 드릴 비주얼(스프라이트)은 유지(들고만 있음).

> 이름은 `SuppressMining`(범용) — suppress는 제트팩이 드릴 모드일 때만 켜므로 실질적으로 드릴만 정지.

### 3-B. `IPlayerController.SetJetpackThrust(bool active, float ascendSpeed)`
컨트롤러 이동/중력 업데이트에 통합. Update에서 세팅한 속도가 FixedUpdate에 덮이지 않게 컨트롤러가 소유.
**기존 `antiGravityOverride` 분기(HandleNormalMovement 343행~)와 동일 패턴**으로 얹는다.

`PlayerController` 구현:
- 필드: `bool _jetpackActive; float _jetpackAscend;`
- `SetJetpackThrust(a, s)`: `_jetpackActive = a; _jetpackAscend = s;`
- `HandleNormalMovement` 내부(gravity 리셋 `rb.gravityScale = defaultGravity`(339행) **직후**):
  - `bool jetpack = _jetpackActive;` (이 메서드는 `!isWallClimbing`일 때만 호출되므로[254~255행] 벽타기 가드는 구조적으로 이미 성립)
  - `jetpack`이면:
    - `rb.gravityScale = 0f;` (339행의 default 리셋을 이 프레임만 덮음)
    - 상승 캡 상향: `_superJumpCap = Mathf.Max(_superJumpCap, _jetpackAscend);` (jumpForce×1.2 캡[484행]에 안 깎이게) — velocity 확정 전에 세팅.
  - velocity 확정부: `jetpack`이면 `targetVelocity.y = _jetpackAscend;` (또는 `MoveTowards`로 부드럽게)
  - 스냅 차단: `canSnap` 조건(439행)에 `&& !_jetpackActive` 추가(이륙 시 바닥 밀착이 상승을 끌어내리지 않게).

> **중력 복원 불필요**: `HandleNormalMovement`가 매 프레임 339행에서 `gravityScale = defaultGravity`로 되돌리므로,
> `_jetpackActive`가 false가 되면 다음 FixedUpdate에서 자동 복원된다. 훅은 명시적 복원 코드가 필요 없다.
> **벽타기**: FixedUpdate가 `isWallClimbing`이면 `HandleWallClimbing()`만 호출[254~255행]하고 HandleNormalMovement를
> 아예 건너뛰므로, 추진은 벽타기 중 구조적으로 발동 불가(relic 측 `!IsWallClimbing` 가드는 이중 안전).
> **대시**: 제트팩 활성 시 suppress로 드릴 대시가 차단되고, 설령 `isDashing`이면 HandleNormalMovement가 333행에서
> 조기 return하므로 그 프레임 추진은 스킵(넉백 등 외부 대시가 우선). 충돌 없음.

## 4. 레벨 파라미터 (초기값)

```
ascendSpeedPerLevel = { 8f, 10f, 12f }    // 상승 목표 속도(<jumpForce×1.2 권장)
drainPerLevel       = { 2.0f, 1.6f, 1.3f } // 초당 연료 소모(레벨↑ = 효율↑)
```

`AscendSpeed()`/`Drain()`는 `level-1` 클램프 인덱싱(다른 유물과 동일 `Lv()` 패턴).

## 5. 의존성 (RelicContext)

| 의존 | 경로 |
|------|------|
| 드릴 모드 판정 | `ctx.tools.IsDrillMode` (ToolController) |
| 드릴 정지 | `ctx.mining.SuppressMining` (신규, 3-A) |
| 연료 드레인/잔량 | `ctx.mining.RefillBattery(-x)` / `ctx.mining.GetDrillBatteryRatio()` |
| 추진 | `ctx.controller.SetJetpackThrust(...)` (신규, 3-B) |
| 벽타기 가드 | `ctx.controller.IsWallClimbing` (기존) |

`RelicContext`에 `tools`·`mining`·`controller` 모두 이미 존재 → 컨텍스트 확장 불필요.

## 6. 수정 파일

- **신규**: `Assets/Scripts/Gameplay/Relics/Behaviours/JetpackRelic.cs`
- **게임 코드 훅**:
  - `UI/Player/IPlayerController.cs` — `SetJetpackThrust(bool, float)` 시그니처
  - `UI/Player/PlayerController.cs` — 구현 + FixedUpdate 통합
  - `UI/Player/PlayerMining.cs` — `SuppressMining` 플래그 + 3개 Handle 게이트 + DigParameters 가드
- **공유(순차 편집)**:
  - `Data/RelicID.cs` — `Jetpack = 4020`
  - `Editor/RelicSliceAssetGenerator.cs` — 등록 1줄 + `db.allRelics` 추가
  - `Gameplay/Relics/Debug/RelicDebugGranter.cs` — `KeypadMinus` grant+equip (Keypad0-9·KeypadPlus 소진, KeypadMinus 자유), slot 0

## 7. 엣지 케이스 (검증 반영)

| 케이스 | 처리 |
|--------|------|
| 상승속도 > jumpForce×1.2 캡 | 훅에서 `_superJumpCap` 상향 |
| 벽타기 중 추진 | `!IsWallClimbing` 가드로 무시 |
| 공중 비행 중 도구 전환 | OnUpdate 드릴모드 이탈 분기가 suppress 해제 + `SetJetpackThrust(false,0)` → 중력은 컨트롤러 339행이 자동 복원 |
| 유물 해제 중 비행 | `OnUnequip`에서 동일 클린업(suppress 해제 + 추진 off) |
| UI 열림/로딩 | 가드로 추진·소모 중단 |
| 연료 0 | 추진 조건 false → 낙하 |
| 지상 점프키 홀드 | 일반 점프로 이륙 → 공중 추진 지속(파라식, 허용) |
| 이단점프 유물 동시 장착 | 공중 점프 1회 중첩(경미, 허용) |

## 8. 검증 (사용자 인게임)

1. `Tools > Relic > Generate Slice Assets` 재실행 → `Jetpack.asset` + DB 갱신.
2. 지하에서 드릴 해금 후 드릴 선택 → 파기 불가 확인, 점프키 홀드 시 상승·연료 감소 확인.
3. 연료 0이면 낙하, 발전기 유물/리젠으로 재충전 후 재비행.
4. 비행 중 마우스휠로 곡괭이 전환 → 드릴 정상 복귀(파기 가능), 중력 정상.
5. 유물 해제(디버그) → 드릴 정상 복귀.
6. 레벨↑ → 상승속도·연료지속 증가 체감.
7. 벽타기 중 점프키 → 추진 안 걸리고 벽타기 정상.
