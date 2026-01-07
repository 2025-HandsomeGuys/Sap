using System.Collections;
using UnityEngine;
using static MineralGenerationProfile;

public class MineralGenerator
{
    private readonly TerrainGenerationProfile _terrainProfile;
    private const int chunkSize = WorldGenerator.chunkSize;

    public MineralGenerator(TerrainGenerationProfile terrainProfile)
    {
        _terrainProfile = terrainProfile;
    }

    public IEnumerator GenerateMineralVeins(WorldManager.ChunkData chunkData, int worldStartY)
    {
        System.Random rng = new System.Random(chunkData.chunkCoord.x * 10000 + chunkData.chunkCoord.y);

        if (_terrainProfile == null || _terrainProfile.layers == null)
        {
            yield break;
        }

        foreach (var layer in _terrainProfile.layers)
        {
            int chunkEndY = worldStartY + chunkSize;
            if (!LayerIntersectsChunk(layer, worldStartY, chunkEndY)) continue;

            if (layer.mineralConfigs == null) continue;

            foreach (var mineral in layer.mineralConfigs)
            {
                int nextLayerDepth = WorldManager.Instance.GetNextLayerDepth(layer);
                float spawnChance;

                if (nextLayerDepth != int.MinValue)
                {
                    float layerThickness = Mathf.Abs(layer.startDepth - nextLayerDepth);
                    if (layerThickness == 0) layerThickness = 1;
                    int relativeDepthInPixels = worldStartY - layer.startDepth;
                    float normalizedDepth = Mathf.Abs(relativeDepthInPixels) / layerThickness;
                    normalizedDepth = Mathf.Clamp01(normalizedDepth);
                    spawnChance = mineral.spawnChanceByDepth.Evaluate(normalizedDepth);
                }
                else
                {
                    int relativeDepth = worldStartY - layer.startDepth;
                    spawnChance = mineral.spawnChanceByDepth.Evaluate(Mathf.Abs(relativeDepth));
                }

                if (rng.NextDouble() >= spawnChance) continue;

                int veinCount = rng.Next(mineral.veinsPerChunk.x, mineral.veinsPerChunk.y + 1);
                if (veinCount <= 0) continue;

                for (int i = 0; i < veinCount; i++)
                {
                    int startX = rng.Next(0, chunkSize);
                    int startY = rng.Next(0, chunkSize);
                    int worldY = worldStartY + startY;

                    if (worldY <= layer.startDepth && worldY > WorldManager.Instance.GetNextLayerDepth(layer))
                    {
                        GenerateVein(chunkData, rng, mineral, startX, startY);
                    }
                }
            }
            yield return null;
        }
    }

    private bool LayerIntersectsChunk(TerrainLayer layer, int chunkStart, int chunkEnd)
    {
        return !(layer.startDepth < chunkStart && WorldManager.Instance.GetNextLayerDepth(layer) > chunkEnd);
    }

    private void GenerateVein(WorldManager.ChunkData chunkData, System.Random rng,
                              MinableSpawnConfig config, int startX, int startY)
    {
        int length = rng.Next(config.veinLength.x, config.veinLength.y + 1);
        int x = startX, y = startY;

        for (int i = 0; i < length; i++)
        {
            if (IsInsideChunk(x, y) && IsBaseTile(chunkData.terrainLayer[x, y]) && chunkData.mineralLayer[x, y] == MineralID.None)
            {
                chunkData.mineralLayer[x, y] = config.minableType;
            }

            (x, y) = RandomStep(x, y, rng, config.veinSpacing);
        }
    }

    private bool IsInsideChunk(int x, int y) =>
        x >= 0 && x < chunkSize && y >= 0 && y < chunkSize;

    private (int, int) RandomStep(int x, int y, System.Random rng, int spacing)
    {
        int dir = rng.Next(0, 4);
        for (int s = 0; s < spacing; s++)
        {
            if (dir == 0) y++;
            else if (dir == 1) y--;
            else if (dir == 2) x--;
            else x++;
        }
        return (x, y);
    }

    private bool IsBaseTile(TileType type) =>
        type >= TileType.Dirt && type <= TileType.MeteoriteRock;
}


