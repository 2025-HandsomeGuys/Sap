using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NpcPopup : MonoBehaviour
{
    [Header("UI 요소")]
    public GameObject popupPanel;
    public Button shopButton;
    public Button upgradeButton;
    public Button questButton;        // 퀘스트 버튼 (이전 버전 호환용)
    public Button talkButton;
    public Button closeButton;
    public TextMeshProUGUI npcNameText;
    
    [Header("메인 퀘스트 표시")]
    public GameObject mainQuestIndicator;  // 느낌표 아이콘 (대화 버튼에 표시)

    // 팝업의 대상. NpcInteraction과 WorldInteractable(트럭 NPC)이 같은 인터페이스로 들어온다.
    private INpcPopupSource currentNPC;
    private bool hasAvailableMainQuest = false;  // 현재 NPC에 수락 가능한 메인퀘스트가 있는지

    /// <summary>
    /// 대상이 살아 있는가. 인터페이스 타입은 <c>== null</c>이 Unity의 파괴 판정(가짜 null)을
    /// 타지 않으므로, 반드시 UnityEngine.Object인 Transform 쪽으로 확인한다.
    /// </summary>
    private bool HasNPC => currentNPC != null && currentNPC.SourceTransform != null;

    void Start()
    {
        if (shopButton != null)
            shopButton.onClick.AddListener(OnShopClicked);
        if (upgradeButton != null)
            upgradeButton.onClick.AddListener(OnUpgradeClicked);
        if (questButton != null)
            questButton.onClick.AddListener(OnQuestClicked);
        if (talkButton != null)
            talkButton.onClick.AddListener(OnTalkClicked);
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);

        if (popupPanel != null)
            popupPanel.SetActive(false);
    }

    public bool IsOpen()
    {
        // UIStateManager의 상태도 확인하여 정확한 상태 반환
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState == UIState.Popup)
        {
            return popupPanel != null && popupPanel.activeSelf;
        }
        return false;
    }

    public void ShowPopup(INpcPopupSource npc)
    {
        if (npc == null || npc.SourceTransform == null) return;

        currentNPC = npc;

        // NPC 이름 표시
        if (npcNameText != null)
            npcNameText.text = npc.NpcName;

        // 버튼 활성화 상태 설정
        if (shopButton != null)
            shopButton.gameObject.SetActive(npc.CanOpenShop);
        if (upgradeButton != null)
            upgradeButton.gameObject.SetActive(npc.CanOpenUpgrade);
        if (questButton != null)
            questButton.gameObject.SetActive(npc.MainQuest != null || npc.IsMainQuestNpc);
        if (talkButton != null)
            talkButton.gameObject.SetActive(npc.Dialogue != null || npc.MainQuest != null);

        // 메인 퀘스트 느낌표 표시 업데이트
        UpdateMainQuestIndicator(npc);

        // 팝업 표시
        if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.Popup, npc.SourceTransform);
        }
        if (popupPanel != null)
            popupPanel.SetActive(true);
    }
    
    /// <summary>
    /// 메인 퀘스트 진행 가능 여부에 따라 느낌표 아이콘 표시
    /// </summary>
    private void UpdateMainQuestIndicator(INpcPopupSource npc)
    {
        hasAvailableMainQuest = false;

        if (QuestManager.Instance != null && npc != null)
        {
            QuestSO questToCheck = npc.MainQuest;
            
            // 메인 퀘스트 전담 NPC라면 현재 활성화된 메인 퀘스트 우선 확인
            if (npc.IsMainQuestNpc)
            {
                QuestSO activeQuest = QuestManager.Instance.GetActiveMainQuest();
                if (activeQuest != null) questToCheck = activeQuest;
            }

            if (questToCheck != null)
            {
                QuestStatus status = QuestManager.Instance.GetQuestStatus(questToCheck.questID);
                // 수락 가능 상태이거나, 진행 중인데 조건을 만족했을 때 느낌표 표시
                if (status == QuestStatus.Available || (status == QuestStatus.Accepted && QuestManager.Instance.CheckRequirements(questToCheck)))
                {
                    hasAvailableMainQuest = true;
                }
            }
        }
        
        if (mainQuestIndicator != null)
        {
            mainQuestIndicator.SetActive(hasAvailableMainQuest);
        }
    }



    private void OnShopClicked()
    {
        // 대화창 즉시 닫고 상점 열기
        if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.Shop, HasNPC ? currentNPC.SourceTransform : null);
        }
    }

    private void OnUpgradeClicked()
    {
        // 대화창 즉시 닫고 업그레이드 열기
        if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.Upgrade, HasNPC ? currentNPC.SourceTransform : null);
        }
    }

    private void OnQuestClicked()
    {
        if (!HasNPC) return;

        Debug.Log("[NpcPopup] 퀘스트 버튼 클릭");

        // 팝업 닫기 전 NPC 참조 저장
        Transform sourceNPC = currentNPC.SourceTransform;
        
        // 팝업 닫기 (이 안에서 currentNPC가 null로 변함)
        ClosePopup();

        // 코드 생성 퀘스트 오버레이를 쓰는 씬이면 '제출 가능' 모드로 그쪽을 연다
        if (UIStateManager.Instance != null && UIStateManager.Instance.useCodeBuiltQuestUI)
        {
            QuestOverlayUI.RequestSubmission();
            UIStateManager.Instance.SetState(UIState.Quest, sourceNPC);
            return;
        }

        // 퀘스트 패널을 '제출 가능' 모드로 열기
        QuestPanelUI questPanel = FindFirstObjectByType<QuestPanelUI>();
        if (questPanel != null)
        {
            questPanel.OpenPanel(true, sourceNPC);
        }
        else if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.Quest, sourceNPC);
            
            // 패널이 열린 후 allowSubmission 설정을 위해 다시 찾음
            var panel = FindFirstObjectByType<QuestPanelUI>();
            if (panel != null) panel.RefreshLog(true);
        }
    }
    
    /// <summary>
    /// 대화 버튼 클릭 - 느낌표가 있으면 수락 대화, 진행 중이면 진행 대화, 없으면 일반 대화
    /// </summary>
    private void OnTalkClicked()
    {
        if (!HasNPC) return;

        QuestDialogueUI dialogueUI = currentNPC.DialogueUI;
        if (dialogueUI == null)
        {
            dialogueUI = FindFirstObjectByType<QuestDialogueUI>(FindObjectsInactive.Include);
        }
        
        if (dialogueUI == null)
        {
            Debug.LogError("[NpcPopup] QuestDialogueUI를 찾을 수 없습니다!");
            return;
        }

        QuestSO questToTalk = currentNPC.MainQuest;
        
        if (QuestManager.Instance != null && currentNPC.IsMainQuestNpc)
        {
            QuestSO activeQuest = QuestManager.Instance.GetActiveMainQuest();
            if (activeQuest != null)
            {
                questToTalk = activeQuest;
            }
        }

        // 퀘스트가 있고 상태가 표시 가능(Available/Accepted)한 경우 퀘스트 대화 시작
        if (questToTalk != null)
        {
            QuestStatus status = QuestManager.Instance.GetQuestStatus(questToTalk.questID);
            if (status == QuestStatus.Available || status == QuestStatus.Accepted)
            {
                dialogueUI.ShowQuestDialogue(questToTalk);
                return;
            }
        }
        
        // 3. 퀘스트 대화 대상이 아니면 일반 대화로 연결
        if (currentNPC.Dialogue != null)
        {
            dialogueUI.ShowDialogue(currentNPC.Dialogue);
        }
        else
        {
            Debug.LogWarning("[NpcPopup] 출력할 대화 데이터가 없습니다.");
        }
    }

    private void OnCloseClicked()
    {
        ClosePopup();
    }
    
    public void ClosePopup()
    {
        currentNPC = null; // 참조 해제
        
        if (popupPanel != null)
        {
            popupPanel.SetActive(false);
        }
        if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.CloseAll();
        }
    }

    void Update()
    {
        // UIStateManager의 상태가 Popup이 아니면 즉시 리턴 및 패널 비활성화
        if (UIStateManager.Instance == null || UIStateManager.Instance.CurrentState != UIState.Popup)
        {
            if (currentNPC != null) currentNPC = null;
            if (popupPanel != null && popupPanel.activeSelf) popupPanel.SetActive(false);
            return;
        }

        // Popup 상태인데 패널이 꺼져있다면 (ShowPopup 직후 등) 다시 켜기
        if (popupPanel != null && !popupPanel.activeSelf && HasNPC)
        {
            popupPanel.SetActive(true);
        }

        // 팝업 위치 업데이트
        UpdatePopupPosition();


    }

    /// <summary>
    /// NPC의 월드 좌표를 스크린 좌표로 변환하여 팝업 위치 갱신
    /// </summary>
    private void UpdatePopupPosition()
    {
        if (!HasNPC || popupPanel == null) return;

        // 카메라 확인
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        // 월드 좌표 + 오프셋을 스크린 좌표로 변환
        Vector3 worldPos = currentNPC.SourceTransform.position + currentNPC.PopupOffset;
        Vector3 screenPos = mainCam.WorldToScreenPoint(worldPos);

        // NPC가 카메라 뒤에 있는 경우 팝업 숨김 처리 (Z축 체크)
        // SetActive 대신 CanvasGroup alpha나 단순히 위치를 화면 밖으로 보낼 수도 있으나, 
        // 여기선 가시성만 처리하고 Update()에서 다시 켤 수 있게 함
        if (screenPos.z < 0)
        {
            popupPanel.SetActive(false);
            return;
        }

        // 위치 적용
        popupPanel.transform.position = screenPos;
    }
}

