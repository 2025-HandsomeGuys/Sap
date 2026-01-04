using UnityEngine;

/// <summary>
/// 상점 아이템 슬롯의 툴팁 정보를 제공
/// </summary>
public class ShopItemTooltipProvider : MonoBehaviour, ITooltipProvider
{
    private ShopItemData shopItemData;
    private ItemSO itemSO;

    public void Initialize(ShopItemData shopItemData, ItemSO itemSO)
    {
        this.shopItemData = shopItemData;
        this.itemSO = itemSO;
    }

    public string GetTooltipTitle()
    {
        if (itemSO == null) return "";
        return itemSO.DisplayName;
    }

    public string GetTooltipContent()
    {
        if (itemSO == null || shopItemData == null) return "";

        string content = itemSO.description;
        content += $"\n\n판매 가격: {shopItemData.price}골드";
        
        string stockInfo = shopItemData.stock < 0 ? "재고: 무제한" : $"재고: {shopItemData.stock}";
        content += $"\n{stockInfo}";

        return content;
    }
}


