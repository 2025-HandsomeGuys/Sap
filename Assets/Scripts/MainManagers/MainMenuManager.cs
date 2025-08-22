using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    public void StartGame()
    {
        SceneManager.LoadScene("GameScene"); // ���߿� ���� ���� �� �̸����� ��ü
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
        SceneManager.LoadScene("SettingsScene"); // Settings �� �̸� �״�� ���
    }

    public void OpenMainMenu()
    {
        SceneManager.LoadScene("MainMenuScene");
    }

}
