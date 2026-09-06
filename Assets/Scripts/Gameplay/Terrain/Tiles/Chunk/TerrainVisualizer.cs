// @tags: visual, texture, gpu, rendering, job-scheduler, distance-field, visual-update, sprite-renderer, chamfer
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;

/// <summary>
/// Handles visual rendering for terrain chunks.
/// Manages texture upload to GPU and coordinates with Job System.
/// </summary>
public class TerrainVisualizer : System.IDisposable
{
    // ============================================================================================================
    //  REFERENCES
    // ============================================================================================================

    private readonly ChunkData _data;
    private readonly Texture2D _mainTexture;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly ChunkJobScheduler _jobScheduler;

    public float TextureThickness { get; set; } = 4f;
    public int PixelsPerUnit { get; set; } = 100;

    // ============================================================================================================
    //  CONSTRUCTION
    // ============================================================================================================

    public TerrainVisualizer(ChunkData data, Texture2D mainTexture, SpriteRenderer mainSR)
    {
        _data = data;
        _mainTexture = mainTexture;
        _spriteRenderer = mainSR;
        _jobScheduler = new ChunkJobScheduler(data);
    }

    // ============================================================================================================
    //  TEXTURE UPDATE
    // ============================================================================================================

    /// <summary>실행 중인 잡을 모두 완료한 뒤 GPU 텍스처에 업로드. IsVisualDirty가 true일 때 호출.</summary>
    public void ApplyTextureSync()
    {
        _jobScheduler.CompleteAllJobs();
        if (_mainTexture != null)
            _mainTexture.Apply(false);
    }

    /// <summary>메인 스레드에서 ChunkData NativeArray를 수정하기 전에 호출.</summary>
    public void CompleteJobs() => _jobScheduler.CompleteAllJobs();

    /// <summary>Init + Chamfer(거리장) 완료 대기. DistanceField를 외부에서 읽기 전에 호출.</summary>
    public void CompleteLighting() => _jobScheduler.CompleteLighting();

    /// <summary>[생성] BasePixels writer(카빙 잡)만 완료 대기. BasePixels를 메인 스레드에서 읽기 전에 호출.</summary>
    public void CompleteCarve() => _jobScheduler.CompleteCarve();

    /// <summary>Init 잡이 아직 실행 중인지 확인 (블로킹 없음).</summary>
    public bool IsInitJobRunning() => !_jobScheduler.IsInitCompleted();

    /// <summary>[생성] 절차적 굴 카빙 잡 스케줄 프록시. ScheduleInitArea 앞에서 호출할 것.</summary>
    public void ScheduleCarveCave(int chunkX, int chunkY, int worldSeed, CaveCarveSettings settings)
        => _jobScheduler.ScheduleCarveCave(_data, chunkX, chunkY, worldSeed, settings);

    /// <summary>[P0] BoundarySync Burst Job 스케줄 프록시. 이웃 데이터 묶음을 받아 Job 으로 위임.</summary>
    public JobHandle ScheduleBoundarySync(ChunkJobScheduler.BoundaryNeighborSet neighbors)
        => _jobScheduler.ScheduleBoundarySync(_data, neighbors);

    /// <summary>[P0] 이웃 청크가 BoundarySync 의존성으로 읽을 lighting handle.</summary>
    public JobHandle GetLightingHandle() => _jobScheduler.GetLightingHandle();

    /// <summary>[P0] 외부 청크의 BoundarySync 가 우리 distanceField 를 [ReadOnly] 로 읽었음을 등록.</summary>
    public void RegisterDistanceFieldReader(JobHandle h) => _jobScheduler.RegisterDistanceFieldReader(h);

    // ============================================================================================================
    //  VISUAL AREA UPDATE (MAIN ENTRY POINT)
    // ============================================================================================================

    public void UpdateVisualsFull(int chunkX, int chunkY, System.Action onPreChamfer = null, bool skipInit = false)
    {
        UpdateVisualsArea(0, 0, _data.Width, _data.Height, chunkX, chunkY, onPreChamfer, skipInit);
    }

    /// <summary>
    /// [Pre-schedule] Init 잡만 먼저 스케줄 (워커에서 미리 실행되도록).
    /// Init 잡을 미리 스케줄해 Round1의 대기를 줄인다.
    /// </summary>
    public void ScheduleInitArea(int minX, int minY, int maxX, int maxY)
    {
        minX = Mathf.Clamp(minX, 0, _data.Width);
        maxX = Mathf.Clamp(maxX, 0, _data.Width);
        minY = Mathf.Clamp(minY, 0, _data.Height);
        maxY = Mathf.Clamp(maxY, 0, _data.Height);
        _jobScheduler.ScheduleInitLighting(_data, minX, minY, maxX, maxY);
    }

    /// <summary>
    /// [Non-blocking Pre-schedule] 이전 Init 잡이 완료된 경우에만 스케줄.
    /// 실행 중인 잡(RefreshVisuals 등)이 있으면 스킵 → Round1(TerrainChunk.ScheduleDistancePass의 skipInit:false 분기)에서 재처리됨.
    /// </summary>
    public bool TryScheduleInitArea(int minX, int minY, int maxX, int maxY)
    {
        minX = Mathf.Clamp(minX, 0, _data.Width);
        maxX = Mathf.Clamp(maxX, 0, _data.Width);
        minY = Mathf.Clamp(minY, 0, _data.Height);
        maxY = Mathf.Clamp(maxY, 0, _data.Height);
        return _jobScheduler.TryScheduleInitLighting(_data, minX, minY, maxX, maxY);
    }

    /// <summary>
    /// onPreChamfer: BoundarySync Burst Job을 스케줄하는 콜백 (비블로킹).
    /// skipInit: true면 Init 생략 — MarkChunkDirty에서 이미 선스케줄된 경우.
    /// </summary>
    public void UpdateVisualsArea(int minX, int minY, int maxX, int maxY, int chunkX, int chunkY, System.Action onPreChamfer = null, bool skipInit = false)
    {
        minX = Mathf.Clamp(minX, 0, _data.Width);
        maxX = Mathf.Clamp(maxX, 0, _data.Width);
        minY = Mathf.Clamp(minY, 0, _data.Height);
        maxY = Mathf.Clamp(maxY, 0, _data.Height);

        if (!skipInit)
        {
            _jobScheduler.ScheduleInitLighting(_data, minX, minY, maxX, maxY); // SetDirtyRect + DS + Init
        }
        else
        {
            _jobScheduler.SetDirtyRect(_data, minX, minY, maxX, maxY);
            _jobScheduler.ScheduleDownsample(_data);
        }

        // onPreChamfer가 BoundarySync Job을 스케줄 → ScheduleChamferPasses가 자동으로 의존성에 포함
        onPreChamfer?.Invoke();

        _jobScheduler.ScheduleChamferPasses(_data);
        _jobScheduler.ScheduleUpsample(_data);

        int texPx = Mathf.Clamp(Mathf.RoundToInt(TextureThickness * PixelsPerUnit), 1, 50);
        _jobScheduler.ScheduleVisualJob(_data, _mainTexture, texPx, chunkX, chunkY);

        _data.IsVisualDirty = true;
    }

    // ============================================================================================================
    //  LATEUPDATE 파이프라인 전용 PASS (Assets/Docs/job-pipeline-waste-removal.md §4)
    // ============================================================================================================

    /// <summary>
    /// [Round 1] 자기 청크의 거리장만 확정한다. 이웃이 Round 2에서 읽을 값이다.
    /// BoundarySync 없음 → _lastExt 의 strip 확장도 일어나지 않는다(§3.3.1).
    ///
    /// skipInit: Step 1에서 Init이 이미 스케줄됐으면 true.
    /// withPreview: true면 Upsample + Visual 을 추가해 조기 텍스처 프리뷰를 만든다(§3.4).
    /// </summary>
    public void ScheduleDistancePass(int minX, int minY, int maxX, int maxY,
                                     int chunkX, int chunkY, bool skipInit, bool withPreview)
    {
        _jobScheduler.SetDirtyRect(_data, minX, minY, maxX, maxY);
        _jobScheduler.ScheduleDownsample(_data);

        if (!skipInit)
            _jobScheduler.ScheduleInit(_data);

        _jobScheduler.ScheduleChamferPasses(_data);

        if (!withPreview) return;

        _jobScheduler.ScheduleUpsample(_data);
        int texPx = Mathf.Clamp(Mathf.RoundToInt(TextureThickness * PixelsPerUnit), 1, 50);
        _jobScheduler.ScheduleVisualJob(_data, _mainTexture, texPx, chunkX, chunkY);
        _data.IsVisualDirty = true;
    }

    /// <summary>
    /// [Round 2] 이웃 거리장을 동기화하고 최종 텍스처까지 만든다.
    /// onPreChamfer 가 BoundarySync Job 을 스케줄하며, 그 안에서 _lastExt 가 strip 으로 확장된다.
    ///
    /// ⚠ SetDirtyRect 를 반드시 먼저 호출한다 — Round 2 는 더 이상 ScheduleInitLighting 을
    ///    거치지 않으므로 _lastExt 를 재확립할 다른 주체가 없다(§5.2).
    /// </summary>
    public void ScheduleSyncAndVisualPass(int minX, int minY, int maxX, int maxY,
                                          int chunkX, int chunkY, System.Action onPreChamfer)
    {
        _jobScheduler.SetDirtyRect(_data, minX, minY, maxX, maxY);
        _jobScheduler.ScheduleDownsample(_data);

        onPreChamfer?.Invoke();   // BoundarySync — 여기서 _lastExt 가 확장된다

        _jobScheduler.ScheduleChamferPasses(_data);
        _jobScheduler.ScheduleUpsample(_data);

        int texPx = Mathf.Clamp(Mathf.RoundToInt(TextureThickness * PixelsPerUnit), 1, 50);
        _jobScheduler.ScheduleVisualJob(_data, _mainTexture, texPx, chunkX, chunkY);
        _data.IsVisualDirty = true;
    }

    public void Dispose() => _jobScheduler.Dispose();

    public bool IsJobRunning() => _jobScheduler.IsJobRunning();

    /// <summary>Visual Job만 완료 여부 확인 (Init/Chamfer는 무시).
    /// outputTexture는 Visual Job만 쓰므로, true면 텍스처 업로드 안전.
    /// </summary>
    public bool IsVisualJobCompleted() => _jobScheduler.IsVisualJobCompleted();
}
