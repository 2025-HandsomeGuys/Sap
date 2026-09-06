# 과부하 배터리(Overload Battery) 유물 설계

`adding-a-relic.md` 가이드 기반. RelicID `OverloadBattery = 4022` (4021 DetectionPulse 다음).

## 1. 정체성

기획: **드릴 대시 때 배터리 효율이 2배 늘어나지만 방향이 무작위로 바뀝니다.**

- 타입: **패시브**. 단, 효과는 **드릴 대시에만** 적용(상시 스탯 아님).
- 이득: 대시 배터리 **소모 절반**(효율 2배) → 더 오래/멀리 대시.
- 대가: 대시 **방향이 마우스 대신 주기적으로 무작위 재추첨** → 통제 불가.
- 레벨 스케일: 효율↑(소모↓) + 재추첨 주기↑(덜 자주 흔들려 다루기 쉬움).
- `maxLevel = 3`.

## 2. 동작 (OnUpdate 불필요)

효과가 드릴 대시 파라미터에만 걸리므로 매 프레임 감시가 필요 없다.
**장착/레벨변경 시 `PlayerMining`의 드릴 전략 토글을 세팅하고, 해제 시 원복**한다.

- `OnEquip` / `OnLevelChanged` → `SetDrillDashDrainMultiplier(Lv)` + `SetDrillDashRandomDirection(true, Lv)`.
- `OnUnequip` → `SetDrillDashDrainMultiplier(1f)` + `SetDrillDashRandomDirection(false)`.

두 토글 모두 **드릴 대시 중에만 소비**되므로(다른 도구/차징엔 무영향), 상시 세팅해도 부작용 없음.

## 3. 게임 코드 훅 (2개 필드, 신규 침습 최소)

효과가 `DrillStrategy` 내부에 있어 유물이 직접 못 건드린다. `DrillStrategy`에 토글 프로퍼티를
추가하고 `PlayerMining`(= `ctx.mining`)이 세터로 중계한다.

### 3-A. `DrillStrategy` 토글 프로퍼티 (기본값 = 무효과)
- `float DashDrainMultiplier = 1f` — `HandleUpdate`의 대시 소모식에 곱: `DASH_BATTERY_DRAIN * DashDrainMultiplier`.
- `bool RandomDashDirection = false` + `float RandomDashInterval = 0.5f` — 대시 중(`_isDrillDashing`) 방향
  계산에서 마우스 `targetAngle`를 무작위 각도로 덮어씀. `RandomDashInterval`마다 `Random.Range(0,360)` 재추첨.
  기존 `SmoothDampAngle` 보간을 그대로 태워 급회전을 부드럽게. `StartDrillDash`에서 조준 방향으로 시드
  → 출발은 조준대로, 한 주기 뒤부터 흐트러짐.
- 차징(대시 전 조준)에는 미적용(`_isDrillDashing` 가드) → 조준은 정상.

### 3-B. `PlayerMining` 중계 세터 + 값 보존
- `SetDrillDashDrainMultiplier(float)` / `SetDrillDashRandomDirection(bool, float)`.
- `_drillStrategy`가 `Start`에서 생성되므로, 그 전에 장착돼도 값을 잃지 않게 백킹 필드에 보관 후
  `Start`에서 전략에 적용.

## 4. 레벨 파라미터 (초기값)

```
drainMultPerLevel      = { 0.5f, 0.42f, 0.33f }   // 효율 2x / ~2.4x / ~3x
randomIntervalPerLevel = { 0.35f, 0.5f, 0.7f }    // 방향 재추첨 주기(s), 클수록 통제↑
randomMinDeltaPerLevel = { 120f, 100f, 80f }      // 재추첨 시 직전 방향 대비 최소 회전각(도), 클수록 더 미쳐날뜀
```

`RandomDashMinDelta`: 재추첨 각도를 직전 목표 각도 기준 `Random.Range(minDelta, 360-minDelta)` 오프셋으로 잡아
원형 거리 최소 `minDelta`를 보장한다(0이면 순수 무작위라 직전과 거의 같은 각이 나올 수 있음). 레벨↑ = 최소각↓로 통제감↑.

## 5. 수정 파일

- **신규**: `Assets/Scripts/Gameplay/Relics/Behaviours/OverloadBatteryRelic.cs`
- **게임 코드 훅**:
  - `UI/Player/Strategies/DrillStrategy.cs` — 토글 프로퍼티 + 소모식 곱 + 무작위 방향 로직 + `StartDrillDash` 시드
  - `UI/Player/PlayerMining.cs` — 백킹 필드 + `Start` 적용 + 중계 세터 2개
- **공유(순차 편집)**:
  - `Data/RelicID.cs` — `OverloadBattery = 4022`
  - `Editor/RelicSliceAssetGenerator.cs` — 등록 1줄 + `db.allRelics` 추가
  - `Gameplay/Relics/Debug/RelicDebugGranter.cs` — `KeypadMultiply` grant+equip, slot 1

## 6. 엣지 케이스

| 케이스 | 처리 |
|--------|------|
| 유물 미장착 | 토글 기본값(1배·off) → 기존 대시와 동일 |
| 드릴 외 도구/차징 | 토글이 대시에서만 소비 → 무영향 |
| 대시 첫 진입 | `StartDrillDash`에서 조준 방향 시드 → 한 주기 뒤 무작위 시작 |
| Start 전 장착 | `PlayerMining` 백킹 필드에 보관 후 Start에서 적용 |
| 해제 중 대시 | `OnUnequip`이 1배·off로 원복 |

## 7. 검증 (사용자 인게임)

1. `Tools > Relic > Generate Slice Assets` 재실행 → `OverloadBattery.asset` + DB 갱신.
2. 지하에서 드릴 대시(우클릭 조준 + 좌클릭) → 방향이 제멋대로 꺾이는지 확인.
3. 미장착 대비 배터리가 약 2배 오래 가는지 확인(효율).
4. `KeypadMultiply`로 재입력해 Lv↑ → 방향이 덜 자주 흔들리고 효율↑ 체감.
5. 곡괭이/삽 전환 시 정상(무영향), 유물 해제 시 대시 정상 복귀.
