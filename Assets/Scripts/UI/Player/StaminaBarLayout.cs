// @tags: stamina, ui, bar, layout, max-reduction, pure-logic
using UnityEngine;

/// <summary>
/// 스태미나 바 세그먼트 하나하나의 '값'(픽셀 아님). 전부 더하면 <see cref="total"/>이 된다.
/// </summary>
public struct StaminaBarSegments
{
    public float total;        // 바 전체가 나타내는 값 = 감소가 없었을 때의 최대치
    public float current;      // 지금 쓸 수 있는 양
    public float recoverable;  // 소모되어 회복 대기 중인 양
    public float injury;
    public float burn;
    public float frostbite;
    public float radiation;
    public float digging;
}

/// <summary>
/// 스태미나 바 배치 계산(순수 로직, EditMode 테스트 대상).
///
/// 핵심 불변식: 입력 <c>effectiveMax</c>는 <see cref="PlayerStat.MaxStamina"/> —
/// <b>이미 상태이상 감소가 반영된 값</b>이다. 따라서 여기서 감소를 다시 빼면 안 되고,
/// 반대로 '감소 전 최대치'는 감소분을 더해서 복원한다.
///
/// 감소 필드(injury 등)는 Flat modifier라 최종값 공식 <c>(Base + ΣFlat) × ΠPercent</c>에서
/// 곱연산 배율까지 먹는다. 그래서 화면에 그릴 땐 <c>percentMultiplier</c>를 곱해
/// effectiveMax와 같은 스케일로 맞춘다.
/// </summary>
public static class StaminaBarLayout
{
    public static StaminaBarSegments Compute(
        float effectiveMax, float currentStamina, float percentMultiplier,
        float injury, float burn, float frostbite, float radiation, float digging)
    {
        float p = Mathf.Max(0f, percentMultiplier);

        var s = new StaminaBarSegments
        {
            injury    = Mathf.Max(0f, injury)    * p,
            burn      = Mathf.Max(0f, burn)      * p,
            frostbite = Mathf.Max(0f, frostbite) * p,
            radiation = Mathf.Max(0f, radiation) * p,
            digging   = Mathf.Max(0f, digging)   * p,
        };

        // 감소가 기준값을 넘기면 MaxStamina가 음수까지 내려갈 수 있다(PlayerStat은 클램프하지 않음).
        float max = Mathf.Max(0f, effectiveMax);
        float lost = s.injury + s.burn + s.frostbite + s.radiation + s.digging;

        s.current     = Mathf.Clamp(currentStamina, 0f, max);
        s.recoverable = max - s.current;
        s.total       = max + lost;
        return s;
    }
}
