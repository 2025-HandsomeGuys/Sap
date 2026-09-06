// @tags: ui, stat, panel, code-generated, player, shared, warehouse, inventory

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 창고·인벤토리 오버레이가 공용으로 쓰는 '플레이어 스탯' 패널.
/// (기존 퀘스트 미리보기 자리를 대체한다)
///
/// <see cref="StatCatalog"/>의 큐레이션된 스탯을 카테고리별로 묶어
/// [카테고리 심볼] + [스탯명] + [수치] 형태로 나열한다.
/// 한 줄에 **두 칸씩**(좌·우) 배치해 이름과 수치가 붙어 한눈에 읽히게 한다.
/// 업그레이드·장비로 기본값에서 강화된 스탯은 초록/주황으로 강조하고 강화량(+N%·+N)을 함께 표기하며,
/// 강화되지 않은 스탯은 흐리게 둔다.
///
/// UI는 전부 코드로 생성한다(CodeBagPanel과 같은 패턴). 값 갱신은 <see cref="Refresh"/>가 담당한다.
/// </summary>
public class CodeStatPanel
{
    public class Config
    {
        public UISkin skin;
        public string diamondGlyph = "◆";
        public float rowHeight = 34f;

        /// <summary>인스펙터 아이콘 오버라이드(비우면 코드 심볼).</summary>
        public StatIconSet icons;

        /// <summary>true면 기본값에서 바뀐(강화된) 스탯만 보여준다. false면 전체를 보여주고 강화된 것만 강조.</summary>
        public bool showOnlyEnhanced = false;
    }

    public RectTransform Root { get; }

    private const float Eps = 0.0001f;

    private readonly Config _cfg;
    private readonly LocTextBinder _loc;
    private readonly List<Group> _groups = new List<Group>();
    private readonly List<Row> _rows = new List<Row>();

    private TextMeshProUGUI _emptyText;

    private class Group
    {
        public GameObject root;                  // 카테고리 헤더
        public StatCategory category;
        public readonly List<GameObject> pairRows = new List<GameObject>(); // 이 카테고리의 2열 줄들
    }

    private class Row
    {
        public GameObject root;      // 스탯 한 칸(셀)
        public GameObject pairRow;   // 이 셀이 속한 2열 줄
        public StatDisplay info;
        public TextMeshProUGUI value;
        public TextMeshProUGUI delta;
    }

    public CodeStatPanel(Transform parent, Config config, LocTextBinder loc)
    {
        _cfg = config ?? new Config();
        _loc = loc;

        // 스크롤 골격 — 스탯이 많아도 창 안에서 스크롤로 소화한다.
        var scroll = CodeUI.CreateScrollView(parent, "StatScroll", out var content);
        Root = (RectTransform)scroll.transform;

        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 5f;
        layout.padding = new RectOffset(2, 6, 4, 4);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        BuildRows(content);

        // 강화된 스탯이 하나도 없을 때(showOnlyEnhanced 모드) 안내
        _emptyText = CodeUI.CreateText(content, "Empty", 15f, FontStyles.Italic, CodeUI.MutedColor,
            TextAlignmentOptions.Top, _loc);
        _emptyText.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        _loc.Bind(_emptyText, "ui_stat_none", "강화된 스탯이 없습니다.");
        _emptyText.gameObject.SetActive(false);
    }

    private void BuildRows(Transform content)
    {
        var stats = StatCatalog.Stats;
        int i = 0;

        while (i < stats.Length)
        {
            StatCategory cat = stats[i].category;

            var group = new Group { category = cat, root = BuildHeader(content, cat) };

            // 같은 카테고리를 두 칸씩 한 줄에 배치한다.
            while (i < stats.Length && stats[i].category == cat)
            {
                var pair = BuildPairRow(content);
                group.pairRows.Add(pair);

                _rows.Add(BuildCell(pair.transform, stats[i], pair));
                i++;

                if (i < stats.Length && stats[i].category == cat)
                {
                    _rows.Add(BuildCell(pair.transform, stats[i], pair));
                    i++;
                }
                else
                {
                    // 홀수 개면 오른쪽은 빈 칸으로 둬 왼쪽 칸이 절반 폭을 유지하게 한다.
                    CodeUI.CreateSpacer(pair.transform);
                }
            }

            _groups.Add(group);
        }
    }

    private GameObject BuildHeader(Transform content, StatCategory category)
    {
        Color accent = StatCatalog.CategoryColor(category);

        var row = CodeUI.CreateRow(content, "Cat_" + category, 26f, 8f);

        var icon = CodeUI.CreateImage(row, "Icon", accent, rounded: false);
        icon.sprite = ResolveIcon(category);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var iconLe = icon.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = 20f;
        iconLe.preferredHeight = iconLe.minHeight = 20f;

        var label = CodeUI.CreateText(row, "Label", 16f, FontStyles.Bold, accent,
            TextAlignmentOptions.MidlineLeft, _loc);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _loc.Bind(label, StatCatalog.CategoryKey(category), StatCatalog.CategoryFallback(category));

        return row.gameObject;
    }

    /// <summary>두 칸(좌·우)을 담는 한 줄. 각 칸은 flexibleWidth로 절반씩 나눠 갖는다.</summary>
    private GameObject BuildPairRow(Transform content)
    {
        var row = CodeUI.CreateRect(content, "PairRow");
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = _cfg.rowHeight;

        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false; // 폭은 각 칸의 flexibleWidth 비율로 나눈다
        layout.childForceExpandHeight = true;
        return row.gameObject;
    }

    private Row BuildCell(Transform pairRow, StatDisplay info, GameObject pairRowGo)
    {
        Color accent = StatCatalog.CategoryColor(info.category);

        var bg = CodeUI.CreateImage(pairRow, "Stat_" + info.type, CodeUI.SlotBg, _cfg.skin.slotSprite, _cfg.skin);
        var bgLe = bg.gameObject.AddComponent<LayoutElement>();
        // 칸 폭을 내부 내용(이름 길이)이 아니라 '정확히 절반'으로 고정한다.
        // preferredWidth/minWidth를 0으로 명시(우선순위가 높은 LayoutElement가 내부 HLG의 계산을 덮어씀)해야
        // 이름이 길고 짧음·짝/홀수와 무관하게 좌우 열 경계와 수치 오른쪽 끝이 줄마다 딱 맞는다.
        bgLe.minWidth = 0f;
        bgLe.preferredWidth = 0f;
        bgLe.flexibleWidth = 1f; // 두 칸이 절반씩
        bgLe.preferredHeight = _cfg.rowHeight;
        var layout = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 3, 3);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        // 아이콘(카테고리 심볼, 카테고리 색)
        var icon = CodeUI.CreateImage(bg.transform, "Icon", accent, rounded: false);
        icon.sprite = ResolveIcon(info.category);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        float iconSize = _cfg.rowHeight - 14f;
        var iconLe = icon.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = iconSize;
        iconLe.preferredHeight = iconLe.minHeight = iconSize;

        // 스탯명
        var name = CodeUI.CreateText(bg.transform, "Name", 15f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineLeft, _loc);
        name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        name.overflowMode = TextOverflowModes.Ellipsis;
        _loc.Bind(name, info.nameKey, info.nameFallback);

        // 강화량(+N% / +N) — "+1000%" 같은 큰 값도 안 잘리게 폭을 잡는다
        var delta = CodeUI.CreateText(bg.transform, "Delta", 13f, FontStyles.Bold, CodeUI.PositiveColor,
            TextAlignmentOptions.MidlineRight, _loc);
        var deltaLe = delta.gameObject.AddComponent<LayoutElement>();
        deltaLe.preferredWidth = deltaLe.minWidth = 52f;

        // 수치
        var value = CodeUI.CreateText(bg.transform, "Value", 16f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineRight, _loc);
        var valueLe = value.gameObject.AddComponent<LayoutElement>();
        // 62 → 96. 채굴 범위 줄이 "×0.79 동결→×0.63"처럼 감쇠 후 값을 나란히 달기 때문.
        // 셀 폭은 모든 줄이 공유해야 2열 정렬이 유지되므로 이 줄만 넓힐 수는 없다.
        valueLe.preferredWidth = valueLe.minWidth = 96f;

        return new Row { root = bg.gameObject, pairRow = pairRowGo, info = info, value = value, delta = delta };
    }

    private Sprite ResolveIcon(StatCategory category)
    {
        Sprite over = _cfg.icons != null ? _cfg.icons.Get(category) : null;
        return over != null ? over : CodeStatIcons.Get(category);
    }

    // ===================================================
    // 값 갱신
    // ===================================================
    public void Refresh(PlayerStat playerStat)
    {
        if (playerStat == null)
        {
            // 스탯을 못 읽으면 전부 흐리게 '-'만 표기
            foreach (var row in _rows)
            {
                row.value.text = "-";
                row.value.color = CodeUI.MutedColor;
                row.delta.text = string.Empty;
            }
            return;
        }

        // 2열 정렬을 지키려 셀은 숨기지 않고 '줄' 단위로만 숨긴다(showOnlyEnhanced 모드에서만).
        var enhancedPairs = new HashSet<GameObject>();

        foreach (var row in _rows)
        {
            float baseV = playerStat.GetBaseValue(row.info.type);
            float finalV = playerStat.GetFinalValue(row.info.type);
            float diff = finalV - baseV;
            bool enhanced = Mathf.Abs(diff) > Eps;

            row.value.text = Format(finalV, row.info.format) + LayerPenaltySuffix(row.info, finalV, playerStat);

            if (enhanced)
            {
                bool better = (diff > 0f) == row.info.higherIsBetter;
                Color c = better ? CodeUI.PositiveColor : CodeUI.NegativeColor;
                row.value.color = c;
                row.delta.text = DeltaText(baseV, diff, row.info.format);
                row.delta.color = c;
                if (row.pairRow != null) enhancedPairs.Add(row.pairRow);
            }
            else
            {
                row.value.color = CodeUI.LabelColor;
                row.delta.text = string.Empty;
            }
        }

        // showOnlyEnhanced일 때만 비어 있는 줄/카테고리를 접는다.
        int totalVisible = 0;
        foreach (var g in _groups)
        {
            int visiblePairs = 0;
            foreach (var pr in g.pairRows)
            {
                bool show = !_cfg.showOnlyEnhanced || enhancedPairs.Contains(pr);
                pr.SetActive(show);
                if (show) { visiblePairs++; totalVisible++; }
            }
            g.root.SetActive(!_cfg.showOnlyEnhanced || visiblePairs > 0);
        }

        if (_emptyText != null)
            _emptyText.gameObject.SetActive(_cfg.showOnlyEnhanced && totalVisible == 0);
    }

    // ===================================================
    // 표기
    // ===================================================
    /// <summary>
    /// 지금 서 있는 층이 파기 반경을 깎고 있으면 "동결→×0.63"을 수치 뒤에 덧붙인다.
    ///
    /// 스탯값 자체를 감쇠 후 값으로 덮지 않는 게 핵심이다 — 플레이어가 산 업그레이드는
    /// 그대로 보이고, 깎는 주체가 지형이라는 게 읽혀야 한다. 감쇠는 LayerDigModifier가
    /// 파기 순간에만 곱하므로 스탯 시스템에는 흔적이 없고, 여기서만 따로 조회한다.
    /// </summary>
    private static string LayerPenaltySuffix(StatDisplay info, float finalV, PlayerStat playerStat)
    {
        if (info.type != StatType.MiningRange) return string.Empty;

        float mod = LayerDigModifier.ResolveAtPlayer(playerStat);
        if (mod > 0.999f) return string.Empty;   // 지상·1지층·저항으로 완전 상쇄

        LayerDigModifier.PenaltyLabel(
            LayerDigModifier.TileTypeAtWorldY(playerStat.transform.position.y),
            out string locKey, out string fallback);

        string hex = ColorUtility.ToHtmlStringRGB(CodeUI.NegativeColor);
        return $" <size=60%><color=#{hex}>{CodeUI.L(locKey, fallback)}→" +
               $"{Format(finalV * mod, info.format)}</color></size>";
    }

    private static string Format(float v, StatValueFormat f)
    {
        switch (f)
        {
            case StatValueFormat.Multiplier: return "×" + v.ToString("0.##");
            case StatValueFormat.Percent01: return Mathf.RoundToInt(v * 100f) + "%";
            case StatValueFormat.Seconds: return v.ToString("0.##") + "s";
            default:
                if (Mathf.Abs(v - Mathf.Round(v)) < 0.05f) return Mathf.RoundToInt(v).ToString();
                return v.ToString("0.#");
        }
    }

    /// <summary>강화량 표기 — 기본값이 있으면 상대(%), 없으면 절대값.</summary>
    private static string DeltaText(float baseV, float diff, StatValueFormat f)
    {
        string sign = diff > 0f ? "+" : "-";
        float mag = Mathf.Abs(diff);

        if (Mathf.Abs(baseV) > 0.001f)
        {
            int pct = Mathf.RoundToInt(mag / Mathf.Abs(baseV) * 100f);
            return sign + pct + "%";
        }

        // 기본값이 0인 스탯(치명타 확률·배터리 회복 등)은 절대값으로 보여준다.
        var absFmt = f == StatValueFormat.Percent01 ? StatValueFormat.Percent01 : StatValueFormat.Plain;
        return sign + Format(mag, absFmt);
    }
}
