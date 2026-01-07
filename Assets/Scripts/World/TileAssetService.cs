using UnityEngine;
using UnityEngine.Tilemaps;

public class TileAssetService
{
    private readonly RuleTile _dirtTile;
    private readonly RuleTile _hardStoneTile;
    private readonly RuleTile _coolStoneTile;
    private readonly RuleTile _iceTile;
    private readonly RuleTile _hotStoneTile;
    private readonly RuleTile _magmaRockTile;
    private readonly RuleTile _meteoriteRockTile;
    private readonly RuleTile _bedrockTile;
    private readonly float _cellSize;
    private readonly float _mineralSizeMultiplier;
    private const int chunkSize = WorldGenerator.chunkSize;

    public TileAssetService(
        RuleTile dirtTile,
        RuleTile hardStoneTile,
        RuleTile coolStoneTile,
        RuleTile iceTile,
        RuleTile hotStoneTile,
        RuleTile magmaRockTile,
        RuleTile meteoriteRockTile,
        RuleTile bedrockTile,
        float cellSize,
        float mineralSizeMultiplier)
    {
        _dirtTile = dirtTile;
        _hardStoneTile = hardStoneTile;
        _coolStoneTile = coolStoneTile;
        _iceTile = iceTile;
        _hotStoneTile = hotStoneTile;
        _magmaRockTile = magmaRockTile;
        _meteoriteRockTile = meteoriteRockTile;
        _bedrockTile = bedrockTile;
        _cellSize = cellSize;
        _mineralSizeMultiplier = mineralSizeMultiplier;
    }

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
                    Vector3 position = new Vector3((startX + x) * _cellSize, worldY * _cellSize, 0);

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

    private TileBase GetBaseTileAsset(TileType type)
    {
        return type switch
        {
            TileType.Dirt => _dirtTile,
            TileType.HardStone => _hardStoneTile,
            TileType.CoolStone => _coolStoneTile,
            TileType.Ice => _iceTile,
            TileType.HotStone => _hotStoneTile,
            TileType.MagmaRock => _magmaRockTile,
            TileType.MeteoriteRock => _meteoriteRockTile,
            TileType.Bedrock => _bedrockTile,
            _ => null
        };
    }
}


