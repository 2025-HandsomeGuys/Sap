using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [Header("Ground Check Settings")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;

    [Header("Interaction Settings")]
    public float collectionRadius = 1f;
    public TextMeshProUGUI interactionPromptText;

    [Header("Inventory UI")]
    public GameObject inventoryPanel;
    public TextMeshProUGUI inventoryContentText;

    [Header("Status (Read-Only)")]
    public bool isGrounded;
    public bool isWallClimbing;

    private Rigidbody2D rb;
    private Animator anim;
    private float moveInput;
    private float verticalInput;
    private float originalGravityScale;
    private bool isInsideWallZone = false; // Check if inside a wall zone
    private List<GameObject> collectibleMineables = new List<GameObject>(); // Renamed from collectibleGems
    private bool jumpRequested = false;
    private bool isInventoryOpen = false;

    private PlayerStats playerStats;

    public Inventory playerInventory;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        playerStats = GetComponent<PlayerStats>();
        originalGravityScale = rb.gravityScale; // Store the initial gravity scale

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }
        UpdateUI();
    }

    void Update()
    {
        if (playerStats == null)
        {
            Debug.LogError("PlayerStats became NULL during gameplay!");
            return; // Stop further execution in Update if null
        }

        if (Input.GetKeyDown(KeyCode.I))
        {
            ToggleInventory();
        }

        if (!isInventoryOpen)
        {
            moveInput = Input.GetAxis("Horizontal");
            verticalInput = Input.GetAxis("Vertical"); // Get vertical input
            anim.SetBool("ismoving", moveInput != 0);
            anim.SetBool("isjumping", !isGrounded);

            if (moveInput > 0)
            {
                transform.localScale = new Vector3(-1, 1, 1);
            }
            else if (moveInput < 0)
            {
                transform.localScale = new Vector3(1, 1, 1);
            }

            isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

            if (isGrounded && Input.GetKeyDown(KeyCode.Space))
            {
                jumpRequested = true;
                anim.SetTrigger("jump");
            }

            if (playerStats == null)
            {
                Debug.LogError("PlayerStats component not found on the player object!");
                return;
            }

            // If stamina is available, inside a wall zone, and LeftShift is pressed, enable wall climbing
            if (isInsideWallZone && Input.GetKey(KeyCode.LeftShift) && playerStats.currentStamina > 0)
            {
                isWallClimbing = true;
            }
            else
            {
                isWallClimbing = false;
            }

            if (isWallClimbing && (moveInput != 0 || verticalInput != 0))
            {
                playerStats.UseStamina(playerStats.staminaCostPerSecond * Time.deltaTime);
            }

            // TODO: Uncomment if you have an "isClimbing" animation parameter
            // anim.SetBool("isClimbing", isWallClimbing);

            if (Input.GetKeyDown(KeyCode.E))
            {
                CollectClosestMineable(); // Renamed from CollectClosestGem
            }

            FindCollectibleMineables(); // Renamed from FindCollectibleGems
        }
    }

    void FixedUpdate()
    {
        if (playerStats == null)
        {
            return;
        }
        if (isInventoryOpen)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (isWallClimbing)
        {
            // When wall climbing: gravity is 0, handle vertical/horizontal movement
            rb.gravityScale = 0f;
            float verticalVelocity = verticalInput * playerStats.wallClimbingSpeed;
            rb.linearVelocity = new Vector2(moveInput * playerStats.moveSpeed, verticalVelocity);
        }
        else
        {
            // Normal state: apply original gravity, handle normal movement and jump
            rb.gravityScale = originalGravityScale;

            float currentMoveSpeed = playerStats.moveSpeed;
            if (playerInventory != null && playerInventory.IsEncumbered)
            {
                currentMoveSpeed *= playerStats.encumberedSpeedMultiplier;
            }

            rb.linearVelocity = new Vector2(moveInput * currentMoveSpeed, rb.linearVelocity.y);

            if (jumpRequested)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                rb.AddForce(new Vector2(0f, playerStats.jumpForce), ForceMode2D.Impulse);
                // SoundManager.Instance.PlaySound("Jump"); // Play jump sound
                jumpRequested = false;
            }
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // Check if the object's tag to be used as a wall is "Wall"
        if (other.CompareTag("Wall"))
        {
            isInsideWallZone = true;
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Wall"))
        {
            isInsideWallZone = false;
            isWallClimbing = false; // Immediately disable wall climbing upon exiting the wall zone
        }
    }

    private void FindCollectibleMineables() // Renamed from FindCollectibleGems
    {
        collectibleMineables.Clear();
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, collectionRadius);
        foreach (Collider2D collider in colliders)
        {
            // Check for the Mineable component instead of a tag for more robustness
            if (collider.GetComponent<Mineable>() != null)
            {
                collectibleMineables.Add(collider.gameObject);
            }
        }
        UpdateUI();
    }

    private void ToggleInventory()
    {
        if (inventoryPanel != null)
        {
            isInventoryOpen = !isInventoryOpen;
            inventoryPanel.SetActive(isInventoryOpen);

            if (isInventoryOpen)
            {
                Time.timeScale = 0f;
                UpdateInventoryDisplay();
            }
            else
            {
                Time.timeScale = 1f;
            }
        }
    }

    public bool IsInventoryOpen()
    {
        return isInventoryOpen;
    }

    private void UpdateInventoryDisplay()
    {
        if (inventoryContentText != null && playerInventory != null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"Weight: {playerInventory.TotalWeight:F1} / {playerInventory.maxWeightLimit:F1} ");
            sb.AppendLine("-----------------");

            if (playerInventory.items.Count == 0)
            {
                sb.AppendLine("Empty");
            }
            else
            {
                foreach (InventorySlot slot in playerInventory.items)
                {
                    if (slot.item != null)
                    {
                        sb.AppendLine($"- {slot.item.itemName} x{slot.quantity} ({(slot.item.weight * slot.quantity):F1} )");
                    }
                }
            }
            inventoryContentText.text = sb.ToString();
        }
    }

    private void CollectClosestMineable() // Renamed from CollectClosestGem
    {
        if (collectibleMineables.Count == 0) return;

        GameObject closestMineableObject = collectibleMineables.OrderBy(g => Vector2.Distance(this.transform.position, g.transform.position)).FirstOrDefault();

        if (closestMineableObject == null) return;

        Mineable mineableComponent = closestMineableObject.GetComponent<Mineable>();
        if (mineableComponent == null)
        {
            Debug.LogError("Mineable object is missing Mineable script!", closestMineableObject);
            return;
        }

        if (mineableComponent.itemData == null)
        {
            Debug.LogError("Mineable script is missing ItemData! Check the prefab and database.", closestMineableObject);
            return;
        }

        if (playerInventory.AddItem(mineableComponent.itemData, 1))
        {
            //SoundManager.Instance.PlaySound("CollectGem"); // Consider making this sound generic

            // If item acquisition is successful, execute stamina reduction logic
            if (mineableComponent.itemData.staminaReduction > 0)
            {
                playerStats.ReduceMaxStamina(mineableComponent.itemData.staminaReduction);
            }

            collectibleMineables.Remove(closestMineableObject);
            // Return the object to the pool using the poolType defined in its Item data
            ObjectPooler.Instance.ReturnToPool(mineableComponent.itemData.poolType, closestMineableObject);
            UpdateUI();
            UpdateInventoryDisplay();
        }
        else
        {
            Debug.Log("Could not add item to inventory. Overweight or full.");
        }
    }

    private void UpdateUI()
    {
        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(collectibleMineables.Count > 0);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
