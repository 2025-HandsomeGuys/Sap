using UnityEngine;

/// <summary>
/// 스태미나 시스템의 각종 수치를 인스펙터에서 버튼으로 테스트해볼 수 있는 헬퍼 컴포넌트입니다.
/// </summary>
public class StaminaTestHelper : MonoBehaviour
{
    public StaminaManager staminaManager;

    [Header("Test Values")]
    public float testAmount = 20f;

    [ContextMenu("Add Injury")]
    public void TestAddInjury() => staminaManager?.AddInjury(testAmount);

    [ContextMenu("Add Burn")]
    public void TestAddBurn() => staminaManager?.AddBurn(testAmount);

    [ContextMenu("Add Frostbite")]
    public void TestAddFrostbite() => staminaManager?.AddFrostbite(testAmount);

    [ContextMenu("Add Radiation")]
    public void TestAddRadiation() => staminaManager?.AddRadiation(testAmount);

    [ContextMenu("Add Digging Reduction")]
    public void TestAddDiggingReduction() => staminaManager?.AddDiggingReduction(testAmount);

    [ContextMenu("Recover All Status")]
    public void TestRecoverStatus() => staminaManager?.RecoverStatus(testAmount, testAmount, testAmount, testAmount);

    [ContextMenu("Reset Digging")]
    public void TestResetDigging() => staminaManager?.ResetDiggingReduction();
}
