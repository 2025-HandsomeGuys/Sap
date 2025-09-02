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
    public TileBase dirtTile; // 광물 아래에 깔아줄 기본 땅 타일

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
                tileToSet = dirtTile;

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
                    chunkData.tileStates[x, y] = TileType.Empty;
                }
                else
                {
                    TileType assignedTileType = TileType.Dirt; // 기본값은 흙

                    // 광물 생성 프로필을 순회하며 깊이와 확률에 따라 타일 타입을 결정
                    // minableSpawnConfigs 리스트는 희귀한 광물부터 먼저 체크하도록 정렬하는 것이 좋습니다.
                    foreach (var config in generationProfile.minableSpawnConfigs)
                    {
                        float spawnChance = config.spawnChanceByDepth.Evaluate(worldGridY);
                        if (random.NextDouble() < spawnChance)
                        {
                            // PoolableType과 TileType Enum 값이 일치한다고 가정
                            assignedTileType = (TileType)config.minableType;
                            break; // 하나라도 생성되면 더 이상 체크하지 않음
                        }
                    }
                    chunkData.tileStates[x, y] = assignedTileType;
                }
            }
        }
    }
}
