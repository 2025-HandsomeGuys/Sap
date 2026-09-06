// @tags: worldmap, map, ui, overlay, fullscreen, pan, zoom, minimap, explore, fog
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 전체 지도 오버레이 — M 키로 여는 대형 맵.
/// 미니맵(UndergroundMinimap)이 플레이어 주변을 원형 계기판으로 보여준다면,
/// 이 오버레이는 탐사한 지역 전체를 화면 대부분을 채우는 사각 지도로 보여준다.
///  · 마우스 휠 : 커서 기준 확대/축소
///  · 마우스 드래그 : 맵 이동
///  · M / ESC : 닫기
///
/// UI는 전부 코드로 생성(DaySummaryUI·SettingsOverlayUI 패턴) — 씬/프리팹 세팅 불필요.
/// 열림/닫힘·플레이어 입력 차단은 UIStateManager(UIState.WorldMap)가 함께 관리한다:
///   여는 쪽  → UIStateManager.SetState(UIState.WorldMap) → WorldMapOverlay.Open()
///   닫는 쪽  → UIStateManager.SetState(UIState.None)     → WorldMapOverlay.Close()
///
/// 지형 표시는 미니맵과 동일하게 실제 청크 픽셀을 샘플링한다(파인 곳=앰버, 흙=암석 톤).
/// DigPathTracker의 탐사 기록을 안개 마스크로 쓰며, 로드되지 않은 청크라도
/// '탐사 완료' 셀이면 통로로 그려 멀리 파온 길이 지도에 남는다.
/// </summary>
public class WorldMapOverlay : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static WorldMapOverlay _instance;

    public static WorldMapOverlay Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<WorldMapOverlay>();
                if (_instance == null)
                {
                    var go = new GameObject("WorldMapOverlay");
                    _instance = go.AddComponent<WorldMapOverlay>();
                }
            }
            return _instance;
        }
    }

    /// <summary>전체 지도가 현재 열려 있는지 (UIStateManager 전역 단축키 차단용).</summary>
    public static bool IsOpen => _instance != null && _instance._isOpen;

    /// <summary>닫힌 바로 그 프레임인지 — 같은 프레임 중복 처리 방지.</summary>
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
    [Header("레이아웃 (1920x1080 기준 px)")]
    [Tooltip("지도 패널 가로 크기")]
    [SerializeField] private float panelWidth = 1640f;
    [Tooltip("지도 패널 세로 크기")]
    [SerializeField] private float panelHeight = 900f;
    [Tooltip("배경을 덮는 검은 막의 알파")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.72f;
    [Tooltip("캔버스 정렬 순서 — 게임 UI보다 위, 설정 오버레이(31000)보다 아래")]
    [SerializeField] private int sortingOrder = 30500;

    [Header("줌 (가로로 보이는 월드 유닛 폭)")]
    [Tooltip("열 때 기본 가로 폭 (청크 1칸 = 10유닛)")]
    [SerializeField] private float defaultWorldWidth = 220f;
    [SerializeField] private float minWorldWidth = 50f;
    [SerializeField] private float maxWorldWidth = 900f;
    [Tooltip("휠 한 칸당 배율 (작을수록 급격)")]
    [Range(0.5f, 0.95f)][SerializeField] private float zoomStep = 0.82f;

    [Header("표시")]
    [Tooltip("지표면의 월드 Y (위쪽은 하늘 톤). 지하 지형이 기준보다 +10 올라와 있어 실제 지표는 y=10 → 여기서 0m")]
    [SerializeField] private float surfaceWorldY = 10f;
    [Tooltip("효과음 이름 (SoundDataSO 등록, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;

    [Header("시야 원형 페이드 (검은 안개)")]
    [Tooltip("보이는 영역 중심 기준 원형 비네트 — 코너로 갈수록 어두워져 사각 경계가 원형으로 보인다")]
    [SerializeField] private bool enableVisionFade = true;
    [Range(0f, 1.5f)][Tooltip("이 반경(패널 반높이 대비)까지는 완전히 선명")]
    [SerializeField] private float visionFullFrac = 0.55f;
    [Range(0.2f, 2.5f)][Tooltip("이 반경에서 바닥값에 도달")]
    [SerializeField] private float visionFadeFrac = 1.35f;
    [Range(0f, 1f)][Tooltip("코너 최소 가시도 — 전체지도라 0보다 크게 둬 탐사 전체가 보이게")]
    [SerializeField] private float visionFloor = 0.32f;

    [Tooltip("지형 소프트 글로우 블러 반경(텍셀, 0=끔/선명). 전체지도는 확대 렌더라 블러가 크게 보여 기본 0")]
    [Range(0, 4)][SerializeField] private int contentBlur = 0;

    [Header("마커")]
    [Tooltip("엘베·청크입구 마커 아이콘 크기 (UI 픽셀, 기준 줌에서의 크기)")]
    [SerializeField] private float markerSize = 30f;
    [Tooltip("확대하면 마커도 같이 커진다(축소하면 작아진다). 끄면 항상 고정 크기")]
    [SerializeField] private bool markerScalesWithZoom = true;
    [Tooltip("줌 배율 하한 — 축소해도 마커가 이보다 작아지지 않는다(안 보일 만큼 작아지는 것 방지)")]
    [Range(0.2f, 1f)][SerializeField] private float markerMinZoomScale = 0.7f;
    [Tooltip("줌 배율 상한 — 확대해도 마커가 이보다 커지지 않는다(맵을 뒤덮는 것 방지)")]
    [Range(1f, 6f)][SerializeField] private float markerMaxZoomScale = 3f;

    [Header("스프라이트 (비우면 코드 생성 아이콘 사용)")]
    [Tooltip("플레이어 위치 마커 스프라이트")]
    [SerializeField] private Sprite playerMarkerSprite;
    [Tooltip("엘리베이터 마커 스프라이트")]
    [SerializeField] private Sprite elevatorMarkerSprite;
    [Tooltip("청크 입구 마커 스프라이트")]
    [SerializeField] private Sprite entranceMarkerSprite;
    [Tooltip("이미 탐험해 재입장 불가한 청크 입구 마커 스프라이트 (비우면 미니맵 값 → 코드 생성 회색)")]
    [SerializeField] private Sprite entranceUsedMarkerSprite;
    [Tooltip("지도 패널 테두리(프레임) 스프라이트. 9-슬라이스 권장. 비우면 코드 생성 라운드 프레임")]
    [SerializeField] private Sprite panelFrameSprite;
    [Tooltip("버튼(닫기·내 위치로) 스프라이트. 9-슬라이스 권장. 비우면 코드 생성 라운드")]
    [SerializeField] private Sprite buttonSprite;

    // ===================================================
    // 팔레트 (미니맵과 통일)
    // ===================================================
    private static readonly Color32 BgCol      = new Color32(14, 17, 23, 255);
    private static readonly Color32 BgEdgeCol   = new Color32(8, 10, 14, 255);
    private static readonly Color32 TunnelCol   = new Color32(214, 178, 120, 255); // 파인 빈 공간
    private static readonly Color32 RockCol     = new Color32(84, 71, 58, 255);     // 탐사됐지만 안 판 흙
    // 청크 그리드 색은 뺐다 — 미니맵과 같은 이유(각진 직선이 원형 룩과 충돌). UndergroundMinimap.RenderMap 주석 참고.
    private static readonly Color32 SkyCol      = new Color32(86, 108, 142, 255);
    private static readonly Color32 SurfaceCol  = new Color32(150, 214, 170, 255);
    private static readonly Color   PanelFrame  = new Color(0.16f, 0.18f, 0.24f, 1f);
    private static readonly Color   AccentAmber = new Color(1f, 0.76f, 0.44f, 1f);
    private static readonly Color   HintCol     = new Color(0.72f, 0.78f, 0.9f, 1f);

    // 마커 기준 크기(줌 배율 1.0에서의 px) — 플레이어·탐지 마커는 자체 기준값을 가진다.
    private const float PlayerMarkerBaseSize = 20f;
    private const float DetectMarkerBaseSize = 16f;

    // 렌더 해상도 — 패널 종횡비에 맞춰 Build에서 texH 계산
    private const int TexW = 512;
    private const float RenderThrottle = 1f / 30f; // 팬/줌 중 재렌더 상한

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built;
    private bool _isOpen;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RectTransform _panelRt;    // 지도 영역(포인터 좌표 기준)
    private RawImage _mapImage;
    private RectTransform _playerMarker;
    private TextMeshProUGUI _coordText;

    // 엘베·청크입구 마커 아이콘 풀 (플레이어 마커와 동일하게 _panelRt 직접 자식으로 생성)
    private readonly List<Image> _markerPool = new List<Image>();

    // 미니맵 스프라이트 공유 — 인스펙터 지정은 미니맵 한 곳에서만 하면 전체지도도 따라 쓴다.
    private UndergroundMinimap _minimap;
    private Sprite MinimapPlayerSprite       => _minimap != null ? _minimap.PlayerMarkerSprite       : null;
    private Sprite MinimapElevatorSprite     => _minimap != null ? _minimap.ElevatorMarkerSprite     : null;
    private Sprite MinimapEntranceSprite     => _minimap != null ? _minimap.EntranceMarkerSprite     : null;
    private Sprite MinimapEntranceUsedSprite => _minimap != null ? _minimap.EntranceUsedMarkerSprite : null;

    // 원형 시야 파라미터 (RenderMap에서 세팅, VisionAtTexel/PaintRock*이 사용)
    private bool _visOn;
    private float _visCx, _visCy, _visFull, _visFade, _visFull2, _visFade2;

    private Texture2D _mapTex;
    private int _texH;
    private Color32[] _pixels;
    private Color32[] _blurTmp;     // 소프트 글로우 블러 임시 버퍼 (재사용)
    private bool[] _exploredGrid;
    // _exploredGrid의 좌표계 (RenderMap에서 세팅) — PaintRockEntry가 탐사도를 되짚어 볼 때 사용
    private float _expCellSize = 3f;
    private int _expCellMinX, _expCellMinY, _expGridW, _expGridH;

    private Vector2 _center;       // 지도 중심 월드 좌표
    private float _worldWidth;     // 가로로 보이는 월드 폭 (줌)
    private bool _viewDirty;
    private bool _forceRender;     // 이번 프레임 스로틀 무시하고 즉시 렌더 (줌)
    private float _renderTimer;

    // 마지막 RenderMap 시점의 중심·폭 — 재렌더 사이에 UV를 밀어 지형을 부드럽게 스크롤하는 기준.
    private Vector2 _renderCenter;
    private float _renderWorldWidth;

    private bool _dragging;
    private Vector2 _lastDragLocal;

    private Transform _playerTr;
    private float _playerSearchTimer;

    private readonly List<UnityEngine.Object> _generated = new List<UnityEngine.Object>();

    private readonly List<RectTransform> _detectMarkers = new List<RectTransform>();
    private Sprite _detectSprite;

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    /// <summary>
    /// 전체 지도를 연다. (UIStateManager.SetState(WorldMap)가 호출)
    /// 지도 장비를 사기 전에는 열리지 않는다 — M 키 쪽에서 이미 막지만,
    /// 다른 경로로 SetState(WorldMap)가 들어와도 빈 화면이 뜨지 않게 여기서도 막는다.
    /// </summary>
    public static void Open()
    {
        if (IsOpen) return;
        if (!MapUnlockGate.IsUnlocked) return;
        Instance.OpenInternal();
    }

    /// <summary>전체 지도를 닫는다. (UIStateManager.SetState(None)에서 호출)</summary>
    public static void CloseStatic()
    {
        if (_instance != null) _instance.Close();
    }

    private void OpenInternal()
    {
        // 미니맵 스프라이트 공유용 참조 확보 (EnsureBuilt의 플레이어 마커 생성보다 먼저).
        if (_minimap == null) _minimap = FindFirstObjectByType<UndergroundMinimap>();

        EnsureBuilt();
        EnsureEventSystem();

        EnsurePlayer();
        _center = _playerTr != null ? (Vector2)_playerTr.position : Vector2.zero;
        _worldWidth = Mathf.Clamp(defaultWorldWidth, minWorldWidth, maxWorldWidth);
        _viewDirty = true;
        _renderTimer = RenderThrottle;
        _dragging = false;

        _prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        _isOpen = true;

        _canvasObj.SetActive(true);
        RefreshFont();
        RenderMap();
        UpdateMarker();
        UpdateMarkers(); // 열자마자 엘베·입구 마커 표시 (Update를 기다리지 않음)
        PlayClick();

        StopAllCoroutines();
        StartCoroutine(FadeIn());
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        _dragging = false;
        Time.timeScale = _prevTimeScale;
        PlayClick();
        if (_canvasObj != null) _canvasObj.SetActive(false);
    }

    /// <summary>닫기 버튼/키에서 호출 — UIStateManager와 상태를 맞춘다.</summary>
    private void RequestClose()
    {
        if (UIStateManager.Instance != null)
            UIStateManager.Instance.SetState(UIState.None); // → CloseStatic()
        else
            Close();
    }

    private IEnumerator FadeIn()
    {
        _canvasGroup.alpha = 0f;
        float t = 0f;
        const float dur = 0.14f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Clamp01(t / dur);
            yield return null;
        }
        _canvasGroup.alpha = 1f;
    }

    private void OnDestroy()
    {
        foreach (var o in _generated)
            if (o != null) Destroy(o);
        _generated.Clear();
    }

    // ===================================================
    // 입력 (휠 확대 / 드래그 이동)
    // ===================================================
    private void Update()
    {
        if (!_isOpen) return;

        EnsurePlayer();
        _forceRender = false;
        HandleZoom();   // 줌이 바뀌면 _forceRender = true
        HandleDrag();

        // 지형 재렌더는 스로틀(줌은 즉시). 재렌더 '사이'에는 UV를 밀어 지형을 부드럽게 스크롤한다
        // → 마커(월드 고정, 매 프레임 배치)와 지형이 항상 같이 움직인다. 무거운 재렌더 없이도 어긋남이 없다.
        _renderTimer += Time.unscaledDeltaTime;
        if (_viewDirty && (_forceRender || _renderTimer >= RenderThrottle))
        {
            _renderTimer = 0f;
            _viewDirty = false;
            RenderMap();
        }
        ApplyPanScroll();
        UpdateMarker();
        UpdateMarkers();
    }

    /// <summary>
    /// 재렌더 사이에 지형 텍스처의 UV를 (_center - _renderCenter)만큼 밀어 부드럽게 스크롤한다.
    /// 지형을 다시 굽지 않고 이미 구운 텍스처를 이동시키는 것 — 마커처럼 월드에 고정된 채로 스크롤된다.
    /// 방금 재렌더한 프레임은 _renderCenter == _center 라 오프셋 0(정중앙). 줌 변화는 즉시 재렌더로 처리하므로
    /// 여기선 이동(translation)만 다룬다. 새로 드러난 가장자리는 다음 스로틀 재렌더가 채운다.
    /// </summary>
    private void ApplyPanScroll()
    {
        if (_mapImage == null || _renderWorldWidth <= 0f) return;
        float vertWorld = _renderWorldWidth * (_texH / (float)TexW);
        float ux = (_center.x - _renderCenter.x) / _renderWorldWidth;
        float uy = (_center.y - _renderCenter.y) / Mathf.Max(0.01f, vertWorld);
        _mapImage.uvRect = new Rect(ux, uy, 1f, 1f);
    }

    /// <summary>
    /// 현재 줌에 따른 마커 크기 배율. 기준 폭(defaultWorldWidth)에서 1.0이고,
    /// 확대(=_worldWidth 감소)하면 커지고 축소하면 작아진다. 상·하한으로 극단값을 막는다.
    /// </summary>
    private float MarkerZoomScale()
    {
        if (!markerScalesWithZoom) return 1f;
        float refW = Mathf.Max(1f, defaultWorldWidth);
        return Mathf.Clamp(refW / Mathf.Max(0.01f, _worldWidth), markerMinZoomScale, markerMaxZoomScale);
    }

    /// <summary>발견된 엘베·청크입구 마커를 지도 위 위치에 배치한다(패널 밖은 가장자리에 클램프).</summary>
    private void UpdateMarkers()
    {
        if (_panelRt == null) return;
        var markers = MapMarkerRegistry.Markers;

        float scale = panelWidth / _worldWidth; // 월드 유닛당 로컬 px
        float ms = markerSize * MarkerZoomScale();  // 줌에 따라 커지는 실제 크기
        float halfW = panelWidth * 0.5f - ms * 0.4f;
        float halfH = panelHeight * 0.5f - ms * 0.4f;

        int used = 0;
        for (int m = 0; m < markers.Count; m++)
        {
            var mk = markers[m];
            float lx = (mk.world.x - _center.x) * scale;
            float ly = (mk.world.y - _center.y) * scale;
            bool outside = lx < -halfW || lx > halfW || ly < -halfH || ly > halfH;
            lx = Mathf.Clamp(lx, -halfW, halfW);
            ly = Mathf.Clamp(ly, -halfH, halfH);

            Image img = GetMarkerImage(used++);
            img.sprite = SpriteForMarker(mk);
            img.rectTransform.sizeDelta = new Vector2(ms, ms); // 줌 배율을 매 프레임 반영
            img.rectTransform.anchoredPosition = new Vector2(lx, ly);
            img.rectTransform.localScale = Vector3.one * (outside ? 0.72f : 1f);
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
        }

        for (int k = used; k < _markerPool.Count; k++)
            if (_markerPool[k].gameObject.activeSelf) _markerPool[k].gameObject.SetActive(false);

        // 플레이어 마커를 항상 맨 위로 (동적 생성된 POI 마커가 덮지 않도록). 이미 마지막이면 no-op.
        if (_playerMarker != null) _playerMarker.SetAsLastSibling();
    }

    /// <summary>인스펙터 스프라이트가 지정돼 있으면 그것을, 없으면 코드 생성 아이콘을 쓴다.</summary>
    private Sprite SpriteForMarker(MapMarkerRegistry.Marker mk)
    {
        // 우선순위: 전체지도 인스펙터 지정 → 미니맵 인스펙터 지정 → 코드 생성 아이콘.
        if (mk.kind == MapMarkerKind.Elevator)
            return elevatorMarkerSprite != null ? elevatorMarkerSprite
                 : MinimapElevatorSprite != null ? MinimapElevatorSprite
                 : MapMarkerVisuals.Elevator;
        // 청크 입구 — 이미 탐험해 재입장 불가한 입구는 별도 스프라이트로 구분.
        if (MapMarkerRegistry.IsEntranceUsed(mk.world))
            return entranceUsedMarkerSprite != null ? entranceUsedMarkerSprite
                 : MinimapEntranceUsedSprite != null ? MinimapEntranceUsedSprite
                 : MapMarkerVisuals.EntranceUsed;
        return entranceMarkerSprite != null ? entranceMarkerSprite
             : MinimapEntranceSprite != null ? MinimapEntranceSprite
             : MapMarkerVisuals.Entrance;
    }

    private Image GetMarkerImage(int index)
    {
        while (index >= _markerPool.Count)
        {
            // 플레이어 마커와 동일하게 _panelRt 직접 자식으로 생성 — 안전하게 렌더되도록.
            var obj = new GameObject("Marker", typeof(RectTransform));
            obj.transform.SetParent(_panelRt, false);
            var img = obj.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = Color.white;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(markerSize, markerSize);
            _markerPool.Add(img);
        }
        return _markerPool[index];
    }

    private bool PointerInPanel(out Vector2 local)
    {
        local = Vector2.zero;
        if (_panelRt == null) return false;
        Vector2 mouse = Input.mousePosition;
        if (!RectTransformUtility.RectangleContainsScreenPoint(_panelRt, mouse, null)) return false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_panelRt, mouse, null, out local);
        return true;
    }

    private void HandleZoom()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.01f) return;
        if (!PointerInPanel(out Vector2 local)) return;

        Vector2 before = LocalToWorld(local);
        float factor = scroll > 0f ? zoomStep : 1f / zoomStep;
        _worldWidth = Mathf.Clamp(_worldWidth * factor, minWorldWidth, maxWorldWidth);
        Vector2 after = LocalToWorld(local);
        _center += before - after; // 커서 아래 월드 좌표 고정
        _viewDirty = true;
        _forceRender = true; // 휠 즉시 반영 (스로틀 대기 없이)
    }

    private void HandleDrag()
    {
        if (Input.GetMouseButtonDown(0) && PointerInPanel(out Vector2 down))
        {
            _dragging = true;
            _lastDragLocal = down;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            _dragging = false;
        }

        if (!_dragging) return;
        if (!Input.GetMouseButton(0)) { _dragging = false; return; }

        // 드래그 중엔 패널 밖으로 나가도 델타를 계속 추적
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_panelRt, Input.mousePosition, null, out Vector2 cur);
        Vector2 delta = cur - _lastDragLocal;
        _lastDragLocal = cur;
        if (delta.sqrMagnitude < 0.0001f) return;

        float worldPerLocal = _worldWidth / panelWidth;
        _center -= delta * worldPerLocal; // 잡은 지점이 손끝을 따라오도록
        _viewDirty = true;
    }

    /// <summary>패널 로컬 좌표(피벗 중앙, ±panel/2) → 월드 좌표.</summary>
    private Vector2 LocalToWorld(Vector2 local)
    {
        float s = _worldWidth / panelWidth;
        return _center + new Vector2(local.x * s, local.y * s);
    }

    // ===================================================
    // 지도 텍스처 렌더링
    // ===================================================
    private void RenderMap()
    {
        if (_mapTex == null) return;

        float wpt = _worldWidth / TexW;         // 텍셀당 월드 유닛 (가로·세로 동일)
        float cx = TexW * 0.5f;
        float cy = _texH * 0.5f;

        float halfW = cx * wpt;
        float halfH = cy * wpt;
        float minWx = _center.x - halfW, maxWx = _center.x + halfW;
        float minWy = _center.y - halfH, maxWy = _center.y + halfH;

        // ── 배경 (세로 그라데이션) ──
        for (int py = 0; py < _texH; py++)
        {
            float vt = py / (float)Mathf.Max(1, _texH - 1);
            byte r = (byte)Mathf.Lerp(BgEdgeCol.r, BgCol.r, vt);
            byte g = (byte)Mathf.Lerp(BgEdgeCol.g, BgCol.g, vt);
            byte b = (byte)Mathf.Lerp(BgEdgeCol.b, BgCol.b, vt);
            var bg = new Color32(r, g, b, 255);
            int row = py * TexW;
            for (int px = 0; px < TexW; px++) _pixels[row + px] = bg;
        }

        // ── 지상(하늘) 틴트 ──
        int surfacePy = Mathf.RoundToInt(cy + (surfaceWorldY - _center.y) / wpt - 0.5f);
        for (int py = Mathf.Max(0, surfacePy + 1); py < _texH; py++)
            BlendRow(py, SkyCol, 0.12f);

        // ── 청크 그리드 — 각진 직선이 부드러운 룩과 충돌해 비활성화. (원하면 이 블록을 되살릴 것) ──

        // ── 탐사 셀 스냅샷 (텍셀마다 HashSet 조회 회피) ──
        var tracker = DigPathTracker.Instance;
        float cellSize = tracker != null ? Mathf.Max(0.01f, tracker.cellSize) : 3f;
        int cellMinX = FastFloor(minWx / cellSize) - 1;
        int cellMinY = FastFloor(minWy / cellSize) - 1;
        int cellMaxX = FastFloor(maxWx / cellSize) + 1;
        int cellMaxY = FastFloor(maxWy / cellSize) + 1;
        int gridW = cellMaxX - cellMinX + 1;
        int gridH = cellMaxY - cellMinY + 1;
        if (gridW < 1) gridW = 1;
        if (gridH < 1) gridH = 1;
        if (_exploredGrid == null || _exploredGrid.Length < gridW * gridH)
            _exploredGrid = new bool[gridW * gridH];
        if (tracker != null)
        {
            for (int gy = 0; gy < gridH; gy++)
                for (int gx = 0; gx < gridW; gx++)
                    _exploredGrid[gy * gridW + gx] =
                        tracker.IsExplored(new Vector2Int(cellMinX + gx, cellMinY + gy));
        }
        else
        {
            System.Array.Clear(_exploredGrid, 0, gridW * gridH); // 트래커 없으면 전부 미탐사
        }
        _expCellSize = cellSize;
        _expCellMinX = cellMinX;
        _expCellMinY = cellMinY;
        _expGridW = gridW;
        _expGridH = gridH;

        // ── 지형 스냅샷 캐시 갱신 (보이는 로드된 청크) ──
        // 로드된 청크는 매번 최신 상태로 굽고, 언로드된 곳은 예전에 구워둔 캐시를 쓴다.
        // → 로드/언로드 구분 없이 캐시 하나로 그리므로 가까운 곳(아래)과 먼 곳(위)이 똑같이 또렷하다.
        var imm = InfinityMapManager.Instance;
        float cw = imm != null ? imm.chunkWidthWorld : 10f;
        float chh = imm != null ? imm.chunkHeightWorld : 10f;
        if (imm != null)
        {
            int ccxMin = FastFloor(minWx / cw), ccxMax = FastFloor(maxWx / cw);
            int ccyMin = FastFloor(minWy / chh), ccyMax = FastFloor(maxWy / chh);
            for (int ccy = ccyMin; ccy <= ccyMax; ccy++)
                for (int ccx = ccxMin; ccx <= ccxMax; ccx++)
                {
                    var coord = new Vector2Int(ccx, ccy);
                    var ch = imm.GetChunk(coord);
                    if (ch != null && ch.baseData.IsCreated)
                        MapTerrainCache.Capture(coord, ch, force: true); // 로드된 건 최신으로
                }
        }

        // 보이는 영역 중심(텍스처 중심) 기준 원형 비네트 — 코너로 갈수록 어두워져 사각 경계가 원형으로 보인다.
        // 팬/줌과 무관하게 항상 화면 중앙이 밝고 가장자리가 원형으로 어두워진다. (VisionAtTexel이 사용)
        _visOn = enableVisionFade;
        _visCx = cx; _visCy = cy;
        float visHalf = Mathf.Max(1f, _texH * 0.5f);         // 정규화 기준: 패널 반높이(텍셀)
        _visFull = visionFullFrac * visHalf;
        _visFade = Mathf.Max(_visFull + 0.01f, visionFadeFrac * visHalf);
        _visFull2 = _visFull * _visFull; _visFade2 = _visFade * _visFade;

        for (int py = 0; py < _texH; py++)
        {
            float wy = _center.y + (py + 0.5f - cy) * wpt;
            if (wy > surfaceWorldY) continue; // 위쪽은 전부 하늘 톤 유지

            int row = py * TexW;
            float cellFy = wy / cellSize - 0.5f;
            int cyc = FastFloor(cellFy);
            float fy = cellFy - cyc;
            int gy0 = cyc - cellMinY;

            for (int px = 0; px < TexW; px++)
            {
                // 어둠은 오직 보이는 영역 중심 원형 비네트로만. 시야 밖이면 그리지 않아 각진 셀 경계가 사라진다.
                float vis = VisionAtTexel(px + 0.5f, py + 0.5f);
                if (vis <= 0.002f) continue;

                float wx = _center.x + (px + 0.5f - cx) * wpt;

                float cellFx = wx / cellSize - 0.5f;
                int cxc = FastFloor(cellFx);
                float fx = cellFx - cxc;
                int gx0 = cxc - cellMinX;

                // 탐사도 바이리니어 보간 → 파진 땅을 부드럽게 따라가는 앰버 글로우
                float e = Mathf.Lerp(
                    Mathf.Lerp(SampleExplored(gx0, gy0, gridW, gridH), SampleExplored(gx0 + 1, gy0, gridW, gridH), fx),
                    Mathf.Lerp(SampleExplored(gx0, gy0 + 1, gridW, gridH), SampleExplored(gx0 + 1, gy0 + 1, gridW, gridH), fx),
                    fy);

                // 지형 empty를 텍셀 내부 4점 평균으로 안티에일리어싱 → 앰버 경계가 딱딱하게 잘리지 않고
                // 부드럽게 페이드. 캐시에 없으면 통로로 가정하되, 미탐사면 어차피 암석 톤으로 은닉된다.
                float emptiness = SampleCacheEmptiness(wx, wy, cw, chh, wpt * 0.4f);

                // 시야 영역은 지형을 빠짐없이 채운다: 탐사한 빈 공간(판 굴)만 앰버, 그 외는 은은한 암석 톤.
                // 검은 빈틈(각진 셀 경계)이 사라지고 어둠은 원형 비네트에서만 생긴다.
                float tun = emptiness * (e * e * (3f - 2f * e));
                Color32 col = new Color32(
                    (byte)(RockCol.r + (TunnelCol.r - RockCol.r) * tun),
                    (byte)(RockCol.g + (TunnelCol.g - RockCol.g) * tun),
                    (byte)(RockCol.b + (TunnelCol.b - RockCol.b) * tun),
                    255);
                float a = vis * (0.5f + 0.5f * tun);
                if (a <= 0.001f) continue;

                int i = row + px;
                Color32 ep = _pixels[i];
                ep.r = (byte)(ep.r + (col.r - ep.r) * a);
                ep.g = (byte)(ep.g + (col.g - ep.g) * a);
                ep.b = (byte)(ep.b + (col.b - ep.b) * a);
                _pixels[i] = ep;
            }
        }

        // ── 지형 소프트 글로우 ── 밝은 지형 경계가 딱딱하게 잘리지 않고 부드럽게 번지도록 블러 (돌·마커는 뒤에 또렷).
        BlurContentRGB(contentBlur);

        // ── 안 캔 돌 채움 ── MapRockCache 스냅샷을 '안 파진 땅'처럼 실제 돌 모양대로. 언로드돼도 유지, 캐지면 소멸.
        foreach (var e in MapRockCache.Entries)
            PaintRockEntry(e, cx, cy, wpt);

        // ── 지표면 라인 ──
        if (surfacePy >= 0 && surfacePy < _texH)
            DrawHLine(surfacePy, SurfaceCol, 0.5f);

        _mapTex.SetPixels32(_pixels);
        _mapTex.Apply(false);

        // 이 텍스처가 대표하는 중심·폭을 기록하고 UV를 정중앙으로 리셋. 이후 프레임의 ApplyPanScroll이
        // 여기서부터의 이동량만큼 UV를 밀어 스크롤한다.
        _renderCenter = _center;
        _renderWorldWidth = _worldWidth;
        if (_mapImage != null) _mapImage.uvRect = new Rect(0f, 0f, 1f, 1f);
    }

    /// <summary>보이는 영역 중심 기준 원형 시야값(0~1). tx,ty = 텍셀 좌표.</summary>
    private float VisionAtTexel(float tx, float ty)
    {
        if (!_visOn) return 1f;
        float dx = tx - _visCx, dy = ty - _visCy;
        float d2 = dx * dx + dy * dy;
        if (d2 >= _visFade2) return visionFloor;
        if (d2 <= _visFull2) return 1f;
        float t = (Mathf.Sqrt(d2) - _visFull) / (_visFade - _visFull);
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(1f, visionFloor, t);
    }

    /// <summary>_pixels의 RGB를 분리형 박스 블러로 부드럽게. 소프트 글로우용(알파 유지).</summary>
    private void BlurContentRGB(int radius)
    {
        if (radius < 1) return;
        if (_blurTmp == null || _blurTmp.Length != _pixels.Length)
            _blurTmp = new Color32[_pixels.Length];

        // 가로 패스: _pixels → _blurTmp
        for (int y = 0; y < _texH; y++)
        {
            int row = y * TexW;
            for (int x = 0; x < TexW; x++)
            {
                int r = 0, g = 0, b = 0, cnt = 0;
                int kmin = x - radius < 0 ? -x : -radius;
                int kmax = x + radius >= TexW ? TexW - 1 - x : radius;
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
        for (int y = 0; y < _texH; y++)
        {
            int row = y * TexW;
            for (int x = 0; x < TexW; x++)
            {
                int r = 0, g = 0, b = 0, cnt = 0;
                int kmin = y - radius < 0 ? -y : -radius;
                int kmax = y + radius >= _texH ? _texH - 1 - y : radius;
                for (int k = kmin; k <= kmax; k++)
                {
                    Color32 p = _blurTmp[(y + k) * TexW + x];
                    r += p.r; g += p.g; b += p.b; cnt++;
                }
                Color32 o = _blurTmp[row + x];
                _pixels[row + x] = new Color32((byte)(r / cnt), (byte)(g / cnt), (byte)(b / cnt), o.a);
            }
        }
    }

    /// <summary>텍셀 footprint 안 4점의 빈 공간 비율(0~1). 앰버 경계 안티에일리어싱용.</summary>
    private float SampleCacheEmptiness(float wx, float wy, float cw, float chh, float half)
    {
        return (CacheEmptyAt(wx - half, wy - half, cw, chh)
              + CacheEmptyAt(wx + half, wy - half, cw, chh)
              + CacheEmptyAt(wx - half, wy + half, cw, chh)
              + CacheEmptyAt(wx + half, wy + half, cw, chh)) * 0.25f;
    }

    private float CacheEmptyAt(float wx, float wy, float cw, float chh)
    {
        int ccx = FastFloor(wx / cw), ccy = FastFloor(wy / chh);
        float fcx = wx / cw - ccx, fcy = wy / chh - ccy;
        // 캐시에 데이터 있으면 그 값, 없으면(한 번도 로드 안 됨) 흙(막힘)으로 취급 → 미지 영역 암석 톤 은닉.
        // 통로로 가정하면 밝혀진 경계 바깥으로 미지의 흙이 앰버처럼 새므로 흙(0)으로 막는다.
        if (MapTerrainCache.TrySampleEmpty(new Vector2Int(ccx, ccy), fcx, fcy, out bool cempty))
            return cempty ? 1f : 0f;
        return 0f;
    }

    /// <summary>돌 스냅샷(MapRockCache.Entry)을 실루엣대로 채운다(시야 반영).</summary>
    private void PaintRockEntry(MapRockCache.Entry e, float cx, float cy, float wpt)
    {
        Color32 col = e.isMineral ? MapMarkerVisuals.MineralRockFill : MapMarkerVisuals.RockFill;
        float xMin = e.min.x, yMin = e.min.y, xMax = e.min.x + e.size.x, yMax = e.min.y + e.size.y;
        float txMin = (xMin - _center.x) / wpt + cx - 0.5f;
        float txMax = (xMax - _center.x) / wpt + cx - 0.5f;
        float tyMin = (yMin - _center.y) / wpt + cy - 0.5f;
        float tyMax = (yMax - _center.y) / wpt + cy - 0.5f;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(txMin));
        int x1 = Mathf.Min(TexW - 1, Mathf.CeilToInt(txMax));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(tyMin));
        int y1 = Mathf.Min(_texH - 1, Mathf.CeilToInt(tyMax));
        if (x0 > x1 || y0 > y1) return; // 화면 밖 → 스킵 (캐시가 커도 저렴)

        for (int y = y0; y <= y1; y++)
        {
            int rowo = y * TexW;
            float wy = _center.y + (y + 0.5f - cy) * wpt;
            for (int x = x0; x <= x1; x++)
            {
                float wx = _center.x + (x + 0.5f - cx) * wpt;
                if (!MapRockCache.Sample(e, wx, wy)) continue;
                // 탐사한 곳의 돌만 그린다 — 캐시에는 '노출 판정을 통과한 돌' 전부가 들어있어
                // (자연 공동에 묻힌 돌은 청크 로드만으로 통과) 거르지 않으면 안 판 땅이 미리 드러난다.
                float a = 0.95f * VisionAtTexel(x + 0.5f, y + 0.5f) * ExploredAt(wx, wy);
                if (a <= 0.003f) continue;
                int i = rowo + x;
                Color32 ep = _pixels[i];
                ep.r = (byte)(ep.r + (col.r - ep.r) * a);
                ep.g = (byte)(ep.g + (col.g - ep.g) * a);
                ep.b = (byte)(ep.b + (col.b - ep.b) * a);
                _pixels[i] = ep;
            }
        }
    }

    private float SampleExplored(int gx, int gy, int gridW, int gridH)
        => (uint)gx < (uint)gridW && (uint)gy < (uint)gridH && _exploredGrid[gy * gridW + gx] ? 1f : 0f;

    /// <summary>월드 좌표의 탐사도(0~1). 지형 루프의 e와 같은 셀 바이리니어 + 스무스스텝.</summary>
    private float ExploredAt(float wx, float wy)
    {
        if (_exploredGrid == null) return 0f;
        float cellFx = wx / _expCellSize - 0.5f;
        float cellFy = wy / _expCellSize - 0.5f;
        int cx0 = FastFloor(cellFx), cy0 = FastFloor(cellFy);
        float fx = cellFx - cx0, fy = cellFy - cy0;
        int gx0 = cx0 - _expCellMinX, gy0 = cy0 - _expCellMinY;
        float e = Mathf.Lerp(
            Mathf.Lerp(SampleExplored(gx0, gy0, _expGridW, _expGridH), SampleExplored(gx0 + 1, gy0, _expGridW, _expGridH), fx),
            Mathf.Lerp(SampleExplored(gx0, gy0 + 1, _expGridW, _expGridH), SampleExplored(gx0 + 1, gy0 + 1, _expGridW, _expGridH), fx),
            fy);
        return e * e * (3f - 2f * e);
    }

    private void FillBlend(int x0, int y0, int x1, int y1, Color32 col, float strength)
    {
        x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0);
        x1 = Mathf.Min(TexW, x1); y1 = Mathf.Min(_texH, y1);
        for (int y = y0; y < y1; y++)
        {
            int row = y * TexW;
            for (int x = x0; x < x1; x++)
            {
                int i = row + x;
                Color32 e = _pixels[i];
                e.r = (byte)(e.r + (col.r - e.r) * strength);
                e.g = (byte)(e.g + (col.g - e.g) * strength);
                e.b = (byte)(e.b + (col.b - e.b) * strength);
                _pixels[i] = e;
            }
        }
    }

    private void DrawVLine(int x, Color32 col, float s) { if (x >= 0 && x < TexW) FillBlend(x, 0, x + 1, _texH, col, s); }
    private void DrawHLine(int y, Color32 col, float s) { if (y >= 0 && y < _texH) FillBlend(0, y, TexW, y + 1, col, s); }
    private void BlendRow(int y, Color32 col, float s) { FillBlend(0, y, TexW, y + 1, col, s); }

    private static int FastFloor(float v)
    {
        int i = (int)v;
        return v < i ? i - 1 : i;
    }

    // ===================================================
    // 플레이어 마커
    // ===================================================
    private void UpdateMarker()
    {
        if (_playerMarker == null) return;
        if (_playerTr == null) { _playerMarker.gameObject.SetActive(false); return; }

        float scale = panelWidth / _worldWidth; // 월드 유닛당 로컬 px
        Vector2 p = _playerTr.position;
        float lx = (p.x - _center.x) * scale;
        float ly = (p.y - _center.y) * scale;

        // 화면 밖이면 가장자리에 붙여 방향을 알려준다
        float halfW = panelWidth * 0.5f - 8f;
        float halfH = panelHeight * 0.5f - 8f;
        bool outside = lx < -halfW || lx > halfW || ly < -halfH || ly > halfH;
        lx = Mathf.Clamp(lx, -halfW, halfW);
        ly = Mathf.Clamp(ly, -halfH, halfH);

        _playerMarker.gameObject.SetActive(true);
        _playerMarker.sizeDelta = Vector2.one * (PlayerMarkerBaseSize * MarkerZoomScale()); // 줌 연동
        _playerMarker.anchoredPosition = new Vector2(lx, ly);
        _playerMarker.localScale = Vector3.one * (outside ? 0.8f : 1f + 0.08f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f)));

        if (_coordText != null)
        {
            var pl = _playerTr.position;
            int depth = Mathf.Max(0, Mathf.RoundToInt(surfaceWorldY - pl.y));
            _coordText.text = $"X {Mathf.RoundToInt(pl.x)}   깊이 {depth}m";
        }
    }

    private void UpdateDetectedMarkers()
    {
        var store = DetectedChunkStore.Instance;
        var all = store.All;

        // 스프라이트 lazy 생성 (기존 MakeDiamondSprite 재사용)
        if (_detectSprite == null) _detectSprite = MakeDiamondSprite(32);

        float scale = panelWidth / _worldWidth; // 월드 유닛당 로컬 px (UpdateMarker와 동일)
        float half = ChunkCoords.WorldSize * 0.5f;
        float halfW = panelWidth * 0.5f - 6f;
        float halfH = panelHeight * 0.5f - 6f;

        int idx = 0;
        foreach (var kv in all)
        {
            Vector3 w = ChunkCoords.ToWorld(kv.Key);
            Vector2 world = new Vector2(w.x + half, w.y + half);
            float lx = (world.x - _center.x) * scale;
            float ly = (world.y - _center.y) * scale;
            if (lx < -halfW || lx > halfW || ly < -halfH || ly > halfH) continue; // 화면 밖 스킵

            RectTransform rt = GetOrCreateDetectMarker(idx++);
            rt.gameObject.SetActive(true);
            rt.sizeDelta = Vector2.one * (DetectMarkerBaseSize * MarkerZoomScale()); // 줌 연동
            rt.anchoredPosition = new Vector2(lx, ly);
            var img = rt.GetComponent<Image>();
            // 미방문 밝게 / 클리어 흐리게
            img.color = kv.Value.visited
                ? new Color(0.7f, 0.7f, 0.7f, 0.5f)
                : new Color(1f, 0.85f, 0.35f, 1f);
        }

        for (int i = idx; i < _detectMarkers.Count; i++)
            _detectMarkers[i].gameObject.SetActive(false);
    }

    private RectTransform GetOrCreateDetectMarker(int i)
    {
        if (i < _detectMarkers.Count) return _detectMarkers[i];
        var marker = new GameObject($"DetectMarker{i}").AddComponent<Image>();
        marker.transform.SetParent(_panelRt, false);
        marker.sprite = _detectSprite;
        marker.raycastTarget = false;
        var rt = marker.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.one * (DetectMarkerBaseSize * MarkerZoomScale());
        _generated.Add(marker);
        _detectMarkers.Add(rt);
        return rt;
    }

    private void EnsurePlayer()
    {
        if (_playerTr != null) return;
        _playerSearchTimer -= Time.unscaledDeltaTime;
        if (_playerSearchTimer > 0f) return;
        _playerSearchTimer = 0.5f;
        var pc = FindFirstObjectByType<PlayerController>();
        if (pc != null) _playerTr = pc.transform;
    }

    // ===================================================
    // UI 생성 (전부 코드)
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _texH = Mathf.Max(8, Mathf.RoundToInt(TexW * panelHeight / panelWidth));

        // ── 캔버스 ──
        _canvasObj = new GameObject("WorldMapCanvas");
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

        // ── 어둡게 덮는 막 ──
        var dim = new GameObject("Dim").AddComponent<Image>();
        dim.transform.SetParent(_canvasObj.transform, false);
        StretchFull(dim.rectTransform);
        dim.color = new Color(0f, 0f, 0f, dimAlpha);
        dim.raycastTarget = true;

        // ── 지도 프레임(테두리) — 인스펙터 스프라이트 지정 시 그것, 아니면 코드 생성 라운드 ──
        var frame = new GameObject("MapFrame").AddComponent<Image>();
        frame.transform.SetParent(_canvasObj.transform, false);
        if (panelFrameSprite != null)
        {
            frame.sprite = panelFrameSprite;
            frame.type = (panelFrameSprite.border.sqrMagnitude > 0.01f) ? Image.Type.Sliced : Image.Type.Simple;
            frame.color = Color.white;
        }
        else
        {
            frame.sprite = MakeRoundedRectSprite(48, 10);
            frame.type = Image.Type.Sliced;
            frame.color = PanelFrame;
        }
        var frameRt = frame.rectTransform;
        frameRt.anchorMin = frameRt.anchorMax = new Vector2(0.5f, 0.5f);
        frameRt.pivot = new Vector2(0.5f, 0.5f);
        frameRt.sizeDelta = new Vector2(panelWidth + 16f, panelHeight + 16f);
        frameRt.anchoredPosition = new Vector2(0f, 0f);

        // ── 지도 영역(RawImage) — 포인터 좌표 기준 ──
        _mapTex = new Texture2D(TexW, _texH, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point, // 바이리니어 업스케일 블러 제거 → 도트처럼 또렷하게
            wrapMode = TextureWrapMode.Clamp
        };
        _generated.Add(_mapTex);
        _pixels = new Color32[TexW * _texH];

        var mapObj = new GameObject("Map", typeof(RectTransform));
        mapObj.transform.SetParent(_canvasObj.transform, false);
        _panelRt = (RectTransform)mapObj.transform;
        _panelRt.anchorMin = _panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRt.pivot = new Vector2(0.5f, 0.5f);
        _panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);
        _panelRt.anchoredPosition = new Vector2(0f, 0f);
        _mapImage = mapObj.AddComponent<RawImage>();
        _mapImage.texture = _mapTex;
        _mapImage.raycastTarget = true; // 뒤쪽 클릭 차단 (드래그는 Input으로 직접 처리)

        // ── 플레이어 마커 (지도 영역 자식) ──
        var marker = new GameObject("PlayerMarker").AddComponent<Image>();
        marker.transform.SetParent(_panelRt, false);
        marker.sprite = playerMarkerSprite != null ? playerMarkerSprite
                      : MinimapPlayerSprite != null ? MinimapPlayerSprite
                      : MakeDiamondSprite(32);
        marker.raycastTarget = false;
        var markerRt = marker.rectTransform;
        markerRt.anchorMin = markerRt.anchorMax = new Vector2(0.5f, 0.5f);
        markerRt.pivot = new Vector2(0.5f, 0.5f);
        markerRt.sizeDelta = new Vector2(PlayerMarkerBaseSize, PlayerMarkerBaseSize);
        _playerMarker = markerRt;

        // ── 상단 제목 + 조작 힌트 ──
        var title = CreateText(_canvasObj.transform, "Title", 34f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        var titleRt = title.rectTransform;
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.sizeDelta = new Vector2(400f, 44f);
        titleRt.anchoredPosition = new Vector2(0f, panelHeight * 0.5f + 42f);
        title.characterSpacing = 10f;
        title.text = "지 도";

        var hint = CreateText(_canvasObj.transform, "Hint", 20f, FontStyles.Normal, HintCol, TextAlignmentOptions.Center);
        var hintRt = hint.rectTransform;
        hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0.5f);
        hintRt.sizeDelta = new Vector2(1000f, 30f);
        hintRt.anchoredPosition = new Vector2(0f, -panelHeight * 0.5f - 30f);
        hint.text = "휠: 확대 · 축소     드래그: 이동     M / ESC: 닫기";

        // ── 좌표/깊이 배지 (좌상단) ──
        _coordText = CreateText(_canvasObj.transform, "Coord", 22f, FontStyles.Bold, AccentAmber, TextAlignmentOptions.MidlineLeft);
        var coordRt = _coordText.rectTransform;
        coordRt.anchorMin = coordRt.anchorMax = new Vector2(0.5f, 0.5f);
        coordRt.sizeDelta = new Vector2(340f, 30f);
        coordRt.anchoredPosition = new Vector2(-panelWidth * 0.5f + 180f, panelHeight * 0.5f + 42f);
        _coordText.text = "";

        // ── 닫기 버튼 (우상단) ──
        var close = CreateButton(_canvasObj.transform, "Close", new Color(0.16f, 0.18f, 0.24f, 1f), RequestClose);
        var closeRt = (RectTransform)close.transform;
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(0.5f, 0.5f);
        closeRt.sizeDelta = new Vector2(48f, 48f);
        closeRt.anchoredPosition = new Vector2(panelWidth * 0.5f - 24f, panelHeight * 0.5f + 44f);
        var closeX = CreateText(close.transform, "X", 26f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        StretchFull(closeX.rectTransform);
        closeX.text = "✕";

        // ── 플레이어 위치로 버튼 ──
        var recenter = CreateButton(_canvasObj.transform, "Recenter", new Color(0.16f, 0.18f, 0.24f, 1f), () =>
        {
            EnsurePlayer();
            if (_playerTr != null) _center = _playerTr.position;
            // 줌은 그대로 두고 위치만 이동한다 (확대 상태에서 눌러도 확대 유지).
            _viewDirty = true;
            PlayClick();
        });
        var recRt = (RectTransform)recenter.transform;
        recRt.anchorMin = recRt.anchorMax = new Vector2(0.5f, 0.5f);
        recRt.sizeDelta = new Vector2(200f, 44f);
        recRt.anchoredPosition = new Vector2(panelWidth * 0.5f - 108f, -panelHeight * 0.5f - 30f);
        var recText = CreateText(recenter.transform, "Text", 20f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        StretchFull(recText.rectTransform);
        recText.text = "◎ 내 위치로";

        _canvasObj.SetActive(false);
    }

    private void RefreshFont()
    {
        var lm = LanguageManager.Instance;
        var font = lm != null ? lm.GetCurrentFont() : null;
        if (font == null) return;
        foreach (var o in _generated)
            if (o is TextMeshProUGUI tmp && tmp != null) tmp.font = font;
    }

    // ===================================================
    // 저수준 헬퍼
    // ===================================================
    private TextMeshProUGUI CreateText(Transform parent, string name, float size, FontStyles style, Color color, TextAlignmentOptions align)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        var lm = LanguageManager.Instance;
        var font = lm != null ? lm.GetCurrentFont() : null;
        if (font != null) tmp.font = font;
        _generated.Add(tmp);
        return tmp;
    }

    private Button CreateButton(Transform parent, string name, Color color, Action onClick)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        if (buttonSprite != null)
        {
            img.sprite = buttonSprite;
            img.type = (buttonSprite.border.sqrMagnitude > 0.01f) ? Image.Type.Sliced : Image.Type.Simple;
            img.color = Color.white;
        }
        else
        {
            img.sprite = MakeRoundedRectSprite(24, 6);
            img.type = Image.Type.Sliced;
            img.color = color;
        }
        var btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        return btn;
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

    // 미등록 키면 공용 버튼음으로 폴백한다. SoundManager를 직접 부르면 무음이 된다.
    private void PlayClick() => CodeUI.PlaySfx(clickSfxName);

    // ── 프로시저럴 스프라이트 ──
    private Sprite MakeRoundedRectSprite(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        float rad = Mathf.Max(1f, radius);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(rad - x, x - (size - 1 - rad), 0f);
                float dy = Mathf.Max(rad - y, y - (size - 1 - rad), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(rad - dist + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        float border = Mathf.Min(rad + 1f, size * 0.5f);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        _generated.Add(tex);
        _generated.Add(sprite);
        return sprite;
    }

    private Sprite MakeDiamondSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        float c = size * 0.5f - 0.5f;
        float coreR = size * 0.3f;
        float outR = coreR + 3f;
        Color32 core = new Color32(255, 246, 228, 255);
        Color32 rim = new Color32(20, 22, 28, 255);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Abs(x - c) + Mathf.Abs(y - c);
                float coreA = Mathf.Clamp01((coreR - d) / 1.3f);
                float outA = Mathf.Clamp01((outR - d) / 1.3f);
                byte r = (byte)Mathf.Lerp(rim.r, core.r, coreA);
                byte g = (byte)Mathf.Lerp(rim.g, core.g, coreA);
                byte b = (byte)Mathf.Lerp(rim.b, core.b, coreA);
                px[y * size + x] = new Color32(r, g, b, (byte)(255f * outA));
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _generated.Add(tex);
        _generated.Add(sprite);
        return sprite;
    }
}
