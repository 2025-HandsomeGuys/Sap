using NUnit.Framework;

public class ElevatorStopLayoutTests
{
    // 정류장 개수 (ElevatorLayerCatalog: 지층 4개 x 상층/중층/하층)
    private const int StopCount = 12;

    // 잠수마다 새로 뽑히는 시드 — 어떤 값이 와도 불변식이 지켜져야 한다.
    private static readonly int[] Seeds = { 0, 1, 7, 12345, 999983, int.MaxValue };

    // 시드는 static이라 테스트 간에 새어나간다 — 앞뒤로 확실히 되돌린다.
    [SetUp]
    public void ResetSeedBefore() => ElevatorStopLayout.Seed = 0;

    [TearDown]
    public void ResetSeedAfter() => ElevatorStopLayout.Seed = 0;

    [Test]
    public void XForLayer_IsDeterministic()
    {
        // 청크는 수시로 언로드/재로드된다 — 같은 층·같은 시드면 언제나 같은 X여야 한다.
        foreach (int seed in Seeds)
        {
            ElevatorStopLayout.Seed = seed;
            for (int layer = 0; layer < StopCount; layer++)
                Assert.AreEqual(
                    ElevatorStopLayout.XForLayer(layer),
                    ElevatorStopLayout.XForLayer(layer),
                    $"seed={seed}, layer={layer}");
        }
    }

    [Test]
    public void XForLayer_StaysWithinRange()
    {
        foreach (int seed in Seeds)
        {
            ElevatorStopLayout.Seed = seed;
            for (int layer = 0; layer < StopCount; layer++)
            {
                int x = ElevatorStopLayout.XForLayer(layer);
                Assert.GreaterOrEqual(x, ElevatorStopLayout.MinX, $"seed={seed}, layer={layer}");
                Assert.LessOrEqual(x, ElevatorStopLayout.MaxX, $"seed={seed}, layer={layer}");
            }
        }
    }

    [Test]
    public void NeighbourLayers_AreSeparated()
    {
        // 위아래로 이웃한 층이 같은 X에 걸리면 "어긋난다"는 느낌이 사라진다.
        foreach (int seed in Seeds)
        {
            ElevatorStopLayout.Seed = seed;
            for (int layer = 1; layer < StopCount; layer++)
            {
                int a = ElevatorStopLayout.XForLayer(layer - 1);
                int b = ElevatorStopLayout.XForLayer(layer);
                Assert.GreaterOrEqual(System.Math.Abs(b - a), ElevatorStopLayout.MinSeparation,
                    $"seed={seed}: layer {layer - 1}(x={a}) ↔ {layer}(x={b})");
            }
        }
    }

    [Test]
    public void LayersAreNotAllTheSameX()
    {
        // 기획 의도: 층마다 X가 달라 아래로 갈수록 지그재그여야 한다.
        foreach (int seed in Seeds)
        {
            ElevatorStopLayout.Seed = seed;
            int first = ElevatorStopLayout.XForLayer(0);
            bool anyDiff = false;
            for (int layer = 1; layer < StopCount; layer++)
                if (ElevatorStopLayout.XForLayer(layer) != first) { anyDiff = true; break; }

            Assert.IsTrue(anyDiff, $"seed={seed}: 모든 층의 X가 동일하다 — 랜덤이 동작하지 않는다.");
        }
    }

    [Test]
    public void DifferentSeeds_ProduceDifferentLayouts()
    {
        // 잠수마다 배치가 바뀌어야 한다. 시드가 달라도 배치가 같으면 시드가 안 먹은 것이다.
        // (개별 층은 우연히 겹칠 수 있으므로 '전체 배치가 하나라도 다른 시드가 있는가'로 본다.)
        ElevatorStopLayout.Seed = 0;
        string baseline = Layout();

        bool anyDifferent = false;
        foreach (int seed in Seeds)
        {
            if (seed == 0) continue;
            ElevatorStopLayout.Seed = seed;
            if (Layout() != baseline) { anyDifferent = true; break; }
        }

        Assert.IsTrue(anyDifferent, "시드를 바꿔도 배치가 그대로다 — Seed가 해시에 안 섞였다.");
    }

    [Test]
    public void EntranceLayer_NeverLandsOnEntranceChunk()
    {
        // 구멍 입구 청크는 (0,0) — ImageChunkOverrider가 PNG로 칠하는 보호 좌표다.
        // 첫 정류장(Y=0)이 x=0을 뽑으면 승강로 픽셀이 페인팅에 덮여 엘리베이터만 지형에 박힌다.
        for (int seed = 0; seed < 500; seed++)
        {
            ElevatorStopLayout.Seed = seed;
            Assert.AreNotEqual(0, ElevatorStopLayout.XForLayer(0), $"seed={seed}");
        }
    }

    [Test]
    public void MinSeparation_FitsRange()
    {
        // 제약이 범위보다 빡세면 후보가 사라진다.
        int span = ElevatorStopLayout.MaxX - ElevatorStopLayout.MinX + 1;
        Assert.Less(ElevatorStopLayout.MinSeparation * 2, span);
    }

    [Test]
    public void NegativeLayer_DoesNotThrow()
    {
        // GetLayerIndexByDepth가 -1을 돌려주는 경로가 실수로 흘러들어와도 죽지 않아야 한다.
        Assert.DoesNotThrow(() => ElevatorStopLayout.XForLayer(-1));
    }

    private static string Layout()
    {
        var sb = new System.Text.StringBuilder();
        for (int layer = 0; layer < StopCount; layer++)
            sb.Append(ElevatorStopLayout.XForLayer(layer)).Append(',');
        return sb.ToString();
    }
}
