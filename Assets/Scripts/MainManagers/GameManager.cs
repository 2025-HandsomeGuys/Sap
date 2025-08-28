using UnityEngine;

public class GameManager : MonoBehaviour
{
    public PlayerStatsController playerStats;
    public SaveManager saveManager;

    void Start()
    {
        // 게임 시작 시 불러오기 (이어하기 또는 새 게임)
        saveManager.Load(playerStats);
    }

    void OnApplicationQuit()
    {
        // 앱 종료 시 저장
        saveManager.Save(playerStats);
    }

    /// <summary>
    /// 버튼이나 UI에서 '처음부터 시작'을 눌렀을 때 호출
    /// </summary>
    public void OnNewGameButton()
    {
        saveManager.NewGame(playerStats);
    }
}
