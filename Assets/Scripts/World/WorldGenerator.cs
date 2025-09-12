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

    [Header("Base Tile Assets")]
    [Tooltip("Assign the RuleTile for each base layer type here.")]
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
    /// Renders the visual tiles and objects based on the pre-calculated chunk data.
    /// </summary>
    public IEnumerator GenerateChunk(Vector2Int chunkCoord, WorldManager.ChunkData chunkData, Tilemap tilemap)
    {
        int startX = chunkCoord.x * chunkSize;
        int startY = chunkCoord.y * chunkSize;
        int tilesSpawnedThisFrame = 0;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                TileType tileState = chunkData.tileStates[x, y];
                if (tileState == TileType.Empty) continue;

                int worldGridX = startX + x;
                int worldGridY = startY + y;
                Vector3Int cellPosition = new Vector3Int(worldGridX, worldGridY, 0);

                // Check if it's a base tile or a resource tile
                if (IsBaseTile(tileState))
                {
                    TileBase tileToSet = GetBaseTileAsset(tileState);
                    if (tileToSet != null) tilemap.SetTile(cellPosition, tileToSet);
                }
                else // It's a resource/mineral
                {
                    // Find which layer this position belongs to, to get the correct base tile
                    TerrainLayer currentLayer = GetLayerForDepth(worldGridY);
                    if(currentLayer != null) {
                         TileBase backgroundTile = GetBaseTileAsset(currentLayer.baseTileType);
                         if (backgroundTile != null) tilemap.SetTile(cellPosition, backgroundTile);
                    }

                    // Spawn the resource object on top
                    SpawnResourceObject(tileState, new Vector3(worldGridX * cellSize, worldGridY * cellSize, 0), chunkData);
                }

                tilesSpawnedThisFrame++;
                if (tilesSpawnedThisFrame >= tilesPerFrame)
                {
                    tilesSpawnedThisFrame = 0;
                    yield return null;
                }
            }
        }
    }

    /// <summary>
    /// Fills the chunkData.tileStates array with base terrain and mineral data according to the terrain profile.
    /// </summary>
    public IEnumerator InitializeChunkDataCoroutine(WorldManager.ChunkData chunkData)
    {
        if (terrainProfile == null)
        {
            Debug.LogError("Terrain Generation Profile is not assigned in the WorldGenerator!");
            yield break;
        }

        // PASS 1: Set Base Terrain
        int chunkStartY = chunkData.chunkCoord.y * chunkSize;
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                int worldY = chunkStartY + y;
                TerrainLayer layer = GetLayerForDepth(worldY);
                if (layer != null)
                {
                    chunkData.tileStates[x, y] = layer.baseTileType;
                }
                else
                {
                    // If below the deepest layer, fill with bedrock
                    chunkData.tileStates[x, y] = TileType.Bedrock;
                }
            }
        }
        yield return null; // Yield after base terrain pass

        // PASS 2: Generate Mineral Veins
        System.Random random = new System.Random(chunkData.chunkCoord.x * 10000 + chunkData.chunkCoord.y);
        foreach (var layer in terrainProfile.layers)
        {
            // Check if this layer is relevant to the current chunk's vertical space
            int chunkEndY = chunkStartY + chunkSize;
            if (layer.startDepth < chunkStartY && (GetNextLayerDepth(layer) > chunkEndY)) continue;

            foreach (var mineralConfig in layer.mineralConfigs)
            {
                // Simplified spawn logic for now, can be expanded
                if (random.NextDouble() < 0.1f) // Example: 10% chance to spawn a vein type
                {
                    int veinCount = random.Next(mineralConfig.veinsPerChunk.x, mineralConfig.veinsPerChunk.y + 1);
                    for (int i = 0; i < veinCount; i++)
                    {
                        // Find a valid starting point within the chunk and this layer's depth
                        int startX = random.Next(0, chunkSize);
                        int startY = random.Next(0, chunkSize);
                        int worldY = chunkStartY + startY;

                        if (worldY <= layer.startDepth && worldY > GetNextLayerDepth(layer))
                        {
                            GenerateVein(chunkData, random, mineralConfig, startX, startY);
                        }
                    }
                }
            }
            yield return null; // Yield after processing each layer's minerals
        }
    }

    private void GenerateVein(WorldManager.ChunkData chunkData, System.Random random, MinableSpawnConfig config, int startX, int startY)
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

            int direction = random.Next(0, 4);
            if (direction == 0) currentY++;
            else if (direction == 1) currentY--;
            else if (direction == 2) currentX--;
            else if (direction == 3) currentX++;
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
        return tileType >= TileType.Dirt && tileType <= TileType.MeteoriteRock;
    }

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