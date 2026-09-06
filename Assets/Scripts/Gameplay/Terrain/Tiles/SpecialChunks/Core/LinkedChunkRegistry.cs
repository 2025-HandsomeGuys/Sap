// @tags: special-chunk, chunk, registry, linked-piece
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 링크 피스 좌표→(프리팹, 앵커) 매핑 캐시.
/// 앵커 스폰 시 채워지며, 비어있어도 SpecialChunkSelector 역산으로 시스템이 동작한다.
/// SubChunkRegistry와 동일한 패턴, 역인덱스(_reverseMap)로 UnregisterByAnchor O(k) 보장.
/// </summary>
public class LinkedChunkRegistry
{
    private readonly Dictionary<Vector2Int, (MonoBehaviour prefab, Vector2Int anchor)> _links
        = new Dictionary<Vector2Int, (MonoBehaviour prefab, Vector2Int anchor)>();

    private readonly Dictionary<Vector2Int, List<Vector2Int>> _reverseMap
        = new Dictionary<Vector2Int, List<Vector2Int>>();

    public void Register(Vector2Int coord, MonoBehaviour prefab, Vector2Int anchor)
    {
        _links[coord] = (prefab, anchor);
        if (!_reverseMap.TryGetValue(anchor, out var list))
        {
            list = new List<Vector2Int>();
            _reverseMap[anchor] = list;
        }
        if (!list.Contains(coord))
            list.Add(coord);
    }

    public bool TryGet(Vector2Int coord, out MonoBehaviour prefab, out Vector2Int anchor)
    {
        if (_links.TryGetValue(coord, out var entry))
        {
            prefab = entry.prefab;
            anchor = entry.anchor;
            return true;
        }
        prefab = null;
        anchor = default;
        return false;
    }

    public bool Contains(Vector2Int coord) => _links.ContainsKey(coord);

    public void UnregisterByAnchor(Vector2Int anchor)
    {
        if (!_reverseMap.TryGetValue(anchor, out var coords)) return;
        foreach (var coord in coords)
            _links.Remove(coord);
        _reverseMap.Remove(anchor);
    }
}
