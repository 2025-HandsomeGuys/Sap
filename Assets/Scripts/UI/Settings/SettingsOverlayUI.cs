// @tags: settings, ui, overlay, blur, sound, graphics, language, controls, panel
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 설정 오버레이 — 호출한 화면 위에 덧씌워지는 설정 패널.
/// 열리는 순간의 화면을 캡처해 다운샘플 블러 + 어둡게 깔고, 그 위에 패널을 띄운다.
/// UI는 전부 코드로 생성(DaySummaryUI 패턴) — 씬/프리팹 세팅 없이 SettingsOverlayUI.Open() 호출만으로 동작.
/// 파라미터(블러 강도·어둡기·조작키 목록 등)를 인스펙터에서 조정하고 싶으면
/// 아무 씬에나 빈 오브젝트에 이 컴포넌트를 미리 붙여두면 된다(싱글톤이 그 인스턴스를 사용).
///
/// 값 저장은 전부 기존 매니저에 위임:
///  - 볼륨: SoundManager (AudioMixer 노출 파라미터 MasterVolume/BGMVolume/SFXVolume)
///  - 해상도/전체화면/VSync/프레임 제한: GraphicSettingsManager (PlayerPrefs)
///  - 언어: LanguageManager (PlayerPrefs + OnLanguageChanged 이벤트)
/// </summary>
public class SettingsOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static SettingsOverlayUI _instance;

    /// <summary>씬에 미리 배치된 인스턴스가 있으면 그것을, 없으면 자동 생성. 에디터 배치는 선택 사항.</summary>
    public static SettingsOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SettingsOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("SettingsOverlayUI");
                    _instance = go.AddComponent<SettingsOverlayUI>();
                }
            }
            return _instance;
        }
    }

    /// <summary>설정 오버레이가 현재 열려 있는지 (UIStateManager의 전역 단축키 차단용).</summary>
    public static bool IsOpen => _instance != null && _instance._isOpen;

    /// <summary>ESC로 닫힌 바로 그 프레임인지 — 같은 프레임에 UIStateManager가 ESC를 중복 처리하는 것 방지.</summary>
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    // ===================================================
    // 인스펙터 조정값 (씬에 미리 배치했을 때만 노출)
    // ===================================================
    [Header("배경 처리")]
    [Tooltip("배경을 덮는 검은 막의 알파 (0 = 안 어두움, 1 = 완전 검정)")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.45f;

    [Tooltip("블러 강도 — 캡처 화면을 절반씩 줄이는 횟수. 클수록 뭉개짐 (3~4 권장)")]
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;

    [Tooltip("블러 배경이 상하 반전되어 보일 때 체크 (그래픽 API에 따라 다를 수 있음)")]
    [SerializeField] private bool flipBlurVertically = false;

    [Header("패널 레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 760f;
    [Tooltip("패널의 화면 위/아래 여백")]
    [SerializeField] private float panelMarginY = 40f;
    [Tooltip("캔버스 정렬 순서 — 게임 UI(≈1000)보다 위, DaySummary(32000)보다 아래")]
    [SerializeField] private int sortingOrder = 31000;

    [Header("도트 스프라이트 (선택 — 비우면 기존 코드 생성 라운드 스타일)")]
    [Tooltip("테두리: 메인 패널·제목 카드의 프레임. 9-슬라이스 임포트한 도트 스프라이트 권장")]
    [SerializeField] private Sprite borderSprite;
    [Tooltip("탭: 각 설정 섹션(사운드·그래픽·언어·조작) 카드 배경")]
    [SerializeField] private Sprite tabSprite;
    [Tooltip("버튼: 화살표(◀▶)·닫기 버튼")]
    [SerializeField] private Sprite buttonSprite;
    [Tooltip("값 상자: 셀렉터(해상도·언어 등) 값 표시 상자")]
    [SerializeField] private Sprite boxSprite;
    [Tooltip("선택 강조: 키보드/마우스 포커스가 있는 줄을 감싸는 이미지. 흰색 꽉 찬 스프라이트를 넣어도 된다 " +
             "— focusSpriteAlpha만큼 자동으로 투명해지고 줄 내용 뒤에 깔린다. 비우면 코드 생성 라운드 테두리")]
    [SerializeField] private Sprite focusSprite;
    [Tooltip("선택 강조 스프라이트의 불투명도. 1 = 불투명(줄 내용이 가려짐), 0.25~0.4 권장")]
    [Range(0f, 1f)][SerializeField] private float focusSpriteAlpha = 0.3f;
    [Tooltip("선택 테두리를 항목보다 얼마나 키울지 (x=좌우, y=위아래, px). 음수면 항목 안쪽에 그린다. " +
             "스프라이트에 여백이 있으면 줄여서 맞춘다")]
    [SerializeField] private Vector2 focusPadding = new Vector2(8f, 3f);
    [Tooltip("도트 스프라이트 9-슬라이스 픽셀 배율. 값이 클수록 테두리(모서리)가 작게, 작을수록 크게 렌더된다")]
    [Range(0.25f, 8f)][SerializeField] private float spritePixelsPerUnit = 1f;
    [Tooltip("체크 시 스프라이트에 팔레트 색을 곱한다(흰색 9-슬라이스용). 이미 색이 입혀진 도트 아트면 해제")]
    [SerializeField] private bool tintSprites = true;

    [Header("그래픽 섹션")]
    [Tooltip("프레임 제한 줄 표시 여부 (VSync ON일 땐 프레임 제한이 무시되므로 숨겨도 됨)")]
    [SerializeField] private bool showFrameRateRow = true;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;
    [Tooltip("ESC로 닫거나 취소할 때 나는 소리")]
    [SerializeField] private string backSfxName = SfxKeys.UiBack;

    [Header("글리프 (폰트에 해당 문자가 없어 □로 보이면 교체)")]
    [Tooltip("섹션 헤더 다이아몬드. ◀▶ 화살표는 폰트 의존을 없애려 삼각형 스프라이트로 그린다.")]
    [SerializeField] private string sectionDiamondGlyph = "◆";

    [Header("조작 섹션 — 표시할 키 목록")]
    [SerializeField]
    private List<ControlRowDef> controlRows = new List<ControlRowDef>
    {
        new ControlRowDef("ui_settings_key_move",      "이동",        "A · D", ""),
        new ControlRowDef("ui_settings_key_jump",      "점프",        "Space", ""),
        new ControlRowDef("ui_settings_key_mine",      "채굴 / 공격", "좌클릭", "ui_settings_key_lclick"),
        new ControlRowDef("ui_settings_key_interact",  "상호작용",    InteractionKeys.InteractLabel, ""),
        new ControlRowDef("ui_settings_key_inventory", "인벤토리",    "Tab", ""),
        new ControlRowDef("ui_settings_key_quest",     "퀘스트 로그", "J", ""),
        new ControlRowDef("ui_settings_key_pause",     "일시정지",    "ESC", ""),
    };

    /// <summary>조작 섹션 한 줄 정의. keyLocKey가 비어있지 않으면 키 표기도 로컬라이즈("좌클릭" 등).</summary>
    [System.Serializable]
    public class ControlRowDef
    {
        [Tooltip("왼쪽 라벨의 Localization key")]
        public string labelKey;
        [Tooltip("Localization 미등록 시 표시할 한국어 폴백")]
        public string labelFallback;
        [Tooltip("오른쪽 키캡에 표시할 문자열 (예: Space, A · D)")]
        public string keyText;
        [Tooltip("키캡 문자열도 번역이 필요하면 Localization key 지정 (예: ui_settings_key_lclick)")]
        public string keyLocKey;

        public ControlRowDef(string labelKey, string labelFallback, string keyText, string keyLocKey)
        {
            this.labelKey = labelKey;
            this.labelFallback = labelFallback;
            this.keyText = keyText;
            this.keyLocKey = keyLocKey;
        }
    }

    // ===================================================
    // 팔레트 (이미지 시안 기준 다크 네이비)
    // ===================================================
    private static readonly Color PanelBg = FromHex(0x111A2E);
    private static readonly Color SectionBg = FromHex(0x18223C);
    private static readonly Color BoxBg = FromHex(0x0B1224);
    private static readonly Color LabelColor = FromHex(0xC7CFE2);
    private static readonly Color DividerColor = FromHex(0x2A3452);
    private static readonly Color ArrowBg = FromHex(0x232D4A);
    private static readonly Color ArrowFg = FromHex(0xAEB8D8);
    private static readonly Color AccentFill = FromHex(0x7B8FD4);
    private static readonly Color SwitchOff = FromHex(0x2A3452);
    private static readonly Color KeyCapBg = FromHex(0xE9EDF7);
    private static readonly Color KeyCapFg = FromHex(0x1B2338);
    private static readonly Color CloseBg = FromHex(0xF5C63F);
    private static readonly Color CloseFg = FromHex(0x33270B);
    private static readonly Color CancelBg = FromHex(0x3A4668); // 취소 버튼 — 중립 회색톤
    private static readonly Color ScrollHandle = FromHex(0x3A4668);
    private static readonly Color NavFocusColor = FromHex(0x9FE3F5); // 키보드 포커스 테두리

    private static readonly Color DiamondSound = FromHex(0xF5C63F);
    private static readonly Color DiamondGraphic = FromHex(0x7B8FD4);
    private static readonly Color DiamondLanguage = FromHex(0xF09A3E);
    private static readonly Color DiamondControl = FromHex(0x58C275);

    private const float RowHeight = 52f;
    private const float FadeDuration = 0.15f;

    private static Color FromHex(int rgb) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built;
    private bool _isOpen;
    private bool _opening; // 캡처 대기 중 중복 Open 방지 (중복 시 _prevTimeScale이 0으로 덮임)
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private Image _dimImage;
    private RenderTexture _blurRT;
    private ScrollRect _scrollRect;

    // 언어 변경 시 갱신할 텍스트들
    private class LocText { public TextMeshProUGUI tmp; public string key; public string fallback; }
    private readonly List<LocText> _locTexts = new List<LocText>();
    private readonly List<TextMeshProUGUI> _allTexts = new List<TextMeshProUGUI>();

    // 값 동기화 대상 위젯들
    private class SliderRef { public Slider slider; public TextMeshProUGUI value; public System.Func<float> get; public System.Action<float> set; }
    private class SelectorRef { public TextMeshProUGUI value; public System.Func<string> text; }
    private class SwitchRef { public Image bg; public RectTransform knob; public System.Func<bool> get; public System.Action<bool> set; public bool state; }
    private readonly List<SliderRef> _sliders = new List<SliderRef>();
    private readonly List<SelectorRef> _selectors = new List<SelectorRef>();
    private readonly List<SwitchRef> _switches = new List<SwitchRef>();

    // ── 키보드 내비게이션 (W/S 이동, A/D 값 조절, E 선택) ──
    // 항목은 UI를 만드는 순서(=화면에 보이는 순서)대로 등록되므로 별도 정렬이 필요 없다.
    // group이 같은 항목끼리는 한 줄에 나란히 있는 것으로 보고 A/D가 '값 조절'이 아니라 '항목 이동'이 된다(취소/적용).
    private class NavItem
    {
        public int group;
        public RectTransform target;           // 포커스 테두리를 씌울 영역
        public Vector2 pad;                    // 테두리를 항목보다 얼마나 키울지 (음수 = 안쪽으로)
        public System.Action<int> adjust;      // A/D — dir: -1 왼쪽, +1 오른쪽
        public System.Action activate;         // E
    }
    private readonly List<NavItem> _nav = new List<NavItem>();
    private int _navGroupSeq;
    private int _navIndex = -1;
    private Image _navHighlight;

    // 꾹 누름 반복 — A/D는 값 굴리기, W/S는 줄 이동. 줄 이동은 값 조절보다 느리게(칸을 건너뛰지 않도록).
    private const float NavRepeatDelay = 0.35f;
    private const float NavRepeatInterval = 0.07f;
    private const float NavVertRepeatInterval = 0.12f;
    private int _navHeldDir;
    private float _navHeldTimer;
    private int _navVertDir;
    private float _navVertTimer;

    // 오버레이가 열려 있는 동안 꺼두는 유니티 기본 내비게이션 (닫을 때 원복)
    private EventSystem _navEventSystem;
    private bool _prevSendNavEvents;

    private bool _langSubscribed;

    // 대기(pending) 설정 값 — Apply 전까지 실제 매니저/PlayerPrefs에 반영하지 않는다.
    // 위젯의 get/set은 전부 이 값만 읽고 쓴다. 취소하면 그냥 닫히고(아무것도 적용 안 함),
    // 다음에 열 때 LoadPending()이 현재 매니저 값으로 다시 채운다.
    private float _pMaster, _pBgm, _pSfx;
    private int _pResIndex;
    private bool _pFullscreen;
    private bool _pVsync;
    private int _pFrameRate;
    private int _pLangIndex;

    // 기준(baseline) 값 — LoadPending 시점의 값. 대기값이 이와 다르면 '변경됨'으로 보고
    // '적용' 버튼을 노란색으로 강조한다. Apply/재로드 후 다시 갱신된다.
    private float _bMaster, _bBgm, _bSfx;
    private int _bResIndex;
    private bool _bFullscreen;
    private bool _bVsync;
    private int _bFrameRate;
    private int _bLangIndex;

    // '적용' 버튼 시각 갱신 대상 + 마지막으로 반영한 dirty 상태(중복 갱신 방지)
    private Image _applyImage;
    private TextMeshProUGUI _applyText;
    private bool _applyDirty;
    private bool _applyStateInitialized;

    private static readonly List<int> FrameRateOptions = new List<int> { 30, 60, 120, 144, 240, -1 };
    private static readonly string[] LanguageNames = { "한국어", "English", "中文" }; // 언어명은 해당 언어로 고정 표기

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    /// <summary>현재 화면 위에 설정 오버레이를 띄운다. (GameManager.OpenSettings()가 호출)</summary>
    public static void Open()
    {
        if (IsOpen || Instance._opening) return;
        Instance.StartCoroutine(Instance.OpenRoutine());
    }

    /// <summary>오버레이를 실제로 숨기고 시간을 복원한다(적용/취소 공통).</summary>
    private void Hide(bool isBack = false)
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        Time.timeScale = _prevTimeScale;
        _navHeldDir = _navVertDir = 0;

        // 껐던 유니티 기본 내비게이션 원복 (다른 UI가 키보드 조작을 쓸 수 있어야 한다)
        if (_navEventSystem != null)
        {
            _navEventSystem.sendNavigationEvents = _prevSendNavEvents;
            _navEventSystem = null;
        }

        // 미리듣기로만 바꿔둔 믹서 값을 저장값으로 되돌린다. 적용으로 닫혔다면
        // ApplyPending이 이미 PlayerPrefs에 썼으므로 같은 값이 다시 걸린다(무해).
        // 닫는 소리보다 먼저 해야 그 소리부터 확정된 볼륨으로 난다.
        RestoreSavedVolumes();

        if (isBack) PlayBack();
        else PlayClick();

        if (_canvasObj != null) _canvasObj.SetActive(false);
        ReleaseBlur();
    }

    /// <summary>적용 — 대기 중 설정을 실제 매니저/PlayerPrefs에 반영하고 닫는다.</summary>
    public void Apply()
    {
        if (!_isOpen) return;
        ApplyPending();
        Hide(isBack: false);
    }

    /// <summary>취소 — 아무것도 적용하지 않고 닫는다. (대기값은 다음 열 때 현재값으로 재로드)</summary>
    public void Cancel()
    {
        Hide(isBack: true);
    }

    /// <summary>외부 호환용 — 취소와 동일(변경사항 미적용, ui_back 사운드 재생).</summary>
    public void Close() => Cancel();

    /// <summary>현재 매니저 값을 대기 상태로 불러온다(열 때 1회). 위젯은 이 값을 표시·수정한다.</summary>
    private void LoadPending()
    {
        var sm = SoundManager.Instance;
        _pMaster = sm != null ? sm.GetMasterVolume() : PlayerPrefs.GetFloat("MasterVolume", 1f);
        _pBgm = sm != null ? sm.GetBGMVolume() : PlayerPrefs.GetFloat("BGMVolume", 1f);
        _pSfx = sm != null ? sm.GetSFXVolume() : PlayerPrefs.GetFloat("SFXVolume", 1f);

        var gm = GraphicSettingsManager.Instance;
        if (gm != null)
        {
            _pResIndex = gm.GetCurrentResolutionIndex();
            _pFullscreen = gm.GetIsFullscreen();
            _pVsync = gm.GetVSync();
            _pFrameRate = gm.GetCurrentFrameRate();
        }
        else
        {
            _pResIndex = 0;
            _pFullscreen = Screen.fullScreen;
            _pVsync = QualitySettings.vSyncCount > 0;
            _pFrameRate = Application.targetFrameRate;
        }

        _pLangIndex = LanguageManager.Instance != null ? (int)LanguageManager.Instance.CurrentLanguage : 0;

        // 방금 불러온 대기값을 기준값으로 복사 — 이후 사용자가 바꾸면 '변경됨'으로 판정.
        _bMaster = _pMaster; _bBgm = _pBgm; _bSfx = _pSfx;
        _bResIndex = _pResIndex; _bFullscreen = _pFullscreen; _bVsync = _pVsync;
        _bFrameRate = _pFrameRate; _bLangIndex = _pLangIndex;
    }

    /// <summary>대기값이 기준값과 다른지(사용자가 뭔가 바꿨는지).</summary>
    private bool HasPendingChanges()
    {
        const float eps = 0.0001f;
        return Mathf.Abs(_pMaster - _bMaster) > eps
            || Mathf.Abs(_pBgm - _bBgm) > eps
            || Mathf.Abs(_pSfx - _bSfx) > eps
            || _pResIndex != _bResIndex
            || _pFullscreen != _bFullscreen
            || _pVsync != _bVsync
            || _pFrameRate != _bFrameRate
            || _pLangIndex != _bLangIndex;
    }

    /// <summary>'적용' 버튼 색/텍스트를 변경 여부에 맞춰 갱신. 변경 있음=노란색, 없음=취소 버튼과 동일.</summary>
    private void RefreshApplyState()
    {
        if (_applyImage == null) return;
        bool dirty = HasPendingChanges();
        if (_applyStateInitialized && dirty == _applyDirty) return;
        _applyDirty = dirty;
        _applyStateInitialized = true;

        // CreateImage의 색 적용 규칙과 동일: 인스펙터 skin(buttonSprite)을 쓰고 tintSprites=false면 흰색 유지.
        bool tinted = buttonSprite == null || tintSprites;
        Color bg = dirty ? CloseBg : CancelBg;
        _applyImage.color = tinted ? bg : Color.white;
        if (_applyText != null) _applyText.color = dirty ? CloseFg : Color.white;
    }

    /// <summary>대기 상태를 실제 매니저에 커밋한다(Apply에서만 호출).</summary>
    private void ApplyPending()
    {
        var sm = SoundManager.Instance;
        if (sm != null)
        {
            sm.SetMasterVolume(_pMaster);
            sm.SetBGMVolume(_pBgm);
            sm.SetSFXVolume(_pSfx);
        }

        var gm = GraphicSettingsManager.Instance;
        if (gm != null)
        {
            gm.SetResolution(_pResIndex, _pFullscreen); // 해상도+전체화면 함께 적용
            gm.SetVSync(_pVsync);
            gm.SetFrameRate(_pFrameRate);
        }

        var lm = LanguageManager.Instance;
        if (lm != null && (int)lm.CurrentLanguage != _pLangIndex)
            lm.SetLanguage(_pLangIndex); // OnLanguageChanged → RefreshTexts (닫히기 직전이라 무해)

        PlayerPrefs.Save();
    }

    private IEnumerator OpenRoutine()
    {
        _opening = true;
        EnsureBuilt();
        EnsureEventSystem();

        // 오버레이가 캡처에 찍히지 않도록 끈 채로 프레임 끝까지 대기 후 캡처
        _canvasObj.SetActive(false);
        yield return new WaitForEndOfFrame();
        CaptureBlur();
        _opening = false;

        _prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        _isOpen = true;

        TrySubscribeLanguage();
        LoadPending();   // 위젯이 읽을 대기값을 현재 매니저 값으로 채운다
        RefreshTexts();
        SyncValues();
        _applyStateInitialized = false; // 열 때마다 강제로 한 번 갱신되도록
        RefreshApplyState();            // 방금 로드 → 변경 없음 → 취소 버튼과 같은 색으로 시작

        _canvasObj.SetActive(true);
        if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;

        // 키보드 포커스를 맨 첫 항목으로 (레이아웃이 잡힌 뒤여야 테두리 위치가 맞는다)
        Canvas.ForceUpdateCanvases();
        _navHeldDir = _navVertDir = 0;
        _navIndex = -1;
        SetNavIndex(0, false);

        // 유니티 기본 내비게이션(Move/Submit)을 끈다.
        _navEventSystem = EventSystem.current;
        if (_navEventSystem != null)
        {
            _prevSendNavEvents = _navEventSystem.sendNavigationEvents;
            _navEventSystem.sendNavigationEvents = false;
            _navEventSystem.SetSelectedGameObject(null);
        }

        // 페이드인 (timeScale=0이므로 unscaled)
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

        // 대기값이 바뀌면(슬라이더·셀렉터·토글 어디서든) '적용' 버튼을 노란색으로 강조.
        RefreshApplyState();

        // 선택 상태가 남아 있으면 버튼·슬라이더가 계속 하이라이트 색으로 보인다(포커스는 우리가 관리).
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null)
            es.SetSelectedGameObject(null);

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        HandleNavInput();
    }

    // ===================================================
    // 키보드 내비게이션
    // ===================================================
    /// <summary>W/S 항목 이동, A/D 값 조절(또는 같은 줄 항목 이동), Space 선택.</summary>
    private void HandleNavInput()
    {
        if (_nav.Count == 0) return;

        // W/S — 누르는 순간 1칸, 계속 누르고 있으면 반복해서 쭉 이동
        int vDir = 0;
        if (Input.GetKeyDown(KeyCode.W)) vDir = -1;
        else if (Input.GetKeyDown(KeyCode.S)) vDir = +1;

        if (vDir != 0)
        {
            _navVertDir = vDir;
            _navVertTimer = NavRepeatDelay;
            MoveNav(vDir);
            return;
        }

        if (_navVertDir != 0)
        {
            bool vHeld = _navVertDir < 0 ? Input.GetKey(KeyCode.W) : Input.GetKey(KeyCode.S);
            if (!vHeld)
            {
                _navVertDir = 0;
            }
            else
            {
                _navVertTimer -= Time.unscaledDeltaTime; // timeScale=0이므로 unscaled
                if (_navVertTimer <= 0f)
                {
                    _navVertTimer = NavVertRepeatInterval;
                    MoveNav(_navVertDir);
                }
            }
        }

        // 확인/선택은 스페이스바
        if (Input.GetKeyDown(KeyCode.Space))
        {
            var cur = CurrentNav();
            cur?.activate?.Invoke();
            return;
        }

        // A/D — 누르는 순간 1회, 계속 누르고 있으면 반복
        int dir = 0;
        if (Input.GetKeyDown(KeyCode.A)) dir = -1;
        else if (Input.GetKeyDown(KeyCode.D)) dir = +1;

        if (dir != 0)
        {
            _navHeldDir = dir;
            _navHeldTimer = NavRepeatDelay;
            AdjustOrMove(dir);
            return;
        }

        if (_navHeldDir != 0)
        {
            bool stillHeld = _navHeldDir < 0 ? Input.GetKey(KeyCode.A) : Input.GetKey(KeyCode.D);
            if (!stillHeld) { _navHeldDir = 0; return; }

            _navHeldTimer -= Time.unscaledDeltaTime; // timeScale=0이므로 unscaled
            if (_navHeldTimer <= 0f)
            {
                _navHeldTimer = NavRepeatInterval;
                AdjustOrMove(_navHeldDir);
            }
        }
    }

    private NavItem CurrentNav() =>
        (_navIndex >= 0 && _navIndex < _nav.Count) ? _nav[_navIndex] : null;

    /// <summary>
    /// W/S — 다음/이전 '줄'로 이동. 같은 group(취소·적용)은 한 줄로 묶어 통째로 건너뛴다.
    /// 순환하지 않는다 — 맨 위에서 W를 더 누르면 제자리, 맨 아래도 마찬가지.
    /// </summary>
    private void MoveNav(int dir)
    {
        int cur = Mathf.Clamp(_navIndex, 0, _nav.Count - 1);
        int group = _nav[cur].group;

        int i = cur;
        while (true)
        {
            int next = i + dir;
            if (next < 0 || next >= _nav.Count) return; // 끝에 닿음 — 제자리
            i = next;
            if (_nav[i].group != group) break;
        }

        // 도착한 줄의 첫 항목으로 맞춘다
        int landed = _nav[i].group;
        while (i > 0 && _nav[i - 1].group == landed) i--;

        SetNavIndex(i, true);
    }

    /// <summary>A/D — 같은 줄에 다른 항목이 있으면 항목 이동, 아니면 그 항목의 값 조절.</summary>
    private void AdjustOrMove(int dir)
    {
        var cur = CurrentNav();
        if (cur == null) return;

        int nb = _navIndex + dir;
        if (nb >= 0 && nb < _nav.Count && _nav[nb].group == cur.group)
        {
            SetNavIndex(nb, true);
            return;
        }

        cur.adjust?.Invoke(dir);
    }

    /// <summary>padX/padY를 생략하면 인스펙터 focusPadding을 쓴다.</summary>
    private void RegisterNav(RectTransform target, System.Action<int> adjust, System.Action activate,
        int group = -1, float padX = float.NaN, float padY = float.NaN)
    {
        if (float.IsNaN(padX)) padX = focusPadding.x;
        if (float.IsNaN(padY)) padY = focusPadding.y;

        int index = _nav.Count;
        _nav.Add(new NavItem
        {
            group = group >= 0 ? group : _navGroupSeq++,
            target = target,
            pad = new Vector2(padX, padY),
            adjust = adjust,
            activate = activate,
        });

        if (target.GetComponent<Graphic>() == null)
        {
            var hit = target.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
        }
        var hover = target.gameObject.AddComponent<SettingsNavHover>();
        hover.Bind(this, index);
    }

    /// <summary>마우스가 항목 위에 올라왔을 때 — 포커스만 옮기고 소리·자동 스크롤은 하지 않는다.</summary>
    internal void FocusFromPointer(int index) => SetNavIndex(index, playSfx: false, scroll: false);

    /// <summary>포커스를 옮기고 테두리를 그 항목 위로 이동시킨다.</summary>
    private void SetNavIndex(int index, bool playSfx, bool scroll = true)
    {
        if (_nav.Count == 0 || _navHighlight == null) return;
        index = Mathf.Clamp(index, 0, _nav.Count - 1);

        bool changed = index != _navIndex;
        _navIndex = index;
        if (changed) _navHeldDir = 0; // 포커스가 바뀌면 꾹 누름 반복은 끊는다

        var item = _nav[index];
        if (item.target == null) return;

        var rt = _navHighlight.rectTransform;
        if (changed || rt.parent != item.target)
        {
            rt.SetParent(item.target, false);
            if (focusSprite != null) rt.SetAsFirstSibling();
            else rt.SetAsLastSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-item.pad.x, -item.pad.y);
            rt.offsetMax = new Vector2(item.pad.x, item.pad.y);
        }
        _navHighlight.gameObject.SetActive(true);

        if (scroll) ScrollNavIntoView(item.target);
        if (changed && playSfx) PlayClick();
    }

    /// <summary>포커스된 항목이 스크롤 영역 밖이면 보이도록 스크롤.</summary>
    private void ScrollNavIntoView(RectTransform target)
    {
        if (_scrollRect == null || _scrollRect.content == null || _scrollRect.viewport == null) return;
        if (!target.IsChildOf(_scrollRect.content)) return;

        Canvas.ForceUpdateCanvases();
        var content = _scrollRect.content;
        float contentH = content.rect.height;
        float viewH = _scrollRect.viewport.rect.height;
        float scrollable = contentH - viewH;
        if (scrollable <= 1f) return;

        Vector3 world = target.TransformPoint(target.rect.center);
        float centerFromTop = -content.InverseTransformPoint(world).y;
        float half = target.rect.height * 0.5f;

        const float topMargin = 84f;
        const float bottomMargin = 24f;

        float curTop = (1f - _scrollRect.verticalNormalizedPosition) * scrollable;
        float min = centerFromTop + half + bottomMargin - viewH;
        float max = centerFromTop - half - topMargin;
        if (min > max) min = max;

        float top = Mathf.Clamp(Mathf.Clamp(curTop, min, max), 0f, scrollable);
        _scrollRect.verticalNormalizedPosition = 1f - top / scrollable;
    }

    private void OnDestroy()
    {
        if (_langSubscribed && LanguageManager.Instance != null)
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
        ReleaseBlur();
    }

    // ===================================================
    // 배경 캡처 + 다운샘플 블러
    // ===================================================
    private void CaptureBlur()
    {
        ReleaseBlur();

        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();

        int w = Mathf.Max(shot.width >> 1, 8);
        int h = Mathf.Max(shot.height >> 1, 8);
        RenderTexture cur = RenderTexture.GetTemporary(w, h, 0);
        cur.filterMode = FilterMode.Bilinear;
        Graphics.Blit(shot, cur);

        for (int i = 1; i < blurDownsamples; i++)
        {
            w = Mathf.Max(w >> 1, 8);
            h = Mathf.Max(h >> 1, 8);
            var next = RenderTexture.GetTemporary(w, h, 0);
            next.filterMode = FilterMode.Bilinear;
            Graphics.Blit(cur, next);
            RenderTexture.ReleaseTemporary(cur);
            cur = next;
        }

        var up = RenderTexture.GetTemporary(w * 2, h * 2, 0);
        up.filterMode = FilterMode.Bilinear;
        Graphics.Blit(cur, up);
        RenderTexture.ReleaseTemporary(cur);

        _blurRT = up;
        _blurImage.texture = _blurRT;
        _blurImage.uvRect = flipBlurVertically ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
        Destroy(shot);
    }

    private void ReleaseBlur()
    {
        if (_blurRT != null)
        {
            if (_blurImage != null) _blurImage.texture = null;
            RenderTexture.ReleaseTemporary(_blurRT);
            _blurRT = null;
        }
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        // ── 캔버스 ──
        _canvasObj = new GameObject("SettingsCanvas");
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

        // ── 블러 배경 (캡처 화면) ──
        var blurObj = new GameObject("BlurBackdrop");
        blurObj.transform.SetParent(_canvasObj.transform, false);
        _blurImage = blurObj.AddComponent<RawImage>();
        StretchFull(_blurImage.rectTransform);
        _blurImage.raycastTarget = true;

        // ── 어둡게 덮는 막 ──
        var dimObj = new GameObject("Dim");
        dimObj.transform.SetParent(_canvasObj.transform, false);
        _dimImage = dimObj.AddComponent<Image>();
        StretchFull(_dimImage.rectTransform);
        _dimImage.color = new Color(0f, 0f, 0f, dimAlpha);

        // ── 패널 ──
        var panel = CreateImage(_canvasObj.transform, "Panel", PanelBg, rounded: true, skin: borderSprite);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = new Vector2(0.5f, 0f);
        panelRt.anchorMax = new Vector2(0.5f, 1f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(panelWidth, -panelMarginY * 2f);
        panelRt.anchoredPosition = Vector2.zero;

        const float side = 20f;
        const float titleH = 84f;
        const float closeH = 64f;

        // ── 제목 카드 ──
        var titleCard = CreateImage(panel.transform, "TitleCard", SectionBg, rounded: true, skin: borderSprite);
        var titleRt = titleCard.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.offsetMin = new Vector2(side, -(side + titleH));
        titleRt.offsetMax = new Vector2(-side, -side);
        var titleText = CreateText(titleCard.transform, "Title", 34f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        StretchFull(titleText.rectTransform);
        titleText.characterSpacing = 12f;
        RegisterLoc(titleText, "ui_settings_title", "설 정");

        // ── 스크롤 영역 ──
        var scrollObj = new GameObject("ScrollArea", typeof(RectTransform));
        scrollObj.transform.SetParent(panel.transform, false);
        var scrollRt = (RectTransform)scrollObj.transform;
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(side, side + closeH + 16f);
        scrollRt.offsetMax = new Vector2(-side, -(side + titleH + 14f));
        _scrollRect = scrollObj.AddComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
        _scrollRect.scrollSensitivity = 35f;

        var viewportObj = new GameObject("Viewport", typeof(RectTransform));
        viewportObj.transform.SetParent(scrollObj.transform, false);
        var viewportRt = (RectTransform)viewportObj.transform;
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = new Vector2(-14f, 0f);
        viewportObj.AddComponent<RectMask2D>();
        var viewportImg = viewportObj.AddComponent<Image>();
        viewportImg.color = Color.clear;
        _scrollRect.viewport = viewportRt;

        var contentObj = new GameObject("Content", typeof(RectTransform));
        contentObj.transform.SetParent(viewportObj.transform, false);
        var contentRt = (RectTransform)contentObj.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
        var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 16f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        var fitter = contentObj.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scrollRect.content = contentRt;

        // ── 스크롤바 ──
        _scrollRect.verticalScrollbar = CreateScrollbar(scrollObj.transform);
        _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        // ── 섹션들 ──
        BuildSoundSection(contentObj.transform);
        BuildGraphicSection(contentObj.transform);
        BuildLanguageSection(contentObj.transform);
        BuildControlSection(contentObj.transform);

        // ── 취소 / 적용 버튼 ──
        const float btnW = 210f;
        const float btnGap = 24f;
        float btnHalf = (btnW + btnGap) * 0.5f;

        var cancelBtn = CreateButton(panel.transform, "CancelButton", CancelBg, rounded: true, onClick: Cancel, skin: buttonSprite);
        var cancelRt = (RectTransform)cancelBtn.transform;
        cancelRt.anchorMin = cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(btnHalf, side);
        cancelRt.sizeDelta = new Vector2(btnW, closeH);
        var cancelText = CreateText(cancelBtn.transform, "Text", 24f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        StretchFull(cancelText.rectTransform);
        cancelText.characterSpacing = 6f;
        RegisterLoc(cancelText, "ui_settings_cancel", "취 소");

        var applyBtn = CreateButton(panel.transform, "ApplyButton", CancelBg, rounded: true, onClick: Apply, skin: buttonSprite);
        var applyRt = (RectTransform)applyBtn.transform;
        applyRt.anchorMin = applyRt.anchorMax = new Vector2(0.5f, 0f);
        applyRt.pivot = new Vector2(0.5f, 0f);
        applyRt.anchoredPosition = new Vector2(-btnHalf, side);
        applyRt.sizeDelta = new Vector2(btnW, closeH);
        _applyImage = applyBtn.GetComponent<Image>();
        var applyText = CreateText(applyBtn.transform, "Text", 24f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        StretchFull(applyText.rectTransform);
        applyText.characterSpacing = 6f;
        RegisterLoc(applyText, "ui_settings_apply", "적 용");
        _applyText = applyText;

        int btnGroup = _navGroupSeq++;
        RegisterNav(applyRt, null, Apply, btnGroup);
        RegisterNav(cancelRt, null, Cancel, btnGroup);

        // ── 키보드/마우스 포커스 테두리 ──
        _navHighlight = CreateImage(panel.transform, "NavHighlight", NavFocusColor, rounded: false, skin: focusSprite);
        if (focusSprite == null)
        {
            _navHighlight.sprite = GetOutlineSprite();
            _navHighlight.type = Image.Type.Sliced;
        }
        else
        {
            var c = _navHighlight.color;
            c.a = focusSpriteAlpha;
            _navHighlight.color = c;
        }
        _navHighlight.raycastTarget = false;
        _navHighlight.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _navHighlight.gameObject.SetActive(false);

        _canvasObj.SetActive(false);
    }

    // ===================================================
    // 섹션 빌더
    // ===================================================
    private void BuildSoundSection(Transform parent)
    {
        var body = CreateSection(parent, "Section_Sound", DiamondSound, "ui_settings_sec_sound", "사운드");

        AddSliderRow(body, "ui_settings_master", "마스터 볼륨",
            () => _pMaster, v => { _pMaster = v; PreviewMaster(v); });
        AddSliderRow(body, "ui_settings_bgm", "배경음악 (BGM)",
            () => _pBgm, v => { _pBgm = v; PreviewBgm(v); });
        AddSliderRow(body, "ui_settings_sfx", "효과음 (SFX)",
            () => _pSfx, v => { _pSfx = v; PreviewSfx(v); });
    }

    // ===================================================
    // 볼륨 미리듣기
    //
    // 이 창은 '적용'을 눌러야 저장한다. 하지만 볼륨만은 귀로 확인하지 않으면 고를 수 없어서,
    // 슬라이더를 움직이는 동안은 저장 없이 믹서에만 반영한다
    // (SoundManager.Preview*Volume — PlayerPrefs를 건드리지 않는다).
    // 취소/ESC로 닫으면 Hide가 ReapplySavedVolumes로 저장값을 되돌린다.
    //
    // BGM은 이미 울리고 있으니 슬라이더 값만 바꿔도 바로 들린다. 효과음은 조용한 창이라
    // 들려줄 소리가 없어서, 값이 바뀔 때마다 샘플 클릭음을 낸다. 드래그 중에는 값이
    // 프레임마다 들어오므로 스로틀로 솎아낸다(겹쳐 울리면 소리가 터진다).
    // 마스터도 마찬가지 — BGM이 꺼져 있으면 마스터만 움직여선 아무 변화도 안 들린다.
    // ===================================================
    private readonly SfxThrottle _previewThrottle = new SfxThrottle(0.12f);

    private void PreviewMaster(float v)
    {
        SoundManager.Instance?.PreviewMasterVolume(v);
        PreviewTick("master");
    }

    private void PreviewBgm(float v)
    {
        SoundManager.Instance?.PreviewBGMVolume(v);
    }

    private void PreviewSfx(float v)
    {
        SoundManager.Instance?.PreviewSFXVolume(v);
        PreviewTick("sfx");
    }

    /// <summary>
    /// 미리듣기용 샘플 효과음. timeScale이 0이므로 unscaledTime으로 스로틀한다.
    ///
    /// ◀▶ 버튼·키보드로 값을 바꾸는 경로(StepSlider)는 이미 클릭음을 낸 뒤 슬라이더 값을
    /// 건드린다 → 같은 프레임에 샘플음이 겹쳐 두 번 울린다. 그 프레임은 건너뛴다.
    /// </summary>
    private void PreviewTick(string key)
    {
        if (_clickSfxFrame == Time.frameCount) return;

        if (_previewThrottle.ShouldPlay(key, Time.unscaledTime))
            CodeUI.PlaySfx(clickSfxName);
    }

    /// <summary>미리듣기로 흐트러진 믹서를 저장값으로 되돌린다(취소·적용 공통).</summary>
    private void RestoreSavedVolumes()
    {
        _previewThrottle.Clear();
        SoundManager.Instance?.ReapplySavedVolumes();
    }

    private void BuildGraphicSection(Transform parent)
    {
        var body = CreateSection(parent, "Section_Graphic", DiamondGraphic, "ui_settings_sec_graphic", "그래픽");

        AddSelectorRow(body, "ui_settings_resolution", "해상도",
            text: () =>
            {
                var mgr = GraphicSettingsManager.Instance;
                if (mgr == null || mgr.SupportResolutions == null || mgr.SupportResolutions.Length == 0)
                    return $"{Screen.width} × {Screen.height}";
                var res = mgr.SupportResolutions[Mathf.Clamp(_pResIndex, 0, mgr.SupportResolutions.Length - 1)];
                return $"{res.width} × {res.height}";
            },
            step: dir =>
            {
                var mgr = GraphicSettingsManager.Instance;
                if (mgr == null || mgr.SupportResolutions == null) return;
                int count = mgr.SupportResolutions.Length;
                if (count <= 0) return;
                _pResIndex = (_pResIndex + dir + count) % count;
            });

        AddSwitchRow(body, "ui_settings_fullscreen", "전체화면",
            get: () => _pFullscreen,
            set: on => _pFullscreen = on);

        AddSwitchRow(body, "ui_settings_vsync", "수직동기화 (VSync)",
            get: () => _pVsync,
            set: on => _pVsync = on);

        if (showFrameRateRow)
        {
            AddSelectorRow(body, "ui_settings_framerate", "프레임 제한",
                text: () => _pFrameRate < 0 ? L("ui_settings_fps_unlimited", "무제한") : $"{_pFrameRate} FPS",
                step: dir =>
                {
                    int idx = FrameRateOptions.IndexOf(_pFrameRate);
                    if (idx < 0) idx = 1; // 60
                    idx = (idx + dir + FrameRateOptions.Count) % FrameRateOptions.Count;
                    _pFrameRate = FrameRateOptions[idx];
                });
        }
    }

    private void BuildLanguageSection(Transform parent)
    {
        var body = CreateSection(parent, "Section_Language", DiamondLanguage, "ui_settings_sec_language", "언어");

        AddSelectorRow(body, "ui_settings_language", "게임 언어",
            text: () => LanguageNames[Mathf.Clamp(_pLangIndex, 0, LanguageNames.Length - 1)],
            step: dir =>
            {
                int count = LanguageNames.Length;
                _pLangIndex = ((_pLangIndex + dir) % count + count) % count;
            });
    }

    private void BuildControlSection(Transform parent)
    {
        var body = CreateSection(parent, "Section_Control", DiamondControl, "ui_settings_sec_control", "조작");

        foreach (var def in controlRows)
        {
            if (def == null || string.IsNullOrEmpty(def.keyText)) continue;
            AddKeyRow(body, def);
        }

        RegisterNav((RectTransform)body, null, null, group: -1, padX: -3f, padY: -3f);
    }

    // ===================================================
    // 위젯 조립 헬퍼
    // ===================================================
    private Transform CreateSection(Transform parent, string name, Color diamondColor, string titleKey, string titleFallback)
    {
        var card = CreateImage(parent, name, SectionBg, rounded: true, skin: tabSprite);
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 18, 20);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(card.transform, false);
        var hLayout = header.AddComponent<HorizontalLayoutGroup>();
        hLayout.spacing = 10f;
        hLayout.childControlWidth = true;
        hLayout.childControlHeight = true;
        hLayout.childForceExpandWidth = false;
        hLayout.childForceExpandHeight = false;
        hLayout.childAlignment = TextAnchor.MiddleLeft;
        header.AddComponent<LayoutElement>().preferredHeight = 36f;

        var diamond = CreateText(header.transform, "Diamond", 18f, FontStyles.Normal, diamondColor, TextAlignmentOptions.Center);
        diamond.text = sectionDiamondGlyph;
        var title = CreateText(header.transform, "Title", 24f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);
        RegisterLoc(title, titleKey, titleFallback);

        var line = CreateImage(card.transform, "Divider", DividerColor, rounded: false);
        line.gameObject.AddComponent<LayoutElement>().preferredHeight = 2f;

        return card.transform;
    }

    private void AddSliderRow(Transform section, string labelKey, string labelFallback,
        System.Func<float> get, System.Action<float> set)
    {
        var row = CreateRow(section, "Row_" + labelKey, labelKey, labelFallback);

        var sliderRef = new SliderRef { get = get, set = set };

        CreateArrowButton(row, -1, () => StepSlider(sliderRef, -0.05f));

        var sliderObj = new GameObject("Slider", typeof(RectTransform));
        sliderObj.transform.SetParent(row, false);
        var le = sliderObj.AddComponent<LayoutElement>();
        le.preferredWidth = 230f;
        le.preferredHeight = 26f;

        var bg = CreateImage(sliderObj.transform, "Background", BoxBg, rounded: true);
        bg.sprite = GetRoundedSprite(8, 0.5f);
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(1f, 0.5f);
        bgRt.sizeDelta = new Vector2(0f, 12f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObj.transform, false);
        var faRt = (RectTransform)fillArea.transform;
        faRt.anchorMin = new Vector2(0f, 0.5f);
        faRt.anchorMax = new Vector2(1f, 0.5f);
        faRt.sizeDelta = new Vector2(-12f, 12f);
        var fill = CreateImage(fillArea.transform, "Fill", AccentFill, rounded: true);
        fill.sprite = GetRoundedSprite(8, 0.5f);
        fill.rectTransform.sizeDelta = new Vector2(6f, 0f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderObj.transform, false);
        var haRt = (RectTransform)handleArea.transform;
        haRt.anchorMin = Vector2.zero;
        haRt.anchorMax = Vector2.one;
        haRt.sizeDelta = new Vector2(-12f, 0f);
        var handle = CreateImage(handleArea.transform, "Handle", Color.white, rounded: true);
        handle.sprite = GetRoundedSprite(8, 0.5f);
        handle.rectTransform.sizeDelta = new Vector2(12f, 26f);

        var slider = sliderObj.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(v =>
        {
            sliderRef.set?.Invoke(v);
            UpdateSliderLabel(sliderRef, v);
        });
        sliderRef.slider = slider;

        CreateArrowButton(row, +1, () => StepSlider(sliderRef, +0.05f));

        var value = CreateText(row, "Value", 22f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineRight);
        value.gameObject.AddComponent<LayoutElement>().preferredWidth = 48f;
        sliderRef.value = value;

        _sliders.Add(sliderRef);
        RegisterNav((RectTransform)row, dir => StepSlider(sliderRef, dir * 0.05f), null);
    }

    private void StepSlider(SliderRef r, float delta)
    {
        PlayClick();
        r.slider.value = Mathf.Clamp01(Mathf.Round((r.slider.value + delta) * 20f) / 20f);
    }

    private void UpdateSliderLabel(SliderRef r, float v)
    {
        if (r.value != null) r.value.text = Mathf.RoundToInt(v * 100f).ToString();
    }

    private void AddSelectorRow(Transform section, string labelKey, string labelFallback,
        System.Func<string> text, System.Action<int> step)
    {
        var row = CreateRow(section, "Row_" + labelKey, labelKey, labelFallback);

        var selRef = new SelectorRef { text = text };
        System.Action<int> stepAndRefresh = dir => { PlayClick(); step(dir); RefreshSelectors(); };

        CreateArrowButton(row, -1, () => stepAndRefresh(-1));

        var box = CreateImage(row, "ValueBox", BoxBg, rounded: true, skin: boxSprite);
        var boxLe = box.gameObject.AddComponent<LayoutElement>();
        boxLe.preferredWidth = 236f;
        boxLe.preferredHeight = 42f;
        var valueText = CreateText(box.transform, "Value", 22f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        StretchFull(valueText.rectTransform);
        selRef.value = valueText;

        CreateArrowButton(row, +1, () => stepAndRefresh(+1));

        _selectors.Add(selRef);
        RegisterNav((RectTransform)row, stepAndRefresh, () => stepAndRefresh(+1));
    }

    private void AddSwitchRow(Transform section, string labelKey, string labelFallback,
        System.Func<bool> get, System.Action<bool> set)
    {
        var row = CreateRow(section, "Row_" + labelKey, labelKey, labelFallback);

        var sw = new SwitchRef { get = get, set = set };

        System.Action<bool> apply = on =>
        {
            if (sw.state == on) return;
            PlayClick();
            sw.state = on;
            sw.set?.Invoke(on);
            UpdateSwitchVisual(sw);
        };

        var btn = CreateButton(row, "Switch", SwitchOff, rounded: true, onClick: () => apply(!sw.state));
        var swLe = btn.gameObject.AddComponent<LayoutElement>();
        swLe.preferredWidth = 68f;
        swLe.preferredHeight = 34f;
        sw.bg = btn.GetComponent<Image>();
        sw.bg.sprite = GetRoundedSprite(32, 0.5f);

        var knob = CreateImage(btn.transform, "Knob", Color.white, rounded: true);
        knob.sprite = GetRoundedSprite(24, 0.5f);
        knob.raycastTarget = false;
        var knobRt = knob.rectTransform;
        knobRt.anchorMin = new Vector2(0.5f, 0.5f);
        knobRt.anchorMax = new Vector2(0.5f, 0.5f);
        knobRt.sizeDelta = new Vector2(26f, 26f);
        sw.knob = knobRt;

        _switches.Add(sw);
        RegisterNav((RectTransform)row, dir => apply(dir > 0), () => apply(!sw.state));
    }

    private void UpdateSwitchVisual(SwitchRef sw)
    {
        sw.bg.color = sw.state ? AccentFill : SwitchOff;
        sw.knob.anchoredPosition = new Vector2(sw.state ? 15f : -15f, 0f);
    }

    private void AddKeyRow(Transform section, ControlRowDef def)
    {
        var row = CreateRow(section, "Row_" + def.labelKey, def.labelKey, def.labelFallback);

        var cap = CreateImage(row, "KeyCap", KeyCapBg, rounded: true);
        var capLayout = cap.gameObject.AddComponent<HorizontalLayoutGroup>();
        capLayout.padding = new RectOffset(16, 16, 6, 6);
        capLayout.childControlWidth = true;
        capLayout.childControlHeight = true;
        capLayout.childForceExpandWidth = false;
        capLayout.childForceExpandHeight = false;
        capLayout.childAlignment = TextAnchor.MiddleCenter;
        var capLe = cap.gameObject.AddComponent<LayoutElement>();
        capLe.minWidth = 72f;
        capLe.preferredHeight = 36f;

        var keyText = CreateText(cap.transform, "Key", 18f, FontStyles.Bold, KeyCapFg, TextAlignmentOptions.Center);
        if (!string.IsNullOrEmpty(def.keyLocKey))
            RegisterLoc(keyText, def.keyLocKey, def.keyText);
        else
            keyText.text = def.keyText;
    }

    private Transform CreateRow(Transform section, string name, string labelKey, string labelFallback)
    {
        var row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(section, false);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        row.AddComponent<LayoutElement>().preferredHeight = RowHeight;

        var label = CreateText(row.transform, "Label", 22f, FontStyles.Normal, LabelColor, TextAlignmentOptions.MidlineLeft);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        RegisterLoc(label, labelKey, labelFallback);

        return row.transform;
    }

    private void CreateArrowButton(Transform parent, int dir, System.Action onClick)
    {
        var btn = CreateButton(parent, dir < 0 ? "ArrowLeft" : "ArrowRight", ArrowBg, rounded: true, onClick: onClick, skin: buttonSprite);
        if (buttonSprite == null)
            btn.image.sprite = GetRoundedSprite(24, 0.28f);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 34f;
        le.preferredHeight = 34f;

        var tri = new GameObject("Arrow").AddComponent<Image>();
        tri.transform.SetParent(btn.transform, false);
        tri.sprite = GetTriangleSprite(dir);
        tri.color = ArrowFg;
        tri.raycastTarget = false;
        var trt = tri.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(13f, 15f);
    }

    private Scrollbar CreateScrollbar(Transform parent)
    {
        var sbObj = new GameObject("Scrollbar", typeof(RectTransform));
        sbObj.transform.SetParent(parent, false);
        var sbRt = (RectTransform)sbObj.transform;
        sbRt.anchorMin = new Vector2(1f, 0f);
        sbRt.anchorMax = new Vector2(1f, 1f);
        sbRt.pivot = new Vector2(1f, 0.5f);
        sbRt.sizeDelta = new Vector2(6f, 0f);
        sbRt.anchoredPosition = Vector2.zero;

        var bg = sbObj.AddComponent<Image>();
        bg.sprite = GetRoundedSprite(4, 0.5f);
        bg.type = Image.Type.Sliced;
        bg.color = BoxBg;

        var slideArea = new GameObject("Sliding Area", typeof(RectTransform));
        slideArea.transform.SetParent(sbObj.transform, false);
        var saRt = (RectTransform)slideArea.transform;
        saRt.anchorMin = Vector2.zero;
        saRt.anchorMax = Vector2.one;
        saRt.offsetMin = Vector2.zero;
        saRt.offsetMax = Vector2.zero;

        var handle = CreateImage(slideArea.transform, "Handle", ScrollHandle, rounded: true);
        handle.sprite = GetRoundedSprite(4, 0.5f);
        var hRt = handle.rectTransform;
        hRt.offsetMin = Vector2.zero;
        hRt.offsetMax = Vector2.zero;

        var sb = sbObj.AddComponent<Scrollbar>();
        sb.handleRect = hRt;
        sb.targetGraphic = handle;
        sb.direction = Scrollbar.Direction.BottomToTop;
        return sb;
    }

    // ===================================================
    // 값 동기화 / 로컬라이즈
    // ===================================================
    private void SyncValues()
    {
        foreach (var r in _sliders)
        {
            float v = Mathf.Clamp01(r.get != null ? r.get() : 1f);
            r.slider.SetValueWithoutNotify(v);
            UpdateSliderLabel(r, v);
        }
        RefreshSelectors();
        foreach (var sw in _switches)
        {
            sw.state = sw.get != null && sw.get();
            UpdateSwitchVisual(sw);
        }
    }

    private void RefreshSelectors()
    {
        foreach (var s in _selectors)
            if (s.value != null && s.text != null) s.value.text = s.text();
    }

    private void RegisterLoc(TextMeshProUGUI tmp, string key, string fallback)
    {
        _locTexts.Add(new LocText { tmp = tmp, key = key, fallback = fallback });
        tmp.text = L(key, fallback);
    }

    private void TrySubscribeLanguage()
    {
        if (_langSubscribed || LanguageManager.Instance == null) return;
        LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
        _langSubscribed = true;
    }

    private void OnLanguageChanged(LanguageType _)
    {
        RefreshTexts();
        RefreshSelectors();
    }

    private void RefreshTexts()
    {
        var lm = LanguageManager.Instance;
        var font = lm != null ? lm.GetCurrentFont() : null;

        foreach (var t in _allTexts)
            if (font != null && t != null) t.font = font;

        foreach (var l in _locTexts)
            if (l.tmp != null) l.tmp.text = L(l.key, l.fallback);
    }

    private static string L(string key, string fallback)
    {
        var lm = LanguageManager.Instance;
        if (lm == null || string.IsNullOrEmpty(key)) return fallback;
        string s = lm.L(key);
        return (string.IsNullOrEmpty(s) || s == key) ? fallback : s;
    }

    // SoundManager를 직접 부르지 않는다. CodeUI.PlaySfx는 키가 미등록이면
    // 공용 버튼음(SfxKeys.UiButton)으로 폴백하므로, 전용 음원이 아직 없어도 소리가 난다.
    // 직접 호출하면 HasSFX 가드에서 조용히 무음으로 빠진다(이 창이 무음이던 원인).
    private int _clickSfxFrame = -1;

    private void PlayClick()
    {
        _clickSfxFrame = Time.frameCount;
        CodeUI.PlaySfx(clickSfxName);
    }

    // CodeUI.PlayBack은 같은 프레임의 중복 뒤로가기음·뒤따르는 클릭음을 정리해준다.
    private void PlayBack() => CodeUI.PlayBack(backSfxName);

    // ===================================================
    // 저수준 생성 헬퍼
    // ===================================================
    private Image CreateImage(Transform parent, string name, Color color, bool rounded, Sprite skin = null)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        img.color = color;

        if (skin != null)
        {
            img.sprite = skin;
            img.type = (skin.border.sqrMagnitude > 0.01f) ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = Mathf.Max(0.01f, spritePixelsPerUnit);
            img.color = tintSprites ? color : Color.white;
            return img;
        }

        if (rounded)
        {
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
        }
        return img;
    }

    private Button CreateButton(Transform parent, string name, Color color, bool rounded, System.Action onClick, Sprite skin = null)
    {
        var img = CreateImage(parent, name, color, rounded, skin);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        return btn;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, float fontSize, FontStyles style, Color color, TextAlignmentOptions align)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        var lm = LanguageManager.Instance;
        var font = lm != null ? lm.GetCurrentFont() : null;
        if (font != null) tmp.font = font;

        _allTexts.Add(tmp);
        return tmp;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    // ── 둥근 모서리 스프라이트 ──
    private static readonly Dictionary<int, Sprite> _roundedSprites = new Dictionary<int, Sprite>();

    private static Sprite GetRoundedSprite() => GetRoundedSprite(64, 0.28f);

    private static Sprite GetRoundedSprite(int size, float radiusRatio)
    {
        int key = size * 1000 + Mathf.RoundToInt(radiusRatio * 100f);
        if (_roundedSprites.TryGetValue(key, out var cached) && cached != null) return cached;

        float radius = Mathf.Max(size * radiusRatio - 0.5f, 1f);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        float border = Mathf.Min(Mathf.Ceil(radius) + 1f, size * 0.5f);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        _roundedSprites[key] = sprite;
        return sprite;
    }

    // ── 라운드 사각 '테두리만' 스프라이트 ──
    private static Sprite _outlineSprite;

    private static Sprite GetOutlineSprite()
    {
        if (_outlineSprite != null) return _outlineSprite;

        const int size = 48;
        const float radius = 11f;
        const float thickness = 2.5f;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                float sd = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                float a = Mathf.Clamp01(0.5f - sd) * Mathf.Clamp01(sd + thickness + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);

        float border = Mathf.Ceil(radius) + 1f;
        _outlineSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        return _outlineSprite;
    }

    // ── 삼각형 화살표 스프라이트 ──
    private static readonly Dictionary<int, Sprite> _triangleSprites = new Dictionary<int, Sprite>();

    private static Sprite GetTriangleSprite(int dir)
    {
        int key = dir < 0 ? 0 : 1;
        if (_triangleSprites.TryGetValue(key, out var cached) && cached != null) return cached;

        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        const float baseX = 0.26f, tipX = 0.74f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S;
                float v = (y + 0.5f) / S;
                float fx = dir < 0 ? 1f - u : u;
                float a = 0f;
                if (fx >= baseX && fx <= tipX)
                {
                    float halfH = (tipX - fx) / (tipX - baseX) * 0.5f;
                    a = Mathf.Clamp01((halfH - Mathf.Abs(v - 0.5f)) * S);
                }
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        _triangleSprites[key] = sprite;
        return sprite;
    }
}

/// <summary>
/// 설정 오버레이 항목에 붙어 마우스가 올라오면 키보드 포커스를 그 항목으로 옮긴다.
/// IPointerEnterHandler '만' 구현한다 — EventTrigger처럼 드래그·휠까지 받아버리면
/// 그 이벤트가 ScrollRect까지 올라가지 못해 패널 스크롤이 죽는다.
/// (SettingsOverlayUI.RegisterNav가 런타임에 AddComponent)
/// </summary>
internal class SettingsNavHover : MonoBehaviour, IPointerEnterHandler
{
    private SettingsOverlayUI _owner;
    private int _index;

    internal void Bind(SettingsOverlayUI owner, int index)
    {
        _owner = owner;
        _index = index;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_owner != null) _owner.FocusFromPointer(_index);
    }
}