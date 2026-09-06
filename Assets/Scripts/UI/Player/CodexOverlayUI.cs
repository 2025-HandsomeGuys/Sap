// @tags: codex, collection, ui, overlay, code-generated, discovery, mineral, equipment, item, relic, tabs

using System.Collections;
using System.Collections.Generic;
using Relic;
using Relic.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 도감 오버레이 — 광물·장비·아이템·유물 4개 탭. 발견한 것은 아이콘·이름·설명으로,
/// 아직 못 얻은 것은 실루엣+???로 보여준다. 좌측은 카테고리별 그룹 격자, 우측은 선택 항목 상세.
/// UI는 전부 코드로 생성한다(다른 코드 오버레이와 동일 패턴) — 씬/프리팹 세팅 없이 <see cref="Open"/>만으로 동작.
///
/// 그룹 기준(정렬):
///  - 광물: 단계(MineralID 100/200/300/400 → 1~4)
///  - 장비: 부위(<see cref="EquipmentType"/> 머리/옷/신발)
///  - 아이템: 효과 유형(<see cref="ItemActiveType"/>)
///  - 유물: 발동 방식(<see cref="RelicType"/> 패시브/액티브)
///
/// 발견 기록의 원천은 <see cref="CollectionCodex"/>(전역 static, 세이브 연동). 여는 즉시
/// 창고·가방 보유분과 보유 유물을 발견 백필해 누락을 막는다.
///
/// 일시정지 메뉴(<see cref="PauseOverlayUI"/>)의 '도감' 버튼이 <c>UIState.Codex</c>로 연다.
/// 배경 장식은 인스펙터 <see cref="backgroundSprite"/>(비우면 패널 색만).
///
/// 스프라이트 스킨(<see cref="UISkin"/>): 도감은 씬 배치본이 없어 런타임에 자동 생성되므로 인스펙터로
/// 스킨을 지정할 대상이 없다. 대신 <see cref="SetSkin"/>/<see cref="Open"/>로 여는 쪽(일시정지 메뉴)의
/// 스킨을 넘겨받아 같은 스프라이트 테마로 그린다. 그래서 도감 스프라이트는 PauseOverlayUI의 skin에서 설정한다.
/// </summary>
public class CodexOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤 + 상태 플래그 (UIStateManager가 폴링)
    // ===================================================
    private static CodexOverlayUI _instance;

    public static CodexOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<CodexOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("CodexOverlayUI");
                    _instance = go.AddComponent<CodexOverlayUI>();
                }
            }
            return _instance;
        }
    }

    public static bool IsOpen => _instance != null && _instance._isOpen;
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    /// <summary>
    /// 도감을 닫을 때 게임플레이(None)로 나가지 않고 일시정지 메뉴로 되돌아갈지.
    /// 일시정지 메뉴에서 도감을 열 때 <see cref="OpenFromPause"/>로 켜 둔다 —
    /// 그러면 ESC로 도감을 닫아도 곧장 게임으로 복귀하지 않고 일시정지 창으로 돌아온다.
    /// </summary>
    private static bool s_returnToPause;

    /// <summary>일시정지 메뉴 경로로 도감을 연다(닫을 때 일시정지로 복귀). <c>SetState(Codex)</c> 전에 호출.</summary>
    public static void OpenFromPause() => s_returnToPause = true;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!_isOpen && !_opening) return;
        _opening = false;
        _isOpen = false;
        Time.timeScale = 1f;
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 인스펙터
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Tooltip("도감 패널 안쪽에 깔리는 장식 배경 이미지(양피지·책 등). 비우면 패널 색만.")]
    [SerializeField] private Sprite backgroundSprite;
    [Tooltip("배경 이미지 색조(투명도 포함). 너무 밝으면 카드가 안 보이니 어둡게/반투명 권장.")]
    [SerializeField] private Color backgroundTint = new Color(1f, 1f, 1f, 0.35f);
    [Tooltip("배경 이미지를 원본 비율로 채울지(가운데 크롭). 끄면 패널에 늘려 채운다.")]
    [SerializeField] private bool backgroundPreserveAspect = false;

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.5f;
    [SerializeField] private bool useBlurBackdrop = true;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("동작")]
    [Tooltip("열려 있는 동안 게임을 멈춘다(Time.timeScale = 0)")]
    [SerializeField] private bool pauseGameWhileOpen = true;
    [SerializeField] private bool closeOnEscape = true;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 1320f;
    [SerializeField] private float panelHeight = 860f;
    [SerializeField] private int gridColumns = 5;
    [SerializeField] private Vector2 cellSize = new Vector2(120f, 138f);
    [SerializeField] private Vector2 cellSpacing = new Vector2(14f, 14f);
    [SerializeField] private int sortingOrder = 30470;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;

    // ===================================================
    // 상수
    // ===================================================
    private const float FadeDuration = 0.14f;
    private const int TabCount = 5;
    /// <summary>마지막 탭 = 가이드(튜토리얼 다시보기). CodexCategory에는 없는 탭이라 인덱스로만 다룬다
    /// (CodexCategory는 세이브 리스트 인덱스라 값을 늘리면 구세이브가 깨진다).</summary>
    private const int GuideTab = 4;
    // 미발견 항목 실루엣 색 — 순수 검정으로 스프라이트 모양(알파)만 남긴다(완전 실루엣).
    private static readonly Color SilhouetteColor = new Color(0f, 0f, 0f, 1f);

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built, _isOpen, _opening, _needsRefresh;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;
    private int _activeTab = (int)CodexCategory.Mineral;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    // WASD 키보드 내비게이션 (다른 코드 오버레이와 같은 공용 시스템)
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();
    private readonly List<ICodeNavItem> _navItems = new List<ICodeNavItem>();
    private readonly Dictionary<ICodeNavItem, CodexEntry> _itemToEntry = new Dictionary<ICodeNavItem, CodexEntry>();
    private ICodeNavItem _lastFocused;

    private TextMeshProUGUI _progress, _emptyText, _hint;
    private ScrollRect _scroll;
    private RectTransform _list;

    // 탭
    private readonly Button[] _tabButtons = new Button[TabCount];
    private readonly Image[] _tabImages = new Image[TabCount];
    private readonly TextMeshProUGUI[] _tabLabels = new TextMeshProUGUI[TabCount];

    // 상세 패널
    private Image _detailIcon;
    private TextMeshProUGUI _detailName, _detailGroup, _detailDesc;
    private CodexEntry _selected;
    private int _previewPage;   // 가이드 탭 옆칸 미리보기의 현재 페이지

    private bool _langSubscribed;

    // 한 항목(칸)의 데이터. 카테고리별로 SO에서 뽑아 채운다.
    private class CodexEntry
    {
        public string id;
        public Sprite icon;
        public string name;
        public string description;
        public bool discovered;
        public int groupKey;   // 그룹(단계·부위·유형) 정렬·헤더 키
        public int sortId;     // 그룹 안 정렬용(enum 정수값)
        public int pageCount;  // 가이드 탭 전용 — 페이지 수(상세에 표시)
        public GuideSO guide;  // 가이드 탭 전용 — 옆칸 미리보기가 페이지를 직접 읽는다
    }

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    public static void Open(UISkin externalSkin = null)
    {
        if (externalSkin != null) Instance.AdoptSkin(externalSkin);
        if (IsOpen) { Instance._needsRefresh = true; return; }
        if (Instance._opening) return;
        Instance.StartCoroutine(Instance.OpenRoutine());
    }

    /// <summary>
    /// 도감을 열기 직전, 소유 오버레이(일시정지 메뉴 등)의 스프라이트 스킨을 넘겨 같은 테마로 그리게 한다.
    /// 도감은 씬 배치본이 없어 런타임에 빈 스킨으로 자동 생성되므로, 이 경로로만 스프라이트가 들어온다.
    /// <see cref="UIStateManager"/>가 상태 전환으로 여는 주 경로를 커버하려면 <c>SetState</c> 전에 호출할 것.
    /// </summary>
    public static void SetSkin(UISkin externalSkin)
    {
        if (externalSkin != null) Instance.AdoptSkin(externalSkin);
    }

    /// <summary>새 스킨을 받아들인다. 이미 빈 스킨으로 조립돼 있으면 다음 열기에서 다시 짓도록 허문다.</summary>
    private void AdoptSkin(UISkin s)
    {
        if (s == null || ReferenceEquals(s, skin)) return;
        skin = s;
        // 열려 있는 동안 스킨이 바뀌는 경우는 없다(같은 참조를 매번 넘김) — 닫힌 상태에서만 재조립.
        if (_built && !_isOpen) RebuildUI();
    }

    /// <summary>조립된 UI를 허물고 재조립 대기 상태로 되돌린다. 다음 <see cref="OpenRoutine"/>의 EnsureBuilt가 새 스킨으로 다시 짓는다.</summary>
    private void RebuildUI()
    {
        _nav.End();
        _blur.Release(_blurImage);
        if (_canvasObj != null) Destroy(_canvasObj);
        _canvasObj = null;
        _canvasGroup = null;
        _blurImage = null;
        _built = false;
        _selected = null;
        _lastFocused = null;
        _navItems.Clear();
        _itemToEntry.Clear();
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

        _nav.End();
        _lastFocused = null;
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        // 일시정지에서 열렸다면 게임으로 나가지 않고 일시정지 창으로 되돌아간다.
        bool backToPause = s_returnToPause;
        s_returnToPause = false;

        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Codex)
            ui.SetState(backToPause ? UIState.Pause : UIState.None);
    }

    private IEnumerator OpenRoutine()
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();

        // 구버전 세이브·발견 누락 대비 — 지금 보유한 것(창고·가방·유물)은 발견 처리한다.
        BackfillFromHoldings();

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

        if (!_langSubscribed && LanguageManager.Instance != null)
        {
            LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
            _langSubscribed = true;
        }

        _loc.Refresh();
        UpdateTabVisuals();
        RefreshAll();

        _canvasObj.SetActive(true);
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;

        _nav.Begin();
        _nav.ClearFocus();   // 커서 없이 시작 — 첫 W/A/S/D 입력이 커서를 켠다
        _lastFocused = null;

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

        // 가이드 다시보기가 도감 위에 떠 있는 동안엔 도감이 키를 먹지 않는다
        // (같은 프레임의 ESC로 도감까지 같이 닫히는 것을 막는다).
        if (GuideOverlayUI.IsOpen || GuideOverlayUI.ClosedThisFrame) return;

        // Space는 Submit로도 잡힌다 — 직전에 마우스로 누른 버튼이 선택된 채 남아 있으면 이중 처리된다.
        CodeUI.ClearSelection();

        if (_activeTab == GuideTab)
        {
            if (Input.GetKeyDown(KeyCode.Space)) { ReplaySelectedGuide(); return; }
            // 목록이 한 줄짜리 세로 스크롤이라 좌우 이동이 필요 없다 — A/D를 미리보기 페이지 넘김에 쓴다.
            if (Input.GetKeyDown(KeyCode.A)) { FlipPreview(-1); return; }
            if (Input.GetKeyDown(KeyCode.D)) { FlipPreview(+1); return; }
        }

        if (closeOnEscape && Input.GetKeyDown(KeyCode.Escape))
        {
            CodeUI.PlayBack();
            Close();
            return;
        }

        // Q / E : 탭 전환 (좌/우 순환)
        if (Input.GetKeyDown(KeyCode.Q)) { CycleTab(-1); return; }
        if (Input.GetKeyDown(KeyCode.E)) { CycleTab(+1); return; }

        // W/A/S/D 이동. 확인(Space)은 도감에선 '상세 보기'라 별도 동작 없이 포커스 추적으로 갱신된다.
        _nav.Update();

        // 키보드 커서·마우스 호버가 옮겨가면 상세 패널을 그 항목으로 갱신(둘이 포커스 하나를 공유).
        var focused = _nav.Focused;
        if (focused != null && !ReferenceEquals(focused, _lastFocused))
        {
            _lastFocused = focused;
            if (_itemToEntry.TryGetValue(focused, out var entry))
                SelectEntry(entry);
        }
    }

    private void LateUpdate()
    {
        if (!_isOpen) return;
        if (_needsRefresh)
        {
            _needsRefresh = false;
            RefreshAll();
        }
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        _nav.End();
        if (_langSubscribed && LanguageManager.Instance != null)
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
        _blur.Release(_blurImage);
    }

    private void OnLanguageChanged(LanguageType _)
    {
        _loc.Refresh();
        UpdateTabVisuals();
        _needsRefresh = true;
    }

    // ===================================================
    // 탭
    // ===================================================
    private void CycleTab(int dir)
    {
        SwitchTab((_activeTab + dir + TabCount) % TabCount);
    }

    private void SwitchTab(int tab)
    {
        if (tab == _activeTab) return;
        _activeTab = tab;
        CodeUI.PlaySfx(clickSfxName);
        _selected = null;
        UpdateTabVisuals();
        RefreshAll();
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        _nav.ClearFocus();
        _lastFocused = null;
    }

    private void UpdateTabVisuals()
    {
        for (int i = 0; i < TabCount; i++)
        {
            bool active = _activeTab == i;
            var img = _tabImages[i];
            if (img != null)
            {
                Color bg = active ? TabColor(i) : CodeUI.TabIdleBg;
                Sprite s = active && skin.tabSelectedSprite != null ? skin.tabSelectedSprite : skin.tabSprite;
                CodeUI.ApplySkin(img, bg, s, skin);
            }
            if (_tabLabels[i] != null)
                _tabLabels[i].color = active ? CodeUI.GoldFg : CodeUI.LabelColor;
        }
    }

    // ===================================================
    // 발견 백필
    // ===================================================
    /// <summary>지금 보유한 것(창고·가방·유물)을 발견 처리(구버전 세이브·기록 누락 보정).</summary>
    private static void BackfillFromHoldings()
    {
        var wh = WarehouseManager.Instance;
        if (wh != null)
        {
            DiscoverSlots(wh.StoredMinerals);
            DiscoverSlots(wh.StoredItems);
            DiscoverSlots(wh.StoredEquipments);
        }

        var minInv = FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
        if (minInv != null) DiscoverSlots(minInv.ReadonlyItems);
        var itemInv = FindFirstObjectByType<ItemInventory>(FindObjectsInactive.Include);
        if (itemInv != null) DiscoverSlots(itemInv.ReadonlyItems);
        var equipInv = FindFirstObjectByType<EquipmentInventory>(FindObjectsInactive.Include);
        if (equipInv != null) DiscoverSlots(equipInv.ReadonlyItems);

        // 유물은 영구 보유라 '보유 = 발견'. 매니저가 있으면 그 목록에서 백필한다.
        var relicMgr = FindFirstObjectByType<RelicManager>(FindObjectsInactive.Include);
        if (relicMgr != null && relicMgr.Inventory != null)
        {
            foreach (var kv in relicMgr.Inventory.Owned)
                if (kv.Key != RelicID.None)
                    CollectionCodex.Discover(CodexCategory.Relic, kv.Key.ToString());
        }
    }

    private static void DiscoverSlots(IReadOnlyList<InventorySlot> slots)
    {
        if (slots == null) return;
        foreach (var slot in slots)
        {
            if (slot == null || slot.item == null || slot.quantity <= 0) continue;
            switch (slot.item)
            {
                case MineralSO m: CollectionCodex.Discover(CodexCategory.Mineral, m.mineralID.ToString()); break;
                case ItemSO it:   CollectionCodex.Discover(CodexCategory.Item, it.itemID.ToString()); break;
                case EquipmentSO e: CollectionCodex.Discover(CodexCategory.Equipment, e.equipmentID.ToString()); break;
            }
        }
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("CodexOverlayCanvas");
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

        // 인스펙터 장식 배경 — 패널 안쪽에 깔린다(카드보다 뒤).
        if (backgroundSprite != null)
        {
            var bg = CodeUI.CreateImage(panel.transform, "DecorBackground", backgroundTint, rounded: false);
            bg.sprite = backgroundSprite;
            bg.type = Image.Type.Simple;
            bg.preserveAspect = backgroundPreserveAspect;
            bg.raycastTarget = false;
            var bgRt = bg.rectTransform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(8f, 8f);
            bgRt.offsetMax = new Vector2(-8f, -8f);
        }

        const float side = 24f;
        const float titleH = 58f;
        const float tabBarH = 52f;
        const float hintH = 28f;
        const float rightPaneW = 420f;
        const float gap = 18f;
        const float topBlock = side + titleH + tabBarH + 16f; // 목록·상세가 시작하는 상단 여백

        BuildTitleBar(panel.transform, side, titleH);
        BuildTabBar(panel.transform, side, titleH, tabBarH);

        // ── 좌측: 격자 (스크롤) ──
        var listHost = CodeUI.CreateImage(panel.transform, "ListCard", CodeUI.CardBg, skin.cardSprite, skin);
        var host = listHost.rectTransform;
        host.anchorMin = new Vector2(0f, 0f);
        host.anchorMax = new Vector2(1f, 1f);
        host.offsetMin = new Vector2(side, side + hintH + 6f);
        host.offsetMax = new Vector2(-(side + rightPaneW + gap), -topBlock);
        var hostLayout = listHost.gameObject.AddComponent<VerticalLayoutGroup>();
        hostLayout.padding = new RectOffset(14, 14, 14, 14);
        hostLayout.childControlWidth = true;
        hostLayout.childControlHeight = true;
        hostLayout.childForceExpandWidth = true;
        hostLayout.childForceExpandHeight = true;

        _scroll = CodeUI.CreateScrollView(listHost.transform, "Scroll", out _list);
        var listLayout = _list.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 16f;
        listLayout.padding = new RectOffset(4, 10, 4, 8);
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        _emptyText = CodeUI.CreateText(listHost.transform, "Empty", 20f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(_emptyText.rectTransform);
        _emptyText.gameObject.SetActive(false);

        // ── 우측: 상세 패널 ──
        BuildDetailPane(panel.transform, side, hintH, rightPaneW, topBlock);

        // ── 하단: 조작 안내 ──
        var hint = CodeUI.CreateText(panel.transform, "Hint", 16f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        var hintRt = hint.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.offsetMin = new Vector2(side, side);
        hintRt.offsetMax = new Vector2(-side, side + hintH);
        _hint = hint;   // 폰트 추적은 CreateText(_loc)가 이미 함 — 문자열은 BindHint가 탭에 맞춰 채운다
        BindHint();

        _nav.moveSfxName = clickSfxName;
        _nav.collect = list => { list.Clear(); list.AddRange(_navItems); };

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

        var title = CodeUI.CreateText(bar, "Title", 30f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, _loc);
        title.characterSpacing = 6f;
        _loc.Bind(title, "ui_codex_title", "도감");

        CodeUI.CreateSpacer(bar);

        _progress = CodeUI.CreateText(bar, "Progress", 22f, FontStyles.Bold, CodeUI.MineralColor,
            TextAlignmentOptions.MidlineRight, _loc);
        _progress.gameObject.AddComponent<LayoutElement>().preferredWidth = 300f;
    }

    private void BuildTabBar(Transform panel, float side, float titleH, float tabBarH)
    {
        var bar = CodeUI.CreateRect(panel, "TabBar");
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(side, -(side + titleH + 8f + tabBarH));
        bar.offsetMax = new Vector2(-side, -(side + titleH + 8f));
        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleCenter;

        string[] keys = { "ui_codex_tab_mineral", "ui_codex_tab_equipment", "ui_codex_tab_item",
                          "ui_codex_tab_relic", "ui_codex_tab_guide" };
        string[] fallbacks = { "광물", "장비", "아이템", "유물", "가이드" };

        for (int i = 0; i < TabCount; i++)
        {
            int tab = i;
            var btn = CodeUI.CreateTextButton(bar, "Tab_" + TabName(tab), CodeUI.TabIdleBg, CodeUI.LabelColor, 20f,
                () => SwitchTab(tab), out var label, skin.tabSprite, skin, _loc);
            label.characterSpacing = 3f;
            _loc.Bind(label, keys[i], fallbacks[i]);
            _tabButtons[i] = btn;
            _tabImages[i] = btn.targetGraphic as Image;
            _tabLabels[i] = label;
        }
    }

    private void BuildDetailPane(Transform panel, float side, float hintH, float paneW, float topBlock)
    {
        var card = CodeUI.CreateImage(panel, "DetailCard", CodeUI.CardBg, skin.cardSprite, skin);
        var rt = card.rectTransform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.offsetMin = new Vector2(-(side + paneW), side + hintH + 6f);
        rt.offsetMax = new Vector2(-side, -topBlock);

        var col = card.gameObject.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(26, 26, 30, 26);
        col.spacing = 16f;
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = true;
        col.childForceExpandHeight = false;
        col.childAlignment = TextAnchor.UpperCenter;

        var iconBox = CodeUI.CreateImage(card.transform, "IconBox", CodeUI.SlotBg, skin.slotSprite, skin);
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredHeight = iconLe.minHeight = 200f;

        _detailIcon = CodeUI.CreateImage(iconBox.transform, "Icon", Color.white, rounded: false);
        _detailIcon.raycastTarget = false;
        _detailIcon.preserveAspect = true;
        var diRt = _detailIcon.rectTransform;
        diRt.anchorMin = new Vector2(0.5f, 0.5f);
        diRt.anchorMax = new Vector2(0.5f, 0.5f);
        diRt.pivot = new Vector2(0.5f, 0.5f);
        diRt.sizeDelta = new Vector2(150f, 150f);
        diRt.anchoredPosition = Vector2.zero;

        _detailName = CodeUI.CreateText(card.transform, "Name", 26f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        _detailName.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        _detailGroup = CodeUI.CreateText(card.transform, "Group", 16f, FontStyles.Bold, CodeUI.MineralColor,
            TextAlignmentOptions.Center, _loc);
        _detailGroup.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        CodeUI.CreateDivider(card.transform);

        _detailDesc = CodeUI.CreateText(card.transform, "Desc", 18f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.TopLeft, _loc);
        _detailDesc.textWrappingMode = TextWrappingModes.Normal;
        _detailDesc.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
    }

    // ===================================================
    // 갱신
    // ===================================================
    private void RefreshAll()
    {
        _navItems.Clear();
        _itemToEntry.Clear();
        ClearChildren(_list);

        BindHint();
        var entries = BuildEntries(_activeTab);
        if (entries.Count == 0)
        {
            SetProgress(0, 0);
            ShowEmpty(_activeTab == GuideTab
                ? CodeUI.L("ui_codex_guide_empty", "아직 가이드가 없습니다.")
                : CodeUI.L("ui_codex_empty", "도감 데이터를 불러올 수 없습니다."));
            return;
        }
        _emptyText.gameObject.SetActive(false);

        // 그룹(단계·부위·유형)별로 묶고, 각 그룹 안은 sortId 순.
        var byGroup = new SortedDictionary<int, List<CodexEntry>>();
        int total = 0, discovered = 0;
        foreach (var e in entries)
        {
            total++;
            if (e.discovered) discovered++;
            if (!byGroup.TryGetValue(e.groupKey, out var list))
            {
                list = new List<CodexEntry>();
                byGroup[e.groupKey] = list;
            }
            list.Add(e);
        }
        SetProgress(discovered, total);

        CodexEntry firstSelectable = null;
        foreach (var kv in byGroup)
        {
            kv.Value.Sort((a, b) => a.sortId.CompareTo(b.sortId));
            BuildGroupSection(kv.Key, kv.Value, ref firstSelectable);
        }

        // 선택 유지: 이전 선택 항목이 이번 탭에 여전히 있으면 그대로, 없으면 첫 항목.
        CodexEntry keep = null;
        if (_selected != null)
            foreach (var e in entries)
                if (e.id == _selected.id) { keep = e; break; }
        SelectEntry(keep ?? firstSelectable);

        _nav.Refresh();
    }

    private List<CodexEntry> BuildEntries(int tab)
    {
        var result = new List<CodexEntry>();
        if (tab == GuideTab) { BuildGuideEntries(result); return result; }

        var cat = (CodexCategory)tab;
        switch (cat)
        {
            case CodexCategory.Mineral:
            {
                var db = MineralDatabase.Instance;
                if (db == null || db.allMinerals == null) break;
                foreach (var m in db.allMinerals)
                {
                    if (m == null || m.mineralID == MineralID.None) continue;
                    string id = m.mineralID.ToString();
                    result.Add(new CodexEntry
                    {
                        id = id, icon = m.Icon, name = m.DisplayName, description = m.Description,
                        discovered = CollectionCodex.IsDiscovered(cat, id),
                        groupKey = Mathf.Clamp((int)m.mineralID / 100, 1, 4), sortId = (int)m.mineralID,
                    });
                }
                break;
            }
            case CodexCategory.Equipment:
            {
                var db = EquipmentDatabase.Instance;
                if (db == null || db.allEquipments == null) break;
                foreach (var eq in db.allEquipments)
                {
                    if (eq == null || eq.equipmentID == EquipmentID.None) continue;
                    string id = eq.equipmentID.ToString();
                    result.Add(new CodexEntry
                    {
                        id = id, icon = eq.Icon, name = eq.DisplayName, description = eq.Description,
                        discovered = CollectionCodex.IsDiscovered(cat, id),
                        groupKey = (int)eq.equipmentType, sortId = (int)eq.equipmentID,
                    });
                }
                break;
            }
            case CodexCategory.Item:
            {
                var db = ItemDatabase.Instance;
                if (db == null || db.allItems == null) break;
                foreach (var it in db.allItems)
                {
                    if (it == null || it.itemID == ItemID.None) continue;
                    string id = it.itemID.ToString();
                    result.Add(new CodexEntry
                    {
                        id = id, icon = it.Icon, name = it.DisplayName, description = it.Description,
                        discovered = CollectionCodex.IsDiscovered(cat, id),
                        groupKey = (int)it.activeType, sortId = (int)it.itemID,
                    });
                }
                break;
            }
            case CodexCategory.Relic:
            {
                var db = RelicDatabase.Instance;
                if (db == null || db.allRelics == null) break;
                foreach (var r in db.allRelics)
                {
                    if (r == null || r.id == RelicID.None) continue;
                    string id = r.id.ToString();
                    result.Add(new CodexEntry
                    {
                        id = id, icon = r.icon,
                        name = CodeUI.L(r.displayNameKey, r.displayNameKey),
                        description = CodeUI.L(r.descriptionKey, r.descriptionKey),
                        discovered = CollectionCodex.IsDiscovered(cat, id),
                        groupKey = (int)r.type, sortId = (int)r.id,
                    });
                }
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// 가이드 탭 — 등록된 모든 가이드를 한 칸씩. '본 적 있는' 가이드만 열려 있고(다시보기 가능),
    /// 아직 안 본 가이드는 다른 탭의 미발견 항목과 같은 실루엣·??? 처리다.
    /// </summary>
    private void BuildGuideEntries(List<CodexEntry> result)
    {
        int order = 0;
        foreach (var g in GuideManager.AllGuides)
        {
            if (g == null || !g.IsValid) continue;

            bool seen = GuideManager.IsSeen(g.guideId);
            var first = g.pages[0];
            string title = !string.IsNullOrEmpty(g.bannerTitleKey) || !string.IsNullOrEmpty(g.bannerTitleFallback)
                ? CodeUI.L(g.bannerTitleKey, g.bannerTitleFallback)
                : CodeUI.L(first.titleKey, first.titleFallback);

            result.Add(new CodexEntry
            {
                id = g.guideId,
                icon = GuideThumbnail(g),
                name = title,
                description = CodeUI.L(first.bodyKey, first.bodyFallback),
                discovered = seen,
                groupKey = 0,
                sortId = order++,
                pageCount = g.pages.Count,
                guide = g,
            });
        }
    }

    /// <summary>가이드 칸 대표 이미지 — 이미지/프레임 페이지 중 첫 번째. 동영상뿐이면 없음(빈 칸).</summary>
    private static Sprite GuideThumbnail(GuideSO g)
    {
        foreach (var page in g.pages)
        {
            if (page == null) continue;
            if (page.mediaType == GuideMediaType.Image && page.image != null) return page.image;
            if (page.mediaType == GuideMediaType.Frames && page.frames != null && page.frames.Length > 0)
                return page.frames[0];
        }
        return null;
    }

    /// <summary>가이드 탭에서 선택된 가이드를 다시 재생(도감은 뒤에 열린 채 유지된다).</summary>
    private void ReplaySelectedGuide()
    {
        if (_activeTab != GuideTab || _selected == null || !_selected.discovered) return;
        CodeUI.PlaySfx(clickSfxName);
        // 도감이 일시정지 메뉴에서 물려받은 스킨을 가이드에도 넘겨 한 벌로 보이게 한다
        // (가이드 오버레이를 씬에 배치했다면 그쪽 인스펙터 스킨이 이미 있어 이 호출은 무시된다).
        GuideOverlayUI.SetSkin(skin);
        GuideManager.Show(_selected.id);   // force — seen 기록·전체 토글과 무관하게 표시
    }

    private void BuildGroupSection(int groupKey, List<CodexEntry> entries, ref CodexEntry firstSelectable)
    {
        // 가이드 탭은 도감과 다른 물건이다 — 그룹 헤더·격자 없이 위에서 아래로 읽는 목록.
        if (_activeTab == GuideTab)
        {
            var rows = CodeUI.CreateRect(_list, "Rows");
            var rowCol = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            rowCol.spacing = 8f;
            rowCol.childControlWidth = true;
            rowCol.childControlHeight = true;
            rowCol.childForceExpandWidth = true;
            rowCol.childForceExpandHeight = false;

            foreach (var e in entries)
            {
                if (firstSelectable == null) firstSelectable = e;
                BuildGuideRow(rows, e);
            }
            return;
        }

        var header = CodeUI.CreateText(_list, "GroupHeader", 18f, FontStyles.Bold, TabColor(_activeTab),
            TextAlignmentOptions.MidlineLeft);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        header.text = GroupLabel(_activeTab, groupKey);

        var gridObj = CodeUI.CreateRect(_list, "Grid");
        var grid = gridObj.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = cellSize;
        grid.spacing = cellSpacing;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, gridColumns);
        grid.childAlignment = TextAnchor.UpperLeft;

        foreach (var e in entries)
        {
            if (firstSelectable == null) firstSelectable = e;
            BuildCard(gridObj, e);
        }
    }

    /// <summary>가이드 목록의 한 줄 — 제목(왼쪽) + 완료 여부(오른쪽). 클릭하면 옆칸 미리보기가 그 가이드로 바뀐다.</summary>
    private void BuildGuideRow(Transform parent, CodexEntry entry)
    {
        // 미리보기는 마우스를 올리기만 해도 따라오므로(호버가 포커스를 공유), 클릭은 '크게 보기'로 쓴다.
        var btn = CodeUI.CreateButton(parent, "Row", CodeUI.SlotBg,
            () => { SelectEntry(entry); ReplaySelectedGuide(); },
            skin.slotSprite, skin);
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;

        var row = btn.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(16, 14, 0, 0);
        row.spacing = 10f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;
        row.childAlignment = TextAnchor.MiddleLeft;

        var title = CodeUI.CreateText(btn.transform, "Title", 19f, FontStyles.Bold,
            entry.discovered ? Color.white : CodeUI.MutedColor, TextAlignmentOptions.MidlineLeft);
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        title.text = entry.discovered ? entry.name : CodeUI.L("ui_codex_undiscovered", "???");

        var state = CodeUI.CreateText(btn.transform, "State", 15f, FontStyles.Bold,
            entry.discovered ? CodeUI.GoldColor : CodeUI.MutedColor, TextAlignmentOptions.MidlineRight);
        state.gameObject.AddComponent<LayoutElement>().preferredWidth = 74f;
        state.text = entry.discovered
            ? CodeUI.L("ui_codex_guide_done", "완료")
            : CodeUI.L("ui_codex_guide_locked", "잠김");

        var nav = CodeNavButton.Attach(btn, skin);
        if (nav != null)
        {
            _navItems.Add(nav);
            _itemToEntry[nav] = entry;
        }
    }

    /// <summary>
    /// 상세 칸 이미지의 크기 규칙. 도감 아이콘은 정사각(150), 가이드 미리보기는 사진이라 칸을 꽉 채운다
    /// (preserveAspect가 켜져 있어 비율은 유지된 채 레터박스로 들어간다).
    /// </summary>
    private void SetDetailIconWide(bool wide)
    {
        if (_detailIcon == null) return;
        var rt = _detailIcon.rectTransform;
        if (wide)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, 10f);
            rt.offsetMax = new Vector2(-10f, -10f);
        }
        else
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(150f, 150f);
            rt.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>옆칸 미리보기의 페이지를 넘긴다(가이드 탭 A/D). 끝에서 멈춘다 — 순환하면 몇 번째인지 감이 사라진다.</summary>
    private void FlipPreview(int dir)
    {
        if (_selected == null || !_selected.discovered || _selected.guide == null) return;
        int total = _selected.guide.pages.Count;
        int next = Mathf.Clamp(_previewPage + dir, 0, total - 1);
        if (next == _previewPage) return;
        _previewPage = next;
        CodeUI.PlaySfx(clickSfxName);
        ShowGuidePreview(_selected);
    }

    /// <summary>선택된 가이드의 현재 페이지를 오른쪽 칸에 그대로 보여준다(사진 + 제목 + 본문).</summary>
    private void ShowGuidePreview(CodexEntry entry)
    {
        var g = entry.guide;
        if (g == null || g.pages == null || g.pages.Count == 0) return;

        _previewPage = Mathf.Clamp(_previewPage, 0, g.pages.Count - 1);
        var page = g.pages[_previewPage];

        SetDetailIconWide(true);
        if (_detailIcon != null)
        {
            Sprite media = page.mediaType == GuideMediaType.Image ? page.image
                         : page.mediaType == GuideMediaType.Frames && page.frames != null && page.frames.Length > 0
                             ? page.frames[0] : null;
            _detailIcon.sprite = media;
            _detailIcon.enabled = media != null;
            _detailIcon.color = Color.white;
        }
        if (_detailName != null)
            _detailName.text = CodeUI.L(page.titleKey, page.titleFallback);
        if (_detailGroup != null)
        {
            _detailGroup.text = g.pages.Count > 1
                ? $"A  <  {_previewPage + 1} / {g.pages.Count}  >  D"
                : $"{_previewPage + 1} / {g.pages.Count}";
            _detailGroup.color = CodeUI.MutedColor;
        }
        if (_detailDesc != null)
            _detailDesc.text = CodeUI.L(page.bodyKey, page.bodyFallback);
    }

    private void BuildCard(Transform parent, CodexEntry entry)
    {
        var btn = CodeUI.CreateButton(parent, "Card", CodeUI.SlotBg, () => SelectEntry(entry),
            skin.slotSprite, skin);
        var col = btn.gameObject.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(8, 8, 10, 8);
        col.spacing = 6f;
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = true;
        col.childForceExpandHeight = false;
        col.childAlignment = TextAnchor.UpperCenter;

        var icon = CodeUI.CreateImage(btn.transform, "Icon",
            entry.discovered ? Color.white : SilhouetteColor, rounded: false);
        icon.sprite = entry.icon;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.enabled = entry.icon != null;
        var iconLe = icon.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredHeight = iconLe.minHeight = 78f;

        var name = CodeUI.CreateText(btn.transform, "Name", 14f, FontStyles.Bold,
            entry.discovered ? CodeUI.LabelColor : CodeUI.MutedColor, TextAlignmentOptions.Center);
        name.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        name.text = entry.discovered ? entry.name : CodeUI.L("ui_codex_undiscovered", "???");
        name.textWrappingMode = TextWrappingModes.Normal;

        var nav = CodeNavButton.Attach(btn, skin);
        if (nav != null)
        {
            _navItems.Add(nav);
            _itemToEntry[nav] = entry;
        }
    }

    private void SelectEntry(CodexEntry entry)
    {
        if (entry == null) return;
        bool changed = !ReferenceEquals(entry, _selected);
        _selected = entry;

        if (_activeTab == GuideTab)
        {
            if (changed) _previewPage = 0;
            if (entry.discovered && entry.guide != null) { ShowGuidePreview(entry); return; }

            // 아직 안 본 가이드: 사진도 본문도 가리고 잠김 문구만.
            SetDetailIconWide(true);
            if (_detailIcon != null) { _detailIcon.sprite = null; _detailIcon.enabled = false; }
            if (_detailName != null) _detailName.text = CodeUI.L("ui_codex_undiscovered", "???");
            if (_detailGroup != null)
            {
                _detailGroup.text = CodeUI.L("ui_codex_guide_unseen", "아직 보지 않음");
                _detailGroup.color = CodeUI.MutedColor;
            }
            if (_detailDesc != null)
                _detailDesc.text = CodeUI.L("ui_codex_guide_unseen_desc",
                    "아직 이 가이드를 만나지 못했습니다. 계속 진행해 보세요.");
            return;
        }

        SetDetailIconWide(false);
        if (_detailIcon != null)
        {
            _detailIcon.sprite = entry.icon;
            _detailIcon.enabled = entry.icon != null;
            _detailIcon.color = entry.discovered ? Color.white : SilhouetteColor;
        }
        if (_detailName != null)
            _detailName.text = entry.discovered
                ? entry.name
                : CodeUI.L(_activeTab == GuideTab ? "ui_codex_guide_unseen" : "ui_codex_undiscovered", "???");
        if (_detailGroup != null)
        {
            _detailGroup.text = GroupLabel(_activeTab, entry.groupKey);
            _detailGroup.color = TabColor(_activeTab);
        }
        if (_detailDesc != null)
        {
            if (!entry.discovered)
                _detailDesc.text = _activeTab == GuideTab
                    ? CodeUI.L("ui_codex_guide_unseen_desc", "아직 이 가이드를 만나지 못했습니다. 계속 진행해 보세요.")
                    : CodeUI.L("ui_codex_undiscovered_desc", "아직 발견하지 못했습니다. 계속 탐험해 보세요.");
            else _detailDesc.text = entry.description;
        }
    }

    private void SetProgress(int discovered, int total)
    {
        if (_progress == null) return;
        string key = _activeTab == GuideTab ? "ui_codex_guide_progress" : "ui_codex_progress";
        _progress.text = LanguageManager.Instance != null
            ? LanguageManager.Instance.LF(key, discovered, total)
            : (_activeTab == GuideTab ? $"완료 {discovered} / {total}" : $"발견 {discovered} / {total}");
        _progress.color = (total > 0 && discovered >= total) ? CodeUI.GoldColor : TabColor(_activeTab);
    }

    private void ShowEmpty(string message)
    {
        if (_emptyText == null) return;
        _emptyText.gameObject.SetActive(true);
        _emptyText.text = message;
    }

    // ===================================================
    // 카테고리별 색·그룹 라벨
    // ===================================================
    private static string TabName(int tab) => tab == GuideTab ? "Guide" : ((CodexCategory)tab).ToString();

    private static Color TabColor(int tab)
    {
        if (tab == GuideTab) return CodeUI.GoldColor;
        switch ((CodexCategory)tab)
        {
            case CodexCategory.Equipment: return CodeUI.EquipColor;
            case CodexCategory.Item:      return CodeUI.ItemColor;
            case CodexCategory.Relic:     return CodeUI.RelicColor;
            default:                      return CodeUI.MineralColor;
        }
    }

    /// <summary>그룹 헤더·상세의 그룹 라벨. 카테고리마다 groupKey의 의미가 다르다.</summary>
    private static string GroupLabel(int tab, int groupKey)
    {
        if (tab == GuideTab) return CodeUI.L("ui_codex_guide_group", "가이드");
        switch ((CodexCategory)tab)
        {
            case CodexCategory.Mineral:
                return CodeUI.L($"ui_codex_tier{groupKey}",
                    LanguageManager.Instance != null
                        ? LanguageManager.Instance.LF("ui_codex_tier_label", groupKey) : $"{groupKey}단계");

            case CodexCategory.Equipment:
                switch ((EquipmentType)groupKey)
                {
                    case EquipmentType.Head:    return CodeUI.L("ui_codex_eq_head", "머리");
                    case EquipmentType.Clothes: return CodeUI.L("ui_codex_eq_clothes", "옷");
                    case EquipmentType.Shoes:   return CodeUI.L("ui_codex_eq_shoes", "신발");
                    default:                    return CodeUI.L("ui_codex_group_etc", "기타");
                }

            case CodexCategory.Item:
                switch ((ItemActiveType)groupKey)
                {
                    case ItemActiveType.Instant: return CodeUI.L("ui_codex_item_instant", "즉시 사용");
                    case ItemActiveType.Buff:    return CodeUI.L("ui_codex_item_buff", "버프");
                    case ItemActiveType.Hybrid:  return CodeUI.L("ui_codex_item_hybrid", "복합");
                    case ItemActiveType.Utility: return CodeUI.L("ui_codex_item_utility", "유틸리티");
                    default:                     return CodeUI.L("ui_codex_group_etc", "기타");
                }

            case CodexCategory.Relic:
                return (RelicType)groupKey == RelicType.Active
                    ? CodeUI.L("ui_codex_relic_active", "액티브")
                    : CodeUI.L("ui_codex_relic_passive", "패시브");
        }
        return string.Empty;
    }

    /// <summary>하단 조작 안내 — 가이드 탭에서만 Space(다시 보기)가 추가된다.</summary>
    private void BindHint()
    {
        if (_hint == null) return;
        // _loc.Bind는 등록이 누적돼 탭을 옮길 때마다 쌓이므로(옛 키가 다시 이긴다) 여기선 직접 채운다.
        // 언어 변경 때는 RefreshAll이 다시 불려 이 함수가 새 언어로 덮어쓴다.
        _hint.text = _activeTab == GuideTab
            ? CodeUI.L("ui_codex_hint_guide",
                "Q / E : 탭    ·    W S : 목록    ·    A D : 페이지    ·    Space : 크게 보기    ·    ESC : 닫기")
            : CodeUI.L("ui_codex_hint", "Q / E : 탭    ·    W A S D : 이동    ·    ESC : 닫기");
    }

    private static void ClearChildren(RectTransform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }
}
