// @tags: special-chunk, chunk, multi-chunk, registry, spawn
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 멀티청크 스페셜의 서브청크 위치를 등록·조회·역산하는 레지스트리.
/// SRP: 등록 상태 관리만 담당. 선택·스폰 로직과 무관.
///
/// 최적화: _reverseMap(앵커 → 서브좌표 목록) 역인덱스 추가.
/// UnregisterByAnchor가 O(n) 전체 탐색 → O(k) (k = 발자국 크기)로 개선됨.
/// </summary>
public class SubChunkRegistry
{
    // 인스턴스가 있는 서브청크 (레거시 방식 호환용)
    private readonly Dictionary<Vector2Int, TerrainChunk> _instances
        = new Dictionary<Vector2Int, TerrainChunk>();

    // 모든 예약 위치 (인스턴스 유무 불문) → 앵커 좌표 역산용
    private readonly Dictionary<Vector2Int, Vector2Int> _anchorMap
        = new Dictionary<Vector2Int, Vector2Int>();

    // 역인덱스: 앵커 좌표 → 해당 앵커에 속한 모든 서브 좌표 목록
    // UnregisterByAnchor를 O(n) → O(k)로 개선
    private readonly Dictionary<Vector2Int, List<Vector2Int>> _reverseMap
        = new Dictionary<Vector2Int, List<Vector2Int>>();

    // ─── 등록 ────────────────────────────────────────────────────

    /// <summary>TerrainChunk 인스턴스와 함께 등록 (레거시 서브청크 방식).</summary>
    public void Register(Vector2Int coord, TerrainChunk chunk, Vector2Int anchorCoord)
    {
        _instances[coord] = chunk;
        AddToMaps(coord, anchorCoord);
    }

    /// <summary>
    /// TerrainChunk 인스턴스 없이 좌표만 예약 등록 (대형 앵커 방식).
    /// BuildLoadQueue에서 해당 좌표를 완전히 skip하기 위해 사용.
    /// </summary>
    public void RegisterReserved(Vector2Int coord, Vector2Int anchorCoord)
        => AddToMaps(coord, anchorCoord);

    private void AddToMaps(Vector2Int coord, Vector2Int anchorCoord)
    {
        _anchorMap[coord] = anchorCoord;

        if (!_reverseMap.TryGetValue(anchorCoord, out var list))
        {
            list = new List<Vector2Int>();
            _reverseMap[anchorCoord] = list;
        }
        if (!list.Contains(coord))
            list.Add(coord);
    }

    // ─── 조회 ────────────────────────────────────────────────────

    /// <summary>등록된 서브청크 인스턴스를 반환. 파괴된 인스턴스는 lazy 제거.</summary>
    public bool TryGet(Vector2Int coord, out TerrainChunk chunk)
    {
        if (_instances.TryGetValue(coord, out chunk))
        {
            if (chunk == null)
            {
                _instances.Remove(coord);
                RemoveFromMaps(coord);
                return false;
            }
            return true;
        }
        chunk = null;
        return false;
    }

    public bool TryGetAnchor(Vector2Int coord, out Vector2Int anchorCoord)
        => _anchorMap.TryGetValue(coord, out anchorCoord);

    /// <summary>인스턴스 유무와 관계없이 예약된 서브 위치인지 확인한다.</summary>
    public bool Contains(Vector2Int coord) => _anchorMap.ContainsKey(coord);

    // ─── 해제 ────────────────────────────────────────────────────

    /// <summary>
    /// 앵커 언로드 시 해당 앵커에 속한 모든 서브슬롯 예약을 해제한다.
    /// 역인덱스(_reverseMap)를 사용해 O(k) (k = 발자국 크기)로 처리한다.
    /// </summary>
    public void UnregisterByAnchor(Vector2Int anchorCoord)
    {
        if (!_reverseMap.TryGetValue(anchorCoord, out var subCoords))
            return;

        foreach (var coord in subCoords)
        {
            _anchorMap.Remove(coord);
            _instances.Remove(coord);
        }

        _reverseMap.Remove(anchorCoord);
    }

    // ─── 유틸리티 ────────────────────────────────────────────────

    /// <summary>
    /// 앵커 좌표에 등록된 서브 좌표 목록을 기반으로 실제 발자국(청크 단위)을 계산한다.
    /// LargeStaticTerrainChunk.Width 대신 사용 → TerrainChunk 앵커에서도 올바른 footprint 반환.
    /// </summary>
    public Vector2Int GetFootprint(Vector2Int anchorCoord)
    {
        if (!_reverseMap.TryGetValue(anchorCoord, out var subCoords) || subCoords.Count == 0)
            return Vector2Int.one;

        int maxDx = 1, maxDy = 1;
        foreach (var sub in subCoords)
        {
            maxDx = Mathf.Max(maxDx, sub.x - anchorCoord.x + 1);
            maxDy = Mathf.Max(maxDy, anchorCoord.y - sub.y + 1);
        }
        return new Vector2Int(maxDx, maxDy);
    }

    // ─── 내부 ────────────────────────────────────────────────────

    private void RemoveFromMaps(Vector2Int coord)
    {
        if (!_anchorMap.TryGetValue(coord, out var anchorCoord))
            return;

        _anchorMap.Remove(coord);

        if (_reverseMap.TryGetValue(anchorCoord, out var list))
        {
            list.Remove(coord);
            if (list.Count == 0)
                _reverseMap.Remove(anchorCoord);
        }
    }
}
