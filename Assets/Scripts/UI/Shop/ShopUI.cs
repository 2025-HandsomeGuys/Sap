using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public enum TabType
{
    Shop,
    Upgrade
}

public class ShopUI : MonoBehaviour
{
    [Header("UI 패널 연결")]
    public GameObject shopPanel; // 전체 상점 패널
    public Button closeButton; // 닫기 버튼

    [Header("상점 참조")]
    public ShopManager shopManager;
    public InventoryUI inventoryUI;

    [Header("판매 영역 UI")]
    public Transform sellDropZone; // 광물 드롭 존
    public TextMeshProUGUI goldText; // 현재 골드 표시

    [Header("구매 영역 UI")]
    public Transform buyItemContainer; // 구매 가능한 아이템 슬롯 컨테이너
    public GameObject shopItemSlotPrefab; // 상점 아이템 슬롯 프리팹

    [Header("탭 시스템")]
    public Button shopTabButton; // 상점 탭 버튼
    public Button upgradeTabButton; // 업그레이드 탭 버튼
    public GameObject shopContentPanel; // 상점 콘텐츠 패널
    public GameObject upgradeContentPanel; // 업그레이드 콘텐츠 패널
    public ToolUpgradeUI toolUpgradeUI; // 도구 강화 UI

    [Header("기타")]
    public QuantityPrompt quantityPrompt; // 수량 입력 프롬프트

    private List<GameObject> shopItemSlotObjects = new List<GameObject>();
    private bool isShopOpen = false;
    private ShopItemData selectedShopItem;
    private TabType currentTab = TabType.Shop;

    void Start()
    {
        if (shopPanel != null)
        {
            shopPanel.SetActive(false);
        }

        // 자동 참조
        if (shopManager == null)
            shopManager = FindFirstObjectByType<ShopManager>();
        if (inventoryUI == null)
            inventoryUI = FindFirstObjectByType<InventoryUI>();

        // ShopDropZone 설정
        if (sellDropZone != null)
        {
            ShopDropZone dropZone = sellDropZone.GetComponent<ShopDropZone>();
            if (dropZone == null)
            {
                dropZone = sellDropZone.gameObject.AddComponent<ShopDropZone>();
            }
            dropZone.shopManager = shopManager;
            dropZone.inventoryUI = inventoryUI;
        }

        // 닫기 버튼 설정
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => CloseShop());
        }

        // 탭 버튼 설정
        if (shopTabButton != null)
        {
            shopTabButton.onClick.RemoveAllListeners();
            shopTabButton.onClick.AddListener(() => SwitchTab(TabType.Shop));
        }
        if (upgradeTabButton != null)
        {
            upgradeTabButton.onClick.RemoveAllListeners();
            upgradeTabButton.onClick.AddListener(() => SwitchTab(TabType.Upgrade));
        }

        // 도구 강화 UI 자동 참조
        if (toolUpgradeUI == null)
            toolUpgradeUI = FindFirstObjectByType<ToolUpgradeUI>();

        // 골드 변경 이벤트 구독
        SubscribeToGoldEvents();

        // 초기 골드 표시
        UpdateGoldDisplay();

        // 초기 탭 설정
        SwitchTab(TabType.Shop);
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

    void Update()
    {
        // ESC 키로 상점 닫기
        if (isShopOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseShop();
        }
    }

    public void OpenShop()
    {
        if (shopPanel == null)
        {
            Debug.LogWarning("[ShopUI] shopPanel이 설정되지 않았습니다.");
            return;
        }

        isShopOpen = true;
        shopPanel.SetActive(true);
        Time.timeScale = 0f; // 게임 일시정지

        UpdateGoldDisplay();
        SwitchTab(currentTab); // 마지막으로 본 탭으로 전환
    }

    public void SwitchTab(TabType tab)
    {
        currentTab = tab;

        // 상점 탭
        if (shopContentPanel != null)
        {
            shopContentPanel.SetActive(tab == TabType.Shop);
        }

        // 업그레이드 탭
        if (upgradeContentPanel != null)
        {
            upgradeContentPanel.SetActive(tab == TabType.Upgrade);
        }

        // 탭 버튼 활성화 상태 업데이트
        UpdateTabButtons();

        // 탭에 따른 콘텐츠 업데이트
        if (tab == TabType.Shop)
        {
            UpdateShopItems();
            UpdateDescription(null);
        }
        else if (tab == TabType.Upgrade)
        {
            if (toolUpgradeUI != null)
            {
                toolUpgradeUI.OnUpgradeTabActivated(); // 도구 강화 UI 업데이트
            }
        }
    }

    private void UpdateTabButtons()
    {
        // 상점 탭 버튼
        if (shopTabButton != null)
        {
            // 활성화된 탭은 약간 어둡게, 비활성화된 탭은 밝게 표시
            var colors = shopTabButton.colors;
            colors.normalColor = currentTab == TabType.Shop ? new Color(0.8f, 0.8f, 0.8f, 1f) : Color.white;
            shopTabButton.colors = colors;
        }

        // 업그레이드 탭 버튼
        if (upgradeTabButton != null)
        {
            var colors = upgradeTabButton.colors;
            colors.normalColor = currentTab == TabType.Upgrade ? new Color(0.8f, 0.8f, 0.8f, 1f) : Color.white;
            upgradeTabButton.colors = colors;
        }
    }

    public void CloseShop()
    {
        if (shopPanel == null) return;

        isShopOpen = false;
        shopPanel.SetActive(false);
        Time.timeScale = 1f; // 게임 재개

        // 선택 초기화
        selectedShopItem = null;
    }

    public bool IsOpen()
    {
        return isShopOpen;
    }

    private void UpdateGoldDisplay()
    {
        if (goldText != null && shopManager != null && shopManager.playerStats != null)
        {
            goldText.text = $"골드: {shopManager.playerStats.gold}";
        }
    }

    private void UpdateShopItems()
    {
        if (buyItemContainer == null || shopItemSlotPrefab == null || shopManager == null || shopManager.shopItemDatabase == null)
        {
            Debug.LogWarning("[ShopUI] 구매 아이템 업데이트 실패: 필수 참조가 없습니다.");
            return;
        }

        // 기존 슬롯 제거
        foreach (GameObject slot in shopItemSlotObjects)
        {
            Destroy(slot);
        }
        shopItemSlotObjects.Clear();

        // 판매 가능한 아이템 목록 가져오기
        List<ShopItemData> availableItems = shopManager.shopItemDatabase.GetAvailableItems();

        foreach (var shopItemData in availableItems)
        {
            if (shopItemData == null) continue;

            // ItemDatabase에서 아이템 정보 가져오기
            if (ItemDatabase.Instance == null) continue;
            ItemSO itemSO = ItemDatabase.Instance.GetItemByID(shopItemData.itemID);
            if (itemSO == null) continue;

            // 슬롯 생성
            GameObject newSlot = Instantiate(shopItemSlotPrefab, buyItemContainer);
            shopItemSlotObjects.Add(newSlot);

            // RectTransform 설정
            RectTransform slotRect = newSlot.GetComponent<RectTransform>();
            if (slotRect != null)
            {
                slotRect.localScale = Vector3.one;
                slotRect.localPosition = Vector3.zero;
                // 컨테이너의 레이아웃 그룹이 자동으로 배치하도록 설정
            }

            SetupShopItemSlot(newSlot, shopItemData, itemSO);
        }
    }

    private void SetupShopItemSlot(GameObject slotObject, ShopItemData shopItemData, ItemSO itemSO)
    {
        // 아이콘 설정
        Image icon = slotObject.transform.Find("ItemIcon")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.sprite = itemSO.Icon;
            icon.enabled = (icon.sprite != null);
        }

        // 아이템 이름 설정
        TextMeshProUGUI nameText = slotObject.transform.Find("ItemName")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text = itemSO.DisplayName;
        }

        // 가격 표시
        TextMeshProUGUI priceText = slotObject.transform.Find("PriceText")?.GetComponent<TextMeshProUGUI>();
        if (priceText != null)
        {
            priceText.text = $"{shopItemData.price}골드";
        }

        // 재고 표시
        TextMeshProUGUI stockText = slotObject.transform.Find("StockText")?.GetComponent<TextMeshProUGUI>();
        if (stockText != null)
        {
            if (shopItemData.stock < 0)
            {
                stockText.text = "재고 : 무제한";
            }
            else
            {
                stockText.text = $"재고: {shopItemData.stock}";
            }
        }

        // 슬롯 클릭 이벤트
        Button slotButton = slotObject.GetComponent<Button>();
        if (slotButton != null)
        {
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(() =>
            {
                selectedShopItem = shopItemData;
                UpdateDescription(shopItemData);
            });
        }

        // 구매 버튼
        Button buyButton = slotObject.transform.Find("BuyButton")?.GetComponent<Button>();
        if (buyButton != null)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(() => OnBuyButtonClicked(shopItemData, itemSO));
        }

        // 툴팁 트리거 추가
        TooltipTrigger tooltipTrigger = slotObject.GetComponent<TooltipTrigger>();
        if (tooltipTrigger == null)
        {
            tooltipTrigger = slotObject.AddComponent<TooltipTrigger>();
        }

        // 툴팁 제공자 추가
        ShopItemTooltipProvider tooltipProvider = slotObject.GetComponent<ShopItemTooltipProvider>();
        if (tooltipProvider == null)
        {
            tooltipProvider = slotObject.AddComponent<ShopItemTooltipProvider>();
        }
        tooltipProvider.Initialize(shopItemData, itemSO);
        tooltipTrigger.tooltipProvider = tooltipProvider;
    }

    private void OnBuyButtonClicked(ShopItemData shopItemData, ItemSO itemSO)
    {
        if (shopManager == null || shopManager.playerStats == null)
            return;

        // 골드 확인
        if (shopManager.playerStats.gold < shopItemData.price)
        {
            Debug.Log("골드가 부족합니다!");
            return;
        }

        // 재고 확인
        if (shopItemData.stock >= 0 && shopItemData.stock <= 0)
        {
            Debug.Log("재고가 없습니다!");
            return;
        }

        // 수량 입력 프롬프트 표시
        if (quantityPrompt != null)
        {
            int maxQuantity = shopItemData.stock < 0 ? 99 : shopItemData.stock;
            int affordableQuantity = shopManager.playerStats.gold / shopItemData.price;
            maxQuantity = Mathf.Min(maxQuantity, affordableQuantity);

            string title = $"{itemSO.DisplayName} 구매";
            string info = $"가격: {shopItemData.price}골드\n최대 구매 가능: {maxQuantity}개";

            quantityPrompt.Show(title, 1, maxQuantity, (quantity) =>
            {
                bool success = shopManager.BuyItem(shopItemData.itemID, quantity);
                if (success)
                {
                    UpdateGoldDisplay();
                    UpdateShopItems(); // 재고 업데이트를 위해
                    UpdateDescription(selectedShopItem);
                }
            }, info);
        }
        else
        {
            // 프롬프트가 없으면 1개만 구매
            bool success = shopManager.BuyItem(shopItemData.itemID, 1);
            if (success)
            {
                UpdateGoldDisplay();
                UpdateShopItems();
                UpdateDescription(selectedShopItem);
            }
        }
    }

    private void UpdateDescription(ShopItemData shopItemData)
    {
        // 모든 상세 설명은 툴팁으로 표시됨
        // 선택된 아이템 상태만 업데이트 (필요한 경우를 위해 유지)
    }

    // 골드 업데이트 (외부에서 호출 가능)
    public void OnGoldChanged()
    {
        if (isShopOpen)
        {
            UpdateGoldDisplay();
        }
    }
}

