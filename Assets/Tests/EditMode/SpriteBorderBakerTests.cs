using NUnit.Framework;
using UnityEngine;

public class SpriteBorderBakerTests
{
    // 중앙 정사각형 + 투명 여백을 가진 읽기 가능한 스프라이트 생성
    private static Sprite MakeSquareSprite(int size, int margin, Color32 fill)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        var clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool solid = x >= margin && x < size - margin &&
                         y >= margin && y < size - margin;
            px[y * size + x] = solid ? fill : clear;
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // 단색 테두리 아틀라스 (모든 픽셀 동일 색)
    private static Texture2D MakeBorderTex(int w, int h, Color32 c)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private static Color32 PixelAt(Sprite s, int x, int y)
    {
        return s.texture.GetPixels32()[y * s.texture.width + x];
    }

    [Test]
    public void Bake_AirStaysTransparent()
    {
        var src = MakeSquareSprite(24, 6, new Color32(100, 120, 140, 255));
        var border = MakeBorderTex(8, 8, new Color32(0, 0, 255, 255));

        var baked = SpriteBorderBaker.Bake(src, border,
            new SpriteBorderBaker.Settings { textureThickness = 2f, borderPixelsPerUnit = 100 });

        Assert.AreEqual(0, PixelAt(baked, 0, 0).a, "여백(공기) 픽셀은 투명이어야 한다");
    }

    [Test]
    public void Bake_InteriorKeepsOriginalColor()
    {
        var fill = new Color32(100, 120, 140, 255);
        // textureThickness=2f, borderPixelsPerUnit=100 → texPx = Clamp(RoundToInt(2*100),1,50) = 50,
        // 즉 dist(공기로부터의 chamfer 거리, ~5/px) <= 250 인 픽셀까지 테두리로 덧칠된다.
        // 정중앙이 이 임계값(250, 실제 픽셀 ~50px)보다 확실히 멀어야 원색이 보존되므로
        // 180×20 (내부 140×140, 중심까지 ~70px, dist ~350 > 250) 크기를 사용한다.
        var src = MakeSquareSprite(180, 20, fill);
        var border = MakeBorderTex(8, 8, new Color32(0, 0, 255, 255));

        var baked = SpriteBorderBaker.Bake(src, border,
            new SpriteBorderBaker.Settings { textureThickness = 2f, borderPixelsPerUnit = 100 });

        Color32 c = PixelAt(baked, 90, 90);   // 정사각형 정중앙 = 가장자리에서 가장 먼 곳
        Assert.AreEqual(fill.r, c.r);
        Assert.AreEqual(fill.g, c.g);
        Assert.AreEqual(fill.b, c.b);
        Assert.AreEqual(255, c.a);
    }

    [Test]
    public void Bake_EdgeGetsBorderColor()
    {
        var fill = new Color32(100, 120, 140, 255);
        var src = MakeSquareSprite(24, 6, fill);
        var borderColor = new Color32(0, 0, 255, 255);
        var border = MakeBorderTex(8, 8, borderColor);

        var baked = SpriteBorderBaker.Bake(src, border,
            new SpriteBorderBaker.Settings { textureThickness = 2f, borderPixelsPerUnit = 100 });

        // 정사각형 최상단 솔리드 행(y = size-margin-1 = 17)의 중앙 픽셀은 공기와 인접 → 원래색이 아니어야 한다
        Color32 c = PixelAt(baked, 12, 17);
        bool changed = c.r != fill.r || c.g != fill.g || c.b != fill.b;
        Assert.IsTrue(changed, "가장자리 솔리드 픽셀은 테두리/rim 이 덧그려져 원래색과 달라야 한다");
        Assert.AreEqual(255, c.a);
    }
}
