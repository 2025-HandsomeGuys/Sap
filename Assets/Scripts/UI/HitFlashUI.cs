using UnityEngine;
using System.Collections;

/// <summary>
/// 파기 시 화면 전체를 순간 밝게 플래시하는 UI 효과.
/// 삽·곡괭이 전용 (드릴은 연속 파기로 인한 시각적 피로 때문에 제외).
///
/// 설정 방법:
/// 1. Canvas (ScreenSpace-Overlay, Sort Order 높게) 생성
/// 2. 자식에 Image 추가 (색: 흰색, RectTransform = 화면 전체)
/// 3. 이 컴포넌트를 Canvas에 부착, CanvasGroup 연결
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class HitFlashUI : MonoBehaviour
{
    public static HitFlashUI Instance;

    private CanvasGroup _canvasGroup;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        _canvasGroup = GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false; // 클릭 이벤트 통과
        _canvasGroup.interactable = false;
    }

    /// <summary>
    /// 플래시 발동.
    /// peakAlpha: 최대 불투명도 (0~1), duration: 전체 지속 시간(초)
    /// </summary>
    public void Flash(float peakAlpha = 0.15f, float duration = 0.08f)
    {
        StopAllCoroutines();
        StartCoroutine(DoFlash(Mathf.Clamp01(peakAlpha), duration));
    }

    private IEnumerator DoFlash(float peak, float duration)
    {
        float half = duration * 0.5f;

        // 0 → peak
        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            _canvasGroup.alpha = Mathf.Lerp(0f, peak, t / half);
            yield return null;
        }

        // peak → 0
        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            _canvasGroup.alpha = Mathf.Lerp(peak, 0f, t / half);
            yield return null;
        }

        _canvasGroup.alpha = 0f;
    }
}
