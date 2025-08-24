using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [Header("UI 연결")]
    public Inventory inventory;
    public GameObject inventorySlotPrefab;
    public Transform slotContainer;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI weightText;

    private List<GameObject> slotObjects = new List<GameObject>();

    void Start()
    {
        if (inventory == null) return;
        inventory.OnInventoryChanged += UpdateUI;
        UpdateUI();
    }

    void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnInventoryChanged -= UpdateUI;
        }
    }

    public void UpdateUI()
    {
        // 무게 텍스트 업데이트
        if (weightText != null)
        {
            weightText.text = $"Weight: {inventory.TotalWeight} / {inventory.maxWeightLimit}";
        }

        // 설명 텍스트 업데이트 (첫번째 아이템 기준)
        if (descriptionText != null)
        {
            if (inventory.items.Count > 0 && inventory.items[0].item != null)
            {
                descriptionText.text = inventory.items[0].item.description;
            }
            else
            {
                descriptionText.text = ""; // 인벤토리가 비었으면 설명도 비움
            }
        }

        // 슬롯들 다시 그리기
        foreach (GameObject slot in slotObjects)
        {
            Destroy(slot);
        }
        slotObjects.Clear();

        foreach (InventorySlot itemSlot in inventory.items)
        {
            GameObject newSlot = Instantiate(inventorySlotPrefab, slotContainer);
            slotObjects.Add(newSlot);

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

            // 전체 버리기 버튼 설정
            Button dropAllButton = newSlot.transform.Find("DropAllButton")?.GetComponent<Button>();
            if (dropAllButton != null)
            {
                dropAllButton.onClick.RemoveAllListeners();
                dropAllButton.onClick.AddListener(() => {
                    inventory.DropItem(itemSlot); // DropItem은 전체 슬롯을 제거
                });
            }

            // 한 개 버리기 버튼 설정
            Button dropSingleButton = newSlot.transform.Find("DropSingleButton")?.GetComponent<Button>();
            if (dropSingleButton != null)
            {
                // 아이템이 2개 이상일 때만 버튼을 활성화
                bool shouldBeActive = itemSlot.quantity > 1;
                dropSingleButton.gameObject.SetActive(shouldBeActive);

                if (shouldBeActive)
                {
                    dropSingleButton.onClick.RemoveAllListeners();
                    dropSingleButton.onClick.AddListener(() => {
                        inventory.DropSingleItem(itemSlot); // DropSingleItem은 1개만 제거
                    });
                }
            }
        }
    }
}
