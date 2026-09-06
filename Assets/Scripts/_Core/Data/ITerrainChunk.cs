// @tags: interface, chunk, terrain, tile, rendering
using UnityEngine;

/// <summary>
/// Common interface for both TerrainChunk (legacy) and TerrainChunk_Refactored.
/// This allows InfinityMapManager to work with either component type.
/// </summary>
public interface ITerrainChunk
{
    // Chunk dimensions
    int width { get; }
    int height { get; }
    float PPU { get; }  // Pixels Per Unit (or pixelsPerUnit in refactored version)
    
    // Initialization
    void FirstTimeInit(int w, int h);
    
    // Reuse system (for chunk pooling)
    void Reuse_Step1_Prepare(Transform playerTransform, Color32[] sourcePixels, Color32[] newBorderPixels,
                             Color32[] newSecondaryBorderPixels,
                             byte[] loadPixelInfo, bool hasChanges);
    void Reuse_Step2_Finalize();
    
    // Digging/Modification
    void Dig(Vector2 mouseWorldPos, float radius, int toolIndex);
    
    // State tracking
    bool hasBeenModified { get; }
    
    // Lighting updates
    void UpdateBoundaryLighting(bool left, bool right, bool top, bool bottom);
    
    // Queries
    bool IsPixelEmptyLocal(Vector2 localPos);
    
    // GameObject access
    GameObject gameObject { get; }
    Transform transform { get; }
}
