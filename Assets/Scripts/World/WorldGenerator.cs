using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

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

    // Service instances
    private TerrainGenerator _terrainGenerator;
    private MineralGenerator _mineralGenerator;
    private TileAssetService _tileAssetService;
    private int _bedrockStartDepth;

    private void Start()
    {
        InitializeManagers();
    }

    private void InitializeManagers()
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

        _bedrockStartDepth = terrainProfile.layers[6].startDepth;

        // Initialize services
        _terrainGenerator = new TerrainGenerator(terrainProfile, _bedrockStartDepth);
        _mineralGenerator = new MineralGenerator(terrainProfile);
        _tileAssetService = new TileAssetService(
            dirtTile,
            hardStoneTile,
            coolStoneTile,
            iceTile,
            hotStoneTile,
            magmaRockTile,
            meteoriteRockTile,
            bedrockTile,
            cellSize,
            mineralSizeMultiplier
        );
    }

    // ------------------ CHUNK TILE GENERATION ------------------

    /// <summary>
    /// Creates a 1D TileBase array for a chunk's tilemap representation.
    /// For resource tiles, it places the layer's base tile.
    /// </summary>
    public TileBase[] CreateTilebaseArray(Vector2Int chunkCoord, WorldManager.ChunkData chunkData)
    {
        if (_tileAssetService == null)
        {
            Debug.LogError("TileAssetService is not initialized! Call InitializeManagers() first.");
            return null;
        }
        return _tileAssetService.CreateTilebaseArray(chunkCoord, chunkData);
    }

    public void PreSpawnMineralsForChunk(WorldManager.ChunkData chunkData)
    {
        if (_tileAssetService == null)
        {
            Debug.LogError("TileAssetService is not initialized! Call InitializeManagers() first.");
            return;
        }
        _tileAssetService.PreSpawnMineralsForChunk(chunkData);
    }

    // ------------------ CHUNK DATA INITIALIZATION ------------------

    /// <summary>
    /// Fills chunkData with terrain + veins + minerals according to the profile.
    /// </summary>
    public IEnumerator InitializeChunkDataCoroutine(WorldManager.ChunkData chunkData)
    {
        if (_terrainGenerator == null || _mineralGenerator == null)
        {
            Debug.LogError("Generators are not initialized! Call InitializeManagers() first.");
            yield break;
        }

        if (terrainProfile == null)
        {
            Debug.LogError("Terrain Generation Profile is not assigned!");
            yield break;
        }

        int chunkWorldStartY = chunkData.chunkCoord.y * chunkSize;

        yield return _terrainGenerator.FillBaseTerrain(chunkData, chunkWorldStartY);
        yield return _terrainGenerator.GenerateDiagonalVeins(chunkData, chunkWorldStartY);
        yield return _mineralGenerator.GenerateMineralVeins(chunkData, chunkWorldStartY);
    }
}
