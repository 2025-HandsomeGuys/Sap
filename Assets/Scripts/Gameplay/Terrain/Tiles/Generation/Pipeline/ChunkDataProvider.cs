// @tags: pipeline, chunk, generation, data-container, dto, save, restore, special-chunk
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Responsible for gathering all data required to generate a chunk.
/// Acts as the single source of truth for chunk data, accessing various managers.
/// </summary>
public class ChunkDataProvider
{
    private TileVisualSettings _visualSettings;
    private WorldPersistenceSystem _persistenceSystem;
    private int _worldSeed;
    private bool _enableLayerBlending;

    // Cache references if needed, or access singletons directly here (as agreed in plan)
    // Ideally, we'd inject interfaces, but for now we wrap singletons.

    public ChunkDataProvider(
        TileVisualSettings visualSettings,
        WorldPersistenceSystem persistenceSystem,
        int worldSeed,
        bool enableLayerBlending)
    {
        _visualSettings = visualSettings;
        _persistenceSystem = persistenceSystem;
        _worldSeed = worldSeed;
        _enableLayerBlending = enableLayerBlending;
    }

    public ChunkGenerationContext GetContext(Vector2Int coord, Transform parent)
    {
        var context = new ChunkGenerationContext
        {
            Coord = coord,
            WorldSeed = _worldSeed,
            SourceWidth = TileDataManager.Instance.sourceWidth,
            SourceHeight = TileDataManager.Instance.sourceHeight
        };

        //Debug.Log($"[TRACE][ChunkDataProvider] GetContext 진입: {coord}");

        // 1-A. 서브 청크 선조회 — 이미 앵커 스폰 시 등록된 서브 청크인지 확인
        if (SpecialChunkManager.Instance != null &&
            SpecialChunkManager.Instance.TryGetRegisteredSubChunk(coord, out var registeredSub))
        {
            //Debug.Log($"[TRACE][ChunkDataProvider] {coord} → 1-A 등록된 서브청크");
            context.SpecialChunkInstance = registeredSub;
            // [Fix] Border 렌더링을 위해 TileType 결정 및 비주얼 데이터 로드
            context.TileType = TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y);
            LoadVisualData(context, context.TileType);
            return context;
        }

        // 1-A-LINK. 링크 피스 캐시·역산 경로 (앵커 로드 여부 무관)
        // SpawnSpecialChunkIfPossible보다 먼저 실행해 링크 피스 좌표에 다른 특수청크가 스폰되는 것을 막는다.
        if (SpecialChunkManager.Instance != null)
        {
            var linkedTileType = TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y);
            if (SpecialChunkManager.Instance.TryGetLinkedPiecePrefab(
                coord, linkedTileType, _worldSeed, out var linkedPrefab))
            {
                Debug.Log($"[LinkedChunk] 피스 스폰: coord={coord} prefab={linkedPrefab?.name ?? "기본"} layer={linkedTileType}");
                context.TileType = linkedTileType;
                if (linkedPrefab != null)
                    context.LinkedPiecePrefab = linkedPrefab.gameObject;

                if (_persistenceSystem.TryGetChunkData(coord, out var linkSaved))
                    context.SavedData = linkSaved;

                LoadVisualData(context, context.TileType);

                if (!context.IsRestoredFromSave)
                    GenerateNewData(context);

                return context;
            }
        }

        // 2. Determine Tile Type (SavedData 체크에 필요하므로 먼저 결정)
        context.TileType = TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y);

        // 3. Check for Saved Data (Priority 1) — 일반 청크로 저장된 경우만 여기서 early return!
        // [Fix] 일반 청크로 저장된 좌표를 재로드 시 특수청크로 덮어쓰는 버그 방지.
        // 언로드→재로드 시 exclusion zone 상태가 달라져 TrySelect 결과가 바뀔 수 있으므로,
        // wasNormalChunk=true면 특수청크 판단 없이 그대로 복원한다.
        // 특수 청크로 저장된 경우(wasNormalChunk=false)는 아래 SpawnSpecialChunk 경로로 재스폰.
        ChunkSaveData savedData;
        if (_persistenceSystem.TryGetChunkData(coord, out savedData) && savedData.wasNormalChunk)
        {
            context.SavedData = savedData;
            LoadVisualData(context, context.TileType);
            return context;
        }

        // 1-B. 앵커 스페셜 청크 시도 (저장된 데이터 없는 신규 청크만 해당)
        if (SpecialChunkManager.Instance != null)
        {
            List<(Vector2Int subCoord, IChunk subChunk)> subChunks;
            var specialChunk = SpecialChunkManager.Instance.SpawnSpecialChunkIfPossible(
                coord,
                context.TileType,
                _worldSeed,
                parent,
                out subChunks
            );

            if (specialChunk != null)
            {
                //Debug.Log($"[TRACE][ChunkDataProvider] {coord} → 1-B 특수청크 스폰 성공: {specialChunk.gameObject.name}");
                context.SpecialChunkInstance = specialChunk;
                context.SubChunkInstances    = subChunks;
                // [Fix] Border 렌더링을 위해 비주얼 데이터 로드 (TileType은 line 52에서 이미 결정됨)
                LoadVisualData(context, context.TileType);
                // [Fix] 이전에 wasNormalChunk=false로 저장된 특수청크 픽셀 복원 데이터 전달
                if (savedData != null && savedData.hasChanges)
                    context.SavedData = savedData;
                return context;
            }
            //Debug.Log($"[TRACE][ChunkDataProvider] {coord} → 1-B 특수청크 없음(null)");
        }

        // 1-A2. 서브청크 사전 차단
        if (SpecialChunkManager.Instance != null)
        {
            if (SpecialChunkManager.Instance.IsSubChunkCoord(coord, context.TileType, _worldSeed))
            {
                //Debug.Log($"[TRACE][ChunkDataProvider] {coord} → 1-A2 IsBlocked(서브좌표 차단)");
                context.IsBlocked = true;
                return context;
            }
        }

        // 4. Generate New Data (Priority 2)
        GenerateNewData(context);

        return context;
    }

    // ============================================================================================================
    //  [GC] 블렌딩 경로 PixelInfo 스테이징 버퍼 재사용
    //
    //  안전 근거: Phase 1(ChunkGenerationPipeline.ExecutePhase1_SpawnAndInitialize)은 메인 스레드에서
    //  동기 실행되고, TerrainChunk.Reuse_Step1_Prepare가 같은 호출 안에서 CopyFrom으로 NativeArray에
    //  즉시 복사한다. 따라서 다음 청크가 같은 버퍼를 덮어써도 문제가 없다.
    //  ⚠ 이 배열의 참조를 프레임을 넘겨 보관하는 코드를 추가하면 안 된다.
    // ============================================================================================================
    private static byte[] s_pixelInfoBuffer;

    private static byte[] RentPixelInfoBuffer(int length, byte fillId)
    {
        if (s_pixelInfoBuffer == null || s_pixelInfoBuffer.Length != length)
            s_pixelInfoBuffer = new byte[length];

        var buf = s_pixelInfoBuffer;
        for (int i = 0; i < length; i++)
            buf[i] = fillId;
        return buf;
    }

    private void LoadVisualData(ChunkGenerationContext context, TileType type)
    {
        // Fetch Border Data
        context.BorderPixels = TileDataManager.Instance.GetBorderPixels(type);
        if (context.BorderPixels != null)
        {
            Vector2Int dims = TileDataManager.Instance.GetBorderDimensions(type);
            context.BorderWidth = dims.x;
            context.BorderHeight = dims.y;
        }
    }

    private void GenerateNewData(ChunkGenerationContext context)
    {
        // [Fix] Validate Source Dimensions
        if (context.SourceWidth <= 0 || context.SourceHeight <= 0)
        {
            // Try fetch again
            if (TileDataManager.Instance != null && TileDataManager.Instance.sourceWidth > 0)
            {
                context.SourceWidth = TileDataManager.Instance.sourceWidth;
                context.SourceHeight = TileDataManager.Instance.sourceHeight;
            }
            else
            {
                // Last resort fallback to prevent array creation errors
                Debug.LogError($"[ChunkDataProvider] Invalid Source Dimensions for {context.Coord}: {context.SourceWidth}x{context.SourceHeight}. Using fallback 32x32.");
                context.SourceWidth = 32;
                context.SourceHeight = 32;
            }
        }

        int totalPixels = context.SourceWidth * context.SourceHeight;
        byte currentTypeId = (byte)context.TileType;

        // [GC] 기본은 "전체가 현재 층 ID" — 배열을 만들지 않고 ID만 넘긴다.
        // TerrainChunk가 ChunkData.FillPixelInfo(MemSet)로 채운다. 청크당 1MB 할당 제거.
        // 블렌딩이 실제로 일어나는 경계 청크에서만 아래 배열 경로로 전환한다.
        context.PixelInfo = null;
        context.UniformPixelInfoId = currentTypeId;

        // Base Ground Pixels
        Color32[] groundPixels = TileDataManager.Instance.GetGroundPixels(context.TileType);

        // Blending Logic
        if (_enableLayerBlending)
        {
            // ... (Blending implementation)
            TileType typeBelow;
            if (TerrainBlender.ShouldBlendLayer(context.Coord, context.TileType, true, out typeBelow))
            {
                // ... Existing Blending Logic ...
                Color32[] belowPixels = TileDataManager.Instance.GetGroundPixels(typeBelow);
                if (belowPixels != null)
                {
                    byte belowTypeId = (byte)typeBelow;

                    // [GC] 블렌딩 구역만 아래 층 ID로 덮이므로 실제 배열이 필요 → 재사용 버퍼 사용.
                    // 아래 층 PixelInfo는 전체가 belowTypeId 단일값이라 배열로 만들지 않고 ID만 전달한다.
                    context.PixelInfo = RentPixelInfoBuffer(totalPixels, currentTypeId);
                    context.UniformPixelInfoId = -1;

                    // Execute Blending
                    BlendingProfileData profile =
                        TileDataManager.Instance.GetBlendingProfileForLayer(context.TileType);
                    groundPixels = TerrainBlender.CreateBlendedPixels(
                        groundPixels,
                        belowPixels,
                        context.PixelInfo,
                        belowTypeId,
                        context.TileType,
                        typeBelow,
                        context.SourceWidth,
                        context.SourceHeight,
                        profile
                    );

                    // Fetch Secondary Border Data
                    context.SecondaryBorderPixels = TileDataManager.Instance.GetBorderPixels(typeBelow);
                    context.SecondaryTileId = belowTypeId;
                    if (context.SecondaryBorderPixels != null)
                    {
                        Vector2Int dims = TileDataManager.Instance.GetBorderDimensions(typeBelow);
                        context.SecondaryBorderWidth = dims.x;
                        context.SecondaryBorderHeight = dims.y;
                    }
                }
            }
        }

        // Safety Fallback
        if (groundPixels == null)
        {
            Debug.LogError($"[ChunkDataProvider] Missing pixels for {context.TileType} at {context.Coord}. SourceDims: {context.SourceWidth}x{context.SourceHeight}");
            groundPixels = new Color32[context.SourceWidth * context.SourceHeight];
        }
        else
        {
            // Debug.Log($"[ChunkDataProvider] Generated {groundPixels.Length} pixels for {context.Coord}");
        }

        context.GroundPixels = groundPixels;
        LoadVisualData(context, context.TileType);
    }
}
