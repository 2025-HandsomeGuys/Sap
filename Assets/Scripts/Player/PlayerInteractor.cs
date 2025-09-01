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
        if (context.started) // 'performed'에서 'started'로 변경하여 키를 누르는 즉시 반응하도록 수정
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
            Debug.LogError("Gem object is missing Mineable script!");
            return;
        }

        if (gemComponent.itemData == null)
        {
            Debug.LogError("Gem script is missing ItemData! Assign it in the prefab inspector.");
            return;
        }

        if (playerInventory.AddItem(gemComponent.itemData, 1))
        {
            Debug.Log("[Collector] AddItem 성공. 획득 처리 시작.");

            if (gemComponent.itemData.staminaReduction > 0)
            {
                Debug.Log("[Collector] 스태미나 감소 시도.");
                playerStats.ReduceMaxStamina(gemComponent.itemData.staminaReduction);
                Debug.Log("[Collector] 스태미나 감소 완료.");
            }

            Debug.Log("[Collector] 리스트에서 광물 제거 시도.");
            collectibleGems.Remove(closestGemObject);
            Debug.Log("[Collector] 리스트에서 광물 제거 완료. 오브젝트 풀 반환 시도.");

            ObjectPooler.Instance.ReturnToPool(PoolableType.Gem, closestGemObject);
            Debug.Log("[Collector] 오브젝트 풀 반환 완료. UI 업데이트 시도.");

            UpdateInteractionPrompt();
            Debug.Log("[Collector] 획득 처리 완전 종료.");
        }
        else
        {
            Debug.LogWarning($"[Collector] AddItem이 false를 반환함: {gemComponent.itemData.itemName}");
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