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

    public string CurrentMod = "walking";

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
        MovementModCheck();

        // Read inputs every frame
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");
        ModWalking();
        ModClimbing();
    }

    private void MovementModCheck()
    {
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            anim.SetBool("isclimbing", !anim.GetBool("isclimbing"));
            if (anim.GetBool("isclimbing") == true)
            {
                anim.SetTrigger("climb");
                CurrentMod = "climbing";
            }             
            else
                CurrentMod = "walking";
        }
    }
 
    private void ModWalking()
    {
        if(CurrentMod == "walking")
        {

            //dirctioncheck
            if (horizontalInput < 0)           
                transform.localScale = new Vector3(1, 1, 1);   
            else if (horizontalInput > 0)            
                transform.localScale = new Vector3(-1, 1, 1);
            else // <<When horizontal input is zero, face the mouse>>
            {

                Vector2 mousePos = (Vector2)cam.ScreenToWorldPoint(Input.mousePosition);
                if (mousePos.x > transform.position.x)

                    transform.localScale = new Vector3(-1, 1, 1);

                else

                    transform.localScale = new Vector3(1, 1, 1);                      
            }           
            //walk movement         
            playerRigidbody.linearVelocity = new Vector2(horizontalInput * playerStats.moveSpeed, playerRigidbody.linearVelocity.y);
            playerRigidbody.gravityScale = originalGravityScale;
            anim.SetBool("ismoving", horizontalInput != 0);

            //jump movement
            if (Input.GetKeyDown(KeyCode.Space) && !anim.GetBool("isjumping"))
            {
                playerRigidbody.AddForce(Vector3.up * playerStats.jumpForce, ForceMode2D.Impulse);
                anim.SetTrigger("jump");
                anim.SetBool("isjumping", true);
            }           

        }
    }

    private void ModClimbing()
    {

        if (CurrentMod == "climbing")
        {
            //climb movement
            playerRigidbody.linearVelocity = new Vector2(horizontalInput * playerStats.wallClimbingSpeed, verticalInput * playerStats.wallClimbingSpeed);
            playerRigidbody.gravityScale = 0f;
            anim.SetBool("isclbmoving", horizontalInput != 0 || verticalInput!=0);
            //use stamina
            if (verticalInput != 0 || horizontalInput != 0)
            {
                playerStats.UseStamina(playerStats.staminaCostPerSecond * Time.fixedDeltaTime);
            }

        
      
        }
    }
}
