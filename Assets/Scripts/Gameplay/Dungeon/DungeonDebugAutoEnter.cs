// @tags: dungeon, debug, test, auto-enter, entry
using System.Collections;
using UnityEngine;

/// <summary>
/// [디버그 전용] 던전 문을 거치지 않고 Play 시 지정한 던전으로 즉시 자동 진입한다.
/// 지하 씬에 빈 GameObject를 만들어 이 컴포넌트를 붙이고 dungeonPrefab만 꽂으면,
/// 실제 <see cref="DungeonOverlayController.EnterDungeon"/> 경로를 그대로 태워 진입한다
/// (offset 격리·앵커 스왑·SuppressVoidFreeze·카메라 셋업 모두 프로덕션과 동일).
///
/// 던전 레이아웃/기믹을 빠르게 반복 테스트할 때 문 상호작용 단계만 건너뛰는 용도.
/// 기본적으로 에디터에서만 동작하며, 빌드에 남아도 runInBuild=false면 아무 일도 하지 않는다.
/// </summary>
public class DungeonDebugAutoEnter : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("자동 진입할 던전 지오메트리 프리팹 (DungeonEntryPoint 포함)")]
    [SerializeField] private GameObject dungeonPrefab;

    [Tooltip("저장 상태 격리용 테스트 인스턴스 좌표. 실제 던전 문 좌표와 겹치지 않는 값을 쓴다.")]
    [SerializeField] private Vector2Int testInstanceCoord = new Vector2Int(-9999, -9999);

    [Header("Timing")]
    [Tooltip("Play 후 이만큼(초) 기다렸다가 진입 시도. 지하 청크가 먼저 로드될 시간을 준다.")]
    [SerializeField] private float startupDelay = 0.5f;

    [Tooltip("맵/플레이어가 준비될 때까지 최대 대기 시간(초).")]
    [SerializeField] private float maxWaitTime = 10f;

    [Header("Safety")]
    [Tooltip("체크하면 빌드(에디터 외)에서도 동작한다. 기본 false — 에디터 전용.")]
    [SerializeField] private bool runInBuild = false;

    private void Start()
    {
        if (!Application.isEditor && !runInBuild)
        {
            enabled = false;
            return;
        }
        if (dungeonPrefab == null)
        {
            Debug.LogWarning("[DungeonDebugAutoEnter] dungeonPrefab이 비어있어 자동 진입을 건너뜁니다.", this);
            return;
        }
        StartCoroutine(AutoEnterRoutine());
    }

    private IEnumerator AutoEnterRoutine()
    {
        if (startupDelay > 0f) yield return new WaitForSeconds(startupDelay);

        // 맵과 플레이어가 준비될 때까지 대기 (없으면 EnterDungeon 내부에서 실패).
        float elapsed = 0f;
        while ((InfinityMapManager.Instance == null ||
                GameObject.FindGameObjectWithTag("Player") == null) &&
               elapsed < maxWaitTime)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (GameObject.FindGameObjectWithTag("Player") == null)
        {
            Debug.LogError("[DungeonDebugAutoEnter] 'Player' 태그 오브젝트를 못 찾아 자동 진입을 중단합니다.", this);
            yield break;
        }

        Debug.Log($"[DungeonDebugAutoEnter] '{dungeonPrefab.name}' 자동 진입 (coord={testInstanceCoord}).", this);
        DungeonOverlayController.Instance.EnterDungeon(dungeonPrefab, testInstanceCoord);
    }
}
