// @tags: vfx, audio, special-chunk, digging, particle, explosion, pool
using UnityEngine;

/// <summary>
/// 압축 쓰레기 벽 VFX 컴포넌트.
///
/// PlayHit()    — 피격 시 작은 흔들림 및 파티클
/// PlayDestroy() — 파괴 시 쓰레기 파티클 버스트 + 화면 흔들림
///
/// 파티클은 VfxPool을 거쳐 재사용된다. 특히 PlayDestroy는 한 프레임에
/// destroyBurstCount개를 한꺼번에 띄우므로 Instantiate/Destroy로 돌리면
/// 그 프레임만 튄다.
///
/// SOLID — SRP: 시각/청각 피드백만 담당.
/// </summary>
public class CompressedTrashVFX : MonoBehaviour
{
    [Header("피격 이펙트")]
    [Tooltip("피격 시 재생할 파티클 프리팹 (null이면 스킵)")]
    public GameObject hitParticlePrefab;

    [Header("파괴 이펙트")]
    [Tooltip("파괴 시 재생할 파티클 프리팹 (null이면 스킵)")]
    public GameObject destroyParticlePrefab;

    [Tooltip("파괴 시 생성할 파티클 수")]
    public int destroyBurstCount = 8;

    [Tooltip("파티클 자동 소멸 시간 (초)")]
    public float particleLifetime = 1.5f;

    // ─── 유니티 이벤트 ────────────────────────────────────────────
    private void Start()
    {
        // 첫 타격에서 Instantiate 히칭이 나지 않도록 미리 채워 둔다.
        VfxPool.Prewarm(hitParticlePrefab, 2);
        VfxPool.Prewarm(destroyParticlePrefab, destroyBurstCount);
    }

    // ─── 공개 API ─────────────────────────────────────────────────
    /// <summary>피격 시 호출. CompressedTrashWallEntity.Dig()에서 호출.</summary>
    public void PlayHit()
    {
        if (hitParticlePrefab == null) return;

        VfxPool.Play(hitParticlePrefab, transform.position, Quaternion.identity, particleLifetime);
    }

    /// <summary>파괴 시 호출. CompressedTrashWallEntity.Die()에서 호출.</summary>
    public void PlayDestroy(Vector3 center)
    {
        if (destroyParticlePrefab == null) return;

        // 여러 방향으로 파티클 버스트
        for (int i = 0; i < destroyBurstCount; i++)
        {
            float angle = i * (360f / destroyBurstCount) + Random.Range(-15f, 15f);
            Quaternion rot = Quaternion.Euler(0f, 0f, angle);

            Vector3 offset = new Vector3(
                Random.Range(-0.3f, 0.3f),
                Random.Range(-0.1f, 0.3f),
                0f
            );

            // 풀 인스턴스는 이 오브젝트가 파괴돼도 살아남는다(풀 루트 소속).
            VfxPool.Play(destroyParticlePrefab, center + offset, rot, particleLifetime);
        }
    }
}
