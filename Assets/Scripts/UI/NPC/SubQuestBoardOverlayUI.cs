// @tags: subquest, board, ui, overlay, code-generated, accept, slot, mining-level

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 서브퀘스트 게시판 오버레이 — 수락 가능한 서브퀘스트를 목록으로 보여주고 슬롯에 담는다.
/// UI는 전부 코드로 생성한다(다른 코드 오버레이와 같은 패턴) — 씬/프리팹 세팅 없이 <see cref="Open"/>만으로 동작.
///
/// 기존 프리팹 <see cref="SubQuestBoardUI"/>와 같은 역할:
///  - 슬롯 정보(현재/최대)를 상단에 표시
///  - 각 서브퀘스트: 이름·설명·요구 채광레벨 + '수락' 버튼(슬롯이 차면 '슬롯 부족')
///  - 수락은 QuestManager.AcceptQuest 에 위임
///
/// UIStateManager 의 <c>useCodeBuiltSubQuestUI</c> 가 켜져 있으면 이 오버레이가 프리팹 대신 열린다.
/// </summary>
public class SubQuestBoardOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static SubQuestBoardOverlayUI _instance;

    public static SubQuestBoardOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SubQuestBoardOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("SubQuestBoardOverlayUI");
                    _instance = go.AddComponent<SubQuestBoardOverlayUI>();
                }
            }
            return _instance;
        }
    }

    public static bool IsOpen => _instance != null && _instance._isOpen;
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!_isOpen && !_opening) return;
        _opening = false;
        _isOpen = false;
        Time.timeScale = 1f;
        Unsubscribe();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 인스펙터
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.5f;
    [SerializeField] private bool useBlurBackdrop = true;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("동작")]
    [Tooltip("열려 있는 동안 게임을 멈춘다(Time.timeScale = 0)")]
    [SerializeField] private bool pauseGameWhileOpen = true;
    [SerializeField] private bool closeOnEscape = true;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 1040f;
    [SerializeField] private float panelHeight = 780f;
    [SerializeField] private float cardHeight = 132f;
    [SerializeField] private int sortingOrder = 30460;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 상수
    // ===================================================
    private const float FadeDuration = 0.14f;
    private const float ToastDuration = 2.2f;

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built, _isOpen, _opening, _needsRefresh;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f, _toastUntil;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    // WASD 키보드 내비게이션 (창고·일시정지 오버레이와 같은 공용 시스템)
    private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();
    private readonly List<ICodeNavItem> _navItems = new List<ICodeNavItem>();

    private TextMeshProUGUI _slotInfo, _emptyText, _hintText, _toastText;
    private ScrollRect _scroll;
    private RectTransform _list;

    private bool _subscribed, _langSubscribed;

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    public static void Open()
    {
        if (IsOpen) { Instance._needsRefresh = true; return; }
        if (Instance._opening) return;
        Instance.StartCoroutine(Instance.OpenRoutine());
    }

    public static void CloseStatic()
    {
        if (_instance != null) _instance.Close();
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        if (pauseGameWhileOpen) Time.timeScale = _prevTimeScale;
        CodeUI.PlaySfx(clickSfxName);

        ClearToast();
        _nav.End();
        Unsubscribe();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.SubQuestBoard) ui.SetState(UIState.None);
    }

    private IEnumerator OpenRoutine()
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        ClearToast();

        if (useBlurBackdrop)
        {
            _canvasObj.SetActive(false);
            yield return new WaitForEndOfFrame();
            _blur.Capture(_blurImage, blurDownsamples, flipBlurVertically);
        }
        _blurImage.enabled = useBlurBackdrop;
        _opening = false;

        _prevTimeScale = Time.timeScale;
        if (pauseGameWhileOpen) Time.timeScale = 0f;
        _isOpen = true;

        Subscribe();
        _loc.Refresh();
        RefreshAll();

        _canvasObj.SetActive(true);
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;

        _nav.Begin();
        _nav.ClearFocus();   // 커서 없이 시작 — 첫 W/A/S/D 입력이 커서를 켠다

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

        // Space는 Submit로도 잡힌다 — 직전에 마우스로 누른 버튼이 선택된 채 남아 있으면
        // 내비게이터의 확인과 그 버튼의 onClick이 겹쳐 한 번에 두 번 실행된다.
        CodeUI.ClearSelection();

        // ESC로 닫는다. 확인/선택(수락)은 이제 스페이스바가 '포커스된 카드 수락'으로 쓰이므로
        // 더 이상 Space로 닫지 않는다(WASD 내비게이션과 통일).
        if (closeOnEscape && Input.GetKeyDown(KeyCode.Escape))
        {
            CodeUI.PlayBack();
            Close();
            return;
        }

        // W/A/S/D 이동 + Space 수락
        _nav.Update();
    }

    private void LateUpdate()
    {
        if (!_isOpen) return;
        if (_toastUntil > 0f && Time.unscaledTime >= _toastUntil) ClearToast();
        if (_needsRefresh)
        {
            _needsRefresh = false;
            RefreshAll();
        }
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        _nav.End();
        Unsubscribe();
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 이벤트
    // ===================================================
    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        if (!_langSubscribed && LanguageManager.Instance != null)
        {
            LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
            _langSubscribed = true;
        }
    }

    private void Unsubscribe() => _subscribed = false;

    private void OnLanguageChanged(LanguageType _)
    {
        _loc.Refresh();
        _needsRefresh = true;
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("SubQuestBoardOverlayCanvas");
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
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);
        panelRt.anchoredPosition = Vector2.zero;

        const float side = 22f;
        const float titleH = 64f;
        const float hintH = 30f;

        BuildTitleBar(panel.transform, side, titleH);

        // 목록 스크롤
        var listHost = CodeUI.CreateImage(panel.transform, "ListCard", CodeUI.CardBg, skin.cardSprite, skin);
        var host = listHost.rectTransform;
        host.anchorMin = Vector2.zero;
        host.anchorMax = Vector2.one;
        host.offsetMin = new Vector2(side, side + hintH + 6f);
        host.offsetMax = new Vector2(-side, -(side + titleH + 8f));
        var hostLayout = listHost.gameObject.AddComponent<VerticalLayoutGroup>();
        hostLayout.padding = new RectOffset(14, 14, 14, 14);
        hostLayout.childControlWidth = true;
        hostLayout.childControlHeight = true;
        hostLayout.childForceExpandWidth = true;
        hostLayout.childForceExpandHeight = true;

        _scroll = CodeUI.CreateScrollView(listHost.transform, "Scroll", out _list);
        var listLayout = _list.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 10f;
        listLayout.padding = new RectOffset(2, 8, 2, 2);
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        // 빈 상태 문구 (목록 위에 겹쳐 중앙 표시)
        _emptyText = CodeUI.CreateText(listHost.transform, "Empty", 20f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(_emptyText.rectTransform);
        _emptyText.gameObject.SetActive(false);

        // 조작 안내 + 토스트
        _hintText = CodeUI.CreateText(panel.transform, "Hint", 16f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        var hintRt = _hintText.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.offsetMin = new Vector2(side, side);
        hintRt.offsetMax = new Vector2(-side, side + hintH);
        _loc.Bind(_hintText, "ui_subquest_hint", "W / A / S / D : 이동   ·   Space : 수락   ·   ESC : 닫기");

        _toastText = CodeUI.CreateText(panel.transform, "Toast", 17f, FontStyles.Bold,
            Color.white, TextAlignmentOptions.Center, _loc);
        var toastRt = _toastText.rectTransform;
        toastRt.anchorMin = hintRt.anchorMin;
        toastRt.anchorMax = hintRt.anchorMax;
        toastRt.pivot = hintRt.pivot;
        toastRt.offsetMin = hintRt.offsetMin;
        toastRt.offsetMax = hintRt.offsetMax;
        _toastText.gameObject.SetActive(false);

        // 내비게이터는 매 갱신마다 새로 만들어지는 카드 버튼 목록(_navItems)을 실시간으로 참조한다.
        _nav.moveSfxName = clickSfxName;
        _nav.collect = list => { list.Clear(); list.AddRange(_navItems); };

        _canvasObj.SetActive(false);
    }

    private void BuildTitleBar(Transform panel, float side, float titleH)
    {
        var bar = CodeUI.CreateRect(panel, "TitleBar");
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(side, -(side + titleH));
        bar.offsetMax = new Vector2(-side, -side);
        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var diamond = CodeUI.CreateText(bar, "Diamond", 22f, FontStyles.Normal, CodeUI.QuestColor,
            TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;

        var title = CodeUI.CreateText(bar, "Title", 30f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, _loc);
        title.characterSpacing = 6f;
        _loc.Bind(title, "ui_subquest_title", "서브퀘스트");

        CodeUI.CreateSpacer(bar);

        _slotInfo = CodeUI.CreateText(bar, "SlotInfo", 20f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineRight, _loc);
        _slotInfo.gameObject.AddComponent<LayoutElement>().preferredWidth = 280f;
    }

    // ===================================================
    // 갱신
    // ===================================================
    private void RefreshAll()
    {
        _navItems.Clear();
        ClearChildren(_list);

        var qm = QuestManager.Instance;
        if (qm == null)
        {
            SetSlotInfo(0, QuestManager.MAX_SUB_QUEST_SLOTS);
            ShowEmpty(CodeUI.L("ui_subquest_no_manager", "퀘스트 매니저를 찾을 수 없습니다."));
            return;
        }

        // 게시판 열 때 최신 해금 상태 반영 (레벨업 직후 등)
        qm.RefreshSubQuestUnlocks();

        int active = qm.GetActiveSubQuestCount();
        int max = QuestManager.MAX_SUB_QUEST_SLOTS;
        SetSlotInfo(active, max);

        var available = new List<QuestSO>();
        foreach (var quest in qm.subQuests)
        {
            if (quest == null) continue;
            if (quest.questType != QuestType.Sub) continue;
            if (qm.GetQuestStatus(quest.questID) != QuestStatus.Available) continue;
            available.Add(quest);
        }

        if (available.Count == 0)
        {
            ShowEmpty(CodeUI.L("ui_subquest_empty", "수락 가능한 서브퀘스트가 없습니다."));
            return;
        }

        _emptyText.gameObject.SetActive(false);
        bool hasSlot = qm.CanAcceptSubQuest();
        foreach (var quest in available)
            BuildQuestCard(quest, hasSlot);

        // 목록을 다시 그렸으니 키보드 커서를 원래 자리에서 가장 가까운 카드로 되돌린다.
        _nav.Refresh();
    }

    private void SetSlotInfo(int active, int max)
    {
        if (_slotInfo == null) return;
        _slotInfo.text = LanguageManager.Instance != null
            ? LanguageManager.Instance.LF("ui_subquest_slot_info", active, max)
            : $"슬롯: {active}/{max}";
        _slotInfo.color = active >= max ? CodeUI.NegativeColor : CodeUI.LabelColor;
    }

    private void ShowEmpty(string message)
    {
        if (_emptyText == null) return;
        _emptyText.gameObject.SetActive(true);
        _emptyText.text = message;
    }

    private void BuildQuestCard(QuestSO quest, bool hasSlot)
    {
        var cardBg = CodeUI.CreateImage(_list, "QuestCard", CodeUI.SlotBg, skin.slotSprite, skin);
        cardBg.gameObject.AddComponent<LayoutElement>().preferredHeight = cardHeight;
        var row = cardBg.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(18, 18, 14, 14);
        row.spacing = 14f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;
        row.childAlignment = TextAnchor.MiddleLeft;

        // 왼쪽: 이름 + 설명 + 레벨
        // 카드는 매 갱신마다 다시 만들므로 binder(_loc)에 묶지 않는다 — 폰트는 생성 시 적용되고,
        // 언어 변경 시엔 RefreshAll이 목록을 통째로 다시 만든다.
        var texts = CodeUI.CreateColumn(cardBg.transform, "Texts", 4f);
        texts.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var name = CodeUI.CreateText(texts, "Name", 22f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.TopLeft);
        name.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
        name.text = quest.QuestName;

        var desc = CodeUI.CreateText(texts, "Desc", 16f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.TopLeft);
        desc.textWrappingMode = TextWrappingModes.Normal;
        desc.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        desc.text = quest.QuestDescription;

        var level = CodeUI.CreateText(texts, "Level", 15f, FontStyles.Bold, CodeUI.QuestColor,
            TextAlignmentOptions.MidlineLeft);
        level.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        level.text = LanguageManager.Instance != null
            ? LanguageManager.Instance.LF("ui_subquest_mining_level", quest.requiredMiningLevel)
            : $"채광 Lv.{quest.requiredMiningLevel}";

        // 오른쪽: 수락 버튼 (라벨도 매번 새로 만들어지므로 binder 없이 만든다)
        var btn = CodeUI.CreateTextButton(cardBg.transform, "Accept",
            hasSlot ? CodeUI.PositiveColor : CodeUI.NeutralBg, hasSlot ? CodeUI.GoldFg : Color.white, 19f,
            () => OnAccept(quest), out var btnLabel, skin.buttonSprite, skin);
        var btnLe = btn.gameObject.AddComponent<LayoutElement>();
        btnLe.preferredWidth = btnLe.minWidth = 150f;
        btn.interactable = hasSlot;
        btnLabel.text = hasSlot
            ? CodeUI.L("ui_subquest_accept", "수락")
            : CodeUI.L("ui_subquest_no_slot", "슬롯 부족");

        // 이 '수락' 버튼을 WASD 이동 대상으로 등록(슬롯이 없어 비활성이면 자동으로 건너뛴다)
        var nav = CodeNavButton.Attach(btn, skin);
        if (nav != null) _navItems.Add(nav);
    }

    private void OnAccept(QuestSO quest)
    {
        var qm = QuestManager.Instance;
        if (qm == null || quest == null) return;

        if (!qm.CanAcceptSubQuest())
        {
            Toast("ui_subquest_no_slot", "슬롯 부족", CodeUI.NegativeColor);
            return;
        }

        qm.AcceptQuest(quest);
        CodeUI.PlaySfx(clickSfxName);
        ToastRaw(string.Format(CodeUI.L("ui_subquest_accepted", "{0} 수락됨"), quest.QuestName), CodeUI.PositiveColor);
        _needsRefresh = true;
    }

    private static void ClearChildren(RectTransform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    // ===================================================
    // 토스트
    // ===================================================
    private void Toast(string key, string fallback, Color color) => ToastRaw(CodeUI.L(key, fallback), color);

    private void ToastRaw(string text, Color color)
    {
        if (_toastText == null) return;
        _toastText.text = text;
        _toastText.color = color;
        _toastText.gameObject.SetActive(true);
        if (_hintText != null) _hintText.gameObject.SetActive(false);
        _toastUntil = Time.unscaledTime + ToastDuration;
    }

    private void ClearToast()
    {
        _toastUntil = 0f;
        if (_toastText != null) _toastText.gameObject.SetActive(false);
        if (_hintText != null) _hintText.gameObject.SetActive(true);
    }
}
