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
        return shopItems.Find(item => item.itemType == ShopItemType.Item && item.itemID == itemID);
    }

    public ShopItemData GetItemData(EquipmentID equipmentID)
    {
        return shopItems.Find(item => item.itemType == ShopItemType.Equipment && item.equipmentID == equipmentID);
    }

    public ShopItemData GetItemData(Relic.Data.RelicID relicID)
    {
        return shopItems.Find(item => item.itemType == ShopItemType.Relic && item.relicID == relicID);
    }

    public int GetPrice(Relic.Data.RelicID relicID)
    {
        var itemData = GetItemData(relicID);
        return itemData != null ? itemData.price : 0;
    }

    public int GetPrice(ItemID itemID)
    {
        var itemData = GetItemData(itemID);
        return itemData != null ? itemData.price : 0;
    }

    public int GetPrice(EquipmentID equipmentID)
    {
        var itemData = GetItemData(equipmentID);
        return itemData != null ? itemData.price : 0;
    }
}










