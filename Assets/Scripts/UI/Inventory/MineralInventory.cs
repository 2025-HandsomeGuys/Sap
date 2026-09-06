// @tags: inventory, mineral, weight, encumbrance, item, player, event
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

public class MineralInventory : MonoBehaviour
{
    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    // ── 무게 설정 ────────────────────────────────────────────────
    // 직렬화하지 않는다. 프리팹(40)과 씬(50)에 서로 다른 값이 굳어 있었고,
    // C# 기본값을 고쳐도 인스펙터 값이 이겨서 밸런싱이 반영되지 않았다.
    // (ItemInventory.SlotCount / EquipmentInventory.SlotCount와 같은 방식)

    /// <summary>업그레이드 0일 때의 가방 무게 한도. 밸런싱은 이 값만 고친다.</summary>
    public const float BaseWeightLimit = 8f;

    /// <summary>과적 판정 비율. 한도 × 이 값을 넘으면 과적.</summary>
    public const float EncumbranceRatio = 0.8f;

    /// <summary>
    /// 이 무게 이상이면 광물을 더 담을 수 없다.
    /// 런타임에 <see cref="EncumbranceController"/>가 무게 한도 업그레이드를 더해 갱신한다.
    /// </summary>
    [NonSerialized] public float maxWeightLimit = BaseWeightLimit;

    /// <summary>
    /// 과적 임계값. 한도에서 파생되므로 업그레이드로 한도가 오르면 함께 오른다.
    /// (예전엔 25로 고정된 별도 필드라, 실제 페널티가 걸리는 값과 UI 표시가 어긋나 있었다.)
    /// </summary>
    public float encumbranceThreshold => maxWeightLimit * EncumbranceRatio;

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

    // 가방이 가득 차 더 이상 광물을 받을 수 없는 상태(자석 유물이 자력 차단 판정에 사용).
    public bool IsFull => TotalWeight >= maxWeightLimit;

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
    public int AddItem(MineralSO itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return 0;

        int initialQuantity = quantity;
        int addedCount = 0;

        // 스택 가능한 아이템 처리
        if (itemToAdd.Stackable)
        {
            // 1. 기존 슬롯 중 공간이 있는 슬롯들을 먼저 채움
            // MaxStackSize가 0이면 무제한 스택으로 간주 (또는 999 등)
            int effectiveStackSize = (itemToAdd.MaxStackSize > 0) ? itemToAdd.MaxStackSize : int.MaxValue;

            for (int i = 0; i < items.Count; i++)
            {
                if (quantity <= 0) break; // 모두 추가 완료

                InventorySlot slot = items[i];
                if (slot.item != null && slot.item.Id == itemToAdd.Id && slot.quantity < effectiveStackSize)
                {
                    // 공간 계산
                    int space = effectiveStackSize - slot.quantity;
                    
                    // 무게 제한 체크
                    float weightPerItem = itemToAdd.Weight;
                    float availableWeight = maxWeightLimit - TotalWeight;
                    int weightLimitCount = Mathf.FloorToInt(availableWeight / weightPerItem);
                    
                    // 실제 추가할 수량: 남은 수량, 공간, 무게 제한 중 최소값
                    int toAdd = Mathf.Min(quantity, space);
                    toAdd = Mathf.Min(toAdd, weightLimitCount);

                    if (toAdd > 0)
                    {
                        slot.quantity += toAdd;
                        addedCount += toAdd;
                        quantity -= toAdd;
                    }
                }
            }

            // 2. 남은 수량이 있으면 새 슬롯 생성
            while (quantity > 0)
            {
                float weightPerItem = itemToAdd.Weight;
                float availableWeight = maxWeightLimit - TotalWeight;
                int weightLimitCount = Mathf.FloorToInt(availableWeight / weightPerItem);
                
                // 더 이상 무게 때문에 추가 불가
                if (weightLimitCount <= 0) break;

                int stack = Mathf.Min(effectiveStackSize, quantity);
                stack = Mathf.Min(stack, weightLimitCount);

                items.Add(new InventorySlot(itemToAdd, stack));
                addedCount += stack;
                quantity -= stack;
            }
        }
        else
        {
            // 비스택형 (각각 1개씩 슬롯 생성)
            float weightPerItem = itemToAdd.Weight;
            
            while (quantity > 0)
            {
                if (TotalWeight + weightPerItem > maxWeightLimit) break;
                
                items.Add(new InventorySlot(itemToAdd, 1));
                addedCount++;
                quantity--;
            }
        }

        if (addedCount > 0)
        {
            SettlementManager.Instance?.AddMinedMineral(itemToAdd.mineralID, addedCount);
            // 가방에 실제로 들어온 순간이 '발견'이다(도감 기록). 이미 발견됐으면 내부에서 무시된다.
            CollectionCodex.Discover(CodexCategory.Mineral, itemToAdd.mineralID.ToString());
            OnInventoryChanged?.Invoke();
        }
        return addedCount;
    }

    // 특정 위치에 아이템 추가 (HeldItemManager 되돌리기용)
    public bool AddItemAt(int index, MineralSO itemToAdd, int quantity)
    {
        if (itemToAdd == null || quantity <= 0) return false;

        // 무게 확인
        if (TotalWeight + (itemToAdd.Weight * quantity) > maxWeightLimit)
        {
            Debug.Log("가방이 한계에 도달해 더 이상 광물을 추가할 수 없습니다.");
            return false;
        }

        // 인덱스 보정
        if (index < 0) index = 0;
        if (index > items.Count) index = items.Count;

        // 해당 위치에 삽입
        InventorySlot newSlot = new InventorySlot(itemToAdd, quantity);
        items.Insert(index, newSlot);
        
        OnInventoryChanged?.Invoke();
        return true;
    }

    // 특정 위치의 아이템 삭제 (인덱스 기반)
    public bool RemoveItemAt(int index, int quantity)
    {
        if (index < 0 || index >= items.Count) return false;
        
        InventorySlot slot = items[index];
        if (slot == null || slot.item == null) return false;

        int removeAmount = Mathf.Min(slot.quantity, quantity);
        slot.quantity -= removeAmount;

        if (slot.quantity <= 0)
        {
            items.RemoveAt(index);
        }

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
            OnInventoryChanged?.Invoke();
            return;
        }

        foreach (var s in loadedData.slots)
        {
            try
            {
                if (mineralDb != null)
                {
                    // [수정] Enum.TryParse를 사용하여 대소문자 구분 없이 견고하게 파싱
                    if (System.Enum.TryParse<MineralID>(s.itemId, true, out var mineralId))
                    {
                        var so = mineralDb.GetMineralByID(mineralId);
                        if (so != null)
                        {
                            // [추가] 아이콘 유무 확인 로깅
                            if (so.Icon == null)
                            {
                                Debug.LogWarning($"[MineralInventory] 광물 {so.DisplayName} ({s.itemId})의 아이콘(Sprite)이 설정되지 않았습니다.");
                            }

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
                                                Debug.LogWarning("[MineralInventory] 가방이 한계에 도달해 일부 광물만 로드되었습니다.");
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
                                                Debug.LogWarning("[MineralInventory] 가방이 한계에 도달해 일부 광물만 로드되었습니다.");
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
                                        Debug.LogWarning("[MineralInventory] 가방이 한계에 도달해 일부 광물만 로드되었습니다.");
                                        break;
                                    }
                                    items.Add(new InventorySlot(so, 1));
                                }
                            }
                        }
                        else
                        {
                            Debug.LogError($"[MineralInventory] ID '{s.itemId}'에 해당하는 광물 SO를 데이터베이스에서 찾지 못했습니다.");
                        }
                    }
                    else
                    {
                        Debug.LogError($"[MineralInventory] 광물 ID 파싱 실패: '{s.itemId}'가 MineralID Enum에 존재하지 않습니다.");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MineralInventory] 데이터 로드 중 예외 발생: {s.itemId} / {e.Message}");
            }
        }

        OnInventoryChanged?.Invoke();
    }

    // 슬롯 순서 변경 메서드들 (드래그 앤 드롭 등에서 호출 방지)
    public bool SwapSlots(int index1, int index2)
    {
        // 광물 인벤토리는 슬롯 드래그/집기가 제한되어 있으므로 항상 false 반환
        return false;
    }

    public bool MoveSlot(int fromIndex, int toIndex)
    {
        // 광물 인벤토리는 슬롯 드래그/집기가 제한되어 있으므로 항상 false 반환
        return false;
    }
}

