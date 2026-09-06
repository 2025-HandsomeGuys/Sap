// @tags: chunk, image, painter, hook, protected-coord, reload
using UnityEngine;

/// <summary>
/// 보호 좌표(작가가 PNG로 픽셀을 덮어쓰는 청크)의 "로드 후 처리" 훅.
///
/// 왜 훅이 필요한가:
/// Phase 2 데코레이션은 청크의 자식(ROCK_/MINERAL_)을 전부 풀 반납한 뒤 데코레이터를 돌린다.
/// 페인팅·광물 생성을 그보다 먼저 하면 방금 만든 광물이 그 정리 루프에 쓸려나가고,
/// 반대로 폴링으로 "청크가 생겼다"를 감지하면 Phase 1(레지스트리 등록)과 Phase 2 사이에
/// 끼어들어 같은 문제가 생긴다. Phase 2 끝에서 파이프라인이 직접 불러줘야 순서가 확정된다.
///
/// 등록: SpecialChunkManager.RegisterPostLoadPainter
/// 호출: ChunkGenerationPipeline Phase 2 (데코레이터 루프 직후) — 첫 로드·재로드 모두
/// </summary>
public interface IChunkPostLoadPainter
{
    /// <summary>청크가 로드되고 데코레이션이 끝난 직후 호출된다. (매 로드마다)</summary>
    void OnChunkLoaded(TerrainChunk chunk, Vector2Int coord);
}
