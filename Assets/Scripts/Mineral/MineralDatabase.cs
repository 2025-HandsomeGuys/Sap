using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MineralDatabase", menuName = "Database/Mineral Database")]
public class MineralDatabase : ScriptableObject
{
    public static MineralDatabase Instance { get; private set; }

    public List<MineralSO> allMinerals;

    private Dictionary<MineralID, MineralSO> mineralDictionary;

    private void OnEnable()
    {
        Instance = this;

        mineralDictionary = new Dictionary<MineralID, MineralSO>();
        if (allMinerals != null)
        {
            foreach (var mineral in allMinerals)
            {
                if (mineral != null && mineral.mineralID != MineralID.None && !mineralDictionary.ContainsKey(mineral.mineralID))
                {
                    mineralDictionary.Add(mineral.mineralID, mineral);
                }
            }
        }
    }

    public MineralSO GetMineralByID(MineralID id)
    {
        if (mineralDictionary == null)
        {
            OnEnable();
        }

        mineralDictionary.TryGetValue(id, out MineralSO mineral);
        return mineral;
    }
}
