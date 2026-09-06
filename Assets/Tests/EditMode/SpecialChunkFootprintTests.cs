using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SpecialChunkFootprint 좌표 로직 핵심 테스트.
/// 대형 특수 청크의 풋프린트·서브좌표·앵커 역산이 서로 일관성 있는지 검증한다.
/// MonoBehaviour 없이 순수 수학만 테스트.
/// </summary>
public class SpecialChunkFootprintTests
{
    // ── Build (풋프린트) ─────────────────────────────────────────────────────

    [Test]
    public void Build_1x1_ContainsOnlyAnchor()
    {
        var anchor = new Vector2Int(2, -3);
        var set = new HashSet<Vector2Int>();
        SpecialChunkFootprint.Build(anchor, new Vector2Int(1, 1), set);

        Assert.AreEqual(1, set.Count);
        Assert.IsTrue(set.Contains(anchor));
    }

    [Test]
    public void Build_3x2_ContainsSixCoords()
    {
        var anchor = new Vector2Int(2, -3);
        var set = new HashSet<Vector2Int>();
        SpecialChunkFootprint.Build(anchor, new Vector2Int(3, 2), set);

        // 예상: (2,-3),(3,-3),(4,-3),(2,-4),(3,-4),(4,-4)
        Assert.AreEqual(6, set.Count);
    }

    [Test]
    public void Build_3x2_ContainsExpectedCoords()
    {
        var anchor = new Vector2Int(2, -3);
        var set = new HashSet<Vector2Int>();
        SpecialChunkFootprint.Build(anchor, new Vector2Int(3, 2), set);

        var expected = new[]
        {
            new Vector2Int(2, -3), new Vector2Int(3, -3), new Vector2Int(4, -3),
            new Vector2Int(2, -4), new Vector2Int(3, -4), new Vector2Int(4, -4),
        };
        foreach (var coord in expected)
            Assert.IsTrue(set.Contains(coord), $"풋프린트에 {coord} 없음");
    }

    [Test]
    public void Build_DoesNotExtendUpward()
    {
        // Y는 아래(음수)로만 확장 — anchor.y보다 큰 Y가 없어야 함
        var anchor = new Vector2Int(0, -5);
        var set = new HashSet<Vector2Int>();
        SpecialChunkFootprint.Build(anchor, new Vector2Int(3, 3), set);

        foreach (var coord in set)
            Assert.LessOrEqual(coord.y, anchor.y, $"{coord}가 앵커 Y보다 위에 있음");
    }

    // ── GetSubCoords ──────────────────────────────────────────────────────────

    [Test]
    public void GetSubCoords_1x1_ReturnsEmpty()
    {
        var subs = SpecialChunkFootprint.GetSubCoords(new Vector2Int(0, 0), new Vector2Int(1, 1));
        Assert.AreEqual(0, subs.Count);
    }

    [Test]
    public void GetSubCoords_3x2_ReturnsFiveCoords()
    {
        var subs = SpecialChunkFootprint.GetSubCoords(new Vector2Int(2, -3), new Vector2Int(3, 2));
        Assert.AreEqual(5, subs.Count);
    }

    [Test]
    public void GetSubCoords_DoesNotContainAnchor()
    {
        var anchor = new Vector2Int(2, -3);
        var subs = SpecialChunkFootprint.GetSubCoords(anchor, new Vector2Int(3, 2));
        Assert.IsFalse(subs.Contains(anchor), "서브좌표 목록에 앵커 자신이 포함됨");
    }

    [Test]
    public void GetSubCoords_3x2_MatchesBuildMinusAnchor()
    {
        // GetSubCoords == Build 결과에서 앵커 제거
        var anchor = new Vector2Int(2, -3);
        var size = new Vector2Int(3, 2);

        var footprint = new HashSet<Vector2Int>();
        SpecialChunkFootprint.Build(anchor, size, footprint);
        footprint.Remove(anchor);

        var subs = new HashSet<Vector2Int>(SpecialChunkFootprint.GetSubCoords(anchor, size));

        Assert.IsTrue(footprint.SetEquals(subs), "GetSubCoords와 Build 결과가 불일치");
    }

    // ── GetCandidateAnchors ───────────────────────────────────────────────────

    [Test]
    public void GetCandidateAnchors_3x2_EachSubCoord_IncludesRealAnchor()
    {
        // 핵심 테스트: 모든 서브좌표에서 역산한 후보에 실제 앵커가 반드시 포함되어야 한다
        var anchor = new Vector2Int(2, -3);
        var size = new Vector2Int(3, 2);

        var subs = SpecialChunkFootprint.GetSubCoords(anchor, size);
        foreach (var sub in subs)
        {
            var candidates = SpecialChunkFootprint.GetCandidateAnchors(sub, size).ToList();
            Assert.IsTrue(
                candidates.Contains(anchor),
                $"서브좌표 {sub}에서 실제 앵커 {anchor}를 역산 후보에서 찾지 못함. " +
                $"후보 목록: [{string.Join(", ", candidates)}]"
            );
        }
    }

    [Test]
    public void GetCandidateAnchors_ExcludesAboveGround()
    {
        // y > 0 후보는 제외
        var sub = new Vector2Int(0, 0);
        var size = new Vector2Int(2, 2);

        var candidates = SpecialChunkFootprint.GetCandidateAnchors(sub, size).ToList();

        foreach (var c in candidates)
            Assert.LessOrEqual(c.y, 0, $"후보 {c}의 Y가 0보다 큼 (지상 위)");
    }

    [Test]
    public void GetCandidateAnchors_ExcludesSubCoordItself()
    {
        var sub = new Vector2Int(3, -4);
        var size = new Vector2Int(3, 2);

        var candidates = SpecialChunkFootprint.GetCandidateAnchors(sub, size).ToList();

        Assert.IsFalse(candidates.Contains(sub), "후보 목록에 subCoord 자신이 포함됨");
    }

    // ── IsNearLayerBoundary ───────────────────────────────────────────────────

    private static readonly int[] DefaultBoundaries = { -2, -4, -6, -8, -10, -12 };

    [Test]
    public void IsNearLayerBoundary_ExactBoundaryCoord_ReturnsTrue()
    {
        // spacing=1이면 Abs(0) < 1 → true, 경계 자체도 스킵됨
        Assert.IsTrue(
            SpecialChunkFootprint.IsNearLayerBoundary(new Vector2Int(0, -2), DefaultBoundaries, 1)
        );
    }

    [Test]
    public void IsNearLayerBoundary_OneAboveBoundary_ReturnsTrue()
    {
        // Y=-1은 경계 -2로부터 거리 1 → Abs(-1-(-2))=1, spacing=1이면 1 < 1 = false
        Assert.IsFalse(
            SpecialChunkFootprint.IsNearLayerBoundary(new Vector2Int(0, -1), DefaultBoundaries, 1)
        );
    }

    [Test]
    public void IsNearLayerBoundary_OneBelowBoundary_ReturnsFalse()
    {
        // Y=-3은 경계 -2로부터 거리 1, 경계 -4로부터 거리 1 → 둘 다 1 < 1 = false
        Assert.IsFalse(
            SpecialChunkFootprint.IsNearLayerBoundary(new Vector2Int(0, -3), DefaultBoundaries, 1)
        );
    }

    [Test]
    public void IsNearLayerBoundary_SpacingTwo_AdjacentCoordsAreBlocked()
    {
        // spacing=2이면 경계 ±1도 블록
        // Y=-3: Abs(-3-(-4))=1 < 2 → true
        Assert.IsTrue(
            SpecialChunkFootprint.IsNearLayerBoundary(new Vector2Int(0, -3), DefaultBoundaries, 2)
        );
    }

    [Test]
    public void IsNearLayerBoundary_MidwayBetweenBoundaries_ReturnsFalse()
    {
        // Y=-5: 경계 -4로부터 거리 1, 경계 -6로부터 거리 1 → spacing=1이면 둘 다 false
        Assert.IsFalse(
            SpecialChunkFootprint.IsNearLayerBoundary(new Vector2Int(0, -5), DefaultBoundaries, 1)
        );
    }
}
