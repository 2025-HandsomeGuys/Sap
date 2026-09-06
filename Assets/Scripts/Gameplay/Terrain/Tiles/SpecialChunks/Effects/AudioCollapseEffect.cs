// @tags: vfx, audio, special-chunk, trap, collapse, interface
using UnityEngine;

/// <summary>
/// ICollapseEffect 구현 — 오디오 피드백.
/// SRP: 오디오 재생 책임만 담당한다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AudioCollapseEffect : MonoBehaviour, ICollapseEffect
{
    [Tooltip("붕괴 경고 사운드 (우우웅...)")]
    public AudioClip warningSound;

    [Tooltip("붕괴 완료 사운드 (쿠구구궁!)")]
    public AudioClip collapseSound;

    private AudioSource _src;

    void Awake()
    {
        _src = GetComponent<AudioSource>();

        // 프리팹의 AudioSource는 믹서 그룹이 비어 있다 → 설정의 효과음 슬라이더가 안 먹는다.
        AudioRouting.Route(_src, AudioChannel.SFX);
    }

    public void PlayWarning()
    {
        if (_src != null && warningSound != null)
            _src.PlayOneShot(warningSound);
    }

    public void PlayCollapse()
    {
        if (_src != null && collapseSound != null)
            _src.PlayOneShot(collapseSound);
    }
}
