using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 퀘스트의 보상 타입
/// </summary>
[System.Serializable]
public enum QuestRewardType
{
    Money,
    Item,
    Equipment,
    Mineral
}

/// <summary>
/// 퀘스트 보상 정보
/// </summary>
[System.Serializable]
public class QuestReward
{
    public QuestRewardType rewardType;
    public int moneyAmount;
    public ItemSO itemReward;
    public EquipmentSO equipmentReward;
    public MineralSO mineralReward;
    public int rewardAmount = 1;
    
    public Sprite GetRewardIcon()
    {
        switch (rewardType)
        {
            case QuestRewardType.Item:
                return itemReward != null ? itemReward.icon : null;
            case QuestRewardType.Equipment:
                return equipmentReward != null ? equipmentReward.icon : null;
            case QuestRewardType.Mineral:
                return mineralReward != null ? mineralReward.icon : null;
            default:
                return null;
        }
    }
    
    public string GetRewardDescription()
    {
        var lm = LanguageManager.Instance;
        switch (rewardType)
        {
            case QuestRewardType.Money:
                string moneyFormat = lm != null ? lm.L("quest_reward_money") : "돈 {0}";
                return string.Format(moneyFormat, moneyAmount);
            case QuestRewardType.Item:
                if (itemReward != null) return $"{itemReward.DisplayName} x{rewardAmount}";
                return lm != null ? lm.L("quest_reward_item") : "아이템";
            case QuestRewardType.Equipment:
                if (equipmentReward != null) return $"{equipmentReward.DisplayName} x{rewardAmount}";
                return lm != null ? lm.L("quest_reward_equipment") : "장비";
            case QuestRewardType.Mineral:
                if (mineralReward != null) return $"{mineralReward.DisplayName} x{rewardAmount}";
                return lm != null ? lm.L("quest_reward_mineral") : "광물";
            default:
                return "";
        }
    }
}

/// <summary>
/// 퀘스트 요구사항의 종류
/// </summary>
public enum RequirementType
{
    Mineral,       // 특정 광물 수집
    SceneVisit,    // 특정 씬 방문
    CustomFlag     // 특정 커스텀 이벤트 플래그 달성
}

/// <summary>
/// 퀘스트 요구사항
/// </summary>
[System.Serializable]
public class QuestRequirement
{
    public RequirementType type;

    [Header("Mineral Requirement")]
    public MineralSO requiredMineral;
    public int requiredAmount;

    [Header("Scene/Flag Requirement")]
    [Tooltip("방문할 씬의 이름(예: DemoMain) 또는 달성할 플래그의 이름")]
    public string targetString;
}

/// <summary>
/// 퀘스트의 상태
/// </summary>
public enum QuestStatus
{
    Locked,
    Available,
    Accepted,
    Completed
}

/// <summary>
/// 퀘스트 타입
/// </summary>
public enum QuestType
{
    Main,
    Sub
}

/// <summary>
/// 퀘스트 데이터를 정의하는 ScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "New Quest", menuName = "Game Data/Quest SO")]
public class QuestSO : ScriptableObject
{
    [Header("퀘스트 기본 정보")]
    public int questID;
    public string questNameKey;           // Localization key
    public string questDescriptionKey;    // Localization key
    
    [Header("퀘스트 타입")]
    public QuestType questType = QuestType.Main;
    
    [Header("메인 퀘스트 - 순차 진행 설정")]
    [Tooltip("메인 퀘스트 전용: 이전에 완료해야 할 퀘스트 ID (-1이면 첫 퀘스트)")]
    public int requiredPreviousQuestID = -1;
    
    [Header("서브 퀘스트 - 채광레벨 설정")]
    [Tooltip("서브 퀘스트 전용: 이 퀘스트가 해금되는 최소 채광레벨")]
    public int requiredMiningLevel = 1;
    
    [Header("퀘스트 요구사항")]
    public List<QuestRequirement> requirements = new List<QuestRequirement>();
    
    [Header("퀘스트 보상")]
    public List<QuestReward> rewards = new List<QuestReward>();
    
    [Header("대화 데이터")]
    public DialogueData acceptDialogue;
    public DialogueData progressDialogue;
    public DialogueData completeDialogue;

    /// <summary>
    /// 현재 언어로 퀘스트 이름 반환
    /// </summary>
    public string QuestName => LanguageManager.Instance?.L(questNameKey) ?? questNameKey;

    /// <summary>
    /// 현재 언어로 퀘스트 설명 반환
    /// </summary>
    public string QuestDescription => LanguageManager.Instance?.L(questDescriptionKey) ?? questDescriptionKey;
}
