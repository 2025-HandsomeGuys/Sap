using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 서브퀘스트 게시판 UI - 서브퀘스트 수락 전용
/// </summary>
public class SubQuestBoardUI : MonoBehaviour
{
    [Header("UI 요소")]
    public GameObject subQuestBoardPanel;
    public Transform questListParent;
    public GameObject questItemPrefab;
    public TextMeshProUGUI titleUI;
    public TextMeshProUGUI slotInfoUI;  // "서브퀘스트 슬롯: 1/2"
    public TextMeshProUGUI emptyUI;     // "수락 가능한 서브퀘스트가 없습니다"

    [Header("Localization Keys")]
    public string titleTextKey = "ui_subquest_title";
    public string slotInfoFormatKey = "ui_subquest_slot_info";
    public string emptyMessageKey = "ui_subquest_empty";
    public string notFoundManagerMessageKey = "ui_subquest_no_manager";
    public string miningLevelFormatKey = "ui_subquest_mining_level";
    public string acceptTextKey = "ui_subquest_accept";
    public string noSlotTextKey = "ui_subquest_no_slot";
    
    private List<QuestSO> availableSubQuests = new List<QuestSO>();
    
    
    
    void OnEnable()
    {
        RefreshQuestList();
    }
    
    /// <summary>
    /// 게시판 열기
    /// </summary>
    public void OpenSubQuestBoard(Transform source = null)
    {
        if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.SubQuestBoard, source);
        }
        else if (subQuestBoardPanel != null)
        {
            subQuestBoardPanel.SetActive(true);
        }
        
        RefreshQuestList();
    }
    
    /// <summary>
    /// 게시판 닫기
    /// </summary>
    public void CloseSubQuestBoard()
    {
        if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.None);
        }
        else if (subQuestBoardPanel != null)
        {
            subQuestBoardPanel.SetActive(false);
        }
    }
    
    /// <summary>
    /// 수락 가능한 서브퀘스트 목록 갱신
    /// </summary>
    public void RefreshQuestList()
    {
        availableSubQuests.Clear();
        ClearContainer(questListParent);
        
        // 제목 설정
        if (titleUI != null)
        {
            titleUI.text = LanguageManager.Instance?.L(titleTextKey) ?? titleTextKey;
        }

        if (QuestManager.Instance == null)
        {
            string msg = LanguageManager.Instance?.L(notFoundManagerMessageKey) ?? "퀘스트 매니저를 찾을 수 없습니다.";
            ShowEmptyMessage(msg);
            return;
        }

        // [추가] 게시판 열 때 최신 상태로 갱신 (레벨업 직후 등 반영)
        QuestManager.Instance.RefreshSubQuestUnlocks();
        
        // 슬롯 정보 업데이트
        UpdateSlotInfo();
        
        Debug.Log($"[SubQuestBoardUI] 서브퀘스트 목록 갱신 시작 (총 {QuestManager.Instance.subQuests.Count}개)");

        // 수락 가능한 서브퀘스트 찾기
        foreach (var quest in QuestManager.Instance.subQuests)
        {
            if (quest == null) continue;
            
            QuestStatus status = QuestManager.Instance.GetQuestStatus(quest.questID);
            
            // 디버그용 로그
            if (status != QuestStatus.Available)
            {
                Debug.Log($"[SubQuestBoardUI] 퀘스트 제외: {quest.QuestName} (ID: {quest.questID}) / 사유: 상태가 {status}임 (Available이어야 함)");
                continue;
            }
            
            // 타입 체크 (혹시 Main으로 되어있는지)
            if (quest.questType != QuestType.Sub)
            {
                Debug.Log($"[SubQuestBoardUI] 퀘스트 제외: {quest.QuestName} / 사유: 타입이 {quest.questType}임 (Sub여야 함)");
                continue;
            }

            availableSubQuests.Add(quest);
        }
        
        Debug.Log($"[SubQuestBoardUI] 최종 표시할 퀘스트 수: {availableSubQuests.Count}");
        
        // UI 업데이트
        if (availableSubQuests.Count == 0)
        {
            string msg = LanguageManager.Instance?.L(emptyMessageKey) ?? "수락 가능한 서브퀘스트가 없습니다.";
            ShowEmptyMessage(msg);
            return;
        }
        
        if (emptyUI != null) emptyUI.gameObject.SetActive(false);
        
        foreach (var quest in availableSubQuests)
        {
            CreateQuestItem(quest);
        }
    }
    
    /// <summary>
    /// 슬롯 정보 업데이트
    /// </summary>
    private void UpdateSlotInfo()
    {
        if (slotInfoUI == null) return;
        
        int current = QuestManager.Instance.GetActiveSubQuestCount();
        int max = QuestManager.MAX_SUB_QUEST_SLOTS;
        
        if (LanguageManager.Instance != null)
        {
            slotInfoUI.text = LanguageManager.Instance.LF(slotInfoFormatKey, current, max);
        }
        else
        {
            slotInfoUI.text = $"서브퀘스트 슬롯: {current}/{max}";
        }
        
        slotInfoUI.color = current >= max ? Color.red : Color.white;
    }
    
    /// <summary>
    /// 퀘스트 아이템 UI 생성
    /// </summary>
    private void CreateQuestItem(QuestSO quest)
    {
        if (questItemPrefab == null || questListParent == null) return;
        
        GameObject item = Instantiate(questItemPrefab, questListParent);
        
        // 퀘스트 이름 표시
        var nameText = item.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text = quest.QuestName;
        }
        else
        {
            // 대체: 첫 번째 TMP 찾기
            var tmpText = item.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null) tmpText.text = quest.QuestName;
        }
        
        // 설명 표시 (있으면)
        var descText = item.transform.Find("DescriptionText")?.GetComponent<TextMeshProUGUI>();
        if (descText != null)
        {
            descText.text = quest.QuestDescription;
        }
        
        // 레벨 요구사항 표시 (있으면)
        var levelText = item.transform.Find("LevelText")?.GetComponent<TextMeshProUGUI>();
        if (levelText != null)
        {
            if (LanguageManager.Instance != null)
            {
                levelText.text = LanguageManager.Instance.LF(miningLevelFormatKey, quest.requiredMiningLevel);
            }
            else
            {
                levelText.text = $"채광 Lv.{quest.requiredMiningLevel}";
            }
        }
        
        // 수락 버튼 설정
        var acceptButton = item.GetComponentInChildren<Button>();
        if (acceptButton != null)
        {
            bool canAccept = QuestManager.Instance.CanAcceptSubQuest();
            acceptButton.interactable = canAccept;
            acceptButton.onClick.AddListener(() => OnAcceptQuest(quest));
            
            // 버튼 텍스트 변경
            var buttonText = acceptButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                if (canAccept)
                {
                    buttonText.text = LanguageManager.Instance?.L(acceptTextKey) ?? "수락";
                }
                else
                {
                    buttonText.text = LanguageManager.Instance?.L(noSlotTextKey) ?? "슬롯 부족";
                }
            }
        }
    }
    
    /// <summary>
    /// 퀘스트 수락 처리
    /// </summary>
    private void OnAcceptQuest(QuestSO quest)
    {
        if (quest == null) return;
        
        if (!QuestManager.Instance.CanAcceptSubQuest())
        {
            Debug.LogWarning("[SubQuestBoardUI] 서브퀘스트 슬롯이 가득 찼습니다.");
            return;
        }
        
        QuestManager.Instance.AcceptQuest(quest);
        Debug.Log($"[SubQuestBoardUI] 서브퀘스트 수락: {quest.QuestName}");
        
        // 목록 갱신
        RefreshQuestList();
    }
    
    /// <summary>
    /// 빈 상태 메시지 표시
    /// </summary>
    private void ShowEmptyMessage(string message)
    {
        if (emptyUI != null)
        {
            emptyUI.gameObject.SetActive(true);
            emptyUI.text = message;
        }
    }
    
    /// <summary>
    /// 컨테이너 비우기
    /// </summary>
    private void ClearContainer(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }
    }
    
}
