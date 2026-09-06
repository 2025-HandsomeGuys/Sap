using System.Collections.Generic;

/// <summary>
/// 스탯 수정치를 제공하는 시스템이 구현하는 인터페이스.
/// PlayerStat은 등록된 모든 IStatProvider의 Modifier를 합산하여 최종 값을 계산.
/// </summary>
public interface IStatProvider
{
    /// <summary>
    /// 현재 이 시스템이 제공하는 모든 스탯 수정치 목록을 반환
    /// </summary>
    IReadOnlyList<StatModifier> GetModifiers();
}
