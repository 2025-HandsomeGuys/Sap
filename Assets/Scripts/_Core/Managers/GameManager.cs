// @tags: game-manager, manager, singleton, save, scene
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance; // 싱글톤

    /// <summary>
    /// 메인메뉴 인트로 연출 중에는 false — 연출이 끝날 때까지 메뉴 버튼(새게임/이어하기/설정/종료)이
    /// 눌려도 무시된다. 메뉴의 AnimationTrigger가 연출 시작 시 false로, 끝날 때 true로 되돌린다.
    /// 게임플레이 중에는 항상 true(기본값)라 일시정지 설정 등에는 영향이 없다.
    /// </summary>
    public static bool MenuInteractable = true;

    [SerializeField] private SaveManager _saveManager;
    public SaveManager saveManager
    {
        get
        {
            if (_saveManager == null)
            {
                _saveManager = SaveManager.Instance ?? FindFirstObjectByType<SaveManager>();
            }
            return _saveManager;
        }
        private set => _saveManager = value;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);

            // _saveManager 초기화 (프로퍼티 getter를 통해 자동 수행되지만 Awake에서 명시적으로 시도)
            if (_saveManager == null)
                _saveManager = SaveManager.Instance ?? FindFirstObjectByType<SaveManager>();
        }
        else
        {
            Destroy(gameObject); // 중복 방지
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnGeneralSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnGeneralSceneLoaded;
    }

    private void OnGeneralSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // [수정됨] 컷씬과 튜토리얼 씬에서는 자동 로드를 스킵합니다.
        if (scene.name == "LoadingScene" || scene.name == "SettlementScene" ||
            scene.name == "ComicScene" ) return;

        // Additive 로드는 로딩 시스템(LoadingSceneController)에서 직접 SetActiveScene 이후
        // SaveManager.Load()를 호출하여 처리하므로 여기서의 자동 실행은 제외합니다.
        if (mode == LoadSceneMode.Additive) return;

        if (saveManager == null)
        {
            Debug.Log("[GameManager] saveManager가 할당되지 않았거나 파괴되었습니다. 현재 씬에서 갱신을 시도합니다.");
            saveManager = FindFirstObjectByType<SaveManager>();
        }

        // 지하씬은 LoadingSceneController에서 Load()를 직접 호출하므로 여기서는 스킵
        if (scene.name == "DemoUnderground")
        {
            Debug.Log("[GameManager] 지하씬 로드 — OnGeneralSceneLoaded에서 Load() 스킵 (LoadingSceneController가 처리)");
            return;
        }

        if (saveManager != null)
        {
            saveManager.Load();
        }
        else
        {
            Debug.LogError("[GameManager] SaveManager를 찾을 수 없습니다. (이 씬에 SaveManager가 없는지 확인 필요)");
        }
    }

    private void Start()
    {
        // [수정됨] 현재 씬이 메인메뉴, 컷씬, 튜토리얼씬일 때는 로드를 시도하지 않도록 방어 코드 추가
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != "MainMenuScene" && currentScene != "ComicScene" && currentScene != "TutorialScene")
        {
            saveManager.Load();
        }
    }

    /// <summary>
    /// 현재 씬이 지하씬인지 확인합니다. 지하씬에서는 자동 저장을 차단합니다.
    /// </summary>
    private bool IsUndergroundScene()
    {
        return SceneManager.GetActiveScene().name == "DemoUnderground";
    }

    private void OnApplicationQuit()
    {
        if (saveManager == null) return;

        if (IsUndergroundScene())
        {
            Debug.Log("[GameManager] 지하 씬에서 종료 — 저장하지 않음 (지상 진입 전 상태 보존)");
            return;
        }

        saveManager.Save();
        Debug.Log("게임 종료: 지상 데이터 저장 완료");
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus || saveManager == null) return;

        if (IsUndergroundScene())
        {
            //Debug.Log("[GameManager] 지하 씬에서 일시정지 — 저장하지 않음");
            return;
        }

        saveManager.Save();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus || saveManager == null) return;

        if (IsUndergroundScene())
        {
            return;
        }

        saveManager.Save();
    }

    // ── 새 게임 버튼(Start) ──
    // 항상 슬롯 선택 UI를 거친다 (슬롯 선택 → 덮어쓰기 확인 → 세이브 이름 입력).
    //
    // 예전에는 빈 슬롯이 있으면 UI를 건너뛰고 바로 새 게임을 시작했는데,
    // 그 지름길로는 세이브 이름을 물어볼 지점이 없어서 첫 플레이가 항상 이름 없이 시작됐다.
    // 경로를 하나로 합쳐 이름 입력이 빠지는 구멍을 없앤다.
    public void NewGame()
    {
        if (!MenuInteractable) return; // 인트로 연출 중 클릭 무시

        var sm = saveManager;
        if (sm == null)
        {
            Debug.LogError("[GameManager] NewGame: SaveManager를 찾을 수 없습니다.");
            return;
        }

        SaveSlotUI.Instance.ShowNewGame();
    }

    // ── 이어하기 버튼(Continue) ──
    // 저장된 슬롯 목록을 띄워 이어할 슬롯을 고르게 한다.
    public void ContinueGame()
    {
        if (!MenuInteractable) return; // 인트로 연출 중 클릭 무시
        SaveSlotUI.Instance.ShowContinue();
    }

    // ── 게임 종료 버튼(Exit) ──
    public void QuitGame()
    {
        if (!MenuInteractable) return; // 인트로 연출 중 클릭 무시
        Debug.Log("[GameManager] 게임 종료");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OpenSettings()
    {
        if (!MenuInteractable) return; // 인트로 연출 중 클릭 무시 (게임플레이 중엔 항상 true)

        if (!IsUndergroundScene())
        {
            saveManager.Save();
        }
        // 씬 전환 대신 현재 화면 위에 오버레이로 표시 (배경 캡처 → 블러 + 어둡게)
        SettingsOverlayUI.Open();
    }

    public void OpenMainMenu()
    {
        if (IsUndergroundScene())
        {
            // 지하에서는 저장하지 않음 — 파일의 지상 데이터가 보존됨
            Debug.Log("[GameManager] 지하에서 메인메뉴 이동 — 저장 없이 이동");
        }
        else
        {
            saveManager.Save();
        }

        // 이 프로젝트는 매니저·오버레이 UI 상당수가 DontDestroyOnLoad라, 로딩씬을 거쳐
        // 넘어가면 그 잔존물(일시정지/상점/인벤토리 오버레이 등)이 메인메뉴 위에 남아
        // 클릭을 먹거나 timeScale을 0으로 굳혀버린다. 메인메뉴는 "씬을 새로 Play한 것과
        // 같은 깨끗한 상태"여야 하므로, 따라온 DontDestroyOnLoad 오브젝트를 전부 파괴하고
        // 로딩씬을 거치지 않고 MainMenuScene을 Single 모드로 바로 로드한다.
        Time.timeScale = 1f;
        LoadingData.IsLoading = false;
        DestroyPersistentObjects();
        SceneManager.LoadScene("MainMenuScene", LoadSceneMode.Single);
    }

    /// <summary>
    /// DontDestroyOnLoad 씬에 쌓인 모든 루트 오브젝트(자기 자신 포함)를 파괴한다.
    /// Single 씬 로드는 DontDestroyOnLoad 오브젝트를 지우지 않으므로 수동으로 정리해야
    /// MainMenuScene이 잔존물 없이 새로 Play한 것과 동일하게 시작한다.
    /// </summary>
    private static void DestroyPersistentObjects()
    {
        // DontDestroyOnLoad 전용 씬 핸들을 얻기 위한 임시 오브젝트
        var probe = new GameObject("~DDOLProbe");
        DontDestroyOnLoad(probe);
        Scene ddolScene = probe.scene;
        Destroy(probe);

        if (!ddolScene.IsValid()) return;

        foreach (var root in ddolScene.GetRootGameObjects())
        {
            Destroy(root);
        }
    }

    public void OpenUpgroundScene()
    {
        if (!IsUndergroundScene())
        {
            saveManager.Save();
        }
        SceneLoader.LoadScene("DemoUpground");
    }
}