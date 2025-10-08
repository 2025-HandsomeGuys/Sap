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

    [Header("Region Pool Settings")]
    [SerializeField] private int regionSize = 1; // Each region is 1x1 chunks
    [SerializeField] private int maxRegionPoolSize = 30;

    // --- Deferred Collider Generation ---
    private readonly HashSet<Vector2Int> _dirtyRegionColliders = new HashSet<Vector2Int>();

    private static TileBase[] emptyTileArray;

    public enum ChunkStatus { Loading, Generated, Ready, Unloaded }

    public class ChunkData
    {
        public TileType[,] terrainLayer; // Changed from tileStates
        public MineralID[,] mineralLayer; // Added
        public Dictionary<Vector2Int, GameObject> hiddenMinerals; // Added to store pre-spawned minerals
        public Vector2Int chunkCoord;
        public List<GameObject> spawnedItems;
        public ChunkStatus status;
        public Coroutine generationCoroutine;
        public TileBase[] tiles;

        public ChunkData(Vector2Int coord, int chunkSize)
        {
            chunkCoord = coord;
            terrainLayer = new TileType[chunkSize, chunkSize]; // Changed
            mineralLayer = new MineralID[chunkSize, chunkSize]; // Added
            hiddenMinerals = new Dictionary<Vector2Int, GameObject>(); // Added
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

    private void LateUpdate()
    {
        // Regenerate all dirty colliders at the end of the frame
        if (_dirtyRegionColliders.Count > 0)
        {
            foreach (var regionCoord in _dirtyRegionColliders)
            {
                if (_activeRegionTilemaps.TryGetValue(regionCoord, out var tilemap))
                {
                    RegenerateRegionCollider(tilemap);
                }
            }
            _dirtyRegionColliders.Clear();
        }
    }
    #endregion

    #region Public API
    public float CellSize => groundTilemap.cellSize.x;

    /// <summary>
    /// Digs a collection of tiles, deferring collider regeneration to LateUpdate.
    /// </summary>
    public void DigTiles(IEnumerable<Vector3Int> cellPositions)
    {
        var positionsToClear = new List<Vector3Int>();
        var dirtyRegions = new HashSet<Vector2Int>();

        foreach (var cellPosition in cellPositions)
        {
            Vector2Int chunkCoord = GetChunkCoordFromCellPosition(cellPosition);
            if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData) && chunkData.status == ChunkStatus.Ready)
            {
                int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
                int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);

                if (localX >= 0 && localX < WorldGenerator.chunkSize && localY >= 0 && localY < WorldGenerator.chunkSize)
                {
                    if (chunkData.terrainLayer[localX, localY] != TileType.Empty)
                    {
                        chunkData.terrainLayer[localX, localY] = TileType.Empty;
                        positionsToClear.Add(cellPosition);
                        dirtyRegions.Add(GetRegionCoord(chunkCoord));
                    }
                }
            }
        }

        if (positionsToClear.Count > 0)
        {
            // Batch clear the main tilemap
            var tilesToSet = new TileBase[positionsToClear.Count]; // Array of nulls
            groundTilemap.SetTiles(positionsToClear.ToArray(), tilesToSet);

            // Batch clear the region tilemaps
            foreach (var regionCoord in dirtyRegions)
            {
                if (_activeRegionTilemaps.TryGetValue(regionCoord, out Tilemap regionTilemap))
                {
                    regionTilemap.SetTiles(positionsToClear.ToArray(), tilesToSet);
                }
                _dirtyRegionColliders.Add(regionCoord);
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
                return chunkData.terrainLayer[localX, localY];
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
            if (chunkData.status == ChunkStatus.Ready || chunkData.status == ChunkStatus.Generated)
            {
                var serializableChunk = new SerializableChunkData(chunkData.chunkCoord.x, chunkData.chunkCoord.y, WorldGenerator.chunkSize);
                
                for (int x = 0; x < WorldGenerator.chunkSize; x++)
                {
                    for (int y = 0; y < WorldGenerator.chunkSize; y++)
                    {
                        int index = y * WorldGenerator.chunkSize + x;
                        serializableChunk.terrainLayer[index] = (int)chunkData.terrainLayer[x, y];
                        serializableChunk.mineralLayer[index] = (int)chunkData.mineralLayer[x, y];
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

    public TerrainLayer GetLayerForDepth(int depth)
    {
        TerrainLayer current = null;
        foreach (var layer in worldGenerator.terrainProfile.layers)
        {
            if (depth <= layer.startDepth) current = layer;
            else return current;
        }
        return current;
    }

    public int GetNextLayerDepth(TerrainLayer currentLayer)
    {
        int index = worldGenerator.terrainProfile.layers.IndexOf(currentLayer);
        return (index >= 0 && index < worldGenerator.terrainProfile.layers.Count - 1)
            ? worldGenerator.terrainProfile.layers[index + 1].startDepth
            : int.MinValue;
    }
    /// <summary>
    /// Gets the type of mineral at a specific world position.
    /// </summary>
    public MineralID GetMineralIDAt(Vector3 worldPosition)
    {
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);
        if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData) && chunkData.status == ChunkStatus.Ready)
        {
            Vector3Int cellPosition = groundTilemap.WorldToCell(worldPosition);
            int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);

            if (localX >= 0 && localX < WorldGenerator.chunkSize && localY >= 0 && localY < WorldGenerator.chunkSize)
            {
                return chunkData.mineralLayer[localX, localY];
            }
        }
        return MineralID.None;
    }

    /// <summary>
    /// Clears the mineral at a specific world position (sets it to None).
    /// </summary>
    public void ClearMineralAt(Vector3 worldPosition)
    {
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);
        if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData) && chunkData.status == ChunkStatus.Ready)
        {
            Vector3Int cellPosition = groundTilemap.WorldToCell(worldPosition);
            int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);

            if (localX >= 0 && localX < WorldGenerator.chunkSize && localY >= 0 && localY < WorldGenerator.chunkSize)
            {
                chunkData.mineralLayer[localX, localY] = MineralID.None;
            }
        }
    }

    public GameObject GetHiddenMineralAt(Vector3 worldPosition)
    {
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);
        if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
            Vector3Int cellPosition = groundTilemap.WorldToCell(worldPosition);
            int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);
            var localCoord = new Vector2Int(localX, localY);

            if (chunkData.hiddenMinerals.TryGetValue(localCoord, out GameObject mineralObj))
            {
                return mineralObj;
            }
        }
        return null;
    }
    #endregion
    
    #region Chunk Update Orchestration
    private void RequestChunkUpdate()
    {
        if (_chunkUpdateCoroutine != null) return;
        _chunkUpdateCoroutine = StartCoroutine(UpdateChunksCoroutine());
    }

    private IEnumerator UpdateChunksCoroutine()
    {
        var dirtyRegionCoords = new HashSet<Vector2Int>();
        var chunksToUnload = new List<Vector2Int>();

        List<Coroutine> generationCoroutines = LoadAndGenerateChunksInRange();
        foreach (var coroutine in generationCoroutines)
        {
            yield return coroutine;
        }

        PlaceTilesAndIdentifyChunksToUnload(dirtyRegionCoords, chunksToUnload);
        UnloadChunks(chunksToUnload, dirtyRegionCoords);

        // Add all regions modified during this update to the global dirty set
        foreach (var regionCoord in dirtyRegionCoords)
        {
            _dirtyRegionColliders.Add(regionCoord);
        }

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
                    if (chunkData.status == ChunkStatus.Unloaded)
                    {
                        chunkData.status = ChunkStatus.Loading;
                        chunkData.generationCoroutine = StartCoroutine(RenderChunkCoroutine(chunkData));
                        generationCoroutines.Add(chunkData.generationCoroutine);
                    }
                }
                else
                {
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
            if (chunkData.status == ChunkStatus.Generated)
            {
                PlaceTilesForChunk(chunkData);
                dirtyRegionCoords.Add(GetRegionCoord(chunkData.chunkCoord));
                chunkData.status = ChunkStatus.Ready;
            }

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
        yield return StartCoroutine(worldGenerator.InitializeChunkDataCoroutine(chunkData));
        worldGenerator.PreSpawnMineralsForChunk(chunkData); // Pre-spawn mineral objects
        chunkData.tiles = worldGenerator.CreateTilebaseArray(chunkData.chunkCoord, chunkData);
        chunkData.status = ChunkStatus.Generated;
        chunkData.generationCoroutine = null;
    }

    private IEnumerator RenderChunkCoroutine(ChunkData chunkData)
    {
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

        regionTilemap.SetTilesBlock(bounds, chunkData.tiles);
    }

    private void UnloadChunk(Vector2Int chunkCoord)
    {
        if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData) && chunkData.status == ChunkStatus.Ready)
        {
            // 1. 해당 청크가 속한 Region의 Tilemap에서 타일들을 제거합니다.
            Vector2Int regionCoord = GetRegionCoord(chunkCoord);
            if (_activeRegionTilemaps.TryGetValue(regionCoord, out Tilemap regionTilemap))
            {
                int startX = chunkCoord.x * WorldGenerator.chunkSize;
                int startY = chunkCoord.y * WorldGenerator.chunkSize;
                var bounds = new BoundsInt(startX, startY, 0, WorldGenerator.chunkSize, WorldGenerator.chunkSize, 1);
                
                // emptyTileArray는 모든 타일을 null(빈 타일)로 설정하기 위한 배열입니다.
                regionTilemap.SetTilesBlock(bounds, emptyTileArray); 
            }

            // 2. Region의 활성 청크 카운트를 줄입니다.
            DecrementRegionChunkCount(chunkCoord);
            
            // 3. 청크의 상태를 'Unloaded'로 변경합니다.
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

    private Vector2Int GetChunkCoordFromCellPosition(Vector3Int cellPos)
    {
        int x = Mathf.FloorToInt((float)cellPos.x / WorldGenerator.chunkSize);
        int y = Mathf.FloorToInt((float)cellPos.y / WorldGenerator.chunkSize);
        return new Vector2Int(x, y);
    }

    private Vector2Int GetRegionCoord(Vector2Int chunkCoord)
    {
        return new Vector2Int(
            Mathf.FloorToInt((float)chunkCoord.x / regionSize),
            Mathf.FloorToInt((float)chunkCoord.y / regionSize)
        );
    }
    #endregion

    #region Region Pooling
    private Tilemap GetOrCreateRegionTilemap(Vector2Int regionCoord)
    {
        if (_activeRegionTilemaps.TryGetValue(regionCoord, out Tilemap regionTilemap))
        {
            return regionTilemap;
        }

        GameObject regionObject = (_regionPool.Count > 0) ? _regionPool.Dequeue() : Instantiate(regionPrefab, transform);
        regionObject.name = $"Region_{regionCoord.x}_{regionCoord.y}";
        regionObject.SetActive(true);

        Tilemap newTilemap = regionObject.GetComponentInChildren<Tilemap>();
        _activeRegionTilemaps.Add(regionCoord, newTilemap);
        return newTilemap;
    }

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

    private void ReturnRegionToPool(Vector2Int regionCoord)
    {
        if (_activeRegionTilemaps.TryGetValue(regionCoord, out Tilemap tilemapToDeactivate))
        {
            GameObject objectToDeactivate = tilemapToDeactivate.transform.parent.gameObject;
            tilemapToDeactivate.ClearAllTiles();

            if (_regionPool.Count < maxRegionPoolSize)
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