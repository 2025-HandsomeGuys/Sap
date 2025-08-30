using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using UnityEngine.InputSystem;

public class InventoryUI : MonoBehaviour
{
    [Header("UI 연결")]
    public GameObject inventoryPanel; // The entire inventory panel
    public Inventory inventory;
    public GameObject inventorySlotPrefab;
    public Transform slotContainer;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI weightText;

    private List<GameObject> slotObjects = new List<GameObject>();
    private bool isInventoryOpen = false;

    void Start()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false); // Start with inventory closed
        }

        if (inventory == null) return;
        inventory.OnInventoryChanged += UpdateUI;
    }

    void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnInventoryChanged -= UpdateUI;
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
        if (inventoryPanel == null) return;

        isInventoryOpen = !isInventoryOpen;
        inventoryPanel.SetActive(isInventoryOpen);

        if (isInventoryOpen)
        {
            Time.timeScale = 0f; // Pause the game
            UpdateUI(); // Update UI only when opening
        }
        else
        {
            Time.timeScale = 1f; // Resume the game
        }
    }

    public bool IsOpen()
    {
        return isInventoryOpen;
    }

    public void UpdateUI()
    {
        if (inventory == null) return;

        if (weightText != null)
        {
            weightText.text = $"Weight: {inventory.TotalWeight} / {inventory.maxWeightLimit}";
        }

        if (descriptionText != null)
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

        foreach (GameObject slot in slotObjects)
        {
            Destroy(slot);
        }
        slotObjects.Clear();

        if (slotContainer == null || inventorySlotPrefab == null) return;

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

            Button dropAllButton = newSlot.transform.Find("DropAllButton")?.GetComponent<Button>();
            if (dropAllButton != null)
            {
                dropAllButton.onClick.RemoveAllListeners();
                dropAllButton.onClick.AddListener(() => {
                    inventory.DropItem(itemSlot);
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
                    });
                }
            }
        }
    }
}