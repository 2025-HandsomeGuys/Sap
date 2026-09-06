// @tags: dungeon, player, spawn, scene
using UnityEngine;

/// <summary>
/// 던전 씬용 플레이어 스포너. 오버세계용 PlayerSpawner와 달리 청크 시스템(InfinityMapManager)을
/// 기다리지 않는다 — 던전은 청크 없이 손배치 지형이므로.
///  - 플레이어를 스폰 지점에 배치하고 즉시 물리를 켠다(Dynamic).
///  - isReturningFromDungeon 플래그는 건드리지 않는다. 이 플래그는 던전에서 나갈 때
///    오버세계 PlayerSpawner가 소비해 문 위치로 복귀시키는 데 쓰인다.
/// 던전 씬에는 이 컴포넌트를 쓰고, 오버세계용 PlayerSpawner는 두지 말 것.
/// </summary>
public class DungeonPlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject playerObj;
    [Tooltip("던전 입구 스폰 위치")]
    [SerializeField] private Vector2 spawnPosition;

    private void Start()
    {
        if (playerObj == null)
        {
            Debug.LogError("[DungeonPlayerSpawner] playerObj가 비어 있습니다.");
            return;
        }

        playerObj.transform.position = spawnPosition;
        playerObj.SetActive(true);

        var rb = playerObj.GetComponentInChildren<Rigidbody2D>();
        if (rb != null) rb.bodyType = RigidbodyType2D.Dynamic;

        Debug.Log($"[DungeonPlayerSpawner] 던전 스폰 완료: {spawnPosition}");
    }
}
