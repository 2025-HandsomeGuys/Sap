using NUnit.Framework;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonPathBuilderTests
{
    private static DungeonShapeMask Mask(params string[] rows)
    {
        var r = DungeonShapeParser.Parse(string.Join("\n", rows) + "\n", "test");
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        return r.Masks[0];
    }

    // 개구부는 반드시 양쪽에 대칭으로 선다. 한쪽만 서면 방 하나가 벽을 보고 서 있게 된다.
    private static void AssertSymmetric(RoomPlan[,] plan)
    {
        int h = plan.GetLength(0), w = plan.GetLength(1);
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                if (c + 1 < w)
                    Assert.AreEqual((plan[r, c].Opens & RoomOpen.R) != 0,
                                    (plan[r, c + 1].Opens & RoomOpen.L) != 0, $"({r},{c}) 좌우 비대칭");
                if (r + 1 < h)
                    Assert.AreEqual((plan[r, c].Opens & RoomOpen.D) != 0,
                                    (plan[r + 1, c].Opens & RoomOpen.U) != 0, $"({r},{c}) 상하 비대칭");
            }
    }

    // 개구부를 따라 S에서 모든 던전 칸에 닿는지.
    private static int CountReachable(RoomPlan[,] plan, DungeonShapeMask mask)
    {
        var seen = new bool[plan.GetLength(0), plan.GetLength(1)];
        var stack = new System.Collections.Generic.Stack<(int r, int c)>();
        seen[mask.StartRow, mask.StartCol] = true;
        stack.Push((mask.StartRow, mask.StartCol));
        int n = 0;

        while (stack.Count > 0)
        {
            var (r, c) = stack.Pop();
            n++;
            var o = plan[r, c].Opens;
            if ((o & RoomOpen.L) != 0 && !seen[r, c - 1]) { seen[r, c - 1] = true; stack.Push((r, c - 1)); }
            if ((o & RoomOpen.R) != 0 && !seen[r, c + 1]) { seen[r, c + 1] = true; stack.Push((r, c + 1)); }
            if ((o & RoomOpen.U) != 0 && !seen[r - 1, c]) { seen[r - 1, c] = true; stack.Push((r - 1, c)); }
            if ((o & RoomOpen.D) != 0 && !seen[r + 1, c]) { seen[r + 1, c] = true; stack.Push((r + 1, c)); }
        }
        return n;
    }

    private static int CountDungeonCells(DungeonShapeMask mask)
    {
        int n = 0;
        for (int r = 0; r < mask.Height; r++)
            for (int c = 0; c < mask.Width; c++)
                if (mask.IsDungeon(r, c)) n++;
        return n;
    }

    [Test]
    public void Build_EveryDungeonCellIsReachable()
    {
        var mask = Mask("S....", ".....", "##...", "....X");
        var plan = DungeonPathBuilder.Build(mask, 0.25f, new System.Random(7));

        Assert.AreEqual(CountDungeonCells(mask), CountReachable(plan, mask), "고립된 방이 있다");
    }

    [Test]
    public void Build_OpensAreSymmetric()
    {
        var mask = Mask("S....", ".....", "##...", "....X");
        AssertSymmetric(DungeonPathBuilder.Build(mask, 0.5f, new System.Random(3)));
    }

    [Test]
    public void Build_RockCellsHaveNoOpens()
    {
        var mask = Mask("S....", ".....", "##...", "....X");
        var plan = DungeonPathBuilder.Build(mask, 1f, new System.Random(3));

        for (int r = 0; r < mask.Height; r++)
            for (int c = 0; c < mask.Width; c++)
                if (!mask.IsDungeon(r, c))
                {
                    Assert.AreEqual(RoomRole.Rock, plan[r, c].Role, $"({r},{c})");
                    Assert.AreEqual(RoomOpen.None, plan[r, c].Opens, $"({r},{c})");
                }
    }

    // 추가 연결 0이면 스패닝 트리 = 간선 수가 (칸 수 - 1)이어야 한다.
    [Test]
    public void Build_NoExtraConnections_IsSpanningTree()
    {
        var mask = Mask("S....", ".....", ".....", "....X");
        var plan = DungeonPathBuilder.Build(mask, 0f, new System.Random(11));

        int edges = 0;
        for (int r = 0; r < mask.Height; r++)
            for (int c = 0; c < mask.Width; c++)
            {
                if ((plan[r, c].Opens & RoomOpen.R) != 0) edges++;
                if ((plan[r, c].Opens & RoomOpen.D) != 0) edges++;
            }

        Assert.AreEqual(CountDungeonCells(mask) - 1, edges, "사이클이 생겼다");
    }

    [Test]
    public void Build_ExtraConnections_AddCycles()
    {
        var mask = Mask("S....", ".....", ".....", "....X");
        var tree = DungeonPathBuilder.Build(mask, 0f, new System.Random(11));
        var looped = DungeonPathBuilder.Build(mask, 1f, new System.Random(11));

        Assert.Greater(CountEdges(looped), CountEdges(tree));
    }

    private static int CountEdges(RoomPlan[,] plan)
    {
        int edges = 0;
        for (int r = 0; r < plan.GetLength(0); r++)
            for (int c = 0; c < plan.GetLength(1); c++)
            {
                if ((plan[r, c].Opens & RoomOpen.R) != 0) edges++;
                if ((plan[r, c].Opens & RoomOpen.D) != 0) edges++;
            }
        return edges;
    }

    [Test]
    public void Build_StartAndEndRolesComeFromMask()
    {
        var mask = Mask("S..X");
        var plan = DungeonPathBuilder.Build(mask, 0f, new System.Random(1));

        Assert.AreEqual(RoomRole.Start, plan[0, 0].Role);
        Assert.AreEqual(RoomRole.End, plan[0, 3].Role);
        Assert.AreEqual(RoomRole.Normal, plan[0, 1].Role);
    }

    [Test]
    public void Build_SameSeed_SamePlan()
    {
        var mask = Mask("S....", ".....", "....X");
        var a = DungeonPathBuilder.Build(mask, 0.5f, new System.Random(42));
        var b = DungeonPathBuilder.Build(mask, 0.5f, new System.Random(42));

        for (int r = 0; r < mask.Height; r++)
            for (int c = 0; c < mask.Width; c++)
                Assert.AreEqual(a[r, c].Opens, b[r, c].Opens, $"({r},{c})");
    }
}
