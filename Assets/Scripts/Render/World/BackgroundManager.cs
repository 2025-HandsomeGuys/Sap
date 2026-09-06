using UnityEngine;
using System.Collections.Generic;

public class BackgroundManager : MonoBehaviour
{
    public Transform playerTransform;
    public GameObject backgroundTilePrefab;
    public int renderDistance = 2; // How many tiles in each direction from the player
    public float ceilingLevel = 0f; // Background tiles will not appear above this Y-coordinate
    public List<Sprite> backgroundSprites = new List<Sprite>(); // Optional: Multiple sprites for variation

    private Vector2 tileSize;
    private Dictionary<Vector2Int, GameObject> activeTiles = new Dictionary<Vector2Int, GameObject>();

    /// <summary>현재 활성화된 배경 타일. XRayController가 단색 머티리얼 스왑 대상으로 순회한다.</summary>
    public IEnumerable<GameObject> ActiveTiles => activeTiles.Values;
    private Queue<GameObject> tilePool = new Queue<GameObject>(); // Object Pool
    private HashSet<Vector2Int> tilesToKeep = new HashSet<Vector2Int>(); // Reused collection
    private List<Vector2Int> tilesToRemove = new List<Vector2Int>(); // Reused collection
    private Vector2Int currentPlayerTilePosition;

    void Start()
    {
        if (playerTransform == null)
        {
            Debug.LogError("Player Transform is not assigned in BackgroundManager!");
            this.enabled = false;
            return;
        }

        if (backgroundTilePrefab == null)
        {
            Debug.LogError("Background Tile Prefab is not assigned in BackgroundManager!");
            this.enabled = false;
            return;
        }

        // Get tile size from the prefab's SpriteRenderer
        SpriteRenderer prefabSpriteRenderer = backgroundTilePrefab.GetComponent<SpriteRenderer>();
        if (prefabSpriteRenderer != null && prefabSpriteRenderer.sprite != null)
        {
            tileSize = prefabSpriteRenderer.sprite.bounds.size;
        }
        else
        {
            Debug.LogError("Background Tile Prefab does not have a SpriteRenderer or a sprite assigned!");
            this.enabled = false;
            return;
        }

        currentPlayerTilePosition = GetPlayerTilePosition();
        GenerateBackgroundTiles();
    }

    void Update()
    {
        if (playerTransform == null) return;

        Vector2Int newPlayerTilePosition = GetPlayerTilePosition();
        if (newPlayerTilePosition != currentPlayerTilePosition)
        {
            currentPlayerTilePosition = newPlayerTilePosition;
            GenerateBackgroundTiles();
        }
    }

    Vector2Int GetPlayerTilePosition()
    {
        if (playerTransform == null) return Vector2Int.zero;

        return new Vector2Int(
            Mathf.FloorToInt(playerTransform.position.x / tileSize.x),
            Mathf.FloorToInt(playerTransform.position.y / tileSize.y)
        );
    }

    void GenerateBackgroundTiles()
    {
        tilesToKeep.Clear();

        for (int x = currentPlayerTilePosition.x - renderDistance; x <= currentPlayerTilePosition.x + renderDistance; x++)
        {
            for (int y = currentPlayerTilePosition.y - renderDistance; y <= currentPlayerTilePosition.y + renderDistance; y++)
            {
                Vector2Int tilePos = new Vector2Int(x, y);
                
                // Only consider tiles below or at the ceilingLevel
                // Check if the top edge of the tile is above the ceilingLevel
                if ((y * tileSize.y + (tileSize.y / 2f)) > ceilingLevel)
                {
                    continue; // Skip this tile if its top edge is above the ceiling
                }

                tilesToKeep.Add(tilePos);

                if (!activeTiles.ContainsKey(tilePos))
                {
                    Vector3 spawnPosition = new Vector3(x * tileSize.x, y * tileSize.y, 0f);
                    GameObject newTile = GetTileFromPool(spawnPosition);
                    
                    newTile.name = $"BackgroundTile_{x}_{y}";
                    // Directly set properties instead of using BackgroundScroller
                    newTile.transform.position = spawnPosition;
                    SpriteRenderer sr = newTile.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                         // [New] Random Variation (Flip & Rotate) via Seeding
                         // Use a hash of the coordinates to get a consistent seed for this tile
                         int seed = (tilePos.x * 73856093) ^ (tilePos.y * 19349663);
                         System.Random prng = new System.Random(seed);
                         
                         // 0. Random Sprite Selection (if multiple are provided)
                         // IMPORTANT: Assign sprite before setting drawMode to Tiled to prevent Unity warnings.
                         if (backgroundSprites != null && backgroundSprites.Count > 0)
                         {
                             int spriteIndex = prng.Next(0, backgroundSprites.Count);
                             sr.sprite = backgroundSprites[spriteIndex];
                         }

                         sr.drawMode = SpriteDrawMode.Tiled;
                         sr.size = tileSize;
                         sr.enabled = true;

                         // 1. Random Flip
                         // User Request: No Horizontal or Vertical Flip. Just Random Sprites.
                         sr.flipX = false; 
                         sr.flipY = false;
                         
                         // 2. Rotation
                         // User Request: No Rotation.
                         newTile.transform.rotation = Quaternion.identity;
                    }

                    activeTiles.Add(tilePos, newTile);
                }
            }
        }

        // Deactivate tiles that are out of range
        tilesToRemove.Clear();
        foreach (var entry in activeTiles)
        {
            if (!tilesToKeep.Contains(entry.Key))
            {
                tilesToRemove.Add(entry.Key);
            }
        }

        foreach (var tilePos in tilesToRemove)
        {
            ReturnTileToPool(activeTiles[tilePos]);
            activeTiles.Remove(tilePos);
        }
    }

    GameObject GetTileFromPool(Vector3 position)
    {
        if (tilePool.Count > 0)
        {
            GameObject tile = tilePool.Dequeue();
            tile.SetActive(true);
            return tile;
        }
        else
        {
            GameObject tile = Instantiate(backgroundTilePrefab, position, Quaternion.identity, transform);
            return tile;
        }
    }

    void ReturnTileToPool(GameObject tile)
    {
        tile.SetActive(false);
        tilePool.Enqueue(tile);
    }
}
