// @tags: environment, sleep, nap, upgrade, save, daycycle, manager

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 하루에 가능한 낮잠 횟수를 관리하는 경량 싱글톤.
///
/// <list type="bullet">
///   <item><b>하루 가능 횟수</b>는 업그레이드(<see cref="UpgradeEffectType.NapCount"/>)로 결정된다.
///   업그레이드 노드가 아직 트리에 없으면 0회 → 낮잠 불가. (설계상 나중에 노드를 붙이면 그대로 늘어난다)</item>
///   <item><b>오늘 쓴 횟수</b>는 <see cref="PlayerData.napUsedToday"/>에 저장돼 세이브/로드로 유지된다.</item>
///   <item>새 하루가 시작되면(수면·디버그 날짜 스킵 등 어떤 경로든) 사용량을 0으로 리셋한다.
///   날짜 변경을 매 프레임 폴링해 판정하므로 특정 호출부에 리셋을 심을 필요가 없다.</item>
/// </list>
///
/// 씬 배치 불필요 — <see cref="RuntimeInitializeOnLoadMethod"/>로 자동 생성된다.
/// </summary>
public class NapManager : MonoBehaviour
{
    public static NapManager Instance { get; private set; }

    // 디버그 override (>=0이면 업그레이드 대신 이 값을 하루 가능 횟수로 쓴다). DebugCommands의 nap 명령이 세팅.
    private int _debugMaxOverride = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        AutoCreate();
        SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
        SceneManager.sceneLoaded += OnSceneLoadedEnsure;
    }

    private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode) => AutoCreate();

    /// <summary>
    /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가
    /// DontDestroyOnLoad 씬의 루트를 전부 파괴해 이 오브젝트도 같이 죽는다.
    /// [RuntimeInitializeOnLoadMethod]는 세션당 한 번만 돌아 재생성되지 않으므로,
    /// 메인메뉴를 다녀오면 낮잠 횟수 관리가 멈춰 있었다. static 이벤트 구독은 파괴와
    /// 무관하게 살아남으므로 씬 로드마다 되살린다(SoundManager와 같은 패턴).
    /// </summary>
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("NapManager");
        Instance = go.AddComponent<NapManager>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        NormalizeForToday();
    }

    /// <summary>
    /// 저장된 <see cref="PlayerData.napDay"/>가 현재 날짜와 다르면 새 하루로 보고 사용량을 0으로 리셋한다.
    /// napDay를 함께 저장하므로, 세이브 로드로 날짜가 점프해도 오탐 리셋이 없다.
    /// 조회 직전에도 불러 항상 '오늘 기준' 값을 보장한다.
    /// </summary>
    private void NormalizeForToday()
    {
        var dc = DayCycleManager.Instance;
        var pd = SaveData;
        if (dc == null || pd == null) return;

        if (pd.napDay != dc.CurrentDay)
        {
            pd.napDay = dc.CurrentDay;
            pd.napUsedToday = 0;
        }
    }

    // ===================================================
    // 조회
    // ===================================================

    /// <summary>하루에 가능한 낮잠 총 횟수 (업그레이드 결과 또는 디버그 override).</summary>
    public int MaxNapsPerDay
    {
        get
        {
            if (_debugMaxOverride >= 0) return _debugMaxOverride;
            if (UpgradeManager.Instance == null) return 0;
            return Mathf.Max(0, Mathf.RoundToInt(
                UpgradeManager.Instance.GetStatValue(UpgradeEffectType.NapCount, 0f)));
        }
    }

    /// <summary>오늘 이미 쓴 낮잠 횟수 (세이브 연동).</summary>
    public int UsedToday
    {
        get
        {
            NormalizeForToday();
            var pd = SaveData;
            return pd != null ? Mathf.Max(0, pd.napUsedToday) : 0;
        }
    }

    /// <summary>세이브의 플레이어 데이터(없으면 null).</summary>
    private static PlayerData SaveData =>
        GameManager.Instance != null && GameManager.Instance.saveManager != null
            ? GameManager.Instance.saveManager.playerData : null;

    /// <summary>오늘 남은 낮잠 횟수.</summary>
    public int RemainingNaps => Mathf.Max(0, MaxNapsPerDay - UsedToday);

    /// <summary>지금 낮잠을 잘 수 있는가(횟수가 남았는가).</summary>
    public bool CanNap => RemainingNaps > 0;

    // ===================================================
    // 소비 / 리셋
    // ===================================================

    /// <summary>낮잠 1회 소비. 성공 시 true. (호출 전 <see cref="CanNap"/> 확인 권장)</summary>
    public bool ConsumeNap()
    {
        if (!CanNap) return false;
        SetUsedToday(UsedToday + 1);
        return true;
    }

    /// <summary>오늘 사용량을 강제로 리셋(디버그·테스트용).</summary>
    public void ResetUsedToday() => SetUsedToday(0);

    private void SetUsedToday(int value)
    {
        NormalizeForToday();
        var pd = SaveData;
        if (pd == null) return;
        pd.napUsedToday = Mathf.Max(0, value);
    }

    // ===================================================
    // 디버그
    // ===================================================

    /// <summary>디버그: 하루 가능 횟수 override 설정(-1이면 업그레이드 값 사용).</summary>
    public void SetDebugMaxOverride(int max) => _debugMaxOverride = max;

    public int DebugMaxOverride => _debugMaxOverride;
}
