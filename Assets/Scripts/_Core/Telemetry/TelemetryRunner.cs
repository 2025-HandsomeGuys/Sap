// @tags: telemetry, runner, monobehaviour, heartbeat, flush, bootstrap, error

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 텔레메트리 수명주기 담당 MonoBehaviour.
/// 게임 시작 시 자동 생성되어(RuntimeInitializeOnLoadMethod) 씬 전환을 넘어 살아남는다.
/// 씬에 오브젝트를 배치할 필요가 없다.
///
/// 담당: 60초 주기 플러시, heartbeat, 누적 플레이타임 갱신, 예외 캡처, 종료 훅.
/// </summary>
public sealed class TelemetryRunner : MonoBehaviour
{
    private const float FlushInterval = 60f;
    private const float HeartbeatInterval = 60f;

    private static TelemetryRunner s_instance;
    private static bool s_sessionStartLogged;

    private float _flushTimer;
    private float _heartbeatTimer;

    // 에러 핸들러가 자기 자신이 만든 로그를 다시 처리하는 것을 막는다
    private bool _inErrorHandler;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureExists();
        SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
        SceneManager.sceneLoaded += OnSceneLoadedEnsure;
    }

    private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode) => EnsureExists();

    /// <summary>
    /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가
    /// DontDestroyOnLoad 씬의 루트를 전부 파괴해 이 오브젝트도 같이 죽는다.
    /// [RuntimeInitializeOnLoadMethod]는 세션당 한 번만 돌아 재생성되지 않으므로,
    /// 메인메뉴를 다녀오면 텔레메트리가 영구 정지됐다. static 이벤트 구독은 파괴와
    /// 무관하게 살아남으므로 씬 로드마다 되살린다(SoundManager와 같은 패턴).
    /// </summary>
    private static void EnsureExists()
    {
        if (s_instance != null) return;

        var go = new GameObject("[TelemetryRunner]");
        s_instance = go.AddComponent<TelemetryRunner>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
        s_instance = this;

        Telemetry.Init();   // 이미 켜져 있으면 세션 ID·파일을 그대로 이어 쓴다
        Application.logMessageReceived += OnLogMessage;

        // 부활한 경우엔 session_start를 다시 쓰지 않는다 —
        // 같은 세션 파일에 두 번 들어가면 CSV 익스포터가 회차를 잘못 가른다.
        if (s_sessionStartLogged) return;
        s_sessionStartLogged = true;

        Telemetry.Log(TelemetryEvents.SessionStart, TelemetryPayload.New()
            .Add("os", SystemInfo.operatingSystem)
            .Add("cpu", SystemInfo.processorType)
            .Add("gpu", SystemInfo.graphicsDeviceName)
            .Add("ram_mb", SystemInfo.systemMemorySize)
            .Add("screen", $"{Screen.width}x{Screen.height}")
            .Add("language", Application.systemLanguage.ToString()));
    }

    private void Update()
    {
        if (Telemetry.Context == null) return;

        float dt = Time.unscaledDeltaTime;
        Telemetry.Context.PlaytimeTotal += dt;

        _flushTimer += dt;
        if (_flushTimer >= FlushInterval)
        {
            _flushTimer = 0f;
            Telemetry.Flush();
        }

        _heartbeatTimer += dt;
        if (_heartbeatTimer >= HeartbeatInterval)
        {
            _heartbeatTimer = 0f;
            // 알트+F4로 꺼도 마지막 하트비트가 이탈 지점을 알려준다(설계 §3.5)
            Telemetry.Log(TelemetryEvents.Heartbeat, TelemetryPayload.New()
                .Add("screen", SceneManager.GetActiveScene().name));
        }
    }

    /// <summary>예외·에러 로그를 error 이벤트로 남긴다(설계 §3.6).</summary>
    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        if (_inErrorHandler) return;

        _inErrorHandler = true;
        try
        {
            Telemetry.Log(TelemetryEvents.Error, TelemetryPayload.New()
                .Add("type", type.ToString())
                .Add("message", Truncate(condition, 300))
                .Add("trace", Truncate(stackTrace, 1000)));
        }
        finally
        {
            _inErrorHandler = false;
        }
    }

    /// <summary>
    /// 파괴도 로그 구독이 남으면, 부활한 인스턴스와 겹쳐 error 이벤트가 두 번 기록된다.
    /// (destroy된 MonoBehaviour의 메서드도 managed 필드만 만지면 계속 호출된다)
    /// </summary>
    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessage;
        if (s_instance == this) s_instance = null;
    }

    private void OnApplicationQuit()
    {
        Application.logMessageReceived -= OnLogMessage;

        Telemetry.Log(TelemetryEvents.SessionEnd, TelemetryPayload.New()
            .Add("screen", SceneManager.GetActiveScene().name)
            .Add("session_seconds", Telemetry.Context != null ? (int)Telemetry.Context.PlaytimeTotal : 0));

        Telemetry.Shutdown();
    }

    /// <summary>창 최소화·포커스 상실 시 즉시 플러시한다.</summary>
    private void OnApplicationPause(bool paused)
    {
        if (paused) Telemetry.Flush();
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= max ? s : s.Substring(0, max);
    }
}
