// @tags: interface, highlight, visual, interaction, player
/// <summary>
/// ISP: IInteractable과 분리된 시각 강조 전용 인터페이스.
/// 모든 IInteractable이 하이라이트를 필요로 하지는 않으므로 별도 인터페이스로 분리한다.
/// PlayerInteractor가 타깃 변경 시 호출한다.
/// </summary>
public interface IHighlightable
{
    void SetHighlighted(bool highlighted);
}
