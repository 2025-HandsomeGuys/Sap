using System.Collections; // Added for IEnumerator
using System.Collections.Generic; // Still needed for List (though not returned)
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
    public IEnumerator GenerateChunk(Vector2Int chunkCoord) // Changed return type to IEnumerator
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

                // Only generate tiles below the surface level
                if (worldGridY >= surfaceLevel)
                {
                    continue;
                }

                // --- Tile Generation ---
                Vector3 spawnPosition = new Vector3(worldGridX * cellSize, worldGridY * cellSize, 0);
                string tagToSpawn;

                if (worldGridY < surfaceLevel - 2 && Random.value < stoneProbability)
                {
                    tagToSpawn = "stone";
                }
                else
                {
                    tagToSpawn = "dirt";
                }

                GameObject tile = ObjectPooler.Instance.SpawnFromPool(tagToSpawn, spawnPosition, Quaternion.identity);
                if (tile != null)
                {
                    tile.transform.SetParent(this.transform);
                    // spawnedObjects.Add(tile); // Removed: no longer returning list
                }

                // --- Gem Generation (within the same loop for efficiency) ---
                if (Random.value < gemSpawnChance)
                {
                    // We can use the same spawnPosition for the gem
                    GameObject gem = ObjectPooler.Instance.SpawnFromPool("gem", spawnPosition, Quaternion.identity);
                    if (gem != null)
                    {
                        gem.transform.SetParent(this.transform);
                        // spawnedObjects.Add(gem); // Removed: no longer returning list
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
        // yield return spawnedObjects; // Removed: no longer returning list
    }
}
