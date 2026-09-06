# 도구 강화 시스템 설정 가이드
@tags: upgrade, tool, UI, setup, guide, UpgradeManager

## 📋 개요
도구 강화 시스템은 플레이어가 골드를 사용하여 도구별로 다양한 강화를 할 수 있는 시스템입니다. **ShopUI에 통합되어** 상점과 업그레이드를 한 곳에서 관리할 수 있습니다. 강인도(채굴 파워)는 **단일 슬롯**에서 레벨업하며, 해금 조건에 따라 자동으로 잠김/해금됩니다.

## 🎯 강화 가능한 항목

### 도구별 강화
- **삽**: 스테미나 소모 감소
- **곡괭이**: 광물 추가 드랍률 증가
- **공통(삽·곡괭이)**: 파는 범위 증가
- **드릴**: 전진 속도 증가, 지속 시간 증가

### 강인도(채굴 파워) - 단일 슬롯에서 레벨업
- **강인도 레벨 1~7**: 하나의 슬롯에서 레벨업 (해금 조건에 따라 자동 잠김/해금)
- 다음 레벨로 업그레이드하려면 모든 도구 강화가 현재 강인도 레벨 이상이어야 함
- 효과: 땅을 판 뒤 다음 땅을 다시 팔 때까지 걸리는 시간 소량 감소
- 다음 단계의 강인도를 업그레이드하면 이전 단계의 땅이 더 쉽게 파짐
- 다음 단계에 걸맞는 강인도를 갖고 있지 않으면 땅이 파지지 않음

## 🔧 Inspector 설정 가이드

### 1. ToolUpgradeUI 설정 (ShopUI에 통합됨)

**위치**: ShopUI의 `Upgrade Content Panel` 내부 또는 별도 GameObject

**컴포넌트**: `ToolUpgradeUI`

**설정 항목**:

#### 참조
- `Player Stats`: `PlayerStatsController` 컴포넌트가 있는 오브젝트 연결 (자동 찾기 가능)

#### UI 요소
- `Gold Text`: 현재 골드를 표시할 TextMeshProUGUI (ShopUI와 공유 가능)
- `Unlock Condition Text`: 해금 조건을 표시할 TextMeshProUGUI

#### 도구 강화 슬롯
각 도구 강화 타입별로 `ToolUpgradeStat` 컴포넌트가 있는 GameObject를 연결:
- `Shovel Stamina Upgrade`: 삽 스테미나 소모 감소
- `Pickaxe Drop Rate Upgrade`: 곡괭이 광물 드랍률 증가
- `Common Dig Range Upgrade`: 공통 파는 범위 증가
- `Drill Speed Upgrade`: 드릴 전진 속도 증가 (드릴 1)
- `Drill Duration Upgrade`: 드릴 지속 시간 증가 (드릴 2)

#### 강인도 슬롯 (단일 슬롯)
**하나의 슬롯만 필요합니다** - 레벨에 따라 자동으로 업그레이드됩니다:
- `Hardness Level Upgrade`: 강인도 (레벨 1~7, 해금 조건에 따라 잠김/해금)

### 2. ToolUpgradeStat 설정 (각 강화 슬롯)

**위치**: 업그레이드 패널 내 각 강화 슬롯 GameObject

**컴포넌트**: `ToolUpgradeStat`

**설정 항목**:

#### References
- `Player Stats`: `PlayerStatsController` 컴포넌트가 있는 오브젝트 연결 (자동 찾기도 가능)
- `Button`: 강화 버튼
- `Cost Text`: 비용을 표시할 TextMeshProUGUI
- `Level Text`: 레벨을 표시할 TextMeshProUGUI
- `Stat Value Text`: 현재 스텟 값을 표시할 TextMeshProUGUI

#### Upgrade Settings
- `Upgrade Type`: 어떤 강화인지 선택
  - ShovelStaminaReduction (삽: 스테미나 소모 감소)
  - PickaxeDropRateIncrease (곡괭이: 광물 드랍률 증가)
  - CommonDigRangeIncrease (공통: 파는 범위 증가)
  - DrillSpeedIncrease (드릴: 전진 속도 증가)
  - DrillDurationIncrease (드릴: 지속 시간 증가)
  - **HardnessLevel** (강인도 - 단일 슬롯, 레벨 1~7 자동 관리)
- `Base Cost`: 첫 강화 비용 (기본값: 10)
- `Cost Increase`: 레벨당 비용 증가량 (기본값: 5)
- `Increase Amount`: 레벨당 증가량 (기본값: 10, UI 표시용)

**강인도 해금 조건**:
- 강인도는 자동으로 해금 조건을 확인합니다
- 다음 레벨로 업그레이드하려면 모든 도구 강화가 현재 강인도 레벨 이상이어야 합니다
- 예: 강인도 레벨 2로 업그레이드하려면 모든 도구 강화가 레벨 1 이상이어야 함

### 3. ToolUpgradeManager 설정

**위치**: 씬의 빈 GameObject (예: "ToolUpgradeManager")

**컴포넌트**: `ToolUpgradeManager`

**설정**: 자동으로 싱글톤으로 작동하며 별도 설정 불필요

### 4. 업그레이드 패널 UI 구성 (ShopUI에 통합)

**권장 레이아웃** (ShopUI의 UpgradeContentPanel 내부):
```
UpgradeContentPanel (GameObject) - ShopUI의 upgradeContentPanel
├── ToolUpgradeUI (GameObject with ToolUpgradeUI)
│   ├── ToolUpgradesArea (왼쪽/오른쪽)
│   │   ├── ShovelUpgradeSlot (GameObject with ToolUpgradeStat)
│   │   ├── PickaxeUpgradeSlot (GameObject with ToolUpgradeStat)
│   │   ├── CommonUpgradeSlot (GameObject with ToolUpgradeStat)
│   │   ├── DrillSpeedUpgradeSlot (GameObject with ToolUpgradeStat)
│   │   └── DrillDurationUpgradeSlot (GameObject with ToolUpgradeStat)
│   ├── HardnessArea (중앙)
│   │   └── HardnessLevelUpgradeSlot (GameObject with ToolUpgradeStat) - 단일 슬롯
│   └── UnlockConditionText (TextMeshProUGUI) - 해금 조건 안내
```

**참고**: 
- `GoldText`는 ShopUI의 `goldText`와 공유할 수 있습니다
- ToolUpgradeUI는 독립적인 패널 관리 없이 ShopUI의 탭 시스템에 통합됩니다

## 🎮 사용 방법

### 업그레이드 창 열기
1. 상점 이미지 클릭하여 상점 창 열기
2. "업그레이드" 탭 클릭
3. 도구 강화 슬롯에서 원하는 강화 선택

### 강화 실행
1. 원하는 도구 강화 버튼 클릭
2. 골드가 차감되고 강화 적용
3. 레벨이 올라가고 다음 강화 비용이 증가

### 강인도 해금
1. 강인도는 **단일 슬롯**에서 레벨업합니다
2. 다음 레벨로 업그레이드하려면:
   - 모든 도구 강화가 현재 강인도 레벨 이상이어야 함
   - 예: 강인도 레벨 2로 업그레이드하려면 모든 도구 강화가 레벨 1 이상
3. 조건을 만족하지 않으면 버튼이 비활성화되고 잠김 상태로 표시됨
4. 강인도 업그레이드 시 다음 단계의 땅을 파괴할 수 있음

## 💾 저장 시스템

도구 강화 레벨은 자동으로 저장됩니다:
- `SaveManager`가 도구 강화 데이터를 `PlayerData`에 포함하여 저장
- 게임을 불러올 때 강화 레벨이 자동으로 복원됨
- 각 강화 슬롯은 `SaveManager`에서 레벨 정보를 불러와서 표시

## ⚙️ 코드 구조

### 주요 클래스

1. **ToolUpgradeType** (enum)
   - 도구별 강화 타입 정의

2. **ToolUpgradeData**
   - 도구 강화 레벨 저장용 데이터 클래스
   - Dictionary를 사용하여 각 타입별 레벨 관리
   - 강인도 레벨 조회 메서드 제공

3. **ToolUpgradeStat**
   - 개별 강화 슬롯 컴포넌트
   - 강화 실행 및 UI 업데이트 담당
   - 강인도 해금 조건 확인

4. **ToolUpgradeUI**
   - 도구 강화 UI 관리 (ShopUI에 통합됨)
   - 골드 표시 및 이벤트 구독
   - 해금 조건 표시
   - 독립적인 패널 관리 없음 (ShopUI의 탭 시스템 사용)

5. **ToolUpgradeManager**
   - 도구 강화 효과를 관리하고 적용
   - 싱글톤 패턴으로 게임 전역에서 접근 가능
   - 강인도 체크 및 보너스 계산

6. **DiggingController** (수정됨)
   - 강인도 체크 추가
   - 삽 강화 효과 적용 (스테미나 소모 감소)
   - 공통 강화 효과 적용 (파는 범위 증가)
   - 강인도 쿨다운 감소 효과 적용

7. **SaveManager** (수정됨)
   - 도구 강화 데이터 저장/불러오기 메서드 추가
   - `GetToolUpgradeData()`, `SetToolUpgradeLevel()`, `GetToolUpgradeLevel()`, `GetHardnessLevel()` 메서드 제공

8. **PlayerData** (수정됨)
   - `toolUpgradeData` 필드 추가
   - 도구 강화 데이터를 저장 파일에 포함

## 🔄 강화 효과

### 삽: 스테미나 소모 감소
- 레벨당 5% 감소
- 예: 레벨 1 = 5% 감소, 레벨 2 = 10% 감소

### 곡괭이: 광물 추가 드랍률 증가
- 레벨당 3% 증가
- 예: 레벨 1 = 3% 증가, 레벨 2 = 6% 증가

### 공통: 파는 범위 증가
- 레벨당 10% 증가
- 예: 레벨 1 = 10% 증가, 레벨 2 = 20% 증가

### 드릴: 전진 속도 증가
- 레벨당 15% 증가

### 드릴: 지속 시간 증가
- 레벨당 20% 증가

### 강인도: 쿨다운 감소
- 레벨당 2% 감소
- 예: 레벨 1 = 2% 감소, 레벨 2 = 4% 감소

## 🎯 강인도 시스템

### 강인도 레벨별 필요 타일
- 레벨 1: Dirt (무른땅)
- 레벨 2: HardStone (단단한땅)
- 레벨 3: CoolStone (서늘한땅)
- 레벨 4: Ice (빙하기땅)
- 레벨 5: HotStone (더운땅)
- 레벨 6: MagmaRock (마그마땅)
- 레벨 7: MeteoriteRock (최종땅)

### 강인도 해금 조건
강인도 레벨 N으로 업그레이드하려면:
1. 현재 강인도 레벨이 N-1 이상이어야 함
2. 모든 도구 강화가 레벨 N-1 이상이어야 함:
   - 삽 강화 ≥ N-1
   - 곡괭이 강화 ≥ N-1
   - 공통 강화 ≥ N-1
   - 드릴 속도 강화 ≥ N-1
   - 드릴 지속 강화 ≥ N-1
3. 조건을 만족하지 않으면 버튼이 비활성화됨

### 강인도 효과
- **필수 조건**: 다음 단계의 강인도를 갖고 있지 않으면 땅이 파지지 않음
- **보너스 효과**: 현재 강인도가 필요한 강인도보다 높으면 파괴 속도 보너스 제공
  - 레벨 차이당 5% 보너스
  - 예: 레벨 3으로 레벨 1 땅 파기 = 10% 보너스

## ⚠️ 주의사항

1. **PlayerStatsController**는 반드시 씬에 있어야 합니다.
2. **SaveManager**가 씬에 있어야 도구 강화 레벨이 저장됩니다.
3. **ToolUpgradeManager**는 자동으로 생성되지만, 수동으로 추가할 수도 있습니다.
4. 각 `ToolUpgradeStat` 컴포넌트의 `Upgrade Type`이 올바르게 설정되어야 합니다.
5. 강인도 슬롯은 `Upgrade Type`을 `HardnessLevel`로 설정하면 자동으로 해금 조건을 확인합니다.
6. 골드가 부족하거나 해금 조건을 만족하지 않으면 버튼이 자동으로 비활성화됩니다.
7. **플레이어 자체 스텟은 아이템이나 장비로 버프 가능하지만 자체 업그레이드는 없습니다.**

## 🐛 문제 해결

### 강화가 작동하지 않을 때
1. `PlayerStatsController`가 올바르게 연결되었는지 확인
2. `SaveManager`가 씬에 있는지 확인
3. `ToolUpgradeManager`가 존재하는지 확인
4. 콘솔에 에러 메시지가 있는지 확인

### 강인도가 해금되지 않을 때
1. 이전 단계의 모든 도구 강화가 완료되었는지 확인
2. 이전 단계의 강인도가 해금되어 있는지 확인
3. `Required Hardness Level`이 올바르게 설정되었는지 확인
4. 해금 조건 텍스트를 확인하여 부족한 강화 확인

### 강인도가 적용되지 않을 때
1. `ToolUpgradeManager.Instance`가 null이 아닌지 확인
2. `DiggingController`가 `ToolUpgradeManager`를 사용하는지 확인
3. 강인도 레벨이 올바르게 저장되었는지 확인

## 📝 예시 코드

### 강화 효과 적용 (코드에서)
```csharp
// 스테미나 소모 감소 적용
float baseStaminaCost = 10f;
float reduction = ToolUpgradeManager.Instance.GetShovelStaminaReduction();
float finalCost = baseStaminaCost * (1f - reduction / 100f);

// 강인도 체크
TileType tileType = TileType.HardStone;
if (ToolUpgradeManager.Instance.CanBreakTile(tileType))
{
    // 파괴 가능
}
else
{
    Debug.Log("강인도가 부족합니다!");
}
```

### 강인도 보너스 적용
```csharp
float baseDigSpeed = 1.0f;
float bonus = ToolUpgradeManager.Instance.GetHardnessBonusForTile(tileType);
float finalSpeed = baseDigSpeed * (1f + bonus / 100f);
```

## 🎨 UI 디자인 팁

1. **강인도 중앙 배치**
   - 강인도 슬롯을 화면 중앙에 배치
   - 도구 강화 슬롯은 좌우에 배치

2. **해금 조건 시각화**
   - 해금 불가능한 강인도는 회색 처리
   - 해금 조건 텍스트로 필요한 강화 표시
   - 진행 상황을 프로그레스 바로 표시 (선택사항)

3. **단계별 그룹화**
   - 각 단계별로 도구 강화와 강인도를 그룹화
   - 단계별 배경색이나 테두리로 구분

## 🔮 확장 가능성

시스템을 확장하여 다음 기능을 추가할 수 있습니다:
- 강화 최대 레벨 제한
- 강화 비용 곡선 커스터마이징
- 강화 프리셋 시스템
- 강화 히스토리/통계
- 강화 효과 미리보기
- 강화 애니메이션/이펙트


