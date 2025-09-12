using UnityEngine;
using System.Collections.Generic;

public class InventoryController : MonoBehaviour
{
    public InventoryData data = new InventoryData();
    public List<InventorySlot> items = new List<InventorySlot>();

    public void AddItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0) return;

        // 스택 가능 아이템이면 기존 슬롯 찾기
        var existing = items.Find(slot => slot.item == item);
        if (existing != null && item.stackable)
        {
            existing.AddQuantity(quantity);
        }
        else
        {
            items.Add(new InventorySlot(item, quantity));
        }

        Debug.Log($"아이템 추가됨: {item.itemName} x{quantity}");
    }

    public void RemoveItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0) return;

        var slot = items.Find(slot => slot.item == item);
        if (slot != null)
        {
            slot.quantity -= quantity;
            if (slot.quantity <= 0)
                items.Remove(slot);

            Debug.Log($"아이템 제거됨: {item.itemName} x{quantity}");
        }
    }

    public InventoryData ToData()
    {
        data.slots.Clear();

        foreach (var slot in items)
        {
            data.slots.Add(new InventorySlotData
            {
                itemId = slot.item.itemID.ToString(),
                quantity = slot.quantity
            });
        }

        return data;
    }

    public void FromData(InventoryData loadedData)
    {
        items.Clear();

        if (loadedData == null || loadedData.slots == null || loadedData.slots.Count == 0)
        {
            Debug.Log("불러올 인벤토리가 없음 → 빈 인벤토리 유지");
            return;
        }

        foreach (var slotData in loadedData.slots)
        {
            Item item = ItemDatabase.Instance.GetItemByID(
                (ItemID)System.Enum.Parse(typeof(ItemID), slotData.itemId)
            );

            if (item != null)
            {
                items.Add(new InventorySlot(item, slotData.quantity));
            }
            else
            {
                Debug.LogWarning($"ID {slotData.itemId} 에 해당하는 아이템을 찾을 수 없음");
            }
        }
    }

    [ContextMenu("테스트 아이템 추가")]
    public void AddTestItem()
    {
        if (ItemDatabase.Instance != null && ItemDatabase.Instance.allItems.Count > 0)
        {
            // DB의 첫 번째 아이템을 테스트로 추가
            AddItem(ItemDatabase.Instance.allItems[0], 1);
        }
        else
        {
            Debug.LogWarning("ItemDatabase에 아이템이 없음");
        }
    }

    [ContextMenu("테스트 아이템 제거")]
    public void RemoveTestItem()
    {
        if (ItemDatabase.Instance != null && ItemDatabase.Instance.allItems.Count > 0)
        {
            // DB의 첫 번째 아이템을 테스트로 제거
            RemoveItem(ItemDatabase.Instance.allItems[0], 1);
        }
        else
        {
            Debug.LogWarning("ItemDatabase에 아이템이 없음");
        }
    }
}