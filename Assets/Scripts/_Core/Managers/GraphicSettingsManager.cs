// @tags: graphics, settings, manager, singleton, resolution, quality
using UnityEngine;
using UnityEngine.SceneManagement;

public class GraphicSettingsManager : MonoBehaviour
{
    private static bool _isQuitting = false;
    private static GraphicSettingsManager _instance;

    /// <summary>씬 배치 없이도 동작하는 자동 생성 싱글톤. 어느 씬에서 설정창을 열어도 사용 가능.</summary>
    public static GraphicSettingsManager Instance
    {
        get
        {
            if (_isQuitting) return null;

            if (_instance == null)
            {
                _instance = FindFirstObjectByType<GraphicSettingsManager>();
                if (_instance == null)
                {
                    var go = new GameObject("GraphicSettingsManager");
                    _instance = go.AddComponent<GraphicSettingsManager>();
                }
            }
            return _instance;
        }
    }

    /// <summary>게임 시작 시 인스턴스를 보장해 저장된 그래픽 설정(해상도·VSync·프레임 제한)을 자동 적용한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var _ = Instance;
        SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
        SceneManager.sceneLoaded += OnSceneLoadedEnsure;
    }

    /// <summary>
    /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가
    /// DontDestroyOnLoad 씬의 루트를 전부 파괴해 이 오브젝트도 같이 죽는다.
    /// [RuntimeInitializeOnLoadMethod]는 세션당 한 번만 돌아 재생성되지 않으므로,
    /// 메인메뉴를 다녀오면 저장된 그래픽 설정을 들고 있는 인스턴스가 사라졌다. static 이벤트 구독은 파괴와
    /// 무관하게 살아남으므로 씬 로드마다 되살린다(SoundManager와 같은 패턴).
    /// </summary>
    private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode)
    {
        if (_isQuitting) return;
        var _ = Instance;
    }

    [System.Serializable]
    public struct ResolutionData
    {
        public int width;
        public int height;

        public ResolutionData(int w, int h)
        {
            width = w;
            height = h;
        }

        public override string ToString() => $"{width} x {height}";
    }

    public readonly ResolutionData[] SupportResolutions = new ResolutionData[]
    {
        new ResolutionData(3840, 2160),
        new ResolutionData(2560, 1440),
        new ResolutionData(1920, 1080),
        new ResolutionData(1600, 900),
        new ResolutionData(1280, 720),
        new ResolutionData(1000, 500)
    };

    private const string RESOLUTION_KEY = "Settings_Resolution";
    private const string FULLSCREEN_KEY = "Settings_FullscreenMode"; // Changed key to reflect mode
    private const string FRAMERATE_KEY = "Settings_FrameRate";
    private const string VSYNC_KEY = "Settings_VSync";

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        transform.SetParent(null); // 최상위 부모로 이동
        DontDestroyOnLoad(gameObject);

        LoadAndApplySettings();
    }

    private void OnApplicationQuit() => _isQuitting = true;

    public void LoadAndApplySettings()
    {
        // Default values
        int resIndex = PlayerPrefs.GetInt(RESOLUTION_KEY, 0); // Default to 1920x1080
        int screenMode = PlayerPrefs.GetInt(FULLSCREEN_KEY, (int)FullScreenMode.FullScreenWindow);
        int frameRate = PlayerPrefs.GetInt(FRAMERATE_KEY, 60);
        bool vsync = PlayerPrefs.GetInt(VSYNC_KEY, 0) != 0;

        ApplyResolution(resIndex, (FullScreenMode)screenMode);
        ApplyFrameRate(frameRate);
        ApplyVSync(vsync);
    }

    public void SetResolution(int index, bool isFullscreen)
    {
        // For UI Toggle compatibility:
        // ON -> FullScreenWindow (Borderless)
        // OFF -> Windowed
        FullScreenMode mode = isFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        SetResolution(index, mode);
    }

    public void SetResolution(int index, FullScreenMode mode)
    {
        if (index < 0 || index >= SupportResolutions.Length) return;

        PlayerPrefs.SetInt(RESOLUTION_KEY, index);
        PlayerPrefs.SetInt(FULLSCREEN_KEY, (int)mode);
        PlayerPrefs.Save();

        ApplyResolution(index, mode);
    }

    private void ApplyResolution(int index, FullScreenMode mode)
    {
        ResolutionData res = SupportResolutions[index];
        Screen.SetResolution(res.width, res.height, mode);
    }

    public void SetFrameRate(int limit)
    {
        PlayerPrefs.SetInt(FRAMERATE_KEY, limit);
        PlayerPrefs.Save();

        ApplyFrameRate(limit);
    }

    private void ApplyFrameRate(int limit)
    {
        Application.targetFrameRate = limit;
    }

    public void SetVSync(bool enabled)
    {
        PlayerPrefs.SetInt(VSYNC_KEY, enabled ? 1 : 0);
        PlayerPrefs.Save();

        ApplyVSync(enabled);
    }

    private void ApplyVSync(bool enabled)
    {
        // VSync가 켜지면 Application.targetFrameRate(프레임 제한)는 무시된다
        QualitySettings.vSyncCount = enabled ? 1 : 0;
    }

    public int GetCurrentResolutionIndex() => PlayerPrefs.GetInt(RESOLUTION_KEY, 0);
    public bool GetIsFullscreen() => PlayerPrefs.GetInt(FULLSCREEN_KEY, (int)FullScreenMode.FullScreenWindow) != (int)FullScreenMode.Windowed;
    public int GetCurrentFrameRate() => PlayerPrefs.GetInt(FRAMERATE_KEY, 60);
    public bool GetVSync() => PlayerPrefs.GetInt(VSYNC_KEY, 0) != 0;
}
