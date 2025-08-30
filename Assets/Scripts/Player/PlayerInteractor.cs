using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;
using static Constants;

public class PlayerInteractor : MonoBehaviour
{
    [Header("Dependencies")]
    public InventoryUI inventoryUI; // Assign in inspector

    [Header("Interaction Settings")]
    public float collectionRadius = 1f;
    public TextMeshProUGUI interactionPromptText;

    private List<GameObject> collectibleGems = new List<GameObject>();
    private Inventory playerInventory;
    private PlayerStatsController playerStats;

    void Start()
    {
        playerInventory = GetComponent<Inventory>();
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
        if (context.performed)
        {
            if (inventoryUI != null && inventoryUI.IsOpen())
            {
                return;
            }
            CollectClosestGem();
        }
    }

    void Update()
    {
        if (inventoryUI != null && inventoryUI.IsOpen())
        {
            if (collectibleGems.Count > 0)
            {
                collectibleGems.Clear();
                UpdateInteractionPrompt();
            }
            return;
        }

        FindCollectibleGems();
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
        UpdateInteractionPrompt();
    }

    private void CollectClosestGem()
    {
        if (collectibleGems.Count == 0) return;

        GameObject closestGemObject = collectibleGems.OrderBy(g => Vector2.Distance(this.transform.position, g.transform.position)).FirstOrDefault();

        if (closestGemObject == null) return;

        Mineable gemComponent = closestGemObject.GetComponent<Mineable>();
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
            if (gemComponent.itemData.staminaReduction > 0)
            {
                playerStats.ReduceMaxStamina(gemComponent.itemData.staminaReduction);
            }

            collectibleGems.Remove(closestGemObject);
            ObjectPooler.Instance.ReturnToPool(PoolableType.Gem, closestGemObject);
            UpdateInteractionPrompt();
        }
        else
        {
            Debug.Log("Could not add gem to inventory. Overweight or full.");
        }
    }

    private void UpdateInteractionPrompt()
    {
        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(collectibleGems.Count > 0);
        }
    }
}