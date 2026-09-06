// @tags: special-chunk, spawn, manager, chunk, generation, pipeline
using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// CSV ChunkPlan 기반 특수 청크 기믹 타입
/// </summary>
public enum SpecialChunkType
{
    None,
    CompressedTrashWall, // 압축 쓰레기 벽 — 파기 시 쓰레기 산란 + 20% 희귀광물
    ScrapExplosion,   // 압축 쓰레기 벽 — 파괴 시 고철 파편 파티클
    DelayedBlast,     // 메탄가스 광석 — 채굴 후 2초 뒤 폭발
    CollapseFloor,    // 싱크홀 — 접촉 0.2초 뒤 바닥 소멸
    GuideLine,        // 구리 케이블 — 파괴 시 전류 방향 스파크
    Pitfall,          // 매몰 갱도 — 바닥 나무 밟으면 낙하
    DropSpike,        // 산화된 공동 — 천장 종유석 낙하
    Mine,             // 광산 공동 — 파기 가능 지형 + 파기 불가 구조물 (패턴 C)
    AntiGravity,      // 반중력 공동 — 진입 시 중력 반전
    MagmaJump,        // 용암 점프맵 — 벽타기 불가, 용암 바닥(화상 피해), 가라앉는 기둥 기믹
    DungeonDoor,      // 던전 입구 — F키 상호작용 시 던전 씬으로 진입
}

/// <summary>
/// 특수 청크 스폰을 조율하는 퍼사드.
/// 선택 로직은 SpecialChunkSelector, 레지스트리는 SubChunkRegistry에 위임한다.
/// SRP: 조율(오케스트레이션)만 담당.
/// </summary>
[DefaultExecutionOrder(-50)]
public class SpecialChunkManager : MonoBehaviour
{
    public static SpecialChunkManager Instance { get; private set; }

    #region Nested Data Structs — Unity 직렬화 호환을 위해 이동하지 않음

    [System.Serializable]
    public struct LinkedPiece
    {
        [Tooltip("앵커 기준 상대 좌표. 예: (1,0) = 오른쪽 1칸, (0,-1) = 아래 1칸")]
        public Vector2Int offset;
        [Tooltip("해당 위치에 스폰할 TerrainChunk 프리팹. null이면 기본 청크 프리팹 사용.")]
        public MonoBehaviour prefab;
    }

    [System.Serializable]
    public struct SpecialChunkDef
    {
        [Tooltip("The root prefab. Usually has LargeStaticTerrainChunk attached.")]
        public MonoBehaviour prefab;

        [Tooltip("Probability of appearance (0.0 ~ 100.0%)")]
        [Range(0f, 100f)]
        public float spawnChance;

        [Tooltip("이 청크의 특수 기믹 종류 (기믹 스크립트 연동용)")]
        public SpecialChunkType chunkType;

        [Tooltip("생성 가능한 최소 지층 깊이 (Y 좌표, 0 = 제한 없음)")]
        public int minDepth;

        [Tooltip("생성 가능한 최대 지층 깊이 (Y 좌표, 0 = 제한 없음)")]
        public int maxDepth;

        [Tooltip("점유 청크 가로 수 (기본값 1)")]
        public int chunkSizeX;

        [Tooltip("점유 청크 세로 수 (기본값 1)")]
        public int chunkSizeY;

        [Tooltip("연결된 피스 목록. 비어있으면 단일 청크 앵커로 동작.")]
        public LinkedPiece[] linkedPieces;

        public int SizeX => Mathf.Max(1, chunkSizeX);
        public int SizeY => Mathf.Max(1, chunkSizeY);
    }

    [System.Serializable]
    public struct SpecialChunkPool
    {
        public TileType targetLayer;
        public List<SpecialChunkDef> chunks;
    }

    #endregion

    #region Inspector Fields

    [Header("Configuration")]
    [SerializeField] private List<SpecialChunkPool> pools;

    [Header("Spacing")]
    [SerializeField] private int minChunkSpacing = 2;
    [SerializeField] private int layerBoundarySpacing = 1;
    [SerializeField] private int[] layerBoundaryYCoords = { -9, -18, -27, -36 };

    #endregion

    #region Private State

    private SpecialChunkSelector _selector;
    private SubChunkRegistry _registry;
    private LinkedChunkRegistry _linkedRegistry;

    // 특수청크 스폰을 차단할 보호 좌표 집합 (ImageChunkOverrider 등록용)
    private readonly HashSet<Vector2Int> _protectedCoords = new HashSet<Vector2Int>();

    // 보호 좌표 → 로드 후 처리 훅 (PNG 페인팅·광물 재생성).
    // ChunkGenerationPipeline이 Phase 2 데코레이터 루프 직후에 조회·호출한다.
    private readonly Dictionary<Vector2Int, IChunkPostLoadPainter> _postLoadPainters
        = new Dictionary<Vector2Int, IChunkPostLoadPainter>();

    // 실제로 스폰된 특수청크 앵커 좌표 → 기믹 종류.
    // 나침반 등 "실제 배치된 특수청크"를 조회하는 소비자용. 예측(TrySelect)이 아닌 실 인스턴스만 담는다.
    private readonly Dictionary<Vector2Int, SpecialChunkType> _activeAnchors
        = new Dictionary<Vector2Int, SpecialChunkType>();

    // 앵커 좌표 → 프리팹에 배치된 CompassPoi 마커 Transform.
    // 나침반이 청크 중심 대신 이 지점을 가리킨다. 마커가 없는 앵커는 미포함.
    private readonly Dictionary<Vector2Int, Transform> _anchorPois
        = new Dictionary<Vector2Int, Transform>();

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // JSON 설정 적용 (SpecialChunkSettingsLoader 는 Execution Order -150 으로 먼저 Awake)
        if (SpecialChunkSettingsLoader.Instance != null)
            ApplySpecialChunkSettings(SpecialChunkSettingsLoader.Instance.Settings);

        _selector = new SpecialChunkSelector(pools, minChunkSpacing, layerBoundarySpacing, layerBoundaryYCoords);
        _registry = new SubChunkRegistry();
        _linkedRegistry = new LinkedChunkRegistry();
    }

    private void ApplySpecialChunkSettings(SpecialChunkSettingsData s)
    {
        minChunkSpacing      = s.spawning.minChunkSpacing;
        layerBoundarySpacing = s.spawning.layerBoundarySpacing;

        // spawnChance override: SpecialChunkType 이름으로 매칭
        var chanceMap = s.spawning.GetSpawnChanceDict();
        if (pools == null) return;
        for (int pi = 0; pi < pools.Count; pi++)
        {
            var pool = pools[pi];
            if (pool.chunks == null) continue;
            for (int ci = 0; ci < pool.chunks.Count; ci++)
            {
                var def = pool.chunks[ci];
                if (chanceMap.TryGetValue(def.chunkType.ToString(), out float chance))
                {
                    def.spawnChance = chance;
                    pool.chunks[ci] = def; // struct 이므로 재할당 필요
                }
            }
        }
        //Debug.Log($"[SpecialChunkManager] specialChunkSettings.json 적용 완료");
    }


    #endregion

    #region Public API — Registry 조회

    /// <summary>
    /// 해당 좌표가 미리 등록된 서브 청크 인스턴스인지 확인한다.
    /// 파괴된 인스턴스는 lazy하게 제거된다.
    /// </summary>
    public bool TryGetRegisteredSubChunk(Vector2Int coord, out IChunk chunk)
    {
        bool found = _registry.TryGet(coord, out TerrainChunk tc);
        chunk = tc;
        return found;
    }

    /// <summary>
    /// 서브청크 좌표에 대응하는 앵커 좌표를 반환한다.
    /// </summary>
    public bool TryGetAnchorCoord(Vector2Int subCoord, out Vector2Int anchorCoord)
        => _registry.TryGetAnchor(subCoord, out anchorCoord);

    /// <summary>
    /// 앵커 청크 언로드 시 호출. 해당 앵커의 서브슬롯 예약을 SubChunkRegistry에서 제거한다.
    /// 미호출 시 재방문 시 스폰 실패(stale exclusion zone 버그) 발생.
    /// </summary>
    public void UnregisterSubChunksForAnchor(Vector2Int anchorCoord)
    {
        _registry.UnregisterByAnchor(anchorCoord);
        _linkedRegistry.UnregisterByAnchor(anchorCoord);
        _activeAnchors.Remove(anchorCoord);
        _anchorPois.Remove(anchorCoord);
    }

    /// <summary>
    /// 현재 로드되어 실제로 배치된 특수청크 앵커들 (좌표 → 기믹 종류).
    /// 예측(TrySelect)이 아닌 실 인스턴스만 포함한다. 나침반·미니맵 등이 조회한다.
    /// </summary>
    public IReadOnlyDictionary<Vector2Int, SpecialChunkType> ActiveAnchors => _activeAnchors;

    /// <summary>
    /// 앵커 프리팹에 배치된 CompassPoi 마커의 현재 월드 위치를 반환한다.
    /// 마커가 없으면 false (호출측은 청크 중심으로 폴백). 나침반이 조회한다.
    /// </summary>
    public bool TryGetAnchorPoi(Vector2Int anchorCoord, out Vector2 worldPos)
    {
        if (_anchorPois.TryGetValue(anchorCoord, out Transform t) && t != null)
        {
            worldPos = t.position;
            return true;
        }
        worldPos = default;
        return false;
    }

    /// <summary>
    /// 앵커의 실제 멀티청크 발자국(청크 단위)을 반환한다.
    /// LargeStaticTerrainChunk.Width 대신 SubChunkRegistry 기반으로 계산하므로
    /// TerrainChunk 앵커에서도 올바른 footprint를 반환한다.
    /// </summary>
    public Vector2Int GetAnchorFootprint(Vector2Int anchorCoord)
        => _registry.GetFootprint(anchorCoord);

    /// <summary>캐시에서 링크 피스 프리팹을 반환한다. 캐시 미스 시 false.</summary>
    public bool TryGetLinkedPiece(Vector2Int coord, out MonoBehaviour prefab, out Vector2Int anchor)
        => _linkedRegistry.TryGet(coord, out prefab, out anchor);

    /// <summary>
    /// 캐시 우선 조회 → 캐시 미스 시 결정론적 역산.
    /// ChunkDataProvider가 링크 피스 스폰 경로에서 호출한다.
    /// </summary>
    public bool TryGetLinkedPiecePrefab(
        Vector2Int coord, TileType layerType, int worldSeed, out MonoBehaviour prefab)
    {
        if (_linkedRegistry.TryGet(coord, out prefab, out _)) return true;
        return _selector.TryGetLinkedAnchorByPool(coord, layerType, worldSeed, _registry, out _, out prefab);
    }

    #endregion

    #region Public API — 보호 좌표

    /// <summary>
    /// 해당 좌표에 특수청크 스폰을 차단한다. (예: ImageChunkOverrider 사용 위치)
    /// </summary>
    public void RegisterProtectedCoord(Vector2Int coord) => _protectedCoords.Add(coord);

    /// <summary>
    /// 보호 좌표 등록을 해제한다.
    /// </summary>
    public void UnregisterProtectedCoord(Vector2Int coord) => _protectedCoords.Remove(coord);

    /// <summary>
    /// 보호 좌표(ImageChunkOverrider 등 작가가 픽셀을 완전히 덮어쓰는 청크)인지 조회한다.
    /// true면 절차 데코레이션(엘리베이터·바위·광물)을 건너뛴다.
    /// </summary>
    public bool IsProtectedCoord(Vector2Int coord) => _protectedCoords.Contains(coord);

    /// <summary>
    /// 보호 좌표의 "로드 후 처리"를 등록한다. 청크가 로드·재로드될 때마다
    /// ChunkGenerationPipeline이 Phase 2 데코레이션 직후에 호출한다.
    /// 좌표당 1개 — 같은 좌표에 다시 등록하면 덮어쓴다.
    /// </summary>
    public void RegisterPostLoadPainter(Vector2Int coord, IChunkPostLoadPainter painter)
    {
        if (painter == null) return;
        _postLoadPainters[coord] = painter;
    }

    /// <summary>등록된 로드 후 처리를 해제한다. 다른 컴포넌트가 등록한 것은 건드리지 않는다.</summary>
    public void UnregisterPostLoadPainter(Vector2Int coord, IChunkPostLoadPainter painter)
    {
        if (_postLoadPainters.TryGetValue(coord, out var current) && current == painter)
            _postLoadPainters.Remove(coord);
    }

    /// <summary>해당 좌표에 등록된 로드 후 처리를 조회한다.</summary>
    public bool TryGetPostLoadPainter(Vector2Int coord, out IChunkPostLoadPainter painter)
        => _postLoadPainters.TryGetValue(coord, out painter);

    #endregion

    #region Public API — 선택

    /// <summary>
    /// 특수 청크 프리팹을 결정론적으로 선택한다. 없으면 null.
    /// </summary>
    public MonoBehaviour GetSpecialChunk(Vector2Int coord, TileType layerType, int worldSeed)
        => _selector.TrySelect(coord, layerType, worldSeed, _registry)?.prefab;

    /// <summary>
    /// origin 기준 체비쇼프 반경 radius 내의 특수청크 앵커를 결정론적으로 예측한다.
    /// 로드 여부와 무관 — 청크 생성 파이프라인이 스폰 판정에 쓰는 _selector.TrySelect를 그대로 호출한다.
    /// 나침반/탐지 유물이 로딩 범위 밖을 조회할 때 사용.
    /// </summary>
    public void PredictAnchorsInRadius(
        Vector2Int origin, int radius,
        List<(Vector2Int coord, SpecialChunkType type)> results)
    {
        if (results == null) return;
        results.Clear();
        if (_selector == null) return;

        int seed = InfinityMapManager.Instance != null ? InfinityMapManager.Instance.worldSeed : 0;
        var tdm = TileDataManager.Instance;
        if (tdm == null) return;

        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            var coord = new Vector2Int(origin.x + dx, origin.y + dy);
            TileType layer = tdm.GetTileTypeAtPosition(coord.x, coord.y);
            var def = _selector.TrySelect(coord, layer, seed, _registry);
            if (def != null)
                results.Add((coord, def.Value.chunkType));
        }
    }

    /// <summary>
    /// 주어진 좌표가 어떤 대형 앵커 청크의 예약 서브 위치인지 판별한다.
    /// 앵커가 아직 스폰되지 않았어도 동작한다.
    /// </summary>
    public bool IsSubChunkCoord(Vector2Int coord, TileType layerType, int worldSeed)
    {
        if (_registry.Contains(coord)) return true;
        if (_linkedRegistry.Contains(coord)) return true;
        if (_selector.IsSubChunkByPool(coord, layerType, worldSeed, _registry)) return true;
        return _selector.IsLinkedPieceByPool(coord, layerType, worldSeed, _registry);
    }

    /// <summary>
    /// 서브청크 좌표에 해당하는 앵커 좌표를 역산한다.
    /// 앵커가 아직 스폰되지 않은 경우에도 동작한다.
    /// </summary>
    public bool TryGetSubChunkAnchorCoord(
        Vector2Int subCoord, TileType layerType, int worldSeed, out Vector2Int anchorCoord)
    {
        if (_registry.TryGetAnchor(subCoord, out anchorCoord)) return true;
        return _selector.TryGetAnchorByPool(subCoord, layerType, worldSeed, out anchorCoord);
    }

    #endregion

    #region Public API — 스폰

    /// <summary>
    /// 특수 청크를 확률적으로 선택하고, 당첨되면 대형 앵커 청크를 즉시 생성(Instantiate)하여 반환합니다.
    /// 앵커 prefab의 width/height가 청크 크기(1000px)를 초과하면 해당 범위의 서브 위치를
    /// SubChunkRegistry에 예약 등록합니다 (TerrainChunk 인스턴스는 생성하지 않음).
    /// 꽝이면 null을 반환합니다.
    /// </summary>
    public IChunk SpawnSpecialChunkIfPossible(
        Vector2Int coord, TileType layerType, int worldSeed,
        Transform parent,
        out List<(Vector2Int subCoord, IChunk subChunk)> outSubChunks)
    {
        outSubChunks = null;

        // 보호 좌표 차단 (앵커)
        if (_protectedCoords.Contains(coord))
            return null;

        // 1. 앵커 프리팹 선택
        //Debug.Log($"[SpecialChunkManager] SpawnSpecialChunkIfPossible 호출: coord={coord}, layer={layerType}");
        SpecialChunkDef? selectedDef = _selector.TrySelect(coord, layerType, worldSeed, _registry);
        if (selectedDef == null) return null;
        //Debug.Log($"[SpecialChunkManager] {coord} → 선택됨: {selectedDef.Value.prefab.name}");

        var def = selectedDef.Value;

        // 보호 좌표 차단 (링크 피스)
        if (def.linkedPieces != null)
        {
            foreach (var piece in def.linkedPieces)
            {
                if (_protectedCoords.Contains(coord + piece.offset))
                    return null;
            }
        }

        Debug.Log($"[LinkedChunk] 앵커 스폰: coord={coord} prefab={def.prefab.name} layer={layerType}");

        // 2. 앵커 스폰 (prefab 크기 그대로 사용 — 대형 청크면 여러 청크 공간을 차지)
        Vector3 anchorPos = ChunkCoords.ToWorld(coord);
        //Debug.Log($"[SpecialChunkManager] CHUNKSIZE Spawn | coord={coord} | prefab={def.prefab.name} | anchorPos={anchorPos}");
        GameObject anchorObj = Instantiate(def.prefab.gameObject, anchorPos, Quaternion.identity, parent);
        anchorObj.name = $"Special_{coord.x}_{coord.y}";
        // IChunk 인터페이스를 구현하는 컴포넌트(TerrainChunk 또는 LargeStaticTerrainChunk) 찾기
        IChunk anchorChunk = anchorObj.GetComponent<IChunk>();
        if (anchorChunk == null)
        {
            //Debug.LogError($"[SpecialChunkManager] Prefab '{def.prefab.name}' missing IChunk (TerrainChunk or LargeStaticTerrainChunk) component!");
            Destroy(anchorObj);
            return null;
        }

        // OCP: 타입 검사 없이 IChunk 계약으로 처리 (OnSpawned / NeedsDelayedActivation)
        anchorChunk.OnSpawned(coord);

        // 특수청크 앵커 마킹 — 저장 분류·언로드 Destroy/Pool 판단에 사용.
        // 자식에 IChunkInitializer(가마솥·던전문 등)를 둔 경우도 정확히 특수청크로 식별되게 한다.
        if (anchorChunk is TerrainChunk anchorTc) anchorTc.IsSpecialChunkInstance = true;

        // 실제 배치된 앵커 등록 (나침반 등 소비자용). 언로드 시 UnregisterSubChunksForAnchor에서 제거.
        _activeAnchors[coord] = def.chunkType;

        // 프리팹에 CompassPoi 마커가 있으면 나침반 조준점으로 등록 (첫 번째 마커만 사용).
        var poi = anchorObj.GetComponentInChildren<CompassPoi>(true);
        if (poi != null) _anchorPois[coord] = poi.transform;

        // IChunkInitializer — InitializationOrder 오름차순 정렬 후 실행
        // GetComponentsInChildren: 루트 포함 자식까지 탐색 (IndestructibleOverlayInit 등 자식 초기화 지원)
        var initializers = anchorObj.GetComponentsInChildren<IChunkInitializer>();
        System.Array.Sort(initializers, (a, b) => a.InitializationOrder.CompareTo(b.InitializationOrder));
        foreach (var initializer in initializers)
            initializer.Initialize(parent);


        // OCP: 각 청크 타입이 스스로 지연 활성화 필요 여부를 결정
        // 직접 Instantiate된 TC 앵커(IChunkInitializer 보유)는 Job이 없으므로 지연 비활성화 불필요
        // — SetActive(false)하면 자식 DiggableSection들도 비활성화되어 Phase 1 초기화가 무효화됨
        if (anchorChunk.NeedsDelayedActivation && anchorObj.GetComponent<IChunkInitializer>() == null)
            anchorObj.SetActive(false);

        // 링크 피스 캐시 채우기 (앵커가 로드됐을 때만 실행)
        if (def.linkedPieces != null)
        {
            foreach (var piece in def.linkedPieces)
            {
                if (piece.prefab == null) continue;
                var pieceCoord = coord + piece.offset;
                _linkedRegistry.Register(pieceCoord, piece.prefab, coord);
                Debug.Log($"[LinkedChunk] 앵커={coord} → 피스 캐시 등록: {pieceCoord} prefab={piece.prefab.name}");
            }
        }

        // 3. 서브 위치 예약 등록 (TerrainChunk 인스턴스 없이 좌표만 등록)
        //    BuildLoadQueue가 이 위치들을 IsSubChunkCoord로 skip하도록 함
        int sizeX = def.SizeX;
        int sizeY = def.SizeY;
        //Debug.Log($"[SpecialChunkManager] CHUNKSIZE | chunkSizeX={def.chunkSizeX} chunkSizeY={def.chunkSizeY} | sizeX={sizeX} sizeY={sizeY}");

        if (sizeX > 1 || sizeY > 1)
        {
            // 앵커에 상대 위치 주입 (offset = zero)
            foreach (var part in anchorObj.GetComponents<IMultiChunkPart>())
                part.OnMultiChunkSpawned(coord, Vector2Int.zero);

            // 자식 TerrainChunk를 Coord 기준으로 인덱싱 (파기 가능 서브청크 지원)
            var childChunkMap = new Dictionary<Vector2Int, TerrainChunk>();
            foreach (var tc in anchorObj.GetComponentsInChildren<TerrainChunk>())
                childChunkMap[tc.Coord] = tc;

            for (int dx = 0; dx < sizeX; dx++)
            {
                for (int dy = 0; dy > -sizeY; dy--)
                {
                    if (dx == 0 && dy == 0) continue; // 앵커 자신은 skip

                    Vector2Int subCoord = coord + new Vector2Int(dx, dy);

                    if (childChunkMap.TryGetValue(subCoord, out TerrainChunk childTc))
                    {
                        // 실제 TerrainChunk 인스턴스로 등록 → 파기·조명·세이브 활성화
                        _registry.Register(subCoord, childTc, coord);
                        outSubChunks ??= new List<(Vector2Int, IChunk)>();
                        outSubChunks.Add((subCoord, childTc));
                    }
                    else
                    {
                        // 정적 영역: 좌표만 예약 (일반 TerrainChunk 스폰 차단)
                        _registry.RegisterReserved(subCoord, coord);
                    }
                }
            }
        }

        //Debug.Log($"[SpecialChunkManager] SpawnComplete | name={anchorObj.name} | alive={(anchorChunk as UnityEngine.Object) != null} | active={anchorObj.activeSelf}");
        return anchorChunk;
    }

    /// <summary>서브 청크 불필요한 호출처용 오버로드</summary>
    public IChunk SpawnSpecialChunkIfPossible(
        Vector2Int coord, TileType layerType, int worldSeed,
        Transform parent)
    {
        return SpawnSpecialChunkIfPossible(
            coord, layerType, worldSeed, parent,
            out _);
    }

    #endregion
}
