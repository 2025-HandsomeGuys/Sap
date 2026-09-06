using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ToolData", menuName = "Game Data/Tool Data")]
public class ToolData : ScriptableObject
{
    [Header("Basic Info")]
    public string toolName;
    public ToolID toolID;
    public int baseDamage = 1;

    [System.Serializable]
    public struct TileMultiplier
    {
        public TileType tileType;
        public float radiusMultiplier;
        public float damageMultiplier;
    }

    [Header("Tile Specific Multipliers")]
    public List<TileMultiplier> tileMultipliers = new List<TileMultiplier>();

    // 캐싱용 딕셔너리
    private Dictionary<TileType, TileMultiplier> _multiplierDict;

    public void Initialize()
    {
        if (_multiplierDict != null) return;
        
        _multiplierDict = new Dictionary<TileType, TileMultiplier>();
        foreach (var tm in tileMultipliers)
        {
            _multiplierDict[tm.tileType] = tm;
        }
    }

    public TileMultiplier GetMultiplier(TileType type)
    {
        Initialize();
        if (_multiplierDict.TryGetValue(type, out TileMultiplier mult))
        {
            return mult;
        }
        // 기본값: 배율 1.0 (상성 없음)
        return new TileMultiplier { tileType = type, radiusMultiplier = 1.0f, damageMultiplier = 1.0f };
    }
}
