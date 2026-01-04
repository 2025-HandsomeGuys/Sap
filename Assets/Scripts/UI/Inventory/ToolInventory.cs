using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

public class ToolInventory : MonoBehaviour
{
    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    [Header("슬롯 제한 설정")]
    [Tooltip("도구 인벤토리에 넣을 수 있는 최대 슬롯 수")]
    public int maxSlotCount = 10;

    [Header("도구 목록(내부 전용)")]
    [SerializeField] private List<InventorySlot> items = new List<InventorySlot>();

    [Header("선택 사항")]
    public Transform playerTransform; // 필요 시 사용

    public IReadOnlyList<InventorySlot> ReadonlyItems => items;

    public int CurrentSlotCount => items.Count;

    public int CountOf(InterfaceInventoryItem target)
    {
        if (target == null)
            return 0;
        int sum = 0;
        foreach (var s in items)
        {
            if (s.item == null)
                continue;
            if (s.item.Id != target.Id)
                continue;
            sum += s.item.Stackable ? s.quantity : 1;
        }
        return sum;
    }

    // 도구 추가 시도
    public bool AddItem(ToolSO itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return false;

        // 슬롯 제한 확인 (도구는 스택 불가이므로 개수 = 슬롯 수)
        if (CurrentSlotCount + quantity > maxSlotCount)
        {
            Debug.Log($"도구 인벤토리가 가득 찼습니다. (최대 {maxSlotCount}개)");
            return false;
        }

        // 도구는 스택 불가이므로 개수만큼 개별 슬롯
        for (int i = 0; i < quantity && CurrentSlotCount < maxSlotCount; i++)
        {
            items.Add(new InventorySlot(itemToAdd, 1));
        }

        Debug.Log($"[ToolInventory] Tool Added: {itemToAdd.DisplayName}, New Total Slots: {CurrentSlotCount}");
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool RemoveItem(ToolSO itemToRemove, int quantity = 1)
    {
        if (itemToRemove == null || quantity <= 0) return false;

        // 도구는 스택 불가이므로 슬롯 여러 개 제거
        int remain = quantity;
        for (int i = items.Count - 1; i >= 0 && remain > 0; i--)
        {
            var s = items[i];
            if (s.item != null && s.item.Id == itemToRemove.Id)
            {
                items.RemoveAt(i);
                remain--;
            }
        }
        if (remain > 0)
        {
            return false;
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    public int RemoveAllOf(ToolSO itemToRemove)
    {
        if (itemToRemove == null) return 0;

        int removed = 0;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            var s = items[i];
            if (s.item != null && s.item.Id == itemToRemove.Id)
            {
                items.RemoveAt(i);
                removed++;
            }
        }

        if (removed > 0) OnInventoryChanged?.Invoke();
        return removed;
    }

    public ToolInventoryData ToData()
    {
        var data = new ToolInventoryData();
        foreach (var slot in items)
        {
            if (slot.item is ToolSO toolSO)
            {
                data.slots.Add(new ToolSlotData
                {
                    itemId = toolSO.toolID.ToString(),
                    quantity = slot.quantity
                });
            }
        }
        return data;
    }

    public void FromData(ToolInventoryData loadedData, ToolDatabase toolDb)
    {
        items.Clear();

        if (loadedData == null || loadedData.slots == null || loadedData.slots.Count == 0)
        {
            Debug.Log("불러올 도구 인벤토리가 없어 빈 인벤토리로 시작합니다.");
            OnInventoryChanged?.Invoke();
            return;
        }

        foreach (var s in loadedData.slots)
        {
            try
            {
                if (toolDb != null)
                {
                    var toolId = (ToolID)System.Enum.Parse(typeof(ToolID), s.itemId);
                    var so = toolDb.GetToolByID(toolId);
                    if (so != null)
                    {
                        // 도구는 스택 불가이므로 개수만큼 슬롯 생성
                        for (int i = 0; i < s.quantity && CurrentSlotCount < maxSlotCount; i++)
                        {
                            items.Add(new InventorySlot(so, 1));
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"ID {s.itemId}에 해당하는 도구를 찾지 못했습니다.");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"도구 ID 파싱 실패: {s.itemId} / {e.Message}");
            }
        }

        OnInventoryChanged?.Invoke();
    }

    // 슬롯 순서 변경 메서드들 (드래그 앤 드롭용)
    public bool SwapSlots(int index1, int index2)
    {
        if (index1 < 0 || index1 >= items.Count || index2 < 0 || index2 >= items.Count)
            return false;
        if (index1 == index2) return true;

        var temp = items[index1];
        items[index1] = items[index2];
        items[index2] = temp;

        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool MoveSlot(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= items.Count || toIndex < 0 || toIndex >= items.Count)
            return false;
        if (fromIndex == toIndex) return true;

        var item = items[fromIndex];
        items.RemoveAt(fromIndex);
        items.Insert(toIndex, item);

        OnInventoryChanged?.Invoke();
        return true;
    }
}

