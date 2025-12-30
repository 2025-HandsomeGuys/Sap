using UnityEngine;
using UnityEngine.EventSystems;

public class ShopDropZone : MonoBehaviour, IDropHandler
{
    [Header("참조")]
    public ShopManager shopManager;
    public InventoryUI inventoryUI;
    public ShopUI shopUI;

    void Awake()
    {
        // 자동 참조
        if (shopManager == null)
            shopManager = FindFirstObjectByType<ShopManager>();
        if (inventoryUI == null)
            inventoryUI = FindFirstObjectByType<InventoryUI>();
        if (shopUI == null)
            shopUI = FindFirstObjectByType<ShopUI>();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (shopManager == null || inventoryUI == null)
            return;

        // 드래그 중인 슬롯 찾기
        InventorySlotDragHandler draggedHandler = InventorySlotDragHandler.currentDraggingHandler;
        
        // 찾지 못했으면 다른 방법으로 찾기
        if (draggedHandler == null)
        {
            if (eventData.pointerDrag != null)
            {
                draggedHandler = eventData.pointerDrag.GetComponent<InventorySlotDragHandler>();
            }
            if (draggedHandler == null)
            {
                draggedHandler = FindDraggedSlot();
            }
        }

        if (draggedHandler == null)
            return;

        // 광물 인벤토리에서만 판매 가능
        if (draggedHandler.inventoryType != InventorySlotDragHandler.InventoryType.Minerals)
        {
            Debug.Log("광물만 판매할 수 있습니다.");
            return;
        }

        // 슬롯 정보 가져오기
        InventorySlot slot = GetSlotItem(draggedHandler);
        if (slot == null || slot.item == null)
            return;

        // 광물인지 확인
        if (!(slot.item is MineralSO mineral))
        {
            Debug.Log("광물만 판매할 수 있습니다.");
            return;
        }

        // 판매 처리
        int maxQuantity = slot.quantity;
        QuantityPrompt quantityPrompt = shopUI.quantityPrompt;  
        if (quantityPrompt != null)
        {
            quantityPrompt.Show(
                title: $"판매: {mineral.DisplayName}",
                minValue: 1,
                maxValue: maxQuantity,
                confirmCallback: (selectedQuantity) =>
                {
                    bool success = shopManager.SellItem(mineral, selectedQuantity);
                    if (success)
                    {
                        Debug.Log($"{mineral.DisplayName} {selectedQuantity}개를 판매했습니다.");
                        draggedHandler.CleanupDragObject();
                    }
                },
                info: $"가격: {shopManager.priceDatabase.GetPrice(mineral.mineralID)}골드\n최대 판매 가능: {maxQuantity}개"
            );
        }
        else{
            bool success = shopManager.SellItem(mineral, maxQuantity);
            if (success)
            {
                Debug.Log($"{mineral.DisplayName} {maxQuantity}개를 판매했습니다.");
                draggedHandler.CleanupDragObject();
            }
        }
    }

    private InventorySlotDragHandler FindDraggedSlot()
    {
        if (inventoryUI == null) return null;

        // 광물 인벤토리 컨테이너에서 alpha가 낮은 슬롯 찾기
        Transform container = inventoryUI.mineralSlotContainer;
        if (container == null) return null;

        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            InventorySlotDragHandler handler = child.GetComponent<InventorySlotDragHandler>();
            if (handler != null && handler.inventoryType == InventorySlotDragHandler.InventoryType.Minerals)
            {
                CanvasGroup cg = child.GetComponent<CanvasGroup>();
                if (cg != null && cg.alpha < 1f)
                {
                    return handler;
                }
            }
        }
        return null;
    }

    private InventorySlot GetSlotItem(InventorySlotDragHandler handler)
    {
        if (inventoryUI == null || inventoryUI.mineralInventory == null)
            return null;

        if (handler.slotIndex < inventoryUI.mineralInventory.ReadonlyItems.Count)
        {
            return inventoryUI.mineralInventory.ReadonlyItems[handler.slotIndex];
        }
        return null;
    }
}

