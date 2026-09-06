// @tags: dungeon, trap, route, waypoint, moving, obstacle
using UnityEngine;

/// <summary>
/// 웨이포인트를 왕복하는 장애물. <see cref="RouteAutoLinker"/>가 런타임에 스폰하며 값을 주입한다.
///
/// 속도는 **구간(edge)마다** 다를 수 있다. <see cref="edgeSpeedMultipliers"/>[i]는 웨이포인트
/// i-1 ↔ i 사이 구간의 배율이며, 양방향 모두 같은 값을 쓴다(가는 길과 오는 길이 달라지면
/// 저작자가 예측할 수 없다). 0번은 들어오는 구간이 없어 쓰이지 않는다.
/// </summary>
public class MovingObstacle : MonoBehaviour
{
    [HideInInspector] public float speed = 5f;
    [HideInInspector] public float waitTimeAtEnds = 0f;
    [HideInInspector] public float rotationSpeed = 0f; // 추가된 회전 속도 변수
    [HideInInspector] public Transform[] waypoints;

    /// <summary>[i] = 웨이포인트 i-1 ↔ i 구간의 속도 배율. 비어 있으면 전 구간 1배.</summary>
    [HideInInspector] public float[] edgeSpeedMultipliers;

    /// <summary>[i] = 웨이포인트 i에 도착했을 때 멈춰 있는 시간(초). 비어 있으면 안 멈춘다.</summary>
    [HideInInspector] public float[] waypointPauses;

    private int targetIndex = 0;
    private bool isMovingForward = true;

    private bool isWaiting = false;
    private float waitTimer = 0f;
    private float waitDuration = 0f;

    void Update()
    {
        // 1. 회전 로직: 대기 중일 때나 이동 중일 때 항상 빙글빙글 돕니다.
        // 2D 스프라이트이므로 Z축(0, 0, 회전값)을 기준으로 회전시킵니다.
        if (rotationSpeed != 0f)
        {
            transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
        }

        if (waypoints == null || waypoints.Length < 2) return;

        // 2. 대기 중일 때는 타이머만 흘러가고 이동 로직은 건너뜁니다.
        if (isWaiting)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= waitDuration)
            {
                isWaiting = false;
                waitTimer = 0f;
                UpdateTargetIndex();
            }
            return;
        }

        // 3. 평상시 이동 로직 — 지금 지나는 구간의 배율을 곱한다.
        Transform target = waypoints[targetIndex];
        float step = speed * CurrentSpeedMultiplier() * Time.deltaTime;
        transform.position = Vector2.MoveTowards(transform.position, target.position, step);

        // 4. 목표 점에 도달했을 때의 처리
        if (Vector2.Distance(transform.position, target.position) < 0.05f)
        {
            bool reachedEnd = (isMovingForward && targetIndex == waypoints.Length - 1);
            bool reachedStart = (!isMovingForward && targetIndex == 0);

            // 끝단 대기와 구간 정지 중 긴 쪽을 쓴다. 끝단에 정지 지점이 겹쳐도 두 번 안 쉰다.
            float pause = PauseAt(targetIndex);
            if (reachedEnd || reachedStart) pause = Mathf.Max(pause, waitTimeAtEnds);

            if (pause > 0f)
            {
                isWaiting = true;
                waitDuration = pause;
            }
            else
            {
                UpdateTargetIndex();
            }
        }
    }

    // 지금 향하고 있는 구간의 배율. 진행 방향과 무관하게 같은 구간이면 같은 값이 나온다.
    private float CurrentSpeedMultiplier()
    {
        if (edgeSpeedMultipliers == null || edgeSpeedMultipliers.Length != waypoints.Length)
            return 1f;

        int edge = isMovingForward ? targetIndex : targetIndex + 1;
        if (edge <= 0 || edge >= edgeSpeedMultipliers.Length) return 1f;

        float mul = edgeSpeedMultipliers[edge];
        return mul > 0f ? mul : 1f;
    }

    private float PauseAt(int index)
    {
        if (waypointPauses == null || waypointPauses.Length != waypoints.Length) return 0f;
        if (index < 0 || index >= waypointPauses.Length) return 0f;
        return Mathf.Max(0f, waypointPauses[index]);
    }

    void UpdateTargetIndex()
    {
        if (isMovingForward)
        {
            targetIndex++;
            if (targetIndex >= waypoints.Length)
            {
                targetIndex = waypoints.Length - 2;
                isMovingForward = false;
            }
        }
        else
        {
            targetIndex--;
            if (targetIndex < 0)
            {
                targetIndex = 1;
                isMovingForward = true;
            }
        }
    }
}
