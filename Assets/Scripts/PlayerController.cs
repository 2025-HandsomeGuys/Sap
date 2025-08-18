using UnityEngine;
using TMPro; // Using TextMeshPro
using System.Collections.Generic;
using System.Linq;
using static Constants;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))] // Animator 컴포넌트도 필수로 요구
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

    [Header("Inventory UI")]
    public GameObject inventoryPanel;
    public TextMeshProUGUI inventoryContentText;

    [Header("Status (Read-Only)")]
    public bool isGrounded;

    private Rigidbody2D rb;
    private Animator anim; // 애니메이터 변수 추가
    private float moveInput;
    private List<GameObject> collectibleGems = new List<GameObject>();
    private bool jumpRequested = false;
    private bool isInventoryOpen = false;

    public Inventory playerInventory; // Reference to the player's inventory

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>(); // Animator 컴포넌트 가져오기
        // Hide the prompt text at the start
        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }
        // Hide inventory panel at start
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }
        UpdateUI(); // Initial UI update
    }

    void Update()
    {
        // Handle inventory toggle input
        if (Input.GetKeyDown(KeyCode.I)) // 'I' for Inventory
        {
            ToggleInventory();
        }

        if (!isInventoryOpen) // Only process player input if inventory is NOT open
        {
            // Get horizontal input
            moveInput = Input.GetAxis("Horizontal");

            // 애니메이션 파라미터 설정
            anim.SetBool("ismoving", moveInput != 0);
            anim.SetBool("isjumping", !isGrounded);

            // 이동 방향에 따라 캐릭터 뒤집기
            if (moveInput > 0)
            {
                transform.localScale = new Vector3(-1, 1, 1);
            }
            else if (moveInput < 0)
            {
                transform.localScale = new Vector3(1, 1, 1);
            }

            // Check if the player is on the ground
            isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

            // Handle jumping input (스페이스 바로 변경)
            if (isGrounded && Input.GetKeyDown(KeyCode.Space))
            {
                jumpRequested = true;
                anim.SetTrigger("jump"); // 점프 애니메이션 트리거
            }

            // Handle gem collection input
            if (Input.GetKeyDown(KeyCode.E))
            {
                CollectClosestGem();
            }
        }
    }

    private void ToggleInventory()
    {
        if (inventoryPanel != null)
        {
            isInventoryOpen = !isInventoryOpen; // Toggle the flag
            inventoryPanel.SetActive(isInventoryOpen); // Set panel active state

            if (isInventoryOpen) // If opening inventory
            {
                Time.timeScale = 0f; // Pause game
                UpdateInventoryDisplay(); // Update content when opened
            }
            else // If closing inventory
            {
                Time.timeScale = 1f; // Resume game
            }
        }
    }

    private void UpdateInventoryDisplay()
    {
        if (inventoryContentText != null && playerInventory != null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("--- Inventory ---");
            if (playerInventory.collectedGems.Count == 0)
            {
                sb.AppendLine("Empty");
            }
            else
            {
                foreach (Gem gem in playerInventory.collectedGems)
                {
                    if (gem != null)
                    {
                        Debug.Log($"Inventory item: {gem.name} (Weight: {gem.weight:F1})"); // Add this line
                        sb.AppendLine($"- {gem.name} (Weight: {gem.weight:F1})");
                    }
                }
            }
            sb.AppendLine($"Total Weight: {playerInventory.TotalWeight:F1}");
            inventoryContentText.text = sb.ToString();
        }
    }

    void FixedUpdate()
    {
        if (isInventoryOpen) // If inventory is open, prevent movement
        {
            rb.linearVelocity = Vector2.zero; // Stop all movement
            return;
        }

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