using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.IO; // Added for file operations

public class WorldManager : MonoBehaviour
{
    #region Singleton
    public static WorldManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }

        if (emptyTileArray == null)
        {
            emptyTileArray = new TileBase[WorldGenerator.chunkSize * WorldGenerator.chunkSize];
        }
    }
    #endregion

    [Header("References")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private WorldGenerator worldGenerator;
    [SerializeField] private Tilemap groundTilemap; // The 'master' tilemap for visuals
    [SerializeField] private GameObject regionPrefab;

    [Header("World Settings")]
    [Tooltip("The view distance in chunks around the player.")]
    [SerializeField] private int viewDistanceInChunks = 1;

    // --- Chunk Management ---
    private Vector2Int _currentPlayerChunkCoord;
    private readonly Dictionary<Vector2Int, ChunkData> _chunkDataMap = new Dictionary<Vector2Int, ChunkData>();
    private Coroutine _chunkUpdateCoroutine;

    // --- Region Pool Management ---
    private readonly Dictionary<Vector2Int, Tilemap> _activeRegionTilemaps = new Dictionary<Vector2Int, Tilemap>();
    private readonly Dictionary<Vector2Int, int> _activeChunksPerRegion = new Dictionary<Vector2Int, int>();
    private readonly Queue<GameObject> _regionPool = new Queue<GameObject>();
    private const int REGION_SIZE = 6; // Each region is 6x6 chunks
    private const int MAX_REGION_POOL_SIZE = 30;

    private static TileBase[] emptyTileArray;

    public enum ChunkStatus { Loading, Generated, Ready, Unloaded }

    public class ChunkData
    {
        public TileType[,] tileStates;
        public Vector2Int chunkCoord;
        public List<GameObject> spawnedItems;
        public ChunkStatus status;
        public Coroutine generationCoroutine;
        public TileBase[] tiles;

        public ChunkData(Vector2Int coord, int chunkSize)
        {
            chunkCoord = coord;
            tileStates = new TileType[chunkSize, chunkSize];
            spawnedItems = new List<GameObject>();
            status = ChunkStatus.Loading;
            generationCoroutine = null;
            tiles = new TileBase[chunkSize * chunkSize];
        }
    }

    #region Unity Lifecycle
    private void Start()
    {
        if (playerTransform == null || worldGenerator == null || groundTilemap == null || regionPrefab == null)
        {
            Debug.LogError("A required reference (Player, WorldGenerator, Tilemap, or RegionPrefab) is not assigned in WorldManager!");
            this.enabled = false;
            return;
        }

        _currentPlayerChunkCoord = GetChunkCoordFromPosition(playerTransform.position);
        RequestChunkUpdate();
    }

    private void Update()
    {
        Vector2Int playerChunk = GetChunkCoordFromPosition(playerTransform.position);
        if (playerChunk != _currentPlayerChunkCoord)
        {
            _currentPlayerChunkCoord = playerChunk;
            RequestChunkUpdate();
        }
    }
    #endregion

    #region Public API
    public float CellSize => groundTilemap.cellSize.x;

    /// <summary>
    /// Digs a collection of tiles, regenerating the collider only once per affected region.
    /// This is the high-performance method for digging.
    /// </summary>
    public void DigTiles(IEnumerable<Vector3> worldPositions)
    {
        var dirtyRegions = new HashSet<Vector2Int>();

        foreach (var worldPos in worldPositions)
        {
            Vector2Int regionCoord = DigSingleTile(worldPos);
            if (regionCoord.x != int.MinValue)
            {
                dirtyRegions.Add(regionCoord);
            }
        }

        foreach (var regionCoord in dirtyRegions)
        {
            if (_activeRegionTilemaps.TryGetValue(regionCoord, out var tilemap))
            {
                RegenerateRegionCollider(tilemap);
            }
        }
    }

    /// <summary>
    /// Gets the type of tile at a specific world position.
    /// </summary>
    public TileType GetTileTypeAt(Vector3 worldPosition)
    {
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);
        if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData) && chunkData.status == ChunkStatus.Ready)
        {
            Vector3Int cellPosition = groundTilemap.WorldToCell(worldPosition);
            int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);

            if (localX >= 0 && localX < WorldGenerator.chunkSize && localY >= 0 && localY < WorldGenerator.chunkSize)
            {
                return chunkData.tileStates[localX, localY];
            }
        }
        return TileType.Empty;
    }
    
    /// <summary>
    /// Gathers all loaded chunk data and saves it to a file at the specified path.
    /// </summary>
    public void SaveWorld(string filePath)
    {
        SerializableWorldData worldData = new SerializableWorldData();
        foreach (var chunkData in _chunkDataMap.Values)
        {
            // Only save chunks that are actually loaded and ready
            if (chunkData.status == ChunkStatus.Ready || chunkData.status == ChunkStatus.Generated)
            {
                var serializableChunk = new SerializableChunkData(chunkData.chunkCoord.x, chunkData.chunkCoord.y, WorldGenerator.chunkSize);
                
                // Flatten 2D array into 1D for serialization
                for (int x = 0; x < WorldGenerator.chunkSize; x++)
                {
                    for (int y = 0; y < WorldGenerator.chunkSize; y++)
                    {
                        serializableChunk.tileStates[y * WorldGenerator.chunkSize + x] = (int)chunkData.tileStates[x, y];
                    }
                }
                worldData.allChunkData.Add(serializableChunk);
            }
        }

        string json = JsonUtility.ToJson(worldData, true);
        File.WriteAllText(filePath, json);
    }


    public Vector3Int WorldToCell(Vector3 worldPos) => groundTilemap.WorldToCell(worldPos);
    public Vector3 GetCellCenterWorld(Vector3Int cellPos) => groundTilemap.GetCellCenterWorld(cellPos);
    #endregion
    
    #region Internal Digging Logic
    /// <summary>
    /// Handles the logic for digging a single tile. Does NOT regenerate the collider.
    /// </summary>
    /// <returns>The coordinate of the modified region, or a min value vector if no change was made.</returns>
    private Vector2Int DigSingleTile(Vector3 worldPosition)
    {
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);

        if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData) && chunkData.status == ChunkStatus.Ready)
        {
            Vector3Int cellPosition = WorldToCell(worldPosition);
            int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);

            if (localX >= 0 && localX < WorldGenerator.chunkSize && localY >= 0 && localY < WorldGenerator.chunkSize)
            {
                if (chunkData.tileStates[localX, localY] != TileType.Empty)
                {
                    // 1. Update data model
                    chunkData.tileStates[localX, localY] = TileType.Empty;

                    // 2. Update visual tilemap
                    groundTilemap.SetTile(cellPosition, null);

                    // 3. Update collision tilemap (without regenerating collider)
                    Vector2Int regionCoord = GetRegionCoord(chunkCoord);
                    if (_activeRegionTilemaps.TryGetValue(regionCoord, out Tilemap regionTilemap))
                    {
                        regionTilemap.SetTile(cellPosition, null);
                    }
                    return regionCoord;
                }
            }
        }
        return new Vector2Int(int.MinValue, int.MinValue);
    }
    #endregion

    #region Chunk Update Orchestration
    private void RequestChunkUpdate()
    {
        // Prevent multiple updates from running simultaneously
        if (_chunkUpdateCoroutine != null) return;
        _chunkUpdateCoroutine = StartCoroutine(UpdateChunksCoroutine());
    }

    private IEnumerator UpdateChunksCoroutine()
    {
        var dirtyRegionCoords = new HashSet<Vector2Int>();
        var chunksToUnload = new List<Vector2Int>();

        // Phase 1: Load new chunks and wait for their data to be generated.
        List<Coroutine> generationCoroutines = LoadAndGenerateChunksInRange();
        foreach (var coroutine in generationCoroutines)
        {
            yield return coroutine;
        }

        // Phase 2: Place tiles for newly generated chunks and identify old chunks to unload.
        PlaceTilesAndIdentifyChunksToUnload(dirtyRegionCoords, chunksToUnload);

        // Phase 3: Unload chunks that are out of range.
        UnloadChunks(chunksToUnload, dirtyRegionCoords);

        // Phase 4: Regenerate colliders for all regions that were modified.
        RegenerateDirtyRegionColliders(dirtyRegionCoords);

        _chunkUpdateCoroutine = null;
    }
    #endregion

    #region Chunk Update Sub-tasks
    private List<Coroutine> LoadAndGenerateChunksInRange()
    {
        var generationCoroutines = new List<Coroutine>();
        for (int xOffset = -viewDistanceInChunks; xOffset <= viewDistanceInChunks; xOffset++)
        {
            for (int yOffset = -viewDistanceInChunks; yOffset <= viewDistanceInChunks; yOffset++)
            {
                Vector2Int chunkCoord = new Vector2Int(_currentPlayerChunkCoord.x + xOffset, _currentPlayerChunkCoord.y + yOffset);

                if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
                {
                    // If the chunk was previously unloaded, start rendering it again.
                    if (chunkData.status == ChunkStatus.Unloaded)
                    {
                        chunkData.status = ChunkStatus.Loading;
                        chunkData.generationCoroutine = StartCoroutine(RenderChunkCoroutine(chunkData));
                        generationCoroutines.Add(chunkData.generationCoroutine);
                    }
                }
                else
                {
                    // If the chunk is brand new, create its data and start the full generation process.
                    ChunkData newChunkData = new ChunkData(chunkCoord, WorldGenerator.chunkSize);
                    _chunkDataMap.Add(chunkCoord, newChunkData);
                    newChunkData.generationCoroutine = StartCoroutine(FullChunkGenerationSequence(newChunkData));
                    generationCoroutines.Add(newChunkData.generationCoroutine);
                }
            }
        }
        return generationCoroutines;
    }

    private void PlaceTilesAndIdentifyChunksToUnload(HashSet<Vector2Int> dirtyRegionCoords, List<Vector2Int> chunksToUnload)
    {
        foreach (var chunkData in _chunkDataMap.Values)
        {
            // Place tiles for chunks that have just finished generating.
            if (chunkData.status == ChunkStatus.Generated)
            {
                PlaceTilesForChunk(chunkData);
                dirtyRegionCoords.Add(GetRegionCoord(chunkData.chunkCoord));
                chunkData.status = ChunkStatus.Ready;
            }

            // Identify chunks that are now outside the view distance + a buffer.
            if (chunkData.status == ChunkStatus.Ready)
            {
                if (Mathf.Abs(chunkData.chunkCoord.x - _currentPlayerChunkCoord.x) > viewDistanceInChunks + 1 ||
                    Mathf.Abs(chunkData.chunkCoord.y - _currentPlayerChunkCoord.y) > viewDistanceInChunks + 1)
                {
                    chunksToUnload.Add(chunkData.chunkCoord);
                }
            }
        }
    }

    private void UnloadChunks(List<Vector2Int> chunksToUnload, HashSet<Vector2Int> dirtyRegionCoords)
    {
        foreach (Vector2Int chunkCoord in chunksToUnload)
        {
            UnloadChunk(chunkCoord);
            dirtyRegionCoords.Add(GetRegionCoord(chunkCoord));
        }
    }

    private void RegenerateDirtyRegionColliders(HashSet<Vector2Int> dirtyRegionCoords)
    {
        foreach (var regionCoord in dirtyRegionCoords)
        {
            if (_activeRegionTilemaps.TryGetValue(regionCoord, out var tilemap))
            {
                RegenerateRegionCollider(tilemap);
            }
        }
    }

    private void RegenerateRegionCollider(Tilemap regionTilemap)
    {
        var composite = regionTilemap.GetComponentInParent<CompositeCollider2D>();
        if (composite != null)
        {
            composite.GenerateGeometry();
        }
    }
    #endregion

    #region Chunk Data and Tile Placement
    private IEnumerator FullChunkGenerationSequence(ChunkData chunkData)
    {
        // This coroutine orchestrates the generation of brand new chunk data.
        yield return StartCoroutine(worldGenerator.InitializeChunkDataCoroutine(chunkData));
        chunkData.tiles = worldGenerator.GenerateChunkTiles(chunkData.chunkCoord, chunkData);
        chunkData.status = ChunkStatus.Generated;
        chunkData.generationCoroutine = null;
    }

    private IEnumerator RenderChunkCoroutine(ChunkData chunkData)
    {
        // This coroutine handles "rendering" a chunk that was already in memory but unloaded.
        // For now, it just marks it as ready to be placed again.
        chunkData.status = ChunkStatus.Generated;
        yield return null;
        chunkData.generationCoroutine = null;
    }

    private void PlaceTilesForChunk(ChunkData chunkData)
    {
        Vector2Int regionCoord = GetRegionCoord(chunkData.chunkCoord);
        if (!_activeChunksPerRegion.ContainsKey(regionCoord)) _activeChunksPerRegion[regionCoord] = 0;
        _activeChunksPerRegion[regionCoord]++;

        Tilemap regionTilemap = GetOrCreateRegionTilemap(regionCoord);

        int startX = chunkData.chunkCoord.x * WorldGenerator.chunkSize;
        int startY = chunkData.chunkCoord.y * WorldGenerator.chunkSize;
        var bounds = new BoundsInt(startX, startY, 0, WorldGenerator.chunkSize, WorldGenerator.chunkSize, 1);

        // Set tiles on both the main visual tilemap and the region-specific collision tilemap
        groundTilemap.SetTilesBlock(bounds, chunkData.tiles);
        regionTilemap.SetTilesBlock(bounds, chunkData.tiles);
    }

    private void UnloadChunk(Vector2Int chunkCoord)
    {
        if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData) && chunkData.status == ChunkStatus.Ready)
        {
            // Clear the visual tiles from the main tilemap
            int startX = chunkCoord.x * WorldGenerator.chunkSize;
            int startY = chunkCoord.y * WorldGenerator.chunkSize;
            var bounds = new BoundsInt(startX, startY, 0, WorldGenerator.chunkSize, WorldGenerator.chunkSize, 1);
            groundTilemap.SetTilesBlock(bounds, emptyTileArray);

            // Update region management and potentially pool the region
            DecrementRegionChunkCount(chunkCoord);

            chunkData.status = ChunkStatus.Unloaded;
        }
    }
    #endregion

    #region Region and Coordinate Utilities
    private Vector2Int GetChunkCoordFromPosition(Vector3 position)
    {
        Vector3Int cellPos = groundTilemap.WorldToCell(position);
        int x = Mathf.FloorToInt((float)cellPos.x / WorldGenerator.chunkSize);
        int y = Mathf.FloorToInt((float)cellPos.y / WorldGenerator.chunkSize);
        return new Vector2Int(x, y);
    }

    private Vector2Int GetRegionCoord(Vector2Int chunkCoord)
    {
        return new Vector2Int(
            Mathf.FloorToInt((float)chunkCoord.x / REGION_SIZE),
            Mathf.FloorToInt((float)chunkCoord.y / REGION_SIZE)
        );
    }
    #endregion

    #region Region Pooling
    /// <summary>
    /// Retrieves an active Tilemap for a region, creating or un-pooling one if necessary.
    /// </summary>
    private Tilemap GetOrCreateRegionTilemap(Vector2Int regionCoord)
    {
        if (_activeRegionTilemaps.TryGetValue(regionCoord, out Tilemap regionTilemap))
        {
            return regionTilemap;
        }

        // Dequeue from pool or instantiate a new one
        GameObject regionObject = (_regionPool.Count > 0) ? _regionPool.Dequeue() : Instantiate(regionPrefab, transform);
        regionObject.name = $"Region_{regionCoord.x}_{regionCoord.y}";
        regionObject.SetActive(true);

        Tilemap newTilemap = regionObject.GetComponentInChildren<Tilemap>();
        _activeRegionTilemaps.Add(regionCoord, newTilemap);
        return newTilemap;
    }

    /// <summary>
    /// Decrements the active chunk count for a region and returns it to the pool if it's empty.
    /// </summary>
    private void DecrementRegionChunkCount(Vector2Int chunkCoord)
    {
        Vector2Int regionCoord = GetRegionCoord(chunkCoord);
        if (_activeChunksPerRegion.ContainsKey(regionCoord))
        {
            _activeChunksPerRegion[regionCoord]--;
            if (_activeChunksPerRegion[regionCoord] <= 0)
            {
                _activeChunksPerRegion.Remove(regionCoord);
                ReturnRegionToPool(regionCoord);
            }
        }
    }

    /// <summary>
    /// Deactivates a region's GameObject and adds it to a pool for reuse.
    /// </summary>
    private void ReturnRegionToPool(Vector2Int regionCoord)
    {
        if (_activeRegionTilemaps.TryGetValue(regionCoord, out Tilemap tilemapToDeactivate))
        {
            GameObject objectToDeactivate = tilemapToDeactivate.transform.parent.gameObject;
            tilemapToDeactivate.ClearAllTiles();

            if (_regionPool.Count < MAX_REGION_POOL_SIZE)
            {
                objectToDeactivate.SetActive(false);
                _regionPool.Enqueue(objectToDeactivate);
            }
            else
            {
                Destroy(objectToDeactivate);
            }

            _activeRegionTilemaps.Remove(regionCoord);
        }
    }
    #endregion
}
