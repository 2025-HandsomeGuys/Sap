// @tags: special-chunk, hazard, falling, vibration, base
using System.Collections;
using UnityEngine;

/// <summary>
/// 천장 낙하물 공통 베이스 — 진동/감지 트리거 → 돌가루 예고 → 딜레이 → 낙하 → 착지 연쇄진동.
///
/// [SOLID]
///   SRP: 낙하 생명주기(예고·낙하·착지)만 담당.
///   OCP: 트리거/피격/설정은 자식이 오버라이드 (LoadSettings, OnLanded, BeginDropSequence 호출).
///   DIP: VibrationManager.Instance? 로 느슨하게 참조.
///
/// 중복 방지: _warning(예고 중) / _falling(낙하 중) 플래그로 재진입 차단.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public abstract class FallingHazardBase : MonoBehaviour, IVibrationReceiver
{
    [Header("Warning — 낙하 예고  ※ warningDelay는 런타임에 JSON 값으로 덮어씀")]
    [Tooltip("낙하 전 재생할 돌가루 예고 파티클 (없으면 생략)")]
    [SerializeField] protected ParticleSystem warningParticle;

    [Tooltip("예고 후 실제 낙하까지 딜레이 (초) — JSON: physics.fallWarningDelay")]
    [SerializeField] protected float warningDelay = 0.5f;

    [Header("Impact")]
    [Tooltip("착지 후 연쇄 진동 전파 반경")]
    [SerializeField] protected float impactRadius = 2f;

    [Tooltip("착지 시 재생할 파편 파티클 (없으면 생략)")]
    [SerializeField] protected ParticleSystem shardParticle;

    protected Rigidbody2D _rb;
    protected bool        _falling;
    protected bool        _warning;

    // 자식이 Awake를 오버라이드하면 반드시 base.Awake()를 호출해야 LoadSettings()가 실행된다.
    protected virtual void Awake()
    {
        _rb           = GetComponent<Rigidbody2D>();
        _rb.simulated = false; // 낙하 전 물리 비활성화
        LoadSettings();
    }

    // ─── IVibrationReceiver ──────────────────────────────────────
    public void OnVibration() => BeginDropSequence();

    /// <summary>예고 시퀀스 시작. 이미 예고/낙하 중이면 무시.</summary>
    protected void BeginDropSequence()
    {
        if (_warning || _falling) return;
        _warning = true;
        StartCoroutine(WarnThenDrop());
    }

    private IEnumerator WarnThenDrop()
    {
        if (warningParticle != null)
            warningParticle.Play();

        yield return new WaitForSeconds(warningDelay);

        Drop();
    }

    private void Drop()
    {
        _falling         = true;
        _rb.simulated    = true;
        _rb.gravityScale = 1f;
        Debug.Log($"[{GetType().Name}] 낙하 시작.");
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        if (!_falling) return;

        // 연쇄 진동 전파 (자신은 곧 파괴 → 무한 루프 없음)
        VibrationManager.Instance?.TriggerVibration(transform.position, impactRadius);

        // 자식별 착지 추가 동작 (피격 등)
        OnLanded(col);

        // 파편 파티클 — 부모 분리 후 재생 (gameObject 파괴 후에도 유지)
        if (shardParticle != null)
        {
            shardParticle.transform.SetParent(null);
            shardParticle.Play();
        }

        Debug.Log($"[{GetType().Name}] 착지 — 연쇄 진동.");
        Destroy(gameObject);
    }

    // ─── 자식 확장 지점 ──────────────────────────────────────────
    /// <summary>JSON 설정 적용 (자식별 구현).</summary>
    protected abstract void LoadSettings();

    /// <summary>착지 시 추가 동작 (기본 없음). 예: 플레이어 피격.</summary>
    protected virtual void OnLanded(Collision2D col) { }
}
