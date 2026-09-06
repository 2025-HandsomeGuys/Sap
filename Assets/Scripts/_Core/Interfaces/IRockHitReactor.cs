// @tags: rock, hit, reaction, interface, animation
/// <summary>
/// 바위가 "실제로 타격당한 순간"에만 반응하고 싶은 컴포넌트가 구현한다.
/// DiggableRock.Dig()가 HP를 깎은 직후 브로드캐스트한다.
///
/// IDamageStageable과 구분할 것:
///  - IDamageStageable.OnHpRatioChanged는 스폰·풀 재사용·HP 회복 때도 ratio=1로 불린다.
///    "지금 맞았다"를 구분할 수 없으므로 히트 연출을 걸면 돌이 스폰될 때마다 재생된다.
///  - 이 인터페이스는 타격 경로에서만 불린다. 치명타(HP 0)일 때는 호출되지 않는다
///    — 그 순간은 DestroyRock()의 파괴 VFX가 대신하기 때문이다.
/// </summary>
public interface IRockHitReactor
{
    /// <param name="worldPos">타격 지점(월드 좌표)</param>
    /// <param name="damage">이번 타격으로 들어간 데미지</param>
    void OnRockHit(UnityEngine.Vector2 worldPos, float damage);

    /// <summary>
    /// 파괴가 확정된 직후, 구멍·조각·드롭을 계산하기 전에 불린다.
    /// 연출이 transform 등에 얹어 둔 오프셋을 즉시 걷어내고 배치 확정값으로 되돌릴 것.
    ///
    /// 이게 없으면 흔들림이 진행 중인 프레임에 치명타가 들어왔을 때
    /// DestroyRock()이 원점으로 되돌려진 transform을 읽어 조각·효과음·광물이
    /// 청크 원점(예: 청크 (2,-7)의 월드 (20,-70))에 쏟아진다.
    /// </summary>
    void OnRockBreaking();
}
