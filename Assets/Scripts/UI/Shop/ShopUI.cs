using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class ShopUI : MonoBehaviour
{
    public static ShopUI Instance { get; private set; }

    [Header("UI 패널 연결")]
    [Tooltip("상점 최상위 루트 GameObject (ShopRoot). Inspector에서 반드시 설정해야 합니다!")]
    public GameObject shopPanel; // ShopRoot를 여기에 드래그하세요


    [Header("상점 참조")]
    public ShopManager shopManager;

    [Header("판매 영역 UI")]
    public Transform sellDropZone; // 광물 드롭 존
    public TextMeshProUGUI goldText; // 현재 골드 표시

    [Header("구매 영역 UI")]
    public Transform buyItemContainer; // 구매 가능한 아이템 슬롯 컨테이너
    public GameObject shopItemSlotPrefab; // 상점 아이템 슬롯 프리팹

    [Header("Localization Keys")]
    public string textGoldPrefixKey = "ui_shop_gold_prefix";
    public string textSellFormatKey = "ui_shop_sell_format";
    public string textPriceFormatKey = "ui_shop_price_format";
    public string textStockUnlimitedKey = "ui_shop_stock_unlimited";
    public string textStockFormatKey = "ui_shop_stock_format";
    public string textBuyTitleFormatKey = "ui_shop_buy_title_format";
    public string textBuyInfoFormatKey = "ui_shop_buy_info_format";

    [Header("기타")]
    public QuantityPrompt quantityPrompt; // 수량 입력 프롬프트
    public ConfirmationPrompt confirmationPrompt; // 확인 프롬프트

    private List<GameObject> shopItemSlotObjects = new List<GameObject>();
    private bool isShopOpen = false;
    private ShopItemType currentTab = ShopItemType.Item; // 현재 선택된 탭 (기본값: Item)

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ShopUI] 중복된 ShopUI 인스턴스가 감지되었습니다. 기존 인스턴스를 유지합니다.");
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // ===== 중요: shopPanel 확인 =====
        if (shopPanel == null)
        {
            Debug.LogError("[ShopUI] shopPanel이 설정되지 않았습니다! Inspector에서 ShopRoot를 shopPanel에 드래그하세요.");
        }

        // 초기에는 shopPanel 비활성화 (UIStateManager가 관리하므로 제거)
        /* 
        if (shopPanel != null)
        {
            shopPanel.SetActive(false);
        }
        */

        // 자동 참조
        if (shopManager == null)
            shopManager = FindFirstObjectByType<ShopManager>();

        // ShopDropZone 설정
        if (sellDropZone != null)
        {
            ShopDropZone dropZone = sellDropZone.GetComponent<ShopDropZone>();
            if (dropZone == null)
            {
                dropZone = sellDropZone.gameObject.AddComponent<ShopDropZone>();
            }
            dropZone.shopManager = shopManager;
        }



        // 골드 변경 이벤트 구독
        SubscribeToGoldEvents();

        // 가격 표시 텍스트가 클릭을 방해하지 않도록 설정
        if (dropZoneInfoText != null)
        {
            dropZoneInfoText.raycastTarget = false;
        }

        // 초기 데이터 업데이트
        UpdateGoldDisplay();
        UpdateShopItems();
    }

    void OnDestroy()
    {
        UnsubscribeFromGoldEvents();
    }

    private void SubscribeToGoldEvents()
    {
        if (shopManager != null && shopManager.playerStats != null)
        {
            shopManager.playerStats.OnGoldChanged -= OnGoldChanged;
            shopManager.playerStats.OnGoldChanged += OnGoldChanged;
        }
    }

    private void UnsubscribeFromGoldEvents()
    {
        if (shopManager != null && shopManager.playerStats != null)
        {
            shopManager.playerStats.OnGoldChanged -= OnGoldChanged;
        }
    }

    private void OnGoldChanged(int newGold)
    {
        UpdateGoldDisplay();
    }

    [Header("드롭존 오버레이")]
    public GameObject dropZonePanel; // 아이템을 드래그/홀드 중일 때 나타나는 패널
    public TextMeshProUGUI dropZoneInfoText; // 드롭존 패널에 표시될 안내 텍스트 (가격 등)

    void Update()
    {
        // 드롭존 상태 업데이트
        if (isShopOpen)
        {
            UpdateDropZoneState();

            // Q/E 키로 탭 전환
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E))
            {
                // 프롬프트가 열려있지 않을 때만 탭 전환
                bool isPromptActive = (quantityPrompt != null && quantityPrompt.gameObject.activeInHierarchy) || 
                                      (confirmationPrompt != null && confirmationPrompt.gameObject.activeInHierarchy);
                if (!isPromptActive)
                {
                    if (currentTab == ShopItemType.Item)
                        SelectEquipmentTab();
                    else
                        SelectItemTab();
                }
            }
        }
    }

    private void UpdateDropZoneState()
    {
        if (dropZonePanel == null) return;

        bool showDropZone = false;
        long expectedPrice = 0;
        int quantity = 0;
        string itemName = "";

        if (HeldItemManager.Instance != null && HeldItemManager.Instance.IsHoldingItem)
        {
            var heldItem = HeldItemManager.Instance.GetHeldItem();
            if (heldItem is MineralSO mineral)
            {
                if (HeldItemManager.Instance.GetOriginalInventoryType() == InventorySlotDragHandler.InventoryType.WarehouseMinerals)
                {
                    showDropZone = true;
                    quantity = HeldItemManager.Instance.GetHeldQuantity();
                    itemName = mineral.DisplayName;
                    if (shopManager != null && shopManager.priceDatabase != null)
                    {
                        expectedPrice = (long)shopManager.priceDatabase.GetPrice(mineral.mineralID) * quantity;
                    }
                }
            }
        }
        
        if (!showDropZone && InventorySlotDragHandler.currentDraggingHandler != null)
        {
            var handler = InventorySlotDragHandler.currentDraggingHandler;
            if (handler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals)
            {
                if (WarehouseManager.Instance != null && handler.slotIndex >= 0 && handler.slotIndex < WarehouseManager.Instance.AllSlots.Count)
                {
                    var slot = WarehouseManager.Instance.AllSlots[handler.slotIndex];
                    if (slot != null && slot.item is MineralSO mineral)
                    {
                        showDropZone = true;
                        quantity = slot.quantity;
                        itemName = mineral.DisplayName;
                        if (shopManager != null && shopManager.priceDatabase != null)
                        {
                            expectedPrice = (long)shopManager.priceDatabase.GetPrice(mineral.mineralID) * quantity;
                        }
                    }
                }
            }
        }

        if (showDropZone)
        {
            if (!dropZonePanel.activeSelf) dropZonePanel.SetActive(true);
            if (dropZoneInfoText != null)
            {
                // dropZoneInfoText.text = $"{itemName} {quantity}개 판매\n예상 가격: <color=yellow>{expectedPrice} G</color>";
                dropZoneInfoText.text = LanguageManager.Instance != null ? LanguageManager.Instance.LF(textSellFormatKey, itemName, quantity, expectedPrice) : $"{itemName} {quantity}개 판매\n예상 가격: {expectedPrice} G";
            }
        }
        else
        {
            if (dropZonePanel.activeSelf) dropZonePanel.SetActive(false);
        }
    }

    public void OpenShop()
    {
        if (shopPanel == null) return;

        isShopOpen = true;
        currentTab = ShopItemType.Item; // 상점 열 때 기본 탭은 Item으로 설정
        shopPanel.SetActive(true);
        
        UpdateGoldDisplay();
        UpdateShopItems();
        

    }

    public void CloseShop()
    {
        if (shopPanel == null) return;

        isShopOpen = false;
        shopPanel.SetActive(false);
        
        // [수정] UIStateManager 상태 동기화 추가
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState == UIState.Shop)
        {
            UIStateManager.Instance.SetState(UIState.None);
        }
    }

    // --- 탭 전환 메서드 ---
    public void SelectItemTab()
    {
        currentTab = ShopItemType.Item;
        UpdateShopItems();
    }

    public void SelectEquipmentTab()
    {
        currentTab = ShopItemType.Equipment;
        UpdateShopItems();
    }

    public bool IsOpen()
    {
        return isShopOpen;
    }

    private void UpdateGoldDisplay()
    {
        if (goldText != null && shopManager != null && shopManager.playerStats != null)
        {
            goldText.text = $"{(LanguageManager.Instance?.L(textGoldPrefixKey) ?? textGoldPrefixKey)}{shopManager.playerStats.Gold}";
        }
    }

    private void UpdateShopItems()
    {
        if (buyItemContainer == null || shopItemSlotPrefab == null || shopManager == null || shopManager.shopItemDatabase == null)
            return;

        foreach (GameObject slot in shopItemSlotObjects)
        {
            Destroy(slot);
        }
        shopItemSlotObjects.Clear();

        List<ShopItemData> availableItems = shopManager.shopItemDatabase.GetAvailableItems();

        foreach (var shopItemData in availableItems)
        {
            if (shopItemData == null) continue;
            
            // 현재 선택된 탭과 일치하는 아이템만 필터링
            if (shopItemData.itemType != currentTab) continue;

            InterfaceInventoryItem item = null;
            if (shopItemData.itemType == ShopItemType.Item)
            {
                if (ItemDatabase.Instance != null)
                    item = ItemDatabase.Instance.GetItemByID(shopItemData.itemID);
            }
            else if (shopItemData.itemType == ShopItemType.Equipment)
            {
                if (EquipmentDatabase.Instance != null)
                    item = EquipmentDatabase.Instance.GetEquipmentByID(shopItemData.equipmentID);
            }

            if (item == null) continue;

            GameObject newSlot = Instantiate(shopItemSlotPrefab, buyItemContainer);
            shopItemSlotObjects.Add(newSlot);

            RectTransform slotRect = newSlot.GetComponent<RectTransform>();
            if (slotRect != null)
            {
                slotRect.localScale = Vector3.one;
                slotRect.localPosition = Vector3.zero;
            }

            SetupShopItemSlot(newSlot, shopItemData, item);
        }
    }

    private void SetupShopItemSlot(GameObject slotObject, ShopItemData shopItemData, InterfaceInventoryItem item)
    {
        Image icon = slotObject.transform.Find("ItemIcon")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.sprite = item.Icon;
            icon.enabled = (icon.sprite != null);
        }

        TextMeshProUGUI nameText = slotObject.transform.Find("ItemName")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text = item.DisplayName;
        }

        TextMeshProUGUI priceText = slotObject.transform.Find("PriceText")?.GetComponent<TextMeshProUGUI>();
        if (priceText != null)
        {
            priceText.text = LanguageManager.Instance != null ? LanguageManager.Instance.LF(textPriceFormatKey, shopItemData.price) : $"{shopItemData.price}골드";
        }

        TextMeshProUGUI stockText = slotObject.transform.Find("StockText")?.GetComponent<TextMeshProUGUI>();
        if (stockText != null)
        {
            stockText.text = shopItemData.stock < 0 
                ? (LanguageManager.Instance?.L(textStockUnlimitedKey) ?? textStockUnlimitedKey) 
                : (LanguageManager.Instance != null ? LanguageManager.Instance.LF(textStockFormatKey, shopItemData.stock) : $"재고: {shopItemData.stock}");
        }

        // 슬롯 전체 클릭 시 구매 로직 연결
        Button slotButton = slotObject.GetComponent<Button>();
        if (slotButton != null)
        {
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(() => OnBuyButtonClicked(shopItemData, item));
        }
        else
        {
            // BuyButton을 찾아서 연결 (기존 방식 유지/폴백)
            Button buyButton = slotObject.transform.Find("BuyButton")?.GetComponent<Button>();
            if (buyButton != null)
            {
                buyButton.onClick.RemoveAllListeners();
                buyButton.onClick.AddListener(() => OnBuyButtonClicked(shopItemData, item));
            }
        }

        TooltipTrigger tooltipTrigger = slotObject.GetComponent<TooltipTrigger>() ?? slotObject.AddComponent<TooltipTrigger>();
        ShopItemTooltipProvider tooltipProvider = slotObject.GetComponent<ShopItemTooltipProvider>() ?? slotObject.AddComponent<ShopItemTooltipProvider>();
        
        // 아이템/장비 통합 툴팁 초기화
        tooltipProvider.Initialize(shopItemData, item);
        tooltipTrigger.tooltipProvider = tooltipProvider;
    }

    private void OnBuyButtonClicked(ShopItemData shopItemData, InterfaceInventoryItem item)
    {
        if (shopManager?.playerStats == null) return;

        if (shopManager.playerStats.Gold < shopItemData.price) return;
        if (shopItemData.stock >= 0 && shopItemData.stock <= 0) return;

        if (quantityPrompt != null)
        {
            int maxQuantity = shopItemData.stock < 0 ? 99 : shopItemData.stock;
            int affordableQuantity = shopManager.playerStats.Gold / shopItemData.price;
            maxQuantity = Mathf.Min(maxQuantity, affordableQuantity);
            
            // 장비의 경우 스택이 안될 수 있으므로 체크
            if (!item.Stackable) maxQuantity = 1;

            string title = LanguageManager.Instance != null ? LanguageManager.Instance.LF(textBuyTitleFormatKey, item.DisplayName) : $"{item.DisplayName} 구매";
            string info = LanguageManager.Instance != null ? LanguageManager.Instance.LF(textBuyInfoFormatKey, shopItemData.price, maxQuantity) : $"가격: {shopItemData.price}골드\n최대 구매 가능: {maxQuantity}개";

            quantityPrompt.Show(title, 1, maxQuantity, (quantity) =>
            {
                if (shopManager.BuyItem(shopItemData, quantity))
                {
                    UpdateGoldDisplay();
                    UpdateShopItems();
                }
            }, info);
        }
        else
        {
            if (shopManager.BuyItem(shopItemData, 1))
            {
                UpdateGoldDisplay();
                UpdateShopItems();
            }
        }
    }

    public void OnGoldChanged()
    {
        if (isShopOpen) UpdateGoldDisplay();
    }
}


