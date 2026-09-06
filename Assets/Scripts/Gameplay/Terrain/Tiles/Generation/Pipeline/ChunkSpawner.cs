// @tags: pipeline, chunk, generation, factory, spawn
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [Refactored] Pure Factory for creating TerrainChunks.
/// Decoupled from data gathering and business logic.
/// Operates solely on ChunkGenerationContext.
/// </summary>
public class ChunkSpawner
{
    private GameObject chunkPrefab;
    private Transform parentTransform;
    private Transform player;
    private ChunkPool chunkPool;
    
    // [Phase 2] Dependencies
    private TileVisualSettings visualSettings;
    private int worldSeed;

    // [Phase 3] Abstract Factory Pattern
    private StandardChunkFactory _standardFactory;
    private SpecialChunkFactory _specialFactory;
    
    private IChunkProvider _chunkProvider; // [New]

    public Transform Player => player;
    public IChunkProvider ChunkProvider => _chunkProvider;

    public ChunkSpawner(
        GameObject prefab,
        Transform parent,
        Transform playerTransform,
        TileVisualSettings settings,
        ChunkPool pool,
        int seed, // [Re-added]
        IChunkProvider provider // [New]
    )
    {
        this.chunkPrefab = prefab;
        this.parentTransform = parent;
        this.player = playerTransform;
        this.visualSettings = settings;
        this.chunkPool = pool;
        this.worldSeed = seed;
        this._chunkProvider = provider;

        // Initialize Factories
        _standardFactory = new StandardChunkFactory(pool, prefab, parent, playerTransform, settings, provider);
        _specialFactory = new SpecialChunkFactory(provider, playerTransform);
    }

    /// <summary>
    /// Creates or reuses a chunk based on the provided context.
    /// </summary>
    public IChunk SpawnChunk(ChunkGenerationContext context)
    {
        // Debug Tracing
        // [Fix] Allow GroundPixels to be null if we are restoring from save (SavedData provided)
        if (!context.IsRestoredFromSave && (context.GroundPixels == null || context.GroundPixels.Length == 0) && !context.IsSpecialChunk)
        {
            Debug.LogError($"[ChunkSpawner] GroundPixels is missing for {context.Coord}!");
        }

        IChunk chunk = null;

        try 
        {
            // Delegate to appropriate factory
            if (context.IsSpecialChunk)
            {
                chunk = _specialFactory.CreateChunk(context);
            }
            else
            {
                chunk = _standardFactory.CreateChunk(context);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ChunkSpawner] Factory failed to create chunk {context.Coord}: {e.Message}\n{e.StackTrace}");
            throw;
        }

        return chunk;
    }
    
    // [Phase 2] Decoration
    // Use Decorator Pattern (OCP)
    private List<IChunkDecorator> _decorators;

    private void InitializeDecorators()
    {
        _decorators = new List<IChunkDecorator>();
        
        // 1. Elevator (Must be first to reserve space)
        _decorators.Add(new ElevatorDecorator());

        // 2. Rock (Mineral보다 먼저 실행해서 점유 영역을 등록)
        _decorators.Add(new RockDecorator(visualSettings));

        // 3. Mineral (Rock이 등록한 PreOccupiedAreas를 참조해 겹침 방지)
        _decorators.Add(new MineralDecorator());
    }

    public void DecorateChunk_Phase2(TerrainChunk chunk, Vector2Int coord)
    {
        // [P2-4] Steps 이터레이터를 소비하여 기존 동기 동작 유지
        foreach (var _ in DecorateChunk_Phase2_Steps(chunk, coord)) { }
    }

    private MineralDecorator _mineralOnlyDecorator;

    /// <summary>
    /// 특수청크(작가 배치 프리팹)용 — 광물만 생성한다.
    /// 일반 데코 경로는 청크 자식을 "ROCK_/MINERAL_ 외 Destroy"로 정리해 작가 배치 자식까지
    /// 파괴하므로 특수청크엔 쓸 수 없다. 여기선 MINERAL_ 자식만 풀 반납하고 광물만 재생성한다.
    /// (엘리베이터·바위는 작가 레이아웃과 충돌하므로 제외)
    /// </summary>
    /// <param name="ignoreProtection">
    /// true면 보호 좌표에서도 광물을 생성한다. IChunkPostLoadPainter(ImageChunkOverrider 등)가
    /// PNG 페인팅을 마친 뒤 호출하는 전용 경로다 — 페인팅 후 픽셀을 기준으로 자리를 고르므로
    /// 광물이 방 공동에 떠서 지지검사에 탈락하는 문제가 없다.
    /// 일반 파이프라인 경로에서는 절대 true로 부르지 말 것(페인팅 전 지형에 박힌다).
    /// </param>
    public void DecorateMineralsOnly(TerrainChunk chunk, Vector2Int coord, bool ignoreProtection = false)
    {
        // 보호 좌표(ImageChunkOverrider 등)는 여전히 스킵
        if (!ignoreProtection &&
            SpecialChunkManager.Instance != null &&
            SpecialChunkManager.Instance.IsProtectedCoord(coord))
            return;

        // 기존 MINERAL_ 자식만 풀 반납 — 특수청크의 다른 자식(작가 배치)은 건드리지 않는다.
        for (int i = chunk.transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = chunk.transform.GetChild(i).gameObject;
            if (child.name.StartsWith("MINERAL_"))
                MineralGenerator.ReturnToPool(child);
        }

        if (TileDataManager.Instance == null) return;

        if (_mineralOnlyDecorator == null) _mineralOnlyDecorator = new MineralDecorator();

        TileType targetType = TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y);
        var context = new DecorationContext(coord, worldSeed, targetType, chunk.hasBeenModified);

        // 작가가 프리팹에 직접 배치한 돌(selfRegister)이 등록해둔 영역을 제외 영역으로 넘긴다.
        // 여긴 RockDecorator를 안 돌리므로 직접 채워줘야 한다.
        foreach (var bound in chunk.GeneratedRockBounds)
            context.PreOccupiedAreas.Add(bound);

        // ElevatorDecorator는 자기 DecorationContext에만 영역을 넣으므로 여기선 청크에서 읽는다.
        // (보호 좌표에도 엘리베이터는 생성된다 — 시작 방 0,0)
        if (chunk.ElevatorArea.HasValue)
            context.PreOccupiedAreas.Add(chunk.ElevatorArea.Value);

        _mineralOnlyDecorator.Decorate(chunk, context);
    }

    /// <summary>
    /// [P2-4] 데코레이터를 하나씩 실행하며 yield — 호출측에서 프레임 분산 가능.
    /// </summary>
    public IEnumerable<IChunkDecorator> DecorateChunk_Phase2_Steps(TerrainChunk chunk, Vector2Int coord)
    {
        if (_decorators == null) InitializeDecorators();

        bool isModified = chunk.hasBeenModified;

        // [최적화 적용] 기존 장식물 파괴(Destroy) 대신 풀링(Pooling)으로 반납
        if (chunk.transform.childCount > 0)
        {
            for (int i = chunk.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = chunk.transform.GetChild(i).gameObject;

                if (child.name.StartsWith("ROCK_"))
                    RockSpawner.ReturnToPool(child);
                else if (child.name.StartsWith("MINERAL_"))
                    MineralGenerator.ReturnToPool(child);
                else if (child.name == ChunkBackground.ChildName)
                    continue; // 층 배경: TerrainChunk.Awake에서 1회만 생성 → 파괴하면 재생성 경로가 없다
                else
                    UnityEngine.Object.Destroy(child);
            }
        }

        // 보호 좌표(ImageChunkOverrider 등 작가가 PNG로 픽셀을 완전히 덮어쓰는 청크)는
        // 절차 바위·광물을 건너뛴다. 위 자식 정리(풀 반납)는 이미 수행됐으므로,
        // 재로드 시마다 절차 광물이 다시 깔리는 재생성·과밀 문제도 함께 차단된다.
        // 단, 엘리베이터는 시작 방(0,0 등) 보호 좌표에도 필요하므로 예외로 허용한다.
        //
        // [중요] 이 판정은 루프 '안'에서 매번 다시 읽는다 — 루프 밖에서 한 번 캐시하면 안 된다.
        // ElevatorDecorator가 첫 번째로 돌면서 정류장 좌표를 보호 좌표로 새로 등록하는데
        // (ElevatorRoomPainter.RegisterStop), 캐시해두면 그 등록이 같은 로드에서 반영되지 않아
        // 뒤따르는 Rock·Mineral이 "곧 방으로 칠해질 지형" 위에 데코를 깔아버린다.
        TileType targetType = TileDataManager.Instance != null
            ? TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y)
            : TileType.Dirt;
        DecorationContext context = new DecorationContext(coord, worldSeed, targetType, isModified);

        foreach (var decorator in _decorators)
        {
            // 보호 좌표: 엘리베이터만 허용, 바위·광물은 스킵
            if (!(decorator is ElevatorDecorator) &&
                SpecialChunkManager.Instance != null &&
                SpecialChunkManager.Instance.IsProtectedCoord(coord))
                continue;

            try { decorator.Decorate(chunk, context); }
            catch (Exception e)
            {
                Debug.LogError($"[ChunkSpawner] Decoration failed for {decorator.GetType().Name} at {coord}: {e.Message}\n{e.StackTrace}");
            }
            yield return decorator; // 한 데코레이터 완료 — 호출측에서 yield 여부 판단
        }
    }
}