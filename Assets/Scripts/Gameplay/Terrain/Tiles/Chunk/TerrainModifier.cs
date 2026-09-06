// @tags: digging, terrain-modification, pixel-destruction, island-removal, erosion, narrow-protrusion, explosion, bfs
using UnityEngine;
using Unity.Collections;
using System;
using System.Collections.Generic;

/// <summary>
/// Pure logic class for terrain modification algorithms.
/// This class is NOT a MonoBehaviour and contains NO Unity component dependencies.
/// All methods take ChunkData as input and modify it directly.
/// </summary>
public class TerrainModifier
{
    // ============================================================================================================
    //  CONSTANTS (Migrated from TerrainChunk)
    // ============================================================================================================

    private const float ROCK_DIG_THRESHOLD = 0.04f;      // Rock digging threshold (shovel vs rock)
    private const int SHOVEL_TOOL_INDEX = 1;             // 삽. MiningStaminaTuning.Shovel과 같은 값
    // ROCK_DIG_THRESHOLD(0.04)는 '거리 제곱 / 반경 제곱' 비율이다.
    // 마스크 경로에는 sqrRadius가 없으므로 거리 비율로 환산해 쓴다. sqrt(0.04) = 0.2
    private const float ROCK_DIG_RADIUS_RATIO = 0.2f;
    private const int DIG_MARGIN_EXTRA = 5;              // Dig boundary calculation margin
    private const int BORDER_MARGIN_EXTRA = 10;          // Border margin extra
    private const int ADAPTIVE_MARGIN_MIN = 5;           // Adaptive margin minimum
    private const float ADAPTIVE_MARGIN_FACTOR = 0.2f;   // Adaptive margin factor
    private const int PARTICLE_SAMPLE_RATE = 8;          // 섬 제거 시 8픽셀당 파티클 1개 (퍼포먼스 최적화)
    private const int MAX_ISLAND_PARTICLES = 256;        // 섬 1개 제거당 파티클 상한 (대형 섬 붕괴 시 스파이크 방지)
    
    // ============================================================================================================
    //  DELEGATES (Callbacks for Unity-dependent operations)
    // ============================================================================================================
    
    /// <summary>
    /// Callback for spawning debris particles. Called when pixels are removed.
    /// Parameters: worldPos, color
    /// </summary>
    public delegate void ParticleSpawnCallback(Vector2 worldPos, Color32 color);
    
    /// <summary>
    /// Callback to convert local pixel coordinates to world position.
    /// Parameters: localX, localY
    /// Returns: world position
    /// </summary>
    public delegate Vector2 PixelToWorldCallback(int x, int y);

    /// <summary>
    /// 픽셀이 파괴될 때 발생하는 static 이벤트.
    /// worldPos: 파괴된 픽셀의 월드 좌표, destroyedColor: 파괴 전 원래 색상.
    /// </summary>
    public static event System.Action<Vector2, Color32> OnPixelDestroyed;
    
    // ============================================================================================================
    //  CONFIGURATION (Injected via Constructor or Properties)
    // ============================================================================================================
    
    public float PixelsPerUnit { get; set; } = 100f;
    // [제거됨] public float ReachOffset { get; set; } = 1.0f; // Digger에서 계산된 위치를 쓰기 위해 제거
    public const float VERTICAL_SCALE_DEFAULT = 1.5f;
    public float VerticalScale { get; set; } = VERTICAL_SCALE_DEFAULT;
    public bool UseIslandRemoval { get; set; } = true;

    /// <summary>
    /// 공중 섬으로 판정할 최대 픽셀 수(면적). 이 값을 넘는 덩어리는 '땅'으로 간주해 남긴다.
    /// 100 PPU 기준 10000px = 1×1 유닛, 90000px = 3×3 유닛.
    /// worldSettings.json 의 chunk.maxIslandSize 로 덮어쓴다.
    /// </summary>
    public int MaxIslandSize { get; set; } = 90000;
    
    // ============================================================================================================
    //  CROSS-CHUNK SUPPORT
    // ============================================================================================================
    
    /// <summary>
    /// Holds references to neighboring chunk data for GC-free cross-chunk pixel checks.
    /// </summary>
    public struct NeighborContext
    {
        public ChunkData Top;
        public ChunkData Bottom;
        public ChunkData Left;
        public ChunkData Right;
    }

    // ============================================================================================================
    //  CACHED STRUCTURES (Island Removal - 플랫 배열 최적화)
    // ============================================================================================================

    // HashSet 대신 byte[] 플랫 배열 사용 — 인덱스 접근 O(1), 캐시 미스 없음
    // 0 = 미방문, 1 = 현재 탐색 클러스터, 2 = 안전지대(ground 연결 확정)
    private byte[] _pixelState;

    /// <summary>
    /// 클러스터 탐색용 스택(flat index). Queue(BFS) 대신 Stack(DFS)인 이유:
    /// BFS는 시작점에서 동심원으로 퍼져 경계까지 거리 d 를 확인하는 데 O(d²) 픽셀을 방문하지만,
    /// DFS는 한 방향으로 직진해 경계(=ground)에 O(d) 로 도달한다.
    /// 압도적 다수인 "땅에 붙은 덩어리" 판정이 조기 종료되므로 MaxIslandSize 를 크게 올려도 비용이 거의 안 는다.
    /// 진짜 공중 섬은 어차피 클러스터 전체를 방문해야 하므로 순회 순서와 무관하게 결과가 동일하다.
    /// </summary>
    private List<int> _searchStack;
    private List<int> _currentCluster;  // flat index 저장

    // 좁은 돌기 제거용 캐시 (GC 최소화)
    private List<int> _narrowRemoveList = new List<int>(512);

    // IslandRemoval: 이전 호출에서 방문한 픽셀 인덱스 추적
    // Array.Clear(1M bytes) 대신 방문한 픽셀만 선택적으로 0으로 리셋
    private readonly List<int> _visitedIndices = new List<int>(4096);

    // 섬 제거가 실제로 지운 픽셀의 합집합 경계(청크 로컬). CheckFloatingIslandsInArea 가 리셋하고
    // ProcessIsland 가 넓힌다. 호출부가 더티 렉트를 여기까지 늘려야 한다 — 배경은 그 메서드 주석.
    private int _islandMinX, _islandMinY, _islandMaxX, _islandMaxY;
    private bool _islandRemovedAny;
    
    // ============================================================================================================
    //  CONSTRUCTOR
    // ============================================================================================================
    
    public TerrainModifier(float ppu = 100f, float verticalScale = 1.5f)
    {
        PixelsPerUnit = ppu;
        VerticalScale = verticalScale;
        // ReachOffset 초기화 제거
    }

    /// <summary>
    /// 섬 감지 탐색 버퍼를 청크 크기에 맞게 초기화합니다.
    /// TerrainChunk.Awake()에서 청크 크기 확정 후 반드시 호출해야 합니다.
    /// </summary>
    public void InitializeIslandBuffers(int width, int height)
    {
        int size = width * height;
        // MaxIslandSize 가 커도 청크마다 수 MB 를 선점하지 않도록 초기 용량은 상한을 둔다.
        // (DFS 스택 깊이는 클러스터 둘레 수준이라 실제로 이 이상 잘 안 커진다. 필요하면 List가 알아서 늘어남)
        int capacity = Mathf.Min(MaxIslandSize + 1, size, INITIAL_CLUSTER_CAPACITY);
        _pixelState = new byte[size];
        _searchStack = new List<int>(capacity);
        _currentCluster = new List<int>(capacity);
    }

    private const int INITIAL_CLUSTER_CAPACITY = 8192;

    // ============================================================================================================
    //  PUBLIC API: DIGGING
    // ============================================================================================================
    
    /// <summary>
    /// Digs a hole in the terrain at the specified world position.
    /// Returns the modified area bounds for visual updates.
    /// </summary>
    public DigResult Dig(ChunkData data, Vector2 mouseWorldPos, Vector2 playerWorldPos, 
                         float radius, int toolIndex,
                         // [New] 좌표 변환 함수 주입
                         System.Func<Vector2, Vector2Int> worldToPixelConverter, 
                         NeighborContext neighbors = default,
                         ParticleSpawnCallback particleCallback = null,
                         PixelToWorldCallback pixelToWorld = null)
    {
        // Use injected converter
        Vector2Int mousePixel = worldToPixelConverter(mouseWorldPos);
        Vector2Int playerPixel = worldToPixelConverter(playerWorldPos);

        // [Logic Update] Calculate direction & reachOffsetPx FIRST (Moved up to fix CS0841)
        float distToTarget = Vector2.Distance(playerWorldPos, mouseWorldPos);
        float reachOffsetPx = distToTarget * PixelsPerUnit;

        // [Restored] Calculate dig direction & angle (User requested)
        Vector2 direction = (mouseWorldPos - playerWorldPos).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x);
        
        // [FIX - Precision Across Chunks] 
        // Calculate the TRUE geometric center of the dig hole in World Space first!
        // This avoids quantization differences when FloorToInt is applied independently in different chunks.
        Vector2 trueCenterWorld = playerWorldPos + (direction * (reachOffsetPx / PixelsPerUnit));
        
        // Use the injected converter to get the exact identical pixel coordinate in this chunk
        Vector2Int trueCenterPixel = worldToPixelConverter(trueCenterWorld);
        
        int centerPx = trueCenterPixel.x;
        int centerPy = trueCenterPixel.y;
        
        float radiusPx = radius * PixelsPerUnit;
        
        // [Note] reachOffsetPx is now defined above, so we don't redefine it here.
        
        // Calculate bounds
        // 마스크는 세로로 길거나 회전하면 타원 기준 margin을 벗어나 잘린다.
        // 마스크 경로에서는 회전 외접원 반경을 쓴다. 넉넉해도 실제 제거는 마스크 판정이 정하므로
        // 결과가 틀리지 않는다 — 모자라면 잘린다.
        bool useShovelMask = (toolIndex == SHOVEL_TOOL_INDEX) && ShovelDigMask.IsActive;
        int maxRadiusPx = useShovelMask
            ? Mathf.CeilToInt(ShovelDigMask.BoundsRadiusPx(radiusPx))
            : Mathf.CeilToInt(radiusPx * Mathf.Max(1f, VerticalScale));
        int margin = maxRadiusPx + DIG_MARGIN_EXTRA;
        
        int rawMinX = centerPx - margin;
        int rawMaxX = centerPx + margin;
        int rawMinY = centerPy - margin;
        int rawMaxY = centerPy + margin;
        
        int minX = Mathf.Clamp(rawMinX, 0, data.Width);
        int maxX = Mathf.Clamp(rawMaxX, 0, data.Width);
        int minY = Mathf.Clamp(rawMinY, 0, data.Height);
        int maxY = Mathf.Clamp(rawMaxY, 0, data.Height);
        
        // Process pixels (Pass true geometric center)
        int removedPixels = ProcessDigPixels(data, minX, minY, maxX, maxY,
                                         centerPx, centerPy,
                                         radiusPx, angle, toolIndex,
                                         particleCallback, pixelToWorld);
        bool modified = removedPixels > 0;

        DigResult result = new DigResult
        {
            WasModified = modified,
            RawMinX = rawMinX,
            RawMaxX = rawMaxX,
            RawMinY = rawMinY,
            RawMaxY = rawMaxY,
            ClampedMinX = minX,
            ClampedMaxX = maxX,
            ClampedMinY = minY,
            ClampedMaxY = maxY,
            RadiusPx = radiusPx,
            RemovedPixels = removedPixels
        };
        
        //Debug.Log($"[테][TerrainModifier] Dig Result - Modified:{modified}. RawBounds(X:{rawMinX}~{rawMaxX}, Y:{rawMinY}~{rawMaxY}). ClampedBounds(X:{minX}~{maxX}, Y:{minY}~{maxY})");
        
        if (modified)
        {
            data.MarkDirty();
        }
        
        return result;
    }

    /// <summary>
    /// Digs a perfectly circular hole in the terrain at the specified world position (for explosions).
    /// </summary>
    public DigResult Explode(ChunkData data, Vector2 explosionWorldPos, float radius,
                             System.Func<Vector2, Vector2Int> worldToPixelConverter, 
                             NeighborContext neighbors = default,
                             ParticleSpawnCallback particleCallback = null,
                             PixelToWorldCallback pixelToWorld = null)
    {
        Vector2Int centerPixel = worldToPixelConverter(explosionWorldPos);
        int centerPx = centerPixel.x;
        int centerPy = centerPixel.y;
        
        float radiusPx = radius * PixelsPerUnit;
        
        // Calculate bounds
        int maxRadiusPx = Mathf.CeilToInt(radiusPx);
        int margin = maxRadiusPx + DIG_MARGIN_EXTRA;
        
        int rawMinX = centerPx - margin;
        int rawMaxX = centerPx + margin;
        int rawMinY = centerPy - margin;
        int rawMaxY = centerPy + margin;
        
        int minX = Mathf.Clamp(rawMinX, 0, data.Width);
        int maxX = Mathf.Clamp(rawMaxX, 0, data.Width);
        int minY = Mathf.Clamp(rawMinY, 0, data.Height);
        int maxY = Mathf.Clamp(rawMaxY, 0, data.Height);
        
        float sqrRadius = radiusPx * radiusPx;
        bool pixelChanged = false;
        
        for (int y = minY; y < maxY; y++)
        {
            float dy = y - centerPy;
            for (int x = minX; x < maxX; x++)
            {
                int index = data.ToIndex(x, y);
                if (!data.IsValid(x, y) || data.BasePixels[index].a == 0) continue;

                // 파기 불가 픽셀 스킵 (IndestructibleOverlay)
                if (data.IndestructibleMask.IsCreated && data.IndestructibleMask[index] != 0) continue;

                float dx = x - centerPx;
                float distSqr = (dx * dx) + (dy * dy);

                if (distSqr <= sqrRadius)
                {
                    Color32 debrisColor = data.BasePixels[index];
                    data.BasePixels[index] = new Color32(0, 0, 0, 0);
                    data.PixelInfo[index] = 0;
                    pixelChanged = true;

                    if (pixelToWorld != null)
                    {
                        Vector2 worldPos = pixelToWorld(x, y);
                        particleCallback?.Invoke(worldPos, debrisColor);
                        OnPixelDestroyed?.Invoke(worldPos, debrisColor);
                    }
                }
            }
        }
        
        DigResult result = new DigResult
        {
            WasModified = pixelChanged,
            RawMinX = rawMinX, RawMaxX = rawMaxX, RawMinY = rawMinY, RawMaxY = rawMaxY,
            ClampedMinX = minX, ClampedMaxX = maxX, ClampedMinY = minY, ClampedMaxY = maxY,
            RadiusPx = radiusPx
        };
        
        if (pixelChanged)
        {
            data.MarkDirty();
        }
        
        return result;
    }
    
    /// <summary>
    /// Result of a dig operation, includes bounds for visual updates.
    /// </summary>
    /// <summary>
    /// 섬 제거가 지운 영역. 파기 반경과 **무관하게** 넓을 수 있어서 따로 돌려준다.
    /// Removed 가 false 면 나머지 값은 의미 없다.
    /// </summary>
    public struct IslandRemovalResult
    {
        public bool Removed;
        public int MinX, MinY, MaxX, MaxY;   // [Min, Max) — Max 는 배타적
    }

    public struct DigResult
    {
        public bool WasModified;
        public int RawMinX, RawMaxX, RawMinY, RawMaxY;      // Raw bounds (may exceed chunk)
        public int ClampedMinX, ClampedMaxX, ClampedMinY, ClampedMaxY;  // Clamped to chunk
        public float RadiusPx;
        /// <summary>
        /// 이번 파기로 실제 제거된 픽셀 수. 밸런스 텔레메트리용(balance-csv-design.md §7).
        /// Explode() 경로는 세우지 않는다 — 폭발은 플레이어가 판 것이 아니다.
        /// </summary>
        public int RemovedPixels;
    }

    // ============================================================================================================
    //  PRIVATE HELPERS: PIXEL PROCESSING
    // ============================================================================================================
    
    private int ProcessDigPixels(ChunkData data, int minX, int minY, int maxX, int maxY,
                                   int centerPx, int centerPy, float radiusPx, float angle, int toolIndex,
                                   ParticleSpawnCallback particleCallback, PixelToWorldCallback pixelToWorld)
    {
        float cos = Mathf.Cos(-angle);
        float sin = Mathf.Sin(-angle);
        float sqrRadius = radiusPx * radiusPx;

        // 마스크 분기. 샘플러를 못 얻으면(마스크 없음·반경 0) 아래 타원 판정으로 그대로 떨어진다.
        // 단축 평가로 TryGetSampler가 호출되지 않는 경로가 있어(삽이 아닌 도구),
        // 확정 대입을 위해 default로 먼저 초기화한다. 이 값은 useMask가 false일 때만 남고
        // 그 경우 Contains는 호출되지 않는다.
        ShovelDigMask.Sampler maskSampler = default;
        bool useMask = (toolIndex == SHOVEL_TOOL_INDEX)
                       && ShovelDigMask.TryGetSampler(radiusPx, out maskSampler);
        // 돌(pixelType 2)은 마스크 경로에서도 중심 근처에서만 파인다 — 타원 경로의 규칙과 같다.
        float rockLimitPx = radiusPx * ROCK_DIG_RADIUS_RATIO;
        float sqrRockLimit = rockLimitPx * rockLimitPx;

        int removed = 0;

        for (int y = minY; y < maxY; y++)
        {
            float dy = y - centerPy;

            for (int x = minX; x < maxX; x++)
            {
                int index = data.ToIndex(x, y);

                // Skip if already air
                if (data.BasePixels[index].a == 0) continue;

                // 파기 불가 픽셀 스킵 (IndestructibleOverlay)
                if (data.IndestructibleMask.IsCreated && data.IndestructibleMask[index] != 0) continue;

                // indestructible 인접 픽셀 보호 (1픽셀 버퍼)
                if (data.HasIndestructiblePixels && IsAdjacentToIndestructible(data, x, y)) continue;

                // Transform to local dig space (rotated ellipse)
                // CenterPx is ALREADY the geometric center of the dig hole!
                float dx = x - centerPx;

                // Rotation only. Translation is already done.
                float localX = dx * cos - dy * sin;
                float localY = dx * sin + dy * cos;

                byte pixelType = data.PixelInfo[index]; // 1=Dirt, 2=Rock

                if (useMask)
                {
                    // --- 마스크 판정 ---
                    // VerticalScale(전방 늘림)은 걸지 않는다. 모양은 이미지가 정의한다.
                    if (!maskSampler.Contains(localX, localY)) continue;

                    // Shovel can't dig rock efficiently
                    if (pixelType == 2)
                    {
                        float distSqrFromCenter = (localX * localX) + (localY * localY);
                        if (distSqrFromCenter > sqrRockLimit) continue;
                    }
                }
                else
                {
                    // --- 기존 타원 판정 (원본 그대로) ---
                    // Apply vertical scaling (ellipse)
                    float currentScale = (localX >= 0) ? VerticalScale : 1.0f;
                    float localXScaled = localX / currentScale;
                    float distSqr = (localXScaled * localXScaled) + (localY * localY);

                    if (distSqr > sqrRadius) continue;

                    // Shovel can't dig rock efficiently
                    if (pixelType == 2 && toolIndex == SHOVEL_TOOL_INDEX)
                    {
                        // Only dig at center (very small area)
                        if (distSqr > sqrRadius * ROCK_DIG_THRESHOLD) continue;
                    }
                }

                // Save debris color for particle
                Color32 debrisColor = data.BasePixels[index];

                // Remove pixel
                data.BasePixels[index] = new Color32(0, 0, 0, 0);
                data.PixelInfo[index] = 0;
                removed++;

                // Spawn debris particle via callback + fire destroy event
                if (pixelToWorld != null)
                {
                    Vector2 worldPos = pixelToWorld(x, y);
                    particleCallback?.Invoke(worldPos, debrisColor);
                    OnPixelDestroyed?.Invoke(worldPos, debrisColor);
                }
            }
        }

        return removed;
    }

    // ============================================================================================================
    //  PUBLIC API: ISLAND REMOVAL
    // ============================================================================================================
    
    /// <summary>
    /// 지정 영역 내 공중 섬을 감지하고 제거합니다.
    /// byte[] 플랫 배열 BFS로 HashSet 대비 캐시 효율 대폭 향상.
    /// </summary>
    public IslandRemovalResult CheckFloatingIslandsInArea(ChunkData data, int minX, int minY, int maxX, int maxY,
                                           NeighborContext neighbors = default,
                                           PixelToWorldCallback pixelToWorld = null,
                                           ParticleSpawnCallback particleCallback = null)
    {
        _islandRemovedAny = false;
        _islandMinX = int.MaxValue; _islandMinY = int.MaxValue;
        _islandMaxX = int.MinValue; _islandMaxY = int.MinValue;

        if (!UseIslandRemoval) return default;

        // 버퍼 미초기화 시 자동 초기화 (안전망)
        if (_pixelState == null) InitializeIslandBuffers(data.Width, data.Height);

        minX = Mathf.Clamp(minX, 0, data.Width);
        maxX = Mathf.Clamp(maxX, 0, data.Width);
        minY = Mathf.Clamp(minY, 0, data.Height);
        maxY = Mathf.Clamp(maxY, 0, data.Height);

        // 이전 호출에서 방문한 픽셀만 선택적으로 리셋 (Array.Clear 1MB 전체 초기화 회피)
        for (int i = 0; i < _visitedIndices.Count; i++)
            _pixelState[_visitedIndices[i]] = 0;
        _visitedIndices.Clear();

        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                int index = data.ToIndex(x, y);

                // solid이고 아직 안전지대(2)로 확정되지 않은 픽셀만 처리
                if (data.BasePixels[index].a != 0 && _pixelState[index] != 2)
                {
                    ProcessIsland(data, x, y, neighbors, pixelToWorld, particleCallback);
                }
            }
        }

        if (!_islandRemovedAny) return default;

        return new IslandRemovalResult
        {
            Removed = true,
            MinX = _islandMinX,
            MinY = _islandMinY,
            MaxX = _islandMaxX + 1,   // 배타적으로 맞춘다
            MaxY = _islandMaxY + 1,
        };
    }
    
    /// <summary>
    /// 시작 픽셀에서 DFS로 클러스터를 탐색하고, 공중 섬이면 제거합니다.
    /// flat index 기반 처리로 PixelNode 구조체 박싱/언박싱 제거.
    /// </summary>
    private void ProcessIsland(ChunkData data, int startX, int startY,
                               NeighborContext neighbors,
                               PixelToWorldCallback pixelToWorld,
                               ParticleSpawnCallback particleCallback)
    {
        _searchStack.Clear();
        _currentCluster.Clear();

        // 시작 픽셀 추가
        int startIndex = data.ToIndex(startX, startY);
        _pixelState[startIndex] = 1;
        _searchStack.Add(startIndex);
        _currentCluster.Add(startIndex);

        int count = 0;
        bool isConnectedToGround = false;
        int width = data.Width;

        while (_searchStack.Count > 0)
        {
            int last = _searchStack.Count - 1;
            int idx = _searchStack[last];
            _searchStack.RemoveAt(last);
            count++;

            // MaxIslandSize 초과 → 큰 덩어리는 ground로 간주
            if (count > MaxIslandSize)
            {
                isConnectedToGround = true;
                // 스택에 남은 픽셀도 cluster에 추가해 safe 마킹 누락 방지
                for (int i = 0; i < _searchStack.Count; i++)
                    _currentCluster.Add(_searchStack[i]);
                _searchStack.Clear();
                break;
            }

            // flat index → (x, y) 디코딩
            int x = idx % width;
            int y = idx / width;

            // 4방향 이웃 검사
            if (CheckNeighborFlat(data, neighbors, x + 1, y) ||
                CheckNeighborFlat(data, neighbors, x - 1, y) ||
                CheckNeighborFlat(data, neighbors, x, y + 1) ||
                CheckNeighborFlat(data, neighbors, x, y - 1))
            {
                isConnectedToGround = true;
                break;
            }
        }

        if (isConnectedToGround)
        {
            // 클러스터 전체를 안전지대(2)로 마킹
            foreach (int idx in _currentCluster)
                _pixelState[idx] = 2;
        }
        else
        {
            // 큰 섬일수록 샘플 간격을 벌려 파티클 총량을 MAX_ISLAND_PARTICLES 이하로 유지한다.
            // 작은 섬(≈2048px 이하)은 기존과 동일하게 8픽셀당 1개.
            int sampleRate = Mathf.Max(
                PARTICLE_SAMPLE_RATE,
                Mathf.CeilToInt((float)_currentCluster.Count / MAX_ISLAND_PARTICLES));

            int particleSampleCounter = 0;
            foreach (int idx in _currentCluster)
            {
                Color32 color = data.BasePixels[idx];
                data.BasePixels[idx] = new Color32(0, 0, 0, 0);
                data.PixelInfo[idx] = 0;

                // 지운 범위 누적. 플러드필은 호출 렉트를 **씨앗으로만** 쓰고 그 밖까지 뻗으므로,
                // 호출부가 파기 렉트만 더티로 잡으면 지운 자리의 테두리·콜라이더가 낡은 채 남는다.
                {
                    int bx = idx % width;
                    int by = idx / width;
                    if (bx < _islandMinX) _islandMinX = bx;
                    if (bx > _islandMaxX) _islandMaxX = bx;
                    if (by < _islandMinY) _islandMinY = by;
                    if (by > _islandMaxY) _islandMaxY = by;
                }
                _islandRemovedAny = true;

                // sampleRate 픽셀당 1개 파티클 (대규모 섬 제거 시 콜백 폭증 방지)
                if (particleCallback != null && pixelToWorld != null &&
                    ++particleSampleCounter % sampleRate == 0)
                {
                    int px = idx % width;
                    int py = idx / width;
                    particleCallback(pixelToWorld(px, py), color);
                }
            }

            data.MarkDirty();
        }

        // 다음 호출에서 선택적 리셋이 가능하도록 방문한 픽셀 인덱스 누적
        _visitedIndices.AddRange(_currentCluster);
    }

    /// <summary>
    /// 이웃 픽셀의 연결성을 검사합니다. ground 연결이면 true 반환.
    /// _pixelState 배열 직접 접근으로 HashSet.Contains() 대비 캐시 효율 향상.
    /// TODO (#3): Cross-chunk island detection not implemented. (RESOLVED: GC-Free NativeArray Wrapping)
    /// </summary>
    private bool CheckNeighborFlat(ChunkData data, NeighborContext neighbors, int x, int y)
    {
        // 청크 경계 밖 래핑 (Cross-Chunk Reference)
        if (x < 0)
        {
            if (neighbors.Left != null)
            {
                int nx = neighbors.Left.Width - 1;
                int indexL = neighbors.Left.ToIndex(nx, y);
                return neighbors.Left.BasePixels[indexL].a != 0;
            }
            return true;
        }
        if (x >= data.Width)
        {
            if (neighbors.Right != null)
            {
                int nx = 0;
                int indexR = neighbors.Right.ToIndex(nx, y);
                return neighbors.Right.BasePixels[indexR].a != 0;
            }
            return true;
        }
        if (y < 0)
        {
            if (neighbors.Bottom != null)
            {
                int ny = neighbors.Bottom.Height - 1;
                int indexB = neighbors.Bottom.ToIndex(x, ny);
                return neighbors.Bottom.BasePixels[indexB].a != 0;
            }
            return true;
        }
        if (y >= data.Height)
        {
            if (neighbors.Top != null)
            {
                int ny = 0;
                int indexT = neighbors.Top.ToIndex(x, ny);
                return neighbors.Top.BasePixels[indexT].a != 0;
            }
            return true;
        }

        int index = data.ToIndex(x, y);

        // 빈 픽셀 = 연결 없음
        if (data.BasePixels[index].a == 0) return false;

        // 이미 안전지대(2) = ground에 연결됨
        if (_pixelState[index] == 2) return true;

        // 미방문(0) solid 픽셀 = 탐색 스택에 추가
        if (_pixelState[index] == 0)
        {
            _pixelState[index] = 1;
            _searchStack.Add(index);
            _currentCluster.Add(index);
        }

        return false;
    }



    // ============================================================================================================
    //  PUBLIC API: NARROW PROTRUSION REMOVAL
    // ============================================================================================================

    /// <summary>
    /// 원형 파기 구멍들이 맞닿을 때 생기는 좁고 뾰족한 흙 돌기를 제거한다.
    /// 픽셀 기준 4방향(수평·수직·대각 2개)으로 너비를 측정해
    /// 가장 좁은 방향의 너비가 narrowThreshold 이하이면 제거한다.
    /// Dig() 호출 후, CheckFloatingIslands() 이전에 호출할 것.
    /// </summary>
    /// <param name="narrowThreshold">제거 기준 너비(픽셀). 이 값 이하 너비이면 돌기로 판단 (기본: 6)</param>
    public void RemoveNarrowProtrusions(ChunkData data, int minX, int minY, int maxX, int maxY, int narrowThreshold = 6)
    {
        // 경계 마진: ScanSolidWidth가 청크 경계에서 멈추면 이웃으로 이어진 넓은 지형도
        // "좁은 돌기"로 오판해 깎인다. scan 거리(narrowThreshold)만큼 안쪽 픽셀만 처리해
        // 경계 너머를 참조하지 않게 한다. (경계 1줄의 돌기는 의도적으로 깎지 않음)
        int margin = narrowThreshold;
        minX = Mathf.Clamp(minX, margin, Mathf.Max(margin, data.Width  - margin));
        maxX = Mathf.Clamp(maxX, margin, Mathf.Max(margin, data.Width  - margin));
        minY = Mathf.Clamp(minY, margin, Mathf.Max(margin, data.Height - margin));
        maxY = Mathf.Clamp(maxY, margin, Mathf.Max(margin, data.Height - margin));

        _narrowRemoveList.Clear();

        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                int idx = data.ToIndex(x, y);
                if (data.BasePixels[idx].a == 0) continue;                // 이미 빈 픽셀

                // 파기 불가 픽셀 보호
                if (data.IndestructibleMask.IsCreated && data.IndestructibleMask[idx] != 0) continue;
                if (data.HasIndestructiblePixels && IsAdjacentToIndestructible(data, x, y)) continue;

                // 4방향으로 너비 측정 (현재 픽셀 포함)
                int widthH  = ScanSolidWidth(data, x, y,  1,  0, -1,  0, narrowThreshold); // 수평
                int widthV  = ScanSolidWidth(data, x, y,  0,  1,  0, -1, narrowThreshold); // 수직
                int widthD1 = ScanSolidWidth(data, x, y,  1,  1, -1, -1, narrowThreshold); // 대각 ↗↙
                int widthD2 = ScanSolidWidth(data, x, y,  1, -1, -1,  1, narrowThreshold); // 대각 ↘↖

                int minWidth = Mathf.Min(widthH, Mathf.Min(widthV, Mathf.Min(widthD1, widthD2)));

                if (minWidth <= narrowThreshold)
                    _narrowRemoveList.Add(idx);
            }
        }

        // 2패스: 마킹된 픽셀 일괄 제거 (스캔 중 상태가 오염되지 않도록)
        foreach (int idx in _narrowRemoveList)
        {
            data.BasePixels[idx] = new Color32(0, 0, 0, 0);
            data.PixelInfo[idx] = 0;
        }
    }

    /// <summary>
    /// 지정 픽셀에서 두 방향(dx1/dy1, dx2/dy2)으로 solid 픽셀 수를 세어 합산한 너비를 반환.
    /// 반환값 = 방향1 solid 연속 수 + 1(현재) + 방향2 solid 연속 수.
    /// maxScan 이상이면 넓은 지형으로 간주해 maxScan*2+1을 반환한다.
    /// </summary>
    private int ScanSolidWidth(ChunkData data, int x, int y, int dx1, int dy1, int dx2, int dy2, int maxScan)
    {
        int dist1 = 0;
        for (int k = 1; k <= maxScan; k++)
        {
            int nx = x + dx1 * k;
            int ny = y + dy1 * k;
            if (!data.IsValid(nx, ny) || data.BasePixels[data.ToIndex(nx, ny)].a == 0) break;
            dist1++;
        }

        int dist2 = 0;
        for (int k = 1; k <= maxScan; k++)
        {
            int nx = x + dx2 * k;
            int ny = y + dy2 * k;
            if (!data.IsValid(nx, ny) || data.BasePixels[data.ToIndex(nx, ny)].a == 0) break;
            dist2++;
        }

        return dist1 + 1 + dist2;
    }

    // ============================================================================================================
    //  PUBLIC API: EDGE EROSION (SMOOTHING)
    // ============================================================================================================

    /// <summary>
    /// 파기 직후 구멍 가장자리의 고립 픽셀을 CA(Cellular Automata) 이웃 카운팅으로 제거해 구멍을 부드럽게 만든다.
    /// Dig() 호출 후, CheckFloatingIslands() 이전에 호출할 것.
    /// </summary>
    /// <param name="airThreshold">
    /// 8방향 이웃 중 빈 픽셀 수가 이 값 이상이면 제거 (기본: 5).
    /// 낮을수록 강하게 깎임 (3=공격적, 5=보통, 7=보수적).
    /// </param>
    public void ErodeEdges(ChunkData data, int minX, int minY, int maxX, int maxY, int airThreshold = 5)
    {
        // 경계 마진: 8방향 이웃 카운팅이 청크 경계 밖을 solid로 오인해 경계 픽셀을
        // 잘못 깎는다. 1px 안쪽 픽셀만 처리해 경계 너머를 참조하지 않게 한다.
        const int margin = 1;
        minX = Mathf.Clamp(minX, margin, Mathf.Max(margin, data.Width  - margin));
        maxX = Mathf.Clamp(maxX, margin, Mathf.Max(margin, data.Width  - margin));
        minY = Mathf.Clamp(minY, margin, Mathf.Max(margin, data.Height - margin));
        maxY = Mathf.Clamp(maxY, margin, Mathf.Max(margin, data.Height - margin));

        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                int idx = data.ToIndex(x, y);
                if (data.BasePixels[idx].a == 0) continue;

                // 파기 불가 픽셀 보호
                if (data.IndestructibleMask.IsCreated && data.IndestructibleMask[idx] != 0) continue;
                if (data.HasIndestructiblePixels && IsAdjacentToIndestructible(data, x, y)) continue;

                // 8방향 이웃 중 빈 픽셀 수 카운팅
                int airCount = 0;
                if (IsAirForErosion(data, x + 1, y))     airCount++;
                if (IsAirForErosion(data, x - 1, y))     airCount++;
                if (IsAirForErosion(data, x, y + 1))     airCount++;
                if (IsAirForErosion(data, x, y - 1))     airCount++;
                if (IsAirForErosion(data, x + 1, y + 1)) airCount++;
                if (IsAirForErosion(data, x - 1, y + 1)) airCount++;
                if (IsAirForErosion(data, x + 1, y - 1)) airCount++;
                if (IsAirForErosion(data, x - 1, y - 1)) airCount++;

                if (airCount >= airThreshold)
                {
                    data.BasePixels[idx] = new Color32(0, 0, 0, 0);
                    data.PixelInfo[idx] = 0;
                }
            }
        }
    }

    /// <summary>
    /// 침식 판별용: 해당 좌표가 빈 픽셀이면 true.
    /// 청크 경계 밖은 보수적으로 solid(false) 취급해 경계 픽셀이 과침식되지 않도록 한다.
    /// </summary>
    private bool IsAirForErosion(ChunkData data, int x, int y)
    {
        if (!data.IsValid(x, y)) return false; // 경계 밖 = solid 취급
        return data.BasePixels[data.ToIndex(x, y)].a == 0;
    }

    // ============================================================================================================
    //  PUBLIC API: PIXEL REMOVAL (UTILITY)
    // ============================================================================================================
    
    /// <summary>
    /// Removes a single pixel at the specified coordinate.
    /// </summary>
    public void RemovePixel(ChunkData data, int x, int y)
    {
        if (!data.IsValid(x, y)) return;

        int idx = data.ToIndex(x, y);
        data.BasePixels[idx] = new Color32(0, 0, 0, 0);
        data.PixelInfo[idx] = 0;

        data.MarkDirty();
    }

    /// <summary>
    /// 8방향 이웃 픽셀 중 IndestructibleMask=1인 픽셀이 있으면 true.
    /// indestructible 주변 1픽셀 버퍼 보호에 사용.
    /// </summary>
    private bool IsAdjacentToIndestructible(ChunkData data, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (!data.IsValid(nx, ny)) continue;
                if (data.IndestructibleMask[data.ToIndex(nx, ny)] != 0) return true;
            }
        }
        return false;
    }
}