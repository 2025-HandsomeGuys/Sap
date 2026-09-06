// @tags: special-chunk, generation, spawn, chunk, multi-chunk
using UnityEngine;

/// <summary>
/// 압축 쓰레기 벽을 3개 연달아 배치하는 생성기.
///
/// SOLID — SRP: 패턴 배치만 담당. HP·드롭·비주얼과 완전 분리.
/// OCP: blockCount나 offset만 바꿔 다른 패턴(L자, 세로 등)으로 확장 가능.
///
/// 사용 방법:
///  - 루트 TrashWall 프리팹에만 부착. 추가 블록(복사본)에는 부착하지 않는다.
///  - SpecialChunkManager.SpawnSpecialChunkIfPossible() 이후
///    chunk.GetComponent&lt;TrashWallGenerator&gt;()?.SpawnPattern(parent) 호출.
/// </summary>
public class TrashWallGenerator : MonoBehaviour, IChunkInitializer
{
    public int InitializationOrder => 0;

    /// <inheritdoc/>
    public void Initialize(Transform parent) => SpawnPattern(parent);

    [Header("배치 설정")]
    [Tooltip("추가로 생성할 블록 수. 기본 1개 + 여기서 설정한 수 = 전체 블록 수.")]
    public int extraBlockCount = 2; // 1(루트) + 2(추가) = 3개 (기획 명세)

    [Tooltip("블록 간격 오프셋 (유닛). 기본은 가로 0.5u 간격.")]
    public Vector2 blockOffset = new Vector2(0.5f, 0f);

    [Tooltip("추가 블록 프리팹. 루트 블록과 동일한 프리팹 사용 권장.")]
    public GameObject blockPrefab;

    /// <summary>
    /// 이 오브젝트를 기준으로 extraBlockCount만큼 추가 블록을 옆에 배치한다.
    /// SpecialChunkManager에서 호출.
    /// </summary>
    public void SpawnPattern(Transform parent)
    {
        if (blockPrefab == null)
        {
            Debug.LogWarning("[TrashWallGenerator] blockPrefab이 비어 있습니다.");
            return;
        }

        for (int i = 1; i <= extraBlockCount; i++)
        {
            Vector3 offset = new Vector3(blockOffset.x * i, blockOffset.y * i, 0f);
            GameObject block = Instantiate(blockPrefab, transform.position + offset, Quaternion.identity, parent);
            block.name = $"TrashWall_Extra_{i}";
        }

        Debug.Log($"[TrashWallGenerator] 배치 완료 — 총 {1 + extraBlockCount}개 블록");
    }
}
