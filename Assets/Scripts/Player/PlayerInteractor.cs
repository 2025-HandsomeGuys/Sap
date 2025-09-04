using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [Header("Dependencies")]
    public InventoryUI inventoryUI; // Assign in inspector
    public Inventory playerInventory; // Assign in inspector

    [Header("Interaction Settings")]
    public float collectionRadius = 1f;
    public TextMeshProUGUI interactionPromptText;

    private List<GameObject> collectibleItems = new List<GameObject>();
    private PlayerStatsController playerStats;

    void Start()
    {
        
        playerStats = GetComponent<PlayerStatsController>();

        if (inventoryUI == null)
        {
            Debug.LogError("InventoryUI is not assigned in the PlayerInteractor inspector!", this);
        }

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
        }
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            if (inventoryUI != null && inventoryUI.IsOpen())
            {
                return;
            }
            CollectClosestItem();
        }
    }

    void Update()
    {
        if (inventoryUI != null && inventoryUI.IsOpen())
        {
            if (collectibleItems.Count > 0)
            {
                collectibleItems.Clear();
                UpdateInteractionPrompt();
            }
            return;
        }

        FindCollectibleItems();
    }

    private void FindCollectibleItems()
    {
        collectibleItems.Clear();
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, collectionRadius);
        foreach (Collider2D collider in colliders)
        {
            // Check for the Mineable component instead of a specific tag
            if (collider.GetComponent<Mineable>() != null)
            {
                collectibleItems.Add(collider.gameObject);
            }
        }
        UpdateInteractionPrompt();
    }

    private void CollectClosestItem()
    {
        if (collectibleItems.Count == 0) return;

        GameObject closestItemObject = collectibleItems.OrderBy(g => Vector2.Distance(this.transform.position, g.transform.position)).FirstOrDefault();

        if (closestItemObject == null) return;

        Mineable itemComponent = closestItemObject.GetComponent<Mineable>();
        if (itemComponent == null)
        {
            Debug.LogError("Item object is missing Mineable script!", closestItemObject);
            return;
        }

        Item itemData = itemComponent.itemData;
        if (itemData == null)
        {
            Debug.LogError("Mineable script is missing ItemData! Assign it in the prefab inspector.", closestItemObject);
            return;
        }

        if (playerInventory.AddItem(itemData, 1))
        {
            if (itemData.staminaReduction > 0)
            {
                playerStats.ReduceMaxStamina(itemData.staminaReduction);
            }

            collectibleItems.Remove(closestItemObject);
            
            // Use the poolType from the Item data to return the object to the correct pool
            ObjectPooler.Instance.ReturnToPool(itemData.poolType, closestItemObject);

            UpdateInteractionPrompt();
        }
    }

    private void UpdateInteractionPrompt()
    {
        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(collectibleItems.Count > 0);
        }
    }
}
