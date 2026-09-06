// @tags: interface, damage, special-chunk, vfx, event
/// <summary>
/// HP 비율(0~1)이 변경될 때 브로드캐스트를 받는 구독 인터페이스.
///
/// SOLID:
///  - ISP: OnHpRatioChanged 하나만 — 최소 계약.
///  - OCP: 비주얼·오디오·이펙트 등 새 구독체를 언제든 자유롭게 추가 가능.
///  - DIP: HP 엔티티(TrashWallEntity, DiggableRock 등)는 이 인터페이스에만 의존,
///          구체 구현 클래스를 전혀 알지 못한다.
/// </summary>
public interface IDamageStageable
{
    /// <param name="hpRatio">현재 HP / 최대 HP. 0(사망 직전) ~ 1(풀피)</param>
    void OnHpRatioChanged(float hpRatio);
}
