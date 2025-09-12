using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    private string path;

    [Header("기본값 설정용 SO")]
    public PlayerSO playerSO;

    private PlayerStatsController stats; // 내부에서 관리
    private InventoryController inventory;

    private void Awake()
    {
        path = Path.Combine(Application.persistentDataPath, "playerData.json");
        stats = FindObjectOfType<PlayerStatsController>(); // 씬에서 자동 찾기
        inventory = FindObjectOfType<InventoryController>();
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

        PlayerData data = stats.ToData();
        data.inventory = inventory.ToData();
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, json);
        Debug.Log("저장 완료: " + path);
    }

    public void Load()
    {
        if (stats == null)
        {
            stats = FindObjectOfType<PlayerStatsController>();
            if (stats == null)
            {
                Debug.LogError("PlayerStatsController를 찾을 수 없습니다!");
                return;
            }
        }

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            PlayerData data = JsonUtility.FromJson<PlayerData>(json);
            stats.FromData(data);
            if (data.inventory != null && data.inventory.slots != null && data.inventory.slots.Count > 0)
            {
                inventory.FromData(data.inventory);
                Debug.Log($"인벤토리 불러오기 완료 (슬롯 개수: {data.inventory.slots.Count})");
            }
            else
            {
                inventory.FromData(new InventoryData()); // 빈 인벤토리로 초기화
                Debug.Log("저장된 인벤토리가 없어 빈 인벤토리로 초기화됨");
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