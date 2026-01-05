using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ItemSlotData
{
    public string itemId; // ItemID enum 문자열
    public int quantity;
}

[System.Serializable]
public class ItemInventoryData
{
    public List<ItemSlotData> slots = new List<ItemSlotData>();
}

