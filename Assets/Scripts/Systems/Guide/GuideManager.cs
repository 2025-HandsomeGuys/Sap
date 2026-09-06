// @tags: guide, tutorial, manager, singleton, trigger, toggle, save, seen, hotkey

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 가이드 시스템의 두뇌. 새로운 것을 접했을 때 <see cref="Trigger"/>가 호출되면
/// 게임을 멈추고 <see cref="GuideOverlayUI"/>로 해당 가이드를 띄운다.
///
/// 설치 불필요 — 게임 시작 시 자동 생성(<see cref="Bootstrap"/>)되어 DontDestroyOnLoad로 유지된다.
/// 가이드 데이터는 <b>Resources/Guides/</b> 아래의 <see cref="GuideSO"/> 에셋을 자동 로드한다.
///
/// 테스트: 인스펙터 없이도 단축키로 켜고 끌 수 있다(<see cref="debugHotkeys"/> 기본 On).
///  - F8  : 가이드 전체 On/Off 토글
///  - F9  : "이미 봄" 기록 초기화(다시 뜨게)
///  - F10 : testGuide로 지정한 가이드 강제 재생
/// </summary>
public class GuideManager : MonoBehaviour
{
    // ===================================================
    // 싱글톤 + 부트스트랩
    // ===================================================
    private static GuideManager _instance;

    public static GuideManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<GuideManager>();
                if (_instance == null)
                {
                    var go = new GameObject("GuideManager");
                    _instance = go.AddComponent<GuideManager>();
                }
            }
            return _instance;
        }
    }

    /// <summary>씬 로드 후 자동으로 매니저를 띄운다(씬에 미리 배치할 필요 없음).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        _ = Instance; // 접근만 해도 생성된다
        SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
        SceneManager.sceneLoaded += OnSceneLoadedEnsure;
    }

    /// <summary>
    /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가
    /// DontDestroyOnLoad 씬의 루트를 전부 파괴해 이 오브젝트도 같이 죽는다.
    /// [RuntimeInitializeOnLoadMethod]는 세션당 한 번만 돌아 재생성되지 않으므로,
    /// 메인메뉴를 다녀오면 가이드 매니저가 다음 접근 전까지 없는 상태였다. static 이벤트 구독은 파괴와
    /// 무관하게 살아남으므로 씬 로드마다 되살린다(SoundManager와 같은 패턴).
    /// </summary>
    private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode)
    {
        _ = Instance;
    }

    // ===================================================
    // 인스펙터 (씬에 미리 배치했을 때만 노출 — 선택 사항)
    // ===================================================
    [Header("가이드 On/Off")]
    [Tooltip("가이드 전체 켜기/끄기. 끄면 GuideManager.Trigger()로 부른 가이드가 뜨지 않는다(게임플레이 가이드 차단). " +
             "인스펙터에서 직접 제어 — 켜고 끌 때 이 체크박스만 쓰면 된다")]
    [SerializeField] private bool enableGuides = true;

    [Header("디버그 단축키 (테스트용)")]
    [Tooltip("체크했을 때만 아래 단축키가 동작한다. 기본 꺼짐 — 다른 테스트 단축키와 충돌 방지. " +
             "enableGuides와 무관하게 동작한다(강제 프리뷰). 씬에 GuideManager를 배치하고 이 값을 켤 것")]
    [SerializeField] private bool debugHotkeys = false;
    [SerializeField] private KeyCode resetSeenKey = KeyCode.F9;     // 본 기록 초기화
    [SerializeField] private KeyCode replaySampleKey = KeyCode.F10; // 테스트 가이드 강제 재생

    [Tooltip("F10으로 재생할 테스트용 GuideSO. 비워 두면 F10은 아무 것도 하지 않는다")]
    [SerializeField] private GuideSO testGuide;

    // ===================================================
    // 상태
    // ===================================================
    private readonly Dictionary<string, GuideSO> _guides = new Dictionary<string, GuideSO>();

    // SaveManager가 없는 씬(메인메뉴 등)에서도 한 세션 내 중복 노출을 막기 위한 메모리 폴백
    private readonly HashSet<string> _sessionSeen = new HashSet<string>();
    private bool _registered;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // 씬에 배치된 인스턴스라면, 그 인스펙터 설정을 살아있는 싱글톤에 넘긴다.
            // (게임을 다른 씬에서 시작하면 자동 생성 인스턴스가 먼저 생겨 이 컴포넌트가 파괴되는데,
            //  그때 인스펙터에서 켠 debugHotkeys·testGuide 등이 무시되지 않도록 넘겨준다)
            _instance.ApplyInspectorConfigFrom(this);
            Destroy(gameObject);
            return;
        }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        RegisterAll();
    }

    /// <summary>씬에 배치된 인스턴스의 인스펙터 값을 살아있는 싱글톤에 복사한다(설정 핸드오프).</summary>
    private void ApplyInspectorConfigFrom(GuideManager src)
    {
        if (src == null) return;
        enableGuides        = src.enableGuides;
        debugHotkeys        = src.debugHotkeys;
        resetSeenKey        = src.resetSeenKey;
        replaySampleKey     = src.replaySampleKey;
        if (src.testGuide != null) { testGuide = src.testGuide; Register(testGuide); }
    }

    private void Update()
    {
        if (!debugHotkeys) return;

        if (Input.GetKeyDown(resetSeenKey))
        {
            ResetSeen();
            Debug.Log($"[GuideManager] 가이드 '본 기록' 초기화됨 ({resetSeenKey})");
        }
        else if (Input.GetKeyDown(replaySampleKey))
        {
            if (testGuide != null)
            {
                Register(testGuide);            // 아직 등록 안 됐으면 등록(Resources 밖의 에셋도 OK)
                GuideOverlayUI.Show(testGuide); // seen 기록 무시하고 바로 표시
            }
            else
            {
                Debug.LogWarning("[GuideManager] testGuide가 비어 있습니다 — 인스펙터에 GuideSO를 지정하세요.");
            }
        }
    }

    // ===================================================
    // 등록 (Resources/Guides 에셋)
    // ===================================================
    private void RegisterAll()
    {
        if (_registered) return;
        _registered = true;

        // Resources/Guides/ 아래 모든 GuideSO 에셋 자동 로드 — 가이드는 전부 에셋으로만 정의한다
        // (코드 내장 샘플은 Guide_Controls.asset으로 대체돼 삭제됐다).
        var loaded = Resources.LoadAll<GuideSO>("Guides");
        foreach (var g in loaded) Register(g);
    }

    /// <summary>가이드를 레지스트리에 등록(중복 id는 나중 것이 덮어씀).</summary>
    public void Register(GuideSO guide)
    {
        if (guide == null || !guide.IsValid)
        {
            if (guide != null)
                Debug.LogWarning($"[GuideManager] 유효하지 않은 가이드 무시: '{guide.name}' (id/pages 확인)");
            return;
        }
        _guides[guide.guideId] = guide;
    }

    // ===================================================
    // 트리거 / 표시
    // ===================================================
    /// <summary>
    /// 조건형 진입점 — 새로운 것을 접했을 때 게임 코드에서 호출.
    /// 가이드가 꺼져 있거나(테스트 토글), showOnce 가이드를 이미 봤으면 아무 것도 안 한다.
    /// </summary>
    public static void Trigger(string guideId)
    {
        if (_instance == null) { _ = Instance; }
        _instance.TriggerInternal(guideId);
    }

    private void TriggerInternal(string guideId)
    {
        if (!enableGuides) return;
        if (string.IsNullOrEmpty(guideId)) return;

        if (!_guides.TryGetValue(guideId, out var guide))
        {
            Debug.LogWarning($"[GuideManager] 등록되지 않은 가이드 id: '{guideId}'");
            return;
        }

        if (guide.showOnce && HasSeen(guideId)) return;  // 이미 봄
        if (GuideOverlayUI.IsOpen) return;               // 이미 다른 가이드 표시 중

        Present(guide);
    }

    /// <summary>
    /// 명시적/테스트 진입점 — seen 기록·전체 토글과 무관하게 강제로 띄운다(force 기본).
    /// force=false면 Trigger와 동일하게 조건을 존중한다.
    /// </summary>
    public static void Show(string guideId, bool force = true)
    {
        if (_instance == null) { _ = Instance; }
        if (!force) { _instance.TriggerInternal(guideId); return; }

        if (_instance._guides.TryGetValue(guideId, out var guide))
            _instance.Present(guide);
        else
            Debug.LogWarning($"[GuideManager] 등록되지 않은 가이드 id: '{guideId}'");
    }

    private void Present(GuideSO guide)
    {
        // MarkSeen 이전에 읽어야 '이번이 처음인지'가 나온다
        bool firstTime = !HasSeen(guide.guideId);

        if (guide.showOnce) MarkSeen(guide.guideId);

        Telemetry.Log(TelemetryEvents.GuideShown, TelemetryPayload.New()
            .Add("guide_id", guide.guideId)
            .Add("first_time", firstTime));

        GuideOverlayUI.Show(guide);
    }

    /// <summary>등록된 가이드 id 목록(디버그/에디터용).</summary>
    public IEnumerable<string> RegisteredIds => _guides.Keys;

    /// <summary>
    /// 등록된 모든 가이드(도감 '가이드' 탭용). 매니저가 아직 없으면 만들고 등록까지 마친 뒤 돌려준다 —
    /// 도감을 먼저 여는 경우에도 목록이 비지 않게 한다.
    /// </summary>
    public static IEnumerable<GuideSO> AllGuides
    {
        get
        {
            var mgr = Instance;
            mgr.RegisterAll();   // Awake 전에 접근해도 목록이 차 있도록
            return mgr._guides.Values;
        }
    }

    /// <summary>도감에서 '이미 본 가이드'인지 조회(정적 진입점).</summary>
    public static bool IsSeen(string guideId) => Instance.HasSeen(guideId);

    // ===================================================
    // 전체 On/Off (인스펙터 enableGuides — 이것만 켜고 끄면 된다)
    // ===================================================
    /// <summary>가이드 전체 활성 여부(인스펙터 enableGuides). 코드에서 읽거나 바꿀 수도 있다.</summary>
    public bool Enabled { get => enableGuides; set => enableGuides = value; }

    public void SetEnabled(bool on) => enableGuides = on;

    // ===================================================
    // "이미 봄" 기록 (세이브 연동 + 세션 폴백)
    // ===================================================
    private List<string> SavedSeen
    {
        get
        {
            var pd = SaveManager.Instance != null ? SaveManager.Instance.playerData : null;
            if (pd == null) return null;
            if (pd.seenGuideIds == null) pd.seenGuideIds = new List<string>();
            return pd.seenGuideIds;
        }
    }

    public bool HasSeen(string guideId)
    {
        if (_sessionSeen.Contains(guideId)) return true;
        var saved = SavedSeen;
        return saved != null && saved.Contains(guideId);
    }

    public void MarkSeen(string guideId)
    {
        _sessionSeen.Add(guideId);

        var saved = SavedSeen;
        if (saved != null && !saved.Contains(guideId))
        {
            saved.Add(guideId);
            if (SaveManager.Instance != null) SaveManager.Instance.Save();
        }
    }

    /// <summary>모든 "이미 봄" 기록 삭제(테스트용 — 가이드가 다시 뜨게 된다).</summary>
    public void ResetSeen()
    {
        _sessionSeen.Clear();
        var saved = SavedSeen;
        if (saved != null)
        {
            saved.Clear();
            if (SaveManager.Instance != null) SaveManager.Instance.Save();
        }
    }
}
