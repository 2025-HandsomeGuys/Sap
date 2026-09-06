using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 페이드 효과를 담당하는 싱글톤.
/// </summary>
public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    [SerializeField] private Image fadeImage;
    [SerializeField] private float defaultDuration = 0.5f;

    /// <summary>
    /// 페이드막이 화면을 거의/완전히 덮고 있는가(알파 ≥ 0.9).
    /// 페이드로 가려진 순간에만 UI 변화를 반영하려는 쪽(예: 지상 날짜/시간 HUD)이 참조한다.
    /// </summary>
    public bool IsCovering =>
        fadeImage != null && fadeImage.gameObject.activeSelf && fadeImage.color.a >= 0.9f;

    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            
            if (fadeImage != null)
            {
                fadeImage.gameObject.SetActive(false);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public IEnumerator FadeOut(float duration = -1f)
    {
        if (duration < 0) duration = defaultDuration;
        yield return StartCoroutine(FadeRoutine(0f, 1f, duration));
    }

    public IEnumerator FadeIn(float duration = -1f)
    {
        if (duration < 0) duration = defaultDuration;
        yield return StartCoroutine(FadeRoutine(1f, 0f, duration));
    }

    private IEnumerator FadeRoutine(float startAlpha, float endAlpha, float duration)
    {
        if (fadeImage == null) yield break;
        
        fadeImage.gameObject.SetActive(true);
        Color color = fadeImage.color;
        
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            color.a = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
            fadeImage.color = color;
            yield return null;
        }

        color.a = endAlpha;
        fadeImage.color = color;

        if (endAlpha <= 0)
        {
            fadeImage.gameObject.SetActive(false);
        }
    }
}
