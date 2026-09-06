# 업그레이드 트리 자동 생성 설계

## 개요

업그레이드 트리의 노드 구성·순서·가격을 손으로 짜는 대신, **플레이 루프 시뮬레이터의
한계효용을 따라 자동 생성**한다. 목표하는 감각은 셸다이버(Shell Diver)류의
"하나를 채우면 다른 게 부족해지는" 압박 루프다.

산출물은 **초안**이다. 사람이 손본 행은 생성기가 덮어쓰지 않는다(§4.2 `locked`).

---

## 1. 목표와 비목표

### 목표
- 노드 **구성까지** 자동 생성한다 — "효과 타입 풀 + 목표 곡선"을 주면 몇 티어에
  무슨 노드를 몇 개 놓을지 생성기가 결정한다.
- 병목이 **돌아가며** 바뀐다. 스태미나를 채우면 무게가 모자라고, 무게를 채우면
  시간이 모자란다.
- 트리 원본을 **데이터로** 옮긴다. 지금은 C# 리터럴이라 반복 튜닝이 느리다.

### 비목표
- UI 배치 자동화 — `uiPosition`은 생성기가 티어·행 기준의 격자값만 채우고,
  미세 배치는 사람이 한다.
- 아이콘·로컬라이제이션 문구 생성.
- 2·3·4층 트리 전체 재설계. 이번 범위는 **파이프라인**이고, 첫 적용 대상은 T0다.

---

## 2. 착수 시점의 현황 (측정값, 2026-08-21)

### 2.1 `cost`의 원본이 세 군데다

| 소스 | 노드 수 | 수정 시각 | 면허 I 값 | 역할 |
|---|---|---|---|---|
| `UpgradeTreeGenerator.cs` C# 리터럴 | 50 | 08-19 21:16 | 1,280 | 에셋 생성 원본 |
| `Assets/GameData/UpgradeData/Node/*.asset` | 46 | 08-18 23:54 | 1,280 | 테스트 검사 대상 |
| `priceData.json` `upgradeNodes` | 44 | 08-19 20:30 | **700** | **런타임 최종 승자** |

`PriceApplier.ApplyNodes`가 `node.cost = e.cost`로 덮어쓰므로(`PriceApplier.cs:195`),
플레이어가 실제로 보는 값은 JSON이다. **50개 중 48개가 불일치한다.**

JSON 값(20/60/80/120/180…)은 C# 척추(60/110/170/230/290, 공차 60)와 계보 자체가
다른 옛 사다리다. `PriceDataExporter`로 뽑은 것이 아니다. 즉 **JSON이 스테일이고,
게임이 조용히 절반 가격으로 돌고 있었다.**

→ **결정: 마이그레이션 기준은 C#/에셋(1,280 계보).** JSON은 CSV에서 다시 굽는다.
   체감 난이도가 올라가므로 적용 후 플레이 확인이 필요하다.

### 2.2 배선 오류

- `priceData.json`에 `Facility_Workbench_T0`가 있으나 C#은 `Facility_Workbench_T1`
  → 런타임 경고 `upgradeNodes: 트리에 'Facility_Workbench_T0' 노드 없음`, 작업대 가격 미적용.
- JSON에 없어 가격이 안 먹는 노드 7개:
  `Nap_T0_01` / `Nap_T0_02` / `MapExplore_T0_01` / `MapExplore_T1_01` /
  `Facility_Map_T0` / `Facility_Workbench_T1` / `InventoryWeightSmall_T0_01`
- C#에만 있고 에셋엔 없는 노드 4개(생성기를 돌리면 새로 생김):
  `Facility_Map_T0` / `MapExplore_T0_01` / `MapExplore_T1_01` / `InventoryWeightSmall_T0_01`

### 2.3 `UpgradeTreeCostTests` 현황 — 이미 절반이 빨간불

에셋 값 대조 기준(Unity 실행 아님):

| 테스트 | 기대 | 실제 | 상태 |
|---|---|---|---|
| `Tier0_TotalCost` | 5,150 ±150 | 7,920 | 실패 |
| `MiningLicenses_HaveExplicitCosts` | 면허 I 910 | 1,280 | 실패 |
| `Tier0_MinimumPathToLicense` | 4,120 | 8,340 (16노드) | 실패 |
| `Tier0_EveryDiveAffordsExactlyOneNode` | 회당 60G/+64G | 가격 1.9배 | 실패 유력 (실행 필요) |
| `NodeCount_IsFortySix` | 46 | 46 → 생성 후 50 | 지금은 통과 |
| `Tier1_TotalCost` | 120,000 ±1,000 | 120,100 | 통과 |
| `Backpack_MovedToTier1` | 5,000 | 5,000 | 통과 |

08-18 승격(이동·등반을 필수로, 시설을 척추에 편입, 면허 1,280G) 이후 기대값이
갱신되지 않은 채 방치됐다. 생성기 주석도 이 사실을 적어두고 있다.

### 2.4 이미 있는 것

| 자산 | 위치 |
|---|---|
| 수입 곡선 실측 적합 + 가격 사다리 | `Tools/telemetry/fit_upgrade_prices.py` |
| 필수 노드 집합 역산 + 사다리 위반 진단 | `Tools/telemetry/check_upgrade_tree.py` |
| 실측 데이터 파이프 | `Tools/telemetry/export_csv.py` → `days.csv` / `dives.csv` |
| 에셋 증분 생성 + GUID 보존 | `UpgradeTreeGenerator.cs` |
| 런타임 가격 오버라이드 | `PriceApplier` / `priceData.json` |

---

## 3. 왜 트리 생성기가 아니라 런 모델이 먼저인가

`fit_upgrade_prices.py`의 수입 모델은 이렇다.

```
income(k) = base + growth × (노드 수)
```

**"어떤 노드를 샀는지"가 들어가지 않는다.** 스태미나를 사든 무게를 사든 똑같이 +64G다.
이 모델 위에서는 병목 순환이 원리적으로 표현 불가능하다 — 목표하는 감각이 정확히
"무엇을 샀느냐에 따라 다음에 무엇이 모자라느냐가 달라진다"인데, 모델이 그 구분을 못 한다.

압박 루프는 트리 **모양**이 아니라 수식의 `min()`에서 나온다. `min()`의 주인이 바뀌는 것이
곧 병목이 넘어가는 것이다. 그래서 이 작업은 실질적으로 **위 한 줄을 벡터 모델로
갈아끼우는 것**이고, 나머지는 그 위에 얹힌다.

---

## 4. 설계 A — CSV 단일 원본

### 4.1 파일

`Assets/GameData/UpgradeData/UpgradeTree.csv` (UTF-8, RFC 4180)

```csv
nodeId,tier,effectType,effectValue,isPercentage,parentIds,cost,uiX,uiY,displayNameKey,descriptionKey,locked
MiningRange_T0_01,0,MiningRangeMultiplier,1.15,true,,60,0,-350,<키>,<키>,false
ShovelStamina_T0_01,0,ShovelStaminaReduce,0.9,true,MiningRange_T0_01,110,0,-290,<키>,<키>,false
```

컬럼 배열과 `nodeId` / `cost` / `uiY`는 실제 값이다. 나머지 칸은 형식을 보이기 위한
예시이고, **로컬라이제이션 키·효과값은 마이그레이션 때 C# 원본에서 그대로 옮긴다**
— 새로 짓지 않는다.

- `parentIds` — 세미콜론(`;`) 구분. 쉼표는 CSV 구분자라 쓸 수 없다.
- `effectType` — `UpgradeEffectType` enum **이름 문자열**. `tileData.json` /
  `priceData.json`이 이미 쓰는 규약과 같다(`System.Enum.TryParse`).
- 쉼표를 포함하는 로컬라이제이션 키는 없지만, 리더는 RFC 4180 따옴표를 처리한다
  (`NewsChains.csv`에서 같은 함정을 이미 겪었다).

### 4.2 `locked` 컬럼 — 이 설계의 핵심 안전장치

`true`인 행은 **합성기가 값을 덮어쓰지 않는다.** 존재·선행 연결만 읽고 지나간다.

이게 없으면 "자동 생성 초안"이 매번 수동 튜닝을 날려서 결국 아무도 안 쓰게 된다.
생성기는 `locked` 행을 **고정점(anchor)** 으로 취급하고 나머지를 그 사이에 채운다.

### 4.3 마이그레이션

현재 C# 50개 노드를 값 그대로 CSV로 옮긴다. **동작이 바뀌지 않아야 한다.**
`GetUpgradePlanData()`는 삭제하고, `UpgradeTreeGenerator`는 CSV를 읽는다.

이관 직후 전 행 `locked=false`로 둔다 — 아직 합성기가 없으므로 무해하고,
합성기 도입 시 어느 행을 지킬지 그때 정한다.

---

## 5. 설계 B — 데이터 흐름

```
run_model.py → marginal.py → synth_tree.py        (Python, 표준 라이브러리만)
                     │
                     ▼  생성 (locked 행 보존)
            UpgradeTree.csv   ← 단일 원본 (UVCS 관리)
             │                              │
             ▼ 읽음                          ▼ 읽음
  UpgradeTreeGenerator.cs           PriceDataExporter
   (에셋 증분 생성, GUID 보존)        (upgradeNodes 절 굽기)
             │                              │
             ▼                              ▼
   UpgradeNodeSO / EffectSO           priceData.json
                                             │
                                             ▼ 런타임
                                      PriceApplier → node.cost
```

화살표가 지금과 **반대**다. 현재는 파이썬이 C# 소스를 정규식으로 읽는데,
앞으로는 파이썬이 CSV를 쓰고 C#이 읽는다.

### 5.1 `priceData.json`은 남긴다

빌드 후 게임을 안 고치고 튜닝한다는 존재 이유가 유효하다
(`price-data-json-design.md` §2.2). 다만 `upgradeNodes` 절의 값은 **CSV에서 굽고**,
손으로 고친 것은 다음 생성에서 덮어써진다 — `minerals.layer` 라벨과 같은 규칙(§3.1-A).

`PriceDataExporter`는 현재 에셋에서 읽는다. 이걸 **CSV에서 읽도록** 바꾼다.
그래야 에셋 재생성을 잊어도 JSON이 원본과 어긋나지 않는다(§2.1의 재발 방지).

### 5.2 `check_upgrade_tree.py`의 C# 파서는 삭제한다

CSV 리더로 교체하면 정규식 파싱 코드가 통째로 없어진다. **코드가 준다.**

---

## 6. 설계 C — 런 모델 (`Tools/telemetry/run_model.py`)

### 6.1 수식

```python
def income(stats, layer, dive_seconds):
    # 1) 팔 수 있는 양 — 스태미나 제약 vs 시간 제약
    dug = min(stats.stamina / dig_cost(layer, stats),      # 스태미나
              dive_seconds * dig_rate(stats))              # 시간

    # 2) 담을 수 있는 양 — 무게 제약 vs 슬롯 제약
    carried = min(dug * density(layer),
                  stats.weight_cap / avg_weight(layer),    # 무게
                  stats.slots)                             # 슬롯

    return carried * unit_price(layer) * stats.sell_bonus
```

제약 5개 = 돌아가며 모자랄 수 있는 축 5개:

| 축 | 소비하는 스탯 | 코드 근거 |
|---|---|---|
| 스태미나 | `MaxStamina`, `StaminaCostMultiplier`, `ShovelStaminaReduce` | `StaminaDigCostCalculator.PayCost` |
| 시간 | `MiningSpeed`, `MiningCooldown`, `MiningRange`, `MoveSpeed`, `WallClimbSpeed` | `MiningStaminaTuning` |
| 무게 | `InventoryWeightUp` | `EncumbranceController` |
| 슬롯 | `InventorySlotUp` | 인벤토리 |
| 하드게이트 | `MiningLevel` | 층 강인도. 미달이면 `income = 0` |

`dig_cost(layer, stats)`는 깊이에 따라 오른다(`TileDataJson.maxStaminaReduction`).
`unit_price` / `avg_weight` / `density`는 `priceData.json` + `tileData.json`에서 읽는다 —
**하드코딩하지 않는다.** 광물 가격을 바꾸면 런 모델이 따라와야 한다.

### 6.2 적합 (fitting)

`dives.csv`에서 아래 컬럼으로 계수를 최소제곱 적합한다.

| 구할 것 | 쓰는 컬럼 |
|---|---|
| `dig_rate` | `pixels_dug` ÷ `sec_*`(층별 체류 초) |
| `dig_cost` | (`max_stamina_start` − `max_stamina_end`) ÷ `pixels_dug`, 층별 |
| `density` | `mineral_count` ÷ `pixels_dug`, 층별 |
| `avg_weight` | `minerals` 구성 × `priceData.json`의 `weight` |
| `dive_seconds` | `seconds` 분포의 중앙값 |

**최대 스태미나는 파생시키지 않는다 — `max_stamina_start` / `max_stamina_end`로 직접 찍힌다.**
잠수 소모는 `current`가 아니라 `MaxStamina`가 깎이는 것으로 나타나므로(`balance-csv-design.md`
§5.1.1) `dig_cost`의 분자는 시작 최대치와 종료 최대치의 차다.

`stamina_loss`(`sloss_*` 컬럼)로 그 감소를 원인별로 가를 수 있다. `dig_cost`를 순수하게
파기 비용으로 보고 싶으면 `sloss_digging`만 쓰고, 층 환경 피해까지 포함한 실질 비용을
보고 싶으면 차값을 쓴다.

`max_stamina_start = 0`인 행(계측 실패)은 적합에서 제외한다.

**전제: 하루 = 다이브 1회, 잠수 시작 스태미나 = 최대치.** 이 전제가 깨지면
(같은 날 두 번 내려가면) 시작값을 최대치로 볼 수 없어 `dig_cost`가 과소평가된다.
`dives.csv`를 `run_id + day`로 group-by해 하루 2회 이상인 날을 세고, 있으면
그 날들을 적합에서 뺀다.

### 6.3 병목 라벨은 직접 관측된다 — 검증의 핵심

`dives.csv`에는 `max_stamina_pct`와 `weight_ratio`가 둘 다 있다. 잠수 종료 시점에

- `max_stamina_pct ≈ 0` → **스태미나 병목**
- `weight_ratio ≈ 1` → **무게 병목**
- 둘 다 여유 → 시간·자발 귀환

로 라벨링된다. 이건 추정이 아니라 관측이다.

**합격 기준 (두 개 모두):**
1. 예측 회당 매출이 실측 대비 **±20% 이내**
2. 예측 병목 축이 위 라벨과 **70% 이상 일치**

2번이 없으면 총액만 맞고 병목 구조는 틀린 모델이 통과한다. 그 모델로 트리를
생성하면 숫자는 그럴듯한데 압박감이 없는 트리가 나온다.

### 6.4 모델 드리프트 방지

파이썬이 게임 수식을 재구현하므로 원본과 어긋날 수 있다. 두 장치로 막는다.

- 계수·테이블은 전부 `tileData.json` / `priceData.json`에서 읽는다(§6.1).
- §6.3의 합격 기준을 **회귀 스크립트로** 만들어, 새 `dives.csv`가 쌓일 때마다 돌린다.
  어긋나면 트리를 다시 굽기 전에 모델부터 고친다.

---

## 7. 설계 D — 한계효용과 합성기

### 7.1 한계효용 (`marginal.py`)

각 스탯을 한 단계 올렸을 때의 `income` 증가분.

```python
def marginal(stats, stat_type, step, layer):
    return income(bump(stats, stat_type, step), layer) - income(stats, layer)
```

`min()` 덕분에 **병목이 아닌 축은 한계효용이 자동으로 0**이 된다.
순환은 여기서 공짜로 나온다 — 별도 규칙이 필요 없다.

### 7.2 합성 규칙 (`synth_tree.py`)

1. 현재 스탯에서 한계효용 내림차순 정렬
2. 1위를 다음 노드로 채택. 단 **직전 노드와 같은 축이면 2위로 넘긴다** (순환 강제)
3. 가격은 `fit_upgrade_prices.py`의 사다리를 그대로 쓴다 — 새로 만들지 않는다
4. 스탯에 반영하고 1번으로
5. `MiningLevel` 하드게이트에 닿으면 티어를 끊는다
6. 곁가지 = 그 시점 한계효용 2~3위를 **선택지**로 같은 행에 배치
7. `locked=true` 행을 만나면 값을 그대로 두고 스탯에만 반영

`uiPosition`은 티어·행 기준 격자값(§1 비목표). 척추는 `x=0`, 곁가지는 좌우로 벌린다.

### 7.3 목표 곡선 입력

합성기에 주는 것은 노드 목록이 아니라 곡선이다.

```
층별 목표 체류 일수  (예: 1층 9일, 2층 14일)
구매 리듬            (다이브 1회 = 노드 1개)
효과 타입 풀         (UpgradeEffectType 중 이 층에서 허용할 것)
```

---

## 8. 설계 E — 오토플레이 검증 (`autoplay.py`)

봇 3종을 돌려 일차별 곡선을 뽑는다.

| 봇 | 정책 |
|---|---|
| 그리디 | 매번 한계효용 1위를 산다 |
| 무작위 | 살 수 있는 것 중 무작위 |
| 완전탐색 | N일 앞을 보고 최적 (T0 규모에서만) |

### 자동 검출 항목

| 항목 | 판정 |
|---|---|
| **죽은 노드** | 세 봇 모두 마지막까지 안 사는 노드 |
| **지배 경로** | 세 봇의 구매 순서가 90% 이상 일치 → 선택지가 가짜다 |
| **구매 리듬 이탈** | 하루에 2개 이상 사지거나, 3일 넘게 아무것도 못 사는 구간 |
| **병목 편중** | 한 축이 연속 3회 이상 병목 → 순환이 아니라 벽이다 |

### 이것이 폐기하는 테스트를 대체한다

§9에서 `Ignore` 처리하는 수치 테스트들이 지키려던 것은
"다이브마다 하나는 사지고 두 번째는 모자란다"였다. 그걸 **고정 숫자**가 아니라
**봇이 밟는 리듬 밴드**로 다시 검사한다. 총액이 바뀌어도 리듬이 유지되면 통과한다.

---

## 9. 테스트 처리

### 9.1 수치 6개 — `[Ignore]` + 사유 주석

`Tier0_TotalCost` / `Tier1_TotalCost` / `MiningLicenses_HaveExplicitCosts` /
`Tier0_MinimumPathToLicense` / `Tier0_EveryDiveAffordsExactlyOneNode` / `NodeCount_IsFortySix`

**삭제하지 않는다.** §8의 오토플레이가 이 자리를 대체할 때, 원래 무엇을 지키려
했는지가 남아 있어야 재작성이 가능하다. `Ignore` 사유에 "§8로 대체 예정"을 적는다.

**`Tier1_TotalCost`는 지금 통과 중인데도 함께 `Ignore`한다.** 2층 총액도 플레이하며
조정할 값이고, 1층만 풀고 2층을 묶어두면 §7.2 합성기가 T1에 닿는 순간 같은 교착이
반복된다. 통과 중인 테스트를 끄는 것이므로 여기 명시해 둔다.

이 결정은 CLAUDE.md의 "기대값을 같이 고치지 않는다" 규칙에 대한 **사람의 명시적 판단**이다.
근거: 총액은 플레이하며 조정하는 값이라 고정 기대값이 애초에 맞지 않고,
실제로 §2.3처럼 이미 방치돼 트리를 못 지키고 있었다.

### 9.2 구조 9개 — 유지

`EveryParentReference_Resolves` / `ToolConfig_UnlockNodeIds_AllExistInTree` /
`PickaxeUnlock_IsPrerequisiteOfPickaxeNodes` / `FacilityUnlockNodes_ExistInTree` /
`FacilityUnlockNodes_AreNotOnLicensePath` / `NapNodes_ExistWithNapCountEffect` /
`Backpack_IsNotAPrerequisiteOfAnyLicense` / `ShovelStaminaNodes_UseMultiplierConvention` /
`Backpack_MovedToTier1` (가격 단언만 제거, 티어 단언은 유지)

실측과 무관한 불변식이라 계속 유효하다.

### 9.3 새로 추가

- `UpgradeTreeCsvTests` — CSV 파싱, enum 이름 해석 실패, 선행 순환, 고아 노드
- `PriceDataIntegrationTests`에 한 줄 — `priceData.json`의 `upgradeNodes` id가
  전부 CSV에 존재하는가 (§2.2 재발 방지)

---

## 10. 작업 순서

| 단계 | 내용 | 검증 |
|---|---|---|
| **1** | **동작 무변경 마이그레이션.** C# 50개 → CSV. `UpgradeTreeGenerator`가 CSV를 읽는다. `PriceDataExporter`가 CSV에서 굽는다. §2.2 배선 오류 4건 정리. §9.1 `Ignore`, §9.3 추가 | 생성 전후 에셋 diff가 신규 4개 외에 없을 것. 구조 테스트 9개 통과 |
| **2** | 런 모델 + 적합 (§6) | §6.3 합격 기준 2개 |
| **3** | 한계효용 + 합성기 (§7) | T0를 재생성해 현재 트리와 비교. 사람이 읽고 판단 |
| **4** | 오토플레이 검증 (§8) | 현재 트리에 돌려 죽은 노드·지배 경로를 실제로 잡아내는지 |

**1단계에서 멈추고 검증할 수 있다.** 여기까지만 해도 원본이 데이터로 내려와
반복 튜닝이 빨라지고, 3중 불일치가 사라진다.

---

## 11. 범위 밖

- 2·3·4층 트리 재생성 — 파이프라인이 T0에서 검증된 뒤에 정한다
- 유물(`RelicSO`) 강화비 자동 생성 — 트리와 곡선이 다르다
- 주식·코인 밸런스 — 별도 계보(`stock-*-design.md`)
- Unity 안에서의 시뮬레이션 — 파이썬으로 간다(§12)

---

## 12. 알려진 트레이드오프

**게임 수식을 파이썬에 재구현한다.** C#(EditMode)에서 돌리면 실제 코드를 재사용할 수
있지만, 반복 튜닝마다 Unity 재컴파일이 붙어 느리고 `Tools/telemetry/`의 기존 파이썬
자산과 갈라진다. 드리프트는 §6.4의 두 장치로 막는다.

**`priceData.json`을 손으로 고치는 것이 무의미해진다.** 다음 생성에서 덮어써진다.
빌드 후 임시 실험용으로만 쓰고, 확정값은 CSV에 넣는다. `minerals.layer`와 같은 규칙이다.

**1단계 적용 직후 게임이 어려워진다.** §2.1대로 그동안 절반 가격으로 돌고 있었다.
면허 I이 700G → 1,280G가 된다. 이건 버그 수정이지 밸런스 변경이 아니지만,
체감은 밸런스 변경이므로 플레이 확인이 필요하다.

---

## 13. 변경 대상 파일

### 신규
- `Assets/GameData/UpgradeData/UpgradeTree.csv`
- `Tools/telemetry/run_model.py`
- `Tools/telemetry/marginal.py`
- `Tools/telemetry/synth_tree.py`
- `Tools/telemetry/autoplay.py`
- `Assets/Tests/EditMode/UpgradeTreeCsvTests.cs`

### 수정
- `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` — `GetUpgradePlanData()` 삭제, CSV 리더 추가
- `Assets/Scripts/Editor/PriceDataExporter.cs` — `ExportNodes()`가 CSV에서 읽도록
- `Tools/telemetry/check_upgrade_tree.py` — C# 정규식 파서 → CSV 리더
- `Assets/Tests/EditMode/UpgradeTreeCostTests.cs` — §9.1 `Ignore`, §9.2 유지
- `Assets/Tests/EditMode/PriceDataIntegrationTests.cs` — id 정합 검사 한 줄
- `Assets/StreamingAssets/priceData.json` — `upgradeNodes` 절 재생성 (44 → 50)

### 참고 (안 고침)
- `Assets/Scripts/_Core/Data/PriceApplier.cs` — 런타임 오버라이드 유지

---

## 14. 관련 문서

- `Assets/Docs/economy/mineral-price-design.md` — 수입 곡선 실측(§2.5-A/B), 예산(§7)
- `Assets/Docs/economy/price-data-json-design.md` — JSON 오버라이드 선례
- `Assets/Docs/telemetry/balance-csv-design.md` — `dives.csv` / `days.csv` 컬럼
- `Assets/Docs/superpowers/specs/2026-08-15-t0-spine-and-elevator-design.md` — 현재 T0 척추
