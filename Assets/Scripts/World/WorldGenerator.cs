using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using static MineralGenerationProfile;

public class WorldGenerator : MonoBehaviour
{
    [Header("Chunk Settings")]
    public const int chunkSize = 32;

    [Header("World Generation Profile")]
    public TerrainGenerationProfile terrainProfile;

    [Header("World Settings")]
    [Tooltip("월드 단위 (TerrainChunk와 1:1 매핑을 위해 0.3125f 사용)")]
    public float cellSize = 0.3125f;
    public float mineralSizeMultiplier = 1.5f;

    private int bedrockStartDepth; // Added for dynamic bedrock depth

    private void Start()
    {
        if (terrainProfile == null)
        {
            Debug.LogError("Terrain Generation Profile is not assigned in WorldGenerator!");
            return;
        }

        if (terrainProfile.layers.Count < 7)
        {
            Debug.LogError("Terrain Generation Profile does not have enough layers to determine bedrockStartDepth (requires at least 7 layers).");
            return;
        }

        bedrockStartDepth = terrainProfile.layers[6].startDepth;
    }

    [Header("Base Tile Assets")]
    public RuleTile dirtTile;
    public RuleTile hardStoneTile;
    public RuleTile coolStoneTile;
    public RuleTile iceTile;
    public RuleTile hotStoneTile;
    public RuleTile magmaRockTile;
    public RuleTile meteoriteRockTile;
    public RuleTile bedrockTile;

    [Header("Performance Settings")]
    public int tilesPerFrame = 100;

    // ------------------ CHUNK TILE GENERATION ------------------

    /// <summary>
    /// Creates a 1D TileBase array for a chunk's tilemap representation.
    /// For resource tiles, it places the layer's base tile.
    /// </summary>
    public TileBase[] CreateTilebaseArray(Vector2Int chunkCoord, WorldManager.ChunkData chunkData)
    {
        TileBase[] tiles = new TileBase[chunkSize * chunkSize];

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                TileType tileState = chunkData.terrainLayer[x, y];
                int index = y * chunkSize + x;

                // The logic is now simpler: just get the asset for the terrain tile.
                // The mineral layer is purely data and not visualized directly.
                tiles[index] = GetBaseTileAsset(tileState);
            }
        }
        return tiles;
    }

    public void PreSpawnMineralsForChunk(WorldManager.ChunkData chunkData)
    {
        int startX = chunkData.chunkCoord.x * chunkSize;
        int startY = chunkData.chunkCoord.y * chunkSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                MineralID mineral = chunkData.mineralLayer[x, y];
                if (mineral != MineralID.None)
                {
                    int worldY = startY + y;
                    Vector3 position = new Vector3((startX + x) * cellSize, worldY * cellSize, 0);

                    TerrainLayer layer = WorldManager.Instance.GetLayerForDepth(worldY);
                    if (layer != null)
                    {
                        GameObject mineralObj = ObjectPooler.Instance.SpawnFromPool(layer.layerType, mineral, position, Quaternion.identity);
                        if (mineralObj != null)
                        {
                            mineralObj.SetActive(true); // Make it immediately visible
                        }
                    }
                }
            }
        }
    }

    // ------------------ CHUNK DATA INITIALIZATION ------------------

    /// <summary>
    /// Fills chunkData with terrain + veins + minerals according to the profile.
    /// </summary>
    public IEnumerator InitializeChunkDataCoroutine(WorldManager.ChunkData chunkData)
    {
        if (terrainProfile == null)
        {
            Debug.LogError("Terrain Generation Profile is not assigned!");
            yield break;
        }

        int chunkWorldStartY = chunkData.chunkCoord.y * chunkSize;

        yield return FillBaseTerrain(chunkData, chunkWorldStartY);
        yield return GenerateDiagonalVeins(chunkData, chunkWorldStartY);
        yield return GenerateMineralVeins(chunkData, chunkWorldStartY);
    }

    private IEnumerator FillBaseTerrain(WorldManager.ChunkData chunkData, int worldStartY)
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
                if (worldY <= bedrockStartDepth)
                {
                    assignedTileType = TileType.Bedrock;
                }

                chunkData.terrainLayer[x, y] = assignedTileType;
                chunkData.mineralLayer[x, y] = MineralID.None; // Initialize mineral layer
            }
        }
        yield return null;
    }

    private IEnumerator GenerateDiagonalVeins(WorldManager.ChunkData chunkData, int worldStartY)
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

    private IEnumerator GenerateMineralVeins(WorldManager.ChunkData chunkData, int worldStartY)
    {
        System.Random rng = new System.Random(chunkData.chunkCoord.x * 10000 + chunkData.chunkCoord.y);

        if (terrainProfile == null || terrainProfile.layers == null)
        {
            yield break;
        }

        foreach (var layer in terrainProfile.layers)
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

    // ------------------ UTILITIES ------------------

    private bool IsBaseTile(TileType type) =>
        type >= TileType.Dirt && type <= TileType.MeteoriteRock;

    private TileBase GetBaseTileAsset(TileType type)
    {
        return type switch
        {
            TileType.Dirt => dirtTile,
            TileType.HardStone => hardStoneTile,
            TileType.CoolStone => coolStoneTile,
            TileType.Ice => iceTile,
            TileType.HotStone => hotStoneTile,
            TileType.MagmaRock => magmaRockTile,
            TileType.MeteoriteRock => meteoriteRockTile,
            TileType.Bedrock => bedrockTile,
            _ => null
        };
    }
}
