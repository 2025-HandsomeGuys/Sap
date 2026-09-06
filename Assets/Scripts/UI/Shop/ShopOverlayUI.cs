// @tags: shop, ui, overlay, code-generated, buy, sell, mineral, gold, warehouse, ground

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 오버레이 — 왼쪽에 구매 목록, 오른쪽에 창고 광물 판매 목록을 놓은 화면.
/// UI는 전부 코드로 생성한다(SettingsOverlayUI 패턴) — 씬/프리팹 세팅 없이 <see cref="Open"/> 호출만으로 동작.
/// 배경/버튼 스프라이트는 인스펙터의 <see cref="skin"/>에 넣으면 적용된다(비운 항목은 코드 생성 라운드로 폴백).
///
/// 거래는 전부 기존 매니저에 위임한다:
///  - 구매: ShopManager.BuyItem (골드 차감·창고 적재·재고 감소·DayEarningsLedger 기록까지 담당)
///  - 판매: ShopManager.SellItem (창고 광물만, 슬롯 단위)
///  - 가격: ShopItemDatabase / MineralPriceDatabase
/// </summary>
public class ShopOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static ShopOverlayUI _instance;

    public static ShopOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<ShopOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("ShopOverlayUI");
                    _instance = go.AddComponent<ShopOverlayUI>();
                }
            }
            return _instance;
        }
    }

    /// <summary>상점 오버레이가 열려 있는지 (UIStateManager의 전역 단축키 차단용).</summary>
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
    /// 씬이 바뀌면 무조건 닫는다(안전망).
    /// DontDestroyOnLoad라 열린 채 씬이 바뀌면 새 씬 위에 남아 클릭을 전부 먹고 timeScale이 0으로 굳는다.
    /// </summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!_isOpen && !_opening) return;

        _opening = false;
        _isOpen = false;
        Time.timeScale = 1f;
        HidePopup();
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
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.5f;
    [SerializeField] private bool useBlurBackdrop = true;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("동작")]
    [Tooltip("열려 있는 동안 게임을 멈춘다(Time.timeScale = 0)")]
    [SerializeField] private bool pauseGameWhileOpen = true;
    [Tooltip("ESC로 닫기")]
    [SerializeField] private bool closeOnEscape = true;
    [Tooltip("구매 시 수량 선택 창을 띄운다. 해제하면 1개씩 즉시 구매")]
    [SerializeField] private bool useQuantityPopup = true;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 1560f;
    [SerializeField] private float panelHeight = 900f;
    [SerializeField] private int sortingOrder = 30600;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;
    [SerializeField] private string tradeSfxName = SfxKeys.UiClick;

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 상수
    // ===================================================
    private const float FadeDuration = 0.14f;
    private const float ToastDuration = 2.2f;
    private const float BuyRowHeight = 86f;

    /// <summary>구매 목록 안의 지층 구분 줄 높이.</summary>
    private const float BuySectionHeight = 30f;

    /// <summary>업그레이드 미해금 줄의 아이콘 색. 실루엣이 아니라 '흐리게' — 무슨 장비인지는 보여야 목표가 된다.</summary>
    private static readonly Color LockedIconTint = new Color(1f, 1f, 1f, 0.35f);

    private const float SellRowHeight = 74f;

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built;
    private bool _isOpen;
    private bool _opening;
    private bool _needsRefresh;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;
    private ShopItemType _currentTab = ShopItemType.Item;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    private TextMeshProUGUI _goldText, _sellTotalText, _toastText, _hintText;
    private ScrollRect _buyScroll, _sellScroll;
    private RectTransform _buyList, _sellList;
    private Button _sellAllButton;
    private readonly List<(Button btn, ShopItemType tab, TextMeshProUGUI label)> _tabButtons = new List<(Button, ShopItemType, TextMeshProUGUI)>();

    private readonly List<BuyRow> _buyRows = new List<BuyRow>();
    private readonly List<BuySection> _buySections = new List<BuySection>();
    private readonly List<SellRow> _sellRows = new List<SellRow>();

    // WASD 키보드 내비게이션 (창고·일시정지 오버레이와 같은 공용 시스템)
    // 후보는 collect에서 매 이동마다 실시간 수집한다 — 수량 창이 떠 있으면 그 안에서만 움직인다.
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();
    private readonly List<ICodeNavItem> _navTabs = new List<ICodeNavItem>();
    private readonly List<ICodeNavItem> _navPopup = new List<ICodeNavItem>();
    private ICodeNavItem _navSellAll;

    private ShopManager _shopManager;
    private WarehouseManager _warehouse;
    private PlayerStat _playerStat;

    private float _toastUntil;
    private bool _subscribed;
    private bool _langSubscribed;

    // 수량 선택 창
    private GameObject _popupRoot;
    private TextMeshProUGUI _popupTitle, _popupUnit, _popupAmount, _popupTotal;
    private Image _popupIcon;
    private Button _popupConfirm;
    private TextMeshProUGUI _popupConfirmLabel;
    private ShopItemData _popupData;
    private int _popupQty = 1, _popupMax = 1;

    private class BuyRow
    {
        public GameObject root;
        public Image icon;
        public TextMeshProUGUI name, desc, price, stock, buyLabel;
        public Button buyButton;
        public ICodeNavItem buyNav;
    }

    /// <summary>구매 목록에 끼워 넣는 지층 구분 줄. 장비 탭에서만 켜진다.</summary>
    private class BuySection
    {
        public GameObject root;
        public TextMeshProUGUI label;
    }

    private class SellRow
    {
        public GameObject root;
        public Image icon;
        public TextMeshProUGUI name, unit, total;
        public Button sellOneButton, sellAllButton;
        public TextMeshProUGUI sellOneLabel, sellAllLabel;
        public ICodeNavItem sellOneNav, sellAllNav;
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

        HidePopup();
        _nav.End();
        Unsubscribe();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        // ESC 등으로 스스로 닫혔다면 UIStateManager 상태도 되돌린다.
        // 이 시점엔 이미 _isOpen=false라, SetState가 CloseStatic을 다시 불러도 즉시 반환된다(재귀 없음).
        // 팝업(NPC)에서 들어왔다면 None이 아니라 그 팝업으로 되돌린다(NPC → 팝업 → 상점 → ESC → 팝업).
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Shop) ui.ReturnToPopupOrClose();
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
        _currentTab = ShopItemType.Item;
        _loc.Refresh();
        RefreshAll();
        ClearToast();

        _canvasObj.SetActive(true);
        if (_buyScroll != null) _buyScroll.verticalNormalizedPosition = 1f;
        if (_sellScroll != null) _sellScroll.verticalNormalizedPosition = 1f;

        _nav.Begin();
        _nav.ClearFocus();   // 커서 없이 시작 — 첫 W/A/S/D 입력이 커서를 켠다

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

        // Space는 Input System에서 Submit로도 잡힌다 — 직전에 마우스로 누른 버튼이 선택된 채 남아 있으면
        // 내비게이터의 확인과 그 버튼의 onClick이 겹쳐 한 번에 두 번 실행된다.
        CodeUI.ClearSelection();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 수량 창이 떠 있으면 그것부터 닫는다
            if (_popupRoot != null && _popupRoot.activeSelf) { CodeUI.PlayBack(); HidePopup(); return; }
            if (closeOnEscape) { CodeUI.PlayBack(); Close(); return; }
        }

        // Q / E 로 구매 탭 순환 (수량 창이 없을 때만) — WASD 이동과 별개로 남겨 둔 빠른 탭 전환
        bool popupOpen = _popupRoot != null && _popupRoot.activeSelf;
        if (!popupOpen)
        {
            if (Input.GetKeyDown(KeyCode.E)) SwitchTab(NextTab(_currentTab, +1));
            else if (Input.GetKeyDown(KeyCode.Q)) SwitchTab(NextTab(_currentTab, -1));
        }

        // W/A/S/D 이동 + Space 선택 (구매/판매/탭, 수량 창이 뜨면 그 안에서만 이동)
        _nav.Update();

        if (_toastText != null && _toastUntil > 0f && Time.unscaledTime > _toastUntil) ClearToast();
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
        _nav.End();
        Unsubscribe();
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 참조 / 이벤트
    // ===================================================
    private void ResolveRefs()
    {
        // FindObjectsInactive.Include — ShopManager나 PlayerStat이 꺼진 UI 계층에 붙어 있어도 찾아야 한다
        // (EquipmentInventory가 EquipmentsPanel에 붙어 있던 것과 같은 함정)
        if (_shopManager == null) _shopManager = FindFirstObjectByType<ShopManager>(FindObjectsInactive.Include);
        _warehouse = WarehouseManager.Instance;
        if (_playerStat == null) _playerStat = _shopManager != null ? _shopManager.playerStats : null;
        if (_playerStat == null) _playerStat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;

        if (_warehouse != null) _warehouse.OnWarehouseChanged += MarkDirty;
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

        if (_warehouse != null) _warehouse.OnWarehouseChanged -= MarkDirty;
        if (_playerStat != null) _playerStat.OnGoldChanged -= OnGoldChanged;
    }

    private void MarkDirty() => _needsRefresh = true;
    private void OnGoldChanged(int _) => _needsRefresh = true;

    private void OnLanguageChanged(LanguageType _)
    {
        _loc.Refresh();
        _needsRefresh = true;
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("ShopOverlayCanvas");
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

        const float side = 22f;
        const float titleH = 72f;
        const float hintH = 30f;
        // 닫기 버튼은 두지 않는다 — ESC 로 닫히므로 화면만 차지한다.

        BuildTitleBar(panel.transform, side, titleH);

        var body = CodeUI.CreateRect(panel.transform, "Body");
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(side, side + hintH + 6f);
        body.offsetMax = new Vector2(-side, -(side + titleH + 12f));
        var bodyLayout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 16f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = true;

        // 판매(내 광물)를 왼쪽에 둬서 창고 오버레이의 좌우 감각(내 것 = 왼쪽)과 통일한다. 폭은 반반.
        BuildSellCard(body, 1f);
        BuildBuyCard(body, 1f);

        // 조작 안내 + 토스트 (같은 자리를 번갈아 쓴다 — Toast()가 안내를 숨기고 자기가 뜬다)
        _hintText = CodeUI.CreateText(panel.transform, "Hint", 17f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        var hintRt = _hintText.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.offsetMin = new Vector2(side, side);
        hintRt.offsetMax = new Vector2(-side, side + hintH);
        _loc.Bind(_hintText, "ui_shop_hint", "W / A / S / D : 이동   ·   Space : 선택   ·   Q / E : 탭   ·   ESC : 닫기");

        _toastText = CodeUI.CreateText(panel.transform, "Toast", 18f, FontStyles.Bold,
            CodeUI.NegativeColor, TextAlignmentOptions.Center, _loc);
        var toastRt = _toastText.rectTransform;
        toastRt.anchorMin = hintRt.anchorMin;
        toastRt.anchorMax = hintRt.anchorMax;
        toastRt.pivot = hintRt.pivot;
        toastRt.offsetMin = hintRt.offsetMin;
        toastRt.offsetMax = hintRt.offsetMax;
        _toastText.gameObject.SetActive(false);

        BuildQuantityPopup();

        // WASD 내비게이션 후보 수집 — 수량 창이 떠 있으면 그 안의 버튼만, 아니면
        // 탭·구매 버튼·판매 버튼·전부 팔기를 한 판 위에 섞어 좌표 기준으로 이동한다.
        // (꺼진 행·비활성 버튼은 CodeNavButton.NavUsable이 알아서 걸러 준다)
        _nav.moveSfxName = clickSfxName;
        _nav.collect = list =>
        {
            list.Clear();
            if (_popupRoot != null && _popupRoot.activeSelf)
            {
                list.AddRange(_navPopup);
                return;
            }

            list.AddRange(_navTabs);
            foreach (var row in _buyRows)
                if (row.buyNav != null) list.Add(row.buyNav);
            foreach (var row in _sellRows)
            {
                if (row.sellOneNav != null) list.Add(row.sellOneNav);
                if (row.sellAllNav != null) list.Add(row.sellAllNav);
            }
            if (_navSellAll != null) list.Add(_navSellAll);
        };

        _canvasObj.SetActive(false);
    }

    private void BuildTitleBar(Transform panel, float side, float titleH)
    {
        var bar = CodeUI.CreateRect(panel, "TitleBar");
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(side, -(side + titleH));
        bar.offsetMax = new Vector2(-side, -side);
        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var diamond = CodeUI.CreateText(bar, "Diamond", 22f, FontStyles.Normal, CodeUI.GoldColor, TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;

        var title = CodeUI.CreateText(bar, "Title", 32f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        title.characterSpacing = 8f;
        _loc.Bind(title, "ui_shop_title", "상 점");

        CodeUI.CreateSpacer(bar);

        var goldBox = CodeUI.CreateImage(bar, "GoldBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var goldLe = goldBox.gameObject.AddComponent<LayoutElement>();
        goldLe.preferredWidth = 220f;
        goldLe.preferredHeight = 46f;
        _goldText = CodeUI.CreateText(goldBox.transform, "Gold", 24f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(_goldText.rectTransform);
    }

    private void BuildBuyCard(Transform parent, float flexibleWidth)
    {
        var card = CodeUI.CreateImage(parent, "BuyCard", CodeUI.CardBg, skin.cardSprite, skin);
        card.gameObject.AddComponent<LayoutElement>().flexibleWidth = flexibleWidth;
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CodeUI.CreateRow(card.transform, "Header", 34f, 8f);
        var dot = CodeUI.CreateText(header, "Dot", 16f, FontStyles.Normal, CodeUI.PositiveColor, TextAlignmentOptions.Center, _loc);
        dot.text = diamondGlyph;
        var label = CodeUI.CreateText(header, "Label", 23f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(label, "ui_shop_buy", "구매");
        CodeUI.CreateSpacer(header);

        CodeUI.CreateDivider(card.transform);

        var tabs = CodeUI.CreateRow(card.transform, "Tabs", 42f, 8f);
        AddTab(tabs, ShopItemType.Item, "ui_shop_tab_item", "소모품", CodeUI.ItemColor);
        AddTab(tabs, ShopItemType.Equipment, "ui_shop_tab_equip", "장비", CodeUI.EquipColor);
        AddTab(tabs, ShopItemType.Relic, "ui_shop_tab_relic", "유물", CodeUI.RelicColor);

        _buyScroll = CodeUI.CreateScrollView(card.transform, "BuyScroll", out _buyList);
        var buyLe = _buyScroll.gameObject.AddComponent<LayoutElement>();
        buyLe.flexibleHeight = 1f;
        buyLe.minHeight = BuyRowHeight * 2f;
        var listLayout = _buyList.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 8f;
        listLayout.padding = new RectOffset(2, 2, 4, 4);
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;
    }

    private void BuildSellCard(Transform parent, float flexibleWidth)
    {
        var card = CodeUI.CreateImage(parent, "SellCard", CodeUI.CardBg, skin.cardSprite, skin);
        card.gameObject.AddComponent<LayoutElement>().flexibleWidth = flexibleWidth;
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CodeUI.CreateRow(card.transform, "Header", 34f, 8f);
        var dot = CodeUI.CreateText(header, "Dot", 16f, FontStyles.Normal, CodeUI.MineralColor, TextAlignmentOptions.Center, _loc);
        dot.text = diamondGlyph;
        var label = CodeUI.CreateText(header, "Label", 23f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(label, "ui_shop_sell", "판매");
        CodeUI.CreateSpacer(header);
        var sub = CodeUI.CreateText(header, "Sub", 17f, FontStyles.Normal, CodeUI.MutedColor, TextAlignmentOptions.MidlineRight, _loc);
        sub.gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
        _loc.Bind(sub, "ui_shop_sell_source", "창고의 광물");

        CodeUI.CreateDivider(card.transform);

        _sellScroll = CodeUI.CreateScrollView(card.transform, "SellScroll", out _sellList);
        var sellLe = _sellScroll.gameObject.AddComponent<LayoutElement>();
        sellLe.flexibleHeight = 1f;
        sellLe.minHeight = SellRowHeight * 2f;
        var listLayout = _sellList.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 8f;
        listLayout.padding = new RectOffset(2, 2, 4, 4);
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        // 하단: 총 예상 수익 + 전부 팔기
        var footer = CodeUI.CreateRow(card.transform, "Footer", 48f, 10f);
        _sellTotalText = CodeUI.CreateText(footer, "Total", 20f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.MidlineLeft, _loc);
        _sellTotalText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        _sellAllButton = CodeUI.CreateTextButton(footer, "SellAll", CodeUI.PositiveColor, CodeUI.GoldFg, 19f,
            OnClickSellAll, out var sellAllLabel, skin.buttonSprite, skin, _loc);
        var le = _sellAllButton.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 180f;
        le.preferredHeight = 44f;
        _loc.Bind(sellAllLabel, "ui_shop_sell_all", "전부 팔기");

        _navSellAll = CodeNavButton.Attach(_sellAllButton, skin);
    }

    private void AddTab(Transform parent, ShopItemType tab, string key, string fallback, Color accent)
    {
        var btn = CodeUI.CreateButton(parent, "Tab_" + tab, CodeUI.TabIdleBg, () => SwitchTab(tab), skin.tabSprite, skin);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredHeight = 40f;

        var label = CodeUI.CreateText(btn.transform, "Text", 19f, FontStyles.Bold, accent, TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(label.rectTransform);
        _loc.Bind(label, key, fallback);

        _tabButtons.Add((btn, tab, label));

        var nav = CodeNavButton.Attach(btn, skin);
        if (nav != null) _navTabs.Add(nav);
    }

    // ===================================================
    // 행(row) 생성
    // ===================================================
    /// <summary>
    /// 지층 구분 줄("2층 · 얼음땅" + 가로선). 행 사이에 끼워 넣으므로 형제 순서는
    /// <see cref="RefreshBuyList"/>가 매번 다시 잡는다 — 여기서는 만들기만 한다.
    /// </summary>
    private BuySection CreateBuySection()
    {
        var row = CodeUI.CreateRow(_buyList, "BuySection", BuySectionHeight, 10f,
            TextAnchor.MiddleLeft, new RectOffset(4, 4, 0, 0));

        var section = new BuySection { root = row.gameObject };

        // 문자열은 탭·언어에 따라 매번 바뀌므로 Bind가 아니라 Track이다(폰트만 따라간다).
        section.label = CodeUI.CreateText(row, "Label", 17f, FontStyles.Bold, CodeUI.EquipColor,
            TextAlignmentOptions.MidlineLeft, _loc);
        section.label.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        var line = CodeUI.CreateImage(row, "Line", CodeUI.DividerColor, rounded: false);
        line.raycastTarget = false;
        var lineLe = line.gameObject.AddComponent<LayoutElement>();
        lineLe.flexibleWidth = 1f;
        lineLe.preferredHeight = 2f;

        return section;
    }

    private BuyRow CreateBuyRow()
    {
        var bg = CodeUI.CreateImage(_buyList, "BuyRow", CodeUI.SlotBg, skin.slotSprite, skin);
        bg.gameObject.AddComponent<LayoutElement>().preferredHeight = BuyRowHeight;
        var layout = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var row = new BuyRow { root = bg.gameObject };

        // 아이콘
        var iconBox = CodeUI.CreateImage(bg.transform, "IconBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = 64f;
        iconLe.preferredHeight = 64f;
        row.icon = CodeUI.CreateImage(iconBox.transform, "Icon", Color.white, rounded: false);
        var ir = row.icon.rectTransform;
        ir.anchorMin = Vector2.zero;
        ir.anchorMax = Vector2.one;
        ir.offsetMin = new Vector2(6f, 6f);
        ir.offsetMax = new Vector2(-6f, -6f);
        row.icon.preserveAspect = true;
        row.icon.raycastTarget = false;

        // 이름 + 설명
        // 남는 폭을 전부 가져가되, 글자 길이가 옆 칸을 밀지 않도록 preferred는 0으로 둔다
        // (이걸 안 두면 이름·설명이 긴 줄에서 가격·버튼이 눌려 줄마다 버튼 폭이 달라진다)
        var textCol = CodeUI.CreateColumn(bg.transform, "Text", 2f);
        var textLe = textCol.gameObject.AddComponent<LayoutElement>();
        textLe.preferredWidth = 0f;
        textLe.minWidth = 60f;
        textLe.flexibleWidth = 1f;
        row.name = CodeUI.CreateText(textCol, "Name", 21f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        row.name.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        row.desc = CodeUI.CreateText(textCol, "Desc", 15f, FontStyles.Normal, CodeUI.MutedColor, TextAlignmentOptions.TopLeft, _loc);
        row.desc.textWrappingMode = TextWrappingModes.Normal;
        row.desc.overflowMode = TextOverflowModes.Ellipsis;
        row.desc.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        // 가격 + 재고
        var priceCol = CodeUI.CreateColumn(bg.transform, "Price", 2f);
        var priceLe = priceCol.gameObject.AddComponent<LayoutElement>();
        priceLe.preferredWidth = priceLe.minWidth = 130f;
        priceLe.flexibleWidth = 0f;
        row.price = CodeUI.CreateText(priceCol, "Value", 21f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.MidlineRight, _loc);
        row.price.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        row.stock = CodeUI.CreateText(priceCol, "Stock", 15f, FontStyles.Normal, CodeUI.MutedColor, TextAlignmentOptions.TopRight, _loc);
        row.stock.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        // 구매 버튼
        row.buyButton = CodeUI.CreateTextButton(bg.transform, "Buy", CodeUI.AccentFill, Color.white, 19f,
            null, out row.buyLabel, skin.buttonSprite, skin, _loc);
        var btnLe = row.buyButton.gameObject.AddComponent<LayoutElement>();
        btnLe.preferredWidth = btnLe.minWidth = 110f;
        btnLe.preferredHeight = btnLe.minHeight = 46f;
        btnLe.flexibleWidth = 0f;
        _loc.Bind(row.buyLabel, "ui_shop_buy_btn", "구매");

        row.buyNav = CodeNavButton.Attach(row.buyButton, skin);

        return row;
    }

    private SellRow CreateSellRow()
    {
        var bg = CodeUI.CreateImage(_sellList, "SellRow", CodeUI.SlotBg, skin.slotSprite, skin);
        bg.gameObject.AddComponent<LayoutElement>().preferredHeight = SellRowHeight;
        var layout = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var row = new SellRow { root = bg.gameObject };

        var iconBox = CodeUI.CreateImage(bg.transform, "IconBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = 56f;
        iconLe.preferredHeight = 56f;
        row.icon = CodeUI.CreateImage(iconBox.transform, "Icon", Color.white, rounded: false);
        var ir = row.icon.rectTransform;
        ir.anchorMin = Vector2.zero;
        ir.anchorMax = Vector2.one;
        ir.offsetMin = new Vector2(5f, 5f);
        ir.offsetMax = new Vector2(-5f, -5f);
        row.icon.preserveAspect = true;
        row.icon.raycastTarget = false;

        var textCol = CodeUI.CreateColumn(bg.transform, "Text", 1f);
        var textLe = textCol.gameObject.AddComponent<LayoutElement>();
        textLe.preferredWidth = 0f;
        textLe.minWidth = 60f;
        textLe.flexibleWidth = 1f;
        row.name = CodeUI.CreateText(textCol, "Name", 19f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        row.name.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        row.unit = CodeUI.CreateText(textCol, "Unit", 15f, FontStyles.Normal, CodeUI.MutedColor, TextAlignmentOptions.TopLeft, _loc);
        row.unit.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        row.total = CodeUI.CreateText(bg.transform, "Total", 20f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.MidlineRight, _loc);
        var totalLe = row.total.gameObject.AddComponent<LayoutElement>();
        totalLe.preferredWidth = totalLe.minWidth = 120f;
        totalLe.flexibleWidth = 0f;

        row.sellOneButton = CodeUI.CreateTextButton(bg.transform, "SellOne", CodeUI.NeutralBg, Color.white, 17f,
            null, out row.sellOneLabel, skin.buttonSprite, skin, _loc);
        var oneLe = row.sellOneButton.gameObject.AddComponent<LayoutElement>();
        oneLe.preferredWidth = oneLe.minWidth = 74f;
        oneLe.preferredHeight = oneLe.minHeight = 40f;
        oneLe.flexibleWidth = 0f;
        _loc.Bind(row.sellOneLabel, "ui_shop_sell_one", "1개");

        row.sellAllButton = CodeUI.CreateTextButton(bg.transform, "SellStack", CodeUI.PositiveColor, CodeUI.GoldFg, 17f,
            null, out row.sellAllLabel, skin.buttonSprite, skin, _loc);
        var allLe = row.sellAllButton.gameObject.AddComponent<LayoutElement>();
        allLe.preferredWidth = allLe.minWidth = 84f;
        allLe.preferredHeight = allLe.minHeight = 40f;
        allLe.flexibleWidth = 0f;
        _loc.Bind(row.sellAllLabel, "ui_shop_sell_stack", "전부");

        row.sellOneNav = CodeNavButton.Attach(row.sellOneButton, skin);
        row.sellAllNav = CodeNavButton.Attach(row.sellAllButton, skin);

        return row;
    }

    // ===================================================
    // 갱신
    // ===================================================
    private void RefreshAll()
    {
        if (_warehouse == null) _warehouse = WarehouseManager.Instance;

        RefreshBuyList();
        RefreshSellList();
        RefreshGold();
        UpdateTabVisuals();

        // 목록을 다시 그렸으니 커서가 꺼진 행에 남아 있으면 가까운 항목으로 되돌린다.
        _nav.Refresh();
    }

    /// <summary>
    /// 구매 행 하나에 필요한 표시 정보. 아이템/장비는 InterfaceInventoryItem에서,
    /// 유물은 RelicSO에서 뽑아 오므로 공통 형태로 한 번 정규화한다.
    /// </summary>
    private struct BuyEntry
    {
        public ShopItemData data;
        public Sprite icon;
        public string name;
        public string desc;
        public bool stackable;
        public bool owned;   // 유물 전용 — 이미 보유하면 구매 불가
        public bool locked;  // 업그레이드 관문 미해금 — 목록에는 보이되 구매 불가
        public string lockName; // 잠금을 푸는 노드의 표시 이름
    }

    /// <summary>"'곡괭이 입수' 업그레이드 필요" — 설명 줄과 토스트가 같은 문장을 쓴다.</summary>
    private static string LockReasonText(string nodeName)
    {
        string template = CodeUI.L("ui_shop_need_upgrade", "'{0}' 업그레이드 필요");
        return string.Format(template, nodeName);
    }

    private List<BuyEntry> CollectBuyEntries()
    {
        var entries = new List<BuyEntry>();

        var db = _shopManager != null ? _shopManager.shopItemDatabase : null;
        if (db == null) return entries;

        foreach (var data in db.GetAvailableItems())
        {
            if (data == null || data.itemType != _currentTab) continue;

            // 업그레이드 관문 미해금 항목도 목록에는 남긴다 — 뭘 노리고 트리를 찍을지 보이게 하려는 것.
            // 이름 조회는 잠긴 항목에서만 한다(노드 캐시 조회가 목록 전체에 걸리지 않게).
            bool locked = !ShopUnlockGate.IsUnlocked(data);
            string lockName = locked ? ShopUnlockGate.RequiredNodeName(data.unlockNodeId) : string.Empty;

            if (data.itemType == ShopItemType.Relic)
            {
                var so = Relic.Data.RelicDatabase.Instance != null
                    ? Relic.Data.RelicDatabase.Instance.GetRelicByID(data.relicID)
                    : null;
                if (so == null) continue;

                bool isActive = so.type == Relic.Data.RelicType.Active;
                entries.Add(new BuyEntry
                {
                    data = data,
                    icon = so.icon,
                    name = CodeUI.L(so.displayNameKey, so.displayNameKey),
                    desc = CodeUI.L(so.descriptionKey, so.descriptionKey) + "   ·   " +
                           (isActive ? CodeUI.L("ui_relic_active", "액티브") : CodeUI.L("ui_relic_passive", "패시브")),
                    stackable = false,
                    owned = _shopManager != null && _shopManager.IsRelicOwned(data.relicID),
                    locked = locked,
                    lockName = lockName
                });
                continue;
            }

            InterfaceInventoryItem item = null;
            if (data.itemType == ShopItemType.Item && ItemDatabase.Instance != null)
                item = ItemDatabase.Instance.GetItemByID(data.itemID);
            else if (data.itemType == ShopItemType.Equipment && EquipmentDatabase.Instance != null)
                item = EquipmentDatabase.Instance.GetEquipmentByID(data.equipmentID);

            if (item == null) continue;

            // 장비는 유물처럼 고유 — 이미 보유 중이면 구매 불가로 표시한다.
            bool owned = data.itemType == ShopItemType.Equipment
                         && _shopManager != null
                         && _shopManager.IsEquipmentOwned(data.equipmentID);

            entries.Add(new BuyEntry
            {
                data = data,
                icon = item.Icon,
                name = item.DisplayName,
                desc = item.Description ?? string.Empty,
                stackable = item.Stackable,
                owned = owned,
                locked = locked,
                lockName = lockName
            });
        }

        return entries;
    }

    /// <summary>
    /// 장비 목록을 "지층 → 세트 합계 가격 → 부위" 순으로 다시 세운다.
    /// 세트는 절대 쪼개지 않는다 — 부위 셋이 항상 붙어 나오고, 그 세트 덩어리끼리 싼 순으로 올라간다.
    /// 지층 표에 없는 장비(레거시 3001 등)는 맨 아래 '기타'로 밀린다.
    /// </summary>
    private static void SortEquipmentByLayer(List<BuyEntry> entries)
    {
        // 세트 합계는 "지금 목록에 떠 있는 부위"만 더한다 — 부위 하나가 빠져도 남은 것끼리 같은 기준으로 비교된다.
        var setTotal = new Dictionary<string, long>();
        foreach (var e in entries)
        {
            string set = ShopEquipmentLayers.SetOf(e.data.equipmentID);
            if (set == null) continue;
            setTotal.TryGetValue(set, out long sum);
            setTotal[set] = sum + e.data.price;
        }

        entries.Sort((a, b) =>
        {
            // Unknown은 지층 0이라 그냥 두면 1층보다 위로 올라간다 — 정렬 키에서만 맨 뒤로 민다.
            int ka = LayerSortKey(a.data.equipmentID);
            int kb = LayerSortKey(b.data.equipmentID);
            if (ka != kb) return ka.CompareTo(kb);

            string sa = ShopEquipmentLayers.SetOf(a.data.equipmentID);
            string sb = ShopEquipmentLayers.SetOf(b.data.equipmentID);
            if (sa != sb)
            {
                long ta = sa != null ? setTotal[sa] : a.data.price;
                long tb = sb != null ? setTotal[sb] : b.data.price;
                if (ta != tb) return ta.CompareTo(tb);
                return string.CompareOrdinal(sa ?? string.Empty, sb ?? string.Empty);
            }

            // 같은 세트 안은 머리 → 옷 → 신발.
            int pa = ShopEquipmentLayers.PartOrder(a.data.equipmentID);
            int pb = ShopEquipmentLayers.PartOrder(b.data.equipmentID);
            if (pa != pb) return pa.CompareTo(pb);

            // List.Sort는 불안정 정렬이라 여기까지 같으면 ID로 순서를 못 박는다(갱신마다 줄이 뒤바뀌지 않게).
            return ((int)a.data.equipmentID).CompareTo((int)b.data.equipmentID);
        });
    }

    private static int LayerSortKey(EquipmentID id)
    {
        int layer = ShopEquipmentLayers.LayerOf(id);
        return layer == ShopEquipmentLayers.Unknown ? int.MaxValue : layer;
    }

    private void RefreshBuyList()
    {
        var entries = CollectBuyEntries();

        // 지층으로 나누는 건 장비 탭뿐이다 — 소모품·유물은 DB에 적힌 순서를 그대로 쓴다.
        bool grouped = (_currentTab == ShopItemType.Equipment);
        if (grouped) SortEquipmentByLayer(entries);

        while (_buyRows.Count < entries.Count) _buyRows.Add(CreateBuyRow());

        int gold = _playerStat != null ? _playerStat.Gold : 0;

        // 구분 줄과 행이 번갈아 나오므로 형제 순서를 직접 매긴다(꺼진 행은 뒤로 밀린다).
        int sectionsUsed = 0;
        int sibling = 0;
        int lastLayer = int.MinValue;

        for (int i = 0; i < _buyRows.Count; i++)
        {
            var row = _buyRows[i];
            bool used = i < entries.Count;
            row.root.SetActive(used);
            if (!used) continue;

            var entry = entries[i];
            var data = entry.data;

            if (grouped)
            {
                int layer = ShopEquipmentLayers.LayerOf(data.equipmentID);
                if (layer != lastLayer)
                {
                    lastLayer = layer;
                    while (_buySections.Count <= sectionsUsed) _buySections.Add(CreateBuySection());
                    var section = _buySections[sectionsUsed++];
                    section.root.SetActive(true);
                    section.label.text = ShopEquipmentLayers.LayerLabel(layer);
                    section.root.transform.SetSiblingIndex(sibling++);
                }
            }
            row.root.transform.SetSiblingIndex(sibling++);

            row.icon.sprite = entry.icon;
            row.icon.enabled = (entry.icon != null);
            // 줄은 재사용된다 — 잠김 여부와 무관하게 매번 색을 다시 써야 이전 줄의 흐린 아이콘이 남지 않는다.
            row.icon.color = entry.locked ? LockedIconTint : Color.white;
            row.name.text = entry.name;
            // 잠금 사유(노드 이름)는 설명 줄에 붙여 항상 보이게 한다.
            // 구매 버튼이 비활성이라 클릭 토스트는 뜨지 않는다 — 어떤 업그레이드를 찍어야 열리는지가 화면에 남아야 한다.
            if (entry.locked && !string.IsNullOrEmpty(entry.lockName))
                row.desc.text = entry.desc + "   ·   " + LockReasonText(entry.lockName);
            else
                row.desc.text = entry.desc;
            row.price.text = $"{CodeUI.Gold(data.price)} G";

            bool outOfStock = data.stock >= 0 && data.stock <= 0;
            row.stock.text = entry.locked
                ? CodeUI.L("ui_shop_locked", "업그레이드 필요")
                : (entry.owned
                    ? CodeUI.L("ui_shop_owned", "보유 중")
                    : (data.stock < 0
                        ? CodeUI.L("ui_shop_stock_unlimited", "재고 무제한")
                        : $"{CodeUI.L("ui_shop_stock_label", "재고")} {data.stock}"));
            row.stock.color = entry.locked ? CodeUI.MutedColor
                                           : (entry.owned ? CodeUI.PositiveColor
                                                          : (outOfStock ? CodeUI.NegativeColor : CodeUI.MutedColor));

            bool affordable = gold >= data.price;
            // 잠긴 줄은 골드가 있든 없든 살 수 없다 — 빨간 가격으로 "돈이 문제"라고 오해시키지 않는다.
            row.price.color = entry.locked ? CodeUI.MutedColor
                                           : (affordable ? CodeUI.GoldColor : CodeUI.NegativeColor);

            bool canBuy = affordable && !outOfStock && !entry.owned && !entry.locked;
            row.buyButton.interactable = canBuy;
            CodeUI.ApplySkin(row.buyButton.image, canBuy ? CodeUI.AccentFill : CodeUI.NeutralBg, skin.buttonSprite, skin);
            row.buyLabel.text = entry.locked
                ? CodeUI.L("ui_shop_locked_btn", "잠김")
                : (entry.owned
                    ? CodeUI.L("ui_shop_owned_btn", "보유")
                    : CodeUI.L("ui_shop_buy_btn", "구매"));

            row.buyButton.onClick.RemoveAllListeners();
            var captured = entry;
            row.buyButton.onClick.AddListener(() => OnClickBuy(captured));
        }

        for (int i = sectionsUsed; i < _buySections.Count; i++)
            _buySections[i].root.SetActive(false);
    }

    private void RefreshSellList()
    {
        // 같은 광물은 창고에서 여러 슬롯으로 쪼개져 있어도 한 줄로 합쳐 보여준다.
        // (판매도 슬롯 지정 없이 호출해 WarehouseManager가 여러 슬롯에서 알아서 빼게 한다)
        var entries = new List<(MineralSO mineral, int quantity, int unitPrice)>();
        var indexOf = new Dictionary<string, int>();
        long grandTotal = 0;

        var priceDb = _shopManager != null ? _shopManager.priceDatabase : null;
        if (_warehouse != null)
        {
            var slots = _warehouse.AllSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                var mineral = slots[i]?.item as MineralSO;
                if (mineral == null) continue;

                int unit = priceDb != null ? priceDb.GetPrice(mineral.mineralID) : 0;
                int qty = slots[i].quantity;

                if (indexOf.TryGetValue(mineral.Id, out int at))
                {
                    // 이미 나온 광물 — 수량만 더한다 (등장 순서는 처음 나온 자리를 유지)
                    entries[at] = (entries[at].mineral, entries[at].quantity + qty, entries[at].unitPrice);
                }
                else
                {
                    indexOf[mineral.Id] = entries.Count;
                    entries.Add((mineral, qty, unit));
                }
                grandTotal += (long)unit * qty;
            }
        }

        while (_sellRows.Count < entries.Count) _sellRows.Add(CreateSellRow());

        for (int i = 0; i < _sellRows.Count; i++)
        {
            var row = _sellRows[i];
            bool used = i < entries.Count;
            row.root.SetActive(used);
            if (!used) continue;

            var (mineral, qty, unit) = entries[i];

            row.icon.sprite = mineral.Icon;
            row.icon.enabled = (mineral.Icon != null);
            row.name.text = $"{mineral.DisplayName}  x{qty}";
            row.unit.text = $"{CodeUI.L("ui_shop_unit_price", "개당")} {CodeUI.Gold(unit)} G";
            row.total.text = $"{CodeUI.Gold((long)unit * qty)} G";

            bool sellable = unit > 0;
            row.total.color = sellable ? CodeUI.GoldColor : CodeUI.MutedColor;
            row.sellOneButton.interactable = sellable;
            row.sellAllButton.interactable = sellable;

            var capturedMineral = mineral;
            int capturedQty = qty;

            row.sellOneButton.onClick.RemoveAllListeners();
            row.sellOneButton.onClick.AddListener(() => Sell(capturedMineral, 1));

            row.sellAllButton.onClick.RemoveAllListeners();
            row.sellAllButton.onClick.AddListener(() => Sell(capturedMineral, capturedQty));
        }

        if (_sellTotalText != null)
            _sellTotalText.text = $"{CodeUI.L("ui_shop_expected", "예상 수익")}  {CodeUI.Gold(grandTotal)} G";

        if (_sellAllButton != null) _sellAllButton.interactable = grandTotal > 0;
    }

    private void RefreshGold()
    {
        if (_goldText != null)
            _goldText.text = $"{CodeUI.Gold(_playerStat != null ? _playerStat.Gold : 0)} G";
    }

    private void UpdateTabVisuals()
    {
        foreach (var (btn, tab, label) in _tabButtons)
        {
            bool selected = (tab == _currentTab);
            Sprite sprite = selected
                ? (skin.tabSelectedSprite != null ? skin.tabSelectedSprite : skin.tabSprite)
                : skin.tabSprite;
            CodeUI.ApplySkin(btn.image, selected ? CodeUI.AccentFill : CodeUI.TabIdleBg, sprite, skin);
            label.color = selected ? Color.white : new Color(label.color.r, label.color.g, label.color.b, 0.75f);
        }
    }

    /// <summary>탭 순환. _tabButtons에 등록된 순서(소모품 → 장비 → 유물)를 그대로 쓴다.</summary>
    private ShopItemType NextTab(ShopItemType cur, int dir)
    {
        if (_tabButtons.Count == 0) return cur;
        int at = _tabButtons.FindIndex(t => t.tab == cur);
        if (at < 0) at = 0;
        int next = (at + dir + _tabButtons.Count) % _tabButtons.Count;
        return _tabButtons[next].tab;
    }

    private void SwitchTab(ShopItemType tab)
    {
        if (_currentTab == tab) return;
        CodeUI.PlaySfx(clickSfxName);
        _currentTab = tab;
        RefreshBuyList();
        UpdateTabVisuals();
        if (_buyScroll != null) _buyScroll.verticalNormalizedPosition = 1f;
        _nav.Refresh();
    }

    // ===================================================
    // 거래
    // ===================================================
    private void OnClickBuy(BuyEntry entry)
    {
        CodeUI.PlaySfx(clickSfxName);
        var data = entry.data;
        if (data == null || _shopManager == null) return;

        if (entry.locked)
        {
            ToastText(LockReasonText(entry.lockName), CodeUI.MutedColor);
            return;
        }

        if (entry.owned)
        {
            Toast("ui_shop_already_owned", "이미 보유 중입니다.", CodeUI.MutedColor);
            return;
        }

        int gold = _playerStat != null ? _playerStat.Gold : 0;
        if (data.price > 0 && gold < data.price)
        {
            Toast("ui_shop_no_gold", "골드가 부족합니다.", CodeUI.NegativeColor);
            return;
        }

        int max = data.stock < 0 ? 99 : data.stock;
        if (data.price > 0) max = Mathf.Min(max, gold / data.price);
        if (!entry.stackable) max = 1;
        max = Mathf.Max(1, max);

        if (!useQuantityPopup || max == 1)
        {
            ExecuteBuy(data, 1);
            return;
        }
        ShowPopup(entry, max);
    }

    private void ExecuteBuy(ShopItemData data, int quantity)
    {
        if (_shopManager == null) return;

        if (_shopManager.BuyItem(data, quantity))
        {
            CodeUI.PlaySfx(tradeSfxName);
            if (data.itemType == ShopItemType.Relic)
                Toast("ui_shop_bought_relic", "유물을 얻었습니다. 창고·가방의 유물 칸에서 확인·교체하세요.", CodeUI.PositiveColor);
            else
                Toast("ui_shop_bought", "구매했습니다. 물건은 창고에 있습니다.", CodeUI.PositiveColor);
        }
        else
        {
            Toast("ui_shop_buy_failed", "구매하지 못했습니다.", CodeUI.NegativeColor);
        }
        _needsRefresh = true;
    }

    /// <summary>
    /// 창고의 해당 광물을 amount만큼 판매. 슬롯을 지정하지 않으므로
    /// WarehouseManager가 여러 슬롯에 흩어진 같은 광물을 앞에서부터 합쳐 빼간다.
    /// </summary>
    private void Sell(MineralSO mineral, int amount)
    {
        if (_shopManager == null || mineral == null || amount <= 0) return;

        // 목록을 만든 뒤 창고가 바뀌었을 수 있으니 현재 보유량으로 다시 잘라낸다
        int owned = _warehouse != null ? _warehouse.GetMineralCount(mineral) : 0;
        if (owned <= 0) { _needsRefresh = true; return; }

        int move = Mathf.Clamp(amount, 1, owned);
        if (_shopManager.SellItem(mineral, move, fromWarehouse: true))
        {
            CodeUI.PlaySfx(tradeSfxName);
        }
        else
        {
            Toast("ui_shop_sell_failed", "판매하지 못했습니다.", CodeUI.NegativeColor);
        }
        _needsRefresh = true;
    }

    private void OnClickSellAll()
    {
        CodeUI.PlaySfx(clickSfxName);
        if (_shopManager == null || _warehouse == null) return;

        // 광물별 총량을 먼저 모은 뒤 종류당 한 번씩 판다.
        // (판매 도중 창고가 바뀌므로 순회 중에 팔지 않는다)
        var totals = new Dictionary<string, (MineralSO mineral, int quantity)>();
        foreach (var slot in _warehouse.AllSlots)
        {
            var mineral = slot?.item as MineralSO;
            if (mineral == null || slot.quantity <= 0) continue;

            if (totals.TryGetValue(mineral.Id, out var cur))
                totals[mineral.Id] = (cur.mineral, cur.quantity + slot.quantity);
            else
                totals[mineral.Id] = (mineral, slot.quantity);
        }

        int sold = 0;
        foreach (var entry in totals.Values)
        {
            if (_shopManager.SellItem(entry.mineral, entry.quantity, fromWarehouse: true)) sold++;
        }

        if (sold > 0)
        {
            CodeUI.PlaySfx(tradeSfxName);
            Toast("ui_shop_sold_all", "창고의 광물을 전부 팔았습니다.", CodeUI.PositiveColor);
        }
        else
        {
            Toast("ui_shop_nothing_to_sell", "팔 광물이 없습니다.", CodeUI.MutedColor);
        }
        _needsRefresh = true;
    }

    // ===================================================
    // 수량 선택 창
    // ===================================================
    private void BuildQuantityPopup()
    {
        _popupRoot = new GameObject("QuantityPopup", typeof(RectTransform));
        _popupRoot.transform.SetParent(_canvasObj.transform, false);
        CodeUI.StretchFull((RectTransform)_popupRoot.transform);

        // 뒤쪽 클릭 차단용 막 (클릭하면 닫힘)
        var backdrop = CodeUI.CreateImage(_popupRoot.transform, "Backdrop", new Color(0f, 0f, 0f, 0.55f), rounded: false);
        CodeUI.StretchFull(backdrop.rectTransform);
        var backdropBtn = backdrop.gameObject.AddComponent<Button>();
        backdropBtn.transition = Selectable.Transition.None;
        backdropBtn.onClick.AddListener(HidePopup);

        var panel = CodeUI.CreateImage(_popupRoot.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var rt = panel.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(520f, 400f);
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 22, 22);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        _popupTitle = CodeUI.CreateText(panel.transform, "Title", 25f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, _loc);
        _popupTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        // 아이콘 + 단가
        var infoRow = CodeUI.CreateRow(panel.transform, "Info", 76f, 12f, TextAnchor.MiddleCenter);
        var iconBox = CodeUI.CreateImage(infoRow, "IconBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = 72f;
        iconLe.preferredHeight = 72f;
        _popupIcon = CodeUI.CreateImage(iconBox.transform, "Icon", Color.white, rounded: false);
        var ir = _popupIcon.rectTransform;
        ir.anchorMin = Vector2.zero;
        ir.anchorMax = Vector2.one;
        ir.offsetMin = new Vector2(7f, 7f);
        ir.offsetMax = new Vector2(-7f, -7f);
        _popupIcon.preserveAspect = true;
        _popupIcon.raycastTarget = false;

        _popupUnit = CodeUI.CreateText(infoRow, "Unit", 19f, FontStyles.Normal, CodeUI.LabelColor, TextAlignmentOptions.MidlineLeft, _loc);
        _popupUnit.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // 수량 스테퍼
        var stepper = CodeUI.CreateRow(panel.transform, "Stepper", 56f, 10f, TextAnchor.MiddleCenter);
        var minusBtn = AddStepButton(stepper, -1, () => StepQuantity(-1));
        var amountBox = CodeUI.CreateImage(stepper, "AmountBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var amountLe = amountBox.gameObject.AddComponent<LayoutElement>();
        amountLe.preferredWidth = 180f;
        amountLe.preferredHeight = 52f;
        _popupAmount = CodeUI.CreateText(amountBox.transform, "Amount", 26f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(_popupAmount.rectTransform);
        var plusBtn = AddStepButton(stepper, +1, () => StepQuantity(+1));

        // 최대 / 총액
        var maxBtn = CodeUI.CreateTextButton(panel.transform, "MaxButton", CodeUI.NeutralBg, Color.white, 18f,
            () => { CodeUI.PlaySfx(clickSfxName); SetQuantity(_popupMax); }, out var maxLabel,
            skin.buttonSprite, skin, _loc);
        maxBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        _loc.Bind(maxLabel, "ui_shop_max", "최대 수량");

        _popupTotal = CodeUI.CreateText(panel.transform, "Total", 23f, FontStyles.Bold, CodeUI.GoldColor, TextAlignmentOptions.Center, _loc);
        _popupTotal.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        // 구매(왼쪽) / 취소(오른쪽) — 생성 순서가 곧 좌→우 배치 순서다
        var buttons = CodeUI.CreateRow(panel.transform, "Buttons", 54f, 12f, TextAnchor.MiddleCenter);

        _popupConfirm = CodeUI.CreateTextButton(buttons, "Confirm", CodeUI.GoldColor, CodeUI.GoldFg, 20f,
            OnConfirmBuy, out _popupConfirmLabel, skin.buttonSprite, skin, _loc);
        var confirmLe = _popupConfirm.gameObject.AddComponent<LayoutElement>();
        confirmLe.flexibleWidth = 1f;
        confirmLe.preferredHeight = 50f;
        _loc.Bind(_popupConfirmLabel, "ui_shop_confirm", "구매");

        var cancel = CodeUI.CreateTextButton(buttons, "Cancel", CodeUI.NeutralBg, Color.white, 20f,
            () => { CodeUI.PlaySfx(clickSfxName); HidePopup(); }, out var cancelLabel, skin.buttonSprite, skin, _loc);
        var cancelLe = cancel.gameObject.AddComponent<LayoutElement>();
        cancelLe.flexibleWidth = 1f;
        cancelLe.preferredHeight = 50f;
        _loc.Bind(cancelLabel, "ui_shop_cancel", "취소");

        // 수량 창 안의 WASD 이동 대상 (−/+/최대/구매/취소). 창이 떠 있는 동안 collect가 이 목록만 돌려준다.
        _navPopup.Clear();
        foreach (var b in new[] { minusBtn, plusBtn, maxBtn, _popupConfirm, cancel })
        {
            var nav = CodeNavButton.Attach(b, skin);
            if (nav != null) _navPopup.Add(nav);
        }

        _popupRoot.SetActive(false);
    }

    private Button AddStepButton(Transform parent, int dir, System.Action onClick)
    {
        var btn = CodeUI.CreateButton(parent, dir < 0 ? "Minus" : "Plus", CodeUI.TabIdleBg, onClick, skin.buttonSprite, skin);
        if (skin.buttonSprite == null) btn.image.sprite = CodeUI.Rounded(24, 0.28f);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 52f;
        le.preferredHeight = 52f;

        var tri = new GameObject("Arrow").AddComponent<Image>();
        tri.transform.SetParent(btn.transform, false);
        tri.sprite = CodeUI.Triangle(dir);
        tri.color = CodeUI.LabelColor;
        tri.raycastTarget = false;
        var trt = tri.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(16f, 18f);
        return btn;
    }

    private void ShowPopup(BuyEntry entry, int max)
    {
        var data = entry.data;
        _popupData = data;
        _popupMax = Mathf.Max(1, max);

        _popupTitle.text = entry.name;
        _popupIcon.sprite = entry.icon;
        _popupIcon.enabled = (entry.icon != null);
        _popupUnit.text = $"{CodeUI.L("ui_shop_unit_price", "개당")} {CodeUI.Gold(data.price)} G\n" +
                          $"{CodeUI.L("ui_shop_max_label", "최대")} {_popupMax}";

        SetQuantity(1);
        _popupRoot.transform.SetAsLastSibling();
        _popupRoot.SetActive(true);
        _nav.ClearFocus();   // 후보 집합이 바뀌었으니 커서를 수량 창에서 새로 잡게 한다
    }

    private void HidePopup()
    {
        if (_popupRoot != null) _popupRoot.SetActive(false);
        _popupData = null;
        _nav.ClearFocus();
    }

    private void StepQuantity(int dir)
    {
        CodeUI.PlaySfx(clickSfxName);
        SetQuantity(_popupQty + dir);
    }

    private void SetQuantity(int value)
    {
        _popupQty = Mathf.Clamp(value, 1, _popupMax);
        _popupAmount.text = _popupQty.ToString();

        long total = (long)(_popupData != null ? _popupData.price : 0) * _popupQty;
        int gold = _playerStat != null ? _playerStat.Gold : 0;
        bool affordable = total <= gold;

        _popupTotal.text = $"{CodeUI.L("ui_shop_total", "총액")}  {CodeUI.Gold(total)} G";
        _popupTotal.color = affordable ? CodeUI.GoldColor : CodeUI.NegativeColor;

        _popupConfirm.interactable = affordable;
        CodeUI.ApplySkin(_popupConfirm.image, affordable ? CodeUI.GoldColor : CodeUI.NeutralBg, skin.buttonSprite, skin);
        _popupConfirmLabel.color = affordable ? CodeUI.GoldFg : Color.white;
    }

    private void OnConfirmBuy()
    {
        if (_popupData == null) { HidePopup(); return; }
        var data = _popupData;
        int qty = _popupQty;
        HidePopup();
        ExecuteBuy(data, qty);
    }

    // ===================================================
    // 토스트 안내
    // ===================================================
    private void Toast(string key, string fallback, Color color)
    {
        ToastText(CodeUI.L(key, fallback), color);
    }

    /// <summary>이미 완성된 문장을 띄운다(로컬라이즈 후 값을 끼워 넣어야 하는 경우).</summary>
    private void ToastText(string text, Color color)
    {
        if (_toastText == null) return;
        _toastText.text = text;
        _toastText.color = color;
        _toastText.gameObject.SetActive(true);
        if (_hintText != null) _hintText.gameObject.SetActive(false); // 같은 자리 — 안 숨기면 글자가 겹친다
        _toastUntil = Time.unscaledTime + ToastDuration;
    }

    private void ClearToast()
    {
        _toastUntil = 0f;
        if (_toastText != null) _toastText.gameObject.SetActive(false);
        if (_hintText != null) _hintText.gameObject.SetActive(true);
    }
}
