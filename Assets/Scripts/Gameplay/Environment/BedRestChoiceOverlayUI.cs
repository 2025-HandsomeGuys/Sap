// @tags: environment, sleep, nap, ui, overlay, code-generated, popup, choice

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 침대 낮잠/수면 선택 오버레이 — <b>전부 코드 생성</b>(씬/프리팹 세팅 불필요).
/// NPC 대화처럼 "무엇을 할지" 고르는 작은 팝업이다. 프로젝트의 다른 코드 오버레이
/// (<see cref="ElevatorOverlayUI"/>, PauseOverlayUI 등)와 같은 패턴·같은 <see cref="UISkin"/>을 쓴다.
///
/// <see cref="BedInteractable"/>이 <b>낮잠·수면 둘 다 가능할 때만</b> 이 팝업을 띄운다.
/// 한쪽만 가능하면 팝업 없이 바로 그 동작을 실행한다.
///
/// 선택 결과는 콜백으로 돌려준다(실제 낮잠/수면 코루틴은 침대가 돌린다). 팝업을 먼저 닫고
/// 콜백을 호출하므로, 콜백 안에서 페이드·정산이 시작돼도 UI 상태가 꼬이지 않는다.
///
/// 씬에 미리 배치하면 인스펙터에서 스프라이트를 조정할 수 있고, 없으면 <see cref="Open"/>이
/// 임시 오브젝트를 만들어 기본 스타일로 띄운다.
/// </summary>
public class BedRestChoiceOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤 / 조회
    // ===================================================
    private static BedRestChoiceOverlayUI _instance;

    public static bool IsOpen => _instance != null && _instance._isOpen;
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    /// <summary>선택 팝업을 띄운다. 씬 배치 인스턴스가 있으면 그것을, 없으면 임시 오브젝트를 쓴다.</summary>
    public static void Open(int napRemaining, System.Action onNap, System.Action onSleep)
    {
        if (_instance == null)
        {
            _instance = FindFirstObjectByType<BedRestChoiceOverlayUI>(FindObjectsInactive.Include);
            if (_instance == null)
            {
                var go = new GameObject("BedRestChoiceOverlayUI");
                _instance = go.AddComponent<BedRestChoiceOverlayUI>();
            }
        }
        _instance.Show(napRemaining, onNap, onSleep);
    }

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

    [Header("시트 스프라이트 (버튼·패널 공통, Resources 자동 로드 — skin에 직접 지정하면 그게 우선)")]
    [Tooltip("Resources 하위 경로(확장자 제외). 다중 스프라이트 시트면 이름으로 서브스프라이트를 고른다")]
    [SerializeField] private string buttonSpriteResource = "UI/Stock_UISheet";
    [Tooltip("시트 안 서브스프라이트 이름. 버튼과 배경 패널에 같은 스프라이트를 쓴다")]
    [SerializeField] private string buttonSpriteName = "Stock_UISheet_8";
    [Tooltip("9-슬라이스 픽셀 배율. 작을수록 테두리(모서리)가 크게 렌더된다")]
    [Range(0.25f, 8f)][SerializeField] private float buttonSpritePPU = 0.4f;
    [Tooltip("스프라이트에 팔레트 색을 곱한다(흰색 9-슬라이스용). 이미 색이 입혀진 아트면 해제")]
    [SerializeField] private bool tintButtonSprite = true;

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.55f;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 560f;
    [SerializeField] private float buttonHeight = 60f;
    [Tooltip("일시정지(30800)보다 아래로 두면 ESC 메뉴가 위에 뜬다")]
    [SerializeField] private int sortingOrder = 30700;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;
    [SerializeField] private string moveSfxName = "ui_move";

    // ===================================================
    // 내부 상태
    // ===================================================
    private const float FadeDuration = 0.12f;

    private bool _built, _isOpen;
    private int _lastCloseFrame = -1;
    private int _lastOpenFrame = -1;

    private System.Action _onNap, _onSleep;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private TextMeshProUGUI _subtitle;
    private readonly LocTextBinder _loc = new LocTextBinder();

    private readonly List<ICodeNavItem> _nav = new List<ICodeNavItem>();
    private readonly CodeSlotNavigator _navigator = new CodeSlotNavigator();

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    public void Show(int napRemaining, System.Action onNap, System.Action onSleep)
    {
        if (_isOpen) return;
        _onNap = onNap;
        _onSleep = onSleep;

        EnsureBuilt();
        CodeUI.EnsureEventSystem();

        _loc.Refresh();
        _subtitle.text = string.Format(CodeUI.L("ui_bed_rest_remaining", "남은 낮잠 {0}회"), napRemaining);

        _canvasObj.SetActive(true);
        _isOpen = true;
        _lastOpenFrame = Time.frameCount;
        _navigator.Begin();

        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState != UIState.BedRest) ui.SetState(UIState.BedRest);

        StopAllCoroutines();
        StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
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

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        _onNap = _onSleep = null;

        _navigator.End();
        if (_canvasObj != null) _canvasObj.SetActive(false);

        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.BedRest) ui.SetState(UIState.None);
    }

    /// <summary>선택 실행 — UI를 먼저 접고(상태 None 복원) 콜백을 호출한다.</summary>
    private void Choose(System.Action action)
    {
        if (!_isOpen) return;
        CodeUI.PlaySfx(clickSfxName);

        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        _navigator.End();
        if (_canvasObj != null) _canvasObj.SetActive(false);

        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.BedRest) ui.SetState(UIState.None);

        _onNap = _onSleep = null;
        action?.Invoke();
    }

    private void Update()
    {
        if (!_isOpen) return;

        // 상태가 BedRest에서 벗어났으면(외부에서 닫힘) 정리한다.
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState != UIState.BedRest) { Close(); return; }

        // 연 그 프레임의 키 입력은 무시 — E나 ESC가 아직 눌린 채라 즉시 닫힐 수 있다.
        if (_lastOpenFrame == Time.frameCount) return;

        // Space가 Submit로도 잡혀 선택 버튼이 한 번 더 눌리는 것을 막는다.
        CodeUI.ClearSelection();

        if (Input.GetKeyDown(KeyCode.Escape)) { CodeUI.PlayBack(); Close(); return; }

        _navigator.Update();
    }

    private void OnDestroy()
    {
        _navigator.End();
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// 시트 9-슬라이스 스프라이트를 Resources에서 로드해 <b>버튼과 배경 패널 모두</b> skin에 물린다.
    /// 인스펙터에서 skin에 스프라이트를 직접 지정했다면 그대로 둔다(자동 로드는 비어 있을 때만).
    /// </summary>
    private void ApplyButtonSprite()
    {
        if (skin == null) skin = new UISkin();
        if (skin.buttonSprite != null || skin.panelSprite != null) return; // 인스펙터 지정 우선
        if (string.IsNullOrEmpty(buttonSpriteResource)) return;

        Sprite sprite = null;
        var all = Resources.LoadAll<Sprite>(buttonSpriteResource);
        if (all != null && all.Length > 0)
        {
            if (!string.IsNullOrEmpty(buttonSpriteName))
                foreach (var s in all)
                    if (s != null && s.name == buttonSpriteName) { sprite = s; break; }
            if (sprite == null) sprite = all[0]; // 이름을 못 찾으면 첫 서브스프라이트
        }

        if (sprite == null) return; // 로드 실패 시 코드 생성 라운드 스타일 유지

        // 버튼 + 배경 패널에 같은 스프라이트를 쓴다.
        skin.buttonSprite = sprite;
        skin.panelSprite = sprite;
        skin.spritePixelsPerUnit = buttonSpritePPU;
        skin.tintSprites = tintButtonSprite;
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        ApplyButtonSprite();

        _canvasObj = new GameObject("BedRestChoiceCanvas");
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

        var dim = CodeUI.CreateImage(_canvasObj.transform, "Dim", new Color(0f, 0f, 0f, dimAlpha), rounded: false);
        CodeUI.StretchFull(dim.rectTransform);
        var dimBtn = dim.gameObject.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(Close);   // 바깥 클릭 = 취소

        // ── 패널 ──
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

        var title = CodeUI.CreateText(panel.transform, "Title", 28f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        title.characterSpacing = 6f;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        _loc.Bind(title, "ui_bed_rest_title", "침대");

        _subtitle = CodeUI.CreateText(panel.transform, "Subtitle", 17f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        _subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        CodeUI.CreateDivider(panel.transform);

        // 낮잠 — 주식 1틱만
        var napBtn = CodeUI.CreateTextButton(panel.transform, "Nap", CodeUI.AccentFill, Color.white, 21f,
            () => Choose(_onNap), out var napLabel, skin.buttonSprite, skin, _loc);
        napBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = buttonHeight;
        _loc.Bind(napLabel, "ui_bed_nap", "낮잠 (다음 시세까지)");
        var napNav = CodeNavButton.Attach(napBtn, skin);
        if (napNav != null) _nav.Add(napNav);

        // 하루 마무리 — 수면
        var sleepBtn = CodeUI.CreateTextButton(panel.transform, "Sleep", CodeUI.WarnColor, CodeUI.GoldFg, 21f,
            () => Choose(_onSleep), out var sleepLabel, skin.buttonSprite, skin, _loc);
        sleepBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = buttonHeight;
        _loc.Bind(sleepLabel, "ui_bed_sleep", "하루 마무리 (수면)");
        var sleepNav = CodeNavButton.Attach(sleepBtn, skin);
        if (sleepNav != null) _nav.Add(sleepNav);

        var cancelBtn = CodeUI.CreateTextButton(panel.transform, "Cancel", CodeUI.NeutralBg, Color.white, 20f,
            Close, out var cancelLabel, skin.buttonSprite, skin, _loc);
        cancelBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = buttonHeight - 6f;
        _loc.Bind(cancelLabel, "ui_pause_cancel", "취소");
        var cancelNav = CodeNavButton.Attach(cancelBtn, skin);
        if (cancelNav != null) _nav.Add(cancelNav);

        var hint = CodeUI.CreateText(panel.transform, "Hint", 14f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        _loc.Bind(hint, "ui_bed_rest_hint", "W / S : 이동    Space : 선택    ESC : 취소");

        _navigator.moveSfxName = moveSfxName;
        _navigator.collect = list =>
        {
            list.Clear();
            list.AddRange(_nav);
        };

        _canvasObj.SetActive(false);
    }
}
