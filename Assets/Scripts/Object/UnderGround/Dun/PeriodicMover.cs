using System.Collections;
using UnityEngine;

public class PeriodicMover : MonoBehaviour
{
    [Header("대상 및 거리 설정")]
    [Tooltip("움직일 오브젝트를 넣으세요. 비워두면 이 스크립트가 붙은 오브젝트가 움직입니다.")]
    public Transform targetObject;

    [Tooltip("위로 얼마나 올라갈지 거리를 설정합니다.")]
    public float moveDistance = 1f;

    [Header("시간 및 속도 설정")]
    [Tooltip("원위치(바닥)에서 대기하는 시간 (가시 쿨타임)")]
    public float cooldown = 2f;

    [Tooltip("위로 올라가는 데 걸리는 시간 (작을수록 확! 튀어나옴)")]
    public float moveDuration = 0.15f;

    [Tooltip("위로 다 올라간 후 멈춰있는 시간")]
    public float pauseDuration = 1.5f;

    // 위치 저장용 변수
    private Vector3 startPos;
    private Vector3 endPos;

    void Start()
    {
        // 대상이 지정되지 않았다면 자기 자신을 대상으로 설정
        if (targetObject == null)
        {
            targetObject = transform;
        }

        // 시작 위치와 목표 위치(위로 moveDistance만큼 이동한 위치) 계산
        startPos = targetObject.position;
        endPos = startPos + Vector3.up * moveDistance;

        // 함정 작동 시작
        StartCoroutine(TrapRoutine());
    }

    IEnumerator TrapRoutine()
    {
        // 무한 반복
        while (true)
        {
            // 1. 바닥에서 숨어서 대기 (쿨타임)
            yield return new WaitForSeconds(cooldown);

            // 2. 위로 솟아오르기
            yield return StartCoroutine(MoveRoutine(startPos, endPos, moveDuration));

            // 3. 위에서 대기하며 멈춰있기
            yield return new WaitForSeconds(pauseDuration);

            // 4. 다시 바닥으로 내려가기 (내려갈 때도 같은 속도 적용)
            yield return StartCoroutine(MoveRoutine(endPos, startPos, moveDuration));
        }
    }

    // 실제로 부드럽게(혹은 빠르게) 이동시켜주는 로직
    IEnumerator MoveRoutine(Vector3 from, Vector3 to, float duration)
    {
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            // 설정한 시간(duration)에 맞춰서 from에서 to로 위치를 이동시킴
            targetObject.position = Vector3.Lerp(from, to, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null; // 다음 프레임까지 대기
        }

        // 목표 위치에 정확히 맞춤 (오차 방지)
        targetObject.position = to;
    }
}