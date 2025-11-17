using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using UnityEngine.InputSystem;

public class InventoryUI : MonoBehaviour
{
    [Header("UI 패널 연결")]
    public GameObject inventoryPanel; // 전체 인벤토리 패널
    public GameObject itemPanel;      // Items 패널 (위쪽)
    public GameObject mineralPanel;   // Minerals 패널 (가운데)
    public GameObject toolPanel;      // Tools 패널 (왼쪽)

    [Header("인벤토리 참조")]
    public ItemInventory itemInventory;
    public MineralInventory mineralInventory;
    public ToolInventory toolInventory;

    [Header("슬롯 프리팹")]
    public GameObject inventorySlotPrefab;

    [Header("Items 패널 UI")]
    public Transform itemSlotContainer;
    public TextMeshProUGUI itemDescriptionText;
    public TextMeshProUGUI itemCountText; // 개수 표시 (무게 대신)

    [Header("Minerals 패널 UI")]
    public Transform mineralSlotContainer;
    public TextMeshProUGUI mineralDescriptionText;
    public TextMeshProUGUI mineralWeightText; // 무게 표시

    [Header("Tools 패널 UI")]
    public Transform toolSlotContainer;
    public TextMeshProUGUI toolDescriptionText;
    public TextMeshProUGUI toolCountText; // 슬롯 수 표시

    [Header("기타")]
    public QuantityPrompt quantityPrompt;

    [Header("테스트 버튼 (개발용)")]
    public Button testAddItemButton;      // 아이템 추가 테스트 버튼
    public Button testAddMineralButton;  // 광물 추가 테스트 버튼
    public Button testAddToolButton;     // 도구 추가 테스트 버튼

    private List<GameObject> itemSlotObjects = new List<GameObject>();
    private List<GameObject> mineralSlotObjects = new List<GameObject>();
    private List<GameObject> toolSlotObjects = new List<GameObject>();

    private bool isInventoryOpen = false;
    private InventorySlot selectedItemSlot;
    private InventorySlot selectedMineralSlot;
    private InventorySlot selectedToolSlot;

    void Start()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }

        // 인벤토리 자동 참조 시도
        if (itemInventory == null)
            itemInventory = FindFirstObjectByType<ItemInventory>();
        if (mineralInventory == null)
            mineralInventory = FindFirstObjectByType<MineralInventory>();
        if (toolInventory == null)
            toolInventory = FindFirstObjectByType<ToolInventory>();

        // 참조 확인 로그
        if (itemInventory == null)
            Debug.LogWarning("[InventoryUI] ItemInventory를 찾을 수 없습니다!");
        if (mineralInventory == null)
            Debug.LogWarning("[InventoryUI] MineralInventory를 찾을 수 없습니다!");
        if (toolInventory == null)
            Debug.LogWarning("[InventoryUI] ToolInventory를 찾을 수 없습니다!");

        if (itemSlotContainer == null)
            Debug.LogWarning("[InventoryUI] itemSlotContainer가 설정되지 않았습니다!");
        if (mineralSlotContainer == null)
            Debug.LogWarning("[InventoryUI] mineralSlotContainer가 설정되지 않았습니다!");
        if (toolSlotContainer == null)
            Debug.LogWarning("[InventoryUI] toolSlotContainer가 설정되지 않았습니다!");
        if (inventorySlotPrefab == null)
            Debug.LogWarning("[InventoryUI] inventorySlotPrefab이 설정되지 않았습니다!");

        SubscribeToEvents();

        // 테스트 버튼 이벤트 연결
        SetupTestButtons();

        // 초기 UI 동기화
        UpdateItemCount();
        UpdateMineralWeight();
        UpdateToolCount();
        // Start에서는 인벤토리가 닫혀있으므로 슬롯 업데이트는 하지 않음
        // UpdateAllSlots();
        UpdateDescription(null, null, null);
    }

    private void SetupTestButtons()
    {
        // 아이템 추가 테스트 버튼
        if (testAddItemButton != null)
        {
            testAddItemButton.onClick.RemoveAllListeners();
            testAddItemButton.onClick.AddListener(() => TestAddItem());
        }

        // 광물 추가 테스트 버튼
        if (testAddMineralButton != null)
        {
            testAddMineralButton.onClick.RemoveAllListeners();
            testAddMineralButton.onClick.AddListener(() => TestAddMineral());
        }

        // 도구 추가 테스트 버튼
        if (testAddToolButton != null)
        {
            testAddToolButton.onClick.RemoveAllListeners();
            testAddToolButton.onClick.AddListener(() => TestAddTool());
        }
    }

    // 테스트 메서드들
    public void TestAddItem()
    {
        if (itemInventory == null)
        {
            Debug.LogWarning("[InventoryUI] ItemInventory를 찾을 수 없습니다!");
            return;
        }

        if (ItemDatabase.Instance == null || ItemDatabase.Instance.allItems == null || ItemDatabase.Instance.allItems.Count == 0)
        {
            Debug.LogWarning("[InventoryUI] ItemDatabase에 아이템이 없습니다!");
            return;
        }

        // 첫 번째 아이템 추가 (None이 아닌 것)
        ItemSO itemToAdd = null;
        foreach (var item in ItemDatabase.Instance.allItems)
        {
            if (item != null && item.itemID != ItemID.None)
            {
                itemToAdd = item;
                break;
            }
        }

        if (itemToAdd != null)
        {
            bool success = itemInventory.AddItem(itemToAdd, 1);
            if (success)
            {
                Debug.Log($"[Test] 아이템 추가 성공: {itemToAdd.itemName}");
            }
            else
            {
                Debug.LogWarning($"[Test] 아이템 추가 실패: {itemToAdd.itemName} (인벤토리 가득 참)");
            }
        }
        else
        {
            Debug.LogWarning("[InventoryUI] 추가할 수 있는 아이템을 찾을 수 없습니다!");
        }
    }

    public void TestAddMineral()
    {
        if (mineralInventory == null)
        {
            Debug.LogWarning("[InventoryUI] MineralInventory를 찾을 수 없습니다!");
            return;
        }

        if (MineralDatabase.Instance == null || MineralDatabase.Instance.allMinerals == null || MineralDatabase.Instance.allMinerals.Count == 0)
        {
            Debug.LogWarning("[InventoryUI] MineralDatabase에 광물이 없습니다!");
            return;
        }

        // 첫 번째 광물 추가 (None이 아닌 것)
        MineralSO mineralToAdd = null;
        foreach (var mineral in MineralDatabase.Instance.allMinerals)
        {
            if (mineral != null && mineral.mineralID != MineralID.None)
            {
                mineralToAdd = mineral;
                break;
            }
        }

        if (mineralToAdd != null)
        {
            bool success = mineralInventory.AddItem(mineralToAdd, 1);
            if (success)
            {
                Debug.Log($"[Test] 광물 추가 성공: {mineralToAdd.mineralName}");
            }
            else
            {
                Debug.LogWarning($"[Test] 광물 추가 실패: {mineralToAdd.mineralName} (인벤토리 가득 참 또는 무게 초과)");
            }
        }
        else
        {
            Debug.LogWarning("[InventoryUI] 추가할 수 있는 광물을 찾을 수 없습니다!");
        }
    }

    public void TestAddTool()
    {
        if (toolInventory == null)
        {
            Debug.LogWarning("[InventoryUI] ToolInventory를 찾을 수 없습니다!");
            return;
        }

        if (ToolDatabase.Instance == null || ToolDatabase.Instance.allTools == null || ToolDatabase.Instance.allTools.Count == 0)
        {
            Debug.LogWarning("[InventoryUI] ToolDatabase에 도구가 없습니다!");
            return;
        }

        // 첫 번째 도구 추가 (None이 아닌 것)
        ToolSO toolToAdd = null;
        foreach (var tool in ToolDatabase.Instance.allTools)
        {
            if (tool != null && tool.toolID != ToolID.None)
            {
                toolToAdd = tool;
                break;
            }
        }

        if (toolToAdd != null)
        {
            bool success = toolInventory.AddItem(toolToAdd, 1);
            if (success)
            {
                Debug.Log($"[Test] 도구 추가 성공: {toolToAdd.toolName}");
            }
            else
            {
                Debug.LogWarning($"[Test] 도구 추가 실패: {toolToAdd.toolName} (인벤토리 가득 참)");
            }
        }
        else
        {
            Debug.LogWarning("[InventoryUI] 추가할 수 있는 도구를 찾을 수 없습니다!");
        }
    }

    void Update()
    {
        // 'i' 키를 눌렀을 때 인벤토리 토글
        if (Input.GetKeyDown(KeyCode.I))
        {
            ToggleInventory();
        }
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void SubscribeToEvents()
    {
        if (itemInventory != null)
        {
            itemInventory.OnInventoryChanged -= OnItemInventoryChanged;
            itemInventory.OnInventoryChanged += OnItemInventoryChanged;
        }
        if (mineralInventory != null)
        {
            mineralInventory.OnInventoryChanged -= OnMineralInventoryChanged;
            mineralInventory.OnInventoryChanged += OnMineralInventoryChanged;
        }
        if (toolInventory != null)
        {
            toolInventory.OnInventoryChanged -= OnToolInventoryChanged;
            toolInventory.OnInventoryChanged += OnToolInventoryChanged;
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (itemInventory != null)
            itemInventory.OnInventoryChanged -= OnItemInventoryChanged;
        if (mineralInventory != null)
            mineralInventory.OnInventoryChanged -= OnMineralInventoryChanged;
        if (toolInventory != null)
            toolInventory.OnInventoryChanged -= OnToolInventoryChanged;
    }

    private void OnItemInventoryChanged()
    {
        UpdateItemCount();
        if (isInventoryOpen)
        {
            UpdateItemSlots();
            if (selectedItemSlot != null && selectedItemSlot.item != null)
                UpdateDescription(selectedItemSlot, null, null);
        }
    }

    private void OnMineralInventoryChanged()
    {
        UpdateMineralWeight();
        if (isInventoryOpen)
        {
            UpdateMineralSlots();
            if (selectedMineralSlot != null && selectedMineralSlot.item != null)
                UpdateDescription(null, selectedMineralSlot, null);
        }
    }

    private void OnToolInventoryChanged()
    {
        UpdateToolCount();
        if (isInventoryOpen)
        {
            UpdateToolSlots();
            if (selectedToolSlot != null && selectedToolSlot.item != null)
                UpdateDescription(null, null, selectedToolSlot);
        }
    }

    public void OnOpenInventory(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            ToggleInventory();
        }
    }

    public void ToggleInventory()
    {
        if (inventoryPanel == null)
        {
            Debug.LogWarning("[InventoryUI] inventoryPanel이 설정되지 않았습니다.");
            return;
        }

        isInventoryOpen = !isInventoryOpen;
        inventoryPanel.SetActive(isInventoryOpen);

        if (isInventoryOpen)
        {
            Time.timeScale = 0f;
            Debug.Log($"[InventoryUI] 인벤토리 열림 - Items: {itemInventory?.ReadonlyItems?.Count ?? 0}, Minerals: {mineralInventory?.ReadonlyItems?.Count ?? 0}, Tools: {toolInventory?.ReadonlyItems?.Count ?? 0}");
            UpdateAllSlots();
            UpdateDescription(null, null, null);
        }
        else
        {
            Time.timeScale = 1f;
        }
    }

    public bool IsOpen()
    {
        return isInventoryOpen;
    }

    // Items 패널 업데이트
    public void UpdateItemCount()
    {
        if (itemInventory != null && itemCountText != null)
        {
            itemCountText.text = $"아이템: {itemInventory.CurrentItemCount} / {itemInventory.maxItemCount}";
        }
    }

    // Minerals 패널 업데이트
    public void UpdateMineralWeight()
    {
        if (mineralInventory != null && mineralWeightText != null)
        {
            mineralWeightText.text = $"무게: {mineralInventory.TotalWeight:F1} / {mineralInventory.maxWeightLimit:F1}";
        }
    }

    // Tools 패널 업데이트
    public void UpdateToolCount()
    {
        if (toolInventory != null && toolCountText != null)
        {
            toolCountText.text = $"도구: {toolInventory.CurrentSlotCount} / {toolInventory.maxSlotCount}";
        }
    }

    // 설명 업데이트 (3개 패널 중 선택된 슬롯에 따라)
    public void UpdateDescription(InventorySlot itemSlot, InventorySlot mineralSlot, InventorySlot toolSlot)
    {
        selectedItemSlot = itemSlot;
        selectedMineralSlot = mineralSlot;
        selectedToolSlot = toolSlot;

        // Items 설명
        if (itemDescriptionText != null)
        {
            if (itemSlot != null && itemSlot.item != null)
            {
                var iitem = itemSlot.item;
                string name = iitem.DisplayName;
                string desc = string.Empty;
                if (iitem is ItemSO itemSo)
                    desc = itemSo.description;
                int qty = itemSlot.quantity;
                bool stackable = iitem.Stackable;
                int maxStack = iitem.MaxStackSize;
                string stackInfo = stackable
                    ? (maxStack > 0 ? $"(최대 {maxStack})" : "(스택 가능)")
                    : "(스택 불가)";
                itemDescriptionText.text = $"{name}\n{desc}\n수량: {qty} {stackInfo}";
            }
            else
            {
                itemDescriptionText.text = string.Empty;
            }
        }

        // Minerals 설명
        if (mineralDescriptionText != null)
        {
            if (mineralSlot != null && mineralSlot.item != null)
            {
                var iitem = mineralSlot.item;
                string name = iitem.DisplayName;
                string desc = string.Empty;
                if (iitem is MineralSO mineralSo)
                    desc = mineralSo.description;
                float weight = iitem.Weight;
                int qty = mineralSlot.quantity;
                bool stackable = iitem.Stackable;
                int maxStack = iitem.MaxStackSize;
                string stackInfo = stackable
                    ? (maxStack > 0 ? $"(최대 {maxStack})" : "(스택 가능)")
                    : "(스택 불가)";
                string encum = (mineralInventory != null && mineralInventory.IsEncumbered) ? "\n[경고] 과적 상태입니다." : string.Empty;
                mineralDescriptionText.text = $"{name}\n{desc}\n무게: {weight:0.0}  수량: {qty} {stackInfo}{encum}";
            }
            else
            {
                mineralDescriptionText.text = string.Empty;
            }
        }

        // Tools 설명
        if (toolDescriptionText != null)
        {
            if (toolSlot != null && toolSlot.item != null)
            {
                var iitem = toolSlot.item;
                string name = iitem.DisplayName;
                string desc = string.Empty;
                if (iitem is ToolSO toolSo)
                    desc = toolSo.description;
                int qty = toolSlot.quantity;
                toolDescriptionText.text = $"{name}\n{desc}\n수량: {qty}";
            }
            else
            {
                toolDescriptionText.text = string.Empty;
            }
        }
    }

    public void UpdateAllSlots()
    {
        // 인벤토리가 열려있을 때만 슬롯 업데이트
        if (!isInventoryOpen) return;
        UpdateItemSlots();
        UpdateMineralSlots();
        UpdateToolSlots();
    }

    // Items 슬롯 업데이트
    private void UpdateItemSlots()
    {
        // 인벤토리가 열려있지 않으면 업데이트하지 않음
        if (!isInventoryOpen) return;

        foreach (GameObject slot in itemSlotObjects)
        {
            Destroy(slot);
        }
        itemSlotObjects.Clear();

        if (itemSlotContainer == null || inventorySlotPrefab == null || itemInventory == null)
        {
            Debug.LogWarning("[InventoryUI] Item 슬롯 업데이트 실패: 필수 참조가 없습니다.");
            return;
        }

        var list = itemInventory.ReadonlyItems;
        Debug.Log($"[InventoryUI] UpdateItemSlots: {list.Count}개의 아이템 슬롯 생성 중...");

        for (int i = 0; i < list.Count; i++)
        {
            var itemSlot = list[i];
            if (itemSlot == null || itemSlot.item == null) continue;

            GameObject newSlot = Instantiate(inventorySlotPrefab, itemSlotContainer);
            itemSlotObjects.Add(newSlot);

            SetupSlotUI(newSlot, itemSlot, (slot) =>
            {
                UpdateDescription(slot, null, null);
            }, (item) =>
            {
                if (item is ItemSO itemSO)
                {
                    int removed = itemInventory.RemoveAllOf(itemSO);
                    if (removed > 0) UpdateItemSlots();
                }
            }, (item) =>
            {
                if (item is ItemSO itemSO)
                {
                    bool ok = itemInventory.RemoveItem(itemSO, 1);
                    if (ok) UpdateItemSlots();
                }
            }, (item) =>
            {
                if (item is ItemSO itemSO)
                {
                    int total = itemInventory.CountOf(itemSO);
                    if (total <= 0) return;

                    string title = $"{itemSO.DisplayName} 버리기";
                    string info = $"현재 보유: {total}개\n버릴 개수를 입력하세요.";

                    if (quantityPrompt != null)
                    {
                        quantityPrompt.Show(title, 1, total, (value) =>
                        {
                            bool ok = itemInventory.RemoveItem(itemSO, value);
                            if (ok) UpdateItemSlots();
                        }, info);
                    }
                }
            });
        }

        // 첫 번째 아이템 설명 표시
        if (list.Count > 0)
        {
            InventorySlot firstValid = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].item != null)
                {
                    firstValid = list[i];
                    break;
                }
            }
            UpdateDescription(firstValid, null, null);
        }
        else
        {
            UpdateDescription(null, null, null);
        }
    }

    // Minerals 슬롯 업데이트
    private void UpdateMineralSlots()
    {
        // 인벤토리가 열려있지 않으면 업데이트하지 않음
        if (!isInventoryOpen) return;

        foreach (GameObject slot in mineralSlotObjects)
        {
            Destroy(slot);
        }
        mineralSlotObjects.Clear();

        if (mineralSlotContainer == null || inventorySlotPrefab == null || mineralInventory == null)
        {
            Debug.LogWarning("[InventoryUI] Mineral 슬롯 업데이트 실패: 필수 참조가 없습니다.");
            return;
        }

        var list = mineralInventory.ReadonlyItems;
        Debug.Log($"[InventoryUI] UpdateMineralSlots: {list.Count}개의 광물 슬롯 생성 중...");

        for (int i = 0; i < list.Count; i++)
        {
            var itemSlot = list[i];
            if (itemSlot == null || itemSlot.item == null) continue;

            GameObject newSlot = Instantiate(inventorySlotPrefab, mineralSlotContainer);
            mineralSlotObjects.Add(newSlot);

            SetupSlotUI(newSlot, itemSlot, (slot) =>
            {
                UpdateDescription(null, slot, null);
            }, (item) =>
            {
                if (item is MineralSO mineralSO)
                {
                    int removed = mineralInventory.RemoveAllOf(mineralSO);
                    if (removed > 0) UpdateMineralSlots();
                }
            }, (item) =>
            {
                if (item is MineralSO mineralSO)
                {
                    bool ok = mineralInventory.RemoveItem(mineralSO, 1);
                    if (ok) UpdateMineralSlots();
                }
            }, (item) =>
            {
                if (item is MineralSO mineralSO)
                {
                    int total = mineralInventory.CountOf(mineralSO);
                    if (total <= 0) return;

                    string title = $"{mineralSO.DisplayName} 버리기";
                    string info = $"현재 보유: {total}개\n버릴 개수를 입력하세요.";

                    if (quantityPrompt != null)
                    {
                        quantityPrompt.Show(title, 1, total, (value) =>
                        {
                            bool ok = mineralInventory.RemoveItem(mineralSO, value);
                            if (ok) UpdateMineralSlots();
                        }, info);
                    }
                }
            });
        }

        // 첫 번째 광물 설명 표시
        if (list.Count > 0)
        {
            InventorySlot firstValid = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].item != null)
                {
                    firstValid = list[i];
                    break;
                }
            }
            UpdateDescription(null, firstValid, null);
        }
        else
            UpdateDescription(null, null, null);
    }

    // Tools 슬롯 업데이트
    private void UpdateToolSlots()
    {
        // 인벤토리가 열려있지 않으면 업데이트하지 않음
        if (!isInventoryOpen) return;

        foreach (GameObject slot in toolSlotObjects)
        {
            Destroy(slot);
        }
        toolSlotObjects.Clear();

        if (toolSlotContainer == null || inventorySlotPrefab == null || toolInventory == null)
        {
            Debug.LogWarning("[InventoryUI] Tool 슬롯 업데이트 실패: 필수 참조가 없습니다.");
            return;
        }

        var list = toolInventory.ReadonlyItems;
        Debug.Log($"[InventoryUI] UpdateToolSlots: {list.Count}개의 도구 슬롯 생성 중...");

        for (int i = 0; i < list.Count; i++)
        {
            var itemSlot = list[i];
            if (itemSlot == null || itemSlot.item == null) continue;

            GameObject newSlot = Instantiate(inventorySlotPrefab, toolSlotContainer);
            toolSlotObjects.Add(newSlot);

            SetupSlotUI(newSlot, itemSlot, (slot) =>
            {
                UpdateDescription(null, null, slot);
            }, (item) =>
            {
                if (item is ToolSO toolSO)
                {
                    int removed = toolInventory.RemoveAllOf(toolSO);
                    if (removed > 0) UpdateToolSlots();
                }
            }, (item) =>
            {
                if (item is ToolSO toolSO)
                {
                    bool ok = toolInventory.RemoveItem(toolSO, 1);
                    if (ok) UpdateToolSlots();
                }
            // }, null); // Tools는 수량 지정 버리기 없음
            // 아래는 임시로 추가한 수량 지정 버리기 핸들러
            }, (item) =>
            {
                if (item is ToolSO toolSO)
                {
                    int total = toolInventory.CountOf(toolSO);
                    if (total <= 0) return;

                    string title = $"{toolSO.DisplayName} 버리기";
                    string info = $"현재 보유: {total}개\n버릴 개수를 입력하세요.";

                    if (quantityPrompt != null)
                    {
                        quantityPrompt.Show(title, 1, total, (value) =>
                        {
                            bool ok = toolInventory.RemoveItem(toolSO, value);
                            if (ok) UpdateToolSlots();
                        }, info);
                    }
                }
            });
        }

        // 첫 번째 도구 설명 표시
        if (list.Count > 0)
        {
            InventorySlot firstValid = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].item != null)
                {
                    firstValid = list[i];
                    break;
                }
            }
            UpdateDescription(null, null, firstValid);
        }
        else
            UpdateDescription(null, null, null);
    }

    // 슬롯 UI 설정 헬퍼 메서드
    private void SetupSlotUI(GameObject newSlot, InventorySlot itemSlot,
        System.Action<InventorySlot> onSlotClick,
        System.Action<InterfaceInventoryItem> onDropAll,
        System.Action<InterfaceInventoryItem> onDropSingle,
        System.Action<InterfaceInventoryItem> onDropAmount)
    {
        Image icon = newSlot.transform.Find("ItemIcon")?.GetComponent<Image>();
        TextMeshProUGUI quantityText = newSlot.transform.Find("ItemQuantity")?.GetComponent<TextMeshProUGUI>();

        if (icon != null)
        {
            icon.sprite = itemSlot.item.Icon;
            icon.enabled = (icon.sprite != null);
        }

        if (quantityText != null)
        {
            quantityText.text = (itemSlot.item.Stackable && itemSlot.quantity > 1)
                ? itemSlot.quantity.ToString()
                : string.Empty;
        }

        Button slotButton = newSlot.GetComponent<Button>();
        if (slotButton != null)
        {
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(() => onSlotClick(itemSlot));
        }

        Button dropAllButton = newSlot.transform.Find("DropAllButton")?.GetComponent<Button>();
        if (dropAllButton != null && onDropAll != null)
        {
            dropAllButton.onClick.RemoveAllListeners();
            dropAllButton.onClick.AddListener(() => onDropAll(itemSlot.item));
        }

        Button dropSingleButton = newSlot.transform.Find("DropSingleButton")?.GetComponent<Button>();
        if (dropSingleButton != null && onDropSingle != null)
        {
            bool shouldBeActive = itemSlot.item.Stackable ? itemSlot.quantity > 0 : true;
            dropSingleButton.gameObject.SetActive(shouldBeActive);
            if (shouldBeActive)
            {
                dropSingleButton.onClick.RemoveAllListeners();
                dropSingleButton.onClick.AddListener(() => onDropSingle(itemSlot.item));
            }
        }

        Button dropAmountButton = newSlot.transform.Find("DropAmountButton")?.GetComponent<Button>();
        if (dropAmountButton != null && onDropAmount != null)
        {
            dropAmountButton.gameObject.SetActive(true);
            dropAmountButton.onClick.RemoveAllListeners();
            dropAmountButton.onClick.AddListener(() => onDropAmount(itemSlot.item));
        }
    }
}
