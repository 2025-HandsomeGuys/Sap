using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldManager : MonoBehaviour
{
    [Header("References")]
    public Transform playerTransform;
    public WorldGenerator worldGenerator;

    [Header("Settings")]
    public int viewDistanceInChunks = 2;

    private Vector2Int currentPlayerChunkCoord;
    private HashSet<Vector2Int> generatedChunks = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, List<GameObject>> activeChunkObjects = new Dictionary<Vector2Int, List<GameObject>>();

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
        int x = Mathf.FloorToInt(position.x / (WorldGenerator.chunkSize * worldGenerator.cellSize));
        int y = Mathf.FloorToInt(position.y / (WorldGenerator.chunkSize * worldGenerator.cellSize));
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

                // If this chunk has not been generated yet, generate it
                if (!generatedChunks.Contains(chunkToGenerate))
                {
                    List<GameObject> spawned = worldGenerator.GenerateChunk(chunkToGenerate);
                    activeChunkObjects.Add(chunkToGenerate, spawned);
                    generatedChunks.Add(chunkToGenerate);
                    Debug.Log("Generated Chunk: " + chunkToGenerate);
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

    void UnloadChunk(Vector2Int chunkCoord)
    {
        if (activeChunkObjects.ContainsKey(chunkCoord))
        {
            foreach (GameObject obj in activeChunkObjects[chunkCoord])
            {
                if (obj != null) // Check if object still exists (e.g., not dug up)
                {
                    // Determine tag to return to pool
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
            activeChunkObjects.Remove(chunkCoord);
            generatedChunks.Remove(chunkCoord);
            Debug.Log("Unloaded Chunk: " + chunkCoord);
        }
    }
}