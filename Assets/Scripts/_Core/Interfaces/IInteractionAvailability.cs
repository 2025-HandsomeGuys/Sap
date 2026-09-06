// @tags: interface, interaction, indicator, availability, visual
/// <summary>
/// ISP: 상호작용이 "지금" 가능한지 알려주는 선택적 인터페이스.
/// WorldInteractable의 느낌표 피드백이 이 값을 읽어,
/// false면 플레이어가 근처에 있어도 느낌표를 띄우지 않는다.
/// 예: 침대는 밤(Afternoon)에만 true — 낮에는 표시가 뜨지 않는다.
/// 구현하지 않은 오브젝트는 근접만으로 항상 표시된다.
/// </summary>
public interface IInteractionAvailability
{
    bool IsInteractionAvailable { get; }
}
