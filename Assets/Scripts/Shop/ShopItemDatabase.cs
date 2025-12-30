using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ShopItemDatabase", menuName = "Database/Shop Item Database")]
public class ShopItemDatabase : ScriptableObject
{
    [Header("판매 가능한 아이템 목록")]
    public List<ShopItemData> shopItems = new List<ShopItemData>();

    public List<ShopItemData> GetAvailableItems()
    {
        return shopItems.FindAll(item => item.isAvailable);
    }

    public ShopItemData GetItemData(ItemID itemID)
    {
        return shopItems.Find(item => item.itemID == itemID);
    }

    public int GetPrice(ItemID itemID)
    {
        var itemData = GetItemData(itemID);
        return itemData != null ? itemData.price : 0;
    }
}







