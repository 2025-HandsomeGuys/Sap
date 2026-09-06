// @tags: interface, chunk, terrain, provider, tile
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 청크 정보를 제공하는 공급자 인터페이스.
/// InfinityMapManager 등에서 구현하여 의존성을 추상화함.
/// </summary>
public interface IChunkProvider
{
    /// <summary>
    /// 지정된 좌표의 청크를 반환합니다. 로드되지 않았으면 null을 반환합니다.
    /// </summary>
    TerrainChunk GetChunk(Vector2Int coord);

    /// <summary>
    /// 현재 활성화된 모든 청크를 반환합니다.
    /// </summary>
    IEnumerable<TerrainChunk> GetAllActiveChunks();
    
    /// <summary>
    /// 지정된 좌표의 청크가 로드되어 있는지 확인합니다.
    /// </summary>
    bool HasChunk(Vector2Int coord);
}
