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

                TileBase tileToSet = null;
                GameObject spawnedObject = null; // 생성된 광물 오브젝트를 담을 변수

                // 기본적으로 흙 타일을 깔아줌
                tileToSet = groundRuleTile;

                // tileState가 광물 아이템에 해당하는지 확인
                // 프로필의 설정을 순회하며 TileType과 일치하는지 찾음
                foreach (var config in generationProfile.minableSpawnConfigs)
                {
                    // PoolableType Enum 값과 TileType Enum 값이 일치한다고 가정
                    if ((TileType)config.minableType == tileState)
                    {
                        Vector3 spawnPosition = new Vector3(worldGridX * cellSize, worldGridY * cellSize, 0);
                        spawnedObject = ObjectPooler.Instance.SpawnFromPool(config.minableType, spawnPosition, Quaternion.identity);

                        if (spawnedObject != null)
                        {
                            // Mineable 컴포넌트를 가져와 itemData에 접근
                            Mineable mineableComponent = spawnedObject.GetComponent<Mineable>();
                            if (mineableComponent != null && mineableComponent.itemData != null)
                            {
                                // Item 데이터의 itemPrefab을 사용하여 스케일 조정
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
                                        // 프리팹에 SpriteRenderer가 없으면 기본 스케일 적용
                                        spawnedObject.transform.localScale = Vector3.one * cellSize;
                                    }
                                }
                                else
                                {
                                    // itemPrefab이 null이면 기본 스케일 적용
                                    spawnedObject.transform.localScale = Vector3.one * cellSize;
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"생성된 오브젝트 {spawnedObject.name}에 Mineable 스크립트 또는 ItemData가 없습니다!", spawnedObject);
                                spawnedObject.transform.localScale = Vector3.one * cellSize; // 기본 스케일
                            }

                            // WorldManager가 추적할 수 있도록 리스트에 추가
                            chunkData.spawnedItems.Add(spawnedObject);
                        }
                        break; // 광물을 찾아서 생성했으면 설정 루프를 빠져나옴
                    }
                }

                if (tileToSet != null)
                {
                    Vector3Int cellPosition = new Vector3Int(worldGridX, worldGridY, 0);
                    tilemap.SetTile(cellPosition, tileToSet);
                }

                currentTilesSpawnedInFrame++;
                if (currentTilesSpawnedInFrame >= tilesPerFrame)
                {
                    currentTilesSpawnedInFrame = 0;
                    yield return null;
                }
            }
        }
    }

    public void InitializeChunkData(WorldManager.ChunkData chunkData)
    {
        if (generationProfile == null)
        {
            Debug.LogError("WorldGenerator: MineralGenerationProfile이 할당되지 않았습니다!", this);
            return;
        }

        // 1. 기본 지형(흙) 및 빈 공간 초기화
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

        // 2. 청크 기반 광맥 생성
        System.Random random = new System.Random(chunkData.chunkCoord.x * 10000 + chunkData.chunkCoord.y);
        int representativeDepth = startY + (chunkSize / 2);

        foreach (var config in generationProfile.minableSpawnConfigs)
        {
            // 청크의 대표 깊이를 기준으로 이 광물이 스폰될지 결정
            float spawnChance = config.spawnChanceByDepth.Evaluate(representativeDepth);
            if (random.NextDouble() < spawnChance)
            {
                // 스폰될 광맥의 수 결정
                int veinCount = random.Next(config.veinsPerChunk.x, config.veinsPerChunk.y + 1);

                for (int i = 0; i < veinCount; i++)
                {
                    // 광맥의 길이 결정
                    int length = random.Next(config.veinLength.x, config.veinLength.y + 1);

                    // 청크 내에서 광맥 시작점 찾기 (땅 속에서만 시작하도록)
                    int startVeinX = -1;
                    int startVeinY = -1;
                    int attempts = 0;
                    const int maxAttempts = 100; // 무한 루프 방지

                    while (attempts < maxAttempts)
                    {
                        int randomX = random.Next(0, chunkSize);
                        int randomY = random.Next(0, chunkSize);

                        // 해당 위치가 흙 타일인지 확인
                        if (chunkData.tileStates[randomX, randomY] == TileType.Dirt)
                        {
                            // 해당 깊이에서 광물이 생성될 수 있는지 추가 확인
                            float tileDepth = startY + randomY;
                            if (config.spawnChanceByDepth.Evaluate(tileDepth) > 0)
                            {
                                startVeinX = randomX;
                                startVeinY = randomY;
                                break; // 유효한 위치를 찾았으므로 루프 종료
                            }
                        }
                        attempts++;
                    }

                    // 유효한 시작점을 찾은 경우에만 광맥 생성 진행
                    if (startVeinX != -1)
                    {
                        int currentX = startVeinX;
                        int currentY = startVeinY;

                        for (int j = 0; j < length; j++)
                        {
                            // 현재 위치가 청크 범위 내에 있는지 확인
                            if (currentX >= 0 && currentX < chunkSize && currentY >= 0 && currentY < chunkSize)
                            {
                                // 깊이 조건을 한 번 더 확인하여 월드 경계 근처에 이상한 광물이 생기는 것을 방지
                                float tileDepth = startY + currentY;
                                if (config.spawnChanceByDepth.Evaluate(tileDepth) > 0) // 해당 깊이에서 생성 확률이 0보다 클 때만
                                {
                                     chunkData.tileStates[currentX, currentY] = (TileType)config.minableType;
                                }
                            }

                            // 다음 위치로 이동 (4방향 무작위, 간격 적용)
                            int spacing = Mathf.Max(1, config.veinSpacing); // spacing이 0이하가 되는 것을 방지
                            int direction = random.Next(0, 4); // 0:Up, 1:Down, 2:Left, 3:Right
                            if (direction == 0) currentY += spacing;
                            else if (direction == 1) currentY -= spacing;
                            else if (direction == 2) currentX -= spacing;
                            else if (direction == 3) currentX += spacing;
                        }
                    }
                }
            }
        }
    }
}
