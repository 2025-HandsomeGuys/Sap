using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스프라이트 알파를 모양으로 삼아, 지형과 동일한 테두리 타일 + rim 을 구운 새 Sprite 를 반환한다.
/// 정적 오브젝트 전용 — 메인스레드 원샷 동기 실행 후 즉시 리소스 해제.
/// 기존 지형 파이프라인(ChunkData + TerrainVisualizer + TerrainVisualJob)을 재사용한다.
/// 설계: Assets/Docs/sprite-terrain-border/design.md
/// </summary>
public static class SpriteBorderBaker
{
    public struct Settings
    {
        public float textureThickness;   // 테두리 두께(유닛). 지형과 동일하게 기본 4
        public int borderPixelsPerUnit;  // 두께→픽셀 변환 PPU. 지형=100
    }

    /// <summary>
    /// src 의 스프라이트 rect 알파를 모양으로 테두리를 구운 새 Sprite 반환.
    /// src 또는 borderTex 가 null 이거나 rect 가 비면 src 를 그대로 반환한다.
    /// src.texture / borderTex 는 Read/Write Enabled 여야 한다 — 아니면 GetPixels 에서 예외.
    /// </summary>
    public static Sprite Bake(Sprite src, Texture2D borderTex, Settings s)
    {
        if (src == null || borderTex == null) return src;

        Rect r = src.textureRect;
        int w = Mathf.RoundToInt(r.width);
        int h = Mathf.RoundToInt(r.height);
        if (w <= 0 || h <= 0) return src;

        // 1. 스프라이트 rect 픽셀 추출 (아틀라스 서브렉트 대응). Read/Write 필요.
        Color[] block = src.texture.GetPixels((int)r.x, (int)r.y, w, h);
        var pixels = new Color32[block.Length];
        for (int i = 0; i < block.Length; i++) pixels[i] = block[i];

        // 2. ChunkData 구성 — 알파가 점유, PixelInfo 는 알파에서 자동 생성
        //    이 지점부터 Allocator.Persistent 네이티브 메모리가 잡히므로, 이후 예외가 나도
        //    반드시 Dispose 되도록 try/finally 로 감싼다.
        var data = new ChunkData(w, h);
        TerrainVisualizer vis = null;
        try
        {
            data.LoadPixelData(pixels);
            data.LoadPixelInfo(null);

            // 3. 테두리 아틀라스
            data.SetBorderData(borderTex.GetPixels32(), borderTex.width, borderTex.height);

            // 4. 출력 텍스처
            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = src.texture.filterMode,
                wrapMode = TextureWrapMode.Clamp,
            };

            // 5~6. 파이프라인 원샷 (Downsample→Init→Chamfer→Upsample→VisualJob→Apply)
            //      rim 은 TerrainChunk static 을 그대로 사용. SpriteRenderer 인자는 job 경로에서 미사용 → null.
            vis = new TerrainVisualizer(data, outTex, null);
            vis.TextureThickness = s.textureThickness;
            vis.PixelsPerUnit = s.borderPixelsPerUnit;
            vis.UpdateVisualsFull(0, 0);
            vis.ApplyTextureSync();

            // 7. 새 Sprite — 원본 월드 크기 유지를 위해 src 의 pivot/PPU 사용
            //    테두리는 솔리드 안쪽으로만 그려져 알파(모양)가 그대로다. 하지만 Sprite.Create 는
            //    physics shape 를 기본값(사각 quad)으로 만들므로, PolygonCollider2D 를 Reset 하면
            //    원본 아트가 아닌 사각형으로 재생성돼 보이는 모양과 어긋난다.
            //    → 원본의 physics shape 를 그대로 옮겨 콜라이더가 원본 폴리곤을 유지하게 한다.
            Vector2 pivotNorm = new Vector2(src.pivot.x / w, src.pivot.y / h);
            var baked = Sprite.Create(outTex, new Rect(0, 0, w, h), pivotNorm, src.pixelsPerUnit);
            CopyPhysicsShape(src, baked);
            return baked;
        }
        finally
        {
            // 8. 정리 — outTex 는 반환된 Sprite 가 참조하므로 절대 Dispose 하지 않는다.
            vis?.Dispose();
            data.Dispose();
        }
    }

    /// <summary>
    /// 원본 스프라이트의 physics shape(콜라이더 외곽선)를 구운 스프라이트로 복사한다.
    /// pivot/PPU/rect 크기가 동일하므로 좌표 변환 없이 그대로 옮길 수 있다.
    /// 원본에 커스텀 shape 가 없으면 알파 기반 자동 tight shape 가 넘어온다(둘 다 모양이 같다).
    /// </summary>
    private static void CopyPhysicsShape(Sprite from, Sprite to)
    {
        int count = from.GetPhysicsShapeCount();
        if (count <= 0) return;

        var shapes = new List<Vector2[]>(count);
        var buf = new List<Vector2>();
        for (int i = 0; i < count; i++)
        {
            buf.Clear();
            from.GetPhysicsShape(i, buf);
            shapes.Add(buf.ToArray());
        }
        to.OverridePhysicsShape(shapes);
    }
}
