using System.Collections.Generic;
using UnityEngine;

public class GemPlacer : MonoBehaviour
{
    [Header("Placement Range")]
    public Vector2 minCoords = new Vector2(-10, -10); // 보석이 생성될 최소 월드 좌표
    public Vector2 maxCoords = new Vector2(10, 10);   // 보석이 생성될 최대 월드 좌표

    [Header("Gem Settings")]
    public GameObject gemPrefab;
    [Range(0f, 1f)]
    public float gemSpawnChance = 0.05f; // 보석 생성 확률
    public float minGemDistance = 5.0f; // 보석 사이의 최소 거리

    private List<Vector3> gemPositions = new List<Vector3>();

    void Start()
    {
        if (gemPrefab == null)
        {
            Debug.LogError("Gem Prefab is not set in the GemPlacer!");
            return;
        }

        PlaceGems();
    }

    void PlaceGems()
    {
        // 지정된 좌표 범위 내에서 보석을 배치합니다.
        // 타일 크기(cellSize)를 고려하여 정수 좌표로 순회합니다.
        // 현재는 cellSize가 1.0f라고 가정합니다.
        for (int x = Mathf.FloorToInt(minCoords.x); x <= Mathf.CeilToInt(maxCoords.x); x++)
        {
            for (int y = Mathf.FloorToInt(minCoords.y); y <= Mathf.CeilToInt(maxCoords.y); y++)
            {
                Vector3 spawnPosition = new Vector3(x, y, 0); // z는 0으로 고정

                if (TrySpawnGem(spawnPosition))
                {
                    // 보석을 생성하고, z 위치를 살짝 조정하여 흙 뒤에 배치될 수 있도록 합니다.
                    Vector3 gemPosition = new Vector3(spawnPosition.x, spawnPosition.y, spawnPosition.z + 0.1f);
                    Instantiate(gemPrefab, gemPosition, Quaternion.identity, this.transform);
                }
            }
        }
    }

    bool TrySpawnGem(Vector3 position)
    {
        if (Random.value < gemSpawnChance)
        {
            bool canSpawn = true;
            foreach (Vector3 existingGemPos in gemPositions)
            {
                if (Vector3.Distance(position, existingGemPos) < minGemDistance)
                {
                    canSpawn = false;
                    break;
                }
            }

            if (canSpawn)
            {
                gemPositions.Add(position);
                return true;
            }
        }
        return false;
    }
}