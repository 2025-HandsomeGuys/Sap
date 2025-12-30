using UnityEngine;
using System;

public class PlayerStatsController : MonoBehaviour
{
    // 골드 변경 이벤트
    public event Action<int> OnGoldChanged;
    [Header("Stamina")]
    public float originalMaxStamina;
    public float maxStamina;
    public float currentStamina;
    public float originalStaminaCostPerSecond;
    public float staminaCostPerSecond;

    [Header("Movement")]
    public float originalMoveSpeed;
    public float moveSpeed;
    public float originalJumpForce;
    public float jumpForce;
    public float originalWallClimbingSpeed;
    public float wallClimbingSpeed;
    [Range(0.1f, 1f)]
    public float originalEncumberedSpeedMultiplier;
    public float encumberedSpeedMultiplier;

    [Header("Inventory")]
    public int originalInventorySize; // 인벤토리 크기를 위한 최소 크기
    public int inventorySize;
    public InventoryData inventory = new InventoryData();

    [Header("Mining")]
    public float originalMiningEfficiency = 1f;
    public float miningEfficiency;

    public int originalMiningPower;
    public int miningPower;

    [Header("Gold")]
    public int gold;

    // void Awake()
    // {
    //     // Stamina
    //     maxStamina = originalMaxStamina;
    //     currentStamina = maxStamina;
    //     staminaCostPerSecond = originalStaminaCostPerSecond;

    //     // Movement
    //     moveSpeed = originalMoveSpeed;
    //     jumpForce = originalJumpForce;
    //     wallClimbingSpeed = originalWallClimbingSpeed;
    //     encumberedSpeedMultiplier = originalEncumberedSpeedMultiplier;

    //     // Inventory
    //     inventorySize = Mathf.Max(1, originalInventorySize);

    //     // Mining
    //     miningEfficiency = Mathf.Max(0.1f, originalMiningEfficiency); // 최소 향상
    //     miningPower = originalMiningPower;
    // }

    // ===================================================
    // 🔹 PlayerData 변환 기능
    // ===================================================
    public PlayerData ToData()
    {
        return new PlayerData()
        {
            originalMaxStamina = originalMaxStamina,
            maxStamina = maxStamina,
            currentStamina = currentStamina,
            originalStaminaCostPerSecond = originalStaminaCostPerSecond,
            staminaCostPerSecond = staminaCostPerSecond,

            originalMoveSpeed = originalMoveSpeed,
            moveSpeed = moveSpeed,
            originalJumpForce = originalJumpForce,
            jumpForce = jumpForce,
            originalWallClimbingSpeed = originalWallClimbingSpeed,
            wallClimbingSpeed = wallClimbingSpeed,
            originalEncumberedSpeedMultiplier = originalEncumberedSpeedMultiplier,
            encumberedSpeedMultiplier = encumberedSpeedMultiplier,

            originalInventorySize = originalInventorySize,
            inventorySize = inventorySize,

            originalMiningEfficiency = originalMiningEfficiency,
            miningEfficiency = miningEfficiency,
            originalMiningPower = originalMiningPower,
            miningPower = miningPower,

            gold = gold
        };
    }

    public void FromData(PlayerData data)
    {
        originalMaxStamina = data.originalMaxStamina;
        maxStamina = data.maxStamina;
        currentStamina = data.currentStamina;
        originalStaminaCostPerSecond = data.originalStaminaCostPerSecond;
        staminaCostPerSecond = data.staminaCostPerSecond;

        originalMoveSpeed = data.originalMoveSpeed;
        moveSpeed = data.moveSpeed;
        originalJumpForce = data.originalJumpForce;
        jumpForce = data.jumpForce;
        originalWallClimbingSpeed = data.originalWallClimbingSpeed;
        wallClimbingSpeed = data.wallClimbingSpeed;
        originalEncumberedSpeedMultiplier = data.originalEncumberedSpeedMultiplier;
        encumberedSpeedMultiplier = data.encumberedSpeedMultiplier;

        originalInventorySize = data.originalInventorySize;
        inventorySize = data.inventorySize;

        originalMiningEfficiency = data.originalMiningEfficiency;
        miningEfficiency = data.miningEfficiency;
        originalMiningPower = data.originalMiningPower;
        miningPower = data.miningPower;

        gold = data.gold;
    }

    // ------------ Gold ------------
    public void AddGold(int amount)
    {
        gold += Mathf.Max(0, amount);
        OnGoldChanged?.Invoke(gold);
    }

    public bool SpendGold(int amount)
    {
        if (gold >= amount)
        {
            gold -= amount;
            OnGoldChanged?.Invoke(gold);
            return true;
        }
        Debug.Log("금화 부족!");
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

    // 사용 예시: 공격 향상 속도
    public float GetEffectiveMineSpeed(float toolBaseSpeed, float materialBonus = 0f)
    {
        // materialBonus 예시: 다이아 +0.2f(20%), 돌 -0.1f(-10%)
        float effective = toolBaseSpeed * miningEfficiency * (1f + materialBonus);
        return Mathf.Max(0.01f, effective);
    }

    // 사용 예시: 공격 파워가 해당 경도를 꺨 수 있는지
    public bool CanBreak(int toolPower, int materialHardness)
    {
        return (toolPower + miningPower) >= materialHardness;
    }

    // 상태 이상이 발생하거나 전체 초기화가 필요할 때 사용
    public void ResetToOriginals()
    {
        maxStamina = originalMaxStamina;
        currentStamina = Mathf.Min(currentStamina, maxStamina);
        staminaCostPerSecond = originalStaminaCostPerSecond;

        moveSpeed = originalMoveSpeed;
    }
}