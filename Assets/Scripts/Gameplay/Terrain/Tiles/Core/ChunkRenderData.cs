// @tags: chunk, render-data, parameter-object, border, pixel-data, special-chunk
using UnityEngine;

/// <summary>
/// Parameter Object Pattern: 청크 렌더링에 필요한 모든 데이터를 캡슐화합니다.
/// SpawnChunk_Phase1에서 흩어져 있던 변수들을 하나의 구조체로 묶어 관리합니다.
/// </summary>
public class ChunkRenderData
{
    // === Core Data ===
    public Vector2Int ChunkCoord { get; set; }
    public TileType TargetType { get; set; }
    public bool IsModified { get; set; }
    public bool IsBoundary { get; set; }
    
    // === Pixel Data ===
    public Color32[] PixelsToUse { get; set; }
    public byte[] PixelInfo { get; set; }
    
    // === Border Data ===
    public Color32[] BorderPixels { get; set; }
    public int BorderWidth { get; set; }
    public int BorderHeight { get; set; }
    
    // === Secondary Border ===
    public Color32[] SecondaryBorderPixels { get; set; }
    
    // === Visual Settings ===
    public TileVisualSettings.TileVisualData VisualData { get; set; }
    public bool VisualFound { get; set; }
    
    // === Special Chunk Info ===
    public TerrainChunk SpecialChunk { get; set; }
    
    public ChunkRenderData(Vector2Int coord)
    {
        ChunkCoord = coord;
    }
    
    /// <summary>
    /// 스페셜 청크인지 확인
    /// </summary>
    public bool IsSpecialChunk()
    {
        return SpecialChunk != null;
    }
    
    /// <summary>
    /// 유효한 렌더링 데이터를 가지고 있는지 검증
    /// </summary>
    public bool IsValid()
    {
        return PixelsToUse != null && PixelsToUse.Length > 0;
    }
}
