// @tags: dungeon, trap, route, waypoint, segment
using UnityEngine;

/// <summary>
/// 루트 한 조각. 이어 붙인 조각들이 하나의 왕복 경로가 되고,
/// <see cref="RouteAutoLinker"/>가 그 위에 장애물을 하나 태운다.
///
/// 구간 파라미터(<see cref="speedMultiplier"/>·<see cref="pauseSeconds"/>)는 **조각 단위**다.
/// 던전 임포터가 맵의 [LINKS] 격자를 읽어 인스턴스마다 주입하므로,
/// 빠른 구간을 만들려고 프리팹 변형을 따로 만들 필요가 없다.
/// </summary>
public class RouteSegment : MonoBehaviour
{
    [Tooltip("이 조각이 거쳐갈 점들. 0번이 시작점, 마지막이 끝점입니다.")]
    public Transform[] points;

    [Header("구간 파라미터 (임포터가 [LINKS]에서 주입)")]
    [Tooltip("이 조각을 지나는 동안의 속도 배율. 1이 기본.")]
    public float speedMultiplier = 1f;

    [Tooltip("이 조각의 끝점에 도착했을 때 멈춰 있는 시간(초). 0이면 안 멈춘다.")]
    public float pauseSeconds = 0f;

    // 편의를 위해 시작점과 끝점을 빠르게 가져오는 프로퍼티
    public Transform StartPoint => (points != null && points.Length > 0) ? points[0] : null;
    public Transform EndPoint => (points != null && points.Length > 0) ? points[points.Length - 1] : null;

    private void OnDrawGizmos()
    {
        if (points == null || points.Length < 2) return;

        // 빠른 구간일수록 붉게, 느릴수록 푸르게 — 씬 뷰에서 구간 배속을 한눈에 본다.
        // 색상환(Hue)을 돌린다. RGB로 Lerp하면 cyan↔red의 중간이 정확히 회색(0.5,0.5,0.5)이라
        // ×3 구간이 "값이 안 들어갔다"처럼 보인다.
        float hue = speedMultiplier > 1f
            ? Mathf.Lerp(0.5f, 0f, Mathf.InverseLerp(1f, 5f, speedMultiplier))      // 시안 → 초록 → 빨강
            : Mathf.Lerp(0.667f, 0.5f, Mathf.InverseLerp(0f, 1f, speedMultiplier)); // 파랑 → 시안
        Gizmos.color = Color.HSVToRGB(hue, 0.9f, 1f);

        // 배열에 들어있는 점들을 순서대로 선으로 이어줍니다. (ㄱ자 모양이 그대로 그려짐)
        for (int i = 0; i < points.Length - 1; i++)
        {
            if (points[i] != null && points[i + 1] != null)
            {
                Gizmos.DrawLine(points[i].position, points[i + 1].position);
            }
        }

        // 시작점은 둥글게, 끝점은 네모나게 그려서 방향을 표시
        if (StartPoint != null) Gizmos.DrawSphere(StartPoint.position, 0.15f);
        if (EndPoint != null) Gizmos.DrawCube(EndPoint.position, new Vector3(0.3f, 0.3f, 0.3f));

        // 정지 지점은 노란 링으로 따로 표시
        if (pauseSeconds > 0f && EndPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(EndPoint.position, 0.28f);
        }
    }
}
