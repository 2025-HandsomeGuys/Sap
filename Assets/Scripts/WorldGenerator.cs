
using UnityEngine;
using UnityEngine.Tilemaps;

public class WorldGenerator : MonoBehaviour
{
    [Header("World Settings")]
    public int worldWidth = 100;
    public int worldHeight = 100;
    public int surfaceLevel = 80;

    [Header("Generation Settings")]
    [Range(0f, 1f)]
    public float stoneProbability = 0.5f; // Chance for a tile to be stone instead of dirt in the stone layer

    [Header("Tilemap References")]
    public Tilemap groundTilemap;

    [Header("Tile Assets")]
    public TileBase dirtTile;
    public TileBase stoneTile;

    void Start()
    {
        GenerateWorld();
    }

    void GenerateWorld()
    {
        if (groundTilemap == null || dirtTile == null || stoneTile == null)
        {
            Debug.LogError("Required references are not set in the WorldGenerator!");
            return;
        }

        for (int x = -200; x < worldWidth; x++)
        {
            for (int y = -200; y < surfaceLevel; y++)
            {
                Vector3Int tilePosition = new Vector3Int(x, y, 0);
                TileBase tileToPlace = dirtTile; // Default to dirt

                // If deep enough (more than 2 tiles below surface), randomly place stone
                if (y < surfaceLevel - 2 && Random.Range(0f, 1f) < stoneProbability)
                {
                    tileToPlace = stoneTile;
                }

                groundTilemap.SetTile(tilePosition, tileToPlace);
            }
        }
    }
}
