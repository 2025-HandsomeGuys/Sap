// @tags: anti-gravity, background, visual, phase, blink, special-chunk
using System.Collections;
using UnityEngine;

/// <summary>
/// 반중력 특수청크 전용 배경 오브젝트. AntiGravityZone 위상에 맞춰 스프라이트를 교체하고,
/// 전환 직전 다음 위상 스프라이트를 가속 점멸하여 예고한다. (전역 BackgroundManager와 무관)
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class AntiGravityBackground : MonoBehaviour
{
    [Tooltip("미지정 시 부모에서 AntiGravityZone 탐색")]
    [SerializeField] private AntiGravityZone zone;

    [Tooltip("index = (int)GravityPhase — 0:정상 1:저중력(달) 2:역중력 3:저중력(역방향)")]
    [SerializeField] private Sprite[] phaseSprites = new Sprite[4];

    [Header("Blink (전환 예고)")]
    [Tooltip("점멸 시작 간격(느림)")]
    [SerializeField] private float blinkStartInterval = 0.4f;
    [Tooltip("점멸 종료 간격(전환 직전, 빠름)")]
    [SerializeField] private float blinkEndInterval = 0.08f;

    private SpriteRenderer _sr;
    private GravityPhase _currentPhase = GravityPhase.Normal;
    private Coroutine _blink;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (zone == null) zone = GetComponentInParent<AntiGravityZone>();
    }

    private void OnEnable()
    {
        if (zone == null) return;
        zone.OnPhaseChanged += HandlePhaseChanged;
        zone.OnPhaseWarning += HandlePhaseWarning;
    }

    private void OnDisable()
    {
        if (zone == null) return;
        zone.OnPhaseChanged -= HandlePhaseChanged;
        zone.OnPhaseWarning -= HandlePhaseWarning;
        StopBlink();
    }

    private void HandlePhaseChanged(GravityPhase phase)
    {
        StopBlink();
        _currentPhase = phase;
        ApplySprite(phase);
    }

    private void HandlePhaseWarning(GravityPhase next, float leadTime)
    {
        StopBlink();
        _blink = StartCoroutine(BlinkCo(next, leadTime));
    }

    private IEnumerator BlinkCo(GravityPhase next, float leadTime)
    {
        Sprite baseSprite = SpriteFor(_currentPhase);
        Sprite nextSprite = SpriteFor(next);
        float elapsed = 0f;
        bool showNext = false;

        while (elapsed < leadTime)
        {
            showNext = !showNext;
            _sr.sprite = showNext ? nextSprite : baseSprite;

            float t = leadTime > 0f ? Mathf.Clamp01(elapsed / leadTime) : 1f;
            float interval = Mathf.Lerp(blinkStartInterval, blinkEndInterval, t);
            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }

        _sr.sprite = baseSprite; // 곧 OnPhaseChanged가 다음 위상으로 확정.
        _blink = null;
    }

    private void StopBlink()
    {
        if (_blink == null) return;
        StopCoroutine(_blink);
        _blink = null;
    }

    private void ApplySprite(GravityPhase phase)
    {
        var s = SpriteFor(phase);
        if (s != null) _sr.sprite = s;
    }

    private Sprite SpriteFor(GravityPhase phase)
    {
        int i = (int)phase;
        return (phaseSprites != null && i >= 0 && i < phaseSprites.Length) ? phaseSprites[i] : null;
    }
}
