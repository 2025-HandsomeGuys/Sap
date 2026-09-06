using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인벤토리 슬롯의 좌클릭/우클릭/마우스 휠 입력을 처리하는 핸들러
/// 기존 Drag & Drop과 함께 작동하며, 좌클릭으로 아이템을 집고, 휠로 수량 조절, 우클릭으로 되돌리는 기능 제공
/// </summary>
public class InventorySlotClickHandler : MonoBehaviour, IPointerClickHandler, IScrollHandler
{
    [Header("참조")]
    public InventoryUI inventoryUI;
    public WarehouseUI warehouseUI;
    public int slotIndex;
    public InventorySlotDragHandler.InventoryType inventoryType;

    private HeldItemManager heldItemManager;

    void Awake()
    {
        heldItemManager = HeldItemManager.Instance;
    }

    /// <summary>
    /// 좌클릭: 아이템을 집은 상태로 만들기
    /// 우클릭: 집은 아이템을 1개씩 되돌리기
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        // 인벤토리가 열려있는지 확인
        bool isOpen = false;
        if (inventoryUI != null)
            isOpen = inventoryUI.IsOpen();
        else if (warehouseUI != null)
            isOpen = (warehouseUI.warehousePanel != null && warehouseUI.warehousePanel.activeSelf);
        
        if (!isOpen)
            return;

        // 드래그 중이면 클릭 무시 (드래그 앤 드롭 우선)
        if (InventorySlotDragHandler.currentDraggingHandler != null)
            return;

        // 슬롯 정보 가져오기
        InventorySlot slot = GetSlotItem();

        // [추가] 더블 클릭 처리 (창고 아이템 모으기)
        if (eventData.clickCount == 2 && eventData.button == PointerEventData.InputButton.Left)
        {
            // 창고 슬롯인지 확인
            bool isWarehouse = (inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals ||
                                inventoryType == InventorySlotDragHandler.InventoryType.WarehouseItems ||
                                inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEquipments ||
                                inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEmpty);

            if (isWarehouse && WarehouseManager.Instance != null)
            {
                // 현재 집고 있는 상태라면(첫 번째 클릭으로 인해), 취소 처리
                if (heldItemManager.IsHoldingItem)
                {
                    // 원래 슬롯으로 되돌리기 (첫 번째 클릭 취소 효과)
                    heldItemManager.ReturnAllToOriginalSlot();
                }

                // 모으기 기능 실행
                WarehouseManager.Instance.GatherItems(slotIndex);
                return;
            }
        }

        // 지하씬 여부 판별
        bool isUnderground = inventoryUI != null && inventoryUI.IsUndergroundScene();

        // 좌클릭 처리
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // [수정] 광물 인벤토리 슬롯 좌클릭(들기) 허용 (휴지통 버리기 기능 지원을 위해)
            /*
            if (inventoryType == InventorySlotDragHandler.InventoryType.Minerals)
            {
                return;
            }
            */

            // [지하씬] 장비 슬롯 집기 차단
            if (isUnderground && inventoryType == InventorySlotDragHandler.InventoryType.Equipments)
            {
                return;
            }

            // [지하씬] 아이템 슬롯 클릭 시 즉시 사용
            if (isUnderground && inventoryType == InventorySlotDragHandler.InventoryType.Items
                && slot != null && slot.item is ItemSO && slot.quantity > 0
                && !heldItemManager.IsHoldingItem)
            {
                UseItemInUnderground(slot);
                return;
            }

            // 1. 이미 집은 아이템이 있으면 드랍 시도 (빈 슬롯이어도 드랍 가능해야 함)
            if (heldItemManager.IsHoldingItem)
            {
                heldItemManager.TryDropItem(slotIndex, inventoryType, inventoryUI, warehouseUI);
            }
            // 2. 집은 아이템이 없으면 아이템 집기 (이때는 슬롯에 템이 있어야 함)
            else if (slot != null && slot.item != null && slot.quantity > 0)
            {
                RectTransform rt = GetComponent<RectTransform>();
                Vector2 size = (rt != null) ? rt.sizeDelta : new Vector2(64, 64);
                heldItemManager.PickUpItem(slot, slotIndex, inventoryType, size, inventoryUI, warehouseUI);
            }
        }
        // 우클릭 처리
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (heldItemManager.IsHoldingItem)
            {
                // 호환 가능한 슬롯인지 확인
                bool isCompatible = IsCompatibleForRightClickDrop();
                if (isCompatible)
                {
                    // 호환 가능한 슬롯 위 우클릭 → 1개씩 내려놓기
                    heldItemManager.ReturnOneOnRightClick();
                }
                else
                {
                    // 비호환 슬롯 위 우클릭 → 전량 되돌리기
                    heldItemManager.ReturnAllToOriginalSlot();
                }
            }
            else if (slot != null && slot.item != null && slot.quantity > 0)
            {
                // [지하씬] 장비 슬롯 우클릭 해제 차단
                if (isUnderground && inventoryType == InventorySlotDragHandler.InventoryType.Equipments)
                {
                    return;
                }

                // [지하씬] 아이템 슬롯 우클릭 시 즉시 사용
                if (isUnderground && inventoryType == InventorySlotDragHandler.InventoryType.Items
                    && slot.item is ItemSO)
                {
                    UseItemInUnderground(slot);
                    return;
                }

                // 아이템을 들고 있지 않을 때 우클릭: 빠른 이동
                bool moveAll = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                int moveAmount = moveAll ? int.MaxValue : 1;
                HandleQuickTransfer(slot, moveAmount);
            }
        }
    }

    /// <summary>
    /// 창고 <-> 인벤토리 빠른 이동 처리
    /// </summary>
    private void TransferFromWarehouseToInventory(InventorySlot slot, int amount)
    {
        // [추가] 창고 -> 인벤토리 이동 가능 여부 체크 (광물만 제한)
        if (slot.item is MineralSO && !WarehouseManager.Instance.canWithdrawToInventory)
        {
            Debug.Log("창고에서 광물을 꺼낼 수 없는 상태입니다 (토글 꺼짐).");
            return;
        }
        
        // 이동할 실제 수량 계산 (전체 이동 시 현재 슬롯 수량만큼)
        int quantityToMove = (amount == int.MaxValue) ? slot.quantity : amount;
        
        bool success = false;

        if (slot.item is ItemSO itemSO && InventoryUI.Instance.itemInventory != null)
        {
            success = WarehouseManager.Instance.WithdrawToInventory(InventoryUI.Instance.itemInventory, itemSO, quantityToMove);
        }
        else if (slot.item is MineralSO mineralSO && InventoryUI.Instance.mineralInventory != null)
        {
            success = WarehouseManager.Instance.WithdrawToMineralInventory(InventoryUI.Instance.mineralInventory, mineralSO, quantityToMove);
        }
        else if (slot.item is EquipmentSO equipmentSO && InventoryUI.Instance.equipmentInventory != null)
        {
            // EquipAt을 사용하여 스왑 지원 (기존 장비는 창고로 반환)
            // 유물은 슬롯이 2개라 '빈 칸 우선'으로 자리를 골라야 두 번째 유물이 첫 칸을 덮어쓰지 않는다
            int targetSlotIdx = InventoryUI.Instance.equipmentInventory.FindSlotForAdd(equipmentSO);
            if (targetSlotIdx >= 0)
            {
                EquipmentSO previous;
                if (InventoryUI.Instance.equipmentInventory.EquipAt(targetSlotIdx, equipmentSO, out previous))
                {
                    WarehouseManager.Instance.RemoveItemAt(slotIndex, 1);
                    if (previous != null)
                    {
                        WarehouseManager.Instance.AddEquipment(previous, 1);
                    }
                    success = true;
                }
            }
        }

        if (success)
        {
            // Debug.Log($"[QuickTransfer] Moved {quantityToMove} {slot.item.DisplayName} from Warehouse to Inventory");
        }
    }

    private void HandleQuickTransfer(InventorySlot slot, int amount)
    {
        if (slot == null || slot.item == null) return;

        // [최우선] 장비 장착/해제는 창고 열림 여부와 무관하게 처리
        // 1-A. 창고 장비 → 장비칸 (장착)
        if (inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEquipments && slot.item is EquipmentSO eqFromWarehouse)
        {
            TransferFromWarehouseToInventory(slot, amount);
            return;
        }
        // 1-B. 아이템 인벤토리의 장비 → 장비칸 (장착) — 창고 열림 여부 무관
        if (inventoryType == InventorySlotDragHandler.InventoryType.Items && slot.item is EquipmentSO eqItem)
        {
            if (InventoryUI.Instance != null && InventoryUI.Instance.equipmentInventory != null)
            {
                int targetSlotIdx = InventoryUI.Instance.equipmentInventory.FindSlotForAdd(eqItem);
                if (targetSlotIdx >= 0)
                {
                    EquipmentSO previous;
                    if (InventoryUI.Instance.equipmentInventory.EquipAt(targetSlotIdx, eqItem, out previous))
                    {
                        InventoryUI.Instance.itemInventory.RemoveItem(eqItem, 1);
                        if (previous != null && WarehouseManager.Instance != null)
                        {
                            WarehouseManager.Instance.AddEquipment(previous, 1);
                        }
                    }
                }
            }
            return;
        }
        // 1-C. 장비칸 → 창고 (해제) — 장비는 항상 창고로 이동
        if (inventoryType == InventorySlotDragHandler.InventoryType.Equipments && slot.item is EquipmentSO eqEquipped)
        {
            if (WarehouseManager.Instance != null)
            {
                WarehouseManager.Instance.AddEquipment(eqEquipped, 1);
                InventoryUI.Instance.equipmentInventory.RemoveItem(eqEquipped, 1);
            }
            return;
        }

        // 2. Warehouse -> Inventory 이동 (장비 외 아이템)
        if (inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals ||
            inventoryType == InventorySlotDragHandler.InventoryType.WarehouseItems)
        {
            TransferFromWarehouseToInventory(slot, amount);
        }
        // 3. Inventory -> Warehouse 이동 (창고가 열려있을 때, 장비 외)
        else if (WarehouseManager.Instance != null && 
                 (inventoryUI != null && inventoryUI.warehouseUI != null && inventoryUI.warehouseUI.warehousePanel.activeSelf)) 
        {
            TransferFromInventoryToWarehouse(slot, amount);
        }
    }

    private void TransferFromInventoryToWarehouse(InventorySlot slot, int amount)
    {
        if (WarehouseManager.Instance == null) return;

        // 이동할 실제 수량 계산
        int quantityToMove = (amount == int.MaxValue) ? slot.quantity : amount;

        // WarehouseManager.AddItem 류는 리스트에 추가하므로 기본적으로 성공함 (무게 제한 등이 없다면)
        
        // WarehouseManager.AddItem 류는 리스트에 추가하므로 기본적으로 성공함 (무게 제한 등이 없다면)
        
        // bool success = true; // WarehouseManager Add logic usually succeeds

        // 플레이어 인벤토리에서 제거 먼저 시도? 아니면 창고에 넣고 제거?
        // 안전하게: 창고에 넣고 -> 성공하면 -> 인벤토리에서 제거
        // 하지만 WarehouseManager.AddItem은 void 반환임. 실패 여부 알 수 없음.
        // 현재 로직상 창고 용량 제한 구현이 명확하지 않으므로 일단 진행.

        if (slot.item is ItemSO itemSO)
        {
            WarehouseManager.Instance.AddItem(itemSO, quantityToMove);
            InventoryUI.Instance.itemInventory.RemoveItem(itemSO, quantityToMove);
        }
        else if (slot.item is MineralSO mineralSO)
        {
            WarehouseManager.Instance.AddMineral(mineralSO, quantityToMove);
            InventoryUI.Instance.mineralInventory.RemoveItem(mineralSO, quantityToMove);
        }
        else if (slot.item is EquipmentSO equipmentSO)
        {
            WarehouseManager.Instance.AddEquipment(equipmentSO, quantityToMove);
            InventoryUI.Instance.equipmentInventory.RemoveItem(equipmentSO, quantityToMove);
        }
    }

    /// <summary>
    /// 현재 슬롯이 우클릭으로 1개 내려놓기 가능한 호환 슬롯인지 확인
    /// 같은 인벤토리 계열이고, 빈 슬롯이거나 같은 아이템이면 호환
    /// </summary>
    private bool IsCompatibleForRightClickDrop()
    {
        if (heldItemManager == null || !heldItemManager.IsHoldingItem) return false;

        var heldItem = heldItemManager.GetHeldItem();
        if (heldItem == null) return false;

        var originalType = heldItemManager.GetOriginalInventoryType();

        // 같은 인벤토리 타입 계열인지 확인
        bool sameFamily = false;

        // 완전히 같은 타입
        if (inventoryType == originalType) sameFamily = true;

        // 창고 내부 간 이동
        bool isThisWarehouse = IsWarehouseType(inventoryType);
        bool isOriginalWarehouse = IsWarehouseType(originalType);
        if (isThisWarehouse && isOriginalWarehouse) sameFamily = true;

        // 창고 <-> 인벤토리 (같은 아이템 계열)
        if ((inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals && originalType == InventorySlotDragHandler.InventoryType.Minerals) ||
            (inventoryType == InventorySlotDragHandler.InventoryType.Minerals && originalType == InventorySlotDragHandler.InventoryType.WarehouseMinerals))
            sameFamily = true;
        if ((inventoryType == InventorySlotDragHandler.InventoryType.WarehouseItems && originalType == InventorySlotDragHandler.InventoryType.Items) ||
            (inventoryType == InventorySlotDragHandler.InventoryType.Items && originalType == InventorySlotDragHandler.InventoryType.WarehouseItems))
            sameFamily = true;
        if ((inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEquipments && originalType == InventorySlotDragHandler.InventoryType.Equipments) ||
            (inventoryType == InventorySlotDragHandler.InventoryType.Equipments && originalType == InventorySlotDragHandler.InventoryType.WarehouseEquipments))
            sameFamily = true;

        // WarehouseEmpty는 모든 창고 관련과 호환
        if (inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEmpty && isOriginalWarehouse) sameFamily = true;
        if (originalType == InventorySlotDragHandler.InventoryType.WarehouseEmpty && isThisWarehouse) sameFamily = true;
        if (inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEmpty) sameFamily = true;

        if (!sameFamily) return false;

        // 이 슬롯이 비어있거나 같은 아이템이면 호환
        InventorySlot slot = GetSlotItem();
        if (slot == null || slot.item == null) return true; // 빈 슬롯
        if (slot.item.Id == heldItem.Id) return true; // 같은 아이템

        return false;
    }

    private bool IsWarehouseType(InventorySlotDragHandler.InventoryType type)
    {
        return type == InventorySlotDragHandler.InventoryType.WarehouseItems ||
               type == InventorySlotDragHandler.InventoryType.WarehouseMinerals ||
               type == InventorySlotDragHandler.InventoryType.WarehouseEquipments ||
               type == InventorySlotDragHandler.InventoryType.WarehouseEmpty;
    }

    /// <summary>
    /// 마우스 휠 입력 처리 (슬롯 위에서만 작동 - 보조 기능)
    /// 주로 HeldItemManager에서 전역으로 처리하지만, 슬롯 위에서도 작동하도록 함
    /// 좌클릭으로 아이템을 집은 직후에도 즉시 작동하도록 원래 슬롯 제한 제거
    /// [수정] 아이템을 집고 있지 않을 때는 부모 ScrollRect로 이벤트를 넘겨 스크롤이 가능하게 함
    /// </summary>
    public void OnScroll(PointerEventData eventData)
    {
        // 인벤토리가 열려있는지 확인
        bool isOpen = false;
        if (inventoryUI != null)
            isOpen = inventoryUI.IsOpen();
        else if (warehouseUI != null)
            isOpen = (warehouseUI.warehousePanel != null && warehouseUI.warehousePanel.activeSelf);
        
        if (!isOpen)
            return;

        // 1. 집은 아이템이 없을 때 -> 스크롤 뷰로 이벤트 전달
        if (!heldItemManager.IsHoldingItem)
        {
            ScrollRect scrollRect = GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.OnScroll(eventData);
            }
            return;
        }

        // 2. 집은 아이템이 있을 때 -> 수량 조절 로직 수행
        // 원래 슬롯인지 확인 (원래 슬롯 위에서만 작동하도록 제한)
        // 좌클릭 직후 즉시 작동하도록 원래 슬롯 위에서만 처리
        if (heldItemManager.GetOriginalSlotIndex() == slotIndex &&
            heldItemManager.GetOriginalInventoryType() == inventoryType)
        {
            // 휠 아래로: 되돌리기
            if (eventData.scrollDelta.y < 0)
            {
                int amount = Mathf.Max(1, Mathf.Abs((int)eventData.scrollDelta.y) * 1);
                heldItemManager.ReturnQuantityOnWheelDown(amount);
            }
            // 휠 위로: 다시 집기
            else if (eventData.scrollDelta.y > 0)
            {
                int amount = Mathf.Max(1, (int)eventData.scrollDelta.y * 1);
                heldItemManager.PickUpQuantityOnWheelUp(amount);
            }
        }
    }

    // 장비 슬롯 인덱스 계산은 EquipmentInventory.FindSlotForAdd로 일원화했다.
    // (유물이 2칸이 되면서 '타입 → 인덱스' 1:1 매핑이 성립하지 않는다)

    /// <summary>
    /// 지하씬에서 아이템 즉시 사용 (스태미나 회복 후 소모)
    /// </summary>
    private void UseItemInUnderground(InventorySlot slot)
    {
        if (slot == null || slot.item == null) return;

        // ItemInventory의 통합 사용 로직 호출 (모든 액티브 효과 적용됨)
        if (inventoryUI != null && inventoryUI.itemInventory != null)
        {
            inventoryUI.itemInventory.UseItemAt(slotIndex);
        }
    }

    /// <summary>
    /// 슬롯 정보 가져오기
    /// </summary>
    private InventorySlot GetSlotItem()
    {
        // WarehouseUI 사용 시 (통합 슬롯 리스트)
        if (warehouseUI != null && WarehouseManager.Instance != null)
        {
            if ((inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals ||
                 inventoryType == InventorySlotDragHandler.InventoryType.WarehouseItems ||
                 inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEquipments ||
                 inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEmpty) &&
                slotIndex >= 0 && slotIndex < WarehouseManager.Instance.AllSlots.Count)
            {
                return WarehouseManager.Instance.AllSlots[slotIndex];
            }
            return null;
        }

        // InventoryUI 사용 시
        if (inventoryUI == null) return null;

        switch (inventoryType)
        {
            case InventorySlotDragHandler.InventoryType.Items:
                if (inventoryUI.itemInventory != null && slotIndex < inventoryUI.itemInventory.ReadonlyItems.Count)
                    return inventoryUI.itemInventory.ReadonlyItems[slotIndex];
                break;
            case InventorySlotDragHandler.InventoryType.Minerals:
                if (inventoryUI.mineralInventory != null && slotIndex < inventoryUI.mineralInventory.ReadonlyItems.Count)
                    return inventoryUI.mineralInventory.ReadonlyItems[slotIndex];
                break;
            case InventorySlotDragHandler.InventoryType.Equipments:
                if (inventoryUI.equipmentInventory != null && slotIndex < inventoryUI.equipmentInventory.ReadonlyItems.Count)
                    return inventoryUI.equipmentInventory.ReadonlyItems[slotIndex];
                break;
        }
        return null;
    }
}

