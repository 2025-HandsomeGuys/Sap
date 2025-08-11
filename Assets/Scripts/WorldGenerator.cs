using UnityEngine;

public class WorldGenerator : MonoBehaviour
{
    [Header("World Settings")]
    public int worldStartX = -200;
    public int worldEndX = 100;
    public int worldStartY = -200;
    public int worldHeight = 100; // Note: This is not currently used in generation loops
    public int surfaceLevel = 80;
    public float cellSize = 0.05f; // 타일(오브젝트)의 크기 및 간격

    [Header("Generation Settings")]
    [Range(0f, 1f)]
    public float stoneProbability = 0.5f;
    [Range(0f, 1f)]
    public float gemSpawnChance = 0.05f; // 5% chance to spawn a gem

    // Prefab references are now managed by the ObjectPooler

    void Start()
    {
        GenerateWorld();
    }

    void GenerateWorld()
    {
        for (int x = worldStartX; x < worldEndX; x++)
        {
            for (int y = worldStartY; y < surfaceLevel; y++)
            {
                Vector3 spawnPosition = new Vector3(x * cellSize, y * cellSize, 0);
                string tagToSpawn;

                if (y < surfaceLevel - 2 && Random.value < stoneProbability)
                {
                    tagToSpawn = "stone";
                }
                else
                {
                    tagToSpawn = "dirt";
                }

                // Log the spawn position for every 20th column to avoid spamming the console
                if (x % 20 == 0 && y == worldStartY)
                {
                    Debug.Log("Calculating spawn position for tile at column x=" + x + ": " + spawnPosition);
                }

                GameObject tile = ObjectPooler.Instance.SpawnFromPool(tagToSpawn, spawnPosition, Quaternion.identity);
                if (tile != null)
                {
                    tile.transform.SetParent(this.transform);
                }
            }
        }
        GenerateGems();
    }

    void GenerateGems()
    {
        for (int x = worldStartX; x < worldEndX; x++)
        {
            for (int y = worldStartY; y < surfaceLevel; y++)
            {
                if (Random.value < gemSpawnChance)
                {
                    Vector3 spawnPosition = new Vector3(x * cellSize, y * cellSize, 0);
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