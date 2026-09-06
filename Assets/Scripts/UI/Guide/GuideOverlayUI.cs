// @tags: guide, tutorial, ui, overlay, code-generated, video, media, page, blur, freeze

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 가이드 오버레이 — 새로운 것을 접했을 때 게임을 멈추고 띄우는 안내 모달(세피리아 가이드 참고).
/// 상단 배너 제목 → 미디어(이미지/동영상/프레임) → 본문 → 페이지 네비(◀ n/N ▶) + 닫기(X).
///
/// 다른 코드 생성 오버레이(Settings/Pause)와 동일한 규칙:
///  - 전부 코드로 생성(씬/프리팹 세팅 불필요), <see cref="Show"/> 한 번으로 동작
///  - 열릴 때 화면 캡처 블러 + 어둡게 깔고 timeScale=0 으로 게임 정지
///  - <see cref="IsOpen"/>/<see cref="ClosedThisFrame"/> 로 UIStateManager 전역 단축키와 조율
///
/// 표시 데이터는 <see cref="GuideManager"/>가 <see cref="GuideSO"/> 형태로 넘긴다.
/// </summary>
public class GuideOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static GuideOverlayUI _instance;

    public static GuideOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<GuideOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("GuideOverlayUI");
                    _instance = go.AddComponent<GuideOverlayUI>();
                }
            }
            return _instance;
        }
    }

    /// <summary>가이드 오버레이가 열려 있는지 (UIStateManager 전역 단축키 차단용).</summary>
    public static bool IsOpen => _instance != null && _instance._isOpen;

    /// <summary>닫힌 바로 그 프레임인지 — 같은 프레임의 ESC 중복 처리 방지.</summary>
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>씬이 바뀌면 무조건 닫는다(안전망) — 열린 채 씬이 바뀌면 화면을 덮고 timeScale이 0으로 굳는다.</summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_isOpen && !_opening) return;
        _opening = false;
        Time.timeScale = 1f;
        HideImmediate();
    }

    // ===================================================
    // 인스펙터 (씬에 미리 배치했을 때만 노출 — 선택 사항)
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Tooltip("상단 배너(제목 리본) 전용 이미지. 비우면 skin.cardSprite + 강조색으로 그린다")]
    [SerializeField] private Sprite bannerSprite;
    [Tooltip("배너 이미지 색조. 이미 색이 입혀진 도트 리본이면 흰색 그대로 두면 된다")]
    [SerializeField] private Color bannerTint = Color.white;

    [Tooltip("패널 안쪽에 깔리는 장식 배경 이미지(양피지·책 등). 비우면 패널 색만. 도감과 같은 규칙")]
    [SerializeField] private Sprite backgroundSprite;
    [Tooltip("배경 이미지 색조(투명도 포함). 너무 밝으면 본문이 안 보이니 어둡게/반투명 권장")]
    [SerializeField] private Color backgroundTint = new Color(1f, 1f, 1f, 0.35f);
    [Tooltip("배경 이미지를 원본 비율로 채울지(가운데 크롭). 끄면 패널에 늘려 채운다")]
    [SerializeField] private bool backgroundPreserveAspect = false;

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.6f;
    [SerializeField] private bool useBlurBackdrop = true;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 900f;
    [SerializeField] private float panelHeight = 800f;
    [Tooltip("캔버스 정렬 순서 — 일시정지(30800)보다 위, DaySummary(32000)보다 아래")]
    [SerializeField] private int sortingOrder = 31500;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;

    // ===================================================
    // 내부 상태
    // ===================================================
    private const float FadeDuration = 0.14f;
    private const float Pad = 28f;

    private bool _built, _isOpen, _opening;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    // 위젯 참조
    private TextMeshProUGUI _bannerText, _leadText, _bodyText, _counterText, _placeholderText;
    private RectTransform _mediaHolder;
    private AspectRatioFitter _aspectFitter;
    private Image _mediaImage;
    private RawImage _mediaRaw;
    private Button _prevButton, _nextButton;

    // 미디어 재생 상태
    private VideoPlayer _video;
    private bool _videoActive, _framesActive;
    private Sprite[] _frames;
    private float _frameFps = 8f, _frameTimer;
    private int _frameIndex;

    // 현재 가이드
    private GuideSO _guide;
    private int _pageIndex;

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    /// <summary>가이드를 화면에 띄운다(GuideManager가 호출).</summary>
    public static void Show(GuideSO guide)
    {
        if (guide == null || !guide.IsValid)
        {
            Debug.LogWarning("[GuideOverlayUI] 유효하지 않은 가이드입니다.");
            return;
        }
        if (IsOpen) return; // 이미 표시 중이면 무시
        Instance._guide = guide;
        Instance.StartCoroutine(Instance.OpenRoutine());
    }

    /// <summary>
    /// 가이드를 띄우기 직전, 여는 쪽(도감·일시정지 메뉴 등)의 스프라이트 스킨을 넘겨 같은 테마로 그리게 한다.
    /// 가이드 오버레이는 씬에 배치하지 않으면 런타임에 빈 스킨으로 자동 생성되므로, 그때는 이 경로로만 스프라이트가 들어온다.
    /// 씬에 배치해 인스펙터로 지정한 경우엔 그쪽이 이미 채워져 있으니 굳이 부를 필요가 없다.
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
        if (_built && !_isOpen && !_opening) RebuildUI();   // 열려 있는 동안엔 갈아끼우지 않는다
    }

    /// <summary>조립된 UI를 허물고 재조립 대기 상태로 되돌린다. 다음 <see cref="OpenRoutine"/>의 EnsureBuilt가 새 스킨으로 다시 짓는다.</summary>
    private void RebuildUI()
    {
        StopMedia();
        _blur.Release(_blurImage);
        if (_canvasObj != null) Destroy(_canvasObj);
        _canvasObj = null;
        _canvasGroup = null;
        _blurImage = null;
        _bannerText = _leadText = _bodyText = _counterText = _placeholderText = null;
        _mediaHolder = null;
        _aspectFitter = null;
        _mediaImage = null;
        _mediaRaw = null;
        _prevButton = _nextButton = null;
        _video = null;
        _built = false;
    }

    private IEnumerator OpenRoutine()
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

        _prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        _isOpen = true;

        _loc.Refresh();
        _pageIndex = 0;
        ShowPage(0);

        _canvasObj.SetActive(true);

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

    /// <summary>닫기(스킵 포함). 시간을 원래대로 되돌린다.</summary>
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        Time.timeScale = _prevTimeScale;
        CodeUI.PlaySfx(clickSfxName);
        
        if (_guide != null && _guide.nextDialogue != null)
        {
            StartCoroutine(ShowNextDialogueRoutine(_guide.nextDialogue, _guide.nextDialogueDelay));
        }
        
        HideImmediate();
    }

    private IEnumerator ShowNextDialogueRoutine(DialogueData dialogue, float delay)
    {
        yield return new WaitForSeconds(delay);
        var dialogueUI = Object.FindFirstObjectByType<QuestDialogueUI>(FindObjectsInactive.Include);
        if (dialogueUI != null)
        {
            dialogueUI.ShowDialogue(dialogue, null);
        }
    }

    public static void CloseStatic()
    {
        if (_instance != null) _instance.Close();
    }

    private void HideImmediate()
    {
        StopMedia();
        _guide = null;
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);
    }

    private void Update()
    {
        if (!_isOpen) return;

        // ── 키보드 조작 ──
        // Space는 Submit로도 잡힌다 — 마우스로 누른 '다음' 버튼이 선택된 채 남아 있으면 한 번에 두 장 넘어간다.
        CodeUI.ClearSelection();

        if (Input.GetKeyDown(KeyCode.Escape)) { CodeUI.PlayBack(); Close(); return; }
        // A/D는 방향키와 같은 페이지 넘김(플레이어 이동키와 같은 손 위치 — 도감 재생에서도 동일).
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) { Prev(); }
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) ||
                 Input.GetKeyDown(KeyCode.Space) ||
                 Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Next(); }

        // ── 동영상 텍스처 연결 (준비되는 대로) ──
        if (_videoActive && _video != null && _mediaRaw != null &&
            _mediaRaw.texture == null && _video.texture != null)
        {
            _mediaRaw.texture = _video.texture;
        }

        // ── 프레임 애니메이션 (timeScale=0 이므로 unscaled) ──
        if (_framesActive && _frames != null && _frames.Length > 0)
        {
            _frameTimer += Time.unscaledDeltaTime;
            float spf = 1f / Mathf.Max(1f, _frameFps);
            while (_frameTimer >= spf)
            {
                _frameTimer -= spf;
                _frameIndex = (_frameIndex + 1) % _frames.Length;
                if (_mediaImage != null) _mediaImage.sprite = _frames[_frameIndex];
            }
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 페이지 네비게이션
    // ===================================================
    private void Prev()
    {
        if (_guide == null || _pageIndex <= 0) return;
        CodeUI.PlaySfx(clickSfxName);
        ShowPage(_pageIndex - 1);
    }

    /// <summary>
    /// 다음 페이지로. 마지막 페이지에서는 아무 것도 하지 않는다 —
    /// 페이지를 넘기던 손가락이 그대로 창을 닫아버리면 마지막 장을 읽지 못한다. 닫기는 ESC(·X·건너뛰기)로만.
    /// </summary>
    private void Next()
    {
        if (_guide == null) return;
        if (_pageIndex >= _guide.pages.Count - 1) return;
        CodeUI.PlaySfx(clickSfxName);
        ShowPage(_pageIndex + 1);
    }

    private void ShowPage(int index)
    {
        if (_guide == null || _guide.pages == null || _guide.pages.Count == 0) return;

        _pageIndex = Mathf.Clamp(index, 0, _guide.pages.Count - 1);
        var page = _guide.pages[_pageIndex];

        // 배너: 가이드 공통 제목이 있으면 그것, 없으면 페이지 제목
        bool hasBanner = !string.IsNullOrEmpty(_guide.bannerTitleKey) || !string.IsNullOrEmpty(_guide.bannerTitleFallback);
        string banner = hasBanner
            ? CodeUI.L(_guide.bannerTitleKey, _guide.bannerTitleFallback)
            : CodeUI.L(page.titleKey, page.titleFallback);
        _bannerText.text = banner;

        // 페이지 제목(굵은 리드) — 배너와 겹치면 숨김
        string lead = CodeUI.L(page.titleKey, page.titleFallback);
        bool showLead = !string.IsNullOrEmpty(lead) && lead != banner;
        _leadText.gameObject.SetActive(showLead);
        if (showLead) _leadText.text = lead;

        _bodyText.text = CodeUI.L(page.bodyKey, page.bodyFallback);

        ConfigureMedia(page);

        // 카운터 + 화살표 상태
        int total = _guide.pages.Count;
        _counterText.text = $"{_pageIndex + 1} / {total}";

        bool first = _pageIndex == 0;
        bool last = _pageIndex == total - 1;
        _prevButton.gameObject.SetActive(total > 1);
        _prevButton.interactable = !first;

        // 다음 버튼도 이전 버튼과 같은 규칙 — 끝에서는 비활성만 되고 모양·크기는 그대로다.
        _nextButton.gameObject.SetActive(total > 1);
        _nextButton.interactable = !last;
    }

    // ===================================================
    // 미디어
    // ===================================================
    private void ConfigureMedia(GuidePage page)
    {
        StopMedia();

        switch (page.mediaType)
        {
            case GuideMediaType.Image when page.image != null:
                _mediaImage.sprite = page.image;
                _mediaImage.enabled = true;
                _mediaRaw.enabled = false;
                SetAspect(page.image.rect.width, page.image.rect.height);
                SetPlaceholder(false);
                break;

            case GuideMediaType.Frames when page.frames != null && page.frames.Length > 0:
                _frames = page.frames;
                _frameFps = page.framesPerSecond;
                _frameIndex = 0;
                _frameTimer = 0f;
                _framesActive = true;
                _mediaImage.sprite = _frames[0];
                _mediaImage.enabled = true;
                _mediaRaw.enabled = false;
                SetAspect(_frames[0].rect.width, _frames[0].rect.height);
                SetPlaceholder(false);
                break;

            case GuideMediaType.Video when page.video != null:
                EnsureVideoPlayer();
                _video.clip = page.video;
                _mediaRaw.texture = null;
                _mediaRaw.enabled = true;
                _mediaImage.enabled = false;
                _videoActive = true;
                SetAspect(page.video.width, page.video.height);
                SetPlaceholder(false);
                _video.Play();
                break;

            default:
                SetPlaceholder(true);
                break;
        }
    }

    private void StopMedia()
    {
        _framesActive = false;
        _frames = null;
        _videoActive = false;
        if (_video != null)
        {
            if (_video.isPlaying) _video.Stop();
            _video.clip = null;
        }
        if (_mediaRaw != null) { _mediaRaw.texture = null; _mediaRaw.enabled = false; }
        if (_mediaImage != null) _mediaImage.enabled = false;
    }

    private void SetAspect(float w, float h)
    {
        if (_aspectFitter == null) return;
        if (w > 0.5f && h > 0.5f) _aspectFitter.aspectRatio = w / h;
        _aspectFitter.enabled = true;
    }

    private void SetPlaceholder(bool on)
    {
        if (_placeholderText != null) _placeholderText.gameObject.SetActive(on);
        if (on)
        {
            if (_mediaImage != null) _mediaImage.enabled = false;
            if (_mediaRaw != null) _mediaRaw.enabled = false;
        }
    }

    private void EnsureVideoPlayer()
    {
        if (_video != null) return;
        var vgo = new GameObject("GuideVideoPlayer");
        vgo.transform.SetParent(transform, false);
        _video = vgo.AddComponent<VideoPlayer>();
        _video.playOnAwake = false;
        _video.isLooping = true;
        _video.renderMode = VideoRenderMode.APIOnly; // videoPlayer.texture 로 프레임을 직접 읽는다
        _video.audioOutputMode = VideoAudioOutputMode.None; // 가이드는 무음(필요 시 변경)
        _video.waitForFirstFrame = true;
        _video.skipOnDrop = true;
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("GuideOverlayCanvas");
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

        // 블러 배경
        var blurObj = new GameObject("BlurBackdrop");
        blurObj.transform.SetParent(_canvasObj.transform, false);
        _blurImage = blurObj.AddComponent<RawImage>();
        CodeUI.StretchFull(_blurImage.rectTransform);
        _blurImage.raycastTarget = true;

        // 어둡게
        var dim = CodeUI.CreateImage(_canvasObj.transform, "Dim", new Color(0f, 0f, 0f, dimAlpha), rounded: false);
        CodeUI.StretchFull(dim.rectTransform);
        dim.raycastTarget = true;

        // ── 패널 ──
        var panel = CodeUI.CreateImage(_canvasObj.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);
        panelRt.anchoredPosition = Vector2.zero;
        var panelT = panel.transform;

        // ── 인스펙터 장식 배경 (도감과 같은 규칙 — 패널 안쪽, 다른 요소보다 뒤) ──
        if (backgroundSprite != null)
        {
            var bg = CodeUI.CreateImage(panelT, "DecorBackground", backgroundTint, rounded: false);
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

        // ── 배너 제목 (상단 강조 카드) ──
        // 전용 배너 이미지가 있으면 그것(색조는 bannerTint), 없으면 공용 카드 스프라이트 + 강조색.
        const float bannerH = 74f;
        var banner = bannerSprite != null
            ? CodeUI.CreateImage(panelT, "Banner", bannerTint, bannerSprite, skin)
            : CodeUI.CreateImage(panelT, "Banner", CodeUI.AccentFill, skin.cardSprite, skin);
        TopBar(banner.rectTransform, Pad + 8f, Pad + 8f, Pad, bannerH);
        _bannerText = CodeUI.CreateText(banner.transform, "Title", 34f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(_bannerText.rectTransform);
        _bannerText.characterSpacing = 6f;

        // ── 미디어 박스 ──
        float mediaTop = Pad + bannerH + 18f;
        const float mediaH = 384f;
        var mediaBox = CodeUI.CreateImage(panelT, "MediaBox", CodeUI.BoxBg, skin.boxSprite, skin);
        TopBar(mediaBox.rectTransform, Pad, Pad, mediaTop, mediaH);

        // 종횡비 유지 홀더 (부모 안에 letterbox)
        var holderObj = CodeUI.CreateRect(mediaBox.transform, "MediaHolder");
        _mediaHolder = holderObj;
        _mediaHolder.anchorMin = new Vector2(0.5f, 0.5f);
        _mediaHolder.anchorMax = new Vector2(0.5f, 0.5f);
        _mediaHolder.pivot = new Vector2(0.5f, 0.5f);
        _mediaHolder.sizeDelta = new Vector2(mediaH * 1.6f, mediaH - 20f);
        _aspectFitter = holderObj.gameObject.AddComponent<AspectRatioFitter>();
        _aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _aspectFitter.aspectRatio = 16f / 9f;

        _mediaImage = CodeUI.CreateImage(holderObj, "MediaImage", Color.white, rounded: false);
        _mediaImage.type = Image.Type.Simple;
        _mediaImage.preserveAspect = false; // 홀더가 이미 비율을 맞춘다
        _mediaImage.raycastTarget = false;
        CodeUI.StretchFull(_mediaImage.rectTransform);
        _mediaImage.enabled = false;

        var rawObj = CodeUI.CreateRect(holderObj, "MediaVideo");
        _mediaRaw = rawObj.gameObject.AddComponent<RawImage>();
        _mediaRaw.raycastTarget = false;
        CodeUI.StretchFull(_mediaRaw.rectTransform);
        _mediaRaw.enabled = false;

        // 미디어 없음 플레이스홀더
        _placeholderText = CodeUI.CreateText(mediaBox.transform, "Placeholder", 22f, FontStyles.Italic,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(_placeholderText.rectTransform);
        _loc.Bind(_placeholderText, "ui_guide_no_preview", "미리보기 없음");
        _placeholderText.gameObject.SetActive(false);

        // ── 페이지 제목(리드) ──
        float leadTop = mediaTop + mediaH + 16f;
        _leadText = CodeUI.CreateText(panelT, "Lead", 25f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, _loc);
        TopBar(_leadText.rectTransform, Pad, Pad, leadTop, 34f);

        // ── 본문 ──
        var bodyObj = CodeUI.CreateText(panelT, "Body", 21f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.Top, _loc);
        _bodyText = bodyObj;
        _bodyText.textWrappingMode = TextWrappingModes.Normal;
        TopBar(_bodyText.rectTransform, Pad + 12f, Pad + 12f, leadTop + 42f, 120f);

        // ── 하단 네비게이션 바 ──
        BuildNavBar(panelT);

        _canvasObj.SetActive(false);
    }

    private void BuildNavBar(Transform panelT)
    {
        const float navH = 56f;
        var nav = CodeUI.CreateRect(panelT, "NavBar");
        nav.anchorMin = new Vector2(0f, 0f);
        nav.anchorMax = new Vector2(1f, 0f);
        nav.pivot = new Vector2(0.5f, 0f);
        nav.offsetMin = new Vector2(Pad, Pad);
        nav.offsetMax = new Vector2(-Pad, Pad + navH);

        // 닫기 (왼쪽) — 유일한 닫기 버튼. 우상단 X는 없앴다(같은 일을 하는 버튼이 둘일 이유가 없다).
        var skipBtn = CodeUI.CreateButton(nav, "CloseText", Color.clear, () => Close(), null, null, rounded: false);
        var skipRt = skipBtn.image.rectTransform;
        skipRt.anchorMin = new Vector2(0f, 0.5f);
        skipRt.anchorMax = new Vector2(0f, 0.5f);
        skipRt.pivot = new Vector2(0f, 0.5f);
        skipRt.anchoredPosition = new Vector2(4f, 0f);
        skipRt.sizeDelta = new Vector2(150f, navH);
        var skipText = CodeUI.CreateText(skipBtn.transform, "Text", 19f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Left, _loc);
        CodeUI.StretchFull(skipText.rectTransform);
        _loc.Bind(skipText, "ui_guide_close", "닫기 ESC");

        // 중앙: ◀  n / N  ▶
        var center = CodeUI.CreateRect(nav, "Center");
        center.anchorMin = center.anchorMax = new Vector2(0.5f, 0.5f);
        center.pivot = new Vector2(0.5f, 0.5f);
        center.sizeDelta = new Vector2(300f, navH);

        _prevButton = CodeUI.CreateButton(center, "Prev", CodeUI.TabIdleBg, () => Prev(), skin.buttonSprite, skin);
        var prevRt = _prevButton.image.rectTransform;
        prevRt.anchorMin = prevRt.anchorMax = new Vector2(0f, 0.5f);
        prevRt.pivot = new Vector2(0f, 0.5f);
        prevRt.anchoredPosition = new Vector2(0f, 0f);
        prevRt.sizeDelta = new Vector2(56f, 48f);
        AddTriangle(_prevButton.transform, -1);
        AddKeyCap(_prevButton.transform, "A");

        _counterText = CodeUI.CreateText(center, "Counter", 22f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.Center, _loc);
        var cRt = _counterText.rectTransform;
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.5f);
        cRt.pivot = new Vector2(0.5f, 0.5f);
        cRt.sizeDelta = new Vector2(140f, navH);

        _nextButton = CodeUI.CreateButton(center, "Next", CodeUI.TabIdleBg, () => Next(), skin.buttonSprite, skin);
        var nextRt = _nextButton.image.rectTransform;
        nextRt.anchorMin = nextRt.anchorMax = new Vector2(1f, 0.5f);
        nextRt.pivot = new Vector2(1f, 0.5f);
        nextRt.anchoredPosition = new Vector2(0f, 0f);
        nextRt.sizeDelta = new Vector2(56f, 48f);   // ◀ 버튼과 동일 — 페이지마다 크기가 바뀌면 안 된다
        AddTriangle(_nextButton.transform, 1);

        AddKeyCap(_nextButton.transform, "D");

    }

    /// <summary>
    /// 화살표 버튼 바로 위에 올리는 키캡 글자(A / D). 조작 안내는 이 두 글자가 전부다 —
    /// 하단에 문장으로 늘어놓으면 패널이 글자로 가득 차 정작 본문이 안 읽힌다.
    /// </summary>
    private void AddKeyCap(Transform button, string key)
    {
        var cap = CodeUI.CreateText(button, "KeyCap", 15f, FontStyles.Bold, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        cap.text = key;
        cap.raycastTarget = false;
        var rt = cap.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 4f);
        rt.sizeDelta = new Vector2(40f, 20f);
    }

    /// <summary>◀▶ 글리프 대신 삼각형 스프라이트를 버튼 위에 얹는다(폰트 의존 제거).</summary>
    private void AddTriangle(Transform parent, int dir)
    {
        var tri = new GameObject("Arrow").AddComponent<Image>();
        tri.transform.SetParent(parent, false);
        tri.sprite = CodeUI.Triangle(dir);
        tri.color = CodeUI.LabelColor;
        tri.raycastTarget = false;
        var trt = tri.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(15f, 17f);
    }

    /// <summary>상단 기준 가로 스트레치 배치: 왼/오 인셋 + 위에서 topY 만큼 내려 height 높이.</summary>
    private static void TopBar(RectTransform rt, float insetL, float insetR, float topY, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(insetL, -(topY + height));
        rt.offsetMax = new Vector2(-insetR, -topY);
    }
}
