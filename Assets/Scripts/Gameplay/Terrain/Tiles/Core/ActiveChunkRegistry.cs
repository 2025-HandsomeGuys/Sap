// @tags: registry, active-chunk, chunk-map, grid, coordinate, lifecycle
using System.Collections.Generic;
using UnityEngine;

public class ActiveChunkRegistry
{
    private readonly IChunk[,] _activeChunks; // 2D Array
    private readonly List<Vector2Int> _activeCoordinates; // List for ordered iteration
    private readonly HashSet<Vector2Int> _activeCoordinateSet; // #9 [Fix] O(1) Contains/Remove
    private readonly GridCoordinateSystem _grid;

    public ActiveChunkRegistry(GridCoordinateSystem grid)
    {
        _grid = grid;
        _activeChunks = new IChunk[_grid.XArraySize, _grid.YArraySize];
        _activeCoordinates = new List<Vector2Int>();
        _activeCoordinateSet = new HashSet<Vector2Int>();
    }

    public void Add(Vector2Int coord, IChunk chunk)
    {
        int x = _grid.ToArrayX(coord.x);
        int y = _grid.ToArrayY(coord.y);

        if (_grid.IsValidArrayIndex(x, y))
        {
            _activeChunks[x, y] = chunk;
            if (_activeCoordinateSet.Add(coord))
            {
                _activeCoordinates.Add(coord);
            }
        }
        else
        {
            Debug.LogError($"[ActiveChunkRegistry] Out of bounds: {coord}");
        }
    }

    public void Remove(Vector2Int coord)
    {
        int x = _grid.ToArrayX(coord.x);
        int y = _grid.ToArrayY(coord.y);

        if (_grid.IsValidArrayIndex(x, y))
        {
            _activeChunks[x, y] = null;
            if (_activeCoordinateSet.Remove(coord))
            {
                _activeCoordinates.Remove(coord);
            }
        }
        else
        {
            Debug.LogError($"[ActiveChunkRegistry] Remove out of bounds: {coord}");
        }
    }

    public IChunk Get(Vector2Int coord)
    {
        int x = _grid.ToArrayX(coord.x);
        int y = _grid.ToArrayY(coord.y);

        if (_grid.IsValidArrayIndex(x, y))
        {
            var chunk = _activeChunks[x, y];
            if (chunk == null) return null;

            // IChunk는 인터페이스라 Unity fake-null(Destroy 후에도 C# 레퍼런스가 살아있는 상태)을
            // == null 연산자로 감지 못함. UnityEngine.Object로 캐스팅 후 체크.
            var unityObj = chunk as UnityEngine.Object;
            if (unityObj != null && unityObj == null)
            {
                // 파괴된 오브젝트 lazy 정리
                _activeChunks[x, y] = null;
                _activeCoordinateSet.Remove(coord);
                _activeCoordinates.Remove(coord);
                return null;
            }

            return chunk;
        }
        return null;
    }

    public bool HasChunk(Vector2Int coord)
    {
        return Get(coord) != null;
    }

    /// <summary>
    /// Returns a direct reference to the active coordinates list.
    /// Warning: Do not modify this list directly while iterating.
    /// </summary>
    public IEnumerable<Vector2Int> GetActiveCoordinates()
    {
        return _activeCoordinates;
    }

    /// <summary>
    /// Returns list of coordinates safely copied (if needed for modification during iteration)
    /// </summary>
    public List<Vector2Int> GetActiveCoordinatesCopy()
    {
        return new List<Vector2Int>(_activeCoordinates);
    }

    /// <summary>
    /// [P1-3] 재사용 버퍼에 좌표를 복사한다. GC 할당 없이 언로드 순회에 사용.
    /// </summary>
    public void CopyActiveCoordinatesTo(List<Vector2Int> buffer)
    {
        buffer.Clear();
        buffer.AddRange(_activeCoordinates);
    }
    
    /// <summary>
    /// Returns all active chunks.
    /// </summary>
    public IEnumerable<IChunk> GetAll()
    {
        foreach (var coord in _activeCoordinates)
        {
            var chunk = Get(coord);
            if (chunk != null) yield return chunk;
        }
    }

    /// <summary>
    /// [P-GC] 재사용 버퍼에 활성 청크를 복사. GetAll() 이터레이터 state machine 할당 없음.
    /// </summary>
    public void CopyAllChunksTo(List<IChunk> buffer)
    {
        buffer.Clear();
        for (int i = 0; i < _activeCoordinates.Count; i++)
        {
            var chunk = Get(_activeCoordinates[i]);
            if (chunk != null) buffer.Add(chunk);
        }
    }
}
