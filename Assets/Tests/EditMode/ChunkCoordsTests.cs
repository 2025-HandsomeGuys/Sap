using NUnit.Framework;
using UnityEngine;

/// <summary>
/// ChunkCoords 좌표 변환 왕복·경계 테스트.
/// 청크 좌표 ↔ 월드 좌표 변환의 정확성을 검증한다.
/// </summary>
public class ChunkCoordsTests
{
    // ── 왕복 테스트 ──────────────────────────────────────────────────────────

    [Test]
    public void ToChunk_AfterToWorld_ReturnsOriginalCoord_Positive()
    {
        var original = new Vector2Int(3, 0);
        Assert.AreEqual(original, ChunkCoords.ToChunk(ChunkCoords.ToWorld(original)));
    }

    [Test]
    public void ToChunk_AfterToWorld_ReturnsOriginalCoord_Negative()
    {
        var original = new Vector2Int(-5, -3);
        Assert.AreEqual(original, ChunkCoords.ToChunk(ChunkCoords.ToWorld(original)));
    }

    [Test]
    public void ToChunk_AfterToWorld_ReturnsOriginalCoord_Zero()
    {
        var original = new Vector2Int(0, 0);
        Assert.AreEqual(original, ChunkCoords.ToChunk(ChunkCoords.ToWorld(original)));
    }

    // ── Y축 경계값 테스트 ─────────────────────────────────────────────────

    [Test]
    public void ToChunk_WorldY_JustBelowZero_ReturnsChunkMinusOne()
    {
        // 청크 (0,-1)의 상단 경계 바로 안쪽
        var worldPos = new Vector3(0f, -0.001f, 0f);
        Assert.AreEqual(-1, ChunkCoords.ToChunk(worldPos).y);
    }

    [Test]
    public void ToChunk_WorldY_ExactlyZero_ReturnsChunkZero()
    {
        var worldPos = new Vector3(0f, 0f, 0f);
        Assert.AreEqual(0, ChunkCoords.ToChunk(worldPos).y);
    }

    [Test]
    public void ToChunk_WorldY_InsideChunk_ReturnsChunkZero()
    {
        // 청크 (0,0) 내부 — 9.999는 아직 chunk 0
        var worldPos = new Vector3(0f, 9.999f, 0f);
        Assert.AreEqual(0, ChunkCoords.ToChunk(worldPos).y);
    }

    [Test]
    public void ToChunk_WorldY_ExactlyTen_ReturnsChunkOne()
    {
        // 정확히 10.0이면 다음 청크
        var worldPos = new Vector3(0f, 10f, 0f);
        Assert.AreEqual(1, ChunkCoords.ToChunk(worldPos).y);
    }

    [Test]
    public void ToChunk_WorldY_NegativeTen_ReturnsChunkMinusOne()
    {
        // 청크 (-1)의 하단: 월드 -10.0 → 청크 Y = -1
        var worldPos = new Vector3(0f, -10f, 0f);
        Assert.AreEqual(-1, ChunkCoords.ToChunk(worldPos).y);
    }

    [Test]
    public void ToChunk_WorldY_JustAboveNegativeTen_ReturnsChunkMinusOne()
    {
        // -9.999는 여전히 청크 -1
        var worldPos = new Vector3(0f, -9.999f, 0f);
        Assert.AreEqual(-1, ChunkCoords.ToChunk(worldPos).y);
    }

    // ── ToWorld 위치 검증 ─────────────────────────────────────────────────

    [Test]
    public void ToWorld_ChunkZero_ReturnsOrigin()
    {
        var world = ChunkCoords.ToWorld(new Vector2Int(0, 0));
        Assert.AreEqual(Vector3.zero, world);
    }

    [Test]
    public void ToWorld_ChunkMinusThree_ReturnsCorrectY()
    {
        var world = ChunkCoords.ToWorld(new Vector2Int(0, -3));
        Assert.AreEqual(-30f, world.y, 0.001f);
    }

    [Test]
    public void ToWorld_WorldSize_Is10()
    {
        // 청크 1칸 = 10 world units 고정 확인
        Assert.AreEqual(10f, ChunkCoords.WorldSize);
    }
}
