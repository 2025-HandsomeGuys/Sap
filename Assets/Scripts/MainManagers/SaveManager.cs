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

    private void Awake()
    {
        path = Path.Combine(Application.persistentDataPath, "playerData.json");
        stats = FindFirstObjectByType<PlayerStatsController>(); // 씬에서 자동 찾기
        itemInventory = FindFirstObjectByType<ItemInventory>();
        mineralInventory = FindFirstObjectByType<MineralInventory>();
        toolInventory = FindFirstObjectByType<ToolInventory>();
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
                    itemInventory.FromData(data.itemInventory, ItemDatabase.Instance);
                    Debug.Log($"아이템 인벤토리 불러오기 완료 (슬롯 개수: {data.itemInventory.slots.Count})");
                }
                else
                {
                    itemInventory.FromData(new ItemInventoryData(), ItemDatabase.Instance);
                }
            }

            if (mineralInventory != null)
            {
                if (data.mineralInventory != null && data.mineralInventory.slots != null && data.mineralInventory.slots.Count > 0)
                {
                    mineralInventory.FromData(data.mineralInventory, MineralDatabase.Instance);
                    Debug.Log($"광물 인벤토리 불러오기 완료 (슬롯 개수: {data.mineralInventory.slots.Count})");
                }
                else
                {
                    mineralInventory.FromData(new MineralInventoryData(), MineralDatabase.Instance);
                }
            }

            if (toolInventory != null)
            {
                if (data.toolInventory != null && data.toolInventory.slots != null && data.toolInventory.slots.Count > 0)
                {
                    toolInventory.FromData(data.toolInventory, ToolDatabase.Instance);
                    Debug.Log($"도구 인벤토리 불러오기 완료 (슬롯 개수: {data.toolInventory.slots.Count})");
                }
                else
                {
                    toolInventory.FromData(new ToolInventoryData(), ToolDatabase.Instance);
                }
            }

            Debug.Log("불러오기 완료");
        }
        else
        {
            Debug.LogWarning("저장 파일 없음, SO 기본값으로 초기화");
            NewGame();
        }
    }
}