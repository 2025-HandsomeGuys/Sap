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
    public PlayerStats playerStats;   // �÷��̾� ���� ����
    public Button button;             // UI ��ư
    public TextMeshProUGUI costText;             // ��� ǥ�� UI
    public TextMeshProUGUI levelText;            // ���� ǥ�� UI
    public TextMeshProUGUI goldText; // ���� ��� UI

    [Header("Upgrade Settings")]
    public UpgradeType upgradeType;   // � ���� ��ȭ ��ư����
    public int baseCost = 10;         // ù ��ȭ ���
    public int costIncrease = 5;      // �ܰ躰 ��� ������
    public float increaseAmount = 10; // �ܰ躰 ������

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
        // PlayerStats�� ���׷��̵� �Լ� ���� ȣ��
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

        // ��尡 �����ϸ� PlayerStats �ʿ��� ���׷��̵尡 �� �ǹǷ�,
        // ������ ���׷��̵� �����ߴ��� Ȯ�� �ʿ� �� ��尡 �پ����� üũ
        // (��尡 ������� �ʾҴٸ� ��� �״�� ����)
        if (playerStats.gold < currentCost) return;

        // ���׷��̵� ���� �� ����/��� ����
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
