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

    // Unity Lifecycle Methods
    void Start()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }
        SubscribeToEvents();
        UpdateUI(); // 초기 UI 상태 설정
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    // Event Handling
    private void SubscribeToEvents()
    {
        if (inventory == null)
        {
            Debug.LogWarning("InventoryUI: 인벤토리 참조가 설정되지 않았습니다.", this);
            return;
        }
        // 무게와 같이 자주 바뀌는 UI는 가벼운 함수를 이벤트에 연결
        inventory.OnInventoryChanged += UpdateWeightAndDescription;
    }

    private void UnsubscribeFromEvents()
    {
        if (inventory != null)
        {
            inventory.OnInventoryChanged -= UpdateWeightAndDescription;
        }
    }

    // Inventory Toggling
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
            // 인벤토리를 열 때만 전체 UI를 새로고침 (슬롯 포함)
            UpdateSlots();
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

    // 전체 UI 업데이트 (초기화 시 호출)
    public void UpdateUI()
    {
        if (inventory == null) return;
        UpdateWeightAndDescription();
        UpdateSlots();
    }

    // 무게와 설명 텍스트만 업데이트 (가벼운 작업, 아이템 변경 시마다 호출)
    public void UpdateWeightAndDescription()
    {
        if (inventory != null && weightText != null)
        {
            Debug.Log($"[InventoryUI] Event received. Updating weight display. New Total Weight: {inventory.TotalWeight}");
            weightText.text = $"Weight: {inventory.TotalWeight:F1} / {inventory.maxWeightLimit:F1}";
        }
        if (descriptionText != null && inventory != null)
        {
            if (inventory.items.Count > 0 && inventory.items[0].item != null)
            {
                descriptionText.text = inventory.items[0].item.description;
            }
            else
            {
                descriptionText.text = "";
            }
        }
    }

    // 슬롯 UI만 업데이트 (무거운 작업)
    public void UpdateSlots()
    {
        if (!isInventoryOpen) return; // 인벤토리가 닫혀있으면 슬롯 업데이트 안함

        // 기존 슬롯 오브젝트 정리
        foreach (GameObject slot in slotObjects)
        {
            Destroy(slot);
        }
        slotObjects.Clear();

        if (slotContainer == null || inventorySlotPrefab == null || inventory == null) return;

        // 인벤토리 데이터에 따라 새 슬롯 생성
        foreach (InventorySlot itemSlot in inventory.items)
        {
            GameObject newSlot = Instantiate(inventorySlotPrefab, slotContainer);
            slotObjects.Add(newSlot);

            // 슬롯 UI 요소 설정 (아이콘, 수량 등)
            Image icon = newSlot.transform.Find("ItemIcon").GetComponent<Image>();
            TextMeshProUGUI quantityText = newSlot.transform.Find("ItemQuantity").GetComponent<TextMeshProUGUI>();

            if (icon != null && itemSlot.item != null) icon.sprite = itemSlot.item.icon;
            icon.enabled = (icon.sprite != null);

            if (quantityText != null)
            {
                if (itemSlot.item != null && itemSlot.item.stackable)
                {
                    quantityText.text = itemSlot.quantity.ToString();
                }
                else
                {
                    quantityText.text = "";
                }
            }
            
            // 버튼 리스너 설정
            Button dropAllButton = newSlot.transform.Find("DropAllButton")?.GetComponent<Button>();
            if (dropAllButton != null)
            {
                dropAllButton.onClick.RemoveAllListeners();
                dropAllButton.onClick.AddListener(() => {
                    inventory.DropItem(itemSlot);
                    UpdateSlots(); // 슬롯이 변경되었으므로 슬롯만 다시 그림
                });
            }

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
                        UpdateSlots(); // 슬롯이 변경되었으므로 슬롯만 다시 그림
                    });
                }
            }
        }
    }
}
