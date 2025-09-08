using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using static TileType;
using static Constants; // TAG_GEM 사용을 위해 추가

public class WorldGenerator : MonoBehaviour
{
    [Header("Chunk Settings")]
    public const int chunkSize = 32; // The size of one chunk in tiles (32x32)

    [Header("World Generation Settings")]
    public MineralGenerationProfile generationProfile; // 사용할 광물 생성 프로필
    public int surfaceLevel = 80;
    public float cellSize = 0.05f;
    public float mineralSizeMultiplier = 1.5f;

    [Header("Tile Assets")]
    public RuleTile groundRuleTile; // Rule Tile for ground generation

    [Header("Performance Settings")]
    public int tilesPerFrame = 200; // How many tiles/gems to spawn per frame during incremental loading

    /// <summary>
    /// 실제 타일맵에 타일과 광물 오브젝트를 생성
    /// </summary>
    public IEnumerator GenerateChunk(Vector2Int chunkCoord, WorldManager.ChunkData chunkData, Tilemap tilemap)
    {
        if (generationProfile == null)
        {
            Debug.LogError("WorldGenerator: MineralGenerationProfile이 할당되지 않았습니다!", this);
            yield break; // 프로필이 없으면 코루틴 종료
        }

        int startX = chunkCoord.x * chunkSize;
        int startY = chunkCoord.y * chunkSize;
        int currentTilesSpawnedInFrame = 0;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                TileType tileState = chunkData.tileStates[x, y];
                if (tileState == TileType.Empty)
                {
                    continue;
                }

                int worldGridX = startX + x;
                int worldGridY = startY + y;

                TileBase tileToSet = groundRuleTile; // 기본적으로 흙 타일
                GameObject spawnedObject = null;

                // tileState가 광물인지 확인 → 프로필 순회
                foreach (var config in generationProfile.minableSpawnConfigs)
                {
                    if ((TileType)config.minableType == tileState)
                    {
                        Vector3 spawnPosition = new Vector3(worldGridX * cellSize, worldGridY * cellSize, 0);
                        spawnedObject = ObjectPooler.Instance.SpawnFromPool(config.minableType, spawnPosition, Quaternion.identity);

                        if (spawnedObject != null)
                        {
                            // 스케일 조정
                            Mineable mineableComponent = spawnedObject.GetComponent<Mineable>();
                            if (mineableComponent != null && mineableComponent.itemData != null)
                            {
                                if (mineableComponent.itemData.itemPrefab != null)
                                {
                                    SpriteRenderer prefabRenderer = mineableComponent.itemData.itemPrefab.GetComponent<SpriteRenderer>();
                                    if (prefabRenderer != null)
                                    {
                                        float targetSize = cellSize * mineralSizeMultiplier;
                                        float currentWidth = prefabRenderer.bounds.size.x;
                                        float currentHeight = prefabRenderer.bounds.size.y;
                                        float scaleFactor = targetSize / Mathf.Max(currentWidth, currentHeight);
                                        spawnedObject.transform.localScale = Vector3.one * scaleFactor;
                                    }
                                    else
                                    {
                                        spawnedObject.transform.localScale = Vector3.one * cellSize;
                                    }
                                }
                                else
                                {
                                    spawnedObject.transform.localScale = Vector3.one * cellSize;
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"생성된 오브젝트 {spawnedObject.name}에 Mineable 스크립트 또는 ItemData가 없습니다!", spawnedObject);
                                spawnedObject.transform.localScale = Vector3.one * cellSize;
                            }

                            // WorldManager가 추적할 수 있도록 리스트에 추가
                            chunkData.spawnedItems.Add(spawnedObject);
                        }

                        break; // 광물을 생성했으면 루프 종료
                    }
                }

                // 타일맵에 적용
                if (tileToSet != null)
                {
                    Vector3Int cellPosition = new Vector3Int(worldGridX, worldGridY, 0);
                    tilemap.SetTile(cellPosition, tileToSet);
                }

                // 프레임 분산 처리
                currentTilesSpawnedInFrame++;
                if (currentTilesSpawnedInFrame >= tilesPerFrame)
                {
                    currentTilesSpawnedInFrame = 0;
                    yield return null;
                }
            }
        }
    }

    /// <summary>
    /// 청크 데이터 초기화 (기본 Dirt + 광맥 배치) - 비동기 버전
    /// </summary>
    public IEnumerator InitializeChunkDataCoroutine(WorldManager.ChunkData chunkData)
    {
        if (generationProfile == null)
        {
            Debug.LogError("WorldGenerator: MineralGenerationProfile이 할당되지 않았습니다!", this);
            yield break;
        }

        // 1. 기본 지형 초기화 (surfaceLevel 위는 Empty, 아래는 Dirt)
        int startY = chunkData.chunkCoord.y * chunkSize;
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                int worldGridY = startY + y;
                if (worldGridY >= surfaceLevel)
                {
                    chunkData.tileStates[x, y] = TileType.Empty;
                }
                else
                {
                    chunkData.tileStates[x, y] = TileType.Dirt;
                }
            }
        }

        // 2. 광맥 생성
        System.Random random = new System.Random(chunkData.chunkCoord.x * 10000 + chunkData.chunkCoord.y);
        int representativeDepth = startY + (chunkSize / 2);

        foreach (var config in generationProfile.minableSpawnConfigs)
        {
            float spawnChance = config.spawnChanceByDepth.Evaluate(representativeDepth);
            if (random.NextDouble() < spawnChance)
            {
                int veinCount = random.Next(config.veinsPerChunk.x, config.veinsPerChunk.y + 1);

                for (int i = 0; i < veinCount; i++)
                {
                    int length = random.Next(config.veinLength.x, config.veinLength.y + 1);

                    // 유효한 시작점 찾기
                    int startVeinX = -1;
                    int startVeinY = -1;
                    int attempts = 0;
                    const int maxAttempts = 100;

                    while (attempts < maxAttempts)
                    {
                        int randomX = random.Next(0, chunkSize);
                        int randomY = random.Next(0, chunkSize);

                        if (chunkData.tileStates[randomX, randomY] == TileType.Dirt)
                        {
                            float tileDepth = startY + randomY;
                            if (config.spawnChanceByDepth.Evaluate(tileDepth) > 0)
                            {
                                startVeinX = randomX;
                                startVeinY = randomY;
                                break;
                            }
                        }
                        attempts++;
                    }

                    // 광맥 생성
                    if (startVeinX != -1)
                    {
                        int currentX = startVeinX;
                        int currentY = startVeinY;

                        for (int j = 0; j < length; j++)
                        {
                            if (currentX >= 0 && currentX < chunkSize && currentY >= 0 && currentY < chunkSize)
                            {
                                float tileDepth = startY + currentY;
                                if (config.spawnChanceByDepth.Evaluate(tileDepth) > 0)
                                {
                                    chunkData.tileStates[currentX, currentY] = (TileType)config.minableType;
                                }
                            }

                            // 무작위 방향 이동
                            int spacing = Mathf.Max(1, config.veinSpacing);
                            int direction = random.Next(0, 4); // 0:Up, 1:Down, 2:Left, 3:Right
                            if (direction == 0) currentY += spacing;
                            else if (direction == 1) currentY -= spacing;
                            else if (direction == 2) currentX -= spacing;
                            else if (direction == 3) currentX += spacing;
                        }
                    }
                }
            }
            // 한 종류의 광물 생성이 끝날 때마다 프레임을 넘겨 부하 분산
            yield return null;
        }
    }
}
