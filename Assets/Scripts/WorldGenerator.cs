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

    [Header("Object Prefabs")]
    public GameObject dirtPrefab;
    public GameObject stonePrefab;
    public GameObject gemPrefab;

    void Start()
    {
        GenerateWorld();
    }

    void GenerateWorld()
    {
        if (dirtPrefab == null || stonePrefab == null || gemPrefab == null)
        {
            Debug.LogError("Prefabs are not set in the WorldGenerator!");
            return;
        }

        for (int x = worldStartX; x < worldEndX; x++)
        {
            for (int y = worldStartY; y < surfaceLevel; y++)
            {
                // cellSize를 곱하여 실제 월드 좌표 계산
                Vector3 spawnPosition = new Vector3(x * cellSize, y * cellSize, 0);
                GameObject tileToPlace;

                if (y < surfaceLevel - 2 && Random.value < stoneProbability)
                {
                    tileToPlace = stonePrefab;
                }
                else
                {
                    tileToPlace = dirtPrefab;
                }

                Instantiate(tileToPlace, spawnPosition, Quaternion.identity, this.transform);
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
                    Instantiate(gemPrefab, spawnPosition, Quaternion.identity, this.transform);
                }
            }
        }
    }
}