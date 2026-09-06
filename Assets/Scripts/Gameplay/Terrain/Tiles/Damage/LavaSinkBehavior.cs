// @tags: damage, zone, lava, mineral, vfx, trigger, destroy
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 용암 존에 비-플레이어 오브젝트(광물 등)가 진입하면
/// 사운드 + 연기 + 통통 튀기 시퀀스를 실행한 뒤 소멸시킵니다.
/// 플레이어 데미지는 DamageZone이 별도로 담당합니다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class LavaSinkBehavior : MonoBehaviour
{
    #region Inspector Fields

    [Header("Dissolve Settings")]
    [Tooltip("소멸까지 대기 시간 (초)")]
    [SerializeField] private float dissolveDelay = 0.8f;

    [Header("FX References")]
    [Tooltip("치이익 사운드를 재생할 AudioSource")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("치이익 AudioClip")]
    [SerializeField] private AudioClip sizzleClip;

    [Tooltip("검은 연기 ParticleSystem")]
    [SerializeField] private ParticleSystem smokeParticles;

    #endregion

    #region Private State

    // 현재 처리 중인 아이템 → 중복 진입 방지
    private readonly HashSet<Rigidbody2D> _processing = new HashSet<Rigidbody2D>();

    // 아이템별 코루틴 참조 (OnTriggerExit 시 취소용)
    private readonly Dictionary<Rigidbody2D, Coroutine> _coroutines = new Dictionary<Rigidbody2D, Coroutine>();

    // GC 최적화: WaitForSeconds 캐싱
    private WaitForSeconds _waitDissolve;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _waitDissolve = new WaitForSeconds(dissolveDelay);

        // 프리팹의 AudioSource는 믹서 그룹이 비어 있다 → 설정의 효과음 슬라이더가 안 먹는다.
        AudioRouting.Route(audioSource, AudioChannel.SFX);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[LavaSinkBehavior] '{gameObject.name}'의 Collider2D가 Trigger가 아닙니다. isTrigger를 true로 설정합니다.", this);
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 플레이어는 DamageZone이 처리 — 무시
        if (other.CompareTag("Player")) return;

        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null) return;

        // 떨어진 아이템(광물 등 Dynamic)만 소멸 대상. 기둥·플랫폼 같은
        // 구조물(Kinematic)이나 Static은 제외 — 안 그러면 SinkingPillar가
        // 용암에 닿는 순간 파괴된다.
        if (rb.bodyType != RigidbodyType2D.Dynamic) return;

        // 이미 처리 중이면 중복 시작 방지
        if (_processing.Contains(rb)) return;

        _processing.Add(rb);
        Coroutine c = StartCoroutine(LavaSinkRoutine(rb));
        _coroutines[rb] = c;

        Debug.Log($"[LavaSinkBehavior] '{other.name}' 용암 진입 — 소멸 시퀀스 시작.");
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        // 바운스로 인해 트리거를 잠깐 벗어나도 시퀀스는 유지한다.
        // 시퀀스 도중 오브젝트가 Destroy되면 코루틴 내부 null 체크로 자동 종료된다.
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        _processing.Clear();
        _coroutines.Clear();
    }

    #endregion

    #region Sink Sequence

    private IEnumerator LavaSinkRoutine(Rigidbody2D rb)
    {
        // --- 1. 사운드 & 파티클 ---
        PlaySizzleSound();
        PlaySmokeEffect(rb.position);

        // --- 2. 속도 초기화 ---
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }

        // --- 3. 소멸 대기 ---
        yield return _waitDissolve;

        // --- 4. 소멸 ---
        if (rb != null && rb.gameObject != null)
        {
            Debug.Log($"[LavaSinkBehavior] '{rb.gameObject.name}' 용암에 소멸.");
            _processing.Remove(rb);
            _coroutines.Remove(rb);
            Destroy(rb.gameObject);
        }
    }

    #endregion

    #region Helpers

    private void PlaySizzleSound()
    {
        if (audioSource == null || sizzleClip == null) return;
        audioSource.PlayOneShot(sizzleClip);
    }

    private void PlaySmokeEffect(Vector2 position)
    {
        if (smokeParticles == null) return;
        smokeParticles.transform.position = position;
        smokeParticles.Play();
    }

    private void CancelSinkSequence(Rigidbody2D rb)
    {
        if (_coroutines.TryGetValue(rb, out Coroutine c))
        {
            if (c != null) StopCoroutine(c);
            _coroutines.Remove(rb);
        }
        _processing.Remove(rb);

        Debug.Log($"[LavaSinkBehavior] '{rb.gameObject.name}' 용암 탈출 — 소멸 시퀀스 취소.");
    }

    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.3f); // 주황 반투명
        Gizmos.DrawCube(transform.position, col.bounds.size);
    }
#endif
}
