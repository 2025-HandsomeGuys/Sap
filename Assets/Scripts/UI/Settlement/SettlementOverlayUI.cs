// @tags: settlement, ui, overlay, code-generated, surface, depth, mineral, profit, reveal

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 탐험 정산 오버레이 — 지하에서 지상으로 올라올 때 이번 탐험 성과(최대 깊이·체류 시간·캔 광물·예상 수익)를 보여준다.
/// UI는 전부 코드로 생성한다(다른 코드 오버레이와 같은 패턴).
///
/// 기존 프리팹 <see cref="SettlementUI"/>와 같은 정보를 같은 방식(단계별 페이드 연출 + 아무 키나 눌러 계속)으로 보여주며,
/// <see cref="SettlementSceneController"/>가 <see cref="IsConfirmed"/>를 폴링해 다음 씬 로드로 넘어간다.
/// </summary>
public class SettlementOverlayUI : MonoBehaviour
{
    // ===================================================
    // 싱글톤
    // ===================================================
    private static SettlementOverlayUI _instance;

    public static SettlementOverlayUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SettlementOverlayUI>();
                if (_instance == null)
                {
                    var go = new GameObject("SettlementOverlayUI");
                    _instance = go.AddComponent<SettlementOverlayUI>();
                }
            }
            return _instance;
        }
    }

    public static bool IsOpen => _instance != null && _instance._isOpen;

    /// <summary>연출이 끝까지 재생됐는지(스킵 포함).</summary>
    public bool IsSequenceComplete { get; private set; }
    /// <summary>사용자가 확인(아무 키)했는지 — 씬 컨트롤러가 폴링한다.</summary>
    public bool IsConfirmed { get; private set; }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    // ===================================================
    // 인스펙터
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성 라운드 스타일)")]
    [SerializeField] private UISkin skin = new UISkin();

    [Header("수익 계산 (없으면 수익 줄은 숨김)")]
    [SerializeField] private MineralPriceDatabase priceDatabase;

    [Header("배경 처리")]
    [Range(0f, 1f)][SerializeField] private float dimAlpha = 0.72f;

    [Header("레이아웃 (1920x1080 기준 px)")]
    [SerializeField] private float panelWidth = 900f;
    [SerializeField] private float panelHeight = 820f;
    [SerializeField] private float rowHeight = 62f;
    [SerializeField] private int sortingOrder = 30800;

    [Header("연출")]
    [SerializeField] private float fadeInDuration = 0.4f;
    [SerializeField] private float rowStagger = 0.12f;
    [SerializeField] private float stepDelay = 0.35f;

    [Header("효과음 (SoundDataSO에 등록된 SFX 이름, 없으면 무음)")]
    [SerializeField] private string clickSfxName = SfxKeys.UiClick;

    [Header("글리프 (폰트에 없어 □로 보이면 교체)")]
    [SerializeField] private string diamondGlyph = "◆";

    // ===================================================
    // 내부 상태
    // ===================================================
    private bool _built, _isOpen;
    private Coroutine _seq;

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private readonly LocTextBinder _loc = new LocTextBinder();

    private TextMeshProUGUI _depthValue, _timeValue, _totalText, _profitText;
    private CanvasGroup _depthTile, _timeTile, _footerGroup, _hintGroup;
    private ScrollRect _scroll;
    private RectTransform _list;
    private readonly List<CanvasGroup> _rowGroups = new List<CanvasGroup>();

    // ===================================================
    // 표시
    // ===================================================
    /// <summary>정산 데이터를 물려 연출을 시작한다. priceDb를 넘기면 예상 수익을 계산한다.</summary>
    public void Show(SettlementData data, MineralPriceDatabase priceDb = null)
    {
        if (priceDb != null) priceDatabase = priceDb;

        EnsureBuilt();
        CodeUI.EnsureEventSystem();
        _loc.Refresh();

        IsConfirmed = false;
        IsSequenceComplete = false;
        _isOpen = true;

        PopulateData(data);

        _canvasObj.SetActive(true);
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;

        if (_seq != null) StopCoroutine(_seq);
        _seq = StartCoroutine(RevealRoutine());
    }

    /// <summary>연출을 즉시 완료 상태로 만든다(전부 노출). 코루틴은 그대로 두고 알파만 채운다.</summary>
    public void SkipToEnd()
    {
        _canvasGroup.alpha = 1f;
        SetGroup(_depthTile, 1f);
        SetGroup(_timeTile, 1f);
        foreach (var g in _rowGroups) SetGroup(g, 1f);
        SetGroup(_footerGroup, 1f);
        SetGroup(_hintGroup, 1f);
        IsSequenceComplete = true;
    }

    public void Hide()
    {
        _isOpen = false;
        if (_seq != null) { StopCoroutine(_seq); _seq = null; }
        if (_canvasObj != null) _canvasObj.SetActive(false);
    }

    private void PopulateData(SettlementData data)
    {
        _depthValue.text = $"{data.maxDepth:F1} m";
        int minutes = Mathf.FloorToInt(data.timeUnderground / 60);
        int seconds = Mathf.FloorToInt(data.timeUnderground % 60);
        _timeValue.text = $"{minutes:00}:{seconds:00}";

        ClearRows();

        int totalCount = 0;
        int totalProfit = 0;
        bool hasPrice = priceDatabase != null;

        var minerals = data.minedMinerals;
        if (minerals != null)
        {
            foreach (var pair in minerals)
            {
                if (pair.Value <= 0) continue;
                int value = hasPrice ? priceDatabase.GetPrice(pair.Key) * pair.Value : 0;
                AddMineralRow(pair.Key, pair.Value, hasPrice ? value : -1);
                totalCount += pair.Value;
                totalProfit += value;
            }
        }

        if (totalCount == 0)
        {
            var none = CodeUI.CreateText(_list, "None", 18f, FontStyles.Italic, CodeUI.MutedColor,
                TextAlignmentOptions.Center);
            none.text = CodeUI.L("ui_settlement_nothing", "가져온 광물이 없습니다.");
            none.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            var g = none.gameObject.AddComponent<CanvasGroup>();
            _rowGroups.Add(g);
        }

        _totalText.text = string.Format(CodeUI.L("ui_settlement_total", "총 {0}개 획득"), totalCount);
        if (hasPrice)
        {
            _profitText.gameObject.SetActive(true);
            _profitText.text = $"{CodeUI.Gold(totalProfit)} G";
        }
        else
        {
            _profitText.gameObject.SetActive(false);
        }
    }

    // ===================================================
    // 연출
    // ===================================================
    private bool _skip;

    private IEnumerator RevealRoutine()
    {
        _skip = false;
        _canvasGroup.alpha = 0f;
        SetGroup(_depthTile, 0f);
        SetGroup(_timeTile, 0f);
        foreach (var g in _rowGroups) SetGroup(g, 0f);
        SetGroup(_footerGroup, 0f);
        SetGroup(_hintGroup, 0f);

        // 1. 패널 페이드인 (아무 키나 누르면 스킵)
        float t = 0f;
        while (t < fadeInDuration)
        {
            if (Input.anyKeyDown) { _skip = true; break; }
            t += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Clamp01(t / fadeInDuration);
            yield return null;
        }
        _canvasGroup.alpha = 1f;

        // 2. 깊이 / 시간 타일
        if (!_skip) { SetGroup(_depthTile, 1f); SetGroup(_timeTile, 1f); yield return WaitOrSkip(stepDelay); }

        // 3. 광물 행 스태거
        if (!_skip)
            foreach (var g in _rowGroups)
            {
                SetGroup(g, 1f);
                yield return WaitOrSkip(rowStagger);
                if (_skip) break;
            }

        // 4. 합계 / 수익
        if (!_skip) yield return WaitOrSkip(stepDelay);
        if (!_skip) { SetGroup(_footerGroup, 1f); yield return WaitOrSkip(stepDelay); }

        // 스킵됐다면 전부 즉시 노출
        SkipToEnd();

        // 5. 확인 대기 (직전 프레임 입력이 남아 바로 확인되는 것 방지)
        yield return null;
        while (!IsConfirmed)
        {
            if (Input.anyKeyDown) { IsConfirmed = true; CodeUI.PlaySfx(clickSfxName); }
            yield return null;
        }
        _seq = null;
    }

    /// <summary>duration만큼 대기하되, 도중에 아무 키나 누르면 _skip을 세우고 즉시 반환.</summary>
    private IEnumerator WaitOrSkip(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            if (Input.anyKeyDown) { _skip = true; yield break; }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvasObj = new GameObject("SettlementOverlayCanvas");
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

        var dim = CodeUI.CreateImage(_canvasObj.transform, "Dim", new Color(0.02f, 0.03f, 0.06f, dimAlpha), rounded: false);
        CodeUI.StretchFull(dim.rectTransform);
        dim.raycastTarget = true;

        var panel = CodeUI.CreateImage(_canvasObj.transform, "Panel", CodeUI.PanelBg, skin.panelSprite, skin);
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);
        var panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(28, 28, 26, 24);
        panelLayout.spacing = 16f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        BuildTitle(panel.transform);
        BuildStatTiles(panel.transform);
        BuildMineralList(panel.transform);
        BuildFooter(panel.transform);
        BuildHint(panel.transform);

        _canvasObj.SetActive(false);
    }

    private void BuildTitle(Transform panel)
    {
        var row = CodeUI.CreateRow(panel, "TitleRow", 46f, 10f, TextAnchor.MiddleCenter);
        var diamond = CodeUI.CreateText(row, "Diamond", 22f, FontStyles.Normal, CodeUI.GoldColor,
            TextAlignmentOptions.Center, _loc);
        diamond.text = diamondGlyph;
        var title = CodeUI.CreateText(row, "Title", 30f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, _loc);
        title.characterSpacing = 6f;
        _loc.Bind(title, "ui_settlement_title", "탐험 정산");
    }

    private void BuildStatTiles(Transform panel)
    {
        var row = CodeUI.CreateRow(panel, "Stats", 96f, 14f, TextAnchor.MiddleCenter);
        row.gameObject.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
        var statsLe = row.gameObject.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
        statsLe.preferredHeight = statsLe.minHeight = 96f;
        statsLe.flexibleHeight = 0f;

        _depthTile = BuildTile(row, "ui_settlement_depth", "최대 깊이", out _depthValue, CodeUI.MineralColor);
        _timeTile = BuildTile(row, "ui_settlement_time", "체류 시간", out _timeValue, CodeUI.AccentFill);
    }

    private CanvasGroup BuildTile(Transform parent, string labelKey, string labelFallback,
        out TextMeshProUGUI value, Color accent)
    {
        var box = CodeUI.CreateImage(parent, "Tile", CodeUI.BoxBg, skin.boxSprite, skin);
        box.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var group = box.gameObject.AddComponent<CanvasGroup>();
        var col = box.gameObject.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(16, 16, 12, 12);
        col.spacing = 4f;
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = true;
        col.childForceExpandHeight = false;
        col.childAlignment = TextAnchor.MiddleCenter;

        var label = CodeUI.CreateText(box.transform, "Label", 16f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        _loc.Bind(label, labelKey, labelFallback);

        value = CodeUI.CreateText(box.transform, "Value", 30f, FontStyles.Bold, accent,
            TextAlignmentOptions.Center, _loc);
        value.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

        return group;
    }

    private void BuildMineralList(Transform panel)
    {
        var card = CodeUI.CreateImage(panel, "MineralCard", CodeUI.CardBg, skin.cardSprite, skin);
        var cardLe = card.gameObject.AddComponent<LayoutElement>();
        cardLe.flexibleHeight = 1f;   // 패널의 남는 세로 공간을 전부 차지
        cardLe.minHeight = 240f;
        var col = card.gameObject.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(14, 14, 12, 12);
        col.spacing = 8f;
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = true;
        // 헤더까지 늘어나 목록을 짓누르지 않도록 강제 확장은 끈다. 대신 스크롤에 flexibleHeight를 준다.
        col.childForceExpandHeight = false;

        var header = CodeUI.CreateText(card.transform, "Header", 17f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineLeft, _loc);
        var headerLe = header.gameObject.AddComponent<LayoutElement>();
        headerLe.preferredHeight = headerLe.minHeight = 24f;
        headerLe.flexibleHeight = 0f;
        _loc.Bind(header, "ui_settlement_minerals", "가져온 광물");

        _scroll = CodeUI.CreateScrollView(card.transform, "Scroll", out _list);
        var scrollLe = _scroll.gameObject.AddComponent<LayoutElement>();
        scrollLe.flexibleHeight = 1f;  // 카드에서 헤더를 뺀 나머지를 스크롤이 가져간다
        scrollLe.minHeight = 160f;
        var listLayout = _list.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 6f;
        listLayout.padding = new RectOffset(2, 8, 2, 2);
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;
    }

    private void BuildFooter(Transform panel)
    {
        var row = CodeUI.CreateImage(panel, "Footer", CodeUI.BoxBg, skin.boxSprite, skin);
        var footerLe = row.gameObject.AddComponent<LayoutElement>();
        footerLe.preferredHeight = footerLe.minHeight = 56f;
        footerLe.flexibleHeight = 0f; // 고정 높이 — 목록 공간을 뺏지 않는다
        _footerGroup = row.gameObject.AddComponent<CanvasGroup>();
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleLeft;

        _totalText = CodeUI.CreateText(row.transform, "Total", 20f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, _loc);
        _totalText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        _profitText = CodeUI.CreateText(row.transform, "Profit", 24f, FontStyles.Bold, CodeUI.GoldColor,
            TextAlignmentOptions.MidlineRight, _loc);
        _profitText.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;
    }

    private void BuildHint(Transform panel)
    {
        var hint = CodeUI.CreateText(panel, "Hint", 16f, FontStyles.Normal, CodeUI.MutedColor,
            TextAlignmentOptions.Center, _loc);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        _hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
        _loc.Bind(hint, "ui_settlement_continue", "아무 키나 눌러 계속");
    }

    private void AddMineralRow(MineralID id, int count, int value)
    {
        MineralSO so = MineralDatabase.Instance != null ? MineralDatabase.Instance.GetMineralByID(id) : null;

        var rowBg = CodeUI.CreateImage(_list, "Row", CodeUI.SlotBg, skin.slotSprite, skin);
        rowBg.gameObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;
        var group = rowBg.gameObject.AddComponent<CanvasGroup>();
        _rowGroups.Add(group);

        var layout = rowBg.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 14, 6, 6);
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
        if (so != null && so.Icon != null)
        {
            var img = CodeUI.CreateImage(iconBox.transform, "Icon", Color.white, rounded: false);
            img.sprite = so.Icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var ir = img.rectTransform;
            ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
            ir.offsetMin = new Vector2(4f, 4f); ir.offsetMax = new Vector2(-4f, -4f);
        }

        var nameText = CodeUI.CreateText(rowBg.transform, "Name", 18f, FontStyles.Normal, Color.white,
            TextAlignmentOptions.MidlineLeft);
        nameText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        nameText.text = so != null ? so.DisplayName : id.ToString();

        var countText = CodeUI.CreateText(rowBg.transform, "Count", 18f, FontStyles.Bold, CodeUI.MineralColor,
            TextAlignmentOptions.MidlineRight);
        countText.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;
        countText.text = $"x{count}";

        if (value >= 0)
        {
            var valText = CodeUI.CreateText(rowBg.transform, "Value", 17f, FontStyles.Bold, CodeUI.GoldColor,
                TextAlignmentOptions.MidlineRight);
            valText.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;
            valText.text = $"{CodeUI.Gold(value)} G";
        }
    }

    private void ClearRows()
    {
        _rowGroups.Clear();
        if (_list == null) return;
        for (int i = _list.childCount - 1; i >= 0; i--)
            Destroy(_list.GetChild(i).gameObject);
    }

    private static void SetGroup(CanvasGroup g, float alpha)
    {
        if (g != null) g.alpha = alpha;
    }
}
