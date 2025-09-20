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
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "GameScene" || currentScene == "khbScene" || currentScene == "Intro")
        {
            saveManager.Load();
        }
    }

    private void OnApplicationQuit()
    {
        // 종료 시 자동 저장
        saveManager.Save();
    }

    // 버튼에서 호출 Start Button
    public void NewGame()
    {
        SceneManager.sceneLoaded += OnSceneLoadedForNewGame;
        SceneManager.LoadScene("GameScene");
    }

    private void OnSceneLoadedForNewGame(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoadedForNewGame;
        saveManager.NewGame(); // 씬 로드 후 안전하게 NewGame 실행
    }

    // 이어하기 버튼
    public void ContinueGame()
    {
        if (saveManager.HasSaveData())
        {
            SceneManager.sceneLoaded += OnSceneLoadedForLoad;
            SceneManager.LoadScene("Intro");
        }
        else
        {
            Debug.Log("저장 데이터 없음 → 새 게임 시작");
            NewGame();
        }
    }

    private void OnSceneLoadedForLoad(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoadedForLoad;
        saveManager.Load(); // 씬 로드 후 안전하게 Load 실행
    }

    public void OpenSettings()
    {
        saveManager.Save();
        SceneManager.LoadScene("SettingsScene");
    }

    public void OpenMainMenu()
    {
        saveManager.Save();
        SceneManager.LoadScene("MainMenuScene");
    }

}