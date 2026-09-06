// @tags: underground, session, scene, coroutine, player, exploration
using System.Collections;
using UnityEngine;

/// <summary>
/// 지하 씬 진입 시 탐험 추적을 자동으로 시작하는 컴포넌트.
/// 씬 로드 직후 플레이어 초기화 타이밍 문제를 방지하기 위해 코루틴으로 대기 후 실행.
/// </summary>
public class UndergroundSessionStarter : MonoBehaviour
{
    private const int MaxRetryFrames = 5;

    private IEnumerator Start()
    {
        // 씬 로드 직후에는 플레이어가 아직 Awake/Start를 마치지 않았을 수 있어
        // 최소 1프레임 대기 후 탐색 (최대 MaxRetryFrames 프레임 재시도)
        for (int i = 0; i < MaxRetryFrames; i++)
        {
            yield return null;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && SettlementManager.Instance != null)
            {
                SettlementManager.Instance.StartTracking(player.transform, player.transform.position.y);
                Debug.Log($"[UndergroundSessionStarter] 탐험 추적 시작 완료 (대기 {i + 1}프레임)");
                yield break;
            }
        }

        Debug.LogWarning("[UndergroundSessionStarter] 플레이어 또는 SettlementManager를 찾을 수 없어 추적을 시작하지 못했습니다.");
    }
}
