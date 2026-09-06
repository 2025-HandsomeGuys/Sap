// @tags: interface, trap, collapse, vfx, special-chunk
/// <summary>
/// 싱크홀 붕괴 기믹에 연결 가능한 피드백 효과 인터페이스.
/// OCP: CollapseFloor를 수정하지 않고 새로운 효과(카메라 흔들기, 파티클 등)를 추가할 수 있다.
/// </summary>
public interface ICollapseEffect
{
    /// <summary>붕괴 경고 연출 (플레이어 감지 직후 호출).</summary>
    void PlayWarning();

    /// <summary>붕괴 완료 연출 (픽셀 소멸 직후 호출).</summary>
    void PlayCollapse();
}
