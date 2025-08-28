using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    private string path;

    [Header("기본값 설정용 SO")]
    public PlayerSO playerSO;

    private void Awake()
    {
        path = Path.Combine(Application.persistentDataPath, "playerData.json");
    }

    /// <summary>
    /// 처음부터 시작: SO의 기본값으로 PlayerStatsController 초기화
    /// </summary>
    public void NewGame(PlayerStatsController stats)
    {
        if (playerSO == null)
        {
            Debug.LogError("PlayerSO가 연결되지 않았습니다!");
            return;
        }

        // SO 기반으로 PlayerData 생성
        PlayerData data = new PlayerData(playerSO);
        stats.FromData(data); // PlayerStatsController 초기화
        Save(stats); // 초기값 저장
        Debug.Log("새 게임 시작");
    }

    /// <summary>
    /// 저장
    /// </summary>
    public void Save(PlayerStatsController stats)
    {
        PlayerData data = stats.ToData();
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, json);
        Debug.Log("저장 완료: " + path);
    }

    /// <summary>
    /// 불러오기 (이어하기)
    /// </summary>
    public void Load(PlayerStatsController stats)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            PlayerData data = JsonUtility.FromJson<PlayerData>(json);
            Debug.Log(Application.persistentDataPath);
            stats.FromData(data);
            Debug.Log("불러오기 완료");
        }
        else
        {
            Debug.LogWarning("저장 파일 없음, SO 기본값으로 초기화");
            NewGame(stats); // 파일 없으면 새 게임 시작
        }
    }
}
