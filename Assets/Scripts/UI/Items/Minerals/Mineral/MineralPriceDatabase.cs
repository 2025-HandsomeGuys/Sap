// @tags: mineral, price, database
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
    [Header("⚠ 런타임에 priceData.json이 덮어씀")]
    [SerializeField]
    private List<MineralPriceInfo> prices;

    private Dictionary<MineralID, int> _priceMap;

    private void OnEnable()
    {
        BuildMap();
    }

    private void BuildMap()
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

    /// <summary>
    /// 업그레이드 보너스를 뺀 순수 기저 가격. "비싼 광물"을 판정할 때 쓴다 —
    /// <see cref="GetPrice"/>를 쓰면 판매가 노드를 산 광물이 더 자주 나오는
    /// 의도치 않은 되먹임이 생긴다.
    /// </summary>
    public int GetBasePrice(MineralID mineralID)
    {
        if (_priceMap == null) BuildMap();
        return _priceMap.TryGetValue(mineralID, out int price) ? price : 0;
    }

    public int GetPrice(MineralID mineralID)
    {
        // OnEnable이 아직 안 불린 경로(에디터 로드 직후 등)에서도 안전하게.
        if (_priceMap == null) BuildMap();

        if (!_priceMap.TryGetValue(mineralID, out int price))
            return 0; // Or some default/error value

        // 광물별 판매가 업그레이드(MineralPriceUp)를 여기서 얹는다.
        //
        // 판매·미리보기 경로 4곳(정산·긴급탈출·상점·드롭존)이 전부 이 메서드를 타므로
        // 여기 한 곳만 고치면 표시와 실제 정산이 어긋나지 않는다.
        // 기저 가격(priceData.json)은 그대로 두고 더하기만 한다 — 세이브에 남는 것은
        // 구매한 노드 목록이지 가격이 아니다.
        //
        // UpgradeManager가 없는 경로(에디터 툴·테스트·지상 씬 로드 직전)에서는
        // 보너스 0으로 조용히 지나간다.
        var mgr = UpgradeManager.Instance;
        if (mgr != null) price += mgr.GetMineralPriceBonus(mineralID);

        return price;
    }

    /// <summary>
    /// priceData.json 의 값으로 가격을 덮어쓴다. 목록에 없는 광물은 기존 값을 유지한다.
    /// 설계: Assets/Docs/economy/price-data-json-design.md §5
    /// </summary>
    public void ApplyPriceOverrides(Dictionary<MineralID, int> overrides)
    {
        if (overrides == null) return;
        if (_priceMap == null) BuildMap();

        foreach (var kv in overrides)
            _priceMap[kv.Key] = kv.Value;
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
