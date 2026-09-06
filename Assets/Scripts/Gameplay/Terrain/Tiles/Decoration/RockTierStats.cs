// @tags: rock, tier, hp, drop, balance, decoration
using UnityEngine;

/// <summary>
/// 돌 크기 티어(<see cref="RockSizeTier"/>)별 광물 드롭 개수 범위.
///
/// 크기는 이제 프리팹의 대/중/소(RockBreakVFX.tier)로만 표현된다.
/// 스폰 시 랜덤 크기 배율(구 0.5~1.5)은 <see cref="RockLayoutCalculator"/>에서 이미 제거돼
/// 모든 돌이 원본 크기(scale=1)로 나오므로, 드롭도 배율이 아니라 티어를 기준으로 갈린다.
///
/// HP는 여기 없다 — 프리팹의 DiggableRock.baseHp에 크기별 값을 직접 넣는다.
/// </summary>
public static class RockTierStats
{
    /// <summary>
    /// 일반 돌 1개를 완파했을 때 나오는 광물 개수 범위(min은 보장 하한).
    /// 소 1~3 / 중 2~5 / 대 4~8. 실제 개수는 깊이에 따라 min→max로 램프된다.
    /// 광물돌(MineralRock)은 자체 minDrop/maxDrop을 쓰므로 이 표와 무관하다.
    /// </summary>
    public static (int min, int max) DropRange(RockSizeTier tier)
    {
        switch (tier)
        {
            case RockSizeTier.Large:  return (4, 8);
            case RockSizeTier.Medium: return (2, 5);
            default:                  return (1, 3);
        }
    }
}
