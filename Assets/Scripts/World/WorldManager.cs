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

    private static TileBase[] emptyTileArray;
    private readonly List<Vector2Int> _chunksToUnloadCache = new List<Vector2Int>();

    public class ChunkData
    {
        public TileType[,] tileStates;
        public Vector2Int chunkCoord;
        public List<GameObject> spawnedItems;
        public ChunkStatus status;
        public Coroutine generationCoroutine;

        public ChunkData(Vector2Int coord, int chunkSize)
        {
            chunkCoord = coord;
            tileStates = new TileType[chunkSize, chunkSize];
            spawnedItems = new List<GameObject>();
            status = ChunkStatus.Loading;
            generationCoroutine = null;
        }
    }

    private Vector2Int currentPlayerChunkCoord;
    private readonly Dictionary<Vector2Int, ChunkData> chunkDataMap = new Dictionary<Vector2Int, ChunkData>();

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

        for (int xOffset = -viewDistanceInChunks; xOffset <= viewDistanceInChunks; xOffset++)
        {
            for (int yOffset = -viewDistanceInChunks; yOffset <= viewDistanceInChunks; yOffset++)
            {
                Vector2Int chunkCoord = new Vector2Int(currentPlayerChunkCoord.x + xOffset, currentPlayerChunkCoord.y + yOffset);

                if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
                {
                    // If chunk data exists but is unloaded, reload it
                    if (chunkData.status == ChunkStatus.Unloaded)
                    {
                        chunkData.generationCoroutine = StartCoroutine(RenderChunkCoroutine(chunkData));
                    }
                }
                else
                {
                    // If chunk data doesn't exist, create it for the first time
                    var newChunkData = new ChunkData(chunkCoord, WorldGenerator.chunkSize);
                    chunkDataMap.Add(chunkCoord, newChunkData);
                    newChunkData.generationCoroutine = StartCoroutine(FullChunkGenerationSequence(newChunkData));
                }
            }
        }

        _chunksToUnloadCache.Clear();
        foreach (var chunkData in chunkDataMap.Values)
        {
            if (chunkData.status == ChunkStatus.Ready)
            {
                int xDiff = Mathf.Abs(chunkData.chunkCoord.x - currentPlayerChunkCoord.x);
                int yDiff = Mathf.Abs(chunkData.chunkCoord.y - currentPlayerChunkCoord.y);

                if (xDiff > viewDistanceInChunks + 1 || yDiff > viewDistanceInChunks + 1)
                {
                    _chunksToUnloadCache.Add(chunkData.chunkCoord);
                }
            }
        }

        foreach (Vector2Int chunkCoord in _chunksToUnloadCache)
        {
            UnloadChunk(chunkCoord);
        }
    }

    IEnumerator FullChunkGenerationSequence(ChunkData chunkData)
    {
        chunkData.status = ChunkStatus.Loading;
        yield return StartCoroutine(worldGenerator.InitializeChunkDataCoroutine(chunkData));
        yield return StartCoroutine(worldGenerator.GenerateChunk(chunkData.chunkCoord, chunkData, groundTilemap));
        chunkData.status = ChunkStatus.Ready;
        chunkData.generationCoroutine = null;
    }

    IEnumerator RenderChunkCoroutine(ChunkData chunkData)
    {
        chunkData.status = ChunkStatus.Loading; // Mark as loading while we render
        yield return StartCoroutine(worldGenerator.GenerateChunk(chunkData.chunkCoord, chunkData, groundTilemap));
        chunkData.status = ChunkStatus.Ready;
        chunkData.generationCoroutine = null;
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
            if (chunkData.status == ChunkStatus.Loading && chunkData.generationCoroutine != null)
            {
                StopCoroutine(chunkData.generationCoroutine);
            }

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

            int startX = chunkCoord.x * WorldGenerator.chunkSize;
            int startY = chunkCoord.y * WorldGenerator.chunkSize;
            BoundsInt bounds = new BoundsInt(startX, startY, 0, WorldGenerator.chunkSize, WorldGenerator.chunkSize, 1);
            groundTilemap.SetTilesBlock(bounds, emptyTileArray);

            // Instead of removing the data, just mark it as unloaded
            chunkData.status = ChunkStatus.Unloaded;
        }
    }

    public void TileDug(Vector3 worldPosition)
    {
        Vector3Int cellPosition = groundTilemap.WorldToCell(worldPosition);
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);

        if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
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

    public TileType GetTileTypeAt(Vector3 worldPosition)
    {
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);

        if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
            // We can read tile data even from unloaded chunks
            if (chunkData.status == ChunkStatus.Loading)
            {
                return TileType.Empty; // Can't get data while it's being generated
            }

            Vector3Int cellPosition = groundTilemap.WorldToCell(worldPosition);
            int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);

            if (localX >= 0 && localX < WorldGenerator.chunkSize && localY >= 0 && localY < WorldGenerator.chunkSize)
            {
                return chunkData.tileStates[localX, localY];
            }
        }

        return TileType.Empty; // Default if chunk or tile is not found
    }
}
