using NUnit.Framework;
using UnityEngine;

/// <summary>
/// GridCoordinateSystem 배열 인덱스 변환 테스트.
/// 청크 좌표 ↔ 배열 인덱스 왕복 정확성과 Y축 부호 반전을 검증한다.
/// </summary>
public class GridCoordinateSystemTests
{
    // terrainWidth=50, terrainDepth=36 (일반적인 게임 설정)
    private GridCoordinateSystem _grid;

    [SetUp]
    public void SetUp()
    {
        _grid = new GridCoordinateSystem(terrainWidth: 50, terrainDepth: 36);
    }

    // ── 왕복 테스트 ──────────────────────────────────────────────────────────

    [Test]
    public void ToWorldCoord_AfterToArray_ReturnsOriginal_ChunkZero()
    {
        int cx = 0, cy = 0;
        var result = _grid.ToWorldCoord(_grid.ToArrayX(cx), _grid.ToArrayY(cy));
        Assert.AreEqual(new Vector2Int(cx, cy), result);
    }

    [Test]
    public void ToWorldCoord_AfterToArray_ReturnsOriginal_NegativeChunk()
    {
        int cx = -10, cy = -7;
        var result = _grid.ToWorldCoord(_grid.ToArrayX(cx), _grid.ToArrayY(cy));
        Assert.AreEqual(new Vector2Int(cx, cy), result);
    }

    [Test]
    public void ToWorldCoord_AfterToArray_ReturnsOriginal_PositiveChunk()
    {
        int cx = 20, cy = 0;
        var result = _grid.ToWorldCoord(_grid.ToArrayX(cx), _grid.ToArrayY(cy));
        Assert.AreEqual(new Vector2Int(cx, cy), result);
    }

    // ── Y축 부호 반전 ─────────────────────────────────────────────────────

    [Test]
    public void ToArrayY_ChunkYNegative_ReturnsPositiveIndex()
    {
        // 지하 Y 좌표는 배열에서 양수 인덱스로 저장됨
        Assert.AreEqual(5, _grid.ToArrayY(-5));
    }

    [Test]
    public void ToArrayY_ChunkYZero_ReturnsZero()
    {
        Assert.AreEqual(0, _grid.ToArrayY(0));
    }

    // ── ToArrayX 오프셋 ───────────────────────────────────────────────────

    [Test]
    public void ToArrayX_MinusTerrainWidth_ReturnsZero()
    {
        // 가장 왼쪽 청크 (-50) → 배열 인덱스 0
        Assert.AreEqual(0, _grid.ToArrayX(-50));
    }

    [Test]
    public void ToArrayX_Zero_ReturnsMidpoint()
    {
        // 청크 0 → 배열 중간 (terrainWidth = 50)
        Assert.AreEqual(50, _grid.ToArrayX(0));
    }

    // ── IsValidArrayIndex ─────────────────────────────────────────────────

    [Test]
    public void IsValidArrayIndex_Origin_IsValid()
    {
        Assert.IsTrue(_grid.IsValidArrayIndex(0, 0));
    }

    [Test]
    public void IsValidArrayIndex_MaxBounds_IsValid()
    {
        // xArraySize = 50*2+1 = 101, yArraySize = 36+1 = 37
        Assert.IsTrue(_grid.IsValidArrayIndex(100, 36));
    }

    [Test]
    public void IsValidArrayIndex_NegativeX_IsInvalid()
    {
        Assert.IsFalse(_grid.IsValidArrayIndex(-1, 0));
    }

    [Test]
    public void IsValidArrayIndex_ExceedMaxX_IsInvalid()
    {
        Assert.IsFalse(_grid.IsValidArrayIndex(101, 0));
    }

    [Test]
    public void IsValidArrayIndex_ExceedMaxY_IsInvalid()
    {
        Assert.IsFalse(_grid.IsValidArrayIndex(0, 37));
    }
}
