// @tags: pipeline, chunk, generation, factory, decoration
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 청크 생성의 전체 파이프라인을 정의하는 클래스
/// Phase별 실행 순서와 로직을 캡슐화하여 비즈니스 로직을 분리
/// </summary>
public class ChunkGenerationPipeline
{
    private ActiveChunkRegistry _registry;
    private ChunkSpawner _spawner;
    private ChunkPool _pool;
    private ChunkDataProvider _provider;
    
    // Context for Provider
    private Transform _parent;

    public ChunkGenerationPipeline(
        ActiveChunkRegistry registry,
        ChunkSpawner spawner,
        ChunkPool pool,
        ChunkDataProvider provider,
        Transform parent)
    {
        _registry = registry;
        _spawner = spawner;
        _pool = pool;
        _provider = provider;
        _parent = parent;
    }
    
    /// <summary>
    /// Phase 1: 청크 스폰 및 초기화 (Spawn & Initialize)
    /// </summary>
    /// <param name="coord">청크 좌표</param>
    /// <returns>생성된 TerrainChunk 또는 null</returns>
    public IChunk ExecutePhase1_SpawnAndInitialize(Vector2Int coord)
    {
        //Debug.Log($"[CHUNK_DEBUG] ExecutePhase1_SpawnAndInitialize START for {coord}");
        
        // Double check validation
        if (_registry.HasChunk(coord))
        {
            //Debug.Log($"[CHUNK_DEBUG] Chunk {coord} already in registry, returning null");
            return null;
        }
        
        //Debug.Log($"[CHUNK_DEBUG] Getting context from provider for {coord}...");
        // 1. Refresh Data Context
        var context = _provider.GetContext(coord, _parent);
        //Debug.Log($"[CHUNK_DEBUG] Context received: {(context != null ? "SUCCESS" : "NULL")}");

        // 1-A2. 서브청크 자리 사전 차단 — 앵커 스폰 시 자동 등록될 예정
        if (context.IsBlocked)
            return null;

        // 2. Spawn via Spawner
        //Debug.Log($"[CHUNK_DEBUG] Calling _spawner.SpawnChunk for {coord}...");
        var chunk = _spawner.SpawnChunk(context);
        //Debug.Log($"[Pipeline] SpawnChunk returned: {(chunk != null ? chunk.gameObject.name : "NULL")} for {coord}");
        
        if (chunk != null)
        {
            // Register active chunk
            if (!_registry.HasChunk(coord))
            {
                //Debug.Log($"[CHUNK_DEBUG] Registering chunk {coord} in registry");
                _registry.Add(coord, chunk);
            }
            else
            {
                Debug.LogWarning($"[CHUNK_DEBUG] Chunk {coord} already registered (race condition?)");
            }

            // 멀티 청크: 서브 청크도 즉시 레지스트리에 등록 + 활성화
            if (context.SubChunkInstances != null)
            {
                foreach (var (subCoord, subChunk) in context.SubChunkInstances)
                {
                    if (subChunk == null) continue;
                    if (!_registry.HasChunk(subCoord))
                    {
                        _registry.Add(subCoord, subChunk);

                        if (subChunk is TerrainChunk tChunk)
                        {
                            // 플레이어/청크 프로바이더 주입 (Dig, 이웃 청크 조회에 필요)
                            tChunk.SetChunkProvider(_spawner.ChunkProvider);
                            tChunk.player = _spawner.Player;

                            // 콜라이더 업데이트 + SetActive(true) 수행
                            tChunk.Reuse_Step2_Finalize();

                            tChunk.UpdateBoundaryLighting(true, true, true, true);
                        }
                        else
                        {
                            subChunk.gameObject.SetActive(true);
                        }
                    }
                }
            }
        }
        else
        {
            Debug.LogError($"[CHUNK_DEBUG] SpawnChunk returned NULL for {coord}!");
        }
        
        //Debug.Log($"[CHUNK_DEBUG] ExecutePhase1_SpawnAndInitialize END for {coord}");
        return chunk;
    }
    
    /// <summary>
    /// Phase 2: 청크 마무리 및 장식 (Finalize & Decorate) — 동기 버전 (레거시 호환용)
    /// </summary>
    public void ExecutePhase2_FinalizeAndDecorate(Vector2Int coord, IChunk chunk)
    {
        var unityObj = chunk as UnityEngine.Object;
        if (unityObj == null) return;

        if (chunk is TerrainChunk tChunk)
        {
            tChunk.Reuse_Step2_Finalize();

            bool isSpecialPrebuilt = tChunk.GetComponent<IChunkInitializer>() != null;
            if (!isSpecialPrebuilt)
                _spawner.DecorateChunk_Phase2(tChunk, coord);
            else
                _spawner.DecorateMineralsOnly(tChunk, coord);

            InvokePostLoadPainter(tChunk, coord);

            tChunk.RevealExposedRocks();
            tChunk.RevealExposedCavity();
        }
    }

    /// <summary>
    /// 보호 좌표에 등록된 로드 후 처리(PNG 페인팅 + 광물 재생성)를 실행한다.
    ///
    /// [순서 필수] 반드시 데코레이터 루프 '뒤'에 호출할 것.
    /// DecorateChunk_Phase2_Steps는 시작 시 청크 자식(ROCK_/MINERAL_)을 전부 풀 반납하므로,
    /// 앞에서 부르면 페인터가 방금 만든 광물이 그 정리 루프에 쓸려나간다.
    /// </summary>
    private void InvokePostLoadPainter(TerrainChunk chunk, Vector2Int coord)
    {
        if (SpecialChunkManager.Instance == null) return;
        if (!SpecialChunkManager.Instance.TryGetPostLoadPainter(coord, out var painter)) return;

        try { painter.OnChunkLoaded(chunk, coord); }
        catch (Exception e)
        {
            Debug.LogError($"[Pipeline] PostLoadPainter 실패 {coord}: {e.Message}\n{e.StackTrace}");
        }
    }

    /// <summary>
    /// [P2-4] Phase 2 데코레이터를 하나씩 실행. 호출측(Runner)에서 데코레이터 사이에 yield 가능.
    /// </summary>
    public IEnumerable<int> ExecutePhase2_DecoratorSteps(Vector2Int coord, IChunk chunk)
    {
        var unityObj = chunk as UnityEngine.Object;
        if (unityObj == null) yield break;
        if (!(chunk is TerrainChunk tChunk)) yield break;

        tChunk.Reuse_Step2_Finalize();

        bool isSpecialPrebuilt = tChunk.GetComponent<IChunkInitializer>() != null;
        if (!isSpecialPrebuilt)
        {
            foreach (var _ in _spawner.DecorateChunk_Phase2_Steps(tChunk, coord))
                yield return 0; // 데코레이터 1개 완료 신호
        }
        else
        {
            // 특수청크: 작가 배치를 보존하며 광물만 생성 (엘리베이터·바위 제외)
            _spawner.DecorateMineralsOnly(tChunk, coord);
        }

        InvokePostLoadPainter(tChunk, coord);

        tChunk.RevealExposedRocks();
        tChunk.RevealExposedCavity();
    }
    
    /// <summary>
    /// Phase 3: 조명 업데이트 (Update Lighting)
    /// </summary>
    public void ExecutePhase3_UpdateLighting(IChunk chunk)
    {
        var unityObj = chunk as UnityEngine.Object;
        if (unityObj == null) return;

        if (chunk.gameObject.activeSelf && chunk is TerrainChunk tChunk)
        {
            // 이웃 청크들의 경계 갱신 (MarkChunkDirty로 비동기 위임, TriggerNeighborRefresh 참조)
            tChunk.UpdateBoundaryLighting(true, true, true, true);

            // 신규 청크 자신도 dirty 등록:
            // Phase 2.5에서 SyncBoundary 없이 Chamfer를 예약했으므로, LateUpdate의
            // ProcessDirtyChunksAsync(2-round)가 이웃 DistanceField를 반영한 올바른 경계 조명을 완성.
            var mgr = InfinityMapManager.Instance;
            if (mgr != null)
                mgr.MarkChunkDirty(tChunk, new RectInt(0, 0, tChunk.width, tChunk.height));
        }
    }
    
    public void OnPhase1_SpawnError(Vector2Int coord, Exception error)
    {
        Debug.LogError($"[ChunkPipeline] Phase1 Error at {coord}: {error.Message}\n{error.StackTrace}");
    }
    
    public void OnPhase2_FinalizeError(Vector2Int coord, IChunk chunk, Exception error)
    {
        Debug.LogError($"[ChunkPipeline] Phase2 Error at {coord}: {error.Message}");
        
        _registry.Remove(coord);
        
        if (chunk is TerrainChunk tc)
            _pool.Return(tc);
        else if (chunk != null)
            UnityEngine.Object.Destroy(chunk.gameObject);
    }
}
