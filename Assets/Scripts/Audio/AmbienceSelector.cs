// @tags: sound, ambience, audio, selector, pure, layer, daycycle
/// <summary>
/// 앰비언스 2레이어 키 묶음. Secondary가 null이면 그 레이어는 무음.
/// </summary>
public readonly struct AmbienceSet
{
    public readonly string Primary;
    public readonly string Secondary;

    public AmbienceSet(string primary, string secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }

    public static readonly AmbienceSet Silent = new AmbienceSet(null, null);

    public bool Equals(AmbienceSet other) => Primary == other.Primary && Secondary == other.Secondary;
}

/// <summary>
/// (지상여부, 시간대, 층) → 앰비언스 2레이어 키.
///
/// MonoBehaviour와 분리한 순수 로직이라 EditMode에서 테스트한다.
/// 실제 재생은 AmbienceDirector가 한다.
/// </summary>
public static class AmbienceSelector
{
    public static AmbienceSet Select(bool isSurface, TimeOfDay time, TileType layer)
    {
        if (isSurface)
        {
            // 지상은 층 개념이 없다 — 시간대로만 가른다
            return time == TimeOfDay.Morning
                ? new AmbienceSet(SfxKeys.AmbSurfaceDayCicada, SfxKeys.AmbSurfaceDayBirds)
                : new AmbienceSet(SfxKeys.AmbSurfaceNightInsects, null);
        }

        // 지하는 시간대와 무관하게 층으로만 가른다
        switch (layer)
        {
            case TileType.Dirt:            return AmbienceSet.Silent;   // 1층은 무음
            case TileType.HardStone:       return new AmbienceSet(SfxKeys.AmbLayerWind, null);
            case TileType.CoolStone:
            case TileType.Ice:
            case TileType.HotStone:
            case TileType.MagmaRock:
            case TileType.MeteoriteRock:   return new AmbienceSet(SfxKeys.AmbCaveDrip, null);
            default:                       return AmbienceSet.Silent;
        }
    }
}
