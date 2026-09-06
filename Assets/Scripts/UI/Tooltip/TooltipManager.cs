// @tags: ui, tooltip, code-generated, sprite, 9-slice, hover, singleton, overlay

using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 범용 툴팁 매니저 — 마우스 호버 시 상세 설명 표시.
///
/// [재작성 배경] 기존 매니저는 패널을 씬의 아무 Canvas(FindFirstObjectByType)에 붙여,
/// 창고·인벤토리처럼 자체 고-sortingOrder 캔버스를 코드로 만드는 오버레이 위에서
/// 패널이 오버레이 뒤에 깔려 보이지 않는 버그가 있었다.
/// 이제 매니저가 <b>자기 전용 최상위 오버레이 캔버스</b>(sortingOrder=32760)를 소유하고
/// DontDestroyOnLoad로 유지 → 어떤 오버레이보다도 항상 위에 그려진다.
///
/// [스프라이트] 다른 코드 생성 UI(CodeUI/UISkin)와 같은 규칙.
/// backgroundSprite를 비우면 코드 생성 둥근 박스로 폴백한다.
/// 인스펙터에서 조정하려면 씬에 TooltipManager 오브젝트를 하나 두고 값을 채우면 되고,
/// 아무것도 두지 않아도 Instance가 기본값으로 자동 생성되어 그대로 동작한다.
/// </summary>
public class TooltipManager : MonoBehaviour
{
    private static bool _isQuitting = false;
    private static TooltipManager _instance;
    public static TooltipManager Instance
    {
        get
        {
            if (_isQuitting) return null;

            if (_instance == null)
            {
                _instance = FindFirstObjectByType<TooltipManager>();
                if (_instance == null && !_isQuitting)
                {
                    GameObject go = new GameObject("TooltipManager");
                    _instance = go.AddComponent<TooltipManager>();
                }
            }
            return _instance;
        }
    }

    [Header("배경 스프라이트 (비우면 코드 생성 둥근 박스)")]
    [Tooltip("툴팁 배경 프레임. 9-슬라이스(테두리 있는) 스프라이트를 권장")]
    public Sprite backgroundSprite;
    [Tooltip("9-슬라이스 픽셀 배율. 클수록 테두리(모서리)가 작게 렌더된다")]
    [Range(0.25f, 8f)] public float spritePixelsPerUnit = 1f;
    [Tooltip("체크 시 스프라이트에 backgroundColor를 곱한다(흰색 도트용). 이미 색이 입혀진 도트면 해제")]
    public bool tintBackground = true;

    [Header("색상")]
    public Color backgroundColor = new Color(0.067f, 0.102f, 0.180f, 0.97f); // CodeUI.PanelBg 계열
    public Color titleColor = new Color(0.98f, 0.86f, 0.35f, 1f);            // 금색 강조
    public Color contentColor = new Color(0.78f, 0.81f, 0.89f, 1f);

    [Header("레이아웃")]
    [Tooltip("내용 텍스트 최대 너비(줄바꿈 유도). 4K 기준 px")]
    public float maxWidth = 320f;
    public float titleFontSize = 26f;
    public float contentFontSize = 20f;
    public Vector2 padding = new Vector2(18f, 14f); // (좌우, 상하)
    public float spacing = 6f;

    [Header("동작")]
    public float offsetX = 24f;   // 마우스로부터의 X 오프셋
    public float offsetY = 24f;   // 마우스로부터의 Y 오프셋
    public float showDelay = 0.3f; // 표시 지연 시간

    // ── 런타임 생성물 ──
    private Canvas _canvas;
    private RectTransform _canvasRect;
    private GameObject _panel;
    private RectTransform _panelRect;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _contentText;
    private LayoutElement _contentLayout;

    // ── 상태 ──
    private bool _isShowing;
    private float _showTimer;
    private string _pendingTitle = "";
    private string _pendingContent = "";
    private TooltipTrigger _pendingOwner;
    private Coroutine _hideCoroutine;

    // 툴팁을 띄운 트리거(슬롯) 추적 — exit 이벤트가 유실돼도(슬롯 파괴/리빌드, 창 이탈 등)
    // Update에서 owner 상태를 감시해 잔류 툴팁을 정리한다.
    private TooltipTrigger _activeOwner;
    private RectTransform _ownerRect;
    private Camera _ownerCamera;
    private bool _hasOwner;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // 씬마다 TooltipManager를 둘 수 있게: 이미 살아있는 싱글톤이 이 씬 인스턴스의
            // 스킨/설정을 흡수하고 패널을 다시 그린 뒤, 이 인스턴스는 파괴한다.
            _instance.AdoptSettings(this);
            Destroy(gameObject);
            return;
        }

        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        BuildCanvasAndPanel();
    }

    void Update()
    {
        if (_isShowing && _panel != null && _panel.activeSelf)
        {
            UpdateTooltipPosition();

            // 잔류 툴팁 방지: 트리거가 사라졌거나(파괴·비활성) 마우스가 트리거 영역을 벗어났는데
            // OnPointerExit이 오지 않은 경우를 감시해 정리
            if (_hasOwner)
            {
                if (_activeOwner == null || !_activeOwner.isActiveAndEnabled)
                {
                    HideTooltip();
                }
                else if (_hideCoroutine == null && _ownerRect != null &&
                         !RectTransformUtility.RectangleContainsScreenPoint(_ownerRect, Input.mousePosition, _ownerCamera))
                {
                    // 즉시 숨기지 않고 유예 — 인접 슬롯으로 이동 중이면 다음 Enter가 취소해 깜빡임 없음
                    RequestHide();
                }
            }
        }

        // 지연 표시 처리
        if (!string.IsNullOrEmpty(_pendingTitle) || !string.IsNullOrEmpty(_pendingContent))
        {
            _showTimer += Time.unscaledDeltaTime;
            if (_showTimer >= showDelay)
            {
                ShowTooltip(_pendingTitle, _pendingContent, _pendingOwner);
                _pendingTitle = "";
                _pendingContent = "";
                _pendingOwner = null;
                _showTimer = 0f;
            }
        }
    }

    // ===================================================
    // 생성
    // ===================================================
    private void BuildCanvasAndPanel()
    {
        // 전용 최상위 오버레이 캔버스 — 어떤 씬 오버레이보다 위에 그려지도록
        var canvasObj = new GameObject("TooltipCanvas");
        canvasObj.transform.SetParent(transform, false);
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32760; // short.MaxValue 근처 — 사실상 항상 최상단

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f); // 씬 루트 캔버스와 동일(4K 기준)
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        // GraphicRaycaster는 일부러 붙이지 않는다 — 툴팁은 절대 아래 슬롯의 호버를 가로채면 안 됨.

        _canvasRect = canvasObj.GetComponent<RectTransform>();

        BuildPanel();
    }

    private void BuildPanel()
    {
        if (_canvas == null) return;

        // 배경 이미지 (CodeUI 규칙 재사용 — 스프라이트 없으면 둥근 박스 폴백)
        var opt = new UISkin { spritePixelsPerUnit = spritePixelsPerUnit, tintSprites = tintBackground };
        Image bg = CodeUI.CreateImage(_canvas.transform, "TooltipPanel", backgroundColor,
            backgroundSprite, opt, rounded: true);
        _panel = bg.gameObject;
        _panelRect = _panel.GetComponent<RectTransform>();
        _panelRect.pivot = new Vector2(0f, 1f); // 좌상단 기준(마우스 우하단에 뜨도록)

        // 호버·클릭을 절대 가로채지 않도록
        var cg = _panel.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        // 세로 레이아웃 + 내용에 맞춰 크기 자동
        var layout = _panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset((int)padding.x, (int)padding.x, (int)padding.y, (int)padding.y);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = _panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 제목
        _titleText = CodeUI.CreateText(_panel.transform, "Title", titleFontSize, FontStyles.Bold,
            titleColor, TextAlignmentOptions.TopLeft);
        _titleText.textWrappingMode = TextWrappingModes.Normal;

        // 내용
        _contentText = CodeUI.CreateText(_panel.transform, "Content", contentFontSize, FontStyles.Normal,
            contentColor, TextAlignmentOptions.TopLeft);
        _contentText.textWrappingMode = TextWrappingModes.Normal;

        // 너비 상한 → 긴 내용은 줄바꿈
        _contentLayout = _contentText.gameObject.AddComponent<LayoutElement>();
        _contentLayout.preferredWidth = maxWidth;

        _panel.SetActive(false);
    }

    /// <summary>씬에 새로 놓인 TooltipManager의 스킨·설정을 흡수하고 패널을 다시 그린다.</summary>
    private void AdoptSettings(TooltipManager src)
    {
        if (src == null) return;

        backgroundSprite = src.backgroundSprite;
        spritePixelsPerUnit = src.spritePixelsPerUnit;
        tintBackground = src.tintBackground;
        backgroundColor = src.backgroundColor;
        titleColor = src.titleColor;
        contentColor = src.contentColor;
        maxWidth = src.maxWidth;
        titleFontSize = src.titleFontSize;
        contentFontSize = src.contentFontSize;
        padding = src.padding;
        spacing = src.spacing;
        offsetX = src.offsetX;
        offsetY = src.offsetY;
        showDelay = src.showDelay;

        HideTooltip();
        if (_panel != null) Destroy(_panel);
        _panel = null;
        BuildPanel();
    }

    // ===================================================
    // 표시 / 숨김 (공개 API — 기존과 시그니처 동일)
    // ===================================================
    public void ShowTooltip(string title, string content, TooltipTrigger owner = null)
    {
        CancelHide();

        _activeOwner = owner;
        _hasOwner = owner != null;
        _ownerRect = null;
        _ownerCamera = null;
        if (_hasOwner)
        {
            _ownerRect = owner.transform as RectTransform;
            Canvas ownerCanvas = owner.GetComponentInParent<Canvas>();
            if (ownerCanvas != null && ownerCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                _ownerCamera = ownerCanvas.worldCamera;
        }

        if (_panel == null)
        {
            if (_canvas == null) BuildCanvasAndPanel();
            else BuildPanel();
        }

        var font = LanguageManager.Instance != null ? LanguageManager.Instance.GetCurrentFont() : null;
        if (_titleText != null)
        {
            _titleText.text = title;
            if (font != null) _titleText.font = font;
            _titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
        }
        if (_contentText != null)
        {
            _contentText.text = content;
            if (font != null) _contentText.font = font;
            _contentText.gameObject.SetActive(!string.IsNullOrEmpty(content));
        }

        _panel.SetActive(true);
        _isShowing = true;

        LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
        UpdateTooltipPosition();
    }

    public void ShowTooltipDelayed(string title, string content, TooltipTrigger owner = null)
    {
        CancelHide();

        // 이미 보이는 중이면 딜레이 없이 즉시 교체(빠른 탐색)
        if (_isShowing)
        {
            ShowTooltip(title, content, owner);
            return;
        }

        _pendingTitle = title;
        _pendingContent = content;
        _pendingOwner = owner;
        _showTimer = 0f;
    }

    public void HideTooltip()
    {
        CancelHide();

        if (_panel != null) _panel.SetActive(false);
        _isShowing = false;
        _pendingTitle = "";
        _pendingContent = "";
        _pendingOwner = null;
        _showTimer = 0f;
        _activeOwner = null;
        _ownerRect = null;
        _ownerCamera = null;
        _hasOwner = false;
    }

    /// <summary>외부에서 숨기기 요청 (약간의 유예 후 숨김 — 인접 슬롯 전환 깜빡임 방지).</summary>
    public void RequestHide(float delay = 0.1f)
    {
        CancelHide();
        if (isActiveAndEnabled)
            _hideCoroutine = StartCoroutine(HideTooltipCoroutine(delay));
        else
            HideTooltip();
    }

    private void CancelHide()
    {
        if (_hideCoroutine != null)
        {
            StopCoroutine(_hideCoroutine);
            _hideCoroutine = null;
        }
    }

    private System.Collections.IEnumerator HideTooltipCoroutine(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        _hideCoroutine = null;
        HideTooltip();
    }

    // ===================================================
    // 위치
    // ===================================================
    private void UpdateTooltipPosition()
    {
        if (_panelRect == null || _canvasRect == null) return;

        Vector2 mousePos = Input.mousePosition;

        // 마우스 위치를 캔버스 로컬 좌표로 (오버레이 캔버스이므로 카메라 null)
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, mousePos, null, out localPoint);

        float tooltipWidth = _panelRect.rect.width;
        float tooltipHeight = _panelRect.rect.height;

        float canvasWidth = _canvasRect.rect.width;
        float canvasHeight = _canvasRect.rect.height;
        float margin = 20f;

        // 기본: 마우스 오른쪽 아래
        float x = localPoint.x + offsetX;
        float y = localPoint.y - offsetY;

        // 오른쪽으로 넘치면 마우스 왼쪽에 표시
        if (x + tooltipWidth > canvasWidth * 0.5f - margin)
            x = localPoint.x - tooltipWidth - offsetX;
        // 그래도 왼쪽을 벗어나면 왼쪽 끝 고정
        if (x < -canvasWidth * 0.5f + margin)
            x = -canvasWidth * 0.5f + margin;

        // 아래로 넘치면 마우스 위에 표시
        if (y - tooltipHeight < -canvasHeight * 0.5f + margin)
            y = localPoint.y + tooltipHeight + offsetY;
        // 그래도 위를 벗어나면 위쪽 끝 고정
        if (y > canvasHeight * 0.5f - margin)
            y = canvasHeight * 0.5f - margin;

        _panelRect.anchoredPosition = new Vector2(x, y);
    }

    private void OnApplicationQuit()
    {
        _isQuitting = true;
    }
}
