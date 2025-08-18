using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
    [Header("무게 설정")]
    public float encumbranceThreshold = 25f; // 이 무게를 초과하면 속도가 느려짐
    public float maxWeightLimit = 40f;       // 이 무게 이상이면 아이템을 더 주울 수 없음

    [Header("인벤토리 아이템 목록")]
    public List<InventorySlot> items = new List<InventorySlot>();

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
                return true;
            }
        }
        
        items.Add(new InventorySlot(itemToAdd, quantity));
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
        }
    }
}
