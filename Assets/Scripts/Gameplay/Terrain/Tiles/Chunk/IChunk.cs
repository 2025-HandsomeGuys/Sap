// @tags: chunk, interface, ichunk, lifecycle, spawn, polymorphism
using UnityEngine;

/// <summary>
/// 청크 파이프라인(생성, 표시 등)에서 일반 청크(TerrainChunk)와
/// 거대 고정 청크(LargeStaticTerrainChunk)를 다형성으로 다루기 위한 공통 인터페이스.
/// </summary>
public interface IChunk
{
    Vector2Int Coord   { get; }
    int        Width   { get; } // 픽셀 단위 너비
    int        Height  { get; } // 픽셀 단위 높이
    GameObject gameObject { get; }

    /// <summary>
    /// 스폰 직후 한 번 호출. 좌표 설정 등 타입별 초기화 처리.
    /// OCP: SpecialChunkManager가 구체 타입을 알 필요 없이 이 메서드만 호출한다.
    /// </summary>
    void OnSpawned(Vector2Int coord);

    /// <summary>
    /// Job 완료 전 깜빡임 방지를 위해 스폰 직후 SetActive(false) 유지가 필요하면 true.
    /// OCP: SpecialChunkManager의 타입 검사를 제거한다.
    /// </summary>
    bool NeedsDelayedActivation { get; }
}
