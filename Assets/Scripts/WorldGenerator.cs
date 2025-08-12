using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldGenerator : MonoBehaviour
{
    [Header("Chunk Settings")]
    public const int chunkSize = 32; // The size of one chunk in tiles (32x32)

    [Header("World Generation Settings")]
    public int surfaceLevel = 80;
    public float cellSize = 0.05f;

    [Header("Tile Probabilities")]
    [Range(0f, 1f)]
    public float stoneProbability = 0.5f;
    [Range(0f, 1f)]
    public float gemSpawnChance = 0.05f;

    [Header("Performance Settings")]
    public int tilesPerFrame = 100; // How many tiles/gems to spawn per frame during incremental loading

    // This method generates a single chunk based on its coordinate incrementally
    // It now uses ChunkData to determine what to spawn
    public IEnumerator GenerateChunk(Vector2Int chunkCoord, WorldManager.ChunkData chunkData) // Added ChunkData parameter
    {
        // Calculate the starting world grid coordinates for this chunk
        int startX = chunkCoord.x * chunkSize;
        int startY = chunkCoord.y * chunkSize;

        int currentTilesSpawnedInFrame = 0; // Counter for incremental loading

        // Loop through all the tile positions within this chunk
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                // Calculate the absolute grid coordinate for the tile
                int worldGridX = startX + x;
                int worldGridY = startY + y;

                // --- Tile Generation based on ChunkData ---
                byte tileState = chunkData.tileStates[x, y]; // Get state from ChunkData

                if (tileState == 0) // If state is 0 (empty/dug), skip spawning
                {
                    continue;
                }

                Vector3 spawnPosition = new Vector3(worldGridX * cellSize, worldGridY * cellSize, 0);
                string tagToSpawn = "";

                if (tileState == 1) // Dirt
                {
                    tagToSpawn = "dirt";
                }
                else if (tileState == 2) // Stone
                {
                    tagToSpawn = "stone";
                }

                if (!string.IsNullOrEmpty(tagToSpawn))
                {
                    GameObject tile = ObjectPooler.Instance.SpawnFromPool(tagToSpawn, spawnPosition, Quaternion.identity);
                    if (tile != null)
                    {
                        tile.transform.SetParent(this.transform);
                        chunkData.spawnedTiles.Add(tile); // Add to ChunkData's list
                    }
                }

                // --- Gem Generation (still random, not stored in ChunkData) ---
                if (Random.value < gemSpawnChance)
                {
                    GameObject gem = ObjectPooler.Instance.SpawnFromPool("gem", spawnPosition, Quaternion.identity);
                    if (gem != null)
                    {
                        gem.transform.SetParent(this.transform);
                    }
                }

                // Incremental loading logic
                currentTilesSpawnedInFrame++;
                if (currentTilesSpawnedInFrame >= tilesPerFrame)
                {
                    currentTilesSpawnedInFrame = 0;
                    yield return null; // Pause execution for one frame
                }
            }
        }
    }

    public void InitializeChunkData(WorldManager.ChunkData chunkData)
    {
        // Use a seeded random number generator for deterministic terrain generation per chunk
        // The seed should be based on chunk coordinates to ensure consistency
        System.Random random = new System.Random(chunkData.chunkCoord.x * 10000 + chunkData.chunkCoord.y);

        int startX = chunkData.chunkCoord.x * chunkSize;
        int startY = chunkData.chunkCoord.y * chunkSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                int worldGridY = startY + y;

                if (worldGridY >= surfaceLevel)
                {
                    chunkData.tileStates[x, y] = 0; // Empty (air)
                }
                else
                {
                    // Apply stone probability using the seeded random
                    if (worldGridY < surfaceLevel - 2 && random.NextDouble() < stoneProbability)
                    {
                        chunkData.tileStates[x, y] = 2; // Stone
                    }
                    else
                    {
                        chunkData.tileStates[x, y] = 1; // Dirt
                    }
                }
            }
        }
    }
}
