using UnityEngine;

[CreateAssetMenu(fileName = "PlayerSO", menuName = "Game Data/Player SO")]
public class PlayerSO : ScriptableObject
{
    [Header("Stamina")]
    public float maxStamina = 500f;
    public float currentStamina;
    public float staminaCostPerSecond = 10f;

    [Header("Movement")]
    public float moveSpeed = 2f;
    public float jumpForce = 5f;
    public float wallClimbingSpeed = 2f;
    [Range(0.1f, 1f)]
    public float encumberedSpeedMultiplier = 0.5f;

    [Header("Mining")]
    public int miningLevel = 0;
    public float miningRange = 0.5f;
    public float miningCooldown = 0.05f;

    [Header("Gold")]
    public int gold = 0;
}
