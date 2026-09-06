using NUnit.Framework;
using System.Collections.Generic;
using Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer;

public class StarStrokeStateTests
{
    // 정사각형 A-B-C-D + 대각선 A-C (봉투 일부). 한붓그리기 가능.
    private static List<StarEdge> Square()
    {
        return new List<StarEdge>
        {
            new StarEdge("A", "B"),
            new StarEdge("B", "C"),
            new StarEdge("C", "D"),
            new StarEdge("D", "A"),
            new StarEdge("A", "C"),
        };
    }

    [Test]
    public void FirstPress_StartsPen()
    {
        var s = new StarStrokeState(Square());
        var r = s.TryPress("A");
        Assert.AreEqual(MoveKind.Started, r.Kind);
        Assert.AreEqual("A", s.CurrentNodeId);
        Assert.AreEqual(0, s.DrawnCount);
    }

    [Test]
    public void SamePress_NoEdgesDrawn_CancelsStart()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A");
        var r = s.TryPress("A");
        Assert.AreEqual(MoveKind.CancelledStart, r.Kind);
        Assert.IsNull(s.CurrentNodeId);
    }

    [Test]
    public void ValidAdjacent_DrawsEdge_AndMovesPen()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A");
        var r = s.TryPress("B");
        Assert.AreEqual(MoveKind.Drew, r.Kind);
        Assert.AreEqual(new StarEdge("A", "B"), r.Edge);
        Assert.AreEqual("B", s.CurrentNodeId);
        Assert.AreEqual(1, s.DrawnCount);
        Assert.IsTrue(s.IsEdgeDrawn(new StarEdge("A", "B")));
    }

    [Test]
    public void NonAdjacent_IsIgnored()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("B");
        var r = s.TryPress("D"); // B-D 선은 도형에 없음
        Assert.AreEqual(MoveKind.Ignored, r.Kind);
        Assert.AreEqual("B", s.CurrentNodeId); // 펜 안 움직임
        Assert.AreEqual(0, s.DrawnCount);
    }

    [Test]
    public void AlreadyDrawn_IsIgnored()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A");
        s.TryPress("B"); // A-B 그음, 펜 B
        s.TryPress("A"); // B-A 이미 그은 선 → 무시
        Assert.AreEqual(1, s.DrawnCount);
        Assert.AreEqual("B", s.CurrentNodeId);
    }

    [Test]
    public void DeadEnd_False_WhenUndrawnEdgeRemainsAtPen()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A"); s.TryPress("B"); s.TryPress("C"); s.TryPress("D"); s.TryPress("A");
        Assert.IsFalse(s.IsDeadEnd()); // A에 미사용 A-C 있음
        s.TryPress("C");
        Assert.IsTrue(s.IsSolved);
        Assert.IsFalse(s.IsDeadEnd());
    }

    [Test]
    public void DeadEnd_True_WhenPenHasNoUndrawnEdges()
    {
        // 삼각형 A-B-C 세 변 + 꼬리 C-D. A→B→C→D 로 가면 D에서 막힘.
        var edges = new List<StarEdge>
        {
            new StarEdge("A","B"), new StarEdge("B","C"),
            new StarEdge("C","A"), new StarEdge("C","D"),
        };
        var s = new StarStrokeState(edges);
        s.TryPress("A"); s.TryPress("B"); s.TryPress("C"); s.TryPress("D");
        Assert.IsFalse(s.IsSolved);
        Assert.IsTrue(s.IsDeadEnd());
    }

    [Test]
    public void FullTraversal_Solves()
    {
        var s = new StarStrokeState(Square());
        // 홀수차수 A,C → A에서 시작하는 한붓 경로
        s.TryPress("A"); // start
        s.TryPress("B"); // A-B
        s.TryPress("C"); // B-C
        s.TryPress("A"); // C-A
        s.TryPress("D"); // A-D
        s.TryPress("C"); // D-C
        Assert.IsTrue(s.IsSolved);
        Assert.AreEqual(5, s.DrawnCount);
    }

    [Test]
    public void Reset_ClearsDrawnAndPen()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A"); s.TryPress("B");
        s.Reset();
        Assert.AreEqual(0, s.DrawnCount);
        Assert.IsNull(s.CurrentNodeId);
        Assert.IsFalse(s.IsSolved);
    }

    [Test]
    public void OddDegree_SolvableFigures()
    {
        Assert.IsTrue(StarStrokeState.IsOneStrokePossible(Square())); // 홀수 2개
        var triangle = new List<StarEdge>
        {
            new StarEdge("A","B"), new StarEdge("B","C"), new StarEdge("C","A"),
        };
        Assert.AreEqual(0, StarStrokeState.CountOddDegreeNodes(triangle)); // 전부 차수2
        Assert.IsTrue(StarStrokeState.IsOneStrokePossible(triangle));
    }

    [Test]
    public void OddDegree_UnsolvableFigure()
    {
        // 완전그래프 K4: 모든 정점 차수3 → 홀수 4개 → 불가
        var k4 = new List<StarEdge>
        {
            new StarEdge("A","B"), new StarEdge("A","C"), new StarEdge("A","D"),
            new StarEdge("B","C"), new StarEdge("B","D"), new StarEdge("C","D"),
        };
        Assert.AreEqual(4, StarStrokeState.CountOddDegreeNodes(k4));
        Assert.IsFalse(StarStrokeState.IsOneStrokePossible(k4));
    }

    [Test]
    public void UnknownNode_IsIgnored()
    {
        var s = new StarStrokeState(Square());
        var r = s.TryPress("Z"); // 도형에 등장하지 않는 별
        Assert.AreEqual(MoveKind.Ignored, r.Kind);
        Assert.IsNull(s.CurrentNodeId);
    }

    [Test]
    public void DuplicateAndSelfLoop_DoNotCorruptDegree()
    {
        // 삼각형(전부 차수2, 홀수0) + 중복 A-B + self-loop A-A → 여전히 홀수 0
        var edges = new List<StarEdge>
        {
            new StarEdge("A","B"), new StarEdge("B","C"), new StarEdge("C","A"),
            new StarEdge("A","B"), // 중복 간선
            new StarEdge("A","A"), // self-loop
        };
        Assert.AreEqual(0, StarStrokeState.CountOddDegreeNodes(edges));
        Assert.IsTrue(StarStrokeState.IsOneStrokePossible(edges));
    }

    [Test]
    public void UnnormalizedInspectorEdge_IsNormalizedOnConstruction()
    {
        // Unity 직렬화는 정렬 생성자를 거치지 않아 필드가 역순(B,A)일 수 있음.
        var reversed = new StarEdge { NodeAId = "B", NodeBId = "A" };
        var s = new StarStrokeState(new List<StarEdge> { reversed });
        s.TryPress("A");
        var r = s.TryPress("B"); // 정규화됐다면 A-B 유효
        Assert.AreEqual(MoveKind.Drew, r.Kind);
        Assert.IsTrue(s.IsSolved);
    }

    [Test]
    public void RepressPen_AfterEdgeDrawn_IsIgnored()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A");
        s.TryPress("B");        // A-B 그음, 펜 B
        var r = s.TryPress("B"); // 그은 선 있음 + 현재 펜 재선택 → 무시(취소 아님)
        Assert.AreEqual(MoveKind.Ignored, r.Kind);
        Assert.AreEqual("B", s.CurrentNodeId);
        Assert.AreEqual(1, s.DrawnCount);
    }

    // 직선 그래프 A-B-C-D: 지나온 정점 재상호작용으로 되돌리기 테스트에 사용.
    // (이미 그은 선만 있어 "새로 그을 수 없는" 상황을 만든다.)
    private static List<StarEdge> Line()
    {
        return new List<StarEdge>
        {
            new StarEdge("A", "B"),
            new StarEdge("B", "C"),
            new StarEdge("C", "D"),
        };
    }

    [Test]
    public void Rewind_ToEarlierVertex_UndoesLaterEdges()
    {
        var s = new StarStrokeState(Line());
        s.TryPress("A"); s.TryPress("B"); s.TryPress("C"); // 펜 C, 그음 A-B,B-C
        var r = s.TryPress("B"); // B-C는 이미 그음 → 못 그음, B는 지나온 정점 → 되돌리기
        Assert.AreEqual(MoveKind.Rewound, r.Kind);
        Assert.AreEqual("B", s.CurrentNodeId);
        Assert.AreEqual(1, s.DrawnCount);
        Assert.IsTrue(s.IsEdgeDrawn(new StarEdge("A", "B")));
        Assert.IsFalse(s.IsEdgeDrawn(new StarEdge("B", "C")));
    }

    [Test]
    public void Rewind_ToStart_UndoesEverything()
    {
        var s = new StarStrokeState(Line());
        s.TryPress("A"); s.TryPress("B"); s.TryPress("C");
        var r = s.TryPress("A"); // C-A 선 없음 → 시작점까지 되돌리기
        Assert.AreEqual(MoveKind.Rewound, r.Kind);
        Assert.AreEqual("A", s.CurrentNodeId);
        Assert.AreEqual(0, s.DrawnCount);
    }

    [Test]
    public void RevisitViaUndrawnEdge_PrefersDraw_NotRewind()
    {
        // 삼각형 A-B-C: C에서 A로 갈 때 C-A는 아직 안 그은 유효 선 → 되돌리기 아니라 긋기 우선.
        var tri = new List<StarEdge>
        {
            new StarEdge("A", "B"), new StarEdge("B", "C"), new StarEdge("C", "A"),
        };
        var s = new StarStrokeState(tri);
        s.TryPress("A"); s.TryPress("B"); s.TryPress("C");
        var r = s.TryPress("A"); // C-A 미사용 → 긋기
        Assert.AreEqual(MoveKind.Drew, r.Kind);
        Assert.AreEqual(3, s.DrawnCount);
        Assert.IsTrue(s.IsSolved);
    }

    [Test]
    public void Rewind_ThenContinue_RedrawsEdge()
    {
        var s = new StarStrokeState(Line());
        s.TryPress("A"); s.TryPress("B"); s.TryPress("C");
        s.TryPress("B");         // B로 되돌림, 그음={A-B}
        var r = s.TryPress("C"); // B-C 다시 긋기
        Assert.AreEqual(MoveKind.Drew, r.Kind);
        Assert.AreEqual(2, s.DrawnCount);
        Assert.AreEqual("C", s.CurrentNodeId);
    }
}
