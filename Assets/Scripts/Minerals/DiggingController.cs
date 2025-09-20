using System.Collections.Generic;
using UnityEngine;
using static Constants;

public class DiggingController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private InventoryUI inventoryUI;

    private PlayerStatsController _playerStats;

    [Header("Digging Settings")]
    [SerializeField] private float digRadius = 1.0f;
    [SerializeField] private float digOffset = 0.5f;
    [SerializeField] private float digCooldown = 0.2f;

    private float _nextDigTime = 0f;

    private void Start()
    {
        _playerStats = GetComponent<PlayerStatsController>();

        if (inventoryUI == null)
        {
            Debug.LogWarning($"InventoryUI is not assigned in the {nameof(DiggingController)} inspector.");
        }
        if (_playerStats == null)
        {
            Debug.LogError($"{nameof(PlayerStatsController)} component not found on player! Stamina reduction will not work.");
        }
    }

    /// <summary>
    /// Called by ToolController to initiate a dig action.
    /// </summary>
    public void ExecuteDig(Vector2 currentDigDirection)
    {
        if (!CanDig())
        {
            return;
        }

        _nextDigTime = Time.time + digCooldown;

        Vector2 digCenter = (Vector2)transform.position + (currentDigDirection * digOffset);
        HashSet<Vector3Int> cellsToDig = GetCellsInDigRadius(digCenter);

        int dugTileCount = 0;
        foreach (Vector3Int cellPos in cellsToDig)
        {
            if (DigCell(cellPos))
            {
                dugTileCount++;
            }
        }

        if (dugTileCount > 0)
        {
            Debug.Log($"{dugTileCount} tile(s) were dug.");
        }
    }

    /// <summary>
    /// Checks if the player is currently able to dig.
    /// </summary>
    private bool CanDig()
    {
        if (inventoryUI != null && inventoryUI.IsOpen())
        {
            return false;
        }

        if (Time.time < _nextDigTime)
        {
            return false;
        }

        if (WorldManager.Instance == null)
        {
            Debug.LogError($"{nameof(WorldManager)}.Instance is null. Cannot dig.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Calculates the set of all tilemap cells within a circular radius.
    /// </summary>
    private HashSet<Vector3Int> GetCellsInDigRadius(Vector2 digCenter)
    {
        var cells = new HashSet<Vector3Int>();
        float scanStep = WorldManager.Instance.CellSize / 2f;
        if (scanStep <= 0) scanStep = 0.1f; // Fallback to prevent infinite loops

        for (float x = -digRadius; x <= digRadius; x += scanStep)
        {
            for (float y = -digRadius; y <= digRadius; y += scanStep)
            {
                if (x * x + y * y <= digRadius * digRadius)
                {
                    Vector2 checkPos = digCenter + new Vector2(x, y);
                    cells.Add(WorldManager.Instance.WorldToCell(checkPos));
                }
            }
        }
        return cells;
    }

    /// <summary>
    /// Processes the digging of a single cell. Returns true if a tile was successfully dug.
    /// </summary>
    private bool DigCell(Vector3Int cellPos)
    {
        Vector3 cellWorldCenter = WorldManager.Instance.GetCellCenterWorld(cellPos);
        TileType type = WorldManager.Instance.GetTileTypeAt(cellWorldCenter);

        if (type == TileType.Empty)
        {
            return false;
        }

        // Reduce player's max stamina based on the tile's data
        ReducePlayerStaminaForTile(type);

        // Tell the WorldManager to remove the tile
        WorldManager.Instance.TileDug(cellWorldCenter);

        return true;
    }

    /// <summary>
    /// Reduces player's max stamina based on the properties of the dug tile.
    /// </summary>
    private void ReducePlayerStaminaForTile(TileType tileType)
    {
        if (_playerStats == null) return;

        TileDataJson data = TileDataManager.Instance.GetData(tileType);
        if (data != null && data.maxStaminaReduction > 0)
        {
            _playerStats.ReduceMaxStamina(data.maxStaminaReduction);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Note: This gizmo will now only show a default direction when not in play mode,
        // as currentDigDirection is no longer updated in this script.
        Vector2 digCenter = (Vector2)transform.position + (Vector2.right * digOffset);
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(digCenter, digRadius);
    }
}