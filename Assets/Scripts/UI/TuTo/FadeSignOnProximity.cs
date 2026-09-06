using System.Collections;
using UnityEngine;
using TMPro; // TextMeshPro를 조작하기 위해 필요합니다.

public class FadeSignOnProximity : MonoBehaviour
{
    [Header("맵에 배치한 글자/사진 그룹(SignVisuals)")]
    public GameObject visualsToToggle;
    
    [Header("페이드 인/아웃 걸리는 시간 (초)")]
    public float fadeDuration = 0.5f;

    [Header("옵션")]
    [Tooltip("체크하면 외부에서 ActivateSign()을 호출하기 전까지는 플레이어가 다가가도 반응하지 않습니다.")]
    public bool waitEventToShow = false;

    private SpriteRenderer[] sprites;
    private TMP_Text[] texts;
    private Coroutine fadeCoroutine;

    private bool isActivated = true;
    private bool isPlayerInside = false;

    private void Start()
    {
        if (waitEventToShow)
        {
            isActivated = false;
        }

        if (visualsToToggle != null)
        {
            // 그룹 안(자식 포함)에 있는 모든 사진과 글자 컴포넌트를 한 번에 찾습니다.
            sprites = visualsToToggle.GetComponentsInChildren<SpriteRenderer>();
            texts = visualsToToggle.GetComponentsInChildren<TMP_Text>();
            
            // 시작 시 투명도를 0으로 만들어서 안 보이게 합니다.
            SetAlpha(0f);
        }
    }

    public void ActivateSign()
    {
        isActivated = true;
        // 만약 활성화 시점에 이미 플레이어가 범위 안에 있다면 즉시 페이드 인 시작
        if (isPlayerInside)
        {
            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            fadeCoroutine = StartCoroutine(FadeRoutine(1f));
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInside = true;
            if (isActivated)
            {
                // 기존에 진행 중이던 페이드 효과를 멈추고, 페이드 인(목표 투명도 1)을 시작합니다.
                if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
                fadeCoroutine = StartCoroutine(FadeRoutine(1f));
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInside = false;
            if (isActivated)
            {
                // 플레이어가 나가면 페이드 아웃(목표 투명도 0)을 시작합니다.
                if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
                fadeCoroutine = StartCoroutine(FadeRoutine(0f));
            }
        }
    }

    // 시간에 따라 투명도를 서서히 바꾸는 코루틴
    private IEnumerator FadeRoutine(float targetAlpha)
    {
        // 현재 투명도를 가져옵니다. (첫 번째 사진이나 글자의 투명도 기준)
        float currentAlpha = 0f;
        if (sprites.Length > 0) currentAlpha = sprites[0].color.a;
        else if (texts.Length > 0) currentAlpha = texts[0].color.a;

        float time = 0f;

        while (time < fadeDuration)
        {
            time += Time.deltaTime;
            // Mathf.Lerp를 사용해 현재 투명도에서 목표 투명도로 부드럽게 전환합니다.
            float newAlpha = Mathf.Lerp(currentAlpha, targetAlpha, time / fadeDuration);
            SetAlpha(newAlpha);
            yield return null; // 다음 프레임까지 대기
        }

        // 마지막에 목표 투명도로 정확히 맞춥니다.
        SetAlpha(targetAlpha);
    }

    // 찾아둔 모든 사진과 글자의 투명도를 한 번에 변경하는 함수
    private void SetAlpha(float alpha)
    {
        foreach (var sprite in sprites)
        {
            Color c = sprite.color;
            c.a = alpha;
            sprite.color = c;
        }

        foreach (var text in texts)
        {
            Color c = text.color;
            c.a = alpha;
            text.color = c;
        }
    }
}