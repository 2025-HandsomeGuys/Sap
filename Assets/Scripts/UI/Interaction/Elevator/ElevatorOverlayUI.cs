// @tags: elevator, ui, overlay, code-generated, layer, stop, surface, exit, underground

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지하 엘리베이터 층 선택 오버레이 — <b>전부 코드 생성</b>(패널·버튼 프리팹 세팅 불필요).
/// 다른 코드 오버레이(PauseOverlayUI, EmergencyEscapeOverlayUI 등)와 같은 패턴·같은 <see cref="UISkin"/>을 쓴다.
///
/// 흐름: <see cref="ElevatorManager.OpenElevatorUI"/> → <see cref="Show"/> → 정류장 목록 표시 →
/// 클릭 시 <see cref="ElevatorManager.TeleportPlayer"/>. '지상으로 나가기'는 확인 후
/// <see cref="ExploreExitController.ExecuteExit"/>를 호출한다(구 ElevatorUI와 동일 동작).
///
/// 구 프리팹 기반 <see cref="ElevatorUI"/>는 이 컴포넌트가 씬에 없을 때만 폴백으로 쓰인다
/// (<see cref="ElevatorManager.OpenElevatorUI"/> 참고).
/// </summary>
public class ElevatorOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤 / 조회
    // ===================================================
    private static ElevatorOverlayUI _instance;

    /// <summary>씬에 배치된 인스턴스를 찾는다. 없으면 null — 구 ElevatorUI 폴백 판단에 쓴다.</summary>
    public static ElevatorOverlayUI FindInScene()
    {
        if (_instance != null) return _instance;
        _instance = FindFirstObjectByType<ElevatorOverlayUI>(FindObjectsInactive.Include);
        return _instance;
    }

    public static bool IsOpen => _instance != null && _instance._isOpen;
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    public static void CloseStatic()
    {
        if (_instance != null) _instance.Close();
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

    [Header("씬 전환 (비우면 씬에서 자동으로 찾는다)")]
    [SerializeField] private ExploreExitController exitController;

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.62f;
    [Tooltip("지하에서는 끄는 것을 권장 — 화면 캡처에는 시야 어둠막이 들어오지 않아 갱도가 드러난다")]
    [SerializeField] private bool useBlurBackdrop = false;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 620f;
    [SerializeField] private float listMaxHeight = 460f;
    [SerializeField] private float rowHeight = 62f;
    [SerializeField] private float buttonHeight = 56f;
    [Tooltip("일시정지(30800)보다 아래로 두면 ESC 메뉴가 위에 뜬다")]
    [SerializeField] private int sortingOrder = 30700;

    [Header("동작")]
    [Tooltip("끄면 '지상으로 나가기' 버튼을 숨긴다")]
    [SerializeField] private bool showSurfaceExitButton = true;

    [Header("Localization Keys")]
    [SerializeField] private string surfaceWarningTitleKey = "ui_elevator_surface_title";
    [SerializeField] private string surfaceWarningMsgKey = "ui_elevator_surface_msg";

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;
    [SerializeField] private string moveSfxName = "ui_move";

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";
    [Tooltip("미해금(직접 가본 적 없는) 정류장 표시")]
    [SerializeField] private string lockGlyph = "✕";

    // ===================================================
    // 내부 상태
    // ===================================================
    private const float FadeDuration = 0.12f;

    private bool _built, _isOpen, _opening;
    private int _lastCloseFrame = -1;
    private int _lastOpenFrame = -1;

    private int _xChunk;
    private int _currentLayer;
    private bool _warnedNoExitController;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    private RectTransform _listContent;
    private TextMeshProUGUI _subtitle;
    private Button _surfaceButton;
    private CodeConfirmPopup _confirm;

    // _stopNav은 목록을 다시 그릴 때마다 갈아엎고, _fixedNav(지상 이동·닫기)는 한 번 만들면 유지된다.
    private readonly List<ICodeNavItem> _stopNav = new List<ICodeNavItem>();
    private readonly List<ICodeNavItem> _fixedNav = new List<ICodeNavItem>();
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    /// <summary>정류장 목록을 띄운다. xChunk = 엘리베이터 기둥의 청크 X, currentLayer = 지금 서 있는 정류장.</summary>
    public void Show(int xChunk, int currentLayer)
    {
        if (_isOpen || _opening) return;
        _xChunk = xChunk;
        _currentLayer = currentLayer;
        StartCoroutine(OpenRoutine());
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        CodeUI.PlaySfx(clickSfxName);

        _confirm?.Hide();
        _nav.End();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        // 스스로 닫혔다면 UIStateManager 상태도 되돌린다.
        // 이 시점엔 이미 _isOpen=false라 SetState가 CloseStatic을 다시 불러도 즉시 반환된다(재귀 없음).
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Elevator) ui.SetState(UIState.None);
    }

    /// <summary>씬 전환처럼 상태 복원을 호출자가 직접 하는 경우 — UI만 접는다.</summary>
    private void CloseSilently()
    {
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        _confirm?.Hide();
        _nav.End();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);
    }

    private IEnumerator OpenRoutine()
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        ResolveExitController();

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
        _confirm?.Hide();
        BuildStopRows();

        if (_surfaceButton != null)
            _surfaceButton.gameObject.SetActive(showSurfaceExitButton && exitController != null);

        _canvasObj.SetActive(true);
        _nav.Begin();

        // UIStateManager 상태 동기화 (전역 단축키·플레이어 입력 차단)
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState != UIState.Elevator) ui.SetState(UIState.Elevator);

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

        // 연 그 프레임의 키 입력은 무시 — 상호작용 키(E)나 ESC가 아직 눌린 채라 즉시 닫힐 수 있다.
        if (_lastOpenFrame == Time.frameCount) return;

        // Space가 Submit로도 잡혀 선택된 버튼이 한 번 더 눌리는 것을 막는다
        CodeUI.ClearSelection();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CodeUI.PlayBack();
            // 확인 팝업이 떠 있으면 그것부터 닫는다
            if (_confirm != null && _confirm.IsOpen) { HideConfirm(); return; }
            Close();
            return;
        }

        // 팝업이 떠 있어도 내비게이터는 계속 돈다 — collect가 팝업 버튼만 넘겨주므로
        // A/D로 확인↔취소를 오가고 Space로 실행된다.
        _nav.Update();
    }

    /// <summary>확인 팝업을 닫고 커서를 뒤쪽 목록으로 되돌린다.</summary>
    private void HideConfirm()
    {
        _confirm?.Hide();
        _nav.ClearFocus();   // 커서 대상이 팝업 → 목록으로 통째로 바뀐다
    }

    private void OnDestroy()
    {
        _nav.End();
        _blur.Release(_blurImage);
        if (_instance == this) _instance = null;
    }

    private void ResolveExitController()
    {
        if (exitController != null) return;
        exitController = FindFirstObjectByType<ExploreExitController>(FindObjectsInactive.Include);

        if (exitController == null && showSurfaceExitButton && !_warnedNoExitController)
        {
            _warnedNoExitController = true;
            Debug.LogWarning("[ElevatorOverlayUI] ExploreExitController가 씬에 없어 '지상으로 나가기'를 숨깁니다.");
        }
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("ElevatorOverlayCanvas");
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

        // ── 패널 (내용에 맞춰 세로로 늘어난다) ──
        var panel = CodeUI.CreateImage(_canvasObj.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(panelWidth, 0f);
        panelRt.anchoredPosition = Vector2.zero;

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(26, 26, 24, 24);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 제목
        var titleRow = CodeUI.CreateRow(panel.transform, "TitleRow", 44f, 10f, TextAnchor.MiddleCenter);
        var diamond = CodeUI.CreateText(titleRow, "Diamond", 20f, FontStyles.Normal, CodeUI.GoldColor,
            TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;
        var title = CodeUI.CreateText(titleRow, "Title", 29f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        title.characterSpacing = 8f;
        _loc.Bind(title, "ui_elevator_title", "엘리베이터");

        _subtitle = CodeUI.CreateText(panel.transform, "Subtitle", 17f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        _subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        CodeUI.CreateDivider(panel.transform);

        // 정류장 목록 (많아지면 스크롤)
        var scroll = CodeUI.CreateScrollView(panel.transform, "Stops", out _listContent);
        var scrollLe = scroll.gameObject.AddComponent<LayoutElement>();
        scrollLe.preferredHeight = listMaxHeight;
        scrollLe.flexibleHeight = 0f;
        var listLayout = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 8f;
        listLayout.padding = new RectOffset(2, 8, 2, 2);
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        CodeUI.CreateDivider(panel.transform);

        // 지상으로 나가기 / 닫기
        _surfaceButton = CodeUI.CreateTextButton(panel.transform, "SurfaceExit", CodeUI.WarnColor, CodeUI.GoldFg, 21f,
            OnSurfaceClicked, out var surfaceLabel, skin.buttonSprite, skin, _loc);
        _surfaceButton.gameObject.AddComponent<LayoutElement>().preferredHeight = buttonHeight;
        _loc.Bind(surfaceLabel, "ui_elevator_surface", "지상으로 나가기");
        var surfaceNav = CodeNavButton.Attach(_surfaceButton, skin);
        if (surfaceNav != null) _fixedNav.Add(surfaceNav);

        var closeBtn = CodeUI.CreateTextButton(panel.transform, "Close", CodeUI.NeutralBg, Color.white, 21f,
            Close, out var closeLabel, skin.buttonSprite, skin, _loc);
        closeBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = buttonHeight;
        _loc.Bind(closeLabel, "ui_close", "닫기");
        var closeNav = CodeNavButton.Attach(closeBtn, skin);
        if (closeNav != null) _fixedNav.Add(closeNav);

        var hint = CodeUI.CreateText(panel.transform, "Hint", 14f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        _loc.Bind(hint, "ui_elevator_hint", "W / S : 이동    Space : 선택    ESC : 닫기");

        _confirm = new CodeConfirmPopup(_canvasObj.transform, skin, _loc,
            "ui_elevator_confirm", "이동", () => CodeUI.PlaySfx(clickSfxName));

        _nav.moveSfxName = moveSfxName;
        _nav.collect = list =>
        {
            list.Clear();

            // 확인 팝업이 떠 있으면 커서는 팝업 안(확인/취소)에서만 움직인다.
            if (_confirm != null && _confirm.IsOpen)
            {
                _confirm.CollectNavItems(list);
                return;
            }

            list.AddRange(_stopNav);
            list.AddRange(_fixedNav);
        };

        _canvasObj.SetActive(false);
    }

    // ===================================================
    // 정류장 목록
    // ===================================================
    private void BuildStopRows()
    {
        _stopNav.Clear();
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);

        // ElevatorManager가 있으면 그쪽 목록이 정답(착지 좌표 계산과 같은 소스).
        // 없으면 카탈로그로 폴백해 UI만이라도 뜨게 한다.
        var stops = ElevatorManager.Instance != null && ElevatorManager.Instance.layers != null
            ? ElevatorManager.Instance.layers
            : ElevatorLayerCatalog.Build();

        _subtitle.text = string.Format(CodeUI.L("ui_elevator_position", "현재 위치  X : {0}"), _xChunk);

        for (int i = 0; i < stops.Count; i++)
            CreateStopRow(stops[i], i);

        _nav.Refresh();
    }

    private void CreateStopRow(LayerInfo stop, int index)
    {
        bool isCurrent = index == _currentLayer;
        bool isLoaded = ElevatorManager.Instance != null &&
                        ElevatorManager.Instance.GetElevatorAt(_xChunk, index) != null;

        // 직접 가본 적 없는 정류장은 고를 수 없다(ElevatorStopUnlockStore).
        // 지금 서 있는 층은 도달 즉시 해금되므로 isCurrent면 언제나 해금 상태다.
        bool unlocked = isCurrent || ElevatorStopUnlockStore.IsUnlocked(stop);

        Color bg = isCurrent ? CodeUI.AccentFill : (unlocked && isLoaded ? CodeUI.CardBg : CodeUI.SlotBg);

        // Unity 오브젝트에 ??를 쓰면 fake-null을 통과하므로 명시적으로 비교한다
        Sprite rowSprite = skin.slotSprite != null ? skin.slotSprite : skin.buttonSprite;

        var btn = CodeUI.CreateButton(_listContent, "Stop_" + index, bg,
            () => { if (unlocked) OnStopClicked(index); }, rowSprite, skin);
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;
        btn.interactable = !isCurrent && unlocked;   // 현재 층은 선택 불가 (구 ElevatorUI와 동일)

        var row = btn.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(18, 18, 6, 6);
        row.spacing = 10f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        row.childAlignment = TextAnchor.MiddleLeft;

        var dot = CodeUI.CreateText(btn.transform, "Dot", 14f, FontStyles.Normal,
            isCurrent ? CodeUI.GoldColor : CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        dot.text = unlocked ? diamondGlyph : lockGlyph;
        dot.gameObject.AddComponent<LayoutElement>().preferredWidth = 20f;

        var name = CodeUI.CreateText(btn.transform, "Name", 21f, FontStyles.Bold,
            isCurrent ? Color.white : (unlocked ? CodeUI.LabelColor : CodeUI.MutedColor),
            TextAlignmentOptions.MidlineLeft, _loc);
        name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        // 미해금 정류장은 이름·깊이를 가린다 — 몇 층이 더 있는지는 보이되 무엇인지는 숨긴다.
        name.text = unlocked ? stop.layerName : CodeUI.L("ui_elevator_locked_name", "???");

        var depth = CodeUI.CreateText(btn.transform, "Depth", 16f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.MidlineRight, _loc);
        depth.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;
        depth.text = unlocked
            ? string.Format(CodeUI.L("ui_elevator_depth", "깊이 {0}"), stop.startDepth)
            : CodeUI.L("ui_elevator_locked", "미발견");

        var tag = CodeUI.CreateText(btn.transform, "Tag", 15f, FontStyles.Bold,
            isCurrent ? CodeUI.GoldColor : CodeUI.MutedColor, TextAlignmentOptions.MidlineRight, _loc);
        tag.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;
        tag.text = isCurrent ? CodeUI.L("ui_elevator_here", "현재 위치")
                 : !unlocked ? ""
                 : (isLoaded ? "" : CodeUI.L("ui_elevator_unloaded", "미로드"));

        // 잠긴 줄은 커서가 서지 않게 목록에서 뺀다(들어가도 아무 일도 안 일어나므로).
        if (!unlocked) return;

        var navItem = CodeNavButton.Attach(btn, skin);
        if (navItem != null) _stopNav.Add(navItem);
    }

    // ===================================================
    // 버튼 동작
    // ===================================================
    private void OnStopClicked(int targetLayer)
    {
        CodeUI.PlaySfx(clickSfxName);
        if (targetLayer == _currentLayer) return;

        // 텔레포트는 페이드·착지 보호 코루틴을 돌리므로 UI를 먼저 접는다.
        // 상태는 TeleportPlayer 쪽에서 관리되므로 여기서는 UIStateManager를 None으로 되돌린다.
        Close();

        if (ElevatorManager.Instance != null)
            ElevatorManager.Instance.TeleportPlayer(_xChunk, targetLayer);
        else
            Debug.LogError("[ElevatorOverlayUI] ElevatorManager.Instance가 없어 이동할 수 없습니다.");
    }

    private void OnSurfaceClicked()
    {
        CodeUI.PlaySfx(clickSfxName);
        if (exitController == null) return;

        _confirm.Show(
            CodeUI.L(surfaceWarningTitleKey, "지상으로 이동"),
            CodeUI.L(surfaceWarningMsgKey, "정산 후 지상으로 이동하시겠습니까?"),
            ExecuteSurfaceExit);

        // 커서가 뒤에 깔린 '지상으로 나가기' 버튼을 문 채 남으면 팝업 위에서 Space를 눌렀을 때
        // 팝업이 다시 열린다. 비운 뒤 팝업 첫 항목(확인)으로 넣어 준다 —
        // 포커스가 없으면 Space가 아무 일도 안 해 '안 먹는' 것처럼 보인다.
        _nav.ClearFocus();
        _nav.Move(Vector2.right);   // 포커스가 없을 땐 목록 첫 항목(확인)에 커서만 올린다
    }

    private void ExecuteSurfaceExit()
    {
        // 씬 전환이 시작되므로 UI 상태를 직접 정리한다(새 씬은 None으로 시작해야 한다).
        CloseSilently();
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Elevator) ui.SetState(UIState.None);

        exitController.ExecuteExit();   // 두 번째 확인창 없이 바로 이동
    }
}
