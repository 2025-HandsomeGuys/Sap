using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections;
using System.Collections.Generic;

public class ContainerFade : MonoBehaviour
{
    [Header("외부 모습 스프라이트")]
    public SpriteRenderer exteriorSprite;

    [Header("주변을 검게 만들 배경 스프라이트")]
    public SpriteRenderer darkBackground;

    [Header("페이드 아웃 스프라이트들")]
    public SpriteRenderer[] spritesToFade;

    [Header("페이드 아웃 타일맵들")]
    public TilemapRenderer[] tilemapsToFade;

    [Header("페이드 속도")]
    public float fadeSpeed = 3f;

    [Header("컨테이너 내부용 시네마머신 카메라")]
    public GameObject containerCamera;

    [Header("--- 사운드 먹먹함(BGM 필터) 설정 ---")]
    [Tooltip("낮/밤 스피커에 달린 AudioLowPassFilter를 모두 넣어주세요! (2개)")]
    public AudioLowPassFilter[] bgmFilters;
    public float normalFrequency = 22000f;  // 평소 주파수
    public float muffledFrequency = 1000f;  // 먹먹할 때 주파수
    public float transitionDuration = 1.5f; // 먹먹해지기까지 걸리는 시간(초)

    [Header("--- 앰비언스(풀벌레·새소리) 실내 처리 ---")]
    [Tooltip("체크하면 BGM과 함께 앰비언스도 먹먹해지고 작아진다.")]
    public bool muffleAmbience = true;
    [Tooltip("실내에서의 앰비언스 음량 배율. 0.5 = 원래의 50%.")]
    [Range(0f, 1f)] public float indoorAmbienceVolume = 0.5f;

    private Coroutine fadeCoroutine;
    private bool playerInside;
    private List<Renderer> allRenderersToFade = new List<Renderer>();

    private void Awake()
    {
        if (exteriorSprite != null) allRenderersToFade.Add(exteriorSprite);

        if (spritesToFade != null)
        {
            foreach (var renderer in spritesToFade)
            {
                if (renderer != null) allRenderersToFade.Add(renderer);
            }
        }

        if (tilemapsToFade != null)
        {
            foreach (var renderer in tilemapsToFade)
            {
                if (renderer != null) allRenderersToFade.Add(renderer);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            playerInside = true;
            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            // 들어갈 때: 그래픽 투명(0), 검은 배경 불투명(1), 주파수 -> 먹먹함
            fadeCoroutine = StartCoroutine(FadeRenderersAndAudio(0f, 1f, muffledFrequency));
            if (containerCamera != null) containerCamera.SetActive(true);
            ApplyAmbienceState(true);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            playerInside = false;
            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            // 나갈 때: 그래픽 보임(1), 검은 배경 투명(0), 주파수 -> 정상
            fadeCoroutine = StartCoroutine(FadeRenderersAndAudio(1f, 0f, normalFrequency));
            if (containerCamera != null) containerCamera.SetActive(false);
            ApplyAmbienceState(false);
        }
    }

    /// <summary>
    /// 앰비언스(풀벌레·새소리)에도 BGM과 같은 먹먹함과 실내 음량을 건다.
    ///
    /// BGM은 스피커 오브젝트의 필터를 인스펙터로 직접 물지만, 앰비언스 소스는
    /// SoundManager가 런타임에 만드는 자식이라 인스펙터 참조가 불가능하다 → API 호출.
    /// </summary>
    private void ApplyAmbienceState(bool inside)
    {
        if (!muffleAmbience) return;
        if (SoundManager.Instance == null) return;

        SoundManager.Instance.SetAmbienceFilter(
            inside ? muffledFrequency : normalFrequency,
            inside ? indoorAmbienceVolume : 1f,
            transitionDuration);
    }

    /// <summary>
    /// 실내에 있는 채로 이 오브젝트가 사라지면(씬 전환 등) 앰비언스가 먹먹한 채로 남는다.
    /// SoundManager는 DontDestroyOnLoad라 스스로 복구하지 못하므로 여기서 되돌린다.
    /// </summary>
    private void OnDisable()
    {
        if (!playerInside) return;
        playerInside = false;

        if (muffleAmbience && SoundManager.Instance != null)
            SoundManager.Instance.ClearAmbienceFilter(0f);
    }

    // 설정된 시간(transitionDuration) 동안 일정하게 주파수가 변하는 코루틴
    private IEnumerator FadeRenderersAndAudio(float exteriorTarget, float darkBgTarget, float targetFrequency)
    {
        // 1. 주파수 변화 속도를 계산 (거리 / 시간 = 속도)
        float[] frequencySpeeds = new float[bgmFilters.Length];
        for (int i = 0; i < bgmFilters.Length; i++)
        {
            if (bgmFilters[i] != null)
            {
                float distance = Mathf.Abs(bgmFilters[i].cutoffFrequency - targetFrequency);
                frequencySpeeds[i] = distance / Mathf.Max(0.01f, transitionDuration);
            }
        }

        while (true)
        {
            bool isDone = true;

            // 2. 검은 배경 페이드 처리
            if (darkBackground != null)
            {
                if (!ApplyFadeToRenderer(darkBackground, darkBgTarget)) isDone = false;
            }

            // 3. 나머지 모든 렌더러 페이드 처리
            foreach (Renderer renderer in allRenderersToFade)
            {
                if (renderer != null)
                {
                    if (!ApplyFadeToRenderer(renderer, exteriorTarget)) isDone = false;
                }
            }

            // 4. 사운드 먹먹함(Low Pass Filter) - 정해진 시간(초) 동안 일정하게 변화
            for (int i = 0; i < bgmFilters.Length; i++)
            {
                if (bgmFilters[i] != null)
                {
                    bgmFilters[i].cutoffFrequency = Mathf.MoveTowards(
                        bgmFilters[i].cutoffFrequency,
                        targetFrequency,
                        frequencySpeeds[i] * Time.deltaTime
                    );

                    // 목표 주파수에 도달하지 않았다면 계속 진행
                    if (!Mathf.Approximately(bgmFilters[i].cutoffFrequency, targetFrequency))
                    {
                        isDone = false;
                    }
                }
            }

            if (isDone) break;

            yield return null;
        }
    }

    private bool ApplyFadeToRenderer(Renderer renderer, float targetAlpha)
    {
        float currentAlpha = 0f;

        if (renderer is SpriteRenderer sr)
        {
            Color color = sr.color;
            color.a = Mathf.MoveTowards(color.a, targetAlpha, fadeSpeed * Time.deltaTime);
            sr.color = color;
            currentAlpha = color.a;
        }
        else
        {
            Color color = renderer.material.color;
            color.a = Mathf.MoveTowards(color.a, targetAlpha, fadeSpeed * Time.deltaTime);
            renderer.material.color = color;
            currentAlpha = color.a;
        }

        return Mathf.Approximately(currentAlpha, targetAlpha);
    }
}