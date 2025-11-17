using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

public class ItemInventory : MonoBehaviour
{
    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    [Header("개수 제한 설정")]
    [Tooltip("아이템 인벤토리에 넣을 수 있는 최대 개수")]
    public int maxItemCount = 3;

    [Header("아이템 목록(내부 전용)")]
    [SerializeField] private List<InventorySlot> items = new List<InventorySlot>();

    [Header("선택 사항")]
    public Transform playerTransform; // 필요 시 사용

    public IReadOnlyList<InventorySlot> ReadonlyItems => items;

    public int CurrentItemCount
    {
        get
        {
            int count = 0;
            foreach (InventorySlot slot in items)
            {
                if (slot.item != null)
                {
                    count += slot.item.Stackable ? slot.quantity : 1;
                }
            }
            return count;
        }
    }

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

    // 아이템 추가 시도
    public bool AddItem(ItemSO itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return false;

        // 개수 제한 확인
        int currentCount = CurrentItemCount;
        int addCount = itemToAdd.Stackable ? quantity : quantity;
        if (currentCount + addCount > maxItemCount)
        {
            Debug.Log($"아이템 인벤토리가 가득 찼습니다. (최대 {maxItemCount}개)");
            return false;
        }

        var existing = items.FirstOrDefault(s => s.item != null && s.item.Id == itemToAdd.Id);

        if (itemToAdd.Stackable)
        {
            if (existing != null)
            {
                if (itemToAdd.MaxStackSize > 0)
                {
                    int canAdd = itemToAdd.MaxStackSize - existing.quantity;
                    if (canAdd > 0)
                    {
                        int toAdd = Mathf.Min(canAdd, quantity);
                        existing.AddQuantity(toAdd);
                        quantity -= toAdd;
                    }

                    // 남은 수량이 있으면 새 슬롯 생성(스택 분할)
                    while (quantity > 0 && CurrentItemCount < maxItemCount)
                    {
                        int stack = Mathf.Min(itemToAdd.MaxStackSize, quantity);
                        items.Add(new InventorySlot(itemToAdd, stack));
                        quantity -= stack;
                    }
                }
                else
                {
                    existing.AddQuantity(quantity);
                    quantity = 0;
                }
            }
            else
            {
                if (itemToAdd.MaxStackSize > 0)
                {
                    while (quantity > 0 && CurrentItemCount < maxItemCount)
                    {
                        int stack = Mathf.Min(itemToAdd.MaxStackSize, quantity);
                        items.Add(new InventorySlot(itemToAdd, stack));
                        quantity -= stack;
                    }
                }
                else
                {
                    items.Add(new InventorySlot(itemToAdd, quantity));
                    quantity = 0;
                }
            }
        }
        else
        {
            // 비스택형은 개수만큼 개별 슬롯
            for (int i = 0; i < quantity && CurrentItemCount < maxItemCount; i++)
            {
                items.Add(new InventorySlot(itemToAdd, 1));
            }
            quantity = 0;
        }

        if (quantity > 0)
        {
            Debug.Log($"[ItemInventory] 일부 아이템만 추가되었습니다. (요청: {quantity + (CurrentItemCount - currentCount)}, 추가: {CurrentItemCount - currentCount})");
        }
        else
        {
            Debug.Log($"[ItemInventory] Item Added: {itemToAdd.DisplayName}, New Total Count: {CurrentItemCount}");
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool RemoveItem(ItemSO itemToRemove, int quantity = 1)
    {
        if (itemToRemove == null || quantity <= 0) return false;

        if (itemToRemove.Stackable)
        {
            var slot = items.FirstOrDefault(s => s.item != null && s.item.Id == itemToRemove.Id);
            if (slot == null) return false;

            int take = Mathf.Min(slot.quantity, quantity);
            slot.quantity -= take;
            if (slot.quantity <= 0)
            {
                items.Remove(slot);
            }
        }
        else
        {
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
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    public int RemoveAllOf(ItemSO itemToRemove)
    {
        if (itemToRemove == null) return 0;

        int removed = 0;

        if (itemToRemove.Stackable)
        {
            var slot = items.FirstOrDefault(s => s.item != null && s.item.Id == itemToRemove.Id);
            if (slot != null)
            {
                removed = slot.quantity;
                items.Remove(slot);
            }
        }
        else
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var s = items[i];
                if (s.item != null && s.item.Id == itemToRemove.Id)
                {
                    items.RemoveAt(i);
                    removed++;
                }
            }
        }

        if (removed > 0) OnInventoryChanged?.Invoke();
        return removed;
    }

    public ItemInventoryData ToData()
    {
        var data = new ItemInventoryData();
        foreach (var slot in items)
        {
            if (slot.item is ItemSO itemSO)
            {
                data.slots.Add(new ItemSlotData
                {
                    itemId = itemSO.itemID.ToString(),
                    quantity = slot.quantity
                });
            }
        }
        return data;
    }

    public void FromData(ItemInventoryData loadedData, ItemDatabase itemDb)
    {
        items.Clear();

        if (loadedData == null || loadedData.slots == null || loadedData.slots.Count == 0)
        {
            Debug.Log("불러올 아이템 인벤토리가 없어 빈 인벤토리로 시작합니다.");
            OnInventoryChanged?.Invoke();
            return;
        }

        foreach (var s in loadedData.slots)
        {
            try
            {
                if (itemDb != null)
                {
                    var itemId = (ItemID)System.Enum.Parse(typeof(ItemID), s.itemId);
                    var so = itemDb.GetItemByID(itemId);
                    if (so != null)
                    {
                        if (so.Stackable)
                        {
                            var existing = items.FirstOrDefault(x => x.item != null && x.item.Id == so.Id);
                            if (existing != null)
                            {
                                if (so.MaxStackSize > 0)
                                {
                                    int remain = s.quantity;
                                    int canAdd = so.MaxStackSize - existing.quantity;
                                    int take = Mathf.Min(canAdd, remain);
                                    if (take > 0)
                                    {
                                        existing.AddQuantity(take);
                                        remain -= take;
                                    }
                                    while (remain > 0 && CurrentItemCount < maxItemCount)
                                    {
                                        int stack = Mathf.Min(so.MaxStackSize, remain);
                                        items.Add(new InventorySlot(so, stack));
                                        remain -= stack;
                                    }
                                }
                                else
                                {
                                    existing.AddQuantity(s.quantity);
                                }
                            }
                            else
                            {
                                if (so.MaxStackSize > 0)
                                {
                                    int remain = s.quantity;
                                    while (remain > 0 && CurrentItemCount < maxItemCount)
                                    {
                                        int stack = Mathf.Min(so.MaxStackSize, remain);
                                        items.Add(new InventorySlot(so, stack));
                                        remain -= stack;
                                    }
                                }
                                else
                                {
                                    items.Add(new InventorySlot(so, s.quantity));
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < s.quantity && CurrentItemCount < maxItemCount; i++)
                                items.Add(new InventorySlot(so, 1));
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"ID {s.itemId}에 해당하는 아이템을 찾지 못했습니다.");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"아이템 ID 파싱 실패: {s.itemId} / {e.Message}");
            }
        }

        OnInventoryChanged?.Invoke();
    }
}

