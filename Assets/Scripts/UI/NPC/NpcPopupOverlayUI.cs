// @tags: npc, popup, ui, overlay, code-generated, truck, shop, upgrade, quest, dialogue
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// NPC 머리 위 팝업(상점·업그레이드·퀘스트·대화)을 <b>코드로 생성</b>한 오버레이.
/// 기존 프리팹 기반 <see cref="NpcPopup"/>과 같은 역할·분기 로직이지만, 씬/프리팹 세팅 없이
/// 다른 코드 오버레이(SubQuestBoardOverlayUI 등)와 같은 패턴으로 동작한다.
///
/// UIStateManager의 <c>useCodeBuiltPopupUI</c>가 켜져 있으면 <see cref="TruckNpcBehaviour"/>가
/// 프리팹 대신 이 오버레이를 띄운다. 위치는 대상 NPC의 머리 위(월드→스크린)를 매 프레임 따라간다.
/// 자동 종료(거리 벗어남)는 UIStateManager가 <c>UIState.Popup</c> 소스로 처리한다.
/// </summary>
public class NpcPopupOverlayUI : MonoBehaviour
{
    // ─────────────────────────── 싱글톤 ───────────────────────────
    private static NpcPopupOverlayUI _instance;

    public static NpcPopupOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<NpcPopupOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("NpcPopupOverlayUI");
                    _instance = go.AddComponent<NpcPopupOverlayUI>();
                }
            }
            return _instance;
        }
    }

    public static bool IsOpen => _instance != null && _instance._isOpen;
    public static bool ClosedThisFrame => _instance != null && _instance._lastCloseFrame == Time.frameCount;

    // ─────────────────────────── 인스펙터 ───────────────────────────
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [Tooltip("이 팝업이 쓰는 것은 panelSprite(팝업 배경) · buttonSprite(메뉴 버튼) 두 개다. 나머지 항목은 무시된다.")]
    [SerializeField] private UISkin skin = new UISkin();
    [Tooltip("선택 강조: W/S 포커스 또는 마우스 호버가 올라간 버튼의 배경. 비우면 buttonSprite 그대로 두고 색만 밝아진다")]
    [SerializeField] private Sprite focusSprite;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 240f;
    [SerializeField] private float buttonHeight = 56f;
    [SerializeField] private int sortingOrder = 30470;

    [Header("키보드 조작 (설정 오버레이와 같은 규칙. None이면 비활성)")]
    [Tooltip("위 항목으로 이동")]
    [SerializeField] private KeyCode navUpKey = KeyCode.W;
    [Tooltip("아래 항목으로 이동")]
    [SerializeField] private KeyCode navDownKey = KeyCode.S;
    [Tooltip("포커스된 항목 실행 (UI 확인 키는 스페이스바로 통일)")]
    [SerializeField] private KeyCode confirmKey = KeyCode.Space;
    [Tooltip("꾹 눌렀을 때 연속 이동이 시작되기까지의 시간(초)")]
    [SerializeField] private float navRepeatDelay = 0.35f;
    [Tooltip("연속 이동 간격(초)")]
    [SerializeField] private float navRepeatInterval = 0.1f;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;

    // ─────────────────────────── 내부 상태 ───────────────────────────
    private bool _built, _isOpen;
    private int _lastCloseFrame = -1;

    private INpcPopupSource _npc;

    private GameObject _canvasObj;
    private RectTransform _panel;
    private TextMeshProUGUI _nameText;
    private Button _shopButton, _upgradeButton, _questButton, _talkButton, _closeButton;
    private GameObject _questIndicator;

    private bool _langSubscribed;

    // 키보드 포커스 — 화면에 보이는 버튼만 위에서 아래 순서로 담는다(ShowInternal에서 재구성).
    private readonly System.Collections.Generic.List<Button> _nav = new System.Collections.Generic.List<Button>();
    private readonly System.Collections.Generic.Dictionary<Button, Color> _navBaseBg =
        new System.Collections.Generic.Dictionary<Button, Color>();
    private int _navIndex;
    private KeyCode _navHeldKey = KeyCode.None;
    private float _navNextRepeat;
    // 팝업을 연 그 프레임의 E까지 먹으면 열자마자 첫 항목이 실행된다(E = 상호작용 키).
    private int _openFrame = -1;

    /// <summary>대상이 살아 있는가. 인터페이스는 <c>== null</c>이 Unity 파괴 판정을 못 타므로 Transform으로 확인.</summary>
    private bool HasNpc => _npc != null && _npc.SourceTransform != null;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        // UI 확인 키를 스페이스바로 통일(구 E). confirmKey는 [SerializeField]라
        // 이미 씬·프리팹에 E로 직렬화된 인스턴스가 있으면 기본값을 바꿔도 그대로 남는다 — 여기서 마이그레이션한다.
        if (confirmKey == KeyCode.E) confirmKey = KeyCode.Space;

        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_langSubscribed && LanguageManager.Instance != null)
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!_isOpen) return;
        HideCanvas();
    }

    // ─────────────────────────── 열기 / 닫기 ───────────────────────────

    /// <summary>대상 NPC의 팝업을 띄운다(=기존 NpcPopup.ShowPopup).</summary>
    public static void Show(INpcPopupSource npc)
    {
        if (npc == null || npc.SourceTransform == null) return;
        Instance.ShowInternal(npc);
    }

    public static void CloseStatic()
    {
        if (_instance != null) _instance.Close();
    }

    private void ShowInternal(INpcPopupSource npc)
    {
        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        TrySubscribeLanguage();

        _npc = npc;
        _isOpen = true;

        if (_nameText != null) _nameText.text = npc.NpcName;

        SetButtonActive(_shopButton, npc.CanOpenShop);
        SetButtonActive(_upgradeButton, npc.CanOpenUpgrade);
        SetButtonActive(_questButton, npc.MainQuest != null || npc.IsMainQuestNpc);
        SetButtonActive(_talkButton, npc.Dialogue != null || npc.MainQuest != null);

        UpdateMainQuestIndicator(npc);
        RebuildNav();

        if (UIStateManager.Instance != null)
            UIStateManager.Instance.SetState(UIState.Popup, npc.SourceTransform);

        _canvasObj.SetActive(true);
        UpdatePosition();
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _lastCloseFrame = Time.frameCount;
        _npc = null;
        HideCanvas();

        var ui = UIStateManager.Instance;
        if (ui != null && ui.CurrentState == UIState.Popup) ui.SetState(UIState.None);
    }

    private void HideCanvas()
    {
        _isOpen = false;
        _npc = null;
        if (_canvasObj != null) _canvasObj.SetActive(false);
    }

    private void Update()
    {
        if (!_isOpen) return;

        // UIStateManager 상태가 Popup을 벗어났다면 즉시 숨긴다(상점/업그레이드/대화로 전환 등).
        if (UIStateManager.Instance == null || UIStateManager.Instance.CurrentState != UIState.Popup)
        {
            HideCanvas();
            return;
        }

        if (!HasNpc)
        {
            Close();
            return;
        }

        // 포커스는 이 스크립트가 직접 관리한다. EventSystem 선택이 남아 있으면
        // Space 한 번에 마지막으로 클릭한 버튼까지 같이 눌린다(Space = Submit).
        CodeUI.ClearSelection();

        HandleNavInput();
    }

    // ─────────────────────────── 키보드 포커스 (설정 오버레이와 같은 규칙) ───────────────────────────

    /// <summary>지금 화면에 보이는 버튼만 위→아래 순서로 모아 포커스 목록을 다시 만든다.</summary>
    private void RebuildNav()
    {
        _nav.Clear();
        AddNav(_talkButton);
        AddNav(_questButton);
        AddNav(_shopButton);
        AddNav(_upgradeButton);
        AddNav(_closeButton);

        _navIndex = 0;
        _navHeldKey = KeyCode.None;
        _openFrame = Time.frameCount;
        ApplyNavVisual();

        void AddNav(Button b)
        {
            if (b != null && b.gameObject.activeSelf) _nav.Add(b);
        }
    }

    /// <summary>W/S 이동(꾹 누르면 반복, 순환 없음) + Space 실행.</summary>
    private void HandleNavInput()
    {
        if (_nav.Count == 0) return;

        // 팝업을 연 프레임의 입력은 무시 — NPC에게 말을 건 그 입력이지 메뉴 선택이 아니다.
        if (confirmKey != KeyCode.None && Time.frameCount != _openFrame && Input.GetKeyDown(confirmKey))
        {
            ActivateFocused();
            return;
        }

        int dir = 0;
        if (navUpKey != KeyCode.None && Input.GetKeyDown(navUpKey)) dir = -1;
        else if (navDownKey != KeyCode.None && Input.GetKeyDown(navDownKey)) dir = +1;

        if (dir != 0)
        {
            _navHeldKey = dir < 0 ? navUpKey : navDownKey;
            _navNextRepeat = Time.unscaledTime + navRepeatDelay;
            MoveNav(dir);
            return;
        }

        if (_navHeldKey == KeyCode.None) return;
        if (!Input.GetKey(_navHeldKey)) { _navHeldKey = KeyCode.None; return; }
        if (Time.unscaledTime < _navNextRepeat) return;

        _navNextRepeat = Time.unscaledTime + navRepeatInterval;
        MoveNav(_navHeldKey == navUpKey ? -1 : +1);
    }

    /// <summary>끝에서는 제자리(설정 오버레이와 같이 순환하지 않는다).</summary>
    private void MoveNav(int dir)
    {
        int next = Mathf.Clamp(_navIndex + dir, 0, _nav.Count - 1);
        if (next == _navIndex) return;

        _navIndex = next;
        CodeUI.PlaySfx(clickSfxName);
        ApplyNavVisual();
    }

    private void ActivateFocused()
    {
        if (_navIndex < 0 || _navIndex >= _nav.Count) return;
        var btn = _nav[_navIndex];
        // onClick을 그대로 태워 마우스 클릭과 완전히 같은 경로를 탄다(클릭음·상태 전환 포함).
        if (btn != null) btn.onClick.Invoke();
    }

    /// <summary>마우스가 버튼 위에 올라오면 키보드 포커스도 그리로 옮긴다(설정 오버레이와 동일).</summary>
    internal void FocusFromPointer(Button btn)
    {
        int i = _nav.IndexOf(btn);
        if (i < 0 || i == _navIndex) return;
        _navIndex = i;
        _navHeldKey = KeyCode.None;
        ApplyNavVisual();   // 소리는 내지 않는다 — 마우스가 스쳐 지나가도 딸깍거리지 않게
    }

    /// <summary>포커스된 버튼만 강조 배경으로 바꾼다. focusSprite가 있으면 스프라이트까지 교체.</summary>
    private void ApplyNavVisual()
    {
        for (int i = 0; i < _nav.Count; i++)
        {
            var btn = _nav[i];
            if (btn == null || btn.image == null) continue;

            bool focused = i == _navIndex;
            Color baseBg = _navBaseBg.TryGetValue(btn, out var c) ? c : CodeUI.NeutralBg;
            // 고정 강조색을 쓰면 원래 색이 이미 그 색인 버튼(대화=AccentFill)에서 차이가 안 난다.
            // 자기 색을 밝히는 방식이라 버튼마다 정체성을 유지하면서도 항상 눈에 띈다.
            Color bg = focused ? Color.Lerp(baseBg, Color.white, 0.35f) : baseBg;
            Sprite sprite = focused && focusSprite != null ? focusSprite : skin.buttonSprite;

            CodeUI.ApplySkin(btn.image, bg, sprite, skin);
        }
    }

    private void LateUpdate()
    {
        if (_isOpen && HasNpc) UpdatePosition();
    }

    /// <summary>NPC 월드 좌표 + 오프셋을 스크린 좌표로 변환해 팝업 위치 갱신.</summary>
    private void UpdatePosition()
    {
        if (_panel == null || !HasNpc) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 worldPos = _npc.SourceTransform.position + _npc.PopupOffset;
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

        if (screenPos.z < 0f)
        {
            // NPC가 카메라 뒤 — 화면 밖으로 밀어 숨김(상태는 유지).
            _panel.gameObject.SetActive(false);
            return;
        }

        if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);
        _panel.position = screenPos;
    }

    // ─────────────────────────── 버튼 동작 (기존 NpcPopup과 동일) ───────────────────────────

    private void OnShopClicked()
    {
        CodeUI.PlaySfx(clickSfxName);
        if (UIStateManager.Instance != null)
            UIStateManager.Instance.SetState(UIState.Shop, HasNpc ? _npc.SourceTransform : null);
    }

    private void OnUpgradeClicked()
    {
        CodeUI.PlaySfx(clickSfxName);
        if (UIStateManager.Instance != null)
            UIStateManager.Instance.SetState(UIState.Upgrade, HasNpc ? _npc.SourceTransform : null);
    }

    private void OnQuestClicked()
    {
        if (!HasNpc) return;
        CodeUI.PlaySfx(clickSfxName);

        Transform source = _npc.SourceTransform;
        HideCanvas();

        // 코드 생성 퀘스트 오버레이를 쓰는 씬이면 '제출 가능' 모드로 그쪽을 연다.
        if (UIStateManager.Instance != null && UIStateManager.Instance.useCodeBuiltQuestUI)
        {
            QuestOverlayUI.RequestSubmission();
            UIStateManager.Instance.SetState(UIState.Quest, source);
            return;
        }

        // 프리팹 퀘스트 패널을 '제출 가능' 모드로 연다.
        QuestPanelUI questPanel = FindFirstObjectByType<QuestPanelUI>();
        if (questPanel != null)
        {
            questPanel.OpenPanel(true, source);
        }
        else if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.Quest, source);
            var panel = FindFirstObjectByType<QuestPanelUI>();
            if (panel != null) panel.RefreshLog(true);
        }
    }

    private void OnTalkClicked()
    {
        if (!HasNpc) return;
        CodeUI.PlaySfx(clickSfxName);

        QuestDialogueUI dialogueUI = _npc.DialogueUI;
        if (dialogueUI == null) dialogueUI = FindFirstObjectByType<QuestDialogueUI>(FindObjectsInactive.Include);
        if (dialogueUI == null)
        {
            Debug.LogError("[NpcPopupOverlay] QuestDialogueUI를 찾을 수 없습니다!");
            return;
        }

        QuestSO questToTalk = _npc.MainQuest;
        
        // 메인 퀘스트 전담 NPC라면 현재 활성화된 메인 퀘스트를 자동으로 가져옴
        if (QuestManager.Instance != null && _npc.IsMainQuestNpc)
        {
            QuestSO activeQuest = QuestManager.Instance.GetActiveMainQuest();
            if (activeQuest != null) 
            {
                questToTalk = activeQuest;
            }
        }

        if (questToTalk != null && QuestManager.Instance != null)
        {
            QuestStatus status = QuestManager.Instance.GetQuestStatus(questToTalk.questID);
            if (status == QuestStatus.Available || status == QuestStatus.Accepted)
            {
                // source를 넘겨 대화를 닫을 때 이 NPC의 팝업으로 되돌아오게 한다.
                dialogueUI.ShowQuestDialogue(questToTalk, _npc.SourceTransform);
                return;
            }
        }

        if (_npc.Dialogue != null)
            dialogueUI.ShowDialogue(_npc.Dialogue, _npc.SourceTransform);
        else
            Debug.LogWarning("[NpcPopupOverlay] 출력할 대화 데이터가 없습니다.");
    }

    /// <summary>메인 퀘스트 진행 가능 여부에 따라 느낌표 아이콘 표시(기존 NpcPopup과 동일 규칙).</summary>
    private void UpdateMainQuestIndicator(INpcPopupSource npc)
    {
        bool hasAvailable = false;

        if (QuestManager.Instance != null && npc != null)
        {
            QuestSO questToCheck = npc.MainQuest;
            
            // 메인 퀘스트 전담 NPC라면 현재 활성화된 메인 퀘스트 우선 확인
            if (npc.IsMainQuestNpc)
            {
                QuestSO activeQuest = QuestManager.Instance.GetActiveMainQuest();
                if (activeQuest != null) questToCheck = activeQuest;
            }

            if (questToCheck != null)
            {
                QuestStatus status = QuestManager.Instance.GetQuestStatus(questToCheck.questID);
                if (status == QuestStatus.Available || (status == QuestStatus.Accepted && QuestManager.Instance.CheckRequirements(questToCheck)))
                {
                    hasAvailable = true;
                }
            }
        }

        if (_questIndicator != null) _questIndicator.SetActive(hasAvailable);
    }

    // ─────────────────────────── UI 생성 ───────────────────────────

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("NpcPopupOverlayCanvas");
        _canvasObj.transform.SetParent(transform, false);
        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasObj.AddComponent<GraphicRaycaster>();

        // 머리 위를 따라다니는 작은 패널 (피벗 아래-가운데 → NPC 위로 뜬다)
        var panelImg = CodeUI.CreateImage(_canvasObj.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        _panel = panelImg.rectTransform;
        _panel.pivot = new Vector2(0.5f, 0f);
        _panel.sizeDelta = new Vector2(panelWidth, 10f);

        var layout = panelImg.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panelImg.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 이름
        _nameText = CodeUI.CreateText(_panel, "Name", 24f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        _nameText.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        CodeUI.CreateDivider(_panel);

        // 버튼들
        _talkButton = CreateMenuButton("Talk", CodeUI.L("npc_talk", "대화"), CodeUI.AccentFill, out _questIndicator, OnTalkClicked);
        _questButton = CreateMenuButton("Quest", CodeUI.L("npc_quest", "퀘스트"), CodeUI.QuestColor, out _, OnQuestClicked);
        _shopButton = CreateMenuButton("Shop", CodeUI.L("npc_shop", "상점"), CodeUI.NeutralBg, out _, OnShopClicked);
        _upgradeButton = CreateMenuButton("Upgrade", CodeUI.L("npc_upgrade", "업그레이드"), CodeUI.NeutralBg, out _, OnUpgradeClicked);
        _closeButton = CreateMenuButton("Close", CodeUI.L("npc_close", "닫기"), CodeUI.BoxBg, out _, Close);

        _canvasObj.SetActive(false);
    }

    /// <summary>메뉴 버튼 한 개(라벨 + 선택적 느낌표 아이콘).</summary>
    private Button CreateMenuButton(string name, string label, Color bg, out GameObject indicator, System.Action onClick)
    {
        Button btn = CodeUI.CreateTextButton(_panel, name, bg, Color.white, 20f, onClick, out _, skin.buttonSprite, skin);
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = buttonHeight;
        SetButtonLabel(btn, label);

        // 포커스 해제 시 되돌릴 원래 배경색을 기억해 둔다.
        _navBaseBg[btn] = bg;
        btn.gameObject.AddComponent<NpcPopupNavHover>().Bind(this, btn);

        // 우상단 느낌표 아이콘(퀘스트 있음 표시). 기본 숨김.
        var dot = CodeUI.CreateText(btn.transform, "QuestMark", 22f, FontStyles.Bold, CodeUI.GoldColor,
            TextAlignmentOptions.Center);
        var rt = dot.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-6f, -2f);
        rt.sizeDelta = new Vector2(24f, 24f);
        dot.text = "!";
        dot.raycastTarget = false;
        indicator = dot.gameObject;
        indicator.SetActive(false);

        return btn;
    }

    private static void SetButtonLabel(Button btn, string text)
    {
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = text;
    }

    private static void SetButtonActive(Button btn, bool active)
    {
        if (btn != null) btn.gameObject.SetActive(active);
    }

    // ─────────────────────────── 언어 ───────────────────────────

    private void TrySubscribeLanguage()
    {
        if (_langSubscribed || LanguageManager.Instance == null) return;
        LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
        _langSubscribed = true;
    }

    private void OnLanguageChanged(LanguageType _)
    {
        if (!_built) return;

        // 이름도 번역 대상이다(WorldInteractable.NpcName이 키를 조회해 돌려준다).
        if (_nameText != null && HasNpc) _nameText.text = _npc.NpcName;

        SetButtonLabel(_talkButton, CodeUI.L("npc_talk", "대화"));
        SetButtonLabel(_questButton, CodeUI.L("npc_quest", "퀘스트"));
        SetButtonLabel(_shopButton, CodeUI.L("npc_shop", "상점"));
        SetButtonLabel(_upgradeButton, CodeUI.L("npc_upgrade", "업그레이드"));
        SetButtonLabel(_closeButton, CodeUI.L("npc_close", "닫기"));
    }
}

/// <summary>
/// NPC 팝업 버튼에 붙어 마우스가 올라오면 키보드 포커스를 그 버튼으로 옮긴다.
/// IPointerEnterHandler '만' 구현하는 이유는 SettingsNavHover와 같다 — 드래그·휠까지 받으면
/// 그 이벤트가 상위로 올라가지 못한다. (NpcPopupOverlayUI.CreateMenuButton이 런타임에 AddComponent)
/// </summary>
internal class NpcPopupNavHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler
{
    private NpcPopupOverlayUI _owner;
    private Button _button;

    internal void Bind(NpcPopupOverlayUI owner, Button button)
    {
        _owner = owner;
        _button = button;
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_owner != null) _owner.FocusFromPointer(_button);
    }
}
