using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ToolSlotData
{
    public string itemId; // ToolID enum 문자열
    public int quantity;
}

[System.Serializable]
public class ToolInventoryData
{
    public List<ToolSlotData> slots = new List<ToolSlotData>();
}

