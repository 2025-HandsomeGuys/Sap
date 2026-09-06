using UnityEngine;

/// <summary>
/// 던전 내 함정이나 특정 물체에 닿았을 때 플레이어를 던전 입구(DungeonEntryPoint)로 되돌려 보내는 트리거 스크립트입니다.
/// 이 컴포넌트가 부착된 오브젝트는 Collider2D(Is Trigger 체크 권장)를 가지고 있어야 합니다.
/// </summary>
public class DungeonResetTrigger : MonoBehaviour
{
    private Vector3 _entryPosition;
    private bool _hasEntryPosition;

    private void Start()
    {
        // 최상위 던전 프리팹에서 DungeonEntryPoint를 찾습니다.
        var root = transform.root;
        var entryPoint = root.GetComponentInChildren<DungeonEntryPoint>();

        if (entryPoint != null)
        {
            _entryPosition = entryPoint.transform.position;
            _hasEntryPosition = true;
        }
        else
        {
            Debug.LogWarning($"[DungeonResetTrigger] '{gameObject.name}'의 상위에서 DungeonEntryPoint를 찾을 수 없습니다.");
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            ResetPlayerPosition(collision.gameObject);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            ResetPlayerPosition(collision.gameObject);
        }
    }

    private void ResetPlayerPosition(GameObject player)
    {
        if (!_hasEntryPosition) return;

        // 물리 속도가 남아있으면 텔레포트 후 미끄러질 수 있으므로 속도를 초기화합니다.
        var rb = player.GetComponentInParent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }

        // 플레이어 위치를 입구로 이동
        player.transform.position = _entryPosition;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
        GameDiagnostics.TerrainSeamWatchdog.SuppressFor();
#endif
    }
}
