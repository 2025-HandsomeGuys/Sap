// @tags: vfx, audio, special-chunk, digging, particle
using UnityEngine;

/// <summary>
/// 압축 쓰레기 벽의 파티클·사운드 효과를 담당하는 컴포넌트.
///
/// SOLID — SRP: VFX/오디오만. HP 판정·드롭·비주얼 스프라이트와 완전 분리.
/// TrashWallEntity가 PlayHit() / PlayDestroy()로 직접 호출.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class TrashWallVFX : MonoBehaviour
{
    [Header("타격 이펙트")]
    [Tooltip("타격 시 재생할 고철 파편 파티클 (없으면 생략)")]
    public ParticleSystem hitParticle;

    [Tooltip("타격음 — 둔탁한 금속 깡통 소리 (없으면 생략)")]
    public AudioClip hitSFX;

    [Header("파괴 이펙트")]
    [Tooltip("파괴 시 Instantiate할 VFX 프리팹 (없으면 생략)")]
    public GameObject destroyVFXPrefab;

    [Tooltip("파괴음 — 금속 쓰레기 더미 무너지는 소리 (없으면 생략)")]
    public AudioClip destroySFX;

    private AudioSource _audio;

    private void Awake()
    {
        _audio = GetComponent<AudioSource>();

        // 프리팹의 AudioSource는 믹서 그룹이 비어 있다 → 설정의 효과음 슬라이더가 안 먹는다.
        AudioRouting.Route(_audio, AudioChannel.SFX);
    }

    /// <summary>타격마다 호출. TrashWallEntity.Dig()에서 사용.</summary>
    public void PlayHit()
    {
        if (hitParticle != null) hitParticle.Play();
        if (hitSFX  != null)    _audio.PlayOneShot(hitSFX);
    }

    /// <summary>파괴 직전 호출. TrashWallEntity.Die()에서 사용.</summary>
    public void PlayDestroy(Vector3 pos)
    {
        if (destroySFX != null)       _audio.PlayOneShot(destroySFX);
        if (destroyVFXPrefab != null) Instantiate(destroyVFXPrefab, pos, Quaternion.identity);
    }
}
