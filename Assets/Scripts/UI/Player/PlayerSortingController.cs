using UnityEngine;
using UnityEngine.SceneManagement;

/// @tags: player, sorting, layer, render, underground
/// <summary>
/// 플레이어 스프라이트의 정렬을 '지상 모드'와 '지하 모드'로 전환한다.
///
/// 지상(DemoUpground)은 지형 타일맵이 플레이어와 같은 `player` 정렬 레이어의 order 30에 있어
/// 플레이어(order 1~20)가 자연스럽게 지형 뒤로 들어간다. 지하는 지형이 `Default`/0이고
/// 플레이어는 `player` 레이어라 **레이어가 달라 order 비교 자체가 성립하지 않는다**
/// (정렬 레이어가 order보다 항상 우선). 그래서 지하에서만 플레이어를 지형과 같은
/// `Default` 레이어로 내리고 order를 음수 대역으로 옮겨 지형 뒤에 그린다.
///
/// 지하 모드 정렬 계획:
///   청크 배경 -100 &lt; 특수청크 배경 -99 &lt; **플레이어 -49~-30** &lt; 돌·광물 -1 &lt; 지형 0
/// → 파진 구멍(투명 픽셀)으로만 플레이어가 보이고, 안 캔 돌·광물은 플레이어 앞에 그려진다.
///
/// **이 컴포넌트가 플레이어 sortingOrder의 단일 소유자다.** 외부에서 직접 order를 만지면
/// 모드 전환과 서로 덮어쓴다. 일시적인 순서 조정은 <see cref="SetExtraOrderBoost"/>를 쓸 것
/// (<see cref="PlayerVisionOverlay"/>의 맹인 유물 부스트가 이 경로를 탄다).
///
/// 부착은 자동이다 — 씬 로드마다 부트스트랩이 "Player" 태그 오브젝트에 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSortingController : MonoBehaviour
{
    public static PlayerSortingController Instance { get; private set; }

    [Header("지하 모드 정렬")]
    [Tooltip("지하에서 플레이어 스프라이트를 옮길 정렬 레이어. 지형 청크와 같은 레이어여야 order 비교가 성립한다.")]
    [SerializeField] private string undergroundSortingLayer = "Default";

    [Tooltip("지하 모드에서 원본 order에 더할 값. 지형(0)·돌·광물(-1)보다 뒤, 배경(-100/-99)보다는 앞이어야 한다.")]
    [SerializeField] private int undergroundOrderOffset = -50;

    [Tooltip("원본 order가 이 값 이상인 렌더러는 지하 모드에서도 건드리지 않는다. 지형 위에 떠야 하는 연출(Effect order 1000 = 곡괭이 휘두르기 이펙트)을 제외하는 용도.")]
    [SerializeField] private int keepAboveOrderThreshold = 100;

    [Header("판정")]
    [Tooltip("Start에서 InfinityMapManager 존재 여부로 지하 여부를 자동 판정한다. 지상 씬에는 이 매니저가 없다.")]
    [SerializeField] private bool autoDetectOnStart = true;

    [Tooltip("적용될 때마다 캡처 목록과 실제 레이어/order를 콘솔에 찍는다. 정렬이 의심될 때만 켤 것 — 자동 부착이라 Play 중 인스펙터에서 켜거나 이 기본값을 true로 바꾼다.")]
    [SerializeField] private bool logSorting = false;

    /// <summary>지하 모드에서 플레이어가 놓이는 정렬 레이어 이름. 어둠막이 같은 레이어를 따라가야 해서 공개한다.</summary>
    public string UndergroundSortingLayer => undergroundSortingLayer;

    /// <summary>현재 지하 모드인지.</summary>
    public bool IsUnderground => _underground;

    // 원본(=지상 기준) 정렬값.
    // baseOrder는 고정이 아니다 — Animator가 m_SortingOrder를 애니메이트하는 렌더러(RightHand·Body)에서는
    // 매 프레임 애니메이션이 쓴 값을 새 원본으로 채택한다. lastWritten이 그 판별에 쓰인다.
    private struct Target
    {
        public SpriteRenderer sr;
        public int baseLayerId;
        public int baseOrder;
        public int lastWritten; // 우리가 마지막으로 쓴 order. 현재값이 이것과 다르면 애니메이션이 덮어쓴 것.
    }

    private Target[] _targets = System.Array.Empty<Target>();
    private bool _underground;
    private int _extraBoost;
    private int _undergroundLayerId;

    private void Awake()
    {
        Instance = this;
        _undergroundLayerId = ResolveLayerId(undergroundSortingLayer);
        Capture();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start() => DetectAndApply();

    // 지하 씬에만 InfinityMapManager가 있다. 던전은 지하 씬 안에서 텔레포트로 구현되어 있어
    // 여기서 걸러지지 않는다 — DungeonOverlayController가 진입/이탈 시 직접 전환한다.
    private void DetectAndApply()
    {
        if (autoDetectOnStart) SetUnderground(InfinityMapManager.Instance != null);
    }

    /// <summary>지하 모드 on/off. 던전처럼 지형이 Default가 아닌 공간에서는 반드시 false로 되돌린다.</summary>
    public void SetUnderground(bool on)
    {
        _underground = on;
        Apply();
    }

    /// <summary>
    /// 모드와 무관하게 전체 order에 더할 임시 오프셋(맹인 유물이 플레이어를 어둠막 위로 올릴 때 등).
    /// 0으로 되돌리면 해제된다. 직접 sortingOrder를 만지는 대신 이걸 쓸 것.
    /// </summary>
    public void SetExtraOrderBoost(int boost)
    {
        if (_extraBoost == boost) return;
        _extraBoost = boost;
        Apply();
    }

    /// <summary>
    /// 런타임에 자식 스프라이트가 추가된 경우(장비 교체 등) 새 렌더러만 추가로 등록한다.
    /// 이미 등록된 렌더러의 원본값은 건드리지 않는다 — 다시 캡처하면 적용된 오프셋이 원본으로 굳는다.
    /// </summary>
    public void Refresh()
    {
        Capture();
        Apply();
    }

    // "player 레이어에 있던 몸통 파츠"만 대상으로 잡는다.
    // 이미 Default에 있는 것(FlashLight, EncumbranceIcon)은 지금도 지형과 같은 order 0이라 그대로 둔다.
    private void Capture()
    {
        var found = GetComponentsInChildren<SpriteRenderer>(true);
        var list = new System.Collections.Generic.List<Target>(_targets.Length + found.Length);

        // 기존 등록분 유지 (원본값 보존)
        for (int i = 0; i < _targets.Length; i++)
            if (_targets[i].sr != null) list.Add(_targets[i]);

        for (int i = 0; i < found.Length; i++)
        {
            var sr = found[i];
            if (sr == null) continue;
            if (sr.sortingLayerID == _undergroundLayerId) continue;      // 이미 지형과 같은 레이어
            if (sr.sortingOrder >= keepAboveOrderThreshold) continue;    // 항상 위에 떠야 하는 것

            bool already = false;
            for (int j = 0; j < list.Count; j++)
                if (list[j].sr == sr) { already = true; break; }
            if (already) continue;

            list.Add(new Target { sr = sr, baseLayerId = sr.sortingLayerID, baseOrder = sr.sortingOrder });
        }

        _targets = list.ToArray();
    }

    /// <summary>
    /// ⚠ 매 프레임 재적용이 필수다 — 한 번 쓰고 끝낼 수 없다.
    ///
    /// 플레이어 애니메이션 클립 5개(Climbidle·Climbing·gokdig·gokdig1·gokdig2)가
    /// <c>m_SortingOrder</c>를 직접 애니메이트한다(대상: <c>RightHand</c>=들고 있는 도구, <c>Body</c>).
    /// Unity Animator는 **어느 클립에서든 애니메이트되는 프로퍼티를 매 프레임 관리**해서,
    /// 커브가 없는 상태에서도 Animator 활성 시점에 캐시해둔 기본값(RightHand=15)을 계속 다시 쓴다.
    /// Start에서 한 번 세팅하면 다음 프레임에 바로 지워진다(정렬 레이어는 애니메이트하지 않아 Default로 남고
    /// order만 원복 → "삽만 지형 위" 증상).
    ///
    /// 애니메이션이 정한 앞뒤 연출(도구가 몸 앞/뒤로 가는 것)은 살려야 하므로,
    /// 애니메이션이 쓴 값을 **새 원본으로 채택**하고 그 위에 대역 오프셋만 다시 얹는다.
    /// </summary>
    private void LateUpdate()
    {
        int offset = (_underground ? undergroundOrderOffset : 0) + _extraBoost;

        for (int i = 0; i < _targets.Length; i++)
        {
            var sr = _targets[i].sr;
            if (sr == null) continue;

            // 우리가 쓴 값이 그대로면 아무도 안 건드린 것. 다르면 애니메이션이 덮어쓴 값이므로 원본으로 채택한다.
            if (sr.sortingOrder != _targets[i].lastWritten)
                _targets[i].baseOrder = sr.sortingOrder;

            int order = _targets[i].baseOrder + offset;
            if (sr.sortingOrder != order) sr.sortingOrder = order;
            _targets[i].lastWritten = order;

            int layerId = _underground ? _undergroundLayerId : _targets[i].baseLayerId;
            if (sr.sortingLayerID != layerId) sr.sortingLayerID = layerId;
        }
    }

    private void Apply()
    {
        int offset = (_underground ? undergroundOrderOffset : 0) + _extraBoost;

        for (int i = 0; i < _targets.Length; i++)
        {
            var sr = _targets[i].sr;
            if (sr == null) continue;

            sr.sortingLayerID = _underground ? _undergroundLayerId : _targets[i].baseLayerId;
            sr.sortingOrder = _targets[i].baseOrder + offset;
            _targets[i].lastWritten = sr.sortingOrder;
        }

        if (logSorting) DumpState();
    }

    // 진단용. "삽이 아직 지형 위" 같은 증상이 나오면 이 로그로 (1) 지하 모드 판정 (2) 캡처 누락
    // (3) 실제 적용된 레이어/order 세 가지를 한 번에 구분할 수 있다.
    private void DumpState()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[PlayerSorting] underground={_underground} boost={_extraBoost} 캡처={_targets.Length}개\n");

        var all = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var sr = all[i];
            bool tracked = false;
            for (int j = 0; j < _targets.Length; j++)
                if (_targets[j].sr == sr) { tracked = true; break; }

            sb.Append($"  {(tracked ? "O" : "X")} {sr.name,-16} layer={SortingLayer.IDToName(sr.sortingLayerID),-10} order={sr.sortingOrder}\n");
        }

        Debug.Log(sb.ToString());
    }

    private static int ResolveLayerId(string layerName)
    {
        var layers = SortingLayer.layers;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i].name == layerName) return layers[i].id;

        Debug.LogWarning($"[PlayerSortingController] 정렬 레이어 '{layerName}'가 없습니다 — Default로 폴백합니다.");
        return SortingLayer.NameToID("Default");
    }

    // --- 자동 부착 ---------------------------------------------------------
    // 프리팹에 수동으로 물리지 않아도 되도록 씬 로드마다 "Player" 태그 오브젝트에 붙인다.
    // (SoundManager의 RuntimeInitializeOnLoadMethod 자동 생성과 같은 패턴)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Attach();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

    private static void Attach()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return; // 플레이어 없는 씬(메인메뉴·마켓 등)

        var controller = player.GetComponent<PlayerSortingController>();
        if (controller == null) controller = player.AddComponent<PlayerSortingController>();

        // 이 시점엔 씬의 Awake가 이미 끝나 있어 매니저 판정이 확정이다.
        // Start를 기다리면 한 프레임 동안 플레이어가 지형 앞에 그려지므로 그 자리에서 적용한다.
        // 이미 붙어 있던 경우에도 다시 판정한다 — 플레이어가 씬을 넘어 살아남는 구성에서 모드가 굳지 않도록.
        controller.DetectAndApply();
    }
}
