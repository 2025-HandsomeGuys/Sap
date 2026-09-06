# 스테미나 최대치 감소 시스템 — 구현 완료
@tags: stamina, max-reduction, system, implementation, StaminaManager

## 개요

삽·곡괭이 파기, 낙하 데미지 시 `MaxStamina`가 점진적으로 줄어들고,
지상 씬으로 이동하면 완전히 초기화된다.
`MaxStamina`가 0이 되면 기존 `GameOverHandler` 쓰러짐 시퀀스가 실행된다.

---

## MaxStamina 감소 구조

`StaminaManager`가 `IStatProvider`를 구현해 `PlayerStat`에 modifier를 주입하는 방식.
별도 필드 3개(`injury`, `diggingReduction` 등)의 합산이 `MaxStamina`에 Flat 감소로 반영된다.

```
AddDiggingReduction() / AddInjury()
  → diggingReduction / injury 누적
  → GetModifiers(): StatModifier(MaxStamina, Flat, -(injury + diggingReduction + ...))
  → PlayerStat.RecalculateAll()에서 MaxStamina 재계산
  → ClampCurrentStamina()로 currentStamina 상한 조정
  → currentStamina == 0 → OnStaminaDepleted → GameOverHandler
```

---

## 트리거별 감소 방식

| 원인 | 감소 필드 | 감소량 | 호출 위치 |
|---|---|---|---|
| 삽 지형 파기 | `diggingReduction` | `ShovelTerrainReductionPerCharge × 차징비율` | `SapStrategy.PerformSapDig()` |
| 곡괭이 돌 타격 | `diggingReduction` | 고정값 (`PickaxeRockMaxReduction`) | `PickaxeStrategy.PerformDig()` |
| 곡괭이 지형 파기 | `diggingReduction` | `PickaxeTerrainReductionPerRadius × staminaRadius` | `PickaxeStrategy.PerformDig()` (툴스왑 유물 시) |
| 낙하 데미지 | `injury` | `(낙하속도 - threshold) × multiplier × (1 - FallDamageReduce)` | `PlayerController.OnLanded()` |

감소량은 전부 `MiningStaminaTuning`(static)에서 읽는다. F8 진단 패널에서 런타임 조절 가능.

---

## ⚠ 삽 파기 감소가 오랫동안 동작하지 않았음 (2026-08-05 수정)

위 표의 '삽 파기' 행은 원래 `Digger.DigAt()` / `Digger.shovelReductionPerRadius`(기본 0.5)를 가리켰다.
그런데 **그 경로는 삽으로 도달할 수 없었다**:

- `Digger.Update()`가 `currentTool == 1`(삽)이면 즉시 `return` — 각 전략이 입력을 통제하도록 바뀌면서 생긴 가드
- 다리 역할이어야 할 `Digger.PerformShovelDig()`는 **호출자가 한 곳도 없었다** (인자가 `Vector2`라 애니메이션 이벤트로도 호출 불가)
- 실제 삽 파기는 `SapStrategy.PerformSapDig()`가 `chunk.ModifyTerrain()`을 직접 호출 — 여기엔 비용 지불 코드가 아예 없었다

결과: 삽으로 아무리 파도 MaxStamina가 줄지 않았고, **현재 스태미나도 전혀 소모되지 않았다**.
인스펙터에는 `Shovel Reduction Per Radius = 0.5`가 보여서 동작하는 것처럼 읽혔다.

**수정**: `SapStrategy.PerformSapDig()` 끝에 `PickaxeStrategy`와 대칭인 비용 블록을 추가하고,
죽은 `Digger.shovelReductionPerRadius` 필드는 제거했다. 감소량 0.5는 이 문서의 원래 설계값 그대로 복구.

**단, 현재 스태미나 소모(`ShovelTerrainCostMultiplier`)는 기본 0으로 뒀다** — 이 문서에도 없던 항목이라
새 비용을 임의로 켜지 않고 F8에서 잡기로 했다. 배율 기저값은 `tileData.json`의 층별
`maxStaminaReduction`(지표 0.1 → 최하층 2.0)이며, 곡괭이 돌 1타(2.0) 대비 20배 작다는 점에 주의.

`Digger.PerformShovelDig()`는 여전히 호출자 없는 죽은 메서드로 남아 있다. 파기 로직이
`Digger`와 `SapStrategy`에 중복된 구조라, 삽 경로에는 `Digger` 쪽에만 있는
불괴 타일 CircleCast 보호 · 유물 `TryOverrideTerrainDig` 훅 · 카메라 셰이크/파티클이 빠져 있을 수 있다(미확인).

---

## 벽타기 현재 스테미나 소모

MaxStamina 감소가 아닌 **현재 스테미나 소모** 방식.
`HandleWallClimbing()`에서 매 FixedUpdate마다 `StaminaCostPerSecond × Time.fixedDeltaTime` 차감.

---

## ⚠ 최대치가 깎인 뒤 소모가 바에 안 보이던 버그 (2026-08-09 수정)

증상: 최대치를 깎은 뒤 벽을 타면, **깎인 양만큼 소모될 때까지 바가 전혀 안 줄었다.**
소모 자체는 정상이었고 **표시만** 늦었다.

원인 두 개가 같은 증상을 만들었다.

**(1) `StaminaBar`가 감소분을 두 번 뺐다** — 주 원인.
`stat.MaxStamina`는 `StaminaManager`의 Flat modifier가 **이미 반영된 값**인데,
바는 그걸 `originalMax`(감소 전 최대치)로 보고 감소분을 한 번 더 뺐다.

```
기준 100, 채굴 감소 30 → MaxStamina 70, currentStamina 70(클램프됨)
바 계산: effectiveMax = 70 - 30 = 40 → current = Clamp(70, 0, 40) = 40
→ currentStamina가 70→40으로 떨어지는 동안 표시는 40에 고정
```

수정: 계산을 순수 로직 `StaminaBarLayout.Compute()`로 분리(EditMode 테스트 있음).
`MaxStamina`를 감소가 반영된 값으로 취급하고, 감소 전 최대치는 **더해서 복원**한다.
감소량은 Flat이라 `(Base + ΣFlat) × ΠPercent` 공식의 곱연산 배율까지 먹으므로,
그릴 때 `PlayerStat.GetPercentMultiplier(StatType.MaxStamina)`를 곱해 스케일을 맞춘다
(최대 스태미나 +10% 업그레이드가 있을 때 세그먼트 합이 안 맞던 문제).

**(2) 부상·화상·동상·방사선이 `ClampCurrentStamina()`를 호출하지 않았다.**
`AddDiggingReduction`만 호출하고 있었다. 그래서 낙하·함정 피해 뒤에는
`currentStamina`가 새 `MaxStamina` **위에 떠 있는 상태**가 되어, 그 초과분을 다 쓸 때까지
바가 안 움직였다(바를 고쳐도 남는 별개 원인). 리젠 루프도 `current < max`가 false라 안 돌았다.

수정: `AddInjury` / `AddBurn` / `AddFrostbite` / `AddRadiation`에 클램프 추가.
**동작 변화** — 상태이상만으로 `MaxStamina`가 0이 되면 이제 `OnStaminaDepleted`가 발화해
쓰러짐 시퀀스가 실행된다(원래 설계 의도, 그동안 안 걸렸음).

---

## ⚠ 현재 스태미나를 다 쓰면 게임오버가 되던 버그 (2026-08-11 수정)

증상: **최대치가 멀쩡히 남아 있는데** 벽타기·채굴로 현재 스태미나를 0까지 쓰면 쓰러짐 시퀀스가 돌았다.

원인은 `PlayerStat.UseStamina()`에 남아 있던 **구 시스템(현재 스태미나 = HP)의 발화 코드**다.

```csharp
// 수정 전 — 소모로 0이 되면 그대로 죽었다
currentStamina = Mathf.Max(0f, currentStamina - amount);
if (before > 0f && currentStamina <= 0f) OnStaminaDepleted?.Invoke();
```

`UseStamina`는 벽타기(`PlayerController`), 채굴 비용(`SapStrategy`/`PickaxeStrategy`/
`StaminaDigCostCalculator`), 위험물 피해(`ApplyHazardDamage`/`DamageZone`)가 **전부 공유하는 raw
메서드**다. MaxStamina 감소 시스템으로 넘어오면서 죽음 조건을 `ClampCurrentStamina`로 옮겼는데
이쪽 발화를 지우지 않아 두 조건이 공존했다.

**수정**
- `UseStamina`는 이제 깎기만 한다 — 발화 없음.
- 발화는 `ClampCurrentStamina` 한 곳, 조건은 **`MaxStamina <= 0`** 하나.
  판정 기준을 `currentStamina`에서 `max`로 바꿨다. 예전 조건은 `if (currentStamina > max)` **안에**
  있어서, 이미 지쳐 current가 0인 상태에서 최대치가 0까지 깎이면 발화 자체가 없었다(반대쪽 구멍).
- max가 0인 동안 `ClampCurrentStamina`가 여러 번 불려도 `_depletedFired` 래치로 **1회만** 발화.
  래치는 max가 0 위로 올라오면 풀린다 — `RecoverStatus`/`ResetDiggingReduction`도 그래서
  `ClampCurrentStamina()`를 부른다.
- 기준값(`GetBaseValue(MaxStamina)`)이 0인 초기화 전 상태는 쓰러짐으로 치지 않는다.

회귀 테스트: `Assets/Tests/EditMode/StaminaDepletionTests.cs`

**동작 변화 — 위험물이 부상(MaxStamina 감소)으로 바뀌었다.**
위 수정으로 `UseStamina`가 더는 죽이지 못하게 되자, 그 경로만 쓰던 위험물은 리젠으로 회복되는
무해한 존재가 됐다. 그래서 **같은 커밋에서 위험물 전체를 부상 경로로 옮겼다** — 낙하 데미지·던전
함정(`TrapDamage`)·블랙홀(`BlackHoleHandler`)·반중력(`AntiGravityHandler`)이 이미 쓰던 경로다.

| 위험물 | 경로 | 기본 피해 |
|---|---|---|
| 구르는 바위 `RollingRockEntity` / `TargetedRollingHoleEntity` | `ApplyHazardDamage` | 30 |
| 폭발 `ScrapExplosion` | `ApplyHazardDamage` | 30 |
| 지연 폭발 `DelayedBlast` | `ApplyHazardDamage` | 20 |
| 종유석 `StalactiteTrap` | `ApplyHazardDamage` | 20 |
| 지속 피해 존 `DamageZone` | 직접 `AddInjury` | 10/초 |

- `PlayerStat.ApplyHazardDamage()`가 `StaminaManager.AddInjury()`로 라우팅한다.
  `StaminaManager`는 다른 GameObject에 있을 수 있어 계층 탐색 후 `FindFirstObjectByType` 폴백
  (`Digger`/`PickaxeStrategy`/`PlayerController`와 같은 패턴). 못 찾으면 예전처럼 `UseStamina` 폴백.
- `DamageZone`은 `ApplyHazardDamage`를 타지 않는다 — 매 틱 i-frame/플래시가 걸려 DoT 성격이
  깨지기 때문. `OnTriggerEnter2D`에서 `StaminaManager`를 같이 캐싱해 `AddInjury`를 직접 부른다.

⚠ **밸런스**: 부상은 지상에 올라올 때까지 안 풀린다(`ExploreExitController`의 `RecoverStatus`).
기준 MaxStamina 100 기준으로 바위/폭발 **4~5대면 쓰러지고**, `DamageZone`은 **10초 접촉이면 쓰러진다**.
예전(현재 스태미나 차감 + 리젠)보다 훨씬 무겁다 — 수치는 `worldSettings.json`의
`specialChunks.*.staminaDamage`와 `DamageZone.damagePerSecond`에서 조절한다.

---

## 초기화 — 지상 씬 이동 시

`ExploreExitController.PrepareSettlement()` → `ExitToSurface()` 직전에 실행.

```csharp
var staminaManager = FindFirstObjectByType<StaminaManager>();
staminaManager?.ResetDiggingReduction(); // diggingReduction → 0
staminaManager?.RecoverStatus(float.MaxValue, 0f, 0f); // injury 전체 회복
staminaManager?.RefillStamina();         // currentStamina → MaxStamina
```

## 잠수 시작은 항상 만땅 — `PrepareUndergroundEntry` (2026-08-06)

`injury`/`diggingReduction`은 `StaminaManager`의 런타임 필드라 씬이 바뀌면 저절로 사라지지만,
**`currentStamina`는 `PlayerData`로 저장돼 그대로 따라온다.** 그래서 최대치가 깎인 채 지상에 올라오면
지상에서 최대치는 복구되는데 현재치는 낮게 남고, 다시 내려가면 리젠으로 서서히 차오르는 게 보였다.

지상 복귀 경로가 여러 개(정상 귀환 `ExploreExitController` / 사망 `GameOverHandler` /
긴급 탈출 `PauseOverlayUI`)라 복귀 쪽을 전부 손대는 대신 **내려가는 한 곳**에서 확정한다.

```csharp
// SaveManager.PrepareUndergroundEntry() — Save() 앞
if (stats != null) stats.CurrentStamina = stats.MaxStamina;
```

`PrepareUndergroundEntry()`는 지상→지하 진입 4경로(엘리베이터 `ElevatorEntryUI`, 땅굴
`TravelBehaviours`, `PortalController`, `SceneTransitionTrigger`)가 전부 경유한다.
**던전 복귀(`DungeonExitTrigger`)는 이 함수를 타지 않으므로** 던전을 들락거려 스태미나를
채우는 악용은 생기지 않는다. `Save()`보다 먼저 채워야 파일의 `currentStamina`도 최대치로 굳는다.

---

## 핵심 구현 포인트

### ClampCurrentStamina (PlayerStat.cs)

`MaxStamina`가 줄어도 `currentStamina` 필드는 자동으로 따라 내려가지 않는다.
`AddDiggingReduction` / `AddInjury` 호출 시 `MarkDirty()` 이후 `ClampCurrentStamina()`를 호출해
`currentStamina`를 새 `MaxStamina` 상한에 맞춘다.
`currentStamina`가 0이 되면 `OnStaminaDepleted` 이벤트 발화 → `GameOverHandler` 실행.

```csharp
public void ClampCurrentStamina()
{
    float max = MaxStamina;
    if (currentStamina > max)
    {
        float before = currentStamina;
        currentStamina = Mathf.Max(0f, max);
        if (before > 0f && currentStamina <= 0f)
            OnStaminaDepleted?.Invoke();
    }
}
```

### StaminaManager 캐싱 주의사항

`StaminaManager`가 `PlayerStat`과 **다른 GameObject**에 있을 수 있다.
`GetComponent<StaminaManager>()`만 쓰면 null이 되어 감소가 동작하지 않는다.
`Digger`, `PickaxeStrategy`, `PlayerController` 모두 아래 패턴 사용:

```csharp
// GetComponent 실패 시 씬 전체 탐색으로 폴백
_staminaManager = GetComponent<StaminaManager>() ?? FindFirstObjectByType<StaminaManager>();
// 또는 Digger처럼 처음부터 FindFirstObjectByType 사용
_staminaManager = FindFirstObjectByType<StaminaManager>();
```

### 낙하 데미지 — ApplyHazardDamage 미사용 이유

기존 `_playerStat.ApplyHazardDamage(damage)`는 `UseStamina()` (현재 스테미나 차감) 방식이었다.
injury 시스템으로 교체하면서 `ApplyHazardDamage` 대신 직접 처리:

```csharp
_staminaManager?.AddInjury(damage);   // MaxStamina 감소
HitFlashUI.Instance?.Flash(0.5f, 0.2f); // 피격 연출
_playerStat?.StartInvincibility();    // 무적 시간 (중복 판정 방지)
```

`PlayerStat.StartInvincibility()`는 이미 무적 상태면 중복 실행하지 않는다.

---

## 수치 밸런싱

채굴 관련 8개 값은 `MiningStaminaTuning`(static)에 모여 있고 **F8 진단 패널에서 즉시 조절**된다.
패널 상단에 `현재 깊이 지형 저항 × 반경 × 배율 = 스윙당 소모` 계산식과 마지막 실지불액이 표시된다.

| 파라미터 | 기본값 | 비고 |
|---|---|---|
| `ShovelTerrainReductionPerCharge` | `0.5` | 삽 지형 — MaxStamina 감소 (**차징 비율 기준**) |
| `ShovelRadiusMultiplier` | `1.0` | 삽 전용 파기 크기. **비용에 영향 없음** |
| `ShovelMinChargeRatio` | `0.25` | 미만이면 스윙 자체 취소(모션·파기·비용 전부 없음) |
| `ShovelTerrainCostMultiplier` | `0` | 삽 지형 — 현재 스태미나 (미정, 튜닝 중) |
| `ShovelRockCostMultiplier` / `ShovelRockMaxReduction` | `1` / `2.0` | 툴스왑 유물 전용 |
| `PickaxeRockMaxReduction` | `2.0` | `PlayerMining` 인스펙터가 세션 최초 1회 시드 |
| `PickaxeRockCostMultiplier` / `PickaxeTerrainCostMultiplier` | `1` / `1` | |
| `PickaxeTerrainReductionPerRadius` | `0.5` | 툴스왑 유물 전용 |

패널에서 잡은 값을 영구 반영하려면 `MiningStaminaTuning`의 **필드 선언과 `ResetStatics()` 양쪽**을 고쳐야 한다
(이 패널에는 PlayerSO 굽기 같은 저장 경로가 없다).

| 파라미터 | 컴포넌트 | 기본값 |
|---|---|---|
| `Fall Damage Threshold` | `PlayerController` | `15` |
| `Fall Damage Multiplier` | `PlayerController` | `2` |
