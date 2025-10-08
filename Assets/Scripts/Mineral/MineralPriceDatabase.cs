using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class MineralPriceInfo
{
    public MineralID mineralID;
    public int price;
}

[CreateAssetMenu(fileName = "MineralPriceDatabase", menuName = "Database/Mineral Price Database")]
public class MineralPriceDatabase : ScriptableObject
{
    [SerializeField]
    private List<MineralPriceInfo> prices;

    private Dictionary<MineralID, int> _priceMap;

    private void OnEnable()
    {
        _priceMap = new Dictionary<MineralID, int>();
        if (prices == null) return;
        foreach (var item in prices)
        {
            if (!_priceMap.ContainsKey(item.mineralID))
            {
                _priceMap.Add(item.mineralID, item.price);
            }
        }
    }

    public int GetPrice(MineralID mineralID)
    {
        if (_priceMap.TryGetValue(mineralID, out int price))
        {
            return price;
        }
        return 0; // Or some default/error value
    }

    // Editor-only method to populate prices from all MineralSO assets
#if UNITY_EDITOR
    [ContextMenu("Populate Prices From Minerals")]
    private void PopulatePrices()
    {
        if (prices == null)
        {
            prices = new List<MineralPriceInfo>();
        }

        // Find all MineralSO assets in the project
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:MineralSO");
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            MineralSO mineral = UnityEditor.AssetDatabase.LoadAssetAtPath<MineralSO>(path);
            if (mineral != null)
            {
                // If not already in the list, add it with a default price
                if (!prices.Any(p => p.mineralID == mineral.mineralID))
                {
                    prices.Add(new MineralPriceInfo { mineralID = mineral.mineralID, price = 0 });
                }
            }
        }
    }
#endif
}
