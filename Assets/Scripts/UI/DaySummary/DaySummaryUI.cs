// @tags: daysummary, settlement, ui, overlay, fade, gold, report, skip
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 침대 수면 시 "그날 하루 수익 정산" 연출 오버레이.
/// ScreenFader가 화면을 검게 덮은 상태에서 항목이 위→아래로 하나씩 나타난다.
///  - 클릭/스페이스 1회: 한 블럭 스킵 (연출 중이면 즉시 완성, 대기 중이면 다음 블럭 즉시 출력)
///  - 꾹 누르기(0.35초 이상): 빨리감기 — 남은 블럭이 다다닥 출력 (마지막 프롬프트에서 멈춤)
/// 이익은 초록, 손실은 빨강, 0은 회색. 금액은 짧은 카운트업과 함께 등장.
/// UI는 전부 코드로 생성 — 씬/프리팹 세팅 불필요 (TooltipManager 패턴).
/// </summary>
public class DaySummaryUI : MonoBehaviour
{
    private static DaySummaryUI _instance;

    /// <summary>필요 시 자동 생성되는 싱글톤. 에디터 배치 불필요.</summary>
    public static DaySummaryUI Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("DaySummaryUI");
                _instance = go.AddComponent<DaySummaryUI>();
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── 연출 파라미터 ──────────────────────────────────
    private const float PanelFadeDuration = 0.25f;
    private const float RevealDuration = 0.3f;   // 블럭 하나 등장 시간
    private const float FirstGap = 0.3f;         // 패널 등장 → 첫 블럭
    private const float RowGap = 0.4f;           // 블럭 사이 대기
    private const float TotalExtraGap = 0.5f;    // 최종 손익 앞 추가 대기 (드라마)
    private const float HoldThreshold = 0.35f;   // 이 시간 이상 누르면 빨리감기
    private const float FastForwardStep = 0.05f; // 빨리감기 블럭 간 템포

    // ── 레이아웃 ──────────────────────────────────────
    private const float PanelWidth = 660f;
    private const float TitleHeight = 64f;
    private const float TitleGap = 30f;
    private const float RowHeight = 46f;
    private const float RowSpacing = 6f;
    private const float DividerHeight = 20f;
    private const float PromptGap = 36f;
    private const float PromptHeight = 30f;

    private static readonly Color PlusColor = new Color(0.30f, 0.85f, 0.45f);
    private static readonly Color MinusColor = new Color(0.95f, 0.33f, 0.33f);
    private static readonly Color NeutralColor = new Color(0.72f, 0.75f, 0.80f);
    private static readonly Color LabelColor = new Color(0.85f, 0.86f, 0.90f);

    private class Row
    {
        public CanvasGroup group;        // 블럭 전체 알파
        public RectTransform content;    // 등장 슬라이드용 내부 컨테이너
        public TextMeshProUGUI amountText; // null이면 구분선 같은 정적 블럭
        public int amount;
        public bool signed;              // +/- 부호 표시 여부 (보유 골드 줄은 false)
        public float extraDelayBefore;
        public bool isTotal;             // '최종 손익' 줄 — 동전음 대신 흑자/적자 결과음이 난다
    }

    private GameObject _canvasObj;
    private CanvasGroup _canvasGroup;
    private GameObject _panelObj;
    private TextMeshProUGUI _promptText;
    private readonly List<Row> _rows = new List<Row>();

    public bool IsPlaying { get; private set; }

    /// <summary>
    /// 정산 연출이 재생 중인지(정적 접근). UIStateManager.IsInputBlocked가 이 값을 봐서
    /// 정산 중 플레이어 조작을 막는다. Instance 프로퍼티(자동 생성)가 아닌 _instance를
    /// 직접 봐서, 아직 만들어진 적 없으면 새로 만들지 않는다.
    /// </summary>
    public static bool IsShowing => _instance != null && _instance.IsPlaying;

    // 입력 상태 (Update에서 수집, 코루틴에서 소비)
    private bool _pressed;
    private float _holdTime;
    private bool FastForward => _holdTime >= HoldThreshold;

    private void Update()
    {
        if (!IsPlaying) return;

        if (Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return))
            _holdTime += Time.unscaledDeltaTime;
        else
            _holdTime = 0f;

        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            _pressed = true;
    }

    private bool ConsumePress()
    {
        if (!_pressed) return false;
        _pressed = false;
        return true;
    }

    // ===================================================
    // 재생
    // ===================================================
    /// <summary>정산 연출 재생. 종료(마지막 클릭 + 페이드아웃)까지 대기하는 코루틴.</summary>
    public IEnumerator Play(DayEarningsReport report)
    {
        if (IsPlaying) yield break;

        EnsureCanvas();
        BuildContent(report);

        IsPlaying = true;
        _pressed = false;
        _holdTime = 0f;

        _canvasGroup.alpha = 0f; // 활성화 첫 프레임 번쩍임 방지
        _canvasObj.SetActive(true);
        yield return FadeGroup(_canvasGroup, 0f, 1f, PanelFadeDuration);

        // 블럭 순차 공개
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            float gap = (i == 0 ? FirstGap : RowGap) + row.extraDelayBefore;

            bool skip = false;
            float t = 0f;
            while (t < gap)
            {
                if (FastForward || ConsumePress()) { skip = true; break; }
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            PlayRowSfx(row);

            if (skip)
            {
                SnapRow(row);
                if (FastForward) yield return WaitUnscaled(FastForwardStep);
                continue;
            }

            yield return RevealRow(row);
        }

        // 계속 프롬프트 — 빨리감기로 도달해도 새 클릭이 있어야 닫힌다 (정산을 못 보고 지나치는 것 방지)
        _promptText.gameObject.SetActive(true);
        _pressed = false;
        float blink = 0f;
        while (!ConsumePress())
        {
            blink += Time.unscaledDeltaTime;
            _promptText.alpha = 0.4f + 0.6f * Mathf.PingPong(blink * 1.2f, 1f);
            yield return null;
        }

        yield return FadeGroup(_canvasGroup, 1f, 0f, PanelFadeDuration);

        Destroy(_panelObj);
        _panelObj = null;
        _rows.Clear();
        _canvasObj.SetActive(false);
        IsPlaying = false;
    }

    /// <summary>
    /// 줄이 공개되는 순간의 효과음.
    ///  - 금액 줄: 동전 소리
    ///  - '최종 손익' 줄: 결과음 (동전 소리 대신). 손익 부호와 무관하게 항상 흑자음 —
    ///    업그레이드 지출 때문에 총합이 마이너스여도 실패처럼 들리지 않게 하기 위함
    ///    (적자음 SfxKeys.DaySummaryLoss는 현재 미사용)
    ///  - 구분선(amountText == null): 무음
    ///
    /// 빨리감기로 줄이 연달아 공개돼도 SoundManager의 스로틀(같은 키 40ms)이 중복을
    /// 걸러주므로 동전 소리가 기관총처럼 터지지 않는다.
    ///
    /// 동전 소리는 상점 판매음을 그대로 쓴다. 정산 전용 음원으로 나누고 싶으면
    /// SfxKeys에 키를 추가하고 여기만 바꾸면 된다.
    /// </summary>
    private static void PlayRowSfx(Row row)
    {
        var sm = SoundManager.Instance;
        if (sm == null) return;

        if (row.isTotal)
        {
            sm.PlaySFX(SfxKeys.DaySummaryProfit);
            return;
        }

        if (row.amountText != null)
            sm.PlaySFX(SfxKeys.ShopSell);
    }

    private IEnumerator RevealRow(Row row)
    {
        float t = 0f;
        while (t < RevealDuration)
        {
            if (FastForward || ConsumePress()) break;
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / RevealDuration));
            row.group.alpha = k;
            row.content.anchoredPosition = new Vector2(0f, (1f - k) * -16f);
            if (row.amountText != null)
                row.amountText.text = FormatAmount(Mathf.RoundToInt(Mathf.Lerp(0, row.amount, k)), row.signed);
            yield return null;
        }
        SnapRow(row);
    }

    private void SnapRow(Row row)
    {
        row.group.alpha = 1f;
        row.content.anchoredPosition = Vector2.zero;
        if (row.amountText != null)
            row.amountText.text = FormatAmount(row.amount, row.signed);
    }

    private static IEnumerator FadeGroup(CanvasGroup g, float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            g.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        g.alpha = to;
    }

    private static IEnumerator WaitUnscaled(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void EnsureCanvas()
    {
        if (_canvasObj != null) return;

        _canvasObj = new GameObject("DaySummaryCanvas");
        _canvasObj.transform.SetParent(transform, false);
        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // ScreenFader의 검은 화면보다 위
        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasObj.AddComponent<GraphicRaycaster>();
        _canvasGroup = _canvasObj.AddComponent<CanvasGroup>();

        // 배경 — ScreenFader가 없어도 단독으로 어둡게 깔리도록 + 아래 UI 클릭 차단
        var blockerObj = new GameObject("Blocker");
        blockerObj.transform.SetParent(_canvasObj.transform, false);
        var blockerRect = blockerObj.AddComponent<RectTransform>();
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.offsetMin = Vector2.zero;
        blockerRect.offsetMax = Vector2.zero;
        var blockerImg = blockerObj.AddComponent<Image>();
        blockerImg.color = new Color(0f, 0f, 0f, 1f);
        blockerImg.raycastTarget = true;

        _canvasObj.SetActive(false);
    }

    private void BuildContent(DayEarningsReport report)
    {
        if (_panelObj != null) Destroy(_panelObj);
        _rows.Clear();

        _panelObj = new GameObject("Panel");
        _panelObj.transform.SetParent(_canvasObj.transform, false);
        var panelRect = _panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 1f);

        float y = 0f;

        // 제목 — 패널 페이드인과 함께 등장 (블럭 연출 대상 아님)
        var title = CreateText(_panelObj.transform, "Title", LocF("ds_title", "{0}일차 정산", report.day),
            40f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        SetTopRect(title.rectTransform, y, TitleHeight);
        y += TitleHeight + TitleGap;

        // 정산 항목 — 광물/주식/코인은 0이어도 항상 표시, 지출·기타는 있을 때만
        y = AddAmountRow(y, Loc("ds_mineral", "광물 판매"), report.mineralSale, 26f, false, 0f);
        y = AddAmountRow(y, Loc("ds_stock", "주식 손익"), report.stock, 26f, false, 0f);
        y = AddAmountRow(y, Loc("ds_coin", "코인 손익"), report.coin, 26f, false, 0f);
        if (report.shopPurchase != 0)
            y = AddAmountRow(y, Loc("ds_shop", "상점 구매"), report.shopPurchase, 26f, false, 0f);
        if (report.upgrade != 0)
            y = AddAmountRow(y, Loc("ds_upgrade", "업그레이드"), report.upgrade, 26f, false, 0f);
        if (report.other != 0)
            y = AddAmountRow(y, Loc("ds_other", "기타"), report.other, 26f, false, 0f);

        y = AddDivider(y);
        y = AddAmountRow(y, Loc("ds_total", "최종 손익"), report.total, 30f, false, TotalExtraGap, FontStyles.Bold);
        if (_rows.Count > 0) _rows[_rows.Count - 1].isTotal = true; // 방금 추가한 '최종 손익' 줄
        y = AddAmountRow(y, Loc("ds_gold_after", "보유 골드"), report.goldAfter, 24f, true, 0f);

        // 계속 프롬프트
        y += PromptGap;
        _promptText = CreateText(_panelObj.transform, "Prompt", Loc("ds_continue", "클릭하여 계속"),
            22f, FontStyles.Normal, NeutralColor, TextAlignmentOptions.Center);
        SetTopRect(_promptText.rectTransform, y, PromptHeight);
        y += PromptHeight;
        _promptText.gameObject.SetActive(false);

        panelRect.sizeDelta = new Vector2(PanelWidth, y);
        panelRect.anchoredPosition = new Vector2(0f, y * 0.5f); // 세로 중앙 정렬
    }

    /// <summary>라벨 + 금액 블럭 한 줄 추가. neutral=true면 부호 없이 회색 고정(보유 골드 줄).</summary>
    private float AddAmountRow(float y, string label, int amount, float fontSize, bool neutral, float extraDelay, FontStyles amountStyle = FontStyles.Normal)
    {
        var rowObj = new GameObject("Row_" + label);
        rowObj.transform.SetParent(_panelObj.transform, false);
        var rowRect = rowObj.AddComponent<RectTransform>();
        SetTopRect(rowRect, y, RowHeight);
        var group = rowObj.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var contentObj = new GameObject("Content");
        contentObj.transform.SetParent(rowObj.transform, false);
        var contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        var labelText = CreateText(contentObj.transform, "Label", label, fontSize, FontStyles.Normal, LabelColor, TextAlignmentOptions.MidlineLeft);
        labelText.rectTransform.anchorMin = new Vector2(0f, 0f);
        labelText.rectTransform.anchorMax = new Vector2(0.55f, 1f);
        labelText.rectTransform.offsetMin = Vector2.zero;
        labelText.rectTransform.offsetMax = Vector2.zero;

        Color amountColor = neutral ? NeutralColor
            : amount > 0 ? PlusColor
            : amount < 0 ? MinusColor
            : NeutralColor;
        var amountText = CreateText(contentObj.transform, "Amount", "", fontSize, amountStyle, amountColor, TextAlignmentOptions.MidlineRight);
        amountText.rectTransform.anchorMin = new Vector2(0.45f, 0f);
        amountText.rectTransform.anchorMax = new Vector2(1f, 1f);
        amountText.rectTransform.offsetMin = Vector2.zero;
        amountText.rectTransform.offsetMax = Vector2.zero;

        _rows.Add(new Row
        {
            group = group,
            content = contentRect,
            amountText = amountText,
            amount = amount,
            signed = !neutral,
            extraDelayBefore = extraDelay,
        });

        return y + RowHeight + RowSpacing;
    }

    private float AddDivider(float y)
    {
        var rowObj = new GameObject("Divider");
        rowObj.transform.SetParent(_panelObj.transform, false);
        var rowRect = rowObj.AddComponent<RectTransform>();
        SetTopRect(rowRect, y, DividerHeight);
        var group = rowObj.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var contentObj = new GameObject("Content");
        contentObj.transform.SetParent(rowObj.transform, false);
        var contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        var lineObj = new GameObject("Line");
        lineObj.transform.SetParent(contentObj.transform, false);
        var lineRect = lineObj.AddComponent<RectTransform>();
        lineRect.anchorMin = new Vector2(0f, 0.5f);
        lineRect.anchorMax = new Vector2(1f, 0.5f);
        lineRect.sizeDelta = new Vector2(0f, 2f);
        var lineImg = lineObj.AddComponent<Image>();
        lineImg.color = new Color(1f, 1f, 1f, 0.25f);
        lineImg.raycastTarget = false;

        _rows.Add(new Row { group = group, content = contentRect, amountText = null });
        return y + DividerHeight + RowSpacing;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize, FontStyles style, Color color, TextAlignmentOptions align)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        var lm = LanguageManager.Instance;
        var font = lm != null ? lm.GetCurrentFont() : null;
        if (font != null) tmp.font = font;

        return tmp;
    }

    /// <summary>패널 상단 기준 top-stretch 배치 (y = 패널 위에서부터의 거리).</summary>
    private static void SetTopRect(RectTransform rect, float y, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = new Vector2(0f, -y);
    }

    // ===================================================
    // 텍스트 유틸
    // ===================================================
    private static string FormatAmount(int value, bool signed)
    {
        string s = Mathf.Abs(value).ToString("N0") + " G";
        if (!signed || value == 0) return value < 0 ? "-" + s : s;
        return (value > 0 ? "+" : "-") + s;
    }

    /// <summary>지역화 헬퍼 — 키가 없으면 한국어 폴백 (StockLoc 패턴).</summary>
    private static string Loc(string key, string fallback)
    {
        if (LanguageManager.Instance == null) return fallback;
        string v = LanguageManager.Instance.L(key);
        return string.IsNullOrEmpty(v) || v == key ? fallback : v;
    }

    private static string LocF(string key, string fallback, params object[] args)
    {
        string fmt = Loc(key, fallback);
        try { return string.Format(fmt, args); }
        catch { return fmt; }
    }
}
