using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class DropZonePricePreview : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IDropHandler
{
    [Header("UI 요소")]
    public GameObject previewPanel;
    public TextMeshProUGUI previewText;
    public Image previewIcon;

    [Header("참조")]
    public ShopManager shopManager;
    public TooltipManager tooltipManager; // 기존 툴팁 시스템 활용

    private InterfaceInventoryItem hoveredItem;

    void Start()
    {
        if (previewPanel != null)
            previewPanel.SetActive(false);

        // 자동 참조
        if (shopManager == null)
            shopManager = FindFirstObjectByType<ShopManager>();
        if (tooltipManager == null)
            tooltipManager = TooltipManager.Instance;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 드래그 중인 아이템 확인
        InventorySlotDragHandler dragHandler = InventorySlotDragHandler.currentDraggingHandler;
        if (dragHandler != null)
        {
            InventorySlot slot = GetSlotFromDragHandler(dragHandler);
            if (slot != null && slot.item != null)
            {
                hoveredItem = slot.item;
                ShowPricePreview(hoveredItem);
            }
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HidePricePreview();
        hoveredItem = null;
    }

    public void OnDrop(PointerEventData eventData)
    {
        // 드롭 처리 (기존 ShopDropZone 로직 사용)
        HidePricePreview();
    }

    private InventorySlot GetSlotFromDragHandler(InventorySlotDragHandler handler)
    {
        if (handler == null) return null;

        InventoryUI inventoryUI = FindFirstObjectByType<InventoryUI>();
        if (inventoryUI == null) return null;

        int slotIndex = handler.slotIndex;
        InventorySlotDragHandler.InventoryType type = handler.inventoryType;

        switch (type)
        {
            case InventorySlotDragHandler.InventoryType.Minerals:
                if (inventoryUI.mineralInventory != null && 
                    slotIndex < inventoryUI.mineralInventory.ReadonlyItems.Count)
                    return inventoryUI.mineralInventory.ReadonlyItems[slotIndex];
                break;
            case InventorySlotDragHandler.InventoryType.Items:
                if (inventoryUI.itemInventory != null && 
                    slotIndex < inventoryUI.itemInventory.ReadonlyItems.Count)
                    return inventoryUI.itemInventory.ReadonlyItems[slotIndex];
                break;
        }

        return null;
    }

    private void ShowPricePreview(InterfaceInventoryItem item)
    {
        if (item == null || shopManager == null) return;

        // 가격 계산 (MineralPriceDatabase 사용)
        int price = 0;
        if (item is MineralSO mineral && shopManager.priceDatabase != null)
        {
            price = shopManager.priceDatabase.GetPrice(mineral.mineralID);
        }
        else
        {
            // 광물이 아니면 판매 불가
            if (tooltipManager != null)
            {
                tooltipManager.ShowTooltip(
                    item.DisplayName,
                    "이 아이템은 판매할 수 없습니다."
                );
            }
            return;
        }

        if (price <= 0)
        {
            // 가격이 없으면 판매 불가
            if (tooltipManager != null)
            {
                tooltipManager.ShowTooltip(
                    item.DisplayName,
                    "이 아이템은 판매할 수 없습니다."
                );
            }
            return;
        }

        // 툴팁 시스템 사용 또는 직접 표시
        if (tooltipManager != null)
        {
            tooltipManager.ShowTooltip(
                item.DisplayName,
                $"예상 판매가: {price}골드"
            );
        }
        else if (previewPanel != null && previewText != null)
        {
            previewText.text = $"{item.DisplayName}\n예상 판매가: {price}골드";
            if (previewIcon != null && item.Icon != null)
                previewIcon.sprite = item.Icon;
            previewPanel.SetActive(true);
        }
    }

    private void HidePricePreview()
    {
        if (tooltipManager != null)
        {
            tooltipManager.HideTooltip();
        }
        else if (previewPanel != null)
        {
            previewPanel.SetActive(false);
        }
    }
}

