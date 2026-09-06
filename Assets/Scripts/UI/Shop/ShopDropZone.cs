using UnityEngine;
using UnityEngine.EventSystems;

public class ShopDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("참조")]
    public ShopManager shopManager;
    // public ShopMineralInventoryUI shopMineralInventoryUI; // 제거됨
    public ShopUI shopUI;

    private InterfaceInventoryItem hoveredItem;
    private HeldItemManager heldItemManager;

    void Awake()
    {
        // 자동 참조
        if (shopManager == null)
            shopManager = FindFirstObjectByType<ShopManager>();
        
        if (shopUI == null)
            shopUI = ShopUI.Instance;
        
        heldItemManager = HeldItemManager.Instance;
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (shopManager == null)
            return;

        // 드래그 중인 슬롯 찾기
        InventorySlotDragHandler draggedHandler = InventorySlotDragHandler.currentDraggingHandler;
        
        // 찾지 못했으면 다른 방법으로 찾기
        if (draggedHandler == null && eventData.pointerDrag != null)
        {
            draggedHandler = eventData.pointerDrag.GetComponent<InventorySlotDragHandler>();
        }

        if (draggedHandler == null)
            return;

        // 판매 가능 여부 확인 (창고 광물 또는 인벤토리 광물)
        // 기존에는 WarehouseMinerals만 허용했으나 인벤토리 광물도 추가
        bool canSell = false;
        
        if (draggedHandler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals) canSell = true;
        // 인벤토리 광물(Minerals)도 허용할 경우:
        if (draggedHandler.inventoryType == InventorySlotDragHandler.InventoryType.Minerals) canSell = true;

        if (!canSell)
        {
            Debug.Log("이 아이템은 여기에 판매할 수 없습니다 (광물만 가능).");
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

        // 판매 처리 - 전체 수량을 확인 프롬프트로 판매
        int maxQuantity = slot.quantity;
        
        bool isFromWarehouse = (draggedHandler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals);
        
        ShowSellConfirmation(mineral, maxQuantity, isFromWarehouse, draggedHandler);
    }

    // This helper was looking into ShopMineralInventoryUI, which is removed.
    // If dragging from Warehouse, we don't need this specific UI search as drag handler is self-contained.
    // We'll return null or implement warehouse UI search if strictly needed, but current DragHandler logic covers it.
    private InventorySlotDragHandler FindDraggedSlot()
    {
        // Removed ShopMineralInventoryUI dependency.
        // Standard drag handling via InventorySlotDragHandler static reference usually suffices.
        return null; 
    }

    private InventorySlot GetSlotItem(InventorySlotDragHandler handler)
    {
        if (handler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals)
        {
            if (WarehouseManager.Instance != null && handler.slotIndex >= 0 && handler.slotIndex < WarehouseManager.Instance.AllSlots.Count)
            {
                return WarehouseManager.Instance.AllSlots[handler.slotIndex];
            }
        }
        else if (handler.inventoryType == InventorySlotDragHandler.InventoryType.Minerals)
        {
            // 인벤토리 광물 처리
            if (handler.inventoryUI == null && InventoryUI.Instance != null) handler.inventoryUI = InventoryUI.Instance; // 폴백
            
            if (handler.inventoryUI != null && handler.inventoryUI.mineralInventory != null && 
                handler.slotIndex < handler.inventoryUI.mineralInventory.ReadonlyItems.Count)
            {
                return handler.inventoryUI.mineralInventory.ReadonlyItems[handler.slotIndex];
            }
        }
        return null;
    }

    // 드래그 중인 아이템이 드롭 존 위에 올라왔을 때 가격 미리보기
    // ShopUI에서 패널 전체에 가격을 표시하므로 개별 툴팁은 비활성화
    public void OnPointerEnter(PointerEventData eventData)
    {
        // empty
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // empty
    }

    private void ShowPricePreview(InterfaceInventoryItem item, int quantity)
    {
        // empty - now handled by ShopUI overlay
    }

    /// <summary>
    /// 판매 확인 프롬프트 표시
    /// </summary>
    private void ShowSellConfirmation(MineralSO mineral, int quantity, bool fromWarehouse, InventorySlotDragHandler draggedHandler = null)
    {
        if (shopUI == null || shopUI.confirmationPrompt == null)
        {
            // 확인 프롬프트가 없으면 바로 판매
            int slotIdx = (draggedHandler != null) ? draggedHandler.slotIndex : -1;
            bool success = shopManager.SellItem(mineral, quantity, fromWarehouse, slotIdx);
            if (success)
            {
                Debug.Log($"{mineral.DisplayName} {quantity}개를 판매했습니다. (Source: {(fromWarehouse ? "Warehouse" : "Inventory")})");
                if (draggedHandler != null)
                {
                    draggedHandler.CleanupDragObject();
                }
            }
            return;
        }

        int price = shopManager.priceDatabase.GetPrice(mineral.mineralID);
        int totalGold = price * quantity;
        string message = $"{mineral.DisplayName} {quantity}개를\n{totalGold}골드에 판매하시겠습니까?";

        shopUI.confirmationPrompt.Show(
            title: "판매 확인",
            message: message,
            confirmCallback: () =>
            {
                int slotIdx = (draggedHandler != null) ? draggedHandler.slotIndex : -1;
                bool success = shopManager.SellItem(mineral, quantity, fromWarehouse, slotIdx);
                if (success)
                {
                    Debug.Log($"{mineral.DisplayName} {quantity}개를 판매했습니다. (총 {totalGold}골드)");
                    if (draggedHandler != null)
                    {
                        draggedHandler.CleanupDragObject();
                    }
                }
            }
        );
    }


    private void HidePricePreview()
    {
        TooltipManager tooltipManager = TooltipManager.Instance;
        if (tooltipManager != null)
        {
            tooltipManager.HideTooltip();
        }
    }

    /// <summary>
    /// 좌클릭: 집은 아이템을 판매하기 (Mineral 인벤토리에서만)
    /// </summary>
    /// <summary>
    /// 좌클릭: 집은 아이템을 판매하기 (광물만)
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        // 좌클릭 또는 우클릭 처리
        if (eventData.button != PointerEventData.InputButton.Left && eventData.button != PointerEventData.InputButton.Right)
            return;

        // 집은 아이템이 없으면 무시
        if (heldItemManager == null || !heldItemManager.IsHoldingItem)
            return;

        // 판매 가능 출처 확인 (창고 광물 또는 인벤토리 광물)
        var originalType = heldItemManager.GetOriginalInventoryType();
        bool isFromWarehouse = (originalType == InventorySlotDragHandler.InventoryType.WarehouseMinerals);
        bool isFromInventory = (originalType == InventorySlotDragHandler.InventoryType.Minerals);

        if (!isFromWarehouse && !isFromInventory)
            return;

        // 집은 아이템 가져오기
        InterfaceInventoryItem heldItem = heldItemManager.GetHeldItem();
        int heldQuantity = heldItemManager.GetHeldQuantity();

        if (heldItem == null || heldQuantity <= 0)
            return;

        // 광물인지 확인
        if (!(heldItem is MineralSO mineral))
        {
            Debug.Log("광물만 판매할 수 있습니다.");
            return;
        }

        // 판매 처리
        if (shopManager == null || shopManager.priceDatabase == null || shopManager.playerStats == null)
            return;

        int price = shopManager.priceDatabase.GetPrice(mineral.mineralID);
        if (price <= 0)
        {
            Debug.Log("이 아이템은 판매할 수 없습니다.");
            return;
        }

        int totalGold = price * heldQuantity;
        
        // 아이템 정보 캡처
        MineralSO savedMineral = mineral;
        int savedQuantity = heldQuantity;
        int slotIdx = heldItemManager.GetOriginalSlotIndex();

        // 집은 아이템을 먼저 제거하여 HeldItemManager의 HandleLeftClick이 실행되지 않도록 함
        // 참고: ClearHeldItem()은 UI 상태(마우스 포인터의 아이템)만 지울 뿐, 원본 슬롯의 아이템을 실제로 지우지는 않습니다.
        heldItemManager.ClearHeldItem();
        
        // 확인 프롬프트 표시
        if (shopUI != null && shopUI.confirmationPrompt != null)
        {
            string message = $"{mineral.DisplayName} {heldQuantity}개를\n{totalGold}골드에 판매하시겠습니까?";
            
            shopUI.confirmationPrompt.Show(
                title: "판매 확인",
                message: message,
                confirmCallback: () =>
                {
                    bool success = shopManager.SellItem(savedMineral, savedQuantity, isFromWarehouse, slotIdx);
                    if (success)
                    {
                        Debug.Log($"{savedMineral.DisplayName} {savedQuantity}개를 판매했습니다. (총 {totalGold}골드) - Source: Hand({(isFromWarehouse ? "Warehouse" : "Inventory")})");
                    }
                },
                cancelCallback: () =>
                {
                    // 취소 시 UI 초기화(ClearHeldItem)만 진행되었고 슬롯에서 아이템을 제거한 적이 없으므로,
                    // 추가적인 복구 코드가 필요 없습니다. 원래 위치에 그대로 남아있게 됩니다.
                    Debug.Log("판매가 취소되어 아이템이 원래 슬롯에 유지됩니다.");
                }
            );
        }
        else
        {
            // 프롬프트가 없으면 바로 판매
            bool success = shopManager.SellItem(savedMineral, savedQuantity, isFromWarehouse, slotIdx);
            if (success)
            {
                Debug.Log($"{mineral.DisplayName} {heldQuantity}개를 판매했습니다. (총 {totalGold}골드)");
            }
        }
    }
}

