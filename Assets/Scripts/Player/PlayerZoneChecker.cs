using System.Collections;
using UnityEngine;

public class PlayerZoneChecker : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("Assign the WorldGenerator from the scene so we can access its terrain profile.")]
    public WorldGenerator worldGenerator;

    [Header("Settings")]
    [Tooltip("How often (in seconds) to check the player's current zone and apply effects.")]
    public float checkInterval = 1.0f;

    private PlayerStatsController _playerStats;

    void Start()
    {
        Debug.Log("[PlayerZoneChecker] Initializing...");
        _playerStats = GetComponent<PlayerStatsController>();
        if (_playerStats == null)
        {
            Debug.LogError("[PlayerZoneChecker] PlayerStatsController not found on player! Zone effects will not be applied.", this);
            enabled = false;
            return;
        }

        if (worldGenerator == null)
        {
            Debug.LogError("[PlayerZoneChecker] WorldGenerator is not assigned in the inspector! Zone effects will not be applied.", this);
            enabled = false;
            return;
        }
        
        Debug.Log("[PlayerZoneChecker] Initialization complete. Starting coroutine.");
        StartCoroutine(ZoneCheckCoroutine());
    }

    private IEnumerator ZoneCheckCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkInterval);

            // Convert player's world position to grid cell position
            Vector3Int playerCell = WorldManager.Instance.WorldToCell(transform.position);
            int playerGridY = playerCell.y;
            Debug.Log($"[PlayerZoneChecker] Checking zone. Player World Y: {transform.position.y}, Player Grid Y: {playerGridY}");

            TerrainLayer currentLayer = GetLayerForDepth(playerGridY);

            if (currentLayer != null)
            {
                Debug.Log($"[PlayerZoneChecker] Current Layer: {currentLayer.description} (Damage: {currentLayer.periodicMaxStaminaDamage})");
                if (currentLayer.periodicMaxStaminaDamage > 0)
                {
                    float damageToApply = currentLayer.periodicMaxStaminaDamage * checkInterval;
                    _playerStats.ReduceMaxStamina(damageToApply);
                    Debug.Log($"[PlayerZoneChecker] Applied {damageToApply} max stamina damage.");
                }
            }
            else
            {
                Debug.Log("[PlayerZoneChecker] Player is in an unknown layer (currentLayer is null).");
            }
        }
    }

    private TerrainLayer GetLayerForDepth(int depth)
    {
        if (worldGenerator.terrainProfile == null)
        {
            Debug.LogError("[PlayerZoneChecker] TerrainProfile on WorldGenerator is null!");
            return null;
        }

        TerrainLayer currentLayer = null;
        foreach (var layer in worldGenerator.terrainProfile.layers)
        {
            if (depth <= layer.startDepth)
            {
                currentLayer = layer;
            }
            else
            {
                // We've gone past the layer that contains this depth
                return currentLayer;
            }
        }
        return currentLayer;
    }
}
