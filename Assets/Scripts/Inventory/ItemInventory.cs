using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

public class ItemInventory : MonoBehaviour
{
    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    [Header("개수 제한 설정")]
    [Tooltip("아이템 인벤토리에 넣을 수 있는 최대 총 개수 (슬롯 수가 아닌 아이템 총 개수)")]
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

        // 현재 개수를 한 번만 계산하고 로컬 변수로 추적 (O(N²) 방지)
        int currentCount = CurrentItemCount;
        int initialQuantity = quantity;

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
                        currentCount += toAdd; // 로컬 변수 업데이트
                        quantity -= toAdd;
                    }

                    // 남은 수량이 있으면 새 슬롯 생성(스택 분할)
                    while (quantity > 0 && currentCount < maxItemCount)
                    {
                        int stack = Mathf.Min(itemToAdd.MaxStackSize, quantity);
                        int actualStack = Mathf.Min(stack, maxItemCount - currentCount);
                        if (actualStack <= 0) break;
                        
                        items.Add(new InventorySlot(itemToAdd, actualStack));
                        currentCount += actualStack; // 로컬 변수 업데이트
                        quantity -= actualStack;
                    }
                }
                else
                {
                    // MaxStackSize가 0이면 무제한 스택 가능
                    int canAdd = maxItemCount - currentCount;
                    int toAdd = Mathf.Min(canAdd, quantity);
                    if (toAdd > 0)
                    {
                        existing.AddQuantity(toAdd);
                        currentCount += toAdd; // 로컬 변수 업데이트
                        quantity -= toAdd;
                    }
                }
            }
            else
            {
                if (itemToAdd.MaxStackSize > 0)
                {
                    while (quantity > 0 && currentCount < maxItemCount)
                    {
                        int stack = Mathf.Min(itemToAdd.MaxStackSize, quantity);
                        int actualStack = Mathf.Min(stack, maxItemCount - currentCount);
                        if (actualStack <= 0) break;
                        
                        items.Add(new InventorySlot(itemToAdd, actualStack));
                        currentCount += actualStack; // 로컬 변수 업데이트
                        quantity -= actualStack;
                    }
                }
                else
                {
                    // MaxStackSize가 0이면 무제한 스택 가능
                    int canAdd = maxItemCount - currentCount;
                    int toAdd = Mathf.Min(canAdd, quantity);
                    if (toAdd > 0)
                    {
                        items.Add(new InventorySlot(itemToAdd, toAdd));
                        currentCount += toAdd; // 로컬 변수 업데이트
                        quantity -= toAdd;
                    }
                }
            }
        }
        else
        {
            // 비스택형은 개수만큼 개별 슬롯
            int canAdd = maxItemCount - currentCount;
            int toAdd = Mathf.Min(canAdd, quantity);
            for (int i = 0; i < toAdd; i++)
            {
                items.Add(new InventorySlot(itemToAdd, 1));
                currentCount++; // 로컬 변수 업데이트
            }
            quantity -= toAdd;
        }

        int addedCount = initialQuantity - quantity;
        if (quantity > 0)
        {
            Debug.Log($"[ItemInventory] 일부 아이템만 추가되었습니다. (요청: {initialQuantity}개, 추가: {addedCount}개)");
        }
        else
        {
            Debug.Log($"[ItemInventory] Item Added: {itemToAdd.DisplayName}, New Total Count: {currentCount}");
        }

        OnInventoryChanged?.Invoke();
        return addedCount > 0;
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

        // AddItem을 재사용하여 중복 로직 제거 (DRY 원칙)
        // AddItem이 호출될 때마다 이벤트가 발생하지만, FromData는 로드 시점이므로
        // 성능 최적화가 필요하면 AddItem에 이벤트 호출 제어 파라미터를 추가할 수 있음
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
                        // AddItem을 호출하여 로직 재사용
                        // 이벤트는 AddItem 내부에서 호출되지만, FromData는 로드 시점이므로
                        // 성능 최적화를 위해 이벤트 호출을 일시적으로 비활성화할 수 있음
                        // 현재는 AddItem의 이벤트 호출을 그대로 사용
                        AddItem(so, s.quantity);
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

        // AddItem이 이미 OnInventoryChanged를 호출하므로 중복 호출 제거
        // 단, 로드 중에는 이벤트가 여러 번 발생할 수 있음 (성능 최적화 필요시 개선 가능)
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

