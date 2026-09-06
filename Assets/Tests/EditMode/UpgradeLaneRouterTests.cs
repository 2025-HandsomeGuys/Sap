// @tags: test, editmode, upgrade, tree, ui, line, routing, lane, hub
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 연결선이 가로로 건너갈 높이를 검사한다.
///
/// 두 규칙이 겹쳐 있다:
///   · 허브(분기·합류) — 그 허브에 붙은 선이 **전부 같은 높이**에서 꺾여야
///     한 줄로 나왔다 갈라지고, 한 줄로 모여서 들어간다.
///   · 그 밖의 선 — 레인 점유를 보고 살아있는 트렁크에 붙어 간다.
/// 허브가 먼저다. 계단 배치라 자식마다 높이가 달라도 꺾이는 줄은 하나여야 갈래 수가 읽힌다.
/// </summary>
public class UpgradeLaneRouterTests
{
    private const float Clearance = 95f;   // 노드 반높이(73) + TrunkGap(22)
    private const float LaneA = 0f;
    private const float LaneB = 262.5f;    // uiX 175 * graphScale 1.5
    private const float LaneC = 525f;      // uiX 350

    private static UpgradeLaneRouter Router(params Vector2[] nodes)
    {
        var r = new UpgradeLaneRouter();
        r.Build(new List<Vector2>(nodes));
        return r;
    }

    private static UpgradeLaneRouter.Link Link(
        Vector2 start, Vector2 end, string parentId = "P", string childId = "C",
        bool forks = false, bool merges = false)
        => new UpgradeLaneRouter.Link(parentId, start, childId, end, forks, merges);

    // ===== 레인 점유 (허브가 아닌 선) =====

    [Test]
    public void ParentLaneAlive_JogsLate_NearChild()
    {
        // 부모 레인(A)에 중간 노드가 있다 = 살아있는 트렁크 → 거기 붙어 가다 자식 직전에 갈라진다
        var start = new Vector2(LaneA, 0f);
        var end = new Vector2(LaneB, 600f);
        var r = Router(start, end, new Vector2(LaneA, 300f));

        Assert.AreEqual(600f - Clearance, r.JogY(Link(start, end), Clearance), 0.01f);
    }

    [Test]
    public void ParentLaneEmpty_ChildLaneAlive_JogsEarly_NearParent()
    {
        // 부모 레인(B)은 이 구간이 비었고 자식 레인(A)이 살아있다 → 곧장 A 트렁크로 합류
        var start = new Vector2(LaneB, 0f);
        var end = new Vector2(LaneA, 600f);
        var r = Router(start, end, new Vector2(LaneA, 300f));

        Assert.AreEqual(0f + Clearance, r.JogY(Link(start, end), Clearance), 0.01f);
    }

    [Test]
    public void BothLanesEmpty_JogsLate()
    {
        var start = new Vector2(LaneA, 0f);
        var end = new Vector2(LaneB, 600f);
        var r = Router(start, end);

        Assert.AreEqual(600f - Clearance, r.JogY(Link(start, end), Clearance), 0.01f);
    }

    [Test]
    public void Endpoints_DoNotCountAsLaneOccupancy()
    {
        // 부모·자식 자신이 '중간 노드'로 잡히면 판정이 항상 살아있음으로 기운다
        var start = new Vector2(LaneB, 0f);
        var end = new Vector2(LaneA, 600f);
        var r = Router(start, end);

        Assert.IsFalse(r.IsLaneAlive(LaneB, 0f, 600f));
        Assert.IsFalse(r.IsLaneAlive(LaneA, 0f, 600f));
    }

    [Test]
    public void UpwardConnection_UsesSameRule()
    {
        // 매핑 좌표는 위아래가 뒤집힐 수 있다 — 방향에 의존하면 안 된다
        var start = new Vector2(LaneA, 600f);
        var end = new Vector2(LaneB, 0f);
        var r = Router(start, end, new Vector2(LaneA, 300f));

        Assert.AreEqual(0f + Clearance, r.JogY(Link(start, end), Clearance), 0.01f);
    }

    [Test]
    public void SameLane_ReturnsNaN()
    {
        var start = new Vector2(LaneA, 0f);
        var end = new Vector2(LaneA, 600f);
        Assert.IsNaN(Router(start, end).JogY(Link(start, end), Clearance));
    }

    [Test]
    public void SameRow_ReturnsNaN()
    {
        var start = new Vector2(LaneA, 300f);
        var end = new Vector2(LaneB, 300f);
        Assert.IsNaN(Router(start, end).JogY(Link(start, end), Clearance));
    }

    [Test]
    public void TooCloseToClearBothBoxes_FallsBackToMidpoint()
    {
        // 간격이 노드 두 개 높이도 안 되면 어느 쪽에 붙여도 사각형 안이다 → 절반 지점
        var start = new Vector2(LaneA, 0f);
        var end = new Vector2(LaneB, 100f);
        Assert.AreEqual(50f, Router(start, end).JogY(Link(start, end), Clearance), 0.01f);
    }

    // ===== 허브 (분기·합류) =====

    [Test]
    public void MergePoint_AllIncomingBendAtOneHeight_UsingShortestLink()
    {
        // 합류 노드로 들어오는 선은 부모 높이가 제각각이어도 **한 줄**로 모여서 들어간다.
        // 거리는 가장 짧은 선(여기서는 span 100 -> stub 50)에 맞춘다 —
        // 긴 선에 맞추면 짧은 선의 꺾임이 부모 뒤로 넘어간다.
        var far = new Vector2(LaneB, 0f);      // span 600 -> stub 95
        var near = new Vector2(LaneC, 500f);   // span 100 -> stub 50
        var child = new Vector2(LaneA, 600f);

        var r = Router(far, near, child);
        var links = new List<UpgradeLaneRouter.Link>
        {
            Link(far, child, "P1", "C", merges: true),
            Link(near, child, "P2", "C", merges: true),
        };
        r.BuildHubs(links, Clearance);

        Assert.AreEqual(550f, r.JogY(links[0], Clearance), 0.01f);
        Assert.AreEqual(550f, r.JogY(links[1], Clearance), 0.01f);
    }

    [Test]
    public void ForkPoint_AllOutgoingBendAtOneHeight()
    {
        // 분기 노드에서 나가는 선은 자식 높이가 계단이어도 **한 줄**로 나왔다 거기서 갈라진다
        var parent = new Vector2(LaneA, 0f);
        var farChild = new Vector2(LaneB, 600f);    // stub 95
        var nearChild = new Vector2(LaneC, 120f);   // span 120 -> stub 60

        var r = Router(parent, farChild, nearChild);
        var links = new List<UpgradeLaneRouter.Link>
        {
            Link(parent, farChild, "P", "C1", forks: true),
            Link(parent, nearChild, "P", "C2", forks: true),
        };
        r.BuildHubs(links, Clearance);

        Assert.AreEqual(60f, r.JogY(links[0], Clearance), 0.01f);
        Assert.AreEqual(60f, r.JogY(links[1], Clearance), 0.01f);
    }

    [Test]
    public void MergeBeatsFork_WhenBothApply()
    {
        // 둘 다 걸리면 자식 쪽에 붙여야 들어오는 선들이 모인다.
        // 나가는 트렁크는 노드 반대편이라 겹치지 않는다.
        var start = new Vector2(LaneA, 0f);
        var end = new Vector2(LaneB, 600f);

        var r = Router(start, end);
        var links = new List<UpgradeLaneRouter.Link>
        {
            Link(start, end, "P", "C", forks: true, merges: true),
        };
        r.BuildHubs(links, Clearance);

        Assert.AreEqual(600f - Clearance, r.JogY(links[0], Clearance), 0.01f);
    }

    [Test]
    public void HubStubs_IgnoreSameLaneLinks()
    {
        // 같은 레인 선은 꺾이지 않으므로 허브 거리에 끼면 안 된다 —
        // 끼면 그 선의 span이 공유 거리를 엉뚱하게 끌어내린다.
        var parent = new Vector2(LaneA, 0f);
        var sameLane = new Vector2(LaneA, 40f);     // 세로 직선, span 40
        var crossLane = new Vector2(LaneB, 600f);

        var r = Router(parent, sameLane, crossLane);
        var links = new List<UpgradeLaneRouter.Link>
        {
            Link(parent, sameLane, "P", "C1", forks: true),
            Link(parent, crossLane, "P", "C2", forks: true),
        };
        r.BuildHubs(links, Clearance);

        Assert.IsNaN(r.JogY(links[0], Clearance));
        Assert.AreEqual(Clearance, r.JogY(links[1], Clearance), 0.01f);
    }
}
