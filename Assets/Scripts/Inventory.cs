using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    public List<Gem> collectedGems = new List<Gem>();

    public float TotalWeight
    {
        get
        {
            float total = 0f;
            foreach (Gem gem in collectedGems)
            {
                if (gem != null) // Ensure gem object still exists
                {
                    total += gem.weight;
                }
            }
            return total;
        }
    }

    public void AddGem(Gem gem)
    {
        if (gem != null && !collectedGems.Contains(gem))
        {
            collectedGems.Add(gem);
            // Optionally, deactivate the gem GameObject here if it's not pooled by the PlayerController
            // For now, PlayerController will handle pooling after adding to inventory
            Debug.Log($"Added gem to inventory. Total weight: {TotalWeight}");
        }
    }

    public void RemoveGem(Gem gem)
    {
        if (gem != null && collectedGems.Contains(gem))
        {
            collectedGems.Remove(gem);
            Debug.Log($"Removed gem from inventory. Total weight: {TotalWeight}");
        }
    }

    // Method to return a collected gem to the pool (e.g., when consumed or dropped)
    public void ReturnCollectedGemToPool(Gem gem)
    {
        if (gem != null)
        {
            RemoveGem(gem);
            // Assuming the Gem GameObject has the TAG_GEM tag
            ObjectPooler.Instance.ReturnToPool(Constants.TAG_GEM, gem.gameObject);
        }
    }
}