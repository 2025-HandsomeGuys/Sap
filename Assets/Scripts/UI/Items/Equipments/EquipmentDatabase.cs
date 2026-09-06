using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 장비 데이터베이스 - 모든 장비 SO를 관리
/// </summary>
[CreateAssetMenu(fileName = "EquipmentDatabase", menuName = "Database/Equipment Database")]
public class EquipmentDatabase : ScriptableObject
{
    public static EquipmentDatabase Instance { get; private set; }

    public List<EquipmentSO> allEquipments;

    private Dictionary<EquipmentID, EquipmentSO> equipmentDictionary;

    private void OnEnable()
    {
        Instance = this;

        equipmentDictionary = new Dictionary<EquipmentID, EquipmentSO>();
        if (allEquipments != null)
        {
            foreach (var equipment in allEquipments)
            {
                if (equipment != null && equipment.equipmentID != EquipmentID.None && !equipmentDictionary.ContainsKey(equipment.equipmentID))
                {
                    equipmentDictionary.Add(equipment.equipmentID, equipment);
                }
            }
        }
    }

    public EquipmentSO GetEquipmentByID(EquipmentID id)
    {
        if (equipmentDictionary == null)
        {
            OnEnable();
        }

        equipmentDictionary.TryGetValue(id, out EquipmentSO equipment);
        return equipment;
    }
}
