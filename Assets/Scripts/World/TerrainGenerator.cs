using System.Collections;
using UnityEngine;
using static MineralGenerationProfile;

public class TerrainGenerator
{
    private readonly TerrainGenerationProfile _terrainProfile;
    private readonly int _bedrockStartDepth;
    private const int chunkSize = WorldGenerator.chunkSize;

    public TerrainGenerator(TerrainGenerationProfile terrainProfile, int bedrockStartDepth)
    {
        _terrainProfile = terrainProfile;
        _bedrockStartDepth = bedrockStartDepth;
    }

    public IEnumerator FillBaseTerrain(WorldManager.ChunkData chunkData, int worldStartY)
    {
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                int worldY = worldStartY + y;
                TerrainLayer layer = WorldManager.Instance.GetLayerForDepth(worldY);

                TileType assignedTileType;

                if (layer != null)
                {
                    assignedTileType = layer.baseTileType;
                }
                else
                {
                    assignedTileType = TileType.Empty; // Assign empty for sky areas
                }

                // Override with Bedrock if below the bedrock start depth
                if (worldY <= _bedrockStartDepth)
                {
                    assignedTileType = TileType.Bedrock;
                }

                chunkData.terrainLayer[x, y] = assignedTileType;
                chunkData.mineralLayer[x, y] = MineralID.None; // Initialize mineral layer
            }
        }
        yield return null;
    }

    public IEnumerator GenerateDiagonalVeins(WorldManager.ChunkData chunkData, int worldStartY)
    {
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                int worldY = worldStartY + y;
                TerrainLayer layer = WorldManager.Instance.GetLayerForDepth(worldY);

                if (layer == null || !layer.hasDiagonalVeins) continue;
                if (chunkData.terrainLayer[x, y] != layer.baseTileType) continue;

                if (CheckDiagonalNoise(chunkData.chunkCoord, x, y, layer))
                    chunkData.terrainLayer[x, y] = layer.diagonalVeinTile;
            }
        }
        yield return null;
    }

    private bool CheckDiagonalNoise(Vector2Int chunkCoord, int x, int y, TerrainLayer layer)
    {
        float worldX = chunkCoord.x * chunkSize + x;
        float worldY = chunkCoord.y * chunkSize + y;

        // Angle variation
        float angleNoise = Mathf.PerlinNoise(
            (worldX + 1000) * layer.veinAngleNoiseScale,
            (worldY + 1000) * layer.veinAngleNoiseScale);
        float angle = Mathf.Lerp(layer.veinAngleRange.x, layer.veinAngleRange.y, angleNoise);

        float rad = angle * Mathf.Deg2Rad;
        float rotatedX = worldX * Mathf.Cos(rad) - worldY * Mathf.Sin(rad);
        float rotatedY = worldX * Mathf.Sin(rad) + worldY * Mathf.Cos(rad);

        float mainNoise = Mathf.PerlinNoise(rotatedX * layer.veinNoiseScale,
                                            rotatedY * layer.veinNoiseScale * 0.1f);
        float thicknessNoise = Mathf.PerlinNoise(worldX * layer.veinThicknessNoiseScale,
                                                 worldY * layer.veinThicknessNoiseScale);

        return mainNoise - thicknessNoise > layer.veinThreshold;
    }

    public bool LayerIntersectsChunk(TerrainLayer layer, int chunkStart, int chunkEnd)
    {
        return !(layer.startDepth < chunkStart && WorldManager.Instance.GetNextLayerDepth(layer) > chunkEnd);
    }
}

