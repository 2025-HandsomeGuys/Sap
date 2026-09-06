using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 특수청크가 엘리베이터 정류장 청크를 덮지 않는지 검증한다.
///
/// 배경: 특수청크 당첨은 청크 생성 1-B(ChunkDataProvider)에서 결정되는데 엘리베이터는 한참 뒤
/// Phase 2 데코레이터에서 생기고, 특수청크는 일반 데코 경로를 아예 타지 않는다.
/// 그래서 정류장 좌표가 당첨되면 그 층 엘리베이터가 조용히 사라져 그 층에 갇힌다.
/// </summary>
public class SpecialChunkElevatorAvoidanceTests
{
    private const TileType Layer = TileType.Dirt;
    private const int WorldSeed = 4242;

    // 잠수마다 새로 뽑히는 시드 — 어떤 값이 와도 불변식이 지켜져야 한다.
    private static readonly int[] Seeds = { 0, 1, 7, 12345, 999983 };

    // 시드는 static이라 테스트 간에 새어나간다 — 앞뒤로 확실히 되돌린다.
    [SetUp]
    public void ResetSeedBefore() => ElevatorStopLayout.Seed = 0;

    [TearDown]
    public void ResetSeedAfter() => ElevatorStopLayout.Seed = 0;

    // ─── ElevatorStopLayout.IsStopCoord ──────────────────────────────────────

    [Test]
    public void IsStopCoord_TrueForEveryStop()
    {
        foreach (int seed in Seeds)
        {
            ElevatorStopLayout.Seed = seed;

            var layers = ElevatorLayerCatalog.Build();
            for (int i = 0; i < layers.Count; i++)
            {
                var stop = new Vector2Int(ElevatorStopLayout.XForLayer(i), layers[i].startDepth);
                Assert.IsTrue(ElevatorStopLayout.IsStopCoord(stop),
                    $"seed={seed}, layer={i}, coord={stop}");
            }
        }
    }

    [Test]
    public void IsStopCoord_FalseFarFromEveryStop()
    {
        // MaxX 바깥이라 어떤 시드에서도 정류장이 될 수 없는 좌표.
        var far = new Vector2Int(ElevatorStopLayout.MaxX + 10, -7);

        foreach (int seed in Seeds)
        {
            ElevatorStopLayout.Seed = seed;
            Assert.IsFalse(ElevatorStopLayout.IsStopCoord(far), $"seed={seed}");
        }
    }

    [Test]
    public void IsStopCoord_FollowsSeedChange()
    {
        // 캐시가 시드 변경을 못 따라가면 잠수마다 옛 정류장을 보호하고 새 정류장은 덮인다.
        ElevatorStopLayout.Seed = 1;
        var stopA = StopCoord(0);
        Assert.IsTrue(ElevatorStopLayout.IsStopCoord(stopA));

        ElevatorStopLayout.Seed = 999983;
        var stopB = StopCoord(0);
        Assert.IsTrue(ElevatorStopLayout.IsStopCoord(stopB), "새 시드의 정류장을 못 잡았다");

        // 두 시드의 X가 우연히 같을 수도 있으니, 다를 때만 옛 좌표가 풀렸는지 본다.
        if (stopA != stopB)
            Assert.IsFalse(ElevatorStopLayout.IsStopCoord(stopA), "옛 시드의 정류장이 캐시에 남았다");
    }

    // ─── SpecialChunkSelector 회피 ───────────────────────────────────────────

    [Test]
    public void TrySelect_RejectsAnchorOnStop()
    {
        ElevatorStopLayout.Seed = 7;

        var selector = BuildSelector(SizedDef(1, 1));
        var registry = new SubChunkRegistry();

        var stop = StopCoord(1);   // 지층 중간 정류장 (y < 0)
        Assert.IsNull(selector.TrySelect(stop, Layer, WorldSeed, registry),
            $"정류장 {stop}에 특수청크가 당첨됐다");
    }

    [Test]
    public void TrySelect_AllowsAnchorAwayFromStops()
    {
        // 회피 규칙이 특수청크를 통째로 죽이지 않는지 — 정류장이 아닌 좌표는 그대로 당첨돼야 한다.
        ElevatorStopLayout.Seed = 7;

        var selector = BuildSelector(SizedDef(1, 1));
        var registry = new SubChunkRegistry();

        var far = new Vector2Int(ElevatorStopLayout.MaxX + 10, -7);
        Assert.IsNotNull(selector.TrySelect(far, Layer, WorldSeed, registry),
            $"정류장과 무관한 {far}가 막혔다");
    }

    [Test]
    public void TrySelect_RejectsLargeAnchorWhoseFootprintCoversStop()
    {
        // 앵커 자체는 정류장이 아니어도, 2x2 점유 칸이 정류장을 덮으면 그 층 엘리베이터가 사라진다.
        ElevatorStopLayout.Seed = 7;

        var selector = BuildSelector(SizedDef(2, 2));
        var registry = new SubChunkRegistry();

        var stop = StopCoord(1);
        // 앵커는 좌상단 — (stop.x-1, stop.y+1)에서 시작하면 점유 칸이 stop을 포함한다.
        var anchor = new Vector2Int(stop.x - 1, stop.y + 1);
        Assume.That(ElevatorStopLayout.IsStopCoord(anchor), Is.False, "앵커 자체가 정류장이면 이 케이스가 아니다");

        Assert.IsNull(selector.TrySelect(anchor, Layer, WorldSeed, registry),
            $"앵커 {anchor}의 점유 칸이 정류장 {stop}을 덮는데 당첨됐다");
    }

    [Test]
    public void TrySelect_RejectsLinkedPieceOnStop()
    {
        ElevatorStopLayout.Seed = 7;

        var stop = StopCoord(1);
        var anchor = new Vector2Int(stop.x + 3, stop.y);   // 점유 칸(1x1)은 정류장과 안 겹친다
        Assume.That(ElevatorStopLayout.IsStopCoord(anchor), Is.False, "앵커 자체가 정류장이면 이 케이스가 아니다");

        var def = SizedDef(1, 1);
        def.linkedPieces = new[]
        {
            new SpecialChunkManager.LinkedPiece { offset = stop - anchor, prefab = null }
        };

        var selector = BuildSelector(def);
        var registry = new SubChunkRegistry();

        Assert.IsNull(selector.TrySelect(anchor, Layer, WorldSeed, registry),
            $"앵커 {anchor}의 링크 피스가 정류장 {stop}에 놓이는데 당첨됐다");
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────────────

    private static Vector2Int StopCoord(int layerIndex)
    {
        var layers = ElevatorLayerCatalog.Build();
        return new Vector2Int(ElevatorStopLayout.XForLayer(layerIndex), layers[layerIndex].startDepth);
    }

    /// <summary>항상 당첨되는 정의. prefab은 null이어도 선택 로직은 크기·확률만 본다.</summary>
    private static SpecialChunkManager.SpecialChunkDef SizedDef(int sizeX, int sizeY)
        => new SpecialChunkManager.SpecialChunkDef
        {
            prefab      = null,
            spawnChance = 100f,
            chunkSizeX  = sizeX,
            chunkSizeY  = sizeY,
        };

    /// <summary>
    /// minChunkSpacing=0 — 이웃 간격 규칙(해시 비교)이 끼어들면 무엇이 막았는지 구분이 안 된다.
    /// 이 테스트가 보려는 건 정류장 회피 하나뿐이다.
    /// </summary>
    private static SpecialChunkSelector BuildSelector(SpecialChunkManager.SpecialChunkDef def)
    {
        var pools = new List<SpecialChunkManager.SpecialChunkPool>
        {
            new SpecialChunkManager.SpecialChunkPool
            {
                targetLayer = Layer,
                chunks      = new List<SpecialChunkManager.SpecialChunkDef> { def },
            }
        };

        return new SpecialChunkSelector(pools, 0, 0, new int[0]);
    }
}
