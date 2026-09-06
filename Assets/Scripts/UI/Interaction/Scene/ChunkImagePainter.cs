// @tags: chunk, image, painter, terrain, override
using UnityEngine;

/// <summary>
/// PNG 이미지로 청크의 픽셀 데이터를 통째로 교체하는 공용 유틸.
/// 투명(alpha ≤ 10) = 빈 공간, 불투명 = 땅.
///
/// 호출측 책임 (여기 포함하지 않음):
/// - 청크 로드 대기 (InfinityMapManager.HasChunk)
/// - 보호 좌표 등록 (SpecialChunkManager.RegisterProtectedCoord)
/// - 페인팅 이후의 게임 흐름 (플레이어 활성화 등)
/// </summary>
public static class ChunkImagePainter
{
    /// <summary>불투명 판정 기준 — 이 값보다 큰 alpha를 땅으로 취급한다.</summary>
    private const byte SOLID_ALPHA_THRESHOLD = 10;

    /// <summary>
    /// 청크 픽셀을 terrain 이미지로 교체한다.
    /// </summary>
    /// <param name="border">null이면 청크가 이미 가진 BorderData를 유지한다.</param>
    /// <returns>교체 성공 시 true. 인자가 유효하지 않으면 에러 로그 후 false.</returns>
    public static bool Paint(TerrainChunk chunk, Texture2D terrain, Texture2D border)
    {
        if (chunk == null)
        {
            Debug.LogError("[ChunkImagePainter] chunk가 null.");
            return false;
        }
        if (terrain == null)
        {
            Debug.LogError("[ChunkImagePainter] terrain 이미지가 null! Inspector에서 PNG를 할당했는지 확인.");
            return false;
        }
        if (!terrain.isReadable)
        {
            Debug.LogError($"[ChunkImagePainter] '{terrain.name}' — Read/Write Enabled가 꺼져 있음! Import Settings에서 켜야 함.");
            return false;
        }

        chunk.EnsureJobsCompleted();

        Color32[] pixels = ResampleToChunk(terrain, chunk.width, chunk.height);

        // 픽셀 alpha → PixelInfo(solid/air 플래그) 생성.
        // null로 넘기면 기존 PixelInfo가 유지되어 투명 영역도 solid로 남는다.
        byte[] pixelInfo = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            pixelInfo[i] = (byte)(pixels[i].a > SOLID_ALPHA_THRESHOLD ? 1 : 0);

        // 테두리 텍스처: 할당됐으면 적용, 없으면 기존 BorderData 유지
        Color32[] borderPixels = null;
        int borderW = 0, borderH = 0;
        if (border != null)
        {
            if (!border.isReadable)
                Debug.LogError($"[ChunkImagePainter] '{border.name}' — Border 텍스처의 Read/Write Enabled가 꺼져 있음! 기존 테두리를 유지한다.");
            else
            {
                borderPixels = border.GetPixels32();
                borderW = border.width;
                borderH = border.height;
            }
        }

        chunk.LoadChunkData(pixels, pixelInfo, borderPixels, borderW, borderH);

        // CLAUDE.md 아키텍처 제약 3 — 더티 플래그는 ChunkData 메서드로 세팅한다.
        // (비주얼 + 콜라이더 + 저장 대상 표시)
        chunk.GetData().MarkDirty();

        // 이 경로는 매니저의 MarkChunkDirty를 타지 않으므로 지도 스냅샷을 직접 무효화한다.
        // 빠뜨리면 미니맵·전체지도가 교체 전 지형을 최대 수 초간 계속 그린다.
        MapTerrainCache.InvalidateAll(chunk);

        // 콜라이더를 스로틀 무시하고 즉시 재생성한다.
        //
        // 이걸 안 하면 TryUpdateCollider()의 colliderUpdateInterval(0.2초) 스로틀에 걸려
        // 최대 12프레임 동안 "픽셀은 방인데 콜라이더는 절차 생성된 꽉 찬 지형"인 상태가 남는다.
        // 청크 로드 직후 Reuse_Step2_Finalize()가 방금 콜라이더를 만들면서
        // _lastColliderUpdateTime을 갱신해 두기 때문에 스로틀 대기가 꼬박 걸린다.
        //
        // 그 사이에 플레이어를 Dynamic으로 풀면(PlayerSpawner·ElevatorManager 모두 1프레임만 대기)
        // 꽉 찬 콜라이더 안에서 물리가 켜져 depenetration으로 지형 아래까지 튕겨나간다.
        chunk.ForceUpdateCollider();
        return true;
    }

    /// <summary>이미지가 청크 해상도와 다르면 최근접 샘플링으로 맞춘다.</summary>
    private static Color32[] ResampleToChunk(Texture2D src, int chunkW, int chunkH)
    {
        Color32[] pixels = src.GetPixels32();
        if (pixels.Length == chunkW * chunkH) return pixels;

        Debug.LogWarning($"[ChunkImagePainter] 픽셀 수 불일치 — 이미지 {src.width}x{src.height} → 청크 {chunkW}x{chunkH} 리샘플링.");

        Color32[] resampled = new Color32[chunkW * chunkH];
        float scaleX = (float)src.width  / chunkW;
        float scaleY = (float)src.height / chunkH;
        for (int y = 0; y < chunkH; y++)
        {
            int srcY = Mathf.Clamp(Mathf.RoundToInt(y * scaleY), 0, src.height - 1);
            int dstRow = y * chunkW;
            int srcRow = srcY * src.width;
            for (int x = 0; x < chunkW; x++)
            {
                int srcX = Mathf.Clamp(Mathf.RoundToInt(x * scaleX), 0, src.width - 1);
                resampled[dstRow + x] = pixels[srcRow + srcX];
            }
        }
        return resampled;
    }
}
