using NUnit.Framework;

public class MineralScatterTests
{
    // ─── ScatterGrid.Create ────────────────────────────────────────

    [Test]
    public void Grid_HasEnoughCellsForRequestedCount()
    {
        foreach (int count in new[] { 1, 2, 3, 5, 17, 170, 510, 9604 })
        {
            var g = ScatterGrid.Create(count, 10, 10, 980, 980);
            Assert.GreaterOrEqual(g.CellCount, count, $"count={count}");
        }
    }

    [Test]
    public void Grid_DoesNotOverAllocateCells()
    {
        // rows = ceil(count/cols) 이므로 cols*rows < count + cols 가 성립해야 한다.
        // 이게 깨지면 셀이 필요 이상으로 많아져 빈 셀이 늘고 밀도가 묽어진다.
        foreach (int count in new[] { 3, 5, 17, 170, 510 })
        {
            var g = ScatterGrid.Create(count, 10, 10, 980, 980);
            Assert.Less(g.CellCount, count + g.Cols, $"count={count} cols={g.Cols} rows={g.Rows}");
        }
    }

    [Test]
    public void SquareRegion_GivesNearlySquareGrid()
    {
        // count=170 → cols=ceil(sqrt(170))=14, rows=ceil(170/14)=13
        // 정확히 정사각형일 필요는 없고 "가깝기만" 하면 된다.
        var g = ScatterGrid.Create(170, 10, 10, 980, 980);
        Assert.AreEqual(14, g.Cols);
        Assert.AreEqual(13, g.Rows);
        Assert.LessOrEqual(System.Math.Abs(g.Cols - g.Rows), 1);
    }

    [Test]
    public void WideRegion_GivesMoreColumnsThanRows()
    {
        var g = ScatterGrid.Create(100, 0, 0, 4000, 1000);
        Assert.Greater(g.Cols, g.Rows);
    }

    [Test]
    public void DegenerateInputs_GiveEmptyGrid()
    {
        Assert.AreEqual(0, ScatterGrid.Create(0, 0, 0, 980, 980).CellCount);
        Assert.AreEqual(0, ScatterGrid.Create(-5, 0, 0, 980, 980).CellCount);
        Assert.AreEqual(0, ScatterGrid.Create(10, 0, 0, 0, 980).CellCount);
        Assert.AreEqual(0, ScatterGrid.Create(10, 0, 0, 980, 0).CellCount);
    }

    [Test]
    public void TinyRegion_ClampsGridToPixelSize()
    {
        // 셀 너비가 1px 미만이 되면 안 된다. 이 경우 CellCount < count가 되며,
        // 호출측이 pointIndex >= CellCount로 중단한다.
        var g = ScatterGrid.Create(100, 0, 0, 5, 5);
        Assert.LessOrEqual(g.Cols, 5);
        Assert.LessOrEqual(g.Rows, 5);
    }

    // ─── ScatterGrid.PointAt ───────────────────────────────────────

    [Test]
    public void ZeroJitter_LandsOnCellCenter()
    {
        // 4x4 격자, 영역 0..400 → 셀 100px, 첫 셀 중심 = 50
        var g = ScatterGrid.Create(16, 0, 0, 400, 400);
        Assert.AreEqual(4, g.Cols);
        Assert.AreEqual(4, g.Rows);

        g.PointAt(0, 0.0, 0.0, 0f, out int x0, out int y0);
        Assert.AreEqual(50, x0);
        Assert.AreEqual(50, y0);

        // roll을 뭘 주든 jitter 0이면 중심이다
        g.PointAt(0, 0.99, 0.99, 0f, out int x1, out int y1);
        Assert.AreEqual(50, x1);
        Assert.AreEqual(50, y1);

        // 인덱스 5 = (col 1, row 1) → 중심 (150, 150)
        g.PointAt(5, 0.5, 0.5, 0f, out int x2, out int y2);
        Assert.AreEqual(150, x2);
        Assert.AreEqual(150, y2);
    }

    [Test]
    public void FullJitter_SpansTheWholeCell()
    {
        var g = ScatterGrid.Create(16, 0, 0, 400, 400);

        g.PointAt(0, 0.0, 0.0, 1f, out int lowX, out int lowY);
        g.PointAt(0, 1.0, 1.0, 1f, out int highX, out int highY);

        Assert.AreEqual(0, lowX);    // 셀 왼쪽 끝
        Assert.AreEqual(0, lowY);
        Assert.AreEqual(99, highX);  // 셀 오른쪽 끝(마지막 픽셀) — 셀 밖으로 넘어가면 안 된다
        Assert.AreEqual(99, highY);
    }

    [Test]
    public void EveryCellStaysInsideItsOwnColumnAndRow()
    {
        // 셀 배타성: 서로 다른 셀의 점은 서로 다른 셀 영역 안에 있어야 한다.
        // 이게 깨지면 "셀당 1개"가 무의미해지고 뭉침이 되살아난다.
        // 셀 경계가 정수로 떨어지는 크기를 골라 경계 판정을 엄밀하게 본다.
        var g = ScatterGrid.Create(16, 10, 10, 400, 400);
        Assert.AreEqual(4, g.Cols);
        double cellW = 400.0 / g.Cols;
        double cellH = 400.0 / g.Rows;

        for (int cell = 0; cell < g.CellCount; cell++)
        {
            int cx = cell % g.Cols;
            int cy = cell / g.Cols;

            foreach (double roll in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                g.PointAt(cell, roll, roll, 1f, out int x, out int y);

                Assert.GreaterOrEqual(x, 10 + cx * cellW, $"cell={cell} roll={roll}");
                Assert.Less(x, 10 + (cx + 1) * cellW, $"cell={cell} roll={roll}");
                Assert.GreaterOrEqual(y, 10 + cy * cellH, $"cell={cell} roll={roll}");
                Assert.Less(y, 10 + (cy + 1) * cellH, $"cell={cell} roll={roll}");
            }
        }
    }

    [Test]
    public void PointNeverEscapesTheRegion()
    {
        var g = ScatterGrid.Create(170, 10, 10, 980, 980);

        for (int cell = 0; cell < g.CellCount; cell++)
        {
            // 범위 밖 jitter도 클램프돼야 한다
            foreach (float jitter in new[] { 0f, 0.5f, 1f, 2f, -1f })
            {
                g.PointAt(cell, 0.0, 0.0, jitter, out int xLo, out int yLo);
                g.PointAt(cell, 1.0, 1.0, jitter, out int xHi, out int yHi);

                Assert.GreaterOrEqual(xLo, 10);
                Assert.GreaterOrEqual(yLo, 10);
                Assert.LessOrEqual(xHi, 10 + 980 - 1);
                Assert.LessOrEqual(yHi, 10 + 980 - 1);
            }
        }
    }

    [Test]
    public void OutOfRangeCellIndex_IsClampedNotCrashing()
    {
        var g = ScatterGrid.Create(16, 0, 0, 400, 400);
        Assert.DoesNotThrow(() => g.PointAt(-1, 0.5, 0.5, 1f, out _, out _));
        Assert.DoesNotThrow(() => g.PointAt(9999, 0.5, 0.5, 1f, out _, out _));
    }

    [Test]
    public void EmptyGrid_PointAtDoesNotCrash()
    {
        var g = ScatterGrid.Create(0, 7, 9, 980, 980);
        g.PointAt(0, 0.5, 0.5, 1f, out int x, out int y);
        Assert.AreEqual(7, x);
        Assert.AreEqual(9, y);
    }

    // ─── MineralScatter.ShuffleInPlace ─────────────────────────────

    [Test]
    public void Shuffle_KeepsEveryElementExactlyOnce()
    {
        const int N = 200;
        var buf = new int[N];
        for (int i = 0; i < N; i++) buf[i] = i;

        MineralScatter.ShuffleInPlace(buf, N, new System.Random(1234));

        var seen = new bool[N];
        foreach (int v in buf)
        {
            Assert.IsFalse(seen[v], $"{v} 중복");
            seen[v] = true;
        }
    }

    [Test]
    public void Shuffle_ActuallyReorders()
    {
        const int N = 200;
        var buf = new int[N];
        for (int i = 0; i < N; i++) buf[i] = i;

        MineralScatter.ShuffleInPlace(buf, N, new System.Random(1234));

        int samePosition = 0;
        for (int i = 0; i < N; i++) if (buf[i] == i) samePosition++;
        Assert.Less(samePosition, N / 4, "거의 안 섞였다");
    }

    [Test]
    public void Shuffle_IsDeterministicForSameSeed()
    {
        const int N = 50;
        var a = new int[N];
        var b = new int[N];
        for (int i = 0; i < N; i++) { a[i] = i; b[i] = i; }

        MineralScatter.ShuffleInPlace(a, N, new System.Random(777));
        MineralScatter.ShuffleInPlace(b, N, new System.Random(777));

        CollectionAssert.AreEqual(a, b);
    }

    [Test]
    public void Shuffle_OnlyTouchesTheRequestedPrefix()
    {
        // 정적 재사용 버퍼를 앞부분만 쓰기 때문에 뒤쪽은 건드리면 안 된다.
        var buf = new int[10];
        for (int i = 0; i < 10; i++) buf[i] = i;

        MineralScatter.ShuffleInPlace(buf, 5, new System.Random(42));

        for (int i = 5; i < 10; i++) Assert.AreEqual(i, buf[i]);
    }

    [Test]
    public void Shuffle_HandlesBadInput()
    {
        Assert.DoesNotThrow(() => MineralScatter.ShuffleInPlace(null, 5, new System.Random(1)));
        Assert.DoesNotThrow(() => MineralScatter.ShuffleInPlace(new int[3], 999, new System.Random(1)));
        Assert.DoesNotThrow(() => MineralScatter.ShuffleInPlace(new int[3], 3, null));
    }
}
