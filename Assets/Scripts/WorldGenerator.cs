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
    public int surfaceLevel = 80;
    public float cellSize = 0.05f;

    [Header("Tile Probabilities")]
    [Range(0f, 1f)]
    public float stoneProbability = 0.5f;
    [Range(0f, 1f)]
    public float gemSpawnChance = 0.05f;

    [Header("Tile Assets")]
    public TileBase dirtTile;
    public TileBase stoneTile;
    // public TileBase gemTile; // 더 이상 타일로 그리지 않으므로 주석 처리하거나 삭제

    [Header("Performance Settings")]
    public int tilesPerFrame = 200; // How many tiles/gems to spawn per frame during incremental loading

    public IEnumerator GenerateChunk(Vector2Int chunkCoord, WorldManager.ChunkData chunkData, Tilemap tilemap)
    {
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

                if (tileState == TileType.Dirt)
                {
                    tileToSet = dirtTile;
                }
                else if (tileState == TileType.Stone)
                {
                    tileToSet = stoneTile;
                }
                else if (tileState == TileType.Gem)
                {
                    // Gem일 경우, 그 아래에 흙 타일을 깔아줌 (공중에 뜨지 않도록)
                    tileToSet = dirtTile;

                    // 그리고 Gem 프리팹을 생성
                    Vector3 spawnPosition = new Vector3(worldGridX * cellSize, worldGridY * cellSize, 0);
                    GameObject gemObject = ObjectPooler.Instance.SpawnFromPool(TAG_GEM, spawnPosition, Quaternion.identity);
                    if (gemObject != null)
                    {
                        gemObject.transform.localScale = Vector3.one * cellSize;
                        // WorldManager가 추적할 수 있도록 리스트에 추가
                        chunkData.spawnedGems.Add(gemObject);
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
                    TileType assignedTileType;
                    if (worldGridY < surfaceLevel - 2 && random.NextDouble() < stoneProbability)
                    {
                        assignedTileType = TileType.Stone;
                    }
                    else
                    {
                        assignedTileType = TileType.Dirt;
                    }

                    if (random.NextDouble() < gemSpawnChance)
                    {
                        assignedTileType = TileType.Gem;
                    }
                    chunkData.tileStates[x, y] = assignedTileType;
                }
            }
        }
    }
}
