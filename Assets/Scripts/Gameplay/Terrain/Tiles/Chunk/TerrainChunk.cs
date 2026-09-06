// @tags: chunk, terrain-chunk, orchestrator, digging, visual, collider, dirty-flag, lateupdate, reuse, border, pixel-data, ichunk, save, restore
using UnityEngine;
using UnityEngine.Serialization;
using Unity.Collections;
using System.Collections.Generic;

/// <summary>
/// REFACTORED VERSION: Main controller/orchestrator for a terrain chunk.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
// [Refactored] Use sub-colliders instead of single component
// [RequireComponent(typeof(PolygonCollider2D))]
public class TerrainChunk : MonoBehaviour, ITerrainModifiable, IChunk
{
    // ============================================================================================================
    //  COMPOSITION: SUB-MODULES
    // ============================================================================================================
    private ChunkData _data;
    private TerrainModifier _modifier;
    
    // [Refactored] Expose Modifier for advanced usage (Decorator)
    public TerrainModifier Modifier => _modifier;
    public TerrainVisualizer Visualizer => _visualizer;
    public ChunkData Data => _data; // Exposed for Cross-Chunk Island Detection

    /// <summary>진단용. TerrainSeamWatchdog가 콜라이더 갱신 이력을 읽는다.</summary>
    public TerrainCollider ColliderManager => _colliderManager;

    // [Refactored] Implement ITerrainModifiable
    public void ModifyTerrain(Vector2 worldPos, float radius, int toolIndex)
    {
        Dig(worldPos, radius, toolIndex);
    }
    
    public void Carve(Vector2 worldPos, Color32[] mask, int width, int height, Vector2 pivot)
    {
        Vector2Int pixelPos = WorldToPixel(worldPos);
        TerrainCarver.CarveHole(this, pixelPos.x, pixelPos.y, mask, width, height, pivot);
        
        // Mark as dirty to trigger visual update
        if (_data != null)
        {
            _data.MarkDirty();
        }
    }
    
    private TerrainVisualizer _visualizer;
    private TerrainCollider _colliderManager;

    // ─── [Cave] 묻어둔 굴 ──────────────────────────────────────────────────
    // 굴은 좌표의 순수 함수라 마스크(청크당 1MB)를 들고 있을 필요가 없다. 판정식만 캐시한다.
    private CaveCarveSettings _caveSettings;
    private int  _caveWorldSeed;
    private bool _caveRevealed;

    /// <summary>
    /// 카빙 잡이 스케줄됐고 아직 Complete() 를 안 물었다.
    ///
    /// hiddenUntilExposed 이후로 카빙은 로드 때뿐 아니라 **플레이어가 파다 굴에 닿는 아무 순간**에도
    /// 시작된다. 그동안 메인 스레드가 BasePixels 를 읽으면
    /// "CarveCavePixelsJob writes to baseData. You must call JobHandle.Complete()" 로 터진다
    /// (JobHandle 이 끝나 있어도 AtomicSafetyHandle 은 Complete() 로만 풀린다).
    /// 읽기 경로가 IsTransparent(미니맵·지도캐시·DigPathTracker)와 콜라이더로 흩어져 있어서,
    /// 각자 부르지 않고 이 플래그 하나로 좁혀 둔다.
    /// </summary>
    private bool _carvePending;
    private TerrainJobs.CaveCapsuleGeometry _caveGeo;
    private bool _caveGeoBuilt;

    /// <summary>이 청크의 굴이 이미 열렸는지. 이웃이 경계 노출을 판정할 때 본다.</summary>
    public bool CaveRevealed => _caveRevealed;

    // ============================================================================================================
    //  CONFIGURATION
    // ============================================================================================================
    [Header("Chunk Settings")]
    private int _width = 1000;
    private int _height = 1000;
    [SerializeField, FormerlySerializedAs("pixelsPerUnit")] private float _pixelsPerUnit = 100f;
    public int width => _width;
    public int height => _height;
    public float pixelsPerUnit => _pixelsPerUnit;

    [Header("Player Interaction")]
    [SerializeField, FormerlySerializedAs("player")] private Transform _player;
    public Transform player { get => _player; set => _player = value; }
    [SerializeField] private float verticalScale = 1.5f;

    /// <summary>
    /// 공중 섬 제거 설정. static 인 이유는 Instantiate 가 non-serialized 필드를 프리팹에서
    /// 복사하지 않기 때문 — s_colliderUpdateInterval 과 같은 패턴.
    /// worldSettings.json 의 chunk.useIslandRemoval / chunk.maxIslandSize 로 덮어쓴다.
    ///
    /// s_maxIslandSize = 판정 면적 상한(px). 100 PPU 기준 10000px = 1×1 유닛, 90000px = 3×3 유닛.
    /// 이 값보다 큰 덩어리는 '땅'으로 간주해 남긴다 → 값을 올릴수록 더 큰 공중 섬까지 무너진다.
    /// 비용: 최악의 경우(경계에 닿지 않는 거대 덩어리) 파기 1회당 이 픽셀 수만큼 DFS 방문.
    /// </summary>
    private static bool s_useIslandRemoval = true;
    private static int s_maxIslandSize = 90000;

    /// <summary>worldSettings.json 값을 전체 청크에 적용한다. 청크 생성 전에 호출할 것.</summary>
    public static void SetDefaultIslandRemoval(bool enabled, int maxIslandSize)
    {
        s_useIslandRemoval = enabled;
        s_maxIslandSize = Mathf.Max(1, maxIslandSize);
    }

    [Header("Visual Settings")]
    [SerializeField] private float _textureThickness = 4.0f;
    public float textureThickness => _textureThickness;

    [Header("Border Textures")]
    [SerializeField] private Texture2D _borderTexture;
    [SerializeField] private Texture2D _secondaryBorderTexture;
    public Texture2D borderTexture { get => _borderTexture; set => _borderTexture = value; }
    public Texture2D secondaryBorderTexture { get => _secondaryBorderTexture; set => _secondaryBorderTexture = value; }

    [Header("Performance")]
    /// <summary>
    /// Minimum time (seconds) between collider updates to prevent frame drops.
    /// Collider updates are expensive (full pixel scan + Moore-Neighbor Tracing).
    /// Default: 0.2s = max 5 collider updates/second per chunk.
    /// worldSettings.json의 chunk.colliderUpdateInterval로 전체 덮어쓰기 가능.
    /// </summary>
    private static float s_colliderUpdateInterval = 0.2f;

    /// <summary>
    /// [rim] 공기·파괴불가 픽셀과 맞닿는 최외곽 라인 두께(px). 0 이면 비활성.
    /// worldSettings.json 의 chunk.rimThicknessPx / chunk.rimColor 로 덮어쓴다.
    /// non-serialized 필드는 Instantiate 시 프리팹에서 복사되지 않으므로 static 이어야 한다.
    /// 설계: Assets/Docs/terrain-rim-outline.md
    /// </summary>
    private static int     s_rimThicknessPx = 2;
    private static Color32 s_rimColor       = new Color32(0x24, 0x10, 0x09, 255);

    public static int     RimThicknessPx => s_rimThicknessPx;
    public static Color32 RimColor       => s_rimColor;

    public static void SetRimSettings(int thicknessPx, Color32 color)
    {
        s_rimThicknessPx = Mathf.Max(0, thicknessPx);
        s_rimColor       = color;
    }

    private static byte[] s_clearBuffer;

    // true 이면 VisualJob이 DistanceField 등고선을 회색조 밴드로 출력 (Case A/B 진단용).
    // 런타임 토글: TerrainChunk.DebugDistanceField = true; 후 청크를 재파기하면 반영됨.
    public static bool DebugDistanceField = false;

    // [Refactoring] isStaticSpecialChunk removed entirely. Use LargeStaticTerrainChunk instead.

    // IChunk 인터페이스 구현
    public Vector2Int Coord => new Vector2Int(ChunkX, ChunkY);
    public int Width  => _width;
    public int Height => _height;

    // IChunk — OCP: SpecialChunkManager가 타입 검사 없이 호출
    // TerrainChunk는 transform.position 기반으로 Coord를 계산하므로 OnSpawned에서 별도 초기화 불필요.
    public void OnSpawned(Vector2Int coord) { }
    public bool NeedsDelayedActivation => true;

    // 특수청크 앵커 여부. SpecialChunkManager가 스폰 시 true로 마킹한다.
    // 저장 분류(wasNormalChunk)와 언로드 시 Destroy(특수)/풀 반납(일반) 판단에 쓰인다.
    // 루트 GetComponent<IChunkInitializer>() 판별은 가마솥·던전문처럼 자식에 스크립트를 둔
    // 특수청크를 놓쳐 오분류하므로, 이 명시적 플래그를 사용한다.
    // 특수청크는 Instantiate 후 언로드 시 Destroy되어 풀에 반납되지 않으므로 리셋이 필요 없다.
    public bool IsSpecialChunkInstance;

    // #5 [Fix] Encapsulated rock lists — external access via Add/Clear methods.
    private readonly List<Rect> _generatedRockBounds = new List<Rect>();
    private readonly List<DiggableRock> _spawnedRocks = new List<DiggableRock>();
    public IReadOnlyList<Rect> GeneratedRockBounds => _generatedRockBounds;
    public IReadOnlyList<DiggableRock> SpawnedRocks => _spawnedRocks;
    public void AddRockBound(Rect bound) => _generatedRockBounds.Add(bound);
    public void AddSpawnedRock(DiggableRock rock) => _spawnedRocks.Add(rock);
    public void RemoveSpawnedRock(DiggableRock rock)
    {
        int idx = _spawnedRocks.IndexOf(rock);
        if (idx < 0) return;
        int last = _spawnedRocks.Count - 1;
        _spawnedRocks[idx] = _spawnedRocks[last];
        _spawnedRocks.RemoveAt(last);
    }
    public void ClearRockBounds() => _generatedRockBounds.Clear();
    public void ClearSpawnedRocks() => _spawnedRocks.Clear();

    // 이 청크에 엘리베이터가 점유한 픽셀 영역. ElevatorDecorator가 세팅한다.
    // 일반 데코 경로는 DecorationContext.PreOccupiedAreas로 광물 겹침을 막지만,
    // 그 컨텍스트를 공유하지 않는 경로(DecorateMineralsOnly)는 여기서 읽어야 한다.
    // 엘리베이터가 없는 청크는 null.
    public Rect? ElevatorArea { get; private set; }
    public void SetElevatorArea(Rect area) => ElevatorArea = area;

    // [Cavity Reveal] 이 청크가 "묻혔다가 파면 드러나는" 특수 공동 청크면 컨트롤러가 등록된다.
    // 없으면 null — 일반 청크는 영향 없음.
    private CavityRevealController _cavityReveal;
    public void RegisterCavityReveal(CavityRevealController controller) => _cavityReveal = controller;

    // [Carve 예약] 지금은 solid지만 나중에 air로 파일 예정인 픽셀 마스크(공동 등).
    // 광물 생성은 이 픽셀을 지형으로 보지 않는다 — 안 그러면 carve 후 광물만 허공에 남는다.
    // 소유권은 등록자(CavityRevealController)에 있고, 여기선 참조만 들고 읽는다.
    private bool[] _carveReservedMask;
    public void SetCarveReservedMask(bool[] mask) => _carveReservedMask = mask;
    public void ClearCarveReservedMask() => _carveReservedMask = null;
    public bool IsCarveReserved(int x, int y)
    {
        if (_carveReservedMask == null) return false;
        int idx = y * _width + x;
        return (uint)idx < (uint)_carveReservedMask.Length && _carveReservedMask[idx];
    }

    // [Save] 재로드 시 RockDecorator가 한 번 읽고 소비하는 임시 저장 암석 배치
    private RockSaveEntry[] _pendingSavedRocks;
    /// <summary>저장된 암석 배치를 꺼내고 필드를 null로 초기화합니다 (한 번만 사용).</summary>
    public RockSaveEntry[] GetAndClearSavedRocks()
    {
        var tmp = _pendingSavedRocks;
        _pendingSavedRocks = null;
        return tmp;
    }

    // ============================================================================================================
    //  UNITY COMPONENTS
    // ============================================================================================================
    private SpriteRenderer _spriteRenderer;
    private ChunkBackground _background;
    private PolygonCollider2D _polyCollider;
    private Texture2D _mainTexture;

    // ============================================================================================================
    //  PROXIES
    // ============================================================================================================
    public NativeArray<Color32> baseData => _data?.BasePixels ?? default;
    public NativeArray<byte> pixelInfo => _data?.PixelInfo ?? default;
    // #6 [Fix] Changed from property to method — GetRawTextureData() is a GPU pointer access,
    // calling it as a property on every access is misleading and potentially expensive.
    public NativeArray<Color32> GetRawTexture() => _mainTexture?.GetRawTextureData<Color32>() ?? default;
    
    public NativeArray<Color32> borderData => _data?.BorderData ?? default;
    public NativeArray<Color32> secondaryBorderData => _data?.SecondaryBorderData ?? default;

    public int borderW => _data?.BorderWidth ?? 0;
    public int borderH => _data?.BorderHeight ?? 0;

    public Texture2D texture => _mainTexture;
    public float PPU => pixelsPerUnit;

    public bool isTextureDirty
    {
        get => _data?.IsVisualDirty ?? false;
        set { if (_data != null) _data.IsVisualDirty = value; }
    }
    
    public bool isDirty
    {
        get => _data?.IsColliderDirty ?? false;
        set { if (_data != null) _data.IsColliderDirty = value; }
    }
    
    public bool hasBeenModified => _data?.HasBeenModified ?? false;

    public Vector2 GetWorldPos(int x, int y) => PixelToWorldPos(x, y);
    
    // ============================================================================================================
    //  THROTTLING STATE
    // ============================================================================================================
    private float _lastColliderUpdateTime = -999f; // Initialize to far past

    // [Fix] 멀티청크(4000px wide 등)에서 width/pixelsPerUnit = 40이 되어 ChunkX가 잘못 계산되는 버그 수정.
    // 청크 좌표는 항상 표준 청크 크기(ChunkCoords.WorldSize = 10)를 기준으로 계산해야 한다.
    public int ChunkX => Mathf.FloorToInt(transform.position.x / ChunkCoords.WorldSize);
    public int ChunkY => Mathf.FloorToInt(transform.position.y / ChunkCoords.WorldSize);

    // ============================================================================================================
    //  NEIGHBOR REFERENCES - REMOVED (Switched to on-demand lookup)
    // ============================================================================================================
    // [Refactored] No longer storing neighbor references as fields.
    // Neighbors are now looked up on-demand via InfinityMapManager.Instance.GetChunk()
    // with local caching at function scope to prevent repeated dictionary queries.

    // ============================================================================================================
    //  DEPENDENCIES
    // ============================================================================================================
    private IChunkProvider _chunkProvider;
    public IChunkProvider ChunkProvider => _chunkProvider;

    public void SetChunkProvider(IChunkProvider provider)
    {
        _chunkProvider = provider;
    }

    public void FirstTimeInit(int w, int h)
    {
        if (width != w || height != h)
        {
            Debug.LogWarning($"[TerrainChunk] Resizing not supported at runtime.");
        }
    }

    // ============================================================================================================
    //  UNITY COMPONENTS
    // ============================================================================================================
    // [Reverted] Single Collider Management
    // private TerrainCollider[] _subColliders;
    // private RectInt _accumulatedDirtyBounds;

    // ...

    private void Awake()
    {
        // 1. Cache Components
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _polyCollider = GetComponent<PolygonCollider2D>();
        if (_polyCollider == null)
        {
            _polyCollider = gameObject.AddComponent<PolygonCollider2D>();
            Debug.LogWarning($"[TerrainChunk] PolygonCollider2D 누락 → 자동 추가: {name}");
        }

        // 2. Create Model
        // Debug.Log($"[TerrainChunk] CHUNKSIZE | GO={gameObject.name} | CHUNKSIZE _width={_width} _height={_height}");
        _data = new ChunkData(_width, _height);

        // 3. Create Logic
        _modifier = new TerrainModifier(_pixelsPerUnit, verticalScale);
        _modifier.UseIslandRemoval = s_useIslandRemoval;
        _modifier.MaxIslandSize = s_maxIslandSize;
        _modifier.InitializeIslandBuffers(_width, _height);

        // 4. Initialize Texture
        InitializeTextures();

        // 5. Create View (Visualizer)
        _visualizer = new TerrainVisualizer(_data, _mainTexture, _spriteRenderer);
        UpdateVisualizerSettings();

        // 6. Create Physics Manager
        // [Reverted] Single collider manager
        _colliderManager = new TerrainCollider(_data, _polyCollider, _pixelsPerUnit);
        
        // 7. Create Lighting Calculator
        _lightingCalculator = new TerrainLightingCalculator(this);

        // 8. Create Background (층별 배경 — 스프라이트 할당은 Reuse_Step2_Finalize에서)
        _background = new ChunkBackground(transform);
    }

    // ...

    private void LateUpdate()
    {
        // [LateUpdate Pattern] 텍스처 업데이트는 InfinityMapManager.LateUpdate()에서 일괄 처리
        // IsVisualDirty 처리가 필요한 경우(청크 로딩 등)도 매니저가 담당
        
        // Collider 업데이트는 ApplyTexture()에서 처리 (visual과 동기화)
        // LateUpdate에서는 아무것도 하지 않음
    }
    
    private void InitializeTextures()
    {
        _mainTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        _mainTexture.filterMode = FilterMode.Point;
        _mainTexture.wrapMode = TextureWrapMode.Clamp;

        // [Opt-A1] GC 0: static 버퍼를 재사용해 투명(alpha=0)으로 초기화.
        // Reuse_Step1_Prepare()가 즉시 덮어쓰므로 초기 내용은 영향 없음.
        int byteCount = width * height * 4;
        if (s_clearBuffer == null || s_clearBuffer.Length < byteCount)
            s_clearBuffer = new byte[byteCount];
        _mainTexture.LoadRawTextureData(s_clearBuffer);
        _mainTexture.Apply(false);

        // [Opt-A2] FullRect: SpriteMeshGenerator.TraceShape 호출 차단 (~8ms 절감).
        // 지형 청크는 텍스처 전체를 사용하므로 Tight 메시 불필요.
        _spriteRenderer.sprite = Sprite.Create(
            _mainTexture,
            new Rect(0, 0, width, height),
            new Vector2(0f, 0f), pixelsPerUnit,
            0, SpriteMeshType.FullRect);
    }

    private void Start()
    {
        // InitFromPrefab removed.
    }

    /// <summary>
    /// Awake 직후 파이프라인 데이터나 청크 풀링에 사용되는 기본 초기화
    /// </summary>
    // ReinitializeWithSize completely removed.

    // InitFromPrefab completely removed.
    private void OnDestroy()
    {
        if (_visualizer != null) 
        { 
            try 
            {
                _visualizer.Dispose(); 
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TerrainChunk] Error disposing Visualizer: {e.Message}");
            }
            _visualizer = null; 
        }

        if (_data != null) 
        { 
            try
            {
                _data.Dispose(); 
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TerrainChunk] Error disposing ChunkData: {e.Message}");
            }
            // Debug.Log($"[TerrainChunk] Disposed ChunkData for {gameObject.name}");
            _data = null; 
        }

        if (_mainTexture != null) 
        {
            if (Application.isPlaying) Destroy(_mainTexture);
            else DestroyImmediate(_mainTexture);
        }
        
        // Note: PredefinedShape is an asset, do not Destroy it. 
        // Logic for specialized cleanup if needed.
    }


    
    /// <summary>
    /// Check if enough time has passed since last collider update.
    /// Prevents expensive collider calculations from running every frame.
    /// </summary>
    public static void SetDefaultColliderUpdateInterval(float interval)
    {
        s_colliderUpdateInterval = interval;
    }

    /// <summary>
    /// 콜라이더 갱신 스로틀 조건.
    ///
    /// 안전 조건: dashSpeed × colliderUpdateInterval &lt; 드릴 대시 반경(Digger.DrillDashRadius)
    ///   → 플레이어가 colliderUpdateInterval 동안 이동하는 최대 거리가
    ///     ImmediateDig 선행 파기 반경보다 작아야 콜라이더 공백이 생기지 않음.
    ///
    /// 현재 값:
    ///   dashSpeed              = 2.5  unit/s
    ///   colliderUpdateInterval = 0.2  s
    ///   최대 지연 이동 거리      = 0.5  unit
    ///   ImmediateDig 선행 거리  = DrillDashRadius = DrillRadius 0.5 × 2 = 1.0 unit
    ///   여유                    = 0.5 unit  → 현재 값은 안전
    ///
    /// 주의: dashSpeed > 5.0 또는 드릴 대시 반경 &lt; 0.5 이면 재검토 필요.
    /// </summary>
    private bool ShouldUpdateCollider()
    {
        return (Time.time - _lastColliderUpdateTime) >= s_colliderUpdateInterval;
    }

    // ============================================================================================================
    //  NEIGHBOR QUERIES (On-Demand Lookup)
    // ============================================================================================================

    // [Refactored] Query neighbor distance using on-demand lookup
    // No longer relies on cached neighbor fields
    public ushort GetNeighborDistance(int x, int y)
    {
        // 1. Own bounds - direct access
        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            return _data.DistanceField[y * width + x];
        }

        // 2. Determine neighbor chunk coordinate and local coordinate
        Vector2Int neighborCoord = new Vector2Int(ChunkX, ChunkY);
        int nx = x;
        int ny = y;

        // Adjust for horizontal overflow
        if (x < 0)
        {
            neighborCoord.x--;
            nx = width + x; // Wrap to right side of left neighbor
        }
        else if (x >= width)
        {
            neighborCoord.x++;
            nx = x - width; // Wrap to left side of right neighbor
        }

        // Adjust for vertical overflow
        if (y < 0)
        {
            neighborCoord.y--;
            ny = height + y; // Wrap to top side of bottom neighbor
        }
        else if (y >= height)
        {
            neighborCoord.y++;
            ny = y - height; // Wrap to bottom side of top neighbor
        }

        // 2b. 좌표가 여전히 범위 밖이면 (2청크 이상 거리) 안전하게 0 반환
        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
            return 0;

        // 3. Lookup neighbor chunk on-demand
        var neighbor = InfinityMapManager.Instance?.GetChunk(neighborCoord);

        // 4. Query neighbor's distance field
        if (neighbor == null || neighbor._data == null || !neighbor._data.DistanceField.IsCreated)
        {
            return 0; // Treat as air/boundary if neighbor not loaded
        }

        // Chamfer(forward/backward) Job이 DistanceField를 쓰므로 완료 대기.
        // Visual Job은 DistanceField를 읽기만 하므로 대기 불필요 → CompleteLighting()으로 충분.
        neighbor.CompleteLighting();

        int nIdx = neighbor._data.ToIndex(nx, ny);
        return neighbor._data.DistanceField[nIdx];
    }

    /// <summary>
    /// 카빙 잡이 떠 있으면 완료시킨다. BasePixels 를 **메인 스레드에서 읽기 전에** 부를 것.
    ///
    /// 평소엔 bool 검사 한 번이다 — IsTransparent 는 미니맵이 청크당 96x96 으로 부르는 핫패스라
    /// 조건 없이 CompleteCarve() 를 넣으면 안 된다. 실제 Complete() 는 굴이 열린 뒤
    /// 처음 읽는 쪽이 딱 한 번 문다.
    /// </summary>
    private void EnsureCarveApplied()
    {
        if (!_carvePending) return;
        _carvePending = false;
        _visualizer?.CompleteCarve();
    }

    public bool IsTransparent(int x, int y)
    {
        EnsureCarveApplied();

        // 1. Own bounds - check internal pixels
        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            if (!_data.IsValid(x, y)) return true;
            int idx = _data.ToIndex(x, y);
            return _data.BasePixels[idx].a == 0;
        }

        // 2. Out of bounds - lookup neighbor on-demand
        Vector2Int neighborCoord = new Vector2Int(ChunkX, ChunkY);
        int nx = x;
        int ny = y;

        // Adjust coordinates
        if (x < 0) { neighborCoord.x--; nx = width + x; }
        else if (x >= width) { neighborCoord.x++; nx = x - width; }
        
        if (y < 0) { neighborCoord.y--; ny = height + y; }
        else if (y >= height) { neighborCoord.y++; ny = y - height; }

        // 3. Lookup neighbor chunk
        var neighbor = InfinityMapManager.Instance?.GetChunk(neighborCoord);
        
        // 4. If neighbor missing, treat as transparent (air)
        if (neighbor == null) return true;

        return neighbor.GetPixelAlpha(nx, ny) == 0;
    }

    public byte GetPixelAlpha(int x, int y)
    {
        EnsureCarveApplied();
        if (!_data.IsValid(x, y)) return 0;
        int idx = _data.ToIndex(x, y);
        return _data.BasePixels[idx].a;
    }

    public void UpdateVisualizerSettings()
    {
        if (_visualizer == null) return;
        _visualizer.TextureThickness = textureThickness;
        _visualizer.PixelsPerUnit = (int)pixelsPerUnit;
    }

    // ============================================================================================================
    //  REUSE & API
    // ============================================================================================================

    public void Reuse_Step1_Prepare(ChunkInitializationData data)
    {
        if (data == null) return;

        Color32[] sourcePixels = data.Pixels;
        int solidCount = 0;
        if (sourcePixels != null)
        {
            // Simple check for solid pixels
            for(int i=0; i<sourcePixels.Length; i+=100) if (sourcePixels[i].a > 0) solidCount++;
        }
        //Debug.Log($"[TerrainChunk] Reuse_Step1_Prepare for {ChunkX},{ChunkY}. HasChanges: {data.HasChanges}. SolidPixels(sampled): {solidCount}");

        // 청크 재사용 시 바위 목록 초기화
        _generatedRockBounds.Clear();
        _spawnedRocks.Clear();
        _carveReservedMask = null;
        ElevatorArea = null;

        // [Save] 저장된 암석 배치를 보관 → DecorateChunk_Phase2에서 RockDecorator가 소비
        _pendingSavedRocks = data.SavedRocks;

        // [FIX] Load Data into NativeArrays
        if (sourcePixels != null)
        {
            _data.LoadPixelData(sourcePixels);
        }
        // [GC] 단일 ID 경로 우선 — 매니지드 byte[TotalPixels] 없이 MemSet으로 채운다.
        if (data.UniformPixelInfoId >= 0)
        {
            _data.FillPixelInfo((byte)data.UniformPixelInfoId);
        }
        else if (data.PixelInfo != null)
        {
            _data.LoadPixelInfo(data.PixelInfo);
        }

        // [Fix] Reset HasBeenModified flag!
        // Pooled chunks normally retain this flag, causing RockDecorator to skip carving on fresh chunks.
        _data.HasBeenModified = data.HasChanges;

        // [FIX] Set Border Dimensions (CRITICAL for border rendering!)
        // [FIX] Set Border Dimensions (CRITICAL for border rendering!)
        SetBorderData(data.BorderPixels, data.BorderWidth, data.BorderHeight);
        //Debug.Log($"[BORDER_FIX] Set border dimensions for chunk {ChunkX},{ChunkY}: {data.BorderWidth}x{data.BorderHeight}");

        // [Updated] Secondary Border Logic
        //Debug.Log("[Reuse] Checkpoint 1: Secondary Border");
        SetSecondaryBorderData(data.SecondaryBorderPixels, data.SecondaryBorderWidth, data.SecondaryBorderHeight, data.SecondaryTileId);
        secondaryBorderTexture = null;

        if (data.Player != null) player = data.Player;
        
        // [FIX] Reset collider offset to prevent position offset bugs
        // Note: Sub-colliders manage their own offset (0,0), so this is less relevant for the specific sub-colliders
        // but good to keep if we ever attach something else.
        // if (_polyCollider != null) { _polyCollider.offset = Vector2.zero; } // Legacy removal, handled in sub-colliders
        
        //Debug.Log("[Reuse] Checkpoint 3: Copy Pixels");
        if (!_data.BasePixels.IsCreated)
            Debug.LogError($"[TerrainChunk] BasePixels not created for chunk {ChunkX},{ChunkY}");
        
        // 이웃 정보는 on-demand lookup으로 처리됨

        // [FIX] Mark collider as dirty to ensure collider generates on initial load
        // 실제 UpdateCollider()는 Reuse_Step2_Finalize()에서 SetActive(true) 이후 실행
        _data.IsColliderDirty = true;

        // [Cave] 절차적 굴 카빙. 반드시 ScheduleInitJobOnly() *앞*에서 스케줄한다 —
        // Downsample→Init 이 BasePixels 를 읽으므로 그 뒤에 뚫으면 거리장·테두리가 굴을 놓친다.
        // HasBeenModified 는 건드리지 않는다: 굴은 시드에서 결정적으로 재생성되므로 저장 대상이 아니다.
        _caveSettings  = data.Cave;
        _caveWorldSeed = data.WorldSeed;
        _caveRevealed  = false;
        _caveGeoBuilt  = false;
        _carvePending  = false;

        // 숨김 모드면 지금은 안 판다 — 플레이어가 파다 닿았을 때 TryRevealCave 가 연다.
        if (data.Cave.enabled && _visualizer != null && !data.Cave.hiddenUntilExposed)
        {
            _visualizer.ScheduleCarveCave(ChunkX, ChunkY, data.WorldSeed, data.Cave);
            _caveRevealed = true;
            _carvePending = true;
        }

        // Init Job만 예약하고 즉시 반환 (블로킹 없음).
        // Chamfer + Visual은 ProcessChunkQueue의 Phase 1 wait loop 완료 후 FinishVisualsAfterInit()에서 처리.
        // SyncBoundary는 FinishVisualsAfterInit()이 onPreChamfer=null로 호출되므로 여기서 하지 않는다 —
        // 이후 MarkChunkDirty → LateUpdate Round2(ProcessDirtyChunksAsync)에서 처리된다.
        ScheduleInitJobOnly();
    }

    /// <summary>배경 렌더러 GameObject (없으면 null). XRayController가 배경 톤 분리에 사용.</summary>
    public GameObject BackgroundObject => _background?.GameObject;

    public void Reuse_Step2_Finalize()
    {
        // [생성] 카빙 잡(CarveCaveConnectedJob)은 BasePixels에 쓰는 유일한 잡이다.
        // Phase 2는 이 뒤로 데코레이터(RockSpawner.IsValidPlacement·MineralGenerator)와
        // PostLoadPainter가 전부 메인 스레드에서 BasePixels를 읽으므로 여기서 한 번 풀어 준다.
        //
        // ⚠ 콜라이더 블록 안(아래)에 두면 안 된다. _colliderManager가 없는 청크는 그대로
        //    데코레이터로 넘어가 "CarveCaveConnectedJob writes to baseData. You must call
        //    JobHandle.Complete()" 로 Phase 2가 통째로 터진다. 그러면 그 청크는 풀에 반납되고
        //    Phase 2.5가 반납된 청크에 잡을 새로 걸어 다음 배치의 pool.Get()이 "잡이 살아 있는
        //    청크"를 꺼내 → TerrainVisualJob 에러로 로딩이 멈춘다(ChunkLoadingRunner 주석 참고).
        //
        // Phase 1 대기 루프의 IsJobRunning() 폴링으로는 대체 불가 — IsCompleted가 true여도
        // NativeArray의 AtomicSafetyHandle은 Complete()를 불러야 풀린다.
        EnsureCarveApplied();

        // EnsureJobsCompleted() 제거: 시각 잡(Visual/Chamfer/Init)은 distanceField와
        // outputTexture만 쓰므로 BasePixels를 읽는 UpdateCollider()와 충돌 없음.
        // 잡 완료는 p1JobsFinished 폴링 루프(비블로킹) 또는 LateUpdate ApplyTexture에서 처리.
        gameObject.SetActive(true);

        // [Background] 여기서 갱신하는 이유: Reuse_Step1_Prepare는 StandardChunkFactory에서만
        // 호출되므로 특수 청크가 누락된다. Step2는 두 경로 모두 지나간다.
        _background?.Refresh(ChunkX, ChunkY);

        // [Fix] SetActive(true) 이후 콜라이더 업데이트 → inactive 상태에서 오작동 방지
        if (_colliderManager != null)
        {
            // UpdateCollider()도 BasePixels를 읽는다 — 카빙 완료는 이 메서드 첫머리에서 이미 했다.
            _colliderManager.UpdateCollider();
            _data.IsColliderDirty = false;
            _lastColliderUpdateTime = Time.time;
        }
    }

    /// <summary>
    /// 청크 재로드 시 이미 주변 지형이 파여 노출됐어야 할 바위를 자동 복원.
    /// RevealInTerrain() 내부에서 CountExposedRockPixels()로 게이팅하므로
    /// 멀쩡히 묻혀있는 바위는 자동으로 스킵됨.
    /// </summary>
    public void RevealExposedRocks()
    {
        for (int i = _spawnedRocks.Count - 1; i >= 0; i--)
        {
            DiggableRock rock = _spawnedRocks[i];
            if (rock == null) { _spawnedRocks.RemoveAt(i); continue; }
            rock.RevealInTerrain();
        }
    }

    /// <summary>
    /// 묻힌 특수 공동 청크가 이미 주변이 파여 노출됐어야 하면 복원한다(재로드 시 호출).
    /// 컨트롤러가 없으면(일반 청크) 아무 일도 하지 않는다.
    /// </summary>
    public void RevealExposedCavity()
    {
        _cavityReveal?.RevealIfExposed();

        // [Cave] 복원된 파진 자리, 또는 이웃에서 이미 열린 굴이 경계로 닿았는지 재판정.
        TryRevealCave(default, false);
    }

    // ============================================================================================================
    //  [Cave] 묻어둔 굴 노출 — 돌·특수청크 공동과 같은 방식(닿으면 통째로 연다)
    // ============================================================================================================

    /// <summary>노출로 인정할 최소 표본 수. 1~2px 스침으로 굴 전체가 열리는 걸 막는다.</summary>
    private const int CaveRevealHits = 8;

    /// <summary>
    /// 이 청크의 굴이 그 로컬 픽셀을 덮는지(침식 포함). 이웃이 경계 노출을 볼 때 쓴다.
    ///
    /// 카빙이 한 겹 더 거는 격자 연결성(keptCells)은 보지 않으므로, 실제 파이는 영역보다
    /// **조금 넓게** 답한다(카빙 ⊆ 이 판정). 그 방향이라 안전하다 — 파였는데 못 보는 일은 없고,
    /// 드물게 굴이 조금 일찍 열릴 뿐이다. 근거와 예외 조건은 ChunkJobScheduler.BuildCaveGeometry 주석.
    /// </summary>
    public bool IsCavePixel(int x, int y)
    {
        if (!_caveSettings.enabled || _data == null) return false;
        EnsureCaveGeo();
        return _caveGeo.IsCaveEroded(_caveGeo.chunkOrigin + new Unity.Mathematics.float2(x, y),
                                     _caveSettings.minRadiusPx);
    }

    private void EnsureCaveGeo()
    {
        if (_caveGeoBuilt) return;
        _caveGeo = ChunkJobScheduler.BuildCaveGeometry(_data, ChunkX, ChunkY, _caveWorldSeed, _caveSettings);
        _caveGeoBuilt = true;
    }

    /// <summary>
    /// 아직 안 뚫린 굴이 이 자리(또는 margin 이내)를 덮을 예정인지. **데코 배치가 부르는 용도.**
    ///
    /// 숨김 모드에서는 카빙이 플레이어가 파고 들어온 뒤에 일어난다. 그 사이 Phase 2 데코레이터가
    /// 굴 자리에 광물·돌을 놓아 버리면, 굴이 열리는 순간 지지대를 잃은 광물이 우수수 떨어지고
    /// 돌은 공중에 뜬다. 그래서 배치 단계에서 미리 걸러낸다 — 파낼 때 지우는 것보다 간단하다
    /// (이미 노출·수집 중인 광물이나 세이브의 수집 마커를 신경 쓸 필요가 없다).
    ///
    /// 숨김 모드가 아니면 카빙이 데코보다 먼저라 그 자리가 이미 공기다. 기존 지지 검사가
    /// 알아서 거르므로 false 를 돌려주고 노이즈 계산을 건너뛴다.
    ///
    /// 중심 + 상하좌우 4점만 본다. 판정 한 번이 최대 9회 snoise 라 9점 전수는 과하고,
    /// 굴 판정 자체가 이미 minRadiusPx 로 침식돼 있어 벽면에는 여유가 남는다.
    /// </summary>
    public bool IsFutureCaveNear(int x, int y, int margin)
    {
        if (IsFutureCaveAt(x, y)) return true;
        return CountFutureCaveAround(x, y, margin, margin) > 0;
    }

    /// <summary>
    /// 이 청크에 **아직 안 뚫린 굴이 실제로 있는지.** 굴 벽 전용 배치 패스가 헛돌지 않게 하는 관문이다.
    /// blobChance 때문에 굴이 없는 청크가 30% 쯤 되는데, 거기서 후보를 훑으면 비용만 나간다.
    /// </summary>
    public bool HasHiddenCave
    {
        get
        {
            if (!_caveSettings.enabled || !_caveSettings.hiddenUntilExposed) return false;
            if (_caveRevealed) return false;

            EnsureCaveGeo();
            // 노이즈 모드는 "이 청크에 굴이 있나"를 싸게 알 수 없다 → 있다고 보고 후보 검사에 맡긴다.
            return _caveGeo.useBlob == 0 || _caveGeo.blobExists != 0;
        }
    }

    /// <summary>굴 벽에 따로 깔아 줄 바위 수(tileData.json 의 caveRockCount).</summary>
    public int CaveRockCount => _caveSettings.enabled ? _caveSettings.rockCount : 0;

    /// <summary>그 픽셀 하나가 예정 굴에 들어가는지. 조건은 IsFutureCaveNear 와 같다.</summary>
    public bool IsFutureCaveAt(int x, int y)
    {
        if (!_caveSettings.enabled || !_caveSettings.hiddenUntilExposed) return false;
        if (_caveRevealed) return false;   // 이미 뚫렸으면 픽셀이 공기라 기존 검사로 충분하다

        return IsCavePixel(x, y);
    }

    /// <summary>
    /// 상하좌우 4개 탭 중 예정 굴에 들어가는 개수. 배치물이 굴에 "얼마나 잠기는지"를 재는 용도다.
    /// 돌은 이 값으로 "벽에 박힌 것"과 "허공에 뜬 것"을 가른다.
    /// </summary>
    public int CountFutureCaveAround(int x, int y, int marginX, int marginY)
    {
        if (!_caveSettings.enabled || !_caveSettings.hiddenUntilExposed) return 0;
        if (_caveRevealed) return 0;

        int mx = Mathf.Max(1, marginX);
        int my = Mathf.Max(1, marginY);

        int n = 0;
        if (IsCavePixel(x + mx, y)) n++;
        if (IsCavePixel(x - mx, y)) n++;
        if (IsCavePixel(x, y + my)) n++;
        if (IsCavePixel(x, y - my)) n++;
        return n;
    }

    /// <summary>
    /// 판 자리가 굴에 닿았으면 굴을 연다. probe 는 방금 판 영역(없으면 청크 전체를 성기게 훑는다).
    ///
    /// 전체 훑기는 **공기 픽셀에서만** 판정식을 돌린다. 갓 생성된 청크는 공기가 없어서
    /// 배열 읽기만 하고 끝나므로 로드 비용이 사실상 0 이다.
    /// </summary>
    public void TryRevealCave(RectInt probe, bool hasProbe)
    {
        if (_caveRevealed) return;
        if (!_caveSettings.enabled || !_caveSettings.hiddenUntilExposed) return;
        if (_visualizer == null || _data == null || !_data.BasePixels.IsCreated) return;

        EnsureJobsCompleted();
        EnsureCaveGeo();

        int step = hasProbe ? 2 : 8;
        int x0 = hasProbe ? Mathf.Max(0, probe.xMin) : 0;
        int y0 = hasProbe ? Mathf.Max(0, probe.yMin) : 0;
        int x1 = hasProbe ? Mathf.Min(width,  probe.xMax) : width;
        int y1 = hasProbe ? Mathf.Min(height, probe.yMax) : height;

        int hits = 0;
        for (int y = y0; y < y1; y += step)
        {
            int row = y * width;
            for (int x = x0; x < x1; x += step)
            {
                if (_data.BasePixels[row + x].a != 0) continue;   // 파인 자리만 본다
                if (!IsCavePixel(x, y)) continue;
                if (++hits >= CaveRevealHits) { RevealCaveNow(); return; }
            }
        }

        // 이웃 청크에서 이미 열린 굴이 경계 너머로 닿아 있으면 이쪽도 연다.
        // 이게 없으면 연결 통로가 경계에서 벽으로 끝난다.
        if (!hasProbe && IsCaveOpenAcrossBoundary()) RevealCaveNow();
    }

    /// <summary>
    /// 상하좌우 이웃 중 **굴이 이미 열린** 청크가 있고, 공유 경계에서 양쪽 굴이 맞닿으면 true.
    /// 이웃이 없으면 판정하지 않는다 — 없는 이웃을 공기로 치면(IsTransparent 의 기본 동작)
    /// 로드 순서만으로 굴이 줄줄이 열려버린다.
    /// </summary>
    private bool IsCaveOpenAcrossBoundary()
    {
        var mgr = InfinityMapManager.Instance;
        if (mgr == null) return false;

        return EdgeTouchesOpenCave(mgr, new Vector2Int(ChunkX - 1, ChunkY), true,  0)
            || EdgeTouchesOpenCave(mgr, new Vector2Int(ChunkX + 1, ChunkY), true,  width - 1)
            || EdgeTouchesOpenCave(mgr, new Vector2Int(ChunkX, ChunkY - 1), false, 0)
            || EdgeTouchesOpenCave(mgr, new Vector2Int(ChunkX, ChunkY + 1), false, height - 1);
    }

    private bool EdgeTouchesOpenCave(InfinityMapManager mgr, Vector2Int coord, bool vertical, int myEdge)
    {
        var nb = mgr.GetChunk(coord);
        if (nb == null || !nb.CaveRevealed) return false;

        int len = vertical ? height : width;
        int nbEdge = vertical ? (coord.x < ChunkX ? width - 1 : 0)
                              : (coord.y < ChunkY ? height - 1 : 0);

        int hits = 0;
        for (int t = 0; t < len; t += 4)
        {
            int mx = vertical ? myEdge : t;
            int my = vertical ? t : myEdge;
            if (!IsCavePixel(mx, my)) continue;

            // 이웃의 **픽셀**이 아니라 **굴 판정식**을 본다. 이웃이 방금 열렸다면 카빙 잡이
            // 아직 안 끝나 픽셀은 solid 일 수 있다. 판정식을 보면 잡을 기다릴 필요가 없다.
            int nx = vertical ? nbEdge : t;
            int ny = vertical ? t : nbEdge;
            if (!nb.IsCavePixel(nx, ny)) continue;

            if (++hits >= CaveRevealHits) return true;
        }
        return false;
    }

    private void RevealCaveNow()
    {
        if (_caveRevealed) return;
        _caveRevealed = true;

        _visualizer.ScheduleCarveCave(ChunkX, ChunkY, _caveWorldSeed, _caveSettings);
        _carvePending = true;

        // 잡 완료를 여기서 기다리지 않는다(1M 픽셀 동기 대기 = 프레임 끊김).
        // 이웃 판정이 픽셀 대신 판정식을 보므로 기다릴 이유도 없고,
        // 비주얼·콜라이더는 _caveHandle 이 IsJobRunning/CompleteAllJobs 에 등록돼 있어 알아서 기다린다.
        _data.MarkDirty();
        InfinityMapManager.Instance?.MarkChunkDirty(this, new RectInt(0, 0, width, height));

        // [돌 노출] 굴 벽·바닥·천장에 박힌 돌을 드러낸다. RevealInTerrain 안의
        // CountExposedRockPixels 가 GetPixelAlpha 로 실제 지형을 세므로, 아직 안 드러날 돌은
        // 알아서 걸러진다(묻힌 돌이 통째로 튀어나오지 않는다).
        //
        // ⚠ GetPixelAlpha 가 EnsureCarveApplied 를 지나므로 여기서 카빙 잡을 한 번 물게 된다.
        //   위에서 비동기로 둔 의도와 상충하지만, 굴 발견은 드문 이벤트고 이걸 미루면
        //   "굴은 뚫렸는데 벽의 돌만 안 보이는" 상태가 다음 삽질까지 남는다.
        RevealExposedRocks();

        // 같은 굴이 이웃으로 이어져 있으면 그쪽도 연다.
        var mgr = InfinityMapManager.Instance;
        if (mgr == null) return;
        mgr.GetChunk(new Vector2Int(ChunkX - 1, ChunkY))?.TryRevealCave(default, false);
        mgr.GetChunk(new Vector2Int(ChunkX + 1, ChunkY))?.TryRevealCave(default, false);
        mgr.GetChunk(new Vector2Int(ChunkX, ChunkY - 1))?.TryRevealCave(default, false);
        mgr.GetChunk(new Vector2Int(ChunkX, ChunkY + 1))?.TryRevealCave(default, false);
    }

    /// <summary>
    /// 지형을 판다. 반환값 = 픽셀이 실제로 하나라도 지워졌는지.
    /// 이미 파인 공간·불괴 픽셀을 쳤으면 false — 스태미나 비용을 물릴지 판단하는 데 쓴다.
    /// (기존 호출부는 반환값을 무시하므로 그대로 동작한다.)
    /// </summary>
    public bool Dig(Vector2 mouseWorldPos, float radius, int toolIndex, bool applySmoothing = true)
    {
        if (player == null) return false;

        EnsureJobsCompleted();

        // [Burst] digCenter와 playerPos를 캡처하는 람다 — 역방향 파티클 포함
        Vector2 digCenter = mouseWorldPos;
        Vector2 playerPos = player.position;
        TerrainModifier.ParticleSpawnCallback debrisCallback = (pos, color) =>
            SpawnDebrisParticle(pos, color, digCenter, playerPos);

        TerrainModifier.NeighborContext neighbors = default;
        var mgr = InfinityMapManager.Instance;
        if (mgr != null)
        {
            neighbors.Left = mgr.GetChunk(new Vector2Int(ChunkX - 1, ChunkY))?.Data;
            neighbors.Right = mgr.GetChunk(new Vector2Int(ChunkX + 1, ChunkY))?.Data;
            neighbors.Top = mgr.GetChunk(new Vector2Int(ChunkX, ChunkY + 1))?.Data;
            neighbors.Bottom = mgr.GetChunk(new Vector2Int(ChunkX, ChunkY - 1))?.Data;
        }

        var result = _modifier.Dig(
            _data, mouseWorldPos, player.position,
            radius, toolIndex,
            WorldToPixel,
            neighbors,
            debrisCallback, PixelToWorldPos
        );

        // 밸런스 텔레메트리 — 플레이어가 판 픽셀만 센다(폭발·섬 제거는 이 경로가 아니다).
        if (result.RemovedPixels > 0)
            SettlementManager.Instance?.AddDugPixels(result.RemovedPixels);

        if (result.WasModified)
        {
            // applySmoothing=false(ImmediateDig)일 때는 스무딩·섬 감지 전체 skip.
            // 섬 발생 가능성이 낮고(전방 연속 지형 제거), 0.4초 후 RequestDig가 정리함.
            if (applySmoothing)
            {
                // Narrow Protrusion Removal: 6픽셀 이하 너비의 뾰족한 흙 돌기 제거
                _modifier.RemoveNarrowProtrusions(
                    _data, result.ClampedMinX, result.ClampedMinY,
                    result.ClampedMaxX, result.ClampedMaxY,
                    narrowThreshold: 6
                );

                // Edge Erosion: 8방향 이웃 카운팅으로 고립 픽셀 제거 → 구멍 경계 부드럽게
                _modifier.ErodeEdges(
                    _data, result.ClampedMinX, result.ClampedMinY,
                    result.ClampedMaxX, result.ClampedMaxY,
                    airThreshold: 5
                );

                // Island Removal
                if (s_useIslandRemoval)
                {
                    var island = _modifier.CheckFloatingIslandsInArea(
                        _data, result.ClampedMinX, result.ClampedMinY,
                        result.ClampedMaxX, result.ClampedMaxY,
                        neighbors,
                        PixelToWorldPos, debrisCallback
                    );

                    ExpandResultToIsland(ref result, island);
                }
            }

            //Debug.Log($"[테][TerrainChunk(X:{ChunkX},Y:{ChunkY})] Dig Result Check. RawBounds:{result.RawMinX}~{result.RawMaxX}, {result.RawMinY}~{result.RawMaxY}");

            // 파기 영역이 바위 경계에 닿으면(인접 1px 포함) → 바위 노출
            if (_spawnedRocks.Count > 0)
            {
                // 파기 영역을 1px 확장해 인접 판정
                var adjacentArea = new RectInt(
                    result.RawMinX - 1, result.RawMinY - 1,
                    Mathf.Max(1, result.RawMaxX - result.RawMinX) + 2,
                    Mathf.Max(1, result.RawMaxY - result.RawMinY) + 2);

                for (int ri = _spawnedRocks.Count - 1; ri >= 0; ri--)
                {
                    DiggableRock rock = _spawnedRocks[ri];
                    if (rock == null) { _spawnedRocks.RemoveAt(ri); continue; }

                    // [Fix] 회전 적용된 실제 footprint(RevealBounds)로 판정.
                    // PixelBoundsInChunk는 회전 전 원본 사각형이라 좌하단 pivot 스프라이트에서
                    // 실제 돌과 최대 130px 어긋나 → 돌을 관통해 파도 노출이 안 되던 버그.
                    if (adjacentArea.Overlaps(rock.RevealBounds))
                        rock.RevealInTerrain();
                }
            }

            // [Cavity Reveal] 묻힌 특수 공동이 파기로 air에 맞닿으면 노출
            _cavityReveal?.RevealIfExposed();

            // [Cave Reveal] 판 자리가 묻어둔 굴에 닿았으면 굴을 연다
            if (!_caveRevealed)
            {
                TryRevealCave(new RectInt(
                    result.RawMinX - 1, result.RawMinY - 1,
                    Mathf.Max(1, result.RawMaxX - result.RawMinX) + 2,
                    Mathf.Max(1, result.RawMaxY - result.RawMinY) + 2), true);
            }

            // [이벤트 기반 재낙하] 파기 영역과 겹치는 매설·안착 광물만 지지 재검사
            NotifyMineralsInDigRegion(result);

            // [LateUpdate Pattern] 시각 업데이트를 직접 하지 않고 매니저에게 위임
            int margin = 20;
            int minX = Mathf.Clamp(result.RawMinX - margin, 0, width);
            int maxX = Mathf.Clamp(result.RawMaxX + margin, 0, width);
            int minY = Mathf.Clamp(result.RawMinY - margin, 0, height);
            int maxY = Mathf.Clamp(result.RawMaxY + margin, 0, height);
            var dirtyRect = new RectInt(minX, minY, maxX - minX, maxY - minY);

            if (mgr != null)
            {
                mgr.MarkChunkDirty(this, dirtyRect);
                MarkNeighborsDirty(mgr, result); // #4 [Fix] 추출된 공통 메서드
            }
            else
            {
                // Fallback: 매니저 없을 때 직접 처리 (에디터 테스트 등)
                if (_visualizer != null)
                    _visualizer.UpdateVisualsArea(minX, minY, maxX, maxY, ChunkX, ChunkY, SyncBoundaryDistanceWithNeighbors);
            }
        }

        return result.WasModified;
    }

    // 파기 영역과 겹치는 매설·안착 광물의 지지를 재검사하도록 통지한다(이벤트 기반 재낙하).
    // 물리 자동 wake(콜라이더 재생성) 대신 "실제로 파인 영역"만 쿼리하므로
    // 낙하물이 불필요하게 계속 깨어나 SolveDiscreteIsland가 폭증하던 문제를 막는다.
    private void NotifyMineralsInDigRegion(TerrainModifier.DigResult result)
    {
        Vector2 wMin = GetWorldPos(result.ClampedMinX, result.ClampedMinY);
        Vector2 wMax = GetWorldPos(result.ClampedMaxX, result.ClampedMaxY);
        Vector2 center = (wMin + wMax) * 0.5f;
        // 발밑 여유 margin(0.3u): 파인 흙 바로 위에 얹혀 있던 광물까지 포함
        Vector2 size = new Vector2(Mathf.Abs(wMax.x - wMin.x), Mathf.Abs(wMax.y - wMin.y))
                       + new Vector2(0.3f, 0.3f);
        MineralItemController.NotifyTerrainDug(center, size);
    }

    public void Explode(Vector2 explosionWorldPos, float radius)
    {
        EnsureJobsCompleted();

        // 폭발 중심점 파티클 연출 (원형이므로 방향 없음)
        TerrainModifier.ParticleSpawnCallback debrisCallback = (pos, color) =>
            SpawnDebrisParticle(pos, color, explosionWorldPos);

        TerrainModifier.NeighborContext neighbors = default;
        var mgr = InfinityMapManager.Instance;
        if (mgr != null)
        {
            neighbors.Left = mgr.GetChunk(new Vector2Int(ChunkX - 1, ChunkY))?.Data;
            neighbors.Right = mgr.GetChunk(new Vector2Int(ChunkX + 1, ChunkY))?.Data;
            neighbors.Top = mgr.GetChunk(new Vector2Int(ChunkX, ChunkY + 1))?.Data;
            neighbors.Bottom = mgr.GetChunk(new Vector2Int(ChunkX, ChunkY - 1))?.Data;
        }

        var result = _modifier.Explode(
            _data, explosionWorldPos, radius,
            WorldToPixel,
            neighbors,
            debrisCallback, PixelToWorldPos
        );

        if (result.WasModified)
        {
            // Narrow Protrusion Removal: 폭발 구멍의 6픽셀 이하 너비 돌기 제거
            _modifier.RemoveNarrowProtrusions(
                _data, result.ClampedMinX, result.ClampedMinY,
                result.ClampedMaxX, result.ClampedMaxY,
                narrowThreshold: 6
            );

            // Edge Erosion: 8방향 이웃 카운팅으로 고립 픽셀 제거 → 폭발 구멍 모서리 부드럽게
            _modifier.ErodeEdges(
                _data, result.ClampedMinX, result.ClampedMinY,
                result.ClampedMaxX, result.ClampedMaxY,
                airThreshold: 5
            );

            // Island Removal (BasePixels 수정 후 CopyFrom 한 번만)
            if (s_useIslandRemoval)
            {
                var island = _modifier.CheckFloatingIslandsInArea(
                    _data, result.ClampedMinX, result.ClampedMinY,
                    result.ClampedMaxX, result.ClampedMaxY,
                    neighbors,
                    PixelToWorldPos, debrisCallback
                );

                ExpandResultToIsland(ref result, island);
            }
            // 파기 영역이 바위 경계에 닿으면 노출 (폭발 범위 내)
            if (_spawnedRocks.Count > 0)
            {
                var adjacentArea = new RectInt(
                    result.RawMinX - 1, result.RawMinY - 1,
                    Mathf.Max(1, result.RawMaxX - result.RawMinX) + 2,
                    Mathf.Max(1, result.RawMaxY - result.RawMinY) + 2);

                for (int ri = _spawnedRocks.Count - 1; ri >= 0; ri--)
                {
                    DiggableRock rock = _spawnedRocks[ri];
                    if (rock == null) { _spawnedRocks.RemoveAt(ri); continue; }

                    // [Fix] Dig()와 동일 — 회전 적용된 실제 footprint로 판정
                    if (adjacentArea.Overlaps(rock.RevealBounds))
                        rock.RevealInTerrain();
                }
            }

            // [Cavity Reveal] 묻힌 특수 공동이 폭발로 air에 맞닿으면 노출
            _cavityReveal?.RevealIfExposed();

            // [이벤트 기반 재낙하] 폭발 영역과 겹치는 매설·안착 광물만 지지 재검사
            NotifyMineralsInDigRegion(result);

            // 매니저를 통한 Visual 업데이트 및 인접 청크 더티 마킹
            int margin = 20;
            int minX = Mathf.Clamp(result.RawMinX - margin, 0, width);
            int maxX = Mathf.Clamp(result.RawMaxX + margin, 0, width);
            int minY = Mathf.Clamp(result.RawMinY - margin, 0, height);
            int maxY = Mathf.Clamp(result.RawMaxY + margin, 0, height);
            var dirtyRect = new RectInt(minX, minY, maxX - minX, maxY - minY);

            if (mgr != null)
            {
                mgr.MarkChunkDirty(this, dirtyRect);
                MarkNeighborsDirty(mgr, result); // #4 [Fix] 추출된 공통 메서드
            }
            else
            {
                if (_visualizer != null)
                    _visualizer.UpdateVisualsArea(minX, minY, maxX, maxY, ChunkX, ChunkY, SyncBoundaryDistanceWithNeighbors);
            }
        }
    }

    // #4 [Fix] Dig()와 Explode()의 공통 이웃 dirty 마킹 로직 추출.
    /// <summary>
    /// 섬 제거가 지운 범위를 파기 결과에 합쳐 넣는다.
    ///
    /// 섬 제거의 플러드필은 넘겨준 렉트를 **씨앗으로만** 쓰고 연결된 덩어리 전체를 지운다
    /// (최대 MaxIslandSize=90,000px ≈ 300x300). 파기 렉트(반경 ~100px)만 더티로 잡으면
    /// 그 밖에서 사라진 지형의 테두리·콜라이더가 낡은 채 남고, 지운 자리가 청크 경계에 닿았을
    /// 때는 이웃에게 통지도 안 가서 경계에서 끊긴 모습이 된다.
    /// (2026-09-04 리포트: 돌을 파다 위쪽 지형이 섬 제거로 사라졌는데 테두리가 안 갱신됨)
    ///
    /// Raw* 만 넓힌다 — 더티 렉트와 이웃 통지가 그 값만 본다. Clamped* 는 파기 판정 전용이라
    /// 건드리면 호출부(스태미나·바위 노출)가 의미가 달라진다.
    /// </summary>
    private void ExpandResultToIsland(ref TerrainModifier.DigResult result,
                                      TerrainModifier.IslandRemovalResult island)
    {
        if (!island.Removed) return;

        result.RawMinX = Mathf.Min(result.RawMinX, island.MinX);
        result.RawMinY = Mathf.Min(result.RawMinY, island.MinY);
        result.RawMaxX = Mathf.Max(result.RawMaxX, island.MaxX);
        result.RawMaxY = Mathf.Max(result.RawMaxY, island.MaxY);
    }

    private void MarkNeighborsDirty(InfinityMapManager mgr, TerrainModifier.DigResult result)
    {
        // DistanceField(border 텍스처)는 경계로부터 texPx(최대 50px) 이내 구멍도 영향을 미침.
        const int BORDER_MARGIN = 50;

        bool crossedLeft   = result.RawMinX <= BORDER_MARGIN;
        bool crossedRight  = result.RawMaxX >= width  - BORDER_MARGIN;
        bool crossedTop    = result.RawMaxY >= height - BORDER_MARGIN;
        bool crossedBottom = result.RawMinY <= BORDER_MARGIN;

        // [Opt] 이웃 청크의 인접 경계 스트립만 dirty로 마크 (전체 청크 대신)
        // STRIP = BORDER_MARGIN(50) = ChunkJobScheduler.EXT_MARGIN과 일치 → 거리장 재계산 비용 대폭 감소
        const int STRIP = BORDER_MARGIN;

        // [Opt] 평행 방향(경계를 따라가는 축)도 "판 구간 ± BORDER_MARGIN"으로 국한한다.
        //
        // 종전엔 좌/우 이웃에게 y: 0~height, 상/하 이웃에게 x: 0~width 를 통째로 넘겼다.
        // y=200 근처를 팠어도 이웃은 50×1000을 dirty로 받아 Init·Chamfer·Upsample·Visual이
        // 전부 세로 1000을 훑었다 (LagDiag의 chamferArea=127x1000 이 이것).
        //
        // 우리 공기가 이웃 거리장에 영향을 주는 범위는 유한하다:
        //   maxDist(255) ÷ 직교비용(5) = 51px. 테두리 렌더에 실제로 쓰이는 건
        //   dist ≤ textureThicknessPx(≤50) × 5 = 250, 즉 50px 이내.
        // 판 구간에서 평행 방향으로 50px 넘게 떨어진 이웃 픽셀은 애초에 영향을 받지 않는다.
        //
        // 이웃 측에서 SetDirtyRect가 ext(± EXT_MARGIN=50)를 한 번 더 붙이므로
        // 최종 chamfer 커버리지는 "판 구간 ±100" — 필요한 50의 두 배로 여유롭다.
        //
        // BoundarySyncJob은 이미 syncMin/syncMax(= _lastExt/2)로 dirty span에 국한돼 있어
        // 별도 정합성 처리가 필요 없다. sync 범위 밖 경계 셀은 그쪽 우리 DF가 안 변했으므로
        // 이전에 sync된 값이 그대로 유효하다.
        int yLo = Mathf.Clamp(result.RawMinY - BORDER_MARGIN, 0, height);
        int yHi = Mathf.Clamp(result.RawMaxY + BORDER_MARGIN, 0, height);
        int xLo = Mathf.Clamp(result.RawMinX - BORDER_MARGIN, 0, width);
        int xHi = Mathf.Clamp(result.RawMaxX + BORDER_MARGIN, 0, width);
        int ySpan = yHi - yLo;
        int xSpan = xHi - xLo;

        // 이웃의 "나와 맞닿은 엣지" 스트립:
        //   crossedLeft   → 왼쪽 이웃의 오른쪽 엣지 (x: width-STRIP ~ width), y는 판 구간만
        //   crossedRight  → 오른쪽 이웃의 왼쪽 엣지 (x: 0 ~ STRIP),           y는 판 구간만
        //   crossedTop    → 위쪽 이웃의 아래쪽 엣지 (y: 0 ~ STRIP),           x는 판 구간만
        //   crossedBottom → 아래쪽 이웃의 위쪽 엣지 (y: height-STRIP ~ height), x는 판 구간만
        if (crossedLeft  && ySpan > 0) mgr.MarkNeighborDirty(ChunkX - 1, ChunkY, new RectInt(width - STRIP, yLo, STRIP, ySpan));
        if (crossedRight && ySpan > 0) mgr.MarkNeighborDirty(ChunkX + 1, ChunkY, new RectInt(0,             yLo, STRIP, ySpan));
        if (crossedTop    && xSpan > 0) mgr.MarkNeighborDirty(ChunkX, ChunkY + 1, new RectInt(xLo, 0,              xSpan, STRIP));
        if (crossedBottom && xSpan > 0) mgr.MarkNeighborDirty(ChunkX, ChunkY - 1, new RectInt(xLo, height - STRIP, xSpan, STRIP));

        // 코너는 이미 STRIP×STRIP 이라 더 좁힐 게 없다.
        if (crossedLeft  && crossedBottom) mgr.MarkNeighborDirty(ChunkX - 1, ChunkY - 1, new RectInt(width - STRIP, height - STRIP, STRIP, STRIP));
        if (crossedRight && crossedBottom) mgr.MarkNeighborDirty(ChunkX + 1, ChunkY - 1, new RectInt(0, height - STRIP, STRIP, STRIP));
        if (crossedLeft  && crossedTop)    mgr.MarkNeighborDirty(ChunkX - 1, ChunkY + 1, new RectInt(width - STRIP, 0, STRIP, STRIP));
        if (crossedRight && crossedTop)    mgr.MarkNeighborDirty(ChunkX + 1, ChunkY + 1, new RectInt(0, 0, STRIP, STRIP));
    }

    /// <summary>
    /// [단발 전체 갱신] 지정 영역에 Init + BoundarySync + Chamfer + Upsample + Visual 을 한 번에 예약.
    ///
    /// LateUpdate 파이프라인(Round1/Round2)은 이걸 쓰지 않는다 —
    /// 매니저는 ScheduleDistancePass / ScheduleSyncAndVisualPass 를 쓴다.
    /// 현재 호출자: RollingRockTrap (바위가 지나간 자리를 즉시 갱신).
    /// TerrainCarver / PixelFloorCollapser 가 쓰는 Visualizer.UpdateVisualsArea 와 같은 부류다.
    /// </summary>
    public void DoVisualUpdate(RectInt dirtyRect)
    {
        if (_visualizer == null) return;
        _visualizer.UpdateVisualsArea(
            dirtyRect.xMin, dirtyRect.yMin,
            dirtyRect.xMax, dirtyRect.yMax,
            ChunkX, ChunkY,
            SyncBoundaryDistanceWithNeighbors);
    }

    /// <summary>
    /// [Non-blocking Pre-schedule] LateUpdate Step 1 전용.
    /// 이전 Init 잡이 완료된 경우에만 스케줄한다.
    /// </summary>
    /// <returns>Init 잡이 실제로 스케줄됐으면 true. false면 Round 1이 대신 수행해야 한다.</returns>
    public bool ScheduleInitOnlyIfReady(RectInt rect)
    {
        if (_visualizer == null) return false;
        return _visualizer.TryScheduleInitArea(rect.xMin, rect.yMin, rect.xMax, rect.yMax);
    }

    /// <summary>
    /// [Round 1] 자기 청크 거리장만 확정. BoundarySync·Upsample·Visual 없음.
    /// (배경: Assets/Docs/job-pipeline-waste-removal.md §4)
    /// </summary>
    public void ScheduleDistancePass(RectInt rect, bool hasRect, bool skipInit, bool withPreview)
    {
        if (_visualizer == null) return;

        int minX = hasRect ? rect.xMin : 0;
        int minY = hasRect ? rect.yMin : 0;
        int maxX = hasRect ? rect.xMax : _data.Width;
        int maxY = hasRect ? rect.yMax : _data.Height;

        _visualizer.ScheduleDistancePass(minX, minY, maxX, maxY, ChunkX, ChunkY, skipInit, withPreview);
    }

    /// <summary>
    /// [Round 2] BoundarySync → Chamfer → Upsample → Visual.
    /// </summary>
    public void ScheduleSyncAndVisualPass(RectInt rect, bool hasRect)
    {
        if (_visualizer == null) return;

        int minX = hasRect ? rect.xMin : 0;
        int minY = hasRect ? rect.yMin : 0;
        int maxX = hasRect ? rect.xMax : _data.Width;
        int maxY = hasRect ? rect.yMax : _data.Height;

        _visualizer.ScheduleSyncAndVisualPass(minX, minY, maxX, maxY, ChunkX, ChunkY,
                                              SyncBoundaryDistanceWithNeighbors);
    }

    /// <summary>이전 Init 잡이 완료 상태인지 확인.</summary>
    public bool IsInitJobCompleted() => _visualizer == null || !(_visualizer.IsInitJobRunning());

    /// <summary>
    /// [LateUpdate Pattern] 매니저에서 호출. Job 완료 후 GPU 텍스처 업로드.
    /// 콜라이더 갱신은 TryUpdateCollider()로 분리됨 — 비주얼 파이프라인과 독립적으로 처리.
    /// ProcessDirtyChunksAsync 코루틴 종료 시점과 GPU 스로틀 루프 양쪽에서 호출되므로
    /// IsVisualDirty 가드로 중복 업로드를 방지한다.
    /// </summary>
    public void ApplyTexture()
    {
        if (_data == null || _visualizer == null) return;
        if (!_data.IsVisualDirty) return; // 이미 업로드됨 — 중복 호출 방지
        _visualizer.ApplyTextureSync();
        _data.IsVisualDirty = false;
    }

    /// <summary>
    /// [Decoupled] 비주얼 파이프라인(IsVisualJobCompleted)과 무관하게 콜라이더만 독립 갱신.
    /// InfinityMapManager.LateUpdate에서 매 프레임 호출되며, colliderUpdateInterval 스로틀 적용.
    /// </summary>
    public void TryUpdateCollider()
    {
        if (_data == null || _colliderManager == null || _colliderManager.IsDestroyed()) return;
        if (!_data.IsColliderDirty) return;
        if (!ShouldUpdateCollider()) return;

        // UpdateCollider 는 BasePixels 를 메인 스레드에서 읽는다(TerrainCollider 의 인덱싱·
        // GetUnsafeReadOnlyPtr 둘 다). 굴 노출 카빙이 떠 있을 수 있으므로 먼저 물어 준다.
        EnsureCarveApplied();

        _colliderManager.UpdateCollider();
        _data.IsColliderDirty = false;
        _lastColliderUpdateTime = Time.time;
    }

    /// <summary>
    /// 암석 파괴처럼 즉각적인 콜라이더 공백이 생기는 상황에서 throttle을 무시하고 즉시 갱신.
    /// 일반 파기 흐름에서는 TryUpdateCollider()를 사용할 것.
    /// </summary>
    public void ForceUpdateCollider()
    {
        if (_data == null || _colliderManager == null || _colliderManager.IsDestroyed()) return;
        EnsureCarveApplied();
        _colliderManager.UpdateCollider();
        _data.IsColliderDirty = false;
        _lastColliderUpdateTime = Time.time;
    }

    public void LoadChunkData(Color32[] sourcePixels, byte[] pixelInfo = null, Color32[] borderPixels = null, int borderWidth = 0, int borderHeight = 0)
    {
        EnsureJobsCompleted();
        _data.LoadPixelData(sourcePixels);
        _data.LoadPixelInfo(pixelInfo);

        // borderPixels가 null이면 청크가 이미 가진 테두리를 유지한다 (ChunkImagePainter의 계약).
        // SetBorderData(null, 0, 0)은 BorderWidth/Height를 0으로 지워버리고, 그러면
        // TerrainVisualJob의 테두리 분기가 통째로 꺼져 지형이 baseCol 그대로 그려진다.
        // 방 PNG(elevator.png 등)는 불투명부가 순수 검정 마스크라 방 전체가 새까맣게 보인다.
        // (재로드 때는 Reuse_Step1_Prepare가 층 테두리를 다시 넣어줘서 저절로 정상으로 보였다)
        if (borderPixels != null && borderPixels.Length > 0)
            SetBorderData(borderPixels, borderWidth, borderHeight);

        RefreshVisuals();
    }

    public void InitializeAfterGeneration()
    {
        RefreshVisuals();
    }

    /// <summary>
    /// Init Job만 예약. 메인 스레드 블로킹 없음.
    /// Phase 1 wait loop가 완료를 확인한 뒤 반드시 FinishVisualsAfterInit()을 호출해야 함.
    /// </summary>
    public void ScheduleInitJobOnly()
    {
        if (_visualizer != null)
            _visualizer.ScheduleInitArea(0, 0, _data.Width, _data.Height);
    }

    /// <summary>
    /// 장식(Phase 2) 완료 후 Chamfer + Upsample + Visual만 예약. SyncBoundary 없음(onPreChamfer=null).
    /// - 이웃 CompleteLighting()/EnsureJobsCompleted() 미호출 → 메인 스레드 블로킹 없음.
    /// - SyncBoundary는 Phase 3의 MarkChunkDirty → ProcessDirtyChunksAsync(LateUpdate)에서 처리됨.
    /// </summary>
    public void FinishVisualsAfterInit()
    {
        if (_visualizer != null)
            _visualizer.UpdateVisualsFull(ChunkX, ChunkY, null, skipInit: true);
    }

    public void RefreshVisuals()
    {
        // 이웃 정보는 on-demand lookup으로 처리됨 (no cache needed)
        // ★ Legacy 로직: UpdateBoundarySeeds 대신 Visualizer가 직접 GetNeighborDistance를 사용하므로
        // 별도의 Seed 업데이트 함수 호출이 필요 없을 수 있으나, 확실히 하기 위해 전체 업데이트 예약
        if (_visualizer != null)
        {
            // Debug.Log($"[TerrainChunk] Refreshing Visuals for {ChunkX},{ChunkY}. SR enabled? {_spriteRenderer.enabled}");
            _visualizer.UpdateVisualsFull(ChunkX, ChunkY, SyncBoundaryDistanceWithNeighbors);
        }
        else
        {
            Debug.LogError($"[TerrainChunk] Visualizer is NULL for {ChunkX},{ChunkY}!");
        }
    }

    // ============================================================================================================
    //  LIGHTING & VISUALS
    // ============================================================================================================
    private TerrainLightingCalculator _lightingCalculator;

    public void UpdateBoundaryLighting(bool left, bool right, bool top, bool bottom)
    {
        _lightingCalculator?.UpdateBoundaryLighting(left, right, top, bottom);
    }
    
    /// <summary>
    /// [P0] UpdateVisualsArea 의 onPreChamfer 콜백으로 사용된다.
    /// 이전: 메인 스레드에서 distanceField 픽셀 복사 (블로킹) → ~31ms idle
    /// 현재: BoundarySync Burst Job 을 스케줄 (비블로킹). Chamfer 가 자동으로 의존성에 포함.
    /// </summary>
    public void SyncBoundaryDistanceWithNeighbors()
    {
        _lightingCalculator?.ScheduleBoundarySyncJob();
    }

    public void EnsureJobsCompleted()
    {
        _carvePending = false;   // CompleteJobs 가 _caveHandle 까지 완료시킨다
        _visualizer?.CompleteJobs();
    }

    /// <summary>
    /// LightingBFS가 아직 실행 중이면 완료까지 대기. 이웃 청크가 DistanceField를 읽기 전에 호출.
    /// </summary>
    public void CompleteLighting() => _visualizer?.CompleteLighting();

    /// <summary>[P0] 이웃 청크의 BoundarySync 가 의존성으로 사용할 lighting 핸들.</summary>
    public Unity.Jobs.JobHandle GetLightingHandle()
        => _visualizer != null ? _visualizer.GetLightingHandle() : default;

    /// <summary>[P0] 외부 청크의 BoundarySync 가 우리 distanceField 를 [ReadOnly] 로 읽었음을 등록.</summary>
    public void RegisterDistanceFieldReader(Unity.Jobs.JobHandle h)
        => _visualizer?.RegisterDistanceFieldReader(h);

    // Helpers
    public ChunkData GetData() => _data;
    private void SpawnDebrisParticle(Vector2 worldPos, Color32 color, Vector2 digCenter, Vector2 playerPos = default)
    {
        if (TerrainParticleManager.Instance != null && UnityEngine.Random.value <= TerrainParticleManager.Instance.spawnChance)
        {
            TerrainParticleManager.Instance.SpawnDebris(worldPos, color, digCenter, playerPos);
        }
    }
    private Vector2 PixelToWorldPos(int x, int y) 
    {
        // [Refactored] Simplest form: Local = Pixel / PPU (Since pivot is 0,0)
        float localX = x / pixelsPerUnit;
        float localY = y / pixelsPerUnit;
        return transform.TransformPoint(new Vector2(localX, localY));
    }

    private Vector2Int WorldToPixel(Vector2 worldPos)
    {
        // 1. World -> Local
        Vector2 localPos = transform.InverseTransformPoint(worldPos);

        // 2. Local -> Pixel
        // [Refactored] No offset needed.
        int px = Mathf.FloorToInt(localPos.x * pixelsPerUnit);
        int py = Mathf.FloorToInt(localPos.y * pixelsPerUnit);
        
        return new Vector2Int(px, py);
    }

    public Vector2 GetLocalPositionForPixel(int x, int y)
    {
        // [Refactored] Direct mapping
        float localX = x / pixelsPerUnit;
        float localY = y / pixelsPerUnit;
        return new Vector2(localX, localY);
    }

    /// <summary>
    /// Checks whether a local-space position inside this chunk corresponds
    /// to an "empty" pixel (alpha == 0) in the base terrain data.
    /// Used by InfinityMapManager.IsWorldPositionEmpty.
    /// </summary>
    public bool IsPixelEmptyLocal(Vector2 localPos)
    {
        if (_data == null || !_data.BasePixels.IsCreated)
            return true;

        // 굴 노출 카빙이 이 청크에 떠 있을 수 있다. Dig() 는 **자기 청크만** 잡을 완료시키는데,
        // 노출 연쇄(RevealCaveNow → 이웃 TryRevealCave)가 이웃 청크에 카빙을 새로 걸고
        // 광물 지지 검사(IsWorldPositionEmpty)가 곧바로 그 이웃을 읽는다.
        // (2026-09-04 리포트 [90.30] 예외의 경로)
        EnsureCarveApplied();

        // Inverse of PixelToWorldPos: convert local units back to pixel indices.
        // [Refactored] No half-width offset.
        float xUnitsFromOrigin = localPos.x;
        float yUnitsFromOrigin = localPos.y;

        int px = Mathf.FloorToInt(xUnitsFromOrigin * pixelsPerUnit);
        int py = Mathf.FloorToInt(yUnitsFromOrigin * pixelsPerUnit);

        // Clamp to valid range to be robust near edges.
        px = Mathf.Clamp(px, 0, width - 1);
        py = Mathf.Clamp(py, 0, height - 1);

        if (!_data.IsValid(px, py))
            return true;

        int idx = _data.ToIndex(px, py);
        return _data.BasePixels[idx].a == 0;
    }

    // ============================================================================================================
    //  HELPER METHODS: Border Data Initialization
    // ============================================================================================================
    
    /// <summary>
    /// 특수청크 등 외부에서 border 데이터만 적용할 때 사용 (픽셀 데이터 변경 없음).
    /// </summary>
    public void ApplyBorderDataOnly(Color32[] borderPixels, int borderWidth, int borderHeight,
        Color32[] secondaryBorderPixels, int secondaryBorderWidth, int secondaryBorderHeight, byte secondaryTileId)
    {
        SetBorderData(borderPixels, borderWidth, borderHeight);
        SetSecondaryBorderData(secondaryBorderPixels, secondaryBorderWidth, secondaryBorderHeight, secondaryTileId);
    }

    /// <summary>
    /// 재로드 시 저장된 파진 픽셀 상태를 복원한다.
    /// IChunkInitializer.Initialize() 이후에 호출해야 IndestructibleMask가 유지된다.
    /// ScheduleInitJobOnly()는 호출하지 않음 — 특수청크는 FinishVisualsAfterInit(skipInit:true)로 처리됨.
    /// </summary>
    public void RestoreSavedPixels(Color32[] savedPixels, byte[] savedPixelInfo)
    {
        if (savedPixels != null && savedPixels.Length == _data.BasePixels.Length)
        {
            _data.LoadPixelData(savedPixels);
        }
        if (savedPixelInfo != null && savedPixelInfo.Length == _data.PixelInfo.Length)
            _data.LoadPixelInfo(savedPixelInfo);
        _data.HasBeenModified = true;
        _data.IsColliderDirty = true;
    }

    /// <summary>
    /// Sets main border data with validation. Centralizes border initialization logic.
    /// </summary>
    private void SetBorderData(Color32[] borderPixels, int width, int height)
    {
        if (borderPixels != null && borderPixels.Length > 0)
        {
            if (!_data.BorderData.IsCreated || _data.BorderData.Length != borderPixels.Length)
            {
                if (_data.BorderData.IsCreated) _data.BorderData.Dispose();
                _data.BorderData = new NativeArray<Color32>(borderPixels, Allocator.Persistent);
            }
            else
            {
                _data.BorderData.CopyFrom(borderPixels);
            }
            _data.BorderWidth = width;
            _data.BorderHeight = height;
        }
        else
        {
            // [Fix] Clear border data if input is null/empty
            if (_data.BorderData.IsCreated && _data.BorderData.Length > 0)
            {
                _data.BorderData.Dispose();
                _data.BorderData = new NativeArray<Color32>(0, Allocator.Persistent);
            }
            _data.BorderWidth = 0;
            _data.BorderHeight = 0;
        }
    }
    
    /// <summary>
    /// Sets secondary border data for layer blending.
    /// </summary>
    private void SetSecondaryBorderData(Color32[] pixels, int width, int height, byte tileId)
    {
        if (pixels != null && pixels.Length > 0)
        {
            _data.SetSecondaryBorderData(pixels, width, height, tileId);
        }
        else
        {
            _data.ClearSecondaryBorderData();
        }
    }

    public bool IsJobRunning()
    {
        if (_visualizer != null)
        {
            return _visualizer.IsJobRunning();
        }
        return false;
    }

    /// <summary>Visual Job만 완료 여부 확인 (Init/Chamfer는 무시).
    /// outputTexture는 Visual Job만 쓰므로, true면 텍스처 업로드 안전.
    /// </summary>
    public bool IsVisualJobCompleted()
        => _visualizer == null || _visualizer.IsVisualJobCompleted();

}