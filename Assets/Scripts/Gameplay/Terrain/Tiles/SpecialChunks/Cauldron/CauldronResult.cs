// @tags: cauldron, result, reward, special-chunk
/// <summary>도깨비 가마솥 1회 시도 결과 종류.</summary>
public enum CauldronOutcome { GreatSuccess, Success, Fail, GreatFail }

/// <summary>결과로 무엇이 산출되는지. EasterEgg는 최상위 광물 투입 시 분기(추후 유니크 보상 hook).</summary>
public enum CauldronRewardType { Mineral, Ash, Explosion, EasterEgg }

/// <summary>가마솥 1회 시도의 해석 결과 페이로드.</summary>
public struct CauldronResult
{
    public CauldronOutcome outcome;
    public CauldronRewardType rewardType;
    public MineralID resultMineral; // Mineral / EasterEgg일 때 유효
    public int explosiveCount;      // Explosion일 때 유효
}

/// <summary>확률 묶음. 합은 1.0 가정.</summary>
public struct CauldronProbabilities
{
    public double greatSuccess;
    public double success;
    public double fail;
    public double greatFail;
}
