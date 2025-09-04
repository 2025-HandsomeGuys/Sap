using UnityEngine;
using TMPro;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerController : MonoBehaviour
{
    // Component References (assigned in Awake)
    private Rigidbody2D _rb;
    private Animator _anim;
    private SpriteRenderer _spriteRenderer;
    private PlayerStatsController _playerStats;

    // Public properties for states to access components
    public Rigidbody2D RB => _rb;
    public Animator Anim => _anim;
    public SpriteRenderer SpriteRenderer => _spriteRenderer;
    public PlayerStatsController PlayerStats => _playerStats;


    [Header("Ground Check Settings")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;

    

    [Header("Status (Read-Only)")]
    public string currentStateName;
    public bool isGrounded;
    public bool isInsideWallZone;

    // State Machine
    private PlayerBaseState _currentState;
    public readonly GroundedState GroundedState = new GroundedState();
    public readonly JumpingState JumpingState = new JumpingState();
    public readonly WallClimbingState WallClimbingState = new WallClimbingState();

    // Shared State Data
    public float moveInput;
    public float verticalInput;
    public float originalGravityScale;

    // Public Inventory Reference
    public Inventory playerInventory;
    public InventoryUI inventoryUI;

    private void Awake()
    {
        // Get component references
        _rb = GetComponent<Rigidbody2D>();
        _anim = GetComponent<Animator>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _playerStats = GetComponent<PlayerStatsController>();
        originalGravityScale = _rb.gravityScale;

        
    }

    private void Start()
    {
        // Initialize State Machine
        TransitionToState(GroundedState);
    }

    private void Update()
    {
        // Read Inputs if inventory is not open
        if (inventoryUI == null || !inventoryUI.IsOpen())
        {
            moveInput = Input.GetAxis("Horizontal");
            verticalInput = Input.GetAxis("Vertical");
        }

        _currentState.UpdateState(this);
    }

    private void FixedUpdate()
    {
        _currentState.FixedUpdateState(this);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        _currentState.OnTriggerEnter2D(this, other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        _currentState.OnTriggerExit2D(this, other);
    }

    public void TransitionToState(PlayerBaseState state)
    {
        _currentState = state;
        _currentState.EnterState(this);
        currentStateName = state.GetType().Name;
    }

    

    // --- Gizmos ---
    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
