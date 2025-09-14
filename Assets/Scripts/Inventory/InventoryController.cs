using UnityEngine;
using System.Collections.Generic;

public class InventoryController : MonoBehaviour
{
    public InventoryData data = new InventoryData();
    public List<InventorySlot> items = new List<InventorySlot>();

    public void AddItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0) return;

        // ���� ���� �������̸� ���� ���� ã��
        var existing = items.Find(slot => slot.item == item);
        if (existing != null && item.stackable)
        {
            existing.AddQuantity(quantity);
        }
        else
        {
            items.Add(new InventorySlot(item, quantity));
        }

        Debug.Log($"������ �߰���: {item.itemName} x{quantity}");
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

            Debug.Log($"������ ���ŵ�: {item.itemName} x{quantity}");
        }
    }

    public InventoryData ToData()
    {
        data.slots.Clear();

        foreach (var slot in items)
        {
            data.slots.Add(new InventorySlotData
            {
                itemId = slot.item.MineralID.ToString(),
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
            Debug.Log("�ҷ��� �κ��丮�� ���� �� �� �κ��丮 ����");
            return;
        }

        foreach (var slotData in loadedData.slots)
        {
            Item item = ItemDatabase.Instance.GetItemByID(
                (MineralID)System.Enum.Parse(typeof(MineralID), slotData.itemId)
            );

            if (item != null)
            {
                items.Add(new InventorySlot(item, slotData.quantity));
            }
            else
            {
                Debug.LogWarning($"ID {slotData.itemId} �� �ش��ϴ� �������� ã�� �� ����");
            }
        }
    }

    [ContextMenu("�׽�Ʈ ������ �߰�")]
    public void AddTestItem()
    {
        if (ItemDatabase.Instance != null && ItemDatabase.Instance.allItems.Count > 0)
        {
            // DB�� ù ��° �������� �׽�Ʈ�� �߰�
            AddItem(ItemDatabase.Instance.allItems[0], 1);
        }
        else
        {
            Debug.LogWarning("ItemDatabase�� �������� ����");
        }
    }

    [ContextMenu("�׽�Ʈ ������ ����")]
    public void RemoveTestItem()
    {
        if (ItemDatabase.Instance != null && ItemDatabase.Instance.allItems.Count > 0)
        {
            // DB�� ù ��° �������� �׽�Ʈ�� ����
            RemoveItem(ItemDatabase.Instance.allItems[0], 1);
        }
        else
        {
            Debug.LogWarning("ItemDatabase�� �������� ����");
        }
    }
}