using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum UpgradeType
{
    MaxStamina,
    MoveSpeed,
    JumpForce,
    MiningEfficiency,
    MiningPower
}

public class UpgradeStat : MonoBehaviour
{
    [Header("References")]
    public PlayerStats playerStats;   // 플레이어 스탯 참조
    public Button button;             // UI 버튼
    public TextMeshProUGUI costText;             // 비용 표시 UI
    public TextMeshProUGUI levelText;            // 레벨 표시 UI
    public TextMeshProUGUI goldText; // 남은 골드 UI

    [Header("Upgrade Settings")]
    public UpgradeType upgradeType;   // 어떤 스탯 강화 버튼인지
    public int baseCost = 10;         // 첫 강화 비용
    public int costIncrease = 5;      // 단계별 비용 증가량
    public float increaseAmount = 10; // 단계별 증가량

    private int level = 0;
    private int currentCost;

    void Start()
    {
        currentCost = baseCost;
        UpdateUI();
        button.onClick.AddListener(OnClickUpgrade);
    }

    void OnClickUpgrade()
    {
        // PlayerStats의 업그레이드 함수 직접 호출
        switch (upgradeType)
        {
            case UpgradeType.MaxStamina:
                playerStats.UpgradeMaxStamina(currentCost, increaseAmount);
                break;
            case UpgradeType.MoveSpeed:
                playerStats.UpgradeMoveSpeed(currentCost, increaseAmount);
                break;
            case UpgradeType.JumpForce:
                playerStats.UpgradeJumpForce(currentCost, increaseAmount);
                break;
            case UpgradeType.MiningEfficiency:
                playerStats.UpgradeMiningEfficiency(currentCost, increaseAmount);
                break;
            case UpgradeType.MiningPower:
                playerStats.UpgradeMiningPower(currentCost, Mathf.RoundToInt(increaseAmount));
                break;
        }

        // 골드가 부족하면 PlayerStats 쪽에서 업그레이드가 안 되므로,
        // 실제로 업그레이드 성공했는지 확인 필요 → 골드가 줄었는지 체크
        // (골드가 충분하지 않았다면 비용 그대로 유지)
        if (playerStats.gold < currentCost) return;

        // 업그레이드 성공 시 레벨/비용 갱신
        level++;
        currentCost = baseCost + (level * costIncrease);
        UpdateUI();
    }

    void UpdateUI()
    {
        if (costText != null)
            costText.text = $"Cost: {currentCost}";
        if (levelText != null)
            levelText.text = $"Lv. {level}";
        goldText.text = $"Gold left : {playerStats.gold}";
    }
}
