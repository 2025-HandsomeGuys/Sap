// @tags: upgrade, equipment, relic, ui, overlay, code-generated, workbench, enhance, plusN, ground

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 장비·유물 강화 오버레이(작업대) — 왼쪽에 보유 장비·유물 목록, 오른쪽에 선택 아이템의 강화 상세.
/// UI는 전부 코드로 생성한다(WarehouseOverlayUI·UpgradeOverlayUI 패턴) — 씬/프리팹 세팅 없이 <see cref="Open"/>만으로 동작.
///
/// 데이터·강화 로직 위임:
///  - 장비 레벨/비용/스탯: <see cref="EquipmentUpgradeStore"/> (저작 테이블 또는 임시 테스트 테이블)
///  - 유물 레벨: <see cref="Relic.RelicInventory"/> (TryUpgrade) + <see cref="Relic.RelicManager"/> (RefreshLevel)
///  - 재화: <see cref="PlayerStat"/>(골드) · <see cref="WarehouseManager"/>(창고 광물)
///
/// 진입: <see cref="UIStateManager"/>의 <c>UIState.Workbench</c> → 작업대(WorldInteractable) 상호작용.
/// 강화는 즉시가 아니라 "탕..탕.." 짧은 연출 후 적용된다.
/// </summary>
public class EquipmentUpgradeOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static EquipmentUpgradeOverlayUI _instance;

    public static EquipmentUpgradeOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<EquipmentUpgradeOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("EquipmentUpgradeOverlayUI");
                    _instance = go.AddComponent<EquipmentUpgradeOverlayUI>();
                }
            }
            return _instance;
        }
    }

    public static bool IsOpen => _instance != null && _instance._isOpen;
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>씬이 바뀌면 무조건 닫는다(안전망) — DontDestroyOnLoad라 열린 채 씬이 바뀌면 클릭을 먹고 timeScale이 굳는다.</summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!_isOpen && !_opening) return;
        _opening = false;
        _isOpen = false;
        Time.timeScale = 1f;
        StopUpgradeAnim();
        _nav.End();
        Unsubscribe();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 인스펙터
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.55f;
    [SerializeField] private bool useBlurBackdrop = true;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("동작")]
    [SerializeField] private bool pauseGameWhileOpen = true;
    [SerializeField] private bool closeOnEscape = true;
    [Tooltip("전체·장비·유물 탭을 왼쪽으로 전환하는 키. None이면 비활성")]
    [SerializeField] private KeyCode prevTabKey = KeyCode.Q;
    [Tooltip("전체·장비·유물 탭을 오른쪽으로 전환하는 키. None이면 비활성")]
    [SerializeField] private KeyCode nextTabKey = KeyCode.E;
    [Tooltip("끝에서 한 번 더 누르면 반대쪽 끝으로 순환한다.")]
    [SerializeField] private bool wrapTabCycle = true;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 1480f;
    [SerializeField] private float panelHeight = 900f;
    [Range(2, 8)][SerializeField] private int listColumns = 4;
    [SerializeField] private float slotSize = 92f;
    [SerializeField] private int sortingOrder = 30600;

    [Header("강화 연출 (탕..탕..)")]
    [Tooltip("망치 타격 횟수")]
    [Range(1, 5)][SerializeField] private int hammerHits = 3;
    [Tooltip("타격 간격(초, unscaled)")]
    [SerializeField] private float hammerInterval = 0.16f;

    [Header("효과음 (SoundDataSO에 등록된 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;
    // 강화음은 여기 한 곳에서만 울린다. 타격 연출 1회 = 소리 1회로 화면과 붙어 있어야 하므로
    // 데이터 계층(EquipmentUpgradeStore)에는 사운드를 두지 않는다 — 두면 연출이 끝난 뒤
    // 별개의 연타가 몰아서 터져 "소리가 여러 번 난다"로 들린다.
    [SerializeField] private string hammerSfxName = SfxKeys.EquipEnhance;
    [SerializeField] private string successSfxName = SfxKeys.UiClick;
    [SerializeField] private string failSfxName = SfxKeys.UiClick;

    [Header("글리프")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 상수
    // ===================================================
    private const float FadeDuration = 0.14f;
    private const float Side = 22f;
    private const float TitleH = 72f;
    private const float HintH = 30f;

    private const string ReqMetTag = "<color=#58C275>";
    private const string ReqUnmetTag = "<color=#E06C6C>";

    // ===================================================
    // 상태
    // ===================================================
    private bool _built, _isOpen, _opening, _needsRefresh, _subscribed, _langSubscribed;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    // 데이터 참조
    private EquipmentInventory _equipInv;
    private WarehouseManager _warehouse;
    private Relic.RelicManager _relicMgr;
    private PlayerStat _playerStat;

    // 프레임 위젯
    private TextMeshProUGUI _goldText, _hintText;
    private ScrollRect _listScroll;
    private RectTransform _listGrid;

    // 필터
    private enum Filter { All, Equipment, Relic }
    private Filter _filter = Filter.All;

    /// <summary>화면에 보이는 좌→우 탭 순서. BuildListCard()의 AddFilter 호출 순서와 반드시 일치시킬 것.</summary>
    private static readonly Filter[] FilterOrder = { Filter.All, Filter.Equipment, Filter.Relic };

    private readonly List<(Button btn, Filter f, TextMeshProUGUI label)> _filterButtons = new List<(Button, Filter, TextMeshProUGUI)>();

    // WASD 커서 — 목록 칸·필터 탭·강화 버튼을 하나의 판으로 보고 좌표로 이동한다(창고·인벤토리와 같은 방식).
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();
    private readonly List<CodeNavButton> _navButtons = new List<CodeNavButton>();
    // 슬롯 오른쪽 끝 ↔ 강화 버튼의 구역 점프를 확정적으로 처리하기 위한 참조.
    private CodeNavButton _upgradeNav;
    private CodeSlotView _lastSlot;   // 강화 버튼에서 A로 돌아올 슬롯 자리

    // 목록 슬롯 풀 + 배지
    private readonly List<CodeSlotView> _slots = new List<CodeSlotView>();
    private readonly List<TextMeshProUGUI> _badges = new List<TextMeshProUGUI>();

    // 선택
    private enum SelKind { None, Equipment, Relic }
    private SelKind _selKind = SelKind.None;
    private EquipmentSO _selEq;
    private Relic.Data.RelicID _selRelic = Relic.Data.RelicID.None;
    private Relic.Data.RelicSO _selRelicSo;

    // 목록 엔트리 (슬롯 index → 엔트리)
    private class Entry
    {
        public bool isRelic;
        public EquipmentSO eq;
        public int count;
        public Relic.Data.RelicID relicId;
        public Relic.Data.RelicSO relicSo;
    }
    private readonly List<Entry> _entries = new List<Entry>();

    // 상세 위젯
    private RectTransform _detailIconBox;
    private Image _detailIcon, _detailFlash;
    private TextMeshProUGUI _detailBadge;      // 큰 아이콘 코너의 +N
    private TextMeshProUGUI _detailName, _detailLevel, _detailTestWarn, _detailDesc, _detailStatHeader;
    private RectTransform _statList;
    private readonly List<StatRow> _statRows = new List<StatRow>();
    private TextMeshProUGUI _costValue;
    private Image _materialIcon;
    private TextMeshProUGUI _materialName, _materialValue;
    private Button _upgradeButton;
    private TextMeshProUGUI _upgradeLabel;
    private GameObject _emptyHint;

    private class StatRow
    {
        public GameObject root;
        public TextMeshProUGUI label, value;
    }

    // 강화 연출
    private Coroutine _upgradeAnim;
    private bool _upgrading;

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

        StopUpgradeAnim();
        _nav.End();
        Unsubscribe();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Workbench) ui.SetState(UIState.None);
    }

    private IEnumerator OpenRoutine()
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        ResolveRefs();

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
        _filter = Filter.All;
        _selKind = SelKind.None;
        _selEq = null; _selRelic = Relic.Data.RelicID.None; _selRelicSo = null;
        _loc.Refresh();
        RefreshAll();

        _canvasObj.SetActive(true);
        if (_listScroll != null) _listScroll.verticalNormalizedPosition = 1f;

        _nav.Begin();
        _nav.ClearFocus(); // 커서 없이 시작 — 첫 WASD 입력이 커서를 켠다

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
        if (_upgrading) return; // 연출 중엔 닫기·입력 무시

        if (closeOnEscape && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab)))
        {
            // ESC는 뒤로가기음, Tab은 토글이라 평소 클릭음 그대로.
            if (Input.GetKeyDown(KeyCode.Escape)) CodeUI.PlayBack();
            Close();
            return;
        }

        HandleTabHotkeys();

        // 슬롯에 커서가 있으면 그 자리를 기억(강화 버튼에서 A로 돌아올 자리)
        if (_nav.Focused is CodeSlotView cur) _lastSlot = cur;

        // 슬롯 오른쪽 끝 ↔ 강화 버튼 구역 점프는 좌표 탐색에 맡기지 않고 확정적으로 처리한다.
        // (목록 마지막 줄이 덜 찼을 때 D가 강화 버튼 대신 위쪽 칸으로 새는 걸 막는다)
        if (HandleZoneJump()) return;

        _nav.Update();
    }

    /// <summary>
    /// 슬롯 오른쪽 끝에서 D → 강화 버튼, 강화 버튼에서 A → 슬롯 자리로 복귀.
    /// 처리했으면 true(그 프레임의 일반 WASD 이동은 건너뛴다).
    /// </summary>
    private bool HandleZoneJump()
    {
        if (_upgradeNav == null) return false;
        var focused = _nav.Focused;

        // 슬롯에서 D — 그 슬롯 오른쪽에 다른 슬롯이 없을 때(=오른쪽 끝)만 강화 버튼으로 넘어간다.
        if (Input.GetKeyDown(KeyCode.D) && focused is CodeSlotView slot)
        {
            if (!HasSlotToRight(slot) && _upgradeNav.NavUsable)
            {
                _nav.FocusItem(_upgradeNav);
                return true;
            }
        }

        // 강화 버튼에서 A — 기억해 둔 슬롯(없으면 첫 슬롯)으로 돌아온다.
        if (Input.GetKeyDown(KeyCode.A) && ReferenceEquals(focused, _upgradeNav))
        {
            var target = (_lastSlot != null && _lastSlot.gameObject.activeInHierarchy) ? _lastSlot : FirstActiveSlot();
            if (target != null)
            {
                _nav.FocusItem(target);
                return true;
            }
        }

        return false;
    }

    /// <summary>같은 줄(비슷한 y)에서 이 슬롯보다 오른쪽에 있는 활성 슬롯이 있는지.</summary>
    private bool HasSlotToRight(CodeSlotView slot)
    {
        if (slot == null) return false;
        Vector2 c = SlotCenter(slot);
        float rowTol = slotSize * 0.5f;   // y가 반 칸 이내면 같은 줄로 본다
        foreach (var v in _slots)
        {
            if (v == null || v == slot || !v.gameObject.activeInHierarchy) continue;
            Vector2 vc = SlotCenter(v);
            if (vc.x > c.x + 1f && Mathf.Abs(vc.y - c.y) < rowTol) return true;
        }
        return false;
    }

    private CodeSlotView FirstActiveSlot()
    {
        foreach (var v in _slots)
            if (v != null && v.gameObject.activeInHierarchy) return v;
        return null;
    }

    private static Vector2 SlotCenter(CodeSlotView v)
    {
        var rt = v.NavRect;
        return rt.TransformPoint(rt.rect.center);
    }

    /// <summary>Q/E로 전체 → 장비 → 유물 탭 전환. 화면 순서는 AddFilter 호출 순서(=FilterOrder)와 같다.</summary>
    private void HandleTabHotkeys()
    {
        if (prevTabKey != KeyCode.None && Input.GetKeyDown(prevTabKey)) StepFilter(-1);
        else if (nextTabKey != KeyCode.None && Input.GetKeyDown(nextTabKey)) StepFilter(+1);
    }

    private void StepFilter(int delta)
    {
        int cur = System.Array.IndexOf(FilterOrder, _filter);
        if (cur < 0) cur = 0;

        int next = cur + delta;
        if (wrapTabCycle)
        {
            next = (next % FilterOrder.Length + FilterOrder.Length) % FilterOrder.Length;
        }
        else
        {
            next = Mathf.Clamp(next, 0, FilterOrder.Length - 1);
            if (next == cur) return;
        }

        SwitchFilter(FilterOrder[next]);
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
        _nav.End(); // 정적 호버 이벤트 구독 해제
        Unsubscribe();
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 참조 / 이벤트
    // ===================================================
    private void ResolveRefs()
    {
        if (_equipInv == null) _equipInv = FindFirstObjectByType<EquipmentInventory>(FindObjectsInactive.Include);
        if (_playerStat == null) _playerStat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        _warehouse = WarehouseManager.Instance;
        if (_relicMgr == null) _relicMgr = Relic.RelicManager.EnsureInScene();
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;

        if (_equipInv != null) _equipInv.OnInventoryChanged += MarkDirty;
        if (_warehouse != null) _warehouse.OnWarehouseChanged += MarkDirty;
        if (_relicMgr != null && _relicMgr.Inventory != null) _relicMgr.Inventory.OnChanged += MarkDirty;
        if (_playerStat != null) _playerStat.OnGoldChanged += OnGoldChanged;
        EquipmentUpgradeStore.OnChanged += MarkDirty;

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

        if (_equipInv != null) _equipInv.OnInventoryChanged -= MarkDirty;
        if (_warehouse != null) _warehouse.OnWarehouseChanged -= MarkDirty;
        if (_relicMgr != null && _relicMgr.Inventory != null) _relicMgr.Inventory.OnChanged -= MarkDirty;
        if (_playerStat != null) _playerStat.OnGoldChanged -= OnGoldChanged;
        EquipmentUpgradeStore.OnChanged -= MarkDirty;
    }

    private void MarkDirty() => _needsRefresh = true;
    private void OnGoldChanged(int _) => _needsRefresh = true;
    private void OnLanguageChanged(LanguageType _) { _loc.Refresh(); _needsRefresh = true; }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("EquipmentUpgradeCanvas");
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

        BuildTitleBar(panel.transform);

        var body = CodeUI.CreateRect(panel.transform, "Body");
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(Side, Side + HintH + 6f);
        body.offsetMax = new Vector2(-Side, -(Side + TitleH + 12f));
        var bodyLayout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 16f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = true;

        BuildListCard(body, 1.1f);
        BuildDetailCard(body, 1f);

        _hintText = CodeUI.CreateText(panel.transform, "Hint", 17f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        var hintRt = _hintText.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.offsetMin = new Vector2(Side, Side);
        hintRt.offsetMax = new Vector2(-Side, Side + HintH);
        _loc.Bind(_hintText, "ui_equp_hint", "WASD : 이동   ·   Space : 선택 / 강화   ·   Q / E : 필터 전환   ·   ESC / Tab : 닫기");

        // WASD 커서 — 목록 칸·필터 탭·강화 버튼이 한 판. 스페이스는 항목 자신의 동작
        // (칸 = 좌클릭 선택, 버튼 = 클릭)이라 onActivate를 따로 가로챌 필요가 없다.
        _nav.collect = CollectNavItems;
        _nav.moveSfxName = null; // 칸을 옮길 때마다 소리가 나면 시끄럽다 — 실행(스페이스)에만 소리를 낸다

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

        var diamond = CodeUI.CreateText(bar, "Diamond", 22f, FontStyles.Normal, CodeUI.EquipColor, TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;

        var title = CodeUI.CreateText(bar, "Title", 32f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        title.characterSpacing = 8f;
        _loc.Bind(title, "ui_equp_title", "장 비 강 화");

        CodeUI.CreateSpacer(bar);

        var goldBox = CodeUI.CreateImage(bar, "GoldBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var goldLe = goldBox.gameObject.AddComponent<LayoutElement>();
        goldLe.preferredWidth = 220f;
        goldLe.preferredHeight = 46f;
        _goldText = CodeUI.CreateText(goldBox.transform, "Gold", 24f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(_goldText.rectTransform);
    }

    /// <summary>
    /// 카드 폭을 오직 flexibleWidth 비율로만 정하도록 고정한다.
    /// min/preferredWidth를 0으로 못 박지 않으면 카드 내부 콘텐츠(상세 스탯 텍스트 길이 등)의
    /// preferredWidth가 부모 HorizontalLayoutGroup에 올라가, 장비/유물 선택에 따라 좌우 분할 비율이 흔들린다.
    /// </summary>
    private static void PinCardWidth(GameObject card, float flexibleWidth)
    {
        var le = card.AddComponent<LayoutElement>();
        le.flexibleWidth = flexibleWidth;
        le.minWidth = 0f;
        le.preferredWidth = 0f;
    }

    // ── 왼쪽: 보유 장비·유물 목록 ──
    private void BuildListCard(Transform parent, float flexibleWidth)
    {
        var card = CodeUI.CreateImage(parent, "ListCard", CodeUI.CardBg, skin.cardSprite, skin);
        PinCardWidth(card.gameObject, flexibleWidth);
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CodeUI.CreateRow(card.transform, "Header", 34f);
        var label = CodeUI.CreateText(header, "Label", 23f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(label, "ui_equp_list", "보유 장비·유물");
        CodeUI.CreateSpacer(header);

        CodeUI.CreateDivider(card.transform);

        // 필터 탭
        var tabs = CodeUI.CreateRow(card.transform, "Filters", 40f, 8f);
        AddFilter(tabs, Filter.All, "ui_equp_f_all", "전체", Color.white);
        AddFilter(tabs, Filter.Equipment, "ui_equp_f_equip", "장비", CodeUI.EquipColor);
        AddFilter(tabs, Filter.Relic, "ui_equp_f_relic", "유물", CodeUI.RelicColor);

        _listScroll = CodeUI.CreateScrollView(card.transform, "Scroll", out _listGrid);
        var scrollLe = _listScroll.gameObject.AddComponent<LayoutElement>();
        scrollLe.flexibleHeight = 1f;
        scrollLe.minHeight = 300f;

        var grid = _listGrid.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(slotSize, slotSize);
        grid.spacing = new Vector2(10f, 10f);
        grid.padding = new RectOffset(2, 2, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(2, listColumns);
    }

    /// <summary>
    /// WASD 이동 후보 — 보유 목록 칸 + 강화 버튼. (필터 탭은 Q/E 전담이라 제외)
    /// 강화 버튼은 선택이 없거나 재화가 모자라면 interactable=false라 내비게이터가 알아서 거른다.
    /// </summary>
    private void CollectNavItems(List<ICodeNavItem> into)
    {
        foreach (var view in _slots)
            if (view != null && view.gameObject.activeInHierarchy) into.Add(view);

        foreach (var nav in _navButtons)
            if (nav != null && nav.NavUsable) into.Add(nav);
    }

    private void AddFilter(Transform parent, Filter f, string key, string fallback, Color accent)
    {
        var btn = CodeUI.CreateButton(parent, "Filter_" + f, CodeUI.TabIdleBg, () => SwitchFilter(f), skin.tabSprite, skin);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredHeight = 38f;
        var label = CodeUI.CreateText(btn.transform, "Text", 19f, FontStyles.Bold, accent, TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(label.rectTransform);
        _loc.Bind(label, key, fallback);
        _filterButtons.Add((btn, f, label));
        // 필터 탭은 WASD 이동 대상에 넣지 않는다 — 전환은 Q/E 전담(다른 오버레이와 같은 규칙).
        // 커서가 탭 줄까지 올라가면 목록 위/아래 이동이 한 칸씩 걸린다.
    }

    // ── 오른쪽: 강화 상세 ──
    private void BuildDetailCard(Transform parent, float flexibleWidth)
    {
        var card = CodeUI.CreateImage(parent, "DetailCard", CodeUI.CardBg, skin.cardSprite, skin);
        PinCardWidth(card.gameObject, flexibleWidth);
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.spacing = 11f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CodeUI.CreateRow(card.transform, "Header", 22f);
        var hlabel = CodeUI.CreateText(header, "Label", 23f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(hlabel, "ui_equp_detail", "강화");
        CodeUI.CreateSpacer(header);

        CodeUI.CreateDivider(card.transform);

        // 아이콘 + 이름/레벨
        var head = CodeUI.CreateRow(card.transform, "Head", 108f, 16f, TextAnchor.MiddleLeft);
        _detailIconBox = CodeUI.CreateImage(head, "IconBox", CodeUI.BoxBg, skin.boxSprite, skin).rectTransform;
        var iconLe = _detailIconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = 108f;
        iconLe.preferredHeight = 108f;

        _detailIcon = CodeUI.CreateImage(_detailIconBox.transform, "Icon", Color.white, rounded: false);
        var ir = _detailIcon.rectTransform;
        ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
        ir.offsetMin = new Vector2(10f, 10f); ir.offsetMax = new Vector2(-10f, -10f);
        _detailIcon.preserveAspect = true;
        _detailIcon.raycastTarget = false;

        _detailFlash = CodeUI.CreateImage(_detailIconBox.transform, "Flash", new Color(1f, 1f, 1f, 0f), rounded: false);
        CodeUI.StretchFull(_detailFlash.rectTransform);
        _detailFlash.raycastTarget = false;

        // +N 배지 (아이콘 우상단)
        _detailBadge = CodeUI.CreateText(_detailIconBox.transform, "Badge", 26f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.TopRight, _loc);
        var br = _detailBadge.rectTransform;
        br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one;
        br.offsetMin = new Vector2(0f, 0f); br.offsetMax = new Vector2(-6f, -4f);
        _detailBadge.raycastTarget = false;

        var headCol = CodeUI.CreateColumn(head, "HeadText", 4f);
        headCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _detailName = CodeUI.CreateText(headCol, "Name", 25f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        _detailName.textWrappingMode = TextWrappingModes.Normal;
        _detailName.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        _detailLevel = CodeUI.CreateText(headCol, "Level", 18f, FontStyles.Bold, CodeUI.AccentFill, TextAlignmentOptions.MidlineLeft, _loc);
        _detailLevel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        _detailTestWarn = CodeUI.CreateText(headCol, "TestWarn", 15f, FontStyles.Bold, CodeUI.WarnColor, TextAlignmentOptions.MidlineLeft, _loc);
        _detailTestWarn.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        _detailDesc = CodeUI.CreateText(card.transform, "Desc", 16f, FontStyles.Normal, CodeUI.LabelColor, TextAlignmentOptions.TopLeft, _loc);
        _detailDesc.textWrappingMode = TextWrappingModes.Normal;
        _detailDesc.gameObject.AddComponent<LayoutElement>().minHeight = 40f;

        CodeUI.CreateDivider(card.transform);

        _detailStatHeader = CodeUI.CreateText(card.transform, "StatHeader", 18f, FontStyles.Bold, CodeUI.LabelColor, TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(_detailStatHeader, "ui_equp_stat_header", "강화 시 변화");
        _detailStatHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        _statList = CodeUI.CreateColumn(card.transform, "StatList", 6f);
        // 장비(여러 행)·유물(한 행) 선택이 바뀌어도 아래 비용/재료/버튼이 위아래로 움직이지 않도록
        // '변화' 영역에 최소 높이를 확보한다(레이아웃 통일).
        _statList.gameObject.AddComponent<LayoutElement>().minHeight = 152f;

        // 비용 / 재료
        _costValue = MakeInfoBox(card.transform, "CostBox", "ui_equp_cost", "강화 비용", out _);
        BuildMaterialBox(card.transform);

        // 강화 버튼
        _upgradeButton = CodeUI.CreateTextButton(card.transform, "UpgradeBtn", CodeUI.GoldColor, CodeUI.GoldFg, 21f,
            OnUpgradeClicked, out _upgradeLabel, skin.buttonSprite, skin, _loc);
        _upgradeButton.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;
        _loc.Bind(_upgradeLabel, "ui_equp_do", "강화");
        _upgradeNav = CodeNavButton.Attach(_upgradeButton, skin);
        _navButtons.Add(_upgradeNav);

        // 선택 없음 안내
        _emptyHint = CodeUI.CreateText(card.transform, "Empty", 18f, FontStyles.Normal, CodeUI.MutedColor, TextAlignmentOptions.Center, _loc).gameObject;
        _loc.Bind(_emptyHint.GetComponent<TextMeshProUGUI>(), "ui_equp_empty", "왼쪽에서 강화할 장비나 유물을 선택하세요.");
        _emptyHint.AddComponent<LayoutElement>().preferredHeight = 40f;
    }

    private TextMeshProUGUI MakeInfoBox(Transform parent, string name, string key, string fallback, out TextMeshProUGUI label)
    {
        var box = CodeUI.CreateImage(parent, name, CodeUI.BoxBg, skin.boxSprite, skin);
        box.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
        var row = box.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 14, 0, 0);
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;
        row.childAlignment = TextAnchor.MiddleLeft;

        label = CodeUI.CreateText(box.transform, "Label", 17f, FontStyles.Normal, CodeUI.MutedColor, TextAlignmentOptions.MidlineLeft, _loc);
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;
        if (key != null) _loc.Bind(label, key, fallback);

        var value = CodeUI.CreateText(box.transform, "Value", 19f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineRight, _loc);
        value.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        return value;
    }

    private void BuildMaterialBox(Transform parent)
    {
        var box = CodeUI.CreateImage(parent, "MaterialBox", CodeUI.BoxBg, skin.boxSprite, skin);
        box.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
        var row = box.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 14, 6, 6);
        row.spacing = 10f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;
        row.childAlignment = TextAnchor.MiddleLeft;

        var label = CodeUI.CreateText(box.transform, "Label", 17f, FontStyles.Normal, CodeUI.MutedColor, TextAlignmentOptions.MidlineLeft, _loc);
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;
        _loc.Bind(label, "ui_equp_material", "필요 재료");

        _materialIcon = CodeUI.CreateImage(box.transform, "MatIcon", Color.white, rounded: false);
        var mi = _materialIcon.gameObject.AddComponent<LayoutElement>();
        mi.preferredWidth = mi.preferredHeight = 36f;
        _materialIcon.preserveAspect = true;
        _materialIcon.raycastTarget = false;

        _materialName = CodeUI.CreateText(box.transform, "MatName", 17f, FontStyles.Bold, CodeUI.LabelColor, TextAlignmentOptions.MidlineLeft, _loc);
        _materialName.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        _materialValue = CodeUI.CreateText(box.transform, "MatValue", 19f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineRight, _loc);
        _materialValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;
    }

    // ===================================================
    // 목록 갱신
    // ===================================================
    private void RefreshAll()
    {
        RefreshGold();
        RebuildEntries();
        RefreshList();
        RefreshDetail();
        UpdateFilterVisuals();
    }

    private void RefreshGold()
    {
        if (_goldText != null)
            _goldText.text = $"{CodeUI.Gold(_playerStat != null ? _playerStat.Gold : 0)} G";
    }

    private void RebuildEntries()
    {
        _entries.Clear();
        var seenEquip = new HashSet<EquipmentID>();

        if (_filter != Filter.Relic)
        {
            // 장착 중인 장비
            if (_equipInv != null)
            {
                foreach (var slot in _equipInv.ReadonlyItems)
                {
                    if (slot?.item is EquipmentSO eq && seenEquip.Add(eq.equipmentID))
                        _entries.Add(new Entry { eq = eq, count = 1 });
                }
            }
            // 창고 보관 장비
            if (_warehouse != null)
            {
                foreach (var slot in _warehouse.AllSlots)
                {
                    if (slot?.item is EquipmentSO eq && seenEquip.Add(eq.equipmentID))
                        _entries.Add(new Entry { eq = eq, count = Mathf.Max(1, slot.quantity) });
                }
            }
        }

        if (_filter != Filter.Equipment)
        {
            var inv = _relicMgr != null ? _relicMgr.Inventory : null;
            var db = Relic.Data.RelicDatabase.Instance;
            if (inv != null && db != null)
            {
                foreach (var kv in inv.Owned)
                {
                    var so = db.GetRelicByID(kv.Key);
                    if (so != null)
                        _entries.Add(new Entry { isRelic = true, relicId = kv.Key, relicSo = so });
                }
            }
        }
    }

    private void RefreshList()
    {
        EnsureSlots(_entries.Count);

        for (int i = 0; i < _slots.Count; i++)
        {
            var view = _slots[i];
            var badge = _badges[i];
            bool used = i < _entries.Count;
            view.gameObject.SetActive(used);
            if (!used) continue;

            var e = _entries[i];
            view.index = i;

            if (e.isRelic)
            {
                view.BindCustom(e.relicSo.icon, e.relicSo != null ? Loc(e.relicSo.displayNameKey) : "",
                    e.relicSo != null ? Loc(e.relicSo.descriptionKey) : "", CodeUI.RelicColor, true);
                int lv = GetRelicLevel(e.relicId) - 1; // 유물은 내부적으로 1부터 → 표시는 0강부터(장비와 통일)
                SetBadge(badge, lv);
                bool sel = _selKind == SelKind.Relic && _selRelic == e.relicId;
                view.SetIdleColor(sel ? CodeUI.SlotHover : CodeUI.SlotBg);
            }
            else
            {
                view.Bind(new InventorySlot(e.eq, e.count), CodeUI.EquipColor);
                int lv = EquipmentUpgradeStore.GetLevel(e.eq);
                SetBadge(badge, lv);
                bool sel = _selKind == SelKind.Equipment && _selEq == e.eq;
                view.SetIdleColor(sel ? CodeUI.SlotHover : CodeUI.SlotBg);
            }
        }

        _nav.Refresh(); // 목록이 줄어 커서가 있던 칸이 꺼졌으면 가까운 칸으로 옮긴다
    }

    private void SetBadge(TextMeshProUGUI badge, int level)
    {
        if (badge == null) return;
        if (level > 0)
        {
            badge.text = $"+{level}";
            badge.gameObject.SetActive(true);
        }
        else badge.gameObject.SetActive(false);
    }

    private void EnsureSlots(int needed)
    {
        while (_slots.Count < needed)
        {
            var view = CodeSlotView.Create(_listGrid, skin, slotSize, $"Slot_{_slots.Count}");
            view.zone = 0;
            view.onLeftClick = v => OnSlotClicked(v.index);
            view.onRightClick = v => OnSlotClicked(v.index);
            view.canDrag = _ => false; // 이 화면에선 드래그 이동 없음(스크롤만)

            // +N 배지 (좌상단)
            var badge = CodeUI.CreateText(view.transform, "Badge", 20f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.TopLeft, _loc);
            var rt = badge.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(5f, 0f); rt.offsetMax = new Vector2(0f, -3f);
            badge.raycastTarget = false;
            badge.gameObject.SetActive(false);

            _slots.Add(view);
            _badges.Add(badge);
        }
    }

    private void OnSlotClicked(int index)
    {
        if (_upgrading) return;
        if (index < 0 || index >= _entries.Count) return;
        CodeUI.PlaySfx(clickSfxName);

        var e = _entries[index];
        if (e.isRelic)
        {
            _selKind = SelKind.Relic;
            _selRelic = e.relicId;
            _selRelicSo = e.relicSo;
            _selEq = null;
        }
        else
        {
            _selKind = SelKind.Equipment;
            _selEq = e.eq;
            _selRelic = Relic.Data.RelicID.None;
            _selRelicSo = null;
        }
        RefreshList();   // 선택 하이라이트
        RefreshDetail();
    }

    private void SwitchFilter(Filter f)
    {
        if (_filter == f) return;
        CodeUI.PlaySfx(clickSfxName);
        _filter = f;
        _needsRefresh = true;
    }

    private void UpdateFilterVisuals()
    {
        foreach (var (btn, f, label) in _filterButtons)
        {
            bool selected = (f == _filter);
            Sprite sprite = selected
                ? (skin.tabSelectedSprite != null ? skin.tabSelectedSprite : skin.tabSprite)
                : skin.tabSprite;
            CodeUI.ApplySkin(btn.image, selected ? CodeUI.AccentFill : CodeUI.TabIdleBg, sprite, skin);
            label.color = selected ? Color.white : new Color(label.color.r, label.color.g, label.color.b, 0.75f);
        }
    }

    // ===================================================
    // 상세 갱신
    // ===================================================
    private void RefreshDetail()
    {
        bool hasSel = _selKind != SelKind.None &&
                      ((_selKind == SelKind.Equipment && _selEq != null) ||
                       (_selKind == SelKind.Relic && _selRelicSo != null));

        // 선택된 항목이 목록에서 사라졌는지 확인
        if (hasSel && !SelectionStillPresent()) { ClearSelection(); hasSel = false; }

        _emptyHint.SetActive(!hasSel);
        SetDetailBodyActive(hasSel);
        if (!hasSel) return;

        if (_selKind == SelKind.Equipment) RefreshEquipmentDetail();
        else RefreshRelicDetail();
    }

    private void SetDetailBodyActive(bool on)
    {
        _detailIconBox.gameObject.SetActive(on);
        _detailName.transform.parent.gameObject.SetActive(on); // HeadText column
        _detailDesc.gameObject.SetActive(on);
        _detailStatHeader.gameObject.SetActive(on);
        _statList.gameObject.SetActive(on);
        _costValue.transform.parent.gameObject.SetActive(on);
        _materialName.transform.parent.gameObject.SetActive(on);
        _upgradeButton.gameObject.SetActive(on);
    }

    private void RefreshEquipmentDetail()
    {
        var eq = _selEq;
        int level = EquipmentUpgradeStore.GetLevel(eq);
        int maxLevel = EquipmentUpgradeStore.MaxLevel(eq);
        bool maxed = level >= maxLevel;

        _detailIcon.sprite = eq.icon;
        _detailIcon.enabled = eq.icon != null;
        _detailIcon.color = Color.white;
        SetBadge(_detailBadge, level);

        _detailName.text = level > 0 ? $"{eq.DisplayName}  +{level}" : eq.DisplayName;
        _detailLevel.text = $"Lv {level} / {maxLevel}";
        _detailDesc.text = eq.Description ?? string.Empty;

        bool test = EquipmentUpgradeStore.IsUsingTestData(eq);
        _detailTestWarn.gameObject.SetActive(test);
        if (test) _detailTestWarn.text = CodeUI.L("ui_equp_testwarn", "⚠ 임시 테스트 값 (밸런스 미확정)");

        var step = EquipmentUpgradeStore.GetNextStep(eq);

        // 스탯 변화 미리보기
        BuildEquipmentStatPreview(eq, level, step, maxed);

        // 비용/재료/버튼
        if (maxed || step == null)
        {
            _costValue.text = "—";
            _costValue.color = CodeUI.MutedColor;
            SetMaterialEmpty();
            SetUpgradeButton(false, CodeUI.L("ui_equp_maxed", "최대 강화"));
        }
        else
        {
            int have = _playerStat != null ? _playerStat.Gold : 0;
            bool goldOk = have >= step.goldCost;
            _costValue.text = $"{CodeUI.Gold(step.goldCost)} G";
            _costValue.color = goldOk ? Color.white : CodeUI.NegativeColor;

            var mat = EquipmentUpgradeStore.ResolveMaterial(step);
            bool matOk = SetMaterial(mat, step.materialCount);

            bool can = goldOk && matOk;
            SetUpgradeButton(can, CodeUI.L("ui_equp_do", "강화"));
        }
    }

    private void BuildEquipmentStatPreview(EquipmentSO eq, int level, EquipmentUpgradeLevel step, bool maxed)
    {
        // 표시할 (스탯,타입) 목록 = 다음 단계가 바꾸는 것들
        var changed = new List<(StatType st, ModifierType mt)>();
        if (step != null && step.bonusModifiers != null)
        {
            foreach (var m in step.bonusModifiers)
            {
                if (m == null) continue;
                var k = (m.statType, m.modifierType);
                if (!changed.Contains(k)) changed.Add(k);
            }
        }
        bool defenseChanges = step != null && Mathf.Abs(step.bonusDefense) > 0.001f;

        int rowCount = changed.Count + (defenseChanges ? 1 : 0);
        EnsureStatRows(rowCount);

        var curAgg = Aggregate(eq, level);
        var nextAgg = Aggregate(eq, level + 1);

        int idx = 0;
        if (defenseChanges)
        {
            float curDef = eq.defense + EquipmentUpgradeStore.EffectiveBonusDefense(eq, level);
            float nextDef = eq.defense + EquipmentUpgradeStore.EffectiveBonusDefense(eq, level + 1);
            SetStatRow(idx++, CodeUI.L("stat_defense", "방어력"), $"{FmtMod(ModifierType.Flat, curDef)} → {FmtMod(ModifierType.Flat, nextDef)}");
        }
        foreach (var (st, mt) in changed)
        {
            float cur = curAgg.TryGetValue((st, mt), out var c) ? c : (mt == ModifierType.Percent ? 1f : 0f);
            float next = nextAgg.TryGetValue((st, mt), out var n) ? n : (mt == ModifierType.Percent ? 1f : 0f);
            SetStatRow(idx++, StatDisplayName(st), $"{FmtMod(mt, cur)} → {FmtMod(mt, next)}");
        }

        // 만렙(또는 변화 없음)이면 안내 한 줄
        if (idx == 0)
        {
            EnsureStatRows(1);
            SetStatRow(0, string.Empty, maxed
                ? CodeUI.L("ui_equp_maxed", "최대 강화")
                : CodeUI.L("ui_equp_no_change", "변화 없음"));
            idx = 1;
        }

        // 남은 풀 행 비활성화(선택 전환 시 이전 스탯이 남지 않게)
        for (int i = idx; i < _statRows.Count; i++) _statRows[i].root.SetActive(false);
    }

    private Dictionary<(StatType, ModifierType), float> Aggregate(EquipmentSO eq, int level)
    {
        var d = new Dictionary<(StatType, ModifierType), float>();
        foreach (var m in EquipmentUpgradeStore.BuildEffectiveModifiers(eq, level))
        {
            if (m == null) continue;
            var key = (m.statType, m.modifierType);
            if (m.modifierType == ModifierType.Percent)
            {
                if (!d.ContainsKey(key)) d[key] = 1f;
                d[key] *= m.value;
            }
            else
            {
                if (!d.ContainsKey(key)) d[key] = 0f;
                d[key] += m.value;
            }
        }
        return d;
    }

    // 툴팁(CodeSlotView 등)에서도 같은 표기를 재사용하도록 public.
    public static string FmtMod(ModifierType mt, float v)
    {
        if (mt == ModifierType.Percent) return $"×{v:0.##}";
        return (v >= 0f ? "+" : "") + v.ToString("0.##");
    }

    private void RefreshRelicDetail()
    {
        var so = _selRelicSo;
        int actualLevel = GetRelicLevel(_selRelic);          // 유물은 보유 시 내부적으로 1부터
        int maxActual = Mathf.Max(1, so.maxLevel);
        int displayLevel = Mathf.Max(0, actualLevel - 1);    // 표시는 장비처럼 0강부터
        int maxDisplay = Mathf.Max(0, maxActual - 1);
        bool maxed = actualLevel >= maxActual;

        _detailIcon.sprite = so.icon;
        _detailIcon.enabled = so.icon != null;
        _detailIcon.color = Color.white;
        SetBadge(_detailBadge, displayLevel);

        _detailName.text = displayLevel > 0 ? $"{Loc(so.displayNameKey)}  +{displayLevel}" : Loc(so.displayNameKey);
        _detailLevel.text = $"Lv {displayLevel} / {maxDisplay}";
        _detailDesc.text = Loc(so.descriptionKey);
        _detailTestWarn.gameObject.SetActive(true);
        _detailTestWarn.text = CodeUI.L("ui_equp_testwarn", "⚠ 임시 테스트 값 (밸런스 미확정)");

        // 유물은 behaviour 기반이라 스탯별 delta를 조회할 수 없어, 장비 행과 같은 형식(라벨/값)으로
        // 레벨 진행만 보여준다 → 장비 선택과 레이아웃이 통일된다.
        EnsureStatRows(1);
        SetStatRow(0, CodeUI.L("ui_equp_relic_effect", "유물 효과"),
            maxed ? CodeUI.L("ui_equp_maxed", "최대 강화") : $"+{displayLevel} → +{displayLevel + 1}");
        for (int i = 1; i < _statRows.Count; i++) _statRows[i].root.SetActive(false);

        if (maxed)
        {
            _costValue.text = "—";
            _costValue.color = CodeUI.MutedColor;
            SetMaterialEmpty();
            SetUpgradeButton(false, CodeUI.L("ui_equp_maxed", "최대 강화"));
        }
        else
        {
            int goldCost = RelicUpgradeGold(so, actualLevel);
            int matCount = EquipmentUpgradeFormula.RelicMaterialCount(actualLevel);
            int have = _playerStat != null ? _playerStat.Gold : 0;
            bool goldOk = have >= goldCost;
            _costValue.text = $"{CodeUI.Gold(goldCost)} G";
            _costValue.color = goldOk ? Color.white : CodeUI.NegativeColor;

            var mat = EquipmentUpgradeStore.ResolveMaterial(null); // DB 첫 광물(임시 강화석)
            bool matOk = SetMaterial(mat, matCount);

            SetUpgradeButton(goldOk && matOk, CodeUI.L("ui_equp_do", "강화"));
        }
    }

    // ── 상세 위젯 헬퍼 ──
    private void EnsureStatRows(int needed)
    {
        while (_statRows.Count < needed)
        {
            var box = CodeUI.CreateImage(_statList, "StatRow", CodeUI.SlotBg, skin.slotSprite, skin);
            box.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            var row = box.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(12, 12, 0, 0);
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            row.childAlignment = TextAnchor.MiddleLeft;

            var label = CodeUI.CreateText(box.transform, "Label", 16f, FontStyles.Normal, CodeUI.LabelColor, TextAlignmentOptions.MidlineLeft, _loc);
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
            var value = CodeUI.CreateText(box.transform, "Value", 17f, FontStyles.Bold, CodeUI.PositiveColor, TextAlignmentOptions.MidlineRight, _loc);
            value.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _statRows.Add(new StatRow { root = box.gameObject, label = label, value = value });
        }
    }

    private void SetStatRow(int index, string label, string value)
    {
        if (index < 0 || index >= _statRows.Count) return;
        var r = _statRows[index];
        r.root.SetActive(true);
        r.label.text = label;
        r.value.text = value;
    }

    private bool SetMaterial(MineralSO mat, int count)
    {
        if (mat == null || count <= 0)
        {
            SetMaterialEmpty();
            return count <= 0; // 재료가 필요 없으면 통과
        }
        int have = _warehouse != null ? _warehouse.GetMineralCount(mat) : 0;
        bool ok = have >= count;

        _materialIcon.sprite = mat.Icon;
        _materialIcon.enabled = mat.Icon != null;
        _materialName.text = mat.DisplayName;
        _materialName.gameObject.SetActive(true);
        _materialValue.text = $"{(ok ? ReqMetTag : ReqUnmetTag)}{have}/{count}</color>";
        return ok;
    }

    private void SetMaterialEmpty()
    {
        _materialIcon.enabled = false;
        _materialName.text = CodeUI.L("ui_equp_no_material", "없음");
        _materialValue.text = string.Empty;
    }

    private void SetUpgradeButton(bool interactable, string label)
    {
        _upgradeButton.interactable = interactable && !_upgrading;
        _upgradeLabel.text = label;
    }

    // ===================================================
    // 강화 실행 (탕..탕.. 연출 후 적용)
    // ===================================================
    private void OnUpgradeClicked()
    {
        if (_upgrading) return;
        if (_selKind == SelKind.None) return;
        StopUpgradeAnim();
        _upgradeAnim = StartCoroutine(UpgradeRoutine());
    }

    private IEnumerator UpgradeRoutine()
    {
        _upgrading = true;
        _upgradeButton.interactable = false;

        var iconRt = _detailIcon.rectTransform;
        Vector3 baseScale = Vector3.one;

        for (int hit = 0; hit < Mathf.Max(1, hammerHits); hit++)
        {
            PlayHammerSfx();

            // 스케일 펀치 + 플래시 (레이아웃과 무관하게 안전)
            float t = 0f;
            const float dur = 0.12f;
            if (_detailFlash != null) _detailFlash.color = new Color(1f, 1f, 1f, 0.6f);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = t / dur;
                float punch = Mathf.Sin(k * Mathf.PI); // 0→1→0
                iconRt.localScale = baseScale * (1f + 0.18f * punch);
                iconRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 2f) * 5f);
                if (_detailFlash != null)
                    _detailFlash.color = new Color(1f, 1f, 1f, 0.6f * (1f - k));
                yield return null;
            }
            iconRt.localScale = baseScale;
            iconRt.localRotation = Quaternion.identity;
            if (_detailFlash != null) _detailFlash.color = new Color(1f, 1f, 1f, 0f);

            // 마지막 타격이 아니면 간격
            if (hit < hammerHits - 1)
            {
                float g = 0f;
                while (g < hammerInterval) { g += Time.unscaledDeltaTime; yield return null; }
            }
        }

        // 실제 강화 적용
        bool ok = ApplyUpgrade();
        CodeUI.PlaySfx(ok ? successSfxName : failSfxName);

        if (ok && _detailFlash != null)
        {
            // 성공 초록 플래시
            var c = CodeUI.PositiveColor;
            float t = 0f;
            while (t < 0.25f)
            {
                t += Time.unscaledDeltaTime;
                _detailFlash.color = new Color(c.r, c.g, c.b, 0.5f * (1f - t / 0.25f));
                yield return null;
            }
            _detailFlash.color = new Color(1f, 1f, 1f, 0f);
        }

        _upgrading = false;
        _upgradeAnim = null;
        RefreshAll();
    }

    /// <summary>
    /// 망치 타격 1회분 효과음. 타격마다 음정을 흔들거나 올리지 않는다 — 매번 같은 소리다.
    ///
    /// 씬/프리팹에 범용 기본값(SfxKeys.UiClick)이 구워져 있으면 미지정으로 보고
    /// 강화 전용음으로 폴백한다 — UpgradeOverlayUI.OnUnlockClicked와 같은 규칙.
    /// (그대로 두면 CodeUI.PlaySfx가 ui_button으로 폴백해 강화음이 UI 클릭음이 된다.)
    /// </summary>
    private void PlayHammerSfx()
    {
        string key = (string.IsNullOrEmpty(hammerSfxName) || hammerSfxName == SfxKeys.UiClick)
            ? SfxKeys.EquipEnhance
            : hammerSfxName;

        CodeUI.PlaySfx(key); // 미등록이면 공통 폴백(ui_button)에 맡긴다
    }

    /// <summary>재화 검증·차감 + 레벨 상승. 성공 시 true.</summary>
    private bool ApplyUpgrade()
    {
        if (_selKind == SelKind.Equipment && _selEq != null)
        {
            return EquipmentUpgradeStore.TryUpgrade(_selEq, _playerStat, _warehouse);
        }
        if (_selKind == SelKind.Relic && _selRelicSo != null && _relicMgr != null)
        {
            var inv = _relicMgr.Inventory;
            if (inv == null) return false;

            int level = inv.GetLevel(_selRelic);
            int maxLevel = Mathf.Max(1, _selRelicSo.maxLevel);
            if (level >= maxLevel) return false;

            int goldCost = RelicUpgradeGold(_selRelicSo, level);
            int matCount = EquipmentUpgradeFormula.RelicMaterialCount(level);
            var mat = EquipmentUpgradeStore.ResolveMaterial(null);

            if (_playerStat == null || _playerStat.Gold < goldCost) return false;
            if (matCount > 0 && (mat == null || _warehouse == null || _warehouse.GetMineralCount(mat) < matCount)) return false;

            if (!_playerStat.SpendGold(goldCost)) return false;
            DayEarningsLedger.Report(DayEarningsCategory.Upgrade, -goldCost);
            if (matCount > 0 && mat != null) _warehouse.RemoveMineral(mat, matCount);

            bool up = inv.TryUpgrade(_selRelic, maxLevel);
            if (up) _relicMgr.RefreshLevel(_selRelic);
            return up;
        }
        return false;
    }

    /// <summary>
    /// 유물 강화 골드비. RelicSO.upgradeCosts 를 우선 쓰고, 비어 있으면 기존 공식으로 폴백한다.
    /// upgradeCosts[0] = Lv1→2, [1] = Lv2→3. currentLevel 이 0일 수 있어 Clamp 한다.
    /// 설계: Assets/Docs/economy/price-data-json-design.md §4
    /// </summary>
    private static int RelicUpgradeGold(Relic.Data.RelicSO so, int currentLevel)
    {
        if (so != null && so.upgradeCosts != null && so.upgradeCosts.Length > 0)
        {
            int idx = Mathf.Clamp(currentLevel - 1, 0, so.upgradeCosts.Length - 1);
            return so.upgradeCosts[idx].gold;
        }
        return EquipmentUpgradeFormula.RelicGoldCost(currentLevel);
    }

    private void StopUpgradeAnim()
    {
        if (_upgradeAnim != null) { StopCoroutine(_upgradeAnim); _upgradeAnim = null; }
        _upgrading = false;
        if (_detailIcon != null)
        {
            _detailIcon.rectTransform.localScale = Vector3.one;
            _detailIcon.rectTransform.localRotation = Quaternion.identity;
        }
        if (_detailFlash != null) _detailFlash.color = new Color(1f, 1f, 1f, 0f);
    }

    // ===================================================
    // 유틸
    // ===================================================
    private int GetRelicLevel(Relic.Data.RelicID id)
    {
        var inv = _relicMgr != null ? _relicMgr.Inventory : null;
        return inv != null ? inv.GetLevel(id) : 0;
    }

    private bool SelectionStillPresent()
    {
        foreach (var e in _entries)
        {
            if (_selKind == SelKind.Equipment && !e.isRelic && e.eq == _selEq) return true;
            if (_selKind == SelKind.Relic && e.isRelic && e.relicId == _selRelic) return true;
        }
        return false;
    }

    private void ClearSelection()
    {
        _selKind = SelKind.None;
        _selEq = null;
        _selRelic = Relic.Data.RelicID.None;
        _selRelicSo = null;
    }

    private static string Loc(string key) => CodeUI.L(key, key);

    /// <summary>StatType의 한국어 표시명(로컬라이제이션 키 우선, 없으면 코드 폴백).</summary>
    public static string StatDisplayName(StatType st)
    {
        string fallback = st switch
        {
            StatType.MiningLevel => "채광 레벨",
            StatType.MiningSpeed => "채굴 속도",
            StatType.MiningRange => "채굴 범위",
            StatType.MiningCooldown => "채굴 쿨다운",
            StatType.ToolRange => "도구 사거리",
            StatType.ToolChargeTimeReduce => "차징 시간 감소",
            StatType.PickaxeDamageUp => "곡괭이 피해",
            StatType.RockDamageFlat => "암석 피해",
            StatType.RockDamageMultiplier => "암석 피해 배율",
            StatType.RareMineralChance => "희귀 광물 확률",
            StatType.MineralExtraDropChance => "추가 드롭 확률",
            StatType.MoveSpeed => "이동 속도",
            StatType.JumpForce => "점프력",
            StatType.WallClimbSpeed => "벽타기 속도",
            StatType.FallDamageReduce => "낙하 피해 감소",
            StatType.EncumberedSpeedMultiplier => "과적 속도",
            StatType.MaxStamina => "최대 스태미나",
            StatType.StaminaCostPerSecond => "스태미나 소모",
            StatType.ShovelStaminaReduce => "삽 스태미나 절약",
            StatType.StaminaCostReduce => "행동 스태미나 절약",
            StatType.DrillBatteryCapacity => "드릴 배터리",
            StatType.DrillBatteryRegen => "드릴 배터리 회복",
            StatType.DrillDrainReduce => "드릴 소모 절약",
            StatType.DrillRadius => "드릴 파기 범위",
            StatType.CritChanceUp => "치명타 확률",
            StatType.MaxHp => "최대 체력",
            StatType.Damage => "공격력",
            StatType.MineralSellBonus => "판매 보너스",
            StatType.InventorySlotUp => "인벤 슬롯",
            StatType.InventoryWeightUp => "무게 한도",
            StatType.WarehouseCapacityUp => "창고 슬롯",
            StatType.ConsumableSlotUnlock => "소모품 슬롯",
            StatType.VisionRadiusUp => "시야 범위",
            StatType.FlashlightRangeUp => "손전등 사거리",
            StatType.EnvironmentResistance => "환경 저항",
            StatType.StaminaRegen => "스태미나 재생",
            StatType.HazardFrostResist => "빙결 저항",
            StatType.HazardBurnResist => "화상 저항",
            StatType.HazardRadiationResist => "방사선 저항",
            StatType.Defense => "방어력",
            _ => st.ToString()
        };
        return CodeUI.L("stat_" + st, fallback);
    }
}
