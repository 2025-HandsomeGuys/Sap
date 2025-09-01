using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerStatsController))]
[RequireComponent(typeof(Inventory))]
public class PlayerController : MonoBehaviour
{
    [Header("Dependencies")]
    public InventoryUI inventoryUI; // Assign this in the Inspector

    [Header("Ground Check Settings")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;

    [Header("Status (Read-Only)")]
    public bool isGrounded;
    public bool isWallClimbing;

    private Rigidbody2D rb;
    private Animator anim;
    private Vector2 moveVector;
    private float originalGravityScale;
    private bool isInsideWallZone = false;
    private bool jumpRequested = false;
    private bool sprintHeld = false;

    private PlayerStatsController playerStats;
    private Inventory playerInventory;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        playerStats = GetComponent<PlayerStatsController>();
        playerInventory = GetComponent<Inventory>();
        originalGravityScale = rb.gravityScale;

        if (inventoryUI == null)
        {
            Debug.LogError("InventoryUI is not assigned in the inspector!", this);
        }
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        moveVector = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (context.performed && isGrounded)
        {
            if (inventoryUI != null && inventoryUI.IsOpen()) return;
            jumpRequested = true;
            anim.SetTrigger("jump");
        }
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        sprintHeld = context.ReadValueAsButton();
    }

    void Update()
    {
        if (inventoryUI != null && inventoryUI.IsOpen())
        {
            anim.SetBool("ismoving", false);
            return;
        }

        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        
        anim.SetBool("ismoving", moveVector.x != 0);
        anim.SetBool("isjumping", !isGrounded);

        if (moveVector.x > 0)
        {
            transform.localScale = new Vector3(-1, 1, 1);
        }
        else if (moveVector.x < 0)
        {
            transform.localScale = new Vector3(1, 1, 1);
        }

        HandleWallClimbing();
    }

    void FixedUpdate()
    {
        if (inventoryUI != null && inventoryUI.IsOpen())
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (isWallClimbing)
        {
            rb.gravityScale = 0f;
            float verticalVelocity = moveVector.y * playerStats.wallClimbingSpeed;
            rb.linearVelocity = new Vector2(moveVector.x * playerStats.moveSpeed, verticalVelocity);
        }
        else
        {
            rb.gravityScale = originalGravityScale;

            float currentMoveSpeed = playerStats.moveSpeed;
            if (playerInventory != null && playerInventory.IsEncumbered)
            {
                currentMoveSpeed *= playerStats.encumberedSpeedMultiplier;
            }

            rb.linearVelocity = new Vector2(moveVector.x * currentMoveSpeed, rb.linearVelocity.y);

            if (jumpRequested)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                rb.AddForce(new Vector2(0f, playerStats.jumpForce), ForceMode2D.Impulse);
                jumpRequested = false;
            }
        }
    }

    private void HandleWallClimbing()
    {
        if (playerStats == null)
        {
            Debug.LogError("PlayerStats component not found on the player object!");
            isWallClimbing = false;
            return;
        }

        if (isInsideWallZone && sprintHeld && playerStats.currentStamina > 0)
        {
            isWallClimbing = true;
        }
        else
        {
            isWallClimbing = false;
        }

        if (isWallClimbing && (moveVector.x != 0 || moveVector.y != 0))
        {
            playerStats.UseStamina(playerStats.staminaCostPerSecond * Time.deltaTime);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
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
            isWallClimbing = false;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
