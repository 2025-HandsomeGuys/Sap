using UnityEngine;
using TMPro;
using System;

public class PlayerInteractor : MonoBehaviour
{
    [Header("Dependencies")]
    public InventoryUI inventoryUI; // Assign in inspector
    public ItemInventory itemInventory; // Assign in inspector
    public MineralInventory mineralInventory; // Assign in inspector
    public ToolInventory toolInventory; // Assign in inspector

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

        // InventoryUI 자동 찾기
        if (inventoryUI == null)
        {
            inventoryUI = FindFirstObjectByType<InventoryUI>();
            if (inventoryUI == null)
            {
                Debug.LogWarning("InventoryUI is not assigned in the PlayerInteractor inspector and could not be found automatically!");
            }
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
            
            // 아이템 타입에 따라 적절한 인벤토리에 추가
            if (itemComponent.itemData is ItemSO itemSO)
            {
                itemInventory?.AddItem(itemSO, 1);
                Debug.Log($"Collected 1 {itemSO.itemName}.");
            }
            else if (itemComponent.itemData is MineralSO mineralSO)
            {
                int quantity = 1;
                
                // 곡괭이 강화: 광물 추가 드랍률 증가 적용
                if (ToolUpgradeManager.Instance != null)
                {
                    float dropRateIncrease = ToolUpgradeManager.Instance.GetPickaxeDropRateIncrease();
                    // 드랍률 증가에 따라 추가 광물 획득 확률
                    if (dropRateIncrease > 0 && UnityEngine.Random.Range(0f, 100f) < dropRateIncrease)
                    {
                        quantity++; // 추가 광물 1개 획득
                    }
                }
                
                mineralInventory?.AddItem(mineralSO, quantity);
                Debug.Log($"Collected {quantity} {mineralSO.mineralName}.");
            }
            else if (itemComponent.itemData is ToolSO toolSO)
            {
                toolInventory?.AddItem(toolSO, 1);
                Debug.Log($"Collected 1 {toolSO.toolName}.");
            }
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