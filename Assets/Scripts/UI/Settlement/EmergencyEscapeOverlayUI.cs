// @tags: emergency, escape, ui, overlay, code-generated, lost, kept, mineral, penalty

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 긴급 탈출 결과 오버레이 — 탈출하며 떨어뜨려 '잃은 광물'과 그래도 '챙겨온 광물'을 좌우로 보여준다.
/// UI는 전부 코드로 생성한다(다른 코드 오버레이와 같은 패턴). 데이터는 <see cref="EmergencyEscapeReport"/>에서 읽는다.
///
/// 트리거: 긴급 탈출로 지상에 도착하면 <see cref="UIStateManager"/>가 <see cref="EmergencyEscapeReport.HasPending"/>를 보고 연다.
/// (SaveManager.MergeInventoriesToWarehouse 의 60% 페널티 지점에서 잃은/챙긴 내역이 기록된다)
/// </summary>
public class EmergencyEscapeOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static EmergencyEscapeOverlayUI _instance;

    public static EmergencyEscapeOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<EmergencyEscapeOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("EmergencyEscapeOverlayUI");
                    _instance = go.AddComponent<EmergencyEscapeOverlayUI>();
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
        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);
    }

    // ===================================================
    // 인스펙터
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Header("수익 계산 (없으면 골드 값 숨김. 지상엔 ShopManager가 있어 자동으로 찾는다)")]
    [SerializeField] private MineralPriceDatabase priceDatabase;

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.55f;
    [SerializeField] private bool useBlurBackdrop = true;
    [Range(1, 5)][SerializeField] private int blurDownsamples = 4;
    [SerializeField] private bool flipBlurVertically = false;

    [Header("동작")]
    [SerializeField] private bool pauseGameWhileOpen = true;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 1200f;
    [SerializeField] private float panelHeight = 800f;
    [SerializeField] private float rowHeight = 56f;
    [SerializeField] private int sortingOrder = 30820;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 상수
    // ===================================================
    private const float FadeDuration = 0.16f;

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built, _isOpen, _opening;
    private int _lastCloseFrame = -1;
    private float _prevTimeScale = 1f;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private RawImage _blurImage;
    private readonly ScreenBlur _blur = new ScreenBlur();
    private readonly LocTextBinder _loc = new LocTextBinder();

    private RectTransform _lostList, _keptList;
    private TextMeshProUGUI _lostHeader, _keptHeader, _subtitle, _title;
    private CarryLossReason _reason = CarryLossReason.EmergencyEscape;

    // ===================================================
    // 열기 / 닫기
    // ===================================================
    public static void Open()
    {
        var inst = Instance;
        if (IsOpen || inst._opening) return;

        // 데이터 스냅샷을 뜨고 리포트는 '표시됨'으로 소비 → 매니저가 매 프레임 다시 열지 않는다.
        var lost = new List<KeyValuePair<MineralID, int>>(EmergencyEscapeReport.Lost);
        var kept = new List<KeyValuePair<MineralID, int>>(EmergencyEscapeReport.Kept);
        int lostTotal = EmergencyEscapeReport.LostTotal;
        int keptTotal = EmergencyEscapeReport.KeptTotal;
        inst._reason = EmergencyEscapeReport.Reason;
        EmergencyEscapeReport.MarkShown();

        inst.StartCoroutine(inst.OpenRoutine(lost, kept, lostTotal, keptTotal));
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

        if (_canvasObj != null) _canvasObj.SetActive(false);
        _blur.Release(_blurImage);

        // 연출용 데이터는 한 번 보여줬으면 비운다
        EmergencyEscapeReport.Clear();
    }

    private IEnumerator OpenRoutine(List<KeyValuePair<MineralID, int>> lost, List<KeyValuePair<MineralID, int>> kept,
        int lostTotal, int keptTotal)
    {
        _opening = true;
        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        ResolvePriceDb();

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

        _loc.Refresh();
        BuildData(lost, kept, lostTotal, keptTotal);

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
        // 도착 직후 이동키가 눌려 있을 수 있어 '아무 키'가 아니라 확인 키만 받는다.
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space))
        {
            if (Input.GetKeyDown(KeyCode.Escape)) CodeUI.PlayBack();
            Close();
        }
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        _blur.Release(_blurImage);
    }

    private void ResolvePriceDb()
    {
        if (priceDatabase != null) return;
        var shop = FindFirstObjectByType<ShopManager>(FindObjectsInactive.Include);
        if (shop != null) priceDatabase = shop.priceDatabase;
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("EmergencyEscapeOverlayCanvas");
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

        var dim = CodeUI.CreateImage(_canvasObj.transform, "Dim", new Color(0.05f, 0.01f, 0.02f, dimAlpha), rounded: false);
        CodeUI.StretchFull(dim.rectTransform);

        var panel = CodeUI.CreateImage(_canvasObj.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);
        var panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(28, 28, 26, 22);
        panelLayout.spacing = 14f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        // 제목
        var titleRow = CodeUI.CreateRow(panel.transform, "TitleRow", 46f, 10f, TextAnchor.MiddleCenter);
        var diamond = CodeUI.CreateText(titleRow, "Diamond", 22f, FontStyles.Normal, CodeUI.NegativeColor,
            TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;
        _title = CodeUI.CreateText(titleRow, "Title", 30f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        _title.characterSpacing = 6f;
        _loc.Bind(_title, "ui_escape_title", "긴급 탈출");   // 사망이면 BuildData에서 갈아 끼운다

        _subtitle = CodeUI.CreateText(panel.transform, "Subtitle", 18f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.Center, _loc);
        _subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        _loc.Bind(_subtitle, "ui_escape_subtitle", "서둘러 빠져나오며 짐의 일부를 떨어뜨렸습니다.");

        // 좌우 두 칸 (잃음 / 챙김)
        var cols = CodeUI.CreateRow(panel.transform, "Columns", 0f, 16f, TextAnchor.UpperCenter);
        var colsLayout = cols.gameObject.GetComponent<HorizontalLayoutGroup>();
        colsLayout.childControlHeight = true;
        colsLayout.childForceExpandHeight = true;
        colsLayout.childForceExpandWidth = true;
        cols.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

        _lostList = BuildColumn(cols, "ui_escape_lost", "잃은 광물", CodeUI.NegativeColor, out _lostHeader);
        _keptList = BuildColumn(cols, "ui_escape_kept", "챙겨온 광물", CodeUI.PositiveColor, out _keptHeader);

        // 확인 버튼
        var okBtn = CodeUI.CreateTextButton(panel.transform, "Confirm", CodeUI.GoldColor, CodeUI.GoldFg, 20f,
            Close, out var okLabel, skin.buttonSprite, skin, _loc);
        okBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
        _loc.Bind(okLabel, "ui_escape_confirm", "확인");

        var hint = CodeUI.CreateText(panel.transform, "Hint", 15f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        _loc.Bind(hint, "ui_escape_hint", "Space / ESC : 확인");

        _canvasObj.SetActive(false);
    }

    private RectTransform BuildColumn(Transform parent, string headerKey, string headerFallback, Color accent,
        out TextMeshProUGUI header)
    {
        var card = CodeUI.CreateImage(parent, "Col", CodeUI.CardBg, skin.cardSprite, skin);
        card.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var col = card.gameObject.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(16, 16, 14, 14);
        col.spacing = 8f;
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = true;
        col.childForceExpandHeight = false;

        // 헤더: 색 점 + 라벨 + 총계
        var headerRow = CodeUI.CreateRow(card.transform, "Header", 30f, 8f);
        var dot = CodeUI.CreateText(headerRow, "Dot", 15f, FontStyles.Normal, accent, TextAlignmentOptions.Center, _loc);
        dot.text = diamondGlyph;
        var label = CodeUI.CreateText(headerRow, "Label", 20f, FontStyles.Bold, accent, TextAlignmentOptions.MidlineLeft, _loc);
        _loc.Bind(label, headerKey, headerFallback);
        CodeUI.CreateSpacer(headerRow);
        header = CodeUI.CreateText(headerRow, "Total", 18f, FontStyles.Bold, CodeUI.LabelColor, TextAlignmentOptions.MidlineRight, _loc);
        header.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;

        CodeUI.CreateDivider(card.transform);

        var scroll = CodeUI.CreateScrollView(card.transform, "Scroll", out var list);
        scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var listLayout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 6f;
        listLayout.padding = new RectOffset(2, 8, 2, 2);
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        return list;
    }

    // ===================================================
    // 데이터 채우기
    // ===================================================
    private void BuildData(List<KeyValuePair<MineralID, int>> lost, List<KeyValuePair<MineralID, int>> kept,
        int lostTotal, int keptTotal)
    {
        bool hasPrice = priceDatabase != null;

        int lostValue = FillColumn(_lostList, lost, CodeUI.NegativeColor, hasPrice);
        int keptValue = FillColumn(_keptList, kept, CodeUI.PositiveColor, hasPrice);

        _lostHeader.text = FormatTotal(lostTotal, hasPrice ? lostValue : -1);
        _lostHeader.color = CodeUI.NegativeColor;
        _keptHeader.text = FormatTotal(keptTotal, hasPrice ? keptValue : -1);
        _keptHeader.color = CodeUI.PositiveColor;

        // 제목·부제는 사유(탈출 / 사망)와 결과(뭔가 챙겼는지)에 맞춰 갈아 끼운다.
        // _loc.Bind 로 걸어 둔 기본 문구를 여기서 덮어쓴다 — Refresh()가 BuildData보다 먼저 돈다.
        bool died = _reason == CarryLossReason.Death;

        _title.text = died
            ? CodeUI.L("ui_death_title", "탈진")
            : CodeUI.L("ui_escape_title", "긴급 탈출");

        if (died)
            _subtitle.text = keptTotal > 0
                ? CodeUI.L("ui_death_subtitle", "쓰러진 당신을 누군가 끌어올렸습니다. 손에 쥔 것만 남았습니다.")
                : CodeUI.L("ui_death_subtitle_alllost", "쓰러진 당신을 누군가 끌어올렸습니다. 캔 광물은 모두 잃었습니다.");
        else
            _subtitle.text = keptTotal > 0
                ? CodeUI.L("ui_escape_subtitle", "서둘러 빠져나오며 짐의 대부분을 떨어뜨렸습니다.")
                : CodeUI.L("ui_escape_subtitle_alllost", "서둘러 빠져나오느라 캔 광물을 모두 떨어뜨렸습니다.");
    }

    private string FormatTotal(int count, int value)
    {
        string c = string.Format(CodeUI.L("ui_escape_count", "{0}개"), count);
        if (value < 0) return c;
        return $"{c}  ·  {CodeUI.Gold(value)} G";
    }

    /// <summary>목록 칸을 채우고 총 골드 가치를 반환한다.</summary>
    private int FillColumn(RectTransform list, List<KeyValuePair<MineralID, int>> items, Color accent, bool hasPrice)
    {
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        if (items == null || items.Count == 0)
        {
            var none = CodeUI.CreateText(list, "None", 17f, FontStyles.Italic, CodeUI.MutedColor,
                TextAlignmentOptions.Center);
            none.text = CodeUI.L("ui_escape_none", "없음");
            none.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
            return 0;
        }

        int totalValue = 0;
        foreach (var kv in items)
        {
            int unit = hasPrice ? priceDatabase.GetPrice(kv.Key) : 0;
            int value = unit * kv.Value;
            totalValue += value;
            AddRow(list, kv.Key, kv.Value, hasPrice ? value : -1, accent);
        }
        return totalValue;
    }

    private void AddRow(RectTransform list, MineralID id, int count, int value, Color accent)
    {
        MineralSO so = MineralDatabase.Instance != null ? MineralDatabase.Instance.GetMineralByID(id) : null;

        var rowBg = CodeUI.CreateImage(list, "Row", CodeUI.SlotBg, skin.slotSprite, skin);
        rowBg.gameObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;
        var layout = rowBg.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 12, 5, 5);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var iconBox = CodeUI.CreateImage(rowBg.transform, "IconBox", CodeUI.BoxBg, skin.boxSprite, skin);
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = rowHeight - 12f;
        iconLe.preferredHeight = rowHeight - 12f;
        if (so != null && so.Icon != null)
        {
            var img = CodeUI.CreateImage(iconBox.transform, "Icon", Color.white, rounded: false);
            img.sprite = so.Icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var ir = img.rectTransform;
            ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
            ir.offsetMin = new Vector2(3f, 3f); ir.offsetMax = new Vector2(-3f, -3f);
        }

        var nameText = CodeUI.CreateText(rowBg.transform, "Name", 17f, FontStyles.Normal, Color.white,
            TextAlignmentOptions.MidlineLeft);
        nameText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        nameText.text = so != null ? so.DisplayName : id.ToString();

        var countText = CodeUI.CreateText(rowBg.transform, "Count", 17f, FontStyles.Bold, accent,
            TextAlignmentOptions.MidlineRight);
        countText.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;
        countText.text = $"x{count}";

        if (value >= 0)
        {
            var valText = CodeUI.CreateText(rowBg.transform, "Value", 15f, FontStyles.Normal, CodeUI.MutedColor,
                TextAlignmentOptions.MidlineRight);
            valText.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;
            valText.text = $"{CodeUI.Gold(value)} G";
        }
    }
}
