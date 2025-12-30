using UnityEngine;

public class ShopManager : MonoBehaviour
{
    [Header("Dependencies")]
    public MineralPriceDatabase priceDatabase;
    public ShopItemDatabase shopItemDatabase; // 판매 가능한 아이템 데이터베이스
    public PlayerStatsController playerStats;
    public MineralInventory mineralInventory;
    public ItemInventory itemInventory; // 아이템 구매 시 사용

    private void Start()
    {
        // Assign dependencies automatically if not set in inspector
        if (playerStats == null)
        {
            playerStats = FindFirstObjectByType<PlayerStatsController>();
        }
        if (mineralInventory == null)
        {
            mineralInventory = FindFirstObjectByType<MineralInventory>();
        }
        if (itemInventory == null)
        {
            itemInventory = FindFirstObjectByType<ItemInventory>();
        }
        if (priceDatabase == null)
        {
            Debug.LogError("MineralPriceDatabase is not assigned in the ShopManager! Please assign it in the inspector.");
        }
    }

    public bool SellItem(InterfaceInventoryItem itemToSell, int amount)
    {
        if (itemToSell == null || amount <= 0) return false;
        if (priceDatabase == null || playerStats == null || mineralInventory == null)
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
        int itemCount = mineralInventory.CountOf(itemToSell);

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
        bool removed = mineralInventory.RemoveItem(mineral, amount);
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

    // 아이템 구매 기능
    public bool BuyItem(ItemID itemID, int quantity = 1)
    {
        if (shopItemDatabase == null || playerStats == null || itemInventory == null)
        {
            Debug.LogError("ShopManager is missing dependencies for buying!");
            return false;
        }

        // 1. 상점에서 판매하는 아이템인지 확인
        ShopItemData itemData = shopItemDatabase.GetItemData(itemID);
        if (itemData == null || !itemData.isAvailable)
        {
            Debug.Log($"아이템 {itemID}는 현재 판매하지 않습니다.");
            return false;
        }

        // 2. 재고 확인
        if (itemData.stock >= 0 && itemData.stock < quantity)
        {
            Debug.Log($"재고가 부족합니다. (요청: {quantity}개, 재고: {itemData.stock}개)");
            return false;
        }

        // 3. 가격 확인
        int totalPrice = itemData.price * quantity;
        if (totalPrice <= 0)
        {
            Debug.Log("가격이 설정되지 않았습니다.");
            return false;
        }

        // 4. 골드 확인
        if (playerStats.gold < totalPrice)
        {
            Debug.Log($"골드가 부족합니다. (필요: {totalPrice}골드, 보유: {playerStats.gold}골드)");
            return false;
        }

        // 5. 아이템 데이터베이스에서 아이템 가져오기
        if (ItemDatabase.Instance == null)
        {
            Debug.LogError("ItemDatabase.Instance가 없습니다!");
            return false;
        }

        ItemSO itemToBuy = ItemDatabase.Instance.GetItemByID(itemID);
        if (itemToBuy == null)
        {
            Debug.LogError($"아이템 ID {itemID}에 해당하는 아이템을 찾을 수 없습니다!");
            return false;
        }

        // 6. 인벤토리에 추가 가능한지 확인
        // (AddItem에서 자동으로 확인하지만, 골드를 먼저 차감하기 전에 확인하는 것이 좋음)
        int currentCount = itemInventory.CurrentItemCount;
        int addCount = itemToBuy.Stackable ? quantity : quantity;
        if (currentCount + addCount > itemInventory.maxItemCount)
        {
            Debug.Log($"인벤토리가 가득 찼습니다. (최대 {itemInventory.maxItemCount}개)");
            return false;
        }

        // 7. 골드 차감
        bool goldSpent = playerStats.SpendGold(totalPrice);
        if (!goldSpent)
        {
            Debug.LogError("골드 차감 실패!");
            return false;
        }

        // 8. 아이템 추가
        bool itemAdded = itemInventory.AddItem(itemToBuy, quantity);
        if (!itemAdded)
        {
            // 실패 시 골드 환불
            playerStats.AddGold(totalPrice);
            Debug.LogError("아이템 추가 실패! 골드를 환불했습니다.");
            return false;
        }

        // 9. 재고 감소
        if (itemData.stock >= 0)
        {
            itemData.stock -= quantity;
        }

        Debug.Log($"구매 완료: {itemToBuy.DisplayName} {quantity}개를 {totalPrice}골드에 구매했습니다.");
        return true;
    }
}
