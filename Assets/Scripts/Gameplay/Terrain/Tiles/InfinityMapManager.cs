// @tags: chunk, manager, pipeline, terrain, generation, digging, save
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public partial class InfinityMapManager : MonoBehaviour, IChunkProvider, ITerrainManager
{
    [Header("References (Inspector Only)")]
    public GameObject chunkPrefab;
    public Transform player;

    [Tooltip("층별 청크 배경 테이블. 비우면 배경이 표시되지 않는다.")]
    [SerializeField] private ChunkBackgroundTableSO chunkBackgroundTable;

    [Tooltip("벽타기 판정면(배경 자식의 Wall 트리거) 상한 월드 Y. 이 위로는 벽타기 불가. 구 BackgroundManager.ceilingLevel 값")]
    [SerializeField] private float wallClimbCeilingY = 9f;

    [Header("Diagnostics")]
    [Tooltip("청크 로딩 진단 로그 (문제 해결 후 끄기)")]
    public bool debugChunkLoading = false;

    // [Refactored] Use TileVisualSettings ScriptableObject
    public TileVisualSettings tileVisualSettings;

    // ─── JSON 로드 설정 (worldSettings.json) ──────────────────────
    // 아래 값들은 Awake 에서 WorldSettingsLoader 로 덮어써진다.
    // Inspector 값은 JSON 파일이 없을 때 fallback 으로만 사용된다.
    [Header("Fallback (JSON 없을 때만 사용)")]
    public int viewDistance = 1;
    public bool enableMemoryCache = true;
    public bool enableDiskSave = false;
    public bool enableLayerBlending = true;
    [Range(10f, 128f)] public float globalLightFalloff = 30f;
    [SerializeField] private int chunkSpawningBatchSize = 4;
    [SerializeField] private int maxTimePerBatchMs = 6;
    [SerializeField] private int maxTimePerFramePh2Ms = 5;

    public float chunkWidthWorld => ChunkCoords.WorldSize;
    public float chunkHeightWorld => ChunkCoords.WorldSize;
    // #12 [Fix] Named constant replacing magic number 99999.
    private const int COORD_UNINIT = int.MaxValue;
    private Vector2Int lastChunkCoord = new Vector2Int(COORD_UNINIT, COORD_UNINIT);
    
    // [NEW] Sub-Systems
    private GridCoordinateSystem _gridSystem;
    private ChunkPool _chunkPool;
    private ActiveChunkRegistry _chunkRegistry;
    private WorldPersistenceSystem _persistenceSystem;
    private ChunkLoadingRunner _loadingRunner;
    
    private ChunkSpawner _chunkSpawner;
    private ChunkDataProvider _chunkDataProvider;

    // [New] World Seed for Deterministic Generation
    public int worldSeed;

    private int sourceWidth, sourceHeight;

    // ============================================================================================================
    //  [LateUpdate Pattern] DIRTY CHUNK TRACKING
    // ============================================================================================================
    // 청크 등록 순서를 유지하는 리스트 (자신이 이웃보다 먼저 등록됨)
    private readonly List<TerrainChunk> _dirtyChunksOrdered = new List<TerrainChunk>(8);
    // 청크별 dirty rect (같은 청크를 여러 번 답으면 rect 포함)
    private readonly Dictionary<TerrainChunk, RectInt> _dirtyRects = new Dictionary<TerrainChunk, RectInt>(8);
    // 콜라이더 갱신이 필요한 청크 집합 — LateUpdate에서 전체 순회 대신 이 집합만 순회
    private readonly HashSet<TerrainChunk> _dirtyColliderChunks = new HashSet<TerrainChunk>();
    // [Fix] 실행 중인 ProcessDirtyChunksAsync 코루틴 추적
    private int _activeDirtyProcessCount = 0;

    // ============================================================================================================
    //  [P1-3] 언로드 순회용 재사용 버퍼 (GC 할당 감소)
    // ============================================================================================================
    private readonly List<Vector2Int> _tempCoordBuffer = new List<Vector2Int>(64);

    // ============================================================================================================
    //  [P2-1] BuildLoadQueue 재사용 버퍼
    // ============================================================================================================
    private readonly List<Vector2Int> _buildQueueBuffer = new List<Vector2Int>(32);
    private readonly List<Vector2Int> _anchorsToAddBuffer = new List<Vector2Int>();

    // ============================================================================================================
    //  현재 플레이어 기준 2x2 청크 블록 방향 캐시 (UpdateLoadDirection에서 갱신)
    // ============================================================================================================
    private int _loadDx = 1;
    private int _loadDy = -1;

    // ============================================================================================================
    //  [P1-2] 비동기 언로드 큐
    // ============================================================================================================
    private readonly Queue<Vector2Int> _unloadQueue = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> _pendingUnloadSet = new HashSet<Vector2Int>();
    private bool _isUnloadRoutineRunning = false;
    // [GC-Fix] 코루틴 재진입마다 new 하던 Stopwatch를 필드로 승격
    private readonly System.Diagnostics.Stopwatch _unloadSw = new System.Diagnostics.Stopwatch();

    // ============================================================================================================
    //  [P1-1] 예측 로딩 임계값
    // ============================================================================================================
    private const float PRELOAD_THRESHOLD = 0.35f; // 청크 크기의 35% 이내 진입 시 사전 로드

    // ============================================================================================================
    //  [텍스처 쓰로틀링] GPU 업로드 주기 제한
    // ============================================================================================================
    // 0.05초(20fps)마다 한 번만 texture.Apply() 호출 → 연속 파기 시 GPU 업로드 횟수 대폭 감소
    private float _lastTextureApplyTime = -999f;
    private float _textureUpdateInterval = 0f; // JSON 로드 후 덮어써짐

    // [A/B 안전장치] Round 1에서도 Visual 을 돌려 조기 프리뷰를 만들지 여부.
    // static 이 아니다 — 파이프라인의 주인인 이 매니저만 알면 되고, 각 Pass 호출에 인자로 내려간다.
    // (배경: Assets/Docs/job-pipeline-waste-removal.md §3.4, §5.5)
    private bool _enableRound1Preview = false;

    // ============================================================================================================
    //  [P-GC] LateUpdate GC 제거용 재사용 버퍼·풀
    // ============================================================================================================
    // GetAll() yield 이터레이터 대신 버퍼 순회로 state machine 할당 제거
    private readonly List<IChunk> _allChunksBuffer = new List<IChunk>(64);
    // 스냅샷 List 풀링 — dirty 프레임마다 new List<> 하던 것을 재사용
    // 튜플: (청크, dirty rect, rect 유무, Step1에서 Init이 실제로 스케줄됐는지)
    private readonly Stack<List<(TerrainChunk, RectInt, bool, bool)>> _snapshotPool
        = new Stack<List<(TerrainChunk, RectInt, bool, bool)>>();
    // RemoveWhere 람다 캐싱 — Mono에서 매 호출마다 delegate 객체 생성되는 것을 방지
    private static readonly System.Predicate<TerrainChunk> s_colliderUpdatePredicate = tc =>
    {
        if (tc == null || !tc.gameObject.activeSelf) return true;
        tc.TryUpdateCollider();
        return !tc.isDirty;
    };

    /// <summary>
    /// Dig()에서 호출. dirty rect로 청크 등록. 같은 청크가 여러 번이면 rect union 처리.
    /// </summary>
    public void MarkChunkDirty(TerrainChunk chunk, RectInt rect)
    {
        if (chunk == null) return;
        if (_dirtyRects.TryGetValue(chunk, out RectInt existing))
        {
            int x0 = Mathf.Min(existing.xMin, rect.xMin);
            int y0 = Mathf.Min(existing.yMin, rect.yMin);
            int x1 = Mathf.Max(existing.xMax, rect.xMax);
            int y1 = Mathf.Max(existing.yMax, rect.yMax);
            _dirtyRects[chunk] = new RectInt(x0, y0, x1 - x0, y1 - y0);
        }
        else
        {
            _dirtyRects[chunk] = rect;
            _dirtyChunksOrdered.Add(chunk);
        }
        _dirtyColliderChunks.Add(chunk);

        // 지도 스냅샷도 이 영역만 낡았다고 표시한다. 이게 없으면 미니맵이 근처 청크를
        // 매 렌더 통째로 다시 굽게 되어 프레임의 큰 몫을 잡아먹는다.
        // 누적본(_dirtyRects)이 아니라 이번 변경분(rect)을 넘긴다 — 캐시는 자체 병합분을
        // Capture마다 비우므로, 파이프라인이 처리할 때까지 계속 커지는 누적 rect를 쓰면
        // 이미 다시 구운 영역을 반복해서 또 굽게 된다.
        MapTerrainCache.Invalidate(chunk, rect);
    }

    /// <summary>
    /// 경계를 넘은 이웃 청크의 지정 영역을 dirty로 등록.
    /// 자신보다 나중에 Add되어 LateUpdate에서 소스 청크 완료 후 처리 보장.
    /// [Opt] 전체 청크 대신 영향받는 경계 스트립만 마크 → BFS 비용 대폭 감소.
    /// </summary>
    public void MarkNeighborDirty(int cx, int cy, RectInt dirtyRect)
    {
        var neighbor = _chunkRegistry?.Get(new Vector2Int(cx, cy)) as TerrainChunk;
        if (neighbor == null) return;
        MarkChunkDirty(neighbor, dirtyRect);
    }

    private List<(TerrainChunk, RectInt, bool, bool)> RentSnapshot(int capacity)
    {
        var list = _snapshotPool.Count > 0
            ? _snapshotPool.Pop()
            : new List<(TerrainChunk, RectInt, bool, bool)>(capacity);
        list.Clear();
        return list;
    }

    private void ReturnSnapshot(List<(TerrainChunk, RectInt, bool, bool)> snapshot) => _snapshotPool.Push(snapshot);

    // 싱글턴
    public static InfinityMapManager Instance;

    /// <summary>땅이 파졌을 때 발생하는 이벤트. Vector2 = 월드 파기 위치, float = 파기 반경(월드 유닛).</summary>
    public static event Action<Vector2, float> OnTerrainModified;
    
    void Awake() 
    {
        // Singleton pattern: Keep first instance, destroy duplicates
        if (Instance == null)
        {
            Instance = this;
            LoadingData.IsReady = false; // [Fix] 로딩 화면 무조건 잠금

            // [Background] 층별 배경 테이블 주입.
            // static인 이유는 Instantiate가 non-serialized 필드를 복사하지 않기 때문
            // (SetDefaultColliderUpdateInterval과 같은 패턴). 청크 생성 전에 넣어야 한다.
            ChunkBackground.ResetDiagnostics();
            ChunkBackground.Table = chunkBackgroundTable;
            ChunkBackground.WallCeilingY = wallClimbCeilingY;
            if (chunkBackgroundTable == null)
                Debug.LogWarning("[ChunkBackground] InfinityMapManager의 chunkBackgroundTable 슬롯이 비어 있다 → 배경 미표시.");

            // FixedUpdate 스파이럴 방지: 긴 프레임에서 FixedUpdate 반복 횟수를 제한한다.
            // 드릴 파기는 매 FixedUpdate마다 TerrainChunk.Dig → EnsureJobsCompleted(CompleteAllJobs)로
            // 지형 잡 체인 전체를 동기 완료(메인 스레드 블로킹)한다. 캐치업이 쌓이면 이 무거운 동기화가
            // 프레임당 여러 번 반복돼 랙이 증폭되므로, 최대 2회(0.04s/0.02s)로 낮춰 악순환을 끊는다.
            // 트레이드오프: 심한 랙 순간 물리/게임 시간이 잠깐 느려짐(슬로모션) — 히칭보다는 안전.
            Time.maximumDeltaTime = 0.04f;

            StartCoroutine(InitializeCoroutine());
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    IEnumerator InitializeCoroutine()
    {
        // Step 0: JSON 설정 적용 (WorldSettingsLoader 는 Execution Order -200 으로 먼저 Awake)
        if (WorldSettingsLoader.Instance != null)
            ApplyWorldSettings(WorldSettingsLoader.Instance.Settings);

        InitializeWorldSeed();

        // [Fix] Ensure Visual Settings exist
        if (tileVisualSettings == null)
        {
            tileVisualSettings = Resources.Load<TileVisualSettings>("TileVisualSettings");
            if (tileVisualSettings == null)
            {
                Debug.LogError("[InfinityMapManager] 초기화 중단 — tileVisualSettings 가 null " +
                               "(Inspector 미연결 + Resources/TileVisualSettings 로드 실패). 청크가 생성되지 않습니다.");
                yield break;
            }
        }

        // 조각 VFX 풀은 지형별 티어 버킷을 여기서 주입받아 최초 파괴 시 1회 빌드한다.
        RockFragmentPool.SetSettings(tileVisualSettings);

        // 1. Initialize Core Systems
        // [FIX] Wait for TileDataManager to initialize (it might be initializing in parallel)
        float waitTime = 0f;
        const float MAX_WAIT_TIME = 5f;

        while (TileDataManager.Instance == null && waitTime < MAX_WAIT_TIME)
        {
            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;
        }

        if (TileDataManager.Instance == null)
        {
            Debug.LogError("[InfinityMapManager] 초기화 중단 — TileDataManager.Instance 가 5초 내 초기화되지 않음. 청크가 생성되지 않습니다.");
            yield break;
        }

        _gridSystem = new GridCoordinateSystem(TileDataManager.Instance.terrainWidth, TileDataManager.Instance.terrainDepth);
        _chunkPool = new ChunkPool();
        _chunkRegistry = new ActiveChunkRegistry(_gridSystem);
        _persistenceSystem = new WorldPersistenceSystem(_gridSystem);
        _persistenceSystem.SetActiveChunkRegistry(_chunkRegistry); // Link for saving

        // 2. Initialize Runner
        _loadingRunner = gameObject.AddComponent<ChunkLoadingRunner>();

        // 3. Load Data
        if (enableMemoryCache && enableDiskSave)
            _persistenceSystem.LoadAllDataFromDisk();

        InitializeResources();
        InitializePlayerChunkCoord();
        InitializeChunkSpawner();

        // 4. Initialize Runner with Dependencies
        _loadingRunner.Initialize(
            _chunkRegistry,
            _chunkSpawner,
            _chunkPool,
            _chunkDataProvider,
            _gridSystem,
            player,
            chunkSpawningBatchSize,
            maxTimePerBatchMs,
            maxTimePerFramePh2Ms
        );
        UpdateChunks();

        // [Fix] 초기 생성된 청크가 모두 화면에 렌더링(Job 완료) 될 때까지 대기
        yield return new WaitWhile(() => _loadingRunner != null && _loadingRunner.IsLoadingRoutineRunning);

        // [Fix] Dirty 처리는 LateUpdate에서 수행되는데, LateUpdate는 _isInitialized가 true여야 동작함
        // 데드락 방지를 위해 _isInitialized를 먼저 켜고 LateUpdate가 dirty를 처리하도록 허용
        _isInitialized = true;

        // [Pre-warm] 초기 청크 로드 완료 후 풀 사전 적재 — Dirty 처리와 병렬 실행 (비블로킹)
        // UpdateChunks() 이전이 아닌 이 시점에 두는 이유:
        //   이전에 두면 Instantiate Awake 비용이 6프레임 쌓여 ImageChunkOverrider 타이머(10초)를 초과함.
        StartCoroutine(_loadingRunner.PrewarmPoolAsync(chunkPrefab, transform, 6));

        // [Fix] 후속 처리로 예약된 Dirty Chunks 의 비주얼 스케줄(ProcessDirtyChunksAsync)이 완료될 때까지 추가 대기
        yield return new WaitWhile(() => _dirtyChunksOrdered.Count > 0 || _activeDirtyProcessCount > 0);

        // [추가] Job은 끝났지만 텍스처(GPU) 업로드가 지연된 청크들을 강제 즉시 텍스처 적용
        foreach (var chunk in _chunkRegistry.GetAll())
        {
            var tc = chunk as TerrainChunk;
            if (tc != null && tc.isTextureDirty)
            {
                tc.ApplyTexture();
            }
        }
        
        // GPU에 텍스처가 올라가고 실제로 화면에 렌더링될 수 있도록 여유 프레임 2번 확보
        yield return null;
        yield return null;

        LoadingData.IsReady = true;
        yield return null;
    }

    private void ApplyWorldSettings(WorldSettingsData s)
    {
        viewDistance             = s.world.viewDistance;
        worldSeed                = s.world.seed;
        enableMemoryCache        = s.save.enableMemoryCache;
        enableDiskSave           = s.save.enableDiskSave;
        enableLayerBlending      = s.rendering.enableLayerBlending;
        globalLightFalloff       = s.rendering.globalLightFalloff;
        _textureUpdateInterval   = s.rendering.textureUpdateInterval;
        _enableRound1Preview     = s.rendering.enableRound1Preview;
        chunkSpawningBatchSize   = s.performance.chunkSpawningBatchSize;
        maxTimePerBatchMs        = s.performance.maxTimePerBatchMs;
        maxTimePerFramePh2Ms     = s.performance.maxTimePerFramePh2Ms;
        TerrainChunk.SetDefaultColliderUpdateInterval(s.chunk.colliderUpdateInterval);
        TerrainChunk.SetDefaultIslandRemoval(s.chunk.useIslandRemoval, s.chunk.maxIslandSize);
        TerrainCollider.SetDefaultSimplifyTolerance(s.chunk.colliderSimplifyTolerance);

        // [rim] 최외곽 테두리 (Assets/Docs/terrain-rim-outline.md)
        // JsonUtility 는 hex → Color32 변환을 못 하므로 string 으로 받아 여기서 파싱한다.
        if (!ColorUtility.TryParseHtmlString(s.chunk.rimColor, out Color rimColor))
        {
            Debug.LogWarning($"[InfinityMapManager] chunk.rimColor 파싱 실패: '{s.chunk.rimColor}' — 기본색 사용");
            rimColor = new Color32(0x24, 0x10, 0x09, 255);
        }
        TerrainChunk.SetRimSettings(s.chunk.rimThicknessPx, rimColor);
    }

    private void InitializeWorldSeed()
    {
        if (worldSeed == 0) worldSeed = UnityEngine.Random.Range(1000, 99999);
    }

    private void InitializeResources()
    {
        TerrainChunk chunk = chunkPrefab.GetComponent<TerrainChunk>();
        int fallbackW = chunk != null ? chunk.width : 1000;
        int fallbackH = chunk != null ? chunk.height : 1000;

        if (TileDataManager.Instance != null && tileVisualSettings != null)
        {
            TileDataManager.Instance.InitializeResources(tileVisualSettings.settings, fallbackW, fallbackH);
            sourceWidth = TileDataManager.Instance.sourceWidth;
            sourceHeight = TileDataManager.Instance.sourceHeight;
        }
    }

    private void InitializePlayerChunkCoord()
    {
        // [Safety Check] Ensure we have the correct player reference
        if (player == null)
        {
            // 1. Try finding by PlayerController (most reliable)
            var controller = FindFirstObjectByType<PlayerController>();
            if (controller != null)
            {
                player = controller.transform;
                Debug.Log($"[InfinityMapManager] Auto-assigned Player to found PlayerController: {player.name}");
            }
            // 2. Try finding by Tag "Player"
            else 
            {
                var playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                {
                    player = playerObj.transform;
                    Debug.Log($"[InfinityMapManager] Auto-assigned Player to object with tag 'Player': {player.name}");
                }
                // 3. Fallback to FlashlightController (Legacy)
                else
                {
                    var flashlight = FindFirstObjectByType<FlashlightController>();
                    if (flashlight != null)
                    {
                        player = flashlight.transform;
                        Debug.LogWarning($"[InfinityMapManager] Player field was empty. Auto-assigned to found FlashlightController at {player.position}");
                    }
                }
            }
        }

        if (player != null)
            lastChunkCoord = CalculatePlayerChunkCoord();
        else
            lastChunkCoord = Vector2Int.zero;

        if (debugChunkLoading)
        {
            string pName = player != null ? player.name : "NULL";
            Vector3 pPos = player != null ? player.position : Vector3.zero;
            Debug.Log($"[ChunkDiag] Init player='{pName}' pos={pPos} chunkWorldSize={chunkWidthWorld} " +
                      $"→ playerChunk={lastChunkCoord} viewDistance={viewDistance}\n" +
                      $"   (0,0) skip? {ShouldSkipChunkGeneration(Vector2Int.zero)} / " +
                      $"playerChunk skip? {ShouldSkipChunkGeneration(lastChunkCoord)}");
        }
    }

    private void InitializeChunkSpawner()
    {
        // 1. Create Data Provider (Gathering Logic)
        _chunkDataProvider = new ChunkDataProvider(
            tileVisualSettings,
            _persistenceSystem,
            worldSeed,
            enableLayerBlending
        );

        // 2. Create Spawner (Factory)
        _chunkSpawner = new ChunkSpawner(
            chunkPrefab,
            transform,
            player,
            tileVisualSettings, 
            _chunkPool,
            worldSeed,
            this // [New] IChunkProvider
        );
    }

    private bool _isInitialized = false;

    void Update()
    {
        if (!_isInitialized) return;
        HandlePlayerChunkTracking();
    }

    void LateUpdate()
    {
        if (!_isInitialized) return;

        // ================================================================
        //  Dirty 청크 비동기 처리: 매 LateUpdate마다 스냅샷 후 코루틴 시작
        //  각 코루틴은 독립 스냅샷을 가지므로 동시 실행 안전
        // ================================================================
        if (_dirtyChunksOrdered.Count > 0)
        {
            // [P-GC] 풀에서 꺼낸 List 재사용 — new List<> 할당 제거
            var snapshot = RentSnapshot(_dirtyChunksOrdered.Count);
            foreach (var chunk in _dirtyChunksOrdered)
            {
                if (chunk == null || !chunk.gameObject.activeSelf) continue;
                bool hasRect = _dirtyRects.TryGetValue(chunk, out RectInt rect);
                snapshot.Add((chunk, rect, hasRect, false)); // initScheduled는 Step 1에서 확정
            }
            _dirtyChunksOrdered.Clear();
            _dirtyRects.Clear();
            StartCoroutine(ProcessDirtyChunksAsync(snapshot));
        }

        // ====================================================================
        //  GPU 텍스처 적용: IsVisualDirty 상태인 모든 활성 청크
        //  [Guard] IsVisualJobCompleted() 확인 — Visual Job만 완료 여부 체크.
        //  Init/Chamfer는 distanceField만 쓰고 outputTexture는 건드리지 않으므로
        //  Visual Job 완료 시점에 즉시 업로드 가능.
        //  (기존 !IsJobRunning() 조건은 ImmediateDig 연속 파기 중 Init Job이
        //   항상 예약되어 텍스처 업로드가 영구 차단되는 버그를 유발했음)
        // ====================================================================
        if (_chunkRegistry != null && (Time.time - _lastTextureApplyTime) >= _textureUpdateInterval)
        {
            // [P-GC] GetAll() yield 이터레이터 → 재사용 버퍼 순회로 state machine 할당 제거
            _chunkRegistry.CopyAllChunksTo(_allChunksBuffer);
            for (int i = 0; i < _allChunksBuffer.Count; i++)
            {
                var tc = _allChunksBuffer[i] as TerrainChunk;
                if (tc != null && tc.isTextureDirty && tc.IsVisualJobCompleted())
                    tc.ApplyTexture();
            }
            _lastTextureApplyTime = Time.time;
        }

        // ====================================================================
        //  콜라이더 갱신: IsColliderDirty 상태인 청크만 순회 (전체 청크 순회 불필요)
        //  [Decoupled] IsVisualJobCompleted()와 무관하게 독립 실행.
        //  콜라이더는 BasePixels(CPU)를 직접 읽으므로 GPU Visual Job 완료를 기다릴 필요 없음.
        //  (상세 내용: Assets/Docs/collider-visual-decoupling.md 참고)
        // ====================================================================
        if (_dirtyColliderChunks.Count > 0)
        {
            // [P-GC] static readonly Predicate 캐시 사용 — 매 프레임 delegate 객체 생성 방지
            _dirtyColliderChunks.RemoveWhere(s_colliderUpdatePredicate);
        }
    }

    /// <summary>
    /// Dirty 청크의 Round1 → Round2 조명 처리를 여러 프레임에 걸쳐 비동기로 수행.
    /// CompleteLighting / EnsureJobsCompleted 블로킹을 yield로 대체.
    /// </summary>
    private IEnumerator ProcessDirtyChunksAsync(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        _activeDirtyProcessCount++;
        try
        {
            SnapshotScheduleInitJobs(snapshot);
            yield return null;
            SnapshotScheduleRound1(snapshot);
            while (!SnapshotAreAllJobsDone(snapshot)) yield return null;
            SnapshotCompleteLighting(snapshot);
            SnapshotScheduleRound2(snapshot);
            while (!SnapshotAreAllJobsDone(snapshot)) yield return null;
            SnapshotFinalizeAndApply(snapshot);
        }
        finally
        {
            _activeDirtyProcessCount--;
            ReturnSnapshot(snapshot); // [P-GC] 풀에 반납 — 다음 dirty 프레임에서 재사용
        }
    }

    /// <summary>
    /// Step 1: Init 잡 선스케줄. 이전 Init이 아직 돌고 있으면 스킵된다.
    /// 스킵 여부를 스냅샷에 기록해 Round 1이 대신 수행하게 한다 — Init은 사이클당 정확히 1회.
    /// (배경: Assets/Docs/job-pipeline-waste-removal.md §3.2)
    /// </summary>
    private void SnapshotScheduleInitJobs(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        for (int i = 0; i < snapshot.Count; i++)
        {
            var (chunk, rect, hasRect, _) = snapshot[i];
            if (chunk == null || !chunk.gameObject.activeSelf) continue;

            // hasRect=false(전체 갱신)는 여기서 스케줄하지 않는다 → Round 1이 full-chunk Init 수행
            bool scheduled = hasRect && chunk.ScheduleInitOnlyIfReady(rect);
            snapshot[i] = (chunk, rect, hasRect, scheduled);
        }
    }

    /// <summary>
    /// Step 2: Round 1 — 자기 청크 거리장만 확정한다. 이웃이 Round 2에서 읽을 값이다.
    /// BoundarySync·Upsample·Visual 없음(§3.3, §3.4).
    /// Step 1에서 Init이 스킵됐으면(initScheduled=false) 여기서 대신 수행한다.
    /// </summary>
    private void SnapshotScheduleRound1(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        foreach (var (chunk, rect, hasRect, initScheduled) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            chunk.ScheduleDistancePass(rect, hasRect,
                                       skipInit: initScheduled,
                                       withPreview: _enableRound1Preview);
        }
    }

    /// <summary>Steps 3, 5: 스냅샷 내 모든 청크 잡 완료 여부 확인.</summary>
    private bool SnapshotAreAllJobsDone(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        foreach (var (chunk, _, _, _) in snapshot)
        {
            if (chunk != null && chunk.gameObject.activeSelf && chunk.IsJobRunning())
                return false;
        }
        return true;
    }

    // [LAGDIAG] 임시 진단 플래그. 게이트된 CompleteAll/pipeline 로그를 일괄 on/off 한다.
    // 근원(풀 청크 chamfer = 청크 로드) 확인용으로 필요 시 true로. 평상시 false 유지(로그 폭주·GC 방지).
    public static bool LagDiag = false;

    // [LAGDIAG] 이 값(ms)을 넘는 sync point만 로그한다.
    // 잡 파이프라인 최적화(16→11잡 + rect 스코핑) 이후엔 3ms 스파이크가 거의 안 나오므로,
    // 실제 수치를 보려면 0.5 이하로 낮춰야 한다. 0.0으로 두면 매 CompleteAll을 찍는다(스팸 주의).
    // 실측: Assets/Docs/job-pipeline-waste-removal.md §6.1
    public static double LagDiagThresholdMs = 0.5;
    // [LAGDIAG] 워커 포화 판단용 — 현재 대기 중인 dirty 청크 수.
    public int DirtyChunkCountDbg => _dirtyChunksOrdered.Count;

    /// <summary>Step 3 후처리: Round 1 완료 후 CompleteLighting 호출.</summary>
    private void SnapshotCompleteLighting(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        var __sw = LagDiag ? System.Diagnostics.Stopwatch.StartNew() : null;
        foreach (var (chunk, _, _, _) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            chunk.CompleteLighting();
        }
        if (__sw != null) { __sw.Stop(); double ms = __sw.Elapsed.TotalMilliseconds;
            if (ms > LagDiagThresholdMs) Debug.Log($"[LAGDIAG] pipeline CompleteLighting {ms:F1}ms chunks={snapshot.Count} frame={Time.frameCount}"); }
    }

    /// <summary>
    /// Step 4: Round 2 — 이웃 거리장 동기화(BoundarySync) 후 Chamfer → Upsample → Visual.
    /// Init은 하지 않는다 — Round 1이 남긴 DF는 참값의 상한이고 Chamfer는 min-전파이므로
    /// 리셋 없이도 같은 고정점에 수렴한다(§3.1, ChamferConvergenceTests가 증명).
    /// </summary>
    private void SnapshotScheduleRound2(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        foreach (var (chunk, rect, hasRect, _) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            chunk.ScheduleSyncAndVisualPass(rect, hasRect);
        }
    }

    /// <summary>
    /// Step 6: 텍스처 즉시 업로드. Round 2 wait loop 직후 — Visual Job 완료 보장 시점.
    /// GPU 스로틀 루프와의 중복 업로드는 ApplyTexture 내부 IsVisualDirty 가드가 방지한다.
    /// (배경: Assets/Docs/collider-visual-decoupling.md 참고)
    /// </summary>
    private void SnapshotFinalizeAndApply(
        List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)> snapshot)
    {
        var __sw = LagDiag ? System.Diagnostics.Stopwatch.StartNew() : null;
        foreach (var (chunk, _, _, _) in snapshot)
        {
            if (chunk == null || !chunk.gameObject.activeSelf) continue;
            chunk.EnsureJobsCompleted();
            chunk.ApplyTexture();
        }
        if (__sw != null) { __sw.Stop(); double ms = __sw.Elapsed.TotalMilliseconds;
            if (ms > LagDiagThresholdMs) Debug.Log($"[LAGDIAG] pipeline FinalizeAndApply {ms:F1}ms chunks={snapshot.Count} frame={Time.frameCount}"); }
    }

    private void HandlePlayerChunkTracking()
    {
        if (!player) 
        {
            // Throttled warning to avoid spam
            if (Time.frameCount % 300 == 0) // Every ~5 seconds at 60fps
            {
                Debug.LogWarning("[InfinityMapManager] HandlePlayerChunkTracking: Player is null! Update is running but player reference is missing.");
            }
            return;
        }

        Vector2Int currentChunkCoord = CalculatePlayerChunkCoord();

        if (currentChunkCoord != lastChunkCoord)
        {
            lastChunkCoord = currentChunkCoord;
            UpdateChunks();
        }

        TryPreloadNeighborChunks(); // [P1-1] 경계 근접 시 이웃 청크 사전 로드
    }

    // [P1-1] 플레이어가 청크 경계의 PRELOAD_THRESHOLD 이내에 들어오면 이웃 청크를 미리 큐에 넣는다.
    private void TryPreloadNeighborChunks()
    {
        float cx = player.position.x / chunkWidthWorld;
        float cy = player.position.y / chunkHeightWorld;
        float fx = cx - Mathf.Floor(cx); // 청크 내 x 비율 [0,1]
        float fy = cy - Mathf.Floor(cy); // 청크 내 y 비율 [0,1]

        bool added = false;

        void TryAdd(Vector2Int coord)
        {
            if (!_chunkRegistry.HasChunk(coord) && !_pendingUnloadSet.Contains(coord) && !ShouldSkipChunkGeneration(coord))
            {
                _loadingRunner.PrependToQueue(new[] { coord });
                added = true;
            }
        }

        if (fx > 1f - PRELOAD_THRESHOLD) TryAdd(new Vector2Int(lastChunkCoord.x + 1, lastChunkCoord.y));
        if (fx < PRELOAD_THRESHOLD)       TryAdd(new Vector2Int(lastChunkCoord.x - 1, lastChunkCoord.y));
        if (fy > 1f - PRELOAD_THRESHOLD)  TryAdd(new Vector2Int(lastChunkCoord.x, lastChunkCoord.y + 1));
        if (fy < PRELOAD_THRESHOLD)        TryAdd(new Vector2Int(lastChunkCoord.x, lastChunkCoord.y - 1));

        // [Fix] 대각선 사전 로드: 두 축 모두 경계 근처일 때 해당 대각선 청크를 미리 큐에 추가
        if (fx > 1f - PRELOAD_THRESHOLD && fy > 1f - PRELOAD_THRESHOLD) TryAdd(new Vector2Int(lastChunkCoord.x + 1, lastChunkCoord.y + 1));
        if (fx > 1f - PRELOAD_THRESHOLD && fy < PRELOAD_THRESHOLD)       TryAdd(new Vector2Int(lastChunkCoord.x + 1, lastChunkCoord.y - 1));
        if (fx < PRELOAD_THRESHOLD       && fy > 1f - PRELOAD_THRESHOLD) TryAdd(new Vector2Int(lastChunkCoord.x - 1, lastChunkCoord.y + 1));
        if (fx < PRELOAD_THRESHOLD       && fy < PRELOAD_THRESHOLD)       TryAdd(new Vector2Int(lastChunkCoord.x - 1, lastChunkCoord.y - 1));

        if (added)
            _loadingRunner.StartLoadingIfNeeded();
    }

    private Vector2Int CalculatePlayerChunkCoord()
    {
        if (player == null) return Vector2Int.zero;
        int x = Mathf.FloorToInt(player.position.x / chunkWidthWorld);
        int y = Mathf.FloorToInt(player.position.y / chunkHeightWorld);
        return new Vector2Int(x, y);
    }

    private void UpdateLoadDirection()
    {
        if (player == null) return;
        float cx = player.position.x / chunkWidthWorld;
        float cy = player.position.y / chunkHeightWorld;
        float fx = cx - Mathf.Floor(cx);
        float fy = cy - Mathf.Floor(cy);
        _loadDx = fx >= 0.5f ? 1 : -1;
        _loadDy = fy >= 0.5f ? 1 : -1;
    }

    void UpdateChunks()
    {
        UpdateLoadDirection();
        UnloadDistantChunks();

        List<Vector2Int> loadQueue = BuildLoadQueue();

        if (debugChunkLoading)
        {
            string q = loadQueue.Count == 0 ? "(비어있음!)" : string.Join(", ", loadQueue);
            Debug.Log($"[ChunkDiag] UpdateChunks center={lastChunkCoord} dir=({_loadDx},{_loadDy}) " +
                      $"queue=[{q}] | 이미로드(0,0)?={_chunkRegistry.HasChunk(Vector2Int.zero)}");
        }

        _loadingRunner.UpdateLoadQueue(loadQueue);   // 거리 순 정렬 포함
        PrependSubChunkAnchors(loadQueue);           // 정렬 후 앵커를 큐 앞에 삽입
        _loadingRunner.StartLoadingIfNeeded();
    }

    // [P1-2] 언로드 대상 좌표를 큐에만 넣고 즉시 반환. 실제 처리는 ProcessUnloadQueue 코루틴에서 분산.
    private void UnloadDistantChunks()
    {
        _chunkRegistry.CopyActiveCoordinatesTo(_tempCoordBuffer); // [P1-3] GC 없는 버퍼 재사용

        foreach (var coord in _tempCoordBuffer)
        {
            if (IsChunkOutOfRange(coord) && _pendingUnloadSet.Add(coord))
                _unloadQueue.Enqueue(coord);
        }

        if (!_isUnloadRoutineRunning && _unloadQueue.Count > 0)
            StartCoroutine(ProcessUnloadQueue());
    }

    // [P1-2] 프레임당 4ms 예산으로 언로드 큐를 분산 처리.
    private IEnumerator ProcessUnloadQueue()
    {
        _isUnloadRoutineRunning = true;

        while (_unloadQueue.Count > 0)
        {
            _unloadSw.Restart();
            while (_unloadQueue.Count > 0 && _unloadSw.ElapsedMilliseconds < 4)
            {
                var coord = _unloadQueue.Dequeue();
                _pendingUnloadSet.Remove(coord);
                DoUnloadChunk(coord);
            }
            yield return null;
        }

        _isUnloadRoutineRunning = false;
    }

    private void DoUnloadChunk(Vector2Int coord)
    {
        // 플레이어가 방향을 바꿔 다시 범위 안에 들어왔으면 취소
        if (!IsChunkOutOfRange(coord)) return;

        var chunk = _chunkRegistry.Get(coord);
        if (chunk == null) return; // 이미 제거됨

        var unityObj = chunk as UnityEngine.Object;
        if (unityObj != null)
        {
            if (chunk is TerrainChunk tcPool)
            {
                bool isSubChunkChild = SpecialChunkManager.Instance != null &&
                                       SpecialChunkManager.Instance.TryGetAnchorCoord(coord, out _);
                // 특수청크 여부는 스폰 시 마킹된 플래그로 판정한다.
                // 루트 GetComponent<IChunkInitializer>()는 자식에 스크립트를 둔 특수청크(가마솥·던전문)를
                // 놓쳐 일반청크로 오분류 → 저장 오분류(wasNormalChunk) + Instantiate 객체를 풀에 오반납.
                bool hasInit = tcPool.IsSpecialChunkInstance;

                // 청크 자식 광물을 월드 오브젝트로 분리 (PixelInfo 마킹 포함)
                // 저장 전에 실행해야 갱신된 PixelInfo가 저장에 반영된다
                if (!isSubChunkChild)
                    DetachMineralsToWorld(tcPool);

                // [GC-Fix] 수정된 청크만 저장 — 미수정 청크는 씨드로 재생성 가능
                if (enableMemoryCache && !isSubChunkChild && tcPool.hasBeenModified)
                {
                    _persistenceSystem.SaveChunkDataToMemory(coord, tcPool, !hasInit);
                }

                if (isSubChunkChild)
                {
                    // 자식 TC: 앵커 Destroy 시 함께 파괴됨 — pool 반납 금지
                }
                else if (hasInit)
                {
                    // 스페셜 청크 앵커 TC (Instantiate 생성, 비풀 객체):
                    // 서브슬롯 예약 해제 후 Destroy — pool 반납 금지
                    SpecialChunkManager.Instance?.UnregisterSubChunksForAnchor(coord);
                    Destroy(tcPool.gameObject);
                }
                else
                {
                    _chunkPool.Return(tcPool);
                }
            }
            else
            {
                // LargeStaticTerrainChunk 언로드: 서브슬롯 예약도 함께 해제
                SpecialChunkManager.Instance?.UnregisterSubChunksForAnchor(coord);
                Destroy(chunk.gameObject);
            }
        }

        _chunkRegistry.Remove(coord);
    }

    // 청크 자식 MINERAL_* 오브젝트를 처리한다.
    // "MineralDug" 태그(WakeUp 호출된 것)만 월드로 분리 + 60초 타이머를 붙인다.
    // 땅 속에 묻힌 광물은 풀에 반납만 하고 collected 마킹을 하지 않아 청크 재로드 시 재생성된다.
    private void DetachMineralsToWorld(TerrainChunk chunk)
    {
        int childCount = chunk.transform.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            Transform child = chunk.transform.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            if (!child.name.StartsWith("MINERAL_")) continue;

            if (child.CompareTag("MineralDug"))
            {
                // 파진 광물: 월드에 남기고 타이머 부착 + 재스폰 차단
                string[] parts = child.name.Split('_');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[parts.Length - 2], out int px) &&
                    int.TryParse(parts[parts.Length - 1], out int py))
                {
                    MineralGenerator.MarkMineralCollected(chunk, px, py);
                }
                child.SetParent(null, true);
                if (!child.TryGetComponent<MineralLifetime>(out _))
                    child.gameObject.AddComponent<MineralLifetime>();
            }
            else
            {
                // 땅 속 광물: 풀 반납만 (collected 마킹 없음 → 청크 재로드 시 재생성)
                MineralGenerator.ReturnToPool(child.gameObject);
            }
        }
    }

    private bool IsChunkOutOfRange(Vector2Int chunkCoord)
    {
        // 서브청크: 앵커 footprint 기준으로 범위 판정
        if (SpecialChunkManager.Instance != null &&
            SpecialChunkManager.Instance.TryGetAnchorCoord(chunkCoord, out Vector2Int anchorCoord))
        {
            var anchorChunk = _chunkRegistry.Get(anchorCoord);
            if (anchorChunk != null)
            {
                // registry 기반 footprint 사용: LargeStaticTerrainChunk·TerrainChunk 앵커 모두 정확
                var fp = SpecialChunkManager.Instance.GetAnchorFootprint(anchorCoord);
                return IsFootprintOutOfRange(anchorCoord, fp.x, fp.y);
            }
            return Mathf.Abs(anchorCoord.x - lastChunkCoord.x) > viewDistance ||
                   Mathf.Abs(anchorCoord.y - lastChunkCoord.y) > viewDistance;
        }

        // 대형 정적 특수 청크(앵커): footprint 전체가 범위를 벗어났을 때만 언로드
        if (SpecialChunkManager.Instance != null)
        {
            var chunk = _chunkRegistry.Get(chunkCoord);
            if (chunk != null)
            {   
                // If it's a LargeStaticTerrainChunk, it always behaves as static
                bool isStaticBase = chunk is LargeStaticTerrainChunk;
                // If it's a TerrainChunk, we check its (now removed/obsolete, but keep for logic reference) property. Wait, we removed isStaticSpecialChunk. 
                // LargeStaticTerrainChunk now implies it!
                if (isStaticBase)
                {
                    int sx = Mathf.Max(1, chunk.Width / 1000);
                    int sy = Mathf.Max(1, chunk.Height / 1000);
                    return IsFootprintOutOfRange(chunkCoord, sx, sy);
                }
            }
        }

        // 현재 2x2 블록에 포함되지 않으면 범위 초과
        bool inBlockX = chunkCoord.x == lastChunkCoord.x || chunkCoord.x == lastChunkCoord.x + _loadDx;
        bool inBlockY = chunkCoord.y == lastChunkCoord.y || chunkCoord.y == lastChunkCoord.y + _loadDy;
        return !(inBlockX && inBlockY);
    }

    /// <summary>
    /// 앵커 기준 footprint의 가장 가까운 점이 viewDistance 초과인지 판정.
    /// footprint: x∈[anchor.x, anchor.x+sizeX-1], y∈[anchor.y-sizeY+1, anchor.y]
    /// </summary>
    private bool IsFootprintOutOfRange(Vector2Int anchor, int sizeX, int sizeY)
    {
        int nearestX = Mathf.Clamp(lastChunkCoord.x, anchor.x, anchor.x + sizeX - 1);
        int nearestY = Mathf.Clamp(lastChunkCoord.y, anchor.y - sizeY + 1, anchor.y);
        return Mathf.Abs(nearestX - lastChunkCoord.x) > viewDistance ||
               Mathf.Abs(nearestY - lastChunkCoord.y) > viewDistance;
    }

    private List<Vector2Int> BuildLoadQueue()
    {
        _buildQueueBuffer.Clear(); // [P2-1] 재사용 버퍼

        // 자기 청크를 첫 후보로 둔다.
        // 이동 중에는 이미 로드돼 있어 HasChunk 검사에서 걸러지므로 no-op 이지만,
        // 게임 시작 시점에는 아직 로드된 청크가 없어 자기 청크가 큐에 영영 들어가지 않는다.
        // (그 경우 PlayerSpawner 가 HasChunk(스폰청크)를 기다리다 타임아웃 → Rigidbody 가
        //  Kinematic 에 갇혀 플레이어가 공중에 뜬 채 멈춘다.)
        Vector2Int[] neighbors = new[]
        {
            lastChunkCoord,                                                          // 자기 청크
            new Vector2Int(lastChunkCoord.x + _loadDx, lastChunkCoord.y),         // 수평 (좌 or 우)
            new Vector2Int(lastChunkCoord.x,            lastChunkCoord.y + _loadDy), // 수직 (상 or 하)
            new Vector2Int(lastChunkCoord.x + _loadDx,  lastChunkCoord.y + _loadDy), // 대각선
        };

        foreach (var coord in neighbors)
        {
            if (ShouldSkipChunkGeneration(coord)) continue;
            if (!_chunkRegistry.HasChunk(coord)) _buildQueueBuffer.Add(coord);
        }

        return _buildQueueBuffer;
    }

    /// <summary>
    /// 큐에 서브청크 좌표가 있으면, 해당 앵커 좌표를 큐 앞에 삽입한다.
    /// SortLoadQueueByDistance 이후에 호출해야 정렬에 의해 밀리지 않는다.
    /// </summary>
    private void PrependSubChunkAnchors(List<Vector2Int> sortedQueue)
    {
        if (SpecialChunkManager.Instance == null) return;

        _anchorsToAddBuffer.Clear();

        // sortedQueue에는 이제 서브청크 후보 좌표도 포함됨.
        // 큐 안의 좌표가 서브청크 자리라면 해당 앵커가 뷰 밖에 있을 수 있으므로
        // 앵커를 역산해 큐 앞에 삽입한다.
        foreach (var coord in sortedQueue)
        {
            if (coord.y > 0) continue;

            var tileType = TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y);
            if (!SpecialChunkManager.Instance.IsSubChunkCoord(coord, tileType, worldSeed)) continue;

            if (SpecialChunkManager.Instance.TryGetSubChunkAnchorCoord(coord, tileType, worldSeed, out var anchorCoord)
                && anchorCoord.y <= 0
                && !_chunkRegistry.HasChunk(anchorCoord)
                && !sortedQueue.Contains(anchorCoord)
                && !_anchorsToAddBuffer.Contains(anchorCoord))
            {
                _anchorsToAddBuffer.Add(anchorCoord);
            }
        }

        if (_anchorsToAddBuffer.Count > 0)
            _loadingRunner.PrependToQueue(_anchorsToAddBuffer);
    }

    private bool ShouldSkipChunkGeneration(Vector2Int coord)
    {
        if (coord.y > 0) { return true; }

        if (TileDataManager.Instance != null)
        {
            int maxX = TileDataManager.Instance.terrainWidth;
            if (Mathf.Abs(coord.x) > maxX) { return true; }
        }

        const int MAX_COORD = 10000;
        if (Mathf.Abs(coord.x) > MAX_COORD || Mathf.Abs(coord.y) > MAX_COORD) { return true; }

        if (SpecialChunkManager.Instance != null &&
            SpecialChunkManager.Instance.TryGetRegisteredSubChunk(coord, out _))
        { return true; }

        return false;
    }

    // [Public API] Exposed for external systems (e.g. InputManager)
    public void SaveAllData()
    {
        if (enableMemoryCache && enableDiskSave)
        {
            _persistenceSystem.SaveAllDataToDisk();
        }
    }
    
    // IChunkProvider 구현 — 레지스트리는 IChunk를 보관하지만
    // 조명·이웃 탐색 등 호출처는 TerrainChunk 전용 API를 사용하므로 캐스팅 후 반환.
    // LargeStaticTerrainChunk는 null로 반환되며 조명 계산에서 자연스럽게 건너뜀.
    public TerrainChunk GetChunk(Vector2Int coord) => _chunkRegistry.Get(coord) as TerrainChunk;
    public IEnumerable<TerrainChunk> GetAllActiveChunks()
        => _chunkRegistry.GetAll().OfType<TerrainChunk>();
    public bool HasChunk(Vector2Int coord) => _chunkRegistry.HasChunk(coord);
    public bool IsChunkLoaded(Vector2Int coord) => _chunkRegistry.HasChunk(coord);

    /// <summary>
    /// 보호 좌표(PNG로 픽셀을 덮어쓰는 청크)에 절차 광물을 깐다.
    /// IChunkPostLoadPainter가 페인팅을 마친 직후에만 호출할 것 —
    /// 광물 자리 판정(IsWellSupported)이 페인팅 '후' 픽셀을 봐야
    /// 방 공동에 뜬 광물이 지지검사에 탈락해 튀어나오지 않는다.
    /// 바위·엘리베이터는 건드리지 않는다.
    /// </summary>
    public void SpawnMineralsInChunk(Vector2Int coord)
    {
        if (_chunkSpawner == null) return;
        var chunk = GetChunk(coord);
        if (chunk == null) return;

        _chunkSpawner.DecorateMineralsOnly(chunk, coord, ignoreProtection: true);
    }

    /// <summary>현재 활성화된 모든 청크 좌표를 반환. DigPathTracker의 발견 청크 추적에 사용.</summary>
    public IEnumerable<Vector2Int> GetActiveChunkCoords()
        => _chunkRegistry?.GetActiveCoordinates() ?? System.Linq.Enumerable.Empty<Vector2Int>();

    float ITerrainManager.chunkHeightWorld => ChunkCoords.WorldSize;

    public void ModifyTerrain(Vector2 targetWorldPos, float radius, int toolIndex, bool applySmoothing = true)
    {
        // 삽 마스크가 radius*2를 넘어 뻗으면 그 너머 청크가 호출되지 않아 경계에서 잘린다.
        // 삽이 아닌 도구는 영향 0. (현재 삽은 SapStrategy가 직접 chunk.Dig를 부르므로 이 경로를
        //  타지 않지만, 배선이 바뀌었을 때 조용히 깨지지 않도록 방어해둔다.)
        float maxScale = (toolIndex == 1) ? Mathf.Max(2.0f, ShovelDigMask.ExtentMultiplier) : 2.0f;
        float searchRadius = radius * maxScale + 1.0f;

        // Chunk range calculation
        int minChunkX = Mathf.FloorToInt((targetWorldPos.x - searchRadius) / chunkWidthWorld);
        int maxChunkX = Mathf.FloorToInt((targetWorldPos.x + searchRadius) / chunkWidthWorld);
        int minChunkY = Mathf.FloorToInt((targetWorldPos.y - searchRadius) / chunkHeightWorld);
        int maxChunkY = Mathf.FloorToInt((targetWorldPos.y + searchRadius) / chunkHeightWorld);

        bool anyModified = false;
        for (int x = minChunkX; x <= maxChunkX; x++)
        {
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                Vector2Int chunkCoord = new Vector2Int(x, y);
                IChunk chunk = _chunkRegistry.Get(chunkCoord);
                if (chunk is TerrainChunk tChunk && tChunk as UnityEngine.Object != null)
                {
                    tChunk.Dig(targetWorldPos, radius, toolIndex, applySmoothing);
                    anyModified = true;
                }
            }
        }

        // 파기 성공 시 이벤트 발생 → DigPathTracker가 수신해 경로 기록
        if (anyModified)
            OnTerrainModified?.Invoke(targetWorldPos, radius);
    }

    public void ExplodeTerrain(Vector2 targetWorldPos, float radius)
    {
        // 폭발음 공통 지점 — ScrapExplosion / DelayedBlast / ExplosiveMineralReactor /
        // DashBombRelic이 모두 이 경로를 타므로 개별 호출부에 흩뿌리지 않는다.
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(SfxKeys.Explosion, targetWorldPos);

        float maxScale = 1.0f; // 원형이므로 scale 불필요
        float searchRadius = radius * maxScale + 1.0f;
        
        // Chunk range calculation
        int minChunkX = Mathf.FloorToInt((targetWorldPos.x - searchRadius) / chunkWidthWorld);
        int maxChunkX = Mathf.FloorToInt((targetWorldPos.x + searchRadius) / chunkWidthWorld);
        int minChunkY = Mathf.FloorToInt((targetWorldPos.y - searchRadius) / chunkHeightWorld);
        int maxChunkY = Mathf.FloorToInt((targetWorldPos.y + searchRadius) / chunkHeightWorld);

        bool anyExploded = false;
        for (int x = minChunkX; x <= maxChunkX; x++)
        {
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                Vector2Int chunkCoord = new Vector2Int(x, y);
                IChunk chunk = _chunkRegistry.Get(chunkCoord);
                if (chunk is TerrainChunk tChunk)
                {
                    tChunk.Explode(targetWorldPos, radius);
                    anyExploded = true;
                }
            }
        }

        // 폭발로 파인 자리도 지도 탐사 기록에 남긴다 (파기와 동일)
        if (anyExploded)
            OnTerrainModified?.Invoke(targetWorldPos, radius);
    }
    
    public bool IsWorldPositionEmpty(Vector2 worldPos)
    {
        int chunkX = Mathf.FloorToInt(worldPos.x / chunkWidthWorld);
        int chunkY = Mathf.FloorToInt(worldPos.y / chunkHeightWorld);
        Vector2Int targetCoord = new Vector2Int(chunkX, chunkY);

        IChunk targetChunk = _chunkRegistry.Get(targetCoord);
        if (targetChunk is TerrainChunk tChunk && tChunk as UnityEngine.Object != null)
        {
            Vector2 localPos = worldPos - (Vector2)tChunk.transform.position;
            return tChunk.IsPixelEmptyLocal(localPos);
        }
        // 청크가 아예 없는 좌표(던전 offset 등)는 지형이 없음 = 빈 공간으로 취급 → 그 위 광물이 정상 낙하한다.
        // (청크는 있으나 TerrainChunk가 아닌 경우(LargeStatic 등)는 기존대로 solid 취급하여 오작동 방지.)
        return targetChunk == null;
    }

    /// <summary>
    /// 해당 앵커 좌표에 저장된 청크 데이터가 있으면(=플레이어가 방문·수정함) true.
    /// 탐지 유물이 발견 마커의 '방문/미방문' 상태를 판정할 때 사용.
    /// </summary>
    public bool IsChunkVisited(Vector2Int coord)
        => _persistenceSystem != null && _persistenceSystem.TryGetChunkData(coord, out _);

    private void OnApplicationQuit()
    {
        SaveAllData();
    }
}