using System.Collections.Generic;

/// <summary>
/// 장비 슬롯 데이터 (저장용)
/// </summary>
[System.Serializable]
public class EquipmentSlotData
{
    public string itemId; // EquipmentID enum 문자열
    public int quantity;
}

/// <summary>
/// 장비 인벤토리 데이터 (저장용)
/// </summary>
[System.Serializable]
public class EquipmentInventoryData
{
    public List<EquipmentSlotData> slots = new List<EquipmentSlotData>();
}
