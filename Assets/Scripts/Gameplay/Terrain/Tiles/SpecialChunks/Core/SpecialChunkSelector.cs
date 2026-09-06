// @tags: special-chunk, generation, spawn, chunk, selection
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 특수 청크 확률 선택, 레이어 경계·간격 제약 판별을 담당한다.
/// SRP: 선택 로직만. 스폰·레지스트리와 무관.
/// </summary>
public class SpecialChunkSelector
{
    private readonly List<SpecialChunkManager.SpecialChunkPool> _pools;
    private readonly int _minChunkSpacing;
    private readonly int _layerBoundarySpacing;
    private readonly int[] _layerBoundaryYCoords;

    // GC 절약: HashSet 재사용
    private readonly HashSet<Vector2Int> _footprintBuffer = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _exclusionBuffer = new HashSet<Vector2Int>();

    public SpecialChunkSelector(
        List<SpecialChunkManager.SpecialChunkPool> pools,
        int minChunkSpacing,
        int layerBoundarySpacing,
        int[] layerBoundaryYCoords)
    {
        _pools = pools;
        _minChunkSpacing = minChunkSpacing;
        _layerBoundarySpacing = layerBoundarySpacing;
        _layerBoundaryYCoords = layerBoundaryYCoords;
    }

    /// <summary>
    /// 거리·경계 제약 포함 특수 청크 선택. 당첨 시 def 반환, 꽝 시 null.
    /// </summary>
    public SpecialChunkManager.SpecialChunkDef? TrySelect(
        Vector2Int coord, TileType layerType, int worldSeed, SubChunkRegistry registry)
    {
        SpecialChunkManager.SpecialChunkDef? raw = TrySelectRaw(coord, layerType, worldSeed);
        if (raw == null) return null;

        //if (SpecialChunkFootprint.IsNearLayerBoundary(coord, _layerBoundaryYCoords, _layerBoundarySpacing)) { Debug.Log($"[SpecialChunkSelector] {coord} → 레이어 경계 근처, 스킵"); return null; }

        var def = raw.Value;

        // footprint: prefab 크기 기반 직사각형
        _footprintBuffer.Clear();
        SpecialChunkFootprint.Build(coord, GetSize(def), _footprintBuffer);

        // 엘리베이터 정류장 회피 — 점유 칸(footprint)이나 링크 피스가 정류장 청크를 덮으면 포기한다.
        //
        // 특수청크는 청크 생성 1-B(ChunkDataProvider)에서 결정되는 반면 엘리베이터는 한참 뒤
        // Phase 2 데코레이터에서 생기고, 특수청크는 일반 데코 경로를 아예 타지 않는다.
        // → 정류장 좌표가 당첨되면 그 층 엘리베이터가 조용히 사라져 그 층에 갇힌다.
        //
        // 스폰 시점(SpawnSpecialChunkIfPossible)이 아니라 여기서 막는 이유:
        // IsSubChunkByPool·IsLinkedPieceByPool·PredictAnchorsInRadius(나침반)가 전부 TrySelect로
        // 역산한다. 스폰만 막으면 "앵커는 안 생겼는데 서브좌표라고 판정돼 IsBlocked"가 되어
        // 정류장 청크가 통째로 빈 칸이 된다.
        //
        // 보호 좌표(RegisterProtectedCoord)를 쓰지 않는 이유: 그건 Rock·Mineral 데코까지 막아서
        // 방 PNG가 없는 층의 정류장 청크가 민둥산이 된다. 여기선 특수청크만 배제한다.
        foreach (var fp in _footprintBuffer)
            if (ElevatorStopLayout.IsStopCoord(fp)) return null;

        if (def.linkedPieces != null)
        {
            foreach (var piece in def.linkedPieces)
                if (ElevatorStopLayout.IsStopCoord(coord + piece.offset)) return null;
        }

        // exclusion zone: footprint에서 minChunkSpacing 확장
        _exclusionBuffer.Clear();
        foreach (var fp in _footprintBuffer)
            for (int dx = -_minChunkSpacing; dx <= _minChunkSpacing; dx++)
                for (int dy = -_minChunkSpacing; dy <= _minChunkSpacing; dy++)
                    _exclusionBuffer.Add(fp + new Vector2Int(dx, dy));

        _exclusionBuffer.ExceptWith(_footprintBuffer);

        int myHash = CoordHash(coord, worldSeed);

        foreach (var checkCoord in _exclusionBuffer)
        {
            if (registry.Contains(checkCoord))
            {
                registry.TryGetAnchor(checkCoord, out var blockerAnchor);
                //Debug.Log($"[EXCL_BLOCK] {coord} 차단 | exclusion={checkCoord} | blocker앵커={blockerAnchor}");
                return null;
            }

            // 케이스 1: checkCoord 자체가 이웃 앵커
            if (TrySelectRaw(checkCoord, layerType, worldSeed) != null)
            {
                int neighborHash = CoordHash(checkCoord, worldSeed);
                if (neighborHash > myHash) return null;
                continue;
            }

            // 케이스 2: checkCoord가 대형 이웃 청크의 서브좌표인 경우 (앵커는 exclusion 밖)
            if (TryGetAnchorByPool(checkCoord, layerType, worldSeed, out var neighborAnchor))
            {
                int neighborHash = CoordHash(neighborAnchor, worldSeed);
                if (neighborHash > myHash) return null;
            }
        }

        return def;
    }

    /// <summary>거리·경계 제약 없이 순수 확률만으로 선택한다. 충돌 체크 내부용.</summary>
    public SpecialChunkManager.SpecialChunkDef? TrySelectRaw(
        Vector2Int coord, TileType layerType, int worldSeed)
    {
        int poolIndex = -1;
        for (int i = 0; i < _pools.Count; i++)
        {
            if (_pools[i].targetLayer == layerType) { poolIndex = i; break; }
        }
        if (poolIndex == -1 || _pools[poolIndex].chunks == null || _pools[poolIndex].chunks.Count == 0)
        {
            //Debug.Log($"[SpecialChunkSelector] {coord} layerType={layerType} → 풀 없음 (poolIndex={poolIndex})");
            return null;
        }

        var pool = _pools[poolIndex];
        int hash = CoordHash(coord, worldSeed);
        var prng = new System.Random(hash);

        foreach (var chunkDef in pool.chunks)
        {
            //if (chunkDef.prefab == null) { //Debug.Log($"[SpecialChunkSelector] {coord} → prefab null, 스킵"); continue; }

            bool hasDepthLimit = chunkDef.minDepth != 0 || chunkDef.maxDepth != 0;
            if (hasDepthLimit)
            {
                int depth = -coord.y;
                if (chunkDef.minDepth != 0 && depth < chunkDef.minDepth) continue;
                if (chunkDef.maxDepth != 0 && depth > chunkDef.maxDepth) continue;
            }

            double roll = prng.NextDouble() * 100.0;
            //Debug.Log($"[SpecialChunkSelector] {coord} prefab={chunkDef.prefab.name} roll={roll:F1} chance={chunkDef.spawnChance} → {(roll < chunkDef.spawnChance ? "당첨" : "꽝")}");
            if (roll < chunkDef.spawnChance)
                return chunkDef;
        }
        return null;
    }

    /// <summary>
    /// 충돌 해결(TrySelect) 결과 기반으로 서브 위치 여부를 판별한다.
    /// TrySelectRaw 대신 TrySelect를 사용해 실제 당첨 앵커만 고려한다.
    /// </summary>
    public bool IsSubChunkByPool(Vector2Int coord, TileType layerType, int worldSeed, SubChunkRegistry registry)
    {
        foreach (var pool in _pools)
        {
            if (pool.targetLayer != layerType || pool.chunks == null) continue;

            foreach (var chunkDef in pool.chunks)
            {
                var size = GetSize(chunkDef);
                if (size.x <= 1 && size.y <= 1) continue;

                foreach (var potentialAnchor in SpecialChunkFootprint.GetCandidateAnchors(coord, size))
                {
                    // [Fix] TrySelectRaw → TrySelect: 충돌 해결 결과까지 검증해 오탐 제거
                    var defAtAnchor = TrySelect(potentialAnchor, layerType, worldSeed, registry);
                    if (defAtAnchor != null && defAtAnchor.Value.prefab == chunkDef.prefab)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 해당 좌표가 어떤 앵커의 링크 피스인지 풀 역산으로 판별한다.
    /// 앵커 로드 여부와 무관하게 동작 (결정론적).
    /// </summary>
    /// <summary>TrySelect(충돌 해결 포함)로 역산 — 오탐 없음. TrySelect 내부에서 호출하면 무한 재귀 발생하므로 외부 전용.</summary>
    public bool IsLinkedPieceByPool(Vector2Int coord, TileType layerType, int worldSeed, SubChunkRegistry registry)
    {
        foreach (var pool in _pools)
        {
            if (pool.targetLayer != layerType || pool.chunks == null) continue;
            foreach (var def in pool.chunks)
            {
                if (def.linkedPieces == null || def.linkedPieces.Length == 0) continue;
                foreach (var piece in def.linkedPieces)
                {
                    var potentialAnchor = coord - piece.offset;
                    var defAtAnchor = TrySelect(potentialAnchor, layerType, worldSeed, registry);
                    if (defAtAnchor != null && defAtAnchor.Value.prefab == def.prefab)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 역산으로 앵커 좌표와 이 피스에 지정된 프리팹을 반환한다.
    /// TryGetAnchorByPool의 링크 피스 버전.
    /// </summary>
    /// <summary>TrySelect(충돌 해결 포함)로 역산 — 오탐 없음.</summary>
    public bool TryGetLinkedAnchorByPool(
        Vector2Int coord, TileType layerType, int worldSeed,
        SubChunkRegistry registry,
        out Vector2Int anchorCoord, out MonoBehaviour prefab)
    {
        anchorCoord = default;
        prefab = null;

        foreach (var pool in _pools)
        {
            if (pool.targetLayer != layerType || pool.chunks == null) continue;
            foreach (var def in pool.chunks)
            {
                if (def.linkedPieces == null || def.linkedPieces.Length == 0) continue;
                foreach (var piece in def.linkedPieces)
                {
                    var potentialAnchor = coord - piece.offset;
                    var defAtAnchor = TrySelect(potentialAnchor, layerType, worldSeed, registry);
                    if (defAtAnchor != null && defAtAnchor.Value.prefab == def.prefab)
                    {
                        anchorCoord = potentialAnchor;
                        prefab = piece.prefab;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>레지스트리 없이 순수 풀 탐색으로 앵커 좌표를 역산한다.</summary>
    public bool TryGetAnchorByPool(
        Vector2Int subCoord, TileType layerType, int worldSeed, out Vector2Int anchorCoord)
    {
        anchorCoord = default;

        foreach (var pool in _pools)
        {
            if (pool.targetLayer != layerType || pool.chunks == null) continue;

            foreach (var chunkDef in pool.chunks)
            {
                var size = GetSize(chunkDef);
                if (size.x <= 1 && size.y <= 1) continue;

                foreach (var potentialAnchor in SpecialChunkFootprint.GetCandidateAnchors(subCoord, size))
                {
                    var defAtAnchor = TrySelectRaw(potentialAnchor, layerType, worldSeed);
                    if (defAtAnchor != null && defAtAnchor.Value.prefab == chunkDef.prefab)
                    {
                        anchorCoord = potentialAnchor;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 청크 점유 크기를 반환한다.
    /// LargeStaticTerrainChunk 프리팹이면 컴포넌트의 chunkGridWidth/Height를 우선 사용 (Single Source of Truth).
    /// 그 외 타입은 SpecialChunkDef의 chunkSizeX/Y를 fallback으로 사용.
    /// </summary>
    private static Vector2Int GetSize(SpecialChunkManager.SpecialChunkDef def)
    {
        if (def.prefab is LargeStaticTerrainChunk lstc)
            return new Vector2Int(lstc.chunkGridWidth, lstc.chunkGridHeight);
        return new Vector2Int(def.SizeX, def.SizeY);
    }

    private static int CoordHash(Vector2Int coord, int worldSeed)
        => (coord.x * 73856093) ^ (coord.y * 19349663) ^ (worldSeed * 83492791);
}
