// @tags: upgrade, ui, overlay, code-generated, tree, tier, node, unlock, gold, skill

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 업그레이드 오버레이 — 스킬 트리(노드·티어 밴드·직각 연결선)를 왼쪽에, 선택 노드 상세/해금 패널을 오른쪽에 놓은 화면.
/// UI는 전부 코드로 생성한다(SettingsOverlayUI·ShopOverlayUI 패턴) — 씬/프리팹 세팅 없이 <see cref="Open"/> 호출만으로 동작.
/// 배경/버튼 스프라이트는 인스펙터의 <see cref="skin"/>에 넣으면 적용된다(비운 항목은 코드 생성 라운드로 폴백).
///
/// 트리 데이터·해금 로직은 전부 기존 시스템에 위임한다:
///  - 트리: <see cref="UpgradeTreeSO"/> (UpgradeManager / 씬 UpgradeUI 에서 자동 탐색, 또는 인스펙터 override)
///  - 상태: <see cref="UpgradeTreeState"/> (PlayerData 세이브)
///  - 해금/판정: <see cref="UpgradeManager"/> (골드 차감·DayEarningsLedger·티어 해금·저장까지 담당)
///
/// UIStateManager 의 <c>useCodeBuiltUpgradeUI</c> 가 켜져 있으면 이 오버레이가 프리팹(upgradeRoot) 대신 열린다.
/// 기존 프리팹 <see cref="UpgradeUI"/> 와 공존한다.
/// </summary>
public class UpgradeOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static UpgradeOverlayUI _instance;

    public static UpgradeOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<UpgradeOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("UpgradeOverlayUI");
                    _instance = go.AddComponent<UpgradeOverlayUI>();
                }
            }
            return _instance;
        }
    }

    /// <summary>업그레이드 오버레이가 열려 있는지 (UIStateManager의 전역 단축키 차단용).</summary>
    public static bool IsOpen => _instance != null && _instance._isOpen;

    /// <summary>ESC로 닫힌 바로 그 프레임인지 — 같은 프레임의 중복 처리 방지.</summary>
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// 씬이 바뀌면 무조건 닫는다(안전망) — DontDestroyOnLoad라 열린 채 씬이 바뀌면
    /// 새 씬 위에 남아 클릭을 전부 먹고 timeScale이 0으로 굳는다. (ShopOverlayUI와 동일)
    /// </summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!_isOpen && !_opening) return;

        _opening = false;
        _isOpen = false;
        Time.timeScale = 1f;
        StopFocus();
        Unsubscribe();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 인스펙터
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Header("트리 데이터 (비우면 UpgradeManager / 씬 UpgradeUI 에서 자동 탐색)")]
    [SerializeField] private UpgradeTreeSO treeOverride;

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.55f;
    [SerializeField] private bool useBlurBackdrop = true;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("동작")]
    [Tooltip("열려 있는 동안 게임을 멈춘다(Time.timeScale = 0)")]
    [SerializeField] private bool pauseGameWhileOpen = true;
    [Tooltip("ESC로 닫기")]
    [SerializeField] private bool closeOnEscape = true;
    [Tooltip("노드 클릭 시 그 노드가 잘 보이는 위치로 부드럽게 이동하는 시간(초). 0이면 즉시")]
    [SerializeField] private float focusDuration = 0.25f;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 1800f;
    [SerializeField] private float panelHeight = 968f;
    [Tooltip("노드 클릭 시 뜨는 상세 팝업의 폭")]
    [SerializeField] private float detailWidth = 340f;
    [SerializeField] private int sortingOrder = 30650;

    [Header("노드 크기 / 그래프 배율")]
    [SerializeField] private float nodeWidth = 132f;
    [SerializeField] private float nodeHeight = 146f;
    [Tooltip("그래프 전체 확대 배율 — 클수록 노드가 크고 간격이 넓어진다. 화면을 넘치면 드래그·휠로 이동")]
    [SerializeField] private float graphScale = 1.5f;
    [Tooltip("트리 상·하단 여백")]
    [SerializeField] private float treePadVertical = 200f;
    [Tooltip("트리 좌·우 여백")]
    [SerializeField] private float treePadHorizontal = 100f;

    [Header("휠 확대/축소 (월드맵과 동일 조작)")]
    [Tooltip("휠 한 칸당 배율 (1.15 = 15%씩 확대/축소)")]
    [SerializeField] private float zoomStep = 1.15f;
    [SerializeField] private float minZoom = 0.5f;
    [SerializeField] private float maxZoom = 2.5f;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;
    [SerializeField] private string unlockSfxName = SfxKeys.UpgradeUnlock;

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";
    [SerializeField] private string lockGlyph = "▲";   // 티어 잠금 표시 (자물쇠 글리프 대체)

    // ===================================================
    // 상수
    // ===================================================
    private const float FadeDuration = 0.14f;
    private const float Side = 24f;
    private const float TitleH = 72f;
    private const float HintH = 30f;
    private const float BodySpacing = 16f;
    private const float TreeCardPad = 16f;

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built;
    private bool _treeBuilt;
    private bool _isOpen;
    private bool _opening;
    private bool _needsRefresh;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;
    private bool _subscribed;
    private bool _langSubscribed;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    private PlayerStat _playerStat;
    private UpgradeTreeSO _tree;

    // 프레임 위젯
    private TextMeshProUGUI _goldText, _hintText;
    private ScrollRect _treeScroll;
    private RectTransform _treeContent, _bandLayer, _lineLayer, _nodeLayer, _overlayLayer;
    private TextMeshProUGUI _emptyText;

    // 상세 팝업 위젯 (노드 클릭 시 뜨는 플로팅 패널)
    private RectTransform _panelRect;   // 팝업 위치 클램프 기준
    private RectTransform _detailRoot;   // 팝업 루트 (표시/숨김)
    private Image _detailIcon;
    private TextMeshProUGUI _detailName, _detailTier, _detailDesc, _detailEffect, _detailCost, _detailStatus, _detailPrereq;
    private Button _unlockButton;
    private TextMeshProUGUI _unlockLabel;

    // 트리 뷰
    private readonly List<NodeView> _nodeViews = new List<NodeView>();
    private readonly Dictionary<string, NodeView> _nodeViewById = new Dictionary<string, NodeView>();
    private readonly List<TierView> _tierViews = new List<TierView>();
    private readonly List<LineView> _lineViews = new List<LineView>();

    /// <summary>
    /// 선이 가로로 건너갈 높이를 정하는 레인 라우터. EnsureTreeBuilt에서 노드 좌표로 채운다.
    ///
    /// ⚠ 예전에는 허브(분기·합류) 기준으로 부모 바로 아래·자식 바로 위에서 꺾었다.
    ///   그러면 빈 레인으로 갈라진 선이 화면 절반을 혼자 내려갔다. 이제는 레인 점유를 보고
    ///   살아있는 트렁크에 합쳐져 내려가다 필요한 지점에서만 갈라진다.
    /// </summary>
    private readonly UpgradeLaneRouter _laneRouter = new UpgradeLaneRouter();

    /// <summary>노드 사각형과 선이 꺾이는 지점 사이 여유.</summary>
    private const float TrunkGap = 22f;
    private UpgradeNodeSO _selected;
    private bool _detailShown;   // 팝업을 실제로 띄웠는지 — 포커스 이동이 끝난 뒤에만 true

    // 노드 포커스 이동
    private Coroutine _focusRoutine;

    // WASD 키보드 내비게이션 — 노드를 좌표 기준으로 옮겨 다니고(이동 시 자동 선택 = SelectNode가
    // 뷰를 그 노드로 센터링해 준다), Space로 선택 노드를 '해금'한다. 꾹 누르면 반복 이동.
    private KeyCode _navHeld = KeyCode.None;
    private float _navNextRepeat;
    private const float NavRepeatDelay = 0.35f;
    private const float NavRepeatInterval = 0.10f;

    // 트리 좌표 매핑 (center-origin · graphScale 확대, OrthogonalUILineRenderer 규칙에 맞춤)
    private float _centerY;

    // 휠 확대/축소 배율 — _treeContent.localScale 에 곱한다(graphScale 위에 얹힌 뷰 배율). 열 때 1로 리셋.
    private float _zoom = 1f;

    // 연결선이 피해 가야 할 노드 사각형(매핑 좌표). 선이 노드를 관통하면 그 노드가
    // 부모·자식 사이의 '선행 단계'처럼 보인다 — 실제로 밀착 등반 I이 그렇게 오인됐다.
    private readonly List<Rect> _nodeObstacles = new List<Rect>();
    private const float ObstaclePad = 12f;

    private class NodeView
    {
        public UpgradeNodeSO node;
        public RectTransform root;
        public Image ring;      // 선택/해금가능 강조 테두리
        public Image frame;     // 클릭 대상
        public Image iconBox;
        public Image icon;
        public TextMeshProUGUI name;
        public TextMeshProUGUI cost;
    }

    private class TierView
    {
        public int tier;
        public Image band;
        public GameObject overlay;      // 티어 잠금 시 덮개 (raycast 차단)
        public TextMeshProUGUI overlayText;
    }

    private class LineView
    {
        public OrthogonalUILineRenderer renderer;
        public UpgradeNodeSO parent;
    }

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    public static void Open()
    {
        if (IsOpen || Instance._opening) return;
        Instance.StartCoroutine(Instance.OpenRoutine());
    }

    public static void CloseStatic()
    {
        if (_instance != null) _instance.Close();
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        if (pauseGameWhileOpen) Time.timeScale = _prevTimeScale;
        CodeUI.PlaySfx(clickSfxName);

        StopFocus();
        Unsubscribe();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        // ESC 등으로 스스로 닫혔다면 UIStateManager 상태도 되돌린다.
        // 이 시점엔 이미 _isOpen=false라, SetState가 CloseStatic을 다시 불러도 즉시 반환된다(재귀 없음).
        // 팝업(NPC)에서 들어왔다면 None이 아니라 그 팝업으로 되돌린다(NPC → 팝업 → 업그레이드 → ESC → 팝업).
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Upgrade) ui.ReturnToPopupOrClose();
    }

    private IEnumerator OpenRoutine()
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        ResolveRefs();
        EnsureTreeBuilt();

        if (useBlurBackdrop)
        {
            _canvasObj.SetActive(false);
            yield return new WaitForEndOfFrame();
            _blur.Capture(_blurImage, blurDownsamples, flipBlurVertically);
        }
        _blurImage.enabled = useBlurBackdrop;
        _opening = false;

        _prevTimeScale = Time.timeScale;
        if (pauseGameWhileOpen) Time.timeScale = 0f;
        _isOpen = true;

        Subscribe();
        _loc.Refresh();

        // 열 때는 상세 팝업을 닫힌 상태로 시작한다 (노드를 클릭해야 뜬다)
        _selected = null;
        _detailShown = false;
        RefreshAll();

        _canvasObj.SetActive(true);
        _zoom = 1f;
        if (_treeContent != null) _treeContent.localScale = Vector3.one;
        if (_treeScroll != null)
        {
            _treeScroll.horizontalNormalizedPosition = 0.5f; // 가로 중앙(트렁크 x=0)
            _treeScroll.verticalNormalizedPosition = 0f;     // 세로 바닥(Tier 0)부터
        }

        _canvasGroup.alpha = 0f;
        float t = 0f;
        while (t < FadeDuration)
        {
            t += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Clamp01(t / FadeDuration);
            yield return null;
        }
        _canvasGroup.alpha = 1f;
    }

    private void Update()
    {
        if (!_isOpen) return;

        // Space는 Submit로도 잡힌다 — 직전에 마우스로 누른 버튼이 선택된 채 남아 있으면 중복 실행된다.
        CodeUI.ClearSelection();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 선택된 노드가 있으면(팝업이 떴든, 이동 중이라 아직 안 떴든) 그것부터 취소한다
            if (_selected != null) { CodeUI.PlayBack(); HideDetail(); return; }
            if (closeOnEscape) { CodeUI.PlayBack(); Close(); }
            return;
        }

        HandleZoom();
        HandleNavInput();
    }

    /// <summary>
    /// 마우스 휠로 트리를 확대/축소한다(월드맵 <see cref="WorldMapOverlay"/> 와 동일한 조작감).
    /// 커서 아래 그래프 좌표를 고정한 채 <see cref="_treeContent"/>.localScale 을 조절하고,
    /// 스케일 전후의 월드 위치 차이만큼 콘텐츠를 밀어 커서 지점이 제자리에 있도록 한다.
    /// ScrollRect(Clamped) 는 바뀐 스케일 기준으로 뒤이어 경계 클램프만 해 준다.
    /// </summary>
    private void HandleZoom()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.01f) return;
        if (_treeScroll == null || _treeContent == null || _treeScroll.viewport == null) return;

        Vector2 mouse = Input.mousePosition;
        if (!RectTransformUtility.RectangleContainsScreenPoint(_treeScroll.viewport, mouse, null)) return;

        float factor = scroll > 0f ? zoomStep : 1f / zoomStep;
        float newZoom = Mathf.Clamp(_zoom * factor, minZoom, maxZoom);
        if (Mathf.Approximately(newZoom, _zoom)) return;

        // 커서 아래 그래프(콘텐츠 로컬) 좌표 — 이 변환은 콘텐츠 자체 스케일을 이미 반영해 준다.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_treeContent, mouse, null, out Vector2 graphPoint);
        Vector3 worldBefore = _treeContent.TransformPoint(graphPoint);

        _zoom = newZoom;
        _treeContent.localScale = new Vector3(_zoom, _zoom, 1f);

        Vector3 worldAfter = _treeContent.TransformPoint(graphPoint);
        _treeContent.position += worldBefore - worldAfter; // 커서 아래 지점 고정

        _treeScroll.StopMovement();
    }

    /// <summary>
    /// WASD로 노드 사이를 좌표 기준으로 이동한다(이동 시 자동 선택 → 뷰가 그 노드로 센터링).
    /// Space는 선택된 노드를 '해금'한다(<see cref="OnUnlockClicked"/>가 CanUnlock으로 가드).
    /// </summary>
    private void HandleNavInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (_selected != null) OnUnlockClicked();
            return;
        }

        // 새로 누른 방향키가 우선, 없으면 꾹 누르고 있는 키로 반복
        KeyCode key = KeyCode.None;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) ||
            Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D))
        {
            key = FirstDownDirKey();
            _navHeld = key;
            _navNextRepeat = Time.unscaledTime + NavRepeatDelay;
        }
        else if (_navHeld != KeyCode.None && Input.GetKey(_navHeld))
        {
            if (Time.unscaledTime >= _navNextRepeat)
            {
                _navNextRepeat = Time.unscaledTime + NavRepeatInterval;
                key = _navHeld;
            }
        }
        else
        {
            _navHeld = KeyCode.None;
        }

        if (key != KeyCode.None) NavStep(DirOf(key));
    }

    private static KeyCode FirstDownDirKey()
    {
        if (Input.GetKeyDown(KeyCode.W)) return KeyCode.W;
        if (Input.GetKeyDown(KeyCode.S)) return KeyCode.S;
        if (Input.GetKeyDown(KeyCode.A)) return KeyCode.A;
        return KeyCode.D;
    }

    private static Vector2 DirOf(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.W: return Vector2.up;
            case KeyCode.S: return Vector2.down;
            case KeyCode.A: return Vector2.left;
            default: return Vector2.right;
        }
    }

    /// <summary>현재 선택 노드에서 dir 방향으로 가장 가까운 노드를 골라 선택한다(없으면 뷰 중앙 근처 노드로 진입).</summary>
    private void NavStep(Vector2 dir)
    {
        if (_nodeViews.Count == 0) return;

        NodeView cur = (_selected != null && _nodeViewById.TryGetValue(_selected.nodeId, out var cv)) ? cv : null;
        if (cur == null || cur.root == null)
        {
            var enter = NearestNodeToViewportCenter();
            if (enter != null && enter.node != null) SelectNode(enter.node);
            return;
        }

        Vector2 from = NodeCenter(cur.root);
        NodeView best = null;
        float bestScore = float.MaxValue;
        foreach (var v in _nodeViews)
        {
            if (v == cur || v.root == null || !v.root.gameObject.activeInHierarchy) continue;

            Vector2 d = NodeCenter(v.root) - from;
            float along = d.x * dir.x + d.y * dir.y;
            if (along <= 1f) continue;                         // 진행 방향 반대·같은 줄은 후보 아님
            float side = Mathf.Abs(d.x * dir.y - d.y * dir.x); // 옆으로 벗어난 정도
            float score = along + side * 3f;                   // '똑바로'를 강하게 선호(대각선으로 안 새게)
            if (score < bestScore) { bestScore = score; best = v; }
        }

        if (best != null && best.node != null) SelectNode(best.node);
    }

    private static Vector2 NodeCenter(RectTransform rt) => rt.TransformPoint(rt.rect.center);

    private NodeView NearestNodeToViewportCenter()
    {
        Vector2 center = (_treeScroll != null && _treeScroll.viewport != null)
            ? (Vector2)_treeScroll.viewport.TransformPoint(_treeScroll.viewport.rect.center)
            : (Vector2)(new Vector2(Screen.width, Screen.height) * 0.5f);

        NodeView best = null;
        float bestD = float.MaxValue;
        foreach (var v in _nodeViews)
        {
            if (v.root == null || !v.root.gameObject.activeInHierarchy) continue;
            float d = (NodeCenter(v.root) - center).sqrMagnitude;
            if (d < bestD) { bestD = d; best = v; }
        }
        return best;
    }

    private void LateUpdate()
    {
        if (_isOpen && _needsRefresh)
        {
            _needsRefresh = false;
            RefreshAll();
        }
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        Unsubscribe();
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 참조 / 이벤트
    // ===================================================
    private void ResolveRefs()
    {
        if (_playerStat == null) _playerStat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        if (_tree == null) _tree = ResolveTree();
    }

    private UpgradeTreeSO ResolveTree()
    {
        if (treeOverride != null) return treeOverride;

        var mgr = UpgradeManager.Instance;
        if (mgr != null && mgr.upgradeTree != null) return mgr.upgradeTree;

        var sceneUI = FindFirstObjectByType<UpgradeUI>(FindObjectsInactive.Include);
        if (sceneUI != null && sceneUI.upgradeTree != null) return sceneUI.upgradeTree;

        return null;
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;

        if (UpgradeManager.Instance != null) UpgradeManager.Instance.OnUpgradeStateChanged += OnUpgradeStateChanged;
        if (_playerStat != null) _playerStat.OnGoldChanged += OnGoldChanged;

        if (!_langSubscribed && LanguageManager.Instance != null)
        {
            LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
            _langSubscribed = true;
        }
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;

        if (UpgradeManager.Instance != null) UpgradeManager.Instance.OnUpgradeStateChanged -= OnUpgradeStateChanged;
        if (_playerStat != null) _playerStat.OnGoldChanged -= OnGoldChanged;
    }

    private void OnUpgradeStateChanged() => _needsRefresh = true;
    private void OnGoldChanged(int _) => _needsRefresh = true;

    private void OnLanguageChanged(LanguageType _)
    {
        _loc.Refresh();
        _needsRefresh = true;
    }

    // ===================================================
    // UI 프레임 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("UpgradeOverlayCanvas");
        _canvasObj.transform.SetParent(transform, false);
        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasObj.AddComponent<GraphicRaycaster>();
        _canvasGroup = _canvasObj.AddComponent<CanvasGroup>();

        var blurObj = new GameObject("BlurBackdrop");
        blurObj.transform.SetParent(_canvasObj.transform, false);
        _blurImage = blurObj.AddComponent<RawImage>();
        CodeUI.StretchFull(_blurImage.rectTransform);
        _blurImage.raycastTarget = true;

        var dim = CodeUI.CreateImage(_canvasObj.transform, "Dim", new Color(0f, 0f, 0f, dimAlpha), rounded: false);
        CodeUI.StretchFull(dim.rectTransform);

        var panel = CodeUI.CreateImage(_canvasObj.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);
        panelRt.anchoredPosition = Vector2.zero;

        _panelRect = panelRt;

        BuildTitleBar(panel.transform);

        // 본체: 트리 카드가 화면 전체를 채운다 (상세는 노드 클릭 시 뜨는 플로팅 팝업)
        var body = CodeUI.CreateRect(panel.transform, "Body");
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(Side, Side + HintH + 6f);
        body.offsetMax = new Vector2(-Side, -(Side + TitleH + 12f));

        BuildTreeCard(body);

        // 하단 안내
        _hintText = CodeUI.CreateText(panel.transform, "Hint", 17f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        var hintRt = _hintText.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.offsetMin = new Vector2(Side, Side);
        hintRt.offsetMax = new Vector2(-Side, Side + HintH);
        _loc.Bind(_hintText, "ui_upgrade_hint", "W / A / S / D : 노드 이동   ·   Space : 해금   ·   드래그로 이동   ·   휠로 확대/축소   ·   ESC : 닫기");

        // 상세 팝업 (맨 위에 그려지도록 마지막에 생성). 초기엔 숨김
        BuildDetailPopup(panel.transform);

        _canvasObj.SetActive(false);
    }

    private void BuildTitleBar(Transform panel)
    {
        var bar = CodeUI.CreateRect(panel, "TitleBar");
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(Side, -(Side + TitleH));
        bar.offsetMax = new Vector2(-Side, -Side);
        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var diamond = CodeUI.CreateText(bar, "Diamond", 22f, FontStyles.Normal, CodeUI.AccentFill, TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;

        var title = CodeUI.CreateText(bar, "Title", 32f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        title.characterSpacing = 8f;
        _loc.Bind(title, "ui_upgrade_title", "업 그 레 이 드");

        CodeUI.CreateSpacer(bar);

        var goldBox = CodeUI.CreateImage(bar, "GoldBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var goldLe = goldBox.gameObject.AddComponent<LayoutElement>();
        goldLe.preferredWidth = 220f;
        goldLe.preferredHeight = 46f;
        _goldText = CodeUI.CreateText(goldBox.transform, "Gold", 24f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(_goldText.rectTransform);
    }

    private void BuildTreeCard(Transform parent)
    {
        var card = CodeUI.CreateImage(parent, "TreeCard", CodeUI.CardBg, skin.cardSprite, skin);
        CodeUI.StretchFull(card.rectTransform);

        // 스크롤 뷰 (트리는 절대좌표 배치라 CodeUI.CreateScrollView 대신 직접 만든다)
        var scrollRt = CodeUI.CreateRect(card.transform, "TreeScroll");
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(TreeCardPad, TreeCardPad);
        scrollRt.offsetMax = new Vector2(-TreeCardPad, -TreeCardPad);

        _treeScroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        _treeScroll.horizontal = true;   // 그래프가 뷰포트보다 넓으면 좌우로도 이동
        _treeScroll.vertical = true;
        _treeScroll.movementType = ScrollRect.MovementType.Clamped;
        _treeScroll.scrollSensitivity = 0f;   // 휠은 패닝이 아니라 확대/축소로 쓴다(HandleZoom). 드래그로만 이동.

        // 사용자가 그래프를 움직이는 '입력'을 직접 잡는다(드래그 시작·휠).
        // onValueChanged 로는 코드의 포커스 이동과 사용자 조작이 구분되지 않아,
        // 포커스 중엔 닫기를 막다가 tween 이 끝난 뒤에야 닫히는 어색함이 생겼다.
        scrollRt.gameObject.AddComponent<ViewDragWatch>().onUserMove = OnUserMovedView;

        var viewport = CodeUI.CreateRect(scrollRt, "Viewport");
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();
        var viewportImg = viewport.gameObject.AddComponent<Image>(); // 빈 공간 드래그 스크롤 + 빈 곳 클릭 감지용 + 축소 시 배경
        // 콘텐츠(_treeContent)만 확대/축소되고 이 뷰포트 배경은 고정 크기다.
        // 축소해 콘텐츠가 뷰포트보다 작아져도, 그 여백이 티어 밴드와 같은 어두운 배경으로 채워져
        // "빈칸(밝은 카드)"이 드러나지 않는다. 배경 패널은 그대로 두고 안의 요소만 스케일되게 하는 핵심.
        viewportImg.color = CodeUI.Hex(0x141E36);
        var emptyClick = viewport.gameObject.AddComponent<ClickNotDrag>();
        emptyClick.onClick = OnEmptyClicked; // 드래그가 아닌 '클릭'일 때만 팝업 닫기
        _treeScroll.viewport = viewport;

        // content: center-origin (자식은 anchor 0.5,0.5 로 배치)
        _treeContent = CodeUI.CreateRect(viewport, "Content");
        _treeContent.anchorMin = _treeContent.anchorMax = new Vector2(0.5f, 0.5f);
        _treeContent.pivot = new Vector2(0.5f, 0.5f);
        _treeScroll.content = _treeContent;

        // 레이어 (뒤 → 앞): 밴드 / 선 / 노드 / 잠금덮개
        _bandLayer = MakeLayer(_treeContent, "BandLayer");
        _lineLayer = MakeLayer(_treeContent, "LineLayer");
        _nodeLayer = MakeLayer(_treeContent, "NodeLayer");
        _overlayLayer = MakeLayer(_treeContent, "OverlayLayer");

        // 데이터 없을 때 안내
        _emptyText = CodeUI.CreateText(card.transform, "Empty", 20f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(_emptyText.rectTransform);
        _emptyText.gameObject.SetActive(false);
    }

    private static RectTransform MakeLayer(Transform parent, string name)
    {
        var rt = CodeUI.CreateRect(parent, name);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private void BuildDetailPopup(Transform panel)
    {
        var card = CodeUI.CreateImage(panel, "DetailPopup", CodeUI.PanelBg, skin.panelSprite, skin);
        _detailRoot = card.rectTransform;
        _detailRoot.anchorMin = _detailRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _detailRoot.pivot = new Vector2(0.5f, 0.5f);
        _detailRoot.sizeDelta = new Vector2(detailWidth, 400f); // 높이는 ContentSizeFitter가 덮어씀

        // 트리 위에 떠 보이도록 그림자
        var shadow = card.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(5f, -5f);

        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 11f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // 내용 높이에 맞춰 자동

        // 아이콘 + 이름/티어
        var head = CodeUI.CreateRow(card.transform, "Head", 88f, 14f, TextAnchor.MiddleLeft);
        var iconBox = CodeUI.CreateImage(head, "IconBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = 88f;
        iconLe.preferredHeight = 88f;
        _detailIcon = CodeUI.CreateImage(iconBox.transform, "Icon", Color.white, rounded: false);
        var ir = _detailIcon.rectTransform;
        ir.anchorMin = Vector2.zero;
        ir.anchorMax = Vector2.one;
        ir.offsetMin = new Vector2(9f, 9f);
        ir.offsetMax = new Vector2(-9f, -9f);
        _detailIcon.preserveAspect = true;
        _detailIcon.raycastTarget = false;

        var headCol = CodeUI.CreateColumn(head, "HeadText", 4f);
        headCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _detailName = CodeUI.CreateText(headCol, "Name", 24f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        _detailName.textWrappingMode = TextWrappingModes.Normal;
        _detailName.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        _detailTier = CodeUI.CreateText(headCol, "Tier", 16f, FontStyles.Bold, CodeUI.AccentFill, TextAlignmentOptions.MidlineLeft, _loc);
        _detailTier.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        CodeUI.CreateDivider(card.transform);

        // 설명 (내용 높이에 맞춰 늘어남)
        _detailDesc = CodeUI.CreateText(card.transform, "Desc", 17f, FontStyles.Normal, CodeUI.LabelColor, TextAlignmentOptions.TopLeft, _loc);
        _detailDesc.textWrappingMode = TextWrappingModes.Normal;
        _detailDesc.gameObject.AddComponent<LayoutElement>().minHeight = 48f;

        // 효과 / 비용 상자
        _detailEffect = MakeInfoBox(card.transform, "EffectBox", out _);
        _detailCost = MakeInfoBox(card.transform, "CostBox", out _);

        // 상태 + 선행조건 (선행은 내용에 맞춰 자동 높이)
        _detailStatus = CodeUI.CreateText(card.transform, "Status", 19f, FontStyles.Bold, CodeUI.MutedColor, TextAlignmentOptions.MidlineLeft, _loc);
        _detailStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

        _detailPrereq = CodeUI.CreateText(card.transform, "Prereq", 15f, FontStyles.Normal, CodeUI.WarnColor, TextAlignmentOptions.TopLeft, _loc);
        _detailPrereq.textWrappingMode = TextWrappingModes.Normal;

        // 해금 버튼
        _unlockButton = CodeUI.CreateTextButton(card.transform, "Unlock", CodeUI.GoldColor, CodeUI.GoldFg, 20f,
            OnUnlockClicked, out _unlockLabel, skin.buttonSprite, skin, _loc);
        _unlockButton.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
        _loc.Bind(_unlockLabel, "ui_upgrade_unlock", "해금");

        _detailRoot.gameObject.SetActive(false);
    }

    /// <summary>라벨(왼쪽) + 값(오른쪽) 한 줄 상자. 반환값은 값 텍스트.</summary>
    private TextMeshProUGUI MakeInfoBox(Transform parent, string name, out TextMeshProUGUI label)
    {
        var box = CodeUI.CreateImage(parent, name, CodeUI.BoxBg, skin.boxSprite, skin);
        box.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
        var row = box.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 14, 0, 0);
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;
        row.childAlignment = TextAnchor.MiddleLeft;

        label = CodeUI.CreateText(box.transform, "Label", 17f, FontStyles.Normal, CodeUI.MutedColor, TextAlignmentOptions.MidlineLeft, _loc);
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;

        var value = CodeUI.CreateText(box.transform, "Value", 19f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineRight, _loc);
        value.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        return value;
    }

    // ===================================================
    // 트리 생성 (노드/선/밴드) — 최초 1회
    // ===================================================
    private void EnsureTreeBuilt()
    {
        if (_treeBuilt) return;

        _tree = ResolveTree();
        bool hasData = _tree != null && _tree.allNodes != null && _tree.allNodes.Count > 0;

        if (_emptyText != null)
        {
            _emptyText.gameObject.SetActive(!hasData);
            if (!hasData) _loc.Bind(_emptyText, "ui_upgrade_nodata", "업그레이드 데이터를 찾을 수 없습니다.");
        }
        if (_treeScroll != null) _treeScroll.gameObject.SetActive(hasData);
        if (!hasData) { _treeBuilt = true; return; }

        // --- 좌표 범위 계산 ---
        float minY = float.MaxValue, maxY = float.MinValue;
        float maxAbsX = 1f;
        foreach (var node in _tree.allNodes)
        {
            if (node == null) continue;
            minY = Mathf.Min(minY, node.uiPosition.y);
            maxY = Mathf.Max(maxY, node.uiPosition.y);
            maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(node.uiPosition.x));
        }
        _centerY = (minY + maxY) * 0.5f;

        // 콘텐츠를 노드 실제 범위(×graphScale)에 딱 맞춘다 → 뷰포트보다 크면 스크롤, 작으면 가운데 정렬.
        // (뷰포트 폭을 추정해 압축하던 방식은 추정이 어긋나면 가장자리 노드가 잘려서, 실측 범위 기반으로 바꿨다)
        float z = Mathf.Max(0.2f, graphScale);
        float contentW = (maxAbsX * z + nodeWidth * 0.5f + treePadHorizontal) * 2f;
        float contentH = (maxY - minY) * z + nodeHeight + treePadVertical * 2f;
        _treeContent.sizeDelta = new Vector2(contentW, contentH);

        // --- 티어 밴드 (서로 겹치지 않게 인접 티어 중간값으로 타일링) ---
        BuildTierBands(minY, maxY);

        // --- 노드 ---
        foreach (var node in _tree.allNodes)
        {
            if (node == null || string.IsNullOrEmpty(node.nodeId)) continue;
            var view = CreateNodeView(node);
            _nodeViews.Add(view);
            if (!_nodeViewById.ContainsKey(node.nodeId)) _nodeViewById.Add(node.nodeId, view);
        }

        // --- 연결선이 피할 노드 사각형 + 레인 점유 (선을 만들기 전에 전부 모아야 한다) ---
        _nodeObstacles.Clear();
        float obsHalfW = nodeWidth * 0.5f + ObstaclePad;
        float obsHalfH = nodeHeight * 0.5f + ObstaclePad;
        var mapped = new List<Vector2>(_tree.allNodes.Count);
        foreach (var node in _tree.allNodes)
        {
            if (node == null) continue;
            Vector2 c = new Vector2(Xmap(node.uiPosition.x), Ymap(node.uiPosition.y));
            mapped.Add(c);
            _nodeObstacles.Add(new Rect(c.x - obsHalfW, c.y - obsHalfH, obsHalfW * 2f, obsHalfH * 2f));
        }
        _laneRouter.Build(mapped);

        // --- 연결선 (부모 → 자식) ---
        // 허브(분기·합류) 공유 높이를 알아야 그리므로 **연결을 전부 모은 뒤에** 그린다.
        var childCount = new Dictionary<UpgradeNodeSO, int>();
        foreach (var node in _tree.allNodes)
        {
            if (node == null || node.parentNodes == null) continue;
            foreach (var parent in node.parentNodes)
            {
                if (parent == null) continue;
                childCount.TryGetValue(parent, out int c);
                childCount[parent] = c + 1;
            }
        }

        var links = new List<UpgradeLaneRouter.Link>();
        var linkOwners = new List<(UpgradeNodeSO parent, UpgradeNodeSO child)>();
        foreach (var node in _tree.allNodes)
        {
            if (node == null || node.parentNodes == null) continue;
            bool merges = IsMergePoint(node);
            foreach (var parent in node.parentNodes)
            {
                if (parent == null) continue;
                childCount.TryGetValue(parent, out int kids);
                links.Add(new UpgradeLaneRouter.Link(
                    parent.nodeId, new Vector2(Xmap(parent.uiPosition.x), Ymap(parent.uiPosition.y)),
                    node.nodeId, new Vector2(Xmap(node.uiPosition.x), Ymap(node.uiPosition.y)),
                    kids > 1, merges));
                linkOwners.Add((parent, node));
            }
        }

        float clearance = nodeHeight * 0.5f + TrunkGap;
        _laneRouter.BuildHubs(links, clearance);
        for (int i = 0; i < links.Count; i++)
            CreateConnection(linkOwners[i].parent, linkOwners[i].child, links[i], clearance);

        _treeBuilt = true;
    }

    private void BuildTierBands(float globalMinY, float globalMaxY)
    {
        // 티어별 노드 Y 범위 + 가장 왼쪽 X 수집 (X는 티어 번호를 노드 없는 곳에 두기 위함)
        var lo = new Dictionary<int, float>();
        var hi = new Dictionary<int, float>();
        var minX = new Dictionary<int, float>();
        foreach (var node in _tree.allNodes)
        {
            if (node == null) continue;
            float y = node.uiPosition.y;
            if (!lo.ContainsKey(node.tier)) { lo[node.tier] = y; hi[node.tier] = y; }
            else { lo[node.tier] = Mathf.Min(lo[node.tier], y); hi[node.tier] = Mathf.Max(hi[node.tier], y); }

            float x = node.uiPosition.x;
            if (!minX.ContainsKey(node.tier)) minX[node.tier] = x;
            else minX[node.tier] = Mathf.Min(minX[node.tier], x);
        }

        var tiers = new List<int>(lo.Keys);
        tiers.Sort((a, b) => lo[a].CompareTo(lo[b]));

        for (int i = 0; i < tiers.Count; i++)
        {
            int tier = tiers[i];
            float bandBottom = (i == 0)
                ? globalMinY - treePadVertical
                : BandSeam(tiers[i - 1], hi[tiers[i - 1]], lo[tier]);
            float bandTop = (i == tiers.Count - 1)
                ? globalMaxY + treePadVertical
                : BandSeam(tier, hi[tier], lo[tiers[i + 1]]);
            CreateTierBand(tier, bandBottom, bandTop, i, minX[tier]);
        }
    }

    /// <summary>
    /// 지층 <paramref name="lowerTier"/>와 그 위 지층을 가르는 선의 uiY.
    ///
    /// 기본은 "아래 지층 맨 위 노드와 위 지층 맨 아래 노드의 중간"이다. 두 지층의
    /// uiY가 겹치면 그 중간이 양쪽 노드를 가로지르므로, UpgradeTierBands.csv에
    /// 값을 적어 두면 그쪽을 쓴다(편집기에서 선을 끌면 적힌다).
    ///
    /// 아래 띠의 윗변과 위 띠의 아랫변이 **같은 함수**를 타야 둘이 딱 붙는다 —
    /// 한쪽만 덮어쓰면 띠 사이에 틈이 생기거나 서로 겹친다.
    /// </summary>
    private float BandSeam(int lowerTier, float lowerTop, float upperBottom)
    {
        TierInfo info = _tree != null ? _tree.GetTierInfo(lowerTier) : null;
        if (info != null && info.hasBandTop) return info.bandTop;
        return (lowerTop + upperBottom) * 0.5f;
    }

    private void CreateTierBand(int tier, float bandBottom, float bandTop, int order, float tierMinX)
    {
        float height = (bandTop - bandBottom) * graphScale;
        float centerMap = ((bandBottom + bandTop) * 0.5f - _centerY) * graphScale;

        TierInfo info = _tree.GetTierInfo(tier);
        Color bandColor = (order % 2 == 0) ? CodeUI.Hex(0x141E36) : CodeUI.Hex(0x18223C);

        var band = CodeUI.CreateImage(_bandLayer, $"Tier{tier}Band", bandColor, skin.cardSprite, skin);
        band.raycastTarget = false;
        SetupBandRect(band.rectTransform, centerMap, height);

        // 큰 반투명 티어 번호 — 그 티어의 '가장 왼쪽 노드'보다 더 왼쪽 빈 공간에 둔다.
        // (예전엔 트렁크 기준 고정 위치라 노드 위에 겹쳤다. 티어마다 왼쪽 끝이 달라 티어별로 계산)
        var num = CodeUI.CreateText(band.transform, "Num", 110f, FontStyles.Bold,
            new Color(1f, 1f, 1f, 0.06f), TextAlignmentOptions.MidlineRight, _loc);
        var numRt = num.rectTransform;
        numRt.anchorMin = new Vector2(0.5f, 0.5f);
        numRt.anchorMax = new Vector2(0.5f, 0.5f);
        numRt.pivot = new Vector2(1f, 0.5f); // 오른쪽 끝 기준 → 글자는 항상 이 지점의 왼쪽에만 그려진다
        numRt.sizeDelta = new Vector2(200f, 220f);
        numRt.anchoredPosition = new Vector2(Xmap(tierMinX) - nodeWidth * 0.5f - 44f, 0f);
        // 내부 tier는 0부터지만 화면 표시는 1단계부터 (tier+1)
        num.text = (tier + 1).ToString();

        // 티어 이름 (상단)
        var nameText = CodeUI.CreateText(band.transform, "TierName", 18f, FontStyles.Bold,
            new Color(1f, 1f, 1f, 0.28f), TextAlignmentOptions.Center, _loc);
        var nameRt = nameText.rectTransform;
        nameRt.anchorMin = new Vector2(0.5f, 1f);
        nameRt.anchorMax = new Vector2(0.5f, 1f);
        nameRt.pivot = new Vector2(0.5f, 1f);
        nameRt.sizeDelta = new Vector2(500f, 30f);
        nameRt.anchoredPosition = new Vector2(0f, -12f);
        nameText.text = (info != null && !string.IsNullOrEmpty(info.TierName)) ? info.TierName : $"Tier {tier + 1}";

        // 잠금 덮개 (raycast 차단 → 잠긴 티어 노드는 클릭 불가)
        var overlay = CodeUI.CreateImage(_overlayLayer, $"Tier{tier}Lock", new Color(0.03f, 0.05f, 0.10f, 0.86f), skin.cardSprite, skin);
        SetupBandRect(overlay.rectTransform, centerMap, height);
        overlay.raycastTarget = true;

        var lockCol = CodeUI.CreateColumn(overlay.transform, "LockText", 6f);
        var lockColRt = lockCol;
        lockColRt.anchorMin = new Vector2(0.5f, 0.5f);
        lockColRt.anchorMax = new Vector2(0.5f, 0.5f);
        lockColRt.pivot = new Vector2(0.5f, 0.5f);
        lockColRt.sizeDelta = new Vector2(560f, 120f);
        lockCol.gameObject.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

        var lockMark = CodeUI.CreateText(lockCol, "Mark", 30f, FontStyles.Bold, CodeUI.WarnColor, TextAlignmentOptions.Center, _loc);
        lockMark.text = lockGlyph;
        var overlayText = CodeUI.CreateText(lockCol, "Cond", 19f, FontStyles.Bold, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center, _loc);
        overlayText.textWrappingMode = TextWrappingModes.Normal;
        overlayText.text = (info != null && !string.IsNullOrEmpty(info.UnlockConditionText))
            ? info.UnlockConditionText
            : CodeUI.L("ui_upgrade_tier_locked", "이전 단계를 완료하면 열립니다.");

        _tierViews.Add(new TierView { tier = tier, band = band, overlay = overlay.gameObject, overlayText = overlayText });
    }

    private static void SetupBandRect(RectTransform rt, float centerY, float height)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = new Vector2(0f, centerY);
        rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
        rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
    }

    private NodeView CreateNodeView(UpgradeNodeSO node)
    {
        var root = CodeUI.CreateRect(_nodeLayer, "Node_" + node.nodeId);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(nodeWidth, nodeHeight);
        root.anchoredPosition = new Vector2(Xmap(node.uiPosition.x), Ymap(node.uiPosition.y));

        // 강조 링 (선택/해금가능 시 표시)
        var ring = CodeUI.CreateImage(root, "Ring", CodeUI.AccentFill, skin.slotHighlightSprite, skin);
        ring.raycastTarget = false;
        var ringRt = ring.rectTransform;
        ringRt.anchorMin = Vector2.zero;
        ringRt.anchorMax = Vector2.one;
        ringRt.offsetMin = new Vector2(-5f, -5f);
        ringRt.offsetMax = new Vector2(5f, 5f);
        ring.gameObject.SetActive(false);

        // 프레임 (클릭 대상)
        var frame = CodeUI.CreateImage(root, "Frame", CodeUI.SlotBg, skin.slotSprite, skin);
        CodeUI.StretchFull(frame.rectTransform);
        var btn = frame.gameObject.AddComponent<Button>();
        btn.targetGraphic = frame;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.14f, 1.14f, 1.14f, 1f);
        colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        btn.colors = colors;

        // 선택은 Button.onClick 이 아니라 ClickNotDrag 로 받는다.
        // (그래프를 드래그하면 콘텐츠가 손끝을 따라와 같은 노드 위에서 손을 떼므로,
        //  onClick 을 쓰면 '팬'이 노드 클릭으로 잘못 처리된다. Button 은 호버/프레스 연출용으로만 남긴다)
        var captured = node;
        frame.gameObject.AddComponent<ClickNotDrag>().onClick = () => SelectNode(captured);

        // 아이콘 상자
        var iconBox = CodeUI.CreateImage(frame.transform, "IconBox", CodeUI.BoxBg, skin.boxSprite, skin);
        iconBox.raycastTarget = false;
        var boxRt = iconBox.rectTransform;
        float iconSize = nodeWidth - 44f;
        boxRt.anchorMin = new Vector2(0.5f, 1f);
        boxRt.anchorMax = new Vector2(0.5f, 1f);
        boxRt.pivot = new Vector2(0.5f, 1f);
        boxRt.sizeDelta = new Vector2(iconSize, iconSize);
        boxRt.anchoredPosition = new Vector2(0f, -10f);

        var icon = CodeUI.CreateImage(iconBox.transform, "Icon", Color.white, rounded: false);
        icon.raycastTarget = false;
        var iconRt = icon.rectTransform;
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(7f, 7f);
        iconRt.offsetMax = new Vector2(-7f, -7f);
        icon.preserveAspect = true;

        // 이름 (하단)
        var nameText = CodeUI.CreateText(frame.transform, "Name", 14f, FontStyles.Bold, Color.white, TextAlignmentOptions.Top, _loc);
        nameText.textWrappingMode = TextWrappingModes.Normal;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        var nameRt = nameText.rectTransform;
        nameRt.anchorMin = new Vector2(0f, 0f);
        nameRt.anchorMax = new Vector2(1f, 0f);
        nameRt.pivot = new Vector2(0.5f, 0f);
        nameRt.offsetMin = new Vector2(4f, 22f);
        nameRt.offsetMax = new Vector2(-4f, 40f);
        nameText.text = node.DisplayName;

        // 비용 (맨 아래)
        var costText = CodeUI.CreateText(frame.transform, "Cost", 13f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.Bottom, _loc);
        var costRt = costText.rectTransform;
        costRt.anchorMin = new Vector2(0f, 0f);
        costRt.anchorMax = new Vector2(1f, 0f);
        costRt.pivot = new Vector2(0.5f, 0f);
        costRt.offsetMin = new Vector2(4f, 4f);
        costRt.offsetMax = new Vector2(-4f, 20f);
        costText.text = $"{CodeUI.Gold(node.cost)} G";

        return new NodeView
        {
            node = node,
            root = root,
            ring = ring,
            frame = frame,
            iconBox = iconBox,
            icon = icon,
            name = nameText,
            cost = costText,
        };
    }

    private void CreateConnection(UpgradeNodeSO parent, UpgradeNodeSO child,
                                  in UpgradeLaneRouter.Link link, float clearance)
    {
        var lineObj = new GameObject("Line", typeof(RectTransform));
        lineObj.transform.SetParent(_lineLayer, false);

        var r = lineObj.AddComponent<OrthogonalUILineRenderer>();
        r.lineWidth = 4f;
        r.lineColor = CodeUI.Hex(0x3A4668);          // 잠김
        r.unlockedLineColor = CodeUI.GoldColor;       // 부모 해금 시
        r.lockedLineColor = CodeUI.Hex(0x2A3452);
        // 차선 간격은 노드 반폭보다 넓어야 우회한 선이 노드에 붙어 보이지 않는다
        r.detourLane = nodeWidth * 0.5f + 46f;

        bool parentUnlocked = UpgradeManager.Instance != null && UpgradeManager.Instance.IsNodeUnlocked(parent.nodeId);
        // 사람이 그린 꺾임점은 ui 좌표로 저장돼 있다(트리 중앙이 움직여도 안 흔들리게) — 매핑으로 바꾼다.
        r.DrawOrthogonalLine(link.Start, link.End, parentUnlocked, _nodeObstacles,
                             _laneRouter.JogY(link, clearance), MapBends(child, parent.nodeId));

        // 선분이 클릭/스크롤을 가로채지 않도록
        foreach (var img in lineObj.GetComponentsInChildren<Image>()) img.raycastTarget = false;

        _lineViews.Add(new LineView { renderer = r, parent = parent });
    }

    /// <summary>사람이 그린 꺾임점을 ui → 매핑 좌표로 옮긴다. 지정이 없으면 null(= 자동).</summary>
    private List<Vector2> MapBends(UpgradeNodeSO child, string parentId)
    {
        var ui = child != null ? child.GetLineBends(parentId) : null;
        if (ui == null || ui.Count == 0) return null;

        var mapped = new List<Vector2>(ui.Count);
        for (int i = 0; i < ui.Count; i++) mapped.Add(new Vector2(Xmap(ui[i].x), Ymap(ui[i].y)));
        return mapped;
    }

    /// <summary>부모가 둘 이상인 합류 노드인가. 들어오는 선이 노드까지 각자 레인을 지켜야 한다.</summary>
    private static bool IsMergePoint(UpgradeNodeSO child)
    {
        if (child == null || child.parentNodes == null) return false;

        int parents = 0;
        foreach (var p in child.parentNodes)
        {
            if (p == null) continue;
            if (++parents > 1) return true;
        }
        return false;
    }

    private float Xmap(float x) => x * graphScale;
    private float Ymap(float y) => (y - _centerY) * graphScale;

    // ===================================================
    // 선택 / 해금
    // ===================================================
    private void SelectNode(UpgradeNodeSO node)
    {
        if (node == null) return;
        CodeUI.PlaySfx(clickSfxName);
        _selected = node;
        _detailShown = false;   // 이동이 끝난 뒤에 띄운다 (이동 중에 띄우면 팝업 위치가 계속 바뀌어 어색하다)
        RefreshNodes();          // 선택 링은 즉시 표시해 클릭 반응을 준다
        RefreshDetail();         // → 팝업은 아직 숨김

        if (_nodeViewById.TryGetValue(node.nodeId, out var view) && view != null)
            StartFocus(view);    // 이동이 끝나면 FocusRoutine 이 그 자리에서 팝업을 띄운다
    }

    /// <summary>트리 빈 공간을 클릭하면 상세 팝업을 닫는다.</summary>
    private void OnEmptyClicked()
    {
        if (_selected == null && (_detailRoot == null || !_detailRoot.gameObject.activeSelf)) return;
        CodeUI.PlaySfx(clickSfxName);
        HideDetail();
    }

    /// <summary>
    /// 사용자가 그래프를 움직이기 시작하면(드래그·휠) 즉시 포커스 이동을 멈추고 팝업을 닫는다.
    /// 진행 중인 tween 을 바로 취소하므로 tween 과 드래그가 스크롤을 동시에 건드려 튀는 일이 없다.
    /// </summary>
    private void OnUserMovedView()
    {
        StopFocus();
        // 팝업이 떠 있든, 아직 이동 중이라 안 떴든(선택만 된 상태) 전부 취소한다
        if (_selected != null) HideDetail();
    }

    private void HideDetail()
    {
        StopFocus();
        _selected = null;
        _detailShown = false;
        if (_detailRoot != null) _detailRoot.gameObject.SetActive(false);
        RefreshNodes(); // 선택 링 해제
    }

    private void StopFocus()
    {
        if (_focusRoutine != null) { StopCoroutine(_focusRoutine); _focusRoutine = null; }
    }

    private void StartFocus(NodeView view)
    {
        StopFocus();
        if (_treeScroll == null || view == null) return;
        _focusRoutine = StartCoroutine(FocusRoutine(view));
    }

    /// <summary>클릭한 노드가 뷰포트 가운데 오도록 스크롤을 부드럽게 이동시킨다.</summary>
    private IEnumerator FocusRoutine(NodeView view)
    {
        _treeScroll.StopMovement(); // 남아있던 관성 제거

        Vector2 from = _treeScroll.normalizedPosition;
        Vector2 to = ComputeFocusNormalized(view);

        // 실질적으로 이동이 없으면 기다리지 않고 바로 띄운다
        float dur = ((to - from).sqrMagnitude < 1e-6f) ? 0f : Mathf.Max(0f, focusDuration);
        if (dur > 0.001f)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                _treeScroll.normalizedPosition = Vector2.Lerp(from, to, k);
                yield return null;
            }
        }

        _treeScroll.normalizedPosition = to;
        _treeScroll.StopMovement();

        // 이동이 끝난 자리에서 띄우고 배치한다 → 뜬 뒤에는 위치가 변하지 않는다
        _detailShown = true;
        RefreshDetail();
        PositionDetailNear(view.root);

        _focusRoutine = null;
    }

    /// <summary>노드를 뷰포트 중앙에 두는 normalizedPosition (스크롤 여유가 없는 축은 그대로).</summary>
    private Vector2 ComputeFocusNormalized(NodeView view)
    {
        Vector2 result = _treeScroll.normalizedPosition;
        if (_treeContent == null || _treeScroll.viewport == null) return result;

        // 휠 확대 배율(_treeContent.localScale)만큼 콘텐츠·노드 좌표가 커진다 — normalizedPosition 은
        // ScrollRect 가 스케일 반영한 경계로 해석하므로 여기서도 _zoom 을 곱해 맞춘다.
        Vector2 contentSize = _treeContent.rect.size * _zoom;
        Vector2 viewSize = _treeScroll.viewport.rect.size;
        Vector2 nodePos = view.root.anchoredPosition * _zoom; // 콘텐츠 중심 기준(스케일 반영)

        float fromLeft = contentSize.x * 0.5f + nodePos.x;
        float fromBottom = contentSize.y * 0.5f + nodePos.y;

        float rangeX = contentSize.x - viewSize.x;
        float rangeY = contentSize.y - viewSize.y;
        if (rangeX > 1f) result.x = Mathf.Clamp01((fromLeft - viewSize.x * 0.5f) / rangeX);
        if (rangeY > 1f) result.y = Mathf.Clamp01((fromBottom - viewSize.y * 0.5f) / rangeY);
        return result;
    }

    /// <summary>상세 팝업을 클릭한 노드 옆에 배치하고 본체 영역 안으로 클램프한다.</summary>
    private void PositionDetailNear(RectTransform nodeRect)
    {
        if (_detailRoot == null || _panelRect == null || nodeRect == null) return;

        // 정보 팝업은 줌 배율과 무관하게 '고정 크기'로 화면에 읽기 좋게 뜬다.
        // (트리를 확대하면 노드는 커지지만 이 패널까지 키우면 화면을 벗어나므로 스케일을 걸지 않는다)
        _detailRoot.localScale = Vector3.one;

        // 크기는 RefreshDetail 에서 이미 확정해 둔다 (여기는 포커스 이동 중 매 프레임 호출되므로 계산만)
        float w = _detailRoot.rect.width;
        float h = _detailRoot.rect.height;

        // 노드 중심(월드) → 패널 로컬 좌표
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, nodeRect.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_panelRect, screen, null, out Vector2 nodeLocal);

        // 노드는 _zoom 만큼 커진 상태이므로, 팝업이 노드를 덮지 않게 노드 반폭·간격만 배율을 반영한다.
        float halfNodeW = nodeWidth * 0.5f * _zoom;
        float gap = 22f * _zoom;
        float panelHalfW = _panelRect.rect.width * 0.5f;
        float panelHalfH = _panelRect.rect.height * 0.5f;

        // 오른쪽에 두되, 넘치면 왼쪽으로
        float cx = nodeLocal.x + halfNodeW + gap + w * 0.5f;
        if (cx + w * 0.5f > panelHalfW - 12f)
            cx = nodeLocal.x - halfNodeW - gap - w * 0.5f;
        float cy = nodeLocal.y;

        // 본체 영역(타이틀·힌트 제외) 안으로 클램프
        float topLimit = panelHalfH - (Side + TitleH + 12f);
        float botLimit = -panelHalfH + (Side + HintH + 6f);
        cx = Mathf.Clamp(cx, -panelHalfW + w * 0.5f + 12f, panelHalfW - w * 0.5f - 12f);

        float availTop = topLimit - h * 0.5f - 8f;
        float availBot = botLimit + h * 0.5f + 8f;
        cy = (availBot > availTop) ? (topLimit + botLimit) * 0.5f : Mathf.Clamp(cy, availBot, availTop);

        _detailRoot.anchoredPosition = new Vector2(cx, cy);
        _detailRoot.SetAsLastSibling();
    }

    private void OnUnlockClicked()
    {
        var mgr = UpgradeManager.Instance;
        if (_selected == null || mgr == null) return;

        // 디버그 연쇄 해금(콘솔 upg). 선행·계층·골드를 전부 무시하고 이 노드까지 통째로 연다.
        // 치트 구매는 텔레메트리에 남기지 않는다 — upgrade_purchased에 섞이면 밸런스 표가 오염된다.
        if (UpgradeManager.DebugChainUnlock && !mgr.IsMaxed(_selected))
        {
            mgr.DebugForceUnlockChain(_selected);
            CodeUI.PlaySfx(SfxKeys.UpgradeUnlock);
            _needsRefresh = true;
            return;
        }

        if (mgr.CanUnlock(_selected))
        {
            if (mgr.UnlockNode(_selected))
            {
                // 씬/프리팹에 범용 기본값(SfxKeys.UiClick)이 구워져 있으면
                // 미지정으로 보고 강화 전용음으로 폴백한다.
                string sfx = (string.IsNullOrEmpty(unlockSfxName) || unlockSfxName == SfxKeys.UiClick)
                    ? SfxKeys.UpgradeUnlock
                    : unlockSfxName;

                // 업그레이드 트리 노드 해금 — 단발 파워업음.
                // (장비 강화(+N)는 별개 시스템이다. 그쪽은 EquipmentUpgradeStore.TryUpgrade에서
                //  SfxKeys.EquipEnhance 금속 3연타가 울린다. 두 소리를 섞지 말 것.)
                CodeUI.PlaySfx(sfx);
                // OnUpgradeStateChanged 이벤트가 _needsRefresh 를 세우지만, 즉시성 위해 한 번 더
                _needsRefresh = true;
            }
        }
        else
        {
            // 못 사는 노드에 스페이스를 눌렀다 = "사고 싶었지만 못 샀다".
            // UnlockNode 가 upgrade_blocked 를 남기고 false 를 돌려준다.
            // 이 else 가 없으면 그 기록이 어디서도 안 생긴다 — 버튼은 비활성이라
            // 클릭 경로가 없고, 스페이스만이 여기 도달한다.
            // (배경: Assets/Docs/economy/upgrade-balance-charter.md §6-A)
            mgr.UnlockNode(_selected);
        }
    }

    // ===================================================
    // 갱신
    // ===================================================
    private void RefreshAll()
    {
        RefreshGold();
        RefreshNodes();
        RefreshTiers();
        RefreshLines();
        RefreshDetail();
    }

    private void RefreshGold()
    {
        if (_goldText != null)
            _goldText.text = $"{CodeUI.Gold(_playerStat != null ? _playerStat.Gold : 0)} G";
    }

    private void RefreshNodes()
    {
        var mgr = UpgradeManager.Instance;
        foreach (var v in _nodeViews)
        {
            if (v == null || v.node == null) continue;

            // 이름은 언어 변경 대비 매번 갱신
            v.name.text = v.node.DisplayName;

            NodeLockState state = mgr != null ? mgr.GetNodeLockState(v.node) : NodeLockState.Locked;
            bool selected = (_selected != null && _selected.nodeId == v.node.nodeId);

            Color catColor = CategoryColor(v.node);
            bool hasIcon = v.node.icon != null;
            v.icon.enabled = hasIcon;
            if (hasIcon) v.icon.sprite = v.node.icon;

            // 다단계 노드는 '보유' 대신 레벨과 다음 가격을 보여준다
            int level = mgr != null ? mgr.GetNodeLevel(v.node) : 0;
            bool maxed = mgr != null && mgr.IsMaxed(v.node);
            int nextCost = mgr != null ? mgr.GetNextCost(v.node) : v.node.cost;

            switch (state)
            {
                case NodeLockState.Unlocked:
                    CodeUI.ApplySkin(v.frame, CodeUI.Hex(0x1E2A48), skin.slotSprite, skin);
                    v.iconBox.color = Dim(catColor, 0.55f);
                    v.icon.color = Color.white;
                    v.name.color = Color.white;
                    if (v.node.IsMultiLevel && !maxed)
                    {
                        // 아직 올릴 여지는 있는데 지금은 못 사는 상태(골드 부족 등)
                        v.cost.text = $"{LevelTag(level, v.node)}  {CodeUI.Gold(nextCost)} G";
                        v.cost.color = CodeUI.MutedColor;
                    }
                    else
                    {
                        v.cost.text = v.node.IsMultiLevel
                            ? $"{LevelTag(level, v.node)}  {CodeUI.L("ui_upgrade_owned", "보유")}"
                            : CodeUI.L("ui_upgrade_owned", "보유");
                        v.cost.color = CodeUI.PositiveColor;
                    }
                    break;

                case NodeLockState.Unlockable:
                    CodeUI.ApplySkin(v.frame, CodeUI.Hex(0x243257), skin.slotSprite, skin);
                    v.iconBox.color = Dim(catColor, 0.7f);
                    v.icon.color = Color.white;
                    v.name.color = Color.white;
                    v.cost.text = v.node.IsMultiLevel
                        ? $"{LevelTag(level, v.node)}  {CodeUI.Gold(nextCost)} G"
                        : $"{CodeUI.Gold(nextCost)} G";
                    v.cost.color = CodeUI.GoldColor;
                    break;

                case NodeLockState.TierLocked:
                    CodeUI.ApplySkin(v.frame, CodeUI.Hex(0x0C1220), skin.slotSprite, skin);
                    v.iconBox.color = CodeUI.Hex(0x141B2E);
                    v.icon.color = new Color(1f, 1f, 1f, 0.25f);
                    v.name.color = new Color(0.5f, 0.55f, 0.66f, 1f);
                    v.cost.text = $"{CodeUI.Gold(nextCost)} G";
                    v.cost.color = CodeUI.MutedColor;
                    break;

                default: // Locked (선행/골드 부족)
                    CodeUI.ApplySkin(v.frame, CodeUI.SlotBg, skin.slotSprite, skin);
                    v.iconBox.color = Dim(catColor, 0.28f);
                    v.icon.color = new Color(1f, 1f, 1f, 0.5f);
                    v.name.color = CodeUI.LabelColor;
                    v.cost.text = $"{CodeUI.Gold(nextCost)} G";
                    v.cost.color = CodeUI.MutedColor;
                    break;
            }

            // 강조 링: 선택 = 주황, 해금가능 = 초록
            if (selected)
            {
                v.ring.gameObject.SetActive(true);
                CodeUI.ApplySkin(v.ring, CodeUI.WarnColor, skin.slotHighlightSprite, skin);
            }
            else if (state == NodeLockState.Unlockable)
            {
                v.ring.gameObject.SetActive(true);
                CodeUI.ApplySkin(v.ring, CodeUI.PositiveColor, skin.slotHighlightSprite, skin);
            }
            else
            {
                v.ring.gameObject.SetActive(false);
            }
        }
    }

    private void RefreshTiers()
    {
        var mgr = UpgradeManager.Instance;
        foreach (var t in _tierViews)
        {
            if (t == null || t.overlay == null) continue;
            bool unlocked = mgr != null && mgr.IsTierUnlocked(t.tier);
            t.overlay.SetActive(!unlocked);
        }
    }

    private void RefreshLines()
    {
        var mgr = UpgradeManager.Instance;
        foreach (var l in _lineViews)
        {
            if (l == null || l.renderer == null || l.parent == null) continue;
            bool parentUnlocked = mgr != null && mgr.IsNodeUnlocked(l.parent.nodeId);
            l.renderer.SetUnlocked(parentUnlocked);
        }
    }

    private void RefreshDetail()
    {
        bool has = _selected != null && _detailShown;
        if (_detailRoot != null) _detailRoot.gameObject.SetActive(has);
        if (!has) return;

        var mgr = UpgradeManager.Instance;
        var node = _selected;

        _detailIcon.enabled = node.icon != null;
        if (node.icon != null) _detailIcon.sprite = node.icon;
        else _detailIcon.color = Dim(CategoryColor(node), 0.7f);
        if (node.icon != null) _detailIcon.color = Color.white;

        _detailName.text = node.DisplayName;

        int level = mgr != null ? mgr.GetNodeLevel(node) : 0;
        bool maxed = mgr != null && mgr.IsMaxed(node);

        TierInfo info = _tree != null ? _tree.GetTierInfo(node.tier) : null;
        string tierName = (info != null && !string.IsNullOrEmpty(info.TierName)) ? info.TierName : $"Tier {node.tier + 1}";
        // 다단계 노드는 계층 줄에 레벨을 같이 적는다 — 줄을 새로 만들면 팝업이 커져 노드를 가린다
        _detailTier.text = node.IsMultiLevel ? $"{tierName}   {LevelTag(level, node)}" : tierName;

        _detailDesc.text = string.IsNullOrEmpty(node.Description) ? "" : node.Description;

        // 효과 (다단계면 현재 총합 + 레벨당 증가분)
        SetInfoBoxLabel(_detailEffect, CodeUI.L("ui_upgrade_effect", "효과"));
        _detailEffect.text = EffectText(node, level);
        _detailEffect.color = CategoryColor(node);

        // 비용 (다음 레벨 가격)
        SetInfoBoxLabel(_detailCost, CodeUI.L("ui_upgrade_cost", "비용"));
        int gold = _playerStat != null ? _playerStat.Gold : 0;
        int nextCost = mgr != null ? mgr.GetNextCost(node) : node.cost;
        _detailCost.text = maxed ? CodeUI.L("ui_upgrade_level_max", "MAX") : $"{CodeUI.Gold(nextCost)} G";

        // 상태 판정
        bool tierUnlocked = mgr != null && mgr.IsTierUnlocked(node.tier);
        bool prereqMet = ArePrereqsMet(node, mgr);
        bool affordable = gold >= nextCost;

        _detailCost.color = (maxed || affordable) ? CodeUI.GoldColor : CodeUI.NegativeColor;

        string prereq = "";
        if (maxed)
        {
            _detailStatus.text = node.IsMultiLevel
                ? CodeUI.L("ui_upgrade_status_maxlevel", "최대 레벨")
                : CodeUI.L("ui_upgrade_status_owned", "보유 중");
            _detailStatus.color = CodeUI.PositiveColor;
        }
        else if (!tierUnlocked)
        {
            _detailStatus.text = CodeUI.L("ui_upgrade_status_tierlocked", "계층 잠김");
            _detailStatus.color = CodeUI.MutedColor;
            if (info != null && !string.IsNullOrEmpty(info.UnlockConditionText)) prereq = info.UnlockConditionText;
        }
        else if (!prereqMet)
        {
            _detailStatus.text = CodeUI.L("ui_upgrade_status_prereq", "선행 업그레이드 필요");
            _detailStatus.color = CodeUI.WarnColor;
            prereq = MissingParentText(node, mgr);
        }
        else if (!affordable)
        {
            _detailStatus.text = CodeUI.L("ui_upgrade_status_nogold", "골드 부족");
            _detailStatus.color = CodeUI.NegativeColor;
        }
        else
        {
            _detailStatus.text = level >= 1
                ? CodeUI.L("ui_upgrade_status_levelup", "강화 가능")
                : CodeUI.L("ui_upgrade_status_ready", "해금 가능");
            _detailStatus.color = CodeUI.PositiveColor;
        }
        _detailPrereq.text = prereq;

        // 해금 버튼 — 이미 산 다단계 노드면 '강화 Lv N'으로 바뀐다
        bool canUnlock = mgr != null && mgr.CanUnlock(node);
        // 연쇄 해금 치트가 켜져 있으면 잠긴 노드도 눌러야 한다 — 버튼이 비활성이면
        // 클릭 경로가 없어 치트가 스페이스로만 먹는다.
        bool cheat = UpgradeManager.DebugChainUnlock && !maxed;
        _unlockButton.interactable = canUnlock || cheat;
        if (maxed)
        {
            _unlockLabel.text = node.IsMultiLevel
                ? CodeUI.L("ui_upgrade_level_maxed", "최대 레벨")
                : CodeUI.L("ui_upgrade_owned_full", "보유 완료");
            CodeUI.ApplySkin(_unlockButton.image, CodeUI.Hex(0x1E2A48), skin.buttonSprite, skin);
            _unlockLabel.color = CodeUI.PositiveColor;
        }
        else
        {
            string verb = level >= 1
                ? $"{CodeUI.L("ui_upgrade_levelup", "강화")} Lv {level + 1}"
                : CodeUI.L("ui_upgrade_unlock", "해금");
            _unlockLabel.text = cheat && !canUnlock
                ? $"{verb}  (연쇄 해금)"
                : $"{verb}  ({CodeUI.Gold(nextCost)} G)";
            if (canUnlock || cheat)
            {
                CodeUI.ApplySkin(_unlockButton.image, CodeUI.GoldColor, skin.buttonSprite, skin);
                _unlockLabel.color = CodeUI.GoldFg;
            }
            else
            {
                CodeUI.ApplySkin(_unlockButton.image, CodeUI.NeutralBg, skin.buttonSprite, skin);
                _unlockLabel.color = new Color(1f, 1f, 1f, 0.7f);
            }
        }

        // 내용이 바뀌었으니 팝업 크기를 여기서 확정한다 (PositionDetailNear 는 이 크기를 읽어 배치만 한다)
        LayoutRebuilder.ForceRebuildLayoutImmediate(_detailRoot);
    }

    private static void SetInfoBoxLabel(TextMeshProUGUI valueText, string label)
    {
        // 값 텍스트의 형제(Label)를 찾아 세팅
        var parent = valueText.transform.parent;
        var labelTr = parent.Find("Label");
        if (labelTr != null)
        {
            var l = labelTr.GetComponent<TextMeshProUGUI>();
            if (l != null) l.text = label;
        }
    }

    // ===================================================
    // 헬퍼
    // ===================================================
    private static bool ArePrereqsMet(UpgradeNodeSO node, UpgradeManager mgr)
    {
        if (mgr == null) return false;
        if (node.parentNodes == null) return true;
        foreach (var p in node.parentNodes)
            if (p != null && !mgr.IsNodeUnlocked(p.nodeId)) return false;
        return true;
    }

    private static string MissingParentText(UpgradeNodeSO node, UpgradeManager mgr)
    {
        if (node.parentNodes == null || mgr == null) return "";
        var names = new List<string>();
        foreach (var p in node.parentNodes)
            if (p != null && !mgr.IsNodeUnlocked(p.nodeId)) names.Add("· " + p.DisplayName);
        if (names.Count == 0) return "";
        return CodeUI.L("ui_upgrade_prereq_label", "필요:") + "\n" + string.Join("\n", names);
    }

    /// <summary>"Lv 2/5" 뱃지. 아직 안 산 노드는 "Lv 0/5"로 남은 단계를 미리 보여준다.</summary>
    private static string LevelTag(int level, UpgradeNodeSO node)
        => $"Lv {level}/{node.EffectiveMaxLevel}";

    /// <summary>
    /// 효과 표시. 다단계 노드는 '지금 붙어 있는 총합 (레벨당 증가분)'을 같이 적는다 —
    /// 레벨당 값만 적으면 Lv3인데 +5로 보여 실제 스탯과 어긋난다.
    /// </summary>
    private static string EffectText(UpgradeNodeSO node, int level = 0)
    {
        if (node.effect == null) return "-";

        string per = FormatEffect(node.effect, 1);
        if (!node.IsMultiLevel || level <= 0) return per;

        return $"{FormatEffect(node.effect, level)}  ({CodeUI.L("ui_upgrade_per_level", "레벨당")} {per})";
    }

    /// <summary>효과값을 level단계 적용한 결과. 곱연산은 거듭제곱, 합연산은 곱셈이다.</summary>
    private static string FormatEffect(UpgradeEffectSO effect, int level)
    {
        int lv = Mathf.Max(1, level);
        if (effect.isPercentage)
        {
            float pct = (Mathf.Pow(effect.value, lv) - 1f) * 100f;
            string psign = pct >= 0f ? "+" : "";
            return $"{psign}{pct:0.#}%";
        }
        float v = effect.value * lv;
        string sign = v >= 0f ? "+" : "";
        // 소수 3자리까지 — 0.# 로 자르면 -0.05 단계값이 -0.1 로 반올림돼 CSV 값과 달라 보인다
        return $"{sign}{v:0.###}";
    }

    /// <summary>효과 타입 대역(100·200·…)으로 색을 매긴다 — 트리 시각 그룹핑용.</summary>
    private static Color CategoryColor(UpgradeNodeSO node)
    {
        if (node == null || node.effect == null) return CodeUI.Hex(0x8A94AE);
        int t = (int)node.effect.type;
        if (t >= 100 && t < 200) return CodeUI.MineralColor;        // 채굴
        if (t >= 200 && t < 250) return CodeUI.EquipColor;          // 도구
        if (t >= 250 && t < 300) return CodeUI.Hex(0xE0B060);       // 드릴
        if (t >= 300 && t < 400) return CodeUI.NegativeColor;       // 전투
        if (t >= 400 && t < 500) return CodeUI.ItemColor;           // 이동/체력
        if (t >= 500 && t < 600) return CodeUI.GoldColor;           // 자원/확률
        if (t >= 600 && t < 700) return CodeUI.QuestColor;          // 인벤/시설
        if (t >= 700 && t < 800) return CodeUI.AccentFill;          // 시야/환경
        return CodeUI.Hex(0x8A94AE);
    }

    private static Color Dim(Color c, float mul) => new Color(c.r * mul, c.g * mul, c.b * mul, 1f);

    /// <summary>
    /// '클릭'만 잡는다(노드 선택 · 빈 곳 클릭 공용). 드래그(그래프 이동)와 구분하기 위해
    /// 누른 지점과 뗀 지점의 이동량이 임계값 이하일 때만 콜백을 호출한다.
    /// 그래프를 드래그하면 콘텐츠가 손끝을 따라와 같은 대상 위에서 손을 떼므로
    /// Button.onClick / IPointerClickHandler 로는 팬과 클릭이 구분되지 않는다.
    /// (ScrollRect 드래그는 IDragHandler로 따로 처리되므로 팬은 그대로 동작한다)
    /// </summary>
    private class ClickNotDrag : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public System.Action onClick;
        private Vector2 _down;
        private bool _tracking;
        private const float Threshold = 12f;

        public void OnPointerDown(PointerEventData e) { _down = e.position; _tracking = true; }

        public void OnPointerUp(PointerEventData e)
        {
            bool wasClick = _tracking
                            && !e.dragging   // 유니티가 드래그로 판정했으면(그래프를 움직였으면) 클릭이 아니다
                            && (e.position - _down).sqrMagnitude <= Threshold * Threshold;
            _tracking = false;
            if (wasClick) onClick?.Invoke();
        }
    }

    /// <summary>
    /// 사용자가 스크롤 뷰를 '직접' 움직이는 입력(드래그 시작·휠)만 알린다.
    /// ScrollRect 와 같은 오브젝트에 붙여 두면 두 컴포넌트가 같은 이벤트를 함께 받는다.
    /// </summary>
    private class ViewDragWatch : MonoBehaviour, IBeginDragHandler, IScrollHandler
    {
        public System.Action onUserMove;

        public void OnBeginDrag(PointerEventData e) => onUserMove?.Invoke();
        public void OnScroll(PointerEventData e) => onUserMove?.Invoke();
    }
}
