// @tags: interface, npc, popup, interaction, quest, dialogue
using UnityEngine;

/// <summary>
/// <see cref="NpcPopup"/>·<see cref="NpcPopupOverlayUI"/>가 "누구의 팝업인지"를 읽는 창구.
///
/// 통합 컴포넌트(<see cref="WorldInteractable"/>의 트럭 NPC 종류)가 같은 팝업을 띄우도록
/// 데이터만 인터페이스로 뽑았다. 팝업의 표시·분기 로직은 프리팹/코드 양쪽에서 같다.
///
/// 새 구현체를 만들 일은 거의 없다 — 트럭·상인 같은 건 WorldInteractable로 붙이면 된다.
/// </summary>
public interface INpcPopupSource
{
    /// <summary>팝업 상단에 표시할 이름.</summary>
    string NpcName { get; }

    /// <summary>일반 대화 데이터. 없으면 대화 버튼이 숨는다.</summary>
    DialogueData Dialogue { get; }

    /// <summary>이 NPC에 직접 물린 메인 퀘스트(매니저에 없는 특수 퀘스트용 폴백).</summary>
    QuestSO MainQuest { get; }

    /// <summary>메인 퀘스트 담당 NPC인가. 느낌표 표시·퀘스트 대화 분기를 가른다.</summary>
    bool IsMainQuestNpc { get; }

    /// <summary>상점 버튼을 띄울지.</summary>
    bool CanOpenShop { get; }

    /// <summary>업그레이드 버튼을 띄울지.</summary>
    bool CanOpenUpgrade { get; }

    /// <summary>팝업이 뜰 머리 위 오프셋(월드 기준).</summary>
    Vector3 PopupOffset { get; }

    /// <summary>팝업 위치·거리 자동 종료의 기준이 되는 Transform.</summary>
    Transform SourceTransform { get; }

    /// <summary>연결해 둔 대화 UI. null이면 팝업이 씬에서 찾는다.</summary>
    QuestDialogueUI DialogueUI { get; }

    /// <summary>대화 중 비출 특정 카메라 (없으면 기본 카메라 유지).</summary>
    Unity.Cinemachine.CinemachineCamera DialogueCamera { get; }
}
