using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 퀘스트 수락 및 완료를 위한 대화 UI.
/// 좌/우 대화창을 분리하여 관리하며, 화자에 따라 반투명 처리 및 텍스트 표시를 제어합니다.
/// 각 패널마다 별도의 버튼(Next, Close, Accept, Complete)을 가집니다.
/// </summary>
public class QuestDialogueUI : MonoBehaviour
{
    [System.Serializable]
    public class DialoguePanel
    {
        public CanvasGroup panelGroup;    // 패널 전체 투명도 제어용
        public Image portrait;            // 캐릭터 초상화
        public Image nameImage;           // 캐릭터 이름 이미지 (신규)
        public TextMeshProUGUI nameText;  // 캐릭터 이름
        public TextMeshProUGUI lineText;  // 대화 내용
        public Image panelBackground;     // 대화창 배경

        [Header("Indicators")]
        public GameObject triangleIndicator; // 다음 대화 대기 표시용 삼각형 상징
        [HideInInspector] public Vector3 initialIndicatorPos; // 초기 위치 저장용
    }

    [Header("대화 패널 설정")]
    public DialoguePanel leftPanel;
    public DialoguePanel rightPanel;

    [Header("하이라이트 설정")]
    public Color dimColor = new Color(0.5f, 0.5f, 0.5f, 1.0f); // 비활성 패널 색상 (어둡게)
    public Color activeColor = Color.white;                // 활성 패널 색상
    public Vector3 activeScale = Vector3.one;
    public Vector3 dimScale = Vector3.one * 0.95f;

    [Header("타이핑 설정")]
    public float typingSpeed = 0.05f;     // 글자 출력 속도

    [Header("인디케이터 애니메이션 설정")]
    public float indicatorMoveAmount = 10f; // 움직임 범위 (픽셀/유닛)
    public float indicatorMoveSpeed = 8f;   // 움직임 속도

    private DialogueData currentDialogue;
    // 좌/우 패널이 각자 '자기 쪽 화자'의 이름을 유지한다.
    // DialogueLine은 말하는 쪽 이름 하나만 들고 있어서, 그대로 양쪽에 쓰면
    // 비활성 패널까지 현재 화자 이름으로 덮여 A/B가 같은 이름으로 보인다.
    private string leftSpeakerName = "";
    private string rightSpeakerName = "";
    private int currentLineIndex = 0;
    private QuestSO currentQuest;

    private bool isTyping = false;
    private Coroutine typingCoroutine;
    private string targetFullText;
    private DialoguePanel activePanel;

    // 이 오브젝트(DialogueRoot)는 평소 비활성이고, UIStateManager가 UIState.Dialogue로
    // 바꿀 때 켜진다. Start로 두면 그 활성화가 일어난 '다음 프레임'에 ResetUI가 돌아
    // 이미 표시한 첫 줄을 지워버린다 — Awake는 SetActive(true) 시점에 동기로 불리므로
    // ShowDialogue가 DisplayLine에 도달하기 전에 초기화가 끝난다. 순서를 되돌리지 말 것.
    void Awake()
    {
        // 초기 위치 저장
        if (leftPanel.triangleIndicator != null) 
            leftPanel.initialIndicatorPos = leftPanel.triangleIndicator.transform.localPosition;
        if (rightPanel.triangleIndicator != null) 
            rightPanel.initialIndicatorPos = rightPanel.triangleIndicator.transform.localPosition;

        // 왼쪽 패널 버튼 이벤트 연결
        SetupPanelButtons(leftPanel);
        // 오른쪽 패널 버튼 이벤트 연결
        SetupPanelButtons(rightPanel);
            
        ResetUI();
    }

    private void SetupPanelButtons(DialoguePanel panel)
    {
        // 기존 버튼 리스너 제거 또는 직접적인 버튼 사용 지양에 따라 비워둠
    }

    private void ResetUI()
    {
        // 양쪽 패널 모두 초기화
        ResetPanelUI(leftPanel);
        ResetPanelUI(rightPanel);
    }

    private void ResetPanelUI(DialoguePanel panel)
    {
        if (panel == null) return;
        
        if (panel.triangleIndicator != null) panel.triangleIndicator.SetActive(false);

        InitializePanelContent(panel);
    }

    private void InitializePanelContent(DialoguePanel panel)
    {
        if (panel == null) return;
        if (panel.nameText != null) panel.nameText.text = "";
        if (panel.nameImage != null) panel.nameImage.gameObject.SetActive(false);
        if (panel.lineText != null) panel.lineText.text = "";
        if (panel.portrait != null) panel.portrait.color = Color.clear; 
        if (panel.panelGroup != null) panel.panelGroup.alpha = 0f;      
    }

    private System.Action onDialogueCloseCallback;

    /// <param name="source">이 대화를 연 NPC(<see cref="INpcPopupSource"/>). 넘기면 대화를 닫을 때
    /// 그 NPC의 팝업으로 되돌아간다(NPC → 팝업 → 대화 → 닫기 → 팝업). null이면 그대로 None으로 닫힌다.</param>
    /// <param name="onComplete">대화가 끝났을 때 실행할 콜백 함수</param>
    public void ShowDialogue(DialogueData dialogue, Transform source = null, System.Action onComplete = null)
    {
        if (dialogue == null || dialogue.dialogueLines.Count == 0) return;

        ResetUI();
        currentDialogue = dialogue;
        currentQuest = null;
        currentLineIndex = 0;
        CacheSpeakerNames(currentDialogue);

        onDialogueCloseCallback = onComplete;

        UIStateManager.Instance.SetState(UIState.Dialogue, source);
        DisplayLine(currentDialogue.dialogueLines[0]);
    }

    /// <param name="source">이 대화를 연 NPC. <see cref="ShowDialogue"/>와 같은 용도(닫을 때 팝업 복귀).</param>
    public void ShowQuestDialogue(QuestSO quest, Transform source = null, System.Action onComplete = null)
    {
        if (quest == null) return;

        ResetUI();
        currentQuest = quest;
        onDialogueCloseCallback = onComplete;

        QuestStatus status = QuestManager.Instance.GetQuestStatus(quest.questID);
        DialogueData dialogueToShow = null;
        
        switch (status)
        {
            case QuestStatus.Available: 
                dialogueToShow = quest.acceptDialogue; 
                break;
            case QuestStatus.Accepted: 
                if (QuestManager.Instance.CheckRequirements(quest))
                {
                    dialogueToShow = quest.completeDialogue;
                    // 조건 만족 시 말을 걸자마자 즉시 완료 처리
                    QuestManager.Instance.CompleteQuest(quest);
                }
                else
                {
                    dialogueToShow = quest.progressDialogue;
                }
                break;
            case QuestStatus.Completed: 
                dialogueToShow = quest.completeDialogue; 
                break;
        }

        UIStateManager.Instance.SetState(UIState.Dialogue, source);

        if (dialogueToShow != null && dialogueToShow.dialogueLines.Count > 0)
        {
            currentDialogue = dialogueToShow;
            currentLineIndex = 0;
            CacheSpeakerNames(currentDialogue);
            DisplayLine(currentDialogue.dialogueLines[0]);
        }
        else
        {
            // 대화 데이터가 없는 경우 즉시 퀘스트 상태 처리 후 닫음
            ProcessQuestEndState(quest, status);
            CloseDialogue();
        }
    }

    /// <summary>
    /// 대화를 시작할 때 각 패널의 이름을 미리 정한다. 상대 패널은 아직 말하지 않았어도
    /// 이름이 보여야 하므로, 해당 쪽에서 처음 말하는 줄의 화자 이름을 당겨온다.
    /// </summary>
    private void CacheSpeakerNames(DialogueData dialogue)
    {
        leftSpeakerName = "";
        rightSpeakerName = "";
        if (dialogue == null) return;

        foreach (var l in dialogue.dialogueLines)
        {
            if (l == null) continue;
            if (l.speakerSide == DialogueSide.Left)
            {
                if (string.IsNullOrEmpty(leftSpeakerName)) leftSpeakerName = l.SpeakerName;
            }
            else
            {
                if (string.IsNullOrEmpty(rightSpeakerName)) rightSpeakerName = l.SpeakerName;
            }
        }
    }

    private void DisplayLine(DialogueLine line)
    {
        if (line == null) return;

        bool isLeft = line.speakerSide == DialogueSide.Left;

        // 말하는 쪽 이름만 갱신 — 반대쪽은 자기 이름을 그대로 유지한다.
        if (isLeft) leftSpeakerName = line.SpeakerName;
        else        rightSpeakerName = line.SpeakerName;
        
        // 1. 활성 패널 설정
        activePanel = isLeft ? leftPanel : rightPanel;
        UpdatePanelContent(activePanel, line, true);

        // 2. 비활성 패널 설정
        DialoguePanel inactivePanel = isLeft ? rightPanel : leftPanel;
        UpdatePanelContent(inactivePanel, line, false);

        // 3. 타이핑 효과 시작
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(activePanel, line.Text));
    }

    private System.Collections.IEnumerator TypeText(DialoguePanel panel, string text)
    {
        isTyping = true;
        targetFullText = text;
        
        if (panel.triangleIndicator != null) panel.triangleIndicator.SetActive(false);

        // 1. AutoSize로 최종 크기 계산 및 고정 (가독성 개선)
        panel.lineText.enableAutoSizing = true;
        panel.lineText.text = text;
        panel.lineText.ForceMeshUpdate();

        float finalSize = panel.lineText.fontSize;
        panel.lineText.enableAutoSizing = false;
        panel.lineText.fontSize = finalSize;

        // 2. 타이핑 시작 (maxVisibleCharacters 활용하여 태그 노출 방지)
        int totalVisible = panel.lineText.textInfo.characterCount;
        panel.lineText.maxVisibleCharacters = 0;

        for (int i = 0; i <= totalVisible; i++)
        {
            panel.lineText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(typingSpeed);
        }

        CompleteTyping();
    }

    private void CompleteTyping()
    {
        if (activePanel != null)
        {
            // 전체 텍스트 노출
            activePanel.lineText.maxVisibleCharacters = 9999;
            if (activePanel.triangleIndicator != null) activePanel.triangleIndicator.SetActive(true);
        }
        isTyping = false;
        typingCoroutine = null;
    }

    private void UpdatePanelContent(DialoguePanel panel, DialogueLine line, bool isActive)
    {
        if (panel == null) return;

        Color targetColor = isActive ? activeColor : dimColor;
        
        // 이름 및 이름 배경 처리 (상시 노출하되 색상으로 구분)
        string panelName = (panel == leftPanel) ? leftSpeakerName : rightSpeakerName;
        bool hasName = !string.IsNullOrEmpty(panelName);
        if (panel.nameText != null) 
        {
            panel.nameText.text = panelName;
            SetTextColor(panel.nameText, targetColor);
            panel.nameText.gameObject.SetActive(hasName);
        }
        if (panel.nameImage != null)
        {
            panel.nameImage.gameObject.SetActive(hasName);
            SetImageColor(panel.nameImage, targetColor);
        }

        if (isActive)
        {
            if (panel.lineText != null) 
            {
                panel.lineText.text = line.Text;
                panel.lineText.gameObject.SetActive(true);
            }
            if (panel.panelBackground != null) panel.panelBackground.enabled = true;
        }
        else
        {
            if (panel.lineText != null) 
            {
                // 이전 대화 내용을 유지하고 싶다면 여기서 clear하지 않음
                // 하지만 현재 구조상 한쪽만 텍스트를 보여주므로 일단 유지 혹은 비움 선택
                panel.lineText.text = ""; 
                panel.lineText.gameObject.SetActive(false);
            }
            if (panel.panelBackground != null) panel.panelBackground.enabled = false;
            
            // 비활성 패널의 지시계 숨김
            if (panel.triangleIndicator != null) panel.triangleIndicator.SetActive(false);
        }

        Sprite targetSprite = (panel == leftPanel) ? line.leftSprite : line.rightSprite;
        
        if (panel.portrait != null)
        {
            if (targetSprite != null)
            {
                panel.portrait.sprite = targetSprite;
                SetImageColor(panel.portrait, targetColor);
            }
            else
            {
                 if (currentLineIndex == 0) SetImageColor(panel.portrait, Color.clear);
            }
            
            Vector3 targetScale = isActive ? activeScale : dimScale;
            panel.portrait.transform.localScale = targetScale;
        }

        if (panel.panelGroup != null) panel.panelGroup.alpha = 1f; 
    }

    private void HidePanelButtons(DialoguePanel panel)
    {
        // 버튼을 사용하지 않으므로 비워둠
    }

    private void SetTextColor(TextMeshProUGUI text, Color color)
    {
        if (text == null) return;
        text.color = color;
    }

    private void SetImageColor(Image image, Color color)
    {
        if (image == null) return;
        image.color = color;
    }

    private void UpdateButtonsState(DialoguePanel activePanel)
    {
        // 버튼을 사용하지 않으므로 비워둠
    }

    private void NextLine()
    {
        if (currentDialogue == null) return;

        currentLineIndex++;
        if (currentLineIndex < currentDialogue.dialogueLines.Count)
        {
            DisplayLine(currentDialogue.dialogueLines[currentLineIndex]);
        }
        else
        {
            // 대화가 끝났을 때
            if (currentQuest != null)
            {
                QuestStatus status = QuestManager.Instance.GetQuestStatus(currentQuest.questID);
                ProcessQuestEndState(currentQuest, status);
            }
            
            ProcessDialogueEndActions();
            CloseDialogue();
        }
    }

    private void ProcessDialogueEndActions()
    {
        if (currentDialogue != null)
        {
            // 가이드 트리거
            if (!string.IsNullOrEmpty(currentDialogue.guideIdToTrigger))
            {
                GuideManager.Trigger(currentDialogue.guideIdToTrigger);
            }

            // 퀘스트 즉시 시작
            if (currentDialogue.questToStart != null && QuestManager.Instance != null)
            {
                QuestManager.Instance.ForceAcceptQuest(currentDialogue.questToStart);
            }

            // 퀘스트 해금만
            if (currentDialogue.questToUnlock != null && QuestManager.Instance != null)
            {
                QuestManager.Instance.SetQuestStatus(currentDialogue.questToUnlock, QuestStatus.Available);
            }

            // 퀘스트 완료 처리
            if (currentDialogue.questToComplete != null && QuestManager.Instance != null)
            {
                if (currentDialogue.forceCompleteQuest)
                {
                    QuestManager.Instance.ForceCompleteQuest(currentDialogue.questToComplete);
                }
                else
                {
                    QuestManager.Instance.CompleteQuest(currentDialogue.questToComplete);
                }
            }
        }
    }

    private void ProcessQuestEndState(QuestSO quest, QuestStatus status)
    {
        if (quest == null || QuestManager.Instance == null) return;

        // 수락 대화가 끝난 후에만 퀘스트가 열림(수락됨)
        if (status == QuestStatus.Available)
        {
            QuestManager.Instance.AcceptQuest(quest);
        }
        // 완료(Completed) 처리는 대화 시작 시점(ShowQuestDialogue)에 이미 수행되었으므로 여기선 아무것도 안 함
    }



    public void CloseDialogue()
    {
        ResetUI();
        // 팝업(NPC)에서 들어온 대화면 그 팝업으로 되돌리고, 아니면 None으로 닫는다(ReturnToPopupOrClose가 판정).
        UIStateManager.Instance.ReturnToPopupOrClose();

        onDialogueCloseCallback?.Invoke();
        onDialogueCloseCallback = null;
    }

    void Update()
    {
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState == UIState.Dialogue)
        {
            // 인디케이터 애니메이션 처리
            AnimateIndicator(leftPanel);
            AnimateIndicator(rightPanel);

            // 마우스 왼쪽 클릭, 스페이스바, 또는 F키 입력 감지
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.F))
            {
                if (isTyping)
                {
                    // 타이핑 중이면 즉시 완료
                    if (typingCoroutine != null) StopCoroutine(typingCoroutine);
                    CompleteTyping();
                }
                else
                {
                    // 타이핑 완료 상태면 다음 대화로
                    NextLine();
                }
            }
        }
    }

    private void AnimateIndicator(DialoguePanel panel)
    {
        if (panel == null || panel.triangleIndicator == null || !panel.triangleIndicator.activeSelf) return;

        float newY = panel.initialIndicatorPos.y + Mathf.Sin(Time.time * indicatorMoveSpeed) * indicatorMoveAmount;
        Vector3 pos = panel.triangleIndicator.transform.localPosition;
        pos.y = newY;
        panel.triangleIndicator.transform.localPosition = pos;
    }
}
