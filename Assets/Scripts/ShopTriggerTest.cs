using UnityEngine;
using System.Linq;

public class ShopTriggerTest : MonoBehaviour
{
    private bool isPlayerInside = false;
    private DiggingController playerDiggingController;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInside = true;
            playerDiggingController = other.GetComponent<DiggingController>();
            Debug.Log("Player entered the shop trigger. Press 'I' to sell, 'O' to upgrade shovel.");
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInside = false;
            playerDiggingController = null;
            Debug.Log("Player exited the shop trigger.");
        }
    }

    private void Update()
    {
        if (isPlayerInside)
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                SellFirstMineral();
            }

            if (Input.GetKeyDown(KeyCode.O))
            {
                UpgradeShovel();
            }
        }
    }

    private void SellFirstMineral()
    {
        var shopManager = FindFirstObjectByType<ShopManager>();
        if (shopManager == null)
        {
            Debug.LogError("ShopManager not found!");
            return;
        }

        var inventory = shopManager.mineralInventory;
        if (inventory == null)
        {
            Debug.LogError("Player inventory not found on ShopManager!");
            return;
        }

        // Find the first mineral in the inventory
        MineralSO mineralToSell = null;
        foreach (var slot in inventory.ReadonlyItems)
        {
            if (slot.item is MineralSO mineral)
            {
                mineralToSell = mineral;
                break;
            }
        }

        if (mineralToSell != null)
        {
            shopManager.SellItem(mineralToSell, 1);
        }
        else
        {
            Debug.Log("No minerals in inventory to sell.");
        }
    }

    private void UpgradeShovel()
    {
        if (playerDiggingController != null)
        {
            // For testing, upgrade is free and has a fixed amount.
            // You can add cost check here by using ShopManager to spend gold.
            playerDiggingController.IncreaseDigRadius(0.1f);
        }
        else
        {
            Debug.LogError("DiggingController not found on the player!");
        }
    }
}
