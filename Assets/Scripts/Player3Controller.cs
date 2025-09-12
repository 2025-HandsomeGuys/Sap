using UnityEngine;

public class Player3Controller : MonoBehaviour, IPlayerController
{
    [Header("Movement")]
    public Rigidbody2D playerRigidbody;

    // Components
    private Animator anim;
    private Camera cam;
    private PlayerStatsController playerStats; // For stamina and other stats

    // State Tracking
    private float horizontalInput;
    private float verticalInput; // Added for animation purposes
    private float originalGravityScale;
    private bool isInsideWallZone = false;
    public bool isWallClimbing = false;

    // Interface property implementation
    public bool IsWallClimbing => isWallClimbing;


    void Awake()
    {
        anim = GetComponent<Animator>();
        playerStats = GetComponent<PlayerStatsController>();
    }

    void Start()
    {
        cam = Camera.main;
        if (playerRigidbody != null)
        {
            originalGravityScale = playerRigidbody.gravityScale;
        }

        if (playerStats == null)
        {
            Debug.LogError("PlayerStatsController not found on Player! Stats-driven values will not work.");
        }
    }

    // --- Trigger Detection for Ground and Walls ---
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Wall"))
        {
            isInsideWallZone = true;
        }
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!other.CompareTag("Wall"))
        {
            anim.SetBool("isjumping", false);
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


    void Update()
    {
        // Read inputs every frame
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        HandleSpriteFlippingAndAnimation();
        HandleWallClimbingState();

        if (!isWallClimbing)
        {
            HandleJumping();
        }
    }

    private void HandleSpriteFlippingAndAnimation()
    {
        // --- Sprite Flipping (runs in all states) ---
        if (horizontalInput < 0)
        {
            transform.localScale = new Vector3(1, 1, 1);
        }
        else if (horizontalInput > 0)
        {
            transform.localScale = new Vector3(-1, 1, 1);
        }
        else // When horizontal input is zero
        {
            // If not on a wall, face the mouse
            if (!isWallClimbing && cam != null)
            {
                Vector2 mousePos = (Vector2)cam.ScreenToWorldPoint(Input.mousePosition);
                if (mousePos.x > transform.position.x)
                    transform.localScale = new Vector3(-1, 1, 1);
                else
                    transform.localScale = new Vector3(1, 1, 1);
            }
        }

        // --- Animation ---
        if (isWallClimbing)
        {
            // On a wall, moving is based on any input
            anim.SetBool("ismoving", horizontalInput != 0 || verticalInput != 0);
        }
        else
        {
            // On the ground, moving is based on horizontal input only
            anim.SetBool("ismoving", horizontalInput != 0);
        }
    }

    private void HandleWallClimbingState()
    {
        if (playerStats == null) return;

        if (isWallClimbing && (!Input.GetKey(KeyCode.LeftShift) || playerStats.currentStamina <= 0))
        {
            isWallClimbing = false;
        }
        else if (!isWallClimbing && isInsideWallZone && Input.GetKey(KeyCode.LeftShift) && playerStats.currentStamina > 0)
        {
            isWallClimbing = true;
        }
    }

    private void HandleJumping()
    {
        if (playerStats == null) return;

        if (Input.GetKeyDown(KeyCode.Space) && !anim.GetBool("isjumping"))
        {
            playerRigidbody.AddForce(Vector3.up * playerStats.jumpForce, ForceMode2D.Impulse);
            anim.SetTrigger("jump");
            anim.SetBool("isjumping", true);
        }
    }

    void FixedUpdate()
    {
        if (isWallClimbing)
        {
            HandleWallClimbingMovement();
        }
        else
        {
            HandleNormalMovementPhysics();
        }
    }

    private void HandleWallClimbingMovement()
    {
        if (playerStats == null) return;

        playerRigidbody.linearVelocity = new Vector2(horizontalInput * playerStats.moveSpeed, verticalInput * playerStats.wallClimbingSpeed);
        playerRigidbody.gravityScale = 0f;

        if (verticalInput != 0 || horizontalInput != 0)
        {
            playerStats.UseStamina(playerStats.staminaCostPerSecond * Time.fixedDeltaTime);
        }
    }

    private void HandleNormalMovementPhysics()
    {
        if (playerStats == null) return;

        playerRigidbody.linearVelocity = new Vector2(horizontalInput * playerStats.moveSpeed, playerRigidbody.linearVelocity.y);
        playerRigidbody.gravityScale = originalGravityScale;
    }
}
