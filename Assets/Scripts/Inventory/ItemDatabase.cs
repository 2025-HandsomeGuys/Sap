
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/Item Database")]
public class ItemDatabase : ScriptableObject
{
    public static ItemDatabase Instance { get; private set; }

    public List<Item> allItems;

    private Dictionary<ItemID, Item> itemDictionary;

    private void OnEnable()
    {
        Instance = this;

        itemDictionary = new Dictionary<ItemID, Item>();
        if (allItems != null)
        {
            foreach (var item in allItems)
            {
                if (item != null && item.itemID != ItemID.None && !itemDictionary.ContainsKey(item.itemID))
                {
                    itemDictionary.Add(item.itemID, item);
                }
            }
        }
    }

    public Item GetItemByID(ItemID id)
    {
        if (itemDictionary == null)
        {
            OnEnable();
        }

        itemDictionary.TryGetValue(id, out Item item);
        return item;
    }
}
