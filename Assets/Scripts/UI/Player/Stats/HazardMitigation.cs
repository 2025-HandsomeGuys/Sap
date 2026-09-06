// @tags: stat, damage, defense, resist, hazard, pure-logic, player

/// <summary>
/// 방어력·속성 저항이 받는 피해를 얼마나 깎는지 계산하는 순수 함수.
///
/// MonoBehaviour에서 분리한 이유는 EditMode에서 값만으로 검증하기 위해서다
/// (<c>Assets/Tests/EditMode/HazardMitigationTests.cs</c>).
///
/// <b>소프트캡</b>을 쓴다 — <c>amount × K / (K + stat)</c>. 뺄셈 감산(<c>amount - stat</c>)이면
/// 방어력이 피해량을 넘는 순간 지속 피해가 0이 되어 용암·냉기지대가 완전히 무력화된다.
/// 이 곡선은 아무리 쌓아도 0에 닿지 않으면서 체감은 선형에 가깝다.
///
/// K는 "이 값이면 피해 절반"이 되는 지점이다:
///  - <see cref="DefenseK"/> 10 — 폴백 강화 테이블이 레벨당 방어력 +1을 주므로
///    (<see cref="EquipmentUpgradeFormula.TestDefensePerLevel"/>) 3부위 만강(+12)이 약 45% 감소가 된다.
///  - <see cref="ResistK"/> 20 — 방한/방열 포션 값이 20이라(FrostbiteResist·BurnResist.asset)
///    포션 하나가 정확히 "해당 속성 피해 절반"이 된다.
/// </summary>
public static class HazardMitigation
{
    /// <summary>방어력이 이 값이면 부상 피해가 절반.</summary>
    public const float DefenseK = 10f;

    /// <summary>속성 저항이 이 값이면 해당 속성 피해가 절반(= 방한/방열 포션 1개).</summary>
    public const float ResistK = 20f;

    /// <summary>
    /// <paramref name="amount"/>에 소프트캡 감산을 적용한 값. stat이 0 이하면 그대로 돌려준다.
    /// 회복(음수 amount)에는 감산을 걸지 않는다 — 방어력이 높을수록 덜 회복되면 안 된다.
    /// </summary>
    public static float Apply(float amount, float stat, float k)
    {
        if (amount <= 0f || stat <= 0f || k <= 0f) return amount;
        return amount * (k / (k + stat));
    }
}
