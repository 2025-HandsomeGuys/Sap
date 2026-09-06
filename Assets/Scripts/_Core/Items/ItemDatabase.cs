// @tags: item, database, scriptable-object, so, registry
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Database/Item Database")]
public class ItemDatabase : ScriptableObject
{
    public static ItemDatabase Instance { get; private set; }

    public List<ItemSO> allItems;

    private Dictionary<ItemID, ItemSO> itemDictionary;

    private void OnEnable()
    {
        Instance = this;

        itemDictionary = new Dictionary<ItemID, ItemSO>();
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

    public ItemSO GetItemByID(ItemID id)
    {
        if (itemDictionary == null)
        {
            OnEnable();
        }

        itemDictionary.TryGetValue(id, out ItemSO item);
        return item;
    }
}
