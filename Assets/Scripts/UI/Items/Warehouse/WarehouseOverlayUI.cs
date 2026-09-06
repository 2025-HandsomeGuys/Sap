// @tags: warehouse, ui, overlay, code-generated, inventory, slot, drag, drop, tab, sort, ground

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 창고 오버레이 — 지상씬에서 창고와 가방을 나란히 놓고 물건을 주고받는 화면.
/// UI는 전부 코드로 생성한다(SettingsOverlayUI 패턴) — 씬/프리팹 세팅 없이 <see cref="Open"/> 호출만으로 동작.
/// 배경/버튼/슬롯 스프라이트를 바꾸고 싶으면 아무 씬에나 빈 오브젝트에 이 컴포넌트를 붙이고
/// 인스펙터의 <see cref="skin"/>에 도트 스프라이트를 넣으면 된다(비운 항목은 코드 생성 라운드로 폴백).
///
/// 데이터는 전부 기존 매니저에 위임한다:
///  - 창고: WarehouseManager (슬롯 목록·정렬·스왑)
///  - 가방: ItemInventory / MineralInventory / EquipmentInventory
///  - 골드: PlayerStat
///
/// 조작: 좌클릭 = 스택 전부 옮기기 / 우클릭 = 1개 / 드래그 = 자유 이동(창고 안에서는 자리 교환).
/// 창고 → 가방 이동 시 가방 칸이 이미 차 있으면 그 물건이 창고로 되돌아가며 서로 맞바꿔진다.
/// '가방 비우기'는 아이템·광물뿐 아니라 착용 중인 장비·유물까지 전부 창고로 보낸다.
/// </summary>
public class WarehouseOverlayUI : MonoBehaviour, CodeHeldItem.IHost
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static WarehouseOverlayUI _instance;

    /// <summary>씬에 미리 배치된 인스턴스가 있으면 그것을, 없으면 자동 생성. 에디터 배치는 선택 사항.</summary>
    public static WarehouseOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<WarehouseOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("WarehouseOverlayUI");
                    _instance = go.AddComponent<WarehouseOverlayUI>();
                }
            }
            return _instance;
        }
    }

    /// <summary>창고 오버레이가 열려 있는지 (UIStateManager의 전역 단축키 차단용).</summary>
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
        Time.timeScale = 1f; // 새 씬은 항상 정상 속도로 시작
        CodeHeldItem.Cancel();
        _nav.End();
        Unsubscribe();
        _warehouse?.StripRelicSlots(); // 편입한 유물 칸 정리
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

    [Header("장비 칸 플레이스홀더 (빈 칸에 흐리게 표시)")]
    [SerializeField] private Sprite headPlaceholder;
    [SerializeField] private Sprite clothesPlaceholder;
    [SerializeField] private Sprite shoesPlaceholder;
    [SerializeField] private Sprite relicPlaceholder;

    [Header("소지금")]
    [Tooltip("화폐(동전) 아이콘. 비우면 코드로 그린 기본 금화를 쓴다.")]
    [SerializeField] private Sprite goldIconSprite;

    [Header("배경 처리")]
    [Tooltip("배경을 덮는 검은 막의 알파 (0 = 안 어두움, 1 = 완전 검정)")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.5f;
    [Tooltip("열릴 때 뒷 화면을 캡처해 블러로 깔지 여부")]
    [SerializeField] private bool useBlurBackdrop = true;
    [Tooltip("블러 강도 — 캡처 화면을 절반씩 줄이는 횟수. 클수록 뭉개짐")]
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [Tooltip("블러 배경이 상하 반전되어 보일 때 체크")]
    [SerializeField] private bool flipBlurVertically = false;

    [Header("동작")]
    [Tooltip("열려 있는 동안 게임을 멈춘다(Time.timeScale = 0)")]
    [SerializeField] private bool pauseGameWhileOpen = true;
    [Tooltip("ESC / Tab 으로 닫기")]
    [SerializeField] private bool closeOnEscape = true;

    [Header("플레이어 프리뷰")]
    [Tooltip("지정하면 실제 UiPlayer 프리팹을 실시간 렌더(장비·모션 반영). 비우면 아래 페이퍼돌 방식")]
    [SerializeField] private GameObject uiPlayerPreviewPrefab;
    [Tooltip("페이퍼돌 폴백 — 기본 모습 + 장비 외형 (프리팹 미지정 시에만 사용)")]
    [SerializeField] private PlayerPreviewSkin playerPreview = new PlayerPreviewSkin();
    [Tooltip("프리뷰가 최소한 확보할 가로 폭 (px). 실제로는 칸 묶음을 뺀 남는 폭을 전부 쓴다")]
    [SerializeField] private float minPreviewWidth = 300f;

    [Header("레이아웃 (1920x1080 기준 px)")]
    // 1920x1080 기준 디자인 px. CanvasScaler(ScaleWithScreenSize, ref 1920x1080)가 해상도에 맞춰 비례 확대한다.
    // 폭은 내용(창고 격자 + 가방 슬롯·프리뷰)에 맞춰 너무 넓지 않게 — 격자는 CodeGridFill이 폭을 자동으로 채운다.
    [SerializeField] private float panelWidth = 1420f;
    // 가방 영역은 슬롯 3줄 고정 높이라, 늘어난 높이는 전부 아래 스탯 패널로 간다(스탯을 키우는 레버).
    [SerializeField] private float panelHeight = 960f;
    [Tooltip("창고 격자 한 줄에 놓을 칸 수")]
    [Range(3, 10)][SerializeField] private int warehouseColumns = 6;
    [Tooltip("가방 아이템 격자 한 줄에 놓을 칸 수 (실제 칸 수는 ItemInventory.maxSlotCount를 따른다)")]
    [Range(1, 6)][SerializeField] private int itemColumns = 2;
    [SerializeField] private float slotSize = 86f;
    [Tooltip("캔버스 정렬 순서 — 게임 UI(≈1000)보다 위, 설정(31000)보다 아래")]
    [SerializeField] private int sortingOrder = 30500;

    [Header("쓰레기통")]
    [Tooltip("쓰레기통 아이콘 스프라이트. 비우면 코드로 그린 기본 아이콘을 쓴다.")]
    [SerializeField] private Sprite trashSprite;
    [Tooltip("쓰레기통 칸 전체 높이 (px)")]
    [SerializeField] private float trashHeight = 62f;
    [Tooltip("쓰레기통 아이콘 한 변 (px, 정사각형)")]
    [SerializeField] private float trashIconSize = 40f;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;
    [SerializeField] private string moveSfxName = SfxKeys.UiClick;

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 상수 / 구역
    // ===================================================
    private const float FadeDuration = 0.14f;
    private const float ToastDuration = 2.2f;

    /// <summary>CodeSlotView.zone에 넣는 구역 번호.</summary>
    private static class Zone
    {
        public const int Warehouse = 0;
        public const int BagItem = 1;
        public const int BagEquipment = 2;
        // 광물은 가방에 두지 않는다 — 지하에서 캔 광물은 창고로 자동 이동하고,
        // 창고에서 가방으로 되꺼내는 흐름은 설계상 없다. 그 자리는 플레이어 스탯 패널이 쓴다.

        // (구) 미장착 유물 전용 zone은 제거됐다 — 유물도 이제 Zone.Warehouse 슬롯으로 그려진다.
        /// <summary>가방의 유물 장착 칸(로드아웃).</summary>
        public const int BagRelic = 4;
    }

    private enum Tab { All, Minerals, Items, Equipments, Relics }

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built;
    private bool _isOpen;
    private bool _opening;
    private bool _needsRefresh;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;
    private Tab _currentTab = Tab.All;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    // 위젯 참조
    private TextMeshProUGUI _goldText, _capacityText, _toastText, _hintText;
    private ScrollRect _warehouseScroll;
    private RectTransform _warehouseGrid;
    private readonly List<(Button btn, Tab tab, TextMeshProUGUI label)> _tabButtons = new List<(Button, Tab, TextMeshProUGUI)>();

    // 상단 탭 (창고 | 퀘스트) — 본문을 통째로 스왑한다
    private enum TopTab { Storage, Quest }
    private TopTab _topTab = TopTab.Storage;
    private RectTransform _storageContent, _questContent;
    private CodeQuestView _questView;
    private CodeStatPanel _statPanel;
    private readonly List<(Button btn, TopTab tab, TextMeshProUGUI label)> _topTabs = new List<(Button, TopTab, TextMeshProUGUI)>();

    // 슬롯 풀 — 유물도 이제 일반 창고 슬롯(WarehouseRelicItem)으로 이 풀에 섞여 그려진다.
    private readonly List<CodeSlotView> _warehouseSlots = new List<CodeSlotView>();
    // 미장착 유물을 창고 슬롯으로 편입할 때 쓰는 재사용 버퍼 (SO, 레벨). 매 갱신마다 다시 채운다.
    private readonly List<(Relic.Data.RelicSO so, int level)> _relicScratch = new List<(Relic.Data.RelicSO, int)>();

    // 쓰레기통 / 확인 팝업
    private Image _trashBox;
    private TextMeshProUGUI _trashHint;
    private Color _trashIdleBg, _trashHotBg;
    private CodeConfirmPopup _confirm;

    // 가방 칸(장비·아이템·유물 + 플레이어 프리뷰) — 지하 인벤토리와 공용
    private CodeBagPanel _bag;

    // WASD 키보드 커서 (마우스 호버와 같은 강조를 공유) — 스페이스바로 그 칸·버튼을 실행한다
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();
    // 커서가 들를 버튼들 (상단 탭 · 분류 탭 · 정렬 · 가방 비우기). 만들면서 순서대로 등록된다.
    private readonly List<CodeNavButton> _navButtons = new List<CodeNavButton>();

    // 데이터 참조
    private WarehouseManager _warehouse;
    private ItemInventory _itemInv;
    private MineralInventory _mineralInv;
    private EquipmentInventory _equipInv;
    private PlayerStat _playerStat;

    private float _toastUntil;
    private bool _subscribed;
    private bool _langSubscribed;

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    /// <summary>창고 오버레이를 띄운다.</summary>
    public static void Open()
    {
        if (IsOpen || Instance._opening) return;
        Instance.StartCoroutine(Instance.OpenRoutine());
    }

    /// <summary>창고 오버레이를 닫는다.</summary>
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
        _confirm?.Hide();
        SetTrashHot(false);
        _nav.End();
        Unsubscribe();
        // 세션 중 편입했던 유물 칸을 걷어낸다(구독 해제 후라 재갱신 없음). 저장 상태를 깨끗이 유지.
        _warehouse?.StripRelicSlots();
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

        if (useBlurBackdrop)
        {
            // 오버레이가 캡처에 찍히지 않도록 끈 채로 프레임 끝까지 대기 후 캡처
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
        _confirm?.Hide();
        SetTrashHot(false);
        _currentTab = Tab.All;
        _topTab = TopTab.Storage; // 열 때는 항상 창고 탭부터
        _loc.Refresh();
        RefreshAll();
        ApplyTopTab();
        ClearToast();
        _nav.Begin();

        _canvasObj.SetActive(true);
        if (_warehouseScroll != null) _warehouseScroll.verticalNormalizedPosition = 1f;

        // 페이드인 (timeScale=0일 수 있으므로 unscaled)
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

        // 확인 팝업이 떠 있는 동안엔 팝업 조작만 받는다 — Space·엔터=확인 / ESC·Tab=취소.
        if (_confirm != null && _confirm.IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                _confirm.ConfirmNow();
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab))
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CodeUI.PlayBack();
                _confirm.Hide();
            }
            return;
        }

        // 손에 든 동안엔 다른 단축키를 막는다 — ESC/Tab은 닫기가 아니라 '손 비우기'로 처리한다.
        // (휠 개수 조절·고스트 이동·빈 곳 클릭 취소는 CodeHeldItem이 직접 담당한다)
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

        // Q / E 로 탭 순환. 창고 탭에서는 분류 탭(전체/광물/…), 퀘스트 탭에서는 메인/서브를 넘긴다.
        if (Input.GetKeyDown(KeyCode.Q)) StepActiveTab(-1);
        else if (Input.GetKeyDown(KeyCode.E)) StepActiveTab(+1);

        // WASD = 칸·버튼 이동 / 스페이스 = 실행
        // (퀘스트 탭에서도 돌린다 — 안 그러면 키보드만으로는 창고 탭으로 돌아올 수 없다.
        //  꺼져 있는 창고 본문의 칸·버튼은 내비게이터가 후보에서 거른다)
        _nav.Update();

        if (_toastText != null && _toastUntil > 0f && Time.unscaledTime > _toastUntil) ClearToast();
    }

    private void LateUpdate()
    {
        if (!_isOpen) return;

        // 매니저 이벤트가 한 동작에서 여러 번 오므로 프레임당 한 번만 갱신한다
        // (프리뷰는 페이퍼돌이라 장비가 바뀔 때만 다시 그리면 된다 — RefreshAll이 처리)
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
        _warehouse = WarehouseManager.Instance;

        // FindObjectsInactive.Include 필수 —
        // EquipmentInventory는 옛 인벤토리 UI의 EquipmentsPanel에 붙어 있는데,
        // 코드 오버레이를 쓰면 그 패널이 꺼진 채로 남아 기본 탐색으로는 찾히지 않는다.
        // (SaveManager도 같은 이유로 Include를 쓴다)
        if (_itemInv == null) _itemInv = FindFirstObjectByType<ItemInventory>(FindObjectsInactive.Include);
        if (_mineralInv == null) _mineralInv = FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
        if (_equipInv == null) _equipInv = FindFirstObjectByType<EquipmentInventory>(FindObjectsInactive.Include);
        if (_playerStat == null) _playerStat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;

        if (_warehouse != null) _warehouse.OnWarehouseChanged += MarkDirty;
        if (_itemInv != null) _itemInv.OnInventoryChanged += MarkDirty;
        if (_mineralInv != null) _mineralInv.OnInventoryChanged += MarkDirty;
        if (_equipInv != null) _equipInv.OnInventoryChanged += MarkDirty;
        if (_playerStat != null)
        {
            _playerStat.OnGoldChanged += OnGoldChanged;
            _playerStat.OnStatChanged += MarkDirty; // 업그레이드·장비 강화 → 스탯 패널 갱신
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

        if (_warehouse != null) _warehouse.OnWarehouseChanged -= MarkDirty;
        if (_itemInv != null) _itemInv.OnInventoryChanged -= MarkDirty;
        if (_mineralInv != null) _mineralInv.OnInventoryChanged -= MarkDirty;
        if (_equipInv != null) _equipInv.OnInventoryChanged -= MarkDirty;
        if (_playerStat != null)
        {
            _playerStat.OnGoldChanged -= OnGoldChanged;
            _playerStat.OnStatChanged -= MarkDirty;
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

        // ── 캔버스 ──
        _canvasObj = new GameObject("WarehouseOverlayCanvas");
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

        // ── 블러 배경 + 어둡게 덮는 막 ──
        var blurObj = new GameObject("BlurBackdrop");
        blurObj.transform.SetParent(_canvasObj.transform, false);
        _blurImage = blurObj.AddComponent<RawImage>();
        CodeUI.StretchFull(_blurImage.rectTransform);
        _blurImage.raycastTarget = true; // 뒤쪽 UI 클릭 차단

        var dim = CodeUI.CreateImage(_canvasObj.transform, "Dim", new Color(0f, 0f, 0f, dimAlpha), rounded: false);
        CodeUI.StretchFull(dim.rectTransform);

        // ── 패널 ──
        var panel = CodeUI.CreateImage(_canvasObj.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);
        panelRt.anchoredPosition = Vector2.zero;

        const float side = 22f;
        const float titleH = 72f;
        const float hintH = 30f;
        // 닫기 버튼은 두지 않는다 — ESC / Tab 으로 닫히므로 화면만 차지한다.

        BuildTitleBar(panel.transform, side, titleH);

        // ── 본문 영역 — 창고 탭 ↔ 퀘스트 탭을 이 자리에서 통째로 스왑한다 ──
        var content = CodeUI.CreateRect(panel.transform, "ContentArea");
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = new Vector2(side, side + hintH + 6f);
        content.offsetMax = new Vector2(-side, -(side + titleH + 12f));

        // 창고 탭 본문 (창고 | 가방)
        _storageContent = CodeUI.CreateRect(content, "StorageContent");
        CodeUI.StretchFull(_storageContent);
        var bodyLayout = _storageContent.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 16f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = true;

        // 왼쪽은 [창고 격자 + 쓰레기통]을 세로로 쌓는다(지하 인벤토리와 같은 배치).
        var left = CodeUI.CreateColumn(_storageContent, "LeftColumn", 10f);
        left.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        BuildWarehouseCard(left, 1f);
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

        // ── 조작 안내 (토스트와 같은 자리를 번갈아 쓴다 — Toast()가 이걸 숨기고 자기가 뜬다) ──
        _hintText = CodeUI.CreateText(panel.transform, "Hint", 17f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        var hintRt = _hintText.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.offsetMin = new Vector2(side, side);
        hintRt.offsetMax = new Vector2(-side, side + hintH);
        // 힌트 문구는 상단 탭(창고/퀘스트)에 따라 UpdateHint()가 바꾼다 — 바인딩하지 않고 폰트만 추적한다.

        // ── 안내 문구(토스트) — 힌트와 같은 자리에 번갈아 표시 ──
        _toastText = CodeUI.CreateText(panel.transform, "Toast", 18f, FontStyles.Bold,
            CodeUI.NegativeColor, TextAlignmentOptions.Center, _loc);
        var toastRt = _toastText.rectTransform;
        toastRt.anchorMin = hintRt.anchorMin;
        toastRt.anchorMax = hintRt.anchorMax;
        toastRt.pivot = hintRt.pivot;
        toastRt.offsetMin = hintRt.offsetMin;
        toastRt.offsetMax = hintRt.offsetMax;
        _toastText.gameObject.SetActive(false);

        _confirm = new CodeConfirmPopup(_canvasObj.transform, skin, _loc,
            "ui_inv_drop", "버리기", () => CodeUI.PlaySfx(clickSfxName));

        // WASD 커서 — 창고 격자와 가방 칸을 하나의 판으로 보고 좌표로 이동한다.
        _nav.collect = CollectNavSlots;
        _nav.onActivate = OnNavActivate;
        _nav.moveSfxName = null; // 칸을 옮길 때마다 소리가 나면 시끄럽다 — 실행(스페이스)에만 소리를 낸다

        _canvasObj.SetActive(false);
    }

    /// <summary>
    /// WASD 이동 후보 — 창고 칸 + 가방 칸 + 버튼(상단 탭 · 분류 탭 · 정렬 · 가방 비우기).
    /// 꺼진 항목(퀘스트 탭일 때의 창고 본문 등)은 내비게이터가 알아서 거른다.
    /// </summary>
    private void CollectNavSlots(List<ICodeNavItem> into)
    {
        foreach (var view in _warehouseSlots)
            if (view != null && view.gameObject.activeInHierarchy) into.Add(view);

        _bag?.CollectSlots(into);

        foreach (var nav in _navButtons)
            if (nav != null && nav.NavUsable) into.Add(nav);

        // 퀘스트 탭 [메인][서브 I][서브 II] — Q/E 말고 WASD·Space로도 넘길 수 있게
        _questView?.CollectNavItems(into);
    }

    /// <summary>
    /// 스페이스바 — 좌클릭과 같은 동작. 창고 칸이면 꺼내기(장비·유물은 장착),
    /// 가방 칸이면 창고로 넣기(= 장비·유물 해제), 버튼이면 그 버튼을 누른다.
    /// </summary>
    private void OnNavActivate(ICodeNavItem item)
    {
        if (item is CodeSlotView view)
        {
            // 키보드(스페이스)는 마우스의 '손에 집기'와 달리 즉시 전량 이동한다 —
            // 마우스 고스트가 없는 키보드 조작에서 손에 집기는 어색하므로 옛 방식(즉시 이동)을 유지한다.
            NavActivateSlot(view);
            return;
        }

        item?.NavActivate();
    }

    /// <summary>키보드 스페이스: 창고 칸=꺼내기(유물은 장착) / 가방 칸=넣기(유물은 해제). 전량 즉시.</summary>
    private void NavActivateSlot(CodeSlotView view)
    {
        if (view.zone == Zone.Warehouse)
        {
            if (!view.HasItem) return;
            if (WarehouseManager.IsRelicSlot(view.Slot)) { EquipRelicFromWarehouse(view.index, -1); return; }
            Withdraw(view.index, view.Slot.quantity);
        }
        else if (view.zone == Zone.BagRelic)
        {
            UnequipRelic(view.index);
        }
        else
        {
            if (!view.HasItem) return;
            Deposit(view.zone, view.index, view.Slot.quantity);
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

        var diamond = CodeUI.CreateText(bar, "Diamond", 22f, FontStyles.Normal, CodeUI.GoldColor, TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;

        // 상단 탭 — [창고] [퀘스트]. 누르면 본문이 통째로 바뀐다.
        AddTopTab(bar, TopTab.Storage, "ui_wh_storage", "창고");
        AddTopTab(bar, TopTab.Quest, "ui_wh_quest", "퀘스트");

        CodeUI.CreateSpacer(bar);

        // 소지금 — 화폐 이미지 + 숫자만
        _goldText = CodeUI.CreateGoldBox(bar, goldIconSprite, _loc, skin);
    }

    private void BuildWarehouseCard(Transform parent, float flexibleWidth)
    {
        var card = CodeUI.CreateImage(parent, "WarehouseCard", CodeUI.CardBg, skin.cardSprite, skin);
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

        // 가방에서 끌어온 아이템을 카드 아무 데나 떨어뜨리면 '넣기'
        CodeDropZone.Attach(card.gameObject, OnDroppedOnWarehousePanel);

        // 헤더
        var header = CodeUI.CreateRow(card.transform, "Header", 34f);
        var label = CodeUI.CreateText(header, "Label", 23f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(label, "ui_wh_storage", "창고");
        CodeUI.CreateSpacer(header);
        _capacityText = CodeUI.CreateText(header, "Capacity", 20f, FontStyles.Bold, CodeUI.LabelColor, TextAlignmentOptions.MidlineRight, _loc);
        _capacityText.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;

        CodeUI.CreateDivider(card.transform);

        // 탭
        var tabs = CodeUI.CreateRow(card.transform, "Tabs", 42f, 8f);
        AddTab(tabs, Tab.All, "ui_wh_tab_all", "전체", Color.white);
        AddTab(tabs, Tab.Minerals, "ui_wh_tab_mineral", "광물", CodeUI.MineralColor);
        AddTab(tabs, Tab.Items, "ui_wh_tab_item", "아이템", CodeUI.ItemColor);
        AddTab(tabs, Tab.Equipments, "ui_wh_tab_equip", "장비", CodeUI.EquipColor);
        AddTab(tabs, Tab.Relics, "ui_wh_tab_relic", "유물", CodeUI.RelicColor);

        // 격자 스크롤 — 남는 세로 공간을 전부 차지하되 최소 높이는 확보한다
        _warehouseScroll = CodeUI.CreateScrollView(card.transform, "Scroll", out _warehouseGrid);
        var scrollLe = _warehouseScroll.gameObject.AddComponent<LayoutElement>();
        scrollLe.flexibleHeight = 1f;
        scrollLe.minHeight = 200f;
        AddGrid(_warehouseGrid, warehouseColumns);

        // 하단 버튼
        var footer = CodeUI.CreateRow(card.transform, "Footer", 46f, 10f);
        AddCardButton(footer, "ui_wh_sort", "정렬", CodeUI.NeutralBg, Color.white, OnClickSort, 130f);
        AddCardButton(footer, "ui_wh_deposit_all", "가방 비우기", CodeUI.NeutralBg, Color.white, OnClickDepositAll, 180f);
        CodeUI.CreateSpacer(footer);
    }

    /// <summary>
    /// 쓰레기통 — 창고·가방 칸을 여기로 끌어다 놓으면 버린다(확인 팝업을 한 번 거친다).
    /// 지하 인벤토리 오버레이와 같은 생김새·같은 자리(목록 바로 아래).
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
        boxLe.flexibleHeight = 0f; // 창고 카드만 남는 세로 공간을 먹는다

        var row = _trashBox.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 16, 8, 8);
        row.spacing = 12f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        row.childAlignment = TextAnchor.MiddleLeft;

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

    /// <summary>스프라이트를 안 넣었을 때 쓰는 코드 생성 쓰레기통 아이콘 (뚜껑 + 통 + 세로 줄).</summary>
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

        Part(root, "Handle", new Vector2(0.40f, 0.82f), new Vector2(0.60f, 0.90f), shell);
        Part(root, "Lid",    new Vector2(0.16f, 0.70f), new Vector2(0.84f, 0.80f), shell);

        var body = Part(root, "Body", new Vector2(0.24f, 0.14f), new Vector2(0.76f, 0.68f), shell);

        var hollow = Part(body.transform, "Hollow", Vector2.zero, Vector2.one, inner);
        hollow.rectTransform.offsetMin = new Vector2(3f, 3f);
        hollow.rectTransform.offsetMax = new Vector2(-3f, -3f);

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

        // 창고에서 끌어온 아이템을 카드 아무 데나 떨어뜨리면 '꺼내기'
        CodeDropZone.Attach(card.gameObject, OnDroppedOnBagPanel);

        var header = CodeUI.CreateRow(card.transform, "Header", 34f);
        var label = CodeUI.CreateText(header, "Label", 23f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(label, "ui_wh_bag", "가방");
        CodeUI.CreateSpacer(header);

        CodeUI.CreateDivider(card.transform);

        // ── 가방 칸: [장비 → 아이템 → 유물] 세로 한 줄 + 오른쪽 플레이어 프리뷰 ──
        // 지하 인벤토리 오버레이와 같은 컴포넌트를 쓴다(CodeBagPanel).
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
            equipmentInteractable = true, // 지상에서는 장착 해제 가능
            itemInteractable = true,
            // 유물 칸은 RelicManager 로드아웃을 그린다 — 클릭으로 장착/해제.
            zoneRelic = Zone.BagRelic,
            useRelicLoadout = true,
            relicInteractable = true,
            headPlaceholder = headPlaceholder,
            clothesPlaceholder = clothesPlaceholder,
            shoesPlaceholder = shoesPlaceholder,
            relicPlaceholder = relicPlaceholder,
            itemPlaceholder = relicPlaceholder, // 아이템 빈 칸도 유물 칸과 같은 플레이스홀더로
            minPreviewWidth = minPreviewWidth
        }, _loc);

        _bag.onLeftClick = OnBagSlotLeftClick;
        _bag.onRightClick = OnBagSlotRightClick;
        _bag.onDropReceived = OnSlotDropReceived;

        // 플레이어 스탯 — 예전 퀘스트 요약이 있던 자리. 남는 세로 공간을 전부 차지한다.
        AddSectionLabel(card.transform, "ui_stat_title", "스탯", CodeUI.AccentFill);

        _statPanel = new CodeStatPanel(card.transform, new CodeStatPanel.Config
        {
            skin = skin,
            diamondGlyph = diamondGlyph,
            icons = statIcons
        }, _loc);
        var statLe = _statPanel.Root.gameObject.AddComponent<LayoutElement>();
        statLe.flexibleHeight = 1f;
        statLe.minHeight = 180f;
    }

    // ── 조립 헬퍼 ──
    private void AddGrid(RectTransform target, int columns)
    {
        var grid = target.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(slotSize, slotSize);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(2, 2, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, columns);
        // 카드 폭에 맞춰 칸 수·크기를 자동으로 채운다 — 넓어져도 오른쪽이 남지 않는다(해상도 변화도 대응).
        var fill = target.gameObject.AddComponent<CodeGridFill>();
        fill.targetCell = slotSize;
        fill.minColumns = warehouseColumns;
    }

    private void AddSectionLabel(Transform parent, string key, string fallback, Color dotColor)
    {
        var row = CodeUI.CreateRow(parent, "SectionLabel", 28f, 8f);
        var dot = CodeUI.CreateText(row, "Dot", 15f, FontStyles.Normal, dotColor, TextAlignmentOptions.Center, _loc);
        dot.text = diamondGlyph;
        var label = CodeUI.CreateText(row, "Label", 19f, FontStyles.Bold, CodeUI.LabelColor, TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(label, key, fallback);
        CodeUI.CreateSpacer(row);
    }

    private void AddTab(Transform parent, Tab tab, string key, string fallback, Color accent)
    {
        var btn = CodeUI.CreateButton(parent, "Tab_" + tab, CodeUI.TabIdleBg, () => SwitchTab(tab), skin.tabSprite, skin);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredHeight = 40f;

        var label = CodeUI.CreateText(btn.transform, "Text", 19f, FontStyles.Bold, accent, TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(label.rectTransform);
        _loc.Bind(label, key, fallback);

        _tabButtons.Add((btn, tab, label));
        _navButtons.Add(CodeNavButton.Attach(btn, skin));
    }

    private void AddCardButton(Transform parent, string key, string fallback, Color bg, Color fg,
        System.Action onClick, float width)
    {
        var btn = CodeUI.CreateTextButton(parent, "Btn_" + key, bg, fg, 19f, onClick, out var label,
            skin.buttonSprite, skin, _loc);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 42f;
        _loc.Bind(label, key, fallback);
        _navButtons.Add(CodeNavButton.Attach(btn, skin));
    }

    // ===================================================
    // 갱신
    // ===================================================
    private void RefreshAll()
    {
        if (_warehouse == null) _warehouse = WarehouseManager.Instance;

        // 가방(유물 로드아웃 포함)을 먼저 그린다 — 창고 격자가 _bag.RelicMgr을 참조한다
        _bag?.Refresh(_itemInv, _equipInv);
        RefreshWarehouseGrid();
        _statPanel?.Refresh(_playerStat);
        RefreshInfo();
        UpdateTabVisuals();
        UpdateHint();
        _nav.Refresh(); // 꺼진 칸을 물고 있으면 커서를 놓는다

        // 퀘스트 탭이 열려 있으면 함께 갱신(창고 광물 변화 → 요구 진행도 반영)
        if (_topTab == TopTab.Quest) _questView?.Refresh();
    }

    private void RefreshWarehouseGrid()
    {
        // 미장착 보유 유물을 창고 슬롯(WarehouseRelicItem)으로 편입한다(세션 전용).
        // 이후 로직은 유물을 광물·아이템·장비와 완전히 동일한 슬롯으로 취급한다 → 스왑·정렬·용량 일원화.
        SyncWarehouseRelics();

        var display = new List<(InventorySlot slot, int index)>();

        if (_warehouse != null)
        {
            var slots = _warehouse.AllSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                bool empty = slot == null || slot.item == null;

                if (_currentTab == Tab.All)
                {
                    display.Add((slot, i)); // 빈 칸도 보여줘야 남은 용량이 보인다
                    continue;
                }
                if (empty) continue; // 필터 탭에서는 빈 칸 숨김

                bool match = _currentTab switch
                {
                    Tab.Minerals => slot.item is MineralSO,
                    Tab.Items => slot.item is ItemSO,
                    Tab.Equipments => slot.item is EquipmentSO, // 유물은 EquipmentSO가 아니므로 제외됨
                    Tab.Relics => WarehouseManager.IsRelicSlot(slot),
                    _ => true
                };
                if (match) display.Add((slot, i));
            }
        }

        EnsureSlots(_warehouseSlots, _warehouseGrid, display.Count, Zone.Warehouse,
            onLeft: OnWarehouseSlotLeftClick, onRight: OnWarehouseSlotRightClick, onDrop: OnSlotDropReceived,
            onDouble: OnWarehouseSlotDoubleClick);

        for (int i = 0; i < _warehouseSlots.Count; i++)
        {
            var view = _warehouseSlots[i];
            bool used = i < display.Count;
            view.gameObject.SetActive(used);
            if (!used) continue;

            view.index = display[i].index;
            var slot = display[i].slot;
            view.Bind(slot, AccentOf(slot));
            // 강화 단계(+N)를 강화 UI와 동일하게 표기 — 장비는 강화 저장소, 유물은 어댑터가 물고 있는 레벨.
            if (slot != null && slot.item is EquipmentSO eq)
                view.SetLevelBadge(EquipmentUpgradeStore.GetLevel(eq));
            else if (slot != null && slot.item is WarehouseRelicItem relicItem)
                view.SetLevelBadge(Mathf.Max(0, relicItem.level - 1)); // 유물은 내부 1부터 → 표시는 0강부터
        }
    }

    /// <summary>
    /// 미장착 보유 유물을 모아 <see cref="WarehouseManager.SyncRelicSlots"/>로 창고 슬롯에 편입한다.
    /// 실물(소유·레벨·로드아웃)은 여전히 RelicInventory가 갖고 있고, 창고 슬롯은 세션 중의 표시·조작용 어댑터다.
    /// </summary>
    private void SyncWarehouseRelics()
    {
        if (_warehouse == null) return;

        _relicScratch.Clear();

        var mgr = _bag != null ? _bag.RelicMgr : null;
        var inv = mgr != null ? mgr.Inventory : null;
        var db = Relic.Data.RelicDatabase.Instance;

        if (inv != null && db != null)
        {
            foreach (var kv in inv.Owned)
            {
                bool equipped = false;
                for (int s = 0; s < inv.SlotCount; s++)
                    if (inv.GetEquipped(s) == kv.Key) { equipped = true; break; }
                if (equipped) continue; // 장착 중인 유물은 가방 로드아웃 칸에만 보인다

                var so = db.GetRelicByID(kv.Key);
                if (so != null) _relicScratch.Add((so, kv.Value));
            }
        }

        _warehouse.SyncRelicSlots(_relicScratch);
    }

    private void RefreshInfo()
    {
        // 창고 사용량
        if (_capacityText != null)
        {
            int used = 0, total = 0;
            if (_warehouse != null)
            {
                total = _warehouse.totalSlots;
                foreach (var s in _warehouse.AllSlots)
                    if (s != null && s.item != null) used++;
            }
            _capacityText.text = $"{used} / {total}";
            _capacityText.color = (total > 0 && used >= total) ? CodeUI.NegativeColor : CodeUI.LabelColor;
        }

        // 소지금 (화폐 이미지 + 숫자만 — 접미사 없음)
        if (_goldText != null)
            _goldText.text = CodeUI.Gold(_playerStat != null ? _playerStat.Gold : 0);
    }

    private void UpdateTabVisuals()
    {
        foreach (var (btn, tab, label) in _tabButtons)
        {
            bool selected = (tab == _currentTab);
            Sprite sprite = selected
                ? (skin.tabSelectedSprite != null ? skin.tabSelectedSprite : skin.tabSprite)
                : skin.tabSprite;
            Color bg = selected ? CodeUI.AccentFill : CodeUI.TabIdleBg;

            CodeUI.ApplySkin(btn.image, bg, sprite, skin);
            label.color = selected ? Color.white : new Color(label.color.r, label.color.g, label.color.b, 0.75f);
        }
    }

    /// <summary>슬롯 풀 크기 맞추기 — 모자라면 만들고, 남으면 그대로 두되 비활성화는 호출부가 한다.</summary>
    private void EnsureSlots(List<CodeSlotView> pool, RectTransform parent, int needed, int zone,
        System.Action<CodeSlotView> onLeft, System.Action<CodeSlotView> onRight,
        System.Action<CodeSlotView, CodeSlotView> onDrop, System.Action<CodeSlotView> onDouble = null)
    {
        while (pool.Count < needed)
        {
            var view = CodeSlotView.Create(parent, skin, slotSize, $"Slot_{zone}_{pool.Count}");
            view.zone = zone;
            view.onLeftClick = onLeft;
            view.onRightClick = onRight;
            view.onDropReceived = onDrop;
            view.onDoubleClick = onDouble;
            pool.Add(view);
        }
    }

    private Color AccentOf(InventorySlot slot)
    {
        if (slot == null || slot.item == null) return Color.clear;
        if (slot.item is MineralSO) return CodeUI.MineralColor;
        if (slot.item is WarehouseRelicItem) return CodeUI.RelicColor;
        if (slot.item is EquipmentSO) return CodeUI.EquipColor;
        return CodeUI.ItemColor;
    }

    // 장비 칸 플레이스홀더 선택은 CodeBagPanel이 담당한다(유물이 2칸이라 타입 기준으로 고른다).

    // ===================================================
    // 조작 — 탭 / 버튼
    // ===================================================
    /// <summary>상단 탭(창고/퀘스트) 버튼 하나. 탭 스킨을 따르며 _topTabs에 등록된다.</summary>
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
            RefreshWarehouseGrid();
            RefreshInfo();
            if (_warehouseScroll != null) _warehouseScroll.verticalNormalizedPosition = 1f;
        }
        else
        {
            // 커서는 걷지 않는다 — 상단 탭은 살아 있으므로 그대로 두면 W/S로 창고 탭에 돌아올 수 있다.
            // (창고 본문 칸에 있던 커서는 Refresh가 가장 가까운 살아 있는 항목으로 옮긴다)
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

    /// <summary>Q/E — 창고 탭이면 분류 탭, 퀘스트 탭이면 메인/서브 탭을 넘긴다.</summary>
    private void StepActiveTab(int dir)
    {
        if (_topTab == TopTab.Quest) _questView?.StepTab(dir);
        else StepTab(dir);
    }

    /// <summary>하단 힌트 문구를 현재 상단 탭에 맞게 바꾼다.</summary>
    private void UpdateHint()
    {
        if (_hintText == null) return;
        _hintText.text = _topTab == TopTab.Quest
            ? CodeUI.L("ui_topquest_hint", "WASD : 이동 / Space : 선택   ·   Q / E : 퀘스트 탭 전환   ·   ESC / Tab : 닫기")
            : CodeUI.L("ui_wh_hint", "좌클릭 : 손에 집기·전부 옮기기   ·   우클릭 : 장착 / 해제 (아이템 1개씩)   ·   WASD : 이동 / Space : 선택   ·   쓰레기통 : 버리기   ·   ESC / Tab : 닫기");
    }

    private void SwitchTab(Tab tab)
    {
        if (_currentTab == tab) return;
        CodeUI.PlaySfx(clickSfxName);
        _currentTab = tab;
        RefreshWarehouseGrid();
        UpdateTabVisuals();
        if (_warehouseScroll != null) _warehouseScroll.verticalNormalizedPosition = 1f;
    }

    private void StepTab(int dir)
    {
        int count = System.Enum.GetValues(typeof(Tab)).Length;
        int next = ((int)_currentTab + dir + count) % count;
        SwitchTab((Tab)next);
    }

    private void OnClickSort()
    {
        CodeUI.PlaySfx(clickSfxName);
        _warehouse?.SortWarehouse();
    }

    private void OnClickDepositAll()
    {
        CodeUI.PlaySfx(clickSfxName);
        if (_warehouse == null) return;

        // 가방을 통째로 비운다 — 아이템·광물뿐 아니라 착용 중인 장비·유물까지 전부 창고로 보낸다.
        // (지상 화면 전용 버튼이라 맨몸으로 지하에 내려갈 위험은 플레이어 판단에 맡긴다.
        //  자동 정리인 SaveManager.MergeInventoriesToWarehouse는 여전히 장비를 건드리지 않는다)
        _warehouse.DepositAllFromInventory(_itemInv, _mineralInv, _equipInv);

        // 유물도 전부 해제해 창고 목록으로 돌려보낸다 (장비와 같은 취급)
        var mgr = _bag != null ? _bag.RelicMgr : null;
        if (mgr != null)
            for (int i = 0; i < mgr.Inventory.SlotCount; i++)
                if (mgr.Inventory.GetEquipped(i) != Relic.Data.RelicID.None) mgr.UnequipSlot(i);

        Toast("ui_wh_deposited", "가방의 물건과 장비·유물을 모두 창고에 넣었습니다.", CodeUI.PositiveColor);
        _needsRefresh = true;
    }

    // ===================================================
    // 조작 — 슬롯
    // ===================================================
    // 클릭 = "손에 집기"(CodeHeldItem). 드래그(자유 이동·스왑)는 CodeSlotView가 그대로 담당한다.
    //  · 빈 손 + 아이템 칸 → 스택을 손에 집는다.
    //  · 손에 든 상태 → 좌클릭=전부 내려놓기 / 우클릭=1개 내려놓기.
    //  · 유물 칸은 스택 개념이 없어 기존 클릭(장착/해제)을 유지한다.
    private void OnWarehouseSlotLeftClick(CodeSlotView view)
    {
        if (CodeHeldItem.Active) { CodeHeldItem.PlaceAll(view); return; }
        if (!view.HasItem) return;
        // 유물 칸 = 장착(빈 로드아웃 우선) — 손에 집지 않는다.
        if (WarehouseManager.IsRelicSlot(view.Slot)) { EquipRelicFromWarehouse(view.index, -1); return; }
        CodeHeldItem.PickUp(view, this);
    }

    /// <summary>
    /// 창고 칸 더블클릭 = 여기저기 흩어진 같은 아이템을 이 스택으로 끌어모아 손에 집는다.
    /// 최대 스택수까지만 모이고, 넘치는 만큼은 다른 칸에 그대로 흩어진 채 남는다.
    ///
    /// 흐름: 첫 클릭이 (onLeftClick으로) 이미 이 칸을 손에 집은 상태다.
    ///  1) 그 손을 비우고(데이터는 안 뺐으므로 원본 그대로),
    ///  2) WarehouseManager.GatherItems로 이 칸에 같은 아이템을 실제로 병합(최대 스택 제한),
    ///  3) 병합 결과를 반영해 격자를 즉시 다시 그린 뒤,
    ///  4) 그 칸을 다시 손에 집는다.
    /// 이렇게 하면 손에 집기(단일 원본) 불변식을 깨지 않는다 — 손이 잡는 건 언제나 병합이 끝난 '한 칸'이다.
    /// </summary>
    private void OnWarehouseSlotDoubleClick(CodeSlotView view)
    {
        if (view == null || !view.HasItem) return;
        var slot = view.Slot;
        // 스택 불가(장비 등)·유물 칸은 모으기 대상이 아니다.
        if (slot.item == null || !slot.item.Stackable) return;
        if (WarehouseManager.IsRelicSlot(slot)) return;
        if (_warehouse == null) return;

        int index = view.index;

        CodeHeldItem.Cancel();          // 첫 클릭이 집어 든 손을 비운다(원본 데이터는 그대로)
        _warehouse.GatherItems(index);  // 같은 아이템을 이 칸으로 병합(최대 스택까지, 나머지는 흩어진 채)
        RefreshAll();                   // 병합 결과로 격자 재바인딩(아직 손에 든 게 없어 desync 없음)

        // 병합 후 이 창고 인덱스를 그리고 있는 칸을 찾아 다시 손에 집는다.
        var target = FindActiveWarehouseView(index);
        if (target != null && target.HasItem) CodeHeldItem.PickUp(target, this);
    }

    /// <summary>지금 활성 상태로 창고 슬롯 <paramref name="warehouseIndex"/>를 그리고 있는 칸을 찾는다(없으면 null).</summary>
    private CodeSlotView FindActiveWarehouseView(int warehouseIndex)
    {
        foreach (var v in _warehouseSlots)
            if (v != null && v.gameObject.activeInHierarchy && v.zone == Zone.Warehouse && v.index == warehouseIndex)
                return v;
        return null;
    }

    private void OnWarehouseSlotRightClick(CodeSlotView view)
    {
        if (CodeHeldItem.Active) { CodeHeldItem.PlaceOne(view); return; }
        if (!view.HasItem) return;
        // 우클릭 = 장착. 유물은 로드아웃에, 장비는 착용 칸에, 아이템은 스택이 여러 개여도 한 번에 하나씩 가방으로.
        if (WarehouseManager.IsRelicSlot(view.Slot)) { EquipRelicFromWarehouse(view.index, -1); return; }
        Withdraw(view.index, 1);
    }

    private void OnBagSlotLeftClick(CodeSlotView view)
    {
        if (view.zone == Zone.BagRelic)
        {
            if (CodeHeldItem.Active) { CodeHeldItem.PlaceAll(view); return; } // 창고 유물을 이 로드아웃 칸에 장착
            UnequipRelic(view.index);
            return;
        }
        if (CodeHeldItem.Active) { CodeHeldItem.PlaceAll(view); return; }
        if (!view.HasItem) return;
        CodeHeldItem.PickUp(view, this);
    }

    private void OnBagSlotRightClick(CodeSlotView view)
    {
        if (view.zone == Zone.BagRelic)
        {
            if (CodeHeldItem.Active) { CodeHeldItem.PlaceOne(view); return; }
            UnequipRelic(view.index);
            return;
        }
        if (CodeHeldItem.Active) { CodeHeldItem.PlaceOne(view); return; }
        if (!view.HasItem) return;
        // 우클릭 = 장착 해제. 장비는 창고로, 아이템은 스택이 여러 개여도 한 번에 하나씩 창고로.
        Deposit(view.zone, view.index, 1);
    }

    /// <summary>슬롯 위에 슬롯을 (드래그로) 떨어뜨렸을 때 — 스택 전부 이동.</summary>
    private void OnSlotDropReceived(CodeSlotView target, CodeSlotView source)
    {
        MoveBetween(source, target, source.HasItem ? source.Slot.quantity : 1);
    }

    /// <summary>
    /// source 스택에서 amount개를 target 칸으로 옮긴다. 드래그(전량)와 손에 집기(부분 수량)가 공용.
    /// 유물·장비처럼 개수 개념이 없는 이동은 amount와 무관하게 1개/전량으로 처리한다.
    /// </summary>
    private void MoveBetween(CodeSlotView source, CodeSlotView target, int amount)
    {
        // 가방 로드아웃 유물 → 창고 쪽에 떨어뜨리면 해제(창고 슬롯으로 되돌아온다).
        if (source.zone == Zone.BagRelic)
        {
            if (target.zone == Zone.Warehouse) UnequipRelic(source.index);
            return;
        }

        if (!source.HasItem) return;

        bool sourceIsRelic = source.zone == Zone.Warehouse && WarehouseManager.IsRelicSlot(source.Slot);

        // 창고 유물 → 가방 로드아웃 칸: 그 칸에 장착.
        if (sourceIsRelic && target.zone == Zone.BagRelic)
        {
            EquipRelicFromWarehouse(source.index, target.index);
            return;
        }

        if (target.zone == Zone.BagRelic) return; // 유물 로드아웃 칸에는 유물이 아닌 것을 못 넣는다

        // 창고 안에서의 이동. 전량이면 병합/스왑, 부분(손에 집기 하나씩)이면 그만큼만 옮긴다.
        if (source.zone == Zone.Warehouse && target.zone == Zone.Warehouse)
        {
            if (_warehouse != null)
            {
                int fromQty = source.Slot != null ? source.Slot.quantity : 0;
                bool ok = (amount >= fromQty)
                    ? _warehouse.MergeOrSwapSlots(source.index, target.index)      // 전량 = 기존 드래그 동작(병합·스왑)
                    : _warehouse.MovePartial(source.index, target.index, amount) > 0; // 부분 = 그만큼만
                if (ok) CodeUI.PlaySfx(moveSfxName);
            }
            return;
        }

        // 창고 유물을 가방의 아이템·장비 칸에 떨어뜨림 → 빈 로드아웃에 장착.
        if (sourceIsRelic && (target.zone == Zone.BagItem || target.zone == Zone.BagEquipment))
        {
            EquipRelicFromWarehouse(source.index, -1);
            return;
        }

        int move = Mathf.Clamp(amount, 1, source.Slot.quantity);

        // 창고 → 가방 (떨어뜨린 칸을 알려준다 — 그 칸에 뭐가 있으면 서로 맞바꾼다)
        if (source.zone == Zone.Warehouse) { Withdraw(source.index, move, target.zone, target.index); return; }

        // 가방 → 창고 (특정 칸 지정은 지원하지 않음 — 창고가 알아서 빈 칸/스택에 넣는다)
        if (target.zone == Zone.Warehouse) { Deposit(source.zone, source.index, move); return; }

        // 가방 → 가방: 옮길 곳이 없으므로 무시
    }

    private void OnDroppedOnWarehousePanel(CodeSlotView source)
    {
        if (source == null) return;
        if (source.zone == Zone.BagRelic) { UnequipRelic(source.index); return; }
        if (!source.HasItem || source.zone == Zone.Warehouse) return; // 이미 창고 슬롯이면 무시
        Deposit(source.zone, source.index, source.Slot.quantity);
    }

    private void OnDroppedOnBagPanel(CodeSlotView source)
    {
        if (source == null) return;
        if (!source.HasItem || source.zone != Zone.Warehouse) return;
        // 창고 유물을 가방 영역에 떨어뜨리면 장착, 그 외는 가방으로 꺼내기.
        if (WarehouseManager.IsRelicSlot(source.Slot)) { EquipRelicFromWarehouse(source.index, -1); return; }
        Withdraw(source.index, source.Slot.quantity);
    }

    // ===================================================
    // 유물 장착 / 해제 (실물은 RelicInventory가 갖고 있다)
    // ===================================================

    /// <summary>
    /// 창고 슬롯의 유물을 로드아웃에 장착. targetSlot이 -1이면 빈 슬롯 우선, 없으면 첫 슬롯.
    /// 장착되면 그 유물은 미장착 목록에서 빠지므로 다음 갱신에서 <see cref="SyncWarehouseRelics"/>가 창고 칸을 비운다.
    /// </summary>
    private void EquipRelicFromWarehouse(int warehouseIndex, int targetSlot)
    {
        var mgr = _bag != null ? _bag.RelicMgr : null;
        if (mgr == null || _warehouse == null) return;

        var slots = _warehouse.AllSlots;
        if (warehouseIndex < 0 || warehouseIndex >= slots.Count) return;

        var relicItem = slots[warehouseIndex]?.item as WarehouseRelicItem;
        if (relicItem == null || relicItem.RelicId == Relic.Data.RelicID.None) return;

        var inv = mgr.Inventory;

        // 유물 칸은 업그레이드로만 열린다(RelicManager.BaseSlotCount = 0).
        // 한 칸도 없으면 장착 자체가 불가 — 조용히 실패하지 않도록 이유를 알려준다.
        if (inv.SlotCount <= 0)
        {
            Toast("ui_wh_relic_no_slot", "유물 칸이 없습니다. 업그레이드에서 유물 슬롯을 먼저 여세요.",
                CodeUI.NegativeColor);
            return;
        }

        int slot = targetSlot;
        if (slot < 0 || slot >= inv.SlotCount)
        {
            slot = 0;
            for (int i = 0; i < inv.SlotCount; i++)
                if (inv.GetEquipped(i) == Relic.Data.RelicID.None) { slot = i; break; }
        }

        mgr.EquipSlot(slot, relicItem.RelicId);
        CodeUI.PlaySfx(moveSfxName);
        _needsRefresh = true;
    }

    private void UnequipRelic(int slot)
    {
        var mgr = _bag != null ? _bag.RelicMgr : null;
        if (mgr == null) return;
        if (mgr.Inventory.GetEquipped(slot) == Relic.Data.RelicID.None) return;

        mgr.UnequipSlot(slot);
        CodeUI.PlaySfx(moveSfxName);
        _needsRefresh = true;
    }

    // ===================================================
    // 쓰레기통
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
            case Zone.Warehouse:
                // 유물은 고유라 버릴 수 없다 — 창고 슬롯에 얹힌 유물이면 막는다.
                if (WarehouseManager.IsRelicSlot(GetAt(_warehouse != null ? _warehouse.AllSlots : null, source.index)))
                {
                    Toast("ui_wh_trash_relic_locked", "유물은 버릴 수 없습니다.", CodeUI.MutedColor);
                    break;
                }
                // 장비도 고유라 버릴 수 없다 — 창고 칸에 든 게 장비면 막는다.
                if (IsEquipmentSlot(_warehouse != null ? _warehouse.AllSlots : null, source.index))
                {
                    Toast("ui_wh_trash_equip_locked", "장비는 버릴 수 없습니다.", CodeUI.MutedColor);
                    break;
                }
                AskDiscard(_warehouse != null ? _warehouse.AllSlots : null, source.index,
                    (idx, qty) => { _warehouse.RemoveItemAt(idx, qty); return true; }, amount);
                break;

            case Zone.BagItem:
                AskDiscard(_itemInv != null ? _itemInv.ReadonlyItems : null, source.index,
                    (idx, qty) => _itemInv.RemoveItemAt(idx, qty), amount);
                break;

            case Zone.BagEquipment:
                Toast("ui_wh_trash_equip_locked", "장비는 버릴 수 없습니다.", CodeUI.MutedColor);
                break;

            case Zone.BagRelic:
                Toast("ui_wh_trash_relic_locked", "유물은 버릴 수 없습니다.", CodeUI.MutedColor);
                break;
        }
    }

    /// <summary>해당 칸에 든 것이 장비(EquipmentSO)인지 — 창고처럼 여러 종류가 섞인 칸의 버리기 차단용.</summary>
    private static bool IsEquipmentSlot(IReadOnlyList<InventorySlot> list, int index)
    {
        if (list == null || index < 0 || index >= list.Count) return false;
        return list[index]?.item is EquipmentSO;
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
    /// 슬롯 하나를 통째로 버릴지 묻는다. 확인 후에도 같은 물건이 그 자리에 있는지 다시 확인하고 지운다
    /// (팝업이 떠 있는 동안 창고가 바뀌면 엉뚱한 칸을 지우게 되므로).
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
        string amount = quantity > 1 ? $"{displayName} x{quantity}" : displayName;

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
    // 손에 집기 호스트 (CodeHeldItem.IHost)
    // ===================================================
    bool CodeHeldItem.IHost.HeldHostOpen => _isOpen;
    ScrollRect CodeHeldItem.IHost.HeldScrollRect => _warehouseScroll;
    void CodeHeldItem.IHost.HeldRefresh() => _needsRefresh = true;
    void CodeHeldItem.IHost.HeldPlaySfx() => CodeUI.PlaySfx(moveSfxName);

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

    /// <summary>source 칸이 가리키는 라이브 인벤토리 슬롯(zone·index 기준). 손에 집기 정합성 판정용.</summary>
    private InventorySlot HeldSourceSlot(CodeSlotView source)
    {
        if (source == null) return null;
        switch (source.zone)
        {
            case Zone.Warehouse: return GetAt(_warehouse != null ? _warehouse.AllSlots : null, source.index);
            case Zone.BagItem: return GetAt(_itemInv != null ? _itemInv.ReadonlyItems : null, source.index);
            case Zone.BagEquipment: return GetAt(_equipInv != null ? _equipInv.ReadonlyItems : null, source.index);
            default: return null;
        }
    }

    int CodeHeldItem.IHost.HeldCommit(CodeSlotView source, CodeSlotView target, int amount)
    {
        var beforeSlot = HeldSourceSlot(source);
        string beforeId = beforeSlot?.item?.Id;
        int before = beforeSlot?.item != null ? beforeSlot.quantity : 0;

        MoveBetween(source, target, amount);

        var afterSlot = HeldSourceSlot(source);
        string afterId = afterSlot?.item?.Id;
        int after = afterSlot?.item != null ? afterSlot.quantity : 0;

        // 아이템이 바뀌었거나(스왑) 사라졌으면 손을 통째로 비운다.
        if (afterId != beforeId) return int.MaxValue;
        return Mathf.Max(0, before - after);
    }

    void CodeHeldItem.IHost.HeldTrash(CodeSlotView source, int amount) => TrashSource(source, amount);

    bool CodeHeldItem.IHost.HeldTrashOne(CodeSlotView source)
    {
        if (source == null) return false;
        switch (source.zone)
        {
            case Zone.Warehouse:
                // 유물·장비는 고유라 하나씩도 버릴 수 없다 — 안내는 기존 경로에 맡긴다.
                if (WarehouseManager.IsRelicSlot(GetAt(_warehouse != null ? _warehouse.AllSlots : null, source.index)) ||
                    IsEquipmentSlot(_warehouse != null ? _warehouse.AllSlots : null, source.index))
                {
                    TrashSource(source, 1);
                    return false;
                }
                return DiscardOne(_warehouse != null ? _warehouse.AllSlots : null, source.index,
                    (idx, qty) => { _warehouse.RemoveItemAt(idx, qty); return true; });

            case Zone.BagItem:
                return DiscardOne(_itemInv != null ? _itemInv.ReadonlyItems : null, source.index,
                    (idx, qty) => _itemInv.RemoveItemAt(idx, qty));

            default:
                TrashSource(source, 1);
                return false;
        }
    }


    // ===================================================
    // 이동 로직
    // ===================================================
    /// <summary>
    /// 창고 슬롯 → 가방. 실제로 들어간 수량만 창고에서 뺀다.
    /// 가방 쪽 자리가 이미 차 있으면 그 물건을 창고로 돌려보내며 서로 맞바꾼다.
    /// </summary>
    /// <param name="targetZone">드롭으로 지정된 가방 구역 (클릭이면 -1 → 가방이 알아서 자리를 정한다)</param>
    /// <param name="targetIndex">드롭으로 지정된 가방 칸 인덱스</param>
    private void Withdraw(int warehouseIndex, int amount, int targetZone = -1, int targetIndex = -1)
    {
        if (_warehouse == null) return;

        var slots = _warehouse.AllSlots;
        if (warehouseIndex < 0 || warehouseIndex >= slots.Count) return;

        var slot = slots[warehouseIndex];
        if (slot == null || slot.item == null) return;

        int move = Mathf.Clamp(amount, 1, slot.quantity);

        switch (slot.item)
        {
            case ItemSO item when _itemInv != null:
            {
                // 떨어뜨린 칸에 다른 아이템이 있으면 맞바꾸기로 처리
                if (targetZone == Zone.BagItem && TrySwapWithBagItem(warehouseIndex, item, targetIndex)) return;

                int added = _itemInv.AddItem(item, move);
                if (added <= 0)
                {
                    // 빈 칸이 없으면 클릭으로도 맞바꿔준다 — 어느 칸인지 안 찍었으므로 첫 칸과 교환
                    if (TrySwapWithBagItem(warehouseIndex, item, 0)) return;
                    Toast("ui_wh_bag_full_item", "아이템 칸이 가득 찼습니다.", CodeUI.NegativeColor);
                    return;
                }
                _warehouse.RemoveItemAt(warehouseIndex, added);
                CodeUI.PlaySfx(moveSfxName);
                return;
            }

            // 광물은 창고 전용 — 가방으로 되꺼내는 흐름이 설계에 없으므로 막고 안내만 한다
            case MineralSO _:
                Toast("ui_wh_mineral_locked", "광물은 창고에 보관합니다. 상점에서 판매하세요.", CodeUI.MutedColor);
                return;

            case EquipmentSO equipment when _equipInv != null:
                WithdrawEquipment(warehouseIndex, equipment, targetZone, targetIndex);
                return;

            default:
                Toast("ui_wh_no_bag", "가방을 찾을 수 없습니다.", CodeUI.NegativeColor);
                return;
        }
    }

    /// <summary>
    /// 창고 장비 → 가방 장비/유물 칸. 그 부위에 이미 장비가 있으면 벗겨서 창고로 보내고 새 장비를 착용한다.
    /// (드래그로 특정 칸을 찍으면 그 칸, 클릭이면 EquipmentInventory가 정한 자리 — 유물은 빈 칸 우선)
    /// </summary>
    private void WithdrawEquipment(int warehouseIndex, EquipmentSO equipment, int targetZone, int targetIndex)
    {
        int slotIndex = (targetZone == Zone.BagEquipment
                         && EquipmentInventory.IsSlotForType(targetIndex, equipment.equipmentType))
            ? targetIndex
            : _equipInv.FindSlotForAdd(equipment);

        if (slotIndex < 0 || !EquipmentInventory.IsSlotForType(slotIndex, equipment.equipmentType))
        {
            Toast("ui_wh_equip_bad_slot", "이 장비를 넣을 수 있는 칸이 아닙니다.", CodeUI.NegativeColor);
            return;
        }

        var bagSlot = GetAt(_equipInv.ReadonlyItems, slotIndex);
        var worn = bagSlot?.item as EquipmentSO;

        // 같은 장비면 맞바꿀 게 없다 — 스택형 유물의 수량만 늘린다
        if (worn != null && worn.Id == equipment.Id)
        {
            int added = _equipInv.AddItem(equipment, 1);
            if (added <= 0) { Toast("ui_wh_equip_full", "해당 부위에 이미 장비가 있습니다.", CodeUI.NegativeColor); return; }
            _warehouse.RemoveItemAt(warehouseIndex, added);
            CodeUI.PlaySfx(moveSfxName);
            return;
        }

        int wornQty = worn != null ? Mathf.Max(1, bagSlot.quantity) : 0;

        // 벗긴 장비를 창고에 돌려놓을 자리를 먼저 확인한다 — 확인 없이 벗기면 장비가 사라진다.
        // 꺼내는 칸이 이 장비 1개뿐이면 그 칸이 비므로 자리로 셀 수 있다.
        bool sourceFrees = _warehouse.AllSlots[warehouseIndex].quantity <= 1;
        if (worn != null && !HasWarehouseRoomFor(worn, wornQty, sourceFrees ? warehouseIndex : -1))
        {
            Toast("ui_wh_full", "창고가 가득 찼습니다.", CodeUI.NegativeColor);
            return;
        }

        _warehouse.RemoveItemAt(warehouseIndex, 1);
        if (!_equipInv.EquipAt(slotIndex, equipment, out var previous))
        {
            _warehouse.AddEquipment(equipment, 1); // 착용 실패 → 창고로 되돌린다
            Toast("ui_wh_equip_bad_slot", "이 장비를 넣을 수 있는 칸이 아닙니다.", CodeUI.NegativeColor);
            return;
        }

        if (previous != null) _warehouse.AddEquipment(previous, wornQty);
        CodeUI.PlaySfx(moveSfxName);
    }

    /// <summary>
    /// 창고 아이템 ↔ 가방 아이템 칸 맞바꾸기. 대상 칸이 비었거나 같은 아이템이면 false(일반 경로로).
    /// 창고가 가득 차 못 바꾼 경우도 안내만 하고 true — 일반 경로로 흘러가 중복 처리되면 안 된다.
    /// </summary>
    private bool TrySwapWithBagItem(int warehouseIndex, ItemSO item, int bagIndex)
    {
        var bagSlot = GetAt(_itemInv?.ReadonlyItems, bagIndex);
        var held = bagSlot?.item as ItemSO;
        if (held == null || held.Id == item.Id) return false;

        int heldQty = Mathf.Max(1, bagSlot.quantity);
        bool sourceFrees = _warehouse.AllSlots[warehouseIndex].quantity <= 1;
        if (!HasWarehouseRoomFor(held, heldQty, sourceFrees ? warehouseIndex : -1))
        {
            Toast("ui_wh_full", "창고가 가득 찼습니다.", CodeUI.NegativeColor);
            return true;
        }

        _itemInv.RemoveItemAt(bagIndex, heldQty);
        if (!_itemInv.PlaceAt(bagIndex, item, 1))
        {
            _itemInv.PlaceAt(bagIndex, held, heldQty); // 되돌리기
            return true;
        }

        _warehouse.RemoveItemAt(warehouseIndex, 1);
        _warehouse.AddItem(held, heldQty);
        CodeUI.PlaySfx(moveSfxName);
        return true;
    }

    /// <summary>가방 슬롯 → 창고. 창고가 가득 차면 넣지 않는다.</summary>
    private void Deposit(int zone, int bagIndex, int amount)
    {
        if (_warehouse == null) return;
        if (!HasWarehouseRoomFor(zone, bagIndex, amount))
        {
            Toast("ui_wh_full", "창고가 가득 찼습니다.", CodeUI.NegativeColor);
            return;
        }

        switch (zone)
        {
            case Zone.BagItem:
            {
                var list = _itemInv?.ReadonlyItems;
                if (list == null || bagIndex < 0 || bagIndex >= list.Count) return;
                var item = list[bagIndex]?.item as ItemSO;
                if (item == null) return;

                int move = Mathf.Clamp(amount, 1, list[bagIndex].quantity);
                _warehouse.AddItem(item, move);
                _itemInv.RemoveItemAt(bagIndex, move);
                break;
            }
            case Zone.BagEquipment:
            {
                var list = _equipInv?.ReadonlyItems;
                if (list == null || bagIndex < 0 || bagIndex >= list.Count) return;
                var equipment = list[bagIndex]?.item as EquipmentSO;
                if (equipment == null) return;

                _warehouse.AddEquipment(equipment, 1);
                _equipInv.RemoveItemAt(bagIndex, 1);
                break;
            }
            default:
                return;
        }

        CodeUI.PlaySfx(moveSfxName);
    }

    /// <summary>
    /// 가방 칸의 물건을 창고에 넣을 자리가 있는지 미리 확인.
    /// WarehouseManager는 자리가 없으면 들어간 만큼만 처리하므로,
    /// 가방에서 빼기 전에 여기서 막아야 "빠졌는데 안 들어간" 상태가 안 생긴다.
    /// </summary>
    private bool HasWarehouseRoomFor(int zone, int bagIndex, int amount)
    {
        if (_warehouse == null) return false;

        InventorySlot bagSlot = zone switch
        {
            Zone.BagItem => GetAt(_itemInv?.ReadonlyItems, bagIndex),
            Zone.BagEquipment => GetAt(_equipInv?.ReadonlyItems, bagIndex),
            _ => null
        };
        if (bagSlot?.item == null) return false;

        return HasWarehouseRoomFor(bagSlot.item, Mathf.Clamp(amount, 1, bagSlot.quantity));
    }

    /// <summary>
    /// 창고에 이 물건을 <paramref name="need"/>개 넣을 자리가 있는지.
    /// </summary>
    /// <param name="freedIndex">
    /// 넣기 직전에 비워질 창고 칸(맞바꾸기의 원본 칸). 지금은 차 있어도 빈 칸으로 세어준다. 없으면 -1.
    /// </param>
    private bool HasWarehouseRoomFor(InterfaceInventoryItem item, int need, int freedIndex = -1)
    {
        if (_warehouse == null || item == null || need <= 0) return false;

        var slots = _warehouse.AllSlots;

        // 1) 기존 스택에 들어갈 여유
        if (item.Stackable)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (i == freedIndex) continue; // 곧 비는 칸은 스택 대상이 아니다
                var s = slots[i];
                if (s?.item == null || s.item.Id != item.Id) continue;
                need -= Mathf.Max(0, item.MaxStackSize - s.quantity);
                if (need <= 0) return true;
            }
        }

        // 2) 빈 칸
        int perSlot = item.Stackable ? Mathf.Max(1, item.MaxStackSize) : 1;
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (i != freedIndex && s != null && s.item != null) continue;
            need -= perSlot;
            if (need <= 0) return true;
        }

        return false;
    }

    private static InventorySlot GetAt(IReadOnlyList<InventorySlot> list, int index) =>
        (list != null && index >= 0 && index < list.Count) ? list[index] : null;

    // ===================================================
    // 토스트 안내
    // ===================================================
    private void Toast(string key, string fallback, Color color) => ToastRaw(CodeUI.L(key, fallback), color);

    /// <summary>이미 완성된 문장을 그대로 띄운다 (물건 이름을 끼워 넣은 문구 등).</summary>
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
}
