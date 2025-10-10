using UnityEngine;
using System.Collections.Generic;

public class BackgroundManager : MonoBehaviour
{
    public Transform playerTransform;
    public GameObject backgroundTilePrefab;
    public int renderDistance = 2; // How many tiles in each direction from the player
    public float ceilingLevel = 0f; // Background tiles will not appear above this Y-coordinate

    private Vector2 tileSize;
    private Dictionary<Vector2Int, GameObject> activeTiles = new Dictionary<Vector2Int, GameObject>();
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
        Vector2Int newPlayerTilePosition = GetPlayerTilePosition();
        if (newPlayerTilePosition != currentPlayerTilePosition)
        {
            currentPlayerTilePosition = newPlayerTilePosition;
            GenerateBackgroundTiles();
        }
    }

    Vector2Int GetPlayerTilePosition()
    {
        return new Vector2Int(
            Mathf.FloorToInt(playerTransform.position.x / tileSize.x),
            Mathf.FloorToInt(playerTransform.position.y / tileSize.y)
        );
    }

    void GenerateBackgroundTiles()
    {
        HashSet<Vector2Int> tilesToKeep = new HashSet<Vector2Int>();

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
                    GameObject newTile = Instantiate(backgroundTilePrefab, spawnPosition, Quaternion.identity, transform);
                    newTile.name = $"BackgroundTile_{x}_{y}";
                    
                    BackgroundScroller scroller = newTile.GetComponent<BackgroundScroller>();
                    if (scroller != null)
                    {
                        scroller.SetTileProperties(spawnPosition, tileSize);
                    }
                    else
                    {
                        Debug.LogWarning($"BackgroundScroller component not found on {newTile.name}.");
                    }

                    activeTiles.Add(tilePos, newTile);
                }
            }
        }

        // Deactivate/destroy tiles that are out of range
        List<Vector2Int> tilesToRemove = new List<Vector2Int>();
        foreach (var entry in activeTiles)
        {
            if (!tilesToKeep.Contains(entry.Key))
            {
                tilesToRemove.Add(entry.Key);
            }
        }

        foreach (var tilePos in tilesToRemove)
        {
            Destroy(activeTiles[tilePos]); // For now, destroy. Can be pooled later.
            activeTiles.Remove(tilePos);
        }
    }
}
