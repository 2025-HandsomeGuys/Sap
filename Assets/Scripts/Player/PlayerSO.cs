using UnityEngine;

[CreateAssetMenu(fileName = "PlayerSO", menuName = "Game Data/Player SO")]
public class PlayerSO : ScriptableObject
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
    [Range(0.1f, 1f)]
    public float originalEncumberedSpeedMultiplier = 0.5f;
    public float encumberedSpeedMultiplier;

    [Header("Inventory")]
    public int originalInventorySize = 20; // 인벤토리 최소 크기
    public int inventorySize;

    [Header("Mining")]
    public float originalMiningEfficiency = 1f;
    public float miningEfficiency;
    public int originalMiningPower = 0;
    public int miningPower;

    [Header("Gold")]
    public int gold = 0;
}
