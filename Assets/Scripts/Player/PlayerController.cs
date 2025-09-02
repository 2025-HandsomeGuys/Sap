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

    [Header("Inventory UI")]
    public GameObject inventoryPanel;
    public TextMeshProUGUI inventoryContentText;

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

    private void Awake()
    {
        // Get component references
        _rb = GetComponent<Rigidbody2D>();
        _anim = GetComponent<Animator>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _playerStats = GetComponent<PlayerStatsController>();
        originalGravityScale = _rb.gravityScale;

        // Hide UI panels at start
        if (inventoryPanel != null) inventoryPanel.SetActive(false);
    }

    private void Start()
    {
        // Initialize State Machine
        TransitionToState(GroundedState);
    }

    private void Update()
    {
        // Read Inputs if inventory is not open
        if (!IsInventoryOpen())
        {
            moveInput = Input.GetAxis("Horizontal");
            verticalInput = Input.GetAxis("Vertical");
        }

        // Toggle Inventory Input
        if (Input.GetKeyDown(KeyCode.I))
        {
            ToggleInventory();
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

    // --- Public Methods for UI/Inventory ---

    public void ToggleInventory()
    {
        bool isOpen = !inventoryPanel.activeSelf;
        inventoryPanel.SetActive(isOpen);
        Time.timeScale = isOpen ? 0f : 1f;
        if (isOpen) UpdateInventoryDisplay();
    }

    public bool IsInventoryOpen()
    {
        return inventoryPanel.activeSelf;
    }

    public void UpdateInventoryDisplay()
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
                foreach (var slot in playerInventory.items)
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

    // --- Gizmos ---
    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
