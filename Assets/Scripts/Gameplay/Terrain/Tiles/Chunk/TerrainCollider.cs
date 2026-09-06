// @tags: collider, polygon-collider, mesh-gen, moore-neighbor, rdp-simplification, winding-order, physics
using UnityEngine;
using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

/// <summary>
/// Manages PolygonCollider2D generation for terrain chunks.
/// Uses Moore-Neighbor Tracing for outline detection + Ramer-Douglas-Peucker simplification.
/// Implements Even-Odd winding rule to correctly handle holes (Outer=CCW, Inner=CW).
/// </summary>
public class TerrainCollider
{
    // ============================================================================================================
    //  REFERENCES
    // ============================================================================================================
    
    private readonly ChunkData _data;
    private readonly PolygonCollider2D _polyCollider;
    private readonly float _pixelsPerUnit;
    
    // ============================================================================================================
    //  CONFIGURATION
    // ============================================================================================================
    
    /// <summary>
    /// Ramer-Douglas-Peucker simplification tolerance.
    /// This value is in world coordinates (not pixels), so it's PPU-dependent.
    /// Lower values = more precise collider, but more vertices.
    /// Recommended: ~0.5 pixels in world units (0.5 / PPU)
    ///
    /// [static] 모든 콜라이더 인스턴스가 공유한다. worldSettings.json의
    /// chunk.colliderSimplifyTolerance 값이 Awake에서 자동 적용된다.
    /// 값이 클수록 정점·broad-phase 프록시 수가 줄어 Physics2D.FindNewContacts 비용이 감소한다.
    /// </summary>
    private static float s_simplifyTolerance = 0.005f; // ~0.5 pixel at PPU=100

    /// <summary>
    /// worldSettings.json(chunk.colliderSimplifyTolerance)에서 호출.
    /// 모든 터레인 콜라이더 인스턴스에 공유 적용된다. 0 이하 값은 무시(전체 정점 유지 방지).
    /// </summary>
    public static void SetDefaultSimplifyTolerance(float tolerance)
    {
        if (tolerance > 0f) s_simplifyTolerance = tolerance;
    }

    /// <summary>
    /// 현재 적용 중인 단순화 허용오차(월드 유닛). 진단용 —
    /// 콜라이더 윤곽이 픽셀 경계에서 이만큼 안쪽으로 깎일 수 있다는 상한이다.
    /// </summary>
    public static float SimplifyTolerance => s_simplifyTolerance;
    
    // [Reverted] Single Collider Configuration
    // private BoundsInt _clipBounds; 
    
    // ============================================================================================================
    //  OPTIMIZATION: CACHED BUFFERS (Zero GC)
    // ============================================================================================================
    
    private bool[] _cachedVisited;
    private List<Vector2> _rawPathPoints = new List<Vector2>(2000);
    private List<Vector2> _simplifiedPoints = new List<Vector2>(2000);
    
    // List to hold all simplified paths before applying to collider
    private List<List<Vector2>> _pendingPaths = new List<List<Vector2>>();
    
    // [Optimization] List pooling to reduce GC pressure
    private Queue<List<Vector2>> _pathListPool = new Queue<List<Vector2>>();
    private const int MAX_POOL_CAPACITY = 1000; // Max capacity per pooled list

    // [Fix] FixPathWindings 임시 배열 캐시 — 매 호출마다 new[] 하던 것을 재사용
    private PathBounds[] _pathBoundsBuffer = new PathBounds[16];
    private int[] _depthsBuffer = new int[16];

    // 청크 경계 너머 픽셀이 solid인지 조회하는 콜백 (TerrainChunk.IsTransparent 역)
    // null이면 경계 보정 없이 기존 동작 유지
    private System.Func<int, int, bool> _isNeighborSolid;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
    // ============================================================================================================
    //  DIAGNOSTICS (개발 빌드 전용)
    //
    //  "흙은 그대로인데 몸이 통과한다"는 증상은 곧 '지형 픽셀은 있는데 콜라이더 경로가 없다'는 뜻이다.
    //  그 순간을 사후에 재현할 수 없으므로, 콜라이더를 만들 때마다 그 흔적을 남긴다.
    //  TerrainSeamWatchdog가 이 값을 읽어 리포트에 싣는다.
    // ============================================================================================================

    private const int TRACE_OK       = 0;
    private const int TRACE_TINY     = 1;   // 닫혔지만 3점 미만 (1~2픽셀 부스러기 — 정상)
    private const int TRACE_OPEN     = 2;   // 시작점으로 못 돌아옴 → 이 영역은 콜라이더가 없다
    private const int TRACE_OVERFLOW = 3;   // maxLoops 초과

    private int _traceStatus;
    private int _diagFailedTraces;
    private int _diagFailKind;
    private Vector2Int _diagFirstFailStart;

    /// <summary>마지막 UpdateCollider()가 만든 path 수.</summary>
    public int LastPathCount { get; private set; }
    /// <summary>마지막 갱신에서 닫히지 않은(=콜라이더를 못 얻은) 윤곽 수. 0이 정상.</summary>
    public int LastFailedTraces { get; private set; }
    /// <summary>실패 종류(2=OPEN, 3=OVERFLOW).</summary>
    public int LastFailKind { get; private set; }
    /// <summary>첫 실패 윤곽의 시작 픽셀.</summary>
    public Vector2Int LastFailStart { get; private set; }
    public float LastUpdateTime { get; private set; } = -1f;
    public int LastUpdateFrame { get; private set; } = -1;
    public int UpdateCount { get; private set; }

    /// <summary>이 콜라이더 갱신이 이상했음을 알린다. (this, 사유)</summary>
    public static event System.Action<TerrainCollider, string> OnAnomaly;

    private void RecordDiagnostics(int pathCount)
    {
        LastPathCount = pathCount;
        LastFailedTraces = _diagFailedTraces;
        LastFailKind = _diagFailKind;
        LastFailStart = _diagFirstFailStart;
        LastUpdateTime = Time.time;
        LastUpdateFrame = Time.frameCount;
        UpdateCount++;

        if (_diagFailedTraces > 0)
        {
            OnAnomaly?.Invoke(this,
                $"닫히지 않은 윤곽 {_diagFailedTraces}개 (kind={_diagFailKind}, " +
                $"첫 시작픽셀={_diagFirstFailStart.x},{_diagFirstFailStart.y}) → 그 영역은 콜라이더 없음");
        }
        else if (pathCount == 0 && HasAnySolidPixelSampled())
        {
            OnAnomaly?.Invoke(this, "path=0 인데 지형 픽셀이 남아 있음 → 청크 전체가 콜라이더 없음");
        }
    }

    /// <summary>
    /// 8픽셀 격자 샘플링으로 solid 픽셀 존재 여부만 본다.
    /// path==0인 드문 경로에서만 호출되므로 전수 검사할 필요가 없다.
    /// </summary>
    private bool HasAnySolidPixelSampled()
    {
        if (!_data.BasePixels.IsCreated) return false;
        int w = _data.Width, h = _data.Height;
        for (int y = 0; y < h; y += 8)
        {
            int row = y * w;
            for (int x = 0; x < w; x += 8)
                if (_data.BasePixels[row + x].a != 0) return true;
        }
        return false;
    }
#endif

    // ============================================================================================================
    //  CONSTRUCTION
    // ============================================================================================================
    
    public TerrainCollider(ChunkData data, PolygonCollider2D polyCollider, float pixelsPerUnit)
    {
        _data = data;
        _polyCollider = polyCollider;
        _pixelsPerUnit = pixelsPerUnit;
        
        // [Fix] Reset collider offset to ensure consistent positioning
        // PolygonCollider2D uses local space relative to GameObject position
        // Since transform.position is the bottom-left corner, offset should be (0, 0)
        if (_polyCollider != null)
        {
            Vector2 oldOffset = _polyCollider.offset;
            _polyCollider.offset = Vector2.zero;
            if (oldOffset != Vector2.zero)
            {
                Debug.LogWarning($"[TerrainCollider] Constructor: Reset offset from {oldOffset} to {Vector2.zero}");
            }
        }
    }

    // ============================================================================================================
    //  PUBLIC API: COLLIDER UPDATE
    // ============================================================================================================
    
    public void UpdateCollider()
    {
        if (_polyCollider == null)
        {
            Debug.LogError("[TerrainCollider] PolygonCollider2D is null!");
            return;
        }

        // [FIX] Always reset offset before updating to ensure consistency
        _polyCollider.offset = Vector2.zero;

        InitializeTracingState();
        FindAndTraceAllOutlines();

#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
        // ApplyPathsToCollider()가 _pendingPaths를 비우므로 그 전에 세어둔다.
        int diagPathCount = _pendingPaths.Count;
#endif
        ApplyPathsToCollider();

#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
        RecordDiagnostics(diagPathCount);
#endif
    }
    
    public void SetNeighborQuery(System.Func<int, int, bool> isNeighborSolid)
    {
        _isNeighborSolid = isNeighborSolid;
    }

    public bool IsDestroyed()
    {
        return _polyCollider == null || _polyCollider.Equals(null);
    }
    
    /// <summary>
    /// Initialize buffers and reset state for outline tracing.
    /// </summary>
    private void InitializeTracingState()
    {
        _pendingPaths.Clear();

#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
        _diagFailedTraces = 0;
        _diagFailKind = TRACE_OK;
        _diagFirstFailStart = new Vector2Int(-1, -1);
        _traceStatus = TRACE_OK;
#endif

        if (_cachedVisited == null || _cachedVisited.Length != _data.TotalPixels)
        {
            _cachedVisited = new bool[_data.TotalPixels];
        }
        else
        {
            // Only clear visited state within our bounds to save time?
            // Actually, we must clear relevant parts. 
            // _cachedVisited is sized to TotalPixels (shared data size), but we only touch our bounds.
            // CAUTION: If multiple colliders share the same _cachedVisited array, that would be bad.
            // BUT: usage of _cachedVisited implies per-collider instance state.
            // Since this class is instantiated 4 times, each has its own bool array.
            // Just clearing it is fine. 
            Array.Clear(_cachedVisited, 0, _cachedVisited.Length);
        }
    }
    
    // ============================================================================================================
    //  OUTLINE TRACING (OPTIMIZED UNSAFE)
    // ============================================================================================================

    /// <summary>
    /// Scan all pixels to find and trace outline paths.
    /// Uses unsafe pointers for direct memory access to bypass array bounds checks.
    /// </summary>
    private unsafe void FindAndTraceAllOutlines()
    {
        if (!_data.BasePixels.IsCreated) return;

        int w = _data.Width;
        int h = _data.Height;
        int total = w * h;

        // Get raw pointers
        // Note: NativeArray must be valid. _cachedVisited is managed so we use fixed.
        Color32* pixelsPtr = (Color32*)_data.BasePixels.GetUnsafeReadOnlyPtr();

        // IndestructibleMask: mask=1 픽셀은 콜라이더 생성에서 제외 (오버레이 자체 콜라이더가 담당)
        bool hasMask = _data.IndestructibleMask.IsCreated && _data.IndestructibleMask.Length == total;
        byte* maskPtr = hasMask ? (byte*)_data.IndestructibleMask.GetUnsafeReadOnlyPtr() : null;

        fixed (bool* visitedPtr = _cachedVisited)
        {
            // 이웃 청크와 접하는 경계 픽셀을 사전에 visited 처리하여
            // 청크 엣지를 따라 윤곽선이 꺾이는 아티팩트를 방지한다
            PreMarkBoundaryVisited(w, h, pixelsPtr, maskPtr, visitedPtr);

            // Linear scan
            for (int y = 0; y < h; y++)
            {
                // Calculate row start index
                int rowOffset = y * w;

                for (int x = 0; x < w; x++)
                {
                    int index = rowOffset + x;

                    // 1. Skip if Visited
                    if (visitedPtr[index]) continue;

                    // 2. Skip if Empty (Air) or Indestructible overlay pixel
                    if (pixelsPtr[index].a == 0 || (maskPtr != null && maskPtr[index] != 0)) continue;

                    // 3. Check Left Edge (Is this a start of a new outline?)
                    // It is an edge if x==0 OR left pixel is empty/indestructible
                    bool isLeftEdge = (x == 0) || (pixelsPtr[index - 1].a == 0) || (maskPtr != null && maskPtr[index - 1] != 0);

                    if (isLeftEdge)
                    {
                        // Trace logic also moved to unsafe for consistency and shared pointer usage
                        if (TraceOutlineUnsafe(x, y, w, h, pixelsPtr, visitedPtr, maskPtr))
                        {
                            SimplifyAndCollectPath();
                        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
                        // TRACE_TINY(1~2픽셀 부스러기)는 정상이라 세지 않는다.
                        // OPEN/OVERFLOW만이 "지형은 있는데 콜라이더 경로가 없다"는 실제 사고다.
                        else if (_traceStatus != TRACE_TINY)
                        {
                            if (_diagFailedTraces == 0) _diagFirstFailStart = new Vector2Int(x, y);
                            _diagFailedTraces++;
                            _diagFailKind = _traceStatus;
                        }
#endif
                    }
                }
            }
        }
    }

    /// <summary>
    /// 청크 경계에 인접한 이웃 청크가 solid인 경우 해당 경계 픽셀을 visited로 표시.
    /// 이렇게 하면 Moore-Neighbor Tracer가 청크 엣지를 따라 돌지 않아
    /// 청크 경계에서 콜라이더 윤곽이 수직/사선으로 꺾이는 아티팩트가 사라진다.
    /// </summary>
    private unsafe void PreMarkBoundaryVisited(int w, int h, Color32* pixelsPtr, byte* maskPtr, bool* visitedPtr)
    {
        if (_isNeighborSolid == null) return;

        for (int y = 0; y < h; y++)
        {
            // 오른쪽 경계 (x = w-1, 이웃 좌표 x = w)
            int idxR = y * w + (w - 1);
            if (pixelsPtr[idxR].a != 0 && (maskPtr == null || maskPtr[idxR] == 0) && _isNeighborSolid(w, y))
                visitedPtr[idxR] = true;

            // 왼쪽 경계 (x = 0, 이웃 좌표 x = -1)
            int idxL = y * w;
            if (pixelsPtr[idxL].a != 0 && (maskPtr == null || maskPtr[idxL] == 0) && _isNeighborSolid(-1, y))
                visitedPtr[idxL] = true;
        }

        for (int x = 0; x < w; x++)
        {
            // 위쪽 경계 (y = h-1, 이웃 좌표 y = h)
            int idxT = (h - 1) * w + x;
            if (pixelsPtr[idxT].a != 0 && (maskPtr == null || maskPtr[idxT] == 0) && _isNeighborSolid(x, h))
                visitedPtr[idxT] = true;

            // 아래쪽 경계 (y = 0, 이웃 좌표 y = -1)
            int idxB = x;
            if (pixelsPtr[idxB].a != 0 && (maskPtr == null || maskPtr[idxB] == 0) && _isNeighborSolid(x, -1))
                visitedPtr[idxB] = true;
        }
    }

    /// <summary>
    /// Optimized Moore-Neighbor Tracing using pointers.
    /// </summary>
    private unsafe bool TraceOutlineUnsafe(int startX, int startY, int width, int height, Color32* pixelsPtr, bool* visitedPtr, byte* maskPtr)
    {
        _rawPathPoints.Clear();
        int curX = startX;
        int curY = startY;
        
        // Lookup tables for 8 neighbors (N, NE, E, SE, S, SW, W, NW) clockwise
        // dx: { 0, 1, 1, 1, 0, -1, -1, -1 }
        // dy: { 1, 1, 0, -1, -1, -1, 0, 1 }
        // Stackalloc for small lookups to avoid bounds checks of managed arrays
        int* dx = stackalloc int[8] { 0, 1, 1, 1, 0, -1, -1, -1 };
        int* dy = stackalloc int[8] { 1, 1, 0, -1, -1, -1, 0, 1 };

        int enterFrom = 6; // Initially from West (Left)
        int loopCount = 0;
        int maxLoops = (width * height) * 2;
        if (maxLoops < 2000) maxLoops = 2000;

        do
        {
            // Mark current pixel visited
            // We re-calculate index here: curY * width + curX
            visitedPtr[curY * width + curX] = true;

            // Add point (Center of pixel)
            _rawPathPoints.Add(new Vector2((curX + 0.5f) / _pixelsPerUnit, (curY + 0.5f) / _pixelsPerUnit));

            // Start search direction: (enterFrom + 2) in circular 0..7
            // This represents "Backwards + 90 degrees Clockwise" (Moore Neighbor rule)
            int startCheckDir = (enterFrom + 2);
            if (startCheckDir >= 8) startCheckDir -= 8;

            int foundDir = -1;

            // Check 8 neighbors
            for (int i = 0; i < 8; i++)
            {
                // int dir = (startCheckDir + i) % 8; 
                // Optimized cycling:
                int dir = startCheckDir + i;
                if (dir >= 8) dir -= 8;

                int nx = curX + dx[dir];
                int ny = curY + dy[dir];

                // Bounds Check
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    // Check Solidity (indestructible pixels excluded from terrain collider)
                    int nIdx = ny * width + nx;
                    if (pixelsPtr[nIdx].a != 0 && (maskPtr == null || maskPtr[nIdx] == 0))
                    {
                        // Found next solid pixel
                        curX = nx;
                        curY = ny;
                        foundDir = dir;
                        
                        // New enterFrom is Is Opposite of dir (dir + 4)
                        enterFrom = dir + 4;
                        if (enterFrom >= 8) enterFrom -= 8;
                        
                        break;
                    }
                }
            }

            if (foundDir == -1)
            {
                // Isolated pixel or dead end
                break;
            }

            loopCount++;
        }
        while ((curX != startX || curY != startY) && loopCount < maxLoops);

        if (loopCount >= maxLoops)
        {
            Debug.LogError($"[TerrainCollider] Max loop limit reached! Start: {startX},{startY}");
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
            _traceStatus = TRACE_OVERFLOW;
#endif
            return false;
        }

        bool closed = (curX == startX && curY == startY);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
        // 닫히지 않은 윤곽 = 이 solid 영역이 콜라이더 경로를 하나도 못 얻는다.
        // 닫혔지만 점이 3개 미만인 것은 1~2픽셀 부스러기라 무해하다.
        _traceStatus = !closed ? TRACE_OPEN
                     : (_rawPathPoints.Count < 3 ? TRACE_TINY : TRACE_OK);
#endif

        // Return true only if closed loop AND minimum 3 points
        return closed && _rawPathPoints.Count >= 3;
    }


    /// <summary>
    /// Check if a global coordinate is solid (ignoring clip bounds).
    /// Used for Snapping.
    /// </summary>
    // [Reverted] Helper method removed


    // ============================================================================================================
    //  PATH SIMPLIFICATION & COLLECTION
    // ============================================================================================================
    
    private void SimplifyAndCollectPath()
    {
        _simplifiedPoints.Clear();
        LineUtility.Simplify(_rawPathPoints, s_simplifyTolerance, _simplifiedPoints);

        if (_simplifiedPoints.Count >= 3)
        {
            // [Optimized] Use pooled list instead of allocating new one
            var pathList = GetPooledPathList();
            pathList.AddRange(_simplifiedPoints);
            _pendingPaths.Add(pathList);
        }
    }

    // ============================================================================================================
    //  WINDING ORDER & APPLICATION
    // ============================================================================================================

    private void ApplyPathsToCollider()
    {
        if (_pendingPaths.Count == 0)
        {
            _polyCollider.pathCount = 0;
            return;
        }

        // Apply windings: Outer = CCW, Inner = CW
        FixPathWindings(_pendingPaths);

        _polyCollider.pathCount = _pendingPaths.Count;
        //Debug.Log($"[TerrainCollider] Applying {_pendingPaths.Count} paths to collider.");
        for (int i = 0; i < _pendingPaths.Count; i++)
        {
            // [Optimized] Convert to array before passing to Unity API
            // This ensures Unity doesn't hold a reference to our pooled list
            _polyCollider.SetPath(i, _pendingPaths[i]);
        }
        
        // [Optimized] Return lists to pool after collider update
        ReturnPathListsToPool();
    }

    /// <summary>
    /// Analyzes path nesting and enforces Even-Odd winding rule.
    /// Depth 0 (Root) = Solid -> CCW
    /// Depth 1 (Hole) = Void -> CW
    /// Depth 2 (Island) = Solid -> CCW ...
    /// </summary>
    private void FixPathWindings(List<List<Vector2>> paths)
    {
        int count = paths.Count;
        if (_pathBoundsBuffer.Length < count)
        {
            _pathBoundsBuffer = new PathBounds[count * 2];
            _depthsBuffer = new int[count * 2];
        }

        for (int i = 0; i < count; i++)
            _pathBoundsBuffer[i] = CalculatePathBounds(paths[i]);

        for (int i = 0; i < count; i++)
        {
            _depthsBuffer[i] = 0;
            for (int j = 0; j < count; j++)
            {
                if (i == j) continue;
                if (!BoundsContains(_pathBoundsBuffer[j], _pathBoundsBuffer[i])) continue;
                if (IsPathInside(paths[i], paths[j])) _depthsBuffer[i]++;
            }
        }

        for (int i = 0; i < count; i++)
        {
            bool isSolid = (_depthsBuffer[i] % 2 == 0);
            bool isCCW = IsCounterClockwise(paths[i]);
            if (isSolid && !isCCW) paths[i].Reverse();
            else if (!isSolid && isCCW) paths[i].Reverse();
        }
    }

    private bool IsPathInside(List<Vector2> inner, List<Vector2> outer)
    {
        if (inner.Count == 0 || outer.Count < 3) return false;
        // Optimization: Use first point check
        return IsPointInPolygon(inner[0], outer);
    }

    private bool IsPointInPolygon(Vector2 p, List<Vector2> polygon)
    {
        bool inside = false;
        int j = polygon.Count - 1;
        for (int i = 0; i < polygon.Count; i++)
        {
            if ( (polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                 p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x )
            {
                inside = !inside;
            }
            j = i;
        }
        return inside;
    }


    private bool IsCounterClockwise(List<Vector2> path)
    {
        float area = 0;
        for (int i = 0; i < path.Count; i++)
        {
            int j = (i + 1) % path.Count;
            area += (path[j].x - path[i].x) * (path[j].y + path[i].y);
        }
        // Signed Area formula: > 0 is CW (usually), < 0 is CCW? 
        // Standard Surveyors formula: (X2-X1)(Y2+Y1).
        // If result < 0 -> CCW. If result > 0 -> CW.
        return area < 0; 
    }
    
    // ============================================================================================================
    //  AABB OPTIMIZATION FOR WINDING ORDER
    // ============================================================================================================
    
    /// <summary>
    /// Simple AABB (Axis-Aligned Bounding Box) structure.
    /// </summary>
    private struct PathBounds
    {
        public float minX, minY, maxX, maxY;
    }
    
    /// <summary>
    /// Calculate tight bounding box for a path.
    /// </summary>
    private PathBounds CalculatePathBounds(List<Vector2> path)
    {
        if (path.Count == 0)
            return new PathBounds { minX = 0, minY = 0, maxX = 0, maxY = 0 };
        
        PathBounds bounds = new PathBounds
        {
            minX = path[0].x,
            minY = path[0].y,
            maxX = path[0].x,
            maxY = path[0].y
        };
        
        for (int i = 1; i < path.Count; i++)
        {
            if (path[i].x < bounds.minX) bounds.minX = path[i].x;
            if (path[i].x > bounds.maxX) bounds.maxX = path[i].x;
            if (path[i].y < bounds.minY) bounds.minY = path[i].y;
            if (path[i].y > bounds.maxY) bounds.maxY = path[i].y;
        }
        
        return bounds;
    }
    
    /// <summary>
    /// Check if bounds 'outer' completely contains bounds 'inner'.
    /// Early rejection test before expensive polygon containment.
    /// </summary>
    private bool BoundsContains(PathBounds outer, PathBounds inner)
    {
        return outer.minX <= inner.minX &&
               outer.maxX >= inner.maxX &&
               outer.minY <= inner.minY &&
               outer.maxY >= inner.maxY;
    }
    
    // ============================================================================================================
    //  LIST POOLING (GC OPTIMIZATION)
    // ============================================================================================================
    
    /// <summary>
    /// Get a pooled List<Vector2> or create a new one if pool is empty.
    /// </summary>
    private List<Vector2> GetPooledPathList()
    {
        if (_pathListPool.Count > 0)
        {
            var list = _pathListPool.Dequeue();
            list.Clear(); // Ensure it's empty
            return list;
        }
        
        // Create new list with reasonable initial capacity
        return new List<Vector2>(200);
    }
    
    /// <summary>
    /// Return a single list to the pool for reuse.
    /// Lists with excessive capacity are discarded to prevent memory bloat.
    /// </summary>
    private void ReturnPooledPathList(List<Vector2> list)
    {
        // Don't pool lists that have grown too large
        if (list.Capacity > MAX_POOL_CAPACITY)
        {
            return; // Let GC handle it
        }
        
        _pathListPool.Enqueue(list);
    }
    
    /// <summary>
    /// Return all pending path lists to the pool after collider update.
    /// </summary>
    private void ReturnPathListsToPool()
    {
        foreach (var pathList in _pendingPaths)
        {
            ReturnPooledPathList(pathList);
        }
        _pendingPaths.Clear();
    }
}

