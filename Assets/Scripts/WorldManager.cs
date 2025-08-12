using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    [Header("Settings")]
    public int viewDistanceInChunks = 2;

    // New: Data structure to hold chunk state
    public class ChunkData
        {
            public byte[,] tileStates; // 0: empty, 1: dirt, 2: stone
            public Vector2Int chunkCoord; // Store chunk coordinates for easy reference
            public List<GameObject> spawnedTiles; // New: Store references to spawned tiles in this chunk

            public ChunkData(Vector2Int coord, int chunkSize)
            {
                chunkCoord = coord;
                tileStates = new byte[chunkSize, chunkSize];
                spawnedTiles = new List<GameObject>(); // Initialize the list
            }
        }

    private Vector2Int currentPlayerChunkCoord;
    private HashSet<Vector2Int> generatedChunks = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> loadingChunks = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, ChunkData> chunkDataMap = new Dictionary<Vector2Int, ChunkData>(); // New: Stores the state of each chunk

    void Start()
    {
        if (playerTransform == null || worldGenerator == null)
        {
            Debug.LogError("Player Transform or World Generator not assigned in WorldManager!");
            this.enabled = false; // Disable this script if references are missing
            return;
        }

        // Initial chunk generation around the player
        UpdateChunks();
    }

    void Update()
    {
        // Calculate the player's current chunk coordinate
        Vector2Int playerChunk = GetChunkCoordFromPosition(playerTransform.position);

        // If the player has moved to a new chunk, update the world
        if (playerChunk != currentPlayerChunkCoord)
        {
            currentPlayerChunkCoord = playerChunk;
            UpdateChunks();
        }
    }

    Vector2Int GetChunkCoordFromPosition(Vector3 position)
    {
        // Ensure worldGenerator.cellSize is not zero to prevent division by zero
        float effectiveCellSize = worldGenerator.cellSize > 0 ? worldGenerator.cellSize : 1f; 
        float chunkSizeInWorldUnits = WorldGenerator.chunkSize * effectiveCellSize;

        int x = Mathf.FloorToInt(position.x / chunkSizeInWorldUnits);
        int y = Mathf.FloorToInt(position.y / chunkSizeInWorldUnits);
        return new Vector2Int(x, y);
    }

    void UpdateChunks()
    {
        currentPlayerChunkCoord = GetChunkCoordFromPosition(playerTransform.position);

        // --- Load/Generate new chunks --- 
        for (int xOffset = -viewDistanceInChunks; xOffset <= viewDistanceInChunks; xOffset++)
        {
            for (int yOffset = -viewDistanceInChunks; yOffset <= viewDistanceInChunks; yOffset++)
            {
                Vector2Int chunkToGenerate = new Vector2Int(
                    currentPlayerChunkCoord.x + xOffset,
                    currentPlayerChunkCoord.y + yOffset
                );

                // If this chunk has not been generated yet and is not currently loading, start generation
                if (!generatedChunks.Contains(chunkToGenerate) && !loadingChunks.Contains(chunkToGenerate))
                {
                    loadingChunks.Add(chunkToGenerate);
                    Debug.Log($"Attempting to generate chunk: {chunkToGenerate}");

                    // Get or create ChunkData for this chunk
                    ChunkData chunkData;
                    if (!chunkDataMap.TryGetValue(chunkToGenerate, out chunkData))
                    {
                        // If ChunkData doesn't exist, create new default data
                        chunkData = new ChunkData(chunkToGenerate, WorldGenerator.chunkSize); // Updated constructor call
                        worldGenerator.InitializeChunkData(chunkData); // Initialize tileStates
                        chunkDataMap.Add(chunkToGenerate, chunkData);
                    }

                    StartCoroutine(GenerateChunkCoroutineWrapper(chunkToGenerate, chunkData)); // Pass chunkData
                }
            }
        }

        // --- Unload chunks that are too far away ---
        List<Vector2Int> chunksToUnload = new List<Vector2Int>();
        foreach (Vector2Int chunkCoord in generatedChunks)
        {
            int xDiff = Mathf.Abs(chunkCoord.x - currentPlayerChunkCoord.x);
            int yDiff = Mathf.Abs(chunkCoord.y - currentPlayerChunkCoord.y);

            // If chunk is outside the view distance, mark for unloading
            // We use viewDistanceInChunks + 1 to create a buffer zone
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

    IEnumerator GenerateChunkCoroutineWrapper(Vector2Int chunkCoord, ChunkData chunkData) // Pass chunkData
    {
        Debug.Log($"Starting generation for chunk: {chunkCoord}");
        // Call the WorldGenerator's coroutine
        yield return StartCoroutine(worldGenerator.GenerateChunk(chunkCoord, chunkData)); // Pass chunkData

        // Once the generation coroutine is complete, move from loading to generated
        loadingChunks.Remove(chunkCoord);
        generatedChunks.Add(chunkCoord);
        Debug.Log($"Finished Loading Chunk: {chunkCoord}");
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        Debug.Log($"Attempting to unload chunk: {chunkCoord}");
        if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
            foreach (GameObject obj in chunkData.spawnedTiles)
            {
                if (obj != null) // Check if object still exists (e.g., not dug up)
                {
                    string tag = "";
                    if (obj.CompareTag("dirt")) tag = "dirt";
                    else if (obj.CompareTag("stone")) tag = "stone";
                    else if (obj.CompareTag("gem")) tag = "gem";

                    if (!string.IsNullOrEmpty(tag))
                    {
                        ObjectPooler.Instance.ReturnToPool(tag, obj);
                    }
                    else
                    {
                        // If object has no recognized tag, just destroy it (or log a warning)
                        Debug.LogWarning("Object in chunk " + chunkCoord + " has no recognized tag for pooling: " + obj.name);
                        Destroy(obj);
                    }
                }
            }
            chunkData.spawnedTiles.Clear(); // Clear the list after returning objects to pool
        }
        // Remove from generatedChunks (loadingChunks is handled by wrapper)
        generatedChunks.Remove(chunkCoord);
        Debug.Log($"Unloaded Chunk: {chunkCoord}");
    }

    // New: Method to update chunk data when a tile is dug
    public void TileDug(Vector3 worldPosition)
    {
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);
        if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
            // Calculate tile's local coordinates within the chunk
            float effectiveCellSize = worldGenerator.cellSize > 0 ? worldGenerator.cellSize : 1f;
            // No need for chunkSizeInWorldUnits here, just effectiveCellSize

            int tileX = Mathf.FloorToInt(worldPosition.x / effectiveCellSize);
            int tileY = Mathf.FloorToInt(worldPosition.y / effectiveCellSize);

            // Convert world grid coords to local chunk grid coords
            int localX = tileX - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = tileY - (chunkCoord.y * WorldGenerator.chunkSize);

            if (localX >= 0 && localX < WorldGenerator.chunkSize &&
                localY >= 0 && localY < WorldGenerator.chunkSize)
            {
                chunkData.tileStates[localX, localY] = 0; // Set to empty
                Debug.Log("Tile dug at " + worldPosition + " in chunk " + chunkCoord + " (local: " + localX + "," + localY + ")");
            }
        }
    }
}