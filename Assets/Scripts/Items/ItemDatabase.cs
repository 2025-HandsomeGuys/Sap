
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Database/Item Database")]
public class ItemDatabase : ScriptableObject
{
    public static ItemDatabase Instance { get; private set; }

    public List<ItemSO> allItems;

    private Dictionary<MineralID, ItemSO> itemDictionary;

    private void OnEnable()
    {
        Instance = this;

        itemDictionary = new Dictionary<MineralID, ItemSO>();
        if (allItems != null)
        {
            foreach (var item in allItems)
            {
                if (item != null && item.itemID != MineralID.None && !itemDictionary.ContainsKey(item.itemID))
                {
                    itemDictionary.Add(item.itemID, item);
                }
            }
        }
    }

    public ItemSO GetItemByID(MineralID id)
    {
        if (itemDictionary == null)
        {
            OnEnable();
        }

        itemDictionary.TryGetValue(id, out ItemSO item);
        return item;
    }
}
