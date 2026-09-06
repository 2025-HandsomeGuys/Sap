using UnityEngine;

/// <summary>
/// 인벤토리 슬롯의 툴팁 정보를 제공
/// </summary>
public class InventorySlotTooltipProvider : MonoBehaviour, ITooltipProvider
{
    private InventorySlot slot;
    private InventoryUI inventoryUI;
    private WarehouseUI warehouseUI;

    private InventorySlotDragHandler dragHandler;

    [Header("Localization Keys")]
    public string textQuantityKey = "tt_quantity";
    public string textMaxStackKey = "tt_max_stack";
    public string textStackableKey = "tt_stackable";
    public string textUnstackableKey = "tt_unstackable";
    public string textWeightKey = "tt_weight";
    public string textEncumberedKey = "tt_encumbered";

    public void Initialize(InventorySlot slot, InventoryUI inventoryUI)
    {
        this.slot = slot;
        this.inventoryUI = inventoryUI;
        this.warehouseUI = null;
        dragHandler = GetComponent<InventorySlotDragHandler>();
    }

    public void InitializeForWarehouse(InventorySlot slot, WarehouseUI warehouseUI)
    {
        this.slot = slot;
        this.inventoryUI = null;
        this.warehouseUI = warehouseUI;
        dragHandler = GetComponent<InventorySlotDragHandler>();
    }

    private InventorySlot GetCurrentSlot()
    {
        if (dragHandler == null) return slot;
        
        int slotIndex = dragHandler.slotIndex;
        InventorySlotDragHandler.InventoryType type = dragHandler.inventoryType;

        // WarehouseUI 사용 시
        if (warehouseUI != null && WarehouseManager.Instance != null &&
            (type == InventorySlotDragHandler.InventoryType.WarehouseMinerals ||
             type == InventorySlotDragHandler.InventoryType.WarehouseItems ||
             type == InventorySlotDragHandler.InventoryType.WarehouseEquipments ||
             type == InventorySlotDragHandler.InventoryType.WarehouseEmpty))
        {
            if (slotIndex >= 0 && slotIndex < WarehouseManager.Instance.AllSlots.Count)
            {
                return WarehouseManager.Instance.AllSlots[slotIndex];
            }
            return slot;
        }
        
        // InventoryUI 사용 시
        if (inventoryUI == null) return slot;
        
        switch (type)
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
        
        return slot;
    }

    public string GetTooltipTitle()
    {
        InventorySlot currentSlot = GetCurrentSlot();
        if (currentSlot == null || currentSlot.item == null) return "";
        return currentSlot.item.DisplayName;
    }

    public string GetTooltipContent()
    {
        InventorySlot currentSlot = GetCurrentSlot();
        if (currentSlot == null || currentSlot.item == null) 
        {
            return "";
        }

        var iitem = currentSlot.item;
        var lm = LanguageManager.Instance;
        string content = "";

        // 설명 가져오기
        string description = iitem.Description;
        if (!string.IsNullOrEmpty(description))
        {
            content = description;
        }

        // 수량 정보
        int qty = currentSlot.quantity;
        if (!string.IsNullOrEmpty(content))
        {
            content += "\n\n";
        }
        content += lm != null ? lm.LF(textQuantityKey, qty) : $"수량: {qty}";

        // 스택 정보
        bool stackable = iitem.Stackable;
        int maxStack = iitem.MaxStackSize;
        if (stackable)
        {
            content += maxStack > 0 
                ? (lm != null ? lm.LF(textMaxStackKey, maxStack) : $" (최대 {maxStack})") 
                : (lm?.L(textStackableKey) ?? textStackableKey);
        }
        else
        {
            content += lm?.L(textUnstackableKey) ?? textUnstackableKey;
        }

        // 아이템의 경우 무게 정보
        if (iitem is ItemSO)
        {
            float weight = iitem.Weight;
            if (weight > 0)
            {
                content += "\n" + (lm != null ? lm.LF(textWeightKey, weight) : $"무게: {weight:0.0}");
            }
        }
        // 광물의 경우 무게 정보
        else if (iitem is MineralSO)
        {
            float weight = iitem.Weight;
            content += "\n" + (lm != null ? lm.LF(textWeightKey, weight) : $"무게: {weight:0.0}");
            
            // 과적 상태 체크
            bool isEncumbered = false;
            if (inventoryUI != null && inventoryUI.mineralInventory != null)
                isEncumbered = inventoryUI.mineralInventory.IsEncumbered;
            
            if (isEncumbered)
            {
                content += lm?.L(textEncumberedKey) ?? textEncumberedKey;
            }
        }

        return content;
    }
}
