using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 인벤토리/창고의 휴지통 기능 처리 UI
/// </summary>
public class TrashSlotUI : MonoBehaviour, IPointerClickHandler, IDropHandler
{
    [Header("UI 참조")]
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private WarehouseUI warehouseUI;
    [SerializeField] private ConfirmationPrompt confirmationPrompt;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (HeldItemManager.Instance != null && HeldItemManager.Instance.IsHoldingItem)
            {
                TryTrashHeldItem();
            }
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        InventorySlotDragHandler draggedHandler = InventorySlotDragHandler.currentDraggingHandler;
        if (draggedHandler == null && eventData.pointerDrag != null)
        {
            draggedHandler = eventData.pointerDrag.GetComponent<InventorySlotDragHandler>();
        }

        if (draggedHandler != null)
        {
            InventorySlot draggedSlot = draggedHandler.GetSlotItem();
            if (draggedSlot != null && draggedSlot.item != null)
            {
                TryTrashDraggedItem(draggedHandler, draggedSlot.item, draggedSlot.quantity);
            }
        }
    }

    public void TryTrashHeldItem()
    {
        if (HeldItemManager.Instance == null || !HeldItemManager.Instance.IsHoldingItem) return;

        InterfaceInventoryItem item = HeldItemManager.Instance.GetHeldItem();
        int quantity = HeldItemManager.Instance.GetHeldQuantity();
        var invType = HeldItemManager.Instance.GetOriginalInventoryType();

        Debug.Log($"[TrashSlotUI] TryTrashHeldItem called for {item.DisplayName}, Qty: {quantity}, Type: {invType}");

        ProcessTrash(item, quantity, invType, () =>
        {
            Debug.Log("[TrashSlotUI] ProcessTrash onConfirm called (Held Item).");
            HeldItemManager.Instance.TrashHeldItem();
        }, () =>
        {
            Debug.Log("[TrashSlotUI] ProcessTrash onCancel called (Held Item).");
        });
    }

    private void TryTrashDraggedItem(InventorySlotDragHandler handler, InterfaceInventoryItem item, int quantity)
    {
        Debug.Log($"[TrashSlotUI] TryTrashDraggedItem called for {item.DisplayName}, Qty: {quantity}, Type: {handler.inventoryType}");
        ProcessTrash(item, quantity, handler.inventoryType, () =>
        {
            Debug.Log("[TrashSlotUI] ProcessTrash onConfirm called (Dragged Item).");
            RemoveFromInventory(handler, quantity);
            handler.CleanupDragObject();
        }, () =>
        {
            Debug.Log("[TrashSlotUI] ProcessTrash onCancel called (Dragged Item).");
            handler.CleanupDragObject();
        });
    }

    private void ProcessTrash(InterfaceInventoryItem item, int quantity, InventorySlotDragHandler.InventoryType invType, System.Action onConfirm, System.Action onCancel)
    {
        bool isUnderground = inventoryUI != null && inventoryUI.IsUndergroundScene();

        Debug.Log($"[TrashSlotUI] ProcessTrash started. isUnderground: {isUnderground}, ItemType: {item.GetType().Name}");

        // 지하 씬 제약 조건: 광물 인벤토리를 사용하므로 광물만 버리기 가능
        if (isUnderground)
        {
            if (!(item is MineralSO))
            {
                Debug.Log("[TrashSlotUI] 지하에서는 광물만 버릴 수 있습니다.");
                onCancel?.Invoke();
                return;
            }
        }

        // 광물은 확인 절차 없이 즉시 버림
        if (item is MineralSO)
        {
            Debug.Log("[TrashSlotUI] 광물 확인 절차 없이 즉시 버림 수행.");
            onConfirm?.Invoke();
            return;
        }

        // 장비는 유물처럼 고유라 버릴 수 없다.
        if (item is EquipmentSO)
        {
            Debug.Log("[TrashSlotUI] 장비는 버릴 수 없습니다.");
            onCancel?.Invoke();
            return;
        }

        // 아이템 또는 장비는 확인창 표시
        if (confirmationPrompt != null)
        {
            Debug.Log("[TrashSlotUI] 확인창 표시.");
            string titleKey = "ui_trash_confirm_title";
            string title = LanguageManager.Instance?.L(titleKey) ?? "버리기 확인";

            string msg = "";
            if (item is EquipmentSO)
            {
                string formatKey = "ui_trash_confirm_equip_format";
                string format = LanguageManager.Instance?.L(formatKey) ?? "정말 {0} 장비를 버리시겠습니까?\n(버린 장비는 복구할 수 없습니다)";
                msg = string.Format(format, item.DisplayName);
            }
            else
            {
                string formatKey = "ui_trash_confirm_item_format";
                string format = LanguageManager.Instance?.L(formatKey) ?? "정말 {0} {1}개를 버리시겠습니까?\n(버린 아이템은 복구할 수 없습니다)";
                msg = string.Format(format, item.DisplayName, quantity);
            }

            confirmationPrompt.Show(title, msg, () => onConfirm?.Invoke(), () => onCancel?.Invoke());
        }
        else
        {
            Debug.Log("[TrashSlotUI] 확인창 없음. 즉시 버림.");
            // 확인 프롬프트가 할당되지 않은 경우 즉시 버림
            onConfirm?.Invoke();
        }
    }

    private void RemoveFromInventory(InventorySlotDragHandler handler, int quantity)
    {
        Debug.Log($"[TrashSlotUI] RemoveFromInventory called. Type: {handler.inventoryType}, SlotIndex: {handler.slotIndex}");
        if (handler.inventoryType == InventorySlotDragHandler.InventoryType.Items)
        {
            var invUI = handler.inventoryUI != null ? handler.inventoryUI : inventoryUI;
            if (invUI != null && invUI.itemInventory != null)
            {
                invUI.itemInventory.RemoveItemAt(handler.slotIndex, quantity);
            }
        }
        else if (handler.inventoryType == InventorySlotDragHandler.InventoryType.Minerals)
        {
            var invUI = handler.inventoryUI != null ? handler.inventoryUI : inventoryUI;
            if (invUI != null && invUI.mineralInventory != null)
            {
                Debug.Log($"[TrashSlotUI] Calling mineralInventory.RemoveItemAt({handler.slotIndex}, {quantity})");
                bool result = invUI.mineralInventory.RemoveItemAt(handler.slotIndex, quantity);
                Debug.Log($"[TrashSlotUI] mineralInventory.RemoveItemAt result: {result}");
            }
            else
            {
                Debug.LogWarning("[TrashSlotUI] invUI or mineralInventory is null!");
            }
        }
        else if (handler.inventoryType == InventorySlotDragHandler.InventoryType.Equipments)
        {
            var invUI = handler.inventoryUI != null ? handler.inventoryUI : inventoryUI;
            if (invUI != null && invUI.equipmentInventory != null)
            {
                var slotItem = handler.GetSlotItem();
                if (slotItem != null && slotItem.item is EquipmentSO eq)
                {
                    invUI.equipmentInventory.RemoveItem(eq, quantity);
                }
            }
        }
        else if (handler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseItems ||
                 handler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals ||
                 handler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEquipments)
        {
            if (WarehouseManager.Instance != null)
            {
                WarehouseManager.Instance.RemoveItemAt(handler.slotIndex, quantity);
            }
        }
    }
}
