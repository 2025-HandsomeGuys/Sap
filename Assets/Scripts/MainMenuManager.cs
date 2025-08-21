using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    public void StartGame()
    {
        SceneManager.LoadScene("GameScene"); // 나중에 실제 게임 씬 이름으로 교체
    }

    public void ContinueGame()
    {
        if (PlayerPrefs.HasKey("SavedLevel"))
        {
            string savedLevel = PlayerPrefs.GetString("SavedLevel");
            SceneManager.LoadScene(savedLevel);
        }
        else
        {
            SceneManager.LoadScene("GameScene");
        }
    }

    public void OpenSettings()
    {
        SceneManager.LoadScene("SettingsScene"); // Settings 씬 이름 그대로 사용
    }

    public void OpenMainMenu()
    {
        SceneManager.LoadScene("MainMenuScene");
    }

}
