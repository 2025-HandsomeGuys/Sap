// @tags: interface, terrain, manager, dig, chunk, world
using UnityEngine;

/// <summary>
/// 지형 수정(땅파기)을 제공하는 인터페이스.
/// Digger는 InfinityMapManager 대신 이 인터페이스에만 의존한다.
///
/// [SOLID]
///   DIP: Digger가 구체 클래스(InfinityMapManager)가 아닌 인터페이스에 의존.
///   OCP: StaticChunkTerrainManager 등 새 구현체를 추가하는 것만으로 테스트/확장 가능.
/// </summary>
public interface ITerrainManager
{
    /// <summary>청크의 월드 높이 (유닛). Digger의 깊이 계산에 사용.</summary>
    float chunkHeightWorld { get; }

    /// <summary>지정 위치의 지형을 파낸다.</summary>
    /// <param name="applySmoothing">false이면 ErodeEdges/RemoveNarrowProtrusions/CheckFloatingIslands를 skip. ImmediateDig 전용.</param>
    void ModifyTerrain(Vector2 worldPos, float radius, int toolIndex, bool applySmoothing = true);

    /// <summary>지정 위치를 중심으로 원형 폭발 형태의 지형을 파괴한다.</summary>
    void ExplodeTerrain(Vector2 worldPos, float radius);
}
