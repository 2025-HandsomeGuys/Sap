# T0 업그레이드 트리 확장 + 삽 스태미나 배선

작성일: 2026-08-11
범위: T0(첫 번째 땅) 트리 재구성 10노드 → 15노드, `ShovelStaminaReduce` 효과 배선
선행 문서: [economy/mineral-price-design.md](../../economy/mineral-price-design.md) — 예산(§7.1)·톱니 곡선(§3.2)·자리 교체 원칙(§6.1)이 전부 여기서 온다. 이 문서만 보고 작업하면 예산이 어긋난다.

---

## 1. 문제

T0 트리에 곡괭이 계열이 앞에 몰려 있다. 루트 `MiningSpeed_T0_01`("채굴 기본기 I")과 그 자식 `MiningPower_T0_01`("곡괭이 연마 I")이 **둘 다 `MiningSpeedMultiplier 1.1`로 완전히 같은 효과**이고, 설명문도 둘 다 "곡괭이질 속도가 10% 증가합니다"다.

그런데 1층 초반에는 바위가 거의 나오지 않고, 나와도 캘 스태미나가 없다. 곡괭이 강화가 가장 먼저 놓여 있는데 그 시점에 쓸 데가 없다.

부수 문제 둘:

- **T0 10노드 중 7개가 면허 I 필수 경로다.** 실질 선택지가 3개뿐이라 트리가 얇게 느껴진다.
- 삽 강화의 재료인 `UpgradeEffectType.ShovelStaminaReduce`가 **배선되어 있지 않다.** T1의 `ShovelStamina_T1_01`(7,200G)은 사도 아무 일도 일어나지 않는다.

---

## 2. 착수 시점의 현황 (측정값)

### 2.1 살아있는 효과 / 죽은 효과

`UpgradeEffectType` 27종 중 게임플레이 소비처가 확인되는 것은 **13종**이다.

| 효과 | 소비처 |
|---|---|
| `MiningSpeedMultiplier` | `SapStrategy.cs:170` (삽 차징 속도), `PlayerStat.cs:303` |
| `MiningRangeMultiplier` | `SapStrategy.cs:400` (삽 파기 반경) |
| `MiningCooldownMultiplier` | `Digger.cs:127` |
| `ToolRange` | `SapStrategy.cs:401` (삽 파기 반경) |
| `PickaxeDamageUp` | `SapStrategy.cs:320` (돌 타격 피해) |
| `StaminaCostMultiplier` | `StaminaDigCostCalculator.cs:43` — **곡괭이만**(삽은 §3.1 참고) |
| `FallDamageReduce` | `PlayerController.cs:395`, `AntiGravityHandler.cs:239` |
| `VisionRadiusUp` | `PlayerVisionOverlay.cs:321` |
| `InventoryWeightUp` | `EncumbranceController.cs:109` |
| `MaxStaminaUp` | `StatType.MaxStamina` 직결 |
| `MoveSpeedMultiplier` | `PlayerStat.MoveSpeed` |
| `ClimbSpeedMultiplier` / `WallClimbSpeed` | `PlayerStat.WallClimbSpeed` |
| `MiningLevel` | `SapStrategy.cs:390` 티어 게이트 |

나머지 **12종은 죽어 있다.** 선행 문서 §10.1이 3종(`InventorySlotUp`·`WarehouseCapacityUp`·`MineralSellBonus`)을 이미 기록했고, 이번 조사에서 9종을 추가로 확인했다.

| 효과 | 상태 |
|---|---|
| `ShovelStaminaReduce` | 읽는 코드 없음. **이번 작업에서 배선한다** |
| `MineralExtraDropChance` | `PickupableItem.cs:242`가 `GetStatValue(type, 0f)`로 읽는데, 곱연산이라 `0 × 배율 = 항상 0`. 읽는 코드는 있지만 값이 안 온다 |
| `DrillBatteryCapacity` / `DrillBatteryRegen` | 소비처 파일 없음 |
| `FlashlightRangeUp` | `FlashlightController`가 읽지 않음 |
| `ToolChargeTimeReduce` | 아이템이 값을 넣기만 하고(`ItemActiveEffectManager.cs:217`) 읽는 곳이 없음 |
| `RareMineralChance` | 위와 같음 (`:218`) |
| `EnvironmentResistance` | 위와 같음 (`:216`). 해저드는 `HazardFrostResist`/`HazardBurnResist`를 쓴다 |
| `CritChanceUp` | 소비처 없음 |
| `ConsumableSlotUnlock` | 소비처 없음 |

티어별 분포:

| 티어 | 노드 | 죽은 노드 |
|---|---|---|
| T0 | 10 | 1 (창고 확장 I) |
| T1 | 15 | 8 |
| T2 | 10 | 5 |

**T0은 거의 멀쩡하다.** T1 이후가 심각하지만 이번 작업 범위가 아니다 — §7에 기록만 남긴다.

### 2.2 예산

선행 문서 §7.1: 1층 예산 22,900G(16회 × 1,430G) 중 **T0 트리 몫 14,900G**, 그중 면허 I은 3,500G 고정.

현재 면허 I 최소 경로 비용:

```
MiningSpeed(700) → MiningPower(1100) → PickaxeDamage(1800)
MoveSpeed(1100) → MaxStamina(1400)
WarehouseCapacity(1100) → VisionRadius(1400)
면허 I(3500)
= 12,100G
```

**선택 노드에 쓸 수 있는 돈은 14,900 − 12,100 = 2,800G뿐이다.** 장비 6,000G까지 더하면 1층 예산의 79%가 필수 지출이다.

---

## 3. 설계의 뼈대

### 3.1 삽의 비용은 '최대 스태미나 감소' 하나뿐이다

삽으로 지형을 팔 때 비용이 나가는 경로는 둘인데, 하나는 이미 0이다.

| 비용 | 코드 | 현재 |
|---|---|---|
| 현재 스태미나 | `PayCost` → `MiningStaminaTuning.ShovelTerrainCostMultiplier` | **0배 = 공짜** (`MiningStaminaTuning.cs:32`) |
| **최대 스태미나 감소** | `ShovelTerrainReductionPerCharge(0.5) × 차징비율` (`SapStrategy.cs:348`) | 유일한 실질 비용 |

따라서 `ShovelStaminaReduce`를 현재 스태미나 쪽(`StaminaCostMultiplier`와 같은 자리)에 걸면 `0 × 0.8 = 0`이라 또 무효가 된다. **`SapStrategy.cs:348`의 최대치 감소에 곱하는 것이 유일하게 의미 있는 지점이다.**

돌 타격(`ShovelRockMaxReduction`)에는 **적용하지 않는다.** 노드 이름이 "삽질"이고, 툴스왑 유물로 삽이 돌을 캘 때 곡괭이보다 싸지는 역전이 생긴다.

### 3.2 기본값이 없으면 배선해도 안 산다

`StatBaseValueTable.Get`은 없는 키에 `0f`를 돌려주고(`StatBaseValueTable.cs:45`), `PlayerStat.SetDefaultMultiplicativeBaseValues()`에 `ShovelStaminaReduce`가 빠져 있다. 최종값 공식이 `(Base + ΣFlat) × ΠPercent`라 **0에 무엇을 곱해도 0**이다.

읽는 코드를 붙이는 것만으로는 살아나지 않는다. 기본값 `1f`를 함께 심어야 한다.

### 3.3 곱연산 값은 '곱할 배율'이다

이 프로젝트의 `isPercentage: true`는 **곱할 배율**을 뜻한다(`UpgradeManager.GetStatValue`의 `multiplier *= node.effect.value`). `StaminaCost_T1_01`이 "10% 감소"에 `0.9f`를 쓰는 것이 그 예다.

현재 `ShovelStamina_T1_01`의 값은 `0.2f`인데 설명문은 "20% 감소"다. 규약대로면 **80% 감소**가 된다. 지금까지 죽어 있어서 드러나지 않았던 데이터 버그다. **`0.8`이 맞다.**

### 3.4 확장 방향 — 곁가지가 아니라 필수 경로를 늘린다

"선택지를 늘린다"는 §2.2의 예산이 허락하지 않는다. 곁가지에 쓸 수 있는 돈이 2,800G뿐이라 곁가지를 많이 붙이면 하나하나가 무의미해진다.

대신 **총액과 최소 경로 비용을 그대로 둔 채 필수 노드 수만 늘린다.** 노드당 가격이 내려가므로 면허 I 도달 시점(16회 = 16일차)과 §7.1 예산이 둘 다 보존된다.

| | 현재 | 변경 후 |
|---|---|---|
| T0 노드 수 | 10 | **15** |
| 필수 경로 노드 수 | 7 | **10** |
| 최소 경로 비용 | 12,100G | **11,900G** (−200G, §4.3) |
| T0 총액 | 14,900G | **14,900G** (동일) |
| 구매 리듬 | 2.3회 탐험당 1개 | **1.6회당 1개** |

### 3.5 같은 효과를 반으로 쪼개지 않는다

노드를 늘릴 때 "10% 증가"를 "5% 두 개"로 나누면 개별 체감이 사라져 사고도 뭐가 달라졌는지 모른다. 현재 루트와 `MiningPower_T0_01`이 정확히 그 상태다.

늘어나는 노드는 전부 **서로 다른 종류의 효과**로 채운다. §2.1의 살아있는 13종이 그 상한이다.

### 3.6 행을 테마로 묶는다 — 삽 → 스태미나 → 곡괭이

노드 순서를 "그 시점에 실제로 쓸 수 있는 것"에 맞춘다.

| 행 | 테마 | 이유 |
|---|---|---|
| 루트 | 도구 공용 | 삽 차징·곡괭이 양쪽에 걸린다 |
| 2행 | **삽** | 초반 유일한 행동이 지형 파기다 |
| 3행 | **스태미나** | 더 오래 팔 수 있게 된다 |
| 4행 | **곡괭이** | 이때쯤 바위가 나오고, 스태미나 노드를 이미 세 개 샀다 |
| 5행 | 면허 I | |

**세로 갈래가 아니라 가로 행을 테마로 묶는다.** 갈래별로 나누면(삽 갈래 / 이동 갈래 / 탐색 갈래) 곡괭이를 4행에 놓아도 플레이어는 갈래를 따라 세로로 사기 때문에 "삽 → 스태미나 → 곡괭이" 순서가 체감되지 않는다.

이동·시야·낙하·벽타기 계열은 이 순서에 끼지 않으므로 **전부 곁가지로 뺀다.** 살아있는 효과 13종이 필수 10 + 곁가지 5에 정확히 들어맞는다.

### 3.7 트리는 아래에서 위로 자란다

`uiPosition`의 **y가 클수록 나중 단계**다. 루트가 가장 아래(y = −350), 면허 I이 가장 위(y = +250), 그 위로 T1(y = 500~950) · T2(y = 1200~1500)가 이어진다.

이 문서의 구조도도 **면허가 위, 루트가 아래**로 그린다. 반대로 그리면 곁가지가 면허 다음 단계처럼 읽힌다.

### 3.8 자리는 비우지 않고 효과만 교체한다

죽은 `WarehouseCapacity_T0_01`(창고 확장 I)을 제거한다. §10.1이 지적한 "지키지 못하는 약속"이 하나 줄어든다.

원래 이 노드는 선행 문서 §6.1이 배낭을 T1으로 옮기면서 **트리 구조를 유지하려고** T0 자리에 끼워 넣은 것이다(`VisionRadius_T0_01`의 선행이라 끊으면 면허 I 경로가 깨졌다). 이번 확장은 T0 전체를 다시 짜므로 그 제약이 사라진다 — 시야는 곁가지로 빠지고 창고 자리는 그냥 없어진다.

**배낭(`InventoryWeightUp`)은 T0으로 내리지 않는다.** §3.2 톱니 곡선이 깨지고 `UpgradeTreeCostTests.Backpack_MovedToTier1_AndCostsFiveThousand`가 잡는다.

---

## 4. 확정 트리 — T0 15노드

### 4.1 구조

**아래에서 위로 읽는다**(§3.7). 루트가 맨 아래, 면허 I이 맨 위.

```
                          심층 탐사 면허 I                        y=+400
                                 ▲
             ┌───────────────────┴───────────────────┐
      날카로운 곡괭이 I                       연속 채광 리듬 I     y=+250   ← 5행 곡괭이 숙련
             └───────────────────┬───────────────────┘
                                 ▲
                          ★ 곡괭이 입수 ★                        y=+100   ← 4행 도구 해금
                                 ▲
             ┌───────────────────┼───────────────────┐
       지구력 강화 I       효율적인 호흡 I       강인한 체력 I     y= −50   ← 3행 스태미나
             ▲                   ▲                   ▲
       가벼운 삽질 I         도구 숙련 I           팔 뻗기 I       y=−200   ← 2행 삽
             └───────────────────┼───────────────────┘
                                 ▲
                          넓은 삽날 I  [루트]                      y=−350


  곁가지 (면허 선행 아님)

    맑은 눈 I         y= −50          낙법 훈련 I    밀착 등반 I    y= −50 / +100
        ▲                                  ▲             ▲
  가벼운 발걸음 I     y=−200                └── 벽 기어오르기 I ──┘   y=−200
        └──────────────── 루트 ────────────────┘
```

**곡괭이 입수가 트리의 허리다.** 2·3행 세 갈래가 전부 여기로 모이고, 곡괭이 숙련 2개와 면허가 그 위로 이어진다. 루트부터 면허까지 **11노드가 모두 필수**다.

곁가지 5개(`가벼운 발걸음 I`, `맑은 눈 I`, `벽 기어오르기 I`, `낙법 훈련 I`, `밀착 등반 I`)는 이동·탐색 계열로, 삽→스태미나→곡괭이 순서에 끼지 않아 전부 빼냈다(§3.6).

### 4.2 노드 명세

`Assets/Scripts/Editor/UpgradeTreeGenerator.cs`의 `GetUpgradePlanData()` T0 블록을 아래로 교체한다.

**필수 경로 11노드**

| # | nodeId | 표시명 | 효과 | 값 | pct | 비용 | 선행 | uiPosition |
|---|---|---|---|---|---|---|---|---|
| 1 | `MiningRange_T0_01` | 넓은 삽날 I | `MiningRangeMultiplier` | 1.12 | ✓ | 60 | — | (0, -350) |
| 2 | `ShovelStamina_T0_01` | 가벼운 삽질 I | `ShovelStaminaReduce` | 0.8 | ✓ | 130 | 1 | (-350, -200) |
| 3 | `MiningSpeed_T0_01` | 도구 숙련 I | `MiningSpeedMultiplier` | 1.1 | ✓ | 160 | 1 | (0, -200) |
| 4 | `ToolRange_T0_01` | 팔 뻗기 I | `ToolRange` | 1.1 | ✓ | 200 | 1 | (350, -200) |
| 5 | `MaxStamina_T0_01` | 지구력 강화 I | `MaxStaminaUp` | 10 | — | 230 | 2 | (-350, -50) |
| 6 | `StaminaCost_T0_01` | 효율적인 호흡 I | `StaminaCostMultiplier` | 0.92 | ✓ | 260 | 3 | (0, -50) |
| 7 | `MaxStaminaMultiplier_T0_01` | 강인한 체력 I | `MaxStaminaUp` | 1.1 | ✓ | 300 | 4 | (350, -50) |
| 8 | `PickaxeUnlock_T0_01` | 곡괭이 입수 | `None` | — | — | 330 | 5, 6, 7 | (0, 100) |
| 9 | `PickaxeDamage_T0_01` | 날카로운 곡괭이 I | `PickaxeDamageUp` | 5 | — | 370 | 8 | (-350, 250) |
| 10 | `MiningCooldown_T0_01` | 연속 채광 리듬 I | `MiningCooldownMultiplier` | 0.93 | ✓ | 400 | 8 | (350, 250) |
| 11 | `MiningLevel_T0_Final` | 심층 탐사 면허 I | `MiningLevel` | 1 | — | 700 | 9, 10 | (0, 400) |

**⚠ 1번과 3번을 맞바꿨다 (2026-08-13)**

원래 루트는 `도구 숙련 I`(휘두르는 속도 +10%)이었다. 그런데 **속도는 회당 가져오는 돈을 안 바꾼다** —
한 번에 파내는 양이 그대로라 다이브 수입이 제자리다. 첫 구매가 수입에 안 걸리면 "업그레이드가 곧 수입"인
T0 곡선(§2.5)의 첫 칸이 헛돈다. 그래서 **첫 노드를 삽 크기(`넓은 삽날 I`)로 바꿨다** — 파는 반경이
넓어지면 한 번에 캐는 광물이 바로 늘고, 삽 비용은 반경이 아니라 차징에만 걸려서 스태미나 부담도 안 는다.

**가격은 자리에 그대로 두고 노드만 옮겼다**(루트 60 / 2행 중앙 160). 값을 같이 옮기면
`UpgradeTreeCostTests`의 총액·최소경로·구매리듬이 전부 어긋난다. 선행도 자리 기준으로 재배선했다 —
2·4번과 곁가지 12·14번의 선행은 새 루트(`MiningRange_T0_01`), 6번의 선행은 2행으로 내려온
`MiningSpeed_T0_01`이다.

**곁가지 5노드** — 이동·탐색 계열, 면허 선행 아님

| # | nodeId | 표시명 | 효과 | 값 | pct | 비용 | 선행 | uiPosition |
|---|---|---|---|---|---|---|---|---|
| 12 | `MoveSpeed_T0_01` | 가벼운 발걸음 I | `MoveSpeedMultiplier` | 1.1 | ✓ | 130 | 1 | (-700, -200) |
| 13 | `VisionRadius_T0_01` | 맑은 눈 I | `VisionRadiusUp` | 1.15 | ✓ | 180 | 12 | (-700, -50) |
| 14 | `ClimbSpeed_T0_01` | 벽 기어오르기 I | `ClimbSpeedMultiplier` | 1.15 | ✓ | 130 | 1 | (700, -200) |
| 15 | `FallDamage_T0_01` | 낙법 훈련 I | `FallDamageReduce` | 0.85 | ✓ | 180 | 14 | (700, -50) |
| 16 | `WallClimbSpeed_T0_01` | 밀착 등반 I | `WallClimbSpeed` | 1 | — | 180 | 14 | (700, 100) |

**⚠ 15·16번은 형제인데 같은 열에 쌓여 있다 (2026-08-13 추가)**

둘 다 선행이 14번(벽 기어오르기 I)인데 x가 700으로 같아 세로로 겹쳐 놓였다. `OrthogonalUILineRenderer`는 x가 같으면 직선 수직선을 그으므로 **14 → 16 선이 15번 노드를 정확히 관통했고**, 플레이어가 이걸 3단 체인으로 읽고 "낙법 훈련 I을 안 샀는데 밀착 등반 I이 사진다"를 버그로 신고했다. `UpgradeManager.CanUnlock`의 선행 검사는 처음부터 정상이었다 — 보이는 모양만 거짓말을 했다.

좌표로 풀면(16번을 (1050, -50)으로 이동) 콘텐츠 폭이 2,432 → 3,482px(+43%)가 되고, 폭이 `maxAbsX` 기준 좌우 대칭으로 잡히는 탓에 왼쪽에 그만큼 빈 공간이 생긴다. 그래서 **좌표는 그대로 두고 선이 노드를 피해 가도록** 고쳤다(`OrthogonalUILineRenderer.BuildPath` — 기본 직선이 노드 사각형을 지나면 옆 차선으로 우회). 같은 관통이 트리 전체에 10건 있었고(T1 4건·T2 2건 포함) 전부 해소됐다.

새 노드를 넣을 때 형제를 같은 열에 쌓는 것 자체는 이제 허용된다. 다만 `UpgradeTreeLineRoutingTests`가 관통을 검사하므로, 우회로가 없을 만큼 빽빽하게 채우면 거기서 걸린다.

**16번 `밀착 등반 I`이 +1인 이유**

T1의 `WallClimbSpeed_T1_01`도 같은 효과 +2다. 600G짜리 T0 노드가 7,200G짜리 T1 노드와 같은 값을 주면 안 되므로 T0을 +1로 낮춘다. T1 쪽 가격이 §7.3의 일괄 배율(×13)에서 나온 값이라 효과 대비 비싼 것은 기존 설계에서 오는 문제이고, 이번 작업에서 건드리지 않는다.

**제거되는 노드 2개**

- `MiningPower_T0_01` — 루트와 완전히 같은 효과(§1). 삽 노드가 그 자리를 대신한다
- `WarehouseCapacity_T0_01` — 죽은 효과(§3.8)

### 4.3 예산 검산

**⚠ 2026-08-12 재산정.** 아래는 실측 회당 100G 기준이다. 최초 작성 시의 14,900G는 선행 문서가 전제한 회당 1,430G에서 나온 값인데, 실제 플레이에서 회당 100G로 측정됐다(선행 문서 §2.5-A). 첫 업그레이드가 800G면 아무것도 못 사고 8번을 다녀와야 한다.

```
루트                       60      (실측 기준으론 90이지만 60으로 지정)
2행 삽         130 160 200  =    490
3행 스태미나   230 260 300  =    790
4행 곡괭이 입수       330    =    330
5행 곡괭이 숙련   370 400    =    770
면허 I                    700
                              ───────
최소 경로                       3,140G

곁가지  130 130 180 180 180  =    800
                              ───────
총액                            3,940G
```

**사다리를 세운 원리 — 수입이 늘어나는 것을 반영한다**

업그레이드를 살수록 회당 수입이 오른다(스태미나↑ = 더 오래 팜, 삽·범위↑ = 더 빨리 팜). 가격이 평평하면 후반에 한 다이브로 두 개를 사게 된다.

측정된 기울기는 **노드당 +37.5G**다 — 0노드 100G, 4노드(삽·범위·스태미나) 250G. 처음 가정했던 +8G보다 **5배 가파르다.**

| 다이브 | 회당 수입 | 구매 | 잔액 | 다음 가격 |
|---|---|---|---|---|
| 1 | 100 | 60 (루트) | 40 | 130 |
| 2 | 138 | 130 (삽) | 48 | 160 |
| 3 | 175 | 160 (삽) | 62 | 200 |
| 4 | 212 | 200 (삽) | 75 | 230 |
| 5 | 250 | 230 (스태미나) | 95 | 260 |
| 6 | 288 | 260 (스태미나) | 122 | 300 |
| 7 | 325 | 300 (스태미나) | 148 | 330 |
| 8 | 362 | **330 (곡괭이 입수)** | 180 | 370 |
| 9 | 400 | 370 (곡괭이) | 210 | 400 |
| 10 | 438 | 400 (곡괭이) | 248 | 700 |
| 11 | 475 | 700 (면허) | 23 | — |

**10다이브 연속으로 정확히 하나씩 사지고, 두 번째는 항상 모자란다.** 면허는 11다이브째.

행 안에서 가격이 다른 것은 의도적이다. 같은 행의 형제 노드는 아무 순서로나 살 수 있으므로 플레이어는 자연히 싼 것부터 사고, 그 오름차순이 곧 위 사다리가 된다.

곁가지 130/180G는 "필수 노드를 못 사는 다이브에 대신 살 것"이다.

면허 700G는 총액의 18%다.

**T1 이후는 손대지 않았다.** 2층 회당 매출을 아직 측정하지 않았기 때문이다. 면허 I 도달 시점 회당 매출이 약 475G인데 T1 첫 노드가 5,200G라 **11다이브 절벽**이 남아 있다.

**재측정 후 다시 뽑으려면** — `python Tools/telemetry/fit_upgrade_prices.py --csv-dir <out> --nodes 9`

---

### 4.3-2 재산정 (2026-08-15) — 기울기를 절반으로 잡았다

**⚠ §4.3의 사다리는 폐기됐다.** 아래가 현재 값이다.

#### 무엇이 어긋났나

9일치 회차 로그에서 T0 19노드(3,940G)가 **9~10일차에 전부 소진됐다.** 1층 깊이 200m 중 45m(22%)까지밖에 안 판 시점이다. 판 픽셀로 보면 1,881,265px ≈ 1.9청크로, 1층 약 1,000청크의 **0.19%**다.

원인은 §4.3의 `growth`다. 노드 수에 대한 회당 매출을 다시 적합하니:

| 보유 노드 | 2 | 3 | 4 | 7 | 9 | 11 | 13 |
|---|---|---|---|---|---|---|---|
| 회당 매출 | 151 | 200 | 385 | 505 | 760 | 688 | 873 |

최소제곱 → **base 60 / growth 64.** §4.3이 쓴 +37.5G의 1.7배다. 가격이 수입을 못 따라가 §4.3 표의 "매 다이브 하나씩"이 무너지고 **5·6·7·8일차 전부 하루 두 개씩** 사졌다.

#### 표본에서 뺀 것 — 긴급 탈출

9일차 첫 다이브는 `emergency_escape`로 광물 42개 중 25개(60%)를 잃었다. 이 표본을 포함하면 base 141 / growth 46으로 갈린다. 뺀 이유는 그 다이브가 **애초에 일어나면 안 되는 것**이었기 때문이다.

`SceneTransitionTrigger.IsClosedForToday()`는 오후에 지하 진입을 막고, 정상 귀환은 `ExploreExitController.PrepareSettlement`에서 `SetAfternoon()`을 부른다. 그런데 긴급 탈출은 `ExploreExitController`를 거치지 않아 **아침이 유지됐다.** 즉 긴급 탈출이 "하루 1다이브"의 유일한 우회로였고, 광물 60%를 버리고도 그날 최고 수입(1,105G)이 나왔다 — 페널티가 이득으로 뒤집혀 있었다.

`dive_start` 110건 중 `Afternoon`이 **0건**인 것이 그 증거다. `PauseOverlayUI.ExecuteEmergencyEscape`에 `SetAfternoon()`을 넣어 막았다.

#### 확정 사다리

| # | 노드 | §4.3 | 현재 |
|---|---|---|---|
| 1 | `MiningRange_T0_01` | 60 | **60** |
| 2 | `ShovelStamina_T0_01` | 130 | **110** |
| 3 | `MiningSpeed_T0_01` | 160 | **170** |
| 4 | `ToolRange_T0_01` | 200 | **230** |
| 5 | `MaxStamina_T0_01` | 230 | **290** |
| 6 | `StaminaCost_T0_01` | 260 | **350** |
| 7 | `MaxStaminaMultiplier_T0_01` | 300 | **410** |
| 8 | `PickaxeUnlock_T0_01` | 330 | **470** |
| 9 | `PickaxeDamage_T0_01` | 370 | **530** |
| 10 | `MiningCooldown_T0_01` | 400 | **590** |
| 11 | `MiningLevel_T0_Final` | 700 | **910** |
| | **최소 경로** | 3,140 | **4,120** |

곁가지 5개는 비례 조정: `MoveSpeed`·`ClimbSpeed` 130 → **170**, `VisionRadius`·`FallDamage`·`WallClimbSpeed` 180 → **230**. 곁가지 합 800 → **1,030**, T0 총액(시설 제외) 3,940 → **5,150G**.

잔액 곡선은 `0 14 32 54 80 110 144 182 224 270`이고 면허는 11회차다.

#### ⚠ 이것으로 분량 문제가 풀리지 않는다

소요 다이브는 **11회로 §4.3과 같다.** 수입 모델을 함께 올렸으니 총액만 +31%가 되고 일수는 안 늘어난다. 이 재산정의 효과는 **"하루 두 개씩 사지는 리듬 붕괴"를 고치는 것**이지 1층 체류 기간을 늘리는 것이 아니다.

긴급 탈출 수정까지 합쳐 실측 9~10일 → **11일** 정도가 기대치다. "1층 지형을 다 파는 동안 성장이 이어진다"를 만들려면 **노드 수 자체를 늘려야** 하고, 그건 살아있는 효과 13종(§2.1)이 이미 다 쓰인 상태라 죽은 효과를 배선하는 작업이 선행된다. 별도 설계로 다룬다.

#### 표본의 한계

회차 1개, 7점, 노드 13개까지다. 면허 이후 구간은 외삽이다.

그리고 이 적합은 `days.csv` 없이 `shop_transaction`(sell) 합 ÷ 하루 다이브 수로 근사한 것이다 — `day_settled` 이벤트가 **109개 세션 전체에서 0건**이었기 때문이다. 원인은 그 로깅이 씬에 배치되지 않은 `BedInteractable`에만 있었고, 실제 수면 경로인 `SleepSequence.Run()`에는 없었던 것이다. 같이 옮겼으므로 **다음 회차부터는 `--csv-dir` 자동 경로를 쓸 수 있다.** `Telemetry.Context.Day` 갱신도 같은 이유로 빠져 있어서, 그때까지의 로그는 이벤트의 `day` 필드가 씬 로드 시점까지 어제 날짜로 남아 있다.

### 4.4 설명문 수정

루트의 설명문을 "곡괭이질 속도가 10% 증가합니다"에서 **"도구를 휘두르는 속도가 10% 증가합니다"** 로 바꾼다. `MiningSpeedMultiplier`는 삽 차징에도 걸리므로(`SapStrategy.cs:170`) 현재 설명은 사실과 다르다.

---

## 5. 배선 — `ShovelStaminaReduce`

### 5.1 기본값 심기

`PlayerStat.SetDefaultMultiplicativeBaseValues()` 끝에 추가한다.

```csharp
// ShovelStaminaReduce: 기본 삽질 비용 배율 1 (곱연산 기준, 낮을수록 좋음)
if (baseValues.Get(StatType.ShovelStaminaReduce) <= 0f)
    baseValues.Set(StatType.ShovelStaminaReduce, 1f);
```

### 5.2 소비처

`SapStrategy.PerformSapDig()`의 지형 파기 비용 지점(현재 `SapStrategy.cs:348`).

```csharp
float maxReduce = MiningStaminaTuning.ShovelTerrainReductionPerCharge * chargeBasis;
if (_context.playerStats != null)
    maxReduce *= _context.playerStats.GetFinalValue(StatType.ShovelStaminaReduce);
_staminaManager?.AddDiggingReduction(maxReduce);
```

노드를 하나도 안 산 상태에서는 배율이 1이라 **기존 세이브의 체감이 바뀌지 않는다.**

돌 타격 경로(`AddDiggingReduction(GetRockMaxReduction(...))`, 현재 `SapStrategy.cs:341`)에는 **곱하지 않는다**(§3.1).

### 5.3 스탯 UI 등록

`CodeStatIcons`의 `StatCategory.Stamina` 블록에 추가한다. 지금은 목록에 없어서 스탯 패널·툴팁에 아예 표시되지 않는다.

```csharp
new StatDisplay(StatType.ShovelStaminaReduce, StatCategory.Stamina, "ui_stat_shovel_stamina", "삽질 스태미나", StatValueFormat.Multiplier, false),
```

`false`는 "낮을수록 좋음"이다(`StaminaCostPerSecond`·`FallDamageReduce`와 같은 취급).

### 5.4 T1 노드 값 재조정

`ShovelStamina_T1_01`이 II가 되므로 값을 `0.2` → **`0.85`** 로 바꾸고 표시명을 `가벼운 삽질 II`로 고친다.

두 개를 모두 사면 `0.8 × 0.85 = 0.68` — 삽질 최대 스태미나 감소가 32% 줄어든다.

T1 비용(7,200G)은 그대로 둔다. §7.2 예산이 T1 총액 119,900G에 묶여 있어 값을 바꾸면 다른 노드가 따라 움직인다.

---

## 6. T1 표시명 충돌 정리

T0에 새로 생기는 4개가 T1의 기존 노드와 같은 "I"를 쓴다. T1 쪽 표시명만 II로 올린다. **효과값·비용·nodeId는 건드리지 않는다** — §7.2 예산이 걸려 있다.

| nodeId | 현재 표시명 | 변경 |
|---|---|---|
| `StaminaCost_T1_01` | 효율적인 호흡 I | 효율적인 호흡 II |
| `ClimbSpeed_T1_01` | 벽 기어오르기 I | 벽 기어오르기 II |
| `MiningCooldown_T1_01` | 연속 채광 리듬 I | 연속 채광 리듬 II |
| `WallClimbSpeed_T1_01` | 밀착 기어오르기 속도 | 밀착 등반 II |
| `ShovelStamina_T1_01` | 가벼운 삽질 I | 가벼운 삽질 II (§5.4) |

---

## 7. 범위 밖 — 기록만 남긴다

§2.1의 죽은 효과 11종(`ShovelStaminaReduce` 제외)은 **이번 작업에서 배선하지 않는다.** 필요한 시점에 하나씩 붙인다.

선행 문서 §10.1을 갱신해 3종 → 12종으로 늘리고, 새로 확인한 `MineralExtraDropChance`의 `0 × 배율` 원인을 함께 적는다.

**면허 II 최소 경로의 무효 금액도 정정이 필요하다.** §10.1은 23,400G(27%)라고 적었는데, 죽은 노드 3종만 세고 나머지를 빠뜨렸다. 최소 경로 87,500G를 노드별로 펼치면:

| 노드 | 비용 | 상태 |
|---|---|---|
| `MiningSpeed_T1_01` | 5,200 | 살아있음 |
| `DrillCapacity_T1_01` | 6,500 | **죽음** |
| `DrillRegen_T1_01` | 7,800 | **죽음** |
| `StaminaCost_T1_01` | 5,900 | 살아있음 |
| `ShovelStamina_T1_01` | 7,200 | **죽음** → 이번 작업으로 살아남 |
| `InventorySlot_T1_01` | 6,500 | **죽음** |
| `WarehouseCapacity_T1_01` | 7,800 | **죽음** |
| `MineralExtraDrop_T1_01` | 6,500 | **죽음** |
| `MineralSell_T1_01` | 9,100 | **죽음** |
| `MiningLevel_T1_Final` | 25,000 | 살아있음 |
| **합계** | **87,500** | |

무효액은 **51,400G(58.7%)** 다. 이번 작업으로 `ShovelStamina_T1_01`이 살아나면 **44,200G(50.5%)** 로 줄지만, 여전히 면허 II까지 내는 돈의 절반이 아무 일도 하지 않는다. T1 작업에서 가장 먼저 다뤄야 할 항목이다.

---

## 8. 변경 대상 파일

| 파일 | 변경 |
|---|---|
| `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` | T0 블록 전체 교체(§4.2), 루트 설명문(§4.4), T1 표시명·`ShovelStamina_T1_01` 값(§5.4·§6) |
| `Assets/GameData/UpgradeData/{Effect,Node}/*.asset` | 위 생성기 실행 결과 |
| `Assets/Scripts/UI/Player/Stats/PlayerStat.cs` | `ShovelStaminaReduce` 기본값 1f(§5.1) |
| `Assets/Scripts/UI/Player/Strategies/SapStrategy.cs` | 최대치 감소에 배율 곱하기(§5.2) |
| `Assets/Scripts/UI/Core/CodeStatIcons.cs` | 스탯 표시 등록(§5.3) |
| `Assets/Tests/EditMode/UpgradeTreeCostTests.cs` | 노드 수 35 → **40**, 테스트명 `NodeCount_IsThirtyFive` → `NodeCount_IsForty` |
| `Assets/Docs/economy/mineral-price-design.md` | §7.1 노드 수, §10.1 죽은 효과 목록(§7) |

노드 수 40 = 기존 35 − T0 10 + T0 15.

---

## 9. 검증

| 항목 | 기대값 | 어긋나면 |
|---|---|---|
| `UpgradeTreeCostTests` 전체 | 통과 | 노드 수·총액 재확인 |
| T0 총액 | 14,900G | §4.3 검산 |
| 면허 I 최소 경로 | 11,900G | 4행 단가 조정 |
| 면허 I 도달 | 16일차 부근(16회) | 변화 없어야 정상 — 바뀌면 최소 경로가 틀어진 것 |
| 삽질 최대치 감소 (노드 0개) | 차징 1.0에 −0.5 | 배율이 1이 아님 → 기본값 미적용(§3.2) |
| 삽질 최대치 감소 (T0 삽 노드 구매) | 차징 1.0에 −0.4 | `0.2`/`0.8` 규약 확인(§3.3) |
| 삽질 최대치 감소 (T0+T1) | 차징 1.0에 −0.34 | — |
| 돌 타격 최대치 감소 | 삽 노드와 무관하게 −2 고정 | 돌 경로에 배율이 새어 들어감(§3.1) |
| F8 진단 패널 | "삽질 스태미나" 줄이 보임 | `CodeStatIcons` 등록 누락(§5.3) |

`ShovelStaminaReduce` 검증은 F8 패널의 `LogDigs` 토글로 `[SapDig] ... 최대치 −0.xxxx` 로그를 직접 읽어 확인한다.

---

## 9-A. 도구 해금 (2026-08-12 추가)

### 9-A.1 구조는 이미 있었고, 가리키는 곳이 없었다

`StreamingAssets/toolConfig.json`이 도구 인덱스별 해금 노드를 **문자열로** 들고 있고, `ToolController.IsToolUnlocked`가 `UpgradeManager.IsNodeUnlocked`로 대조한다. 빈 문자열이면 항상 해금이다.

```json
{ "unlockNodeIds": ["", "", "PickaxeUnlock_T0_01", "DrillCapacity_T1_01"] }
```

인덱스는 `0=맨손 1=삽 2=곡괭이 3=드릴`(`MiningStaminaTuning.Shovel/Pickaxe`와 같은 체계).

착수 시점에는 `["", "", "debug_pickaxe", "debug_drill"]`이었다. **두 id 모두 트리에 없는 노드**라 곡괭이·드릴이 영구 잠금이어야 정상인데 실제로는 둘 다 풀려 있었다 — 세이브에 `debug_pickaxe`가 강제 해금(`UpgradeManager.ForceUnlock`)돼 있었을 가능성이 크다.

**문자열 참조를 아무도 검사하지 않는 것이 진짜 문제다.** 오타를 내면 그 도구가 조용히 영구 잠금이 되고 컴파일러도 에디터도 말이 없다. `ToolConfig_UnlockNodeIds_AllExistInTree` 테스트가 이 구멍을 막는다.

### 9-A.2 곡괭이 = T0 4행, 트리의 허리

`PickaxeUnlock_T0_01`(곡괭이 입수, 330G)을 4행에 두고 2·3행 세 갈래를 전부 여기로 모은다. 곡괭이 숙련 2개(`날카로운 곡괭이 I`·`연속 채광 리듬 I`)가 그 자식이다 — **곡괭이 없이 곡괭이 강화를 사는 일이 없게** 한다(`PickaxeUnlock_IsPrerequisiteOfPickaxeNodes`).

구매 시점은 **8다이브째**로, "곡괭이는 후반"이라는 §3.6 순서와 맞는다.

**해금은 효과가 아니라 nodeId로 걸린다.** 그래서 이 노드의 `effectType`은 `None`이다 — 스탯을 주지 않는다. `UpgradeStatProvider`의 매핑에 없으므로 조용히 건너뛴다.

### 9-A.3 ⚠ 곡괭이가 잠기면 돌을 아예 못 판다

`ToolCapabilities.Resolve`에서 `canDigRock`이 true인 도구는 **곡괭이(2) 하나뿐**이다. 맨손·삽 모두 false다. 그리고 돌(`DiggableRock.DropRareMinerals`)은 **희귀 광물 공급원**이다.

즉 8다이브째까지는 희귀 광물이 나오지 않는다. 의도한 바이지만 두 가지를 확인해야 한다.

- 돌 뒤에 **막히면 안 되는 길**이 없을 것 (던전 입구·특수 청크 진입로 등)
- 툴스왑 유물(`ToolCapabilities.SwapTerrainRock`)이 곡괭이 해금 전에 나오면 삽이 돌을 캐게 된다 — 게이트 우회다

### 9-A.4 드릴은 T1 첫 드릴 노드에 붙였다

곡괭이 경로를 정상화하면 `debug_drill`을 가리키던 드릴이 **영구 잠금이 된다.** 새로 만들어지는 버그라 같이 막았다.

`DrillCapacity_T1_01`을 **"드릴 입수"** 로 이름만 바꾸고 `unlockNodeIds[3]`이 이것을 가리키게 했다. 드릴 계열의 첫 노드라 위치가 자연스럽고, **가격·구조·테스트가 하나도 바뀌지 않는다**(T1 총액 120,000G 유지).

부수 효과로 죽은 노드 하나가 값을 하게 됐다 — 배터리 용량 효과는 여전히 소비처가 없지만(선행 문서 §10.1 마) 노드 자체는 이제 드릴을 준다.

---

## 10. 적용 순서와 기존 세이브

### 10.1 생성기를 먼저 돌린다

`UpgradeTreeCostTests`는 **생성 결과인 에셋**을 읽는다. `UpgradeTreeGenerator.cs`만 고치고 `Tools/Upgrade/Upgrade Tree Generator`를 실행하지 않으면 테스트가 옛 에셋을 보고 실패한다.

`기존 생성된 에셋 삭제 후 재생성`을 켠 채로 실행한다 — 제거된 두 노드(`MiningPower_T0_01`·`WarehouseCapacity_T0_01`)의 에셋이 남아 있으면 노드 수가 40을 넘는다.

### 10.2 기존 세이브는 깨지지 않지만 노드를 잃는다

세이브(`PlayerData.upgradeTreeState.unlockedNodeIds`)는 nodeId 문자열 목록이다. 제거된 두 노드를 이미 산 세이브가 있어도 조회가 전부 널 가드를 타므로(`UpgradeStatProvider.cs:107`, `UpgradeManager.GetStatValue`의 `TryGetValue`) **예외는 나지 않는다.**

다만 그 두 노드에 쓴 골드는 돌아오지 않고, 새로 생긴 T0 노드 5개는 안 산 상태가 된다. 개발 중 세이브라 별도 마이그레이션은 두지 않는다 — 밸런스를 볼 거면 새 슬롯으로 시작하는 편이 정확하다.
