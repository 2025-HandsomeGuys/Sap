using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;   // IJobParallelFor.Run(n) 확장 메서드
using UnityEngine;

/// <summary>
/// rect-local dispatch 검증 — 잡이 rect 안만 쓰고 rect 밖은 건드리지 않는지.
/// 배경: Assets/Docs/job-pipeline-waste-removal.md §5.4
/// </summary>
public class TerrainJobsRectScopeTests
{
    private const int FULL_W = 16;
    private const int FULL_H = 16;
    private const int HALF_W = 8;
    private const int HALF_H = 8;
    private const int MAXD = 255;
    private const ushort SENTINEL = 12345;

    [Test]
    public void DownsampleMaskJob_WritesOnlyInsideRect()
    {
        var baseData  = new NativeArray<Color32>(FULL_W * FULL_H, Allocator.Temp);
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var baseHalf  = new NativeArray<Color32>(HALF_W * HALF_H, Allocator.Temp);
        var infoHalf  = new NativeArray<byte>(HALF_W * HALF_H, Allocator.Temp);

        // 전부 솔리드, 단 full (4,4)만 공기 → half (2,2)가 공기가 되어야 한다
        for (int i = 0; i < baseData.Length; i++) baseData[i] = new Color32(0, 0, 0, 255);
        baseData[4 * FULL_W + 4] = new Color32(0, 0, 0, 0);

        // half 전체를 구분 가능한 값으로 미리 채움 → rect 밖이 보존되는지 확인용
        for (int i = 0; i < baseHalf.Length; i++) baseHalf[i] = new Color32(9, 9, 9, 99);

        // half rect = (1,1)~(4,4)
        new TerrainJobs.DownsampleMaskJob
        {
            baseData = baseData, pixelInfo = pixelInfo,
            baseHalf = baseHalf, pixelInfoHalf = infoHalf,
            fullWidth = FULL_W, fullHeight = FULL_H, halfWidth = HALF_W,
            rectMinX = 1, rectMinY = 1, rectWidth = 3
        }.Run(3 * 3);

        // rect 안: half(2,2)는 공기(alpha 0)
        Assert.AreEqual(0, baseHalf[2 * HALF_W + 2].a, "rect 안 half(2,2)는 공기여야 한다");
        // rect 안: half(1,1)은 솔리드(alpha 255)
        Assert.AreEqual(255, baseHalf[1 * HALF_W + 1].a, "rect 안 half(1,1)은 솔리드여야 한다");
        // rect 밖: 미리 채운 sentinel이 그대로
        Assert.AreEqual(99, baseHalf[0 * HALF_W + 0].a, "rect 밖 half(0,0)은 보존돼야 한다");
        Assert.AreEqual(99, baseHalf[7 * HALF_W + 7].a, "rect 밖 half(7,7)은 보존돼야 한다");

        baseData.Dispose(); pixelInfo.Dispose(); baseHalf.Dispose(); infoHalf.Dispose();
    }

    [Test]
    public void InitDistanceFieldJob_ResetsDirtyRect_PreservesSolidOutsideDirty_SkipsOutsideExt()
    {
        var baseData  = new NativeArray<Color32>(HALF_W * HALF_H, Allocator.Temp);
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var df        = new NativeArray<ushort>(HALF_W * HALF_H, Allocator.Temp);

        for (int i = 0; i < baseData.Length; i++) baseData[i] = new Color32(0, 0, 0, 255);
        baseData[3 * HALF_W + 3] = new Color32(0, 0, 0, 0); // (3,3) 공기

        for (int i = 0; i < df.Length; i++) df[i] = SENTINEL;

        // ext = (1,1)~(6,6), dirty = (2,2)~(5,5)
        new TerrainJobs.InitDistanceFieldJob
        {
            baseData = baseData, pixelInfo = pixelInfo, distanceField = df,
            maxDist = MAXD,
            dirtyMinX = 2, dirtyMinY = 2, dirtyMaxX = 5, dirtyMaxY = 5,
            extMinX = 1, extMinY = 1, extWidth = 5,
            width = HALF_W
        }.Run(5 * 5);

        // dirty 안 솔리드 → maxDist로 리셋
        Assert.AreEqual(MAXD, df[2 * HALF_W + 2], "dirty 안 솔리드는 maxDist로 리셋");
        // dirty 안 공기 → 0
        Assert.AreEqual(0, df[3 * HALF_W + 3], "dirty 안 공기는 0");
        // ext 안 / dirty 밖 솔리드 → 기존 값(sentinel) 보존
        Assert.AreEqual(SENTINEL, df[1 * HALF_W + 1], "ext 안·dirty 밖 솔리드는 기존 값 보존");
        // ext 밖 → 아예 안 건드림
        Assert.AreEqual(SENTINEL, df[0 * HALF_W + 0], "ext 밖은 dispatch되지 않아야 한다");
        Assert.AreEqual(SENTINEL, df[7 * HALF_W + 7], "ext 밖은 dispatch되지 않아야 한다");

        baseData.Dispose(); pixelInfo.Dispose(); df.Dispose();
    }

    [Test]
    public void UpsampleDistanceJob_DoublesHalfValue_OnlyInsideRect()
    {
        var halfField = new NativeArray<ushort>(HALF_W * HALF_H, Allocator.Temp);
        var fullField = new NativeArray<ushort>(FULL_W * FULL_H, Allocator.Temp);

        for (int i = 0; i < halfField.Length; i++) halfField[i] = 10;
        for (int i = 0; i < fullField.Length; i++) fullField[i] = SENTINEL;

        // full rect = (4,4)~(8,8)
        new TerrainJobs.UpsampleDistanceJob
        {
            halfField = halfField, fullField = fullField,
            fullWidth = FULL_W, halfWidth = HALF_W, maxDist = MAXD,
            rectMinX = 4, rectMinY = 4, rectWidth = 4
        }.Run(4 * 4);

        Assert.AreEqual(20, fullField[4 * FULL_W + 4], "rect 안은 half 값 ×2");
        Assert.AreEqual(20, fullField[7 * FULL_W + 7], "rect 안은 half 값 ×2");
        Assert.AreEqual(SENTINEL, fullField[3 * FULL_W + 3], "rect 밖은 보존");
        Assert.AreEqual(SENTINEL, fullField[8 * FULL_W + 8], "rect 밖은 보존");

        halfField.Dispose(); fullField.Dispose();
    }

    [Test]
    public void UpsampleDistanceJob_ClampsToMaxDist()
    {
        var halfField = new NativeArray<ushort>(HALF_W * HALF_H, Allocator.Temp);
        var fullField = new NativeArray<ushort>(FULL_W * FULL_H, Allocator.Temp);

        for (int i = 0; i < halfField.Length; i++) halfField[i] = 200; // ×2 = 400 > 255

        new TerrainJobs.UpsampleDistanceJob
        {
            halfField = halfField, fullField = fullField,
            fullWidth = FULL_W, halfWidth = HALF_W, maxDist = MAXD,
            rectMinX = 0, rectMinY = 0, rectWidth = FULL_W
        }.Run(FULL_W * FULL_H);

        Assert.AreEqual(MAXD, fullField[0], "maxDist로 클램프돼야 한다");

        halfField.Dispose(); fullField.Dispose();
    }
}
