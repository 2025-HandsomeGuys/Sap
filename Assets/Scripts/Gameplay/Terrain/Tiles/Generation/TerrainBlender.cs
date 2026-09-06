// @tags: generation, terrain, chunk, blending, layer
using UnityEngine;

public static class TerrainBlender
{
    /// <summary>
    /// 이 청크가 경계 청크인지 확인 (아래 청크와 타일 타입이 다른지)
    /// </summary>
    public static bool ShouldBlendLayer(Vector2Int coord, TileType currentType, bool enableLayerBlending, out TileType typeBelow)
    {
        typeBelow = currentType;
        if (!enableLayerBlending) return false;

        if (TileDataManager.Instance != null)
        {
            typeBelow = TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y - 1);
        }

        // 아래쪽 타일과만 블렌딩 (위쪽은 블렌딩 안 함으로 중복 방지)
        return currentType != typeBelow;
    }

    /// <summary>
    /// 경계 청크의 픽셀을 y 위치 기반 선형 그라데이션으로 혼합
    /// </summary>
    /// <param name="belowTypeId">
    /// 아래 층 PixelInfo ID. 아래 층 PixelInfo는 전체가 단일값이므로 배열 대신 값 하나만 받는다.
    /// [GC] 예전에는 호출측이 byte[width*height]를 이 값으로 가득 채워 넘겼다 (청크당 1MB 낭비).
    /// </param>
    public static Color32[] CreateBlendedPixels(
        Color32[]           sourcePixels,
        Color32[]           belowPixels,
        byte[]              pixelInfo,
        byte                belowTypeId,
        TileType            currentType,
        TileType            typeBelow,
        int                 width,
        int                 height,
        BlendingProfileData profile)
    {
        if (sourcePixels == null)
        {
            Debug.LogError("[TerrainBlender] Source pixels for blending are null!");
            return new Color32[width * height];
        }

        // [GC] Clone() 대신 재사용 버퍼로 복사 — 청크당 Color32[1M](4MB) 할당 제거.
        Color32[] blendedPixels = RentBlendBuffer(sourcePixels.Length);
        System.Array.Copy(sourcePixels, blendedPixels, sourcePixels.Length);

        if (currentType == typeBelow)
            return blendedPixels;

        if (belowPixels == null)
        {
            Debug.LogWarning($"[TerrainBlender] No cache for below type {typeBelow}");
            return blendedPixels;
        }

        ApplyGradientBlend(blendedPixels, belowPixels, pixelInfo, belowTypeId, width, height, profile);

        return blendedPixels;
    }

    // ============================================================================================================
    //  [GC] 블렌딩 결과 버퍼 재사용
    //
    //  안전 근거: 호출측(ChunkDataProvider.GenerateNewData)은 이 배열을 context.GroundPixels에 넣고,
    //  같은 동기 호출 안에서 TerrainChunk.Reuse_Step1_Prepare가 LoadPixelData(CopyFrom)로 복사한다.
    //  ⚠ 반환된 배열의 참조를 프레임을 넘겨 보관하는 코드를 추가하면 안 된다.
    // ============================================================================================================
    private static Color32[] s_blendBuffer;

    private static Color32[] RentBlendBuffer(int length)
    {
        if (s_blendBuffer == null || s_blendBuffer.Length != length)
            s_blendBuffer = new Color32[length];
        return s_blendBuffer;
    }

    // ============================================================================================================
    //  그라데이션 블렌딩
    // ============================================================================================================

    private static void ApplyGradientBlend(
        Color32[]           basePixels,
        Color32[]           otherPixels,
        byte[]              pixelInfo,
        byte                otherTypeId,
        int                 width,
        int                 height,
        BlendingProfileData profile)
    {
        int blendHeight = Mathf.RoundToInt(height * profile.blendHeightRatio);

        for (int y = 0; y < blendHeight; y++)
        {
            // y=0(하단 끝): t=1.0 → 아래 층 100%
            // y=blendHeight(블렌딩 구역 상단): t=0.0 → 위 층 100%
            float t = 1.0f - (float)y / blendHeight;

            for (int x = 0; x < width; x++)
            {
                int idx    = y * width + x;
                int otherY = height - 1 - y;   // 아래 청크의 상단 행과 대응
                int otherIdx = otherY * width + x;

                // 공기(파진 공간) 픽셀 스킵
                if (basePixels[idx].a == 0) continue;
                if (otherIdx < 0 || otherIdx >= otherPixels.Length) continue;
                if (otherPixels[otherIdx].a == 0) continue;

                basePixels[idx] = LerpColor(basePixels[idx], otherPixels[otherIdx], t);

                // PixelInfo: 하단 절반(t > 0.5)은 아래 층 재료로 판정
                if (pixelInfo != null && idx < pixelInfo.Length)
                {
                    if (t > 0.5f)
                        pixelInfo[idx] = otherTypeId;
                }
            }
        }
    }

    private static Color32 LerpColor(Color32 a, Color32 b, float t)
    {
        return new Color32(
            (byte)(a.r + (b.r - a.r) * t),
            (byte)(a.g + (b.g - a.g) * t),
            (byte)(a.b + (b.b - a.b) * t),
            a.a);
    }
}
