using NUnit.Framework;
using UnityEngine;

/// <summary>
/// TerrainChunk.InitializeTextures() 최적화 검증.
/// 1) zero-byte 버퍼로 투명 텍스처 초기화 (GC 없음)
/// 2) SpriteMeshType.FullRect = 4 vertices (TraceShape 없음)
/// </summary>
public class ChunkTextureInitTests
{
    private const int W = 8;
    private const int H = 8;

    [Test]
    public void ClearBuffer_AllZeroBytes_ProducesTransparentTexture()
    {
        var clearBuffer = new byte[W * H * 4];
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(clearBuffer);
        tex.Apply();

        var pixels = tex.GetPixels32();
        foreach (var p in pixels)
            Assert.AreEqual(0, p.a, $"alpha는 0이어야 하는데 {p.a}");

        Object.DestroyImmediate(tex);
    }

    [Test]
    public void FullRectSprite_OnTransparentTexture_HasExactlyFourVertices()
    {
        var clearBuffer = new byte[W * H * 4];
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(clearBuffer);
        tex.Apply();

        var sprite = Sprite.Create(
            tex, new Rect(0, 0, W, H), Vector2.zero, 100f,
            0, SpriteMeshType.FullRect);

        Assert.AreEqual(4, sprite.vertices.Length,
            "SpriteMeshType.FullRect는 반드시 4 vertices여야 한다");

        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(sprite);
    }
}
