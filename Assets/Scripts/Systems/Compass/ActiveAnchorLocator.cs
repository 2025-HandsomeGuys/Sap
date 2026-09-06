// @tags: compass, special-chunk, locator, active-anchor
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>실제 배치된 특수청크 앵커 목록을 공급한다 (좌표 → 종류).</summary>
public delegate IEnumerable<KeyValuePair<Vector2Int, SpecialChunkType>> ActiveAnchorSource();

/// <summary>
/// 실제로 스폰된 특수청크 앵커 중 origin에서 가장 가까운 것을 찾는다.
/// 예측이 아닌 실 인스턴스(SpecialChunkManager.ActiveAnchors)만 대상으로 하므로
/// 월드 밖 좌표(예: y=10000 던전 스테이징)를 헛짚지 않는다.
/// 매니저 대신 ActiveAnchorSource 델리게이트를 주입받아 테스트·재사용이 쉽다.
/// </summary>
public class ActiveAnchorLocator : ISpecialChunkLocator
{
    private readonly int _maxRadius; // 체비쇼프 반경(청크). 0 이하면 무제한.
    private readonly ActiveAnchorSource _source;
    private readonly Predicate<SpecialChunkType> _filter;

    // 앵커 좌표 → 조준 월드 위치. null 반환 시 청크 중심으로 폴백. null이면 항상 중심.
    private readonly Func<Vector2Int, Vector2?> _worldPosResolver;

    public ActiveAnchorLocator(int maxRadius, ActiveAnchorSource source,
        Predicate<SpecialChunkType> filter = null,
        Func<Vector2Int, Vector2?> worldPosResolver = null)
    {
        _maxRadius = maxRadius;
        _source = source;
        _filter = filter;
        _worldPosResolver = worldPosResolver;
    }

    public bool TryFindNearest(Vector2Int origin, out CompassTarget target)
    {
        target = default;
        if (_source == null) return false;

        var anchors = _source();
        if (anchors == null) return false;

        bool haveBest = false;
        float bestSqr = float.MaxValue;

        foreach (var kv in anchors)
        {
            Vector2Int coord = kv.Key;
            if (_filter != null && !_filter(kv.Value)) continue;

            int dx = coord.x - origin.x;
            int dy = coord.y - origin.y;

            // 반경 제한 (체비쇼프). _maxRadius <= 0 이면 무제한.
            if (_maxRadius > 0 && Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) > _maxRadius)
                continue;

            float sqr = dx * dx + dy * dy;
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            target = new CompassTarget
            {
                coord = coord,
                // 프리팹에 지정된 조준점(CompassPoi)이 있으면 그 지점, 없으면 청크 중심.
                worldPos = _worldPosResolver?.Invoke(coord) ?? ChunkCenterWorld(coord),
                type = kv.Value,
            };
            haveBest = true;
        }

        return haveBest;
    }

    private static Vector2 ChunkCenterWorld(Vector2Int coord)
    {
        Vector3 w = ChunkCoords.ToWorld(coord);
        float half = ChunkCoords.WorldSize * 0.5f;
        return new Vector2(w.x + half, w.y + half);
    }
}
