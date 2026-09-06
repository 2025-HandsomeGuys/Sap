// @tags: quest, ui, view, code-generated, shared, embedded, log, requirement, reward

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 창고·인벤토리 오버레이의 '퀘스트' 탭에 끼워 넣는 재사용 퀘스트 브라우저.
///
/// <see cref="QuestOverlayUI"/>(J 키 단독 퀘스트 로그)의 <b>읽기 전용 본문</b>을 그대로 옮겨 온 것으로,
/// 같은 정보를 같은 모양으로 보여준다:
///  - 내부 탭 [메인] [서브 I] [서브 II] (클릭 또는 소유자가 <see cref="StepTab"/> 호출)
///  - 수락 상태의 퀘스트만 요구 광물(창고 보유/필요)·보상을 펼친다
///
/// 제출(완료 확정)은 트럭 NPC 흐름이 담당하므로 여기에는 제출 버튼이 없다(오버레이는 확인용).
/// 값 갱신은 <see cref="Refresh"/>가 담당하며, 갱신 후 <see cref="onChanged"/>가 호출된다.
/// </summary>
public class CodeQuestView
{
    public class Config
    {
        public UISkin skin;
        public float rowHeight = 60f;
        public string diamondGlyph = "◆";
        public string clickSfxName = SfxKeys.UiClick;
    }

    private const int TabCount = 3; // 메인 + 서브 슬롯 2개

    public RectTransform Root { get; }
    public int Tab => _tab;
    public QuestSO CurrentQuest { get; private set; }

    /// <summary>Refresh 끝에 호출된다(소유자가 부가 UI를 동기화할 때).</summary>
    public System.Action onChanged;

    private readonly Config _cfg;
    private readonly LocTextBinder _loc;

    private int _tab;
    private readonly List<(Button btn, TextMeshProUGUI label, int index)> _tabs = new List<(Button, TextMeshProUGUI, int)>();

    // 내부 탭도 소유자(창고·인벤토리 오버레이)의 WASD 커서가 돌아다닐 수 있게 항목으로 노출한다.
    private readonly List<CodeNavButton> _tabNav = new List<CodeNavButton>();
    private TextMeshProUGUI _questTitle, _statusText, _descText;
    private RectTransform _reqSection, _reqList, _rewardSection, _rewardList;
    private TextMeshProUGUI _reqHeader, _rewardHeader;

    public CodeQuestView(Transform parent, Config config, LocTextBinder loc)
    {
        _cfg = config ?? new Config();
        _loc = loc;

        var card = CodeUI.CreateImage(parent, "QuestView", CodeUI.CardBg, _cfg.skin.cardSprite, _cfg.skin);
        Root = card.rectTransform;
        CodeUI.StretchFull(Root);

        const float pad = 16f;
        const float tabH = 42f;

        BuildTabs(Root, pad, tabH);
        BuildBody(Root, pad, tabH);
    }

    private void BuildTabs(Transform root, float pad, float tabH)
    {
        var row = CodeUI.CreateRect(root, "QTabs");
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.offsetMin = new Vector2(pad, -(pad + tabH));
        row.offsetMax = new Vector2(-pad, -pad);
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
        var btn = CodeUI.CreateButton(parent, "QTab" + index, CodeUI.TabIdleBg, () => SwitchTab(index),
            _cfg.skin.tabSprite, _cfg.skin);
        var label = CodeUI.CreateText(btn.transform, "Text", 18f, FontStyles.Bold, CodeUI.QuestColor,
            TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(label.rectTransform);
        _loc.Bind(label, key, fallback);
        _tabs.Add((btn, label, index));

        var nav = CodeNavButton.Attach(btn, _cfg.skin);
        if (nav != null) _tabNav.Add(nav);
    }

    /// <summary>
    /// 소유자의 <see cref="CodeSlotNavigator"/>에 이 뷰의 WASD 이동 대상을 넘긴다.
    /// 퀘스트 탭이 화면에 없을 때(가방·창고 본문을 보고 있을 때)는 아무것도 넣지 않는다 —
    /// 꺼진 항목은 내비게이터가 거르지만, 여기서 미리 걸러 두면 커서 계산이 헛돌지 않는다.
    /// </summary>
    public void CollectNavItems(List<ICodeNavItem> into)
    {
        if (into == null || Root == null || !Root.gameObject.activeInHierarchy) return;

        foreach (var nav in _tabNav)
            if (nav != null && nav.NavUsable) into.Add(nav);
    }

    private void BuildBody(Transform root, float pad, float tabH)
    {
        var scroll = CodeUI.CreateScrollView(root, "QBody", out var content);
        var scrollRt = (RectTransform)scroll.transform;
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.pivot = new Vector2(0.5f, 0.5f);
        scrollRt.offsetMin = new Vector2(pad, pad);
        scrollRt.offsetMax = new Vector2(-pad, -(pad + tabH + 8f));

        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 4, 4);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // 제목 + 상태 배지
        var head = CodeUI.CreateRow(content, "QuestHead", 40f, 10f);
        _questTitle = CodeUI.CreateText(head, "QuestTitle", 25f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.MidlineLeft, _loc);
        _questTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        _questTitle.overflowMode = TextOverflowModes.Ellipsis;
        _statusText = CodeUI.CreateText(head, "Status", 17f, FontStyles.Bold, CodeUI.MutedColor,
            TextAlignmentOptions.MidlineRight, _loc);
        _statusText.gameObject.AddComponent<LayoutElement>().preferredWidth = 210f;

        _descText = CodeUI.CreateText(content, "Desc", 17f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.TopLeft, _loc);
        _descText.textWrappingMode = TextWrappingModes.Normal;
        _descText.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;

        CodeUI.CreateDivider(content);

        // 필요 광물
        _reqSection = CodeUI.CreateColumn(content, "ReqSection", 6f);
        _reqHeader = CodeUI.CreateText(_reqSection, "ReqHeader", 17f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineLeft, _loc);
        _reqHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        _loc.Bind(_reqHeader, "quest_requirements", "필요 광물");
        _reqList = CodeUI.CreateColumn(_reqSection, "ReqList", 6f);

        // 보상
        _rewardSection = CodeUI.CreateColumn(content, "RewardSection", 6f);
        _rewardHeader = CodeUI.CreateText(_rewardSection, "RewardHeader", 17f, FontStyles.Bold, CodeUI.LabelColor,
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
        CodeUI.PlaySfx(_cfg.clickSfxName);
        _tab = index;
        Refresh();
    }

    public void StepTab(int dir)
    {
        int next = ((_tab + dir) % TabCount + TabCount) % TabCount;
        SwitchTab(next);
    }

    public void SetTab(int index)
    {
        if (index < 0 || index >= TabCount) return;
        _tab = index;
    }

    private void UpdateTabVisuals()
    {
        foreach (var (btn, label, index) in _tabs)
        {
            bool selected = index == _tab;
            Sprite sprite = selected
                ? (_cfg.skin.tabSelectedSprite != null ? _cfg.skin.tabSelectedSprite : _cfg.skin.tabSprite)
                : _cfg.skin.tabSprite;
            CodeUI.ApplySkin(btn.image, selected ? CodeUI.AccentFill : CodeUI.TabIdleBg, sprite, _cfg.skin);
            label.color = selected ? Color.white
                : new Color(CodeUI.QuestColor.r, CodeUI.QuestColor.g, CodeUI.QuestColor.b, 0.75f);
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

    public void Refresh()
    {
        UpdateTabVisuals();

        var qm = QuestManager.Instance;
        var quest = CurrentTabQuest();
        CurrentQuest = quest;

        if (quest == null || qm == null)
        {
            ShowEmpty();
            onChanged?.Invoke();
            return;
        }

        _questTitle.text = quest.QuestName;
        _descText.text = quest.QuestDescription;

        QuestStatus status = qm.GetQuestStatus(quest.questID);
        bool accepted = status == QuestStatus.Accepted;
        bool complete = accepted && qm.CheckRequirements(quest);

        UpdateStatus(status, accepted, complete);

        _reqSection.gameObject.SetActive(accepted);
        _rewardSection.gameObject.SetActive(accepted);
        if (accepted)
        {
            BuildRequirements(quest);
            BuildRewards(quest);
        }

        onChanged?.Invoke();
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
            // 오버레이는 확인용 — 제출은 트럭에서 한다.
            _statusText.text = CodeUI.L("quest_status_submit_at_truck", "트럭에서 제출 가능");
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
    }

    private void BuildRequirements(QuestSO quest)
    {
        ClearChildren(_reqList);

        var wh = WarehouseManager.Instance;
        if (quest.requirements == null || quest.requirements.Count == 0)
        {
            var none = CodeUI.CreateText(_reqList, "None", 15f, FontStyles.Italic, CodeUI.MutedColor,
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
                int cur = WarehouseManager.Instance != null ? WarehouseManager.Instance.GetMineralCount(req.requiredMineral) : 0;
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
            var none = CodeUI.CreateText(_rewardList, "None", 15f, FontStyles.Italic, CodeUI.MutedColor,
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
        var rowBg = CodeUI.CreateImage(parent, "Row", CodeUI.SlotBg, _cfg.skin.slotSprite, _cfg.skin);
        rowBg.gameObject.AddComponent<LayoutElement>().preferredHeight = _cfg.rowHeight;
        var layout = rowBg.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 12, 6, 6);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var iconBox = CodeUI.CreateImage(rowBg.transform, "IconBox", CodeUI.BoxBg, _cfg.skin.boxSprite, _cfg.skin);
        var iconLe = iconBox.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = _cfg.rowHeight - 12f;
        iconLe.preferredHeight = _cfg.rowHeight - 12f;

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
        // 언어 변경 시엔 Refresh가 행을 통째로 다시 만든다. (binder에 쌓이면 파괴된 참조가 남는다)
        var label = CodeUI.CreateText(rowBg.transform, "Label", 17f, FontStyles.Normal, textColor,
            TextAlignmentOptions.MidlineLeft);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        label.text = text;
    }

    private static void ClearChildren(RectTransform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.Destroy(parent.GetChild(i).gameObject);
    }
}
