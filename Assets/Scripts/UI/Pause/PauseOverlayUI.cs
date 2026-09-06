// @tags: pause, ui, overlay, code-generated, settings, emergency-escape, mainmenu, quit

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 일시정지 오버레이 — 설정 / 도감 / 긴급탈출(지하 전용) / 메인화면.
/// UI는 전부 코드로 생성한다(SettingsOverlayUI 패턴) — 씬/프리팹 세팅 없이 <see cref="Open"/>만으로 동작.
/// 스프라이트는 다른 코드 오버레이와 같은 <see cref="UISkin"/>을 쓴다.
///
///  - 계속하기 버튼은 없다 — ESC로만 닫는다(<see cref="Close"/>).
///  - 게임 종료 버튼도 없다 — 종료는 반드시 메인 화면으로 나간 뒤 메인 화면의 종료 버튼으로 한다.
///  - 긴급탈출은 지하 씬에서만 보이고, 확인 후 창고 병합 → 지상 씬 이동
///  - 메인화면은 지하에서만 "진행 내용이 저장되지 않는다" 경고를 띄운다
/// </summary>
public class PauseOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static PauseOverlayUI _instance;

    public static PauseOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<PauseOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("PauseOverlayUI");
                    _instance = go.AddComponent<PauseOverlayUI>();
                }
            }
            return _instance;
        }
    }

    /// <summary>일시정지 오버레이가 열려 있는지 (UIStateManager의 전역 단축키 차단용).</summary>
    public static bool IsOpen => _instance != null && _instance._isOpen;

    /// <summary>ESC로 닫힌 바로 그 프레임인지 — 같은 프레임의 중복 처리 방지.</summary>
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// 씬이 바뀌면 무조건 닫는다(안전망).
    /// 이 오버레이는 DontDestroyOnLoad라, 열린 채로 씬이 바뀌면 새 씬 위에 그대로 남아
    /// 전체 화면 배경이 클릭을 전부 먹어버린다. timeScale도 0으로 굳는다.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_isOpen && !_opening) return;

        _opening = false;
        Time.timeScale = 1f; // 새 씬은 항상 정상 속도로 시작
        CloseWithoutRestoringTime();
    }

    // ===================================================
    // 인스펙터
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.6f;

    [Tooltip("켜면 화면을 캡처(ScreenCapture)해 블러 처리한 이미지를 배경으로 깐다.\n\n" +
             "지하에서는 끄는 것을 권장 — 캡처 이미지에 PlayerVisionOverlay의 어둠막이 들어오지 않아 " +
             "일시정지를 열면 갱도 전체가 드러난 화면이 배경으로 깔린다(오버레이가 꺼진 것처럼 보인다). " +
             "끄면 살아 있는 화면 위에 dimAlpha만 덮으므로 시야 어둠이 그대로 보인다.")]
    [SerializeField] private bool useBlurBackdrop = false;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("씬 이름")]
    [Tooltip("이 씬에서만 '긴급 탈출' 버튼이 보인다")]
    [SerializeField] private string targetUndergroundScene = "DemoUnderground";
    [Tooltip("긴급 탈출 시 돌아갈 지상 씬")]
    [SerializeField] private string targetUpgroundScene = "DemoUpground";

    [Tooltip("이 씬들에서는 일시정지를 열지 않는다.\n" +
             "UIStateManager는 DontDestroyOnLoad라 자체 UIStateManager가 없는 씬(메인메뉴 등)에서도 " +
             "ESC를 계속 처리한다. 옛 PauseUI는 그 씬에 패널이 없어 아무 일도 없었지만, " +
             "이 오버레이는 없으면 스스로 만들어 뜨기 때문에 메인메뉴를 덮어버린다.")]
    [SerializeField]
    private string[] disabledScenes = { "MainMenuScene", "LoadingScene", "SettlementScene" };

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 520f;
    [SerializeField] private float buttonHeight = 62f;
    [Tooltip("캔버스 정렬 순서 — 설정(31000)보다 아래여야 설정이 위에 뜬다")]
    [SerializeField] private int sortingOrder = 30800;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;
    [Tooltip("ESC로 닫거나 취소할 때 나는 소리")]
    [SerializeField] private string backSfxName = SfxKeys.UiBack;
    [Tooltip("W/S로 커서가 움직일 때 나는 소리")]
    [SerializeField] private string moveSfxName = "ui_move";

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 내부 상태
    // ===================================================
    private const float FadeDuration = 0.12f;

    private bool _built, _isOpen, _opening;
    private bool _langSubscribed;
    private int _lastCloseFrame = -1;
    private int _lastOpenFrame = -1;
    private float _prevTimeScale = 1f;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    private Button _escapeButton;

    // 확인 팝업
    private GameObject _confirmRoot;
    private TextMeshProUGUI _confirmTitle, _confirmMessage;
    private System.Action _confirmAction;

    // 키보드 조작 (W/S 이동 · Space 선택) — 다른 코드 오버레이와 같은 규칙.
    // 확인 팝업이 떠 있으면 커서는 팝업 버튼만 돌아다녀야 하므로 목록을 둘로 나눠 둔다.
    private readonly List<ICodeNavItem> _menuNav = new List<ICodeNavItem>();
    private readonly List<ICodeNavItem> _confirmNav = new List<ICodeNavItem>();
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    public static void Open()
    {
        if (IsOpen || Instance._opening) return;
        if (Instance.IsDisabledInCurrentScene())
        {
            // 메인메뉴·로딩 같은 씬에서는 열지 않는다.
            // 상태가 Pause로 굳으면 이후 ESC 처리가 꼬이므로 즉시 None으로 되돌린다.
            var ui = UIStateManager.Instance;
            if (ui != null && ui.CurrentState == UIState.Pause) ui.SetState(UIState.None);
            return;
        }
        Instance.StartCoroutine(Instance.OpenRoutine());
    }

    /// <summary>현재 씬이 일시정지를 띄우면 안 되는 씬인지.</summary>
    private bool IsDisabledInCurrentScene()
    {
        if (disabledScenes == null || disabledScenes.Length == 0) return false;

        string current = SceneManager.GetActiveScene().name;
        foreach (var name in disabledScenes)
        {
            if (!string.IsNullOrEmpty(name) &&
                string.Equals(name, current, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static void CloseStatic()
    {
        if (_instance != null) _instance.Close();
    }

    /// <summary>일시정지 해제(계속하기). 시간도 원래대로 되돌린다.</summary>
    public void Close(bool isEscape = false)
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        Time.timeScale = _prevTimeScale;

        // isEscape 여부에 따라 뒤로가기(취소) 효과음과 일반 클릭 효과음을 분리
        if (isEscape) CodeUI.PlayBack(backSfxName);
        else CodeUI.PlaySfx(clickSfxName);

        HideConfirm();
        _nav.End();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        // ESC 등으로 스스로 닫혔다면 UIStateManager 상태도 되돌린다.
        // 이 시점엔 이미 _isOpen=false라, SetState가 CloseStatic을 다시 불러도 즉시 반환된다(재귀 없음).
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Pause) ui.SetState(UIState.None);
    }

    /// <summary>
    /// 씬 전환·종료처럼 시간 복원을 직접 하는 경우 — 상태만 정리하고 timeScale은 건드리지 않는다.
    ///
    /// UIStateManager도 반드시 None으로 되돌린다. UIStateManager는 DontDestroyOnLoad라
    /// 메인메뉴처럼 자체 UIStateManager가 없는 씬으로 가면 CurrentState가 Pause인 채로 남고,
    /// 거기서 ESC를 누르면 일시정지 오버레이가 메인메뉴 위에 떠서 클릭을 전부 막는다.
    /// </summary>
    private void CloseWithoutRestoringTime()
    {
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        HideConfirm();
        _nav.End();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Pause) ui.SetState(UIState.None);
    }

    private IEnumerator OpenRoutine()
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        TrySubscribeLanguage();

        if (useBlurBackdrop)
        {
            _canvasObj.SetActive(false);
            yield return new WaitForEndOfFrame();
            _blur.Capture(_blurImage, blurDownsamples, flipBlurVertically);
        }
        _blurImage.enabled = useBlurBackdrop;
        _opening = false;

        _prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        _isOpen = true;
        _lastOpenFrame = Time.frameCount;

        _loc.Refresh();
        HideConfirm();

        // 긴급 탈출은 지하 씬에서만
        if (_escapeButton != null)
            _escapeButton.gameObject.SetActive(SceneManager.GetActiveScene().name == targetUndergroundScene);

        _canvasObj.SetActive(true);
        _nav.Begin();
        _nav.ClearFocus();   // 매번 커서 없이 시작 — 첫 W/S 입력이 커서를 켠다

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

        // 열린 바로 그 프레임의 ESC는 무시한다.
        // UIStateManager.Update가 ESC를 받아 Open()을 부르면 _isOpen이 그 자리에서 true가 되는데,
        // 이 Update가 같은 프레임 뒤에 돌면 아직 눌려 있는 그 ESC로 곧장 다시 닫아버린다
        // (스크립트 실행 순서에 따라 열자마자 닫혀 안 열리는 것처럼 보인다).
        // 예전엔 OpenRoutine의 WaitForEndOfFrame이 우연히 이걸 막아 줬지만, 기대면 안 되는 타이밍이었다.
        if (_lastOpenFrame == Time.frameCount) return;

        // 설정 오버레이가 위에 떠 있으면 ESC는 그쪽이 처리한다 (둘 다 처리하면 한 번에 두 개가 닫힌다)
        if (SettingsOverlayUI.IsOpen || SettingsOverlayUI.ClosedThisFrame) return;

        // Space는 Input System에서 Submit로도 잡힌다 — 직전에 마우스로 누른 버튼이 선택된 채 남아 있으면
        // 내비게이터의 확인과 그 버튼의 onClick이 겹쳐 한 번에 두 번 실행된다.
        CodeUI.ClearSelection();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 확인 팝업이 떠 있으면 그것부터 닫는다
            if (_confirmRoot != null && _confirmRoot.activeSelf)
            {
                CodeUI.PlayBack(backSfxName);
                HideConfirm();
                return;
            }
            Close(true); // ESC 키로 닫힘을 명시
            return;
        }

        // W/S 이동 + Space 선택 (timeScale=0에서도 unscaledTime으로 동작)
        _nav.Update();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _nav.End();
        if (_langSubscribed && LanguageManager.Instance != null)
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
        _blur.Release(_blurImage);
    }

    /// <summary>
    /// 언어 변경 이벤트 구독(1회). 설정 오버레이가 이 위에 떠서 언어를 바꾸고 닫히면,
    /// 뒤에 그대로 열려 있던 일시정지 패널이 갱신되지 않아 옛 언어로 남는다.
    /// 이벤트로 <see cref="LocTextBinder.Refresh"/>를 걸어 그 자리에서 새 언어로 다시 채운다.
    /// </summary>
    private void TrySubscribeLanguage()
    {
        if (_langSubscribed || LanguageManager.Instance == null) return;
        LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
        _langSubscribed = true;
    }

    // 확인 팝업(제목·본문)은 표시 시점에 문자열을 넣는 동적 텍스트라 여기서 갱신하지 않는다.
    // 설정 오버레이가 이 위를 덮고 있는 동안에는 확인 팝업을 띄울 수 없으므로 문제되지 않는다.
    private void OnLanguageChanged(LanguageType _) => _loc.Refresh();

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("PauseOverlayCanvas");
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

        // ── 패널 (버튼 수에 맞춰 세로로 늘어난다) ──
        var panel = CodeUI.CreateImage(_canvasObj.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(panelWidth, 0f);
        panelRt.anchoredPosition = Vector2.zero;

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 26, 26);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 제목
        var titleRow = CodeUI.CreateRow(panel.transform, "TitleRow", 46f, 10f, TextAnchor.MiddleCenter);
        var diamond = CodeUI.CreateText(titleRow, "Diamond", 20f, FontStyles.Normal, CodeUI.GoldColor,
            TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;
        var title = CodeUI.CreateText(titleRow, "Title", 30f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        title.characterSpacing = 8f;
        _loc.Bind(title, "ui_pause_title", "일시정지");

        CodeUI.CreateDivider(panel.transform);

        // 버튼들 — 계속하기는 ESC로만(버튼 제거), 종료는 메인 화면에서만.
        AddButton(panel.transform, "ui_pause_settings", "설 정", CodeUI.NeutralBg, Color.white, OnSettings);
        AddButton(panel.transform, "ui_codex_title", "도감", CodeUI.NeutralBg, CodeUI.MineralColor, OnCodex);
        _escapeButton = AddButton(panel.transform, "ui_pause_escape", "긴급 탈출", CodeUI.WarnColor, CodeUI.GoldFg, OnEmergencyEscape);
        AddButton(panel.transform, "ui_pause_mainmenu", "메인 화면", CodeUI.NeutralBg, Color.white, OnMainMenu);

        var hint = CodeUI.CreateText(panel.transform, "Hint", 14f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        _loc.Bind(hint, "ui_pause_hint", "W / S : 이동    Space : 선택    ESC : 닫기");

        BuildConfirmPopup();

        // 확인 팝업이 떠 있으면 커서는 팝업 안에서만 움직인다
        _nav.moveSfxName = moveSfxName;
        _nav.collect = list =>
        {
            list.Clear();
            list.AddRange(_confirmRoot != null && _confirmRoot.activeSelf ? _confirmNav : _menuNav);
        };

        _canvasObj.SetActive(false);
    }

    private Button AddButton(Transform parent, string key, string fallback, Color bg, Color fg, System.Action onClick)
    {
        var btn = CodeUI.CreateTextButton(parent, "Btn_" + key, bg, fg, 22f, onClick, out var label,
            skin.buttonSprite, skin, _loc);
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = buttonHeight;
        label.characterSpacing = 4f;
        _loc.Bind(label, key, fallback);

        var nav = CodeNavButton.Attach(btn, skin);
        if (nav != null) _menuNav.Add(nav);
        return btn;
    }

    // ===================================================
    // 확인 팝업
    // ===================================================
    private void BuildConfirmPopup()
    {
        _confirmRoot = new GameObject("ConfirmPopup", typeof(RectTransform));
        _confirmRoot.transform.SetParent(_canvasObj.transform, false);
        CodeUI.StretchFull((RectTransform)_confirmRoot.transform);

        var backdrop = CodeUI.CreateImage(_confirmRoot.transform, "Backdrop", new Color(0f, 0f, 0f, 0.6f), rounded: false);
        CodeUI.StretchFull(backdrop.rectTransform);
        var backdropBtn = backdrop.gameObject.AddComponent<Button>();
        backdropBtn.transition = Selectable.Transition.None;
        backdropBtn.onClick.AddListener(HideConfirm);

        var panel = CodeUI.CreateImage(_confirmRoot.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var rt = panel.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(620f, 0f);

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 24, 24);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _confirmTitle = CodeUI.CreateText(panel.transform, "Title", 25f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        _confirmTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        _confirmMessage = CodeUI.CreateText(panel.transform, "Message", 19f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.Center, _loc);
        _confirmMessage.textWrappingMode = TextWrappingModes.Normal;
        _confirmMessage.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;

        // 확인(왼쪽) / 취소(오른쪽) — 상점 수량 창과 같은 순서
        var buttons = CodeUI.CreateRow(panel.transform, "Buttons", 54f, 12f, TextAnchor.MiddleCenter);

        var ok = CodeUI.CreateTextButton(buttons, "Confirm", CodeUI.GoldColor, CodeUI.GoldFg, 20f,
            OnConfirmYes, out var okLabel, skin.buttonSprite, skin, _loc);
        var okLe = ok.gameObject.AddComponent<LayoutElement>();
        okLe.flexibleWidth = 1f;
        okLe.preferredHeight = 50f;
        _loc.Bind(okLabel, "ui_pause_confirm", "확인");
        var okNav = CodeNavButton.Attach(ok, skin);
        if (okNav != null) _confirmNav.Add(okNav);

        var cancel = CodeUI.CreateTextButton(buttons, "Cancel", CodeUI.NeutralBg, Color.white, 20f,
            () => { CodeUI.PlaySfx(backSfxName); HideConfirm(); }, out var cancelLabel, skin.buttonSprite, skin, _loc);
        var cancelLe = cancel.gameObject.AddComponent<LayoutElement>();
        cancelLe.flexibleWidth = 1f;
        cancelLe.preferredHeight = 50f;
        _loc.Bind(cancelLabel, "ui_pause_cancel", "취소");
        var cancelNav = CodeNavButton.Attach(cancel, skin);
        if (cancelNav != null) _confirmNav.Add(cancelNav);

        _confirmRoot.SetActive(false);
    }

    private void ShowConfirm(string titleKey, string titleFallback, string msgKey, string msgFallback,
        System.Action onYes)
    {
        _confirmTitle.text = CodeUI.L(titleKey, titleFallback);
        _confirmMessage.text = CodeUI.L(msgKey, msgFallback);
        _confirmAction = onYes;
        _confirmRoot.transform.SetAsLastSibling();
        _confirmRoot.SetActive(true);

        // 커서 대상이 메뉴 → 팝업으로 통째로 바뀌므로 포커스를 비운다
        _nav.ClearFocus();
    }

    private void HideConfirm()
    {
        bool wasOpen = _confirmRoot != null && _confirmRoot.activeSelf;
        if (_confirmRoot != null) _confirmRoot.SetActive(false);
        _confirmAction = null;
        if (wasOpen) _nav.ClearFocus();
    }

    private void OnConfirmYes()
    {
        CodeUI.PlaySfx(clickSfxName);
        var action = _confirmAction;
        HideConfirm();
        action?.Invoke();
    }

    // ===================================================
    // 버튼 동작 (기존 PauseUI와 동일)
    // ===================================================
    /// <summary>
    /// 도감 열기. 일시정지에서 곧장 <see cref="UIState.Codex"/>로 전환한다 —
    /// SetState가 이 일시정지 오버레이를 닫고(시간 복원) 도감 오버레이를 연다(도감이 다시 timeScale=0).
    /// 도감을 ESC로 닫으면 게임플레이가 아니라 이 일시정지 창으로 되돌아온다
    /// (<see cref="CodexOverlayUI.OpenFromPause"/>).
    /// </summary>
    private void OnCodex()
    {
        // 도감은 씬 배치본이 없어 스킨을 인스펙터로 못 넣는다 — 일시정지 메뉴의 스킨을 물려줘 같은 스프라이트로 그린다.
        // SetState(주 경로)와 아래 폴백 Open() 둘 다 커버하려면 전환 '전에' 넘겨야 한다.
        CodexOverlayUI.SetSkin(skin);
        // 도감을 닫을 때 게임으로 나가지 말고 일시정지 창으로 돌아오게 한다.
        CodexOverlayUI.OpenFromPause();

        var ui = UIStateManager.Instance;
        if (ui != null)
        {
            ui.SetState(UIState.Codex);
        }
        else
        {
            // UIStateManager가 없는 예외 상황 폴백 — 직접 닫고 연다.
            Close();
            CodexOverlayUI.Open();
        }
    }

    private void OnSettings()
    {
        CodeUI.PlaySfx(clickSfxName);
        // 설정은 이 위에 오버레이로 뜬다 — 일시정지(timeScale=0)는 유지되고,
        // SettingsOverlayUI가 닫힐 때 이전 timeScale(=0)을 그대로 복원한다.
        if (GameManager.Instance != null) GameManager.Instance.OpenSettings();
    }

    private void OnEmergencyEscape()
    {
        CodeUI.PlaySfx(clickSfxName);
        ShowConfirm("ui_pause_escape_title", "긴급 탈출",
            "ui_pause_escape_msg", "지상으로 즉시 돌아갑니다.\n캔 광물의 일부를 잃게 됩니다.",
            ExecuteEmergencyEscape);
    }

    private void ExecuteEmergencyEscape()
    {
        // 지상 진입 시 페널티를 적용하기 위한 대기 플래그
        LoadingData.IsEmergencyEscapePending = true;

        Time.timeScale = 1f;
        CloseWithoutRestoringTime(); // 위에서 이미 1로 되돌렸다

        // 탈출도 '지상으로 올라오는 것'이라 정상 귀환과 같이 시간대를 '오후'로 넘긴다.
        // 이게 없으면 아침이 유지되어 그날 다시 내려갈 수 있고(SceneTransitionTrigger.
        // IsClosedForToday가 오후에만 막는다), 광물 60%를 버리고도 한 탕 더 뛰는 쪽이
        // 이득이 되어 탈출 페널티가 뒤집힌다. 아래 저장보다 먼저여야 세이브에 실린다.
        if (DayCycleManager.Instance != null)
            DayCycleManager.Instance.SetAfternoon();

        // 탈출해서 올라가도 내려갈 때 쓴 입구 앞에 나온다(아래 저장보다 먼저 비워야 파일에 실린다).
        SurfaceReturnRouter.HandOff();

        // 씬 전환 직전에 인벤토리를 창고로 병합 (광물 유실 방지 + 광물 60% 페널티 집계)
        if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
            GameManager.Instance.saveManager.MergeInventoriesToWarehouse();

        // 사망과 같은 게임오버 연출을 한 번 재생한 뒤 로딩씬 → 지상 순으로 넘긴다.
        // 지상에 도착하면 UIStateManager가 EmergencyEscapeReport.HasPending을 보고 탈출 정산창을 띄운다.
        string upground = targetUpgroundScene;
        GameOverSequenceUI.Play(GameOverReason.EmergencyEscape, () => SceneLoader.LoadScene(upground));
    }

    private void OnMainMenu()
    {
        CodeUI.PlaySfx(clickSfxName);

        // 지하에서는 진행 내용이 저장되지 않으므로 경고를 띄운다
        if (SceneManager.GetActiveScene().name == targetUndergroundScene)
        {
            ShowConfirm("ui_pause_mainmenu_title", "메인 화면으로 이동",
                "ui_pause_mainmenu_msg", "지하에서의 진행 내용은 저장되지 않습니다.\n정말 메인 화면으로 이동하시겠습니까?",
                GoToMainMenu);
            return;
        }
        GoToMainMenu();
    }

    private void GoToMainMenu()
    {
        Time.timeScale = 1f;
        CloseWithoutRestoringTime();
        if (GameManager.Instance != null) GameManager.Instance.OpenMainMenu();
    }
}