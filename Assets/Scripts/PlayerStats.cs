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
    public int originalInventorySize = 20; // 인벤토리 증가는 없을 것 같음
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
        miningEfficiency = Mathf.Max(0.1f, originalMiningEfficiency); // 배율 하한
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
        Debug.Log("골드 부족!");
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
            Debug.Log($"MaxStamina 업그레이드! 현재: {maxStamina}");
        }
    }

    public void UpgradeMoveSpeed(int cost, float increaseAmount)
    {
        if (SpendGold(cost))
        {
            originalMoveSpeed += increaseAmount;
            moveSpeed = originalMoveSpeed;
            Debug.Log($"MoveSpeed 업그레이드! 현재: {moveSpeed}");
        }
    }

    public void UpgradeJumpForce(int cost, float increaseAmount)
    {
        if (SpendGold(cost))
        {
            originalJumpForce += increaseAmount;
            jumpForce = originalJumpForce;
            Debug.Log($"jumpForce 업그레이드! 현재: {jumpForce}");
        }
    }

    public void UpgradeMiningEfficiency(int cost, float increaseAmount)
    {
        if (SpendGold(cost))
        {
            originalMiningEfficiency = Mathf.Clamp(miningEfficiency + increaseAmount, 0.1f, 10f);
            miningEfficiency = originalMiningEfficiency;
            Debug.Log($"MiningEfficiency 업그레이드! 현재: {miningEfficiency}");
        }
    }

    public void UpgradeMiningPower(int cost, int increaseAmount)
    {
        if (SpendGold(cost))
        {
            originalMiningPower += increaseAmount;
            miningPower = originalMiningPower;
            Debug.Log($"MiningPower 업그레이드! 현재: {miningPower}");
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
    //// 배율(곱)로 관리: 1.0 = 기본, 2.0 = 2배 속도
    //public void SetMiningEfficiency(float multiplier)
    //{
    //    miningEfficiency = Mathf.Clamp(multiplier, 0.1f, 10f);
    //}

    //// 도구 파워에 더해지는 가산값
    //public void AddMiningPower(int delta)
    //{
    //    miningPower = Mathf.Max(0, miningPower + delta);
    //}

    // 헬퍼: 도구 기본 속도와 소재 보너스를 받아 실제 속도 반환
    public float GetEffectiveMineSpeed(float toolBaseSpeed, float materialBonus = 0f)
    {
        // materialBonus 예시: 흙 +0.2f(20%), 돌 -0.1f(-10%)
        float effective = toolBaseSpeed * miningEfficiency * (1f + materialBonus);
        return Mathf.Max(0.01f, effective);
    }

    // 헬퍼: 현재 파워로 해당 경도를 캘 수 있는지
    public bool CanBreak(int toolPower, int materialHardness)
    {
        return (toolPower + miningPower) >= materialHardness;
    }

    // 원본 값으로 일부/전체 초기화가 필요할 때 사용
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
