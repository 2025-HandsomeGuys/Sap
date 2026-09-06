using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

public class ItemInventory : MonoBehaviour
{
    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    [Header("개수 제한 설정")]
    /// <summary>
    /// 업그레이드 0일 때의 아이템 칸 수. 밸런싱은 이 값만 고친다.
    /// 2026-08-22: 2 → 1. 나머지 칸은 T0 업그레이드(InventorySlotUp)로 연다.
    /// </summary>
    public const int BaseSlotCount = 1;

    /// <summary>
    /// 업그레이드로 늘어난 칸 수. <see cref="EncumbranceController"/>가
    /// <see cref="StatType.InventorySlotUp"/>을 읽어 채운다.
    /// 세이브에 남기지 않는다 — 원본은 구매한 노드 목록이다.
    /// </summary>
    [NonSerialized] public int bonusSlotCount;

    // 프로퍼티라 프리팹·씬에 예전에 직렬화된 값(3)을 무시하고 항상 이 계산을 쓴다.
    // (EquipmentInventory.maxSlotCount와 같은 방식)
    public int maxSlotCount => BaseSlotCount + bonusSlotCount;


    [Tooltip("아이템 인벤토리에 넣을 수 있는 최대 총 개수 (슬롯 수가 아닌 아이템 총 개수) - 현재는 슬롯 기반으로 변경되어 참고용으로 유지")]
    public int maxItemCount = 999;

    [Header("아이템 목록(내부 전용)")]
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
                    total += slot.item.Weight * slot.quantity;
            }
            return total;
        }
    }

    private void Awake()
    {
        EnsureInitialSlots();
    }

    /// <summary>
    /// 항상 고정된 개수의 슬롯을 유지하도록 보장합니다.
    /// </summary>
    /// <summary>
    /// 칸 수처럼 아이템 목록 바깥에서 바뀐 것을 UI에 알린다.
    /// (이벤트는 클래스 밖에서 Invoke할 수 없다)
    /// </summary>
    public void NotifyChanged() => OnInventoryChanged?.Invoke();

    public void EnsureInitialSlots()
    {
        while (items.Count < maxSlotCount)
        {
            items.Add(new InventorySlot(null, 0));
        }

        // 줄일 때는 '빈 칸만' 버린다.
        // 기본 칸이 1이 되고 나머지를 업그레이드로 열게 되면서, 업그레이드가 아직
        // 적용되지 않은 순간(세이브 로드 순서)에 이 함수가 불릴 수 있다.
        // 예전처럼 RemoveRange로 잘라내면 그 한 번에 들고 있던 아이템이 조용히 사라진다.
        while (items.Count > maxSlotCount)
        {
            InventorySlot last = items[items.Count - 1];
            if (last.item != null)
            {
                Debug.LogWarning(
                    $"[ItemInventory] 칸 수({maxSlotCount})보다 많은 아이템을 들고 있다 " +
                    $"— '{last.item.Id}' 등 {items.Count - maxSlotCount}개를 남겨 둔다. " +
                    "업그레이드 적용보다 인벤토리 로드가 먼저 돌았을 수 있다.");
                break;
            }
            items.RemoveAt(items.Count - 1);
        }
    }

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

    // 특정 위치의 아이템 삭제 (인덱스 기반)
    public bool RemoveItemAt(int index, int quantity)
    {
        if (index < 0 || index >= items.Count) return false;
        
        InventorySlot slot = items[index];
        if (slot == null || slot.item == null || slot.quantity < quantity) return false;

        slot.quantity -= quantity;
        if (slot.quantity <= 0)
        {
            slot.item = null;
            slot.quantity = 0;
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    // 아이템 추가 시도 (반환값: 실제로 추가된 개수)
    public int AddItem(ItemSO itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return 0;

        EnsureInitialSlots();

        int initialQuantity = quantity;

        // 한 슬롯에 무조건 1개만 들어가도록 수정 (빈 슬롯만 찾음)
        for (int i = 0; i < items.Count && quantity > 0; i++)
        {
            if (items[i].item == null)
            {
                items[i].item = itemToAdd;
                items[i].quantity = 1;
                quantity -= 1;
            }
        }

        int addedCount = initialQuantity - quantity;
        if (addedCount > 0)
        {
            // 가방에 실제로 들어온 순간이 '발견'(도감 기록). 이미 발견됐으면 내부에서 무시된다.
            CollectionCodex.Discover(CodexCategory.Item, itemToAdd.itemID.ToString());
            OnInventoryChanged?.Invoke();
        }
        return addedCount;
    }

    // [추가] 일반적인 인터페이스 아이템 제거 지원 (장비 등)
    public bool RemoveItem(InterfaceInventoryItem itemToRemove, int quantity = 1)
    {
        if (itemToRemove == null || quantity <= 0) return false;

        bool changed = false;
        int remain = quantity;

        // 뒤에서부터 제거 (일반적인 스택 처리 관례)
        for (int i = items.Count - 1; i >= 0 && remain > 0; i--)
        {
            var slot = items[i];
            if (slot.item != null && slot.item.Id == itemToRemove.Id)
            {
                int take = Mathf.Min(slot.quantity, remain);
                slot.quantity -= take;
                remain -= take;
                
                if (slot.quantity <= 0)
                {
                    slot.item = null;
                    slot.quantity = 0;
                }
                changed = true;
            }
        }

        if (changed) OnInventoryChanged?.Invoke();
        return remain == 0;
    }

    // [추가] 일반적인 인터페이스 아이템 추가 지원 (장비 등)
    public int AddItem(InterfaceInventoryItem itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return 0;
        
        // ItemSO로 캐스팅 가능하면 기존 메서드 사용 (혹시 모를 특수 로직 유지)
        if (itemToAdd is ItemSO itemSO)
        {
            return AddItem(itemSO, quantity);
        }

        EnsureInitialSlots();

        int initialQuantity = quantity;

        // 한 슬롯에 무조건 1개만 들어가도록 수정 (빈 슬롯만 찾음)
        for (int i = 0; i < items.Count && quantity > 0; i++)
        {
            if (items[i].item == null)
            {
                items[i].item = itemToAdd;
                items[i].quantity = 1;
                quantity -= 1;
            }
        }

        int addedCount = initialQuantity - quantity;
        if (addedCount > 0) OnInventoryChanged?.Invoke();
        return addedCount;
    }

    /// <summary>
    /// 지정한 칸에 아이템을 놓는다 (빈 칸일 때만 성공).
    /// AddItem은 '첫 빈 칸'을 쓰므로 창고↔가방 스왑처럼 "방금 비운 그 칸"에 정확히 넣어야 할 때 이걸 쓴다.
    /// </summary>
    public bool PlaceAt(int index, InterfaceInventoryItem item, int quantity = 1)
    {
        if (item == null || quantity <= 0) return false;

        EnsureInitialSlots();
        if (index < 0 || index >= items.Count) return false;
        if (items[index].item != null) return false;

        items[index].item = item;
        items[index].quantity = quantity;

        OnInventoryChanged?.Invoke();
        return true;
    }

    public int RemoveAllOf(ItemSO itemToRemove)
    {
        if (itemToRemove == null) return 0;

        int removed = 0;
        foreach (var slot in items)
        {
            if (slot.item != null && slot.item.Id == itemToRemove.Id)
            {
                removed += slot.quantity;
                slot.item = null;
                slot.quantity = 0;
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
        EnsureInitialSlots();
        foreach (var slot in items)
        {
            slot.item = null;
            slot.quantity = 0;
        }

        if (loadedData == null || loadedData.slots == null || loadedData.slots.Count == 0)
        {
            OnInventoryChanged?.Invoke();
            return;
        }

        // AddItem을 재사용하여 중복 로직 제거 (DRY 원칙)
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

    /// <summary>
    /// 특정 인덱스의 아이템을 사용합니다.
    /// </summary>
    public bool UseItemAt(int index)
    {
        if (index < 0 || index >= items.Count) return false;

        InventorySlot targetSlot = items[index];
        if (targetSlot == null || targetSlot.item == null || targetSlot.quantity <= 0) return false;

        ItemSO itemSO = targetSlot.item as ItemSO;
        if (itemSO == null) return false;

        // 1. 소모 처리
        RemoveItemAt(index, 1);

        // 2. 효과 발동
        if (ItemActiveEffectManager.Instance != null)
        {
            ItemActiveEffectManager.Instance.UseItem(itemSO);
        }
        else
        {
            // 매니저가 없는 경우 하위 호환 로직 (staminaReduction 활용)
            var stats = UnityEngine.Object.FindFirstObjectByType<PlayerStat>();
            if (stats != null && itemSO.staminaReduction > 0)
            {
                stats.RecoverStamina(itemSO.staminaReduction);
            }
        }

        Debug.Log($"[Inventory] Used item at slot {index}: {itemSO.DisplayName}");
        return true;
    }

    /// <summary>
    /// 아이템 ID로 첫 번째 일치하는 아이템을 사용합니다. (하위 호환용)
    /// </summary>
    public bool UseItem(string itemId)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].item != null && items[i].item.Id == itemId && items[i].quantity > 0)
            {
                return UseItemAt(i);
            }
        }
        return false;
    }
}
