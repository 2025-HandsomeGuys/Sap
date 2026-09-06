# 장비·유물 가격 레벨디자인

작성일: 2026-08-09
선행 문서: [mineral-price-design.md](mineral-price-design.md) — 층별 회당 매출이 여기서 나온다. 그 수치가 바뀌면 이 문서의 가격도 같이 움직인다.
범위: **가격만 확정한다.** 장비 효과(`statModifiers`)는 이번 범위가 아니다(§8).

---

## 1. 목표와 접근

광물 가격을 지수 곡선으로 재설계한 뒤(1층 회당 1,430G → 2층 6,197G), 장비 27종이 1,000G 플랫·유물 28종이 500G 플랫로 남아 스케일이 완전히 어긋났다. 이를 층 곡선에 맞춘다.

**접근: 역할과 층을 먼저 배정해 가격을 정하고, 효과 수치는 그 가격을 정당화하도록 나중에 채운다.**

효과부터 정하면 27종을 하나씩 저울질해야 한다. 가격이 먼저 있으면 "12,000G짜리 방한복이니 이 정도는 해줘야 한다"는 기준이 생겨 효과 설계가 오히려 쉬워진다. 대가는 당분간 **비싸지만 아무 효과 없는 장비가 남는다**는 것이다(§8).

---

## 2. 착수 시점의 현황 (측정값)

### 2.1 장비 — 가격 이전에 효과가 없다

`Equip_*` 27개 중 **26개의 `statModifiers`가 비어 있다.** 유일한 예외가 `Equip_Climber_Armor`(3103, `statType 14` = 1.1)다. `defense`도 전부 0이다.

효과가 있는 것은 레거시 4종뿐이고, 그중 상점에 있는 것은 하나뿐이다.

| 에셋 | ID | 부위 | defense | statModifiers | 상점 |
|---|---|---|---|---|---|
| `EquipmentData.asset` | 3001 | 머리 | 2 | statType 2, 31 | 15G |
| `EquipmentData 1.asset` | 3101 | 몸 | 5 | statType 11, 23, 32 | **없음** |
| `EquipmentData 2.asset` | 3201 | 발 | 1 | statType 12, 13 | **없음** |
| `EquipmentData 3.asset` | 3301 | 유물 | 0 | statType 3, 10 | **없음** |

즉 **스탯이 있는 장비는 살 수 없고, 살 수 있는 장비는 스탯이 없다.**

### 2.2 유물 — 효과는 실재하고 가격만 플랫이다

| 항목 | 현재 |
|---|---|
| 상점가 | 4001(테스트용) 100G, **나머지 27종 전부 500G** |
| 강화비 | 전부 **100 / 250**, `maxLevel` 3 |
| 구성 | Passive 18종 / Active 10종 |
| 동작 | `RelicBehaviour` 파생 클래스로 **실제 구현되어 있다** |
| 슬롯 | 기본 **2개**(`RelicSaveData.slotCount`, 확장 가능) |

슬롯이 2개라 **28종을 다 사는 구조가 아니라 골라 쓰는 구조**다. 개별 가격이 높아도 총 지출은 통제된다.

---

## 3. 설계 원칙

### 3.1 가격이 곧 잠금이다 — 별도 해금 시스템을 만들지 않는다

층 도달을 감지해 상점을 여는 코드를 넣지 않는다. **27종 전부 `isAvailable: 1`로 노출하고, 가격만으로 막는다.**

1층 플레이어가 상점에서 60만 G짜리 4층 장비를 보게 되는데, 이것이 의도다 — **"4층에서는 저 정도 버는구나"** 하고 목표를 세우는 장치다.

### 3.2 층 안에서는 단일 가격

층 안에서 필수(생존)와 선택(효율)의 가격을 구분하지 않는다. 전부 같은 값이다.

그래서 **각 층에서는 세트 하나만 겨우 산다.** 어느 것을 먼저 살지가 선택이 되고, 나머지는 한 층 늦게 따라온다. "싸니까 선택 세트"가 아니라 "지금은 이것부터"가 된다.

### 3.3 유물은 장비와 같은 사다리를 쓴다

유물 구매가 = 같은 티어 장비의 **부위당 가격**. 강화비는 구매가의 50%(Lv1→2) / 150%(Lv2→3).

결과로 **유물 하나 풀강 = 같은 층 장비 한 세트**가 된다. 외우기 쉽고, "장비를 갖출까 유물을 키울까"가 같은 저울에 올라간다.

> ⚠ **현재 게임에서는 이 등식이 성립하지 않는다.** `EquipmentUpgradeOverlayUI`가 `RelicSO.upgradeCosts`를 읽지 않고 `EquipmentUpgradeTable.RelicGoldCost(int currentLevel) => Math.Max(1, currentLevel) * 2222`라는 별도 공식을 쓰기 때문이다(`EquipmentUpgradeTable.cs:83,86`, `EquipmentUpgradeOverlayUI.cs:1156,1339`). 실제 강화비는 티어와 무관하게 Lv1→2 2,222G / Lv2→3 4,444G 고정이라 티어3 유물 풀강은 설계상 600,000G가 아니라 실제 206,666G다. 설계 원칙 자체는 유효하고, 배선만 안 된 상태다 — §10 참조.

---

## 4. 장비 — 세트 9개의 역할과 층

층 배정 근거는 대부분 이미 데이터에 있다.

| 세트 | 역할 | 층 | 근거 |
|---|---|---|---|
| **Miner** 광부 | 채굴 기본 | 1 | 시작 장비 |
| **Winter** 방한 | 동상 저항 | 2 | Ice = `zoneStatusType: Frostbite`, 0.5/s |
| **Climber** 등반 | 벽타기 | 2 | `wallClimbSpeedRatio 0.5`·`wallSlipForce 2.0`이 **Ice에만** 있다 |
| **Carrier** 운반 | 무게 한도 | 2 | 광물 평균 무게가 0.574 → 1.073으로 두 배 되는 층 |
| **Heat** 방열 | 화상 저항 | 3 | MagmaRock = `Burn`, 1.0/s |
| **Engineer** 기술자 | 드릴 | 3 | 드릴이 주력이 되는 구간 |
| **Shovel** 삽 | 삽 효율 | 3 | 파는 양이 늘어나는 구간 |
| **Scientist** 과학자 | 방사선 저항 | 4 | MeteoriteRock = `Radiation`, 2.0/s |
| **Marathon** 마라톤 | 이동·스태미나 | 4 | 체류 시간이 가장 짧은 층 |

Scientist는 이름만으로 역할이 잡히지 않아 **방사선 저항**으로 배정했다. 층별 상태이상이 동상/화상/방사선 셋인데 방한·방열이 앞의 둘을 맡으므로 남은 자리가 정확히 방사선이다.

---

## 5. 장비 가격표 (27종)

| 층 | 부위당 | 세트 합 | 세트 | 층 합계 |
|---|---|---|---|---|
| 1 | **2,000** | 6,000 | Miner | 6,000 |
| 2 | **12,000** | 36,000 | Winter · Climber · Carrier | 108,000 |
| 3 | **46,000** | 138,000 | Heat · Engineer · Shovel | 414,000 |
| 4 | **200,000** | 600,000 | Scientist · Marathon | 1,200,000 |

**총 1,728,000G.**

### 5.1 equipmentID 대응표

`ShopItemDatabase.asset`의 `itemType: 1` 항목에 적용한다. **27종 전부 `isAvailable: 1`.**

| 세트 | 층 | 헬멧(30xx) | 복장(31xx) | 부츠(32xx) | 부위당 가격 |
|---|---|---|---|---|---|
| Miner | 1 | 3004 | 3104 | 3204 | **2,000** |
| Winter | 2 | 3006 | 3106 | 3206 | **12,000** |
| Climber | 2 | 3003 | 3103 | 3203 | **12,000** |
| Carrier | 2 | 3005 | 3105 | 3205 | **12,000** |
| Heat | 3 | 3007 | 3107 | 3207 | **46,000** |
| Engineer | 3 | 3008 | 3108 | 3208 | **46,000** |
| Shovel | 3 | 3010 | 3110 | 3210 | **46,000** |
| Scientist | 4 | 3009 | 3109 | 3209 | **200,000** |
| Marathon | 4 | 3011 | 3111 | 3211 | **200,000** |

`equipmentID: 3001`(레거시, 15G)은 이 표에 없다. 상점에 남아 있으나 이번 범위 밖이다.

### 5.2 ⚠ 직전 작업의 잠금 결정을 뒤집는다

광물 가격 작업에서 21개 장비를 `isAvailable: 0`으로 잠갔다. 이유는 "1,000G 플랫이라 2층 방한복(12,000G)보다 싸서 밸런스가 뒤집힌다"였다. **제대로 된 가격이 붙으면 그 이유가 사라지므로 전부 다시 연다.**

---

## 6. 유물 가격표 (28종)

> ⚠ **2026-08-27: 유물은 상점에서 빠졌다.** 획득 경로가 탐험(돌 완파 드롭 + 던전 상자)으로
> 바뀌어 `ShopItemDatabase.asset`의 `itemType: 2` 28행이 전부 `isAvailable: 0`이다.
> 아래 구매가는 **티어 라벨로만 살아 있다** — §6.1의 티어 배정이 곧 지층별 드롭 풀이 된다.
> 설계: `Assets/Docs/relic-exploration-drop.md`

| 티어 | 구매가 | Lv1→2 | Lv2→3 | 풀강 총액 | = 장비 |
|---|---|---|---|---|---|
| 1 | **12,000** | 6,000 | 18,000 | 36,000 | 2층 한 세트 |
| 2 | **46,000** | 23,000 | 69,000 | 138,000 | 3층 한 세트 |
| 3 | **200,000** | 100,000 | 300,000 | 600,000 | 4층 한 세트 |

구매가는 `ShopItemDatabase.asset`의 `itemType: 2` 항목, 강화비는 각 `RelicSO.upgradeCosts`(길이 2, `maxLevel - 1`)에 넣는다.

> ⚠ 위 표의 Lv1→2 / Lv2→3 값은 `RelicSO.upgradeCosts` 에셋에는 그대로 들어가지만, **현재 게임은 이 값을 읽지 않는다.** 실제 강화 UI는 `EquipmentUpgradeTable.RelicGoldCost`의 고정 공식(레벨 × 2,222G)을 쓴다. 자세한 내용은 §3.3 경고와 §10 참조.

### 6.1 티어 배정

| 티어 | 종 | relicID · 이름 |
|---|---|---|
| **1** | 7 | 4002 PigeonFeather · 4003 Magnet · 4005 SpiderGlove · 4012 JunkSpring · 4017 ToolSwap · 4023 Mp3 · 4024 Blind |
| **2** | 12 | 4006 Generator · 4007 GamblerGlasses · 4008 Anvil · 4009 Steroid · 4013 GravityFlip · 4014 DashBomb · 4015 Furnace · 4018 BlackMarket · 4021 DetectionPulse · 4022 OverloadBattery · 4027 OneWayPortal · 4028 Trident |
| **3** | 8 | 4004 Invincibility · 4010 PlasmaCutter · 4011 DrillDrone · 4016 Lightning · 4019 MinerDrone · 4020 Jetpack · 4025 XRay · 4026 Hourglass |
| — | 1 | 4001 TestStatRelic — 테스트용. `isAvailable: 0`으로 상점에서 뺀다 |

**이 배정은 이름과 `RelicBehaviour` 클래스명만 보고 추정한 것이다.** XRay·MinerDrone·Invincibility를 3티어로, Magnet·Mp3를 1티어로 둔 정도가 확실하고 나머지는 근거가 약하다. 실제 강도를 아는 사람이 조정해야 한다 — **§10의 후속 항목**이다.

---

## 7. 예산 검증

### 7.1 층별로 필수 세트 하나만 겨우 산다

| 층 | 총 획득 | 소요 (트리 + 필수 장비 + 포션) | 여유 | 그 층 나머지 세트 |
|---|---|---|---|---|
| 2 | 193,004 | 171,900 (T1 119,900 + Winter 36,000 + 포션 16,000) | 21,100 | Climber·Carrier 각 36,000 — **못 산다** → 3층에서 |
| 3 | 1,008,000* | 875,000 (T2 665,000 + Heat 138,000 + 포션 72,000) | 133,000 | Engineer·Shovel 각 138,000 — 못 산다. 대신 2층 잔여 2세트(72,000)를 여기서 |
| 4 | 4,536,000* | 2,124,000 (**T3 1,200,000** + Scientist 600,000 + 포션 324,000) | 2,412,000 | Marathon 600,000 + 3층 잔여 2세트(276,000) + 유물 |

\* 3·4층 회당 매출은 미확정(선행 문서 §8). T3는 §7.2에서 압축한 값이다.

### 7.2 ⚠ T3 트리 예산이 압축된다

선행 문서 §8은 4층 전용 트리(T3)를 대략 2,500,000G로 스케치했다. 장비 1,728,000G와 유물이 몫을 가져가므로 **T3는 약 1,200,000G로 줄어든다.**

120회(=120일) 총 획득 580만G 기준 배분:

| 항목 | 금액 | 비중 |
|---|---|---|
| 업그레이드 트리 (T0 14,900 + T1 119,900 + T2 665,000 + **T3 1,200,000**) | 1,999,800 | 34% |
| 장비 27종 | 1,728,000 | 30% |
| 유물 (슬롯 2개 최종 풀강 + 거쳐가는 구매) | 약 1,500,000 | 26% |
| ↳ ⚠ 이 수치는 강화비가 `RelicSO.upgradeCosts`대로 배선됐다고 가정한 값이다. 실제로는 `EquipmentUpgradeTable.RelicGoldCost` 고정 공식(레벨 × 2,222G)이 적용되므로 실제 지출은 이보다 훨씬 적다(§3.3, §10) | — | — |
| 포션 (2층 16,000 + 3층 72,000 + 4층 324,000) | 412,000 | 7% |
| 마켓 종잣돈 | 약 160,000 | 3% |

T3는 아직 존재하지 않는 트리라 자유롭게 줄일 수 있다. **4층 작업 시 이 1,200,000G를 상한으로 설계할 것.**

---

## 8. 이번 범위가 아닌 것 — 장비 효과

**가격만 정하고 `statModifiers`는 비워 둔다.** §1의 접근에 따른 의도된 상태다.

즉 이 문서를 적용한 직후에는 **600,000G짜리 Scientist 세트가 아무 효과도 없다.** 지금 방한 세트 36,000G가 이미 그 상태이고, 이번 작업이 그 범위를 27종 전체로 넓힌다. 효과를 채우기 전에는 플레이 테스트에서 "비싸기만 하다"는 결론밖에 안 나온다.

### 8.1 효과를 채우기 전에 필요한 선행 작업

**`StatType`에 방사선 저항이 없다.** `HazardFrostResist`·`HazardBurnResist`는 있는데 방사선용이 빠졌다. `ItemActiveEffect`에는 `RestoreRadiation`(회복)이 있으므로 회복은 되지만 저항은 불가능하다.

4층 Scientist 세트를 실제로 동작시키려면 `StatType`에 **`HazardRadiationResist`를 추가**해야 한다. `StatType`은 enum이므로 **끝에 추가**할 것 — 중간에 끼우면 기존 에셋의 직렬화 값이 밀린다(`ItemActiveEffect`에 같은 취지의 주석이 이미 달려 있다).

### 8.2 세트별 효과 후보 (참고용, 미확정)

효과를 채울 때의 출발점으로만 남긴다.

| 세트 | 핵심 `StatType` 후보 |
|---|---|
| Miner | `MiningSpeed`, `PickaxeDamageUp`, `RockDamageMultiplier` |
| Winter | `HazardFrostResist`, `EnvironmentResistance` |
| Climber | `WallClimbSpeed`, `FallDamageReduce`, `JumpForce` |
| Carrier | `InventoryWeightUp`, `EncumberedSpeedMultiplier` |
| Heat | `HazardBurnResist`, `EnvironmentResistance` |
| Engineer | `DrillBatteryCapacity`, `DrillBatteryRegen` |
| Shovel | `ShovelStaminaReduce`, `ToolRange` |
| Scientist | `HazardRadiationResist`(신설 필요), `EnvironmentResistance` |
| Marathon | `MoveSpeed`, `MaxStamina`, `StaminaRegen` |

---

## 9. 변경 대상 파일

| 파일 | 변경 내용 |
|---|---|
| `Assets/GameData/ShopData/ShopItemDatabase.asset` | 장비 27종 `price` + `isAvailable: 1` 복원 / 유물 27종 `price` + 4001 `isAvailable: 0` |
| `Assets/GameData/Relics/*.asset` (27개) | `upgradeCosts` 2개 항목의 `gold` |
| `Assets/Docs/economy/mineral-price-design.md` | §8에 T3 예산 상한 1,200,000G 반영 |

`Assets/GameData/EquipmentData/*.asset`은 **건드리지 않는다.** `EquipmentSO.price`를 읽는 코드가 없다(선행 문서 §9). 상점 가격은 `ShopItemData.price` 한 곳에서만 관리된다.

---

## 10. 후속 / 미해결

| 항목 | 내용 |
|---|---|
| **유물 티어 재배정** | §6.1의 배정은 이름 기반 추정이다. 실제 강도를 아는 사람이 조정해야 한다 |
| **장비 효과 채우기** | §8. `HazardRadiationResist` 신설이 선행 |
| **3·4층 가격 재조정** | 회당 매출이 확정되면 3·4층 장비·유물 가격도 같이 움직인다 |
| **레거시 장비 3101·3201·3301** | 스탯이 있는데 상점에 없다. 노출할지 폐기할지 미정 |
| **유물 슬롯 확장 비용** | `RelicSaveData.slotCount`가 확장 가능한데 확장 경로·비용이 정해져 있지 않다 |
| **유물 강화비 미배선** | `RelicSO.upgradeCosts`(`RelicSO.cs:25`)는 `RelicSliceAssetGenerator.cs:89`가 쓰기만 하고, 읽는 코드가 프로젝트 전체에 없다. 실제 강화비는 `EquipmentUpgradeTable.cs:86`의 `RelicGoldCost(int currentLevel) => Math.Max(1, currentLevel) * 2222` 공식이며 `EquipmentUpgradeTable.cs:83` 주석에 "유물은 자체 upgradeCosts를 쓰지 않고 이 공식으로 통일한다"고 명시돼 있다. `EquipmentUpgradeOverlayUI.cs`의 1156행(표시)·1339행(차감)이 이 공식을 사용한다(강화 UI에는 이미 "⚠ 임시 테스트 값(밸런스 미확정)" 배지가 있다, `EquipmentUpgradeOverlayUI.cs:1137-1138`). 결과: §3.3·§6의 티어별 강화비 표가 게임에 반영되지 않는다. 두 갈래 선택지 — (1) `EquipmentUpgradeOverlayUI`가 `RelicSO.upgradeCosts`를 읽도록 배선한다, (2) `EquipmentUpgradeTable.RelicGoldCost` 공식을 티어별 값에 맞게 고친다. 어느 쪽인지는 미정 |
| **디버그 유물 지급기가 빌드 씬에 노출** | `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs`가 `#if UNITY_EDITOR`/`DEVELOPMENT_BUILD` 가드 없이 F1~F12·키패드로 유물 즉시 지급 + 최대 레벨 강화를 수행한다. 이 컴포넌트가 붙은 `Assets/Scenes/Demo/DemoUnderground.unity`가 `ProjectSettings/EditorBuildSettings.asset`의 빌드 씬 목록에 포함돼 있다. 이번 가격 작업이 만든 버그는 아니지만 노출 규모를 키운다 — 이전엔 500G 유물을 공짜로 얻는 수준이었으나 지금은 200,000G 유물 + 즉시 풀강을 공짜로 얻는다 |

---

## 11. 검증 방법

| 항목 | 기대 | 어긋나면 |
|---|---|---|
| 2층에서 Winter 세트 구매 시점 | 여유 21,100G로 36,000G는 못 산다 → 트리를 덜 사고 먼저 살 수도 있어야 함 | 2층 부위 가격 12,000을 조정 |
| 2층 Climber·Carrier 구매 시점 | 3층 초반 | 3층 여유(133,000)로 두 세트(72,000)가 들어가는지 확인 |
| 1층 상점 첫인상 | 4층 장비 600,000G가 "목표"로 읽힐 것 | "압도적이다"는 반응이면 4층 가격을 낮추기보다 **정렬·필터**로 해결할 것 — 가격 사다리 자체는 예산에서 역산된 값이다 |
| 유물 대 장비 선택 | 같은 티어에서 "유물 풀강 vs 장비 한 세트"가 실제로 고민될 것 | 한쪽이 압도적이면 §3.3의 50%/150% 강화 배율을 조정 |
| 상점 가격 텍스트 폭 | 200,000G 항목의 가격 텍스트가 구매 버튼과 겹치지 않는지 육안 확인 | `ShopOverlayUI.cs:652-655`의 가격 컬럼이 `preferredWidth`/`minWidth` 130f 고정(21pt Bold, NoWrap) — `1,000 G`(7자) 기준 폭에 `200,000 G`(9자)가 들어가면 겹칠 수 있다. 겹치면 컬럼 폭 조정 필요 |
