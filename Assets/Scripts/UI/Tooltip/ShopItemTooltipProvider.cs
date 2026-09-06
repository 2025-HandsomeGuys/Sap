using UnityEngine;

/// <summary>
/// 상점 아이템 슬롯의 툴팁 정보를 제공
/// </summary>
public class ShopItemTooltipProvider : MonoBehaviour, ITooltipProvider
{
    private ShopItemData shopItemData;
    private InterfaceInventoryItem item;

    [Header("Localization Keys")]
    public string textPriceLabelKey = "tt_price_label";
    public string textStockUnlimitedKey = "ui_shop_stock_unlimited";
    public string textStockFormatKey = "ui_shop_stock_format";

    public void Initialize(ShopItemData shopItemData, InterfaceInventoryItem item)
    {
        this.shopItemData = shopItemData;
        this.item = item;
    }

    public string GetTooltipTitle()
    {
        if (item == null) return "";
        return item.DisplayName;
    }

    public string GetTooltipContent()
    {
        if (item == null || shopItemData == null) return "";

        var lm = LanguageManager.Instance;
        string content = item.Description;
        content += "\n\n" + (lm != null ? lm.LF(textPriceLabelKey, shopItemData.price) : $"판매 가격: {shopItemData.price}골드");
        
        string stockInfo = shopItemData.stock < 0 
            ? (lm?.L(textStockUnlimitedKey) ?? textStockUnlimitedKey) 
            : (lm != null ? lm.LF(textStockFormatKey, shopItemData.stock) : $"재고: {shopItemData.stock}");
        content += $"\n{stockInfo}";

        return content;
    }
}
