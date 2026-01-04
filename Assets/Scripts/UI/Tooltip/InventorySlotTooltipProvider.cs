using UnityEngine;

/// <summary>
/// 인벤토리 슬롯의 툴팁 정보를 제공
/// </summary>
public class InventorySlotTooltipProvider : MonoBehaviour, ITooltipProvider
{
    private InventorySlot slot;
    private InventoryUI inventoryUI;
    private InventorySlotDragHandler dragHandler; // 슬롯 인덱스로 최신 슬롯 가져오기

    public void Initialize(InventorySlot slot, InventoryUI inventoryUI)
    {
        this.slot = slot;
        this.inventoryUI = inventoryUI;
        dragHandler = GetComponent<InventorySlotDragHandler>();
    }

    private InventorySlot GetCurrentSlot()
    {
        // 최신 슬롯 정보 가져오기
        if (dragHandler == null || inventoryUI == null) return slot;
        
        int slotIndex = dragHandler.slotIndex;
        InventorySlotDragHandler.InventoryType type = dragHandler.inventoryType;
        
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
            case InventorySlotDragHandler.InventoryType.Tools:
                if (inventoryUI.toolInventory != null && slotIndex < inventoryUI.toolInventory.ReadonlyItems.Count)
                    return inventoryUI.toolInventory.ReadonlyItems[slotIndex];
                break;
        }
        
        return slot; // 폴백
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
            Debug.LogWarning("[InventorySlotTooltipProvider] currentSlot 또는 item이 null입니다.");
            return "";
        }

        var iitem = currentSlot.item;
        string content = "";

        // 설명 가져오기 (타입별로 처리)
        string description = "";
        if (iitem is ItemSO itemSO)
        {
            description = itemSO != null && !string.IsNullOrEmpty(itemSO.description) ? itemSO.description : "";
        }
        else if (iitem is MineralSO mineralSO)
        {
            description = mineralSO != null && !string.IsNullOrEmpty(mineralSO.description) ? mineralSO.description : "";
        }
        else if (iitem is ToolSO toolSO)
        {
            description = toolSO != null && !string.IsNullOrEmpty(toolSO.description) ? toolSO.description : "";
        }

        // 설명이 있으면 추가
        if (!string.IsNullOrEmpty(description))
        {
            content = description;
        }
        else
        {
            // 설명이 없으면 기본 메시지 (디버그용)
            Debug.LogWarning($"[InventorySlotTooltipProvider] {iitem.DisplayName}의 description이 비어있습니다.");
        }

        // 수량 정보
        int qty = currentSlot.quantity;
        if (!string.IsNullOrEmpty(content))
        {
            content += "\n\n";
        }
        content += $"수량: {qty}";

        // 스택 정보
        bool stackable = iitem.Stackable;
        int maxStack = iitem.MaxStackSize;
        if (stackable)
        {
            content += maxStack > 0 ? $" (최대 {maxStack})" : " (스택 가능)";
        }
        else
        {
            content += " (스택 불가)";
        }

        // 아이템의 경우 무게 정보도 표시 (광물과 동일하게)
        if (iitem is ItemSO)
        {
            float weight = iitem.Weight;
            if (weight > 0)
            {
                content += $"\n무게: {weight:0.0}";
            }
        }
        // 광물의 경우 무게 정보
        else if (iitem is MineralSO)
        {
            float weight = iitem.Weight;
            content += $"\n무게: {weight:0.0}";
            
            // 과적 상태 체크
            if (inventoryUI != null && inventoryUI.mineralInventory != null && inventoryUI.mineralInventory.IsEncumbered)
            {
                content += "\n[경고] 과적 상태입니다.";
            }
        }

        return content;
    }
}

