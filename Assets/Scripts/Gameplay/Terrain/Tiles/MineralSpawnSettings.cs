// @tags: mineral, spawn, settings, config
/// <summary>
/// 광물 스폰 튜닝 값 묶음. tileData.json에서 와서 MineralDecorator가 채운다.
/// 값이 늘어날 때마다 GenerateMinerals 시그니처가 길어지는 것을 막는다.
/// 배경: Assets/Docs/mineral-even-scatter.md §3-5
/// </summary>
public struct MineralSpawnSettings
{
    /// <summary>층 배율(mineralDensity) × 전역 배율(globalMineralDensity).</summary>
    public float DensityMultiplier;

    /// <summary>배치 지터 0~1. 0 = 격자 정중앙(인공적), 1 = 셀 전체(자연스러움).</summary>
    public float ScatterJitter;

    /// <summary>청크별 요청/배치 개수를 콘솔에 출력(튜닝용).</summary>
    public bool LogSummary;

    public static MineralSpawnSettings Default => new MineralSpawnSettings
    {
        DensityMultiplier = 1f,
        ScatterJitter = 1f,
        LogSummary = false,
    };
}
