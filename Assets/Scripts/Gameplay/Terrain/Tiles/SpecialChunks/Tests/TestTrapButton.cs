// @tags: test, trap, rock, special-chunk, trigger
using UnityEngine;

/// <summary>
/// [테스트 전용] 플레이어가 닿으면 연결된 RollingRockEntity.Activate()를 호출하는 간이 함정 버튼.
/// Stage 1 물리 테스트용 — Stage 4에서 RollingRockTrap으로 교체 예정.
/// </summary>
public class TestTrapButton : MonoBehaviour
{
    [Tooltip("활성화할 바위 엔티티")]
    public RollingRockEntity rockEntity;

    private bool _triggered;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_triggered) return;
        if (!other.CompareTag("Player")) return;

        _triggered = true;
        Debug.Log("[TestTrapButton] 플레이어 감지 → Activate()");
        rockEntity?.Activate();
    }
}
