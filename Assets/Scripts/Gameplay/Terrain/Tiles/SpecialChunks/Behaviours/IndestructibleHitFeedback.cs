// @tags: indestructible, feedback, audio, digging, special-chunk, interface
using UnityEngine;

/// <summary>
/// 파괴 불가 영역 타격 시 오디오 피드백을 담당하는 컴포넌트.
///
/// SOLID:
///  - SRP: 타격 피드백(오디오)만 담당. 픽셀 초기화와 완전 분리.
///  - ISP: IIndestructibleHit 하나만 구현 — IndestructibleOverlayInit에서 분리됨.
///  - OCP: 시각 피드백(파티클 등) 추가 시 이 컴포넌트만 확장하면 됨.
///
/// 사용법:
///  IndestructibleOverlayInit과 같은 GameObject 또는 자식에 추가.
///  hitSound, audioSource를 Inspector에서 연결.
/// </summary>
public class IndestructibleHitFeedback : MonoBehaviour, IIndestructibleHit
{
    [Header("타격 피드백")]
    [SerializeField] private AudioClip _hitSound;
    [SerializeField] private AudioSource _audioSource;

    // 프리팹의 AudioSource는 믹서 그룹이 비어 있다 → 설정의 효과음 슬라이더가 안 먹는다.
    private void Awake() => AudioRouting.Route(_audioSource, AudioChannel.SFX);

    public void OnHitAttempt(Vector2 worldPos, int toolIndex)
    {
        Debug.Log($"[IndestructibleHitFeedback] 파괴 불가 영역 타격 — {gameObject.name}, tool={toolIndex}");

        // 인스펙터에 클립이 물려 있으면 그것을 우선한다(기존 프리팹 설정 보존).
        if (_audioSource != null && _hitSound != null)
        {
            _audioSource.PlayOneShot(_hitSound);
            return;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(SfxKeys.DigBlocked, worldPos);
    }
}
