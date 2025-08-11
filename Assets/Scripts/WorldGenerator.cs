using UnityEngine;

public class WorldGenerator : MonoBehaviour
{
    [Header("Chunk Settings")]
    public const int chunkSize = 64; // The size of one chunk in tiles (64x64)

    [Header("World Generation Settings")]
    public int surfaceLevel = 80;
    public float cellSize = 0.05f;

    [Header("Tile Probabilities")]
    [Range(0f, 1f)]
    public float stoneProbability = 0.5f;
    [Range(0f, 1f)]
    public float gemSpawnChance = 0.05f;

    // This method generates a single chunk based on its coordinate
    public void GenerateChunk(Vector2Int chunkCoord)
    {
        // Calculate the starting world grid coordinates for this chunk
        int startX = chunkCoord.x * chunkSize;
        int startY = chunkCoord.y * chunkSize;

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
                }

                // --- Gem Generation (within the same loop for efficiency) ---
                if (Random.value < gemSpawnChance)
                {
                    // We can use the same spawnPosition for the gem
                    GameObject gem = ObjectPooler.Instance.SpawnFromPool("gem", spawnPosition, Quaternion.identity);
                    if (gem != null)
                    {
                        gem.transform.SetParent(this.transform);
                    }
                }
            }
        }
    }
}
