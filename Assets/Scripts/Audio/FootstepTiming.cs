// @tags: sound, footstep, audio, timing, pure
using UnityEngine;

/// <summary>
/// 이동 속도 → 스텝 간격.
///
/// 속도에 반비례시키되 상·하한으로 클램프한다. 클램프가 없으면 느리게 걸을 때
/// 발소리가 몇 초에 한 번씩 나고, 빠를 때는 기관총처럼 들린다.
/// </summary>
public static class FootstepTiming
{
    public const float MinInterval = 0.18f;
    public const float MaxInterval = 0.6f;

    /// <summary>baseInterval은 "속도 1일 때의 간격". 기본값은 지상 걷기 기준으로 맞췄다.</summary>
    public static float IntervalFor(float speed, float baseInterval = 0.9f)
    {
        float raw = baseInterval / Mathf.Max(Mathf.Abs(speed), 0.1f);
        return Mathf.Clamp(raw, MinInterval, MaxInterval);
    }
}
