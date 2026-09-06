using System.Collections.Generic;

[System.Serializable]
public class WarehouseData
{
    public ItemInventoryData itemInventory = new ItemInventoryData();
    public MineralInventoryData mineralInventory = new MineralInventoryData();
    public EquipmentInventoryData equipmentInventory = new EquipmentInventoryData();
}

