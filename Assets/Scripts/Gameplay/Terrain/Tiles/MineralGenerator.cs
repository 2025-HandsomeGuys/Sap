// @tags: mineral, generation, chunk, terrain, spawn, loot
using UnityEngine;
using System.Collections.Generic;
using System;

public static class MineralGenerator
{
    // [Constants]
    private const int HASH_X = 73856093;
    private const int HASH_Y = 19349663;
    
    // [Constants for Minerals]
    private const byte MINERAL_PIXEL_TYPE = 3;
    private const byte MINERAL_COLLECTED_PIXEL_TYPE = 4; // 수집된 광물 마커 — 재로드 시 재스폰 방지
    private const int MINERAL_VALIDATION_PADDING = 5;

    // 균등 배치용 정적 재사용 버퍼. MineralGenerator는 데코레이션 단계에서 메인 스레드로만
    // 호출되므로 정적 버퍼가 안전하다. 청크마다 new 하면 로드 경로에 GC가 쌓인다.
    // 배경: Assets/Docs/performance/chunk-load-gc.md
    private static int[] s_cellOrder = new int[1024];
    private static int[] s_requested = new int[16];
    private static bool s_limitWarningShown;

    // 셀 안에서 유효 위치를 찾는 시도 횟수. 셀이 작고(수십 px) 갓 생성된 청크는 거의 꽉 차 있으므로,
    // 여기서 다 실패하면 그 셀이 실제로 빈 공간이라는 뜻이다. 더 시도할 이유가 없다.
    private const int MAX_ATTEMPTS_PER_CELL = 6;

    // [최적화] 광물 종류별 오브젝트 풀
    private static Dictionary<string, Queue<GameObject>> _mineralPools = new Dictionary<string, Queue<GameObject>>();

    /// <summary>
    /// 광물을 풀에 반납한다. SpawnMineralObject가 만든 것(poolKey 보유)만 받고,
    /// 다른 시스템이 직접 Instantiate한 광물은 파괴한다 — 호출측이 출처를 구분할 필요 없다.
    /// </summary>
    public static void ReturnToPool(GameObject mineral)
    {
        if (mineral == null) return;

        // 풀 신원의 유일한 근거는 스폰 시 주입한 poolKey다.
        // 예전엔 mineral.name.Split('_')[1]로 파싱했는데, MineralSO 이름에 '_'가 들어가는 순간
        // 넣는 키(mineralSO.name)와 빼는 키가 어긋난다 → 그 큐는 아무도 안 꺼내거나(누수)
        // 같은 앞토막을 가진 다른 광물로 나온다. RockSpawner가 같은 이유로 이미 이름 기반을 버렸다.
        string poolKey = mineral.TryGetComponent<MineralItemController>(out var mic) ? mic.poolKey : null;

        if (string.IsNullOrEmpty(poolKey))
        {
            // MINERAL_ 접두사가 붙었는데 poolKey가 없다 = 스폰 경로가 주입에 실패했다는 뜻.
            // 다른 시스템(IceBreakable·SnowmanEntity·DokkaebiCauldron)이 만든 광물은
            // 접두사가 없으므로 경고 없이 조용히 파괴된다.
            if (mineral.name.StartsWith("MINERAL_"))
                Debug.LogWarning($"[MineralGenerator] poolKey 없는 풀 광물: {mineral.name} — 프리팹의 MineralItemController를 확인하세요.");

            UnityEngine.Object.Destroy(mineral);
            return;
        }

        mineral.SetActive(false);
        mineral.transform.SetParent(null);

        if (!_mineralPools.ContainsKey(poolKey))
        {
            _mineralPools[poolKey] = new Queue<GameObject>();
        }
        _mineralPools[poolKey].Enqueue(mineral);
    }

    public static void GenerateMinerals(TerrainChunk chunk, List<MineralRuleJson> mineralRules, Vector2Int coord, int worldSeed, TileType tileType, List<Rect> excludedAreas, MineralSpawnSettings settings)
    {
        if (mineralRules == null || mineralRules.Count == 0 || chunk == null) return;
        // Phase 2(장식) 시점에 InitDistanceFieldJob이 pixelInfo를 읽는 중일 수 있으므로,
        // PixelInfo 쓰기 전 반드시 모든 잡을 완료해야 한다.
        chunk.EnsureJobsCompleted();

        System.Random prng = GetDeterministicRandom(coord, worldSeed);
        int currentDepth = Mathf.Abs(coord.y);

        // ── 1단계: 요청 개수 집계 ──────────────────────────────────────────
        // 격자 크기를 정하려면 청크 전체 개수를 먼저 알아야 한다.
        // rule마다 따로 배치하면 서로를 몰라서 겹치고 뭉친다(설계 §1-2).
        EnsureCapacity(ref s_requested, mineralRules.Count);

        int total = 0;
        for (int r = 0; r < mineralRules.Count; r++)
        {
            // [중요] 난수 2개는 rule마다 무조건 소비한다.
            // 깊이 범위를 벗어난 rule에서 건너뛰면 난수 스트림이 깊이별로 어긋나
            // 같은 시드에서도 배치가 요동친다.
            double n = MineralDensity.ExpectedCount(mineralRules[r], currentDepth,
                                                    settings.DensityMultiplier, prng.NextDouble());
            int requested = MineralDensity.ProbabilisticRound(n, prng.NextDouble());
            s_requested[r] = requested;
            total += requested;
        }

        if (total <= 0) return;

        // ── 2단계: 격자 생성 + 셀 순서 셔플 ────────────────────────────────
        int lo = MINERAL_VALIDATION_PADDING + MINERAL_SUPPORT_MARGIN_MAX;
        int regionW = chunk.width - lo * 2;
        int regionH = chunk.height - lo * 2;
        if (regionW <= 0 || regionH <= 0) return;

        total = ClampToPhysicalLimit(total, regionW, regionH, coord);

        ScatterGrid grid = ScatterGrid.Create(total, lo, lo, regionW, regionH);
        int cellCount = grid.CellCount;
        if (cellCount <= 0) return;

        EnsureCapacity(ref s_cellOrder, cellCount);
        for (int i = 0; i < cellCount; i++) s_cellOrder[i] = i;
        MineralScatter.ShuffleInPlace(s_cellOrder, cellCount, prng);

        // ── 3단계: 배치 ───────────────────────────────────────────────────
        // 셀 순서가 섞여 있으므로 각 rule은 자동으로 청크 전역에 흩어진다.
        // 셔플 한 번으로 "종류별 균등"과 "전체 균등"이 동시에 성립한다.
        //
        // 청크 로드는 GC에 민감한 경로다. 로그가 꺼져 있으면 StringBuilder도 문자열도 만들지 않는다.
        System.Text.StringBuilder log = settings.LogSummary ? new System.Text.StringBuilder() : null;
        int totalRequested = 0;
        int totalPlaced = 0;
        int pointIndex = 0;

        for (int r = 0; r < mineralRules.Count; r++)
        {
            int requested = s_requested[r];
            var rule = mineralRules[r];

            // 요청 0도 로그에는 남긴다 — 깊이 필터로 잘린 건지 밀도가 0인 건지 구분이 안 되면
            // 튜닝할 때 어느 값을 건드려야 하는지 알 수 없다.
            if (requested <= 0)
            {
                if (log != null) log.Append("\n  ").Append(rule.mineralType).Append(" 0/0");
                continue;
            }

            int placed = 0;

            try
            {
                if (TryGetMineralData(rule.mineralType, out MineralSO mineralSO))
                {
                    // 지지 마진은 광물마다 다르다 — 프리팹의 supportCheckRadius가 기준이다.
                    int supportMargin = GetSupportMarginPx(mineralSO, chunk);

                    for (int i = 0; i < requested && pointIndex < cellCount; i++)
                    {
                        int cell = s_cellOrder[pointIndex++];
                        if (TryFindPositionInCell(chunk, grid, cell, prng, settings.ScatterJitter,
                                                  excludedAreas, supportMargin, out int x, out int y))
                        {
                            SpawnAndCarveMineral(chunk, x, y, mineralSO);
                            placed++;
                        }
                        // 실패한 셀은 그냥 건너뛴다. 다른 곳에 다시 시도하면
                        // 남은 흙에 몰아넣게 되고, 그게 뭉침의 원인이다(설계 §4-2).
                    }
                }
            }
            catch (System.Exception e)
            {
                // rule 하나가 터져도 나머지 광물은 정상 생성돼야 한다
                Debug.LogError($"[MineralGenerator] Error generating mineral '{rule?.mineralType}': {e.Message}\n{e.StackTrace}");
            }

            if (log != null)
            {
                totalRequested += requested;
                totalPlaced += placed;
                log.Append("\n  ").Append(rule.mineralType).Append(' ')
                   .Append(placed).Append('/').Append(requested);
            }
        }

        if (log != null)
        {
            // 요청 대비 배치 비율이 낮으면 지형이 성겨서 IsWellSupported가 떨어뜨리고 있다는 뜻이다.
            // 배경: Assets/Docs/mineral-density-redesign.md §6-2
            int percent = (totalRequested > 0) ? Mathf.RoundToInt(100f * totalPlaced / totalRequested) : 0;
            Debug.Log($"[Minerals] chunk({coord.x},{coord.y}) {tileType} depth={currentDepth}" +
                      $" density={settings.DensityMultiplier:0.##} jitter={settings.ScatterJitter:0.##}" +
                      $" grid={grid.Cols}x{grid.Rows}" +
                      $" | 배치 {totalPlaced} / 요청 {totalRequested} ({percent}%){log}");
        }
    }

    /// <summary>
    /// 최소 간격(지지 마진 ×2) 기준으로 영역에 물리적으로 들어갈 수 있는 최대 개수로 자른다.
    /// 이보다 많이 요청되면 어차피 배치되지 않고 s_cellOrder만 무한정 커진다.
    /// 정상 경로가 아니라 잘못된 설정에 대한 방어이므로 경고는 세션당 1회만 낸다.
    /// </summary>
    private static int ClampToPhysicalLimit(int total, int regionW, int regionH, Vector2Int coord)
    {
        int spacing = MINERAL_SUPPORT_MARGIN_MAX * 2;
        int limit = Mathf.Max(1, (regionW / spacing) * (regionH / spacing));
        if (total <= limit) return total;

        if (!s_limitWarningShown)
        {
            s_limitWarningShown = true;
            Debug.LogWarning($"[MineralGenerator] chunk({coord.x},{coord.y}) 광물 요청 {total}개가 " +
                             $"물리적 한계 {limit}개를 넘어 잘렸습니다. " +
                             $"perChunk / mineralDensity / globalMineralDensity 값을 확인하세요. " +
                             $"(이 경고는 세션당 1회만 표시)");
        }
        return limit;
    }

    /// <summary>정적 버퍼를 필요한 크기까지만 키운다. 줄이지는 않는다.</summary>
    private static void EnsureCapacity(ref int[] buffer, int needed)
    {
        if (buffer == null || buffer.Length < needed)
            buffer = new int[Mathf.NextPowerOfTwo(needed)];
    }

    private static System.Random GetDeterministicRandom(Vector2Int coord, int worldSeed)
    {
        int seed = (coord.x * HASH_X) ^ (coord.y * HASH_Y) ^ worldSeed;
        return new System.Random(seed);
    }

    private static bool TryGetMineralData(string typeName, out MineralSO result)
    {
        result = null;
        if (string.IsNullOrEmpty(typeName)) return false;
        if (MineralDatabase.Instance == null) return false;

        if (Enum.TryParse(typeName, true, out MineralID mId) && mId != MineralID.None)
        {
            result = MineralDatabase.Instance.GetMineralByID(mId);
        }
        
        return result != null && result.mineralPrefab != null;
    }

    // 광물이 배치 직후 지지 검사(MineralItemController.CheckSupport)에서 탈락해
    // 스스로 월드로 튀어나오는 것을 막기 위한 이웃 솔리드 반경(px).
    //
    // [주의] 이 값은 반드시 CheckSupport가 실제로 샘플하는 반경 이상이어야 한다.
    // CheckSupport는 프리팹의 supportCheckRadius(월드 단위) × 3x3 이웃 9점을 보는데,
    // 현재 광물 프리팹은 전부 0.1 = 10px다. 예전 상수 5px는 코드 주석의 "≈4px"를 믿고
    // 정한 값이라 실제보다 작았고, 그래서 방 벽에서 5~9px 떨어진 광물이 배치 검증은
    // 통과하고도 로드 직후 지지검사에서 탈락해 스스로 튀어나왔다
    // (PNG 페인팅 방처럼 공동 경계가 긴 청크에서 특히 눈에 띈다).
    // 이제 프리팹 값에서 직접 뽑는다 — GetSupportMarginPx 참고.
    private const int MINERAL_SUPPORT_MARGIN_MIN = 5;
    private const int MINERAL_SUPPORT_MARGIN_MAX = 16;

    // MineralSO → 지지 마진(px) 캐시. 프리팹 GetComponent를 광물 1개마다 돌지 않기 위함.
    // 값은 프리팹 에셋에서 오므로 런타임 중 변하지 않는다(에디터 재생 사이엔 도메인 리로드로 비워짐).
    private static readonly Dictionary<MineralSO, int> s_supportMarginCache = new Dictionary<MineralSO, int>();
    private static bool s_marginClampWarningShown;

    /// <summary>
    /// 이 광물의 배치 지지 마진(px). 프리팹 <see cref="MineralItemController.supportCheckRadius"/>를
    /// 픽셀로 환산한 값 = CheckSupport가 실제로 샘플하는 반경.
    /// 프리팹에 컨트롤러가 없으면(순수 드롭 아이템 등) 최소값을 쓴다.
    /// </summary>
    private static int GetSupportMarginPx(MineralSO mineralSO, TerrainChunk chunk)
    {
        if (mineralSO == null) return MINERAL_SUPPORT_MARGIN_MIN;
        if (s_supportMarginCache.TryGetValue(mineralSO, out int cached)) return cached;

        int margin = MINERAL_SUPPORT_MARGIN_MIN;
        if (mineralSO.mineralPrefab != null &&
            mineralSO.mineralPrefab.TryGetComponent<MineralItemController>(out var controller))
        {
            // pixelsPerUnit은 청크 공통 값이지만, 캐시가 첫 호출 청크 기준으로 굳는 것을
            // 막기 위해 여기서 바로 환산해 넣는다(현재 모든 청크가 같은 PPU다).
            float ppu = chunk != null ? chunk.pixelsPerUnit : 100f;
            int raw = Mathf.CeilToInt(controller.supportCheckRadius * ppu);
            margin = Mathf.Clamp(raw, MINERAL_SUPPORT_MARGIN_MIN, MINERAL_SUPPORT_MARGIN_MAX);

            if (raw > MINERAL_SUPPORT_MARGIN_MAX && !s_marginClampWarningShown)
            {
                s_marginClampWarningShown = true;
                Debug.LogWarning($"[MineralGenerator] '{mineralSO.name}' 프리팹의 supportCheckRadius가 " +
                                 $"{controller.supportCheckRadius}({raw}px)로 너무 큽니다. 배치 마진을 " +
                                 $"{MINERAL_SUPPORT_MARGIN_MAX}px로 자르므로 이 광물은 벽 근처에서 " +
                                 $"스스로 떨어져 나올 수 있습니다. (세션당 1회 경고)");
            }
        }

        s_supportMarginCache[mineralSO] = margin;
        return margin;
    }

    /// <summary>
    /// 한 셀 안에서 유효한 스폰 좌표를 찾는다. 청크 전역이 아니라 셀 안만 뒤지는 것이
    /// 균등 배치의 핵심이다 — 실패해도 다른 곳으로 새지 않는다.
    /// 배경: Assets/Docs/mineral-even-scatter.md §3-3
    /// </summary>
    private static bool TryFindPositionInCell(TerrainChunk chunk, ScatterGrid grid, int cellIndex,
                                              System.Random prng, float jitter,
                                              List<Rect> excludedAreas, int supportMargin,
                                              out int x, out int y)
    {
        x = 0;
        y = 0;

        for (int attempt = 0; attempt < MAX_ATTEMPTS_PER_CELL; attempt++)
        {
            // 첫 시도만 설정된 지터를 쓴다. jitter=0이면 재시도가 전부 같은 중심점이 되어
            // 무의미하므로, 재시도부터는 셀 전체(1.0)로 넓힌다.
            // 의도한 미감은 성공하는 경우에 유지되고, 실패할 때만 완화된다.
            float j = (attempt == 0) ? jitter : 1f;
            grid.PointAt(cellIndex, prng.NextDouble(), prng.NextDouble(), j, out x, out y);

            // 중심 + 이웃(상하좌우·대각)이 모두 솔리드여야 지지 검사를 통과해 얌전히 박혀 있는다.
            if (!IsWellSupported(chunk, x, y, supportMargin)) continue;

            // [엘리베이터 제외] 엘리베이터가 차지하는 픽셀 영역에는 광물 스폰 안 함
            if (excludedAreas != null && IsInExcludedArea(x, y, excludedAreas)) continue;

            // [굴 제외] 아직 안 뚫린 굴 자리는 피한다. 여기 놓으면 굴이 열리는 순간
            // 지지대를 잃고 우수수 떨어진다. 지지 검사와 같은 margin 을 써서 벽면에 딱 붙은
            // 자리도 함께 거른다.
            if (chunk.IsFutureCaveNear(x, y, supportMargin)) continue;

            return true;
        }

        return false;
    }

    // CheckSupport와 '동일한' 3x3 이웃(반경 = 프리팹 supportCheckRadius의 픽셀 환산값)이
    // 전부 솔리드인지 확인. CheckSupport는 threshold(0.5~0.8)만큼 빈 칸을 허용하지만
    // 여기선 9점 전부를 요구한다 — 배치 검증이 더 엄격해야 경계에서 안 튀어나온다.
    private static bool IsWellSupported(TerrainChunk chunk, int x, int y, int m)
    {
        return IsGroundPixel(chunk, x, y)
            && IsGroundPixel(chunk, x + m, y) && IsGroundPixel(chunk, x - m, y)
            && IsGroundPixel(chunk, x, y + m) && IsGroundPixel(chunk, x, y - m)
            && IsGroundPixel(chunk, x + m, y + m) && IsGroundPixel(chunk, x - m, y + m)
            && IsGroundPixel(chunk, x + m, y - m) && IsGroundPixel(chunk, x - m, y - m);
    }

    // excludedAreas는 픽셀 좌표 기준 Rect (ElevatorSpawner 반환값과 동일 좌표계)
    private static bool IsInExcludedArea(int pixelX, int pixelY, List<Rect> excludedAreas)
    {
        Vector2 pixelPos = new Vector2(pixelX, pixelY);
        foreach (var area in excludedAreas)
        {
            if (area.Contains(pixelPos)) return true;
        }
        return false;
    }

    private static bool IsGroundPixel(TerrainChunk chunk, int x, int y)
    {
        var chunkData = chunk.GetData();
        if (chunkData == null || !chunkData.BasePixels.IsCreated) return false;

        int idx = y * chunk.width + x;
        if (idx < 0 || idx >= chunkData.BasePixels.Length) return false;

        if (chunkData.BasePixels[idx].a == 0) return false;

        // carve 예약 픽셀(특수청크 공동 등)은 지금 solid여도 곧 air가 된다 → 스폰 금지.
        // 여기 박으면 노출 시 광물만 공동 한가운데 남는다.
        if (chunk.IsCarveReserved(x, y)) return false;

        // 이미 수집된 광물 위치는 재스폰 금지
        if (chunkData.PixelInfo.IsCreated && chunkData.PixelInfo[idx] == MINERAL_COLLECTED_PIXEL_TYPE)
            return false;

        // 파기 불가 픽셀엔 스폰 금지 — 특수청크의 인디스트럭터블 테두리 등에 박히면 영영 못 캔다.
        if (chunkData.HasIndestructiblePixels && chunkData.IndestructibleMask.IsCreated
            && chunkData.IndestructibleMask[idx] != 0)
            return false;

        return true;
    }

    /// <summary>
    /// 광물이 수집되었을 때 해당 픽셀을 MINERAL_COLLECTED_PIXEL_TYPE(4)로 마킹.
    /// 청크 재로드 시 IsGroundPixel이 이 위치를 건너뛰어 재스폰을 방지한다.
    /// 내장 지형 광물(TerrainChunk 자식)에만 호출할 것.
    /// </summary>
    public static void MarkMineralCollected(TerrainChunk chunk, int x, int y)
    {
        if (chunk == null) return;
        chunk.EnsureJobsCompleted();

        var data = chunk.GetData();
        if (data == null || !data.PixelInfo.IsCreated) return;

        int idx = y * chunk.width + x;
        if (idx < 0 || idx >= data.PixelInfo.Length) return;

        data.PixelInfo[idx] = MINERAL_COLLECTED_PIXEL_TYPE;
        data.HasBeenModified = true; // 저장 대상으로 등록
    }

    private static void SpawnAndCarveMineral(TerrainChunk chunk, int x, int y, MineralSO mineralSO)
    {
        SpawnMineralObject(chunk, x, y, mineralSO);
        MarkPixelAsMineral(chunk, x, y);
    }

    private static void MarkPixelAsMineral(TerrainChunk chunk, int x, int y)
    {
        // Jobs must already be completed by the GenerateMinerals call-site (EnsureJobsCompleted).
        var chunkData = chunk.GetData();
        if (chunkData == null || !chunkData.PixelInfo.IsCreated) return;

        int idx = y * chunk.width + x;
        if (idx >= 0 && idx < chunkData.PixelInfo.Length)
        {
            chunkData.PixelInfo[idx] = MINERAL_PIXEL_TYPE;
        }
    }

    // [최적화] 프리팹 풀링 적용
    private static void SpawnMineralObject(TerrainChunk chunk, int x, int y, MineralSO mineralSO)
    {
        Vector2 localPos = chunk.GetLocalPositionForPixel(x, y);
        localPos.x += 0.5f / chunk.pixelsPerUnit;
        localPos.y += 0.5f / chunk.pixelsPerUnit;
        
        // 🚨 에러 수정 부분: mineralName(LocalizedString) 대신 SO 파일의 고유 이름(string)을 사용합니다.
        string poolKey = mineralSO.name; 
        GameObject minObj = null;

        // 해당 종류(Gold, Iron 등)의 풀에 보관된 광물이 있다면 꺼내옵니다.
        if (_mineralPools.TryGetValue(poolKey, out Queue<GameObject> pool))
        {
            while (pool.Count > 0)
            {
                GameObject pooledObj = pool.Dequeue();
                if (pooledObj != null)
                {
                    minObj = pooledObj;
                    break;
                }
            }
        }

        // 풀에 없으면 새로 생성, 있으면 재활용
        bool fromPool = minObj != null;
        if (fromPool)
            minObj.transform.SetParent(chunk.transform, false);
        else
            minObj = UnityEngine.Object.Instantiate(mineralSO.mineralPrefab, chunk.transform);

        // [순서 중요] 위치·회전을 활성화(SetActive) '전에' 확정한다.
        // 풀 재사용 광물은 SetActive(true) 순간 OnEnable → CheckStructuralIntegrityRoutine
        // 첫 반복이 동기 실행되는데, 이때 위치가 이전(풀) 좌표면 지지검사 실패 → WakeUp() →
        // SetParent(null)로 분리되고, 이후 localPosition 세팅이 부모 없는 상태로 적용돼
        // 월드 원점(0,0) 근처로 광물이 튄다. 위치를 먼저 세팅해야 지지검사가 올바른 지점을 본다.
        minObj.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
        minObj.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));

        // 이름 규격 강제 — 반납 키가 아니라 '자식 스캔' 용도다.
        // ChunkSpawner의 장식 정리 루프와 InfinityMapManager.DetachMineralsToWorld가
        // 청크 자식 중 광물을 이 접두사로 골라낸다.
        minObj.name = $"MINERAL_{poolKey}_{x}_{y}";

        // 풀 신원 주입 — ReturnToPool이 이 값으로 큐를 찾는다(이름 파싱 아님).
        if (minObj.TryGetComponent<MineralItemController>(out var mic))
            mic.poolKey = poolKey;

        // 위치 확정 후 활성화 (신규 Instantiate 분은 이미 활성 상태)
        if (fromPool)
            minObj.SetActive(true);
    }
}