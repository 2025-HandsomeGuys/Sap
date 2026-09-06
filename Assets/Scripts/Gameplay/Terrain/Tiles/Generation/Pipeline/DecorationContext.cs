// @tags: decoration, pipeline, chunk, data-container, generation
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 청크 장식(Decoration) 단계에서 공유되는 데이터 컨텍스트
/// </summary>
public class DecorationContext
{
    public Vector2Int Coord { get; private set; }
    public int WorldSeed { get; private set; }
    public TileType TargetTileType { get; private set; }
    public bool IsModified { get; private set; }
    
    // 장식들이 서로의 영역을 침범하지 않게 공유하는 점유된 영역 리스트
    public List<Rect> PreOccupiedAreas { get; private set; }

    public DecorationContext(Vector2Int coord, int worldSeed, TileType tileType, bool isModified)
    {
        Coord = coord;
        WorldSeed = worldSeed;
        TargetTileType = tileType;
        IsModified = isModified;
        PreOccupiedAreas = new List<Rect>();
    }
}
