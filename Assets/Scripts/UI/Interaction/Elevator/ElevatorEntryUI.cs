// @tags: elevator, entrance, ui, surface, scene, selection, overlay, code-generated

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지상 엘리베이터 입구용 층 선택 오버레이. <b>전부 코드 생성</b>(씬/프리팹 세팅 불필요) —
/// 프로젝트의 다른 코드 생성 UI(<see cref="ElevatorOverlayUI"/>, PauseOverlayUI 등)와 같은 방식·같은 <see cref="UISkin"/>이다.
///
/// 흐름: <see cref="Open"/>(지하 엘리베이터 기둥 X, 지하 씬 이름) → 정류장 버튼 목록 표시 →
/// 클릭 시 <see cref="PlayerData"/>에 착지 오버라이드(청크 좌표)를 세팅하고 지하 씬을 로드한다.
/// 지하에서 PlayerSpawner가 오버라이드를 소비해 그 엘리베이터 청크 중앙에 착지시킨다.
///
/// 정류장 목록은 <see cref="ElevatorLayerCatalog"/>에서 얻으므로 지상 씬에
/// ElevatorManager가 없어도 동작한다.
///
/// 씬에 미리 배치해 두면 인스펙터에서 스프라이트·레이아웃을 조정할 수 있고,
/// 없으면 <see cref="Open"/>이 임시 오브젝트를 만들어 기본 스타일로 띄운다.
/// </summary>
public class ElevatorEntryUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤 / 조회
    // ===================================================
    private static ElevatorEntryUI _instance;

    public static bool IsOpen => _instance != null && _instance._isOpen;

    // 닫힌 프레임 번호를 static으로 둔다 — self-created 인스턴스는 Close에서 파괴되며 _instance=null이 되므로
    // 인스턴스 필드로는 그 프레임 판정이 사라진다.
    private static int _lastCloseFrame = -1;

    /// <summary>ESC로 닫힌 바로 그 프레임인지 — 같은 프레임에 일시정지가 중복으로 열리는 것 방지.</summary>
    public static bool ClosedThisFrame => _lastCloseFrame == Time.frameCount;

    /// <summary>지상 엘리베이터 입구가 호출한다. 씬에 배치된 오버레이가 있으면 그것을, 없으면 임시 오브젝트를 쓴다.</summary>
    public static void Open(int xChunk, string undergroundScene)
    {
        if (_instance == null)
        {
            _instance = FindFirstObjectByType<ElevatorEntryUI>(FindObjectsInactive.Include);
            if (_instance == null)
            {
                var go = new GameObject("ElevatorEntryUI");
                _instance = go.AddComponent<ElevatorEntryUI>();
                _instance._selfCreated = true;
            }
        }
        _instance.Show(xChunk, undergroundScene);
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

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.62f;
    [SerializeField] private bool useBlurBackdrop = true;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 620f;
    [SerializeField] private float listMaxHeight = 460f;
    [SerializeField] private float rowHeight = 62f;
    [SerializeField] private float buttonHeight = 56f;
    [Tooltip("일시정지(30800)보다 아래로 두면 ESC 메뉴가 위에 뜬다")]
    [SerializeField] private int sortingOrder = 30700;

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

    private bool _built, _isOpen, _opening, _selfCreated;
    private bool _setUIState;
    private int _lastOpenFrame = -1;

    private int _xChunk;
    private string _undergroundScene;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    private RectTransform _listContent;

    // _stopNav은 목록을 다시 그릴 때마다 갈아엎고, _fixedNav(닫기)는 한 번 만들면 유지된다.
    private readonly List<ICodeNavItem> _stopNav = new List<ICodeNavItem>();
    private readonly List<ICodeNavItem> _fixedNav = new List<ICodeNavItem>();
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    private void Show(int xChunk, string undergroundScene)
    {
        if (_isOpen || _opening) return;
        _xChunk = xChunk;
        _undergroundScene = undergroundScene;
        StartCoroutine(OpenRoutine());
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

        _isOpen = true;
        _lastOpenFrame = Time.frameCount;

        _loc.Refresh();
        BuildStopRows();

        _canvasObj.SetActive(true);
        _nav.Begin();

        // 다른 전역 단축키·상호작용 차단 (열려 있는 동안 Elevator 상태로 둔다)
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState != UIState.Elevator)
        {
            ui.SetState(UIState.Elevator);
            _setUIState = true;
        }

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
        CodeUI.PlaySfx(clickSfxName);

        RestoreUIState();
        _nav.End();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        // 코드가 만든 임시 오브젝트는 쓰고 버린다(씬에 배치된 인스턴스는 남긴다).
        if (_selfCreated)
        {
            _instance = null;
            Destroy(gameObject);
        }
    }

    private void RestoreUIState()
    {
        var ui = UIStateManager.Instance;
        if (_setUIState && ui != null && ui.CurrentState == UIState.Elevator)
            ui.SetState(UIState.None);
        _setUIState = false;
    }

    private void Update()
    {
        if (!_isOpen) return;
        if (_lastOpenFrame == Time.frameCount) return;

        CodeUI.ClearSelection();

        if (Input.GetKeyDown(KeyCode.Escape)) { CodeUI.PlayBack(); Close(); return; }
        _nav.Update();
    }

    private void OnDestroy()
    {
        _nav.End();
        _blur.Release(_blurImage);
        if (_instance == this) _instance = null;
    }

    // ===================================================
    // 정류장 선택
    // ===================================================
    /// <summary>정류장을 하나 골랐을 때 — 착지 오버라이드를 심고 지하 씬으로 내려간다.</summary>
    private void SelectStop(LayerInfo stop)
    {
        CodeUI.PlaySfx(clickSfxName);

        var saveManager = GameManager.Instance != null ? GameManager.Instance.saveManager : null;
        if (saveManager == null || saveManager.playerData == null)
        {
            Debug.LogError("[ElevatorEntryUI] SaveManager/PlayerData가 없어 진입할 수 없습니다.");
            return;
        }

        // 지상→지하는 맵이 새로 생성되므로 좌표 기반 인스턴스 상태를 초기화하고 확정 저장한다
        // (땅굴 입구 TunnelEntranceBehaviour와 동일 처리).
        // 이걸 먼저 부르는 이유: 여기서 이번 잠수의 엘리베이터 배치 시드가 새로 뽑힌다.
        // 착지 X를 앞에서 계산하면 지난 잠수 배치를 가리켜 엘리베이터 없는 생지형에 내린다.
        saveManager.PrepareUndergroundEntry();

        var pData = saveManager.playerData;
        pData.spawnAtElevator = true;

        // 착지 X는 고른 층이 정한다 — 층마다 엘리베이터가 하나뿐이고 X도 층마다 다르다
        // (ElevatorStopLayout). 지상 입구의 elevatorXChunk는 착지 좌표와 무관하다.
        // (지상 씬에는 ElevatorManager가 없어 순수 함수를 직접 부른다.)
        pData.elevatorEntryXChunk = ElevatorStopLayout.XForLayer(stop.layerIndex);
        pData.elevatorEntryYChunk = stop.startDepth;   // 정류장 청크 Y = 엘리베이터가 선 깊이

        // 지상 복귀 시 엘리베이터 앞으로 나오도록 입구를 기록한다.
        SurfaceReturnRouter.Stamp(SurfaceReturnPoint.Elevator);

        // PrepareUndergroundEntry의 Save()는 위 값들보다 앞서 돌았다 — 지하 씬은 파일에서
        // 다시 읽으므로 착지 좌표를 여기서 한 번 더 굳혀야 한다.
        saveManager.Save();

        // 씬 전환 전에 UI 상태를 되돌린다(새 씬의 UIStateManager는 None으로 시작해야 한다).
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        RestoreUIState();
        _nav.End();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        Debug.Log($"[ElevatorEntryUI] 지하 엘리베이터로 이동: 착지 X:{pData.elevatorEntryXChunk}, 깊이 {stop.startDepth} ({stop.layerName})");
        SceneLoader.LoadScene(_undergroundScene);
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("ElevatorEntryOverlayCanvas");
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
        var title = CodeUI.CreateText(titleRow, "Title", 28f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        title.characterSpacing = 6f;
        _loc.Bind(title, "ui_elevator_entry_title", "어느 층으로 내려갈까요?");

        CodeUI.CreateDivider(panel.transform);

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

        _nav.moveSfxName = moveSfxName;
        _nav.collect = list =>
        {
            list.Clear();
            list.AddRange(_stopNav);
            list.AddRange(_fixedNav);
        };

        _canvasObj.SetActive(false);
    }

    private void BuildStopRows()
    {
        _stopNav.Clear();
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);

        // 얕은 → 깊은 순 그대로
        var stops = ElevatorLayerCatalog.Build();
        Debug.Log($"[ElevatorEntryUI] 정류장 {stops.Count}개 / 해금 {ElevatorStopUnlockStore.UnlockedCount}개 " +
                  $"(depths: {string.Join(", ", stops.ConvertAll(l => l.startDepth))})");
        Sprite rowSprite = skin.slotSprite != null ? skin.slotSprite : skin.buttonSprite;

        foreach (var stop in stops)
        {
            var captured = stop;

            // 직접 가본 적 없는 정류장은 고를 수 없다(ElevatorStopUnlockStore).
            bool unlocked = ElevatorStopUnlockStore.IsUnlocked(captured);

            var btn = CodeUI.CreateButton(_listContent, "Stop_" + captured.layerIndex,
                unlocked ? CodeUI.CardBg : CodeUI.SlotBg,
                () => { if (unlocked) SelectStop(captured); }, rowSprite, skin);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;
            btn.interactable = unlocked;

            var row = btn.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(18, 18, 6, 6);
            row.spacing = 10f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childAlignment = TextAnchor.MiddleLeft;

            var dot = CodeUI.CreateText(btn.transform, "Dot", 14f, FontStyles.Normal, CodeUI.MutedColor,
                TextAlignmentOptions.Center, _loc);
            dot.text = unlocked ? diamondGlyph : lockGlyph;
            dot.gameObject.AddComponent<LayoutElement>().preferredWidth = 20f;

            var name = CodeUI.CreateText(btn.transform, "Name", 21f, FontStyles.Bold,
                unlocked ? CodeUI.LabelColor : CodeUI.MutedColor, TextAlignmentOptions.MidlineLeft, _loc);
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // 미해금 정류장은 이름·깊이를 숨긴다 — 어디까지 있는지는 보이되 무엇인지는 가려 둔다.
            name.text = unlocked ? captured.layerName : CodeUI.L("ui_elevator_locked_name", "???");

            var depth = CodeUI.CreateText(btn.transform, "Depth", 16f, FontStyles.Normal, CodeUI.MutedColor,
                TextAlignmentOptions.MidlineRight, _loc);
            depth.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;
            depth.text = unlocked
                ? string.Format(CodeUI.L("ui_elevator_depth", "깊이 {0}"), captured.startDepth)
                : CodeUI.L("ui_elevator_locked", "미발견");

            // 잠긴 줄은 커서가 서지 않게 목록에서 뺀다(들어가도 아무 일도 안 일어나므로).
            if (!unlocked) continue;

            var navItem = CodeNavButton.Attach(btn, skin);
            if (navItem != null) _stopNav.Add(navItem);
        }

        _nav.Refresh();
    }
}
