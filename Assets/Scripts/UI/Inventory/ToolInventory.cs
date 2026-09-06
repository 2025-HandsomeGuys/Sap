using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

/// <summary>
/// 장비(도구) 인벤토리를 관리하는 클래스.
/// 머리(0), 옷(1), 신발(2) 전용 슬롯 3개로 고정 운영됩니다.
/// </summary>
public class ToolInventory : MonoBehaviour
{
    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    [Header("장비 슬롯 목록 (고정 3슬롯)")]
    [Tooltip("Index 0: 머리, Index 1: 옷, Index 2: 신발")]
    [SerializeField] private List<InventorySlot> items = new List<InventorySlot>();

    // 하위 호환성을 위한 프로퍼티 및 메서드
    public int maxSlotCount => 3;
    public int CurrentSlotCount => items.Count(s => s.item != null);

    public int CountOf(InterfaceInventoryItem target)
    {
        if (target == null) return 0;
        return items.Count(s => s.item != null && s.item.Id == target.Id);
    }

    private void Awake()
    {
        EnsureThreeSlots();
    }

    /// <summary>
    /// 항상 3개의 슬롯을 유지하도록 보장합니다.
    /// </summary>
    private void EnsureThreeSlots()
    {
        while (items.Count < 3)
        {
            items.Add(new InventorySlot(null, 0));
        }
        if (items.Count > 3)
        {
            items.RemoveRange(3, items.Count - 3);
        }
    }

    public IReadOnlyList<InventorySlot> ReadonlyItems => items;

    /// <summary>
    /// 특정 장비 타입에 해당하는 슬롯 인덱스를 반환합니다.
    /// </summary>
    private int GetSlotIndex(EquipmentType type)
    {
        switch (type)
        {
            case EquipmentType.Head: return 0;
            case EquipmentType.Clothes: return 1;
            case EquipmentType.Shoes: return 2;
            default: return -1;
        }
    }

    /// <summary>
    /// 도구/장비 추가 시도 (장비 타입에 따라 자동 슬롯 배정)
    /// </summary>
    public int AddItem(InterfaceInventoryItem itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return 0;

        EnsureThreeSlots();

        // EquipmentSO 또는 ToolSO 타입 확인
        EquipmentType equipType = EquipmentType.None;
        
        if (itemToAdd is EquipmentSO equipmentSO)
        {
            equipType = equipmentSO.equipmentType;
        }
        else
        {
            Debug.LogWarning($"[ToolInventory] {itemToAdd.DisplayName}은 장비가 아닙니다.");
            return 0;
        }

        int slotIndex = GetSlotIndex(equipType);
        if (slotIndex == -1)
        {
            Debug.LogWarning($"[ToolInventory] {itemToAdd.DisplayName}은 유효한 장비 타입이 아닙니다 (None).");
            return 0;
        }

        InventorySlot slot = items[slotIndex];

        // 1. 이미 다른 종류의 아이템이 들어있는 경우 (교체하지 않고 실패 처리)
        if (slot.item != null && slot.item.Id != itemToAdd.Id)
        {
            Debug.Log($"[ToolInventory] {slotIndex}번 슬롯에 이미 다른 도구({slot.item.DisplayName})가 있습니다.");
            return 0;
        }

        int addedCount = 0;
        if (itemToAdd.Stackable)
        {
            if (slot.item == null)
            {
                slot.item = itemToAdd;
                slot.quantity = 0;
            }

            int canAdd = (itemToAdd.MaxStackSize > 0) ? (itemToAdd.MaxStackSize - slot.quantity) : quantity;
            addedCount = Mathf.Min(canAdd, quantity);
            slot.AddQuantity(addedCount);
        }
        else
        {
            // 비스택형 (일반 도구/장비)
            if (slot.item == null)
            {
                slot.item = itemToAdd;
                slot.quantity = 1;
                addedCount = 1;
            }
            else
            {
                addedCount = 0;
            }
        }

        if (addedCount > 0) OnInventoryChanged?.Invoke();
        return addedCount;
    }

    // ToolSO를 받는 오버로드 (하위 호환성)
    public int AddItem(ToolSO itemToAdd, int quantity = 1)
    {
        Debug.LogWarning("[ToolInventory] ToolSO는 더 이상 지원되지 않습니다. EquipmentSO를 사용하세요.");
        return 0;
    }

    public bool RemoveItem(InterfaceInventoryItem itemToRemove, int quantity = 1)
    {
        if (itemToRemove == null) return false;
        
        EquipmentType equipType = EquipmentType.None;
        if (itemToRemove is EquipmentSO equipmentSO)
        {
            equipType = equipmentSO.equipmentType;
        }
        else
        {
            return false;
        }
        
        int index = GetSlotIndex(equipType);
        if (index != -1 && items[index].item != null && items[index].item.Id == itemToRemove.Id)
        {
            return RemoveItemAt(index, quantity);
        }
        return false;
    }

    // ToolSO 받는 오버로드 (하위 호환성, 더 이상 작동하지 않음)
    public bool RemoveItem(ToolSO itemToRemove, int quantity = 1)
    {
        Debug.LogWarning("[ToolInventory] ToolSO는 더 이상 지원되지 않습니다. EquipmentSO를 사용하세요.");
        return false;
    }

    public bool RemoveItemAt(int index, int quantity = 1)
    {
        if (index < 0 || index >= items.Count) return false;
        
        InventorySlot slot = items[index];
        if (slot.item == null || slot.quantity < quantity) return false;

        slot.quantity -= quantity;
        if (slot.quantity <= 0)
        {
            slot.item = null;
            slot.quantity = 0;
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    public int RemoveAllOf(InterfaceInventoryItem itemToRemove)
    {
        if (itemToRemove == null) return 0;
        
        EquipmentType equipType = EquipmentType.None;
        if (itemToRemove is EquipmentSO equipmentSO)
        {
            equipType = equipmentSO.equipmentType;
        }
        else
        {
            return 0;
        }
        
        int index = GetSlotIndex(equipType);
        if (index != -1 && items[index].item != null && items[index].item.Id == itemToRemove.Id)
        {
            int qty = items[index].quantity;
            RemoveItemAt(index, qty);
            return qty;
        }
        return 0;
    }

    // ToolSO를 받는 오버로드 (하위 호환성)
    // Tool SO를 받는 오버로드 (하위 호환성)
    public int RemoveAllOf(ToolSO itemToRemove)
    {
        Debug.LogWarning("[ToolInventory] ToolSO는 더 이상 지원되지 않습니다. EquipmentSO를 사용하세요.");
        return 0;
    }

    public EquipmentInventoryData ToData()
    {
        var data = new EquipmentInventoryData();
        for (int i = 0; i < items.Count; i++)
        {
            var slot = items[i];
            if (slot.item is EquipmentSO equipmentSO)
            {
                data.slots.Add(new EquipmentSlotData
                {
                    itemId = equipmentSO.equipmentID.ToString(),
                    quantity = slot.quantity
                });
            }
        }
        return data;
    }

    public void FromData(EquipmentInventoryData loadedData, EquipmentDatabase equipmentDb)
    {
        // 슬롯 초기화
        EnsureThreeSlots();
        for (int i = 0; i < items.Count; i++)
        {
            items[i].item = null;
            items[i].quantity = 0;
        }

        if (loadedData == null || loadedData.slots == null)
        {
            OnInventoryChanged?.Invoke();
            return;
        }

        foreach (var s in loadedData.slots)
        {
            try
            {
                if (equipmentDb != null)
                {
                    var equipmentId = (EquipmentID)System.Enum.Parse(typeof(EquipmentID), s.itemId);
                    var so = equipmentDb.GetEquipmentByID(equipmentId);
                    if (so != null)
                    {
                        AddItem(so, s.quantity);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"장비 ID 파싱 실패: {s.itemId} / {e.Message}");
            }
        }
    }

    public bool SwapSlots(int index1, int index2) { return false; }
    public bool MoveSlot(int fromIndex, int toIndex) { return false; }
}
