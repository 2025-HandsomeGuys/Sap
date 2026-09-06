// @tags: quest, ui, overlay, code-generated, log, main, sub, requirement, reward, submit

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 퀘스트 로그 오버레이 — 메인 퀘스트 + 서브퀘스트 슬롯을 탭으로 넘겨 보는 화면.
/// UI는 전부 코드로 생성한다(다른 코드 오버레이와 같은 패턴) — 씬/프리팹 세팅 없이 <see cref="Open"/>만으로 동작.
///
/// 기존 프리팹 <see cref="QuestPanelUI"/>와 같은 정보를 보여준다:
///  - 탭: [메인] [서브 I] [서브 II]  (Q/E 로 넘기고, J/ESC 로 닫는다)
///  - 수락 상태일 때만 요구 광물(창고 보유/필요)·보상 목록을 펼친다
///  - 상점/트럭 NPC 를 통해 열면(제출 허용) 요구를 채웠을 때 '제출' 버튼이 활성화된다
///
/// UIStateManager 의 <c>useCodeBuiltQuestUI</c> 가 켜져 있으면 이 오버레이가 프리팹 대신 열린다.
/// </summary>
public class QuestOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static QuestOverlayUI _instance;

    public static QuestOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<QuestOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("QuestOverlayUI");
                    _instance = go.AddComponent<QuestOverlayUI>();
                }
            }
            return _instance;
        }
    }

    public static bool IsOpen => _instance != null && _instance._isOpen;
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    // NPC(상점·트럭) 팝업에서 '제출 가능' 모드로 열고 싶을 때 SetState 직전에 요청한다.
    // SetState 가 Open()을 부르므로 인자를 못 넘겨, 정적 플래그로 한 번만 전달한다.
    private static bool _pendingSubmission;
    public static void RequestSubmission() => _pendingSubmission = true;

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

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 1120f;
    [SerializeField] private float panelHeight = 760f;
    [SerializeField] private float rowHeight = 64f;
    [SerializeField] private int sortingOrder = 30450;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 상수
    // ===================================================
    private const float FadeDuration = 0.14f;
    private const float ToastDuration = 2.2f;
    private const int TabCount = 3; // 메인 + 서브 슬롯 2개

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built, _isOpen, _opening, _needsRefresh, _allowSubmission;
    private int _lastCloseFrame = -1;
    private int _tab; // 0: 메인, 1: 서브 슬롯0, 2: 서브 슬롯1
    private float _prevTimeScale = 1f, _toastUntil;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    private readonly List<(Button btn, TextMeshProUGUI label, int index)> _tabs = new List<(Button, TextMeshProUGUI, int)>();
    private TextMeshProUGUI _questTitle, _statusText, _descText, _hintText, _toastText;
    private RectTransform _reqSection, _reqList, _rewardSection, _rewardList;
    private TextMeshProUGUI _reqHeader, _rewardHeader;
    private Button _submitBtn;
    private TextMeshProUGUI _submitLabel;

    private bool _subscribed, _langSubscribed;

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    public static void Open()
    {
        bool allow = _pendingSubmission;
        _pendingSubmission = false;

        var inst = Instance;
        inst._allowSubmission = allow;

        if (IsOpen) { inst._tab = 0; inst._needsRefresh = true; return; }
        if (inst._opening) return;
        inst.StartCoroutine(inst.OpenRoutine());
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
        Unsubscribe();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        // 팝업(NPC)에서 들어왔다면 None이 아니라 그 팝업으로 되돌린다(NPC → 팝업 → 퀘스트 → ESC → 팝업).
        // J 키로 연 경우엔 소스가 없어 ReturnToPopupOrClose가 그대로 None으로 닫는다.
        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Quest) ui.ReturnToPopupOrClose();
    }

    private IEnumerator OpenRoutine()
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();

        _tab = 0;
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

        // J/ESC 로 닫기. Q/E 로 탭 전환. (Tab 은 인벤토리라 여기선 쓰지 않는다)
        if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Escape))
        {
            // ESC는 뒤로가기음, J는 토글이라 평소 클릭음 그대로.
            if (Input.GetKeyDown(KeyCode.Escape)) CodeUI.PlayBack();
            Close();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Q)) StepTab(-1);
        else if (Input.GetKeyDown(KeyCode.E)) StepTab(1);
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

    private void Unsubscribe()
    {
        _subscribed = false;
    }

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

        _canvasObj = new GameObject("QuestOverlayCanvas");
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
        const float tabH = 46f;
        const float hintH = 30f;
        const float submitH = 56f;

        BuildTitleBar(panel.transform, side, titleH);
        BuildTabs(panel.transform, side, titleH, tabH);

        // 본문 카드 (탭 아래 ~ 제출 버튼 위)
        var card = CodeUI.CreateImage(panel.transform, "Body", CodeUI.CardBg, skin.cardSprite, skin);
        var body = card.rectTransform;
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(side, side + hintH + submitH + 10f);
        body.offsetMax = new Vector2(-side, -(side + titleH + 8f + tabH + 8f));
        var bodyLayout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        bodyLayout.padding = new RectOffset(22, 22, 20, 20);
        bodyLayout.spacing = 10f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = false;

        BuildBodyContents(card.transform);

        // 제출 버튼
        _submitBtn = CodeUI.CreateTextButton(panel.transform, "Submit", CodeUI.PositiveColor, CodeUI.GoldFg, 21f,
            OnSubmit, out _submitLabel, skin.buttonSprite, skin, _loc);
        var subRt = _submitBtn.image.rectTransform;
        subRt.anchorMin = new Vector2(0f, 0f);
        subRt.anchorMax = new Vector2(1f, 0f);
        subRt.pivot = new Vector2(0.5f, 0f);
        subRt.offsetMin = new Vector2(side, side + hintH + 6f);
        subRt.offsetMax = new Vector2(-side, side + hintH + 6f + submitH);
        _loc.Bind(_submitLabel, "quest_submit", "제출하기");

        // 조작 안내 + 토스트 (같은 자리)
        _hintText = CodeUI.CreateText(panel.transform, "Hint", 16f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center, _loc);
        var hintRt = _hintText.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.offsetMin = new Vector2(side, side);
        hintRt.offsetMax = new Vector2(-side, side + hintH);
        _loc.Bind(_hintText, "quest_hint", "Q / E : 탭 전환   ·   J / ESC : 닫기");

        _toastText = CodeUI.CreateText(panel.transform, "Toast", 17f, FontStyles.Bold,
            Color.white, TextAlignmentOptions.Center, _loc);
        var toastRt = _toastText.rectTransform;
        toastRt.anchorMin = hintRt.anchorMin;
        toastRt.anchorMax = hintRt.anchorMax;
        toastRt.pivot = hintRt.pivot;
        toastRt.offsetMin = hintRt.offsetMin;
        toastRt.offsetMax = hintRt.offsetMax;
        _toastText.gameObject.SetActive(false);

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

        var title = CodeUI.CreateText(bar, "Title", 32f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, _loc);
        title.characterSpacing = 8f;
        _loc.Bind(title, "quest_title", "퀘 스 트");

        CodeUI.CreateSpacer(bar);
    }

    private void BuildTabs(Transform panel, float side, float titleH, float tabH)
    {
        var row = CodeUI.CreateRect(panel, "Tabs");
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.offsetMin = new Vector2(side, -(side + titleH + 8f + tabH));
        row.offsetMax = new Vector2(-side, -(side + titleH + 8f));
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        AddTab(row, 0, "quest_tab_main", "메인");
        AddTab(row, 1, "quest_tab_sub1", "서브 I");
        AddTab(row, 2, "quest_tab_sub2", "서브 II");
    }

    private void AddTab(Transform parent, int index, string key, string fallback)
    {
        var btn = CodeUI.CreateButton(parent, "Tab" + index, CodeUI.TabIdleBg, () => SwitchTab(index), skin.tabSprite, skin);
        var label = CodeUI.CreateText(btn.transform, "Text", 19f, FontStyles.Bold, CodeUI.QuestColor,
            TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(label.rectTransform);
        _loc.Bind(label, key, fallback);
        _tabs.Add((btn, label, index));
    }

    private void BuildBodyContents(Transform body)
    {
        // 퀘스트 제목 + 상태 배지 한 줄
        var head = CodeUI.CreateRow(body, "QuestHead", 40f, 10f);
        _questTitle = CodeUI.CreateText(head, "QuestTitle", 26f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, _loc);
        _questTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _statusText = CodeUI.CreateText(head, "Status", 18f, FontStyles.Bold, CodeUI.MutedColor,
            TextAlignmentOptions.MidlineRight, _loc);
        _statusText.gameObject.AddComponent<LayoutElement>().preferredWidth = 220f;

        _descText = CodeUI.CreateText(body, "Desc", 18f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.TopLeft, _loc);
        _descText.textWrappingMode = TextWrappingModes.Normal;
        _descText.gameObject.AddComponent<LayoutElement>().preferredHeight = 66f;

        CodeUI.CreateDivider(body);

        // 필요 광물
        _reqSection = CodeUI.CreateColumn(body, "ReqSection", 6f);
        _reqHeader = CodeUI.CreateText(_reqSection, "ReqHeader", 18f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineLeft, _loc);
        _reqHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        _loc.Bind(_reqHeader, "quest_requirements", "필요 광물");
        _reqList = CodeUI.CreateColumn(_reqSection, "ReqList", 6f);

        // 보상
        _rewardSection = CodeUI.CreateColumn(body, "RewardSection", 6f);
        _rewardHeader = CodeUI.CreateText(_rewardSection, "RewardHeader", 18f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineLeft, _loc);
        _rewardHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        _loc.Bind(_rewardHeader, "quest_rewards", "보상");
        _rewardList = CodeUI.CreateColumn(_rewardSection, "RewardList", 6f);
    }

    // ===================================================
    // 탭
    // ===================================================
    private void SwitchTab(int index)
    {
        if (index < 0 || index >= TabCount || index == _tab) return;
        CodeUI.PlaySfx(clickSfxName);
        _tab = index;
        RefreshAll();
    }

    private void StepTab(int dir)
    {
        int next = ((_tab + dir) % TabCount + TabCount) % TabCount;
        SwitchTab(next);
    }

    private void UpdateTabVisuals()
    {
        foreach (var (btn, label, index) in _tabs)
        {
            bool selected = index == _tab;
            Sprite sprite = selected
                ? (skin.tabSelectedSprite != null ? skin.tabSelectedSprite : skin.tabSprite)
                : skin.tabSprite;
            CodeUI.ApplySkin(btn.image, selected ? CodeUI.AccentFill : CodeUI.TabIdleBg, sprite, skin);
            label.color = selected ? Color.white : new Color(CodeUI.QuestColor.r, CodeUI.QuestColor.g, CodeUI.QuestColor.b, 0.75f);
        }
    }

    // ===================================================
    // 갱신
    // ===================================================
    private QuestSO CurrentTabQuest()
    {
        var qm = QuestManager.Instance;
        if (qm == null) return null;
        switch (_tab)
        {
            case 0: return qm.GetActiveMainQuest();
            case 1: return qm.GetSubQuestInSlot(0);
            case 2: return qm.GetSubQuestInSlot(1);
            default: return null;
        }
    }

    private void RefreshAll()
    {
        UpdateTabVisuals();

        var qm = QuestManager.Instance;
        var quest = CurrentTabQuest();

        if (quest == null || qm == null)
        {
            ShowEmpty();
            return;
        }

        _questTitle.text = quest.QuestName;
        _descText.text = quest.QuestDescription;

        QuestStatus status = qm.GetQuestStatus(quest.questID);
        bool accepted = status == QuestStatus.Accepted;
        bool complete = accepted && qm.CheckRequirements(quest);

        // 상태 배지
        UpdateStatus(status, accepted, complete);

        // 요구/보상은 수락 상태에서만 (프리팹과 동일)
        _reqSection.gameObject.SetActive(accepted);
        _rewardSection.gameObject.SetActive(accepted);
        if (accepted)
        {
            BuildRequirements(quest);
            BuildRewards(quest);
        }

        // 제출 버튼: 수락 + 요구 충족 + 제출 허용(NPC 경유)일 때만
        bool canSubmit = accepted && complete && _allowSubmission;
        _submitBtn.gameObject.SetActive(accepted);
        _submitBtn.interactable = canSubmit;
        CodeUI.ApplySkin(_submitBtn.image, canSubmit ? CodeUI.PositiveColor : CodeUI.NeutralBg, skin.buttonSprite, skin);
    }

    private void UpdateStatus(QuestStatus status, bool accepted, bool complete)
    {
        if (!accepted)
        {
            _statusText.text = status == QuestStatus.Completed
                ? CodeUI.L("quest_status_completed", "완료됨")
                : CodeUI.L("quest_status_need_accept", "수락 필요");
            _statusText.color = CodeUI.MutedColor;
            return;
        }

        if (complete)
        {
            _statusText.text = _allowSubmission
                ? CodeUI.L("quest_status_submittable", "제출 가능")
                : CodeUI.L("quest_status_submit_at_truck", "트럭에서 제출 가능");
            _statusText.color = CodeUI.PositiveColor;
        }
        else
        {
            _statusText.text = CodeUI.L("quest_status_progress", "진행 중");
            _statusText.color = CodeUI.WarnColor;
        }
    }

    private void ShowEmpty()
    {
        _questTitle.text = CodeUI.L("quest_empty_title", "퀘스트 없음");
        _descText.text = _tab == 0
            ? CodeUI.L("quest_empty_main", "진행 가능한 메인 퀘스트가 없습니다.")
            : CodeUI.L("quest_empty_sub", "게시판에서 서브 퀘스트를 수락하세요.");
        _statusText.text = CodeUI.L("quest_empty_status", "없음");
        _statusText.color = CodeUI.MutedColor;

        _reqSection.gameObject.SetActive(false);
        _rewardSection.gameObject.SetActive(false);
        _submitBtn.gameObject.SetActive(false);
    }

    private void BuildRequirements(QuestSO quest)
    {
        ClearChildren(_reqList);

        var wh = WarehouseManager.Instance;
        if (quest.requirements == null || quest.requirements.Count == 0)
        {
            var none = CodeUI.CreateText(_reqList, "None", 16f, FontStyles.Italic, CodeUI.MutedColor,
                TextAlignmentOptions.MidlineLeft);
            none.text = CodeUI.L("quest_no_requirements", "필요한 광물이 없습니다.");
            none.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            return;
        }

        foreach (var req in quest.requirements)
        {
            if (req.type == RequirementType.Mineral)
            {
                if (req.requiredMineral == null) continue;
                int cur = wh != null ? wh.GetMineralCount(req.requiredMineral) : 0;
                bool met = cur >= req.requiredAmount;
                string text = $"{req.requiredMineral.DisplayName}  ({cur}/{req.requiredAmount})";
                AddIconRow(_reqList, req.requiredMineral.icon, text, met ? CodeUI.PositiveColor : CodeUI.NegativeColor, CodeUI.MineralColor);
            }
            else if (req.type == RequirementType.SceneVisit)
            {
                bool met = QuestManager.Instance != null && QuestManager.Instance.HasQuestFlag("Scene_" + req.targetString);
                string text = $"지역 방문: {req.targetString}";
                AddIconRow(_reqList, null, text, met ? CodeUI.PositiveColor : CodeUI.NegativeColor, Color.clear);
            }
            else if (req.type == RequirementType.CustomFlag)
            {
                bool met = QuestManager.Instance != null && QuestManager.Instance.HasQuestFlag(req.targetString);
                string text = $"{req.targetString}";
                AddIconRow(_reqList, null, text, met ? CodeUI.PositiveColor : CodeUI.NegativeColor, Color.clear);
            }
        }
    }

    private void BuildRewards(QuestSO quest)
    {
        ClearChildren(_rewardList);

        if (quest.rewards == null || quest.rewards.Count == 0)
        {
            var none = CodeUI.CreateText(_rewardList, "None", 16f, FontStyles.Italic, CodeUI.MutedColor,
                TextAlignmentOptions.MidlineLeft);
            none.text = CodeUI.L("quest_no_rewards", "보상이 없습니다.");
            none.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            return;
        }

        foreach (var reward in quest.rewards)
        {
            Color accent = reward.rewardType == QuestRewardType.Money ? CodeUI.GoldColor : CodeUI.ItemColor;
            AddIconRow(_rewardList, reward.GetRewardIcon(), reward.GetRewardDescription(), CodeUI.LabelColor, accent);
        }
    }

    /// <summary>아이콘 + 텍스트 한 줄. 아이콘이 없으면(돈 보상 등) 색 점으로 대체.</summary>
    private void AddIconRow(Transform parent, Sprite icon, string text, Color textColor, Color accent)
    {
        var rowBg = CodeUI.CreateImage(parent, "Row", CodeUI.SlotBg, skin.slotSprite, skin);
        rowBg.gameObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;
        var layout = rowBg.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 12, 6, 6);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var iconBox = CodeUI.CreateImage(rowBg.transform, "IconBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = rowHeight - 12f;
        iconLe.preferredHeight = rowHeight - 12f;

        if (icon != null)
        {
            var img = CodeUI.CreateImage(iconBox.transform, "Icon", Color.white, rounded: false);
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var ir = img.rectTransform;
            ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
            ir.offsetMin = new Vector2(4f, 4f); ir.offsetMax = new Vector2(-4f, -4f);
        }
        else
        {
            var dot = CodeUI.CreateImage(iconBox.transform, "Dot", accent);
            var dr = dot.rectTransform;
            dr.anchorMin = new Vector2(0.5f, 0.5f); dr.anchorMax = new Vector2(0.5f, 0.5f);
            dr.pivot = new Vector2(0.5f, 0.5f);
            dr.sizeDelta = new Vector2(20f, 20f);
            dot.raycastTarget = false;
        }

        // 매 갱신마다 새로 만드는 행이라 binder(_loc)에 묶지 않는다 — 폰트는 생성 시점에 이미 적용되고,
        // 언어 변경 시엔 RefreshAll이 행을 통째로 다시 만든다. (binder에 쌓이면 파괴된 참조가 남는다)
        var label = CodeUI.CreateText(rowBg.transform, "Label", 18f, FontStyles.Normal, textColor,
            TextAlignmentOptions.MidlineLeft);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        label.text = text;
    }

    private static void ClearChildren(RectTransform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    // ===================================================
    // 제출
    // ===================================================
    private void OnSubmit()
    {
        if (!_allowSubmission) return;

        var qm = QuestManager.Instance;
        var quest = CurrentTabQuest();
        if (qm == null || quest == null) return;

        if (qm.GetQuestStatus(quest.questID) != QuestStatus.Accepted) return;
        if (!qm.CheckRequirements(quest))
        {
            Toast("quest_status_progress", "진행 중", CodeUI.WarnColor);
            _needsRefresh = true;
            return;
        }

        CodeUI.PlaySfx(clickSfxName);
        qm.CompleteQuest(quest);

        // 오버레이를 닫은 뒤 완료 대화(있으면)를 재생 — 프리팹 제출 흐름과 동일
        Close();
        var dlg = FindFirstObjectByType<QuestDialogueUI>(FindObjectsInactive.Include);
        if (dlg != null) dlg.ShowQuestDialogue(quest);
    }

    // ===================================================
    // 토스트
    // ===================================================
    private void Toast(string key, string fallback, Color color)
    {
        if (_toastText == null) return;
        _toastText.text = CodeUI.L(key, fallback);
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
