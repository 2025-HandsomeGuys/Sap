using UnityEngine;

public class ShopManager : MonoBehaviour
{
    [Header("Dependencies")]
    public MineralPriceDatabase priceDatabase;
    public PlayerStatsController playerStats;
    public Inventory playerInventory;

    private void Start()
    {
        // Assign dependencies automatically if not set in inspector
        if (playerStats == null)
        {
            playerStats = FindFirstObjectByType<PlayerStatsController>();
        }
        if (playerInventory == null)
        {
            playerInventory = FindFirstObjectByType<Inventory>();
        }
        if (priceDatabase == null)
        {
            Debug.LogError("MineralPriceDatabase is not assigned in the ShopManager! Please assign it in the inspector.");
        }
    }

    public bool SellItem(InterfaceInventoryItem itemToSell, int amount)
    {
        if (itemToSell == null || amount <= 0) return false;
        if (priceDatabase == null || playerStats == null || playerInventory == null)
        {
            Debug.LogError("ShopManager is missing dependencies!");
            return false;
        }

        // Check if the item is a mineral, as only minerals have prices in our database
        if (!(itemToSell is MineralSO mineral))
        {
            Debug.Log("Only minerals can be sold here.");
            return false;
        }

        // 1. Check if the player has enough of the item
        int itemCount = 0;
        foreach (var slot in playerInventory.ReadonlyItems)
        {
            if (slot.item.Id == itemToSell.Id)
            {
                itemCount += slot.quantity;
            }
        }

        if (itemCount < amount)
        {
            Debug.Log($"Not enough {itemToSell.DisplayName} to sell. Required: {amount}, Have: {itemCount}");
            return false;
        }

        // 2. Get the price from the database
        int price = priceDatabase.GetPrice(mineral.mineralID);
        if (price <= 0)
        {
            Debug.Log($"{itemToSell.DisplayName} cannot be sold (price is 0 or less).");
            return false;
        }

        // 3. Add gold to the player
        int totalGold = price * amount;
        playerStats.AddGold(totalGold);

        // 4. Remove the item from the inventory
        bool removed = playerInventory.RemoveItem(itemToSell, amount);
        if (!removed)
        {
            // This should not happen if our initial check was correct, but as a safeguard:
            Debug.LogError($"Failed to remove {itemToSell.DisplayName} from inventory, rolling back gold.");
            playerStats.SpendGold(totalGold); // Attempt to roll back
            return false;
        }

        Debug.Log($"Sold {amount} of {itemToSell.DisplayName} for {totalGold} gold.");
        return true;
    }
}
