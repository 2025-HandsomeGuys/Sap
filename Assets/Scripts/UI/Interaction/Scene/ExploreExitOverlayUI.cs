// @tags: exit, surface, ui, overlay, code-generated, explore, underground, confirm

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지하에서 지상으로 나갈 때 뜨는 '탐험 종료' 확인 오버레이 — <b>전부 코드 생성</b>
/// (프리팹 <c>ConfirmationPrompt</c> 세팅 불필요). 다른 코드 오버레이와 같은 <see cref="UISkin"/>을 쓴다.
///
/// 흐름: 플레이어가 상단 Exit 트리거에 들어오면 <see cref="ExploreExitController"/>가 <see cref="Show"/>를 부른다.
/// 확인 → <see cref="ExploreExitController.ExecuteExit"/>(정산·씬 전환), 취소 → 그냥 닫힘.
///
/// 이 컴포넌트가 씬에 없으면 컨트롤러는 구 <see cref="ConfirmationPrompt"/> 프리팹으로 폴백한다.
/// </summary>
public class ExploreExitOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤 / 조회
    // ===================================================
    private static ExploreExitOverlayUI _instance;

    /// <summary>씬에 배치된 인스턴스를 찾는다. 없으면 null — 구 프롬프트 폴백 판단에 쓴다.</summary>
    public static ExploreExitOverlayUI FindInScene()
    {
        if (_instance != null) return _instance;
        _instance = FindFirstObjectByType<ExploreExitOverlayUI>(FindObjectsInactive.Include);
        return _instance;
    }

    public static bool IsOpen => _instance != null && _instance._isOpen;
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    public static void CloseStatic()
    {
        if (_instance != null) _instance.Cancel();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    // ===================================================
    // 인스펙터
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.62f;
    [Tooltip("지하에서는 끄는 것을 권장 — 화면 캡처에는 시야 어둠막이 들어오지 않아 갱도가 드러난다")]
    [SerializeField] private bool useBlurBackdrop = false;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 640f;
    [SerializeField] private float buttonHeight = 56f;
    [Tooltip("일시정지(30800)보다 아래로 두면 ESC 메뉴가 위에 뜬다")]
    [SerializeField] private int sortingOrder = 30700;

    [Header("Localization Keys")]
    [SerializeField] private string titleKey = "ui_explore_exit_title";
    [SerializeField] private string titleFallback = "탐험 종료";
    [SerializeField] private string messageKey = "ui_explore_exit_msg";
    [SerializeField] private string messageFallback = "정말 탐험을 종료하고 지상으로 올라가시겠습니까?\n캔 광물은 창고로 옮겨집니다.";

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;
    [SerializeField] private string moveSfxName = "ui_move";

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 내부 상태
    // ===================================================
    private const float FadeDuration = 0.12f;

    private bool _built, _isOpen, _opening;
    private int _lastCloseFrame = -1;
    private int _lastOpenFrame = -1;

    private System.Action _onConfirm, _onCancel;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    private TextMeshProUGUI _title, _message;

    private readonly List<ICodeNavItem> _navItems = new List<ICodeNavItem>();
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    /// <summary>확인창을 띄운다. 문구를 비우면 인스펙터의 기본 키·폴백을 쓴다.</summary>
    public void Show(System.Action onConfirm, System.Action onCancel = null,
        string title = null, string message = null)
    {
        if (_isOpen || _opening) return;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        StartCoroutine(OpenRoutine(title, message));
    }

    /// <summary>취소로 닫기 — 취소 콜백을 부른다.</summary>
    public void Cancel()
    {
        if (!_isOpen) return;
        var cancel = _onCancel;
        CloseInternal();
        CodeUI.PlaySfx(clickSfxName);
        cancel?.Invoke();
    }

    private void Confirm()
    {
        if (!_isOpen) return;
        var confirm = _onConfirm;
        CloseInternal();
        CodeUI.PlaySfx(clickSfxName);
        confirm?.Invoke();
    }

    private void CloseInternal()
    {
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        _onConfirm = null;
        _onCancel = null;

        _nav.End();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);
    }

    private IEnumerator OpenRoutine(string title, string message)
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();

        if (useBlurBackdrop)
        {
            _canvasObj.SetActive(false);
            yield return new WaitForEndOfFrame();
            _blur.Capture(_blurImage, blurDownsamples, flipBlurVertically);
        }
        _blurImage.enabled = useBlurBackdrop;
        _opening = false;

        _isOpen = true;
        _lastOpenFrame = Time.frameCount;

        _loc.Refresh();
        _title.text = string.IsNullOrEmpty(title) ? CodeUI.L(titleKey, titleFallback) : title;
        _message.text = string.IsNullOrEmpty(message) ? CodeUI.L(messageKey, messageFallback) : message;

        // UIStateManager 상태는 건드리지 않는다 — 구 ConfirmationPrompt와 같은 동작.
        // (ExploreExitController가 CurrentState != None이면 프롬프트 자체를 띄우지 않으므로,
        //  여기서 상태를 바꾸면 트리거 재진입 판정과 서로 물린다)
        _canvasObj.SetActive(true);
        _nav.Begin();

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
        if (_lastOpenFrame == Time.frameCount) return;

        CodeUI.ClearSelection();

        if (Input.GetKeyDown(KeyCode.Escape)) { CodeUI.PlayBack(); Cancel(); return; }
        _nav.Update();
    }

    private void OnDestroy()
    {
        _nav.End();
        _blur.Release(_blurImage);
        if (_instance == this) _instance = null;
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("ExploreExitOverlayCanvas");
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
        panelRt.sizeDelta = new Vector2(panelWidth, 0f);
        panelRt.anchoredPosition = Vector2.zero;

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 26, 24);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 제목
        var titleRow = CodeUI.CreateRow(panel.transform, "TitleRow", 42f, 10f, TextAnchor.MiddleCenter);
        var diamond = CodeUI.CreateText(titleRow, "Diamond", 20f, FontStyles.Normal, CodeUI.GoldColor,
            TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;
        _title = CodeUI.CreateText(titleRow, "Title", 28f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        _title.characterSpacing = 6f;

        CodeUI.CreateDivider(panel.transform);

        _message = CodeUI.CreateText(panel.transform, "Message", 19f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.Center, _loc);
        _message.textWrappingMode = TextWrappingModes.Normal;
        _message.gameObject.AddComponent<LayoutElement>().preferredHeight = 92f;

        // 확인(왼쪽) / 취소(오른쪽) — 다른 오버레이와 같은 순서
        var buttons = CodeUI.CreateRow(panel.transform, "Buttons", buttonHeight, 12f, TextAnchor.MiddleCenter);

        var ok = CodeUI.CreateTextButton(buttons, "Confirm", CodeUI.GoldColor, CodeUI.GoldFg, 21f,
            Confirm, out var okLabel, skin.buttonSprite, skin, _loc);
        var okLe = ok.gameObject.AddComponent<LayoutElement>();
        okLe.flexibleWidth = 1f;
        okLe.preferredHeight = buttonHeight;
        _loc.Bind(okLabel, "ui_explore_exit_confirm", "지상으로");
        var okNav = CodeNavButton.Attach(ok, skin);
        if (okNav != null) _navItems.Add(okNav);

        var cancel = CodeUI.CreateTextButton(buttons, "Cancel", CodeUI.NeutralBg, Color.white, 21f,
            Cancel, out var cancelLabel, skin.buttonSprite, skin, _loc);
        var cancelLe = cancel.gameObject.AddComponent<LayoutElement>();
        cancelLe.flexibleWidth = 1f;
        cancelLe.preferredHeight = buttonHeight;
        _loc.Bind(cancelLabel, "ui_pause_cancel", "취소");
        var cancelNav = CodeNavButton.Attach(cancel, skin);
        if (cancelNav != null) _navItems.Add(cancelNav);

        var hint = CodeUI.CreateText(panel.transform, "Hint", 14f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        _loc.Bind(hint, "ui_explore_exit_hint", "A / D : 이동    Space : 선택    ESC : 취소");

        _nav.moveSfxName = moveSfxName;
        _nav.collect = list =>
        {
            list.Clear();
            list.AddRange(_navItems);
        };

        _canvasObj.SetActive(false);
    }
}
