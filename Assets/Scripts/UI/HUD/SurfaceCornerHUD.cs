// @tags: hud, surface, corner, ui, code-generated, base, visibility, canvas

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 지상 화면의 <b>모서리에 붙는 코드 생성 HUD</b>의 공통 베이스.
///
/// 캔버스/컨테이너 해석, 모서리 앵커(좌상/우상/좌하/우하), <see cref="SurfaceSceneRegistry"/> 기반
/// '지상일 때만' 표시(던전 안에서는 숨김), 언어 변경 구독·씬 전환 대응·폴링을 한곳에서 처리한다.
/// 실제 내용은 <see cref="BuildContent"/>/<see cref="RefreshContent"/>를 구현하는 서브클래스가 채운다
/// (날짜/시간 = <see cref="SurfaceDateTimeHUD"/>, 퀘스트 = <see cref="SurfaceQuestHUD"/>).
///
/// 앵커링 주의: 새 캔버스를 만들지 않을 때는 <b>이 오브젝트의 부모 RectTransform</b>을 기준으로 삼는다.
/// 부모를 캔버스 크기에 맞춰 두면 그 모서리에 정확히 붙는다(예전에 여기서 캔버스를 겹쳐 깔아 가운데로 가던 버그가 있었다).
/// </summary>
public abstract class SurfaceCornerHUD : MonoBehaviour
{
    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    [Header("배치")]
    [Tooltip("붙일 모서리")]
    [SerializeField] protected Corner corner = Corner.TopLeft;
    [Tooltip("모서리에서 안쪽으로 들어오는 여백")]
    [SerializeField] protected Vector2 cornerOffset = new Vector2(24f, 24f);
    [Tooltip("자체 Canvas를 만든다. 이미 Canvas 밑에 배치했다면 해제")]
    [SerializeField] protected bool createOwnCanvas = true;
    [Tooltip("자체 Canvas의 정렬 순서. 모달 오버레이(수천대)보다 낮게 둔다")]
    [SerializeField] protected int sortingOrder = 50;
    [Tooltip("블록/줄 사이 세로 간격")]
    [SerializeField] protected float blockSpacing = 8f;

    [Header("표시 조건")]
    [Tooltip("SurfaceSceneRegistry 목록에 더해 지상으로 볼 씬. 보통 비워둔다")]
    [SerializeField] protected string[] extraSurfaceScenes;
    [Tooltip("지상 판정 폴링 간격(초)")]
    [SerializeField] protected float visibilityPollInterval = 0.4f;

    protected readonly LocTextBinder _binder = new LocTextBinder();

    private GameObject _canvasObj;
    private GameObject _hudRoot;   // 켜고 끄는 대상 = 컬럼 루트 (컴포넌트 자신은 계속 폴링해야 하므로)
    private bool _built;
    private bool _subscribedLang;
    private float _pollTimer;
    private bool _surfaceVisible;  // 지상 && 던전 아님
    private bool _covered;         // 페이드막이 화면을 덮고 있음
    private bool _wasDialogue;     // 직전 프레임 대화 상태였음
    private bool _visApplied;      // 최초 1회 강제 적용용
    protected bool _lastVisible;   // 실제 표시 여부 = 지상 && !커버

    protected bool AnchorRight => corner == Corner.TopRight || corner == Corner.BottomRight;
    protected bool AnchorBottom => corner == Corner.BottomLeft || corner == Corner.BottomRight;

    // ===================================================
    // 라이프사이클
    // ===================================================
    protected virtual void Start()
    {
        Build();
        EnsureSubscriptions();
        RefreshContent();
        RefreshSurface();
        _covered = IsScreenCovered();
        ApplyVisibility();
    }

    protected virtual void OnEnable()
    {
        EnsureSubscriptions();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    protected virtual void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        RemoveSubscriptions();
    }

    protected virtual void Update()
    {
        // 매니저가 Start 시점에 아직 없었으면 여기서 붙는다.
        EnsureSubscriptions();

        // 페이드 커버는 매 프레임 반영한다 — 시간대 스와프를 페이드로 가리려면 커버 진입/해제를
        // 폴링 간격(0.4s)만큼 놓치면 안 된다. 커버 상태가 바뀌는 순간 표시를 토글하고 내용도 확정한다.
        // 대화 상태 진입/해제 시에도 즉각 숨기기/보이기를 반영한다.
        bool coveredNow = IsScreenCovered();
        bool dialogueNow = (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState == UIState.Dialogue);

        if (coveredNow != _covered || dialogueNow != _wasDialogue)
        {
            _covered = coveredNow;
            _wasDialogue = dialogueNow;
            ApplyVisibility();
            RefreshContent(); // 커버된 채(안 보이는 동안) 대기 값 반영, 해제 시 최신 반영
        }

        _pollTimer += Time.unscaledDeltaTime;
        if (_pollTimer < visibilityPollInterval) return;
        _pollTimer = 0f;

        RefreshSurface();
        ApplyVisibility();
        if (_lastVisible) PollContent();
    }

    // ===================================================
    // 구독 (언어는 공통, 그 외는 서브클래스가 base 호출 후 확장)
    // ===================================================
    protected virtual void EnsureSubscriptions()
    {
        if (!_subscribedLang && LanguageManager.Instance != null)
        {
            LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
            _subscribedLang = true;
            RefreshContent(); // 매니저가 늦게 붙었으면 최신 값으로 한 번 당겨온다
        }
    }

    protected virtual void RemoveSubscriptions()
    {
        if (_subscribedLang && LanguageManager.Instance != null)
        {
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
            _subscribedLang = false;
        }
    }

    private void OnLanguageChanged(LanguageType _) => RefreshContent();

    private void OnActiveSceneChanged(Scene from, Scene to)
    {
        RefreshSurface();
        _covered = IsScreenCovered();
        ApplyVisibility();
    }

    // ===================================================
    // 표시/숨김 (지상이고 && 페이드로 안 덮였을 때만)
    // ===================================================
    private void RefreshSurface()
    {
        _surfaceVisible = SurfaceSceneRegistry.IsActiveSceneSurface(extraSurfaceScenes)
                          && !DungeonOverlayController.IsInDungeon;
    }

    private void ApplyVisibility()
    {
        bool visible = _surfaceVisible && !_covered;
        
        // 대화 중일 때는 퀘스트, 날짜 HUD 등을 숨깁니다.
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState == UIState.Dialogue)
        {
            visible = false;
        }

        if (_visApplied && visible == _lastVisible) return;
        _visApplied = true;
        _lastVisible = visible;

        if (_hudRoot != null) _hudRoot.SetActive(visible);
        if (visible) RefreshContent();
    }

    /// <summary>화면이 페이드막으로 덮여 있는가(HUD 변화를 가려야 하는 순간).</summary>
    protected bool IsScreenCovered()
    {
        var f = ScreenFader.Instance;
        return f != null && f.IsCovering;
    }

    // ===================================================
    // 조립
    // ===================================================
    private void Build()
    {
        if (_built) return;

        CodeUI.EnsureEventSystem();

        RectTransform container = ResolveContainer();

        // 모서리에 앵커된 세로 컬럼 (내용에 맞춰 크기 자동)
        var column = CodeUI.CreateRect(container, "SurfaceHUD_" + GetType().Name);
        Vector2 a = new Vector2(AnchorRight ? 1f : 0f, AnchorBottom ? 0f : 1f);
        column.anchorMin = column.anchorMax = a;
        column.pivot = a;
        column.anchoredPosition = new Vector2(
            AnchorRight ? -cornerOffset.x : cornerOffset.x,
            AnchorBottom ? cornerOffset.y : -cornerOffset.y);

        var vlg = column.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = Mathf.Max(0f, blockSpacing);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = AnchorRight
            ? (AnchorBottom ? TextAnchor.LowerRight : TextAnchor.UpperRight)
            : (AnchorBottom ? TextAnchor.LowerLeft : TextAnchor.UpperLeft);

        var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _hudRoot = column.gameObject;

        BuildContent(column);
        _built = true;
    }

    /// <summary>컬럼(모서리 앵커) 안에 내용 블록을 만든다. 서브클래스가 구현.</summary>
    protected abstract void BuildContent(RectTransform column);

    /// <summary>데이터 → UI 갱신. 서브클래스가 구현. UI가 아직 없을 수 있으니 null 가드할 것.</summary>
    protected abstract void RefreshContent();

    /// <summary>보일 때 매 폴링마다 호출(전용 이벤트가 없어 폴링이 필요한 내용용). 기본 무동작.</summary>
    protected virtual void PollContent() { }

    // ===================================================
    // 컨테이너 / 캔버스
    // ===================================================
    protected RectTransform ResolveContainer()
    {
        if (!createOwnCanvas)
        {
            if (transform.parent is RectTransform parentRt) return parentRt;
            if (transform is RectTransform selfRt) return selfRt;

            Debug.LogWarning($"[{GetType().Name}] Create Own Canvas가 꺼져 있는데 상위가 " +
                             "RectTransform이 아닙니다(Canvas 하위에 배치 필요). 전용 캔버스로 폴백합니다.");
        }
        return (RectTransform)CreateOverlayCanvas();
    }

    private Transform CreateOverlayCanvas()
    {
        _canvasObj = new GameObject(GetType().Name + "_Canvas");
        _canvasObj.transform.SetParent(transform, false);

        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _canvasObj.AddComponent<GraphicRaycaster>();
        return _canvasObj.transform;
    }

    // ===================================================
    // 색 폴백 헬퍼 (서브클래스 공용)
    // ===================================================
    /// <summary>글씨색이 초기화 안 돼(투명 or 불투명 검정) 묻히는 사고 방지 — '거의 검정'이면 폴백.</summary>
    protected static Color Visible(Color c, Color fallback)
    {
        bool nearlyTransparent = c.a < 0.05f;
        bool nearlyBlack = Mathf.Max(c.r, c.g, c.b) < 0.06f;
        return (nearlyTransparent || nearlyBlack) ? fallback : c;
    }

    /// <summary>배경 패널용: 어두운 색은 '의도'이므로 near-black은 폴백 안 하고, 완전 투명(미지정)만 폴백.</summary>
    protected static Color VisibleBg(Color c, Color fallback) => c.a < 0.05f ? fallback : c;
}
