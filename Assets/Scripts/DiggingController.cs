
using UnityEngine;
using UnityEngine.Tilemaps;

public class DiggingController : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;
    public Tilemap groundTilemap;

    void Update()
    {
        // Check for left mouse button click
        if (Input.GetMouseButtonDown(0))
        {
            Dig();
        }
    }

    void Dig()
    {
        if (mainCamera == null || groundTilemap == null)
        {
            Debug.LogError("References not set in DiggingController!");
            return;
        }

        // Get mouse position in world space
        Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);

        // Convert world position to tilemap cell position
        Vector3Int cellPosition = groundTilemap.WorldToCell(mouseWorldPos);

        // Check if there is a tile at that position
        TileBase tile = groundTilemap.GetTile(cellPosition);

        if (tile != null)
        {
            // Remove the tile
            groundTilemap.SetTile(cellPosition, null);
        }
    }
}
