// @tags: surface, spawn, placer, scene, return, player
using UnityEngine;

/// <summary>
/// 지상 씬에 하나 배치한다. 지하에서 올라온 경우 <b>내려갈 때 쓴 입구</b>에 맞춰 플레이어를 옮긴다.
/// (구멍으로 내려갔으면 구멍 앞, 엘리베이터로 내려갔으면 엘리베이터 앞)
///
/// 지정이 없거나(Transform·좌표 둘 다 비움) 기본 복귀면 아무것도 하지 않는다 —
/// 그때는 씬에 배치된 플레이어 위치, 즉 집 앞이 그대로 스폰 지점이다.
///
/// 입구 기록·전달은 <see cref="SurfaceReturnRouter"/> 담당. 여기서는 소비만 한다.
/// </summary>
public class SurfaceSpawnPlacer : MonoBehaviour
{
    [Tooltip("비워두면 \"Player\" 태그로 찾는다")]
    [SerializeField] private Transform playerObj;

    [Header("구멍으로 내려갔을 때 나올 위치")]
    [Tooltip("빈 오브젝트를 끌어다 놓으면 그 위치를 쓴다 (아래 좌표보다 우선)")]
    [SerializeField] private Transform holePoint;
    [SerializeField] private Vector2   holePosition;

    [Header("엘리베이터로 내려갔을 때 나올 위치")]
    [SerializeField] private Transform elevatorPoint;
    [SerializeField] private Vector2   elevatorPosition;

    private void Start()
    {
        SurfaceReturnPoint point = SurfaceReturnRouter.Consume();
        if (point == SurfaceReturnPoint.Default) return;

        Transform player = ResolvePlayer();
        if (player == null)
        {
            Debug.LogWarning("[SurfaceSpawnPlacer] 플레이어를 찾지 못해 위치를 바꾸지 않습니다.");
            return;
        }

        Transform anchor   = point == SurfaceReturnPoint.Hole ? holePoint    : elevatorPoint;
        Vector2   fallback = point == SurfaceReturnPoint.Hole ? holePosition : elevatorPosition;

        Vector3 target;
        if (anchor != null)
        {
            target = anchor.position;
        }
        else if (fallback != Vector2.zero)
        {
            target = new Vector3(fallback.x, fallback.y, 0f);
        }
        else
        {
            // 이 입구에 대한 지정이 없다 — 씬 배치 위치를 그대로 둔다.
            Debug.Log($"[SurfaceSpawnPlacer] {point} 복귀 위치가 지정되지 않아 기본 스폰을 유지합니다.");
            return;
        }

        target.z = player.position.z;
        player.position = target;

        // 씬 진입 직후 남아 있을 수 있는 속도를 지운다(낙하 상태로 시작하지 않도록).
        var rb = player.GetComponentInChildren<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        Debug.Log($"[SurfaceSpawnPlacer] {point} 입구로 내려갔었음 → {target}에 배치");
    }

    private Transform ResolvePlayer()
    {
        if (playerObj != null) return playerObj;

        var tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged != null ? tagged.transform : null;
    }
}
