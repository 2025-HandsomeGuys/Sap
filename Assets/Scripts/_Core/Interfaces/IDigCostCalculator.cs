// @tags: interface, dig, stamina, cost, mining
using UnityEngine;

/// <summary>
/// 채굴 비용(스태미나 등)을 계산하는 인터페이스
/// </summary>
public interface IDigCostCalculator
{
    /// <summary>
    /// 해당 타일을 채굴할 때 필요한 비용을 지불합니다.
    /// </summary>
    /// <param name="tileType">채굴 대상 타일 타입</param>
    /// <param name="radius">채굴 반경 (월드 단위). 기준 1.0f 대비 비례 계산.</param>
    /// <returns>지불 성공 여부 (비용 부족 시 false)</returns>
    bool PayCost(TileType tileType, float radius);

    /// <summary>
    /// 파기 지점 월드 위치 기반으로 비용을 지불한다.
    /// 블렌딩 구역 안이면 두 층 저항값을 선형 보간하여 계산한다.
    /// </summary>
    bool PayCost(Vector2 worldPos, float radius);
}
