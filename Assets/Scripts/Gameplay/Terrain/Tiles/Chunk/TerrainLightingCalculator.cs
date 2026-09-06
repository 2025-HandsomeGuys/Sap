// @tags: lighting, distance-field, boundary-sync, neighbor-sync, chamfer, burst-job
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// 지형의 조명(거리 필드) 계산과 이웃 동기화를 담당하는 클래스
/// </summary>
public class TerrainLightingCalculator
{
    private TerrainChunk _owner;

    public TerrainLightingCalculator(TerrainChunk owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// [P0] BoundarySync 를 Burst Job 으로 스케줄. 메인 스레드 블로킹 없음.
    /// 이전 SyncBoundaryDistanceWithNeighbors 의 ~31ms idle 원인이었던
    /// neighbor.CompleteLighting() 폭포 + 메인 스레드 픽셀 복사를 모두 제거한다.
    /// </summary>
    public void ScheduleBoundarySyncJob()
    {
        if (_owner == null || _owner.ChunkProvider == null) return;

        ChunkData data = _owner.GetData();
        if (data == null || !data.DistanceField.IsCreated) return;

        var visualizer = _owner.Visualizer;
        if (visualizer == null) return;

        var provider = _owner.ChunkProvider;
        int cx = _owner.ChunkX;
        int cy = _owner.ChunkY;

        var n = new ChunkJobScheduler.BoundaryNeighborSet();

        // 각 방향 이웃 청크 참조도 함께 캐시 — BoundarySync 스케줄 후 등록용
        TerrainChunk left        = CaptureNeighbor(provider, new Vector2Int(cx - 1, cy),     out n.left,        out n.leftHandle,        out n.hasLeft);
        TerrainChunk right       = CaptureNeighbor(provider, new Vector2Int(cx + 1, cy),     out n.right,       out n.rightHandle,       out n.hasRight);
        TerrainChunk bottom      = CaptureNeighbor(provider, new Vector2Int(cx, cy - 1),     out n.bottom,      out n.bottomHandle,      out n.hasBottom);
        TerrainChunk top         = CaptureNeighbor(provider, new Vector2Int(cx, cy + 1),     out n.top,         out n.topHandle,         out n.hasTop);
        TerrainChunk bottomLeft  = CaptureNeighbor(provider, new Vector2Int(cx - 1, cy - 1), out n.bottomLeft,  out n.bottomLeftHandle,  out n.hasBottomLeft);
        TerrainChunk bottomRight = CaptureNeighbor(provider, new Vector2Int(cx + 1, cy - 1), out n.bottomRight, out n.bottomRightHandle, out n.hasBottomRight);
        TerrainChunk topLeft     = CaptureNeighbor(provider, new Vector2Int(cx - 1, cy + 1), out n.topLeft,     out n.topLeftHandle,     out n.hasTopLeft);
        TerrainChunk topRight    = CaptureNeighbor(provider, new Vector2Int(cx + 1, cy + 1), out n.topRight,    out n.topRightHandle,    out n.hasTopRight);

        // Y=0 가장 위 청크: top 이웃이 없으면 위쪽이 하늘 → BoundarySyncJob 의 isSkyAbove 분기 활성화
        n.isSkyAbove = (cy == 0 && !n.hasTop);

        // [P0] BoundarySync 스케줄. 반환된 핸들을 각 이웃에게 외부 reader 로 등록 →
        // 이웃 측에서 다음 distanceField writer 스케줄 시 안전 검증 통과.
        JobHandle bsHandle = visualizer.ScheduleBoundarySync(n);

        if (n.hasLeft)        left.RegisterDistanceFieldReader(bsHandle);
        if (n.hasRight)       right.RegisterDistanceFieldReader(bsHandle);
        if (n.hasBottom)      bottom.RegisterDistanceFieldReader(bsHandle);
        if (n.hasTop)         top.RegisterDistanceFieldReader(bsHandle);
        if (n.hasBottomLeft)  bottomLeft.RegisterDistanceFieldReader(bsHandle);
        if (n.hasBottomRight) bottomRight.RegisterDistanceFieldReader(bsHandle);
        if (n.hasTopLeft)     topLeft.RegisterDistanceFieldReader(bsHandle);
        if (n.hasTopRight)    topRight.RegisterDistanceFieldReader(bsHandle);
    }

    private static TerrainChunk CaptureNeighbor(IChunkProvider provider, Vector2Int coord,
        out NativeArray<ushort> field, out JobHandle handle, out bool has)
    {
        var c = provider.GetChunk(coord);
        var d = c?.GetData();
        if (d != null && d.DistanceFieldHalf.IsCreated)
        {
            // [half-res] BoundarySyncJob은 half 격자에서 동작 → 이웃도 half 거리장을 넘긴다.
            field = d.DistanceFieldHalf;
            handle = c.GetLightingHandle();
            has = true;
            return c;
        }
        field = default;
        handle = default;
        has = false;
        return null;
    }

    /// <summary>
    /// [DEPRECATED] 메인 스레드 동기 버전. P0 이후 ScheduleBoundarySyncJob() 으로 대체됨.
    /// 외부 fallback 경로에서만 보존. 호출 시 ~31ms idle 의 원인이 되므로 hot path 에서 사용 금지.
    /// </summary>
    public void SyncBoundaryDistanceWithNeighbors()
    {
        if (_owner == null || _owner.ChunkProvider == null) return;

        ChunkData data = _owner.GetData();
        if (data == null) return;

        var provider = _owner.ChunkProvider;
        int width  = _owner.width;
        int height = _owner.height;
        int cx = _owner.ChunkX;
        int cy = _owner.ChunkY;

        const ushort BOUNDARY_COST    = 5;
        const ushort MaxLightDistScaled = 255;
        const int SYNC_DEPTH = 50;

        var leftChunk        = provider.GetChunk(new Vector2Int(cx - 1, cy));
        var rightChunk       = provider.GetChunk(new Vector2Int(cx + 1, cy));
        var topChunk         = provider.GetChunk(new Vector2Int(cx, cy + 1));
        var bottomChunk      = provider.GetChunk(new Vector2Int(cx, cy - 1));
        var topLeftChunk     = provider.GetChunk(new Vector2Int(cx - 1, cy + 1));
        var topRightChunk    = provider.GetChunk(new Vector2Int(cx + 1, cy + 1));
        var bottomLeftChunk  = provider.GetChunk(new Vector2Int(cx - 1, cy - 1));
        var bottomRightChunk = provider.GetChunk(new Vector2Int(cx + 1, cy - 1));

        // 1. Left Border
        if (leftChunk != null && leftChunk.GetData() != null && leftChunk.GetData().DistanceField.IsCreated)
        {
            leftChunk.CompleteLighting();
            for (int k = 0; k < SYNC_DEPTH; k++)
            {
                int baseCost = BOUNDARY_COST + k * 5;
                for (int y = 0; y < height; y++)
                {
                    int myIdx = data.ToIndex(k, y);
                    ushort newDist = (ushort)Mathf.Min(leftChunk.GetNeighborDistance(width - 1 - k, y) + baseCost, MaxLightDistScaled);
                    if (newDist < data.DistanceField[myIdx])
                        data.DistanceField[myIdx] = newDist;
                }
            }
        }

        // 2. Right
        if (rightChunk != null && rightChunk.GetData() != null && rightChunk.GetData().DistanceField.IsCreated)
        {
            rightChunk.CompleteLighting();
            for (int k = 0; k < SYNC_DEPTH; k++)
            {
                int baseCost = BOUNDARY_COST + k * 5;
                for (int y = 0; y < height; y++)
                {
                    int myIdx = data.ToIndex(width - 1 - k, y);
                    ushort newDist = (ushort)Mathf.Min(rightChunk.GetNeighborDistance(k, y) + baseCost, MaxLightDistScaled);
                    if (newDist < data.DistanceField[myIdx])
                        data.DistanceField[myIdx] = newDist;
                }
            }
        }

        // 3. Bottom
        if (bottomChunk != null && bottomChunk.GetData() != null && bottomChunk.GetData().DistanceField.IsCreated)
        {
            bottomChunk.CompleteLighting();
            for (int k = 0; k < SYNC_DEPTH; k++)
            {
                int baseCost = BOUNDARY_COST + k * 5;
                for (int x = 0; x < width; x++)
                {
                    int myIdx = data.ToIndex(x, k);
                    ushort newDist = (ushort)Mathf.Min(bottomChunk.GetNeighborDistance(x, height - 1 - k) + baseCost, MaxLightDistScaled);
                    if (newDist < data.DistanceField[myIdx])
                        data.DistanceField[myIdx] = newDist;
                }
            }
        }

        // 4. Top
        if (topChunk != null && topChunk.GetData() != null && topChunk.GetData().DistanceField.IsCreated)
        {
            topChunk.CompleteLighting();
            for (int k = 0; k < SYNC_DEPTH; k++)
            {
                int baseCost = BOUNDARY_COST + k * 5;
                for (int x = 0; x < width; x++)
                {
                    int myIdx = data.ToIndex(x, height - 1 - k);
                    ushort newDist = (ushort)Mathf.Min(topChunk.GetNeighborDistance(x, k) + baseCost, MaxLightDistScaled);
                    if (newDist < data.DistanceField[myIdx])
                        data.DistanceField[myIdx] = newDist;
                }
            }
        }
        else if (cy == 0) // Y=0: 위가 하늘(에어) → 거리 BOUNDARY_COST
        {
            for (int x = 0; x < width; x++)
            {
                int myIdx = data.ToIndex(x, height - 1);
                if (BOUNDARY_COST < data.DistanceField[myIdx])
                    data.DistanceField[myIdx] = BOUNDARY_COST;
            }
        }

        // 5. Corners
        ProcessCornerHelper(bottomLeftChunk,  0,       0,        width-1, height-1, data);
        ProcessCornerHelper(bottomRightChunk, width-1, 0,        0,       height-1, data);
        ProcessCornerHelper(topLeftChunk,     0,       height-1, width-1, 0,        data);
        ProcessCornerHelper(topRightChunk,    width-1, height-1, 0,       0,        data);
    }

    private void ProcessCornerHelper(TerrainChunk neighbor, int myX, int myY, int nX, int nY, ChunkData data)
    {
        var nd = neighbor?.GetData();
        if (nd == null || !nd.DistanceField.IsCreated) return;
        neighbor.CompleteLighting();
        const ushort CORNER_COST = 7;
        int myIdx = data.ToIndex(myX, myY);
        ushort newDist = (ushort)Mathf.Min(neighbor.GetNeighborDistance(nX, nY) + CORNER_COST, 255);
        if (newDist < data.DistanceField[myIdx])
            data.DistanceField[myIdx] = newDist;
    }

    /// <summary>
    /// 이웃 청크들에게 조명 갱신을 요청합니다.
    /// </summary>
    public void UpdateBoundaryLighting(bool left, bool right, bool top, bool bottom)
    {
        if (_owner == null || _owner.ChunkProvider == null) return;
        
        int cx = _owner.ChunkX;
        int cy = _owner.ChunkY;

        // Cardinals
        if (left) TriggerNeighborRefresh(cx - 1, cy);
        if (right) TriggerNeighborRefresh(cx + 1, cy);
        if (bottom) TriggerNeighborRefresh(cx, cy - 1);
        if (top) TriggerNeighborRefresh(cx, cy + 1);

        // Diagonals
        if (left && bottom) TriggerNeighborRefresh(cx - 1, cy - 1);
        if (right && bottom) TriggerNeighborRefresh(cx + 1, cy - 1);
        if (left && top) TriggerNeighborRefresh(cx - 1, cy + 1);
        if (right && top) TriggerNeighborRefresh(cx + 1, cy + 1);
    }
    
    private void TriggerNeighborRefresh(int cx, int cy)
    {
        var neighbor = _owner.ChunkProvider.GetChunk(new Vector2Int(cx, cy));
        if (neighbor == null) return;

        // MarkChunkDirty를 통해 LateUpdate의 비동기 처리(ProcessDirtyChunksAsync)에 위임.
        // RefreshVisuals() 직접 호출 시 다수의 JobHandle.Complete가 동기적으로
        // 연달아 발생해 프레임을 꽉 채우는 랙의 원인이 됨.
        var mgr = InfinityMapManager.Instance;
        if (mgr != null)
        {
            var fullRect = new RectInt(0, 0, neighbor.width, neighbor.height);
            mgr.MarkChunkDirty(neighbor, fullRect);
        }
        else
        {
            neighbor.RefreshVisuals(); // fallback: 매니저 없을 때만 동기 호출
        }
    }
}
