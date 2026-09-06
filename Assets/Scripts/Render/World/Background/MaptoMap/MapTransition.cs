using Unity.Cinemachine; // 시네머신 기능을 사용하기 위해 필수 추가!
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MapTransition : MonoBehaviour
{
    [Header("이동 설정")]
    public Transform targetLocation; // 도착 지점
    public float fadeDuration = 0.5f; // 페이드 시간

    [Header("UI 설정")]
    public Image fadeImage; // 검은색 화면 UI

    [Header("시네머신 설정")]
    public CinemachineCamera vcam; // 현재 캐릭터를 찍고 있는 가상 카메라
    public Collider2D newCameraBounds; // 이동할 새 맵의 컨파인더 영역(PolygonCollider2D 등)

    private bool isTransitioning = false;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && !isTransitioning)
        {
            StartCoroutine(TransitionRoutine(collision.transform));
        }
    }

    private IEnumerator TransitionRoutine(Transform playerTransform)
    {
        isTransitioning = true;

        // 1. 페이드 아웃 (화면이 점점 검게 변함)
        float timer = 0f;
        Color color = fadeImage.color;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            color.a = Mathf.Lerp(0f, 1f, timer / fadeDuration);
            fadeImage.color = color;
            yield return null;
        }

        // 화면이 완전히 까매짐
        color.a = 1f;
        fadeImage.color = color;

        // --- 카메라 및 캐릭터 순간이동 처리 시작 ---
        Vector3 previousPosition = playerTransform.position;

        // 캐릭터 위치 이동
        playerTransform.position = targetLocation.position;

        // 시네머신 카메라 처리
        if (vcam != null && newCameraBounds != null)
        {
            CinemachineConfiner2D confiner2D = vcam.GetComponent<CinemachineConfiner2D>();
            if (confiner2D != null)
            {
                confiner2D.BoundingShape2D = newCameraBounds;
                confiner2D.InvalidateBoundingShapeCache();
            }

            // 카메라가 새 위치로 즉시 워프
            vcam.OnTargetObjectWarped(playerTransform, targetLocation.position - previousPosition);

            // ★ 추가된 핵심 코드: 카메라의 이전 프레임 위치 기억을 초기화해서 스르륵 이동하는 현상 차단
            vcam.PreviousStateIsValid = false;
        }
        // --- 카메라 처리 끝 ---

        // ★ 추가된 핵심 코드: 시네머신이 새 위치로 카메라를 완전히 업데이트할 수 있도록 암전 상태에서 1프레임 대기
        yield return null;

        yield return new WaitForSeconds(0.2f); // 맵 로딩 체감 및 안정성을 위한 대기

        // 2. 페이드 인 (화면이 다시 밝아짐)
        timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            color.a = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            fadeImage.color = color;
            yield return null;
        }

        color.a = 0f;
        fadeImage.color = color;

        isTransitioning = false;
    }
}