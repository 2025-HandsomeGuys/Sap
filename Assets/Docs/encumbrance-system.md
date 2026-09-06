# 과적(Encumbrance) 시스템
@tags: encumbrance, weight, system, EncumbranceController, stamina, mineral

**관련 파일**
- `Assets/Scripts/UI/Player/EncumbranceController.cs` — 핵심 로직
- `Assets/Scripts/UI/Player/PlayerController.cs` — `encumbranceMultiplier` 필드
- `Assets/Scripts/UI/Inventory/MineralInventory.cs` — 광물 무게 / 최대 무게 기준
- `Assets/Scripts/UI/Inventory/ItemInventory.cs` — 아이템 무게 합산용 `TotalWeight`
- `Assets/Scripts/UI/Player/Stats/PlayerStat.cs` — `EncumberedSpeedMultiplier` 스탯
- `Assets/Scripts/UI/Player/Stats/StatType.cs` — `EncumberedSpeedMultiplier`, `StaminaRegen`

---

## 개요

플레이어가 광물·아이템을 합산한 무게가 최대 무게의 80% 이상이 되면 **과적 상태**가 된다.
과적 시 이동 속도(지상·경사면·벽타기)가 느려지고 스태미나 회복 속도가 감소한다.

---

## 임계값 계산

```
임계값 = MineralInventory.maxWeightLimit × encumbranceRatio
기본값 = 40f × 0.8f = 32f
```

합산 무게(`TotalWeight = mineralInventory.TotalWeight + itemInventory.TotalWeight`)가 임계값을 초과하면 과적.

---

## 과적 시 효과

| 항목 | 정상 | 과적 |
|---|---|---|
| 이동 속도 | × 1.0 | × `PlayerStat.EncumberedSpeedMultiplier` (기본 0.5) |
| 벽타기 속도 | × 1.0 | × `PlayerStat.EncumberedSpeedMultiplier` (기본 0.5) |
| 스태미나 회복 속도 | × 1.0 | × `encumbranceStaminaRegenMultiplier` (기본 0.5) |

속도 배율(`EncumberedSpeedMultiplier`)은 `PlayerSO`에서 초기화되며 업그레이드 시스템으로 개선 가능.
스태미나 회복 배율은 `EncumbranceController` 인스펙터의 `Encumbrance Stamina Regen Multiplier`에서 조정.

---

## 아키텍처

### EncumbranceController

`IStatProvider`를 구현해 `PlayerStat`에 등록된다.
양쪽 인벤토리의 `OnInventoryChanged` 이벤트를 구독하고, **상태가 바뀔 때만** 처리한다.

```
OnInventoryChanged 발화
  └─ TotalWeight 재계산
  └─ 과적 여부 변화 없으면 → 무시 (매 프레임 처리 없음)
  └─ 변화 있으면:
        ├─ playerController.encumbranceMultiplier 갱신
        ├─ playerStat.MarkDirty() → GetModifiers() 재계산
        └─ OnEncumbranceChanged?.Invoke(isEncumbered)
```

### 속도 적용 경로

`PlayerController`에 `speedMultiplier`와 별도로 `encumbranceMultiplier` 필드가 존재한다.
채굴 전략(`PickaxeStrategy`, `SapStrategy`)이 `speedMultiplier`를 직접 조작하므로,
과적 배율은 독립 필드로 분리해 충돌을 방지한다.

```csharp
// HandleNormalMovement / HandleWallClimbing
velocity = moveInput * moveSpeed * speedMultiplier * encumbranceMultiplier
```

### 스태미나 회복 적용 경로

`StaminaManager.RegenerateStamina()`는 `playerStats.GetFinalValue(StatType.StaminaRegen)`으로
최종 회복량을 계산한다. `EncumbranceController.GetModifiers()`가 과적 시 Percent × 0.5 수정자를
`StaminaRegen`에 제공해 자동으로 절반 속도로 회복된다.

---

## UI 연동 방법

`EncumbranceController`는 UI 구현을 포함하지 않는다.
아래 인터페이스로 외부에서 연결한다.

```csharp
var enc = FindFirstObjectByType<EncumbranceController>();

// 상태 변화 구독 (권장)
enc.OnEncumbranceChanged += (isEncumbered) =>
{
    overloadIcon.SetActive(isEncumbered);
};

// 폴링용 프로퍼티
enc.IsEncumbered        // bool  — 현재 과적 여부
enc.TotalWeight         // float — 현재 합산 무게
enc.EncumbranceThreshold // float — 현재 임계값 (maxWeight × ratio)
```

---

## Unity 설정

플레이어 오브젝트에 `EncumbranceController` 컴포넌트를 추가한다.
인스펙터 참조 슬롯을 비워두면 씬에서 자동 탐색하므로 필수 연결은 없다.

| 인스펙터 필드 | 기본값 | 설명 |
|---|---|---|
| `Encumbrance Ratio` | 0.8 | 최대 무게 대비 과적 임계 비율 |
| `Encumbrance Stamina Regen Multiplier` | 0.5 | 과적 시 스태미나 회복 배율 |
