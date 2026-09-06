// @tags: pipeline, chunk, generation, factory, pool
using UnityEngine;
using System;

/// <summary>
/// 일반적인 절차적 생성 청크를 담당하는 팩토리
/// </summary>
public class StandardChunkFactory : IChunkFactory
{
    private ChunkPool _chunkPool;
    private GameObject _chunkPrefab;
    private Transform _parentTransform;
    private Transform _playerTransform;
    private TileVisualSettings _visualSettings;
    private IChunkProvider _chunkProvider; // [New]

    public StandardChunkFactory(ChunkPool pool, GameObject prefab, Transform parent, Transform player, TileVisualSettings settings, IChunkProvider provider)
    {
        _chunkPool = pool;
        _chunkPrefab = prefab;
        _parentTransform = parent;
        _playerTransform = player;
        _visualSettings = settings;
        _chunkProvider = provider;
    }

    public IChunk CreateChunk(ChunkGenerationContext context)
    {
        TerrainChunk chunk = null;

        // 1. 링크 피스 전용 프리팹 — 앵커 특수청크와 동일한 초기화 경로
        if (context.LinkedPiecePrefab != null && context.LinkedPiecePrefab != _chunkPrefab)
        {
            GameObject obj = UnityEngine.Object.Instantiate(context.LinkedPiecePrefab, _parentTransform);
            chunk = obj.GetComponent<TerrainChunk>();
            chunk.FirstTimeInit(context.SourceWidth, context.SourceHeight);

            chunk.transform.position = ChunkCoords.ToWorld(context.Coord);
            chunk.transform.name = $"Chunk_{context.Coord.x}_{context.Coord.y}";
            chunk.SetChunkProvider(_chunkProvider);
            chunk.player = _playerTransform;

            // IChunkInitializer 실행 (SpriteCavityInitializer 등이 BasePixels 설정)
            var initializers = obj.GetComponentsInChildren<IChunkInitializer>();
            System.Array.Sort(initializers, (a, b) => a.InitializationOrder.CompareTo(b.InitializationOrder));
            foreach (var init in initializers)
                init.Initialize(_parentTransform);

            // 경계 데이터 적용 (SpecialChunkFactory 패턴)
            chunk.ApplyBorderDataOnly(
                context.BorderPixels, context.BorderWidth, context.BorderHeight,
                context.SecondaryBorderPixels, context.SecondaryBorderWidth, context.SecondaryBorderHeight,
                context.SecondaryTileId);

            // 저장된 파진 픽셀 복원
            if (context.SavedData?.modifiedPixels != null)
                chunk.RestoreSavedPixels(context.SavedData.modifiedPixels, context.SavedData.pixelInfo);

            return chunk;
        }
        // 2. Try Reuse from Pool
        else if (_chunkPool.HasAvailableChunks())
        {
            // [Fix] SetActive(true)를 여기서 하지 않음 → Reuse_Step2_Finalize()에서 수행
            // Reuse_Step1_Prepare()가 inactive 상태에서 실행되어야 이전 암석들이 활성화되지 않음
            chunk = _chunkPool.Get();
        }
        else
        {
            // 3. Instantiate New
            GameObject obj = UnityEngine.Object.Instantiate(_chunkPrefab, _parentTransform);
            chunk = obj.GetComponent<TerrainChunk>();
            // Initialize basic buffers
            chunk.FirstTimeInit(context.SourceWidth, context.SourceHeight);
        }

        // 3. Setup Transform
        Vector3 worldPos = ChunkCoords.ToWorld(context.Coord);
        chunk.transform.position = worldPos;
        chunk.transform.name = $"Chunk_{context.Coord.x}_{context.Coord.y}";
        //Debug.Log($"[StandardChunkFactory] 일반 청크 배치 — coord={context.Coord} worldPos={worldPos} 실제={chunk.transform.position}");
        
        // [New] Inject Provider
        chunk.SetChunkProvider(_chunkProvider);
        
        try
        {
            // 4. Initialize Data
            InitializeChunkData(chunk, context);
        }
        catch (Exception e)
        {
            // Error handling: cleanup and rethrow
            if (chunk != null)
            {
                UnityEngine.Object.Destroy(chunk.gameObject);
                chunk = null;
            }
            throw e;
        }

        return chunk;
    }

    private void InitializeChunkData(TerrainChunk chunk, ChunkGenerationContext context)
    {
        if (_visualSettings != null)
        {
            var data = _visualSettings.GetDataForType(context.TileType);
            chunk.borderTexture = data.borderTexture;
            chunk.secondaryBorderTexture = null; 
        }

        // [Refactored] Use Data Object for BOTH New and Restored chunks
        // This ensures consistent behavior (e.g. clearing old borders, setting up colliders)
        var initData = new ChunkInitializationData();
        initData.Player = _playerTransform;
        
        // Common Data (Borders & Visuals)
        initData.BorderPixels = context.BorderPixels;
        initData.BorderWidth = context.BorderWidth;
        initData.BorderHeight = context.BorderHeight;
        
        initData.SecondaryBorderPixels = context.SecondaryBorderPixels;
        initData.SecondaryBorderWidth = context.SecondaryBorderWidth;
        initData.SecondaryBorderHeight = context.SecondaryBorderHeight;
        initData.SecondaryTileId = context.SecondaryTileId;

        // [Cave] 저장 복원 청크도 똑같이 뚫는다.
        //
        // 카빙은 solid 픽셀을 공기로 **지우기만** 하고 (공기면 즉시 return) 지형을 되살리지 않는다.
        // 플레이어도 지형을 지우기만 하므로(메우는 기능 없음) 두 결과는 순서와 무관하게 같다:
        // 이미 카빙됐던 자리는 저장본에서 공기라 no-op 이고, 판 자리는 카빙이 건드리지 않는다.
        //
        // 이걸 켜야 (1) 굴 파라미터를 바꾸기 전에 저장된 청크에도 새 굴이 반영되고,
        // (2) 이웃으로 뻗은 연결 통로가 복원 청크 경계에서 막다른 길로 끝나지 않는다.
        initData.WorldSeed = context.WorldSeed;
        initData.Cave = ResolveCaveSettings(context);

        if (context.IsRestoredFromSave)
        {
            // Restore from SaveData
            initData.Pixels = context.SavedData.modifiedPixels;
            initData.PixelInfo = context.SavedData.pixelInfo;
            initData.HasChanges = true; // Mark as modified so we don't regenerate
            initData.SavedRocks = context.SavedData.savedRocks; // [Save] 암석 배치 복원용
            //Debug.Log($"[ChunkFactory] Initializing RESTORED chunk at {context.Coord}.");
        }
        else
        {
            // Fresh Generation
            initData.Pixels = context.GroundPixels;
            initData.PixelInfo = context.PixelInfo;               // 블렌딩 경계 청크만 non-null
            initData.UniformPixelInfoId = context.UniformPixelInfoId; // [GC] 그 외에는 ID만 전달
            initData.HasChanges = false;
            //Debug.Log($"[ChunkFactory] Initializing FRESH chunk at {context.Coord}. TileType: {context.TileType}");
        }

        chunk.Reuse_Step1_Prepare(initData);
    }

    /// <summary>
    /// 이 청크에 뚫을 굴 설정을 고른다.
    ///
    /// 굴을 파는지 여부는 자기 자신도 이웃도 <see cref="CaveCarveLayout.Carves"/> 하나로 판정한다.
    /// 예전엔 자기 판단과 이웃 판단이 각자 규칙표를 갖고 있어서 PNG 페인팅 청크에서 답이 갈렸고,
    /// 그 경계에 연결 통로가 수직 벽으로 끊긴 자국이 남았다.
    /// </summary>
    private CaveCarveSettings ResolveCaveSettings(ChunkGenerationContext context)
    {
        if (TileDataManager.Instance == null) return CaveCarveSettings.None;

        if (!CaveCarveLayout.Carves(context.Coord, context.WorldSeed))
            return CaveCarveSettings.None;

        var settings = TileDataManager.Instance.GetCaveSettings(context.TileType);

        // 좌우 이웃이 굴을 파는 청크일 때만 그쪽으로 연결 통로를 뻗는다.
        // 안 그러면 받아줄 쪽이 없어 굴이 경계에서 수직 벽으로 잘린다.
        var left  = new Vector2Int(context.Coord.x - 1, context.Coord.y);
        var right = new Vector2Int(context.Coord.x + 1, context.Coord.y);
        settings.leftNeighborCarves  = CaveCarveLayout.Carves(left,  context.WorldSeed);
        settings.rightNeighborCarves = CaveCarveLayout.Carves(right, context.WorldSeed);

        return settings;
    }
}
