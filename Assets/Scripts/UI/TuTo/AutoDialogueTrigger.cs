using UnityEngine;
using UnityEngine.Events;

public class AutoDialogueTrigger : MonoBehaviour
{
    [Header("실행할 대화 데이터")]
    public DialogueData dialogueData; // 일반 대화용 (퀘스트용이 필요하면 QuestSO 변수로 변경하세요)

    [Header("UI 연결")]
    public QuestDialogueUI dialogueUI; // 작성하신 QuestDialogueUI 스크립트 연결

    [Header("옵션")]
    public bool triggerOnStart = false; // 시작하자마자 자동 실행할지 여부
    public bool playOnlyOnce = true; // 한 번만 실행할지 여부
    [Tooltip("대화 시 비출 특정 카메라 (비워두면 기존 화면 유지)")]
    public Unity.Cinemachine.CinemachineCamera dialogueCamera;

    [Header("대화 종료 후 연계")]
    public FadeSignOnProximity linkedSign; // 대화가 끝나면 활성화될 표지판
    
    [Tooltip("대화 종료 시 강제로 시작할 퀘스트. 비워두면 아무 퀘스트도 주지 않습니다.")]
    public QuestSO questToStart;
    
    public UnityEvent onDialogueComplete;

    private bool hasPlayed = false;

    private void Start()
    {
        if (triggerOnStart)
        {
            // UI 초기화 대기를 위해 약간 지연 후 실행
            Invoke("TriggerDialogue", 0.1f);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 시작 시 자동 실행이 아닌 경우에만 트리거 작동
        if (!triggerOnStart && other.CompareTag("Player"))
        {
            TriggerDialogue();
        }
    }

    public void TriggerDialogue()
    {
        // 이미 실행되었고, 한 번만 실행하는 옵션이 켜져있다면 무시
        if (playOnlyOnce && hasPlayed) return;

        if (dialogueUI != null && dialogueData != null)
        {
            // QuestDialogueUI의 ShowDialogue 함수 호출, 종료 시 콜백 연결
            dialogueUI.ShowDialogue(dialogueData, this.transform, () =>
            {
                if (linkedSign != null)
                {
                    linkedSign.ActivateSign();
                }

                // 지정된 퀘스트가 있으면 강제 수락
                if (questToStart != null && QuestManager.Instance != null)
                {
                    QuestManager.Instance.ForceAcceptQuest(questToStart);
                }

                onDialogueComplete?.Invoke();
            });

            hasPlayed = true;
        }
        else
        {
            Debug.LogWarning("대화 데이터나 QuestDialogueUI가 연결되지 않았습니다.");
        }
    }
}