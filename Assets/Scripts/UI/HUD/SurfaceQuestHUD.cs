// @tags: hud, surface, quest, ui, code-generated, localization, sprite

using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지상 <b>우상단</b>에 퀘스트 미리보기를 띄우는 HUD(날짜/시간 HUD와 분리된 독립 컴포넌트).
/// 캔버스/모서리 앵커/지상 표시 판정은 <see cref="SurfaceCornerHUD"/>가 처리하고, 이 클래스는
/// 퀘스트 블록만 채운다.
///
/// 구성(위→아래, 배경 패널 위에 텍스트만):
///  '퀘스트' 헤더 바 → [메인 아이콘] 메인 제목 + <b>요구 광물들</b> → (서브가 있으면) [서브 아이콘] 서브 제목 + 요구 광물들.
/// '메인/서브 퀘스트' 라벨 텍스트 대신 <see cref="mainQuestIcon"/>/<see cref="subQuestIcon"/> 아이콘을 제목 앞에 둔다.
/// 데이터는 <see cref="QuestManager"/>(GetActiveMainQuest / GetSubQuestInSlot)에서 읽고 폴링으로 갱신한다.
/// 각 퀘스트의 <see cref="QuestSO.requirements"/>를 아이콘 + 이름 + 개수(창고 보유/필요, 충족 시 초록)로 제목 아래에 보여준다.
/// 제목 안 '따옴표'로 묶인 부분만 강조색, 나머지 제목은 흰색으로 그린다. 텍스트는 전부 왼쪽 정렬.
/// </summary>
public class SurfaceQuestHUD : SurfaceCornerHUD
{
    // ===================================================
    // 인스펙터 — 배경 / 헤더
    // ===================================================
    [Header("퀘스트 배경 패널")]
    [Tooltip("퀘스트 영역 전체를 감싸는 배경 스프라이트. 비우면 어두운 반투명 라운드 상자로 폴백")]
    [SerializeField] private Sprite questPanelSprite;
    [Tooltip("퀘스트 배경 패널 색. 밝은 지상 배경 위에서 글씨가 잘 보이도록 어두운 반투명 권장")]
    [SerializeField] private Color questPanelTint = new Color(0.055f, 0.086f, 0.13f, 0.82f);
    [Tooltip("퀘스트 배경 패널 안쪽 여백 (x=좌, y=우, z=상, w=하)")]
    [SerializeField] private Vector4 questPanelPadding = new Vector4(16f, 16f, 12f, 14f);
    [Tooltip("퀘스트 패널 가로 폭(px). 0이면 내용에 맞춰 자동. 값을 키우면 좌우로 넓어지고 긴 제목은 줄바꿈된다")]
    [SerializeField] private float questPanelWidth = 480f;
    [Range(0.25f, 8f)][SerializeField] private float spritePixelsPerUnit = 1f;

    [Header("'퀘스트' 헤더")]
    [Tooltip("헤더 배경 스프라이트. 비우면 코드 생성 라운드 상자")]
    [SerializeField] private Sprite questHeaderSprite;
    [SerializeField] private Color questHeaderBgTint = new Color(0.08f, 0.12f, 0.10f, 0.92f);
    [SerializeField] private Color questHeaderTextColor = new Color(0.62f, 0.85f, 0.60f, 1f);

    [Header("퀘스트 종류 아이콘 (제목 앞)")]
    [Tooltip("메인 퀘스트 제목 앞에 붙는 아이콘(A). '메인 퀘스트' 라벨 텍스트를 대체")]
    [SerializeField] private Sprite mainQuestIcon;
    [Tooltip("서브 퀘스트 제목 앞에 붙는 아이콘(B). '서브 퀘스트' 라벨 텍스트를 대체")]
    [SerializeField] private Sprite subQuestIcon;
    [Tooltip("퀘스트 종류 아이콘 크기(px)")]
    [SerializeField] private float questIconSize = 34f;

    [Header("텍스트 색 / 크기")]
    [Tooltip("퀘스트 제목(따옴표 밖) 색 — 기본 흰색")]
    [SerializeField] private Color questTitleColor = Color.white;
    [Tooltip("제목 안 '따옴표'로 묶인 부분 강조색")]
    [SerializeField] private Color questQuoteColor = new Color(0.96f, 0.78f, 0.25f, 1f);
    [SerializeField] private float questHeaderFontSize = 26f;
    [SerializeField] private float questTitleFontSize = 24f;

    [Header("요구 광물 (제목 아래 표시)")]
    [Tooltip("각 퀘스트가 요구하는 광물의 아이콘·이름·개수를 제목 아래에 표시")]
    [SerializeField] private bool showRequirements = true;
    [Tooltip("광물 아이콘 크기")]
    [SerializeField] private float reqIconSize = 30f;
    [Tooltip("광물 아이콘에 외곽선을 그린다. 지하 광물 근접 하이라이트와 같은 Custom/SpriteOutline 셰이더를 써서 스프라이트 모양을 따라 그려진다")]
    [SerializeField] private bool reqIconOutline = false;
    [Tooltip("외곽선 셰이더. 비우면 Custom/SpriteOutline 을 자동으로 찾는다")]
    [SerializeField] private Shader reqIconOutlineShader;
    [Tooltip("외곽선 색 — 기본 흰색")]
    [SerializeField] private Color reqIconOutlineColor = Color.white;
    [Tooltip("외곽선 두께(텍셀). '한 칸' = 1")]
    [Range(1f, 4f)][SerializeField] private float reqIconOutlineThickness = 1f;
    [Range(0f, 1f)][SerializeField] private float reqIconOutlineAlpha = 1f;
    [SerializeField] private float reqFontSize = 20f;
    [Tooltip("광물 이름 색")]
    [SerializeField] private Color reqTextColor = new Color(0.80f, 0.84f, 0.90f, 1f);
    [Tooltip("개수(보유/필요) 색 — 아직 부족할 때")]
    [SerializeField] private Color reqCountColor = new Color(0.96f, 0.85f, 0.50f, 1f);
    [Tooltip("개수 색 — 필요량을 채웠을 때")]
    [SerializeField] private Color reqMetColor = new Color(0.55f, 0.85f, 0.55f, 1f);
    [Tooltip("요구 광물 줄 추가 들여쓰기(px). 기본 정렬은 제목 시작 위치(종류 아이콘 폭 + 간격)에 맞추고, 이 값만큼 더 들여쓴다")]
    [SerializeField] private float reqIndent = 0f;

    [Header("로컬라이제이션 키 (UI_Localization.csv)")]
    [SerializeField] private string questHeaderKey = "hud_quest_header";

    // ===================================================
    // 내부 상태
    // ===================================================
    private GameObject _questBlock;
    private TextMeshProUGUI _questHeaderText;

    // 퀘스트 헤더 행([종류 아이콘][제목])
    private class HeaderRow { public GameObject go; public Image icon; public TextMeshProUGUI title; }
    private HeaderRow _mainHeader;
    private HeaderRow[] _subHeaders;

    // 요구 광물 행 풀 (퀘스트별로 미리 만들어 두고 켜고/끈다 — 매 폴링 재생성 방지)
    // outline/outlineMat: reqIconOutline이 켜졌을 때만. 지하 광물 근접 테두리와 같은 셰이더 레이어.
    private class ReqRow { public GameObject go; public Image icon; public TextMeshProUGUI text; public Image outline; public Material outlineMat; }
    private ReqRow[] _mainReqRows;
    private ReqRow[][] _subReqRows;
    private const int MaxReqPerQuest = 4;

    // 외곽선 셰이더 캐시 + 생성한 인스턴스 머티리얼(OnDestroy에서 해제).
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OutlineAlphaId = Shader.PropertyToID("_OutlineAlpha");
    private Shader _resolvedOutlineShader;
    private readonly System.Collections.Generic.List<Material> _outlineMats = new System.Collections.Generic.List<Material>();

    // 헤더 행([아이콘][제목])의 아이콘↔제목 간격. 요구 광물 줄 들여쓰기를 제목에 맞출 때도 쓴다.
    private const float HeaderRowSpacing = 9f;

    private static readonly Regex QuoteRx = new Regex("('[^']*'|‘[^’]*’)");

    private static readonly Color DefaultHeaderColor = new Color(0.62f, 0.85f, 0.60f, 1f);
    private static readonly Color DefaultQuestPanelTint = new Color(0.055f, 0.086f, 0.13f, 0.82f);
    private static readonly Color DefaultReqTextColor = new Color(0.80f, 0.84f, 0.90f, 1f);
    private static readonly Color DefaultCountColor = new Color(0.96f, 0.85f, 0.50f, 1f);
    private static readonly Color DefaultMetColor = new Color(0.55f, 0.85f, 0.55f, 1f);

    /// <summary>새로 붙일 때 기본 모서리를 우상단으로. (베이스 기본은 좌상단)</summary>
    private void Reset()
    {
        corner = Corner.TopRight;
    }

    // ===================================================
    // 조립
    // ===================================================
    protected override void BuildContent(RectTransform column)
    {
        var blockRt = CodeUI.CreateRect(column, "QuestPreview");
        _questBlock = blockRt.gameObject;

        // 퀘스트 영역 전체 배경 패널 (밝은 지상 배경 위에서 잘 보이도록).
        var panelBg = _questBlock.AddComponent<Image>();
        CodeUI.ApplySkin(panelBg, VisibleBg(questPanelTint, DefaultQuestPanelTint), questPanelSprite, null);
        panelBg.pixelsPerUnitMultiplier = Mathf.Max(0.01f, spritePixelsPerUnit);
        panelBg.raycastTarget = false;

        var vlg = _questBlock.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(
            Mathf.RoundToInt(questPanelPadding.x), Mathf.RoundToInt(questPanelPadding.y),
            Mathf.RoundToInt(questPanelPadding.z), Mathf.RoundToInt(questPanelPadding.w));
        vlg.spacing = Mathf.Max(2f, blockSpacing * 0.5f);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;   // 줄들을 패널 폭에 맞춰 늘려 '왼쪽 정렬'이 의미를 갖게 한다
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        // 패널 가로 폭 고정(넓히기). 0이면 내용에 맞춰 자동.
        if (questPanelWidth > 1f)
        {
            var widthLe = _questBlock.AddComponent<LayoutElement>();
            widthLe.preferredWidth = widthLe.minWidth = questPanelWidth;
        }

        float headerPx = questHeaderFontSize > 1f ? questHeaderFontSize : 26f;
        Color headerColor = Visible(questHeaderTextColor, DefaultHeaderColor);

        // 헤더 "퀘스트" — 풀폭 바 + 왼쪽 액센트 마커 + 왼쪽 텍스트
        var headerRt = CodeUI.CreateRect(_questBlock.transform, "QuestHeader");
        var headerBg = headerRt.gameObject.AddComponent<Image>();
        CodeUI.ApplySkin(headerBg, questHeaderBgTint, questHeaderSprite, null);
        headerBg.pixelsPerUnitMultiplier = Mathf.Max(0.01f, spritePixelsPerUnit);
        headerBg.raycastTarget = false;

        var headerRow = headerRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerRow.padding = new RectOffset(14, 16, 6, 6);
        headerRow.spacing = 9f;
        headerRow.childControlWidth = true;
        headerRow.childControlHeight = true;
        headerRow.childForceExpandWidth = false;
        headerRow.childForceExpandHeight = false;
        headerRow.childAlignment = TextAnchor.MiddleLeft;

        // 액센트 마커 (세로 막대)
        var marker = CodeUI.CreateImage(headerRt, "Marker", headerColor, rounded: false);
        marker.raycastTarget = false;
        var markerLe = marker.gameObject.AddComponent<LayoutElement>();
        markerLe.preferredWidth = markerLe.minWidth = 5f;
        markerLe.preferredHeight = markerLe.minHeight = headerPx * 0.95f;

        _questHeaderText = CodeUI.CreateText(headerRt, "Text", headerPx, FontStyles.Bold,
            headerColor, TextAlignmentOptions.Left, _binder);
        _questHeaderText.characterSpacing = 4f;
        _questHeaderText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // 메인 퀘스트: [아이콘 A] 제목 → 요구 광물들
        _mainHeader = MakeHeaderRow("MainHeader", mainQuestIcon);
        _mainReqRows = showRequirements ? MakeReqRows("MainReq", MaxReqPerQuest) : null;

        // 서브 퀘스트: ([아이콘 B] 제목 → 요구 광물들) × 슬롯 수
        int slots = Mathf.Max(1, QuestManager.MAX_SUB_QUEST_SLOTS);
        _subHeaders = new HeaderRow[slots];
        _subReqRows = new ReqRow[slots][];
        for (int i = 0; i < slots; i++)
        {
            _subHeaders[i] = MakeHeaderRow($"SubHeader{i}", subQuestIcon);
            _subReqRows[i] = showRequirements ? MakeReqRows($"SubReq{i}", MaxReqPerQuest) : null;
        }
    }

    /// <summary>퀘스트 헤더 행 생성([종류 아이콘][제목], 처음엔 비활성). 라벨 텍스트를 대체하는 아이콘 + 제목.</summary>
    private HeaderRow MakeHeaderRow(string name, Sprite iconSprite)
    {
        var rowRt = CodeUI.CreateRect(_questBlock.transform, name);
        var hlg = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(0, 0, 2, 2);
        hlg.spacing = HeaderRowSpacing;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        float iconPx = questIconSize > 1f ? questIconSize : 34f;
        var icon = CodeUI.CreateImage(rowRt, "Icon", Color.white, rounded: false);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.sprite = iconSprite;
        icon.enabled = iconSprite != null;
        var le = icon.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = iconPx;
        le.preferredHeight = le.minHeight = iconPx;

        var title = CodeUI.CreateText(rowRt, "Title", questTitleFontSize > 1f ? questTitleFontSize : 24f,
            FontStyles.Bold, Visible(questTitleColor, Color.white), TextAlignmentOptions.Left, _binder);
        title.textWrappingMode = TextWrappingModes.Normal; // 고정 폭일 때 긴 제목은 줄바꿈
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        rowRt.gameObject.SetActive(false);
        return new HeaderRow { go = rowRt.gameObject, icon = icon, title = title };
    }

    /// <summary>요구 광물 행 풀 생성([아이콘][이름 개수], 처음엔 전부 비활성).</summary>
    private ReqRow[] MakeReqRows(string namePrefix, int count)
    {
        var rows = new ReqRow[count];
        float iconPx = reqIconSize > 1f ? reqIconSize : 30f;
        float fontPx = reqFontSize > 1f ? reqFontSize : 20f;
        // 제목 시작 위치(종류 아이콘 폭 + 헤더 간격)에 맞추고, reqIndent만큼 추가로 들여쓴다.
        float questIconPx = questIconSize > 1f ? questIconSize : 34f;
        int indent = Mathf.RoundToInt(questIconPx + HeaderRowSpacing + Mathf.Max(0f, reqIndent));

        for (int i = 0; i < count; i++)
        {
            var rowRt = CodeUI.CreateRect(_questBlock.transform, $"{namePrefix}{i}");
            var hlg = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(indent, 0, 1, 1);
            hlg.spacing = 7f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var icon = CodeUI.CreateImage(rowRt, "Icon", Color.white, rounded: false);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var le = icon.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = iconPx;
            le.preferredHeight = le.minHeight = iconPx;

            // 외곽선: 지하 광물 근접 테두리와 같은 Custom/SpriteOutline 셰이더를 아이콘 위에 얹는다.
            // 셰이더가 스프라이트 몸통을 투명 처리하고 가장자리만 그리므로 아이콘을 가리지 않는다.
            // (스프라이트는 min이 정해질 때 FillReqs에서 채운다 — 텍셀 크기도 그때 동기화.)
            Image outline = null;
            Material outlineMat = null;
            if (reqIconOutline)
            {
                Shader sh = ResolveOutlineShader();
                if (sh != null)
                {
                    outline = CodeUI.CreateImage(icon.transform, "Outline", Color.white, rounded: false);
                    outline.preserveAspect = true;
                    outline.raycastTarget = false;
                    var ort = outline.rectTransform;
                    ort.anchorMin = Vector2.zero;
                    ort.anchorMax = Vector2.one;
                    ort.offsetMin = Vector2.zero;
                    ort.offsetMax = Vector2.zero;

                    outlineMat = new Material(sh);
                    outlineMat.SetColor(OutlineColorId, Visible(reqIconOutlineColor, Color.white));
                    outlineMat.SetFloat(OutlineWidthId, Mathf.Round(Mathf.Clamp(reqIconOutlineThickness, 1f, 4f)));
                    outlineMat.SetFloat(OutlineAlphaId, Mathf.Clamp01(reqIconOutlineAlpha));
                    outline.material = outlineMat;
                    outline.enabled = false;
                    _outlineMats.Add(outlineMat);
                }
            }

            var text = CodeUI.CreateText(rowRt, "Text", fontPx, FontStyles.Normal,
                Visible(reqTextColor, DefaultReqTextColor), TextAlignmentOptions.Left, _binder);
            text.textWrappingMode = TextWrappingModes.Normal; // 긴 광물 이름 줄바꿈
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            rowRt.gameObject.SetActive(false);
            rows[i] = new ReqRow { go = rowRt.gameObject, icon = icon, text = text, outline = outline, outlineMat = outlineMat };
        }
        return rows;
    }

    /// <summary>외곽선 셰이더 조회. 인스펙터 지정 우선, 없으면 Custom/SpriteOutline(지하 광물 테두리와 동일)을 찾는다.</summary>
    private Shader ResolveOutlineShader()
    {
        if (reqIconOutlineShader != null) return reqIconOutlineShader;
        if (_resolvedOutlineShader == null) _resolvedOutlineShader = Shader.Find("Custom/SpriteOutline");
        if (_resolvedOutlineShader == null)
            Debug.LogWarning("[SurfaceQuestHUD] Custom/SpriteOutline 셰이더를 찾을 수 없어 요구 광물 외곽선을 건너뜁니다.");
        return _resolvedOutlineShader;
    }

    // ===================================================
    // 갱신 (퀘스트는 전용 이벤트가 없어 폴링으로도 갱신)
    // ===================================================
    protected override void PollContent() => RefreshContent();

    protected override void RefreshContent()
    {
        if (_questBlock == null) return; // 아직 Build 전

        var qm = QuestManager.Instance;
        QuestSO main = qm != null ? qm.GetActiveMainQuest() : null;
        int subCount = qm != null ? qm.GetActiveSubQuestCount() : 0;

        bool hasMain = main != null;
        bool hasSub = subCount > 0;

        // 진행 중인 퀘스트가 아예 없으면 퀘스트 HUD 블록 자체를 숨깁니다.
        if (!hasMain && !hasSub)
        {
            _questBlock.SetActive(false);
            return;
        }
        else
        {
            _questBlock.SetActive(true);
        }

        // 헤더는 항상 표시 (퀘스트가 1개라도 있을 때)
        if (_questHeaderText != null)
            _questHeaderText.text = CodeUI.L(questHeaderKey, "퀘스트");

        // 메인 퀘스트: [아이콘 A] 제목
        SetHeader(_mainHeader, hasMain, hasMain ? main.QuestName : "");
        FillReqs(_mainReqRows, hasMain ? main.requirements : null);

        // 서브 퀘스트: 슬롯마다 [아이콘 B] 제목 (있을 때만)
        if (_subHeaders != null) subCount = Mathf.Min(subCount, _subHeaders.Length);

        if (_subHeaders != null)
        {
            for (int i = 0; i < _subHeaders.Length; i++)
            {
                QuestSO sub = (i < subCount && qm != null) ? qm.GetSubQuestInSlot(i) : null;
                SetHeader(_subHeaders[i], sub != null, sub != null ? sub.QuestName : "");
                if (_subReqRows != null) FillReqs(_subReqRows[i], sub != null ? sub.requirements : null);
            }
        }

        _binder.Refresh(); // 폰트 갱신
    }

    /// <summary>요구 광물 행들을 채운다. reqs가 null이면 전부 숨긴다(제목이 숨겨졌을 때).</summary>
    private void FillReqs(ReqRow[] rows, System.Collections.Generic.List<QuestRequirement> reqs)
    {
        if (rows == null) return;
        int n = reqs != null ? reqs.Count : 0;
        var wm = WarehouseManager.Instance;

        for (int i = 0; i < rows.Length; i++)
        {
            var req = (i < n && reqs[i] != null) ? reqs[i] : null;
            var row = rows[i];
            
            if (req == null) { row.go.SetActive(false); continue; }
            row.go.SetActive(true);

            if (req.type == RequirementType.Mineral)
            {
                MineralSO min = req.requiredMineral;
                if (min == null) { row.go.SetActive(false); continue; }
                
                row.icon.sprite = min.Icon;
                row.icon.enabled = min.Icon != null;
                
                if (row.outline != null)
                {
                    row.outline.sprite = min.Icon;
                    bool hasTex = min.Icon != null && min.Icon.texture != null;
                    row.outline.enabled = hasTex;
                    if (hasTex && row.outlineMat != null)
                        row.outlineMat.mainTexture = min.Icon.texture;
                }
                
                int amount = req.requiredAmount;
                int have = wm != null ? wm.GetMineralCount(min) : -1;
                row.text.text = FormatReq(min.DisplayName, have, amount);
            }
            else if (req.type == RequirementType.SceneVisit)
            {
                row.icon.enabled = false;
                if (row.outline != null) row.outline.enabled = false;
                
                bool met = QuestManager.Instance != null && QuestManager.Instance.HasQuestFlag("Scene_" + req.targetString);
                row.text.text = FormatFlagReq($"지역 방문: {req.targetString}", met);
            }
            else if (req.type == RequirementType.CustomFlag)
            {
                row.icon.enabled = false;
                if (row.outline != null) row.outline.enabled = false;
                
                bool met = QuestManager.Instance != null && QuestManager.Instance.HasQuestFlag(req.targetString);
                row.text.text = FormatFlagReq($"{req.targetString}", met);
            }
        }
    }

    /// <summary>"이름  보유/필요"(창고 있으면) 또는 "이름 ×필요". 개수는 충족 시 초록, 아니면 강조색.</summary>
    private string FormatReq(string name, int have, int amount)
    {
        if (have < 0)
            return $"{name} <color=#{ColorUtility.ToHtmlStringRGB(Visible(reqCountColor, DefaultCountColor))}>×{amount}</color>";

        Color c = have >= amount ? Visible(reqMetColor, DefaultMetColor) : Visible(reqCountColor, DefaultCountColor);
        return $"{name} <color=#{ColorUtility.ToHtmlStringRGB(c)}>{have}/{amount}</color>";
    }
    
    private string FormatFlagReq(string name, bool met)
    {
        Color c = met ? Visible(reqMetColor, DefaultMetColor) : Visible(reqCountColor, DefaultCountColor);
        return $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{name}</color>";
    }

    /// <summary>헤더 행([아이콘][제목])을 켜고/끄고 제목을 채운다. 제목 안 따옴표는 강조색.</summary>
    private void SetHeader(HeaderRow h, bool show, string title)
    {
        if (h == null) return;
        h.go.SetActive(show);
        if (!show) return;
        h.title.text = ColorizeQuotes(title);
    }

    /// <summary>제목 안 '따옴표'(ASCII ' 또는 ‘ ’)로 묶인 부분만 강조색으로 감싼다. 나머지는 원래 색(흰색).</summary>
    private string ColorizeQuotes(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        string hex = ColorUtility.ToHtmlStringRGB(Visible(questQuoteColor, DefaultTimeColorRef));
        return QuoteRx.Replace(raw, m => $"<color=#{hex}>{m.Value}</color>");
    }

    // 강조색 폴백(앰버). 날짜 HUD의 시간색과 같은 톤.
    private static readonly Color DefaultTimeColorRef = new Color(0.96f, 0.78f, 0.25f, 1f);

    // 외곽선 인스턴스 머티리얼 메모리 해제.
    private void OnDestroy()
    {
        for (int i = 0; i < _outlineMats.Count; i++)
            if (_outlineMats[i] != null) Destroy(_outlineMats[i]);
        _outlineMats.Clear();
    }
}
