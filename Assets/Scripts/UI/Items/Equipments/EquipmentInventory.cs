using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

/// <summary>
/// 장비 인벤토리를 관리하는 클래스.
/// 머리(0), 옷(1), 신발(2) 전용 슬롯 + 유물 슬롯 <see cref="RelicSlotCount"/>개로 고정 운영됩니다.
///
/// 유물만 슬롯이 여러 개라 "타입 → 인덱스"가 1:1이 아니다.
/// 인덱스를 직접 계산하지 말고 <see cref="FindSlotForAdd"/>(넣을 자리) /
/// <see cref="FindSlotOf"/>(들어있는 자리) / <see cref="IsSlotForType"/>(자리-타입 검증)를 쓸 것.
/// </summary>
public class EquipmentInventory : MonoBehaviour
{
    // ── 슬롯 배치 (외부에서 인덱스를 하드코딩하지 않도록 공개) ──
    public const int SlotHead = 0;
    public const int SlotClothes = 1;
    public const int SlotShoes = 2;
    /// <summary>유물 슬롯이 시작되는 인덱스.</summary>
    public const int SlotRelicFirst = 3;
    /// <summary>유물 슬롯 개수.</summary>
    public const int RelicSlotCount = 2;
    /// <summary>전체 슬롯 수 (머리·옷·신발 + 유물).</summary>
    public const int SlotCount = SlotRelicFirst + RelicSlotCount;

    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    [Header("장비 슬롯 목록 (고정 5슬롯)")]
    [Tooltip("Index 0: 머리, 1: 옷, 2: 신발, 3~4: 유물")]
    [SerializeField] private List<InventorySlot> items = new List<InventorySlot>();

    // 하위 호환성을 위한 프로퍼티 및 메서드
    public int maxSlotCount => SlotCount;
    public int CurrentSlotCount => items.Count(s => s.item != null);

    public int CountOf(InterfaceInventoryItem target)
    {
        if (target == null) return 0;
        return items.Count(s => s.item != null && s.item.Id == target.Id);
    }

    private void Awake()
    {
        EnsureInitialSlots();
    }

    /// <summary>
    /// 항상 고정된 개수의 슬롯을 유지하도록 보장합니다.
    /// </summary>
    private void EnsureInitialSlots()
    {
        while (items.Count < SlotCount)
        {
            items.Add(new InventorySlot(null, 0));
        }
        if (items.Count > SlotCount)
        {
            items.RemoveRange(SlotCount, items.Count - SlotCount);
        }
    }

    public IReadOnlyList<InventorySlot> ReadonlyItems => items;

    /// <summary>
    /// 이 슬롯이 해당 장비 타입을 받을 수 있는지. 유물은 슬롯이 여러 개라 범위로 판정한다.
    /// </summary>
    public static bool IsSlotForType(int index, EquipmentType type)
    {
        switch (type)
        {
            case EquipmentType.Head: return index == SlotHead;
            case EquipmentType.Clothes: return index == SlotClothes;
            case EquipmentType.Shoes: return index == SlotShoes;
            case EquipmentType.Relic: return index >= SlotRelicFirst && index < SlotRelicFirst + RelicSlotCount;
            default: return false;
        }
    }

    /// <summary>해당 슬롯이 받는 장비 타입 (UI 플레이스홀더 표시용). 범위 밖이면 None.</summary>
    public static EquipmentType TypeOfSlot(int index)
    {
        if (index == SlotHead) return EquipmentType.Head;
        if (index == SlotClothes) return EquipmentType.Clothes;
        if (index == SlotShoes) return EquipmentType.Shoes;
        if (index >= SlotRelicFirst && index < SlotRelicFirst + RelicSlotCount) return EquipmentType.Relic;
        return EquipmentType.None;
    }

    /// <summary>단일 슬롯 타입의 고정 인덱스. 유물은 첫 칸을 돌려주므로 넣을 자리는 FindSlotForAdd를 쓸 것.</summary>
    private static int FixedSlotOf(EquipmentType type)
    {
        switch (type)
        {
            case EquipmentType.Head: return SlotHead;
            case EquipmentType.Clothes: return SlotClothes;
            case EquipmentType.Shoes: return SlotShoes;
            case EquipmentType.Relic: return SlotRelicFirst;
            default: return -1;
        }
    }

    /// <summary>
    /// 이 장비를 넣을 슬롯 인덱스. 유물은 '같은 장비가 있는 칸(스택) → 빈 칸' 순으로 찾는다.
    /// 유물 칸이 모두 다른 장비로 차 있으면 첫 유물 칸을 돌려주고, AddItem이 '이미 다른 장비 있음'으로 실패시킨다.
    /// </summary>
    public int FindSlotForAdd(EquipmentSO item)
    {
        if (item == null) return -1;
        EnsureInitialSlots();

        if (item.equipmentType != EquipmentType.Relic)
            return FixedSlotOf(item.equipmentType);

        for (int i = SlotRelicFirst; i < SlotRelicFirst + RelicSlotCount; i++)
            if (items[i].item != null && items[i].item.Id == item.Id) return i;

        for (int i = SlotRelicFirst; i < SlotRelicFirst + RelicSlotCount; i++)
            if (items[i].item == null) return i;

        return SlotRelicFirst;
    }

    /// <summary>이 장비가 현재 들어있는 슬롯 인덱스. 없으면 -1.</summary>
    public int FindSlotOf(EquipmentSO item)
    {
        if (item == null) return -1;
        for (int i = 0; i < items.Count; i++)
            if (items[i].item != null && items[i].item.Id == item.Id) return i;
        return -1;
    }

    /// <summary>
    /// 장비 추가 시도 (장비 타입에 따라 자동 슬롯 배정)
    /// </summary>
    public int AddItem(EquipmentSO itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return 0;

        EnsureInitialSlots();

        int slotIndex = FindSlotForAdd(itemToAdd);
        if (slotIndex == -1)
        {
            Debug.LogWarning($"[EquipmentInventory] {itemToAdd.DisplayName}은 유효한 장비 타입이 아닙니다 (None).");
            return 0;
        }

        InventorySlot slot = items[slotIndex];

        // 1. 이미 다른 종류의 장비가 들어있는 경우 (교체하지 않고 실패 처리)
        if (slot.item != null && slot.item.Id != itemToAdd.Id)
        {
            Debug.Log($"[EquipmentInventory] {slotIndex}번 슬롯에 이미 다른 장비({slot.item.DisplayName})가 있습니다.");
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
            // 비스택형 (일반 장비)
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

        if (addedCount > 0)
        {
            // 가방에 실제로 들어온 순간이 '발견'(도감 기록). 이미 발견됐으면 내부에서 무시된다.
            CollectionCodex.Discover(CodexCategory.Equipment, itemToAdd.equipmentID.ToString());
            OnInventoryChanged?.Invoke();
        }
        return addedCount;
    }

    public bool RemoveItem(EquipmentSO itemToRemove, int quantity = 1)
    {
        if (itemToRemove == null) return false;
        int index = FindSlotOf(itemToRemove); // 유물은 3·4번 중 어디에 있을지 모르므로 탐색
        if (index != -1)
        {
            return RemoveItemAt(index, quantity);
        }
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

    public int RemoveAllOf(EquipmentSO itemToRemove)
    {
        if (itemToRemove == null) return 0;
        int index = FindSlotOf(itemToRemove);
        if (index != -1)
        {
            int qty = items[index].quantity;
            RemoveItemAt(index, qty);
            return qty;
        }
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

    /// <summary>
    /// 특정 슬롯에 장비를 장착합니다. (이미 장비가 있으면 교체됨)
    /// </summary>
    /// <param name="index">슬롯 인덱스 (0:머리, 1:옷, 2:신발, 3~4:유물)</param>
    /// <param name="newEq">장착할 장비</param>
    /// <param name="previousEq">기존에 장착되어 있던 장비 (없으면 null)</param>
    /// <returns>장착 성공 여부</returns>
    public bool EquipAt(int index, EquipmentSO newEq, out EquipmentSO previousEq)
    {
        previousEq = null;
        if (index < 0 || index >= items.Count || newEq == null) return false;

        // 타입 체크 — 유물은 슬롯이 2개라 '인덱스 일치'가 아니라 '범위 포함'으로 판정해야 한다
        if (!IsSlotForType(index, newEq.equipmentType))
        {
            Debug.LogWarning($"[EquipmentInventory] {newEq.DisplayName}({newEq.equipmentType})를 {index}번 슬롯에 장착할 수 없습니다.");
            return false;
        }

        previousEq = items[index].item as EquipmentSO;
        items[index].item = newEq;
        items[index].quantity = 1;

        OnInventoryChanged?.Invoke();
        return true;
    }

    public void FromData(EquipmentInventoryData loadedData, EquipmentDatabase equipmentDb)
    {
        EnsureInitialSlots();

        // DB가 없으면 아래 복원 루프가 한 개도 못 넣는다. 그 상태로 슬롯부터 비우면
        // "복원할 장비가 있는데 전부 사라지는" 조용한 데이터 손실이 된다 → 손대지 않고 중단.
        // (씬 전환 중 EquipmentDatabase 에셋이 아직 안 올라온 경우 등)
        bool hasDataToRestore = loadedData != null && loadedData.slots != null && loadedData.slots.Count > 0;
        if (equipmentDb == null && hasDataToRestore)
        {
            Debug.LogError("[EquipmentInventory] EquipmentDatabase가 null이라 장비를 복원할 수 없습니다. " +
                           "기존 장비를 지우지 않고 복원을 건너뜁니다. (씬의 DatabaseLoader에 EquipmentDatabase 연결 확인)");
            return;
        }

        // 슬롯 초기화
        for (int i = 0; i < items.Count; i++)
        {
            items[i].item = null;
            items[i].quantity = 0;
        }

        if (!hasDataToRestore)
        {
            OnInventoryChanged?.Invoke();
            return;
        }

        foreach (var s in loadedData.slots)
        {
            try
            {
                var equipmentId = (EquipmentID)System.Enum.Parse(typeof(EquipmentID), s.itemId);
                var so = equipmentDb.GetEquipmentByID(equipmentId);
                if (so != null)
                {
                    AddItem(so, s.quantity);
                }
                else
                {
                    Debug.LogWarning($"[EquipmentInventory] '{s.itemId}'를 EquipmentDatabase에서 찾을 수 없습니다. (allEquipments 목록 확인)");
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
