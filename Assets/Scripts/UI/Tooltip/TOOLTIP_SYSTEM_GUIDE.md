# 호버 툴팁 시스템 가이드

## 📋 목차
1. [시스템 개요](#시스템-개요)
2. [구조 설명](#구조-설명)
3. [사용 방법](#사용-방법)
4. [커스터마이징](#커스터마이징)
5. [문제 해결](#문제-해결)

---

## 시스템 개요

### 무엇인가요?
마우스를 슬롯 위에 올리면 상세한 정보를 보여주는 툴팁 시스템입니다.

### 왜 만들었나요?
- 슬롯을 깔끔하게 유지하면서도 상세 정보를 제공
- 인벤토리, 상점, 업그레이드 모두에서 일관된 사용자 경험
- 기존 설명 텍스트 영역을 제거하여 UI 공간 절약

### 작동 방식
1. 마우스를 슬롯 위에 올림 (0.3초 대기)
2. 툴팁이 자동으로 생성되어 표시됨
3. 마우스를 벗어나면 툴팁이 사라짐

---

## 구조 설명

### 핵심 컴포넌트

#### 1. **TooltipManager** (툴팁 매니저)
- **역할**: 툴팁 UI를 관리하는 싱글톤
- **위치**: `Assets/Scripts/UI/Tooltip/TooltipManager.cs`
- **기능**:
  - 툴팁 패널 자동 생성
  - 마우스 위치 추적 및 툴팁 위치 조정
  - 지연 표시 처리 (0.3초)
  - 화면 경계 체크 및 자동 위치 조정

#### 2. **TooltipTrigger** (툴팁 트리거)
- **역할**: 마우스 호버 이벤트 감지
- **위치**: `Assets/Scripts/UI/Tooltip/TooltipTrigger.cs`
- **기능**:
  - `IPointerEnterHandler`: 마우스가 들어올 때
  - `IPointerExitHandler`: 마우스가 나갈 때
  - `ITooltipProvider`와 연동하여 정보 가져오기

#### 3. **ITooltipProvider** (인터페이스)
- **역할**: 툴팁 정보를 제공하는 인터페이스
- **위치**: `Assets/Scripts/UI/Tooltip/ITooltipProvider.cs`
- **메서드**:
  - `GetTooltipTitle()`: 제목 반환
  - `GetTooltipContent()`: 내용 반환

#### 4. **구현체들** (Provider)
각 UI 타입별로 정보를 제공하는 컴포넌트:

- **InventorySlotTooltipProvider**
  - 인벤토리 슬롯 정보 제공
  - 아이템/광물/도구의 이름, 설명, 수량, 스택 정보 등

- **ShopItemTooltipProvider**
  - 상점 아이템 정보 제공
  - 아이템 이름, 설명, 가격, 재고 정보

- **UpgradeTooltipProvider**
  - 업그레이드 슬롯 정보 제공
  - 업그레이드 이름, 설명, 레벨, 효과, 비용, 해금 조건

---

## 사용 방법

### 1단계: TooltipManager 설정 (선택사항)

Unity 에디터에서:
1. 씬에 빈 GameObject 생성
2. 이름을 "TooltipManager"로 변경
3. `TooltipManager` 컴포넌트 추가
4. Inspector에서 설정:
   - `Show Delay`: 툴팁 표시 지연 시간 (기본 0.3초)
   - `Offset X/Y`: 마우스로부터의 오프셋 (기본 10픽셀)

**참고**: TooltipManager가 없으면 자동으로 생성됩니다.

### 2단계: 슬롯에 툴팁 추가

#### 인벤토리 슬롯
**자동 적용됨!** 
- `InventoryUI.SetupSlotUI()` 메서드에서 자동으로 추가됩니다.
- 별도 작업 불필요

#### 상점 슬롯
**자동 적용됨!**
- `ShopUI.SetupShopItemSlot()` 메서드에서 자동으로 추가됩니다.
- 별도 작업 불필요

#### 업그레이드 슬롯
**자동 적용됨!**
- `ToolUpgradeStat.Start()` 메서드에서 자동으로 추가됩니다.
- 별도 작업 불필요

### 3단계: 테스트

1. 게임 실행
2. 인벤토리 열기 (I 키)
3. 아이템 슬롯에 마우스 올리기
4. 0.3초 후 툴팁이 나타나는지 확인
5. 마우스를 벗어나면 툴팁이 사라지는지 확인

---

## 커스터마이징

### 툴팁 스타일 변경

`TooltipManager.CreateTooltipPanel()` 메서드를 수정:

```csharp
// 배경 색상 변경
bg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f); // 검은색 반투명

// 제목 폰트 크기
tooltipTitleText.fontSize = 18;

// 내용 폰트 크기
tooltipContentText.fontSize = 14;
```

### 표시 지연 시간 변경

Inspector에서 `TooltipManager`의 `Show Delay` 값을 변경하거나:

```csharp
public float showDelay = 0.5f; // 0.5초로 변경
```

### 툴팁 위치 조정

```csharp
public float offsetX = 10f; // X 오프셋
public float offsetY = 10f; // Y 오프셋
```

### 새로운 툴팁 Provider 만들기

1. `ITooltipProvider` 인터페이스 구현:

```csharp
public class MyCustomTooltipProvider : MonoBehaviour, ITooltipProvider
{
    public string GetTooltipTitle()
    {
        return "제목";
    }

    public string GetTooltipContent()
    {
        return "내용";
    }
}
```

2. 컴포넌트 추가:

```csharp
MyCustomTooltipProvider provider = gameObject.AddComponent<MyCustomTooltipProvider>();
TooltipTrigger trigger = gameObject.AddComponent<TooltipTrigger>();
trigger.tooltipProvider = provider;
```

---

## 문제 해결

### 툴팁이 나타나지 않아요

**원인 1**: Canvas에 GraphicRaycaster가 없음
- **해결**: Canvas에 `GraphicRaycaster` 컴포넌트 추가

**원인 2**: 슬롯에 Image 컴포넌트가 없음
- **해결**: 슬롯 GameObject에 `Image` 컴포넌트 추가 (투명해도 됨)

**원인 3**: EventSystem이 없음
- **해결**: 씬에 `EventSystem` GameObject 추가 (Unity가 자동으로 추가하지만 확인 필요)

### 툴팁이 화면 밖으로 나가요

**해결**: `TooltipManager.UpdateTooltipPosition()` 메서드가 자동으로 처리합니다.
- 오른쪽 경계를 넘으면 왼쪽에 표시
- 위쪽 경계를 넘으면 아래에 표시

### 툴팁이 너무 빨리/느리게 나타나요

**해결**: `TooltipManager`의 `Show Delay` 값을 조정:
- 빠르게: 0.1초
- 느리게: 0.5초

### 특정 슬롯에만 툴팁이 안 나타나요

**확인 사항**:
1. 슬롯에 `TooltipTrigger` 컴포넌트가 있는지 확인
2. `TooltipTrigger`의 `Tooltip Provider`가 할당되어 있는지 확인
3. Provider의 `GetTooltipTitle()` 또는 `GetTooltipContent()`가 빈 문자열을 반환하지 않는지 확인

---

## 파일 구조

```
Assets/Scripts/UI/Tooltip/
├── TooltipManager.cs              # 툴팁 매니저 (싱글톤)
├── TooltipTrigger.cs              # 호버 이벤트 감지
├── ITooltipProvider.cs             # 툴팁 정보 제공 인터페이스
├── InventorySlotTooltipProvider.cs # 인벤토리 슬롯 정보 제공
├── ShopItemTooltipProvider.cs      # 상점 아이템 정보 제공
└── UpgradeTooltipProvider.cs       # 업그레이드 슬롯 정보 제공
```

---

## 추가 팁

### 툴팁 내용에 색상 추가

TextMeshPro의 Rich Text 기능 사용:

```csharp
content = "<color=yellow>중요한 정보</color>\n일반 텍스트";
```

### 여러 줄 표시

`\n`을 사용하여 줄바꿈:

```csharp
content = "첫 번째 줄\n두 번째 줄\n세 번째 줄";
```

### 조건부 정보 표시

```csharp
if (level > 0)
{
    content += $"\n현재 레벨: {level}";
}
else
{
    content += "\n아직 업그레이드되지 않음";
}
```

---

## 요약

✅ **자동 적용**: 인벤토리, 상점, 업그레이드 슬롯에 자동으로 추가됨  
✅ **간편한 사용**: 마우스만 올리면 자동으로 표시  
✅ **깔끔한 UI**: 슬롯은 간단하게, 상세 정보는 호버 시에만  
✅ **확장 가능**: 새로운 Provider를 쉽게 추가 가능  

문제가 있으면 코드를 확인하거나 디버그 로그를 추가하여 해결하세요!


