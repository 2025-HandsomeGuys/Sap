using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using static Constants;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float jumpForce = 15f;
    [Range(0.1f, 1f)]
    public float encumberedSpeedMultiplier = 0.5f; // 과적 상태일 때의 속도 배율

    [Header("Ground Check Settings")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;

    [Header("Interaction Settings")]
    public TextMeshProUGUI interactionPromptText;
    public TextMeshProUGUI totalWeightText;

    [Header("Inventory UI")]
    public GameObject inventoryPanel;
    public TextMeshProUGUI inventoryContentText;

    [Header("Status (Read-Only)")]
    public bool isGrounded;

    private Rigidbody2D rb;
    private Animator anim;
    private float moveInput;
    private List<GameObject> collectibleGems = new List<GameObject>();
    private bool jumpRequested = false;
    private bool isInventoryOpen = false;

    public Inventory playerInventory;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
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
        if (Input.GetKeyDown(KeyCode.I))
        {
            ToggleInventory();
        }

        if (!isInventoryOpen)
        {
            moveInput = Input.GetAxis("Horizontal");
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
            sb.AppendLine($"Weight: {playerInventory.TotalWeight:F1} / {playerInventory.maxWeightLimit:F1} kg"); // maxWeightLimit 사용
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
                        sb.AppendLine($"- {slot.item.itemName} x{slot.quantity} ({(slot.item.weight * slot.quantity):F1} kg)");
                    }
                }
            }
            inventoryContentText.text = sb.ToString();
        }
    }

    void FixedUpdate()
    {
        if (isInventoryOpen)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        float currentMoveSpeed = moveSpeed;
        if (playerInventory != null && playerInventory.IsEncumbered)
        {
            currentMoveSpeed *= encumberedSpeedMultiplier;
        }

        rb.linearVelocity = new Vector2(moveInput * currentMoveSpeed, rb.linearVelocity.y);

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
        if (collectibleGems.Count == 0) return; // No need for a log here, it's normal.

        GameObject closestGemObject = collectibleGems.OrderBy(g => Vector2.Distance(this.transform.position, g.transform.position)).FirstOrDefault();

        if (closestGemObject == null) return;

        Gem gemComponent = closestGemObject.GetComponent<Gem>();
        if (gemComponent == null)
        {
            Debug.LogError("Gem object is missing Gem script!");
            return;
        }

        if (gemComponent.itemData == null)
        {
            Debug.LogError("Gem script is missing ItemData! Assign it in the prefab inspector.");
            return;
        }

        if (playerInventory.AddItem(gemComponent.itemData, 1))
        {
            collectibleGems.Remove(closestGemObject);
            ObjectPooler.Instance.ReturnToPool(TAG_GEM, closestGemObject);
            UpdateUI();
            UpdateInventoryDisplay(); // 아이템 목록 갱신 추가
        }
        else
        {
            Debug.Log("Could not add gem to inventory. Overweight or full.");
        }
    }

    private void UpdateUI()
    {
        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(collectibleGems.Count > 0);
        }

        if (totalWeightText != null && playerInventory != null)
        {
            totalWeightText.text = $"Weight: {playerInventory.TotalWeight:F1} / {playerInventory.maxWeightLimit:F1} kg";
        }
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}