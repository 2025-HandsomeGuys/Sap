/// <summary>
/// 곡괭이 채굴 시스템이 "퍼즐 상호작용 대상"으로 취급해야 하는 IDiggable 마커.
/// 이 인터페이스를 구현한 대상은 타격 시 스태미나 소모·MaxStamina 감소에서 제외된다.
/// (예: 별자리 한붓그리기 퍼즐의 정점·리셋 레버 — 스위치일 뿐 채굴 대상이 아님)
/// </summary>
public interface IPuzzleDiggable : IDiggable
{
}
