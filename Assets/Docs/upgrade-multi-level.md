# 업그레이드 노드 다단계(레벨) 설계

한 노드를 여러 번 사서 레벨을 올릴 수 있게 한 구조. 기존 "한 번 사면 끝" 노드는
`maxLevel = 1`로 남아 동작이 그대로다.

## 1. 데이터

| 위치 | 필드 | 설명 |
|------|------|------|
| `UpgradeNodeSO` | `maxLevel` | 최대 레벨. 1이면 종래 노드 |
| `UpgradeNodeSO` | `costGrowth` | 레벨당 가격 배율. `Lv N = cost × growth^(N-1)`, 10G 단위 반올림 |
| `UpgradeNodeSO` | `levelCosts` | Lv2부터의 가격을 직접 적을 때만 사용(비우면 공식) |
| `UpgradeTreeState` | `nodeLevels` | `NodeLevelEntry{nodeId, level}` 목록. **Lv2 이상만 저장** |

### Lv1을 저장하지 않는 이유

Lv1은 `unlockedNodeIds`에 들어 있는 것으로 표현한다. 그래서

- 구버전 세이브(= `nodeLevels`가 없는 파일)가 그대로 Lv1로 읽힌다 — 마이그레이션 코드가 없다.
- `IsNodeUnlocked`를 쓰는 기존 소비처(도구 해금·지도 해금·선행 판정·퀘스트)가 무수정으로 동작한다.

`SetLevel`이 이 불변식을 지킨다(레벨 1로 내리면 항목을 지우고, 0으로 내리면 해금 자체를 취소).

### priceData.json

`upgradeNodes[].cost`는 **Lv1 가격만** 덮어쓴다. 상위 레벨은 `costGrowth`/`levelCosts`에서 나온다.

## 2. 효과 합산

레벨 N은 **같은 효과를 N번 적용**한 것과 같다.

- 합연산: `value × N`
- 곱연산: `value^N`

`UpgradeStatProvider`는 Modifier를 N개 넣는 방식으로 이를 구현한다 —
`PlayerStat` 공식이 `(Base + ΣFlat) × ΠPercent`라 결과가 정확히 같고, 스탯 진단 패널에도
레벨 수만큼 출처가 찍혀 어디서 온 수치인지 보인다. `UpgradeManager.GetStatValue`도 같은 규칙이다.

## 3. 계층 게이트(MiningLevel)는 다단계 불가

`UpgradeNodeSO.EffectiveMaxLevel`이 `MiningLevel` 효과 노드를 무조건 1로 클램프한다.
이 노드의 `effect.value`는 "해금할 계층 번호"라서, 레벨이 올라갈 때 **어느 계층을 열지가 정의되지 않는다**.
인스펙터에서 `maxLevel`을 올려도 무시된다.

## 4. UI 규칙

- 노드 카드: 다단계 노드는 `Lv 2/5  1,200 G`, 최대면 `Lv 5/5  보유`.
- 상세 팝업: 계층 줄에 레벨 뱃지, 효과는 `현재 총합 (레벨당 증가분)`, 버튼은 `강화 Lv 3 (1,200 G)`.
- `GetNodeLockState`는 **아직 올릴 여지가 있고 지금 살 수 있으면** `Unlockable`을 돌려준다.
  트리에서 초록 링이 계속 붙어 "더 올릴 수 있다"가 보인다. 최대 레벨이어야 `Unlocked`다.

## 5. 노드를 다단계로 만들려면

`Assets/GameData/UpgradeData/Node/Node_*.asset`을 인스펙터에서 열어 `maxLevel`을 올린다.
`UpgradeTreeGenerator`(Tools/Upgrade)는 `icon`과 마찬가지로 **레벨 설정을 덮어쓰지 않으므로**
트리를 재생성해도 유지된다.

`UpgradeTreeCostTests`는 `cost`(=Lv1 가격) 합계만 보므로 `maxLevel`을 올려도 통과하지만,
**티어 예산은 Lv1 기준이라 다단계를 늘리면 실제 소비 골드가 예산을 넘어선다.** 밸런스는 사람이 판단한다.

### 사례 — `PickaxeStamina_T0_01` (가벼운 곡괭이질)

`maxLevel 4` / `costGrowth 1` → **레벨마다 100G 정액**, 레벨당 곡괭이 스태미나 ×0.9.
곱연산이라 만렙은 0.9⁴ = **−34.4%**다(−40%가 아니다 — 다단계 곱연산의 함정).
효과가 걸리는 곳은 `PickaxeStrategy.PickaxeStaminaScale()` 하나다.

### 사례 — `MaxStaminaMultiplier_T0_01` (강인한 체력)

`maxLevel 2` / `costGrowth 1` → **레벨마다 90G 정액**, 레벨당 최대 스태미나 ×1.05.
곱연산이라 만렙은 1.05² = **+10.25%** — 단발 +10%였던 옛 노드와 사실상 같은 값을 두 번에
나눠 사는 형태다(가격도 410G 단발 → 90G×2). 다단계 노드는 카드에 `Lv 1/2`가 붙으므로
표시 이름에서 로마숫자 `I`은 뗐다.

### 사례 — `InventoryWeight_T0_01` (튼튼한 멜빵)

`maxLevel 4` / `costGrowth 1` → **레벨마다 80G 정액**, 레벨당 무게 한도 +3(기본 8 → 만렙 20).
만렙까지 다 사도 320G다. T0 3행의 '효율적인 호흡 I'(350G 단발) 자리를 물려받아 선행·자식
배선은 그대로지만 **Lv1 가격이 270G 싸졌다** — T0 예산과 '다이브마다 하나' 리듬이 그만큼
헐거워진다(밸런스는 사람이 판단한다는 위 문단 그대로다).

`costGrowth`를 1로 두면 `CostForLevel`이 모든 레벨에 같은 값을 돌려준다 — 정액 요금의
관용 표현이다. 모양은 `UpgradeTreeCostTests.Backpack_T0_IsMultiLevelAndCheap`이 고정한다.

## 6. 텔레메트리

`upgrade_purchased` / `upgrade_blocked`에 `level` 필드가 추가됐고 `cost`는 **그 레벨의 실제 가격**이다.
`SleepSequence`/`BedInteractable`의 노드 수 집계는 여전히 '해금한 노드 종류 수'라 레벨 업으로는 늘지 않는다.
