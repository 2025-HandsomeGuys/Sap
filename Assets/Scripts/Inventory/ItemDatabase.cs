
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/Item Database")]
public class ItemDatabase : ScriptableObject
{
    public static ItemDatabase Instance { get; private set; }

    public List<Item> allItems;

    private Dictionary<MineralID, Item> itemDictionary;

    private void OnEnable()
    {
        Instance = this;

        itemDictionary = new Dictionary<MineralID, Item>();
        if (allItems != null)
        {
            foreach (var item in allItems)
            {
                if (item != null && item.MineralID != MineralID.None && !itemDictionary.ContainsKey(item.MineralID))
                {
                    itemDictionary.Add(item.MineralID, item);
                }
            }
        }
    }

    public Item GetItemByID(MineralID id)
    {
        if (itemDictionary == null)
        {
            OnEnable();
        }

        itemDictionary.TryGetValue(id, out Item item);
        return item;
    }
}
