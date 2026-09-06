// @tags: test, terrain, dig, pixel-count, telemetry, editmode

using NUnit.Framework;
using UnityEngine;

public class TerrainDigPixelCountTests
{
    private const int Size = 64;
    private const float Ppu = 100f;

    /// <summary>월드 좌표 → 픽셀 좌표. PPU만 곱하는 단순 변환.</summary>
    private static Vector2Int ToPixel(Vector2 world)
        => new Vector2Int(Mathf.RoundToInt(world.x * Ppu), Mathf.RoundToInt(world.y * Ppu));

    private static ChunkData MakeSolidChunk()
    {
        var data = new ChunkData(Size, Size);
        for (int i = 0; i < Size * Size; i++)
        {
            data.BasePixels[i] = new Color32(120, 90, 60, 255);
            data.PixelInfo[i] = 1; // 1 = Dirt
        }
        return data;
    }

    private static int CountTransparent(ChunkData data)
    {
        int n = 0;
        for (int i = 0; i < Size * Size; i++)
            if (data.BasePixels[i].a == 0) n++;
        return n;
    }

    [Test]
    public void RemovedPixels가_실제로_지워진_픽셀_수와_같다()
    {
        var data = MakeSolidChunk();
        // VerticalScale=1 → 타원이 아니라 원. 기하가 단순해져 판정이 흔들리지 않는다.
        var modifier = new TerrainModifier(Ppu, 1f);

        // player (0.32,0.32) → 픽셀 (32,32) / mouse (0.42,0.32) → 픽셀 (42,32)
        // 파기 중심은 반경 5px 원으로 청크 안쪽에 완전히 들어온다.
        var result = modifier.Dig(
            data,
            new Vector2(0.42f, 0.32f),
            new Vector2(0.32f, 0.32f),
            radius: 0.05f,
            toolIndex: 0,
            ToPixel);

        Assert.IsTrue(result.WasModified, "고체 지형을 팠는데 WasModified가 false다");
        Assert.Greater(result.RemovedPixels, 0);
        Assert.AreEqual(CountTransparent(data), result.RemovedPixels);

        data.Dispose();
    }

    [Test]
    public void 이미_비어_있으면_RemovedPixels는_0이고_WasModified도_false다()
    {
        var data = new ChunkData(Size, Size); // 전부 투명(알파 0)으로 시작
        var modifier = new TerrainModifier(Ppu, 1f);

        var result = modifier.Dig(
            data,
            new Vector2(0.42f, 0.32f),
            new Vector2(0.32f, 0.32f),
            radius: 0.05f,
            toolIndex: 0,
            ToPixel);

        Assert.AreEqual(0, result.RemovedPixels);
        Assert.IsFalse(result.WasModified, "빈 공간을 팠는데 WasModified가 true다 — 기존 동작이 깨졌다");

        data.Dispose();
    }

    [Test]
    public void 같은_자리를_다시_파면_RemovedPixels는_0이고_WasModified도_false다()
    {
        var data = MakeSolidChunk();
        var modifier = new TerrainModifier(Ppu, 1f);

        var mouse = new Vector2(0.42f, 0.32f);
        var player = new Vector2(0.32f, 0.32f);

        // 1회차: 중심 픽셀 (42,32), 반경 5px. 실제로 뭔가 지워졌는지 먼저 확인(선행 조건).
        var first = modifier.Dig(data, mouse, player, radius: 0.05f, toolIndex: 0, ToPixel);
        Assert.Greater(first.RemovedPixels, 0, "선행 조건: 1회차 파기가 실제로 뭔가 지웠어야 한다");

        // 2회차: 완전히 같은 좌표로 다시 판다 — 전부 이미 공기다.
        var second = modifier.Dig(data, mouse, player, radius: 0.05f, toolIndex: 0, ToPixel);

        Assert.AreEqual(0, second.RemovedPixels, "이미 판 자리를 또 팠는데 RemovedPixels가 0이 아니다 — '이미 공기' 가드 우회 회귀");
        Assert.IsFalse(second.WasModified, "이미 판 자리를 또 팠는데 WasModified가 true다 — 헛스윙에 스태미나가 물리는 회귀");

        data.Dispose();
    }

    [Test]
    public void 일부만_겹치게_다시_파면_RemovedPixels는_새로_투명해진_픽셀_수와_같다()
    {
        var data = MakeSolidChunk();
        var modifier = new TerrainModifier(Ppu, 1f);

        // 1차: 중심 픽셀 (42,32), 반경 5px.
        modifier.Dig(data, new Vector2(0.42f, 0.32f), new Vector2(0.32f, 0.32f),
            radius: 0.05f, toolIndex: 0, ToPixel);

        int transparentBefore = CountTransparent(data);

        // 2차: 중심 픽셀 (45,32), 반경 5px. 중심 간 거리 3px < 반지름 합(10px) → 부분 중첩.
        // raw bounds = center ± (ceil(radiusPx)+DIG_MARGIN_EXTRA) = 45 ± (5+5=10) → X:[35,55], Y:[22,42].
        // 전부 [0,64] 안쪽이라 Mathf.Clamp가 실제로 걸리지 않는다(clamp 후 값과 raw 값이 동일).
        var second = modifier.Dig(data, new Vector2(0.45f, 0.32f), new Vector2(0.35f, 0.32f),
            radius: 0.05f, toolIndex: 0, ToPixel);

        int transparentAfter = CountTransparent(data);

        Assert.Greater(second.RemovedPixels, 0, "부분 중첩인데 새로 지운 픽셀이 0이면 좌표 선택이 잘못됐다");
        Assert.AreEqual(transparentAfter - transparentBefore, second.RemovedPixels,
            "RemovedPixels가 실제로 새로 투명해진 픽셀 수(델타)와 다르다 — 이미 공기였던 픽셀을 또 세고 있을 수 있다");

        data.Dispose();
    }
}
