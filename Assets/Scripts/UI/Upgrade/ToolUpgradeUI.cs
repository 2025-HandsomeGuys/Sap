using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 도구 강화 UI - ShopUI에 통합됨 (독립적인 패널 관리 제거)
/// </summary>
public class ToolUpgradeUI : MonoBehaviour
{
    [Header("참조")]
    public PlayerStatsController playerStats;

    [Header("UI 요소")]
    public TextMeshProUGUI goldText; // 현재 골드 표시 (ShopUI와 공유 가능)

    [Header("도구 강화 슬롯")]
    public ToolUpgradeStat shovelStaminaUpgrade;        // 삽: 스테미나 소모 감소
    public ToolUpgradeStat pickaxeDropRateUpgrade;      // 곡괭이: 광물 추가 드랍률 증가
    public ToolUpgradeStat commonDigRangeUpgrade;       // 공통: 파는 범위 증가
    public ToolUpgradeStat drillSpeedUpgrade;           // 드릴: 전진 속도 증가
    public ToolUpgradeStat drillDurationUpgrade;        // 드릴: 지속 시간 증가

    [Header("강인도 슬롯 (단일 슬롯)")]
    public ToolUpgradeStat hardnessLevelUpgrade;       // 강인도 (레벨 1~7)

    [Header("해금 조건 표시")]
    public TextMeshProUGUI unlockConditionText; // 해금 조건 안내 텍스트

    void Start()
    {
        if (playerStats == null)
            playerStats = FindFirstObjectByType<PlayerStatsController>();

        SubscribeToGoldEvents();
        UpdateGoldDisplay();

        // 강인도 업그레이드 이벤트 구독
        ToolUpgradeStat.OnHardnessUpgraded += OnHardnessUpgraded;

        // 초기 UI 업데이트
        UpdateAllUpgradeSlots();
    }

    void OnDestroy()
    {
        UnsubscribeFromGoldEvents();
        ToolUpgradeStat.OnHardnessUpgraded -= OnHardnessUpgraded;
    }

    private void SubscribeToGoldEvents()
    {
        if (playerStats != null)
        {
            playerStats.OnGoldChanged -= OnGoldChanged;
            playerStats.OnGoldChanged += OnGoldChanged;
        }
    }

    private void UnsubscribeFromGoldEvents()
    {
        if (playerStats != null)
        {
            playerStats.OnGoldChanged -= OnGoldChanged;
        }
    }

    private void OnGoldChanged(int newGold)
    {
        UpdateGoldDisplay();
        UpdateAllUpgradeSlots();
    }

    private void OnHardnessUpgraded(int level)
    {
        Debug.Log($"강인도 레벨 {level} 해금!");
        UpdateAllUpgradeSlots();
    }

    // ShopUI에서 호출하여 업그레이드 탭이 활성화될 때 UI 업데이트
    public void OnUpgradeTabActivated()
    {
        UpdateGoldDisplay();
        UpdateAllUpgradeSlots();
    }

    private void UpdateGoldDisplay()
    {
        if (goldText != null && playerStats != null)
        {
            goldText.text = $"골드: {playerStats.gold}";
        }
    }

    private void UpdateAllUpgradeSlots()
    {
        // 도구 강화 슬롯 데이터 다시 로드 및 업데이트
        if (shovelStaminaUpgrade != null)
        {
            shovelStaminaUpgrade.LoadUpgradeData();
            shovelStaminaUpgrade.UpdateUI();
        }
        if (pickaxeDropRateUpgrade != null)
        {
            pickaxeDropRateUpgrade.LoadUpgradeData();
            pickaxeDropRateUpgrade.UpdateUI();
        }
        if (commonDigRangeUpgrade != null)
        {
            commonDigRangeUpgrade.LoadUpgradeData();
            commonDigRangeUpgrade.UpdateUI();
        }
        if (drillSpeedUpgrade != null)
        {
            drillSpeedUpgrade.LoadUpgradeData();
            drillSpeedUpgrade.UpdateUI();
        }
        if (drillDurationUpgrade != null)
        {
            drillDurationUpgrade.LoadUpgradeData();
            drillDurationUpgrade.UpdateUI();
        }

        // 강인도 슬롯 업데이트 (단일 슬롯)
        if (hardnessLevelUpgrade != null)
        {
            hardnessLevelUpgrade.LoadUpgradeData();
            hardnessLevelUpgrade.UpdateUI();
        }

        // 해금 조건 표시 업데이트
        UpdateUnlockConditionText();
    }

    public void UpdateUnlockConditionText()
    {
        if (unlockConditionText == null) return;

        SaveManager saveManager = FindFirstObjectByType<SaveManager>();
        if (saveManager == null) return;

        ToolUpgradeData data = saveManager.GetToolUpgradeData();
        if (data == null) return;

        int currentHardness = data.GetHardnessLevel();
        int nextHardness = currentHardness + 1;

        if (nextHardness > 7)
        {
            unlockConditionText.text = "모든 강인도 레벨을 해금했습니다!";
            return;
        }

        // 다음 강인도 해금 조건 확인
        int requiredLevel = nextHardness - 1;
        int shovelLevel = data.GetLevel(ToolUpgradeType.ShovelStaminaReduction);
        int pickaxeLevel = data.GetLevel(ToolUpgradeType.PickaxeDropRateIncrease);
        int commonLevel = data.GetLevel(ToolUpgradeType.CommonDigRangeIncrease);
        int drillSpeedLevel = data.GetLevel(ToolUpgradeType.DrillSpeedIncrease);
        int drillDurationLevel = data.GetLevel(ToolUpgradeType.DrillDurationIncrease);

        string condition = $"강인도 레벨 {nextHardness} 해금 조건:\n";
        condition += $"삽 강화: {shovelLevel}/{requiredLevel}\n";
        condition += $"곡괭이 강화: {pickaxeLevel}/{requiredLevel}\n";
        condition += $"공통 강화: {commonLevel}/{requiredLevel}\n";
        condition += $"드릴 속도: {drillSpeedLevel}/{requiredLevel}\n";
        condition += $"드릴 지속: {drillDurationLevel}/{requiredLevel}";

        unlockConditionText.text = condition;
    }

    public void OnGoldChanged()
    {
        UpdateGoldDisplay();
        UpdateAllUpgradeSlots();
    }
}


