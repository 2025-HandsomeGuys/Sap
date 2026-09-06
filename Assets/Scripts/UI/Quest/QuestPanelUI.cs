using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 퀘스트의 정보 표시 및 로그 관리를 담당하는 통합 컴포넌트.
/// 탭 UI를 통해 메인퀘스트(탭1)와 서브퀘스트(탭2,3)를 전환하며 표시.
/// </summary>
public class QuestPanelUI : MonoBehaviour
{
    [Header("모드 설정")]
    public bool isLogPanel = true;

    [Header("탭 UI")]
    public Button[] tabButtons;           // 탭 버튼 (3개: 메인, 서브1, 서브2)
    public GameObject[] tabIndicators;    // 탭 선택 표시 (선택된 탭 하이라이트)
    
    [System.Serializable]
    public struct TabSprites
    {
        public Sprite selected;
        public Sprite unselected;
    }

    [Header("탭 스프라이트 설정")]
    public TabSprites leftTabSprites;
    public TabSprites centerTabSprites;
    public TabSprites rightTabSprites;

    [Header("탭 크기 설정")]
    public float selectedTabHeight = 70f;
    public float unselectedTabHeight = 60f;
    
    [Header("UI Elements")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public Transform requirementsParent;
    public Transform rewardsParent;
    public Button submitButton;

    public TextMeshProUGUI statusText;


    [Header("Prefabs")]
    public GameObject requirementPrefab;
    public GameObject rewardPrefab;



    private QuestSO displayedQuest;
    private int currentTabIndex = 0;

    void Start()
    {
        if (isLogPanel)
        {
            if (submitButton != null) submitButton.onClick.AddListener(OnSubmitClicked);

            
            // 탭 버튼 이벤트 등록
            SetupTabButtons();
        }
    }
    
    private void SetupTabButtons()
    {
        if (tabButtons == null) return;
        
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] != null)
            {
                int tabIndex = i; // 클로저용 로컬 변수
                tabButtons[i].onClick.AddListener(() => OnTabClicked(tabIndex));

                // 마우스 호버 및 클릭 시 어두워지는 효과 제거 (색상 변화 방지)
                var colors = tabButtons[i].colors;
                colors.highlightedColor = Color.white;
                colors.pressedColor = Color.white;
                colors.selectedColor = Color.white;
                tabButtons[i].colors = colors;
            }
        }
    }

    void OnEnable()
    {
        if (isLogPanel)
        {
            currentTabIndex = 0; // 패널 열 때마다 첫 번째 탭으로 초기화
            UpdateTabVisuals();
            RefreshLog();
        }
    }

    public void OpenPanel(bool allowSubmission = false, Transform source = null)
    {
        if (UIStateManager.Instance != null)
            UIStateManager.Instance.SetState(UIState.Quest, source);
        else
            gameObject.SetActive(true);
        
        currentTabIndex = 0;
        UpdateTabVisuals();
        RefreshLog(allowSubmission);
    }

    public void ClosePanel()
    {
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState == UIState.Quest)
            UIStateManager.Instance.SetState(UIState.None);
        else
            gameObject.SetActive(false);
    }
    
    // 단순 확인용 (Q키 입력 시 호출 가능하도록 UIStateManager 등이 사용)
    public void OpenPanel()
    {
        OpenPanel(false);
    }
    
    void Update()
    {
        if (isLogPanel && gameObject.activeInHierarchy && tabButtons != null && tabButtons.Length > 0)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                int newIndex = currentTabIndex - 1;
                if (newIndex < 0) newIndex = tabButtons.Length - 1;
                OnTabClicked(newIndex);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                int newIndex = currentTabIndex + 1;
                if (newIndex >= tabButtons.Length) newIndex = 0;
                OnTabClicked(newIndex);
            }
        }
    }

    #region 탭 UI
    
    /// <summary>
    /// 탭 버튼 클릭 시 호출
    /// </summary>
    public void OnTabClicked(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex > 2) return;
        
        currentTabIndex = tabIndex;
        UpdateTabVisuals();
        DisplayQuestForTab(tabIndex, _currentAllowSubmission);
    }
    
    private bool _currentAllowSubmission = false; // 현재 제출 허용 상태 저장

    /// <summary>
    /// 탭 시각적 상태 업데이트
    /// </summary>
    private void UpdateTabVisuals()
    {
        // 탭 버튼 스프라이트 업데이트
        if (tabButtons != null)
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] != null)
                {
                    Image btnImage = tabButtons[i].GetComponent<Image>();
                    if (btnImage == null) continue;

                    bool isSelected = (i == currentTabIndex);
                    
                    // 위치에 따른 스프라이트 세트 선택
                    TabSprites sprites;
                    if (i == 0) sprites = leftTabSprites;
                    else if (i == tabButtons.Length - 1) sprites = rightTabSprites;
                    else sprites = centerTabSprites;

                    // 상태에 따른 스프라이트 적용
                    btnImage.sprite = isSelected ? sprites.selected : sprites.unselected;

                    // 상태에 따른 높이 조절
                    float targetHeight = isSelected ? selectedTabHeight : unselectedTabHeight;
                    
                    // LayoutElement가 있다면 preferredHeight 우선 조정
                    LayoutElement layoutElem = tabButtons[i].GetComponent<LayoutElement>();
                    if (layoutElem != null)
                    {
                        layoutElem.preferredHeight = targetHeight;
                    }
                    else
                    {
                        RectTransform rt = tabButtons[i].GetComponent<RectTransform>();
                        Vector2 size = rt.sizeDelta;
                        size.y = targetHeight;
                        rt.sizeDelta = size;
                    }
                }
            }
        }
        
        // 탭 인디케이터 업데이트
        if (tabIndicators != null)
        {
            for (int i = 0; i < tabIndicators.Length; i++)
            {
                if (tabIndicators[i] != null)
                {
                    tabIndicators[i].SetActive(i == currentTabIndex);
                }
            }
        }

    }
    
    /// <summary>
    /// 탭 인덱스에 해당하는 퀘스트 표시
    /// </summary>
    private void DisplayQuestForTab(int tabIndex, bool allowSubmission)
    {
        if (QuestManager.Instance == null)
        {
            ClearDisplay();
            return;
        }
        
        QuestSO quest = null;
        switch (tabIndex)
        {
            case 0: // 메인 퀘스트
                quest = QuestManager.Instance.GetActiveMainQuest();
                break;
            case 1: // 서브 퀘스트 슬롯 1
                quest = QuestManager.Instance.GetSubQuestInSlot(0);
                break;
            case 2: // 서브 퀘스트 슬롯 2
                quest = QuestManager.Instance.GetSubQuestInSlot(1);
                break;
        }
        
        DisplayQuest(quest, allowSubmission);
    }
    
    #endregion

    public void RefreshLog(bool allowSubmission = false)
    {
        _currentAllowSubmission = allowSubmission;
        DisplayQuestForTab(currentTabIndex, allowSubmission);
    }

    public void DisplayQuest(QuestSO quest, bool allowSubmission = false)
    {
        displayedQuest = quest;
        
        if (quest == null)
        {
            ClearDisplay();
            return;
        }

        if (titleText)
        {
            titleText.text = quest.QuestName;
            ApplyFont(titleText);
        }
        if (descriptionText)
        {
            descriptionText.text = quest.QuestDescription;
            ApplyFont(descriptionText);
        }

        QuestStatus status = QuestManager.Instance.GetQuestStatus(quest.questID);
        bool isAccepted = status == QuestStatus.Accepted;

        if (isAccepted)
        {
            UpdateRequirements(quest);
            UpdateRewards(quest);
        }
        else
        {
            ClearContainer(requirementsParent);
            ClearContainer(rewardsParent);
        }
        
        if (isLogPanel) UpdateSubmitButton(quest, allowSubmission);
    }

    private void UpdateRequirements(QuestSO quest)
    {
        ClearContainer(requirementsParent);
        if (requirementPrefab == null || requirementsParent == null) return;

        foreach (var req in quest.requirements)
        {
            GameObject obj = Instantiate(requirementPrefab, requirementsParent);
            var textComp = obj.GetComponentInChildren<TextMeshProUGUI>();
            var iconComp = obj.transform.Find("Icon")?.GetComponent<Image>();

            if (req.type == RequirementType.Mineral)
            {
                if (req.requiredMineral == null) { Destroy(obj); continue; }
                int current = WarehouseManager.Instance != null 
                    ? WarehouseManager.Instance.GetMineralCount(req.requiredMineral) 
                    : 0;
                if (textComp != null)
                {
                    textComp.text = $"{req.requiredMineral.DisplayName} ({current}/{req.requiredAmount})";
                    textComp.color = current >= req.requiredAmount ? Color.white : Color.red;
                    ApplyFont(textComp);
                }
                if (iconComp != null && req.requiredMineral.icon != null) iconComp.sprite = req.requiredMineral.icon;
            }
            else if (req.type == RequirementType.SceneVisit)
            {
                bool met = QuestManager.Instance != null && QuestManager.Instance.HasQuestFlag("Scene_" + req.targetString);
                if (textComp != null)
                {
                    textComp.text = $"지역 방문: {req.targetString}";
                    textComp.color = met ? Color.white : Color.red;
                    ApplyFont(textComp);
                }
                if (iconComp != null) iconComp.gameObject.SetActive(false); // 광물 아이콘 숨김
            }
            else if (req.type == RequirementType.CustomFlag)
            {
                bool met = QuestManager.Instance != null && QuestManager.Instance.HasQuestFlag(req.targetString);
                if (textComp != null)
                {
                    textComp.text = $"{req.targetString}";
                    textComp.color = met ? Color.white : Color.red;
                    ApplyFont(textComp);
                }
                if (iconComp != null) iconComp.gameObject.SetActive(false); // 광물 아이콘 숨김
            }
        }
    }

    private void UpdateRewards(QuestSO quest)
    {
        ClearContainer(rewardsParent);
        if (rewardPrefab == null || rewardsParent == null) return;

        foreach (var reward in quest.rewards)
        {
            GameObject obj = Instantiate(rewardPrefab, rewardsParent);
            var textComp = obj.GetComponentInChildren<TextMeshProUGUI>();
            var iconComp = obj.transform.Find("Icon")?.GetComponent<Image>();

            if (textComp != null)
            {
                textComp.text = reward.GetRewardDescription();
                ApplyFont(textComp);
            }
            Sprite icon = reward.GetRewardIcon();
            if (iconComp != null && icon != null) iconComp.sprite = icon;
        }
    }

    private void UpdateSubmitButton(QuestSO quest, bool allowSubmission)
    {
        if (submitButton == null) return;
        
        // 퀘스트가 Accepted 상태일 때만 제출 버튼 표시
        QuestStatus status = QuestManager.Instance.GetQuestStatus(quest.questID);
        bool isAccepted = status == QuestStatus.Accepted;
        
        if (!isAccepted)
        {
            submitButton.interactable = false;
            if (statusText)
            {
                statusText.text = status == QuestStatus.Available 
                    ? GetLocalizedString("quest_status_need_accept") 
                    : GetLocalizedString("quest_status_completed");
                statusText.color = Color.gray;
                ApplyFont(statusText);
            }
            return;
        }
        
        bool isComplete = QuestManager.Instance.CheckRequirements(quest);
        
        // 요구사항을 모두 만족하고, 제출이 허용된 경우(상점 NPC를 통해 퀘스트 패널을 연 경우)에만 클릭 가능
        submitButton.interactable = isComplete && allowSubmission;
        
        if (statusText)
        {
            if (!allowSubmission)
            {
                statusText.text = isComplete 
                    ? GetLocalizedString("quest_status_submit_at_truck") 
                    : GetLocalizedString("quest_status_progress");
                statusText.color = isComplete ? Color.green : Color.yellow;
            }
            else
            {
                statusText.text = isComplete 
                    ? GetLocalizedString("quest_status_submittable") 
                    : GetLocalizedString("quest_status_progress");
                statusText.color = isComplete ? Color.green : Color.yellow;
            }
            ApplyFont(statusText);
        }
    }

    private void OnSubmitClicked()
    {
        if (displayedQuest == null) return;
        
        // 상점 NPC를 통하지 않았다면 제출 불가 처리
        if (!_currentAllowSubmission) return;

        if (QuestManager.Instance.CheckRequirements(displayedQuest))
        {
            // 현재 완료할 퀘스트 저장
            QuestSO completedQuest = displayedQuest;

            // 1. 퀘스트 완료 처리
            QuestManager.Instance.CompleteQuest(completedQuest);
            
            // 2. 퀘스트 패널 닫기 (상태 전이를 위해 대화창 열기 전에 수행)
            ClosePanel();
            
            // 3. 완료 대화 출력
            QuestDialogueUI dialogueUI = FindFirstObjectByType<QuestDialogueUI>(FindObjectsInactive.Include);
            if (dialogueUI != null)
            {
                dialogueUI.ShowQuestDialogue(completedQuest);
            }
        }
    }

    private void ClearDisplay()
    {
        if (titleText) 
        {
            titleText.text = GetLocalizedString("quest_empty_title");
            ApplyFont(titleText);
        }
        
        // 탭별 안내 메시지
        string emptyMessage = currentTabIndex == 0 
            ? GetLocalizedString("quest_empty_main") 
            : GetLocalizedString("quest_empty_sub");
        if (descriptionText)
        {
            descriptionText.text = emptyMessage;
            ApplyFont(descriptionText);
        }
        
        ClearContainer(requirementsParent);
        ClearContainer(rewardsParent);
        if (statusText) 
        {
            statusText.text = GetLocalizedString("quest_empty_status");
            ApplyFont(statusText);
        }
        
        if (submitButton)
        {
            submitButton.interactable = false;
        }
    }

    private void ClearContainer(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    #region 로컬라이징 헬퍼

    private string GetLocalizedString(string key)
    {
        if (LanguageManager.Instance != null)
            return LanguageManager.Instance.L(key);
        return key;
    }

    private void ApplyFont(TextMeshProUGUI textComp)
    {
        if (textComp == null || LanguageManager.Instance == null) return;
        TMP_FontAsset font = LanguageManager.Instance.GetCurrentFont();
        if (font != null) textComp.font = font;
    }

    #endregion
}
