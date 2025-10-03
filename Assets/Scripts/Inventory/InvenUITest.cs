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
    public QuantityPrompt quantityPrompt;

    private List<GameObject> slotObjects = new List<GameObject>();
    private bool isInventoryOpen = false;
    private InventorySlot selectedSlot; // 변경점: 설명/버튼 갱신을 슬롯 단위로 일관화

    void Start()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }

        // [추가] 인벤토리 자동 참조 시도
        if (inventory == null)
        {
            inventory = FindFirstObjectByType<Inventory>(); // 변경점
            if (inventory == null)
            {
                Debug.LogError("InventoryUI: Inventory를 찾을 수 없습니다. 참조를 연결해 주세요.", this);
                return;
            }
        }

        SubscribeToEvents();

        // [변경] 초기 UI 동기화 강화
        UpdateWeight();          // 기존 유지
        UpdateSlots();           // 변경점: 시작 시에도 슬롯 동기화
        UpdateDescription(null); // 변경점: 설명 초기화
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
        // [변경] 무게만 구독 → 공용 핸들러로 변경
        inventory.OnInventoryChanged -= OnInventoryChangedHandler; // 변경점: 중복 구독 방지
        inventory.OnInventoryChanged += OnInventoryChangedHandler; // 변경점: 무게/슬롯/설명 동시 반영
    }

    private void UnsubscribeFromEvents()
    {
        if (inventory != null)
        {
            // [변경] 핸들러 해제 대상 변경
            inventory.OnInventoryChanged -= OnInventoryChangedHandler;
        }
    }

    // [추가] 인벤토리 변경 시 공용 처리
    private void OnInventoryChangedHandler()
    {
        UpdateWeight();
        if (isInventoryOpen)
        {
            UpdateSlots();
            // [추가] 선택 유지: 기존에 선택했던 슬롯이 아직 유효하면 설명 갱신
            if (selectedSlot != null && selectedSlot.item != null)
                UpdateDescription(selectedSlot);
            else
                UpdateDescription(null);
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
            Time.timeScale = 0f;
            UpdateSlots();           // 인벤토리를 열 때 슬롯 새로고침
            UpdateDescription(null); // [추가] 설명 초기화
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

    // [변경] 접근 경로 유지, 표시 포맷만 현행화
    public void UpdateWeight()
    {
        if (inventory != null && weightText != null)
        {
            weightText.text = $"무게: {inventory.TotalWeight:F1} / {inventory.maxWeightLimit:F1}"; // 변경점: 한글 표기
        }
    }

    // [변경] 인자로 ItemSO가 아닌 InventorySlot도 받을 수 있게 오버로드 느낌으로 정리
    //public void UpdateDescription(ItemSO item)
    //{
    //    // [유지] 외부에서 ItemSO만 주는 호출 호환
    //    UpdateDescription(item != null ? new InventorySlot(item, 1) : null); // 변경점: 슬롯 기반으로 위임
    //}

    // [추가] 슬롯 기반 설명 갱신(일관성)
    public void UpdateDescription(InventorySlot slot)
    {
        selectedSlot = slot; // 변경점: 현재 선택 저장

        if (descriptionText == null)
            return;

        if (slot == null || slot.item == null)
        {
            descriptionText.text = string.Empty;
            return;
        }

        var iitem = slot.item; // IInventoryItem
        string name = iitem.DisplayName;       // 기존: slot.item.itemName
        string desc = string.Empty;            // [추가] 설명 안전 처리
        if (iitem is ItemSO itemSo)
            desc = itemSo.description;
        else if (iitem is MineralSO mineralSo)
            desc = mineralSo.description;
        float weight = iitem.Weight;           // 기존: slot.item.weight
        int qty = slot.quantity;
        bool stackable = iitem.Stackable;
        int maxStack = iitem.MaxStackSize;
        // [추가] 스택 표기 보조 문자열
        string stackInfo = stackable
            ? (maxStack > 0 ? $"(최대 {maxStack})" : "(스택 가능)")
            : "(스택 불가)";
        // [추가] 과적 경고(선택)
        string encum = (inventory != null && inventory.IsEncumbered) ? "\n[경고] 과적 상태입니다." : string.Empty;
        // [변경] 최종 표기(필요에 맞게 포맷 조정 가능)
        descriptionText.text =
            $"{name}\n" +
            $"{desc}\n" +
            $"무게: {weight:0.0}  수량: {qty} {stackInfo}" +
            $"{encum}";
    }

    public void UpdateSlots()
    {
        if (!isInventoryOpen) return;

        foreach (GameObject slot in slotObjects)
        {
            Destroy(slot);
        }
        slotObjects.Clear();

        if (slotContainer == null || inventorySlotPrefab == null || inventory == null) return;

        var list = inventory.ReadonlyItems; 

        for (int i = 0; i < list.Count; i++)
        {
            var itemSlot = list[i];
            if (itemSlot == null || itemSlot.item == null) continue;

            GameObject newSlot = Instantiate(inventorySlotPrefab, slotContainer);
            slotObjects.Add(newSlot);

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
            if (slotButton == null)
            {
                Debug.LogWarning($"[InventoryUI] Slot prefab '{inventorySlotPrefab.name}'에 Button이 없습니다.", newSlot);
            }
            else
            {
                slotButton.onClick.RemoveAllListeners();
                slotButton.onClick.AddListener(() => UpdateDescription(itemSlot));
            }

            Button dropAllButton = newSlot.transform.Find("DropAllButton")?.GetComponent<Button>();
            if (dropAllButton != null)
            {
                dropAllButton.onClick.RemoveAllListeners();
                dropAllButton.onClick.AddListener(() =>
                {
                    int removed = inventory.RemoveAllOf(itemSlot.item); 
                    if (removed > 0)
                    {
                        UpdateSlots();
                    }
                });
            }

            Button dropSingleButton = newSlot.transform.Find("DropSingleButton")?.GetComponent<Button>();
            if (dropSingleButton != null)
            {
                bool shouldBeActive = itemSlot.item.Stackable ? itemSlot.quantity > 0 : true; 
                dropSingleButton.gameObject.SetActive(shouldBeActive);
                if (shouldBeActive)
                {
                    dropSingleButton.onClick.RemoveAllListeners();
                    dropSingleButton.onClick.AddListener(() =>
                    {
                        bool ok = inventory.RemoveItem(itemSlot.item, 1); 
                        if (ok)
                        {
                            UpdateSlots();
                        }
                    });
                }
            }

            Button dropAmountButton = newSlot.transform.Find("DropAmountButton")?.GetComponent<Button>();
            if (dropAmountButton != null)
            {
                int totalCount = inventory.CountOf(itemSlot.item);
                bool shouldBeActive = totalCount > 0;
                dropAmountButton.gameObject.SetActive(shouldBeActive);

                if (shouldBeActive)
                {
                    dropAmountButton.onClick.RemoveAllListeners();
                    dropAmountButton.onClick.AddListener(() =>
                    {
                        int total = inventory.CountOf(itemSlot.item);
                        if (total <= 0) return;

                        string title = $"{itemSlot.item.DisplayName} 버리기";
                        string info = $"현재 보유: {total}개\n버릴 개수를 입력하세요.";

                        if (quantityPrompt != null)
                        {
                            quantityPrompt.Show(
                                title,
                                1, total,
                                (value) =>
                                {
                                    bool ok = inventory.RemoveItem(itemSlot.item, value);
                                    if (ok)
                                    {
                                        UpdateSlots();
                                        // 선택 설명 갱신 로직이 있다면 필요 시:
                                        // UpdateDescription(null);
                                    }
                                    else
                                    {
                                        Debug.LogWarning("요청 수량만큼 제거하지 못했습니다.");
                                    }
                                },
                                info
                            );
                        }
                        else
                        {
                            Debug.LogWarning("QuantityPrompt 참조가 없습니다. 인스펙터에 연결해 주세요.");
                        }
                    });
                }
            }


        }

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
            UpdateDescription(firstValid);
        }
        else
        {
            UpdateDescription(null);
        }
    }
}