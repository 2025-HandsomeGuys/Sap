using UnityEngine;

/// <summary>
/// 동상(frostbite) 수치가 쌓일수록 화면 가장자리 오버레이가 진해지는 효과.
/// StaminaManager.AddFrostbite()에서 Instance를 통해 직접 호출받는다 (polling 없음).
///
/// 설정 방법:
/// 1. Canvas (ScreenSpace-Overlay, Sort Order 낮게) 생성
/// 2. 자식에 Image 추가 (비네트 Sprite, RectTransform = 화면 전체)
/// 3. 이 컴포넌트를 Canvas에 부착, CanvasGroup 연결
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class FrostbiteOverlayUI : MonoBehaviour
{
    public static FrostbiteOverlayUI Instance { get; private set; }

    [Header("Settings")]
    [Tooltip("frostbite가 MaxStamina 전체를 채웠을 때의 최대 알파값 (0~1)")]
    [SerializeField] private float maxAlpha = 0.75f;

    [Tooltip("알파 보간 속도. 클수록 빠르게 반응.")]
    [SerializeField] private float lerpSpeed = 3f;

    private CanvasGroup _canvasGroup;
    private float _targetAlpha;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        _canvasGroup = GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;
    }

    /// <summary>
    /// StaminaManager에서 frostbite가 변경될 때마다 호출.
    /// </summary>
    /// <param name="frostbite">현재 동상 수치</param>
    /// <param name="originalMaxStamina">기준 MaxStamina (감소 전 원본값)</param>
    public void UpdateFrostbite(float frostbite, float originalMaxStamina)
    {
        if (originalMaxStamina <= 0f) return;
        float ratio = Mathf.Clamp01(frostbite / originalMaxStamina);
        _targetAlpha = ratio * maxAlpha;
    }

    private void Update()
    {
        _canvasGroup.alpha = Mathf.Lerp(_canvasGroup.alpha, _targetAlpha, Time.deltaTime * lerpSpeed);
    }
}
