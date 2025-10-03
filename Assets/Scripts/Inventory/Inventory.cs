using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System; // Action을 사용하기 위해 추가

public interface InterfaceInventoryItem
{ 
    string Id { get; } 
    string DisplayName { get; } 
    Sprite Icon { get; } 
    float Weight { get; } 
    bool Stackable { get; } 
    int MaxStackSize { get; } 
}

[System.Serializable]
public class InventorySlot
{
    public InterfaceInventoryItem item;
    public int quantity;

    public InventorySlot(InterfaceInventoryItem item, int quantity)
    {
        this.item = item;
        this.quantity = quantity;
    }

    public void AddQuantity(int amount) => quantity += amount;
}

public class Inventory : MonoBehaviour
{
    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    [Header("무게 설정")] 
    [Tooltip("이 무게를 초과하면 과적 상태로 취급(속도 감소 등)")]
    public float encumbranceThreshold = 25f;

    [Tooltip("이 무게 이상이면 아이템을 더 추가할 수 없음")]
    public float maxWeightLimit = 40f;

    [Header("아이템 목록(내부 전용)")]
    [SerializeField] private List<InventorySlot> items = new List<InventorySlot>();

    [Header("선택 사항")]
    public Transform playerTransform; // 필요 시 사용

    public IReadOnlyList<InventorySlot> ReadonlyItems => items;

    /*
     *  Total Weight 
     *  parameter : asfkgjgkldsjgalksjhlka
     */

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
                continue; sum += s.item.Stackable ? s.quantity : 1; 
        } 
        return sum; 
    }

    // 아이템 추가 시도
    public bool AddItem(InterfaceInventoryItem itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return false;

        // 아이템을 추가했을 때 최대 한계 무게를 초과하는지 확인
        if (TotalWeight + (itemToAdd.Weight * quantity) > maxWeightLimit)
        {
            Debug.Log("가방이 한계에 도달해 더 이상 아이템을 추가할 수 없습니다.");
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
                        int toAdd = Math.Min(canAdd, quantity);
                        existing.AddQuantity(toAdd);
                        quantity -= toAdd;
                    }

                    // 남은 수량이 있으면 새 슬롯 생성(스택 분할)
                    while (quantity > 0)
                    {
                        int stack = Math.Min(itemToAdd.MaxStackSize, quantity);
                        items.Add(new InventorySlot(itemToAdd, stack)); // 변경: 생성자 IInventoryItem
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
                        int stack = Math.Min(itemToAdd.MaxStackSize, quantity);
                        items.Add(new InventorySlot(itemToAdd, stack)); // 변경
                        quantity -= stack;
                    }
                }
                else
                {
                    items.Add(new InventorySlot(itemToAdd, quantity)); // 변경
                    quantity = 0;
                }
            }
        }
        else
        {
            // 비스택형은 개수만큼 개별 슬롯
            for (int i = 0; i < quantity; i++)
            {
                items.Add(new InventorySlot(itemToAdd, 1)); // 변경
            }
            quantity = 0;
        }

        Debug.Log($"[Inventory] Item Added: {itemToAdd.DisplayName}, Item Weight: {itemToAdd.Weight}, New Total Weight: {TotalWeight}");
        OnInventoryChanged?.Invoke(); // 이벤트 호출
        return true;
    }

    public bool RemoveItem(InterfaceInventoryItem itemToRemove, int quantity = 1)
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
            // 비스택형은 슬롯 여러 개 제거
            int remain = quantity;
            for (int i = items.Count - 1; i >= 0 && remain > 0; i--)
            {
                var s = items[i];
                // [변경] 참조 비교 → Id 기반
                if (s.item != null && s.item.Id == itemToRemove.Id)
                {
                    items.RemoveAt(i);
                    remain--;
                }
            }
            if (remain > 0)
            {
                // 요청 수량만큼 제거하지 못함
                return false;
            }
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    public int RemoveAllOf(InterfaceInventoryItem itemToRemove)
    {
        if (itemToRemove == null) return 0;

        int removed = 0;

        if (itemToRemove.Stackable) // [변경]
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

    public InventoryData ToData()
    {
        var data = new InventoryData();
        foreach (var slot in items)
        {
            string kind = slot.item is ItemSO ? "Item"
                 : slot.item is MineralSO ? "Mineral"
                 : "Unknown";

            if (kind == "Unknown")
            {
                Debug.LogWarning($"알 수 없는 인벤토리 아이템 타입: {slot.item.GetType().Name}");
                continue;
            }

            data.slots.Add(new InventorySlotData
            {
                // [변경] 공통 Id 사용
                kind = kind,                  // [추가]
                itemId = slot.item.Id,        // [변경] itemID.ToString() → 공통 Id
                quantity = slot.quantity
            });
        }
        return data;
    }

    public void FromData(InventoryData loadedData, ItemDatabase itemDb, MineralDatabase mineralDb)
    {
        items.Clear();

        if (loadedData == null || loadedData.slots == null || loadedData.slots.Count == 0)
        {
            Debug.Log("불러올 인벤토리가 없어 빈 인벤토리로 시작합니다.");
            OnInventoryChanged?.Invoke();
            return;
        }
        
        foreach (var s in loadedData.slots)
        {
            try
            {
                // [추가] kind에 따라 적절한 SO 조회
                InterfaceInventoryItem resolved = null;

                if (string.Equals(s.kind, "Item", StringComparison.OrdinalIgnoreCase))
                {
                    // 예: itemDb.GetItemByID(ItemID)
                    // s.itemId가 enum 이름 문자열이라면 Enum.Parse 필요
                    if (itemDb != null)
                    {
                        var itemId = (ItemID)Enum.Parse(typeof(ItemID), s.itemId);
                        var so = itemDb.GetItemByID(itemId);
                        resolved = so as InterfaceInventoryItem;
                    }
                }
                else if (string.Equals(s.kind, "Mineral", StringComparison.OrdinalIgnoreCase))
                {
                    if (mineralDb != null)
                    {
                        var mineralId = (MineralID)Enum.Parse(typeof(MineralID), s.itemId);
                        var so = mineralDb.GetMineralByID(mineralId);
                        resolved = so as InterfaceInventoryItem;
                    }
                }
                else
                {
                    Debug.LogWarning($"알 수 없는 kind: {s.kind} (id: {s.itemId})");
                }

                if (resolved != null)
                {
                    if (resolved.Stackable)
                    {
                        // [변경] Id 기반으로 기존 슬롯 병합
                        var existing = items.FirstOrDefault(x => x.item != null && x.item.Id == resolved.Id);
                        if (existing != null)
                        {
                            // [선택] 최대 스택 고려
                            if (resolved.MaxStackSize > 0)
                            {
                                int remain = s.quantity;
                                // 먼저 현재 existing에 채울 수 있는 만큼 채우기
                                int canAdd = resolved.MaxStackSize - existing.quantity;
                                int take = Math.Min(canAdd, remain);
                                if (take > 0)
                                {
                                    existing.AddQuantity(take);
                                    remain -= take;
                                }
                                // 남은 수량을 새 스택으로 분리
                                while (remain > 0)
                                {
                                    int stack = Math.Min(resolved.MaxStackSize, remain);
                                    items.Add(new InventorySlot(resolved, stack));
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
                            if (resolved.MaxStackSize > 0)
                            {
                                int remain = s.quantity;
                                while (remain > 0)
                                {
                                    int stack = Math.Min(resolved.MaxStackSize, remain);
                                    items.Add(new InventorySlot(resolved, stack));
                                    remain -= stack;
                                }
                            }
                            else
                            {
                                items.Add(new InventorySlot(resolved, s.quantity));
                            }
                        }
                    }
                    else
                    {
                        // 비스택형: 개수만큼 슬롯 생성
                        for (int i = 0; i < s.quantity; i++)
                            items.Add(new InventorySlot(resolved, 1));
                    }
                }
                else
                {
                    Debug.LogWarning($"ID {s.itemId} ({s.kind})에 해당하는 아이템을 찾지 못했습니다.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"아이템 ID 파싱 실패: {s.itemId} ({s.kind}) / {e.Message}");
            }
        }
        
        OnInventoryChanged?.Invoke();
    }

    // 디버그/테스트용 단축 메뉴
    [ContextMenu("테스트 아이템 추가")]
    public void AddTestItem()
    {
        if (ItemDatabase.Instance != null && ItemDatabase.Instance.allItems.Count > 1)
        {
            var so = ItemDatabase.Instance.allItems[1];
            AddItem(so, 1);
            Debug.Log("[Test] 아이템 1개 추가 완료");
        }
        else
        {
            Debug.LogWarning("ItemDatabase가 비어 있거나 설정되지 않았습니다.");
        }
    }

    [ContextMenu("테스트: 아이템 제거")]
    public void RemoveTest_Item()
    {
        if (ItemDatabase.Instance != null && ItemDatabase.Instance.allItems.Count > 1)
        {
            var so = ItemDatabase.Instance.allItems[1];           // ItemSO
            RemoveItem(so, 1);                                    // [변경] IInventoryItem 경로 사용
            Debug.Log("[Test] 아이템 1개 제거 완료");
        }
        else
        {
            Debug.LogWarning("ItemDatabase가 비어 있거나 설정되지 않았습니다.");
        }
    }

    [ContextMenu("테스트: 광물 추가")]
    public void AddTest_Mineral()
    {
        if (MineralDatabase.Instance != null && MineralDatabase.Instance.allMinerals.Count > 0)
        {
            var so = MineralDatabase.Instance.allMinerals[0];     // MineralSO (IInventoryItem 구현)
            AddItem(so, 3);                                       // [변경] IInventoryItem 경로 사용
            Debug.Log("[Test] 광물 3개 추가 완료");
        }
        else
        {
            Debug.LogWarning("MineralDatabase가 비어 있거나 설정되지 않았습니다.");
        }
    }

    [ContextMenu("테스트: 광물 제거")]
    public void RemoveTest_Mineral()
    {
        if (MineralDatabase.Instance != null && MineralDatabase.Instance.allMinerals.Count > 0)
        {
            var so = MineralDatabase.Instance.allMinerals[0];     // MineralSO
            RemoveItem(so, 2);                                    // [변경] IInventoryItem 경로 사용
            Debug.Log("[Test] 광물 2개 제거 완료");
        }
        else
        {
            Debug.LogWarning("MineralDatabase가 비어 있거나 설정되지 않았습니다.");
        }
    }
}
