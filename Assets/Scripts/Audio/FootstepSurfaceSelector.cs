// @tags: sound, footstep, audio, selector, pure, layer, surface
/// <summary>
/// 발소리 A/B 키 쌍. 왼발·오른발용이다.
/// </summary>
public readonly struct FootstepPair
{
    public readonly string A;
    public readonly string B;

    public FootstepPair(string a, string b)
    {
        A = a;
        B = b;
    }
}

/// <summary>
/// (지상여부, 층, 구역 오버라이드) → 발소리 키 쌍.
///
/// MonoBehaviour와 분리한 순수 로직이라 EditMode에서 테스트한다.
/// 실제 재생은 FootstepPlayer가 한다.
/// </summary>
public static class FootstepSurfaceSelector
{
    // 💡 areaOverride 매개변수 추가 (기본값 "" 할당으로 기존 코드 호환성 유지)
    public static FootstepPair Select(bool isSurface, TileType layer, string areaOverride = "")
    {
        // 1. 구역 오버라이드가 있을 경우 최우선으로 판정
        if (!string.IsNullOrEmpty(areaOverride))
        {
            if (areaOverride == "UpgroundScene_home")
            {
                // 집 안 전용 발소리 키 반환
                return new FootstepPair(SfxKeys.StepHomeA, SfxKeys.StepHomeB);
            }
        }

        // 2. 오버라이드가 없을 경우의 기존 지상/지하 판정
        if (isSurface)
            return new FootstepPair(SfxKeys.StepGrass, SfxKeys.StepGrassB);

        switch (layer)
        {
            case TileType.HardStone:
                return new FootstepPair(SfxKeys.StepHardStone, SfxKeys.StepHardStoneB);

            // 나머지 층은 흙 발소리를 공용으로 쓴다.
            default:
                return new FootstepPair(SfxKeys.StepDirt, SfxKeys.StepDirtB);
        }
    }

    /// <summary>점프 시작음.</summary>
    public static string SelectJump(bool isSurface, TileType layer, string areaOverride = "")
    {
        // 집 안에서 점프할 때의 소리 오버라이드
        if (!string.IsNullOrEmpty(areaOverride) && areaOverride == "UpgroundScene_home")
            return SfxKeys.JumpGrass; // (SfxKeys에 JumpHome이 있다고 가정, 필요시 수정)

        return isSurface ? SfxKeys.JumpGrass : SfxKeys.JumpDirt;
    }

    /// <summary>착지음.</summary>
    public static string SelectLand(bool isSurface, TileType layer, string areaOverride = "")
    {
        // 집 안에서 착지할 때의 소리 오버라이드
        if (!string.IsNullOrEmpty(areaOverride) && areaOverride == "UpgroundScene_home")
            return SfxKeys.StepHomeA; // (SfxKeys에 LandHome이 있다고 가정, 필요시 수정)

        return isSurface ? SfxKeys.LandGrass : SfxKeys.LandDirt;
    }
}