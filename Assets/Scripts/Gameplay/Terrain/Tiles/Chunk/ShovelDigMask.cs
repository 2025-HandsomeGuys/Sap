// @tags: shovel, dig, mask, terrain, shape, static
using UnityEngine;

public static class ShovelDigMask
{
    public const byte AlphaThreshold = 10;

    private static bool[] s_bits;
    private static int s_width;
    private static int s_height;
    private static Vector2 s_scale = Vector2.one;
    private static float s_maxExtentMaskPx;

    // ★ [수정됨] 차징에 따른 축소 비율 (월드 단위/픽셀 단위 충돌 방지용)
    public static float CurrentShrinkRatio = 1f;

    public static bool IsActive => s_bits != null;

    public static void SetBits(bool[] bits, int w, int h, Vector2 scale)
    {
        if (bits == null || w <= 0 || h <= 0 || bits.Length != w * h || scale.x <= 0f || scale.y <= 0f)
        {
            Clear();
            return;
        }

        s_bits = bits;
        s_width = w;
        s_height = h;
        s_scale = scale;
        s_maxExtentMaskPx = MeasureMaxExtent(bits, w, h);
    }

    private static float MeasureMaxExtent(bool[] bits, int w, int h)
    {
        float cx = w * 0.5f;
        float cy = h * 0.5f;
        float maxSqr = 0f;

        for (int v = 0; v < h; v++)
        {
            float dy = (v + 0.5f) - cy;
            float dySqr = dy * dy;
            int row = v * w;

            for (int u = 0; u < w; u++)
            {
                if (!bits[row + u]) continue;

                float dx = (u + 0.5f) - cx;
                float distSqr = dx * dx + dySqr;
                if (distSqr > maxSqr) maxSqr = distSqr;
            }
        }

        float halfDiagonal = Mathf.Sqrt(w * (float)w + h * (float)h) * 0.5f;
        return Mathf.Min(Mathf.Sqrt(maxSqr), halfDiagonal);
    }

    public static void Clear()
    {
        s_bits = null;
        s_width = 0;
        s_height = 0;
        s_scale = Vector2.one;
        s_maxExtentMaskPx = 0f;
        CurrentShrinkRatio = 1f; // ★ [수정됨] 상태 초기화

        s_hasLogged = false;
        s_lastLoggedTexId = 0;
        s_lastLoggedOk = false;
    }

    private static int s_lastLoggedTexId;
    private static Vector2 s_lastLoggedScale;
    private static bool s_lastLoggedOk;
    private static bool s_hasLogged;

    public static int LogEmitCount { get; private set; }

    public static void Set(Texture2D tex, Vector2 scale)
    {
        int texId = tex != null ? tex.GetInstanceID() : 0;

        if (tex == null)
        {
            Clear();
            return;
        }

        if (scale.x <= 0f || scale.y <= 0f)
        {
            Clear();
            LogOnce(texId, scale, false, LogType.Warning,
                $"[ShovelDigMask] '{tex.name}' 배율이 {scale} → 타원 폴백. " +
                "shovelDigMaskScale은 0보다 커야 함");
            return;
        }

        if (!tex.isReadable)
        {
            Clear();
            LogOnce(texId, scale, false, LogType.Error,
                $"[ShovelDigMask] '{tex.name}' Read/Write Enabled가 꺼져 있음 → 타원 폴백. " +
                "Import Settings에서 켤 것");
            return;
        }

        int w = tex.width;
        int h = tex.height;
        Color32[] pixels = tex.GetPixels32();

        var bits = new bool[w * h];
        int opaque = 0;
        for (int i = 0; i < bits.Length; i++)
        {
            bool on = pixels[i].a > AlphaThreshold;
            bits[i] = on;
            if (on) opaque++;
        }

        if (opaque == 0)
        {
            Clear();
            LogOnce(texId, scale, false, LogType.Warning,
                $"[ShovelDigMask] '{tex.name}' {w}x{h}에 알파>{AlphaThreshold} 픽셀이 " +
                "하나도 없음 → 타원 폴백");
            return;
        }

        SetBits(bits, w, h, scale);

        float percent = opaque * 100f / bits.Length;
        LogOnce(texId, scale, true, LogType.Log,
            $"[ShovelDigMask] '{tex.name}' {w}x{h} 적용. " +
            $"불투명 {opaque}px ({percent:F1}%), 배율 {scale}");
    }

    private static void LogOnce(int texId, Vector2 scale, bool ok, LogType level, string message)
    {
        if (s_hasLogged &&
            s_lastLoggedTexId == texId &&
            s_lastLoggedScale == scale &&
            s_lastLoggedOk == ok)
        {
            return;
        }

        s_hasLogged = true;
        s_lastLoggedTexId = texId;
        s_lastLoggedScale = scale;
        s_lastLoggedOk = ok;
        LogEmitCount++;

        switch (level)
        {
            case LogType.Error: Debug.LogError(message); break;
            case LogType.Warning: Debug.LogWarning(message); break;
            default: Debug.Log(message); break;
        }
    }

    private static Vector2 PixelsPerMaskPixel(float radiusPx)
    {
        // ★ [수정됨] 차징으로 인해 줄어든 비율만큼 세로(Y) 픽셀을 다시 역으로 늘려서 최대 크기로 고정!
        float ratio = Mathf.Max(0.01f, CurrentShrinkRatio);
        float radiusX = radiusPx;
        float radiusY = radiusPx / ratio;

        float baseRatioX = radiusX * 2f / s_width;
        float baseRatioY = radiusY * 2f / s_width;

        return new Vector2(baseRatioX * s_scale.x, baseRatioY * s_scale.y);
    }

    public static float ExtentMultiplier
    {
        get
        {
            if (!IsActive) return 0f;
            float maxScale = Mathf.Max(s_scale.x, s_scale.y);
            return maxScale * 2f * s_maxExtentMaskPx / s_width;
        }
    }

    public static float BoundsRadiusPx(float radiusPx)
    {
        if (!IsActive) return 0f;

        // ★ [수정됨] 세로 길이가 복구(확장)되었으므로, 순회 반경도 안 잘리도록 늘어난 값 적용
        float ratio = Mathf.Max(0.01f, CurrentShrinkRatio);
        float maxR = Mathf.Max(radiusPx, radiusPx / ratio);
        return maxR * ExtentMultiplier;
    }

    public struct Sampler
    {
        internal bool[] Bits;
        internal int Width;
        internal int Height;
        internal Vector2 Inv;

        public bool Contains(float localX, float localY)
        {
            // ★ 좌우 반전(Mirroring)은 그대로 유지 (-localX)
            int u = Mathf.FloorToInt(-localX * Inv.x + Width * 0.5f);
            int v = Mathf.FloorToInt(localY * Inv.y + Height * 0.5f);

            if (u < 0 || u >= Width || v < 0 || v >= Height) return false;
            return Bits[v * Width + u];
        }
    }

    public static bool TryGetSampler(float radiusPx, out Sampler sampler)
    {
        sampler = default;
        if (!IsActive) return false;

        Vector2 pxPerMaskPx = PixelsPerMaskPixel(radiusPx);
        if (pxPerMaskPx.x <= 0f || pxPerMaskPx.y <= 0f) return false;

        sampler.Bits = s_bits;
        sampler.Width = s_width;
        sampler.Height = s_height;
        sampler.Inv = new Vector2(1f / pxPerMaskPx.x, 1f / pxPerMaskPx.y);
        return true;
    }
}