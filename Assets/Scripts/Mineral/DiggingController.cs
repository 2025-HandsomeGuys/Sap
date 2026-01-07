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
    
    [Header("TerrainChunk Settings")]
    [Tooltip("LayerMask for detecting TerrainChunk colliders.")]
    [SerializeField] private LayerMask terrainChunkLayer = -1; // 모든 레이어 기본값

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

        // TerrainChunk 시스템 사용 (우선)
        bool terrainChunkFound = TryDigTerrainChunk(digCenter);
        
        // WorldManager 시스템 사용 (fallback, WorldManager가 있는 경우)
        if (!terrainChunkFound && WorldManager.Instance != null)
        {
            DigWithWorldManager(digCenter);
        }
    }

    /// <summary>
    /// TerrainChunk를 사용하여 땅을 팝니다.
    /// </summary>
    private bool TryDigTerrainChunk(Vector2 digCenter)
    {
        // TerrainChunk를 찾기 위해 Physics2D 사용 (LayerMask 적용)
        Collider2D[] hits = Physics2D.OverlapCircleAll(digCenter, digRadius, terrainChunkLayer);
        
        bool foundTerrainChunk = false;
        foreach (Collider2D hit in hits)
        {
            TerrainChunk chunk = hit.GetComponent<TerrainChunk>();
            if (chunk != null)
            {
                foundTerrainChunk = true;
                chunk.Dig(digCenter, digRadius);
                
                // 스테미나 소모 (기본 타일 타입으로 가정, 실제로는 TerrainChunk에서 타입을 가져와야 함)
                if (_playerStats != null)
                {
                    // TerrainChunk에서는 타일 타입을 직접 알기 어려우므로 기본값 사용
                    // TODO: TerrainChunk에서 타일 타입 정보를 가져올 수 있도록 개선 필요
                    TileDataJson data = TileDataManager.Instance?.GetData(TileType.Dirt);
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
            }
        }
        
        return foundTerrainChunk;
    }

    /// <summary>
    /// WorldManager를 사용하여 땅을 팝니다 (기존 방식).
    /// </summary>
    private void DigWithWorldManager(Vector2 digCenter)
    {
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

        // WorldManager가 없어도 TerrainChunk를 사용할 수 있으므로 null 체크 제거
        // WorldManager.Instance가 null이어도 TerrainChunk 시스템을 사용할 수 있습니다.
        
        return true;
    }

    /// <summary>
    /// Calculates the set of all tilemap cells within a circular radius.
    /// </summary>
    private HashSet<Vector3Int> GetCellsInDigRadius(Vector2 digCenter)
    {
        var cells = new HashSet<Vector3Int>();
        
        // WorldManager가 없으면 빈 집합 반환
        if (WorldManager.Instance == null)
        {
            return cells;
        }
        
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
