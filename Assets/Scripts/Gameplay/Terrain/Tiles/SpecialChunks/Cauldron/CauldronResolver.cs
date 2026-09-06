// @tags: cauldron, resolver, probability, special-chunk
using System;

/// <summary>
/// 입력 광물 + RNG → CauldronResult. 확률 추첨 후 성공계열은 사다리 승급, 실패는 재, 대실패는 폭발 개수.
/// Unity 비의존 — EditMode 테스트 대상.
/// </summary>
public class CauldronResolver
{
    private readonly MineralUpgradeLadder _ladder;
    private readonly CauldronProbabilities _probs;
    private readonly int _explosiveMin;
    private readonly int _explosiveMax;

    public CauldronResolver(MineralUpgradeLadder ladder, CauldronProbabilities probs, int explosiveMin, int explosiveMax)
    {
        _ladder = ladder;
        _probs = probs;
        _explosiveMin = explosiveMin;
        _explosiveMax = explosiveMax;
    }

    public CauldronResult Resolve(MineralID input, System.Random rng)
    {
        double roll = rng.NextDouble();
        double gs = _probs.greatSuccess;
        double s  = gs + _probs.success;
        double f  = s + _probs.fail;

        if (roll < gs) return Upgrade(input, 2, CauldronOutcome.GreatSuccess, rng);
        if (roll < s)  return Upgrade(input, 1, CauldronOutcome.Success, rng);
        if (roll < f)  return new CauldronResult { outcome = CauldronOutcome.Fail, rewardType = CauldronRewardType.Ash };

        return new CauldronResult
        {
            outcome = CauldronOutcome.GreatFail,
            rewardType = CauldronRewardType.Explosion,
            explosiveCount = rng.Next(_explosiveMin, _explosiveMax + 1)
        };
    }

    private CauldronResult Upgrade(MineralID input, int steps, CauldronOutcome outcome, System.Random rng)
    {
        MineralID result = _ladder.Resolve(input, steps, rng, out bool clamped);
        return new CauldronResult
        {
            outcome = outcome,
            rewardType = clamped ? CauldronRewardType.EasterEgg : CauldronRewardType.Mineral,
            resultMineral = result
        };
    }
}
