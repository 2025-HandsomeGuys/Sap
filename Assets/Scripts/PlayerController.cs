using UnityEngine;
using TMPro; // Using TextMeshPro
using System.Collections.Generic;
using System.Linq;
using static Constants;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float jumpForce = 15f;

    [Header("Ground Check Settings")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;

    [Header("Interaction Settings")]
    public TextMeshProUGUI interactionPromptText; // Drag your TextMeshPro UI element here
    public TextMeshProUGUI totalWeightText; // Drag your TextMeshPro UI element here to display total weight

    [Header("Status (Read-Only)")]
    public bool isGrounded;

    private Rigidbody2D rb;
    private float moveInput;
    private List<GameObject> collectibleGems = new List<GameObject>();
    private bool jumpRequested = false;

    public Inventory playerInventory; // Reference to the player's inventory

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        // Hide the prompt text at the start
        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        // Get horizontal input
        moveInput = Input.GetAxis("Horizontal");

        // Check if the player is on the ground
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // Handle jumping input
        if (isGrounded && (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)))
        {
            jumpRequested = true;
        }

        // Handle gem collection input
        if (Input.GetKeyDown(KeyCode.E))
        {
            CollectClosestGem();
        }
    }

    void FixedUpdate()
    {
        // Apply horizontal movement
        rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);

        // Apply jump force
        if (jumpRequested)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
            rb.AddForce(new Vector2(0f, jumpForce), ForceMode2D.Impulse);
            jumpRequested = false;
        }
    }

    public void AddCollectibleGem(GameObject gem)
    {
        if (!collectibleGems.Contains(gem))
        {
            collectibleGems.Add(gem);
            UpdateUI();
        }
    }

    public void RemoveCollectibleGem(GameObject gem)
    {
        collectibleGems.Remove(gem);
        UpdateUI();
    }

    private void CollectClosestGem()
    {
        if (collectibleGems.Count == 0) return;

        GameObject closestGem = collectibleGems
            .OrderBy(g => Vector2.Distance(this.transform.position, g.transform.position))
            .FirstOrDefault();

        if (closestGem != null)
        {
            collectibleGems.Remove(closestGem);

            Gem gemComponent = closestGem.GetComponent<Gem>();
            if (gemComponent != null && playerInventory != null)
            {
                playerInventory.AddGem(gemComponent);
            }
            
            ObjectPooler.Instance.ReturnToPool(TAG_GEM, closestGem);
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        if (interactionPromptText != null)
        {
            // Show the prompt only if there are gems nearby
            interactionPromptText.gameObject.SetActive(collectibleGems.Count > 0);
        }

        if (totalWeightText != null && playerInventory != null)
        {
            totalWeightText.text = $"Weight: {playerInventory.TotalWeight:F1}"; // Display total weight, formatted to 1 decimal place
        }
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}