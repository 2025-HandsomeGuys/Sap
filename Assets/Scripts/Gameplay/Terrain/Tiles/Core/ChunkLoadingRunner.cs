// @tags: chunk-loading, coroutine, pipeline, spawn, phase, pool, queue, loading-runner
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChunkLoadingRunner : MonoBehaviour
{
    // Dependencies
    private ActiveChunkRegistry _registry;
    private ChunkSpawner _spawner;
    private ChunkPool _pool;
    private ChunkDataProvider _provider;
    private GridCoordinateSystem _grid;
    
    // Internal State
    private List<Vector2Int> _priorityQueue = new List<Vector2Int>(); // 앵커 우선 처리용
    private List<Vector2Int> _loadQueue = new List<Vector2Int>();
    private int _readHead = 0; // O(1) 소비: RemoveAt(0) 대신 커서 전진
    private bool _isLoadingRoutineRunning = false;
    public bool IsLoadingRoutineRunning => _isLoadingRoutineRunning;
    private Transform _player;

    // [P2-2] 캐시된 scheduler / pipeline (매 코루틴 실행마다 new 하지 않음)
    private ChunkLoadingScheduler _scheduler;
    private ChunkGenerationPipeline _pipeline;

    // [P2-3] GC-free 정렬 비교자
    private struct DistanceComparer : System.Collections.Generic.IComparer<Vector2Int>
    {
        public Vector2 PlayerPos;
        public float ChunkSize;
        public int Compare(Vector2Int a, Vector2Int b)
        {
            float da = (new Vector2(a.x * ChunkSize, a.y * ChunkSize) - PlayerPos).sqrMagnitude;
            float db = (new Vector2(b.x * ChunkSize, b.y * ChunkSize) - PlayerPos).sqrMagnitude;
            return da.CompareTo(db);
        }
    }
    private DistanceComparer _distanceComparer;

    // [GC] UpdateLoadQueue는 UpdateChunks를 타고 매 프레임 호출된다 (로딩 중이 아닐 때도).
    // 매번 new HashSet/new List 하면 이동 내내 쓰레기가 쌓이므로 재사용 버퍼로 처리한다.
    private readonly HashSet<Vector2Int> _newQueueSet = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> _pendingAnchors = new List<Vector2Int>();

    // [GC] ProcessChunkQueue 배치 버퍼. _isLoadingRoutineRunning 가드로 코루틴이 동시에
    // 두 개 돌지 않으므로 인스턴스 필드로 재사용해도 안전하다.
    private readonly List<Vector2Int> _batchCoords = new List<Vector2Int>();
    private readonly List<KeyValuePair<Vector2Int, IChunk>> _batchResults =
        new List<KeyValuePair<Vector2Int, IChunk>>();

    // Optimization Settings
    [SerializeField] private int _batchSize = 4;
    [SerializeField] private int _maxTimePerBatchMs = 6;
    [SerializeField] private int _maxTimePerFramePh2Ms = 5;

    public void Initialize(
        ActiveChunkRegistry registry,
        ChunkSpawner spawner,
        ChunkPool pool,
        ChunkDataProvider provider,
        GridCoordinateSystem grid,
        Transform player,
        int batchSize,
        int timePerBatch,
        int timePerFrame)
    {
        _registry = registry;
        _spawner = spawner;
        _pool = pool;
        _provider = provider;
        _grid = grid;
        _player = player;
        _batchSize = batchSize;
        _maxTimePerBatchMs = timePerBatch;
        _maxTimePerFramePh2Ms = timePerFrame;

        // [P2-2] 코루틴 실행마다 new 하지 않도록 초기화 시점에 한 번만 생성
        _scheduler = new ChunkLoadingScheduler(batchSize, timePerBatch, timePerFrame);
        _pipeline  = new ChunkGenerationPipeline(registry, spawner, pool, provider, transform);
    }

    private int _updateFrameCount = 0;
    void Update()
    {
        _updateFrameCount++;
        if (_updateFrameCount % 60 == 0) 
        {
            // Debug.Log($"[CHUNK_DEBUG] ChunkLoadingRunner.Update() called.");
        }
    }

    public void UpdateLoadQueue(List<Vector2Int> newQueue)
    {
        // newQueue에 없는 항목(PrependToQueue로 추가된 앵커 후보)은 보존한다.
        // 매 프레임 Clear()하면 아직 처리되지 않은 앵커가 사라지는 레이스 컨디션이 발생.
        // [GC] 재사용 버퍼 — Clear()는 용량을 유지하므로 정상 상태에서 할당 0
        _newQueueSet.Clear();
        foreach (var coord in newQueue)
            _newQueueSet.Add(coord); // O(1) Contains용

        // newQueue에 없는 미처리 항목(앵커 후보)을 보존
        var pendingAnchors = _pendingAnchors;
        pendingAnchors.Clear();
        foreach (var coord in _priorityQueue)
        {
            if (!_newQueueSet.Contains(coord) && !_registry.HasChunk(coord))
                pendingAnchors.Add(coord);
        }
        for (int i = _readHead; i < _loadQueue.Count; i++)
        {
            var coord = _loadQueue[i];
            if (!_newQueueSet.Contains(coord) && !_registry.HasChunk(coord))
                pendingAnchors.Add(coord);
        }

        _loadQueue.Clear();
        _readHead = 0;
        _loadQueue.AddRange(newQueue);
        SortLoadQueueByDistance();

        // 앵커는 거리 정렬보다 우선 — 별도 우선 큐로 관리
        _priorityQueue.Clear();
        foreach (var coord in pendingAnchors)
        {
            if (!_priorityQueue.Contains(coord))
                _priorityQueue.Add(coord);
        }
    }

    /// <summary>
    /// 지정된 좌표를 우선 큐에 추가한다 (앵커 우선 처리용).
    /// </summary>
    public void PrependToQueue(IEnumerable<Vector2Int> coords)
    {
        foreach (var coord in coords)
        {
            if (!_priorityQueue.Contains(coord) && !_loadQueue.Contains(coord))
                _priorityQueue.Add(coord);
        }
    }

    public void StartLoadingIfNeeded()
    {
        bool hasWork = _priorityQueue.Count > 0 || _readHead < _loadQueue.Count;
        if (hasWork && !_isLoadingRoutineRunning)
        {
            StartCoroutine(ProcessChunkQueue());
        }
    }

    /// <summary>
    /// 로딩 화면 중 청크 풀을 미리 채운다. 프레임당 1개씩 분산하여 스파이크 없음.
    /// InfinityMapManager.InitializeCoroutine()에서 UpdateChunks() 이전에 호출한다.
    /// </summary>
    public IEnumerator PrewarmPoolAsync(GameObject prefab, Transform parent, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var obj = UnityEngine.Object.Instantiate(prefab, parent);
            var chunk = obj.GetComponent<TerrainChunk>();
            _pool.Return(chunk);
            yield return null;
        }
    }

    private void SortLoadQueueByDistance()
    {
        if (_player == null) return;

        // [P2-3] 구조체 IComparer — 람다 클로저 GC 할당 없음
        _distanceComparer.PlayerPos = new Vector2(_player.position.x, _player.position.y);
        _distanceComparer.ChunkSize = ChunkCoords.WorldSize;
        _loadQueue.Sort(_distanceComparer);
    }

    private IEnumerator ProcessChunkQueue()
    {
        _isLoadingRoutineRunning = true;

        // [P2-2] 캐시된 인스턴스 재사용 (Initialize에서 한 번만 생성)
        var scheduler = _scheduler;
        var pipeline  = _pipeline;

        while (_priorityQueue.Count > 0 || _readHead < _loadQueue.Count)
        {
            // === PHASE 1: Spawn chunks ===
            // 우선 큐 → 일반 큐 순서로 배치 구성 (O(1) 커서 방식)
            var coords = _batchCoords;   // [GC] 재사용 버퍼
            coords.Clear();
            while (coords.Count < scheduler.BatchSize && _priorityQueue.Count > 0)
            {
                var c = _priorityQueue[0];
                _priorityQueue.RemoveAt(0);
                coords.Add(c);
            }
            while (coords.Count < scheduler.BatchSize && _readHead < _loadQueue.Count)
            {
                coords.Add(_loadQueue[_readHead]);
                _readHead++;
            }
            // 커서가 끝까지 도달했으면 리스트 초기화
            if (_readHead >= _loadQueue.Count)
            {
                _loadQueue.Clear();
                _readHead = 0;
            }

            var results = _batchResults;  // [GC] 재사용 버퍼
            results.Clear();

            scheduler.StartBatch();

            foreach (var coord in coords)
            {
                try
                {
                    if (_registry.HasChunk(coord)) 
                    {
                        continue;
                    }

                    var chunk = pipeline.ExecutePhase1_SpawnAndInitialize(coord);
                    if (chunk != null)
                    {
                        results.Add(new KeyValuePair<Vector2Int, IChunk>(coord, chunk));
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ChunkLoadingRunner] CAUGHT EXCEPTION: {e.Message}\n{e.StackTrace}");
                    pipeline.OnPhase1_SpawnError(coord, e);
                }
                
                if (scheduler.ShouldBreakBatch())
                {
                    yield return null;
                }
            }
            
            yield return scheduler.YieldAndRestart();

            // =========================================================================
            // 🚨 [추가된 2차 최적화] Phase 2(장식물)로 넘어가기 전에 Phase 1(지형 Job) 끝날 때까지 대기
            // 이 부분이 들어가야 광물 생성기(MineralGenerator)가 강제로 렉을 유발하지 않습니다!
            // =========================================================================
            bool p1JobsFinished = false;
            while (!p1JobsFinished)
            {
                p1JobsFinished = true;
                foreach (var pair in results)
                {
                    var tc = pair.Value as TerrainChunk;
                    if (tc is UnityEngine.Object tcObj && tcObj != null && tc.IsJobRunning())
                    {
                        p1JobsFinished = false;
                        break;
                    }
                }

                if (!p1JobsFinished)
                {
                    yield return null;
                }
            }
            // =========================================================================

            // === PHASE 2: Finalize chunks (Decorate) ===
            // [P2-4] 데코레이터 사이에도 시간 체크.
            // try-catch 안에서는 yield 불가 → MoveNext()만 try 안에서 호출하고 yield는 밖에서 처리.
            scheduler.StartBatch();
            // [Fix] 인덱스 루프인 이유: Phase 2에서 실패한 청크는 OnPhase2_FinalizeError가
            // 이미 풀에 반납(비활성)했다. 그런데 results에 그대로 남아 있으면 아래 Phase 2.5가
            // 그 청크에 Chamfer/Visual 잡을 새로 예약하고, 다음 배치의 _chunkPool.Get()이
            // "잡이 살아 있는 청크"를 꺼내 준다 → Reuse_Step1_Prepare의 BasePixels 쓰기가
            // "TerrainVisualJob reads from baseData..." 로 터지고 그 뒤 로딩이 통째로 멈춘다.
            // 실패 항목은 여기서 null로 지워 Phase 2.5/3이 건너뛰게 한다.
            for (int ri = 0; ri < results.Count; ri++)
            {
                var pair = results[ri];
                var steps = pipeline.ExecutePhase2_DecoratorSteps(pair.Key, pair.Value).GetEnumerator();
                bool stepError = false;

                while (true)
                {
                    bool hasNext = false;
                    try { hasNext = steps.MoveNext(); }
                    catch (System.Exception e)
                    {
                        pipeline.OnPhase2_FinalizeError(pair.Key, pair.Value, e);
                        stepError = true;
                        break;
                    }

                    if (!hasNext) break;

                    if (scheduler.ShouldYieldFrame())
                        yield return scheduler.YieldAndRestart();
                }

                if (stepError)
                {
                    results[ri] = new KeyValuePair<Vector2Int, IChunk>(pair.Key, null);
                    continue;
                }

                if (scheduler.ShouldYieldFrame())
                    yield return scheduler.YieldAndRestart();
            }
            
            // === PHASE 2.5: 장식 완료 후 Chamfer + Visual 예약 (블로킹 없음) ===
            // TerrainCarver/MineralGenerator 등 BasePixels 쓰기가 모두 끝난 뒤 Chamfer 예약.
            // - SyncBoundary 없이 예약(onPreChamfer=null) → 이웃 Complete() 블로킹 없음.
            // - SyncBoundary는 Phase 3의 MarkChunkDirty → ProcessDirtyChunksAsync(LateUpdate)에서 처리.
            foreach (var pair in results)
            {
                var tc = pair.Value as TerrainChunk;
                if (tc is UnityEngine.Object tcObj2 && tcObj2 != null)
                    tc.FinishVisualsAfterInit();
            }

            // === PHASE 3: Update Lighting (최적화 적용 부분) ===
            
            // 1. 모든 청크에 조명 업데이트 스케줄링(명령)을 내립니다.
            foreach (var pair in results)
            {
                 pipeline.ExecutePhase3_UpdateLighting(pair.Value);
            }

            // 2. [가장 중요한 변경점] 백그라운드 Job이 모두 끝날 때까지 프레임을 넘기며 부드럽게 대기합니다.
            bool allJobsFinished = false;
            while (!allJobsFinished)
            {
                allJobsFinished = true;
                foreach (var pair in results)
                {
                    var tc = pair.Value as TerrainChunk;
                    // 절차적 청크(TerrainChunk)의 Job이 아직 실행 중이면 대기
                    if (tc is UnityEngine.Object tcObj && tcObj != null && tc.IsJobRunning())
                    {
                        allJobsFinished = false;
                        break;
                    }
                }

                // 아직 안 끝난 Job이 있다면 메인 스레드를 멈추지 않고(No Blocking) 다음 프레임에 다시 확인합니다.
                if (!allJobsFinished)
                {
                    yield return null;
                }
            }
        }

        // 재사용 버퍼가 청크 참조를 계속 붙들지 않도록 정리
        _batchCoords.Clear();
        _batchResults.Clear();

        _isLoadingRoutineRunning = false;
    }
}