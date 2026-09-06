using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 업그레이드 트리의 각 노드를 정의하는 SO입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewUpgradeNode", menuName = "Upgrade/Node")]
public class UpgradeNodeSO : ScriptableObject
{
    [Header("Basic Info")]
    [Tooltip("고유 식별자 (필수, 예: 'MiningSpeed_Lv1')")]
    public string nodeId;

    [Tooltip("UI 표시 이름 Localization key")]
    public string displayNameKey;

    [Tooltip("설명 Localization key")]
    public string descriptionKey;

    [Tooltip("UI 표시 아이콘")]
    public Sprite icon;

    [Header("Tier (계층)")]
    [Tooltip("업그레이드 계층 (0 = 최상위, 숫자가 클수록 하위 계층)")]
    public int tier = 0;

    [Header("Requirements")]
    [Tooltip("선행 노드 목록 (AND 조건: 모두 해금되어야 이 노드를 해금 가능)")]
    public List<UpgradeNodeSO> parentNodes;

    [Header("⚠ 런타임에 priceData.json이 덮어씀")]
    [Tooltip("해금 비용 (골드). 다단계 노드면 Lv1 비용이다")]
    public int cost;

    [Header("Level (다단계 강화)")]
    [Tooltip("최대 레벨. 1이면 종래대로 한 번만 사는 노드다")]
    [Min(1)] public int maxLevel = 1;

    [Tooltip("레벨당 비용 증가율. Lv N 비용 = cost x growth^(N-1) (10G 단위 반올림)")]
    [Min(1f)] public float costGrowth = 1.6f;

    [Tooltip("Lv2부터의 비용을 직접 적을 때만 채운다(비워두면 costGrowth 공식 사용). " +
             "priceData.json은 Lv1(cost)만 덮어쓴다")]
    public List<int> levelCosts;

    [Header("Effect")]
    [Tooltip("이 노드가 제공하는 효과")]
    public UpgradeEffectSO effect;

    [Header("UI Layout (Optional)")]
    [Tooltip("수동 배치가 필요할 경우 사용할 UI 좌표")]
    public Vector2 uiPosition;

    [Tooltip("사람이 지정한 연결선 모양(부모별). 비어 있으면 UpgradeLaneRouter가 규칙대로 정한다. " +
             "Tools/upgrade_tree_editor.html에서 선을 더블클릭하면 꺾임점이 늘어나고, CSV lineBends 칸에서 온다")]
    public List<UpgradeLineBend> lineBends = new List<UpgradeLineBend>();

    /// <summary>
    /// 그 부모에서 들어오는 선의 지정 꺾임점 목록(ui 좌표). 지정이 없으면 null.
    ///
    /// 매핑 좌표가 아니라 ui 좌표로 저장돼 있다 — 매핑은 트리 전체 중앙 기준이라
    /// 노드를 하나만 옮겨도 모든 지정값이 통째로 어긋난다.
    /// </summary>
    public List<Vector2> GetLineBends(string parentId)
    {
        if (lineBends == null || string.IsNullOrEmpty(parentId)) return null;

        for (int i = 0; i < lineBends.Count; i++)
        {
            var b = lineBends[i];
            if (b != null && b.parentId == parentId && b.points != null && b.points.Count > 0)
                return b.points;
        }
        return null;
    }

    /// <summary>
    /// 현재 언어로 표시 이름 반환
    /// </summary>
    public string DisplayName => LanguageManager.Instance?.L(displayNameKey) ?? displayNameKey;

    /// <summary>
    /// 현재 언어로 설명 반환
    /// </summary>
    public string Description => LanguageManager.Instance?.L(descriptionKey) ?? descriptionKey;

    /// <summary>
    /// 실제 최대 레벨. 계층 해금 게이트(MiningLevel)는 다단계가 될 수 없다 —
    /// 레벨마다 어느 계층을 열지가 정의되지 않기 때문이다(UpgradeManager.UnlockNode 참고).
    /// </summary>
    public int EffectiveMaxLevel
    {
        get
        {
            if (effect != null && effect.type == UpgradeEffectType.MiningLevel) return 1;
            return Mathf.Max(1, maxLevel);
        }
    }

    /// <summary>다단계 노드인가.</summary>
    public bool IsMultiLevel => EffectiveMaxLevel > 1;

    /// <summary>
    /// level(1-based)을 사는 데 드는 골드.
    /// levelCosts가 그 레벨까지 채워져 있으면 그 값을, 아니면 costGrowth 공식을 쓴다.
    /// </summary>
    public int CostForLevel(int level)
    {
        if (level <= 1) return cost;

        // levelCosts[0]은 Lv2 비용이다 — Lv1은 cost(priceData.json 소관)라 중복해서 적지 않는다
        if (levelCosts != null && levelCosts.Count >= level - 1)
        {
            int explicitCost = levelCosts[level - 2];
            if (explicitCost > 0) return explicitCost;
        }

        float growth = Mathf.Max(1f, costGrowth);
        float raw = cost * Mathf.Pow(growth, level - 1);
        // 트리 가격은 전부 10G 단위다 — 공식값도 거기에 맞춘다
        int rounded = Mathf.RoundToInt(raw / 10f) * 10;
        return Mathf.Max(cost, rounded);
    }
}
