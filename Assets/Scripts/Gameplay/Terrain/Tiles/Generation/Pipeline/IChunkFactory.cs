// @tags: interface, pipeline, chunk, generation, factory
using UnityEngine;

/// <summary>
/// 청크 생성을 담당하는 팩토리 인터페이스
/// </summary>
public interface IChunkFactory
{
    /// <summary>
    /// 컨텍스트를 기반으로 적절한 청크를 생성하거나 반환합니다.
    /// </summary>
    IChunk CreateChunk(ChunkGenerationContext context);
}
