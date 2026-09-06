using UnityEngine;
using System.Collections;

public class LavaMovement : MonoBehaviour
{
    [Header("이동 설정 (인스펙터에서 조정 가능)")]
    [Tooltip("현재 위치에서 위로 얼마나 올라갈지 결정합니다.")]
    public float moveUpDistance = 2f;
    
    [Tooltip("위/아래로 이동하는 데 걸리는 시간입니다.")]
    public float moveDuration = 1.5f;

    [Tooltip("가장 높이 올라갔을 때 머무르는 시간입니다.")]
    public float waitAtTopDuration = 2f;

    [Tooltip("가장 아래로 내려왔을 때 머무르는 시간입니다.")]
    public float waitAtBottomDuration = 2f;

    private Vector3 bottomPosition;
    private Vector3 topPosition;

    void Start()
    {
        // 시작할 때의 위치를 가장 아래 위치로 설정하고, 
        // 목표 위치(가장 위)를 계산해둡니다.
        bottomPosition = transform.position;
        topPosition = transform.position + new Vector3(0, moveUpDistance, 0);

        // 위아래로 움직이는 사이클을 시작합니다.
        StartCoroutine(MoveCycle());
    }

    IEnumerator MoveCycle()
    {
        while (true) // 무한 반복
        {
            // 1. 위로 이동
            yield return StartCoroutine(MoveToPosition(topPosition, moveDuration));
            
            // 2. 위에서 대기
            yield return new WaitForSeconds(waitAtTopDuration);

            // 3. 아래로 이동
            yield return StartCoroutine(MoveToPosition(bottomPosition, moveDuration));

            // 4. 아래에서 대기
            yield return new WaitForSeconds(waitAtBottomDuration);
        }
    }

    // 목표 위치까지 부드럽게 이동시키는 코루틴
    IEnumerator MoveToPosition(Vector3 targetPos, float duration)
    {
        float timeElapsed = 0f;
        Vector3 startPos = transform.position;

        while (timeElapsed < duration)
        {
            // Lerp를 사용하여 시작 위치와 목표 위치 사이를 시간에 따라 부드럽게 보간합니다.
            transform.position = Vector3.Lerp(startPos, targetPos, timeElapsed / duration);
            timeElapsed += Time.deltaTime;
            
            // 다음 프레임까지 대기
            yield return null; 
        }

        // 오차 보정을 위해 마지막에 정확한 목표 위치로 설정해줍니다.
        transform.position = targetPos;
    }
}
