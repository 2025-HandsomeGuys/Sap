using System;
using UnityEngine;

[Serializable]
public class PlayerData
{
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
    public float originalEncumberedSpeedMultiplier;
    public float encumberedSpeedMultiplier;

    [Header("Inventory")]
    public int originalInventorySize;
    public int inventorySize;
    public ItemInventoryData itemInventory = new ItemInventoryData();
    public MineralInventoryData mineralInventory = new MineralInventoryData();
    public ToolInventoryData toolInventory = new ToolInventoryData();

    [Header("Mining")]
    public float originalMiningEfficiency;
    public float miningEfficiency;
    public int originalMiningPower;
    public int miningPower;

    [Header("Gold")]
    public int gold;

    [Header("Upgrade")]
    public ToolUpgradeInventoryData toolUpgradeData = new ToolUpgradeInventoryData(); // 도구 강화 데이터

    // 기본값을 SO에서 가져와서 초기화
    public PlayerData() { }

    public PlayerData(PlayerSO playerSO)
    {
        // Stamina
        originalMaxStamina = playerSO.originalMaxStamina;
        maxStamina = playerSO.maxStamina;
        currentStamina = playerSO.currentStamina;
        originalStaminaCostPerSecond = playerSO.originalStaminaCostPerSecond;
        staminaCostPerSecond = playerSO.staminaCostPerSecond;

        // Movement
        originalMoveSpeed = playerSO.originalMoveSpeed;
        moveSpeed = playerSO.moveSpeed;
        originalJumpForce = playerSO.originalJumpForce;
        jumpForce = playerSO.jumpForce;
        originalWallClimbingSpeed = playerSO.originalWallClimbingSpeed;
        wallClimbingSpeed = playerSO.wallClimbingSpeed;
        originalEncumberedSpeedMultiplier = playerSO.originalEncumberedSpeedMultiplier;
        encumberedSpeedMultiplier = playerSO.encumberedSpeedMultiplier;

        // Inventory
        originalInventorySize = playerSO.originalInventorySize;
        inventorySize = playerSO.inventorySize;
        itemInventory = new ItemInventoryData();
        mineralInventory = new MineralInventoryData();
        toolInventory = new ToolInventoryData();

        // Mining
        originalMiningEfficiency = playerSO.originalMiningEfficiency;
        miningEfficiency = playerSO.miningEfficiency;
        originalMiningPower = playerSO.originalMiningPower;
        miningPower = playerSO.miningPower;

        // Gold
        gold = playerSO.gold;

        // Tool Upgrade
        toolUpgradeData = new ToolUpgradeInventoryData();
    }
}
