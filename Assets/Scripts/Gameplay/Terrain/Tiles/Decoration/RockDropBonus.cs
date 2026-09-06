// @tags: rock, drop, mineral, upgrade, balance, decoration
using UnityEngine;

/// <summary>
/// 업그레이드 <see cref="UpgradeEffectType.RockMineralCountUp"/>("돌에서 나오는 광물 수")를
/// 실제 드롭 개수에 얹는 단일 지점.
///
/// 일반 돌(<see cref="DiggableRock.DropRareMinerals"/>)과 광물돌(<see cref="MineralRock"/>)이
/// 개수를 각자 굴리기 때문에 보너스를 두 곳에 흩어 놓으면 한쪽만 적용되는 사고가 난다.
/// 새 드롭 경로를 만들면 마지막에 이 함수를 통과시킬 것.
///
/// 소수부는 확률로 처리한다 — 0.5면 50% 확률로 +1개. 업그레이드 한 칸이
/// 소형 돌(1~3개)에 +1 정수로 들어가면 증가폭이 33~100%로 튀기 때문이다.
/// </summary>
public static class RockDropBonus
{
    /// <summary>돌 1개에서 실제로 떨굴 광물 개수. 원래 개수가 0이면 보너스도 붙지 않는다.</summary>
    public static int Apply(int baseCount)
    {
        if (baseCount <= 0) return baseCount;

        var mgr = UpgradeManager.Instance;
        if (mgr == null) return baseCount;

        float extra = mgr.GetStatValue(UpgradeEffectType.RockMineralCountUp, 0f);
        if (extra <= 0f) return baseCount;

        int whole = Mathf.FloorToInt(extra);
        float frac = extra - whole;
        if (frac > 0f && Random.value < frac) whole += 1;

        return baseCount + whole;
    }
}
