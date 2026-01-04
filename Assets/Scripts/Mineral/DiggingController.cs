using System.Collections.Generic;
using UnityEngine;
using static Constants;

public class DiggingController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private InventoryUI inventoryUI;

    private PlayerStatsController _playerStats;

    [Header("Digging Settings")]
    [Tooltip("The player's visual center offset from the pivot (usually at the feet).")]
    [SerializeField] private float verticalPivotOffset = 0.5f;
    [Tooltip("The distance from the player's center to the digging point.")]
    [SerializeField] private float actionRadius = 1.0f;
    [Tooltip("The radius of the hole to dig.")]
    [SerializeField] private float digRadius = 0.5f;
    [SerializeField] private float digCooldown = 0.2f;

    private float _nextDigTime = 0f;
    private Camera _cam;
    private float _baseDigRadius; // 기본 파는 범위 저장
    private float _baseDigCooldown; // 기본 쿨다운 저장

    private void Start()
    {
        _playerStats = GetComponent<PlayerStatsController>();
        _cam = Camera.main;

        // 기본값 저장
        _baseDigRadius = digRadius;
        _baseDigCooldown = digCooldown;

        if (inventoryUI == null)
        {
            Debug.LogWarning($"InventoryUI is not assigned in the {nameof(DiggingController)} inspector.");
        }
        if (_playerStats == null)
        {
            Debug.LogError($"{nameof(PlayerStatsController)} component not found on player! Stamina reduction will not work.");
        }

        // 도구 강화 효과 적용
        ApplyToolUpgrades();
    }

    private void ApplyToolUpgrades()
    {
        if (ToolUpgradeManager.Instance == null) return;

        // 공통: 파는 범위 증가
        float rangeIncrease = ToolUpgradeManager.Instance.GetCommonDigRangeIncrease();
        digRadius = _baseDigRadius * (1f + rangeIncrease / 100f);

        // 강인도: 쿨다운 감소
        float cooldownReduction = ToolUpgradeManager.Instance.GetHardnessCooldownReduction();
        digCooldown = _baseDigCooldown * (1f - cooldownReduction / 100f);
        digCooldown = Mathf.Max(0.05f, digCooldown); // 최소 0.05초
    }

    /// <summary>
    /// Called by ToolController to initiate a dig action.
    /// </summary>
    public void ExecuteDig(Vector2 ignoredDirection)
    {
        if (!CanDig() || _cam == null)
        {
            return;
        }

        _nextDigTime = Time.time + digCooldown;

        // 1. Establish the player's center pivot point.
        Vector2 pivot = (Vector2)transform.position + (Vector2.up * verticalPivotOffset);

        // 2. Determine direction from the pivot to the mouse.
        Vector3 screenPos = Input.mousePosition;
        screenPos.z = -_cam.transform.position.z; // Set Z to the distance from camera to the Z=0 plane
        Vector2 mousePos = (Vector2)_cam.ScreenToWorldPoint(screenPos);
        Vector2 currentDigDirection = (mousePos - pivot).normalized;

        // 3. Set the dig location on the circumference of the actionRadius.
        Vector2 digCenter = pivot + (currentDigDirection * actionRadius);

        HashSet<Vector3Int> cellsToProcess = GetCellsInDigRadius(digCenter);

        if (cellsToProcess.Count == 0)
        {
            return;
        }

        var cellPositionsToDig = new List<Vector3Int>();

        // First, process effects for all cells (minerals and terrain)
        foreach (Vector3Int cellPos in cellsToProcess)
        {
            Vector3 cellWorldCenter = WorldManager.Instance.GetCellCenterWorld(cellPos);

            // Reveal hidden minerals
            GameObject hiddenMineral = WorldManager.Instance.GetHiddenMineralAt(cellWorldCenter);
            if (hiddenMineral != null)
            {
                hiddenMineral.SetActive(true);
                // After activating, clear the data to prevent re-activation
                WorldManager.Instance.ClearMineralAt(cellWorldCenter);
            }

            // Then, check for terrain to dig
            TileType type = WorldManager.Instance.GetTileTypeAt(cellWorldCenter);
            if (type != TileType.Empty)
            {
                // 강인도 체크
                if (ToolUpgradeManager.Instance != null && !ToolUpgradeManager.Instance.CanBreakTile(type))
                {
                    // 강인도가 부족하면 파지 않음
                    continue;
                }

                ReducePlayerStaminaForTile(type);
                cellPositionsToDig.Add(cellPos);
            }
        }

        // Now, send the entire batch of terrain tiles to the WorldManager to be processed efficiently
        if (cellPositionsToDig.Count > 0)
        {
            WorldManager.Instance.DigTiles(cellPositionsToDig);
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
            float reduction = data.maxStaminaReduction;

            // 삽 강화: 스테미나 소모 감소 적용
            if (ToolUpgradeManager.Instance != null)
            {
                float staminaReduction = ToolUpgradeManager.Instance.GetShovelStaminaReduction();
                reduction = reduction * (1f - staminaReduction / 100f);
            }

            _playerStats.ReduceMaxStamina(reduction);
        }
    }
        
            public void IncreaseDigRadius(float amount)
            {
                digRadius += amount;
                Debug.Log($"Dig radius increased to {digRadius}");
            }
        
            private void OnDrawGizmosSelected()
            {        // Visualize the pivot, action radius, and dig radius
        Vector2 pivot = (Vector2)transform.position + (Vector2.up * verticalPivotOffset);
        
        // Draw the action radius circle
        Gizmos.color = Color.grey;
        Gizmos.DrawWireSphere(pivot, actionRadius);

        // Draw the dig location and radius for a default direction (right)
        Vector2 digCenter = pivot + (Vector2.right * actionRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(digCenter, digRadius);
    }
}
