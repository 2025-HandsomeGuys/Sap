using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using UnityEngine.InputSystem;

public class InventoryUI : MonoBehaviour
{
    [Header("UI 연결")]
    public GameObject inventoryPanel;
    public Inventory inventory;
    public GameObject inventorySlotPrefab;
    public Transform slotContainer;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI weightText;

    private List<GameObject> slotObjects = new List<GameObject>();
    private bool isInventoryOpen = false;

    // --- Unity Lifecycle & Event Handling ---
    void Start()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }
        SubscribeToEvents();
        // 초기 UI 상태 설정
        UpdateWeight(); 
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void SubscribeToEvents()
    {
        if (inventory == null)
        {
            Debug.LogWarning("InventoryUI: 인벤토리 참조가 설정되지 않았습니다.", this);
            return;
        }
        inventory.OnInventoryChanged += UpdateWeight;
    }

    private void UnsubscribeFromEvents()
    {
        if (inventory != null)
        {
            inventory.OnInventoryChanged -= UpdateWeight;
        }
    }

    // --- Inventory Toggling ---
    public void OnOpenInventory(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            ToggleInventory();
        }
    }

    public void ToggleInventory()
    {
        if (inventoryPanel == null) return;

        isInventoryOpen = !isInventoryOpen;
        inventoryPanel.SetActive(isInventoryOpen);

        if (isInventoryOpen)
        {
            Time.timeScale = 0f;
            UpdateSlots(); // 인벤토리를 열 때 슬롯과 설명을 모두 새로고침
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

    // --- UI Update Logic ---

    // (가벼운 작업) 무게 텍스트만 업데이트. 아이템 변경 시마다 이벤트로 호출됨.
    public void UpdateWeight()
    {
        if (inventory != null && weightText != null)
        {
            weightText.text = $"Weight: {inventory.TotalWeight:F1} / {inventory.maxWeightLimit:F1}";
        }
    }

        // (가벼운 작업) 설명 텍스트만 업데이트. 슬롯 클릭 시 호출됨.
    public void UpdateDescription(Item item)
    {
        string itemName = (item != null) ? item.itemName : "NULL";
        Debug.Log($"[InventoryUI] UpdateDescription called for item: {itemName}");

        if (descriptionText != null)
        {
            descriptionText.text = item != null ? item.description : "";
        }
    }

    // (무거운 작업) 슬롯 전체를 다시 그림. 인벤토리 열 때 또는 아이템 버릴 때 호출됨.
    public void UpdateSlots()
    {
        if (!isInventoryOpen) return;

        foreach (GameObject slot in slotObjects)
        {
            Destroy(slot);
        }
        slotObjects.Clear();

        if (slotContainer == null || inventorySlotPrefab == null || inventory == null) return;

        foreach (InventorySlot itemSlot in inventory.items)
        {
            GameObject newSlot = Instantiate(inventorySlotPrefab, slotContainer);
            slotObjects.Add(newSlot);

            // --- 슬롯 내용 설정 (아이콘, 수량) ---
            Image icon = newSlot.transform.Find("ItemIcon").GetComponent<Image>();
            TextMeshProUGUI quantityText = newSlot.transform.Find("ItemQuantity").GetComponent<TextMeshProUGUI>();
            if (icon != null && itemSlot.item != null) icon.sprite = itemSlot.item.icon;
            icon.enabled = (icon.sprite != null);
            if (quantityText != null)
            {
                quantityText.text = (itemSlot.item != null && itemSlot.item.stackable) ? itemSlot.quantity.ToString() : "";
            }

            // --- 버튼 리스너 설정 ---
            // 1. 슬롯 클릭 시 설명 업데이트
            Button slotButton = newSlot.GetComponent<Button>();
            if (slotButton == null)
            {
                Debug.LogWarning($"[InventoryUI] Slot prefab '{inventorySlotPrefab.name}' is missing a Button component! Cannot set click listener for description.", newSlot);
            }
            else
            {
                slotButton.onClick.RemoveAllListeners();
                slotButton.onClick.AddListener(() => UpdateDescription(itemSlot.item));
            }

            // 2. 모두 버리기 버튼
            Button dropAllButton = newSlot.transform.Find("DropAllButton")?.GetComponent<Button>();
            if (dropAllButton != null)
            {
                dropAllButton.onClick.RemoveAllListeners();
                dropAllButton.onClick.AddListener(() => {
                    inventory.DropItem(itemSlot);
                    UpdateSlots(); // 슬롯 목록이 바뀌었으므로 다시 그림
                });
            }

            // 3. 하나 버리기 버튼
            Button dropSingleButton = newSlot.transform.Find("DropSingleButton")?.GetComponent<Button>();
            if (dropSingleButton != null)
            {
                bool shouldBeActive = itemSlot.quantity > 1;
                dropSingleButton.gameObject.SetActive(shouldBeActive);
                if (shouldBeActive)
                {
                    dropSingleButton.onClick.RemoveAllListeners();
                    dropSingleButton.onClick.AddListener(() => {
                        inventory.DropSingleItem(itemSlot);
                        UpdateSlots(); // 슬롯 목록이 바뀌었으므로 다시 그림
                    });
                }
            }
        }

        // 인벤토리를 열었을 때 기본 설명 설정
        if (inventory.items.Count > 0)
        {
            UpdateDescription(inventory.items[0].item);
        }
        else
        {
            UpdateDescription(null); // 인벤토리가 비었으면 설명도 비움
        }
    }
}