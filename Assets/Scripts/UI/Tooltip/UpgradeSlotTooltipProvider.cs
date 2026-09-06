using UnityEngine;

/// <summary>
/// 업그레이드 슬롯의 툴팁 정보를 제공
/// </summary>
public class UpgradeSlotTooltipProvider : MonoBehaviour, ITooltipProvider
{
    private UpgradeNodeSO _node;

    [Header("Localization Keys")]
    public string textEffectLabelKey = "tt_effect_label";
    public string textCostFormatKey = "tt_cost_format";
    public string textUnlockedKey = "tt_unlocked";
    public string textCanUnlockKey = "tt_can_unlock";
    public string textLockedKey = "tt_locked";
    public string textPreReqLabelKey = "tt_prereq_label";
    public string textInsufficientGoldKey = "tt_insufficient_gold";

    public void Initialize(UpgradeNodeSO node)
    {
        _node = node;
    }

    public string GetTooltipTitle()
    {
        if (_node == null) return "";
        return _node.DisplayName;
    }

    public string GetTooltipContent()
    {
        if (_node == null) return "";

        var lm = LanguageManager.Instance;
        string content = "";

        // 설명
        if (!string.IsNullOrEmpty(_node.Description))
        {
            content = _node.Description;
        }

        // 효과 정보
        if (_node.effect != null)
        {
            if (!string.IsNullOrEmpty(content))
                content += "\n\n";

            string effectText = "";
            if (_node.effect.isPercentage)
            {
                float percentValue = (_node.effect.value - 1f) * 100f;
                effectText = $"{_node.effect.type}: {(percentValue >= 0 ? "+" : "")}{percentValue:0.#}%";
            }
            else
            {
                effectText = $"{_node.effect.type}: +{_node.effect.value}";
            }
            content += $"{lm?.L(textEffectLabelKey) ?? textEffectLabelKey}{effectText}";
        }

        // 레벨 (다단계 노드만)
        int level = UpgradeManager.Instance.GetNodeLevel(_node);
        bool isMaxed = UpgradeManager.Instance.IsMaxed(_node);
        if (_node.IsMultiLevel)
            content += $"\nLv {level}/{_node.EffectiveMaxLevel}";

        // 비용 정보 — 다음 레벨 가격. 최대 레벨이면 더 살 게 없으니 아예 안 적는다
        int nextCost = UpgradeManager.Instance.GetNextCost(_node);
        if (!isMaxed)
            content += "\n" + (lm != null ? lm.LF(textCostFormatKey, nextCost) : $"비용: {nextCost} 골드");

        // 해금 상태
        bool canUnlock = UpgradeManager.Instance.CanUnlock(_node);

        content += "\n\n";
        if (isMaxed)
        {
            content += lm?.L(textUnlockedKey) ?? textUnlockedKey;
        }
        else if (canUnlock)
        {
            content += lm?.L(textCanUnlockKey) ?? textCanUnlockKey;
        }
        else
        {
            content += lm?.L(textLockedKey) ?? textLockedKey;

            // 선행 조건 표시
            if (_node.parentNodes != null && _node.parentNodes.Count > 0)
            {
                System.Collections.Generic.List<string> lockedParentNames = new System.Collections.Generic.List<string>();
                foreach (var parent in _node.parentNodes)
                {
                    if (!UpgradeManager.Instance.IsNodeUnlocked(parent.nodeId))
                    {
                        lockedParentNames.Add(parent.DisplayName);
                    }
                }

                if (lockedParentNames.Count > 0)
                {
                    content += lm?.L(textPreReqLabelKey) ?? textPreReqLabelKey;
                    foreach (var name in lockedParentNames)
                    {
                        content += $"\n- {name}";
                    }
                }
            }

            // 골드 부족 확인
            PlayerStat playerStats = FindFirstObjectByType<PlayerStat>();
            if (playerStats != null && playerStats.Gold < nextCost)
            {
                content += "\n" + (lm != null ? lm.LF(textInsufficientGoldKey, playerStats.Gold) : $"골드 부족 (현재: {playerStats.Gold})");
            }
        }

        return content;
    }
}
