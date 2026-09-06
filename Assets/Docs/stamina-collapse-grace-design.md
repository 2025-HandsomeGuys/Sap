# 탈진 / 쓰러짐 유예 시스템 — 설계

@tags: stamina, exhaustion, collapse, game-over, grace-period, StaminaManager, PlayerStat

작성일: 2026-08-05
관련 문서: [stamina-max-reduction-plan.md](stamina-max-reduction-plan.md)

---

## 1. 배경 — 왜 바꾸는가

기존에는 `PlayerStat.OnStaminaDepleted` 하나가 두 가지 전혀 다른 사건을 함께 발화했다.

| 발화 지점 | 실제 의미 | 결과 |
|---|---|---|
| `UseStamina()` — 현재 스태미나 0 | 힘이 다 빠짐 | **즉사** |
| `ClampCurrentStamina()` — MaxStamina 0 | 부상 누적으로 쓰러짐 | 즉사 |

전자가 문제다. **파기·벽타기로 스태미나를 다 쓰기만 해도 죽는다.**
그리고 코드 어디에도 잔량 검사가 없어(`StaminaDigCostCalculator`, `PlayerController.HandleWallClimbing`)
**죽음이 현재 스태미나의 유일한 페널티**였다.

또한 MaxStamina가 0이 되는 순간 유예 없이 즉사라, 회복 아이템으로 되살릴 여지가 없었다.

---

## 2. 개념 분리

| | 의미 | 0이 되면 |
|---|---|---|
| `CurrentStamina` | 지금 쓸 수 있는 행동 자원 | **탈진** — 벽타기·파기 금지. 죽지 않음 |
| `MaxStamina` | 생존력 (부상·화상·동상·파기누적으로 감소) | **5초 유예 → 쓰러짐(게임오버)** |

유예 5초가 의미를 갖는 근거: MaxStamina를 인게임에서 되살리는 경로가 실제로 존재한다.
- 회복 아이템 — `ItemActiveEffectManager` → `StaminaManager.RecoverStatus()`
- 지상 복귀 — `ExploreExitController.PrepareSettlement()` (전체 회복)

즉 "쓰러지기 직전 5초 안에 회복 아이템을 쓰면 산다"가 성립한다.

---

## 3. 사망 트리거 이전

`PlayerStat`에 이벤트를 하나 추가하고 `GameOverHandler`의 구독 대상을 바꾼다.

```
PlayerStat.OnStaminaDepleted  — 현재 스태미나 0. "탈진 진입" 신호 (사망 아님)
PlayerStat.OnCollapse         — MaxStamina 0이 유예시간 내내 지속됨. 사망
```

- `GameOverHandler.OnEnable/OnDisable`: `OnStaminaDepleted` → `OnCollapse` (한 줄씩)
- `StaminaManager.OnStaminaDepletedForTelemetry`는 그대로 `OnStaminaDepleted` 구독 유지
  (스태미나 소진 지점 텔레메트리는 여전히 "다 썼다" 시점이 맞다)
- `OnCollapse`는 `StaminaManager`가 타이머를 다 채웠을 때 `PlayerStat`을 통해 발화한다.
  이벤트를 `PlayerStat`에 두는 이유: `GameOverHandler`는 `PlayerStat` 기준으로 자동 부착되므로
  ([GameOverHandler.TryAutoAttach](../Scripts/Gameplay/GameOverHandler.cs)) 추가 참조 탐색이 필요 없다.

---

## 4. 유예 타이머 — `StaminaManager.Update` 폴링

```csharp
// 인스펙터
public float collapseGraceSeconds = 5f;

// 상태
private float _collapseTimer;
private bool  _collapseFired;

// Update
if (playerStats.MaxStamina <= 0f)
{
    _collapseTimer += Time.deltaTime;
    if (!_collapseFired && _collapseTimer >= collapseGraceSeconds)
    {
        _collapseFired = true;
        playerStats.RaiseCollapse();   // → GameOverHandler.TriggerGameOver
    }
}
else
{
    _collapseTimer = 0f;   // 회복되면 리셋 — 아이템으로 생존
}
```

공개 프로퍼티: `IsCollapsing`(타이머 진행 중), `CollapseRemaining`(남은 초) — UI·사운드가 폴링한다.

### 왜 이벤트가 아니라 상태 폴링인가

MaxStamina가 0이 되는 경로가 여럿이다 —
`AddInjury` / `AddBurn` / `AddFrostbite` / `AddDiggingReduction` / `SetBaseValue` / `FromData`.
이 중 `ClampCurrentStamina()`(=발화 지점)를 부르는 건 `AddDiggingReduction` **하나뿐**이다.
설계 문서에는 `AddInjury`도 부르게 되어 있었지만 코드에 빠져 있었다(§7 참조).

발화 시점에 의존하면 이런 누락 하나가 그대로 "안 죽는 버그"가 된다.
상태를 폴링하면 어느 경로로 0이 되든 반드시 잡힌다.

비용은 무시할 수 있다 — `MaxStamina`는 dirty일 때만 재계산되는 캐시 조회이고,
`StaminaManager.Update`는 이미 매 프레임 `CurrentStamina`를 읽고 있다.

### 발화 후에는 되돌리지 않는다

`_collapseFired`는 리셋하지 않는다. 게임오버 연출이 이미 시작됐기 때문.
`GameOverHandler.TriggerGameOver`에도 `_isGameOver` 가드가 있어 이중 진입은 없다.

---

## 5. 탈진 — 현재 스태미나 0

### 5.1 히스테리시스 (필수)

회복 속도가 20/s라 0을 찍자마자 다음 프레임에 탈진이 풀리고, 다시 소모해 0이 되는
**깜빡임(핑퐁)** 이 발생한다. 해제 임계치를 따로 둔다.

```
진입: CurrentStamina <= 0
해제: CurrentStamina >= MaxStamina × exhaustRecoverRatio   (기본 0.15, 인스펙터)
```

`PlayerStat.IsExhausted`로 공개한다.

### 5.2 벽타기

`PlayerController`:
- **진입 차단** — 탈진 중 Shift 토글 무시 ([PlayerController.cs:207](../Scripts/UI/Player/PlayerController.cs) 부근)
- **강제 해제** — `HandleWallClimbing()`에서 소모 후 탈진이면 `isWallClimbing = false`,
  `ClimbExit` 트리거, 중력 복구

사다리도 동일하게 적용한다. 현재 소모 로직이 벽/사다리를 구분하지 않고
`HandleWallClimbing()` 끝에서 무조건 차감하기 때문이다. 구분이 필요하면 별도 기획 결정 사항.

### 5.3 파기 — 단일 choke point

`PlayerMining.GetCurrentDigParameters()`에서 `CanDig = false`로 막는다.
이 메서드는 코드 주석에도 명시된 **모든 채굴 스윙(삽·곡괭이·드릴)의 단일 choke point**라
여기 한 줄이면 전 경로가 덮인다. 호출부(`Digger`, `PickaxeStrategy`)는 이미 `CanDig`를 존중한다.

```csharp
var p = _currentStrategy.GetDigParameters(baseRadius, targetTileType);

if (_relicManager != null && p.CanDig)
    _relicManager.ApplyDigModifiers(ref p);

// 탈진 — 스태미나를 쓰는 파기만 막는다.
// 유물 후처리 뒤에 두는 이유: IgnoreStaminaCost(드릴 — 배터리로 판다)가 확정된 다음이어야
// 드릴이 탈진에 걸리지 않는다.
if (p.CanDig && !p.IgnoreStaminaCost && playerStats != null && playerStats.IsExhausted)
    p.CanDig = false;

return p;
```

**드릴은 예외** — `DrillStrategy`가 `IgnoreStaminaCost = true`를 세팅한다(배터리 소모).
탈진해도 드릴은 계속 돌아간다.

`StaminaDigCostCalculator.PayCost`는 건드리지 않는다.
`CanDig` 게이트를 통과한 파기만 여기 도달하므로 이중 방어는 불필요하고,
반환값을 버리는 호출부 3곳([Digger.cs:241](../Scripts/Gameplay/Terrain/Tiles/Digger.cs),
[290](../Scripts/Gameplay/Terrain/Tiles/Digger.cs),
[PickaxeStrategy.cs:288](../Scripts/UI/Player/Strategies/PickaxeStrategy.cs))을 함께 고쳐야 해서
변경 범위만 커진다.

곡괭이는 여기서 막히면 **스윙 모션까지 나간 뒤 아무것도 안 파이는** 상태가 되므로
([PickaxeStrategy.PerformDig](../Scripts/UI/Player/Strategies/PickaxeStrategy.cs)가 `CanDig` 검사보다
먼저 `RaiseDigSwing`·`DigSwing` 사운드를 낸다), `HandleUpdate`의 공격 입력 처리에서도
탈진이면 `ExecuteAttack`을 건너뛴다.

### 5.4 피드백

- 탈진 **진입 시** SFX 1회. `SfxKeys`에 키 추가 후 `SoundManager.PlaySFX` (CLAUDE.md §14 경로 준수)
- 탈진 중 파기·벽타기 시도 시엔 소리를 내지 않는다 (연타 시 소음)

---

## 6. 심장음 — 유예 중 최고 피치

[HeartbeatSfx.cs:53](../Scripts/Audio/HeartbeatSfx.cs)는 `max <= 0f`에서 early return이라
MaxStamina가 0이 되는 순간 피치가 마지막 값에 멎는다(루프는 계속 재생됨).

유예 중(`IsCollapsing`)에는 `pitchAtZero`로 고정한다. 5초 유예의 긴박함을 소리로 알리는 유일한 신호.

---

## 7. 함께 고치는 기존 버그

이 설계가 들어가면 `currentStamina`가 0이 되어도 죽지 않으므로, 아래 수정을 안전하게 넣을 수 있다.
(원래는 "클램프를 넣으면 그 자리에서 즉사한다"는 이유로 못 넣던 것들이다.)

### 7.1 `ClampCurrentStamina()` 누락

`MaxStamina`가 줄어도 `currentStamina`가 따라 내려가지 않아 **current > max** 상태가 남는다.
`StaminaBarUI`가 표시값을 `effectiveMax`로 클램프하기 때문에
**바가 100%에 고정된 채 멈춰 보이고, 실제로는 줄고 있는데 안 줄어드는 것처럼 보인다.**
(벽타기 중 스태미나가 안 준다는 최초 증상의 원인이 이것)

추가할 곳:
- `StaminaManager.AddInjury` / `AddBurn` / `AddFrostbite`
  — [stamina-max-reduction-plan.md §핵심 구현 포인트](stamina-max-reduction-plan.md)에 원래 하기로 되어 있었으나 코드에 빠져 있었음
- `PlayerStat.SetBaseValue` — `StatType.MaxStamina`일 때만 (F8 진단 패널 슬라이더 경로)
- `PlayerStat.FromData` — 세이브 로드 직후

### 7.2 `StaminaBarUI` 이중 차감

```csharp
float originalMax = stat.MaxStamina;          // 이미 -totalReduction이 반영된 최종값
...
float effectiveMax = originalMax - totalReduction;   // 또 뺀다
```

`StaminaManager.GetModifiers()`가 `MaxStamina`에 Flat `-totalReduction`을 주입하므로
`stat.MaxStamina`는 이미 감소가 반영된 값이다. 여기서 또 빼면 부상이 있을 때
사용 가능치가 실제보다 작게 표시된다.

수정: `stat.GetBaseValue(StatType.MaxStamina)` 사용.
같은 "감소 전 최대치"를 `StaminaManager`의 동상 오버레이 호출부는 이미 `GetBaseValue`로 가져오고 있다.

---

## 8. 유예 중 회복 아이템 사용 — 확인 완료

`UIStateManager`에는 인벤토리 열기를 막는 스태미나 조건이 없고,
Tab 인벤토리는 `Time.timeScale`을 건드리지 않는다(Pause·WorldMap 오버레이만 0으로 만든다).

따라서 **유예 5초는 인벤토리를 여는 동안에도 계속 흐른다** — 의도한 긴박함이 그대로 성립한다.
타이머가 `Time.deltaTime`을 쓰는 것도 이 때문이다(unscaled를 쓰면 Pause 중에도 죽는다).

한편 파기는 UI가 열려 있으면 이미 전면 차단된다
(`GetCurrentDigParameters`의 `CurrentState != UIState.None` 검사) — 탈진 게이트와 충돌하지 않는다.

---

## 9. 변경 요약

| 파일 | 변경 |
|---|---|
| `PlayerStat.cs` | `OnCollapse` 이벤트 + `RaiseCollapse()`, `IsExhausted`, `SetBaseValue`/`FromData` 클램프 |
| `StaminaManager.cs` | 유예 타이머(`collapseGraceSeconds`, `IsCollapsing`, `CollapseRemaining`), `AddInjury`/`AddBurn`/`AddFrostbite` 클램프 |
| `GameOverHandler.cs` | 구독 대상 `OnStaminaDepleted` → `OnCollapse` |
| `PlayerController.cs` | 탈진 시 벽타기 진입 차단 + 강제 해제 |
| `PlayerMining.cs` | `GetCurrentDigParameters`에 탈진 게이트 (드릴 제외) |
| `PickaxeStrategy.cs` | 탈진 시 스윙 자체를 건너뜀 (헛스윙 방지) |
| `StaminaVitals.cs` (신규) | 순수 로직 — 탈진 히스테리시스 + 유예 타이머 |
| `StaminaBarUI.cs` | 이중 차감 제거 |
| `HeartbeatSfx.cs` | 유예 중 최고 피치 고정 |
| `SfxKeys.cs` | 탈진 진입 SFX 키 추가 |
