using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MineralDatabase", menuName = "Database/Mineral Database")]
public class MineralDatabase : ScriptableObject
{
    private static MineralDatabase _instance;
    public static MineralDatabase Instance 
    { 
        get
        {
            if (_instance == null)
            {
                // 1. Try to find in Resources
                _instance = Resources.Load<MineralDatabase>("MineralDatabase");
                
                // 2. If valid, initialize
                if (_instance != null)
                {
                    _instance.InitializeDictionary();
                    Debug.Log("[MineralDatabase] Loaded successfully.");
                }
                else
                {
                    Debug.LogError("[MineralDatabase] Failed to load 'MineralDatabase' from Resources! Please ensure the file 'Assets/Resources/MineralDatabase.asset' exists.");
                }
            }
            return _instance;
        }
        private set { _instance = value; }
    }

    public List<MineralSO> allMinerals;

    private Dictionary<MineralID, MineralSO> mineralDictionary;
    private bool _initialized = false;

    private void OnEnable()
    {
        if (_instance == null) _instance = this;
        InitializeDictionary();
    }

    public void InitializeDictionary()
    {
        // _initialized이면서 dictionary도 유효한 경우에만 스킵 (불일치 방지)
        if (_initialized && mineralDictionary != null) return;
        _initialized = false;

        mineralDictionary = new Dictionary<MineralID, MineralSO>();
        if (allMinerals != null)
        {
            foreach (var mineral in allMinerals)
            {
                if (mineral == null) continue;

                if (mineral.mineralID == MineralID.None)
                {
                    Debug.LogWarning($"[MineralDatabase] '{mineral.name}' 광물의 MineralID가 None으로 설정되어 있습니다. 데이터베이스에서 제외됩니다.");
                    continue;
                }

                if (mineral.Icon == null)
                {
                    Debug.LogWarning($"[MineralDatabase] 광물 '{mineral.DisplayName}' ({mineral.mineralID})의 아이콘(Sprite)이 설정되지 않았습니다.");
                }

                if (!mineralDictionary.ContainsKey(mineral.mineralID))
                {
                    mineralDictionary.Add(mineral.mineralID, mineral);
                }
                else
                {
                    Debug.LogWarning($"[MineralDatabase] 중복된 ID 감지: {mineral.mineralID} ({mineral.name}). 기존 항목을 유지합니다.");
                }
            }
        }
        else
        {
            Debug.LogError("[MineralDatabase] allMinerals 리스트가 비어 있습니다! 인스펙터에서 광물들을 등록해주세요.");
        }

        _initialized = true;
    }

    public MineralSO GetMineralByID(MineralID id)
    {
        if (mineralDictionary == null || !_initialized)
        {
            InitializeDictionary();
        }

        if (mineralDictionary == null)
        {
            Debug.LogError($"[MineralDatabase] GetMineralByID({id}) 호출 시 dictionary가 null입니다. 초기화에 실패했습니다.");
            return null;
        }

        mineralDictionary.TryGetValue(id, out MineralSO mineral);
        return mineral;
    }
}
