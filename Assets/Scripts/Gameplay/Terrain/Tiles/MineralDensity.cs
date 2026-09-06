// @tags: mineral, density, spawn, generation, pure-logic
/// <summary>
/// 광물 청크 스폰 개수 계산. Unity 비의존 순수 로직 — EditMode 테스트 대상.
/// 난수는 전부 호출측이 주입한다(청크 결정성 보존 + 테스트 가능성).
/// 배경: Assets/Docs/mineral-density-redesign.md
/// </summary>
public static class MineralDensity
{
    /// <summary>
    /// 깊이 보정 계수. Rare는 0→1(깊을수록 많이), Common은 1→0.5(깊을수록 적게).
    /// 기존 MineralGenerator의 곡선을 그대로 옮긴 것이다.
    /// </summary>
    public static double DepthFactor(int depth, int minDepth, int maxDepth, bool isRare)
    {
        double t = (maxDepth > minDepth)
            ? Clamp01((double)(depth - minDepth) / (maxDepth - minDepth))
            : 1.0;

        return isRare ? t : 1.0 - 0.5 * t;
    }

    /// <summary>perChunk 범위가 쓸 수 있는 형태인지. TileDataManager 로드 검증에서도 쓴다.</summary>
    public static bool HasValidRange(float[] perChunk)
        => perChunk != null && perChunk.Length >= 2;

    /// <summary>
    /// 이 청크에서 스폰을 요청할 개수(실수). 깊이 범위 밖이거나 perChunk가 누락이면 0.
    /// lerpRoll은 [0,1) 균등 난수.
    /// </summary>
    public static double ExpectedCount(MineralRuleJson rule, int depth, double densityMultiplier, double lerpRoll)
    {
        if (rule == null) return 0.0;
        if (depth < rule.minDepth || depth > rule.maxDepth) return 0.0;

        // 누락에 기본값을 주지 않는다 — 설정 안 한 광물이 조용히 나오면 추적이 어렵다.
        if (!HasValidRange(rule.perChunk)) return 0.0;
        if (densityMultiplier <= 0.0) return 0.0;

        double lo = rule.perChunk[0];
        double hi = rule.perChunk[1];
        double n = lo + (hi - lo) * Clamp01(lerpRoll);

        n *= DepthFactor(depth, rule.minDepth, rule.maxDepth, rule.IsRare);
        n *= densityMultiplier;

        return n > 0.0 ? n : 0.0;
    }

    /// <summary>
    /// 소수부를 확률로 반올림. 기대값은 정확히 n이다.
    /// 예: 3.4 → 40% 확률로 4개, 60% 확률로 3개.
    /// 이게 있어야 perChunk [0, 1] 같은 1개 미만 희소 광물을 표현할 수 있다.
    /// roundRoll은 [0,1) 균등 난수.
    /// </summary>
    public static int ProbabilisticRound(double n, double roundRoll)
    {
        if (n <= 0.0) return 0;

        int floor = (int)System.Math.Floor(n);
        double frac = n - floor;

        return roundRoll < frac ? floor + 1 : floor;
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
}
