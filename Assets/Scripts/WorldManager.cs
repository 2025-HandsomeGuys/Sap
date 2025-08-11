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

        // Loop through all chunk positions within the view distance
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
                    worldGenerator.GenerateChunk(chunkToGenerate);
                    generatedChunks.Add(chunkToGenerate);
                    
                }
            }
        }
        // TODO: Add logic here to unload chunks that are too far away
    }
}
