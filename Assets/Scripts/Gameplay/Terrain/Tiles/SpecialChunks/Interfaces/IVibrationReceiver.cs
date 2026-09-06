// @tags: interface, vibration, special-chunk, trap, hazard
/// <summary>
/// 진동 신호에 반응하는 컴포넌트 계약.
///
/// SOLID:
///  - ISP: OnVibration() 하나만 — 최소 계약.
///  - OCP: 새 반응체(유리블록, 눈덩이 등) 추가 시 VibrationManager 코드 불변.
///  - DIP: VibrationManager는 이 인터페이스에만 의존, 구체 구현을 모른다.
/// </summary>
public interface IVibrationReceiver
{
    /// <summary>진동 신호를 수신하면 각자의 방식으로 반응한다.</summary>
    void OnVibration();
}
