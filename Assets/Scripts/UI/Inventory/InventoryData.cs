using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class InventorySlotData
{
    public string kind;
    public string itemId; // 아이템 ID (직접 ScriptableObject 저장 불가)
    public int quantity;
}

[System.Serializable]
public class InventoryData
{
    public List<InventorySlotData> slots = new List<InventorySlotData>();
}