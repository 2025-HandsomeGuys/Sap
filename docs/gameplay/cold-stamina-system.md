# 추위 지형 스테미나 감소 시스템
@tags: stamina, cold, zone-effect, IZoneEffect, frostbite, layer, system

## 개요

지하 깊이에 따라 플레이어의 MaxStamina가 지속적으로 줄어드는 환경 위험 시스템.
`PlayerZoneChecker`가 플레이어 위치를 주기적으로 확인하고, 해당 지층의 `maxStaminaReduction` 값만큼 `StaminaManager.AddFrostbite()`를 호출해 동상을 누적시킨다.

---

## 관련 파일

| 파일 | 역할 |
|------|------|
| `Assets/Scripts/UI/Player/PlayerZoneChecker.cs` | 플레이어 현재 위치의 지층을 감지하고 동상 누적 적용 |
| `Assets/Scripts/UI/Player/StaminaManager.cs` | 동상(frostbite) 수치 관리 → MaxStamina Flat 감소 제공 |
| `Assets/Scripts/UI/Player/Stats/PlayerStat.cs` | 실제 스테미나 수치(CurrentStamina, MaxStamina) 보유 |
| `Assets/Scripts/_Core/Managers/TileDataManager.cs` | 깊이(Y좌표)에 따른 지층 타입 조회, 스테미나 감소값 제공 |
| `Assets/Scripts/_Core/Data/TileDataModels.cs` | `TileDataJson` 데이터 구조 정의 (`maxStaminaReduction` 필드 포함) |
| `Assets/StreamingAssets/tileData.json` | 지층별 실제 수치 데이터 |
| `Assets/Scripts/UI/FrostbiteOverlayUI.cs` | 동상 누적에 따른 화면 가장자리 비네트 효과 |

---

## 동작 흐름

```
[매 checkInterval(1초)마다]
     |
     v
PlayerZoneChecker.ZoneCheckCoroutine()
     |
     | transform.position.y / 10f => playerGridY (그리드 Y 좌표)
     |
     v
TileDataManager.GetTileTypeAtDepth(playerGridY)
     |-- tileData.json의 tiles 배열을 startDepth 기준으로 순회
     |-- playerGridY <= startDepth 인 첫 번째 타일 반환
     |
     v
TileDataManager.GetData(currentTileType)
     |-- 해당 지층의 TileDataJson 반환
     |-- maxStaminaReduction 값 추출
     |
     v
StaminaManager.AddFrostbite(maxStaminaReduction * checkInterval)
     |-- frostbite 수치 누적
     |-- PlayerStat.MarkDirty() 호출
     |-- GetModifiers()에서 MaxStamina에 -frostbite Flat 적용
```

---

## 지층별 스테미나 감소값

| 지층 (TileType) | startDepth (그리드 Y) | maxStaminaReduction | 초당 누적량 |
|-----------------|----------------------|---------------------|------------|
| Dirt (1층)      | 0 이상               | 0.1                 | 0.1/초     |
| Ice (2층)       | -20 이하             | 0.5                 | 0.5/초     |
| MagmaRock (3층) | -40 이하             | 1.0                 | 1.0/초     |
| MeteoriteRock (4층) | -60 이하         | 2.0                 | 2.0/초     |

> `checkInterval = 1.0f` 기준. 실제 누적량 = `maxStaminaReduction * checkInterval`

---

## 핵심 코드 설명

### PlayerZoneChecker — 지층 감지 및 동상 누적

```csharp
// 플레이어 월드 Y -> 그리드 Y 변환
// 청크 높이 10f = 1000px / 100ppu
int playerGridY = Mathf.RoundToInt(transform.position.y / 10f);

// 지층이 바뀌었을 때만 UpdateZoneEffects 호출 (불필요한 재조회 방지)
if (playerGridY != _lastCheckedGridY)
{
    UpdateZoneEffects(playerGridY);
    _lastCheckedGridY = playerGridY;
}

// 매 체크마다 동상 누적 → MaxStamina 감소
if (_currentStaminaReduction > 0)
{
    float frostbiteToApply = _currentStaminaReduction * checkInterval;
    _staminaManager.AddFrostbite(frostbiteToApply);
}
```

### StaminaManager — 동상을 MaxStamina 감소로 변환

```csharp
public void AddFrostbite(float amount)
{
    frostbite += amount;
    playerStats.MarkDirty(); // 다음 GetFinalValue 호출 시 재계산
}

public IReadOnlyList<StatModifier> GetModifiers()
{
    float totalReduction = injury + burn + frostbite + diggingReduction;
    if (totalReduction > 0)
        modifiers.Add(new StatModifier(StatType.MaxStamina, ModifierType.Flat, -totalReduction, ...));
    return modifiers;
}
```

---

## 설정 방법

### Inspector 설정 (PlayerZoneChecker)

| 필드 | 기본값 | 설명 |
|------|--------|------|
| `checkInterval` | 1.0초 | 지층 체크 및 동상 누적 주기 |

### tileData.json 수정

```json
{
  "tileType": "Ice",
  "maxStaminaReduction": 0.5,
  "startDepth": -20
}
```

- `maxStaminaReduction`: 1초당 누적되는 동상 수치 (checkInterval이 1.0일 때 = 직접 초당 누적값)
- `startDepth`: 이 지층이 시작되는 그리드 Y 좌표 (음수 = 지하)

---

## 주의사항

- `PlayerZoneChecker`는 같은 오브젝트에 `StaminaManager` 컴포넌트가 있어야 동작
- `frostbite`는 자동으로 회복되지 않음 — 회복이 필요한 시점에 `StaminaManager.RecoverStatus()` 호출 필요
- 그리드 Y 변환에 사용되는 `10f` 값은 청크 높이(1000px / 100ppu)에서 유래 — 청크 크기가 바뀌면 이 값도 수정 필요
