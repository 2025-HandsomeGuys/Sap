// @tags: minimap, ui, underground, map, radar, sonar, player, hud, depth
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 원형 언더그라운드 미니맵 — "탐사 장비 계기판" 컨셉.
/// 텍셀마다 실제 청크 픽셀 데이터를 직접 샘플링해 지형의 축소판을 그린다
/// (파인 곳=앰버, 남은 흙=암석 톤). DigPathTracker의 탐사 기록은 안개(fog) 마스크로만 쓰이며
/// 안개 경계는 바이리니어 보간으로 부드럽게 처리된다.
/// 베젤 프레임·소나 핑·레이더 스윕·플레이어 펄스·깊이 배지 연출을 붙인다.
///
/// UI는 전부 코드로 생성 (DaySummaryUI 패턴) — 씬에는 이 컴포넌트만 있으면 된다.
/// 프리팹에 남아있는 구버전 테스트용 RawImage / 흰 배경 Image는 자동으로 비활성화한다.
///
/// [Inspector 연결]
///  - rawImage  : (구버전 잔재) 있으면 자동 숨김. 새로 세팅할 땐 비워둬도 됨
///  - playerTr  : 플레이어 Transform. 비워두면 PlayerController를 자동 탐색
/// </summary>
public class UndergroundMinimap : MonoBehaviour
{
    [Header("References")]
    [Tooltip("구버전 테스트용 RawImage — 연결돼 있으면 자동으로 비활성화된다.")]
    [SerializeField] private RawImage rawImage;
    [Tooltip("비워두면 PlayerController를 자동 탐색한다.")]
    [SerializeField] private Transform playerTr;

    [Header("표시 설정")]
    [Tooltip("미니맵 원 지름 (UI 픽셀)")]
    [SerializeField] private float diameter = 280f;

    [Tooltip("플레이어 중심 기준 표시할 셀 반경 (작을수록 화면이 확대됨). " +
             "4 미만으로 줄이면 지형이 MapTerrainCache 격자(청크당 96)보다 확대돼 계단이 보인다 — " +
             "그땐 MapTerrainCache.RES를 함께 올릴 것")]
    [SerializeField] private int viewRadius = 10;

    [Tooltip("연결된 빈 공간 탐사 주기 (초)")]
    [SerializeField] private float updateInterval = 0.1f;

    [Tooltip("플레이어와 연결된 빈 공간만 밝히는 탐사 반경 (셀 단위). 벽 너머는 밝혀지지 않는다")]
    [SerializeField] private int exploreRadius = 2;

    [Header("연출")]
    [Tooltip("느리게 도는 레이더 스윕 광")]
    [SerializeField] private bool enableSweep = true;

    [Tooltip("주기적으로 퍼지는 소나 핑 링")]
    [SerializeField] private bool enablePing = true;

    [Tooltip("지표면 라인 + 지상 영역 틴트 표시")]
    [SerializeField] private bool showSurfaceLine = true;

    [Tooltip("지표면의 월드 Y 좌표 (깊이 계산 기준). 지하 지형이 기준보다 +10 올라와 있어 실제 지표는 y=10 → 여기서 0m")]
    [SerializeField] private float surfaceWorldY = 10f;

    [Header("시야 원형 페이드 (검은 안개)")]
    [Tooltip("플레이어 중심 원형 시야 페이드 — 멀수록 부드럽게 어두워진다(직각 경계 대신 구형태)")]
    [SerializeField] private bool enableVisionFade = true;
    [Range(0f, 1f)][Tooltip("이 반경(맵 반지름 대비)까지는 완전히 선명")]
    [SerializeField] private float visionFullRadius = 0.45f;
    [Range(0f, 1f)][Tooltip("이 반경에서 시야가 바닥값에 도달 (이 밖은 원형으로 완전히 어두워짐)")]
    [SerializeField] private float visionEdgeRadius = 0.94f;
    [Range(0f, 1f)][Tooltip("가장자리 최소 가시도 (0 = 완전 어둠 → 경계가 깔끔한 원형)")]
    [SerializeField] private float visionFloor = 0f;

    [Tooltip("지형 소프트 글로우 블러 반경(텍셀, 0=끔/선명). 기본 0(블러 없음)")]
    [Range(0, 4)][SerializeField] private int contentBlur = 0;

    [Header("지도 마커")]
    [Tooltip("마커 기준 크기 (UI 픽셀). 엘베·입구·플레이어·탐지 마커가 모두 이 값에 비례한다")]
    [SerializeField] private float markerSize = 15f;

    // 각 마커의 markerSize 대비 비율 (기존 기본값 15 기준: 플레이어 14, 글로우 34, 탐지 10, 가장자리 12).
    private const float PlayerMarkerFactor = 14f / 15f;
    private const float PlayerGlowFactor   = 34f / 15f;
    private const float DetectMarkerFactor = 10f / 15f;
    private const float EdgeMarkerFactor   = 12f / 15f;

    [Header("스프라이트 (비우면 코드 생성 아이콘 사용)")]
    [Tooltip("플레이어 위치 마커 스프라이트")]
    [SerializeField] private Sprite playerMarkerSprite;
    [Tooltip("엘리베이터 마커 스프라이트")]
    [SerializeField] private Sprite elevatorMarkerSprite;
    [Tooltip("청크 입구 마커 스프라이트")]
    [SerializeField] private Sprite entranceMarkerSprite;
    [Tooltip("이미 탐험해 재입장 불가한 청크 입구 마커 스프라이트 (비우면 코드 생성 회색 아이콘)")]
    [SerializeField] private Sprite entranceUsedMarkerSprite;

    // 전체지도(WorldMapOverlay)가 미니맵 스프라이트를 공유하기 위한 읽기 전용 접근자.
    public Sprite PlayerMarkerSprite => playerMarkerSprite;
    public Sprite ElevatorMarkerSprite => elevatorMarkerSprite;
    public Sprite EntranceMarkerSprite => entranceMarkerSprite;
    public Sprite EntranceUsedMarkerSprite => entranceUsedMarkerSprite;

    [Tooltip("베젤 프레임(원형 테두리) 스프라이트. 비우면 코드 생성 베젤 사용")]
    [SerializeField] private Sprite frameSprite;
    [Tooltip("프레임 스프라이트 크기 배율 (맵 지름 대비). 코드 베젤은 약 1.20)")]
    [SerializeField] private float frameSpriteScale = 1.2f;
    [Tooltip("코드 생성 원형 베젤의 픽셀 해상도 — 낮을수록 도트가 굵어진다(원형은 유지, 자세히 보면 픽셀). " +
             "frameSprite를 비워둬야 이 원형 베젤이 쓰인다")]
    [Range(24, 256)][SerializeField] private int framePixelResolution = 160;

    [Header("배경 패널 (픽셀 하우징)")]
    [Tooltip("미니맵 뒤에 깔리는 9-슬라이스 패널 스프라이트 (예: Stock_UISheet_8). 비우면 패널 없음")]
    [SerializeField] private Sprite panelBackgroundSprite;
    [Tooltip("패널의 Sprite Pixels Per Unit 흉내값 — 낮을수록 픽셀(9-슬라이스 모서리)이 크고 뭉툭해진다. 스프라이트 원본 PPU는 안 건드리고 Image에서만 적용")]
    [SerializeField] private float panelPixelsPerUnit = 0.4f;
    [Tooltip("패널 크기 배율 (맵 지름 대비)")]
    [SerializeField] private float panelScale = 1.18f;
    [Tooltip("패널 색조 (스프라이트 원색을 유지하려면 흰색)")]
    [SerializeField] private Color panelColor = Color.white;

    [Header("깊이 배지 (하단 캡슐)")]
    [Tooltip("깊이 배지 배경 9-슬라이스 스프라이트 (예: Stock_UISheet_8). 비우면 코드 생성 캡슐")]
    [SerializeField] private Sprite depthPanelSprite;
    [Tooltip("깊이 배지의 Sprite Pixels Per Unit 흉내값 — 낮을수록 모서리 픽셀이 굵어진다(작은 배지라 너무 낮추면 테두리가 뒤덮음)")]
    [SerializeField] private float depthPanelPixelsPerUnit = 50f;
    [Tooltip("깊이 배지 색조 (기존 어두운 톤 유지)")]
    [SerializeField] private Color depthPanelColor = new Color(0.16f, 0.18f, 0.23f, 1f);

    [Header("신호 없음 (던전 등 맵을 볼 수 없는 곳)")]
    [Tooltip("표시할 문구")]
    [SerializeField] private string noSignalText = "NO SIGNAL";
    [Tooltip("신호 없음 색상 (붉은 정적 노이즈 + 문구)")]
    [SerializeField] private Color noSignalColor = new Color(1f, 0.23f, 0.2f, 1f);

    // ── 렌더 상수 ─────────────────────────────────────
    private const int TEX = 256;             // 맵 텍스처 해상도
    private const float MinRenderInterval = 1f / 24f; // 이동 중 재렌더 상한
    private const float ForceRenderInterval = 0.75f;  // 정지 중에도 주기 재렌더 (청크 로드 반영)
    private const float PingInterval = 5f;   // 소나 핑 주기
    private const float PingDuration = 2.2f; // 핑 확산 시간
    private const float SweepDegPerSec = 30f;

    // ── 색상 팔레트 (어두운 동굴 + 앰버 랜턴) ─────────
    private static readonly Color32 BgCenter   = new Color32(17, 20, 27, 255);
    private static readonly Color32 BgEdge     = new Color32(7, 9, 13, 255);
    private const byte BgAlpha = 232;
    private static readonly Color32 TunnelCol  = new Color32(214, 178, 120, 255); // 파인 빈 공간
    private static readonly Color32 RockCol    = new Color32(84, 71, 58, 255);    // 탐사됐지만 안 판 흙
    private static readonly Color32 SkyCol     = new Color32(86, 108, 142, 255);
    private static readonly Color32 SurfaceCol = new Color32(150, 214, 170, 255);
    private static readonly Color   AccentAmber = new Color(1f, 0.76f, 0.44f);
    private static readonly Color   DepthTextCol = new Color(1f, 0.84f, 0.59f);

    // ── 런타임 상태 ───────────────────────────────────
    private RectTransform _root;
    private CanvasGroup _rootGroup;
    private RawImage _mapSurface;
    private RectTransform _sweepRect;
    private Image _sweepImg;
    private RectTransform _pingRect;
    private Image _pingImg;
    private RectTransform _playerRect;
    private Image _playerGlowImg;
    private TextMeshProUGUI _depthText;

    // 신호 없음(던전) 상태
    private TextMeshProUGUI _noSignalLabel;
    private bool _noSignalActive;

    private Texture2D _mapTex;
    private Color32[] _pixels;
    private Color32[] _basePixels;  // 원형 마스크+비네트가 베이크된 배경
    private byte[] _fade;           // 픽셀별 콘텐츠 감쇠 (0=원 밖, 가장자리로 갈수록 감소)
    private float[] _vision;        // 플레이어 중심 원형 시야 (1=선명, 가장자리로 갈수록 0)
    private bool[] _exploredGrid;   // 렌더 1회용 탐사 셀 스냅샷
    // _exploredGrid의 좌표계 (RenderMap에서 세팅) — PaintRockEntry가 탐사도를 되짚어 볼 때 사용
    private float _expCellSize = 3f;
    private int _expOriginCellX, _expOriginCellY, _expGrid;
    private Color32[] _blurTmp;     // 소프트 글로우 블러 임시 버퍼 (재사용)

    // 지도 마커 아이콘 (엘베·청크입구) — 코드 생성 UI Image 풀
    private RectTransform _markerLayer;
    private readonly System.Collections.Generic.List<Image> _markerPool
        = new System.Collections.Generic.List<Image>();

    // 지형 샘플 캐시 — 렌더 중 같은 청크 연속 샘플 시 조회 생략.
    private TerrainChunk _sampChunk;
    private int _sampCx, _sampCy;
    private Vector2 _sampChunkPos;
    private float _sampPPU;
    private bool _sampValid;

    // 청크 크기의 역수. RenderMap 진입 시 한 번 계산해 텍셀 루프의 나눗셈을 곱셈으로 바꾼다.
    private float _invChunkW = 1f, _invChunkH = 1f;

    // 순번 재베이크 커서 — 렌더당 보이는 청크 하나씩 전 영역을 다시 굽는다(자가 치유용).
    private int _rebakeCursor;

    private readonly System.Collections.Generic.List<UnityEngine.Object> _generated
        = new System.Collections.Generic.List<UnityEngine.Object>();

    // 탐지파동 유물 — 발견된 특수청크의 오프센터 마커 (Task 7)
    private readonly System.Collections.Generic.List<RectTransform> _detectMarkers
        = new System.Collections.Generic.List<RectTransform>();
    private Sprite _detectSprite;

    // 탐지파동 발견 알림 — 좌표별 만료 시각. 링이 훑은 순간부터 3초간 반짝인다.
    private const float AnnounceDuration = 3f;
    private readonly System.Collections.Generic.Dictionary<Vector2Int, float> _announceUntil
        = new System.Collections.Generic.Dictionary<Vector2Int, float>();
    // 만료 키 수거용 재사용 버퍼. foreach 도중 Remove하면 InvalidOperationException이 나므로
    // 순회 후 일괄 제거한다. 매 프레임 도는 경로라 GC를 피해 필드로 캐시한다.
    private readonly System.Collections.Generic.List<Vector2Int> _announceExpired
        = new System.Collections.Generic.List<Vector2Int>();

    // 시야 밖 발견 알림용 임시 마커 풀(영구 마커와 분리).
    private readonly System.Collections.Generic.List<RectTransform> _edgeMarkers
        = new System.Collections.Generic.List<RectTransform>();

    // 지도 해금·강화 (업그레이드 배선) — MapUnlockGate 참고.
    // 해금 전에는 미니맵이 존재하지 않고 탐사 기록도 쌓이지 않는다(산 뒤부터 칠해진다).
    private const float UnlockPollInterval = 1f;  // 게이트 조회가 FindFirstObjectByType를 도므로 매 프레임 금지
    private bool _mapUnlocked;
    private int _effExploreRadius;                // 업그레이드가 반영된 탐사 반경 (셀)
    private float _unlockPollTimer;

    private float _exploreTimer;
    private float _renderTimer;
    private float _playerSearchTimer;
    private float _pingTimer;
    private Vector2 _lastRenderPos;
    private bool _renderedOnce;
    private int _lastDepthShown = int.MinValue;

    // ===================================================
    // 초기화
    // ===================================================
    // DetectedChunkStore는 앱 수명 싱글톤이다. 해제하지 않으면 씬 전환 후
    // 파괴된 미니맵이 이벤트에 매달려 MissingReferenceException을 던진다.
    void OnEnable()  => DetectedChunkStore.Instance.OnPinged += OnDetectPinged;
    void OnDisable() => DetectedChunkStore.Instance.OnPinged -= OnDetectPinged;

    private void OnDetectPinged(Vector2Int coord)
    {
        _announceUntil[coord] = Time.time + AnnounceDuration;
    }

    void Awake()
    {
        HideLegacyUI();
        BakeBase();
        BuildUI();

        _effExploreRadius = exploreRadius;
        RefreshUpgradeState(initial: true);
    }

    /// <summary>등장 페이드인 (시작 시 1회, 또는 지도를 구입한 순간).</summary>
    private IEnumerator FadeIn()
    {
        _rootGroup.alpha = 0f;
        float t = 0f;
        const float dur = 0.4f;
        while (t < dur)
        {
            t += Time.deltaTime;
            _rootGroup.alpha = Mathf.SmoothStep(0f, 1f, t / dur);
            yield return null;
        }
        _rootGroup.alpha = 1f;
    }

    IEnumerator Start()
    {
        // 지도를 아직 안 샀으면 등장 자체가 없다 — 해금되는 순간 RefreshUpgradeState가 페이드인을 건다.
        if (!_mapUnlocked) yield break;

        yield return FadeIn();
    }

    /// <summary>
    /// 지도 해금 여부와 탐사 반경을 업그레이드에서 다시 읽는다.
    /// 이벤트 구독 대신 폴링인 이유: UpgradeManager·SaveManager가 미니맵보다 늦게 살아나는
    /// 씬이 있어 Awake 시점 구독이 조용히 실패한다. 1초 주기면 구입 반영 지연은 안 보인다.
    /// </summary>
    private void RefreshUpgradeState(bool initial)
    {
        _effExploreRadius = MapUnlockGate.GetExploreRadius(exploreRadius);

        bool unlocked = MapUnlockGate.IsUnlocked;
        if (!initial && unlocked == _mapUnlocked) return;

        _mapUnlocked = unlocked;
        if (_root != null) _root.gameObject.SetActive(unlocked);

        // 구입한 그 순간의 등장 연출. 시작 시점(initial)은 Start 코루틴이 처리한다.
        if (unlocked && !initial && isActiveAndEnabled) StartCoroutine(FadeIn());
    }

    void OnDestroy()
    {
        foreach (var obj in _generated)
            if (obj != null) Destroy(obj);
        _generated.Clear();
    }

    /// <summary>구버전 테스트 UI(흰 반투명 Image, 직사각 RawImage)를 숨긴다.</summary>
    private void HideLegacyUI()
    {
        var legacyImg = GetComponent<Image>();
        if (legacyImg != null) legacyImg.enabled = false;
        if (rawImage != null) rawImage.gameObject.SetActive(false);
    }

    // ===================================================
    // UI 빌드 (전부 코드 생성)
    // ===================================================
    private void BuildUI()
    {
        var rootObj = new GameObject("MinimapRoot", typeof(RectTransform));
        rootObj.transform.SetParent(transform, false);
        _root = (RectTransform)rootObj.transform;
        _root.sizeDelta = new Vector2(diameter, diameter);
        _root.anchoredPosition = Vector2.zero;
        _rootGroup = rootObj.AddComponent<CanvasGroup>();
        _rootGroup.interactable = false;
        _rootGroup.blocksRaycasts = false;

        float frameSize = diameter * (256f / 214f); // 프레임 텍스처 기하와 일치

        // 1. 뒷 글로우 (배경에서 계기판을 띄워주는 소프트 섀도)
        var glow = CreateImage(_root, "BackGlow", MakeRadialGlowSprite(96, new Color32(0, 0, 0, 255), 1.8f));
        glow.rectTransform.sizeDelta = new Vector2(frameSize * 1.12f, frameSize * 1.12f);
        glow.color = new Color(0f, 0f, 0f, 0.4f);

        // 1.5 배경 패널 (픽셀 하우징) — 9-슬라이스 스프라이트를 낮은 PPU로 크게 늘려 뭉툭한 도트 느낌을 준다.
        //      맵 표면 '뒤'에 깔리도록 여기서 생성한다(뒤에 만든 형제가 위로 그려짐).
        if (panelBackgroundSprite != null)
        {
            var panel = CreateImage(_root, "PixelPanel", panelBackgroundSprite);
            ApplyPixelSlice(panel, panelPixelsPerUnit, fillCenter: true); // 가운데까지 채운 배경면
            panel.color = panelColor;
            float pSize = diameter * Mathf.Max(0.1f, panelScale);
            panel.rectTransform.sizeDelta = new Vector2(pSize, pSize);
        }

        // 2. 맵 표면 (원형 마스크가 베이크된 텍스처)
        _mapTex = new Texture2D(TEX, TEX, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point, // 바이리니어 업스케일 블러 제거 → 도트처럼 또렷하게
            wrapMode = TextureWrapMode.Clamp
        };
        _generated.Add(_mapTex);
        _pixels = new Color32[TEX * TEX];
        Array.Copy(_basePixels, _pixels, _pixels.Length);
        _mapTex.SetPixels32(_pixels);
        _mapTex.Apply(false);

        var surfObj = new GameObject("MapSurface", typeof(RectTransform));
        surfObj.transform.SetParent(_root, false);
        _mapSurface = surfObj.AddComponent<RawImage>();
        _mapSurface.texture = _mapTex;
        _mapSurface.raycastTarget = false;
        _mapSurface.rectTransform.sizeDelta = new Vector2(diameter, diameter);

        // 2.5 마커 레이어 (지형 위, FX·프레임·플레이어 아래) — 엘베·청크입구 아이콘이 여기 담긴다
        var markerObj = new GameObject("Markers", typeof(RectTransform));
        markerObj.transform.SetParent(_root, false);
        _markerLayer = (RectTransform)markerObj.transform;
        _markerLayer.sizeDelta = new Vector2(diameter, diameter);
        _markerLayer.anchoredPosition = Vector2.zero;

        // 3. 레이더 스윕 (느린 회전 광 — 텍스처는 고정, RectTransform만 회전)
        if (enableSweep)
        {
            _sweepImg = CreateImage(_root, "Sweep", MakeSweepSprite(256));
            _sweepImg.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            _sweepImg.color = new Color(1f, 0.78f, 0.5f, 0.16f);
            _sweepRect = _sweepImg.rectTransform;
        }

        // 4. 소나 핑 링
        if (enablePing)
        {
            _pingImg = CreateImage(_root, "PingRing", MakeRingSprite(128, 58f, 2.2f));
            _pingImg.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            _pingImg.color = new Color(1f, 0.8f, 0.52f, 0f);
            _pingRect = _pingImg.rectTransform;
        }

        // 5. 플레이어 글로우 + 마커 (중앙 고정)
        _playerGlowImg = CreateImage(_root, "PlayerGlow", MakeRadialGlowSprite(64, new Color32(255, 205, 140, 255), 2f));
        _playerGlowImg.rectTransform.sizeDelta = Vector2.one * (markerSize * PlayerGlowFactor);
        _playerGlowImg.color = new Color(1f, 0.8f, 0.55f, 0.3f);

        var marker = CreateImage(_root, "PlayerMarker", playerMarkerSprite != null ? playerMarkerSprite : MakeDiamondSprite(32));
        marker.rectTransform.sizeDelta = Vector2.one * (markerSize * PlayerMarkerFactor);
        _playerRect = marker.rectTransform;

        // 6. 베젤 프레임 — 기본은 코드 생성 '원형' 픽셀 베젤(원형 유지 + 도트 양자화).
        //    frameSprite를 지정하면 그 스프라이트로 대체(Simple).
        var frame = CreateImage(_root, "Frame", frameSprite != null ? frameSprite : MakeFrameSprite(512));
        float fSize = frameSprite != null ? diameter * Mathf.Max(0.1f, frameSpriteScale) : frameSize;
        frame.rectTransform.sizeDelta = new Vector2(fSize, fSize);

        // 7. 깊이 배지 (원형 맵 아래에 분리 배치되는 캡슐) — 스프라이트 지정 시 9-슬라이스 픽셀 패널, 아니면 코드 캡슐
        var pill = CreateImage(_root, "DepthPill", depthPanelSprite != null ? depthPanelSprite : MakePillSprite(120, 36));
        if (depthPanelSprite != null)
        {
            ApplyPixelSlice(pill, depthPanelPixelsPerUnit, fillCenter: true);
            pill.color = depthPanelColor;
        }
        pill.rectTransform.sizeDelta = new Vector2(104f, 30f);
        float bezelOuterR = diameter * 0.5f * (240f / 214f); // 프레임 베젤 바깥 반지름
        pill.rectTransform.anchoredPosition = new Vector2(0f, -bezelOuterR - 15f - 4f);

        var textObj = new GameObject("DepthText", typeof(RectTransform));
        textObj.transform.SetParent(pill.rectTransform, false);
        _depthText = textObj.AddComponent<TextMeshProUGUI>();
        var textRect = _depthText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        _depthText.fontSize = 16f;
        _depthText.fontStyle = FontStyles.Bold;
        _depthText.color = DepthTextCol;
        _depthText.alignment = TextAlignmentOptions.Center;
        _depthText.raycastTarget = false;
        _depthText.textWrappingMode = TextWrappingModes.NoWrap;
        _depthText.text = "0m";
        var lm = LanguageManager.Instance;
        var font = lm != null ? lm.GetCurrentFont() : null;
        if (font != null) _depthText.font = font;

        // 8. 신호 없음(NO SIGNAL) 문구 — 최상단, 기본 숨김. 던전 등에서 켜진다.
        var nsObj = new GameObject("NoSignal", typeof(RectTransform));
        nsObj.transform.SetParent(_root, false);
        _noSignalLabel = nsObj.AddComponent<TextMeshProUGUI>();
        var nsRect = _noSignalLabel.rectTransform;
        nsRect.sizeDelta = new Vector2(diameter, 40f);
        nsRect.anchoredPosition = Vector2.zero;
        _noSignalLabel.fontSize = 26f;
        _noSignalLabel.fontStyle = FontStyles.Bold;
        _noSignalLabel.color = noSignalColor;
        _noSignalLabel.alignment = TextAlignmentOptions.Center;
        _noSignalLabel.raycastTarget = false;
        _noSignalLabel.textWrappingMode = TextWrappingModes.NoWrap;
        _noSignalLabel.characterSpacing = 6f;
        _noSignalLabel.text = noSignalText;
        if (font != null) _noSignalLabel.font = font;
        _noSignalLabel.gameObject.SetActive(false);
    }

    private Image CreateImage(Transform parent, string name, Sprite sprite)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// 9-슬라이스 스프라이트를 낮은 유효 PPU로 크게 늘려 뭉툭한 도트 느낌을 준다.
    /// 스프라이트 시트 원본 PPU(공유라 건드릴 수 없음)는 그대로 두고 Image에서만 재해석한다.
    /// fillCenter=true면 배경면(가운데 채움), false면 테두리 링(가운데 비움).
    /// </summary>
    private static void ApplyPixelSlice(Image img, float pixelsPerUnit, bool fillCenter)
    {
        img.type = Image.Type.Sliced;
        img.fillCenter = fillCenter;
        float spritePPU = img.sprite != null && img.sprite.pixelsPerUnit > 0f ? img.sprite.pixelsPerUnit : 100f;
        img.pixelsPerUnitMultiplier = Mathf.Max(0.0001f, pixelsPerUnit / spritePPU);
    }

    // ===================================================
    // 메인 루프
    // ===================================================
    void Update()
    {
        if (_root == null) return;

        _unlockPollTimer += Time.deltaTime;
        if (_unlockPollTimer >= UnlockPollInterval)
        {
            _unlockPollTimer = 0f;
            RefreshUpgradeState(initial: false);
        }

        // 지도 미구입 — 그리지도, 탐사 기록을 남기지도 않는다.
        if (!_mapUnlocked) return;

        EnsurePlayer();

        // 던전 등 '신호 없음' — 붉은 정적 노이즈 + 문구, 정상 맵 렌더 중단.
        bool noSignal = DungeonOverlayController.IsInDungeon;
        if (noSignal != _noSignalActive)
        {
            _noSignalActive = noSignal;
            ApplyNoSignal(noSignal);
        }
        if (noSignal)
        {
            UpdateNoSignal();
            return;
        }

        AnimateFX();
        UpdateDetectedMarkers();

        var tracker = DigPathTracker.Instance;
        if (tracker == null || playerTr == null) return;

        Vector2 pos = playerTr.position;

        _exploreTimer += Time.deltaTime;
        if (_exploreTimer >= updateInterval)
        {
            _exploreTimer = 0f;
            tracker.ExploreArea(pos, _effExploreRadius);
        }

        UpdateDepthLabel(pos.y);
        UpdateMarkers(pos, tracker.cellSize);

        // 서브셀 스크롤: 플레이어가 조금이라도 움직였으면 재렌더.
        // 정지 중에도 ForceRenderInterval마다 갱신해 비동기 청크 로드를 반영한다.
        // 돌 등록/해제·마커 발견(MapMarkerRegistry.IsDirty)도 재렌더 트리거.
        bool moved = (pos - _lastRenderPos).sqrMagnitude > 0.0004f;
        _renderTimer += Time.deltaTime;
        if ((tracker.IsDirty || moved || !_renderedOnce || MapMarkerRegistry.IsDirty || MapRockCache.IsDirty || _renderTimer >= ForceRenderInterval)
            && _renderTimer >= MinRenderInterval)
        {
            _renderTimer = 0f;
            RenderMap(tracker, pos);
            tracker.ClearDirty();
            MapMarkerRegistry.ClearDirty();
            MapRockCache.ClearDirty();
            _lastRenderPos = pos;
            _renderedOnce = true;
        }
    }

    /// <summary>발견된 엘베·청크입구 마커 아이콘을 플레이어 기준 위치로 배치한다(원 밖은 테두리에 클램프).</summary>
    private void UpdateMarkers(Vector2 playerPos, float cellSize)
    {
        if (_markerLayer == null) return;
        var markers = MapMarkerRegistry.Markers;

        float uiPerWorld = diameter / ((viewRadius * 2 + 1) * Mathf.Max(0.01f, cellSize));
        float rimR = diameter * 0.5f - markerSize * 0.5f - 4f;

        int used = 0;
        for (int m = 0; m < markers.Count; m++)
        {
            var mk = markers[m];
            float lx = (mk.world.x - playerPos.x) * uiPerWorld;
            float ly = (mk.world.y - playerPos.y) * uiPerWorld;
            float dist = Mathf.Sqrt(lx * lx + ly * ly);
            bool clamped = dist > rimR;
            if (clamped && dist > 0.001f)
            {
                float s = rimR / dist;
                lx *= s; ly *= s;
            }

            Image img = GetMarkerImage(used++);
            img.sprite = SpriteForMarker(mk);
            // markerSize를 매 프레임 반영 → 인스펙터 값을 런타임에서도 그대로 따라간다.
            img.rectTransform.sizeDelta = new Vector2(markerSize, markerSize);
            img.rectTransform.anchoredPosition = new Vector2(lx, ly);
            img.rectTransform.localScale = Vector3.one * (clamped ? 0.72f : 1f);
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
        }

        for (int k = used; k < _markerPool.Count; k++)
            if (_markerPool[k].gameObject.activeSelf) _markerPool[k].gameObject.SetActive(false);
    }

    /// <summary>마커 종류·상태에 맞는 스프라이트. 인스펙터 지정 우선, 없으면 코드 생성 아이콘.</summary>
    private Sprite SpriteForMarker(MapMarkerRegistry.Marker mk)
    {
        if (mk.kind == MapMarkerKind.Elevator)
            return elevatorMarkerSprite != null ? elevatorMarkerSprite : MapMarkerVisuals.Elevator;
        // 청크 입구 — 이미 탐험해 재입장 불가한 입구는 별도 스프라이트로 구분.
        if (MapMarkerRegistry.IsEntranceUsed(mk.world))
            return entranceUsedMarkerSprite != null ? entranceUsedMarkerSprite : MapMarkerVisuals.EntranceUsed;
        return entranceMarkerSprite != null ? entranceMarkerSprite : MapMarkerVisuals.Entrance;
    }

    private Image GetMarkerImage(int index)
    {
        while (index >= _markerPool.Count)
        {
            var obj = new GameObject("Marker", typeof(RectTransform));
            obj.transform.SetParent(_markerLayer, false);
            var img = obj.AddComponent<Image>();
            img.raycastTarget = false;
            // 앵커/피벗을 중앙으로 명시해야 sizeDelta가 '절대 크기'로 동작한다(기본값 의존 금지).
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(markerSize, markerSize);
            _markerPool.Add(img);
        }
        return _markerPool[index];
    }

    // ===================================================
    // 신호 없음 (던전 등 맵을 볼 수 없는 곳)
    // ===================================================
    /// <summary>신호 없음 on/off 시 콘텐츠 요소 표시를 토글한다.</summary>
    private void ApplyNoSignal(bool on)
    {
        if (_noSignalLabel != null) _noSignalLabel.gameObject.SetActive(on);
        if (_sweepRect != null) _sweepRect.gameObject.SetActive(!on);
        if (_pingRect != null) _pingRect.gameObject.SetActive(!on);
        if (_playerRect != null) _playerRect.gameObject.SetActive(!on);
        if (_playerGlowImg != null) _playerGlowImg.gameObject.SetActive(!on);
        if (_markerLayer != null) _markerLayer.gameObject.SetActive(!on);

        // 탐지 마커 풀은 _markerLayer가 아니라 _root 직속이라 위 토글에 걸리지 않는다.
        // 게다가 신호 없음 중에는 Update가 조기 반환해 UpdateDetectedMarkers가 아예
        // 돌지 않으므로, 여기서 직접 끄지 않으면 던전 진입 직전 상태로 화면에 박제된다.
        // (해제는 불필요 — 복귀하면 UpdateDetectedMarkers가 필요한 것만 다시 켠다.)
        if (on)
        {
            for (int i = 0; i < _detectMarkers.Count; i++)
                _detectMarkers[i].gameObject.SetActive(false);
            for (int i = 0; i < _edgeMarkers.Count; i++)
                _edgeMarkers[i].gameObject.SetActive(false);

            // 맵을 어두운 빈 화면으로만 지운다(정적 노이즈 없이) — 글자만 표기.
            if (_pixels != null && _basePixels != null)
            {
                Array.Copy(_basePixels, _pixels, _pixels.Length);
                _mapTex.SetPixels32(_pixels);
                _mapTex.Apply(false);
            }
        }
        else
        {
            // 정상 맵으로 즉시 복귀하도록 다음 프레임 강제 재렌더
            _renderTimer = ForceRenderInterval;
            _renderedOnce = false;
        }
    }

    /// <summary>신호 없음 중 프레임 갱신 — 문구를 on/off로 깜빡인다.</summary>
    private void UpdateNoSignal()
    {
        if (_noSignalLabel != null)
        {
            var c = noSignalColor;
            c.a = Mathf.Repeat(Time.unscaledTime, 0.9f) < 0.55f ? 1f : 0f; // 켜짐 0.55s / 꺼짐 0.35s
            _noSignalLabel.color = c;
        }
    }

    private void EnsurePlayer()
    {
        if (playerTr != null) return;
        _playerSearchTimer -= Time.deltaTime;
        if (_playerSearchTimer > 0f) return;
        _playerSearchTimer = 1f;
        var pc = FindFirstObjectByType<PlayerController>();
        if (pc != null) playerTr = pc.transform;
    }

    private void AnimateFX()
    {
        float now = Time.time;

        // 플레이어 펄스 — 크기는 markerSize에 비례(인스펙터 값을 런타임에도 따라간다).
        float pulse = 0.5f + 0.5f * Mathf.Sin(now * 2.6f);
        if (_playerRect != null)
        {
            _playerRect.sizeDelta = Vector2.one * (markerSize * PlayerMarkerFactor);
            _playerRect.localScale = Vector3.one * (1f + 0.08f * pulse);
        }
        if (_playerGlowImg != null)
        {
            _playerGlowImg.rectTransform.sizeDelta = Vector2.one * (markerSize * PlayerGlowFactor);
            var c = _playerGlowImg.color;
            c.a = 0.2f + 0.14f * pulse;
            _playerGlowImg.color = c;
        }

        // 레이더 스윕 회전
        if (_sweepRect != null)
            _sweepRect.localRotation = Quaternion.Euler(0f, 0f, -now * SweepDegPerSec);

        // 소나 핑 확산
        if (_pingRect != null)
        {
            _pingTimer += Time.deltaTime;
            if (_pingTimer >= PingInterval) _pingTimer = 0f;

            float t = _pingTimer / PingDuration;
            if (t <= 1f)
            {
                float eased = 1f - (1f - t) * (1f - t); // easeOutQuad
                _pingRect.localScale = Vector3.one * Mathf.Lerp(0.12f, 1f, eased);
                var c = _pingImg.color;
                c.a = Mathf.Pow(1f - t, 1.6f) * 0.4f;
                _pingImg.color = c;
            }
            else if (_pingImg.color.a > 0f)
            {
                var c = _pingImg.color;
                c.a = 0f;
                _pingImg.color = c;
            }
        }
    }

    // ===================================================
    // 탐지파동 유물 — 발견된 특수청크 오프센터 마커 (Task 7)
    // ===================================================
    /// <summary>
    /// DetectedChunkStore.All을 순회해 플레이어 기준 오프센터 마커를 그린다.
    /// 화면 로컬 px/월드 스케일은 RenderMap()의 pxPerWorld(텍셀/월드)와 텍스처 표시 배율
    /// (diameter / TEX)을 곱한 것과 동일 — diameter/((viewRadius*2+1)*cellSize)로 정리된다.
    /// _root(중앙 피벗) 자식이므로 anchoredPosition = (world - player) * PX_PER_WORLD.
    /// </summary>
    private void UpdateDetectedMarkers()
    {
        if (_root == null || playerTr == null) return;
        var tracker = DigPathTracker.Instance;
        if (tracker == null) return;

        if (_detectSprite == null) _detectSprite = MakeDiamondSprite(24);

        float cellSize = Mathf.Max(0.01f, tracker.cellSize);
        float PX_PER_WORLD = diameter / ((viewRadius * 2 + 1) * cellSize);
        float radiusPx = diameter * 0.5f * 0.9f; // 콘텐츠 감쇠(0.8R)와 정합, 원형 안쪽만

        Vector2 pc0 = playerTr.position;
        var all = DetectedChunkStore.Instance.All;
        float half = ChunkCoords.WorldSize * 0.5f;

        int idx = 0;
        int edgeIdx = 0;
        foreach (var kv in all)
        {
            Vector3 w = ChunkCoords.ToWorld(kv.Key);
            Vector2 world = new Vector2(w.x + half, w.y + half);
            Vector2 local = (world - pc0) * PX_PER_WORLD;

            // 시야 안/밖 판정은 매 프레임 재계산한다. 알림 3초 동안 플레이어가 움직이면
            // 가장자리 마커 <-> 영구 마커로 자연스럽게 전환된다.
            bool announcing = IsAnnouncing(kv.Key, out float pulse);

            if (local.magnitude > radiusPx)
            {
                // 시야 밖: 알림 중일 때만 가장자리에 방향 마커를 띄운다.
                if (!announcing) continue;

                RectTransform ert = GetOrCreateEdgeMarker(edgeIdx++);
                ert.gameObject.SetActive(true);
                ert.sizeDelta = Vector2.one * (markerSize * EdgeMarkerFactor); // markerSize 연동
                ert.anchoredPosition = local.normalized * (radiusPx * 0.92f);
                ert.localScale = Vector3.one * (1f + pulse * 0.6f);
                var eimg = ert.GetComponent<Image>();
                eimg.color = new Color(1f, 0.85f, 0.35f, 0.35f + pulse * 0.65f);
                continue;
            }

            RectTransform rt = GetOrCreateDetectMarker(idx++);
            rt.gameObject.SetActive(true);
            rt.sizeDelta = Vector2.one * (markerSize * DetectMarkerFactor); // markerSize 연동
            rt.anchoredPosition = local;

            // 스케일은 매 프레임 명시적으로 세팅한다. 이 풀은 dictionary 순회 순서대로
            // 인덱스가 배정되므로 같은 오브젝트가 프레임마다 다른 좌표를 담당할 수 있다.
            // 리셋을 빠뜨리면 알림이 끝난 뒤 그 오브젝트를 물려받은 마커가 부푼 채로 남는다.
            rt.localScale = announcing ? Vector3.one * (1f + pulse * 0.6f) : Vector3.one;

            var img = rt.GetComponent<Image>();
            if (announcing)
                img.color = new Color(1f, 0.95f, 0.6f, 0.5f + pulse * 0.5f);
            else
                img.color = kv.Value.visited
                    ? new Color(0.7f, 0.7f, 0.7f, 0.5f)
                    : new Color(1f, 0.85f, 0.35f, 1f);
        }

        for (int i = idx; i < _detectMarkers.Count; i++)
            _detectMarkers[i].gameObject.SetActive(false);
        for (int i = edgeIdx; i < _edgeMarkers.Count; i++)
            _edgeMarkers[i].gameObject.SetActive(false);

        FlushExpiredAnnounces();
    }

    private RectTransform GetOrCreateDetectMarker(int i)
    {
        if (i < _detectMarkers.Count) return _detectMarkers[i];
        var marker = new GameObject($"MiniDetect{i}").AddComponent<Image>();
        marker.transform.SetParent(_root, false);
        marker.sprite = _detectSprite;
        marker.raycastTarget = false;
        var rt = marker.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.one * (markerSize * DetectMarkerFactor);
        _generated.Add(marker);
        _detectMarkers.Add(rt);
        return rt;
    }

    private void UpdateDepthLabel(float playerY)
    {
        if (_depthText == null) return;
        int depth = Mathf.Max(0, Mathf.RoundToInt(surfaceWorldY - playerY));
        if (depth == _lastDepthShown) return;
        _lastDepthShown = depth;
        _depthText.text = depth.ToString("N0") + "m";
    }

    // ===================================================
    // 맵 텍스처 렌더링
    // ===================================================
    /// <summary>원형 마스크+비네트 배경과 가장자리 감쇠 LUT를 미리 굽는다.</summary>
    private void BakeBase()
    {
        _basePixels = new Color32[TEX * TEX];
        _fade = new byte[TEX * TEX];
        _vision = new float[TEX * TEX];

        float c = TEX * 0.5f - 0.5f;
        float radius = TEX * 0.5f - 1.5f;

        for (int y = 0; y < TEX; y++)
        {
            for (int x = 0; x < TEX; x++)
            {
                int i = y * TEX + x;
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);

                // 플레이어 중심(=맵 중심) 원형 시야: 안쪽은 선명, 바깥으로 부드럽게 감소.
                // 직각(셀) 경계 대신 구형태로 어두워지게 하는 핵심.
                float rn = radius > 0f ? r / radius : 1f;
                float visT = Mathf.Clamp01(Mathf.InverseLerp(visionFullRadius, visionEdgeRadius, rn));
                visT = visT * visT * (3f - 2f * visT); // smoothstep
                _vision[i] = enableVisionFade ? Mathf.Lerp(1f, visionFloor, visT) : 1f;

                float maskA = Mathf.Clamp01((radius - r) / 1.5f); // 원 가장자리 AA
                if (maskA <= 0f)
                {
                    _basePixels[i] = new Color32(0, 0, 0, 0);
                    _fade[i] = 0;
                    _vision[i] = 0f;
                    continue;
                }

                // 비네트: 중앙에서 가장자리로 갈수록 어두워짐
                float vt = Mathf.Pow(Mathf.Clamp01(r / radius), 1.6f);
                byte br = (byte)Mathf.RoundToInt(Mathf.Lerp(BgCenter.r, BgEdge.r, vt));
                byte bg = (byte)Mathf.RoundToInt(Mathf.Lerp(BgCenter.g, BgEdge.g, vt));
                byte bb = (byte)Mathf.RoundToInt(Mathf.Lerp(BgCenter.b, BgEdge.b, vt));
                _basePixels[i] = new Color32(br, bg, bb, (byte)(BgAlpha * maskA));

                // 콘텐츠 감쇠: 0.8R까지 온전히, 0.97R에서 소멸
                float fadeT = 1f - Mathf.Clamp01((r / radius - 0.80f) / 0.17f);
                fadeT = fadeT * fadeT * (3f - 2f * fadeT); // smoothstep
                _fade[i] = (byte)Mathf.RoundToInt(255f * fadeT * maskA);
            }
        }
    }

    /// <summary>
    /// 알림 중이면 true. pulse01은 0~1 사이를 오가는 반짝임 계수(끝날수록 약해진다).
    /// </summary>
    private bool IsAnnouncing(Vector2Int coord, out float pulse01)
    {
        pulse01 = 0f;
        if (!_announceUntil.TryGetValue(coord, out float until)) return false;

        float remain = until - Time.time;
        if (remain <= 0f) { _announceExpired.Add(coord); return false; }

        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 14f);  // 초당 ~2.2회
        pulse01 = blink * Mathf.Clamp01(remain / AnnounceDuration); // 끝으로 갈수록 감쇠
        return true;
    }

    private void RenderMap(DigPathTracker tracker, Vector2 playerPos)
    {
        Array.Copy(_basePixels, _pixels, _pixels.Length);

        float cellSize = Mathf.Max(0.01f, tracker.cellSize);
        float pxPerWorld = TEX / ((viewRadius * 2 + 1) * cellSize);
        float worldPerPx = 1f / pxPerWorld;
        float center = TEX * 0.5f;
        float halfViewWorld = center * worldPerPx;

        // 1. 청크 그리드 — 각진 직선이 부드러운 원형 룩과 충돌해 뺐다.
        //    되살리려면 간격 필드(월드 유닛, 청크 1칸 = 10)를 다시 추가하고
        //    DrawVLine/DrawHLine으로 격자를 그리면 된다.

        // 2. 지상 영역(하늘) 틴트
        int surfacePy = Mathf.RoundToInt(center + (surfaceWorldY - playerPos.y) * pxPerWorld);
        if (showSurfaceLine)
        {
            for (int y = Mathf.Max(0, surfacePy + 1); y < TEX; y++)
                BlendRow(y, SkyCol, 0.1f);
        }

        // 3. 탐사 셀 스냅샷 — 텍셀마다 HashSet 조회를 피하기 위한 로컬 그리드
        int gridHalf = viewRadius + 2;
        int grid = gridHalf * 2 + 1;
        if (_exploredGrid == null || _exploredGrid.Length < grid * grid)
            _exploredGrid = new bool[grid * grid];
        int pcellX = FastFloor(playerPos.x / cellSize);
        int pcellY = FastFloor(playerPos.y / cellSize);
        for (int gy = 0; gy < grid; gy++)
            for (int gx = 0; gx < grid; gx++)
                _exploredGrid[gy * grid + gx] =
                    tracker.IsExplored(new Vector2Int(pcellX - gridHalf + gx, pcellY - gridHalf + gy));
        _expCellSize = cellSize;
        _expOriginCellX = pcellX - gridHalf;
        _expOriginCellY = pcellY - gridHalf;
        _expGrid = grid;

        // 4. 텍셀 단위 지형 샘플링 — 실제 청크 픽셀을 읽어 지형의 축소판을 그린다.
        //    파인 곳=앰버, 남은 흙=암석 톤. 탐사도(e)는 셀 그리드 바이리니어 보간
        //    → 파진 땅을 부드럽고 자연스럽게 따라가는 글로우. (시야 원 디밍은 없음)
        var imm = InfinityMapManager.Instance;
        _sampChunk = null;
        _sampCx = int.MinValue;
        _sampCy = int.MinValue;
        _sampValid = false;

        // 지형 스냅샷 캐시 채우기 — 지나온 길을 캐시에 남겨두면 전체 지도(M)에서 멀어져도 또렷하게 보인다.
        // 파기는 InfinityMapManager.MarkChunkDirty → MapTerrainCache.Invalidate가 파인 영역만
        // 정확히 짚어주므로, 예전처럼 근처 3×3 청크를 매 렌더 통째로 다시 구울 필요가 없다.
        // 그 훅을 타지 않는 경로(PNG 청크 교체·풀 재사용 등)에 대비해 렌더당 한 청크씩
        // 순번을 돌며 전 영역을 재베이크한다 — 무엇을 놓치든 1초 안에 자가 치유된다.
        if (imm != null)
        {
            float cw = imm.chunkWidthWorld, chw = imm.chunkHeightWorld;
            _invChunkW = 1f / cw;
            _invChunkH = 1f / chw;

            int cxMin = FastFloor((playerPos.x - halfViewWorld) * _invChunkW);
            int cxMax = FastFloor((playerPos.x + halfViewWorld) * _invChunkW);
            int cyMin = FastFloor((playerPos.y - halfViewWorld) * _invChunkH);
            int cyMax = FastFloor((playerPos.y + halfViewWorld) * _invChunkH);

            int visible = (cxMax - cxMin + 1) * (cyMax - cyMin + 1);
            int rebakeAt = visible > 0 ? _rebakeCursor % visible : -1;
            _rebakeCursor = (_rebakeCursor + 1) & 0x3FFFFFFF;

            int seen = 0;
            for (int ccy = cyMin; ccy <= cyMax; ccy++)
                for (int ccx = cxMin; ccx <= cxMax; ccx++)
                {
                    int slot = seen++;
                    var ch = imm.GetChunk(new Vector2Int(ccx, ccy));
                    if (ch == null || !ch.baseData.IsCreated) continue;
                    MapTerrainCache.Capture(new Vector2Int(ccx, ccy), ch, force: slot == rebakeAt);
                }
        }

        for (int py = 0; py < TEX; py++)
        {
            float wy = playerPos.y + (py + 0.5f - center) * worldPerPx;
            if (showSurfaceLine && wy > surfaceWorldY) break; // 아래→위 순회라 이 위는 전부 하늘

            int rowOff = py * TEX;
            float cellFy = wy / cellSize - 0.5f;
            int cy0 = FastFloor(cellFy);
            float fy = cellFy - cy0;
            int gy0 = cy0 - (pcellY - gridHalf);

            for (int px = 0; px < TEX; px++)
            {
                int i = rowOff + px;
                byte f = _fade[i];
                if (f == 0) continue;

                // 어둠은 오직 플레이어 중심 원형 시야로만 만든다. 시야 밖이면 그리지 않아 배경(어둠)이 남는다.
                // → 어두운 부분의 경계가 '탐사 셀'의 각진 모양이 아니라 깔끔한 원이 된다.
                float vis = _vision[i];
                if (vis <= 0.002f) continue;

                float wx = playerPos.x + (px + 0.5f - center) * worldPerPx;

                float cellFx = wx / cellSize - 0.5f;
                int cx0 = FastFloor(cellFx);
                float fx = cellFx - cx0;
                int gx0 = cx0 - (pcellX - gridHalf);
                float e = Mathf.Lerp(
                    Mathf.Lerp(SampleExplored(gx0, gy0, grid), SampleExplored(gx0 + 1, gy0, grid), fx),
                    Mathf.Lerp(SampleExplored(gx0, gy0 + 1, grid), SampleExplored(gx0 + 1, gy0 + 1, grid), fx),
                    fy);

                // 지형 empty를 텍셀 내부 4점 평균으로 안티에일리어싱 → 앰버(판 굴) 경계가
                // 딱딱하게 잘리지 않고 한 텍셀에 걸쳐 부드럽게 페이드한다.
                float emptiness = imm == null ? 1f : SampleTerrainEmptiness(imm, wx, wy, worldPerPx * 0.4f);

                // 시야 원 안은 지형을 빠짐없이 채운다: '탐사한 빈 공간(판 굴)'만 앰버로 밝히고,
                // 그 외(암석·미탐사 공동)는 은은한 암석 톤으로 그려 미탐사 공동을 은닉한다.
                // 검은 빈틈(각진 셀 경계)이 사라지고, 어둠은 원형 시야 falloff에서만 생긴다.
                float tun = emptiness * (e * e * (3f - 2f * e));
                Color32 col = new Color32(
                    (byte)(RockCol.r + (TunnelCol.r - RockCol.r) * tun),
                    (byte)(RockCol.g + (TunnelCol.g - RockCol.g) * tun),
                    (byte)(RockCol.b + (TunnelCol.b - RockCol.b) * tun),
                    255);

                // 암석/미탐사는 은은하게(0.5), 판 굴은 진하게(1.0) → 굴이 도드라진다.
                float a = vis * (f / 255f) * (0.5f + 0.5f * tun);
                if (a <= 0.001f) continue;

                Color32 ep = _pixels[i];
                ep.r = (byte)(ep.r + (col.r - ep.r) * a);
                ep.g = (byte)(ep.g + (col.g - ep.g) * a);
                ep.b = (byte)(ep.b + (col.b - ep.b) * a);
                _pixels[i] = ep;
            }
        }

        // 4.4 지형 소프트 글로우 — 밝은 지형(앰버) 경계가 딱딱하게 잘리지 않고 부드럽게 번지도록 블러.
        //      (돌·지표면·마커는 이 뒤에 또렷하게 그린다)
        BlurContentRGB(contentBlur);

        // 4.5 안 캔 돌 채움 — MapRockCache 스냅샷을 '안 파진 땅'처럼 실제 돌 모양대로 채운다.
        //      언로드돼도 스냅샷이 남아 지도에 유지되고, 실제로 캐지면 캐시에서 빠져 사라진다.
        foreach (var e in MapRockCache.Entries)
            PaintRockEntry(e, playerPos, center, pxPerWorld, worldPerPx);

        // 5. 지표면 라인 — 플레이어 위에 반투명 선으로 도드라져 거슬린다는 피드백으로 비활성화.
        //    (지상/지하 구분은 하늘 톤의 부드러운 경계로 충분)
        // if (showSurfaceLine && surfacePy >= 0 && surfacePy < TEX)
        //     DrawHLine(surfacePy, SurfaceCol, 0.45f);

        _mapTex.SetPixels32(_pixels);
        _mapTex.Apply(false);
    }

    /// <summary>만료 키 일괄 제거. 마커 순회가 끝난 뒤 반드시 호출한다.</summary>
    private void FlushExpiredAnnounces()
    {
        if (_announceExpired.Count == 0) return;
        foreach (var c in _announceExpired) _announceUntil.Remove(c);
        _announceExpired.Clear();
    }

    /// <summary>_pixels의 RGB를 분리형 박스 블러로 부드럽게(알파=원형 마스크는 유지). 소프트 글로우용.</summary>
    private void BlurContentRGB(int radius)
    {
        if (radius < 1) return;
        if (_blurTmp == null || _blurTmp.Length != _pixels.Length)
            _blurTmp = new Color32[_pixels.Length];

        // 가로 패스: _pixels → _blurTmp
        for (int y = 0; y < TEX; y++)
        {
            int row = y * TEX;
            for (int x = 0; x < TEX; x++)
            {
                int r = 0, g = 0, b = 0, cnt = 0;
                int kmin = x - radius < 0 ? -x : -radius;
                int kmax = x + radius >= TEX ? TEX - 1 - x : radius;
                for (int k = kmin; k <= kmax; k++)
                {
                    Color32 p = _pixels[row + x + k];
                    r += p.r; g += p.g; b += p.b; cnt++;
                }
                Color32 o = _pixels[row + x];
                _blurTmp[row + x] = new Color32((byte)(r / cnt), (byte)(g / cnt), (byte)(b / cnt), o.a);
            }
        }

        // 세로 패스: _blurTmp → _pixels
        for (int y = 0; y < TEX; y++)
        {
            int row = y * TEX;
            for (int x = 0; x < TEX; x++)
            {
                int r = 0, g = 0, b = 0, cnt = 0;
                int kmin = y - radius < 0 ? -y : -radius;
                int kmax = y + radius >= TEX ? TEX - 1 - y : radius;
                for (int k = kmin; k <= kmax; k++)
                {
                    Color32 p = _blurTmp[(y + k) * TEX + x];
                    r += p.r; g += p.g; b += p.b; cnt++;
                }
                Color32 o = _blurTmp[row + x];
                _pixels[row + x] = new Color32((byte)(r / cnt), (byte)(g / cnt), (byte)(b / cnt), o.a);
            }
        }
    }

    private RectTransform GetOrCreateEdgeMarker(int i)
    {
        if (i < _edgeMarkers.Count) return _edgeMarkers[i];
        var marker = new GameObject($"MiniEdgeDetect{i}").AddComponent<Image>();
        marker.transform.SetParent(_root, false);
        marker.sprite = _detectSprite;
        marker.raycastTarget = false;
        var rt = marker.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.one * (markerSize * EdgeMarkerFactor);
        _generated.Add(marker);   // OnDestroy에서 함께 정리되도록 필수
        _edgeMarkers.Add(rt);
        return rt;
    }

    /// <summary>돌 스냅샷(MapRockCache.Entry)을 실루엣대로 채운다(_fade·_vision 반영).</summary>
    private void PaintRockEntry(MapRockCache.Entry e, Vector2 playerPos, float center, float pxPerWorld, float worldPerPx)
    {
        Color32 col = e.isMineral ? MapMarkerVisuals.MineralRockFill : MapMarkerVisuals.RockFill;
        float xMin = e.min.x, yMin = e.min.y, xMax = e.min.x + e.size.x, yMax = e.min.y + e.size.y;
        float txMin = center + (xMin - playerPos.x) * pxPerWorld - 0.5f;
        float txMax = center + (xMax - playerPos.x) * pxPerWorld - 0.5f;
        float tyMin = center + (yMin - playerPos.y) * pxPerWorld - 0.5f;
        float tyMax = center + (yMax - playerPos.y) * pxPerWorld - 0.5f;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(txMin));
        int x1 = Mathf.Min(TEX - 1, Mathf.CeilToInt(txMax));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(tyMin));
        int y1 = Mathf.Min(TEX - 1, Mathf.CeilToInt(tyMax));
        if (x0 > x1 || y0 > y1) return; // 화면 밖 → 스킵 (캐시가 커도 저렴)

        for (int y = y0; y <= y1; y++)
        {
            int rowOff = y * TEX;
            float wy = playerPos.y + (y + 0.5f - center) * worldPerPx;
            for (int x = x0; x <= x1; x++)
            {
                int i = rowOff + x;
                byte f = _fade[i];
                if (f == 0) continue;
                float wx = playerPos.x + (x + 0.5f - center) * worldPerPx;
                if (!MapRockCache.Sample(e, wx, wy)) continue;
                // 탐사한 곳의 돌만 그린다 — 캐시는 '노출된 돌' 전부를 담고 있고(자연 공동에 묻힌 돌은
                // 청크 로드만으로 노출 판정을 통과한다), 여기서 거르지 않으면 한 번도 판 적 없는 땅의
                // 돌이 미리 드러난다. 셀 그리드 바이리니어라 경계는 각지지 않고 부드럽게 페이드한다.
                float a = 0.95f * (f / 255f) * _vision[i] * ExploredAt(wx, wy);
                if (a <= 0.003f) continue;
                Color32 ep = _pixels[i];
                ep.r = (byte)(ep.r + (col.r - ep.r) * a);
                ep.g = (byte)(ep.g + (col.g - ep.g) * a);
                ep.b = (byte)(ep.b + (col.b - ep.b) * a);
                _pixels[i] = ep;
            }
        }
    }

    private float SampleExplored(int gx, int gy, int grid)
        => (uint)gx < (uint)grid && (uint)gy < (uint)grid && _exploredGrid[gy * grid + gx] ? 1f : 0f;

    /// <summary>월드 좌표의 탐사도(0~1). 지형 루프의 e와 같은 셀 바이리니어 + 스무스스텝.</summary>
    private float ExploredAt(float wx, float wy)
    {
        float cellFx = wx / _expCellSize - 0.5f;
        float cellFy = wy / _expCellSize - 0.5f;
        int cx0 = FastFloor(cellFx), cy0 = FastFloor(cellFy);
        float fx = cellFx - cx0, fy = cellFy - cy0;
        int gx0 = cx0 - _expOriginCellX, gy0 = cy0 - _expOriginCellY;
        float e = Mathf.Lerp(
            Mathf.Lerp(SampleExplored(gx0, gy0, _expGrid), SampleExplored(gx0 + 1, gy0, _expGrid), fx),
            Mathf.Lerp(SampleExplored(gx0, gy0 + 1, _expGrid), SampleExplored(gx0 + 1, gy0 + 1, _expGrid), fx),
            fy);
        return e * e * (3f - 2f * e);
    }

    private static int FastFloor(float v)
    {
        int i = (int)v;
        return v < i ? i - 1 : i;
    }

    /// <summary>월드 좌표의 실제 지형이 비어있는지(파임·동굴) 청크 데이터에서 직접 읽는다.</summary>
    private bool SampleTerrainEmpty(InfinityMapManager imm, float wx, float wy)
    {
        int cx = FastFloor(wx / imm.chunkWidthWorld);
        int cy = FastFloor(wy / imm.chunkHeightWorld);
        if (cx != _sampCx || cy != _sampCy)
        {
            _sampCx = cx;
            _sampCy = cy;
            _sampChunk = imm.GetChunk(new Vector2Int(cx, cy));
            _sampValid = _sampChunk != null && _sampChunk.baseData.IsCreated;
            if (_sampValid)
            {
                _sampChunkPos = _sampChunk.transform.position;
                _sampPPU = _sampChunk.PPU;
            }
        }
        // 언로드된 청크는 캐시(스냅샷)에서 실제 지형 모양을 읽어 또렷하게 그린다.
        // 캐시에도 없으면(한 번도 로드된 적 없는 미지 영역) 흙(막힘)으로 취급한다.
        // 통로로 가정하면 탐사도(e) 바이리니어 보간이 밝혀진 경계 바깥으로 번질 때
        // 아직 로드 안 된 흙이 앰버(판 굴)처럼 조금 새어 보인다. 흙으로 취급하면
        // 지나온 길은 캐시가 채우므로 사라지지 않고, 미지 영역만 암석 톤으로 은닉된다.
        if (!_sampValid)
        {
            if (MapTerrainCache.TrySampleEmpty(
                    new Vector2Int(_sampCx, _sampCy),
                    wx / imm.chunkWidthWorld - _sampCx,
                    wy / imm.chunkHeightWorld - _sampCy,
                    out bool cachedEmpty))
                return cachedEmpty;
            return false;
        }

        int px = FastFloor((wx - _sampChunkPos.x) * _sampPPU);
        int py = FastFloor((wy - _sampChunkPos.y) * _sampPPU);
        return _sampChunk.IsTransparent(px, py);
    }

    /// <summary>텍셀 footprint 안 4점의 빈 공간 비율(0~1). 앰버 경계를 부드럽게 하는 안티에일리어싱용.</summary>
    private float SampleTerrainEmptiness(InfinityMapManager imm, float wx, float wy, float half)
    {
        float s = 0f;
        if (SampleTerrainEmpty(imm, wx - half, wy - half)) s += 1f;
        if (SampleTerrainEmpty(imm, wx + half, wy - half)) s += 1f;
        if (SampleTerrainEmpty(imm, wx - half, wy + half)) s += 1f;
        if (SampleTerrainEmpty(imm, wx + half, wy + half)) s += 1f;
        return s * 0.25f;
    }

    /// <summary>사각 영역을 가장자리 감쇠를 적용해 색으로 블렌드한다. (RGB만, 알파는 유지)</summary>
    private void FillBlend(int x0, int y0, int x1, int y1, Color32 col, float strength)
    {
        x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0);
        x1 = Mathf.Min(TEX, x1); y1 = Mathf.Min(TEX, y1);
        for (int y = y0; y < y1; y++)
        {
            int row = y * TEX;
            for (int x = x0; x < x1; x++)
            {
                int i = row + x;
                byte f = _fade[i];
                if (f == 0) continue;
                float a = strength * (f / 255f);
                Color32 e = _pixels[i];
                e.r = (byte)(e.r + (col.r - e.r) * a);
                e.g = (byte)(e.g + (col.g - e.g) * a);
                e.b = (byte)(e.b + (col.b - e.b) * a);
                _pixels[i] = e;
            }
        }
    }

    private void DrawVLine(int x, Color32 col, float strength)
    {
        if (x < 0 || x >= TEX) return;
        FillBlend(x, 0, x + 1, TEX, col, strength);
    }

    private void DrawHLine(int y, Color32 col, float strength)
    {
        if (y < 0 || y >= TEX) return;
        FillBlend(0, y, TEX, y + 1, col, strength);
    }

    private void BlendRow(int y, Color32 col, float strength)
    {
        FillBlend(0, y, TEX, y + 1, col, strength);
    }

    // ===================================================
    // 프로시저럴 스프라이트 (전부 1회 생성)
    // ===================================================
    private Sprite MakeSprite(Texture2D tex, bool pointFilter = false)
    {
        tex.filterMode = pointFilter ? FilterMode.Point : FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        _generated.Add(tex);
        _generated.Add(sprite);
        return sprite;
    }

    /// <summary>중심에서 바깥으로 사라지는 부드러운 원형 글로우.</summary>
    private Sprite MakeRadialGlowSprite(int size, Color32 col, float falloffPow)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        float c = size * 0.5f - 0.5f;
        float radius = size * 0.5f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Pow(Mathf.Clamp01(1f - r / radius), falloffPow);
                px[y * size + x] = new Color32(col.r, col.g, col.b, (byte)(255f * a));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        return MakeSprite(tex);
    }

    /// <summary>속이 빈 링 (소나 핑용).</summary>
    private Sprite MakeRingSprite(int size, float ringRadius, float halfWidth)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        float c = size * 0.5f - 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(r - ringRadius) / halfWidth);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255f * a));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        return MakeSprite(tex);
    }

    /// <summary>다이아몬드 플레이어 마커 (밝은 코어 + 어두운 외곽선).</summary>
    private Sprite MakeDiamondSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        float c = size * 0.5f - 0.5f;
        float coreR = size * 0.28f;
        float outR = coreR + 2.5f;
        Color32 coreCol = new Color32(255, 246, 228, 255);
        Color32 rimCol = new Color32(20, 22, 28, 255);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Abs(x - c) + Mathf.Abs(y - c); // L1 거리 = 다이아몬드
                float coreA = Mathf.Clamp01((coreR - d) / 1.3f);
                float outA = Mathf.Clamp01((outR - d) / 1.3f);
                byte rr = (byte)Mathf.Lerp(rimCol.r, coreCol.r, coreA);
                byte gg = (byte)Mathf.Lerp(rimCol.g, coreCol.g, coreA);
                byte bb = (byte)Mathf.Lerp(rimCol.b, coreCol.b, coreA);
                px[y * size + x] = new Color32(rr, gg, bb, (byte)(255f * outA));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        return MakeSprite(tex);
    }

    /// <summary>레이더 스윕: 진행 방향 가장자리가 밝고 꼬리가 길게 사라지는 원뿔 그라데이션.</summary>
    private Sprite MakeSweepSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        float c = size * 0.5f - 0.5f;
        float radius = size * 0.5f - 1.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float edgeA = Mathf.Clamp01((radius - r) / 1.5f);
                if (edgeA <= 0f) { px[y * size + x] = new Color32(255, 255, 255, 0); continue; }

                float ang01 = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) % 1f;
                float conic = Mathf.Pow(ang01, 2.4f);              // 꼬리 → 리딩엣지
                float radial = Mathf.Clamp01((r / radius - 0.12f) / 0.5f); // 중앙은 비움
                float a = conic * radial * edgeA;
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255f * a));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        return MakeSprite(tex);
    }

    /// <summary>
    /// 베젤 프레임: 다크 링 + 틱마크(30°마다, 4방위는 앰버) + 안쪽 앰버 액센트 링
    /// + 바깥 림 하이라이트 + 드롭섀도. 반지름 214px가 맵 원 가장자리와 일치.
    /// </summary>
    private Sprite MakeFrameSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        float c = size * 0.5f - 0.5f;

        // 원형은 유지하되, 좌표를 블록 단위로 양자화해 '자세히 보면 픽셀'인 도트 베젤을 만든다.
        // blk = 한 픽셀 블록의 텍셀 크기. framePixelResolution이 작을수록 블록이 커져 픽셀이 굵어진다.
        int blk = Mathf.Max(1, Mathf.RoundToInt(size / (float)Mathf.Max(8, framePixelResolution)));

        const float mapEdge = 214f;
        const float bezelOut = 240f;
        const float shadowOut = 254f;

        Color bezelIn = new Color(0.10f, 0.11f, 0.145f);
        Color bezelOutCol = new Color(0.145f, 0.16f, 0.20f);
        Color rimCol = new Color(0.37f, 0.41f, 0.49f);
        Color tickCol = new Color(0.55f, 0.59f, 0.66f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 블록 중심 좌표로 계산 → 같은 블록 안 텍셀은 한 색을 공유(도트).
                float bx = (Mathf.Floor(x / (float)blk) + 0.5f) * blk;
                float by = (Mathf.Floor(y / (float)blk) + 0.5f) * blk;
                float dx = bx - c, dy = by - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);

                float outA = 0f;
                Color outC = Color.black;

                // 드롭섀도 (베젤 바깥)
                if (r > bezelOut - 1f && r < shadowOut)
                {
                    float t = Mathf.Clamp01((r - bezelOut) / (shadowOut - bezelOut));
                    outA = 0.38f * Mathf.Pow(1f - t, 1.7f);
                    outC = Color.black;
                }

                // 베젤 본체
                float bezelA = Mathf.Clamp01((r - (mapEdge - 0.8f)) / 1.4f)
                             * Mathf.Clamp01((bezelOut + 0.8f - r) / 1.4f);
                if (bezelA > 0f)
                {
                    float t = Mathf.Clamp01((r - mapEdge) / (bezelOut - mapEdge));
                    // 위쪽이 살짝 밝은 톱라이트
                    float topLight = 0.92f + 0.16f * (by / (float)size);
                    Color col = Color.Lerp(bezelIn, bezelOutCol, t) * topLight;
                    col.a = 1f;

                    // 틱마크: 30°마다, 4방위(0/90/180/270°)는 길고 앰버
                    float angDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float d30 = Mathf.Abs(Mathf.DeltaAngle(angDeg, Mathf.Round(angDeg / 30f) * 30f));
                    float d90 = Mathf.Abs(Mathf.DeltaAngle(angDeg, Mathf.Round(angDeg / 90f) * 90f));
                    float arc30 = d30 * Mathf.Deg2Rad * r;
                    float arc90 = d90 * Mathf.Deg2Rad * r;
                    if (arc90 < 2.2f && r > 219f && r < 237f)
                        col = Color.Lerp(col, AccentAmber, 0.65f * Mathf.Clamp01(2.2f - arc90));
                    else if (arc30 < 1.5f && r > 224f && r < 236f)
                        col = Color.Lerp(col, tickCol, 0.5f * Mathf.Clamp01(1.5f - arc30));

                    outC = Color.Lerp(outC, col, bezelA);
                    outA = Mathf.Max(outA, bezelA);
                }

                // 안쪽 앰버 액센트 링 (맵과 베젤의 경계)
                float accentA = Mathf.Clamp01(1.6f - Mathf.Abs(r - (mapEdge + 1.2f)) / 1.6f) * 0.55f;
                if (accentA > 0f)
                {
                    outC = Color.Lerp(outC, AccentAmber, accentA);
                    outA = Mathf.Max(outA, accentA);
                }

                // 바깥 림 하이라이트
                float rimA = Mathf.Clamp01(1.5f - Mathf.Abs(r - 239f) / 1.5f) * 0.8f;
                if (rimA > 0f)
                {
                    outC = Color.Lerp(outC, rimCol, rimA);
                    outA = Mathf.Max(outA, rimA);
                }

                px[y * size + x] = new Color32(
                    (byte)(255f * Mathf.Clamp01(outC.r)),
                    (byte)(255f * Mathf.Clamp01(outC.g)),
                    (byte)(255f * Mathf.Clamp01(outC.b)),
                    (byte)(255f * Mathf.Clamp01(outA)));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return MakeSprite(tex, pointFilter: true); // 블록 경계가 뭉개지지 않게 Point
    }

    /// <summary>깊이 배지용 캡슐 (다크 필 + 얇은 보더).</summary>
    private Sprite MakePillSprite(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        float cx = w * 0.5f - 0.5f, cy = h * 0.5f - 0.5f;
        float rad = h * 0.5f - 1f;
        float halfLen = w * 0.5f - rad - 1f;
        Color32 fill = new Color32(13, 15, 20, 235);
        Color32 border = new Color32(120, 130, 150, 255);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float ddx = Mathf.Max(Mathf.Abs(x - cx) - halfLen, 0f);
                float dist = Mathf.Sqrt(ddx * ddx + (y - cy) * (y - cy));
                float fillA = Mathf.Clamp01((rad - dist) / 1.2f);
                float borderA = Mathf.Clamp01((rad + 0.6f - dist) / 1.2f)
                              * Mathf.Clamp01((dist - (rad - 1.8f)) / 1.2f) * 0.6f;
                byte rr = (byte)Mathf.Lerp(fill.r, border.r, borderA);
                byte gg = (byte)Mathf.Lerp(fill.g, border.g, borderA);
                byte bb = (byte)Mathf.Lerp(fill.b, border.b, borderA);
                float a = Mathf.Max(fillA * (235f / 255f), borderA);
                px[y * w + x] = new Color32(rr, gg, bb, (byte)(255f * Mathf.Clamp01(a)));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        return MakeSprite(tex);
    }
}
