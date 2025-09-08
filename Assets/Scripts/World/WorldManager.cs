using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public class WorldManager : MonoBehaviour
{
    #region Singleton
    public static WorldManager Instance;

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

        // Initialize the reusable empty tile array to prevent GC allocations
        if (emptyTileArray == null)
        {
            emptyTileArray = new TileBase[WorldGenerator.chunkSize * WorldGenerator.chunkSize];
        }
    }
    #endregion

    [Header("References")]
    public Transform playerTransform;
    public WorldGenerator worldGenerator;
    public Tilemap groundTilemap;

    [Header("Settings")]
    public int viewDistanceInChunks = 1;

    // GC Optimization: A reusable array for clearing tiles to avoid frequent allocations.
    private static TileBase[] emptyTileArray;

    // GC Optimization: A reusable list for unloading chunks to avoid frequent allocations.
    private readonly List<Vector2Int> _chunksToUnloadCache = new List<Vector2Int>();

    public class ChunkData
    {
        public TileType[,] tileStates;
        public Vector2Int chunkCoord;
        public List<GameObject> spawnedItems;
        public ChunkStatus status;
        public Coroutine generationCoroutine; // To track the generation coroutine

        public ChunkData(Vector2Int coord, int chunkSize)
        {
            chunkCoord = coord;
            tileStates = new TileType[chunkSize, chunkSize];
            spawnedItems = new List<GameObject>();
            status = ChunkStatus.Loading; // Default status
            generationCoroutine = null;
        }
    }

    private Vector2Int currentPlayerChunkCoord;
    private Dictionary<Vector2Int, ChunkData> chunkDataMap = new Dictionary<Vector2Int, ChunkData>();

    void Start()
    {
        if (playerTransform == null || worldGenerator == null || groundTilemap == null)
        {
            Debug.LogError("Player Transform, World Generator, or Ground Tilemap not assigned in WorldManager!");
            this.enabled = false;
            return;
        }

        UpdateChunks();
    }

    void Update()
    {
        Vector2Int playerChunk = GetChunkCoordFromPosition(playerTransform.position);
        if (playerChunk != currentPlayerChunkCoord)
        {
            currentPlayerChunkCoord = playerChunk;
            UpdateChunks();
        }
    }

    public Vector3Int WorldToCell(Vector3 worldPos)
    {
        return groundTilemap.WorldToCell(worldPos);
    }

    Vector2Int GetChunkCoordFromPosition(Vector3 position)
    {
        Vector3Int cellPos = groundTilemap.WorldToCell(position);
        int x = Mathf.FloorToInt((float)cellPos.x / WorldGenerator.chunkSize);
        int y = Mathf.FloorToInt((float)cellPos.y / WorldGenerator.chunkSize);
        return new Vector2Int(x, y);
    }

    void UpdateChunks()
    {
        currentPlayerChunkCoord = GetChunkCoordFromPosition(playerTransform.position);

        // Load new chunks
        for (int xOffset = -viewDistanceInChunks; xOffset <= viewDistanceInChunks; xOffset++)
        {
            for (int yOffset = -viewDistanceInChunks; yOffset <= viewDistanceInChunks; yOffset++)
            {
                Vector2Int chunkToGenerate = new Vector2Int(currentPlayerChunkCoord.x + xOffset, currentPlayerChunkCoord.y + yOffset);

                if (!chunkDataMap.ContainsKey(chunkToGenerate))
                {
                    var newChunkData = new ChunkData(chunkToGenerate, WorldGenerator.chunkSize);
                    chunkDataMap.Add(chunkToGenerate, newChunkData);
                    
                    // Start the full generation sequence and track it
                    newChunkData.generationCoroutine = StartCoroutine(FullChunkGenerationSequence(newChunkData));
                }
            }
        }

        // Unload chunks that are out of range
        _chunksToUnloadCache.Clear();
        // Iterate over a copy of the values to allow modification during loop
        foreach (var chunkData in chunkDataMap.Values.ToList())
        {
            int xDiff = Mathf.Abs(chunkData.chunkCoord.x - currentPlayerChunkCoord.x);
            int yDiff = Mathf.Abs(chunkData.chunkCoord.y - currentPlayerChunkCoord.y);

            if (xDiff > viewDistanceInChunks + 1 || yDiff > viewDistanceInChunks + 1)
            {
                _chunksToUnloadCache.Add(chunkData.chunkCoord);
            }
        }

        foreach (Vector2Int chunkCoord in _chunksToUnloadCache)
        {
            UnloadChunk(chunkCoord);
        }
    }

    IEnumerator FullChunkGenerationSequence(ChunkData chunkData)
    {
        // Step 1: Asynchronously initialize the chunk data (heavy calculations)
        yield return StartCoroutine(worldGenerator.InitializeChunkDataCoroutine(chunkData));

        // Step 2: Asynchronously generate the visual chunk (setting tiles and spawning objects)
        yield return StartCoroutine(worldGenerator.GenerateChunk(chunkData.chunkCoord, chunkData, groundTilemap));
        
        // Step 3: Mark chunk as ready
        chunkData.status = ChunkStatus.Ready;
        chunkData.generationCoroutine = null; // Coroutine is finished
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
            // If chunk is still being generated, stop the coroutine
            if (chunkData.status == ChunkStatus.Loading && chunkData.generationCoroutine != null)
            {
                StopCoroutine(chunkData.generationCoroutine);
            }

            // Return objects to the pool
            foreach (GameObject itemObject in chunkData.spawnedItems)
            {
                Mineable mineable = itemObject.GetComponent<Mineable>();
                if (mineable != null && mineable.itemData != null)
                {
                    ObjectPooler.Instance.ReturnToPool(mineable.itemData.poolType, itemObject);
                }
                else
                {
                    Destroy(itemObject);
                }
            }
            chunkData.spawnedItems.Clear();

            // Clear tiles efficiently
            int startX = chunkCoord.x * WorldGenerator.chunkSize;
            int startY = chunkCoord.y * WorldGenerator.chunkSize;

            BoundsInt bounds = new BoundsInt(
                startX, startY, 0,
                WorldGenerator.chunkSize, WorldGenerator.chunkSize, 1
            );
            // Use the cached static array to avoid GC allocation
            groundTilemap.SetTilesBlock(bounds, emptyTileArray);

            // Remove chunk data from the map
            chunkDataMap.Remove(chunkCoord);
        }
    }

    public void TileDug(Vector3 worldPosition)
    {
        Vector3Int cellPosition = groundTilemap.WorldToCell(worldPosition);
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);

        if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
            // Prevent interaction with chunks that are not ready
            if (chunkData.status != ChunkStatus.Ready)
            {
                return;
            }

            int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);

            if (localX >= 0 && localX < WorldGenerator.chunkSize && localY >= 0 && localY < WorldGenerator.chunkSize)
            {
                if (chunkData.tileStates[localX, localY] != TileType.Empty)
                {
                    chunkData.tileStates[localX, localY] = TileType.Empty;
                    groundTilemap.SetTile(cellPosition, null);
                }
            }
        }
    }
}