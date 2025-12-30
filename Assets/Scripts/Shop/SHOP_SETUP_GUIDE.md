# 상점 시스템 설정 가이드

## 📋 개요
상점 시스템은 광물 판매와 아이템 구매 기능을 제공합니다.

## 🔧 Inspector 설정 가이드

### 1. ShopItemDatabase 생성 및 설정

**경로**: `Assets/Create/Database/Shop Item Database`

1. **ShopItemDatabase** ScriptableObject 생성
2. **shopItems** 리스트에 판매할 아이템 추가:
   - `itemID`: 판매할 아이템 ID 선택
   - `price`: 가격 설정
   - `isAvailable`: 판매 가능 여부 (체크 해제 시 판매 안 함)
   - `stock`: 재고 수 (-1이면 무제한)

### 2. ShopManager 설정

**위치**: 씬의 빈 GameObject 또는 상점 오브젝트

**컴포넌트**: `ShopManager`

**설정 항목**:
- `Price Database`: `MineralPriceDatabase` 에셋 연결
- `Shop Item Database`: 위에서 만든 `ShopItemDatabase` 에셋 연결
- `Player Stats`: `PlayerStatsController` 컴포넌트가 있는 오브젝트 연결
- `Mineral Inventory`: `MineralInventory` 컴포넌트가 있는 오브젝트 연결
- `Item Inventory`: `ItemInventory` 컴포넌트가 있는 오브젝트 연결

### 3. ShopUI 설정

**위치**: 씬의 빈 GameObject (예: "ShopUI")

**컴포넌트**: `ShopUI`

**설정 항목**:

#### UI 패널 연결
- `Shop Panel`: 상점 전체 패널 GameObject
- `Close Button`: 상점 닫기 버튼

#### 상점 참조
- `Shop Manager`: 위에서 설정한 `ShopManager` 연결
- `Inventory UI`: `InventoryUI` 컴포넌트가 있는 오브젝트 연결

#### 판매 영역 UI
- `Sell Drop Zone`: 광물을 드롭할 영역 (RectTransform)
- `Gold Text`: 현재 골드를 표시할 TextMeshProUGUI

#### 구매 영역 UI
- `Buy Item Container`: 구매 아이템 슬롯들이 생성될 컨테이너 (RectTransform)
- `Shop Item Slot Prefab`: 상점 아이템 슬롯 프리팹
- `Item Description Text`: 선택한 아이템 설명을 표시할 TextMeshProUGUI

#### 기타
- `Quantity Prompt`: 수량 입력 프롬프트 (InventoryUI와 동일한 것 사용 가능)

### 4. ShopItemSlotPrefab 생성

**프리팹 구조** (예시):
```
ShopItemSlot (Button)
├── ItemIcon (Image)
├── ItemName (TextMeshProUGUI)
├── PriceText (TextMeshProUGUI)
├── StockText (TextMeshProUGUI)
└── BuyButton (Button)
```

**설정**:
- 루트 GameObject에 `Button` 컴포넌트 추가
- 각 자식 요소의 이름을 위와 같이 설정

### 5. ShopTrigger 설정 (상점 이미지 클릭)

**위치**: 상점 이미지가 있는 GameObject

**컴포넌트**: `ShopTrigger`

**설정 항목**:
- `Shop UI`: 위에서 설정한 `ShopUI` 연결
- `Shop Image`: `SpriteRenderer` 컴포넌트 (자동 감지됨)

**추가 설정**:
- 상점 이미지 GameObject에 `SpriteRenderer` 컴포넌트가 있어야 함
- `OnMouseDown()`이 작동하려면:
  - Camera에 `Physics Raycaster` 컴포넌트 추가 (2D의 경우)
  - 또는 `Collider2D` 추가 (2D 충돌 감지용)

### 6. ShopDropZone 설정

**위치**: 상점 패널 내 판매 영역

**컴포넌트**: `ShopDropZone` (ShopUI에서 자동 추가되지만 수동 설정도 가능)

**설정 항목**:
- `Shop Manager`: `ShopManager` 연결
- `Inventory UI`: `InventoryUI` 연결

**UI 설정**:
- 판매 영역에 `RectTransform` 컴포넌트가 있어야 함
- 배경 이미지나 테두리 추가 권장 (시각적 피드백)

### 7. 상점 패널 UI 구성

**권장 레이아웃**:
```
ShopPanel (Canvas 하위)
├── Header
│   ├── Title (TextMeshProUGUI) - "상점"
│   └── GoldText (TextMeshProUGUI) - 골드 표시
├── SellArea
│   ├── SellTitle (TextMeshProUGUI) - "판매"
│   └── SellDropZone (RectTransform) - 드롭 존
├── BuyArea
│   ├── BuyTitle (TextMeshProUGUI) - "구매"
│   ├── BuyItemContainer (ScrollView > Content) - 아이템 슬롯 컨테이너
│   └── ItemDescriptionText (TextMeshProUGUI) - 아이템 설명
└── CloseButton (Button)
```

## 🎮 사용 방법

### 판매
1. 인벤토리 열기 (I 키)
2. 광물 인벤토리에서 광물을 드래그
3. 상점의 판매 영역(SellDropZone)에 드롭
4. 자동으로 판매 처리

### 구매
1. 상점 이미지 클릭하여 상점 열기
2. 구매하고 싶은 아이템 클릭
3. 구매 버튼 클릭
4. 수량 입력 (또는 1개 자동 구매)
5. 골드가 차감되고 아이템이 인벤토리에 추가됨

### 상점 닫기
- 닫기 버튼 클릭
- ESC 키 누르기

## ⚠️ 주의사항

1. **ShopItemDatabase**는 반드시 생성하고 아이템을 추가해야 구매 기능이 작동합니다.
2. **ShopItemSlotPrefab**의 자식 요소 이름이 정확해야 UI가 제대로 표시됩니다.
3. 상점 이미지 클릭이 작동하지 않으면 Camera에 Physics Raycaster를 추가하세요.
4. 골드 표시가 업데이트되지 않으면 `PlayerStatsController`의 `OnGoldChanged` 이벤트가 제대로 연결되었는지 확인하세요.







