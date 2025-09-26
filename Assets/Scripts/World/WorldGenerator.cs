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
    public float cellSize = 0.05f;
    public float mineralSizeMultiplier = 1.5f;

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
        int startY = chunkCoord.y * chunkSize;
        TileBase[] tiles = new TileBase[chunkSize * chunkSize];

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                TileType tileState = chunkData.tileStates[x, y];
                int index = y * chunkSize + x;

                if (tileState == TileType.Empty)
                {
                    tiles[index] = null;
                    continue;
                }

                if (IsBaseTile(tileState))
                {
                    tiles[index] = GetBaseTileAsset(tileState);
                }
                else
                {
                    int worldY = startY + y;
                    TerrainLayer layer = GetLayerForDepth(worldY);
                    if (layer != null)
                    {
                        tiles[index] = GetBaseTileAsset(layer.baseTileType);
                    }
                }
            }
        }
        return tiles;
    }

    /// <summary>
    /// Iterates through chunk data and spawns GameObjects for any non-base resource tiles.
    /// </summary>
    public void SpawnResourceObjectsForChunk(Vector2Int chunkCoord, WorldManager.ChunkData chunkData)
    {
        int startX = chunkCoord.x * chunkSize;
        int startY = chunkCoord.y * chunkSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                TileType tileState = chunkData.tileStates[x, y];

                // If it's not a base tile, it's a resource that needs a GameObject.
                if (!IsBaseTile(tileState) && tileState != TileType.Empty && tileState != TileType.Bedrock)
                {
                    int worldX = startX + x;
                    int worldY = startY + y;
                    SpawnResourceObject(tileState,
                        new Vector3(worldX * cellSize, worldY * cellSize, 0),
                        worldY,
                        chunkData);
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
                TerrainLayer layer = GetLayerForDepth(worldY);
                chunkData.tileStates[x, y] =
                    layer != null ? layer.baseTileType : TileType.Bedrock;
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
                TerrainLayer layer = GetLayerForDepth(worldY);

                if (layer == null || !layer.hasDiagonalVeins) continue;
                if (chunkData.tileStates[x, y] != layer.baseTileType) continue;

                if (CheckDiagonalNoise(chunkData.chunkCoord, x, y, layer))
                    chunkData.tileStates[x, y] = layer.diagonalVeinTile;
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
        Debug.Log($"[WorldGenerator] Starting GenerateMineralVeins for chunk {chunkData.chunkCoord}");
        System.Random rng = new System.Random(chunkData.chunkCoord.x * 10000 + chunkData.chunkCoord.y);

        if (terrainProfile == null || terrainProfile.layers == null || terrainProfile.layers.Count == 0)
        {
            Debug.LogWarning($"[WorldGenerator] terrainProfile or its layers are not configured for chunk {chunkData.chunkCoord}");
            yield break;
        }

        foreach (var layer in terrainProfile.layers)
        {
            Debug.Log($"[WorldGenerator] Processing layer (startDepth: {layer.startDepth})");
            int chunkEndY = worldStartY + chunkSize;
            if (!LayerIntersectsChunk(layer, worldStartY, chunkEndY))
            {
                Debug.Log($"[WorldGenerator] Layer (startDepth: {layer.startDepth}) does not intersect chunk {chunkData.chunkCoord}. Skipping.");
                continue;
            }

            if (layer.mineralConfigs == null || layer.mineralConfigs.Count == 0)
            {
                Debug.Log($"[WorldGenerator] Layer (startDepth: {layer.startDepth}) has no mineralConfigs. Skipping.");
                continue;
            }

            foreach (var mineral in layer.mineralConfigs)
            {
                float spawnChance = mineral.spawnChanceByDepth.Evaluate(Mathf.Abs(worldStartY));
                Debug.Log($"[WorldGenerator] Mineral {mineral.minableType} in layer (startDepth: {layer.startDepth}). SpawnChance: {spawnChance} (worldStartY: {worldStartY})");
                if (rng.NextDouble() >= spawnChance)
                {
                    Debug.Log($"[WorldGenerator] Mineral {mineral.minableType} failed spawnChance check.");
                    continue;
                }

                int veinCount = rng.Next(mineral.veinsPerChunk.x, mineral.veinsPerChunk.y + 1);
                Debug.Log($"[WorldGenerator] Mineral {mineral.minableType} passed spawnChance. VeinCount: {veinCount}");
                if (veinCount <= 0)
                {
                    Debug.Log($"[WorldGenerator] Mineral {mineral.minableType} has veinCount <= 0. Skipping.");
                    continue;
                }

                for (int i = 0; i < veinCount; i++)
                {
                    int startX = rng.Next(0, chunkSize);
                    int startY = rng.Next(0, chunkSize);
                    int worldY = worldStartY + startY;

                    if (worldY <= layer.startDepth && worldY > GetNextLayerDepth(layer))
                    {
                        Debug.Log($"[WorldGenerator] Calling GenerateVein for {mineral.minableType} at worldY {worldY} (layer.startDepth: {layer.startDepth}, nextLayerDepth: {GetNextLayerDepth(layer)}).");
                        GenerateVein(chunkData, rng, mineral, startX, startY);
                    }
                    else
                    {
                        Debug.Log($"[WorldGenerator] Skipping GenerateVein for {mineral.minableType} due to depth condition (worldY: {worldY}, layer.startDepth: {layer.startDepth}, nextLayerDepth: {GetNextLayerDepth(layer)}).");
                    }
                }
            }
            yield return null;
        }
    }

    private bool LayerIntersectsChunk(TerrainLayer layer, int chunkStart, int chunkEnd)
    {
        return !(layer.startDepth < chunkStart && GetNextLayerDepth(layer) > chunkEnd);
    }

    private void GenerateVein(WorldManager.ChunkData chunkData, System.Random rng,
                              MinableSpawnConfig config, int startX, int startY)
    {
        int length = rng.Next(config.veinLength.x, config.veinLength.y + 1);
        int x = startX, y = startY;

        for (int i = 0; i < length; i++)
        {
            if (IsInsideChunk(x, y) && IsBaseTile(chunkData.tileStates[x, y]))
            {
                chunkData.tileStates[x, y] = (TileType)config.minableType;
                Debug.Log($"[WorldGenerator] Placed mineral {config.minableType} at local ({x}, {y}) in chunk {chunkData.chunkCoord}"); // ADD THIS LOG
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

    private TerrainLayer GetLayerForDepth(int depth)
    {
        TerrainLayer current = null;
        foreach (var layer in terrainProfile.layers)
        {
            if (depth <= layer.startDepth) current = layer;
            else return current;
        }
        return current;
    }

    private int GetNextLayerDepth(TerrainLayer currentLayer)
    {
        int index = terrainProfile.layers.IndexOf(currentLayer);
        return (index >= 0 && index < terrainProfile.layers.Count - 1)
            ? terrainProfile.layers[index + 1].startDepth
            : int.MinValue;
    }

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

    private void SpawnResourceObject(TileType resourceType, Vector3 pos, int worldY, WorldManager.ChunkData chunkData)
    {
        Debug.Log($"[WorldGenerator] Attempting to spawn resource: {resourceType} at {pos}");

        TerrainLayer layer = GetLayerForDepth(worldY);
        if (layer == null)
        {
            Debug.LogWarning($"[WorldGenerator] Could not find a terrain layer for depth {worldY}. Cannot spawn resource.");
            return;
        }

        if (System.Enum.TryParse(resourceType.ToString(), out PoolableType poolType))
        {
            Debug.Log($"[WorldGenerator] Successfully parsed TileType {resourceType} to PoolableType {poolType}. Requesting from ObjectPooler for layer {layer.layerType}.");
            GameObject obj = ObjectPooler.Instance.SpawnFromPool(layer.layerType, poolType, pos, Quaternion.identity);
            if (obj != null)
            {
                chunkData.spawnedItems.Add(obj);
                Debug.Log($"[WorldGenerator] Successfully spawned {obj.name} from pool for {poolType} in layer {layer.layerType}.");
            }
            else
            {
                Debug.LogWarning($"[WorldGenerator] Failed to spawn {poolType} from ObjectPooler for layer {layer.layerType}. Pool might be empty or type not found.");
            }
        }
        else
        {
            Debug.LogWarning($"[WorldGenerator] Failed to parse TileType {resourceType} to PoolableType. Check enum names.");
        }
    }
}
