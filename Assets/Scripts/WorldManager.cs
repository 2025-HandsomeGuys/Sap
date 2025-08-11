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
    private HashSet<Vector2Int> loadingChunks = new HashSet<Vector2Int>(); // To track chunks currently being loaded

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
                    StartCoroutine(GenerateChunkCoroutineWrapper(chunkToGenerate));
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

    IEnumerator GenerateChunkCoroutineWrapper(Vector2Int chunkCoord)
    {
        // Call the WorldGenerator's coroutine
        yield return StartCoroutine(worldGenerator.GenerateChunk(chunkCoord));

        // Once the generation coroutine is complete, move from loading to generated
        loadingChunks.Remove(chunkCoord);
        generatedChunks.Add(chunkCoord);
        Debug.Log("Finished Loading Chunk: " + chunkCoord);
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        // Iterate through all children of the WorldGenerator to find objects in this chunk
        // This is less efficient than having a direct list, but necessary for this approach
        List<GameObject> objectsInChunk = new List<GameObject>();
        // Iterate backwards to safely remove children if needed, though we are just collecting here
        for (int i = worldGenerator.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = worldGenerator.transform.GetChild(i);
            Vector2Int childChunkCoord = GetChunkCoordFromPosition(child.position);
            if (childChunkCoord == chunkCoord)
            {
                objectsInChunk.Add(child.gameObject);
            }
        }

        foreach (GameObject obj in objectsInChunk)
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
                    Debug.LogWarning("Object in chunk " + chunkCoord + " has no recognized tag for pooling: " + obj.name);
                    Destroy(obj); // Fallback if tag is not recognized
                }
            }
        }
        // Remove from generatedChunks (loadingChunks is handled by wrapper)
        generatedChunks.Remove(chunkCoord);
        Debug.Log("Unloaded Chunk: " + chunkCoord);
    }
}
