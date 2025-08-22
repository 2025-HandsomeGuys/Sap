using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using static Constants;

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
    private bool isInsideWallZone = false; // 벽 영역 안에 있는지 확인
    private List<GameObject> collectibleGems = new List<GameObject>();
    private bool jumpRequested = false;
    private bool isInventoryOpen = false;

    private PlayerStats playerStats;

    public Inventory playerInventory;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        playerStats = GetComponent<PlayerStats>();
        originalGravityScale = rb.gravityScale; // 초기 중력 값 저장

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
            verticalInput = Input.GetAxis("Vertical"); // 수직 입력 받기
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

            // 스태미나가 있고, 벽 구역 안에 있고, LeftShift를 누르면 벽 타기 활성화
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

            // TODO: "isClimbing" 애니메이션 파라미터가 있다면 주석 해제
            // anim.SetBool("isClimbing", isWallClimbing);

            if (Input.GetKeyDown(KeyCode.E))
            {
                CollectClosestGem();
            }

            FindCollectibleGems();
        }
    }

    void FixedUpdate()
    {
        if (isInventoryOpen)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (isWallClimbing)
        {
            // 벽 타기 상태일 때: 중력 0, 수직/수평 이동 처리
            rb.gravityScale = 0f;
            float verticalVelocity = verticalInput * playerStats.wallClimbingSpeed;
            rb.linearVelocity = new Vector2(moveInput * playerStats.moveSpeed, verticalVelocity);
        }
        else
        {
            // 평상시 상태일 때: 원래 중력 적용, 일반 이동 및 점프 처리
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
                jumpRequested = false;
            }
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // 벽으로 사용할 오브젝트의 Tag가 "Wall"인지 확인
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
            isWallClimbing = false; // 벽 영역을 나가면 즉시 벽 타기 상태 해제
        }
    }

    private void FindCollectibleGems()
    {
        collectibleGems.Clear();
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, collectionRadius);
        foreach (Collider2D collider in colliders)
        {
            if (collider.CompareTag(TAG_GEM))
            {
                collectibleGems.Add(collider.gameObject);
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

    private void CollectClosestGem()
    {
        if (collectibleGems.Count == 0) return;

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
            // 아이템 획득에 성공하면 스태미나 감소 로직 실행
            if (gemComponent.itemData.staminaReduction > 0)
            {
                playerStats.ReduceMaxStamina(gemComponent.itemData.staminaReduction);
            }

            collectibleGems.Remove(closestGemObject);
            ObjectPooler.Instance.ReturnToPool(TAG_GEM, closestGemObject);
            UpdateUI();
            UpdateInventoryDisplay();
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
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
