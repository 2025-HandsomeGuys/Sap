# SOLID 기반 광물 줍기 시스템 & 숨겨진 바위 기능
@tags: mineral, pickup, rock, DiggableRock, hidden, SOLID, IPickupable, system

> 작성일: 2026-02-25
> 관련 브랜치: master

---

## 1. 개요

이 문서는 두 가지 큰 작업을 기록한다.

1. **E키 광물 줍기 시스템** — SOLID 원칙 기반으로 리팩터링
2. **숨겨진 바위(Hidden Rock) 노출 기능** — 흙을 파면 아래 바위가 드러나는 인터랙션

---

## 2. E키 광물 줍기 시스템

### 2.1 설계 원칙 (SOLID)

| 원칙 | 적용 내용 |
|------|-----------|
| **SRP** | `PlayerInputHandler`(E키 감지만), `PlayerInteractor`(탐지+명령만), `PickupableItem`(자기 자신 줍기만) |
| **OCP** | `IInteractable`을 구현한 어떤 오브젝트와도 동작. 새 상호작용 추가 시 기존 코드 수정 불필요 |
| **LSP** | `PickupableItem`, `ElevatorController` 등 모두 `IInteractable`로 교체 가능 |
| **ISP** | `IInteractable`(상호작용)과 `IHighlightable`(발광)을 별도 인터페이스로 분리 |
| **DIP** | `PlayerInteractor`는 구체 클래스 대신 `IInteractable`에 의존 |

### 2.2 생성된 파일

#### `Assets/Scripts/_Core/Interfaces/IInteractable.cs` (신규)
```csharp
public interface IInteractable
{
    string InteractionPrompt { get; }
    void Interact(GameObject interactor);
}
```

#### `Assets/Scripts/_Core/Interfaces/IHighlightable.cs` (신규)
```csharp
public interface IHighlightable
{
    void SetHighlighted(bool highlighted);
}
```

#### `Assets/Scripts/UI/Player/PlayerInputHandler.cs` (신규)
- E키 입력만 감지, `OnInteractPressed` 이벤트 발행
- `PlayerInteractor`가 구독

#### `Assets/Scripts/UI/Items/PickupableItem.cs` (신규)
- `IInteractable` + `IHighlightable` 구현
- `ResolveItemData()`: `Mineable.itemData` → `MineralItemController.mineralData` 순 폴백
- `Interact()`: `MineralInventory` 또는 `ItemInventory`에 아이템 추가
- `RemoveFromWorld()`: `mineralID != None`이면 풀 반환, 아니면 `Destroy`
- `CalculatePickupQuantity()`: `UpgradeManager`의 추가 드랍 확률 반영

#### `Assets/Scripts/UI/Interaction/PlayerInteractor.cs` (전면 리팩터링)
- `FindClosestInteractable()`: `Physics2D.OverlapCircle`로 IInteractable 탐지
- `SetTarget()`: 이전 타깃 하이라이트 OFF → 새 타깃 하이라이트 ON
- `PlayerInputHandler` 없을 때 fallback E키 직접 처리
- `IsAnyUIOpen()`: `InventoryUI.IsOpen()` + `ElevatorUI.IsUIOpen` 체크

### 2.3 수정된 파일

#### `Assets/Scripts/UI/Interaction/Elevator/ElevatorController.cs`
- `IInteractable` 구현 추가
- E키 직접 처리 코드 제거 → `PlayerInteractor`가 일괄 처리

### 2.4 발생했던 버그 & 수정

| 버그 | 원인 | 수정 |
|------|------|------|
| `CS1061: ToolSO.DisplayName 없음` | ToolSO가 InterfaceInventoryItem 미구현 | ToolSO 브랜치 제거 |
| E키 무반응 (로그 없음) | PlayerInputHandler 미연결 | PlayerInteractor.Update()에 fallback E키 추가 |
| `itemData is null` | ObjectPooler MineralDatabase 미연결 | ResolveItemData() 폴백 (MineralItemController.mineralData 사용) |
| Pool SoftGround/None 오류 | mineralID가 None인 채로 ReturnToPool 호출 | RemoveFromWorld()에서 None이면 Destroy 처리 |

---

## 3. 광물 발광(Glow) 효과

### 3.1 생성된 파일

#### `Assets/Shaders/SpriteOutline.shader` (신규)
- Built-in RP CGPROGRAM 셰이더
- 8방향 이웃 픽셀 샘플링으로 테두리 검출
- 몸통 픽셀은 투명 처리, 테두리 픽셀만 `OutlineColor * OutlineAlpha`로 렌더링
- `_OutlineAlpha` 프로퍼티로 스크립트에서 맥동 애니메이션 제어

#### `Assets/Scripts/Render/MineralPickupGlow.cs` (신규)
- `IHighlightable` 구현
- `Awake()`에서 자식 SpriteRenderer 두 개 런타임 생성:
  - `__Outline`: sortingOrder+1, SpriteOutline 머티리얼 인스턴스
  - `__Backlight`: sortingOrder-1, `backlightScale` 배율로 확대
- `SetHighlighted(bool)`: `col.isTrigger == false`(낙하 완료)일 때만 활성화
- `OnEnable()`: 풀 재사용 시 하이라이트 초기화
- `Update()`: Sin 파형으로 `_OutlineAlpha` + 백라이트 알파 맥동
- `OnDestroy()`: 머티리얼 인스턴스 메모리 해제

### 3.2 하이라이트 범위 제한

`PlayerInteractor.SetTarget()`이 타깃 변경 시 `IHighlightable`을 호출하므로 **E키 범위 안의 가장 가까운 광물 1개만** 발광한다.

---

## 4. 숨겨진 바위(Hidden Rock) 기능

### 4.1 기능 설명

```
[초기 상태]
  ┌─────────────┐
  │  DirtPatch  │  ← "Dirt" 물리 레이어, 높은 SortingOrder (흙이 바위를 가림)
  │─────────────│
  │  DiggableRock│ ← 흙 아래에 위치, 초기 isHidden=true (충돌 비활성, 캘 수 없음)
  └─────────────┘

[삽으로 흙 파기]
  → Digger가 "Dirt" 레이어 OverlapCircle → IDirtDiggable.DigDirt() 호출

[파괴 후]
  ┌─────────────┐
  │  DiggableRock│ ← Collider2D 활성화 (isHidden=false), 이제 곡괭이(IDiggable)로 캘 수 있음
  └─────────────┘
```

### 4.2 생성된 파일

#### `Assets/Scripts/_Core/Interfaces/IDirtDiggable.cs` (신규)
```csharp
public interface IDirtDiggable
{
    void DigDirt();
}
```
- `IDiggable`(드릴용 암석)과 ISP 준수를 위해 분리된 별도 인터페이스

#### `Assets/Scripts/UI/Items/DirtPatch.cs` (수정)
- `IDirtDiggable` 구현
- `DigDirt()`: `DiggableRock.Reveal()` 호출 → `Destroy(gameObject)`
- `_diggableRock` Inspector 미연결 시 부모/형제에서 자동 탐색

#### `Assets/Scripts/UI/Map/Terrain/Tiles/Decoration/DiggableRock.cs` (수정)
- `isHidden` 프로퍼티 추가
- 초기화 시 `isHidden`이 `true`면 `PolygonCollider2D` 비활성화 — 캘 수 없는 상태
- `Reveal()`: 흙이 파괴될 때 호출됨. `isHidden = false` 및 `PolygonCollider2D.enabled = true`로 변경하여 캘 수 있는 상태로 전환

### 4.3 수정된 파일

#### `Assets/Scripts/UI/Map/Terrain/Tiles/Digger.cs`

삽입 위치: `toolIndex = digParams.ToolIndex;` 직후, `if (digParams.CanDigRock)` 직전

```csharp
// IDirtDiggable 체크: "Dirt" 물리 레이어의 흙 패치를 먼저 감지한다.
int dirtMask = LayerMask.GetMask("Dirt");
Collider2D[] dirtHits = Physics2D.OverlapCircleAll(actualHitPos, effectiveRadius, dirtMask);
foreach (var dirtHit in dirtHits)
{
    if (dirtHit.TryGetComponent(out IDirtDiggable dirtPatch))
    {
        dirtPatch.DigDirt();
        return; // 흙 패치를 파괴했으면 지형(TerrainChunk)은 파지 않는다.
    }
}
```

우선순위 흐름:
1. CanDig 실패 → return
2. CanDigTerrain 실패 → return
3. **"Dirt" 레이어 IDirtDiggable 감지** → `DigDirt()` + return ← 이번에 추가
4. CanDigRock → `IDiggable.Dig()` + return
5. `ModifyTerrain()` (지형 파기)

---

## 5. Unity Editor 설정 가이드

### 5.1 광물 줍기 시스템
- Player GameObject에 `PlayerInputHandler` 컴포넌트 추가
- Player GameObject에 `MineralInventory`, `ItemInventory` 컴포넌트 확인
- 광물 프리팹 루트에 `PickupableItem` + `MineralPickupGlow` 컴포넌트 추가
- `MineralPickupGlow` Inspector → Outline Shader 슬롯에 `Custom/SpriteOutline` 연결

### 5.2 숨겨진 바위
1. **"Dirt" 물리 레이어 생성**: Project Settings → Tags and Layers → Layer 추가
2. **프리팹 구성** (공통 부모 하위):

   | 자식 오브젝트 | 레이어 | SortingOrder | 컴포넌트 |
   |---|---|---|---|
   | `DirtGo` | Dirt | 더 높은 값 | SpriteRenderer, Collider2D, `DirtPatch` |
   | `RockGo` | Default | 더 낮은 값 | SpriteRenderer, PolygonCollider2D, `DiggableRock` |

3. `DirtPatch` Inspector → `_diggableRock` 슬롯에 같은 프리팹의 `DiggableRock` 연결
4. `DiggableRock` Inspector → `isHidden` 체크, `tileType`에 드롭될 광물의 지층 타입 설정

---

## 6. 파일 변경 목록 요약

| 파일 | 상태 |
|------|------|
| `_Core/Interfaces/IInteractable.cs` | 신규 |
| `_Core/Interfaces/IHighlightable.cs` | 신규 |
| `_Core/Interfaces/IDirtDiggable.cs` | 신규 |
| `UI/Player/PlayerInputHandler.cs` | 신규 |
| `UI/Items/PickupableItem.cs` | 신규 |
| `UI/Items/DirtPatch.cs` | 생성 후 DiggableRock 연동으로 수정 |
| `Map/Terrain/Tiles/Decoration/DiggableRock.cs` | 숨김/노출 기능 추가 |
| `Render/MineralPickupGlow.cs` | 신규 |
| `Assets/Shaders/SpriteOutline.shader` | 신규 |
| `UI/Interaction/PlayerInteractor.cs` | 전면 리팩터링 |
| `UI/Interaction/Elevator/ElevatorController.cs` | IInteractable 추가, E키 제거 |
| `UI/Map/Terrain/Tiles/Digger.cs` | IDirtDiggable 체크 삽입 |
| `UI/Items/HiddenRock.cs` | 폐기 예정 (DiggableRock으로 대체) |
