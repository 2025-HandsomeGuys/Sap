using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System; // Action을 사용하기 위해 추가

[System.Serializable]
public class InventorySlot
{
    public Item item;
    public int quantity;

    public InventorySlot(Item item, int quantity)
    {
        this.item = item;
        this.quantity = quantity;
    }

    public void AddQuantity(int amount)
    {
        quantity += amount;
    }
}

public class Inventory : MonoBehaviour
{
    // 인벤토리 변경 시 호출될 이벤트
    public event Action OnInventoryChanged;

    [Header("무게 설정")]
    public float encumbranceThreshold = 25f; // 이 무게를 초과하면 속도가 느려짐
    public float maxWeightLimit = 40f;       // 이 무게 이상이면 아이템을 더 주울 수 없음

    [Header("인벤토리 아이템 목록")]
    public List<InventorySlot> items = new List<InventorySlot>();
    public Transform playerTransform; // 플레이어의 Transform을 여기에 할당하세요.

    public float TotalWeight
    {
        get
        {
            float total = 0f;
            foreach (InventorySlot slot in items)
            {
                if (slot.item != null)
                {
                    total += slot.item.weight * slot.quantity;
                }
            }
            return total;
        }
    }

    // 과적 상태 (속도 감소) 여부 확인
    public bool IsEncumbered => TotalWeight > encumbranceThreshold;

    // 아이템 추가 시도
    public bool AddItem(Item itemToAdd, int quantity = 1)
    {
        if (itemToAdd == null || quantity <= 0) return false;

        // 최대 한계 무게 확인
        if (TotalWeight >= maxWeightLimit)
        {
            Debug.Log("가방이 한계에 도달해 더 이상 아이템을 추가할 수 없습니다.");
            return false;
        }

        if (itemToAdd.stackable)
        {
            InventorySlot existingSlot = items.FirstOrDefault(slot => slot.item == itemToAdd);
            if (existingSlot != null)
            {
                existingSlot.AddQuantity(quantity);
                OnInventoryChanged?.Invoke(); // 이벤트 호출
                return true;
            }
        }
        
        items.Add(new InventorySlot(itemToAdd, quantity));
        OnInventoryChanged?.Invoke(); // 이벤트 호출
        return true;
    }

    public void RemoveItem(Item itemToRemove, int quantity = 1)
    {
        if (itemToRemove == null || quantity <= 0) return;

        InventorySlot slotToRemove = items.FirstOrDefault(slot => slot.item == itemToRemove);

        if (slotToRemove != null)
        {
            slotToRemove.quantity -= quantity;
            if (slotToRemove.quantity <= 0)
            {
                items.Remove(slotToRemove);
            }
            OnInventoryChanged?.Invoke(); // 이벤트 호출
        }
    }

    public void DropItem(InventorySlot slotToDrop)
    {
        if (slotToDrop == null) return;

        // 인벤토리에서 슬롯 전체 제거
        items.Remove(slotToDrop);
        OnInventoryChanged?.Invoke(); // 인벤토리 변경 이벤트 호출
    }

    public void DropSingleItem(InventorySlot slotToDrop)
    {
        if (slotToDrop == null) return;

        // 수량을 1 감소
        slotToDrop.quantity--;

        // 수량이 0 이하면 슬롯 제거
        if (slotToDrop.quantity <= 0)
        {
            items.Remove(slotToDrop);
        }

        OnInventoryChanged?.Invoke(); // 인벤토리 변경 이벤트 호출
    }
}
