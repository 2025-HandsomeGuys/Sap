using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

public class MineralInventory : MonoBehaviour
{
    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    [Header("무게 설정")]
    [Tooltip("이 무게를 초과하면 과적 상태로 취급(속도 감소 등)")]
    public float encumbranceThreshold = 25f;

    [Tooltip("이 무게 이상이면 아이템을 더 추가할 수 없음")]
    public float maxWeightLimit = 40f;

    [Header("광물 목록(내부 전용)")]
    [SerializeField] private List<InventorySlot> items = new List<InventorySlot>();

    [Header("선택 사항")]
    public Transform playerTransform; // 필요 시 사용

    public IReadOnlyList<InventorySlot> ReadonlyItems => items;

    public float TotalWeight
    {
        get
        {
            float total = 0f;
            foreach (InventorySlot slot in items)
            {
                if (slot.item != null)
                {
                    total += slot.item.Weight * slot.quantity;
                }
            }
            return total;
        }
    }

    // 과적 상태 (속도 감소) 여부 확인
    public bool IsEncumbered => TotalWeight > encumbranceThreshold;

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

    // 광물 추가 시도
    public bool AddItem(MineralSO itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return false;

        // 아이템을 추가했을 때 최대 한계 무게를 초과하는지 확인
        if (TotalWeight + (itemToAdd.Weight * quantity) > maxWeightLimit)
        {
            Debug.Log("가방이 한계에 도달해 더 이상 광물을 추가할 수 없습니다.");
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
                    while (quantity > 0)
                    {
                        // 무게 확인
                        if (TotalWeight + (itemToAdd.Weight * Mathf.Min(itemToAdd.MaxStackSize, quantity)) > maxWeightLimit)
                        {
                            Debug.Log("가방이 한계에 도달해 일부 광물만 추가되었습니다.");
                            break;
                        }
                        int stack = Mathf.Min(itemToAdd.MaxStackSize, quantity);
                        items.Add(new InventorySlot(itemToAdd, stack));
                        quantity -= stack;
                    }
                }
                else
                {
                    // MaxStackSize 정책을 사용하지 않는 경우, 그대로 누적
                    existing.AddQuantity(quantity);
                    quantity = 0;
                }
            }
            else
            {
                // 기존 슬롯이 없을 때도 최대 스택 정책을 적용
                if (itemToAdd.MaxStackSize > 0)
                {
                    while (quantity > 0)
                    {
                        // 무게 확인
                        if (TotalWeight + (itemToAdd.Weight * Mathf.Min(itemToAdd.MaxStackSize, quantity)) > maxWeightLimit)
                        {
                            Debug.Log("가방이 한계에 도달해 일부 광물만 추가되었습니다.");
                            break;
                        }
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
            for (int i = 0; i < quantity; i++)
            {
                // 무게 확인
                if (TotalWeight + itemToAdd.Weight > maxWeightLimit)
                {
                    Debug.Log("가방이 한계에 도달해 일부 광물만 추가되었습니다.");
                    break;
                }
                items.Add(new InventorySlot(itemToAdd, 1));
            }
            quantity = 0;
        }

        Debug.Log($"[MineralInventory] Item Added: {itemToAdd.DisplayName}, Item Weight: {itemToAdd.Weight}, New Total Weight: {TotalWeight}");
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool RemoveItem(MineralSO itemToRemove, int quantity = 1)
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

    public int RemoveAllOf(MineralSO itemToRemove)
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

    public MineralInventoryData ToData()
    {
        var data = new MineralInventoryData();
        foreach (var slot in items)
        {
            if (slot.item is MineralSO mineralSO)
            {
                data.slots.Add(new MineralSlotData
                {
                    itemId = mineralSO.mineralID.ToString(),
                    quantity = slot.quantity
                });
            }
        }
        return data;
    }

    public void FromData(MineralInventoryData loadedData, MineralDatabase mineralDb)
    {
        items.Clear();

        if (loadedData == null || loadedData.slots == null || loadedData.slots.Count == 0)
        {
            Debug.Log("불러올 광물 인벤토리가 없어 빈 인벤토리로 시작합니다.");
            OnInventoryChanged?.Invoke();
            return;
        }

        foreach (var s in loadedData.slots)
        {
            try
            {
                if (mineralDb != null)
                {
                    var mineralId = (MineralID)System.Enum.Parse(typeof(MineralID), s.itemId);
                    var so = mineralDb.GetMineralByID(mineralId);
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
                                    while (remain > 0)
                                    {
                                        if (TotalWeight + (so.Weight * Mathf.Min(so.MaxStackSize, remain)) > maxWeightLimit)
                                        {
                                            Debug.Log("가방이 한계에 도달해 일부 광물만 로드되었습니다.");
                                            break;
                                        }
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
                                    while (remain > 0)
                                    {
                                        if (TotalWeight + (so.Weight * Mathf.Min(so.MaxStackSize, remain)) > maxWeightLimit)
                                        {
                                            Debug.Log("가방이 한계에 도달해 일부 광물만 로드되었습니다.");
                                            break;
                                        }
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
                            for (int i = 0; i < s.quantity; i++)
                            {
                                if (TotalWeight + so.Weight > maxWeightLimit)
                                {
                                    Debug.Log("가방이 한계에 도달해 일부 광물만 로드되었습니다.");
                                    break;
                                }
                                items.Add(new InventorySlot(so, 1));
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"ID {s.itemId}에 해당하는 광물을 찾지 못했습니다.");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"광물 ID 파싱 실패: {s.itemId} / {e.Message}");
            }
        }

        OnInventoryChanged?.Invoke();
    }
}

