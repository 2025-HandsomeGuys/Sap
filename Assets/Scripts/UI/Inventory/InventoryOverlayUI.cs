// @tags: inventory, ui, overlay, code-generated, underground, mineral, bag, equipment, slot

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지하 인벤토리 오버레이 — 왼쪽에 광물 가방, 오른쪽에 장비/아이템/유물 + 플레이어 프리뷰.
/// UI는 전부 코드로 생성한다(SettingsOverlayUI 패턴) — 씬/프리팹 세팅 없이 <see cref="Open"/>만으로 동작.
///
/// 창고 오버레이와 같은 구조지만 **가방으로만 동작**한다.
///  - 창고가 없으므로 물건을 옮길 곳이 없다
///  - **지하에서는 장비·유물을 벗을 수 없다**(기존 규칙 유지) → 장비 칸은 표시 전용
///  - 광물은 무게만 보여주고, 아이템은 좌클릭으로 사용
/// </summary>
public class InventoryOverlayUI : MonoBehaviour, CodeHeldItem.IHost
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static InventoryOverlayUI _instance;

    public static InventoryOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<InventoryOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("InventoryOverlayUI");
                    _instance = go.AddComponent<InventoryOverlayUI>();
                }
            }
            return _instance;
        }
    }

    /// <summary>인벤토리 오버레이가 열려 있는지 (UIStateManager의 전역 단축키 차단용).</summary>
    public static bool IsOpen => _instance != null && _instance._isOpen;

    /// <summary>ESC/Tab으로 닫힌 바로 그 프레임인지 — 같은 프레임의 중복 처리 방지.</summary>
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
        CodeHeldItem.Cancel();
        _confirm?.Hide();
        ClearToast();
        SetTrashHot(false);
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

    [Header("스탯 아이콘 (비우면 코드 생성 심볼)")]
    [SerializeField] private StatIconSet statIcons = new StatIconSet();

    [Header("플레이어 프리뷰")]
    [Tooltip("지정하면 실제 UiPlayer 프리팹을 실시간 렌더(장비·모션 반영). 비우면 아래 페이퍼돌 방식")]
    [SerializeField] private GameObject uiPlayerPreviewPrefab;
    [Tooltip("페이퍼돌 폴백 — 기본 모습 + 장비 외형 (프리팹 미지정 시에만 사용)")]
    [SerializeField] private PlayerPreviewSkin playerPreview = new PlayerPreviewSkin();
    [Tooltip("프리뷰가 최소한 확보할 가로 폭 (px)")]
    [SerializeField] private float minPreviewWidth = 300f;

    [Header("장비 칸 플레이스홀더 (빈 칸에 흐리게 표시)")]
    [SerializeField] private Sprite headPlaceholder;
    [SerializeField] private Sprite clothesPlaceholder;
    [SerializeField] private Sprite shoesPlaceholder;
    [SerializeField] private Sprite relicPlaceholder;

    [Header("소지금")]
    [Tooltip("화폐(동전) 아이콘. 비우면 코드로 그린 기본 금화를 쓴다.")]
    [SerializeField] private Sprite goldIconSprite;

    [Header("무게 게이지")]
    [Tooltip("게이지 바탕(홈) 스프라이트. 비우면 코드로 그린 라운드 박스를 쓴다.")]
    [SerializeField] private Sprite weightTrackSprite;
    [Tooltip("게이지 채움 스프라이트. 비우면 코드로 그린 라운드 박스를 쓴다.")]
    [SerializeField] private Sprite weightFillSprite;
    [Tooltip("버리기 미리보기(집은 만큼 어두워지는 구간) 스프라이트. 비우면 채움 스프라이트를 어둡게 재사용한다.")]
    [SerializeField] private Sprite weightPendingSprite;
    [Tooltip("과중 기준선 스프라이트. 비우면 단색 세로선을 그린다.")]
    [SerializeField] private Sprite encumbranceMarkSprite;
    [Tooltip("게이지 높이 (px). 스프라이트를 쓰면 대개 더 두껍게 잡는다.")]
    [SerializeField] private float weightBarHeight = 10f;
    [Tooltip("과중 기준선 폭 (px)")]
    [SerializeField] private float encumbranceMarkWidth = 3f;
    [Tooltip("과중 기준선이 게이지 위아래로 튀어나오는 길이 (px)")]
    [SerializeField] private float encumbranceMarkOverhang = 3f;
    [Tooltip("게이지 스프라이트의 Pixels Per Unit 배율. 0이면 UISkin의 값을 따른다. " +
             "값을 키우면 9-슬라이스 테두리가 얇아진다(= 원본 픽셀이 작게 찍힌다).")]
    [Range(0f, 8f)][SerializeField] private float weightSpritePixelsPerUnit = 0f;
    [Tooltip("게이지 채움·미리보기·기준선에 색을 입힐지. 끄면 스프라이트 원래 색 그대로 쓴다(과적 색 변화 없음).")]
    [SerializeField] private bool tintWeightSprites = true;

    [Header("쓰레기통")]
    [Tooltip("쓰레기통 아이콘 스프라이트. 비우면 코드로 그린 기본 아이콘을 쓴다.")]
    [SerializeField] private Sprite trashSprite;
    [Tooltip("쓰레기통 칸 전체 높이 (px)")]
    [SerializeField] private float trashHeight = 62f;
    [Tooltip("쓰레기통 아이콘 한 변 (px, 정사각형)")]
    [SerializeField] private float trashIconSize = 40f;

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
    // 1920x1080 기준 디자인 px. CanvasScaler(ScaleWithScreenSize, ref 1920x1080)가 해상도에 맞춰 비례 확대한다.
    // 폭은 내용(광물 격자 + 가방 슬롯·프리뷰)에 맞춰 너무 넓지 않게 — 격자는 CodeGridFill이 폭을 자동으로 채운다.
    [SerializeField] private float panelWidth = 1420f;
    // 가방 영역은 슬롯 3줄 고정 높이라, 늘어난 높이는 전부 아래 스탯 패널로 간다(스탯을 키우는 레버).
    [SerializeField] private float panelHeight = 960f;
    [Tooltip("광물 격자 한 줄에 놓을 칸 수")]
    [Range(2, 10)][SerializeField] private int mineralColumns = 5;
    [Tooltip("아이템 격자 한 줄에 놓을 칸 수")]
    [Range(1, 6)][SerializeField] private int itemColumns = 2;
    [SerializeField] private float slotSize = 86f;
    [SerializeField] private int sortingOrder = 30500;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 상수 / 구역
    // ===================================================
    private const float FadeDuration = 0.14f;
    private const float ToastDuration = 2.2f;

    private static class Zone
    {
        public const int Mineral = 0;
        public const int BagItem = 1;
        public const int BagEquipment = 2;
        /// <summary>유물 로드아웃 칸 (CodeBagPanel.Config.zoneRelic 기본값과 같은 값).</summary>
        public const int BagRelic = 90;
    }

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built, _isOpen, _opening, _needsRefresh;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    private TextMeshProUGUI _weightText, _hintText, _toastText, _goldText;
    private Image _weightFill;
    private RectTransform _weightMark;
    private Image _weightPending;      // 집은/끌고 있는 만큼 — 버리면 줄어들 구간을 어둡게
    private float _shownPendingWeight = -1f;
    private ScrollRect _mineralScroll;
    private RectTransform _mineralGrid;
    private readonly List<CodeSlotView> _mineralSlots = new List<CodeSlotView>();
    private CodeBagPanel _bag;
    private float _toastUntil;

    // WASD 키보드 커서 (마우스 호버와 같은 강조를 공유)
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();
    // 커서가 들를 버튼들 (상단 탭). 만들면서 순서대로 등록된다.
    private readonly List<CodeNavButton> _navButtons = new List<CodeNavButton>();

    // 상단 탭 (가방 | 퀘스트) — 본문을 통째로 스왑한다
    private enum TopTab { Storage, Quest }
    private TopTab _topTab = TopTab.Storage;
    private RectTransform _storageContent, _questContent;
    private CodeQuestView _questView;
    private CodeStatPanel _statPanel;
    private readonly List<(Button btn, TopTab tab, TextMeshProUGUI label)> _topTabs = new List<(Button, TopTab, TextMeshProUGUI)>();

    // 쓰레기통
    private Image _trashBox;
    private TextMeshProUGUI _trashHint;
    private Color _trashIdleBg, _trashHotBg;

    // 버리기 확인 팝업
    private CodeConfirmPopup _confirm;

    private ItemInventory _itemInv;
    private MineralInventory _mineralInv;
    private EquipmentInventory _equipInv;
    private PlayerStat _playerStat;

    private bool _subscribed, _langSubscribed;

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

        CodeHeldItem.Cancel();
        // 다음에 열 때 지난 팝업·토스트가 남아 있으면 안 된다
        _confirm?.Hide();
        ClearToast();
        SetTrashHot(false);

        _nav.End();
        Unsubscribe();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        // ESC 등으로 스스로 닫혔다면 UIStateManager 상태도 되돌린다.
        // 이 시점엔 이미 _isOpen=false라, SetState가 CloseStatic을 다시 불러도 즉시 반환된다(재귀 없음).
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Inventory) ui.SetState(UIState.None);
    }

    private IEnumerator OpenRoutine()
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        ResolveRefs();

        // 지난번에 띄운 팝업·토스트가 남아 있으면 지우고 시작한다
        _confirm?.Hide();
        ClearToast();
        SetTrashHot(false);

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
        _topTab = TopTab.Storage; // 열 때는 항상 가방 탭부터
        _loc.Refresh();
        RefreshAll();
        ApplyTopTab();
        _nav.Begin();

        _canvasObj.SetActive(true);
        if (_mineralScroll != null) _mineralScroll.verticalNormalizedPosition = 1f;

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

        // 집은 개수는 휠로 매 프레임 바뀐다 — 무게 미리보기는 값이 달라질 때만 다시 그린다.
        if (!Mathf.Approximately(PendingMineralWeight(), _shownPendingWeight)) RefreshWeight();

        // 확인 팝업이 떠 있는 동안엔 팝업 조작만 받는다 — Space·엔터=확인 / ESC·Tab=취소.
        // (인벤토리 단축키까지 함께 처리하면 실수로 인벤토리째 닫힌다.)
        if (_confirm != null && _confirm.IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                _confirm.ConfirmNow();
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab))
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CodeUI.PlayBack();
                _confirm?.Hide();
            }
            return;
        }

        // 손에 든 동안엔 다른 단축키를 막는다 — ESC/Tab은 닫기가 아니라 '손 비우기'로 처리한다.
        if (CodeHeldItem.Active)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { CodeUI.PlayBack(); CodeHeldItem.Cancel(); SetTrashHot(false); }
            else if (Input.GetKeyDown(KeyCode.Tab)) { CodeHeldItem.Cancel(); SetTrashHot(false); }
            return;
        }

        if (closeOnEscape && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab)))
        {
            // ESC는 뒤로가기음, Tab은 토글이라 평소 클릭음 그대로.
            if (Input.GetKeyDown(KeyCode.Escape)) CodeUI.PlayBack();
            Close();
            return;
        }

        // Q / E — 퀘스트 탭에서 메인/서브 전환 (가방 탭에선 분류가 없어 무시)
        if (Input.GetKeyDown(KeyCode.Q)) StepActiveTab(-1);
        else if (Input.GetKeyDown(KeyCode.E)) StepActiveTab(+1);

        // WASD = 칸·버튼 이동 / 스페이스 = 실행 (퀘스트 탭에서도 상단 탭은 커서로 돌아다닐 수 있어야 한다)
        _nav.Update();
    }

    private void LateUpdate()
    {
        if (!_isOpen) return;

        if (_toastUntil > 0f && Time.unscaledTime >= _toastUntil) ClearToast();

        if (_needsRefresh)
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
        // FindObjectsInactive.Include 필수 —
        // EquipmentInventory는 옛 인벤토리 UI의 EquipmentsPanel에 붙어 있는데,
        // 코드 오버레이를 쓰면 그 패널이 꺼진 채로 남아 기본 탐색으로는 찾히지 않는다.
        if (_itemInv == null) _itemInv = FindFirstObjectByType<ItemInventory>(FindObjectsInactive.Include);
        if (_mineralInv == null) _mineralInv = FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
        if (_equipInv == null) _equipInv = FindFirstObjectByType<EquipmentInventory>(FindObjectsInactive.Include);
        if (_playerStat == null) _playerStat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;

        if (_itemInv != null) _itemInv.OnInventoryChanged += MarkDirty;
        if (_mineralInv != null) _mineralInv.OnInventoryChanged += MarkDirty;
        if (_equipInv != null) _equipInv.OnInventoryChanged += MarkDirty;
        if (_playerStat != null)
        {
            _playerStat.OnStatChanged += MarkDirty;      // 업그레이드·장비 강화 → 스탯 패널 갱신
            _playerStat.OnGoldChanged += OnGoldChanged;  // 소지금 표시 갱신 (OnStatChanged와 별도 이벤트)
        }

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

        if (_itemInv != null) _itemInv.OnInventoryChanged -= MarkDirty;
        if (_mineralInv != null) _mineralInv.OnInventoryChanged -= MarkDirty;
        if (_equipInv != null) _equipInv.OnInventoryChanged -= MarkDirty;
        if (_playerStat != null)
        {
            _playerStat.OnStatChanged -= MarkDirty;
            _playerStat.OnGoldChanged -= OnGoldChanged;
        }
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

        _canvasObj = new GameObject("InventoryOverlayCanvas");
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
        // 닫기 버튼은 두지 않는다 — ESC / Tab 으로 닫는다.

        BuildTitleBar(panel.transform, side, titleH);

        // ── 본문 영역 — 가방 탭 ↔ 퀘스트 탭을 이 자리에서 통째로 스왑한다 ──
        var content = CodeUI.CreateRect(panel.transform, "ContentArea");
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = new Vector2(side, side + hintH + 6f);
        content.offsetMax = new Vector2(-side, -(side + titleH + 12f));

        // 가방 탭 본문 (광물 | 가방)
        _storageContent = CodeUI.CreateRect(content, "StorageContent");
        CodeUI.StretchFull(_storageContent);
        var bodyLayout = _storageContent.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 16f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = true;

        // 왼쪽은 [광물 목록 + 쓰레기통]을 세로로 쌓는다 — 무게를 보면서 바로 버릴 수 있게 붙여 둔다.
        var left = CodeUI.CreateColumn(_storageContent, "LeftColumn", 12f);
        left.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        BuildMineralCard(left, 1f);
        BuildTrashCard(left);
        BuildBagCard(_storageContent, 1f);

        // 퀘스트 탭 본문 (같은 자리에 겹쳐 두고 SetActive로 전환)
        _questContent = CodeUI.CreateRect(content, "QuestContent");
        CodeUI.StretchFull(_questContent);
        _questView = new CodeQuestView(_questContent, new CodeQuestView.Config
        {
            skin = skin,
            diamondGlyph = diamondGlyph,
            clickSfxName = clickSfxName
        }, _loc);
        _questContent.gameObject.SetActive(false);

        // 조작 안내 + 토스트 (같은 자리를 번갈아 쓴다)
        _hintText = CodeUI.CreateText(panel.transform, "Hint", 17f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        var hintRt = _hintText.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.offsetMin = new Vector2(side, side);
        hintRt.offsetMax = new Vector2(-side, side + hintH);
        // 힌트 문구는 상단 탭(가방/퀘스트)에 따라 UpdateHint()가 바꾼다 — 바인딩하지 않고 폰트만 추적한다.

        // 토스트 — 힌트와 같은 자리. 겹치지 않게 띄울 때 힌트를 숨긴다.
        _toastText = CodeUI.CreateText(panel.transform, "Toast", 18f, FontStyles.Bold,
            Color.white, TextAlignmentOptions.Center, _loc);
        var toastRt = _toastText.rectTransform;
        toastRt.anchorMin = hintRt.anchorMin;
        toastRt.anchorMax = hintRt.anchorMax;
        toastRt.pivot = hintRt.pivot;
        toastRt.offsetMin = hintRt.offsetMin;
        toastRt.offsetMax = hintRt.offsetMax;
        _toastText.gameObject.SetActive(false);

        _confirm = new CodeConfirmPopup(_canvasObj.transform, skin, _loc,
            "ui_inv_drop", "버리기", () => CodeUI.PlaySfx(clickSfxName));

        // WASD 커서 — 광물 격자와 가방 칸을 하나의 판으로 보고 좌표로 이동한다.
        _nav.collect = CollectNavSlots;
        _nav.onActivate = OnNavActivate;
        _nav.moveSfxName = null;

        _canvasObj.SetActive(false);
    }

    /// <summary>WASD 이동 후보 — 광물 칸 + 가방 칸 + 상단 탭 버튼 + (퀘스트 탭일 때) 퀘스트 내부 탭.</summary>
    private void CollectNavSlots(List<ICodeNavItem> into)
    {
        foreach (var view in _mineralSlots)
            if (view != null && view.gameObject.activeInHierarchy) into.Add(view);

        _bag?.CollectSlots(into);

        foreach (var nav in _navButtons)
            if (nav != null && nav.NavUsable) into.Add(nav);

        // 퀘스트 탭 [메인][서브 I][서브 II] — Q/E 말고 WASD·Space로도 넘길 수 있게
        _questView?.CollectNavItems(into);
    }

    /// <summary>
    /// 스페이스바. 버튼이면 그 버튼을 누르고, 칸이면 — 지하 가방은 표시 전용이라
    /// (장비·유물을 벗을 수 없다는 기존 규칙) 장비·유물 칸에서는 왜 안 되는지만 알려 준다.
    /// </summary>
    private void OnNavActivate(ICodeNavItem item)
    {
        if (item is CodeSlotView view)
        {
            if (view.zone == Zone.BagEquipment || view.zone == Zone.BagRelic)
                Toast("ui_inv_equip_locked", "지하에서는 장비·유물을 벗을 수 없습니다. 지상 창고에서 바꾸세요.", CodeUI.MutedColor);
            return;
        }

        item?.NavActivate();
    }

    // ===================================================
    // 손에 집기 (마우스 클릭·휠) — CodeHeldItem
    // ===================================================
    // 지하엔 옮길 창고가 없다. 손에 집기는 '개수 조절 → 쓰레기통 부분 버리기'와 원위치 반납에만 쓰인다.
    //  · 빈 손 + 광물/아이템 칸 → 스택을 손에 집는다.
    //  · 손에 든 상태 → 좌클릭=집은 자리에 되돌리기(다른 칸은 무효) / 우클릭=1개 되돌리기 / 휠=개수 조절.
    private void OnInvSlotLeftClick(CodeSlotView view)
    {
        if (CodeHeldItem.Active) { CodeHeldItem.PlaceAll(view); return; }
        if (!view.HasItem) return;
        CodeHeldItem.PickUp(view, this);
    }

    private void OnInvSlotRightClick(CodeSlotView view)
    {
        if (CodeHeldItem.Active) { CodeHeldItem.PlaceOne(view); return; }
        if (!view.HasItem) return;
        CodeHeldItem.PickUp(view, this);
    }

    // ── CodeHeldItem.IHost ──
    bool CodeHeldItem.IHost.HeldHostOpen => _isOpen;
    ScrollRect CodeHeldItem.IHost.HeldScrollRect => _mineralScroll;
    void CodeHeldItem.IHost.HeldRefresh() => _needsRefresh = true;
    void CodeHeldItem.IHost.HeldPlaySfx() => CodeUI.PlaySfx(clickSfxName);

    int CodeHeldItem.IHost.HeldLiveQuantity(CodeSlotView source)
    {
        var slot = HeldSourceSlot(source);
        return slot?.item != null ? slot.quantity : 0;
    }

    string CodeHeldItem.IHost.HeldLiveItemId(CodeSlotView source)
    {
        var slot = HeldSourceSlot(source);
        return slot?.item?.Id;
    }

    private InventorySlot HeldSourceSlot(CodeSlotView source)
    {
        if (source == null) return null;
        switch (source.zone)
        {
            case Zone.Mineral: return At(_mineralInv != null ? _mineralInv.ReadonlyItems : null, source.index);
            case Zone.BagItem: return At(_itemInv != null ? _itemInv.ReadonlyItems : null, source.index);
            default: return null;
        }
    }

    private static InventorySlot At(IReadOnlyList<InventorySlot> list, int index) =>
        (list != null && index >= 0 && index < list.Count) ? list[index] : null;

    /// <summary>지하는 옮길 곳이 없다 — 어떤 칸으로도 이동하지 않는다(집은 자리 반납·쓰레기통 버리기만 유효).</summary>
    int CodeHeldItem.IHost.HeldCommit(CodeSlotView source, CodeSlotView target, int amount) => 0;

    void CodeHeldItem.IHost.HeldTrash(CodeSlotView source, int amount) => TrashSource(source, amount);

    bool CodeHeldItem.IHost.HeldTrashOne(CodeSlotView source)
    {
        if (source == null) return false;
        switch (source.zone)
        {
            case Zone.Mineral:
                return DiscardOne(_mineralInv != null ? _mineralInv.ReadonlyItems : null, source.index,
                    (idx, qty) => _mineralInv.RemoveItemAt(idx, qty));

            case Zone.BagItem:
                return DiscardOne(_itemInv != null ? _itemInv.ReadonlyItems : null, source.index,
                    (idx, qty) => _itemInv.RemoveItemAt(idx, qty));

            default:
                TrashSource(source, 1); // 못 버리는 구역은 기존 안내 토스트를 그대로 태운다
                return false;
        }
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

        var diamond = CodeUI.CreateText(bar, "Diamond", 22f, FontStyles.Normal, CodeUI.GoldColor,
            TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;

        // 상단 탭 — [가방] [퀘스트]. 누르면 본문이 통째로 바뀐다.
        AddTopTab(bar, TopTab.Storage, "ui_wh_bag", "가방");
        AddTopTab(bar, TopTab.Quest, "ui_wh_quest", "퀘스트");

        CodeUI.CreateSpacer(bar);

        // 소지금 — 화폐 이미지 + 숫자만 (창고 오버레이와 동일)
        _goldText = CodeUI.CreateGoldBox(bar, goldIconSprite, _loc, skin);
    }

    /// <summary>상단 탭(가방/퀘스트) 버튼 하나. 탭 스킨을 따르며 _topTabs에 등록된다.</summary>
    private void AddTopTab(Transform parent, TopTab tab, string key, string fallback)
    {
        var btn = CodeUI.CreateButton(parent, "TopTab_" + tab, CodeUI.TabIdleBg, () => SwitchTopTab(tab), skin.tabSprite, skin);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 160f;
        le.minWidth = 120f;
        le.preferredHeight = 48f;

        Color accent = tab == TopTab.Quest ? CodeUI.QuestColor : Color.white;
        var label = CodeUI.CreateText(btn.transform, "Text", 23f, FontStyles.Bold, accent, TextAlignmentOptions.Center, _loc);
        label.characterSpacing = 4f;
        CodeUI.StretchFull(label.rectTransform);
        _loc.Bind(label, key, fallback);

        _topTabs.Add((btn, tab, label));
        _navButtons.Add(CodeNavButton.Attach(btn, skin));
    }

    private void SwitchTopTab(TopTab tab)
    {
        if (_topTab == tab) return;
        CodeUI.PlaySfx(clickSfxName);
        _topTab = tab;
        ApplyTopTab();
    }

    /// <summary>현재 _topTab에 맞춰 본문 스왑·탭 하이라이트·힌트를 반영하고 해당 탭을 갱신한다.</summary>
    private void ApplyTopTab()
    {
        bool storage = _topTab == TopTab.Storage;
        if (_storageContent != null) _storageContent.gameObject.SetActive(storage);
        if (_questContent != null) _questContent.gameObject.SetActive(!storage);

        UpdateTopTabVisuals();
        UpdateHint();

        if (storage)
        {
            RefreshMinerals();
            RefreshWeight();
            if (_mineralScroll != null) _mineralScroll.verticalNormalizedPosition = 1f;
        }
        else
        {
            // 커서는 걷지 않는다 — 상단 탭은 살아 있어 W/S로 가방 탭에 돌아올 수 있다.
            // (가방 본문 칸에 있던 커서는 Refresh가 가장 가까운 살아 있는 항목으로 옮긴다)
            _nav.Refresh();
            _questView?.Refresh();
        }
    }

    private void UpdateTopTabVisuals()
    {
        foreach (var (btn, tab, label) in _topTabs)
        {
            bool selected = tab == _topTab;
            Sprite sprite = selected
                ? (skin.tabSelectedSprite != null ? skin.tabSelectedSprite : skin.tabSprite)
                : skin.tabSprite;
            CodeUI.ApplySkin(btn.image, selected ? CodeUI.AccentFill : CodeUI.TabIdleBg, sprite, skin);

            Color baseCol = tab == TopTab.Quest ? CodeUI.QuestColor : Color.white;
            label.color = selected ? Color.white : new Color(baseCol.r, baseCol.g, baseCol.b, 0.7f);
        }
    }

    /// <summary>Q/E — 퀘스트 탭에서만 메인/서브 탭을 넘긴다(가방 탭은 분류가 없다).</summary>
    private void StepActiveTab(int dir)
    {
        if (_topTab == TopTab.Quest) _questView?.StepTab(dir);
    }

    /// <summary>하단 힌트 문구를 현재 상단 탭에 맞게 바꾼다.</summary>
    private void UpdateHint()
    {
        if (_hintText == null) return;
        // 토스트가 떠 있는 동안엔 힌트를 건드리지 않는다(같은 자리라 겹친다).
        if (_toastText != null && _toastText.gameObject.activeSelf) return;

        _hintText.text = _topTab == TopTab.Quest
            ? CodeUI.L("ui_topquest_hint", "WASD : 이동 / Space : 선택   ·   Q / E : 퀘스트 탭 전환   ·   ESC / Tab : 닫기")
            : CodeUI.L("ui_inv_hint", "좌클릭 : 손에 집기   ·   우클릭 : 1개 되돌리기   ·   휠 : 개수 조절   ·   쓰레기통 : 버리기   ·   WASD : 이동   ·   ESC / Tab : 닫기");
    }

    private void BuildMineralCard(Transform parent, float flexibleWidth)
    {
        var card = CodeUI.CreateImage(parent, "MineralCard", CodeUI.CardBg, skin.cardSprite, skin);
        var cardLe = card.gameObject.AddComponent<LayoutElement>();
        cardLe.flexibleWidth = flexibleWidth;
        cardLe.flexibleHeight = 1f; // 세로 컬럼에서 쓰레기통을 뺀 나머지를 전부 차지
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CodeUI.CreateRow(card.transform, "Header", 34f, 8f);
        var dot = CodeUI.CreateText(header, "Dot", 16f, FontStyles.Normal, CodeUI.MineralColor,
            TextAlignmentOptions.Center, _loc);
        dot.text = diamondGlyph;
        var label = CodeUI.CreateText(header, "Label", 23f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(label, "ui_inv_mineral", "광물");
        CodeUI.CreateSpacer(header);
        _weightText = CodeUI.CreateText(header, "Weight", 19f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineRight, _loc);
        _weightText.gameObject.AddComponent<LayoutElement>().preferredWidth = 160f;

        CodeUI.CreateDivider(card.transform);

        // 무게 게이지
        var track = CodeUI.CreateImage(card.transform, "WeightTrack", CodeUI.BoxBg);
        ApplyBarSprite(track, weightTrackSprite, CodeUI.BoxBg, tint: true);
        track.gameObject.AddComponent<LayoutElement>().preferredHeight = Mathf.Max(2f, weightBarHeight);
        _weightFill = CodeUI.CreateImage(track.transform, "Fill", CodeUI.AccentFill);
        ApplyBarSprite(_weightFill, weightFillSprite, CodeUI.AccentFill, tintWeightSprites);
        var fillRt = _weightFill.rectTransform;
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        // 버리기 미리보기 — 손에 집었거나 끌고 있는 만큼(=버리면 줄어들 몫)을 채움 바 끝에서 어둡게 덮는다
        var pendingColor = new Color(0f, 0f, 0f, 0.55f);
        _weightPending = CodeUI.CreateImage(track.transform, "PendingFill", pendingColor);
        // 전용 스프라이트가 없으면 채움 스프라이트를 그대로 쓰고 색으로만 어둡게 덮는다.
        ApplyBarSprite(_weightPending,
            weightPendingSprite != null ? weightPendingSprite : weightFillSprite,
            pendingColor, tint: true);
        _weightPending.raycastTarget = false;
        var pendRt = _weightPending.rectTransform;
        pendRt.anchorMin = new Vector2(0f, 0f);
        pendRt.anchorMax = new Vector2(0f, 1f);
        pendRt.pivot = new Vector2(0f, 0.5f);
        pendRt.offsetMin = Vector2.zero;
        pendRt.offsetMax = Vector2.zero;
        _weightPending.gameObject.SetActive(false);

        // 과중 기준선 — 이 선을 넘으면 과적(EncumbranceRatio)
        var mark = CodeUI.CreateImage(track.transform, "EncumbranceMark", CodeUI.WarnColor);
        ApplyBarSprite(mark, encumbranceMarkSprite, CodeUI.WarnColor, tintWeightSprites, rounded: false);
        mark.raycastTarget = false;
        _weightMark = mark.rectTransform;
        _weightMark.pivot = new Vector2(0.5f, 0.5f);
        _weightMark.anchorMin = new Vector2(MineralInventory.EncumbranceRatio, 0f);
        _weightMark.anchorMax = new Vector2(MineralInventory.EncumbranceRatio, 1f);
        float halfW = Mathf.Max(0.5f, encumbranceMarkWidth) * 0.5f;
        _weightMark.offsetMin = new Vector2(-halfW, -encumbranceMarkOverhang);
        _weightMark.offsetMax = new Vector2(halfW, encumbranceMarkOverhang);

        _mineralScroll = CodeUI.CreateScrollView(card.transform, "MineralScroll", out _mineralGrid);
        var scrollLe = _mineralScroll.gameObject.AddComponent<LayoutElement>();
        scrollLe.flexibleHeight = 1f;
        scrollLe.minHeight = slotSize + 20f;

        var grid = _mineralGrid.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(slotSize, slotSize);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(2, 2, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, mineralColumns);
        // 카드 폭에 맞춰 칸 수·크기를 자동으로 채운다 — 넓어져도 오른쪽이 남지 않는다(해상도 변화도 대응).
        var mineralFill = _mineralGrid.gameObject.AddComponent<CodeGridFill>();
        mineralFill.targetCell = slotSize;
        mineralFill.minColumns = mineralColumns;
    }

    /// <summary>
    /// 쓰레기통 — 광물·아이템 칸을 여기로 끌어다 놓으면 버린다.
    /// 지하에서 무게가 꽉 찼을 때 값싼 광물을 덜어내는 용도라 광물 목록 바로 아래에 둔다.
    /// </summary>
    private void BuildTrashCard(Transform parent)
    {
        _trashIdleBg = CodeUI.CardBg;
        _trashHotBg = new Color(CodeUI.NegativeColor.r * 0.42f, CodeUI.NegativeColor.g * 0.24f,
                                CodeUI.NegativeColor.b * 0.26f, 1f);

        _trashBox = CodeUI.CreateImage(parent, "TrashCard", _trashIdleBg, skin.cardSprite, skin);
        var boxLe = _trashBox.gameObject.AddComponent<LayoutElement>();
        boxLe.preferredHeight = trashHeight;
        boxLe.minHeight = trashHeight;
        boxLe.flexibleHeight = 0f; // 광물 카드만 남는 세로 공간을 먹고, 쓰레기통은 딱 이 높이만 쓴다

        var row = _trashBox.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 16, 8, 8);
        row.spacing = 12f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false; // 자식은 제 크기만 — 아이콘이 카드 높이만큼 늘어나 찌그러지지 않게
        row.childAlignment = TextAnchor.MiddleLeft;

        // ── 아이콘: 정사각형 고정 (카드 높이가 바뀌어도 비율 유지) ──
        var iconBox = CodeUI.CreateRect(_trashBox.transform, "IconBox");
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = trashIconSize;
        iconLe.preferredHeight = iconLe.minHeight = trashIconSize;

        if (trashSprite != null)
        {
            var icon = CodeUI.CreateImage(iconBox, "Icon", Color.white, rounded: false);
            icon.sprite = trashSprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            CodeUI.StretchFull(icon.rectTransform);
        }
        else
        {
            BuildDrawnTrashIcon(iconBox);
        }

        // ── 문구: 제목 한 줄 + 짧은 안내 한 줄 (자세한 조작은 하단 힌트가 담당) ──
        var texts = CodeUI.CreateColumn(_trashBox.transform, "Texts", 1f);
        texts.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var title = CodeUI.CreateText(texts, "Label", 19f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, _loc);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        _loc.Bind(title, "ui_inv_trash", "쓰레기통");

        _trashHint = CodeUI.CreateText(texts, "Hint", 14f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.MidlineLeft, _loc);
        _trashHint.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
        _loc.Bind(_trashHint, "ui_inv_trash_hint", "여기로 끌어다 놓으면 버립니다. 집은 채로 우클릭하면 하나씩 버립니다.");

        var zone = CodeDropZone.Attach(_trashBox.gameObject, OnTrashDrop);
        zone.onHoverChanged = SetTrashHot;
        zone.onHeldClick = () => CodeHeldItem.TrashHeld();      // 손에 든 채 좌클릭 = 든 것 전부 버리기
        zone.onHeldRightClick = () => CodeHeldItem.TrashOne();  // 우클릭 = 하나씩 버리기
    }

    /// <summary>
    /// 스프라이트를 안 넣었을 때 쓰는 코드 생성 쓰레기통 아이콘 (뚜껑 + 통 + 세로 줄).
    /// 부모(iconBox)가 정사각형이라 아래 [0,1] 앵커 비율이 그대로 유지된다.
    /// </summary>
    private void BuildDrawnTrashIcon(Transform parent)
    {
        var root = CodeUI.CreateRect(parent, "DrawnTrash");
        CodeUI.StretchFull(root);

        Image Part(Transform p, string name, Vector2 aMin, Vector2 aMax, Color color, bool rounded = true)
        {
            var img = CodeUI.CreateImage(p, name, color, rounded: rounded);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return img;
        }

        Color shell = CodeUI.LabelColor;
        Color inner = CodeUI.BoxBg;

        // 손잡이 · 뚜껑 (가로로 넓게) · 통 (약간만 세로로 긴 사각형)
        Part(root, "Handle", new Vector2(0.40f, 0.82f), new Vector2(0.60f, 0.90f), shell);
        Part(root, "Lid",    new Vector2(0.16f, 0.70f), new Vector2(0.84f, 0.80f), shell);

        var body = Part(root, "Body", new Vector2(0.24f, 0.14f), new Vector2(0.76f, 0.68f), shell);

        // 통 안쪽을 어둡게 파서 '비어 있는 통'처럼 보이게 한다
        var hollow = Part(body.transform, "Hollow", Vector2.zero, Vector2.one, inner);
        hollow.rectTransform.offsetMin = new Vector2(3f, 3f);
        hollow.rectTransform.offsetMax = new Vector2(-3f, -3f);

        // 세로 줄 3개
        for (int i = 0; i < 3; i++)
        {
            float cx = 0.30f + i * 0.20f;
            Part(hollow.transform, $"Stripe{i}",
                new Vector2(cx - 0.045f, 0.18f), new Vector2(cx + 0.045f, 0.82f), shell, rounded: false);
        }
    }

    /// <summary>드래그가 쓰레기통 위에 올라왔을 때 붉게 — '놓으면 버려진다'를 알린다.</summary>
    private void SetTrashHot(bool hot)
    {
        if (_trashBox == null) return;

        // 드래그 중이거나 손에 집은 상태에서 쓰레기통 위에 올리면 붉게 — '놓으면 버려진다'.
        bool on = hot && (CodeSlotView.DragSource != null || CodeHeldItem.Active);

        CodeUI.ApplySkin(_trashBox, on ? _trashHotBg : _trashIdleBg, skin.cardSprite, skin);
        if (_trashHint != null) _trashHint.color = on ? CodeUI.NegativeColor : CodeUI.MutedColor;
    }

    private void BuildBagCard(Transform parent, float flexibleWidth)
    {
        var card = CodeUI.CreateImage(parent, "BagCard", CodeUI.CardBg, skin.cardSprite, skin);
        card.gameObject.AddComponent<LayoutElement>().flexibleWidth = flexibleWidth;
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CodeUI.CreateRow(card.transform, "Header", 34f);
        var label = CodeUI.CreateText(header, "Label", 23f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(label, "ui_inv_gear", "장비 · 아이템");
        CodeUI.CreateSpacer(header);

        CodeUI.CreateDivider(card.transform);

        // 장비·유물은 지하에서 벗을 수 없으므로 표시 전용으로 만든다
        _bag = new CodeBagPanel(card.transform, new CodeBagPanel.Config
        {
            skin = skin,
            preview = playerPreview,
            uiPlayerPreviewPrefab = uiPlayerPreviewPrefab,
            slotSize = slotSize,
            itemColumns = itemColumns,
            diamondGlyph = diamondGlyph,
            zoneItem = Zone.BagItem,
            zoneEquipment = Zone.BagEquipment,
            zoneRelic = Zone.BagRelic,
            // 지하에서도 아이템 칸은 '손에 집기'로 만질 수 있다 — 클릭은 사용(소모)이 아니라 집기라 안전하다.
            // (옛날엔 클릭=UseItemAt이 드래그와 얽혀 아이템이 소모됐다. 지금은 집기→쓰레기통 부분 버리기 용도.)
            // 장비·유물은 지하에서 벗을 수 없으므로 표시 전용으로 둔다.
            equipmentInteractable = false,
            itemInteractable = true,
            itemDraggable = true,
            // 유물 칸은 RelicManager 로드아웃을 그린다. 지하에서는 교체 불가(표시 전용).
            useRelicLoadout = true,
            relicInteractable = false,
            headPlaceholder = headPlaceholder,
            clothesPlaceholder = clothesPlaceholder,
            shoesPlaceholder = shoesPlaceholder,
            relicPlaceholder = relicPlaceholder,
            itemPlaceholder = relicPlaceholder, // 아이템 빈 칸도 유물 칸과 같은 플레이스홀더로
            minPreviewWidth = minPreviewWidth
        }, _loc);

        // 아이템 칸만 손에 집기로 연결한다(장비·유물은 표시 전용이라 콜백이 무시된다).
        // onDropReceived는 지하에 옮길 곳이 없어 no-op — 드래그로 아이템을 쓰레기통에 버리는 건 CodeDropZone이 담당.
        _bag.onLeftClick = OnInvSlotLeftClick;
        _bag.onRightClick = OnInvSlotRightClick;
        _bag.onDropReceived = (t, s) => { };

        // ── 플레이어 스탯 — 가방 아래 남는 세로 공간을 채운다(창고 오버레이와 동일 배치) ──
        var statHeader = CodeUI.CreateRow(card.transform, "StatHeader", 28f, 8f);
        var statDot = CodeUI.CreateText(statHeader, "Dot", 15f, FontStyles.Normal, CodeUI.AccentFill,
            TextAlignmentOptions.Center, _loc);
        statDot.text = diamondGlyph;
        var statLabel = CodeUI.CreateText(statHeader, "Label", 19f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(statLabel, "ui_stat_title", "스탯");
        CodeUI.CreateSpacer(statHeader);

        _statPanel = new CodeStatPanel(card.transform, new CodeStatPanel.Config
        {
            skin = skin,
            diamondGlyph = diamondGlyph,
            icons = statIcons
        }, _loc);
        var statLe = _statPanel.Root.gameObject.AddComponent<LayoutElement>();
        statLe.flexibleHeight = 1f;
        statLe.minHeight = 160f;
    }

    // ===================================================
    // 버리기 (쓰레기통)
    // ===================================================
    private void OnTrashDrop(CodeSlotView source)
    {
        SetTrashHot(false);
        TrashSource(source, -1);
    }

    /// <summary>source 칸을 버린다. amount &lt; 0 이면 슬롯 전량, 아니면 그만큼(손에 집기 부분 버리기).</summary>
    private void TrashSource(CodeSlotView source, int amount)
    {
        if (source == null) return;

        switch (source.zone)
        {
            case Zone.Mineral:
                AskDiscard(_mineralInv != null ? _mineralInv.ReadonlyItems : null, source.index,
                    (idx, qty) => _mineralInv.RemoveItemAt(idx, qty), amount);
                break;

            case Zone.BagItem:
                AskDiscard(_itemInv != null ? _itemInv.ReadonlyItems : null, source.index,
                    (idx, qty) => _itemInv.RemoveItemAt(idx, qty), amount);
                break;

            case Zone.BagEquipment:
                Toast("ui_inv_trash_equip_locked", "지하에서는 장비·유물을 벗거나 버릴 수 없습니다.", CodeUI.NegativeColor);
                break;
        }
    }

    /// <summary>
    /// 칸에서 1개만 즉시 지운다(확인 팝업 없음). 우클릭 연타로 하나씩 버리는 경로 전용 —
    /// 한 개마다 팝업을 띄우면 쓸 수 없는 조작이 된다.
    /// </summary>
    private bool DiscardOne(IReadOnlyList<InventorySlot> list, int index, System.Func<int, int, bool> remove)
    {
        if (list == null || index < 0 || index >= list.Count) return false;
        var slot = list[index];
        if (slot == null || slot.item == null) return false;

        string displayName = slot.item.DisplayName;
        if (!remove(index, 1)) return false;

        CodeUI.PlaySfx(clickSfxName);
        ToastRaw(string.Format(CodeUI.L("ui_inv_trash_done", "{0}을(를) 버렸습니다."), displayName),
            CodeUI.WarnColor);
        _needsRefresh = true;
        return true;
    }

    /// <summary>
    /// 슬롯 하나를 통째로 버릴지 묻는다. 확인 후에도 슬롯이 그대로인지 다시 검사한 뒤 지운다
    /// (팝업이 떠 있는 동안 인벤토리가 바뀌면 엉뚱한 칸을 지우게 되므로).
    /// </summary>
    private void AskDiscard(IReadOnlyList<InventorySlot> list, int index, System.Func<int, int, bool> remove,
        int amountOverride = -1)
    {
        if (list == null || index < 0 || index >= list.Count) return;

        var slot = list[index];
        if (slot == null || slot.item == null) return;

        string itemId = slot.item.Id;
        string displayName = slot.item.DisplayName;
        int quantity = amountOverride > 0 ? Mathf.Min(amountOverride, slot.quantity) : slot.quantity;

        string amount = quantity > 1
            ? $"{displayName} x{quantity}"
            : displayName;

        _confirm.Show(
            CodeUI.L("ui_inv_trash_confirm_title", "버리기"),
            string.Format(CodeUI.L("ui_inv_trash_confirm_msg", "{0}을(를) 버립니다.\n버린 물건은 되돌릴 수 없습니다."), amount),
            () =>
            {
                if (index >= list.Count) return;
                var now = list[index];
                if (now == null || now.item == null || now.item.Id != itemId) return;

                if (remove(index, Mathf.Min(quantity, now.quantity)))
                {
                    CodeUI.PlaySfx(clickSfxName);
                    ToastRaw(string.Format(CodeUI.L("ui_inv_trash_done", "{0}을(를) 버렸습니다."), amount),
                        CodeUI.WarnColor);
                    _needsRefresh = true;
                }
            });
    }

    // ===================================================
    // 토스트 (확인 팝업은 CodeConfirmPopup이 담당)
    // ===================================================
    private void Toast(string key, string fallback, Color color) => ToastRaw(CodeUI.L(key, fallback), color);

    /// <summary>이미 완성된 문장을 그대로 띄운다 (아이템 이름을 끼워 넣은 문구 등).</summary>
    private void ToastRaw(string text, Color color)
    {
        if (_toastText == null) return;
        _toastText.text = text;
        _toastText.color = color;
        _toastText.gameObject.SetActive(true);
        if (_hintText != null) _hintText.gameObject.SetActive(false); // 같은 자리 — 안 숨기면 글자가 겹쳐 안 읽힌다
        _toastUntil = Time.unscaledTime + ToastDuration;
    }

    private void ClearToast()
    {
        _toastUntil = 0f;
        if (_toastText != null) _toastText.gameObject.SetActive(false);
        if (_hintText != null) _hintText.gameObject.SetActive(true);
    }

    // ===================================================
    // 갱신
    // ===================================================
    private void RefreshAll()
    {
        RefreshMinerals();
        _bag?.Refresh(_itemInv, _equipInv);
        RefreshWeight();
        RefreshGold();
        _statPanel?.Refresh(_playerStat);
        UpdateHint();
        _nav.Refresh(); // 꺼진 칸을 물고 있으면 커서를 놓는다

        // 퀘스트 탭이 열려 있으면 함께 갱신(창고 광물 변화 → 요구 진행도 반영)
        if (_topTab == TopTab.Quest) _questView?.Refresh();
    }

    private void RefreshMinerals()
    {
        var list = _mineralInv != null ? _mineralInv.ReadonlyItems : null;
        int count = list != null ? list.Count : 0;

        while (_mineralSlots.Count < count)
        {
            var view = CodeSlotView.Create(_mineralGrid, skin, slotSize, $"Mineral_{_mineralSlots.Count}");
            view.zone = Zone.Mineral;
            // 좌/우클릭 = 손에 집기(CodeHeldItem). 지하엔 옮길 창고가 없으므로 개수 조절 → 쓰레기통 부분 버리기에 쓴다.
            // canDrag는 기본값(광물이 든 칸만 끌림) — 빈 칸 드래그는 CodeSlotView가 부모 ScrollRect로 넘겨 스크롤이 유지된다.
            view.onLeftClick = OnInvSlotLeftClick;
            view.onRightClick = OnInvSlotRightClick;
            _mineralSlots.Add(view);
        }

        for (int i = 0; i < _mineralSlots.Count; i++)
        {
            bool used = i < count;
            _mineralSlots[i].gameObject.SetActive(used);
            if (!used) continue;

            _mineralSlots[i].index = i;
            _mineralSlots[i].Bind(list[i], CodeUI.MineralColor);
        }
    }

    private void RefreshGold()
    {
        if (_goldText == null) return;
        // 화폐 이미지 + 숫자만 — 접미사 없음
        _goldText.text = CodeUI.Gold(_playerStat != null ? _playerStat.Gold : 0);
    }

    /// <summary>
    /// 게이지 조각 하나에 스프라이트를 입힌다. 스프라이트가 있으면 9-슬라이스(테두리가 있을 때)로,
    /// 없으면 예전처럼 코드 생성 라운드 박스로 떨어진다.
    /// tint=false면 색을 입히지 않아 스프라이트 원본 색이 그대로 나온다(과적 색 변화도 함께 꺼진다).
    /// </summary>
    private void ApplyBarSprite(Image img, Sprite sprite, Color color, bool tint, bool rounded = true)
    {
        if (img == null) return;

        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = (sprite.border.sqrMagnitude > 0.01f) ? Image.Type.Sliced : Image.Type.Simple;
            float ppu = weightSpritePixelsPerUnit > 0.001f
                ? weightSpritePixelsPerUnit
                : (skin != null ? skin.spritePixelsPerUnit : 1f);
            img.pixelsPerUnitMultiplier = Mathf.Max(0.01f, ppu);
            img.color = tint ? color : Color.white;
            return;
        }

        img.color = color;
        img.sprite = rounded ? CodeUI.Rounded(8, 0.5f) : null;
        img.type = rounded ? Image.Type.Sliced : Image.Type.Simple;
    }

    /// <summary>게이지 조각의 색만 갱신한다(스프라이트 원본 색을 쓰기로 했으면 건드리지 않는다).</summary>
    private void TintBar(Image img, Sprite sprite, Color color)
    {
        if (img == null) return;
        if (sprite != null && !tintWeightSprites) return;
        img.color = color;
    }

    /// <summary>
    /// 지금 손에 집었거나(CodeHeldItem) 끌고 있는(드래그) 광물의 무게.
    /// 데이터는 아직 안 빠진 상태라 게이지 '끝쪽 어두운 구간'으로만 보여준다 — 버리면 실제로 줄어들 몫.
    /// </summary>
    private float PendingMineralWeight()
    {
        var drag = CodeSlotView.DragSource;
        if (drag != null && drag.zone == Zone.Mineral && drag.HasItem)
            return drag.Slot.item.Weight * drag.Slot.quantity;

        var held = CodeHeldItem.HeldSource;
        if (held != null && held.zone == Zone.Mineral)
        {
            var slot = HeldSourceSlot(held);
            if (slot?.item != null)
                return slot.item.Weight * Mathf.Min(CodeHeldItem.HeldQuantity, slot.quantity);
        }
        return 0f;
    }

    private void RefreshWeight()
    {
        if (_weightText == null || _weightFill == null) return;

        float weight = _mineralInv != null ? _mineralInv.TotalWeight : 0f;
        float max = _mineralInv != null ? Mathf.Max(0.01f, _mineralInv.maxWeightLimit) : 1f;
        float ratio = Mathf.Clamp01(weight / max);

        if (_weightMark != null)
        {
            // 기준선은 실제 판정값(encumbranceThreshold / maxWeightLimit)을 따라간다
            float markRatio = _mineralInv != null
                ? Mathf.Clamp01(_mineralInv.encumbranceThreshold / max)
                : MineralInventory.EncumbranceRatio;
            _weightMark.anchorMin = new Vector2(markRatio, 0f);
            _weightMark.anchorMax = new Vector2(markRatio, 1f);
        }

        _weightText.text = $"{weight:F1} / {max:F1}";
        _weightFill.rectTransform.anchorMax = new Vector2(ratio, 1f);

        // 지금 집은/끌고 있는 광물의 무게 — 버리면 이만큼 줄어든다
        float pending = PendingMineralWeight();
        _shownPendingWeight = pending;
        if (_weightPending != null)
        {
            bool show = pending > 0.0001f;
            _weightPending.gameObject.SetActive(show);
            if (show)
            {
                float from = Mathf.Clamp01((weight - pending) / max);
                var prt = _weightPending.rectTransform;
                prt.anchorMin = new Vector2(from, 0f);
                prt.anchorMax = new Vector2(ratio, 1f);
                prt.offsetMin = Vector2.zero;
                prt.offsetMax = Vector2.zero;
            }
        }

        bool encumbered = _mineralInv != null && _mineralInv.IsEncumbered;
        Color bar = ratio >= 0.999f ? CodeUI.NegativeColor : (encumbered ? CodeUI.WarnColor : CodeUI.AccentFill);
        TintBar(_weightFill, weightFillSprite, bar);
        _weightText.color = bar;
    }

}
