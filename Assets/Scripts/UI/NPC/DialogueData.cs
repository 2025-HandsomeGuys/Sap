using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New Dialogue", menuName = "Game Data/Dialogue Data")]
public class DialogueData : ScriptableObject
{
    [Header("대화 목록")]
    public List<DialogueLine> dialogueLines = new List<DialogueLine>();

    [Header("대화 연출")]
    [Tooltip("이 대화 시 비출 카메라의 이름을 적어주세요. 씬에 있는 해당 이름의 카메라를 찾아 비춥니다.")]
    public string targetCameraName;

    [Header("완료 후 동작")]
    public bool openShopAfterDialogue = false;
    public bool openUpgradeAfterDialogue = false;

    [Tooltip("대화 종료 후 띄울 가이드 ID (비워두면 아무 일도 없음)")]
    public string guideIdToTrigger;

    [Tooltip("대화 종료 후 완료 처리할 퀘스트 (비워두면 무시)")]
    public QuestSO questToComplete;
    
    [Tooltip("대화 종료 후 즉시 수락(진행 중) 상태로 만들 퀘스트")]
    public QuestSO questToStart;

    [Tooltip("대화 종료 후 수락 가능(해금) 상태로 만들 퀘스트")]
    public QuestSO questToUnlock;

    [Tooltip("조건(재료 보유 등)과 무관하게 강제로 퀘스트를 완료 처리할지 여부")]
    public bool forceCompleteQuest = false;
}

