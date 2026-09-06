// @tags: interface, zone, trigger, player, buff
/// <summary>
/// 존 진입/이탈 시 플레이어에게 적용되는 효과를 정의하는 인터페이스.
///
/// [SOLID]
///   ISP: 두 메서드만 선언 — 구현체는 필요한 것만 담당.
///   DIP: ZoneEffectTrigger는 구체 클래스 대신 이 인터페이스에만 의존.
///   OCP: 새 효과(BuffZone, DamageZone 등)는 이 인터페이스를 구현하는 것만으로 확장 가능.
/// </summary>
public interface IZoneEffect
{
    /// <summary>플레이어가 존에 진입했을 때 호출</summary>
    void OnEnter(PlayerStat player);

    /// <summary>플레이어가 존에서 이탈했을 때 호출</summary>
    void OnExit(PlayerStat player);
}
