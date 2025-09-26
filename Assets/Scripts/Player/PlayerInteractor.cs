using UnityEngine;
using TMPro;

public class PlayerInteractor : MonoBehaviour
{
    [Header("Dependencies")]
    public InventoryUI inventoryUI; // Assign in inspector
    public Inventory playerInventory; // Assign in inspector

    [Header("Interaction Settings")]
    public float collectionRadius = 1f;
    public TextMeshProUGUI interactionPromptText;

    // Optimization: Timer to limit how often we check for items.
    private float _checkTimer;
    private const float CHECK_INTERVAL = 0.2f; // Check 5 times per second

    // Optimization: Cache for NonAlloc physics calls to prevent GC allocation.
    private readonly Collider2D[] _colliderCache = new Collider2D[16]; // Max 16 items detected at once
    private Mineable _closestItem; // Cache the component directly
    private PlayerStatsController playerStats;

    void Start()
    {
        playerStats = GetComponent<PlayerStatsController>();

        if (inventoryUI == null)
        {
            Debug.LogWarning("InventoryUI is not assigned in the PlayerInteractor inspector!");
        }

        if (playerInventory == null)
        {
            Debug.LogError("PlayerInventory is not assigned in the PlayerInteractor inspector! Interactions will fail.", this);
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        // If inventory is open, hide prompt and don't allow interaction
        if (inventoryUI != null && inventoryUI.IsOpen())
        {
            if (_closestItem != null)
            {
                _closestItem = null;
                UpdateInteractionPrompt();
            }
            return;
        }

        // Timer-based check for performance
        _checkTimer += Time.deltaTime;
        if (_checkTimer >= CHECK_INTERVAL)
        {
            _checkTimer = 0f;

            Mineable previousClosestItem = _closestItem;
            FindClosestCollectibleItem();

            if (previousClosestItem != _closestItem) // Only update UI if the state changes
            {
                UpdateInteractionPrompt();
            }
        }

        // Check for 'E' key press to collect the closest item found
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (_closestItem != null)
            {
                CollectItem(_closestItem);
            }
        }
    }

    private void FindClosestCollectibleItem()
    {
        _closestItem = null;
        float closestDistSqr = float.MaxValue;

        int hitCount = Physics2D.OverlapCircleNonAlloc(transform.position, collectionRadius, _colliderCache);

        for (int i = 0; i < hitCount; i++)
        {
            // Use TryGetComponent to avoid double lookups
            if (_colliderCache[i].TryGetComponent(out Mineable mineable))
            {
                float distSqr = (transform.position - mineable.transform.position).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    _closestItem = mineable;
                }
            }
        }
    }

    private void CollectItem(Mineable itemComponent)
    {
        if (itemComponent == null) return;

        // if (playerInventory == null) return;

        // ItemSO itemData = itemComponent.itemData;
        // if (itemData == null)
        // {
        //     Debug.LogError("Mineable script is missing ItemData! Assign it in the prefab inspector.", itemComponent.gameObject);
        //     return;
        // }

        // if (playerInventory.AddItem(itemData, 1))
        // {
            // if (itemData.staminaReduction > 0 && playerStats != null)
            // {
            //     playerStats.ReduceMaxStamina(itemData.staminaReduction);
            // }

            GameObject itemObject = itemComponent.gameObject;

            // The collected item is no longer the closest one
            if(itemComponent == _closestItem) 
            {
                _closestItem = null;
            }
            
            ObjectPooler.Instance.ReturnToPool(itemObject);

            // We collected an item, so let's immediately re-check for the next closest one
            FindClosestCollectibleItem();
            UpdateInteractionPrompt();
        // }
    }

    private void UpdateInteractionPrompt()
    {
        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(_closestItem != null);
        }
    }
}