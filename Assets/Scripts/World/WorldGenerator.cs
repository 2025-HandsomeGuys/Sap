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
    [Tooltip("The profile that defines the 7 layers of the world.")]
    public TerrainGenerationProfile terrainProfile;

    [Header("World Settings")]
    public float cellSize = 0.05f;
    public float mineralSizeMultiplier = 1.5f;
    [Tooltip("The world Y coordinate where bedrock begins.")]
    public int bedrockStartY = -1000;

    [Header("Base Tile Assets")]
    [Tooltip("Assign the RuleTile for each base layer type here. This could be replaced with a ScriptableObject mapping in the future.")]
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

    /// <summary>
    /// Generates the TileBase array for a chunk based on its pre-calculated data.
    /// This method does not place tiles, it only returns the data for them.
    /// It also spawns necessary resource objects.
    /// </summary>
    public TileBase[] GenerateChunkTiles(Vector2Int chunkCoord, WorldManager.ChunkData chunkData)
    {
        int chunkWorldStartX = chunkCoord.x * chunkSize;
        int chunkWorldStartY = chunkCoord.y * chunkSize;
        
        TileBase[] tiles = new TileBase[chunkSize * chunkSize];

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                TileType tileState = chunkData.tileStates[x, y];
                int index = y * chunkSize + x; // Calculate index for 1D array

                if (tileState == TileType.Empty) 
                {
                    tiles[index] = null;
                    continue;
                }

                int worldGridX = chunkWorldStartX + x;
                int worldGridY = chunkWorldStartY + y;

                if (IsBaseTile(tileState))
                {
                    tiles[index] = GetBaseTileAsset(tileState);
                }
                else // It's a resource/mineral
                {
                    // Find which layer this position belongs to, to get the correct base tile
                    TerrainLayer currentLayer = GetLayerForDepth(worldGridY);
                    if(currentLayer != null) {
                         tiles[index] = GetBaseTileAsset(currentLayer.baseTileType);
                    }
                    else
                    {
                        // Fallback if a mineral spawns in an undefined layer (e.g. air or bedrock zone)
                        // We'll just use dirt as the background.
                        tiles[index] = dirtTile;
                    }

                    // Spawn the resource object on top
                    SpawnResourceObject(tileState, new Vector3(worldGridX * cellSize, worldGridY * cellSize, 0), chunkData);
                }
            }
        }
        return tiles;
    }

    /// <summary>
    /// Fills the chunkData.tileStates array with base terrain and mineral data in sequential passes.
    /// </summary>
    public IEnumerator InitializeChunkDataCoroutine(WorldManager.ChunkData chunkData)
    {
        if (terrainProfile == null)
        {
            Debug.LogError("Terrain Generation Profile is not assigned in the WorldGenerator!");
            yield break;
        }

        // PASS 1: Set Base Terrain, Air, and Bedrock
        GenerateBaseTerrain(chunkData);
        yield return null;

        // PASS 2: Generate Diagonal Stone Veins
        GenerateDiagonalVeins(chunkData);
        yield return null;

        // PASS 3: Generate Mineral Veins
        GenerateMineralVeins(chunkData);
        yield return null;
    }

    /// <summary>
    /// PASS 1: Fills the chunk with Air, layered base tiles, and Bedrock.
    /// </summary>
    private void GenerateBaseTerrain(WorldManager.ChunkData chunkData)
    {
        int chunkWorldStartY = chunkData.chunkCoord.y * chunkSize;
        int worldTopY = (terrainProfile.layers != null && terrainProfile.layers.Count > 0) ? terrainProfile.layers[0].startDepth : 0;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                int worldY = chunkWorldStartY + y;

                if (worldY > worldTopY)
                {
                    // Condition for Air
                    chunkData.tileStates[x, y] = TileType.Empty;
                }
                else if (worldY <= bedrockStartY)
                {
                    // Condition for Bedrock
                    chunkData.tileStates[x, y] = TileType.Bedrock;
                }
                else
                {
                    // Condition for layered terrain
                    TerrainLayer layer = GetLayerForDepth(worldY);
                    if (layer != null)
                    {
                        chunkData.tileStates[x, y] = layer.baseTileType;
                    }
                    else
                    {
                        // Fallback for any space between the top layer and bedrock that isn't defined
                        // This case shouldn't be hit with correct configuration.
                        chunkData.tileStates[x, y] = TileType.Empty;
                    }
                }
            }
        }
    }

    /// <summary>
    /// PASS 2: Overlays diagonal veins of a secondary tile type (e.g., hard stone) using rotated Perlin noise.
    /// </summary>
    private void GenerateDiagonalVeins(WorldManager.ChunkData chunkData)
    {
        int chunkWorldStartY = chunkData.chunkCoord.y * chunkSize;
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                int worldY = chunkWorldStartY + y;
                TerrainLayer layer = GetLayerForDepth(worldY);

                if (layer != null && layer.hasDiagonalVeins)
                {
                    // Only replace the base tile of the current layer, not other newly placed veins
                    if (chunkData.tileStates[x, y] == layer.baseTileType)
                    {
                        float worldX = chunkData.chunkCoord.x * chunkSize + x;
                        
                        // --- Angle Variation Noise ---
                        // Use a different noise seed/offset for angle noise to decouple it from other noises
                        float angleNoise = Mathf.PerlinNoise((worldX + 1000) * layer.veinAngleNoiseScale, (worldY + 1000) * layer.veinAngleNoiseScale);
                        float currentAngle = Mathf.Lerp(layer.veinAngleRange.x, layer.veinAngleRange.y, angleNoise);

                        // --- Coordinate Rotation ---
                        float angleRad = currentAngle * Mathf.Deg2Rad;
                        float cosAngle = Mathf.Cos(angleRad);
                        float sinAngle = Mathf.Sin(angleRad);
                        float rotatedX = worldX * cosAngle - worldY * sinAngle;
                        float rotatedY = worldX * sinAngle + worldY * cosAngle;

                        // --- Vein Generation Noise ---
                        // Noise A: The main stripes, now rotated and stretched
                        float diagonalNoise = Mathf.PerlinNoise(rotatedX * layer.veinNoiseScale, rotatedY * layer.veinNoiseScale * 0.1f);

                        // Noise B: A second noise field to create thickness variations and breaks
                        float thicknessNoise = Mathf.PerlinNoise(worldX * layer.veinThicknessNoiseScale, worldY * layer.veinThicknessNoiseScale);

                        // Combine the two noises.
                        float combinedNoise = diagonalNoise - thicknessNoise;

                        if (combinedNoise > layer.veinThreshold)
                        {
                            chunkData.tileStates[x, y] = layer.diagonalVeinTile;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// PASS 3: Spawns clusters of minerals based on layer-specific configurations.
    /// </summary>
    private void GenerateMineralVeins(WorldManager.ChunkData chunkData)
    {
        System.Random random = new System.Random(chunkData.chunkCoord.x * 10000 + chunkData.chunkCoord.y);
        int chunkWorldStartY = chunkData.chunkCoord.y * chunkSize;

        foreach (var layer in terrainProfile.layers)
        {
            // Check if this layer is relevant to the current chunk's vertical space
            int chunkWorldEndY = chunkWorldStartY + chunkSize;
            if (layer.startDepth < chunkWorldStartY && (GetNextLayerDepth(layer) > chunkWorldEndY)) continue;

            foreach (var mineralConfig in layer.mineralConfigs)
            {
                // Use the AnimationCurve to determine spawn chance at this depth.
                float spawnChance = mineralConfig.spawnChanceByDepth.Evaluate(Mathf.Abs(chunkWorldStartY)); // Use absolute depth

                if (random.NextDouble() < spawnChance)
                {
                    int veinCount = random.Next(mineralConfig.veinsPerChunk.x, mineralConfig.veinsPerChunk.y + 1);
                    for (int i = 0; i < veinCount; i++)
                    {
                        // Find a valid starting point within the chunk and this layer's depth
                        int startX = random.Next(0, chunkSize);
                        int startY = random.Next(0, chunkSize);
                        int worldY = chunkWorldStartY + startY;

                        // Ensure the vein starts within the correct layer depth
                        if (worldY <= layer.startDepth && worldY > GetNextLayerDepth(layer))
                        {
                            GenerateSingleVein(chunkData, random, mineralConfig, startX, startY);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generates a single mineral vein using a random walk algorithm.
    /// </summary>
    private void GenerateSingleVein(WorldManager.ChunkData chunkData, System.Random random, MinableSpawnConfig config, int startX, int startY)
    {
        int length = random.Next(config.veinLength.x, config.veinLength.y + 1);
        int currentX = startX;
        int currentY = startY;

        for (int j = 0; j < length; j++)
        {
            if (currentX >= 0 && currentX < chunkSize && currentY >= 0 && currentY < chunkSize)
            {
                // Place mineral only if the tile is a base tile (don't overwrite other minerals)
                if (IsBaseTile(chunkData.tileStates[currentX, currentY]))
                {
                    chunkData.tileStates[currentX, currentY] = (TileType)config.minableType;
                }
            }

            // Move to the next position based on a random walk
            int direction = random.Next(0, 4); // 0: Up, 1: Down, 2: Left, 3: Right
            if (direction == 0) currentY++;
            else if (direction == 1) currentY--;
            else if (direction == 2) currentX--;
            else if (direction == 3) currentX++;

            // Add spacing
            for (int s = 0; s < config.veinSpacing - 1; s++)
            {
                if (direction == 0) currentY++;
                else if (direction == 1) currentY--;
                else if (direction == 2) currentX--;
                else if (direction == 3) currentX++;
            }
        }
    }

    private TerrainLayer GetLayerForDepth(int depth)
    {
        // Assumes layers are sorted top-to-bottom in the profile (e.g., 0, -100, -200)
        TerrainLayer currentLayer = null;
        foreach (var layer in terrainProfile.layers)
        {
            if (depth <= layer.startDepth)
            {
                currentLayer = layer;
            }
            else
            {
                // We've gone past the layer that contains this depth
                return currentLayer;
            }
        }
        return currentLayer;
    }

    private int GetNextLayerDepth(TerrainLayer currentLayer)
    {
        int currentIndex = terrainProfile.layers.IndexOf(currentLayer);
        if (currentIndex >= 0 && currentIndex < terrainProfile.layers.Count - 1)
        {
            return terrainProfile.layers[currentIndex + 1].startDepth;
        }
        return int.MinValue; // This is the last layer
    }

    private bool IsBaseTile(TileType tileType)
    {
        return tileType >= TileType.Dirt && tileType <= TileType.Bedrock;
    }

    /// <summary>
    /// Returns the RuleTile asset corresponding to a given base TileType.
    /// Note: This could be refactored to use a ScriptableObject or Dictionary for a more scalable mapping.
    /// </summary>
    private TileBase GetBaseTileAsset(TileType tileType)
    {
        switch (tileType)
        {
            case TileType.Dirt: return dirtTile;
            case TileType.HardStone: return hardStoneTile;
            case TileType.CoolStone: return coolStoneTile;
            case TileType.Ice: return iceTile;
            case TileType.HotStone: return hotStoneTile;
            case TileType.MagmaRock: return magmaRockTile;
            case TileType.MeteoriteRock: return meteoriteRockTile;
            case TileType.Bedrock: return bedrockTile;
            default: return null;
        }
    }

    private void SpawnResourceObject(TileType resourceType, Vector3 position, WorldManager.ChunkData chunkData)
    {
        // The PoolableType enum must match the resource part of the TileType enum
        if (System.Enum.TryParse(resourceType.ToString(), out PoolableType poolType))
        {
            GameObject spawnedObject = ObjectPooler.Instance.SpawnFromPool(poolType, position, Quaternion.identity);
            if (spawnedObject != null)
            {
                // Optional: Adjust scale or other properties
                chunkData.spawnedItems.Add(spawnedObject);
            }
        }
    }
}