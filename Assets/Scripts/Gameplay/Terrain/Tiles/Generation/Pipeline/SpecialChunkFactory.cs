// @tags: pipeline, special-chunk, factory, spawn, chunk, save, restore
using UnityEngine;

/// <summary>
/// 스페셜 청크(미리 만들어진 프리팹 인스턴스)를 반환하는 팩토리
/// </summary>
public class SpecialChunkFactory : IChunkFactory
{
    private IChunkProvider _chunkProvider;
    private Transform _player;

    public SpecialChunkFactory(IChunkProvider provider, Transform player)
    {
        _chunkProvider = provider;
        _player = player;
    }

    public IChunk CreateChunk(ChunkGenerationContext context)
    {
        // 스페셜 청크는 이미 Context에 인스턴스가 할당되어 있음
        if (context.IsSpecialChunk && context.SpecialChunkInstance != null)
        {
            var chunk = context.SpecialChunkInstance;
            
            // TerrainChunk인 경우에만 기존 주입 로직 실행
            if (chunk is TerrainChunk tChunk)
            {
                tChunk.SetChunkProvider(_chunkProvider);
                tChunk.player = _player;
                // [Fix] Border 데이터 적용 — 특수청크는 Reuse_Step1_Prepare를 거치지 않으므로 직접 주입
                tChunk.ApplyBorderDataOnly(
                    context.BorderPixels, context.BorderWidth, context.BorderHeight,
                    context.SecondaryBorderPixels, context.SecondaryBorderWidth, context.SecondaryBorderHeight,
                    context.SecondaryTileId);
                // [Fix] IChunkInitializer.Initialize() 완료 후 저장된 파진 상태 덮어쓰기
                // IndestructibleMask는 Initialize()에서 이미 세팅됐으므로 그대로 유지됨
                if (context.SavedData?.modifiedPixels != null)
                    tChunk.RestoreSavedPixels(context.SavedData.modifiedPixels, context.SavedData.pixelInfo);
            }
            
            return chunk;
        }

        Debug.LogError($"[SpecialChunkFactory] Failed to create chunk for {context.Coord}. Context says SpecialVal={context.IsSpecialChunk}, but Instance is null.");
        return null;
    }
}
