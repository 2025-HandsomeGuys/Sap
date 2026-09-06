using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;   // IJob.Run() / IJobParallelFor.Run(n) 확장 메서드
using UnityEngine;

/// <summary>
/// 설계 §3.1 증명 — Round2의 InitDistanceFieldJob(리셋)을 제거해도 Chamfer 결과가 동일한지.
///
/// Chamfer 2패스는 min-전파다. 시작값이 참값의 상한(upper bound)이면
/// 리셋 여부와 무관하게 같은 고정점으로 수렴해야 한다.
/// 이 테스트가 실패하면 Task 5(Round2 Init 제거)를 진행하면 안 된다.
///
/// 배경: Assets/Docs/job-pipeline-waste-removal.md
/// </summary>
public class ChamferConvergenceTests
{
    private const int W = 16;
    private const int H = 16;
    private const int MAXD = 255;

    [Test]
    public void Chamfer_FromConvergedUpperBound_EqualsFromMaxDistReset()
    {
        // 청크 내부에 공기 한 점 (파낸 구멍)
        var air = new bool[W * H];
        air[5 * H + 5] = true;

        var baseData = MakeBase(air);
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp); // 잡은 Length>0일 때만 읽는다

        NativeArray<ushort> dfRound1 = default, dfB = default, dfC = default;
        try
        {
            // ── Round1: maxDist 리셋 → chamfer. 결과 = "이웃 영향 없는 자기 청크만의 DF" (= 참값의 상한)
            dfRound1 = MakeResetField(air);
            RunChamfer(dfRound1, baseData, pixelInfo);

            // ── Path B (개선안): Round1 결과 위에 경계 시드를 min-merge → chamfer. Init 리셋 없음.
            dfB = new NativeArray<ushort>(W * H, Allocator.Temp);
            dfRound1.CopyTo(dfB);
            ApplyBoundarySeed(dfB);
            RunChamfer(dfB, baseData, pixelInfo);

            // ── Path C (현행): maxDist 리셋 → 경계 시드 → chamfer.
            dfC = MakeResetField(air);
            ApplyBoundarySeed(dfC);
            RunChamfer(dfC, baseData, pixelInfo);

            for (int i = 0; i < W * H; i++)
                Assert.AreEqual(dfC[i], dfB[i], $"불일치 index={i} (x={i % W}, y={i / W})");
        }
        finally
        {
            if (dfRound1.IsCreated) dfRound1.Dispose();
            if (dfB.IsCreated) dfB.Dispose();
            if (dfC.IsCreated) dfC.Dispose();
            baseData.Dispose();
            pixelInfo.Dispose();
        }
    }

    [Test]
    public void Chamfer_FromConvergedUpperBound_EqualsReset_WhenNeighborSeedIsHigher()
    {
        // 경계 시드가 "실거리보다는 크지만 maxDist(255)보다는 작은" 경우.
        // 공기를 경계에서 가장 먼 우상단 코너(15,15)에 둔다 → 좌측 경계(x=0..3)까지의
        // 실제 chamfer 거리는 60~105 범위(아래 계산 참고). 시드는 150+k*5(=150,155,160,165)로
        // 이 범위보다 항상 크게 잡는다.
        //
        // Path B: dfRound1(=실거리, 60~105)이 이미 시드(150~165)보다 작으므로
        //         min-merge에서 시드가 전부 거부된다 — dfB는 실거리 그대로 유지.
        // Path C: 리셋값 255 > 시드이므로 시드가 배열에 "실제로 기록"된다.
        //         forward pass는 같은 행(y=0) 안에서는 시드끼리 5 간격이라 그대로 유지되지만,
        //         backward pass가 (15,15)에서 좌상단 방향으로 여러 행을 거쳐 전파해 오면서
        //         실거리(60~105) < 시드(150~165)이므로 결국 시드를 밀어내고 실거리로 덮어쓴다.
        // → 두 경로 모두 최종적으로 같은 실거리 고정점에 수렴해야 한다(다단계 relaxation 검증).
        var air = new bool[W * H];
        air[15 * H + 15] = true;   // 우상단 코너 — 좌측 경계와 최대한 멀리 떨어뜨림
        var baseData = MakeBase(air);
        var pixelInfo = new NativeArray<byte>(0, Allocator.Temp);

        NativeArray<ushort> dfRound1 = default, dfB = default, dfC = default;
        try
        {
            dfRound1 = MakeResetField(air);
            RunChamfer(dfRound1, baseData, pixelInfo);

            dfB = new NativeArray<ushort>(W * H, Allocator.Temp);
            dfRound1.CopyTo(dfB);
            ApplyHighBoundarySeed(dfB);
            RunChamfer(dfB, baseData, pixelInfo);

            dfC = MakeResetField(air);
            ApplyHighBoundarySeed(dfC);
            RunChamfer(dfC, baseData, pixelInfo);

            for (int i = 0; i < W * H; i++)
                Assert.AreEqual(dfC[i], dfB[i], $"불일치 index={i} (x={i % W}, y={i / W})");
        }
        finally
        {
            if (dfRound1.IsCreated) dfRound1.Dispose();
            if (dfB.IsCreated) dfB.Dispose();
            if (dfC.IsCreated) dfC.Dispose();
            baseData.Dispose();
            pixelInfo.Dispose();
        }
    }

    // ────────────────────────────────────────────────────────────────

    private static NativeArray<Color32> MakeBase(bool[] air)
    {
        var a = new NativeArray<Color32>(W * H, Allocator.Temp);
        for (int i = 0; i < W * H; i++)
            a[i] = air[i] ? new Color32(0, 0, 0, 0) : new Color32(0, 0, 0, 255);
        return a;
    }

    /// InitDistanceFieldJob의 dirty 분기와 동일: 공기→0, 솔리드→maxDist
    private static NativeArray<ushort> MakeResetField(bool[] air)
    {
        var df = new NativeArray<ushort>(W * H, Allocator.Temp);
        for (int i = 0; i < W * H; i++)
            df[i] = air[i] ? (ushort)0 : (ushort)MAXD;
        return df;
    }

    /// BoundarySyncJob의 좌측 엣지 로직과 동일한 min-merge (이웃 거리가 낮은 경우)
    private static void ApplyBoundarySeed(NativeArray<ushort> df)
    {
        for (int y = 0; y < H; y++)
            for (int k = 0; k < 4; k++)
            {
                int idx = y * W + k;
                ushort candidate = (ushort)(5 + k * 5);
                if (candidate < df[idx]) df[idx] = candidate;
            }
    }

    /// 좌측 경계의 실거리(60~105, air가 (15,15)일 때)보다는 크지만 maxDist(255)보다는 작은 시드.
    /// Path B(min-merge)에서는 항상 거부되지만, Path C(리셋 후 기록)에서는 일단 기록된 뒤
    /// backward pass의 다단계 전파로 밀려난다 — 두 경우 모두 최종적으로는 채택되지 않는다.
    private static void ApplyHighBoundarySeed(NativeArray<ushort> df)
    {
        for (int y = 0; y < H; y++)
            for (int k = 0; k < 4; k++)
            {
                int idx = y * W + k;
                ushort candidate = (ushort)(150 + k * 5);
                if (candidate < df[idx]) df[idx] = candidate;
            }
    }

    private static void RunChamfer(NativeArray<ushort> df, NativeArray<Color32> baseData, NativeArray<byte> info)
    {
        new TerrainJobs.ChamferForwardPassJob
        {
            distanceField = df, baseData = baseData, pixelInfo = info,
            width = W, height = H, maxDist = MAXD,
            extMinX = 0, extMinY = 0, extMaxX = W, extMaxY = H
        }.Run();

        new TerrainJobs.ChamferBackwardPassJob
        {
            distanceField = df, baseData = baseData, pixelInfo = info,
            width = W, height = H, maxDist = MAXD,
            extMinX = 0, extMinY = 0, extMaxX = W, extMaxY = H
        }.Run();
    }
}
