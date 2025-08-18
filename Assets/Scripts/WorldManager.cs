using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using static TileType;

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
    }
    #endregion

    [Header("References")]
    public Transform playerTransform;
    public WorldGenerator worldGenerator;
    public Tilemap groundTilemap;

    [Header("Settings")]
    public int viewDistanceInChunks = 1;

    public class ChunkData
    {
        public TileType[,] tileStates;
        public Vector2Int chunkCoord;

        public ChunkData(Vector2Int coord, int chunkSize)
        {
            chunkCoord = coord;
            tileStates = new TileType[chunkSize, chunkSize];
        }
    }

    private Vector2Int currentPlayerChunkCoord;
    private HashSet<Vector2Int> generatedChunks = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> loadingChunks = new HashSet<Vector2Int>();
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
        // Convert world position to tilemap cell position
        Vector3Int cellPos = groundTilemap.WorldToCell(position);

        // Now calculate chunk coordinate from cell position
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
                Vector2Int chunkToGenerate = new Vector2Int(currentPlayerChunkCoord.x + xOffset, currentPlayerChunkCoord.y + yOffset);
                if (!generatedChunks.Contains(chunkToGenerate) && !loadingChunks.Contains(chunkToGenerate))
                {
                    loadingChunks.Add(chunkToGenerate);
                    
                    ChunkData chunkData;
                    if (!chunkDataMap.TryGetValue(chunkToGenerate, out chunkData))
                    {
                        chunkData = new ChunkData(chunkToGenerate, WorldGenerator.chunkSize);
                        worldGenerator.InitializeChunkData(chunkData);
                        chunkDataMap.Add(chunkToGenerate, chunkData);
                    }

                    StartCoroutine(GenerateChunkCoroutineWrapper(chunkToGenerate, chunkData));
                }
            }
        }

        List<Vector2Int> chunksToUnload = new List<Vector2Int>();
        foreach (Vector2Int chunkCoord in generatedChunks)
        {
            int xDiff = Mathf.Abs(chunkCoord.x - currentPlayerChunkCoord.x);
            int yDiff = Mathf.Abs(chunkCoord.y - currentPlayerChunkCoord.y);

            if (xDiff > viewDistanceInChunks + 1 || yDiff > viewDistanceInChunks + 1)
            {
                chunksToUnload.Add(chunkCoord);
            }
        }

        foreach (Vector2Int chunkCoord in chunksToUnload)
        {
            UnloadChunk(chunkCoord);
        }
    }

    IEnumerator GenerateChunkCoroutineWrapper(Vector2Int chunkCoord, ChunkData chunkData)
    {
        yield return StartCoroutine(worldGenerator.GenerateChunk(chunkCoord, chunkData, groundTilemap));
        loadingChunks.Remove(chunkCoord);
        generatedChunks.Add(chunkCoord);
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        if (generatedChunks.Remove(chunkCoord))
        {
            int startX = chunkCoord.x * WorldGenerator.chunkSize;
            int startY = chunkCoord.y * WorldGenerator.chunkSize;

            for (int x = 0; x < WorldGenerator.chunkSize; x++)
            {
                for (int y = 0; y < WorldGenerator.chunkSize; y++)
                {
                    Vector3Int cellPosition = new Vector3Int(startX + x, startY + y, 0);
                    groundTilemap.SetTile(cellPosition, null);
                }
            }
            Debug.Log($"Unloaded Chunk: {chunkCoord}");
        }
    }

    public void TileDug(Vector3 worldPosition)
    {
        Vector3Int cellPosition = groundTilemap.WorldToCell(worldPosition);
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);

        if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
            int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);

            if (localX >= 0 && localX < WorldGenerator.chunkSize && localY >= 0 && localY < WorldGenerator.chunkSize)
            {
                if(chunkData.tileStates[localX, localY] != TileType.Empty)
                {
                    chunkData.tileStates[localX, localY] = TileType.Empty;
                    groundTilemap.SetTile(cellPosition, null);
                    Debug.Log("Tile dug at " + cellPosition + " in chunk " + chunkCoord);
                }
            }
        }
    }
}