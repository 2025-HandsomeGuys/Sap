// @tags: mineral, scatter, distribution, grid, spawn, pure-logic
/// <summary>
/// 광물 균등 배치용 지터 격자(stratified sampling).
/// 영역을 목표 개수만큼의 셀로 나누고 셀당 점 하나를 뽑으면 구조적으로 뭉칠 수 없다.
/// Unity 비의존 순수 로직 — EditMode 테스트 대상. 난수는 전부 호출측이 주입한다.
/// 배경: Assets/Docs/mineral-even-scatter.md
/// </summary>
public struct ScatterGrid
{
    public int Cols;
    public int Rows;
    public int X0;
    public int Y0;
    public int Width;
    public int Height;

    public int CellCount => Cols * Rows;

    // 점이 셀 위쪽 경계에 정확히 걸리면 다음 셀로 새어나간다(roll=1.0 + jitter=1.0).
    // 셀 안에 묶어두기 위한 여유. 셀 크기는 최소 1px이 보장되므로 안전하다.
    private const double CELL_EPSILON = 1e-6;

    /// <summary>
    /// count개 이상을 담는 최소 격자. 셀이 정사각형에 가깝도록 영역 종횡비를 따라간다.
    /// count가 0 이하이거나 영역이 비면 CellCount 0인 격자를 준다.
    ///
    /// 주의: 영역이 아주 좁으면(셀 1px 미만) 클램프가 걸려 CellCount &lt; count가 될 수 있다.
    /// 호출측이 pointIndex &gt;= CellCount로 중단해야 한다.
    /// </summary>
    public static ScatterGrid Create(int count, int x0, int y0, int width, int height)
    {
        var g = new ScatterGrid
        {
            X0 = x0, Y0 = y0, Width = width, Height = height, Cols = 0, Rows = 0
        };
        if (count <= 0 || width <= 0 || height <= 0) return g;

        int cols = (int)System.Math.Ceiling(System.Math.Sqrt((double)count * width / height));
        if (cols < 1) cols = 1;
        if (cols > width) cols = width;        // 셀 너비 1px 미만 방지

        int rows = (count + cols - 1) / cols;  // ceil(count / cols)
        if (rows < 1) rows = 1;
        if (rows > height) rows = height;

        g.Cols = cols;
        g.Rows = rows;
        return g;
    }

    /// <summary>
    /// 셀 중심 기준으로 지터를 적용한 점. jitter 0 = 정확히 셀 중심, 1 = 셀 전체.
    /// rollX/rollY는 [0,1] 균등 난수. 결과는 항상 영역 안이며 해당 셀 밖으로 나가지 않는다.
    /// </summary>
    public void PointAt(int cellIndex, double rollX, double rollY, float jitter, out int x, out int y)
    {
        x = X0;
        y = Y0;
        if (Cols <= 0 || Rows <= 0) return;

        int cellCount = Cols * Rows;
        if (cellIndex < 0) cellIndex = 0;
        else if (cellIndex >= cellCount) cellIndex = cellCount - 1;

        int cx = cellIndex % Cols;
        int cy = cellIndex / Cols;

        double cellW = (double)Width / Cols;
        double cellH = (double)Height / Rows;
        double j = Clamp01(jitter);

        double cellX0 = X0 + cx * cellW;
        double cellY0 = Y0 + cy * cellH;

        double px = cellX0 + (0.5 + (Clamp01(rollX) - 0.5) * j) * cellW;
        double py = cellY0 + (0.5 + (Clamp01(rollY) - 0.5) * j) * cellH;

        px = ClampDouble(px, cellX0, cellX0 + cellW - CELL_EPSILON);
        py = ClampDouble(py, cellY0, cellY0 + cellH - CELL_EPSILON);

        x = ClampInt((int)System.Math.Floor(px), X0, X0 + Width - 1);
        y = ClampInt((int)System.Math.Floor(py), Y0, Y0 + Height - 1);
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    private static double ClampDouble(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    private static int ClampInt(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}

/// <summary>배치 보조 유틸. 셀 방문 순서를 섞는 데 쓴다.</summary>
public static class MineralScatter
{
    /// <summary>
    /// Fisher-Yates. buffer[0..length)만 제자리에서 섞고 뒤쪽은 건드리지 않는다.
    /// 정적 재사용 버퍼를 앞부분만 쓰기 위해 length를 따로 받는다.
    /// </summary>
    public static void ShuffleInPlace(int[] buffer, int length, System.Random prng)
    {
        if (buffer == null || prng == null) return;
        if (length > buffer.Length) length = buffer.Length;

        for (int i = length - 1; i > 0; i--)
        {
            int j = prng.Next(i + 1);
            int tmp = buffer[i];
            buffer[i] = buffer[j];
            buffer[j] = tmp;
        }
    }
}
