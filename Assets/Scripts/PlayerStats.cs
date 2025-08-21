using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [Header("Stamina")]
    public float originalMaxStamina = 500f;
    public float maxStamina;
    public float currentStamina;

    [Header("Movement")]
    public float originalMoveSpeed = 5f;
    public float moveSpeed;

    public float originalJumpForce = 7f;
    public float jumpForce;

    [Header("Inventory")]
    public int originalInventorySize = 20; // �κ��丮 ������ ���� �� ����
    public int inventorySize;

    [Header("Mining")]
    public float originalMiningEfficiency = 1f;
    public float miningEfficiency;

    public int originalMiningPower = 0;
    public int miningPower;

    [Header("Gold")]
    public int gold = 0;

    void Awake()
    {
        // Stamina
        maxStamina = originalMaxStamina;
        currentStamina = maxStamina;

        // Movement
        moveSpeed = originalMoveSpeed;
        jumpForce = originalJumpForce;

        // Inventory
        inventorySize = Mathf.Max(1, originalInventorySize);

        // Mining
        miningEfficiency = Mathf.Max(0.1f, originalMiningEfficiency); // ���� ����
        miningPower = originalMiningPower;
    }

    // ------------ Gold ------------
    public void AddGold(int amount)
    {
        gold += Mathf.Max(0, amount);
    }

    public bool SpendGold(int amount)
    {
        if (gold >= amount)
        {
            gold -= amount;
            return true;
        }
        Debug.Log("��� ����!");
        return false;
    }

    // ------------ Upgrade Functions ------------
    public void UpgradeMaxStamina(int cost, float increaseAmount)
    {
        if (SpendGold(cost))
        {
            originalMaxStamina += increaseAmount;
            maxStamina += increaseAmount;
            currentStamina += increaseAmount;
            Debug.Log($"MaxStamina ���׷��̵�! ����: {maxStamina}");
        }
    }

    public void UpgradeMoveSpeed(int cost, float increaseAmount)
    {
        if (SpendGold(cost))
        {
            originalMoveSpeed += increaseAmount;
            moveSpeed = originalMoveSpeed;
            Debug.Log($"MoveSpeed ���׷��̵�! ����: {moveSpeed}");
        }
    }

    public void UpgradeJumpForce(int cost, float increaseAmount)
    {
        if (SpendGold(cost))
        {
            originalJumpForce += increaseAmount;
            jumpForce = originalJumpForce;
            Debug.Log($"jumpForce ���׷��̵�! ����: {jumpForce}");
        }
    }

    public void UpgradeMiningEfficiency(int cost, float increaseAmount)
    {
        if (SpendGold(cost))
        {
            originalMiningEfficiency = Mathf.Clamp(miningEfficiency + increaseAmount, 0.1f, 10f);
            miningEfficiency = originalMiningEfficiency;
            Debug.Log($"MiningEfficiency ���׷��̵�! ����: {miningEfficiency}");
        }
    }

    public void UpgradeMiningPower(int cost, int increaseAmount)
    {
        if (SpendGold(cost))
        {
            originalMiningPower += increaseAmount;
            miningPower = originalMiningPower;
            Debug.Log($"MiningPower ���׷��̵�! ����: {miningPower}");
        }
    }

    // ------------ Stamina ------------
    public void UseStamina(float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
    }

    public void RecoverStamina(float amount)
    {
        currentStamina = Mathf.Min(maxStamina, currentStamina + amount);
    }

    public void ReduceMaxStamina(float amount)
    {
        maxStamina = Mathf.Max(0f, maxStamina - amount);
        if (currentStamina > maxStamina)
            currentStamina = maxStamina;
    }

    public void RecoverMaxStamina(float amount)
    {
        maxStamina = Mathf.Min(originalMaxStamina, maxStamina + amount);
    }

    //// ------------ Movement ------------
    //public void SetMoveSpeed(float value)
    //{
    //    moveSpeed = Mathf.Max(0f, value);
    //}

    //public void SetJumpForce(float value)
    //{
    //    jumpForce = Mathf.Max(0f, value);
    //}

    //// ------------ Inventory ------------
    //public void SetInventorySize(int size)
    //{
    //    inventorySize = Mathf.Max(1, size);
    //}

    //// ------------ Mining ------------
    //// ����(��)�� ����: 1.0 = �⺻, 2.0 = 2�� �ӵ�
    //public void SetMiningEfficiency(float multiplier)
    //{
    //    miningEfficiency = Mathf.Clamp(multiplier, 0.1f, 10f);
    //}

    //// ���� �Ŀ��� �������� ���갪
    //public void AddMiningPower(int delta)
    //{
    //    miningPower = Mathf.Max(0, miningPower + delta);
    //}

    // ����: ���� �⺻ �ӵ��� ���� ���ʽ��� �޾� ���� �ӵ� ��ȯ
    public float GetEffectiveMineSpeed(float toolBaseSpeed, float materialBonus = 0f)
    {
        // materialBonus ����: �� +0.2f(20%), �� -0.1f(-10%)
        float effective = toolBaseSpeed * miningEfficiency * (1f + materialBonus);
        return Mathf.Max(0.01f, effective);
    }

    // ����: ���� �Ŀ��� �ش� �浵�� Ķ �� �ִ���
    public bool CanBreak(int toolPower, int materialHardness)
    {
        return (toolPower + miningPower) >= materialHardness;
    }

    // ���� ������ �Ϻ�/��ü �ʱ�ȭ�� �ʿ��� �� ���
    public void ResetToOriginals()
    {
        maxStamina = originalMaxStamina;
        currentStamina = Mathf.Min(currentStamina, maxStamina);

        moveSpeed = originalMoveSpeed;
        jumpForce = originalJumpForce;
        inventorySize = Mathf.Max(1, originalInventorySize);

        miningEfficiency = Mathf.Max(0.1f, originalMiningEfficiency);
        miningPower = originalMiningPower;
    }
}
