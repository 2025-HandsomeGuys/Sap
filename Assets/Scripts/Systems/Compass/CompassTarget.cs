// @tags: compass, special-chunk, locator, interface
using UnityEngine;

/// <summary>나침반 탐색 결과 — 가장 가까운 특수청크 앵커.</summary>
public struct CompassTarget
{
    public Vector2Int coord;      // 앵커 청크 좌표
    public Vector2 worldPos;      // 앵커 청크 중심 월드 좌표
    public SpecialChunkType type; // 특수청크 종류 (미확정 시 None)
}

/// <summary>특수청크 방향 탐색 코어. 뷰(화살표·미니맵)를 모른다.</summary>
public interface ISpecialChunkLocator
{
    /// <summary>origin 청크에서 가장 가까운 특수청크 앵커를 찾는다. 없으면 false.</summary>
    bool TryFindNearest(Vector2Int origin, out CompassTarget target);
}
