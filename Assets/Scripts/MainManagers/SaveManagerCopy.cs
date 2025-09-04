using System.IO;
using UnityEngine;

public class SaveManagerCopy : MonoBehaviour
{
    private string path;

    [Header("�⺻�� ������ SO")]
    public PlayerSO playerSO;

    private void Awake()
    {
        path = Path.Combine(Application.persistentDataPath, "playerData_khb_test.json");
    }

    /// <summary>
    /// ó������ ����: SO�� �⺻������ PlayerStatsController �ʱ�ȭ
    /// </summary>
    public void NewGame(PlayerStatsController stats)
    {
        if (playerSO == null)
        {
            Debug.LogError("PlayerSO�� ������� �ʾҽ��ϴ�!");
            return;
        }

        // SO ������� PlayerData ����
        PlayerData data = new PlayerData(playerSO);
        stats.FromData(data); // PlayerStatsController �ʱ�ȭ
        Save(stats); // �ʱⰪ ����
        Debug.Log("�� ���� ����");
    }

    /// <summary>
    /// ����
    /// </summary>
    public void Save(PlayerStatsController stats)
    {
        PlayerData data = stats.ToData();
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, json);
        Debug.Log("���� �Ϸ�: " + path);
    }

    /// <summary>
    /// �ҷ����� (�̾��ϱ�)
    /// </summary>
    public void Load(PlayerStatsController stats)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            PlayerData data = JsonUtility.FromJson<PlayerData>(json);
            Debug.Log(Application.persistentDataPath);
            stats.FromData(data);
            Debug.Log("�ҷ����� �Ϸ�");
        }
        else
        {
            Debug.LogWarning("���� ���� ����, SO �⺻������ �ʱ�ȭ");
            NewGame(stats); // ���� ������ �� ���� ����
        }
    }
}
