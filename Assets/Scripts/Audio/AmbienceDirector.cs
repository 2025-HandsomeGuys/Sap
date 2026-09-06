// @tags: sound, ambience, audio, director, daycycle, layer, monobehaviour
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 시간대·깊이에 따라 앰비언스 2레이어를 전환한다.
///
/// 판정 순서:
///  1) 씬 이름으로 지상/지하를 가른다(SurfaceSceneRegistry에 포함되면 지상).
///  2) 지하면 플레이어 월드 Y를 청크 Y로 변환해 TileDataManager로 층을 얻는다.
///  3) 결과가 이전과 다를 때만 SoundManager에 반영한다.
///
/// 매 프레임 폴링하지 않고 pollInterval(기본 0.5초) 간격으로 검사한다.
/// 던전 안에서는 전부 정지한다(던전은 별도 연출 공간).
///
/// SoundManager와 같은 오브젝트에 붙인다(DontDestroyOnLoad로 씬 전환에도 살아남는다).
/// </summary>
public class AmbienceDirector : MonoBehaviour
{
    private static AmbienceDirector _instance;

    /// <summary>
    /// SoundManager와 같은 이유로 자동 생성한다(씬 수동 배치 불필요).
    /// 씬에 직접 배치해 인스펙터 값을 조정하고 싶으면 그 인스턴스가 먼저 잡히고
    /// 이 부트스트랩은 중복 생성을 건너뛴다.
    /// </summary>
    /// <summary>
    /// ⚠ 자가복구: SoundManager와 같은 이유 — OpenMainMenu의 DestroyPersistentObjects()가
    /// 이 DDOL 싱글톤을 파괴하면 RuntimeInitializeOnLoadMethod는 재실행되지 않아 앰비언스가
    /// 영구 무음이 된다. static 이벤트 구독은 파괴와 무관하게 살아남으므로 씬 로드마다 되살린다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureExists();
        SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
        SceneManager.sceneLoaded += OnSceneLoadedEnsure;
    }

    private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode) => EnsureExists();

    private static void EnsureExists()
    {
        if (_instance != null) return;

        var go = new GameObject("[AmbienceDirector]");
        go.AddComponent<AmbienceDirector>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    [Header("지상 판정")]
    [Tooltip("SurfaceSceneRegistry 목록에 더해 지상으로 볼 씬. 보통 비워둔다.")]
    [SerializeField] private string[] extraSurfaceScenes;

    [Header("폴링")]
    [SerializeField] private float pollInterval = 0.5f;
    [SerializeField] private float fadeDuration = 2f;

    [Header("참조 (비우면 런타임 탐색)")]
    [SerializeField] private Transform player;

    private AmbienceSet _current = AmbienceSet.Silent;
    private bool _hasApplied;
    private float _pollTimer;

    // 청크 1칸 = 10 월드유닛 (1000px / 100PPU)
    private const float ChunkWorldSize = 10f;

    private void OnEnable()
    {
        if (DayCycleManager.Instance != null)
            DayCycleManager.Instance.OnDateTimeChanged += OnDateTimeChanged;
    }

    private void OnDisable()
    {
        if (DayCycleManager.Instance != null)
            DayCycleManager.Instance.OnDateTimeChanged -= OnDateTimeChanged;

        if (SoundManager.Instance != null)
            SoundManager.Instance.StopAllAmbience(0.3f);
    }

    private void OnDateTimeChanged(int day, TimeOfDay time) => Evaluate();

    private void Update()
    {
        _pollTimer += Time.unscaledDeltaTime;
        if (_pollTimer < pollInterval) return;
        _pollTimer = 0f;
        Evaluate();
    }

    private void Evaluate()
    {
        var sm = SoundManager.Instance;
        if (sm == null) return;

        // 던전 안에서는 앰비언스를 끈다
        if (DungeonOverlayController.IsInDungeon)
        {
            Apply(AmbienceSet.Silent);
            return;
        }

        bool isSurface = IsSurfaceScene();
        TimeOfDay time = DayCycleManager.Instance != null
            ? DayCycleManager.Instance.CurrentTime
            : TimeOfDay.Morning;

        TileType layer = TileType.Empty;
        if (!isSurface)
        {
            Transform p = ResolvePlayer();
            if (p == null) return; // 플레이어가 아직 없다 — 다음 폴링에 재시도

            int chunkY = Mathf.FloorToInt(p.position.y / ChunkWorldSize);
            if (TileDataManager.Instance != null)
                layer = TileDataManager.Instance.GetTileTypeAtDepth(chunkY);
        }

        Apply(AmbienceSelector.Select(isSurface, time, layer));
    }

    private void Apply(AmbienceSet set)
    {
        if (_hasApplied && _current.Equals(set)) return;
        _current = set;
        _hasApplied = true;

        var sm = SoundManager.Instance;
        sm.SetAmbience(SoundManager.AmbienceLayer.Primary, set.Primary, fadeDuration);
        sm.SetAmbience(SoundManager.AmbienceLayer.Secondary, set.Secondary, fadeDuration);
    }

    private bool IsSurfaceScene() => SurfaceSceneRegistry.IsActiveSceneSurface(extraSurfaceScenes);

    private Transform ResolvePlayer()
    {
        if (player != null) return player;

        var pc = Object.FindFirstObjectByType<PlayerController>();
        if (pc != null) player = pc.transform;
        return player;
    }
}
