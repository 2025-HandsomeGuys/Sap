// @tags: jobs, burst, job-scheduler, distance-field, chamfer, visual-job, boundary-sync, native-array, lighting
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Schedules and manages Unity Jobs for parallel terrain processing.
/// Pure C# class with no MonoBehaviour dependencies.
/// COMPLETE IMPLEMENTATION migrated from TerrainChunk.Jobs.cs
/// </summary>
public class ChunkJobScheduler
{
    // ============================================================================================================
    //  JOB HANDLES
    // ============================================================================================================

    private JobHandle _visualsJobHandle;
    private JobHandle _initJobHandle;
    private JobHandle _forwardJobHandle;   // ChamferForwardPassJob
    private JobHandle _lightingJobHandle;  // ChamferBackwardPassJob (최종 lighting 완료 기준)
    private JobHandle _boundarySyncHandle; // BoundarySyncJob (Init과 Chamfer 사이, async)

    // [생성] CaveCarvePixelsJob — BasePixels 에 쓰는 유일한 잡 핸들.
    // 다른 핸들과 달리 이건 *지형 원본*을 바꾸므로 Downsample(읽기)보다 반드시 먼저 끝나야 하고,
    // 콜라이더·돌·광물이 메인 스레드에서 BasePixels 를 읽기 전에도 끝나 있어야 한다.
    // → AllPrevHandles() / IsJobRunning() / CompleteAllJobs() 세 곳 모두에 등록되어 있다.
    private JobHandle _caveHandle;

    // [half-res] full↔half 브리지 잡 핸들.
    private JobHandle _downsampleHandle;   // DownsampleMaskJob (full base/info → half)
    private JobHandle _upsampleHandle;     // UpsampleDistanceJob (half DF → full DF)

    // [P0] 외부 청크의 BoundarySync 가 *우리* distanceField 를 [ReadOnly] 로 읽은 핸들 누적.
    // Unity Job System 안전 검증은 외부 read handle 을 자동으로 합쳐주지 않으므로,
    // 우리 distanceField 에 쓰는 모든 writer (Init / BoundarySync / Chamfer) 의 deps 에 직접 합쳐야 한다.
    // 합쳐진 후에는 그 writer 핸들이 prior reader 를 포섭하므로 default 로 리셋해도 안전하다.
    private JobHandle _externalDistanceFieldReaders;

    // ============================================================================================================
    //  BOUNDARY NEIGHBOR SET — ScheduleBoundarySync 입력 데이터
    // ============================================================================================================

    /// <summary>
    /// 8방향 이웃 청크의 distanceField 참조 + 잡 핸들 묶음.
    /// missing slot은 hasFoo=false + 해당 NativeArray는 default(NativeArray) 로 둔다.
    /// </summary>
    public struct BoundaryNeighborSet
    {
        public NativeArray<ushort> left, right, top, bottom;
        public NativeArray<ushort> topLeft, topRight, bottomLeft, bottomRight;
        public JobHandle leftHandle, rightHandle, topHandle, bottomHandle;
        public JobHandle topLeftHandle, topRightHandle, bottomLeftHandle, bottomRightHandle;
        public bool hasLeft, hasRight, hasTop, hasBottom;
        public bool hasTopLeft, hasTopRight, hasBottomLeft, hasBottomRight;
        public bool isSkyAbove;
    }

    // ============================================================================================================
    //  CONFIGURATION
    // ============================================================================================================

    private const int JOB_BATCH_SIZE = 64;
    private const int EXT_MARGIN = 50;

    // SetDirtyRect가 세팅 → 이후 모든 Schedule* 메서드가 읽는다.
    // _lastExt* 는 ScheduleBoundarySync 가 경계 strip 방향으로 확장한다(의도된 동작).
    private int _lastDirtyMinX, _lastDirtyMinY, _lastDirtyMaxX, _lastDirtyMaxY;
    private int _lastExtMinX, _lastExtMinY, _lastExtMaxX, _lastExtMaxY;

    // ============================================================================================================
    //  CONSTRUCTION
    // ============================================================================================================

    // [P0] BoundarySync 의 missing-neighbor 슬롯에 사용하는 length-0 placeholder.
    // default(NativeArray) 는 Unity Job 안전 시스템이 거부할 수 있어 길이 0 의 실제 할당이 필요.
    // [Fix] 각 방향마다 별도 인스턴스를 사용해야 aliasing 에러 방지.
    // Unity Job System 은 같은 NativeArray 가 2개 이상 필드에 할당되면 (ReadOnly 포함) 거부한다.
    private NativeArray<ushort> _emptyLeft, _emptyRight, _emptyTop, _emptyBottom;
    private NativeArray<ushort> _emptyTopLeft, _emptyTopRight, _emptyBottomLeft, _emptyBottomRight;

    public ChunkJobScheduler(ChunkData data)
    {
        // NativeQueue 제거: 2패스 Chamfer 변환은 큐 불필요
        // 각 방향별 별도 length-0 NativeArray 할당 — aliasing 방지
        _emptyLeft        = new NativeArray<ushort>(0, Allocator.Persistent);
        _emptyRight       = new NativeArray<ushort>(0, Allocator.Persistent);
        _emptyTop         = new NativeArray<ushort>(0, Allocator.Persistent);
        _emptyBottom      = new NativeArray<ushort>(0, Allocator.Persistent);
        _emptyTopLeft     = new NativeArray<ushort>(0, Allocator.Persistent);
        _emptyTopRight    = new NativeArray<ushort>(0, Allocator.Persistent);
        _emptyBottomLeft  = new NativeArray<ushort>(0, Allocator.Persistent);
        _emptyBottomRight = new NativeArray<ushort>(0, Allocator.Persistent);
    }

    // ============================================================================================================
    //  RECT STATE
    // ============================================================================================================

    /// <summary>
    /// dirty rect와 그 확장 rect(ext = dirty ± EXT_MARGIN)를 세팅한다.
    /// 이후 스케줄되는 모든 잡이 이 rect를 dispatch 도메인으로 쓴다.
    ///
    /// ⚠ 각 라운드 진입 시 반드시 먼저 호출할 것.
    ///   _lastExt*는 필드이고 ScheduleBoundarySync가 이를 변형하며,
    ///   같은 청크에 대해 ProcessDirtyChunksAsync 코루틴이 동시 실행될 수 있다.
    ///   (배경: Assets/Docs/job-pipeline-waste-removal.md §5.2)
    /// </summary>
    public void SetDirtyRect(ChunkData data, int minX, int minY, int maxX, int maxY)
    {
        int w = data.Width;
        int h = data.Height;

        _lastDirtyMinX = Mathf.Clamp(minX, 0, w);
        _lastDirtyMinY = Mathf.Clamp(minY, 0, h);
        _lastDirtyMaxX = Mathf.Clamp(maxX, 0, w);
        _lastDirtyMaxY = Mathf.Clamp(maxY, 0, h);

        _lastExtMinX = Mathf.Max(_lastDirtyMinX - EXT_MARGIN, 0);
        _lastExtMinY = Mathf.Max(_lastDirtyMinY - EXT_MARGIN, 0);
        _lastExtMaxX = Mathf.Min(_lastDirtyMaxX + EXT_MARGIN, w);
        _lastExtMaxY = Mathf.Min(_lastDirtyMaxY + EXT_MARGIN, h);
    }

    /// <summary>
    /// 이 청크의 모든 pending 핸들 합성 (over-depend).
    /// 청크 내 파이프라인은 어차피 직렬이라 병렬성 손실이 없고, 의존성 누락으로 인한
    /// InvalidOperationException 을 원천 차단한다.
    /// </summary>
    private JobHandle AllPrevHandles()
    {
        JobHandle h = JobHandle.CombineDependencies(
            JobHandle.CombineDependencies(_initJobHandle, _forwardJobHandle, _lightingJobHandle),
            JobHandle.CombineDependencies(_boundarySyncHandle, _upsampleHandle, _externalDistanceFieldReaders));
        h = JobHandle.CombineDependencies(h, _downsampleHandle, _visualsJobHandle);
        return JobHandle.CombineDependencies(h, _caveHandle);
    }

    /// <summary>half 좌표 ext rect. 반환 false면 빈 rect이므로 스케줄하지 않는다.</summary>
    private bool HalfExtRect(out int minX, out int minY, out int rw, out int rh)
    {
        minX = _lastExtMinX / 2;
        minY = _lastExtMinY / 2;
        rw = (_lastExtMaxX + 1) / 2 - minX;
        rh = (_lastExtMaxY + 1) / 2 - minY;
        return rw > 0 && rh > 0;
    }

    /// <summary>full 좌표 ext rect. 반환 false면 빈 rect이므로 스케줄하지 않는다.</summary>
    private bool FullExtRect(out int minX, out int minY, out int rw, out int rh)
    {
        minX = _lastExtMinX;
        minY = _lastExtMinY;
        rw = _lastExtMaxX - minX;
        rh = _lastExtMaxY - minY;
        return rw > 0 && rh > 0;
    }

    // ============================================================================================================
    //  JOB SCHEDULING: VISUAL UPDATE
    // ============================================================================================================

    /// <summary>
    /// Schedules a visual update job (direct pixel copy - NO BORDERS).
    /// Writes DIRECTLY to GPU texture memory.
    /// </summary>
    /// <summary>
    /// Schedules a visual update job (Renders borders + base terrain).
    /// </summary>
    public void ScheduleVisualJob(ChunkData data, Texture2D mainTexture, int texThick, int chunkX, int chunkY)
    {
        // Visual Job은 _lightingJobHandle에 이미 의존하므로 BFS 완료 보장됨.
        // 이전 Visual 쓰기만 완료시킴 (Phase1 대기루프 후에는 이미 완료 상태).
        _visualsJobHandle.Complete();

        NativeArray<Color32> rawTexture = mainTexture.GetRawTextureData<Color32>();

        var job = new TerrainJobs.TerrainVisualJob
        {
            baseData = data.BasePixels,
            distanceField = data.DistanceField,
            borderData = data.BorderData,

            // [Added] Secondary Border Support
            pixelInfo = data.PixelInfo,
            secondaryBorderData = data.SecondaryBorderData,
            secondaryTileId = data.SecondaryTileId,
            secondaryBorderWidth = data.SecondaryBorderWidth,
            secondaryBorderHeight = data.SecondaryBorderHeight,

            outputTexture = rawTexture,
            width = data.Width,
            height = data.Height,

            // Settings
            textureThicknessPx = texThick,
            
            // Border UV Mapping Data
            borderWidth = data.BorderWidth,
            borderHeight = data.BorderHeight,
            
            // Global UV Mapping
            chunkOffsetX = chunkX * data.Width,
            chunkOffsetY = chunkY * data.Height,

            debugDistanceField = TerrainChunk.DebugDistanceField,

            // [rim] TerrainChunk static 직접 읽기 (debugDistanceField 와 동일 패턴).
            // 파생 상수는 여기서 계산한다 — 유도는 Assets/Docs/terrain-rim-outline.md §3-4, §4
            rimThicknessPx      = TerrainChunk.RimThicknessPx,
            rimR2               = TerrainChunk.RimThicknessPx * TerrainChunk.RimThicknessPx,
            rimGateDist         = (TerrainChunk.RimThicknessPx + 2) * 10,
            rimEdgeFallbackDist = TerrainChunk.RimThicknessPx * 5,
            rimColor            = TerrainChunk.RimColor,

            // [rect-local] 방금 거리장이 갱신된 범위(_lastExt)만 dispatch.
            rectMinX = _lastExtMinX,
            rectMinY = _lastExtMinY,
            rectWidth = _lastExtMaxX - _lastExtMinX
        };

        int visW = _lastExtMaxX - _lastExtMinX;
        int visH = _lastExtMaxY - _lastExtMinY;
        if (visW <= 0 || visH <= 0) return; // 빈 rect — 핸들은 그대로 둔다 (설계 §5.4 주의 3)

        // [half-res] VisualJob은 full DistanceField를 읽는다 → Upsample(full DF writer) 완료 후 실행.
        _visualsJobHandle = job.Schedule(visW * visH, JOB_BATCH_SIZE, _upsampleHandle);
    }


    // REMOVED: ScheduleLightingJob (Lighting BFS logic removed)

    // ============================================================================================================
    //  JOB SCHEDULING: 1:1 (조합은 호출자가 정한다)
    // ============================================================================================================

    /// <summary>
    /// full BasePixels/PixelInfo → half 다운샘플. Init·Chamfer가 half 격자에서 읽는다.
    /// 각 라운드 진입 시 1회 호출한다 — 라운드 사이에 플레이어가 다시 팔 수 있으므로
    /// 항상 라이브 BasePixels 를 다시 읽어야 stale halfBase 를 피한다.
    /// </summary>
    /// <summary>
    /// [생성 전용] 절차적 굴을 뚫는다. 청크 전체를 dispatch 도메인으로 쓴다.
    ///
    /// 호출 시점은 Reuse_Step1_Prepare 안, LoadPixelData/FillPixelInfo 뒤 · ScheduleInitArea 앞.
    /// SetDirtyRect 보다 먼저 불려도 상관없다 — 이 잡만 rect 를 안 쓰고 전체를 돈다.
    ///
    /// 여기서 BasePixels 를 읽는 메인 스레드 코드(UpdateCollider, 돌·광물 배치)와 겹치지 않는 근거:
    /// ChunkLoadingRunner 의 Phase1 대기 루프가 IsJobRunning() 이 false 가 될 때까지 프레임을 넘긴 뒤에야
    /// Phase 2(Reuse_Step2_Finalize → 데코레이터)로 넘어간다.
    /// </summary>
    /// <summary>
    /// 해시 기반 난수 한 걸음(LCG). Unity 의 Random 을 쓰지 않는 이유: 청크 로드 순서와
    /// 무관하게 **좌표만 보고 같은 값**이 나와야 언로드→재로드에도 굴이 그대로다(세이브 불필요).
    /// </summary>
    private static float NextRand(ref uint state)
    {
        state = state * 1664525u + 1013904223u;
        return (state >> 8) * (1f / 16777216f);
    }

    /// <summary>좌표 해시로 시작 상태를 만든다. salt 로 용도(존재/연결/Y)를 갈라 준다.</summary>
    private static uint HashState(int a, int b, int salt)
    {
        uint st = math.hash(new int3(a, b, salt));
        return st == 0u ? 0x9E3779B9u : st;
    }

    /// <summary>경계 b(= 청크 b-1 과 b 사이)의 원시 연결 여부. 체인 길이 제한 전 값.</summary>
    private static bool RawLinkAt(int b, int chunkY, int worldSeed, CaveCarveSettings settings)
    {
        uint st = HashState(b, chunkY, (worldSeed ^ settings.seedSalt) + 977);
        return NextRand(ref st) < settings.linkChance;
    }

    /// <summary>
    /// 경계 b 를 실제로 이을지. 조건 세 가지:
    ///   1. 원시 난수 통과
    ///   2. 양쪽 청크에 굴이 있음 (없으면 통로가 벽으로 끝난다)
    ///   3. 연속 연결이 maxLinkedChunks 를 넘지 않음
    ///
    /// 3번은 이 경계에서 **왼쪽으로 연속된 원시 연결 수**를 세서 자른다. 원시값만 보므로
    /// 어느 청크가 계산하든 같은 답이 나온다(경계를 공유하는 두 청크가 반드시 일치해야 한다).
    /// 원시 연속이 길면 앞의 maxLinkedChunks 개만 이어지고 나머지는 끊긴다.
    /// </summary>
    private static bool LinkAt(int b, int chunkY, int worldSeed, CaveCarveSettings settings, ChunkData data)
    {
        if (settings.linkChance <= 0f) return false;
        if (!RawLinkAt(b, chunkY, worldSeed, settings)) return false;

        // [중요] 해시(BlobExistsAt)가 아니라 **실제 배치 성공**을 봐야 한다.
        // 해시는 첫 난수만 보는데 ComputeCapsule 은 그 뒤 기하 제약으로도 실패할 수 있다.
        // 그 경우 FillBlobPlacement 가 통로를 아예 안 그리는데 이웃은 계속 그려서,
        // 통로가 청크 경계에서 수직 벽으로 뚝 끊긴 자국이 남는다.
        if (!ComputeCapsule(b - 1, chunkY, worldSeed, settings, data).exists) return false;
        if (!ComputeCapsule(b,     chunkY, worldSeed, settings, data).exists) return false;

        int maxLinks = math.max(1, settings.maxLinkedChunks - 1);

        int run = 1;
        for (int i = 1; i <= 8; i++)   // 탐색 상한 — 원시 연속이 8을 넘으면 어차피 끊긴다
        {
            if (!RawLinkAt(b - i, chunkY, worldSeed, settings)) break;
            run++;
            if (run > maxLinks) return false;
        }

        return true;
    }

    /// <summary>
    /// 한 청크의 캡슐 배치. **좌표와 설정만** 보고 계산하므로 이웃 청크 것도 그대로 구할 수 있다 —
    /// 연결 통로가 이웃 굴의 끝에 정확히 맞물리는 근거다(이웃이 로드돼 있을 필요 없음).
    /// 연결 정보는 여기서 다루지 않는다. 넣으면 이웃 계산이 재귀가 된다.
    /// </summary>
    private struct BlobPlacement
    {
        public bool   exists;
        public float2 center;
        public float  halfLength;
        public float  radius;
        public float  cos, sin;
        public float  waveAmp, waveFreq, wavePhase;
        public float  bulgeSeed;
        public float2 leftTip, rightTip;
    }

    private static BlobPlacement ComputeCapsule(int chunkX, int chunkY, int worldSeed,
                                                CaveCarveSettings settings, ChunkData data)
    {
        BlobPlacement b = default;

        uint state = HashState(chunkX, chunkY, worldSeed ^ settings.seedSalt);
        if (NextRand(ref state) >= settings.blobChance) return b;

        float margin = math.max(0f, settings.edgeMarginPx);
        float radius = math.lerp(settings.blobRadiusMinPx, settings.blobRadiusMaxPx, NextRand(ref state));

        // 흔들림: 진폭과 파장을 둘 다 굴리므로 짧은 파장이면 지그재그, 길면 완만한 곡선이 된다.
        // (파장을 고정하면 굴 길이가 항상 파장 1~2개라 전부 'S자 한 굽이'로 보인다.)
        float waveLen = math.lerp(settings.blobWaveLenMinPx, settings.blobWaveLenMaxPx, NextRand(ref state));
        float waveAmp = math.lerp(settings.blobWaveAmpMinPx, settings.blobWaveAmpMaxPx, NextRand(ref state));

        // 둘을 독립으로 굴리면 "진폭 최대 × 파장 최소" 조합에서 중심선이 급격히 꺾인다.
        // 사인 중심선의 최대 기울기는 2π·amp/λ 이므로, 그 값을 blobWaveMaxSlope 로 눌러
        // 진폭을 파장에 맞춰 깎는다. 짧은 파장이면 자동으로 얕게 흔들린다.
        // 예: slope 0.5 = 최대 약 27°.
        if (settings.blobWaveMaxSlope > 0f && waveLen > 0f)
        {
            float ampLimit = settings.blobWaveMaxSlope * waveLen / (2f * math.PI);
            waveAmp = math.min(waveAmp, ampLimit);
        }

        // 기울기: 수평 일변도를 깬다.
        float tilt = math.radians(settings.blobTiltMaxDeg * (NextRand(ref state) * 2f - 1f));
        float cos = math.cos(tilt);
        float sin = math.sin(tilt);

        float length = math.lerp(settings.blobLengthMinPx, settings.blobLengthMaxPx, NextRand(ref state));
        float halfLength = length * 0.5f;

        // 기울어진 캡슐의 AABB 반크기:
        //   halfW = (halfLength + rMax)|cos| + (rMax + waveAmp)|sin|
        //   halfH = (halfLength + rMax)|sin| + (rMax + waveAmp)|cos|
        // 이게 여백 안에 들어가도록 halfLength 를 줄여 맞춘다.
        float ac = math.abs(cos);
        float as_ = math.abs(sin);

        float availW = (data.Width  - 2f * margin) * 0.5f;
        float availH = (data.Height - 2f * margin) * 0.5f;

        const float BIG = 1e9f;

        // [최소 종횡비] 굵은 굴일수록 청크에 안 들어가 길이가 깎인다 — 그대로 두면 반경이 큰
        // 추첨일수록 "짧고 뚱뚱한 덩어리"가 되어 길이 하한이 무너진다(10 퍼센타일 387px).
        // 길이가 모자라면 **길이를 포기하는 대신 반경을 줄여** 되찾는다. 굴은 길쭉함을 유지하고,
        // 굵기 추첨의 상단만 살짝 눌린다(평균 반경 95 → 91).
        //
        // rMax 가 radius 에 의존하고 maxAlong 이 다시 rMax 에 의존해서 닫힌 해가 지저분하다.
        // 반경을 조금씩 줄이며 다시 재는 고정점 반복이 훨씬 읽기 쉽고, 청크당 최대 12회라 공짜다.
        float rMax, perp, maxAlong;
        for (int guard = 0; ; guard++)
        {
            // 두께 변조까지 부풀었을 때의 최대 반두께. 배치 여유는 이 값으로 잡아야 청크를 안 넘는다.
            // 끝방 배율까지 반영해야 부푼 끝이 청크 경계에 안 닿는다(닿으면 수직 절단이 생긴다).
            rMax = radius * (1f + settings.blobBulgeAmp) * (1f + settings.blobEndRoomGain)
                 + settings.blobWallAmpPx;
            perp = rMax + waveAmp;

            float limitFromW = (ac > 1e-4f) ? (availW - perp * as_) / ac : BIG;
            float limitFromH = (as_ > 1e-4f) ? (availH - perp * ac) / as_ : BIG;

            maxAlong = math.min(limitFromW, limitFromH);   // halfLength + rMax 의 상한

            // [-1px] 여유를 빼는 이유: 아래 minX/maxX 검사가 여기서 만든 halfW 를 다시 재는데,
            // 여유가 없으면 halfW 가 availW 와 정확히 같아져 부동소수점 오차만으로 minX > maxX 가
            // 성립한다. 길이가 폭 한계에 걸리는 경우(현 설정에선 거의 전부)라 실제로 굴의 1~5% 가
            // 통째로 버려지고 있었다. (배치 실패 자체는 LinkAt 이 ComputeCapsule 로 재확인하므로
            // 이제 이음매를 깨뜨리진 않지만, 굴이 그냥 사라지는 건 그대로 손해라 여유를 둔다.)
            halfLength = math.min(length * 0.5f, maxAlong - rMax - 1f);

            if (settings.blobMinAspect <= 0f) break;
            if (halfLength >= settings.blobMinAspect * radius) break;   // 충분히 길쭉하다
            if (radius <= settings.blobRadiusMinPx * 0.65f) break;      // 더 줄이면 통로가 좁아진다
            if (guard >= 12) break;

            radius *= 0.92f;
        }

        // 길이가 0 이하로 깎이면 굴을 못 놓는다. 살짝 음수면 원형 방으로 떨어뜨린다.
        if (halfLength < 0f)
        {
            if (maxAlong - rMax > -1f) halfLength = 0f;   // 둥근 방
            else return b;
        }

        float halfW = (halfLength + rMax) * ac + perp * as_;
        float halfH = (halfLength + rMax) * as_ + perp * ac;

        float minX = margin + halfW, maxX = data.Width  - margin - halfW;
        float minY = margin + halfH, maxY = data.Height - margin - halfH;
        if (minX > maxX || minY > maxY) return b;

        b.exists     = true;
        b.center     = new float2(math.lerp(minX, maxX, NextRand(ref state)),
                                  math.lerp(minY, maxY, NextRand(ref state)));
        b.halfLength = halfLength;
        b.radius     = radius;
        b.cos        = cos;
        b.sin        = sin;
        b.waveAmp    = waveAmp;
        b.waveFreq   = waveLen > 0f ? (2f * math.PI / waveLen) : 0f;
        b.wavePhase  = NextRand(ref state) * 2f * math.PI;
        b.bulgeSeed  = NextRand(ref state) * 1000f;

        float2 axis = new float2(cos, sin);
        float2 perpDir = new float2(-sin, cos);
        b.leftTip  = b.center - axis * halfLength
                   + perpDir * (waveAmp * math.sin(-halfLength * b.waveFreq + b.wavePhase));
        b.rightTip = b.center + axis * halfLength
                   + perpDir * (waveAmp * math.sin( halfLength * b.waveFreq + b.wavePhase));

        return b;
    }

    /// <summary>
    /// 연결 통로의 반두께. 얇은 쪽 굴에 비례시킨다 — 통로가 굴보다 굵으면 이음매가 튄다.
    /// min() 이라 인자 순서와 무관하게 경계 하나에 값이 하나로 정해진다(양쪽 청크가 같은 굵기).
    /// </summary>
    private static float LinkRadiusFor(BlobPlacement a, BlobPlacement b, CaveCarveSettings settings)
    {
        float thin = math.min(a.radius, b.radius);
        return math.max(settings.minRadiusPx + 35f, thin * settings.linkRadiusFactor);
    }

    /// <summary>
    /// [blob] 이 청크의 굴 배치 + 좌우 연결 통로를 잡 필드에 채운다.
    /// </summary>
    private static void FillBlobPlacement(ref TerrainJobs.CaveCapsuleGeometry field, ChunkData data,
                                          int chunkX, int chunkY, int worldSeed, CaveCarveSettings settings)
    {
        BlobPlacement b = ComputeCapsule(chunkX, chunkY, worldSeed, settings, data);
        if (!b.exists) { field.blobExists = 0; return; }

        field.blobExists     = 1;
        field.blobCenter     = b.center;
        field.blobHalfLength = b.halfLength;
        field.blobRadius     = b.radius;
        field.blobWaveAmp    = b.waveAmp;
        field.blobWaveFreq   = b.waveFreq;
        field.blobWavePhase  = b.wavePhase;
        field.blobCos        = b.cos;
        field.blobSin        = b.sin;
        field.blobBulgeAmp   = settings.blobBulgeAmp;
        field.blobBulgeScale = settings.blobBulgeLenPx > 0f ? (1f / settings.blobBulgeLenPx) : 0f;
        field.blobBulgeSeed  = b.bulgeSeed;
        // 변조로 얇아져도 통로 폭 70px(= (35 - 침식 15) * 2 + 여유)은 남긴다.
        field.blobMinRadius  = settings.minRadiusPx + 35f;
        field.blobWallAmp    = settings.blobWallAmpPx;
        field.blobWallScale  = settings.blobWallScale;
        field.blobEndRoomGain  = settings.blobEndRoomGain;
        field.blobEndRoomLenPx = settings.blobEndRoomLenPx;

        // ─── 좌우 이웃 연결 ─────────────────────────────────────────────────
        // 통로는 **양쪽 굴의 끝점을 잇는 선분 하나**다. 두 청크가 그 같은 선분을 각자 자기 로컬
        // 좌표로 표현하므로, 경계에서 중심선도 굵기도 정확히 이어진다.
        //
        // ⚠ 예전에는 각 청크가 "경계 위의 한 점 → 자기 굴 끝"을 그렸다. 경계 점의 y 는 양쪽이
        //   같게 계산됐지만 **방향이 서로 달라** 중심선이 경계에서 꺾였고, 통로가 짧을수록
        //   꺾임이 커져 경계에서 60~70px 어긋났다. 그러면 양쪽 통로가 서로 못 만나고 각자
        //   수직 벽으로 끝난다(2026-09-03 15:04 리포트: 한쪽 y345~405, 반대쪽 y477~550).
        //   "경계에서 두 값이 같다"로는 부족하고 **선분 전체가 같아야** 한다.
        if (settings.leftNeighborCarves && LinkAt(chunkX, chunkY, worldSeed, settings, data))
        {
            BlobPlacement nb = ComputeCapsule(chunkX - 1, chunkY, worldSeed, settings, data);
            field.hasLinkLeft = 1;
            field.linkRadiusLeft = LinkRadiusFor(nb, b, settings);
            // 이웃 굴의 오른쪽 끝을 내 로컬 좌표로 옮긴다(왼쪽 청크라 -Width).
            field.linkLeftA = new float2(nb.rightTip.x - data.Width, nb.rightTip.y);
            field.linkLeftB = b.leftTip;
        }

        if (settings.rightNeighborCarves && LinkAt(chunkX + 1, chunkY, worldSeed, settings, data))
        {
            BlobPlacement nb = ComputeCapsule(chunkX + 1, chunkY, worldSeed, settings, data);
            field.hasLinkRight = 1;
            field.linkRadiusRight = LinkRadiusFor(b, nb, settings);
            field.linkRightA = new float2(nb.leftTip.x + data.Width, nb.leftTip.y);
            field.linkRightB = b.rightTip;
        }
    }

    /// <summary>
    /// 굴 판정식 한 벌을 만든다. **카빙 잡과 노출 판정이 같은 것을 봐야** 하므로 여기 하나로 둔다
    /// (따로 만들면 "판정은 굴이라는데 안 파여 있는" 상태가 생긴다).
    /// 순수 계산이라 청크가 로드돼 있지 않아도 부를 수 있다.
    ///
    /// ⚠ 단, 이건 **판정식만** 한 벌이다. 실제 카빙은 여기에 격자 연결성(keptCells)을 한 겹 더 건다:
    ///       카빙   = IsCaveEroded(픽셀) AND keptCells[셀] != 0
    ///       노출판정 = IsCaveEroded(픽셀)
    ///   그래서 카빙 ⊆ 노출판정이다. 어긋나는 방향은 "판정은 굴인데 안 파임" 한쪽뿐이고,
    ///   결과는 굴이 조금 일찍 열리는 것에 그친다. **파였는데 노출을 못 보는 일은 없다**
    ///   (굴 안에 서 있는데 안 열리는 사고가 구조적으로 불가능한 근거).
    ///
    ///   맞추려면 노출 판정도 격자 마스크를 만들어야 하는데 셀 62,500개 × 최대 9 snoise 라
    ///   메인 스레드에서 돌릴 물건이 아니다. 그래서 의도적으로 안 맞춘다.
    ///
    ///   간극이 커지는 조건 두 가지 — 이때는 위 근거를 다시 볼 것:
    ///     · caveKeepLargestOnly 를 끄면 (마스크가 아무것도 안 지우므로 간극은 오히려 사라진다)
    ///     · caveBlobWallAmpPx 를 크게 올리면 (요철이 만든 떨어진 껍질이 늘어 간극이 벌어진다)
    ///     · caveUseBlob=false(노이즈 모드)로 돌리면 마스크가 실제로 많은 조각을 지운다 → 간극 큼
    /// </summary>
    public static TerrainJobs.CaveCapsuleGeometry BuildCaveGeometry(
        ChunkData data, int chunkX, int chunkY, int worldSeed, CaveCarveSettings settings)
    {
        // 층마다 다른 도메인 오프셋 — 안 주면 모든 층의 굴 배치가 y 축으로만 다른 같은 무늬가 된다.
        // worldSeed 를 섞으므로 시드가 바뀌면 배치도 통째로 바뀐다.
        uint h = math.hash(new int2(worldSeed, settings.seedSalt));
        float2 seedOffset = new float2(h & 0xFFFF, (h >> 16) & 0xFFFF) * 1.7f;

        var field = new TerrainJobs.CaveCapsuleGeometry
        {
            scale       = settings.scale,
            threshold   = settings.threshold,
            detail      = settings.detail,
            // 요철 진폭까지 포함해 정규화 — 안 넣으면 ridge 가 1을 넘어 threshold 가 헐거워진다.
            invRidgeMax = 1f / (1f + settings.detail + settings.roughness * 1.5f),

            regionScale     = settings.regionScale,
            regionThreshold = settings.regionThreshold,
            regionBand      = settings.regionBand,
            regionFalloff   = settings.regionFalloff,

            aspect     = settings.aspect <= 0f ? 1f : settings.aspect,
            roughness  = settings.roughness,
            roughFreq1 = settings.roughFreq1 <= 0f ? 4.3f : settings.roughFreq1,
            roughFreq2 = settings.roughFreq2 <= 0f ? 8.5f : settings.roughFreq2,

            chunkOrigin  = new float2(chunkX * data.Width, chunkY * data.Height) + seedOffset,
            width        = data.Width,
            height       = data.Height,
            edgeMarginPx = settings.edgeMarginPx,

            useBlob = settings.useBlob ? (byte)1 : (byte)0,
        };

        if (settings.useBlob)
            FillBlobPlacement(ref field, data, chunkX, chunkY, worldSeed, settings);

        return field;
    }

    public void ScheduleCarveCave(ChunkData data, int chunkX, int chunkY, int worldSeed, CaveCarveSettings settings)
    {
        if (!settings.enabled) return;
        if (!data.BasePixels.IsCreated || !data.PixelInfo.IsCreated) return;
        // scale 은 noise 모드 전용이다. blob 모드에서 이걸로 막으면 굴이 조용히 사라진다.
        if (!settings.useBlob && settings.scale <= 0f) return;

        var field = BuildCaveGeometry(data, chunkX, chunkY, worldSeed, settings);

        // 연결성 판정용 저해상도 격자. 침식(minRadiusPx=8)이 폭 16px 미만 통로를 이미 지우므로
        // 4px 셀이면 살아남은 통로가 최소 4셀 폭이라 연결이 끊기지 않는다.
        // 1000x1000 청크 기준 250x250 = 62,500셀 — BFS 두 번이 사실상 공짜다.
        const int CELL_PX = 4;
        int cellsX = (data.Width  + CELL_PX - 1) / CELL_PX;
        int cellsY = (data.Height + CELL_PX - 1) / CELL_PX;
        int cellCount = cellsX * cellsY;

        var cells = new NativeArray<byte>(cellCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var cellStack = new NativeArray<int>(cellCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        var maskJob = new TerrainJobs.CaveGridMaskJob
        {
            cells  = cells,
            field  = field,
            cellsX = cellsX,
            cellPx = CELL_PX,
            // 카빙 잡과 같은 값이어야 한다 — 다르면 침식으로 지워질 목을 '연결됨'으로 오판해
            // 그 목으로만 이어진 외톨이 구멍이 살아남는다.
            minRadiusPx = settings.minRadiusPx,
        };
        JobHandle maskHandle = maskJob.Schedule(cellCount, 256, AllPrevHandles());

        var keepJob = new TerrainJobs.CaveKeepBiggestJob
        {
            cells   = cells,
            stack   = cellStack,
            cellsX  = cellsX,
            cellsY  = cellsY,
            // 셀 중심만 샘플링하므로 통로 가장자리 셀은 solid 로 잡힌다.
            // 1셀(4px) 팽창이 없으면 카빙이 그 셀을 건너뛰어 벽에 4px 계단이 생긴다.
            dilate  = 1,
            enabled = settings.keepLargestOnly ? (byte)1 : (byte)0,
        };
        JobHandle keepHandle = keepJob.Schedule(maskHandle);

        var job = new TerrainJobs.CaveCarvePixelsJob
        {
            baseData    = data.BasePixels,
            pixelInfo   = data.PixelInfo,
            keptCells   = cells,
            field       = field,
            width       = data.Width,
            cellsX      = cellsX,
            cellPx      = CELL_PX,
            minRadiusPx = settings.minRadiusPx,
        };

        // 배치 64(JOB_BATCH_SIZE)는 rect 단위 잡 기준이다. 여기는 100만 픽셀을 한 번에 도는
        // 단발 잡이라 그 값이면 배치가 1.5만 개로 쪼개져 스케줄 오버헤드만 늘어난다.
        _caveHandle = job.Schedule(data.BasePixels.Length, 2048, keepHandle);

        // 격자 버퍼는 카빙이 끝나면 필요 없다. Dispose(handle) 로 넘겨 메인 스레드를 안 막는다.
        // CompleteCarve()/CompleteAllJobs() 가 _caveHandle 을 완료시키면 이 해제도 따라 끝난다.
        cells.Dispose(_caveHandle);
        cellStack.Dispose(_caveHandle);
    }

    public void ScheduleDownsample(ChunkData data)
    {
        if (!HalfExtRect(out int minX, out int minY, out int rw, out int rh)) return;

        var job = new TerrainJobs.DownsampleMaskJob
        {
            baseData = data.BasePixels,
            pixelInfo = data.PixelInfo,
            baseHalf = data.BasePixelsHalf,
            pixelInfoHalf = data.PixelInfoHalf,
            fullWidth = data.Width,
            fullHeight = data.Height,
            halfWidth = data.HalfWidth,
            rectMinX = minX, rectMinY = minY, rectWidth = rw
        };
        _downsampleHandle = job.Schedule(rw * rh, 64, AllPrevHandles());
    }

    /// <summary>
    /// dirty rect의 거리장을 리셋(솔리드→maxDist, 에어→0)하고 ext 영역의 시드를 보정한다.
    /// ScheduleDownsample 이후에 호출할 것 — BasePixelsHalf 를 읽는다.
    /// </summary>
    public void ScheduleInit(ChunkData data)
    {
        if (!HalfExtRect(out int minX, out int minY, out int rw, out int rh)) return;

        var job = new TerrainJobs.InitDistanceFieldJob
        {
            baseData = data.BasePixelsHalf,
            pixelInfo = data.PixelInfoHalf,
            distanceField = data.DistanceFieldHalf,
            maxDist = 255,
            dirtyMinX = _lastDirtyMinX / 2,
            dirtyMinY = _lastDirtyMinY / 2,
            dirtyMaxX = (_lastDirtyMaxX + 1) / 2,
            dirtyMaxY = (_lastDirtyMaxY + 1) / 2,
            extMinX = minX, extMinY = minY, extWidth = rw,
            width = data.HalfWidth
        };
        _initJobHandle = job.Schedule(rw * rh, 64,
            JobHandle.CombineDependencies(_downsampleHandle, AllPrevHandles()));

        _externalDistanceFieldReaders = default; // Init 가 prior external readers 를 포섭함
    }

    /// <summary>half 거리장 → full 거리장(×2). VisualJob 직전에만 필요하다.</summary>
    public void ScheduleUpsample(ChunkData data)
    {
        if (!FullExtRect(out int minX, out int minY, out int rw, out int rh)) return;

        var job = new TerrainJobs.UpsampleDistanceJob
        {
            halfField = data.DistanceFieldHalf,
            fullField = data.DistanceField,
            fullWidth = data.Width,
            halfWidth = data.HalfWidth,
            maxDist = 255,
            rectMinX = minX, rectMinY = minY, rectWidth = rw
        };
        // Upsample: halfDF reader + fullDF writer.
        // halfDF writer가 4개(Init/BoundarySync/ChamferFwd/ChamferBwd)이고 fullDF reader(이전 Visual)도 있으므로
        // 다른 스케줄 메서드와 동일하게 AllPrevHandles()로 over-depend한다 —
        // 이 클래스는 "조합은 호출자가 정한다"는 계약이라 호출 순서에 의존하면 안 된다.
        _upsampleHandle = job.Schedule(rw * rh, JOB_BATCH_SIZE, AllPrevHandles());
    }

    /// <summary>
    /// [래퍼] SetDirtyRect + Downsample + Init.
    /// 단발 전체 갱신 경로(RefreshVisuals 등)와 LateUpdate Step1 이 사용한다.
    /// </summary>
    public void ScheduleInitLighting(ChunkData data, int minX = 0, int minY = 0, int maxX = -1, int maxY = -1)
    {
        if (maxX < 0) maxX = data.Width;
        if (maxY < 0) maxY = data.Height;

        SetDirtyRect(data, minX, minY, maxX, maxY);
        ScheduleDownsample(data);
        ScheduleInit(data);
    }

    /// <summary>
    /// [P0] 외부 청크의 BoundarySync 가 우리 distanceField 를 [ReadOnly] 로 읽었음을 등록.
    /// 이후 우리 쪽 writer (Init / BoundarySync / Chamfer) 스케줄 시 deps 에 합쳐진다.
    /// 호출자: TerrainLightingCalculator.ScheduleBoundarySyncJob() — 자기 BoundarySync 스케줄 직후.
    /// </summary>
    public void RegisterDistanceFieldReader(JobHandle h)
    {
        _externalDistanceFieldReaders = JobHandle.CombineDependencies(_externalDistanceFieldReaders, h);
    }

    /// <summary>
    /// 이전 Init 잡이 완료된 경우에만 Init을 스케줄 (블로킹 없음).
    /// LateUpdate 시작 시 선스케줄 용도 — 실행 중이면 건너뛰고 Round1(TerrainChunk.ScheduleDistancePass의 skipInit:false 분기)에서 재시도.
    /// </summary>
    public bool TryScheduleInitLighting(ChunkData data, int minX, int minY, int maxX, int maxY)
    {
        if (!_initJobHandle.IsCompleted) return false;
        ScheduleInitLighting(data, minX, minY, maxX, maxY);
        return true;
    }

    /// <summary>
    /// [P0] BoundarySync Burst Job 스케줄. Init 완료 + 이웃 lighting 완료 후 자기 distanceField 경계에 기록.
    /// 메인 스레드 블로킹 없음. 이후 ScheduleChamferPasses 가 자동으로 이 핸들을 의존성에 포함한다.
    /// </summary>
    public JobHandle ScheduleBoundarySync(ChunkData data, BoundaryNeighborSet n)
    {
        // 의존성: 자기 init (distanceField writer) + 이전 visual (reader)
        //        + 외부 청크의 distanceField read 핸들 (P0)
        //        + 각 이웃의 lighting handle (이웃의 distanceField 가 안정된 후 읽기 위함)
        JobHandle deps = JobHandle.CombineDependencies(_initJobHandle, _externalDistanceFieldReaders, _visualsJobHandle);
        deps = JobHandle.CombineDependencies(deps, _upsampleHandle); // [half-res] 이전 Upsample(halfDF reader) 후
        if (n.hasLeft)        deps = JobHandle.CombineDependencies(deps, n.leftHandle);
        if (n.hasRight)       deps = JobHandle.CombineDependencies(deps, n.rightHandle);
        if (n.hasTop)         deps = JobHandle.CombineDependencies(deps, n.topHandle);
        if (n.hasBottom)      deps = JobHandle.CombineDependencies(deps, n.bottomHandle);
        if (n.hasTopLeft)     deps = JobHandle.CombineDependencies(deps, n.topLeftHandle);
        if (n.hasTopRight)    deps = JobHandle.CombineDependencies(deps, n.topRightHandle);
        if (n.hasBottomLeft)  deps = JobHandle.CombineDependencies(deps, n.bottomLeftHandle);
        if (n.hasBottomRight) deps = JobHandle.CombineDependencies(deps, n.bottomRightHandle);

        var job = new TerrainJobs.BoundarySyncJob
        {
            distanceField    = data.DistanceFieldHalf, // [half-res]
            leftField        = n.hasLeft        ? n.left        : _emptyLeft,
            rightField       = n.hasRight       ? n.right       : _emptyRight,
            topField         = n.hasTop         ? n.top         : _emptyTop,
            bottomField      = n.hasBottom      ? n.bottom      : _emptyBottom,
            topLeftField     = n.hasTopLeft     ? n.topLeft     : _emptyTopLeft,
            topRightField    = n.hasTopRight    ? n.topRight    : _emptyTopRight,
            bottomLeftField  = n.hasBottomLeft  ? n.bottomLeft  : _emptyBottomLeft,
            bottomRightField = n.hasBottomRight ? n.bottomRight : _emptyBottomRight,
            width            = data.HalfWidth,  // [half-res]
            height           = data.HalfHeight, // [half-res]
            hasLeft          = n.hasLeft,
            hasRight         = n.hasRight,
            hasTop           = n.hasTop,
            hasBottom        = n.hasBottom,
            hasTopLeft       = n.hasTopLeft,
            hasTopRight      = n.hasTopRight,
            hasBottomLeft    = n.hasBottomLeft,
            hasBottomRight   = n.hasBottomRight,
            isSkyAbove       = n.isSkyAbove,
            maxDist          = 255,

            // [Opt] 경계 sync를 dirty span(±EXT_MARGIN)으로 국한 — 전체 엣지(1000px) 대신 파낸 구간만.
            // 이 시점의 _lastExt는 SetDirtyRect가 세팅한 dirty rect±50 (아래 확장 블록 실행 전).
            syncMinX = _lastExtMinX / 2, syncMaxX = (_lastExtMaxX + 1) / 2, // [half-res] ÷2
            syncMinY = _lastExtMinY / 2, syncMaxY = (_lastExtMaxY + 1) / 2,
        };

        _boundarySyncHandle = job.Schedule(deps);
        _externalDistanceFieldReaders = default; // BoundarySync 가 prior external readers 를 포섭함

        // BoundarySync는 경계에서 SYNC_DEPTH(=50px) 깊이까지만 distanceField를 수정한다.
        // (BoundarySyncJob.SYNC_DEPTH 참조 — 반드시 동일 값 유지)
        // 따라서 Round 2 chamfer ext rect는 "이번 dirty가 실제로 닿은 경계" 방향으로만 strip 확장하면 된다.
        //
        // 과거엔 이웃이 하나라도 로드돼 있으면(=거의 항상) 청크 전체(1000x1000)를 단일스레드
        // 2패스로 훑었다. 이는 경계 파기뿐 아니라 청크 중앙 파기에서도 매 프레임 발생해
        // 경계 근처(파기가 여러 청크로 번짐)에서 수십 ms Idle 스파이크의 주원인이었다.
        //
        // 핵심: 확장 여부의 키는 "이웃 로드 여부"가 아니라 "dirty rect가 그 경계에 닿았는가"이다.
        //   - dirty rect가 경계에서 멀면(청크 중앙) → BoundarySync가 값을 바꾸지 않으므로 strip 불필요.
        //   - MarkNeighborsDirty가 이웃을 dirty로 찍을 때도 이미 "맞닿은 경계 strip rect"만 주므로,
        //     이웃 측 dirty rect도 항상 해당 경계에 닿아 있어 이 키로 정확히 커버된다.
        // _lastExt는 직전 ScheduleInitLighting/SetDirtyRect에서 dirty rect(±50)로 세팅된 상태.
        // strip 깊이(SYNC_DEPTH+여유)가 얕으면 경계 "꺾임(kink)" 재발. 코너 2면 동시엔 전체로 폴백.
        {
            const int SYNC_DEPTH  = 50;   // BoundarySyncJob의 상수와 반드시 일치
            const int PROP_MARGIN = 50;   // 안쪽 전파 여유
            const int strip = SYNC_DEPTH + PROP_MARGIN;

            // dirty rect(=현재 _lastExt)가 각 경계에 닿았는지 — 확장 전에 4개를 먼저 확정한다.
            // (아래 if들이 _lastExt를 수정하므로, near* 를 미리 계산하지 않으면 확장이 연쇄해 전체 청크로 번진다.)
            // 보수적 판정: 닿으면 무조건 확장 → under-cover(꺾임)보다 over-cover(약간 낭비)를 택함.
            bool nearLeft   = _lastExtMinX <= SYNC_DEPTH;
            bool nearRight  = _lastExtMaxX >= data.Width  - SYNC_DEPTH;
            bool nearTop    = _lastExtMaxY >= data.Height - SYNC_DEPTH;
            bool nearBottom = _lastExtMinY <= SYNC_DEPTH;

            // [Opt] 경계 법선(normal) 방향으로만 strip 확장. 과거엔 평행 방향을 청크 전체(full height/width)로
            // 강제해 chamfer가 1000px를 훑었으나(드릴 시 fwd+bwd ~19ms), 이제 BoundarySyncJob이 sync 범위를
            // dirty span으로 국한하므로 평행 방향도 dirty span(=현재 _lastExt)만 재계산하면 충분하다.
            // 파낸 구간 밖 경계는 base가 안 변했고 이웃도 strip만 dirty → 이전 값 유효.
            if (n.hasLeft && nearLeft)
            {
                _lastExtMinX = 0;
                _lastExtMaxX = Mathf.Max(_lastExtMaxX, strip);
            }
            if (n.hasRight && nearRight)
            {
                _lastExtMaxX = data.Width;
                _lastExtMinX = Mathf.Min(_lastExtMinX, data.Width - strip);
            }
            if ((n.hasTop || n.isSkyAbove) && nearTop)
            {
                _lastExtMaxY = data.Height;
                _lastExtMinY = Mathf.Min(_lastExtMinY, data.Height - strip);
            }
            if (n.hasBottom && nearBottom)
            {
                _lastExtMinY = 0;
                _lastExtMaxY = Mathf.Max(_lastExtMaxY, strip);
            }
        }

        return _boundarySyncHandle;
    }

    /// <summary>
    /// 이웃 청크가 BoundarySync 의존성으로 사용할 distanceField writer 핸들의 결합.
    /// Init / BoundarySync / ChamferForward / ChamferBackward 모두 distanceField 를 쓰므로
    /// Job System 안전 검증이 누락 없이 통과되도록 4개를 합성해 반환한다.
    /// (예: 같은 프레임 Step 1 에서 Init 만 스케줄된 이웃의 경우 _initJobHandle 이 pending writer 이고
    ///  _lightingJobHandle 은 이전 프레임의 완료된 stale 핸들 — 후자만 넘기면 InvalidOperationException 발생.)
    /// </summary>
    public JobHandle GetLightingHandle()
    {
        return JobHandle.CombineDependencies(
            JobHandle.CombineDependencies(_initJobHandle, _boundarySyncHandle),
            JobHandle.CombineDependencies(_forwardJobHandle, _lightingJobHandle));
    }

    /// <summary>
    /// [2패스 Chamfer] Chamfer forward/backward 만 스케줄한다.
    /// DS(ScheduleDownsample)·Upsample(ScheduleUpsample)은 호출자가 별도로 스케줄한다.
    ///
    /// ext rect는 이 시점의 _lastExt* — ScheduleBoundarySync 가 호출됐다면 strip 확장이 반영돼 있다.
    /// </summary>
    public void ScheduleChamferPasses(ChunkData data)
    {
        if (!HalfExtRect(out int minX, out int minY, out int rw, out int rh)) return;

        int maxX = minX + rw;
        int maxY = minY + rh;

        JobHandle dep = AllPrevHandles();

        var fwd = new TerrainJobs.ChamferForwardPassJob
        {
            distanceField = data.DistanceFieldHalf,
            baseData  = data.BasePixelsHalf,
            pixelInfo = data.PixelInfoHalf,
            width = data.HalfWidth, height = data.HalfHeight, maxDist = 255,
            extMinX = minX, extMinY = minY, extMaxX = maxX, extMaxY = maxY
        };
        _forwardJobHandle = fwd.Schedule(dep);

        var bwd = new TerrainJobs.ChamferBackwardPassJob
        {
            distanceField = data.DistanceFieldHalf,
            baseData  = data.BasePixelsHalf,
            pixelInfo = data.PixelInfoHalf,
            width = data.HalfWidth, height = data.HalfHeight, maxDist = 255,
            extMinX = minX, extMinY = minY, extMaxX = maxX, extMaxY = maxY
        };
        _lightingJobHandle = bwd.Schedule(_forwardJobHandle);

        _externalDistanceFieldReaders = default; // Chamfer 가 prior external readers 를 포섭함
    }

    // ============================================================================================================
    //  JOB COMPLETION
    // ============================================================================================================
    
    /// <summary>
    /// Waits for ALL jobs to complete.
    /// </summary>
    public void CompleteAllJobs()
    {
        // [생성] 카빙은 BasePixels writer 라 가장 먼저 완료해야 한다 (양 경로 공통).
        // 누락 시 청크 파괴(_data.Dispose)가 실행 중인 잡의 NativeArray 를 해제한다.
        _caveHandle.Complete();

        // [half-res] full↔half 브리지 잡도 완료 (양 경로 공통).
        _downsampleHandle.Complete();
        _upsampleHandle.Complete();

        // [LAGDIAG] 임시 계측 — 어느 핸들이 sync point 40ms를 먹는지 핸들별로 분해. 확정 후 제거.
        if (InfinityMapManager.LagDiag)
        {
            var __sw = System.Diagnostics.Stopwatch.StartNew();
            _initJobHandle.Complete();                 double __tInit = __sw.Elapsed.TotalMilliseconds;
            _boundarySyncHandle.Complete();            double __tBnd  = __sw.Elapsed.TotalMilliseconds;
            _forwardJobHandle.Complete();              double __tFwd  = __sw.Elapsed.TotalMilliseconds;
            _lightingJobHandle.Complete();             double __tBwd  = __sw.Elapsed.TotalMilliseconds;
            _visualsJobHandle.Complete();              double __tVis  = __sw.Elapsed.TotalMilliseconds;
            _externalDistanceFieldReaders.Complete();  double __tExt  = __sw.Elapsed.TotalMilliseconds;
            _externalDistanceFieldReaders = default;

            if (__tExt > InfinityMapManager.LagDiagThresholdMs)
            {
                int __dirty = InfinityMapManager.Instance != null ? InfinityMapManager.Instance.DirtyChunkCountDbg : -1;
                Debug.Log($"[LAGDIAG] CompleteAll {__tExt:F1}ms | init={__tInit:F1} bnd={(__tBnd-__tInit):F1} fwd={(__tFwd-__tBnd):F1} bwd={(__tBwd-__tFwd):F1} vis={(__tVis-__tBwd):F1} ext={(__tExt-__tVis):F1} | chamferArea={(_lastExtMaxX-_lastExtMinX)}x{(_lastExtMaxY-_lastExtMinY)} dirtyChunks={__dirty}");
            }
            return;
        }

        _initJobHandle.Complete();
        _boundarySyncHandle.Complete();
        _forwardJobHandle.Complete();
        _lightingJobHandle.Complete();
        _visualsJobHandle.Complete();

        // [P0] 외부 청크의 BoundarySync 가 *우리* distanceField 를 [ReadOnly] 로 읽는 핸들.
        // 우리 5개 writer 핸들과 달리 이건 우리 distanceField 를 deallocate(또는 메인스레드 수정)
        // 하기 전에 반드시 완료해야 한다. 누락 시 청크 파괴(_data.Dispose) 도중
        // "BoundarySyncJob reads from ... topField. You must call JobHandle.Complete()..." 발생.
        _externalDistanceFieldReaders.Complete();
        _externalDistanceFieldReaders = default;
    }

    /// <summary>
    /// BasePixels writer(CaveCarvePixelsJob)만 완료 대기. 메인 스레드에서 BasePixels 를 읽기 전 호출.
    ///
    /// ⚠ IsJobRunning() 폴링으로는 대체할 수 없다. JobHandle.IsCompleted 가 true 여도
    /// NativeArray 의 AtomicSafetyHandle 은 Complete() 를 불러야 풀린다 —
    /// 폴링만 하고 읽으면 "You must call JobHandle.Complete() on the job CaveCarvePixelsJob" 이 뜬다.
    ///
    /// 굴을 안 뚫는 청크(저장 복원 등)는 _caveHandle 이 default 라 이 호출이 no-op 이고,
    /// Phase 1 대기 루프를 이미 지난 경로에서는 잡이 끝나 있으므로 블로킹도 없다.
    /// </summary>
    public void CompleteCarve() => _caveHandle.Complete();

    /// <summary>
    /// Init + 2패스 완료 대기. DistanceField 읽기 전 호출.
    /// </summary>
    public void CompleteLighting()
    {
        // [half-res] full DistanceField는 Upsample이 채우므로 downsample/upsample도 완료해야 읽기 안전.
        _downsampleHandle.Complete();
        _initJobHandle.Complete();
        _boundarySyncHandle.Complete();
        _forwardJobHandle.Complete();
        _lightingJobHandle.Complete();
        _upsampleHandle.Complete();
    }

    public void Dispose()
    {
        try { CompleteAllJobs(); }
        catch (System.Exception e)
        {
            Debug.LogError($"[ChunkJobScheduler] CompleteAllJobs failed during Dispose: {e.Message}");
        }
        if (_emptyLeft.IsCreated)        _emptyLeft.Dispose();
        if (_emptyRight.IsCreated)       _emptyRight.Dispose();
        if (_emptyTop.IsCreated)         _emptyTop.Dispose();
        if (_emptyBottom.IsCreated)      _emptyBottom.Dispose();
        if (_emptyTopLeft.IsCreated)     _emptyTopLeft.Dispose();
        if (_emptyTopRight.IsCreated)    _emptyTopRight.Dispose();
        if (_emptyBottomLeft.IsCreated)  _emptyBottomLeft.Dispose();
        if (_emptyBottomRight.IsCreated) _emptyBottomRight.Dispose();
        // NativeQueue 제거됨: 2패스 Chamfer는 큐 불필요
    }

    /// <summary>
    /// Checks if jobs are currently running.
    /// </summary>
    public bool IsJobRunning()
    {
        // _caveHandle 을 반드시 직접 나열할 것. _downsampleHandle 이 카빙에 의존하므로 전이적으로
        // 커버되는 것처럼 보이지만, ext rect 가 비면 ScheduleDownsample 이 HalfExtRect 에서
        // early-return 해 그 핸들이 갱신되지 않는다 → 카빙 중인 청크를 "한가함"으로 오판한다.
        return !_caveHandle.IsCompleted
            || !_downsampleHandle.IsCompleted || !_initJobHandle.IsCompleted || !_boundarySyncHandle.IsCompleted
            || !_forwardJobHandle.IsCompleted
            || !_lightingJobHandle.IsCompleted || !_upsampleHandle.IsCompleted || !_visualsJobHandle.IsCompleted;
    }

    /// <summary>Init 잡만 완료 상태인지 확인 (블로킹 없음).</summary>
    public bool IsInitCompleted() => _initJobHandle.IsCompleted;

    /// <summary>Visual Job만 완료 여부 확인 (Init/Chamfer는 무시).
    /// outputTexture는 Visual Job만 쓰므로, 이 값이 true면 텍스처 업로드 안전.
    /// </summary>
    public bool IsVisualJobCompleted() => _visualsJobHandle.IsCompleted;
}


