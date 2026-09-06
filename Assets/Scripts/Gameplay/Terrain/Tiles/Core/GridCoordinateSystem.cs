// @tags: coordinate-system, grid, chunk-coord, array-index, world-to-array
using UnityEngine;

public class GridCoordinateSystem
{
    private readonly int _terrainWidth;
    private readonly int _xArraySize;
    private readonly int _yArraySize;

    public int XArraySize => _xArraySize;
    public int YArraySize => _yArraySize;

    public GridCoordinateSystem(int terrainWidth, int terrainDepth)
    {
        _terrainWidth = terrainWidth;
        _xArraySize = terrainWidth * 2 + 1;
        _yArraySize = terrainDepth + 1;
    }

    /// <summary>
    /// Converts world chunk X to array index
    /// Example: -50 → 0, 0 → 50, +50 → 100
    /// </summary>
    public int ToArrayX(int chunkX)
    {
        return chunkX + _terrainWidth;
    }

    /// <summary>
    /// Converts world chunk Y to array index
    /// Example: 0 → 0, -1 → 1, -36 → 36
    /// </summary>
    public int ToArrayY(int chunkY)
    {
        return -chunkY; // Flip sign: 0~-36 becomes 0~36
    }

    /// <summary>
    /// Converts array indices back to world coordinates
    /// </summary>
    public Vector2Int ToWorldCoord(int arrayX, int arrayY)
    {
        int worldX = arrayX - _terrainWidth;
        int worldY = -arrayY;
        return new Vector2Int(worldX, worldY);
    }

    /// <summary>
    /// Validates array indices are within bounds
    /// </summary>
    public bool IsValidArrayIndex(int arrayX, int arrayY)
    {
        return arrayX >= 0 && arrayX < _xArraySize && 
               arrayY >= 0 && arrayY < _yArraySize;
    }
}
