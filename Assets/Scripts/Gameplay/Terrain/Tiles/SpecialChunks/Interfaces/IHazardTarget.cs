// @tags: interface, trap, damage, hazard, player
/// <summary>
/// 트랩·위험 지대에서 피해를 받을 수 있는 대상의 계약.
///
/// SOLID:
///  - DIP: 트랩(ScrapExplosion, DelayedBlast 등)이 PlayerStat 구체 클래스를 직접 참조하지 않는다.
///  - OCP: 플레이어 이외의 대상(몬스터, NPC 등)도 이 인터페이스 구현으로 피해를 받을 수 있다.
/// </summary>
public interface IHazardTarget
{
    /// <summary>트랩·폭발 등 위험 요소에 의한 피해를 적용한다.</summary>
    void ApplyHazardDamage(float amount);
}
