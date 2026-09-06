// @tags: ui, code-generated, save, slot, mainmenu, continue, newgame, overlay, blur, localization

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 저장 슬롯 선택 오버레이 — 메인 화면의 "이어하기(Continue)"·"새 게임(Start)"에서 호출된다.
///
/// 두 가지 모드로 동작한다:
///  - <see cref="SlotMode.Continue"/> : 데이터가 있는 슬롯을 골라 이어하기(빈 슬롯은 비활성).
///  - <see cref="SlotMode.NewGame"/>  : 슬롯을 골라 새로 시작. 이미 데이터가 있으면 덮어쓰기 확인.
///
/// UI는 전부 코드로 생성(SettingsOverlayUI / PauseOverlayUI 패턴) — 씬/프리팹 세팅 없이
/// <see cref="ShowContinue"/> / <see cref="ShowNewGame"/> 호출만으로 동작한다.
/// CodeUI 킷(팔레트·라운드 스프라이트·블러 배경)을 공유해 다른 오버레이와 한 벌로 보인다.
///
/// 텍스트는 UI_Localization.csv의 ui_saveslot_* / ui_menu_* 키로 언어별 전환된다.
///
/// [z-order] 메인메뉴는 로고·구름이 SpriteRenderer/하위 캔버스라, 이 오버레이 캔버스는
/// overrideSorting + 높은 sortingOrder로 항상 위에 그린다. Awake에서 루트로 분리해
/// 부모 캔버스의 정렬에 끌려 내려가지 않게 한다.
///
/// [마이그레이션] 구버전 SaveSlotUICanvas + SaveSlotItemUI + ConfirmationPrompt는 삭제해도 된다.
/// 남아 있어도 Build 시점에 이 컴포넌트가 붙은 오브젝트의 기존 자식을 꺼서 중복 표시를 막는다.
/// </summary>
public class SaveSlotUI : MonoBehaviour
{
    public enum SlotMode { Continue, NewGame }

    // ===================================================
    // 싱글톤
    // ===================================================
    private static SaveSlotUI _instance;

    public static SaveSlotUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SaveSlotUI>();
                if (_instance == null)
                {
                    var go = new GameObject("SaveSlotUI");
                    _instance = go.AddComponent<SaveSlotUI>();
                }
            }
            return _instance;
        }
    }

    public static bool IsOpen => _instance != null && _instance._isOpen;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        // 부모 캔버스 정렬에 끌려가지 않도록 루트로 분리 (SettingsOverlayUI와 동일)
        transform.SetParent(null);

        // 구버전 씬 오브젝트에 부착된 경우: 기존 자식(레거시 슬롯/확인창)을 즉시 꺼서
        // 메뉴 시작 시 옛 UI가 잠깐 보이거나 중복 표시되는 것을 막는다.
        for (int i = transform.childCount - 1; i >= 0; i--)
            transform.GetChild(i).gameObject.SetActive(false);
    }

    // ===================================================
    // 인스펙터 (선택 — 씬에 미리 배치했을 때만 노출)
    // ===================================================
    [Header("도트 스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.62f;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    // [수정됨] 만화 컷씬과 튜토리얼 씬 이름을 인스펙터에서 설정할 수 있게 추가
    [Header("전환할 게임 씬")]
    [SerializeField] private string gameSceneName = "DemoUpground";
    [SerializeField] private string comicSceneName = "ComicScene";
    [SerializeField] private string tutorialSceneName = "TutorialScene";

    [Tooltip("캔버스 정렬 순서 — 메인메뉴 로고/버튼보다 위")]
    [SerializeField] private int sortingOrder = 30000;

    // ===================================================
    // 레이아웃 상수 (1920x1080 기준 px)
    // ===================================================
    private const float PanelWidth = 920f;
    private const float PanelHeight = 900f;
    private const float RowHeight = 104f;
    private const float FadeDuration = 0.15f;

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built;
    private bool _isOpen;
    private bool _opening;
    private bool _langSubscribed;
    private SlotMode _mode = SlotMode.Continue;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _binder = new LocTextBinder();

    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _hintText;
    private RectTransform _listColumn;
    private Row[] _rows;

    // 확인 모달
    private GameObject _confirmModal;
    private TextMeshProUGUI _confirmTitle;
    private TextMeshProUGUI _confirmMessage;
    private TextMeshProUGUI _confirmYesLabel;
    private Action _confirmAction;
    private Button _confirmYesButton;
    private Button _confirmCancelButton;
    private Button _closeButton;

    // 이름 입력 모달 (새 게임 시작 / 이름 변경 공용)
    private GameObject _nameModal;
    private TextMeshProUGUI _nameTitle;
    private TMP_InputField _nameInput;
    private Action<string> _nameAction;

    private bool IsNameModalOpen => _nameModal != null && _nameModal.activeSelf;

    // 키보드 네비게이션 (W/S 이동 · Space 선택) — 포커스 배경 하이라이트, 마우스 호버와 공유
    private static readonly Color NavFocusBg = CodeUI.SlotHover;
    private readonly List<NavTarget> _nav = new List<NavTarget>();
    private int _navIndex = -1;

    private class NavTarget
    {
        public Button button;
        public Image bg;
        public Color baseColor;
    }

    private class Row
    {
        public Button button;
        public Image bg;
        public TextMeshProUGUI numberText;
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI subText;
        public GameObject renameButtonObj;
        public GameObject deleteButtonObj;
    }

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    /// <summary>이어하기 — 데이터가 있는 슬롯을 골라 로드. (GameManager.ContinueGame)</summary>
    public void ShowContinue() => ShowMode(SlotMode.Continue);

    /// <summary>새 게임 — 슬롯을 골라 새로 시작(점유 슬롯은 덮어쓰기 확인). (GameManager.NewGame)</summary>
    public void ShowNewGame() => ShowMode(SlotMode.NewGame);

    /// <summary>기존 API 호환 — 이어하기 모드로 연다.</summary>
    public void Show() => ShowMode(SlotMode.Continue);

    private void ShowMode(SlotMode mode)
    {
        if (_isOpen || _opening) return;
        _mode = mode;
        if (!isActiveAndEnabled) gameObject.SetActive(true);
        StartCoroutine(ShowRoutine());
    }

    public void Hide()
    {
        if (!_isOpen && !_opening) return;
        _isOpen = false;
        _opening = false;
        HideNamePrompt();
        HideConfirm();
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);
        CodeUI.PlaySfx(null);
    }

    private IEnumerator ShowRoutine()
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        TrySubscribeLanguage();

        // 오버레이가 캡처에 찍히지 않도록 끈 채로 프레임 끝까지 대기 후 배경 캡처
        _canvasObj.SetActive(false);
        yield return new WaitForEndOfFrame();
        _blur.Capture(_blurImage, blurDownsamples, flipBlurVertically);
        _opening = false;

        _binder.Refresh();
        RefreshSlots();

        _canvasObj.SetActive(true);
        _isOpen = true;
        HideConfirm();
        RebuildNav(resetFocus: true);

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

        // 이름 입력 중에는 W/S/Space 네비게이션을 통째로 막는다.
        // 레거시 Input은 InputField가 글자를 먹는 것과 무관하게 키를 그대로 읽으므로,
        // 막지 않으면 이름에 's'를 치는 순간 포커스가 내려가고 스페이스바가 버튼을 누른다.
        if (IsNameModalOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CodeUI.PlayBack();
                HideNamePrompt();
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                ConfirmNamePrompt();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CodeUI.PlayBack();
            if (_confirmModal != null && _confirmModal.activeSelf) HideConfirm();
            else Hide();
            return;
        }

        // W/S(또는 ↑/↓) 이동 — 순환 없음
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            MoveNav(-1);
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            MoveNav(+1);

        // Space(또는 Enter)로 선택
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            ActivateNav();
    }

    private void OnDestroy()
    {
        if (_langSubscribed && LanguageManager.Instance != null)
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
        _blur.Release(_blurImage);
    }

    private void TrySubscribeLanguage()
    {
        if (_langSubscribed || LanguageManager.Instance == null) return;
        LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
        _langSubscribed = true;
    }

    private void OnLanguageChanged(LanguageType _)
    {
        _binder.Refresh();
        if (_isOpen) { RefreshSlots(); RebuildNav(resetFocus: false); }
    }

    // ===================================================
    // 슬롯 갱신 (모드별)
    // ===================================================
    private void RefreshSlots()
    {
        var sm = SaveManager.Instance ?? GameManager.Instance?.saveManager;
        if (sm == null || _rows == null) return;

        // 제목/안내를 모드에 맞게
        if (_titleText != null)
            _titleText.text = _mode == SlotMode.Continue
                ? CodeUI.L("ui_saveslot_title_continue", "이어하기")
                : CodeUI.L("ui_saveslot_title_new", "새 게임");
        if (_hintText != null)
            _hintText.text = _mode == SlotMode.Continue
                ? CodeUI.L("ui_saveslot_hint_continue", "이어할 슬롯을 선택하세요")
                : CodeUI.L("ui_saveslot_hint_new", "새로 시작할 슬롯을 선택하세요");

        SaveSlotSummary[] summaries = sm.GetSlotSummaries();
        for (int i = 0; i < _rows.Length; i++)
        {
            var row = _rows[i];
            if (row == null) continue;
            bool hasData = i < summaries.Length && summaries[i].hasData;

            row.numberText.text = (i + 1).ToString();

            if (hasData)
            {
                var s = summaries[i];
                string time = s.time == TimeOfDay.Morning
                    ? CodeUI.L("ui_saveslot_time_morning", "오전")
                    : CodeUI.L("ui_saveslot_time_afternoon", "오후");

                // 1줄 = 세이브 이름(없으면 "슬롯 N"), 2줄 = 진행 상황 요약
                row.titleText.text = SlotLabel(i, s.name);
                row.titleText.color = s.name != null ? CodeUI.LabelColor : CodeUI.MutedColor;
                
                if (s.isTutorialCompleted)
                {
                    row.subText.text = $"{Lf("ui_saveslot_day_fmt", "Day {0}", s.day)}  ·  {time}  ·  " +
                                       $"<color=#{ColorUtility.ToHtmlStringRGB(CodeUI.GoldColor)}>{CodeUI.Gold(s.gold)} G</color>";
                }
                else
                {
                    row.subText.text = CodeUI.L("ui_saveslot_tutorial_progress", "튜토리얼 진행중");
                }
                
                row.subText.color = CodeUI.MutedColor;
                row.subText.gameObject.SetActive(true);
                row.renameButtonObj.SetActive(true);
                row.deleteButtonObj.SetActive(true);

                row.button.interactable = true;
                row.numberText.color = CodeUI.AccentFill;
            }
            else
            {
                row.titleText.text = _mode == SlotMode.NewGame
                    ? CodeUI.L("ui_saveslot_empty_new", "비어 있음 — 새 게임 시작")
                    : CodeUI.L("ui_saveslot_empty", "비어 있음");
                row.titleText.color = CodeUI.MutedColor;
                row.subText.gameObject.SetActive(false);
                row.renameButtonObj.SetActive(false);
                row.deleteButtonObj.SetActive(false);

                // 이어하기 모드에선 빈 슬롯 비활성, 새 게임 모드에선 선택 가능
                row.button.interactable = _mode == SlotMode.NewGame;
                row.numberText.color = _mode == SlotMode.NewGame ? CodeUI.AccentFill : CodeUI.DividerColor;
            }
        }

        // 상호작용 가능 슬롯이 바뀌었을 수 있으니 네비 목록 갱신
        if (_isOpen && (_confirmModal == null || !_confirmModal.activeSelf))
            RebuildNav(resetFocus: false);
    }

    // ===================================================
    // 슬롯 클릭 처리 (모드별)
    // ===================================================
    private void OnSlotClicked(int slotIndex)
    {
        var sm = SaveManager.Instance ?? GameManager.Instance?.saveManager;
        if (sm == null) return;

        bool hasData = sm.GetSlotSummaries()[slotIndex].hasData;

        if (_mode == SlotMode.Continue)
        {
            if (hasData) LoadGame(slotIndex);
            // 빈 슬롯은 버튼이 비활성이라 여기 오지 않는다.
            return;
        }

        // NewGame 모드
        if (hasData)
        {
            ShowConfirm(
                CodeUI.L("ui_saveslot_overwrite_title", "새 게임"),
                Lf("ui_saveslot_overwrite_msg_fmt", "{0}에 저장 데이터가 있습니다. 덮어쓰고 새로 시작할까요?",
                    SlotLabel(slotIndex, sm.GetSlotSummaries()[slotIndex].name)),
                CodeUI.L("ui_saveslot_yes_overwrite", "덮어쓰기"),
                () => PromptNewGameName(slotIndex));
        }
        else
        {
            PromptNewGameName(slotIndex);
        }
    }

    private void OnDeleteClicked(int slotIndex)
    {
        var sm = SaveManager.Instance ?? GameManager.Instance?.saveManager;
        if (sm == null) return;

        ShowConfirm(
            CodeUI.L("ui_saveslot_delete_title", "데이터 삭제"),
            Lf("ui_saveslot_delete_msg_fmt", "{0} 데이터를 삭제할까요? 되돌릴 수 없습니다.",
                SlotLabel(slotIndex, sm.GetSlotSummaries()[slotIndex].name)),
            CodeUI.L("ui_saveslot_yes_delete", "삭제"),
            () =>
            {
                sm.DeleteSave(slotIndex);
                RefreshSlots();
            });
    }

    /// <summary>이름 변경 — 현재 이름을 채운 입력창을 띄우고, 확인 시 파일의 이름만 갈아끼운다.</summary>
    private void OnRenameClicked(int slotIndex)
    {
        var sm = SaveManager.Instance ?? GameManager.Instance?.saveManager;
        if (sm == null) return;

        string current = sm.GetSlotSummaries()[slotIndex].name;
        ShowNamePrompt(
            CodeUI.L("ui_saveslot_rename_title", "이름 변경"),
            current,
            newName =>
            {
                sm.RenameSlot(slotIndex, newName);
                RefreshSlots();
            });
    }

    /// <summary>새 게임 — 세이브 이름을 먼저 물어보고, 확인해야 실제로 슬롯을 만든다.</summary>
    private void PromptNewGameName(int slotIndex)
    {
        ShowNamePrompt(
            CodeUI.L("ui_saveslot_name_title", "세이브 이름"),
            null,
            newName => StartNewGame(slotIndex, newName));
    }

    /// <summary>표시용 슬롯 이름 — 붙인 이름이 없으면 "슬롯 N"으로 폴백한다.</summary>
    private static string SlotLabel(int slotIndex, string savedName)
    {
        return string.IsNullOrEmpty(savedName)
            ? $"{CodeUI.L("ui_saveslot_name", "슬롯")} {slotIndex + 1}"
            : savedName;
    }

    // [수정됨] 튜토리얼 완료 여부를 체크하여 씬을 강제 분기합니다.
    private void LoadGame(int slotIndex)
    {
        var sm = SaveManager.Instance ?? GameManager.Instance?.saveManager;
        if (sm == null) return;

        PlayerPrefs.SetInt("LastPlayedSlot", slotIndex);
        PlayerPrefs.Save();

        sm.CurrentSlotIndex = slotIndex;
        sm.Load(); // 씬 전환 전 데이터를 미리 메모리에 로드 (안전성 확보)

        Hide();

        // 튜토리얼 클리어 여부에 따라 씬 분기
        if (sm.playerData != null && !sm.playerData.isTutorialCompleted)
        {
            Debug.Log("[SaveSlotUI] 튜토리얼 미완료 데이터입니다. 튜토리얼 씬으로 강제 이동합니다.");
            SceneLoader.LoadScene(tutorialSceneName);
        }
        else
        {
            SceneLoader.LoadScene(gameSceneName);
        }
    }

    // [수정됨] 새 게임 시 본게임(gameSceneName)이 아닌 컷씬(comicSceneName)으로 이동합니다.
    private void StartNewGame(int slotIndex, string slotName)
    {
        var sm = SaveManager.Instance ?? GameManager.Instance?.saveManager;
        if (sm == null) return;

        PlayerPrefs.SetInt("LastPlayedSlot", slotIndex);
        PlayerPrefs.Save();

        sm.CurrentSlotIndex = slotIndex;
        Hide();

        sm.NewGame(slotName);

        // 새 게임은 항상 만화 컷씬부터 시작
        SceneLoader.LoadScene(comicSceneName);
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        // ── 캔버스 (overrideSorting으로 로고 위에 확실히 그린다) ──
        _canvasObj = new GameObject("SaveSlotCanvas");
        _canvasObj.transform.SetParent(transform, false);
        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasObj.AddComponent<GraphicRaycaster>();
        _canvasGroup = _canvasObj.AddComponent<CanvasGroup>();

        // ── 블러 배경 ──
        var blurObj = new GameObject("BlurBackdrop");
        blurObj.transform.SetParent(_canvasObj.transform, false);
        _blurImage = blurObj.AddComponent<RawImage>();
        CodeUI.StretchFull(_blurImage.rectTransform);
        _blurImage.raycastTarget = true;

        // ── 어둡게 덮는 막 ──
        var dim = CodeUI.CreateImage(_canvasObj.transform, "Dim", new Color(0f, 0f, 0f, dimAlpha), rounded: false);
        CodeUI.StretchFull(dim.rectTransform);

        // ── 패널 ──
        var panel = CodeUI.CreateImage(_canvasObj.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panelRt.anchoredPosition = Vector2.zero;

        var inner = CodeUI.CreateColumn(panel.transform, "Inner", 16f, new RectOffset(32, 32, 28, 26));
        CodeUI.StretchFull(inner);

        // ── 헤더 프레임 (SELECT CHARACTER 스타일 — 테두리 있는 제목 바) ──
        var header = CodeUI.CreateImage(inner, "Header", CodeUI.CardBg, skin.cardSprite, skin);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
        var outline = header.gameObject.AddComponent<Outline>();
        outline.effectColor = CodeUI.AccentFill;
        outline.effectDistance = new Vector2(2f, -2f);
        _titleText = CodeUI.CreateText(header.transform, "Title", 40f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _binder);
        _titleText.characterSpacing = 8f;
        CodeUI.StretchFull(_titleText.rectTransform);
        _binder.Track(_titleText);

        // 안내
        _hintText = CodeUI.CreateText(inner, "Hint", 22f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _binder);
        _hintText.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        _binder.Track(_hintText);

        // 슬롯 목록
        _listColumn = CodeUI.CreateColumn(inner, "List", 12f);
        _listColumn.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

        _rows = new Row[SaveManager.MaxSlots];
        for (int i = 0; i < _rows.Length; i++)
            _rows[i] = BuildRow(_listColumn, i);

        // 닫기 버튼
        var closeBtn = CodeUI.CreateTextButton(inner, "CloseButton", CodeUI.NeutralBg, CodeUI.LabelColor, 26f,
            () => { CodeUI.PlaySfx(null); Hide(); }, out var closeLabel, skin.buttonSprite, skin, _binder);
        _binder.Bind(closeLabel, "ui_saveslot_close", "닫기");
        closeBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
        _closeButton = closeBtn;
        AddNavHover(closeBtn);

        BuildConfirmModal();
        BuildNameModal();
        _canvasObj.SetActive(false);
    }

    private Row BuildRow(Transform parent, int index)
    {
        var row = new Row();

        var btn = CodeUI.CreateButton(parent, $"Slot{index + 1}", CodeUI.SlotBg,
            () => { CodeUI.PlaySfx(null); OnSlotClicked(index); }, skin.slotSprite, skin);
        row.button = btn;
        row.bg = btn.GetComponent<Image>();
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        AddNavHover(btn);

        // 내부 가로 레이아웃
        var content = CodeUI.CreateRow(btn.transform, "Content", 0f, 18f,
            TextAnchor.MiddleLeft, new RectOffset(20, 16, 8, 8));
        CodeUI.StretchFull(content);

        // 번호 배지 (SELECT CHARACTER의 큰 숫자)
        row.numberText = CodeUI.CreateText(content, "Number", 48f, FontStyles.Bold, CodeUI.AccentFill,
            TextAlignmentOptions.Center, _binder);
        row.numberText.gameObject.AddComponent<LayoutElement>().preferredWidth = 74f;
        _binder.Track(row.numberText);

        // 세로 구분선
        var sep = CodeUI.CreateImage(content, "Sep", CodeUI.DividerColor, rounded: false);
        var sepLe = sep.gameObject.AddComponent<LayoutElement>();
        sepLe.preferredWidth = 2f;
        sepLe.preferredHeight = RowHeight * 0.56f;

        // 정보 (제목 + 부제)
        var infoCol = CodeUI.CreateColumn(content, "Info", 4f);
        var infoLe = infoCol.gameObject.AddComponent<LayoutElement>();
        infoLe.flexibleWidth = 1f;
        // 세로 가운데 정렬
        var infoVlg = infoCol.GetComponent<VerticalLayoutGroup>();
        infoVlg.childAlignment = TextAnchor.MiddleLeft;
        infoVlg.childForceExpandHeight = false;

        row.titleText = CodeUI.CreateText(infoCol, "Line1", 30f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.Left, _binder);
        _binder.Track(row.titleText);

        row.subText = CodeUI.CreateText(infoCol, "Line2", 25f, FontStyles.Bold, CodeUI.GoldColor,
            TextAlignmentOptions.Left, _binder);
        _binder.Track(row.subText);

        // 이름 변경 버튼 — 데이터가 있는 줄에만 보인다.
        // (연필 기호 대신 글자를 쓴다: 도트 폰트에 심볼 글리프가 없으면 두부가 뜬다)
        var renameBtn = CodeUI.CreateTextButton(content, "RenameButton", CodeUI.NeutralBg, CodeUI.LabelColor, 22f,
            () => { CodeUI.PlaySfx(null); OnRenameClicked(index); }, out var renameLabel, skin.buttonSprite, skin, _binder);
        _binder.Bind(renameLabel, "ui_saveslot_rename_short", "이름");
        var renameLe = renameBtn.gameObject.AddComponent<LayoutElement>();
        renameLe.preferredWidth = 84f;
        renameLe.preferredHeight = 60f;
        row.renameButtonObj = renameBtn.gameObject;

        // 삭제 버튼 (빨간 X)
        var delBtn = CodeUI.CreateTextButton(content, "DeleteButton", CodeUI.NegativeColor, Color.white, 28f,
            () => { CodeUI.PlaySfx(null); OnDeleteClicked(index); }, out var delLabel, skin.buttonSprite, skin, _binder);
        if (delLabel != null) delLabel.text = "X";
        var delLe = delBtn.gameObject.AddComponent<LayoutElement>();
        delLe.preferredWidth = 60f;
        delLe.preferredHeight = 60f;
        row.deleteButtonObj = delBtn.gameObject;

        return row;
    }

    // ===================================================
    // 확인 모달 (삭제 / 덮어쓰기 공용)
    // ===================================================
    private void BuildConfirmModal()
    {
        _confirmModal = CodeUI.CreateImage(_canvasObj.transform, "ConfirmModal",
            new Color(0f, 0f, 0f, 0.6f), rounded: false).gameObject;
        CodeUI.StretchFull((RectTransform)_confirmModal.transform);
        _confirmModal.GetComponent<Image>().raycastTarget = true;

        var box = CodeUI.CreateImage(_confirmModal.transform, "Box", CodeUI.CardBg, skin.cardSprite, skin);
        var boxRt = box.rectTransform;
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(600f, 300f);

        var col = CodeUI.CreateColumn(box.transform, "Col", 16f, new RectOffset(30, 30, 26, 24));
        CodeUI.StretchFull(col);

        _confirmTitle = CodeUI.CreateText(col, "Title", 30f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _binder);
        _confirmTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
        _binder.Track(_confirmTitle);

        _confirmMessage = CodeUI.CreateText(col, "Message", 22f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.Center, _binder);
        _confirmMessage.textWrappingMode = TextWrappingModes.Normal;
        _confirmMessage.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        _binder.Track(_confirmMessage);

        var buttonRow = CodeUI.CreateRow(col, "Buttons", 60f, 16f, TextAnchor.MiddleCenter);

        var yesBtn = CodeUI.CreateTextButton(buttonRow, "Confirm", CodeUI.NegativeColor, Color.white, 24f,
            () =>
            {
                CodeUI.PlaySfx(null);
                var action = _confirmAction;
                HideConfirm();
                action?.Invoke();
            }, out _confirmYesLabel, skin.buttonSprite, skin, _binder);
        var yesLe = yesBtn.gameObject.AddComponent<LayoutElement>();
        yesLe.preferredWidth = 220f; yesLe.flexibleWidth = 1f; yesLe.preferredHeight = 54f;
        _binder.Track(_confirmYesLabel);
        _confirmYesButton = yesBtn;
        AddNavHover(yesBtn);

        var cancelBtn = CodeUI.CreateTextButton(buttonRow, "Cancel", CodeUI.NeutralBg, CodeUI.LabelColor, 24f,
            () => { CodeUI.PlaySfx(null); HideConfirm(); }, out var cancelLabel, skin.buttonSprite, skin, _binder);
        var cancelLe = cancelBtn.gameObject.AddComponent<LayoutElement>();
        cancelLe.preferredWidth = 220f; cancelLe.flexibleWidth = 1f; cancelLe.preferredHeight = 54f;
        _binder.Bind(cancelLabel, "ui_saveslot_cancel", "취소");
        _confirmCancelButton = cancelBtn;
        AddNavHover(cancelBtn);

        _confirmModal.SetActive(false);
    }

    private void ShowConfirm(string title, string message, string yesLabel, Action onConfirm)
    {
        if (_confirmModal == null) return;
        _confirmAction = onConfirm;
        if (_confirmTitle != null) _confirmTitle.text = title;
        if (_confirmMessage != null) _confirmMessage.text = message;
        if (_confirmYesLabel != null) _confirmYesLabel.text = yesLabel;
        _confirmModal.transform.SetAsLastSibling();
        _confirmModal.SetActive(true);
        RebuildNav(resetFocus: true);
    }

    private void HideConfirm()
    {
        _confirmAction = null;
        if (_confirmModal != null) _confirmModal.SetActive(false);
        if (_isOpen) RebuildNav(resetFocus: true);
    }

    // ===================================================
    // 이름 입력 모달 (새 게임 / 이름 변경 공용)
    // ===================================================
    private void BuildNameModal()
    {
        _nameModal = CodeUI.CreateImage(_canvasObj.transform, "NameModal",
            new Color(0f, 0f, 0f, 0.6f), rounded: false).gameObject;
        CodeUI.StretchFull((RectTransform)_nameModal.transform);
        _nameModal.GetComponent<Image>().raycastTarget = true;

        var box = CodeUI.CreateImage(_nameModal.transform, "Box", CodeUI.CardBg, skin.cardSprite, skin);
        var boxRt = box.rectTransform;
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(680f, 340f);

        var col = CodeUI.CreateColumn(box.transform, "Col", 14f, new RectOffset(30, 30, 26, 24));
        CodeUI.StretchFull(col);

        _nameTitle = CodeUI.CreateText(col, "Title", 30f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _binder);
        _nameTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
        _binder.Track(_nameTitle);

        _nameInput = BuildInputField(col);

        var hint = CodeUI.CreateText(col, "Hint", 20f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _binder);
        hint.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        _binder.Bind(hint, "ui_saveslot_name_hint", "최대 16자 · 비워두면 슬롯 번호로 표시됩니다");

        var buttonRow = CodeUI.CreateRow(col, "Buttons", 60f, 16f, TextAnchor.MiddleCenter);

        var okBtn = CodeUI.CreateTextButton(buttonRow, "Confirm", CodeUI.PositiveColor, Color.white, 24f,
            () => { CodeUI.PlaySfx(null); ConfirmNamePrompt(); }, out var okLabel, skin.buttonSprite, skin, _binder);
        var okLe = okBtn.gameObject.AddComponent<LayoutElement>();
        okLe.preferredWidth = 220f; okLe.flexibleWidth = 1f; okLe.preferredHeight = 54f;
        _binder.Bind(okLabel, "ui_saveslot_name_ok", "확인");

        var cancelBtn = CodeUI.CreateTextButton(buttonRow, "Cancel", CodeUI.NeutralBg, CodeUI.LabelColor, 24f,
            () => { CodeUI.PlaySfx(null); HideNamePrompt(); }, out var cancelLabel, skin.buttonSprite, skin, _binder);
        var cancelLe = cancelBtn.gameObject.AddComponent<LayoutElement>();
        cancelLe.preferredWidth = 220f; cancelLe.flexibleWidth = 1f; cancelLe.preferredHeight = 54f;
        _binder.Bind(cancelLabel, "ui_saveslot_cancel", "취소");

        _nameModal.SetActive(false);
    }

    /// <summary>코드로 조립한 한 줄 입력창. (TMP_InputField는 뷰포트·텍스트·플레이스홀더 3종이 필요하다)</summary>
    private TMP_InputField BuildInputField(Transform parent)
    {
        var boxImg = CodeUI.CreateImage(parent, "NameInput", CodeUI.BoxBg, skin.boxSprite, skin);
        boxImg.gameObject.AddComponent<LayoutElement>().preferredHeight = 68f;

        // 조립이 끝날 때까지 꺼 둔다.
        // 활성 상태에서 TMP_InputField를 붙이면 textComponent/textViewport가 비어 있는 채로
        // OnEnable이 돌아 캐럿이 만들어지지 않는다. 마지막에 다시 켠다.
        boxImg.gameObject.SetActive(false);

        var viewport = CodeUI.CreateRect(boxImg.transform, "TextArea");
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(18f, 10f);
        viewport.offsetMax = new Vector2(-18f, -10f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var placeholder = CodeUI.CreateText(viewport, "Placeholder", 26f, FontStyles.Italic, CodeUI.MutedColor,
            TextAlignmentOptions.Left, _binder);
        CodeUI.StretchFull(placeholder.rectTransform);
        _binder.Bind(placeholder, "ui_saveslot_name_placeholder", "세이브 이름을 입력하세요");

        var text = CodeUI.CreateText(viewport, "Text", 26f, FontStyles.Normal, Color.white,
            TextAlignmentOptions.Left, _binder);
        CodeUI.StretchFull(text.rectTransform);
        _binder.Track(text);

        var input = boxImg.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.targetGraphic = boxImg;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = SaveSlotName.MaxLength;
        input.richText = false; // 이름에 태그를 넣어 UI를 망가뜨리지 못하게
        input.restoreOriginalTextOnEscape = false; // ESC는 모달 취소로 처리한다

        boxImg.gameObject.SetActive(true);
        return input;
    }

    private void ShowNamePrompt(string title, string initial, Action<string> onConfirm)
    {
        if (_nameModal == null) return;

        _nameAction = onConfirm;
        if (_nameTitle != null) _nameTitle.text = title;

        _nameModal.transform.SetAsLastSibling();
        _nameModal.SetActive(true);
        RebuildNav(resetFocus: true); // 입력 중에는 네비 목록을 비운다

        // 텍스트는 활성화 뒤에 넣는다 — 꺼져 있는 InputField에 넣으면 라벨이 갱신되지 않는 경우가 있다
        if (_nameInput != null) _nameInput.text = initial ?? string.Empty;

        StartCoroutine(FocusNameInputRoutine());
    }

    /// <summary>
    /// 입력창 포커스는 한 프레임 뒤에 준다.
    /// 모달을 연 그 클릭의 PointerUp 처리가 뒤따라오면서 방금 세운 선택을 지우기 때문.
    /// </summary>
    private IEnumerator FocusNameInputRoutine()
    {
        yield return null;
        if (!IsNameModalOpen || _nameInput == null) yield break;

        _nameInput.Select();
        _nameInput.ActivateInputField();
    }

    private void ConfirmNamePrompt()
    {
        if (!IsNameModalOpen) return;

        string raw = _nameInput != null ? _nameInput.text : null;
        var action = _nameAction;
        HideNamePrompt();
        action?.Invoke(SaveSlotName.Sanitize(raw));
    }

    private void HideNamePrompt()
    {
        _nameAction = null;
        if (_nameInput != null) _nameInput.DeactivateInputField();
        if (_nameModal != null) _nameModal.SetActive(false);
        if (_isOpen) RebuildNav(resetFocus: true);
    }

    // ===================================================
    // 키보드 네비게이션 (W/S 이동 · Space 선택)
    // ===================================================
    /// <summary>현재 화면 상태(슬롯 목록 ↔ 확인 모달)에 맞게 네비 대상 목록을 다시 만든다.</summary>
    private void RebuildNav(bool resetFocus)
    {
        // 목록을 갈아엎기 전에 이전 포커스 색을 원래대로 돌린다.
        // 안 그러면 목록에서 빠진 버튼에 하이라이트가 그대로 눌어붙는다.
        for (int i = 0; i < _nav.Count; i++)
            if (_nav[i].bg != null) _nav[i].bg.color = _nav[i].baseColor;

        _nav.Clear();

        // 이름 입력 중에는 네비 대상이 없다 — 목록을 비워 두면 MoveNav/ActivateNav가 전부 무해해진다.
        // (Update의 조기 반환과 이중 안전장치. HideNamePrompt가 다시 채운다)
        if (IsNameModalOpen)
        {
            _navIndex = -1;
            ApplyNavHighlight();
            return;
        }

        bool confirmOpen = _confirmModal != null && _confirmModal.activeSelf;
        if (confirmOpen)
        {
            AddNav(_confirmYesButton, CodeUI.NegativeColor);
            AddNav(_confirmCancelButton, CodeUI.NeutralBg);
        }
        else
        {
            if (_rows != null)
                for (int i = 0; i < _rows.Length; i++)
                    if (_rows[i] != null) AddNav(_rows[i].button, CodeUI.SlotBg);
            AddNav(_closeButton, CodeUI.NeutralBg);
        }

        if (resetFocus || _navIndex < 0 || _navIndex >= _nav.Count)
            _navIndex = FirstInteractableNav();

        ApplyNavHighlight();
    }

    private void AddNav(Button button, Color baseColor)
    {
        if (button == null) return;
        var bg = button.GetComponent<Image>();
        if (bg == null) return;
        _nav.Add(new NavTarget { button = button, bg = bg, baseColor = baseColor });
    }

    private int FirstInteractableNav()
    {
        for (int i = 0; i < _nav.Count; i++)
            if (IsNavUsable(i)) return i;
        return -1;
    }

    private bool IsNavUsable(int i)
    {
        if (i < 0 || i >= _nav.Count) return false;
        var b = _nav[i].button;
        return b != null && b.interactable && b.gameObject.activeInHierarchy;
    }

    // dir 방향으로 상호작용 가능한 다음 대상으로 이동 (순환 없음)
    private void MoveNav(int dir)
    {
        if (_nav.Count == 0) return;
        for (int i = _navIndex + dir; i >= 0 && i < _nav.Count; i += dir)
        {
            if (IsNavUsable(i)) { _navIndex = i; ApplyNavHighlight(); return; }
        }
    }

    private void ActivateNav()
    {
        if (!IsNavUsable(_navIndex)) return;
        _nav[_navIndex].button.onClick.Invoke();
    }

    private void ApplyNavHighlight()
    {
        for (int i = 0; i < _nav.Count; i++)
        {
            if (_nav[i].bg == null) continue;
            _nav[i].bg.color = (i == _navIndex) ? NavFocusBg : _nav[i].baseColor;
        }
    }

    // 마우스 호버도 키보드 포커스와 같은 하이라이트를 공유한다.
    private void AddNavHover(Button button)
    {
        if (button == null) return;
        var go = button.gameObject;
        var trig = go.GetComponent<EventTrigger>();
        if (trig == null) trig = go.AddComponent<EventTrigger>();
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => FocusNavByButton(button));
        trig.triggers.Add(enter);
    }

    private void FocusNavByButton(Button button)
    {
        if (!_isOpen) return;
        for (int i = 0; i < _nav.Count; i++)
        {
            if (_nav[i].button != button) continue;
            if (!IsNavUsable(i)) return;
            if (i != _navIndex) { _navIndex = i; ApplyNavHighlight(); }
            return;
        }
    }

    // ===================================================
    // 로컬라이제이션 포맷 헬퍼
    // ===================================================
    /// <summary>키가 있으면 그 템플릿으로, 없으면 fallback 템플릿으로 string.Format.</summary>
    private static string Lf(string key, string fallbackTemplate, params object[] args)
    {
        string tmpl = CodeUI.L(key, fallbackTemplate);
        try { return string.Format(tmpl, args); }
        catch (FormatException) { return tmpl; }
    }
}