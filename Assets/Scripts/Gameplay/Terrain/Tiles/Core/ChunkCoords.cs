// @tags: coordinate, chunk-coord, world-coord, conversion, static
using UnityEngine;

/// <summary>
/// 청크 좌표 ↔ 월드 좌표 변환 단일 진실 공급원(Single Source of Truth).
/// 모든 청크 위치 계산은 이 클래스를 통한다.
///
/// 규칙:
///   - 청크 1칸 = WorldSize world units (현재 10f = 1000px / 100ppu)
///   - 청크 피벗은 좌하단 (0, 0) 기준
///   - WorldSize 변경 시 이 파일의 상수 하나만 수정하면 전체 반영
///
/// [SOLID] SRP: 좌표 변환 계산만 담당. MonoBehaviour 없음, 싱글턴 아님.
/// </summary>
public static class ChunkCoords
{
    /// <summary>청크 1칸의 월드 크기 (world units). 1000px / 100ppu = 10f.</summary>
    public const float WorldSize = 10f;

    /// <summary>청크 좌표 → 월드 위치 (좌하단 피벗 기준)</summary>
    public static Vector3 ToWorld(Vector2Int coord)
        => new Vector3(coord.x * WorldSize, coord.y * WorldSize, 0f);

    /// <summary>월드 위치 → 청크 좌표 (floor 기반)</summary>
    public static Vector2Int ToChunk(Vector3 worldPos)
        => new Vector2Int(
            Mathf.FloorToInt(worldPos.x / WorldSize),
            Mathf.FloorToInt(worldPos.y / WorldSize));

    /// <summary>
    /// 청크 단위 오프셋 → 월드 오프셋.
    /// 멀티청크 서브 위치 계산 시 사용: anchorCoord + OffsetToWorld(subOffset)
    /// </summary>
    public static Vector3 OffsetToWorld(Vector2Int offset)
        => new Vector3(offset.x * WorldSize, offset.y * WorldSize, 0f);
}
