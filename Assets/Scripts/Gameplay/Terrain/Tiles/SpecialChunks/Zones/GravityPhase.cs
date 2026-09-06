// @tags: gravity, phase, enum, anti-gravity, special-chunk
using UnityEngine;

/// <summary>반중력 구간의 중력 위상.
/// Normal(강한 아래) → LowNormal(약한 아래·달 저중력) → Inverted(강한 위) → LowInverted(약한 위·달 저중력) 순환.
/// 각 방향에서 중력이 약해진 뒤 방향이 뒤집힌다.</summary>
public enum GravityPhase { Normal, LowNormal, Inverted, LowInverted }

/// <summary>GravityPhase 순수 로직 — 순환 순서 및 위상별 gravityScale 계산.</summary>
public static class GravityPhases
{
    /// <summary>순환 시퀀스. 강한→약한(같은 방향) 후 반대 방향으로 뒤집힌다.
    /// Normal → LowNormal → Inverted → LowInverted → (반복).</summary>
    public static readonly GravityPhase[] Cycle =
    {
        GravityPhase.Normal,
        GravityPhase.LowNormal,
        GravityPhase.Inverted,
        GravityPhase.LowInverted,
    };

    /// <summary>순환 시퀀스의 다음 인덱스(끝에서 0으로 되돌아감).</summary>
    public static int NextIndex(int index) => (index + 1) % Cycle.Length;

    /// <summary>중력이 위쪽(천장 방향)을 향하는 위상인지. 스프라이트 뒤집힘·점프 방향 판정용.</summary>
    public static bool IsInvertedDirection(GravityPhase phase)
        => phase == GravityPhase.Inverted || phase == GravityPhase.LowInverted;

    /// <summary>위상별 적용 gravityScale.
    /// Normal=defaultScale, LowNormal=defaultScale×lowFactor,
    /// Inverted=-|defaultScale|×antiMultiplier, LowInverted=그 값×lowFactor.</summary>
    public static float ScaleFor(GravityPhase phase, float defaultScale, float antiMultiplier, float lowFactor)
    {
        switch (phase)
        {
            case GravityPhase.LowNormal:   return defaultScale * lowFactor;
            case GravityPhase.Inverted:    return -Mathf.Abs(defaultScale) * antiMultiplier;
            case GravityPhase.LowInverted: return -Mathf.Abs(defaultScale) * antiMultiplier * lowFactor;
            default:                       return defaultScale;
        }
    }
}
