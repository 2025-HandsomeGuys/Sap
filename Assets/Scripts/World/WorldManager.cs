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

    // Public property for external access
    public Transform playerTransformPublic => playerTransform;

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

    // --- TerrainChunk Management ---
    private readonly Dictionary<Vector2Int, TerrainChunk> _activeTerrainChunks = new Dictionary<Vector2Int, TerrainChunk>();

    [Header("Region Pool Settings")]
    [SerializeField] private int regionSize = 1; // Each region is 1x1 chunks
    [SerializeField] private int maxRegionPoolSize = 30;

    // --- Deferred Collider Generation ---
    private readonly HashSet<Vector2Int> _dirtyRegionColliders = new HashSet<Vector2Int>();

    // --- Initial Loading State ---
    private bool _isInitialLoadComplete = false;

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

        // 초기 로딩 동안 플레이어 비활성화
        /*if (playerTransform.gameObject != null)
        {
            playerTransform.gameObject.SetActive(false);
            Debug.Log("WorldManager: Player disabled during initial chunk loading.");
        }*/

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

    #region Player Stabilization
    /// <summary>
    /// 주어진 X 좌표에서 땅 표면의 Y 좌표를 찾습니다.
    /// </summary>
    private float FindGroundSurfaceY(float x)
    {
        // 플레이어 위치에서 위로 올라가면서 땅 찾기
        Vector3Int startCell = groundTilemap.WorldToCell(new Vector3(x, 100f, 0)); // 위에서 시작
        
        // 위에서 아래로 내려가면서 첫 번째 비어있지 않은 타일 찾기
        for (int y = startCell.y; y >= startCell.y - 100; y--)
        {
            Vector3Int checkCell = new Vector3Int(startCell.x, y, 0);
            Vector3 worldPos = groundTilemap.GetCellCenterWorld(checkCell);
            TileType tileType = GetTileTypeAt(worldPos);
            
            if (tileType != TileType.Empty)
            {
                // 땅을 찾았으면 그 셀의 위쪽 경계 반환
                return groundTilemap.GetCellCenterWorld(checkCell).y + (worldGenerator.cellSize * 0.5f);
            }
        }
        
        return float.MinValue; // 땅을 찾지 못함
    }
    
    private IEnumerator StabilizePlayerAfterActivation()
    {
        yield return null; // 한 프레임 대기
        
        if (playerTransform != null)
        {
            Rigidbody2D rb = playerTransform.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
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

        // 초기 로딩이 완료되면 플레이어 활성화
        /*if (!_isInitialLoadComplete)
        {
            _isInitialLoadComplete = true;
            if (playerTransform != null && playerTransform.gameObject != null)
            {
                // 플레이어 위치를 땅 위로 조정
                Vector3 playerPos = playerTransform.position;
                Vector3Int playerCell = groundTilemap.WorldToCell(playerPos);
                
                // 플레이어의 X 위치에서 땅 표면 찾기 (위에서 아래로)
                float playerX = playerPos.x;
                float groundY = FindGroundSurfaceY(playerX);
                
                // 땅을 찾지 못한 경우 기본 위치 사용
                if (groundY == float.MinValue)
                {
                    Vector3 cellCenter = groundTilemap.GetCellCenterWorld(playerCell);
                    groundY = cellCenter.y + 1.0f;
                }
                else
                {
                    // 땅 위 1 유니티 단위
                    groundY += 1.0f;
                }
                
                playerPos.y = groundY;
                playerTransform.position = playerPos;
                
                // Rigidbody2D가 있다면 velocity 초기화
                Rigidbody2D rb = playerTransform.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                }
                
                // 플레이어 활성화
                playerTransform.gameObject.SetActive(true);
                
                // 한 프레임 대기 후 다시 velocity 초기화 (물리 시뮬레이션 안정화)
                StartCoroutine(StabilizePlayerAfterActivation());
                
                Debug.Log($"WorldManager: Initial chunk loading complete. Player enabled at position {playerPos}.");
            }
        }*/

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
        // TerrainChunk를 사용하는 경우
        if (!_activeTerrainChunks.ContainsKey(chunkData.chunkCoord))
        {
            // TerrainChunk GameObject 생성
            GameObject terrainChunkObj = new GameObject($"TerrainChunk_{chunkData.chunkCoord.x}_{chunkData.chunkCoord.y}");
            terrainChunkObj.transform.SetParent(transform);
            
            // TerrainChunk 컴포넌트 추가
            TerrainChunk terrainChunk = terrainChunkObj.AddComponent<TerrainChunk>();
            
            // SpriteRenderer 추가 (TerrainChunk의 RequireComponent)
            SpriteRenderer sr = terrainChunkObj.GetComponent<SpriteRenderer>();
            if (sr == null) sr = terrainChunkObj.AddComponent<SpriteRenderer>();
            
            // 초기 스프라이트 생성 (빈 텍스처)
            Texture2D initialTexture = new Texture2D(terrainChunk.width, terrainChunk.height, TextureFormat.RGBA32, false);
            initialTexture.SetPixels32(new Color32[terrainChunk.width * terrainChunk.height]);
            initialTexture.Apply();
            sr.sprite = Sprite.Create(initialTexture, new Rect(0, 0, terrainChunk.width, terrainChunk.height), new Vector2(0.5f, 0.5f), terrainChunk.PPU);
            
            // Initialize 호출
            float cellSize = worldGenerator != null ? worldGenerator.cellSize : 0.3125f;
            terrainChunk.Initialize(chunkData, chunkData.chunkCoord, cellSize);
            
            _activeTerrainChunks[chunkData.chunkCoord] = terrainChunk;
        }

        // 기존 Tilemap 시스템도 유지 (필요한 경우)
        // Region 관리 로직은 유지하되, 타일 배치는 TerrainChunk가 담당
        Vector2Int regionCoord = GetRegionCoord(chunkData.chunkCoord);
        if (!_activeChunksPerRegion.ContainsKey(regionCoord)) _activeChunksPerRegion[regionCoord] = 0;
        _activeChunksPerRegion[regionCoord]++;

        // Tilemap은 더 이상 사용하지 않지만, Region 관리를 위해 유지
        GetOrCreateRegionTilemap(regionCoord);
    }

    private void UnloadChunk(Vector2Int chunkCoord)
    {
        if (_chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData) && chunkData.status == ChunkStatus.Ready)
        {
            // 1. TerrainChunk 제거
            if (_activeTerrainChunks.TryGetValue(chunkCoord, out TerrainChunk terrainChunk))
            {
                if (terrainChunk != null && terrainChunk.gameObject != null)
                {
                    Destroy(terrainChunk.gameObject);
                }
                _activeTerrainChunks.Remove(chunkCoord);
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