// @tags: interface, interaction, player, npc, pickup
/// <summary>
/// OCP: 플레이어 상호작용 시스템의 확장 지점.
/// 광물 줍기, 엘리베이터, NPC 대화, 문 등 모든 상호작용 가능한 오브젝트가 구현한다.
/// PlayerInteractor는 이 인터페이스만 알면 되고, 구체 클래스를 알 필요가 없다.
/// </summary>
public interface IInteractable
{
    /// <summary>플레이어 가까이 있을 때 표시할 안내 문구 (예: "줍기")</summary>
    string InteractionPrompt { get; }

    /// <summary>
    /// 상호작용 우선순위. 높을수록 거리와 무관하게 먼저 선택된다.
    /// ElevatorController = 10, PickupableItem = 0 (기본값)
    /// </summary>
    int InteractionPriority => 0;

    /// <summary>
    /// 지금 상호작용 후보로 잡힐 수 있는가.
    /// false면 PlayerInteractor의 탐지 단계에서 아예 제외된다 → 하이라이트·안내 문구도 안 뜬다.
    /// 상태에 따라 켜졌다 꺼지는 오브젝트만 구현한다 (예: 아직 땅에 박힌 광물).
    /// </summary>
    bool CanInteract => true;

    /// <summary>
    /// 플레이어가 F 키를 눌렀을 때 호출된다.
    /// </summary>
    /// <param name="interactor">상호작용을 시도한 플레이어 GameObject</param>
    void Interact(UnityEngine.GameObject interactor);
}
