using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance; // 싱글톤

    public SaveManager saveManager;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject); // 중복 방지
        }
    }

    private void Start()
    {
        // 게임 시작 시 자동으로 로드
        saveManager.Load();
    }

    private void OnApplicationQuit()
    {
        // 종료 시 자동 저장
        saveManager.Save();
    }

    // 버튼에서 호출 Start Button
    public void NewGame()
    {
        saveManager.NewGame();
        SceneManager.LoadScene("GameScene");
    }

    // 버튼에서 호출 Continue Button
    public void ContinueGame()
    {
        if (saveManager.HasSaveData())
        {
            saveManager.Load();
            SceneManager.LoadScene("khbScene");
        }
        else
        {
            Debug.Log("저장 데이터 없음 → 새 게임 시작");
            NewGame();
        }
    }

    public void OpenSettings()
    {
        SceneManager.LoadScene("SettingsScene");
    }

    public void OpenMainMenu()
    {
        SceneManager.LoadScene("MainMenuScene");
    }
}