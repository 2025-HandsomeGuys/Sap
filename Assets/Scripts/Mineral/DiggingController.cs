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

        if (cellsToDig.Count == 0)
        {
            return;
        }

        var worldPositionsToDig = new List<Vector3>();
        int dugTileCount = 0;

        // First, determine which tiles will be dug and process their effects (e.g., stamina)
        foreach (Vector3Int cellPos in cellsToDig)
        {
            Vector3 cellWorldCenter = WorldManager.Instance.GetCellCenterWorld(cellPos);
            TileType type = WorldManager.Instance.GetTileTypeAt(cellWorldCenter);

            if (type != TileType.Empty)
            {
                ReducePlayerStaminaForTile(type);
                worldPositionsToDig.Add(cellWorldCenter);
                dugTileCount++;
            }
        }

        // Now, send the entire batch of tiles to the WorldManager to be processed efficiently
        if (worldPositionsToDig.Count > 0)
        {
            WorldManager.Instance.DigTiles(worldPositionsToDig);
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
        float cellSize = WorldManager.Instance.CellSize;
        if (cellSize <= 0) return cells; // Prevent division by zero

        // Determine the bounding box of the circle in cell coordinates
        Vector3Int minCell = WorldManager.Instance.WorldToCell(digCenter - new Vector2(digRadius, digRadius));
        Vector3Int maxCell = WorldManager.Instance.WorldToCell(digCenter + new Vector2(digRadius, digRadius));

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                var cellPos = new Vector3Int(x, y, 0);
                Vector3 cellCenter = WorldManager.Instance.GetCellCenterWorld(cellPos);

                // Check if the center of the cell is within the circle's radius
                if (Vector2.Distance(digCenter, cellCenter) <= digRadius)
                {
                    cells.Add(cellPos);
                }
            }
        }
        return cells;
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