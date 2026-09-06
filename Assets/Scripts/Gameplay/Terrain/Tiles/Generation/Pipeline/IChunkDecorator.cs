// @tags: interface, decoration, pipeline, chunk, generation
/// <summary>
/// 청크에 장식 요소(바위, 광물, 구조물 등)를 생성하는 데코레이터 인터페이스
/// </summary>
public interface IChunkDecorator
{
    /// <summary>
    /// 청크에 장식을 수행합니다.
    /// </summary>
    /// <param name="chunk">대상 청크</param>
    /// <param name="context">장식 컨텍스트 (좌표, 시드, 점유 영역 등)</param>
    void Decorate(TerrainChunk chunk, DecorationContext context);
}
