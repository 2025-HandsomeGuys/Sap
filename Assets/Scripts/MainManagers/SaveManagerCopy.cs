using System.IO;
using UnityEngine;

public class SaveManagerCopy : MonoBehaviour
{
    private string path;

    [Header("Player SO")]
    public PlayerSO playerSO;

    private void Awake()
    {
        path = Path.Combine(Application.persistentDataPath, "playerData_khb_test.json");
    }

    /// <summary>
    /// New Game: Initialize PlayerStat from SO
    /// </summary>
    public void NewGame(PlayerStat stats)
    {
        if (playerSO == null)
        {
            Debug.LogError("PlayerSO is missing!");
            return;
        }

        // Create PlayerData from SO
        PlayerData data = new PlayerData(playerSO);
        stats.FromData(data); // Initialize PlayerStat
        Save(stats); // Save initial values
        Debug.Log("New Game Started (Test)");
    }

    /// <summary>
    /// Save
    /// </summary>
    public void Save(PlayerStat stats)
    {
        PlayerData data = stats.ToData(new PlayerData(playerSO));
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, json);
        Debug.Log("Save Complete: " + path);
    }

    /// <summary>
    /// Load
    /// </summary>
    public void Load(PlayerStat stats)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            PlayerData data = JsonUtility.FromJson<PlayerData>(json);
            Debug.Log(Application.persistentDataPath);
            stats.FromData(data);
            Debug.Log("Load Complete (Test)");
        }
        else
        {
            Debug.LogWarning("Save file not found, initializing from SO");
            NewGame(stats);
        }
    }
}
