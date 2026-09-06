using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;   // IJobParallelFor.Run(n) 확장 메서드
using UnityEngine;

/// <summary>
/// TerrainVisualJob 의 rim(최외곽 테두리) 판정 검증.
///
/// ⚠ 이 테스트는 rim 판정 로직만 본다. job safety 는 검증하지 못한다 —
///    .Run(n) 은 메인 스레드 순차 실행이라 parallel-for 인덱스 제약을 적용하지 않는다.
///
/// 설계: Assets/Docs/terrain-rim-outline.md
/// </summary>
public class TerrainVisualJobRimTests
{
    private const int W = 16;
    private const int H = 16;
    private const int T = 2;                       // rimThicknessPx
    private const int GATE = (T + 2) * 10;         // 40
    private const int EDGE_FALLBACK = T * 5;       // 10
    private const int SOLID_DIST = 100;            // 솔리드 픽셀의 기본 거리값

    private static readonly Color32 RIM   = new Color32(255, 0, 255, 255);
    private static readonly Color32 SOLID = new Color32(80, 60, 40, 255);
    private static readonly Color32 AIR   = new Color32(0, 0, 0, 0);

    /// <summary>전부 솔리드인 baseData. 호출자가 공기를 뚫는다.</summary>
    private static NativeArray<Color32> MakeSolidBase()
    {
        var a = new NativeArray<Color32>(W * H, Allocator.Temp);
        for (int i = 0; i < a.Length; i++) a[i] = SOLID;
        return a;
    }

    /// <summary>모든 픽셀 dist = value. 0이면 게이트가 항상 열린다.</summary>
    private static NativeArray<ushort> MakeDist(ushort value)
    {
        var a = new NativeArray<ushort>(W * H, Allocator.Temp);
        for (int i = 0; i < a.Length; i++) a[i] = value;
        return a;
    }

    /// <summary>rim 이 켜진 잡을 rect 전체(0,0)~(W,H)에 대해 실행하고 출력 텍스처를 돌려준다.</summary>
    private static NativeArray<Color32> RunJob(
        NativeArray<Color32> baseData,
        NativeArray<byte> pixelInfo,
        NativeArray<ushort> dist,
        int rimThicknessPx)
    {
        var output = new NativeArray<Color32>(W * H, Allocator.Temp);

        // borderData 는 길이 1짜리 더미. textureThicknessPx=0 이라 실제로 샘플되지 않는다.
        var borderData = new NativeArray<Color32>(1, Allocator.Temp);
        var emptySecondary = new NativeArray<Color32>(0, Allocator.Temp);

        new TerrainJobs.TerrainVisualJob
        {
            baseData = baseData,
            distanceField = dist,
            borderData = borderData,
            pixelInfo = pixelInfo,
            secondaryBorderData = emptySecondary,
            secondaryTileId = 0,
            secondaryBorderWidth = 0,
            secondaryBorderHeight = 0,
            outputTexture = output,
            width = W,
            height = H,
            textureThicknessPx = 0,          // borderTexture 분기 비활성 → rim 만 격리
            borderWidth = 1,
            borderHeight = 1,
            chunkOffsetX = 0,
            chunkOffsetY = 0,
            debugDistanceField = false,
            rectMinX = 0,
            rectMinY = 0,
            rectWidth = W,

            rimThicknessPx = rimThicknessPx,
            rimR2 = rimThicknessPx * rimThicknessPx,
            rimGateDist = (rimThicknessPx + 2) * 10,
            rimEdgeFallbackDist = rimThicknessPx * 5,
            rimColor = RIM,
        }.Run(W * H);

        borderData.Dispose();
        emptySecondary.Dispose();
        return output;
    }

    private static bool IsRim(NativeArray<Color32> output, int x, int y)
    {
        Color32 c = output[y * W + x];
        return c.r == RIM.r && c.g == RIM.g && c.b == RIM.b && c.a == RIM.a;
    }

    [Test]
    public void FlatSurface_RimIsExactlyTwoPixelsThick()
    {
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(0);

        // y >= 8 은 공기, y < 8 은 솔리드 → 표면은 y=7
        for (int y = 8; y < H; y++)
        for (int x = 0; x < W; x++)
            baseData[y * W + x] = AIR;

        var output = RunJob(baseData, pixelInfo, dist, T);

        // 내부 열(x=2..13)만 검사 — 가장자리 2px는 폴백 경로라 별도 테스트
        for (int x = T; x < W - T; x++)
        {
            Assert.IsTrue (IsRim(output, x, 7), $"x={x}, y=7 (공기까지 1px) 은 rim 이어야 한다");
            Assert.IsTrue (IsRim(output, x, 6), $"x={x}, y=6 (공기까지 2px) 은 rim 이어야 한다");
            Assert.IsFalse(IsRim(output, x, 5), $"x={x}, y=5 (공기까지 3px) 은 rim 이 아니어야 한다");
        }

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void DiscIsEuclidean_NotChebyshev()
    {
        // 이 테스트가 체비셰프(5×5 정사각) 구현을 잡아낸다.
        // 정사각형이면 (2,2) 오프셋도 rim 이 되어 45° 경사에서 두께가 2.8px 로 벌어진다.
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(0);

        baseData[8 * W + 8] = AIR;   // 공기 픽셀 딱 하나

        var output = RunJob(baseData, pixelInfo, dist, T);

        Assert.IsTrue (IsRim(output, 10,  8), "(2,0) 오프셋: 4 <= 4 → rim");
        Assert.IsTrue (IsRim(output,  9,  9), "(1,1) 오프셋: 2 <= 4 → rim");
        Assert.IsFalse(IsRim(output, 10, 10), "(2,2) 오프셋: 8 > 4 → rim 아님 (체비셰프면 여기서 실패한다)");
        Assert.IsFalse(IsRim(output, 11,  8), "(3,0) 오프셋: 9 > 4 → rim 아님");

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void CarvedObjectBoundaryMarker_IsNotRimSource()
    {
        // `pixelInfo & 128` 은 파괴 불가 마커가 아니다.
        // TerrainCarver 가 지형에 박아넣은 카빙 오브젝트(바위 스프라이트)의 실루엣 외곽선 마커다
        // (TerrainCarver.cs:91, :293 "Set Boundary Flag"). 진짜 파괴 불가는 별개 배열인
        // IndestructibleMask 이고 PixelInfo 를 건드리지 않는다.
        //
        // chamfer 는 이 비트를 거리 0 시드로 쓰지만 rim 은 쓰면 안 된다 — 쓰면 땅속에 완전히
        // 묻힌 바위마다 2px 단색 라인이 생겨 "공기와 맞닿는 곳"이라는 rim 정의가 깨진다.
        // 그 경계는 종전대로 borderTexture 가 그린다.
        var baseData  = MakeSolidBase();                              // 공기 전혀 없음
        var pixelInfo = new NativeArray<byte>(W * H, Allocator.Temp); // 전부 0
        var dist      = MakeDist(0);

        pixelInfo[8 * W + 8] = 128;   // 카빙 오브젝트 실루엣 마커

        var output = RunJob(baseData, pixelInfo, dist, T);

        // 공기가 하나도 없으므로 내부 픽셀에는 rim 이 하나도 없어야 한다.
        // (가장자리 t픽셀은 dist=0 폴백으로 rim 이 되므로 내부만 검사)
        for (int y = T; y < H - T; y++)
        for (int x = T; x < W - T; x++)
            Assert.IsFalse(IsRim(output, x, y),
                $"({x},{y}): 공기가 없는데 rim 이 생겼다 — pixelInfo & 128 을 rim 소스로 쓰고 있다");

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void ChunkEdge_UsesDistanceFieldFallback_NotDiscScan()
    {
        // 가장자리 t픽셀 이내는 원판이 OOB 로 나가므로 distanceField 로 판정한다.
        // baseData 에는 공기가 전혀 없다 → 원판 스캔이었다면 rim 이 하나도 안 나온다.
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(SOLID_DIST);   // 기본은 게이트 밖

        dist[3 * W + 0] = EDGE_FALLBACK;        // (0,3): 10 <= 10 → rim
        dist[3 * W + 1] = EDGE_FALLBACK + 10;   // (1,3): 20 >  10 → rim 아님 (게이트 40 은 통과)

        var output = RunJob(baseData, pixelInfo, dist, T);

        Assert.IsTrue (IsRim(output, 0, 3), "가장자리 픽셀 dist=10 → 폴백으로 rim");
        Assert.IsFalse(IsRim(output, 1, 3), "가장자리 픽셀 dist=20 → 폴백 임계값 초과 → rim 아님");

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void Gate_BlocksDiscScan_WhenDistanceIsLarge()
    {
        // 게이트가 실제로 작동하는지. 실제 거리장에서는 이런 상황이 생길 수 없다
        // (rim 픽셀의 업샘플 dist 상한 = 5t+7 = 17 << 40, 설계 §3-4 유도).
        // 순수하게 게이트 메커니즘만 검증하는 테스트다.
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(0);

        baseData[8 * W + 8] = AIR;
        dist[8 * W + 10] = GATE + 1;   // (10,8) 은 공기에서 2px 지만 게이트를 못 넘는다

        var output = RunJob(baseData, pixelInfo, dist, T);

        Assert.IsFalse(IsRim(output, 10, 8), "게이트를 못 넘으면 원판 스캔을 하지 않는다");
        Assert.IsTrue (IsRim(output,  6, 8), "게이트 안쪽(dist=0)의 같은 거리 픽셀은 정상적으로 rim");

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }

    [Test]
    public void RimThicknessZero_DisablesRimEntirely()
    {
        var baseData  = MakeSolidBase();
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);
        var dist      = MakeDist(0);

        baseData[8 * W + 8] = AIR;

        var output = RunJob(baseData, pixelInfo, dist, 0);   // rimThicknessPx = 0

        for (int i = 0; i < output.Length; i++)
        {
            Color32 c = output[i];
            bool isRimColor = c.r == RIM.r && c.g == RIM.g && c.b == RIM.b && c.a == RIM.a;
            Assert.IsFalse(isRimColor, $"rimThicknessPx=0 이면 rim 픽셀이 하나도 없어야 한다 (idx={i})");
        }

        baseData.Dispose(); pixelInfo.Dispose(); dist.Dispose(); output.Dispose();
    }
}
