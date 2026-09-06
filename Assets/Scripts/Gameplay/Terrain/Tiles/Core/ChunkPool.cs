// @tags: chunk, pool, object-pool, reuse, lifecycle
using System.Collections.Generic;
using UnityEngine;

public class ChunkPool
{
    private readonly Queue<TerrainChunk> _pool = new Queue<TerrainChunk>();

    // #10 [Fix] Cap pool size to prevent unbounded growth from long play sessions.
    private const int MAX_POOL_SIZE = 32;

    /// <summary>
    /// Returns a chunk to the pool and deactivates it.
    /// If it's a special static chunk, or the pool is full, it gets destroyed instead.
    /// </summary>
    public void Return(TerrainChunk chunk)
    {
        if (chunk == null) return;

        chunk.EnsureJobsCompleted();

        if (_pool.Count >= MAX_POOL_SIZE)
        {
            Object.Destroy(chunk.gameObject);
        }
        else
        {
            chunk.gameObject.SetActive(false);
            _pool.Enqueue(chunk);
        }
    }

    /// <summary>
    /// Retrieves a chunk from the pool if available. Returns null otherwise.
    /// </summary>
    public TerrainChunk Get()
    {
        if (_pool.Count > 0)
        {
            TerrainChunk chunk = _pool.Dequeue();

            // Return()에서 이미 완료시키지만, 반납된 뒤에 잡이 다시 걸릴 수 있다 —
            // Phase 2에서 실패한 청크는 곧바로 풀에 반납되는데 같은 배치의 Phase 2.5가
            // 그 청크에 Chamfer/Visual 잡을 예약한다. 그 상태로 꺼내 주면 다음 청크의
            // Reuse_Step1_Prepare(BasePixels 쓰기)가 "TerrainVisualJob reads from baseData.
            // You must call JobHandle.Complete()" 로 터지고 로딩이 통째로 멈춘다.
            // 여기서 한 번 더 풀어 주는 비용은 대개 no-op이다(이미 끝난 핸들).
            if (chunk != null) chunk.EnsureJobsCompleted();

            return chunk;
        }
        return null;
    }

    /// <summary>
    /// Checks if the pool has available chunks.
    /// </summary>
    public bool HasAvailableChunks()
    {
        return _pool.Count > 0;
    }

    /// <summary>
    /// Clears the pool and destroys all contained chunks.
    /// </summary>
    public void Clear()
    {
        while (_pool.Count > 0)
        {
            var chunk = _pool.Dequeue();
            if (chunk != null)
            {
                Object.Destroy(chunk.gameObject);
            }
        }
    }
}
