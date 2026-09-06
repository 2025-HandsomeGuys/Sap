using UnityEngine;
using Relic;
using Relic.Data;

public class ShopManager : MonoBehaviour
{
    [Header("Dependencies")]
    public MineralPriceDatabase priceDatabase;
    public ShopItemDatabase shopItemDatabase; // 판매 가능한 아이템 데이터베이스
    public PlayerStat playerStats;
    public MineralInventory mineralInventory;
    public ItemInventory itemInventory; // 아이템 구매 시 사용
    public RelicManager relicManager;   // 유물 구매 시 사용 (비워두면 런타임에 자동 탐색)

    [Header("Warehouse")]
    public WarehouseManager warehouseManager;

    private ShopUI cachedShopUI;

    private void Start()
    {
        // ... existing references ...
        if (playerStats == null)
            playerStats = FindFirstObjectByType<PlayerStat>();
        if (mineralInventory == null)
            mineralInventory = FindFirstObjectByType<MineralInventory>();
        if (itemInventory == null)
            itemInventory = FindFirstObjectByType<ItemInventory>();
        if (priceDatabase == null)
            Debug.LogError("MineralPriceDatabase is not assigned in the ShopManager! Please assign it in the inspector.");
            
        // WarehouseManager 참조
        if (warehouseManager == null)
            warehouseManager = WarehouseManager.Instance;
    }

    // ...

    public bool SellItem(InterfaceInventoryItem itemToSell, int amount, bool fromWarehouse = true, int slotIndex = -1)
    {
        if (itemToSell == null || amount <= 0) return false;
        
        // 참조 재확인 (씬 전환 등으로 끊겼을 경우)
        if (warehouseManager == null) warehouseManager = WarehouseManager.Instance;
        
        if (priceDatabase == null || playerStats == null)
        {
            Debug.LogError("ShopManager is missing dependencies!");
            return false;
        }

        if (!(itemToSell is MineralSO mineral))
        {
            Debug.Log("Only minerals can be sold here.");
            return false;
        }

        // 판매 소스에 따라 분기 처리
        if (fromWarehouse)
        {
            // 창고에서 판매
            if (warehouseManager != null)
            {
                // 수량 확인 (참조용 - 특정 슬롯에서 직접 뺄 때는 슬롯 수량과 비교해야 하지만, 여기선 호출자가 보장한다고 가정하거나 체크)
                // 만약 slotIndex가 있다면 해당 슬롯이 유효한지 체크하면 더 좋음.
                // 일단 기존 퉁치는 수량 확인 로직 유지하되, 제거 방식만 변경
                int warehouseCount = warehouseManager.GetMineralCount(mineral);
                if (warehouseCount >= amount)
                {
                    int price = priceDatabase.GetPrice(mineral.mineralID);
                    int totalGold = price * amount;
                    
                    if (slotIndex >= 0)
                    {
                        // 특정 슬롯에서 제거
                        warehouseManager.RemoveMineralAt(slotIndex, amount);
                        Debug.Log($"[Warehouse] Sold {amount} of {mineral.DisplayName} from slot {slotIndex} for {totalGold} gold.");
                    }
                    else
                    {
                        // 앞에서부터 제거 (기존 로직)
                        warehouseManager.RemoveMineral(mineral, amount);
                        Debug.Log($"[Warehouse] Sold {amount} of {mineral.DisplayName} for {totalGold} gold.");
                    }

                    playerStats.AddGold(totalGold);
                    DayEarningsLedger.Report(DayEarningsCategory.MineralSale, totalGold);
                    LogShopTransaction("sell", mineral.mineralID.ToString(), amount, totalGold, fromWarehouse);

                    if (SoundManager.Instance != null)
                        SoundManager.Instance.PlaySFX(SfxKeys.ShopSell);

                    return true;
                }
                else
                {
                    Debug.Log($"[Shop] Warehouse has {warehouseCount}, needed {amount}.");
                }
            }
        }
        else
        {
            // 인벤토리에서 판매
            if (mineralInventory != null)
            {
                int itemCount = mineralInventory.CountOf(itemToSell);
                if (itemCount >= amount)
                {
                    int price = priceDatabase.GetPrice(mineral.mineralID);
                    int totalGold = price * amount;
                    
                    if (slotIndex >= 0)
                    {
                        // 특정 슬롯에서 제거
                        mineralInventory.RemoveItemAt(slotIndex, amount);
                        Debug.Log($"[Inventory] Sold {amount} of {mineral.DisplayName} from slot {slotIndex} for {totalGold} gold.");
                    }
                    else
                    {
                        // 앞에서부터 제거
                        mineralInventory.RemoveItem(mineral, amount);
                        Debug.Log($"[Inventory] Sold {amount} of {mineral.DisplayName} for {totalGold} gold.");
                    }

                    playerStats.AddGold(totalGold);
                    DayEarningsLedger.Report(DayEarningsCategory.MineralSale, totalGold);
                    LogShopTransaction("sell", mineral.mineralID.ToString(), amount, totalGold, fromWarehouse);

                    if (SoundManager.Instance != null)
                        SoundManager.Instance.PlaySFX(SfxKeys.ShopSell);

                    return true;
                }
                else
                {
                    Debug.Log($"[Shop] Inventory has {itemCount}, needed {amount}.");
                }
            }
        }

        Debug.Log($"Not enough {itemToSell.DisplayName} to sell in {(fromWarehouse ? "Warehouse" : "Inventory")}.");
        return false;
    }

    public bool BuyItem(ShopItemData itemData, int quantity = 1)
    {
        if (shopItemDatabase == null || playerStats == null || itemData == null)
        {
            Debug.LogError("ShopManager is missing dependencies for buying!");
            return false;
        }

        // SellItem()과 동일하게 참조 재확인 (씬 전환 등으로 끊겼을 경우 대비)
        if (warehouseManager == null)
            warehouseManager = WarehouseManager.Instance;

        if (!itemData.isAvailable) return false;

        // 업그레이드 관문(모이는 노드)을 아직 안 산 항목은 구매 불가.
        // 코드 오버레이(ShopOverlayUI)가 이미 버튼을 막지만, 구 프리팹 UI 등 다른 경로로도 들어오므로 여기서 한 번 더 건다.
        if (!ShopUnlockGate.IsUnlocked(itemData))
        {
            Debug.Log($"[Shop] 업그레이드 미해금 항목: {itemData.unlockNodeId}");
            return false;
        }

        if (itemData.stock >= 0 && itemData.stock < quantity)
        {
            Debug.Log("재고가 부족합니다.");
            return false;
        }

        // 유물은 창고/인벤토리를 거치지 않고 RelicInventory로 바로 들어간다 (수량 개념 없음)
        if (itemData.itemType == ShopItemType.Relic)
            return BuyRelic(itemData);

        // 장비도 유물처럼 고유 — 이미 보유 중이면 중복 구매를 막고, 수량은 항상 1로 강제한다.
        if (itemData.itemType == ShopItemType.Equipment)
        {
            if (IsEquipmentOwned(itemData.equipmentID))
            {
                Debug.Log($"[Shop] 이미 보유한 장비: {itemData.equipmentID}");
                return false;
            }
            quantity = 1;
        }

        int totalPrice = itemData.price * quantity;
        if (playerStats.Gold < totalPrice)
        {
            Debug.Log("골드가 부족합니다.");
            return false;
        }

        InterfaceInventoryItem itemToBuy = null;

        if (itemData.itemType == ShopItemType.Item)
        {
            if (ItemDatabase.Instance != null)
                itemToBuy = ItemDatabase.Instance.GetItemByID(itemData.itemID);
        }
        else if (itemData.itemType == ShopItemType.Equipment)
        {
            if (EquipmentDatabase.Instance != null)
                itemToBuy = EquipmentDatabase.Instance.GetEquipmentByID(itemData.equipmentID);
        }

        if (itemToBuy == null)
        {
            Debug.LogError($"[ShopManager] Could not find item in database. Type: {itemData.itemType}, ID: {(itemData.itemType == ShopItemType.Item ? itemData.itemID : itemData.equipmentID)}");
            return false;
        }

        // 구매 아이템은 무조건 창고로 저장
        if (warehouseManager != null)
        {
            if (playerStats.SpendGold(totalPrice))
            {
                DayEarningsLedger.Report(DayEarningsCategory.ShopPurchase, -totalPrice);
                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFX(SfxKeys.ShopBuy);
                if (itemToBuy is ItemSO item)
                    warehouseManager.AddItem(item, quantity);
                else if (itemToBuy is EquipmentSO equipment)
                    warehouseManager.AddEquipment(equipment, quantity);

                if (itemData.stock >= 0) itemData.stock -= quantity;
                Debug.Log($"[Warehouse] Bought {itemToBuy.DisplayName} x{quantity} → 창고에 저장됨");
                LogShopTransaction("buy", BuyItemKey(itemData), quantity, -totalPrice, true);
                return true;
            }
        }
        else
        {
            // WarehouseManager를 끝내 찾지 못한 경우에만 ItemInventory 폴백 (ItemSO 전용)
            Debug.LogWarning("[ShopManager] WarehouseManager를 찾을 수 없어 ItemInventory로 폴백합니다.");
            if (itemInventory != null && itemToBuy is ItemSO itemSO)
            {
                int currentCount = itemInventory.CurrentItemCount;
                if (currentCount + quantity > itemInventory.maxItemCount) return false;

                if (playerStats.SpendGold(totalPrice))
                {
                    DayEarningsLedger.Report(DayEarningsCategory.ShopPurchase, -totalPrice);
                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFX(SfxKeys.ShopBuy);
                    itemInventory.AddItem(itemSO, quantity);
                    if (itemData.stock >= 0) itemData.stock -= quantity;
                    LogShopTransaction("buy", BuyItemKey(itemData), quantity, -totalPrice, false);
                    return true;
                }
            }
        }

        return false;
    }

    // ===================================================
    // 유물 구매
    // ===================================================

    /// <summary>
    /// 씬의 RelicManager(플레이어에 부착). 씬 전환마다 끊기므로 매번 확인한다.
    /// 매니저가 없는 씬(지상 등)에서는 EnsureInScene()이 플레이어에 붙여 주고 세이브까지 복원한다.
    /// </summary>
    public RelicManager ResolveRelicManager()
    {
        if (relicManager == null)
            relicManager = RelicManager.EnsureInScene();
        return relicManager;
    }

    /// <summary>이미 보유한 유물인지 (상점 UI의 '보유 중' 표시용).</summary>
    public bool IsRelicOwned(RelicID id)
    {
        var mgr = ResolveRelicManager();
        return mgr != null && mgr.Inventory.IsOwned(id);
    }

    /// <summary>
    /// 이미 보유한 장비인지 (유물처럼 고유 — 창고 보관분 + 착용/가방 장비 슬롯을 모두 본다).
    /// 상점 '보유 중' 표시와 중복 구매 차단에 함께 쓴다.
    /// </summary>
    public bool IsEquipmentOwned(EquipmentID id)
    {
        if (id == EquipmentID.None) return false;
        string idStr = id.ToString();

        if (warehouseManager == null) warehouseManager = WarehouseManager.Instance;
        if (warehouseManager != null)
        {
            foreach (var slot in warehouseManager.StoredEquipments)
                if (slot?.item != null && slot.item.Id == idStr) return true;
        }

        // EquipmentInventory는 꺼진 UI 계층(EquipmentsPanel)에 붙어 있을 수 있어 Include로 찾는다.
        var eqInv = FindFirstObjectByType<EquipmentInventory>(FindObjectsInactive.Include);
        if (eqInv != null)
        {
            foreach (var slot in eqInv.ReadonlyItems)
                if (slot?.item != null && slot.item.Id == idStr) return true;
        }

        return false;
    }

    /// <summary>
    /// 유물 구매 — 골드만 차감하고 RelicInventory에 지급한다(창고 경유 없음, 수량 없음).
    /// 빈 로드아웃 슬롯이 있으면 바로 장착까지 해준다(테스트 편의).
    /// </summary>
    private bool BuyRelic(ShopItemData itemData)
    {
        var mgr = ResolveRelicManager();
        if (mgr == null)
        {
            Debug.LogError("[ShopManager] RelicManager를 찾을 수 없어 유물을 지급하지 못했습니다.");
            return false;
        }

        var id = itemData.relicID;
        if (id == RelicID.None) return false;

        if (mgr.Inventory.IsOwned(id))
        {
            Debug.Log($"[Shop] 이미 보유한 유물: {id}");
            return false;
        }

        if (playerStats.Gold < itemData.price) return false;
        if (!playerStats.SpendGold(itemData.price)) return false;

        DayEarningsLedger.Report(DayEarningsCategory.ShopPurchase, -itemData.price);
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.ShopBuy);

        mgr.Inventory.Grant(id);

        // 빈 슬롯이 있으면 자동 장착
        for (int i = 0; i < mgr.Inventory.SlotCount; i++)
        {
            if (mgr.Inventory.GetEquipped(i) == RelicID.None)
            {
                mgr.EquipSlot(i, id);
                break;
            }
        }

        if (itemData.stock >= 0) itemData.stock -= 1;
        Debug.Log($"[Shop] 유물 구매: {id}");
        LogShopTransaction("buy", BuyItemKey(itemData), 1, -itemData.price, false);
        return true;
    }

    // ===================================================
    // 텔레메트리 (설계 §3.3)
    // ===================================================

    /// <summary>상점 거래 기록. gold는 DayEarningsLedger와 같은 부호 규약 — 수입 +, 지출 -.</summary>
    private static void LogShopTransaction(string kind, string item, int amount, int gold, bool warehouse)
    {
        Telemetry.Log(TelemetryEvents.ShopTransaction, TelemetryPayload.New()
            .Add("kind", kind)
            .Add("item", item)
            .Add("amount", amount)
            .Add("gold", gold)
            .Add("warehouse", warehouse));
    }

    private static string BuyItemKey(ShopItemData data)
    {
        if (data == null) return "unknown";
        switch (data.itemType)
        {
            case ShopItemType.Item:      return data.itemID.ToString();
            case ShopItemType.Equipment: return data.equipmentID.ToString();
            case ShopItemType.Relic:     return data.relicID.ToString();
            default:                     return "unknown";
        }
    }

    // 호환성을 위한 오버로드 (기존 호출부 지원)
    public bool BuyItem(ItemID itemID, int quantity = 1)
    {
        ShopItemData data = shopItemDatabase?.GetItemData(itemID);
        return BuyItem(data, quantity);
    }
    public void OpenShop()
    {
        if (ShopUI.Instance != null)
            ShopUI.Instance.OpenShop();
    }

    public void CloseShop()
    {
        if (ShopUI.Instance != null)
            ShopUI.Instance.CloseShop();
    }

    public bool IsShopOpen()
    {
        return ShopUI.Instance != null && ShopUI.Instance.IsOpen();
    }
}
