using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    private string path;

    [Header("기본값 설정용 SO")]
    public PlayerSO playerSO;

    private PlayerStatsController stats; // 내부에서 관리
    private ItemInventory itemInventory;
    private MineralInventory mineralInventory;
    private ToolInventory toolInventory;
    private ToolUpgradeData toolUpgradeData; // 도구 강화 데이터 관리

    private void Awake()
    {
        path = Path.Combine(Application.persistentDataPath, "playerData.json");
        stats = FindFirstObjectByType<PlayerStatsController>(); // 씬에서 자동 찾기
        itemInventory = FindFirstObjectByType<ItemInventory>();
        mineralInventory = FindFirstObjectByType<MineralInventory>();
        toolInventory = FindFirstObjectByType<ToolInventory>();
        // toolUpgradeData는 Load()에서 불러오거나 NewGame()에서 초기화하므로 여기서는 초기화하지 않음
        toolUpgradeData = null;
    }

    public bool HasSaveData()
    {
        return File.Exists(path);
    }

    public void NewGame()
    {
        if (playerSO == null)
        {
            Debug.LogError("PlayerSO가 연결되지 않았습니다!");
            return;
        }

        PlayerData data = new PlayerData(playerSO);
        stats.FromData(data);

        // 인벤토리 초기화
        if (itemInventory != null)
        {
            itemInventory.FromData(new ItemInventoryData(), ItemDatabase.Instance);
        }
        if (mineralInventory != null)
        {
            mineralInventory.FromData(new MineralInventoryData(), MineralDatabase.Instance);
        }
        if (toolInventory != null)
        {
            toolInventory.FromData(new ToolInventoryData(), ToolDatabase.Instance);
        }
        
        // 도구 강화 데이터 초기화
        toolUpgradeData = new ToolUpgradeData();
        
        // ToolUpgradeManager에 데이터 동기화 알림
        ToolUpgradeManager upgradeManager = ToolUpgradeManager.Instance;
        if (upgradeManager != null)
        {
            upgradeManager.RefreshUpgradeData();
        }
        
        Save();
        Debug.Log("새 게임 시작");
    }

    public void Save()
    {
        if (stats == null)
        {
            Debug.LogError("PlayerStatsController를 찾을 수 없습니다!");
            return;
        }
        if (itemInventory == null)
        {
            itemInventory = FindFirstObjectByType<ItemInventory>();
        }
        if (mineralInventory == null)
        {
            mineralInventory = FindFirstObjectByType<MineralInventory>();
        }
        if (toolInventory == null)
        {
            toolInventory = FindFirstObjectByType<ToolInventory>();
        }

        PlayerData data = stats.ToData();
        if (itemInventory != null)
        {
            data.itemInventory = itemInventory.ToData();
        }
        if (mineralInventory != null)
        {
            data.mineralInventory = mineralInventory.ToData();
        }
        if (toolInventory != null)
        {
            data.toolInventory = toolInventory.ToData();
        }
        
        // 도구 강화 데이터 저장 (최신 데이터 가져오기)
        ToolUpgradeData upgradeData = GetToolUpgradeData();
        if (upgradeData != null)
        {
            data.toolUpgradeData = upgradeData.ToInventoryData();
            Debug.Log($"도구 강화 데이터 저장 완료 (슬롯 개수: {data.toolUpgradeData.slots.Count})");
        }
        else
        {
            data.toolUpgradeData = new ToolUpgradeInventoryData();
        }
        
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, json);
        Debug.Log("저장 완료: " + path);
    }

    public void Load()
    {
        if (stats == null)
        {
            stats = FindFirstObjectByType<PlayerStatsController>();
            if (stats == null)
            {
                Debug.LogError("PlayerStatsController를 찾을 수 없습니다!");
                return;
            }
        }
        if (itemInventory == null)
        {
            itemInventory = FindFirstObjectByType<ItemInventory>();
        }
        if (mineralInventory == null)
        {
            mineralInventory = FindFirstObjectByType<MineralInventory>();
        }
        if (toolInventory == null)
        {
            toolInventory = FindFirstObjectByType<ToolInventory>();
        }

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            PlayerData data = JsonUtility.FromJson<PlayerData>(json);

            // 플레이어 스탯 복원
            stats.FromData(data);

            // 인벤토리 복원
            if (itemInventory != null)
            {
                if (data.itemInventory != null && data.itemInventory.slots != null && data.itemInventory.slots.Count > 0)
                {
                    if (ItemDatabase.Instance != null)
                    {
                        itemInventory.FromData(data.itemInventory, ItemDatabase.Instance);
                        Debug.Log($"아이템 인벤토리 불러오기 완료 (슬롯 개수: {data.itemInventory.slots.Count})");
                    }
                    else
                    {
                        Debug.LogError("ItemDatabase.Instance가 null입니다. 아이템 인벤토리를 불러올 수 없습니다.");
                        itemInventory.FromData(new ItemInventoryData(), null);
                    }
                }
                else
                {
                    itemInventory.FromData(new ItemInventoryData(), ItemDatabase.Instance);
                }
            }
            else
            {
                Debug.LogWarning("ItemInventory가 null입니다. 아이템 인벤토리를 불러올 수 없습니다.");
            }

            if (mineralInventory != null)
            {
                if (data.mineralInventory != null && data.mineralInventory.slots != null && data.mineralInventory.slots.Count > 0)
                {
                    if (MineralDatabase.Instance != null)
                    {
                        mineralInventory.FromData(data.mineralInventory, MineralDatabase.Instance);
                        Debug.Log($"광물 인벤토리 불러오기 완료 (슬롯 개수: {data.mineralInventory.slots.Count})");
                    }
                    else
                    {
                        Debug.LogError("MineralDatabase.Instance가 null입니다. 광물 인벤토리를 불러올 수 없습니다.");
                        mineralInventory.FromData(new MineralInventoryData(), null);
                    }
                }
                else
                {
                    mineralInventory.FromData(new MineralInventoryData(), MineralDatabase.Instance);
                }
            }
            else
            {
                Debug.LogWarning("MineralInventory가 null입니다. 광물 인벤토리를 불러올 수 없습니다.");
            }

            if (toolInventory != null)
            {
                if (data.toolInventory != null && data.toolInventory.slots != null && data.toolInventory.slots.Count > 0)
                {
                    if (ToolDatabase.Instance != null)
                    {
                        toolInventory.FromData(data.toolInventory, ToolDatabase.Instance);
                        Debug.Log($"도구 인벤토리 불러오기 완료 (슬롯 개수: {data.toolInventory.slots.Count})");
                    }
                    else
                    {
                        Debug.LogError("ToolDatabase.Instance가 null입니다. 도구 인벤토리를 불러올 수 없습니다.");
                        toolInventory.FromData(new ToolInventoryData(), null);
                    }
                }
                else
                {
                    toolInventory.FromData(new ToolInventoryData(), ToolDatabase.Instance);
                }
            }
            else
            {
                Debug.LogWarning("ToolInventory가 null입니다. 도구 인벤토리를 불러올 수 없습니다.");
            }

            // 도구 강화 데이터 불러오기
            if (data.toolUpgradeData != null && data.toolUpgradeData.slots != null && data.toolUpgradeData.slots.Count > 0)
            {
                toolUpgradeData = new ToolUpgradeData();
                toolUpgradeData.FromInventoryData(data.toolUpgradeData);
                Debug.Log($"도구 강화 데이터 불러오기 완료 (슬롯 개수: {data.toolUpgradeData.slots.Count})");
            }
            else
            {
                toolUpgradeData = new ToolUpgradeData();
                Debug.Log("도구 강화 데이터가 없어 새로 초기화합니다.");
            }

            // ToolUpgradeManager에 데이터 동기화 알림
            ToolUpgradeManager upgradeManager = ToolUpgradeManager.Instance;
            if (upgradeManager != null)
            {
                upgradeManager.RefreshUpgradeData();
            }

            Debug.Log("불러오기 완료");
        }
        else
        {
            Debug.LogWarning("저장 파일 없음, SO 기본값으로 초기화");
            NewGame();
        }
    }

    // 도구 강화 데이터 관련 메서드
    public ToolUpgradeData GetToolUpgradeData()
    {
        // toolUpgradeData가 null이면 새로 생성 (하지만 Load()가 호출되기 전에는 빈 데이터)
        if (toolUpgradeData == null)
        {
            toolUpgradeData = new ToolUpgradeData();
        }
        return toolUpgradeData;
    }

    public void SetToolUpgradeLevel(ToolUpgradeType type, int level)
    {
        ToolUpgradeData data = GetToolUpgradeData();
        data.SetLevel(type, level);
        toolUpgradeData = data; // 참조 업데이트
        
        // ToolUpgradeManager에 즉시 동기화
        ToolUpgradeManager upgradeManager = ToolUpgradeManager.Instance;
        if (upgradeManager != null)
        {
            upgradeManager.RefreshUpgradeData();
        }
        
        Save(); // 자동 저장
    }

    public int GetToolUpgradeLevel(ToolUpgradeType type)
    {
        if (toolUpgradeData == null)
        {
            toolUpgradeData = new ToolUpgradeData();
        }
        return toolUpgradeData.GetLevel(type);
    }

    public int GetHardnessLevel()
    {
        if (toolUpgradeData == null)
        {
            toolUpgradeData = new ToolUpgradeData();
        }
        return toolUpgradeData.GetHardnessLevel();
    }
}