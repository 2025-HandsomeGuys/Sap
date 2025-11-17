using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class MineralSlotData
{
    public string itemId; // MineralID enum 문자열
    public int quantity;
}

[System.Serializable]
public class MineralInventoryData
{
    public List<MineralSlotData> slots = new List<MineralSlotData>();
}

