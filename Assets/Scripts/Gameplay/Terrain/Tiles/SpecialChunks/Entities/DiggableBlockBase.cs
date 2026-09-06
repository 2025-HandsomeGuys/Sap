// @tags: special-chunk, digging, entity, damage, base-class, interface
using UnityEngine;

/// <summary>
/// 곡괭이(toolIndex=2)로만 파괴 가능한 특수 블록의 공통 베이스.
///
/// SOLID:
///  - SRP: HP 관리·HitCooldown·IDamageStageable 브로드캐스트만 담당.
///  - OCP: OnHit()/OnDie() 오버라이드로 VFX·드롭 확장 — 이 클래스 수정 불필요.
///  - DIP: IDamageStageable[] 인터페이스 배열만 참조.
///
/// 서브클래스 구현 계약:
///  - ApplySettings() — SpecialChunkSettingsLoader에서 maxHp 등 오버라이드 (선택)
///  - OnHit()         — 타격 VFX 재생
///  - OnDie(center)   — 드롭 + 파괴 VFX (Destroy는 베이스가 호출)
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public abstract class DiggableBlockBase : MonoBehaviour, IDiggable
{
    [Header("HP")]
    public float maxHp = 25f;

    protected float _currentHp;
    protected IDamageStageable[] _stageListeners;
    protected SpriteRenderer _sr;

    private float _lastHitTime;
    private const float HitCooldown = 0.2f;

    protected virtual void Awake()
    {
        _sr             = GetComponent<SpriteRenderer>();
        _stageListeners = GetComponents<IDamageStageable>();
        ApplySettings();
        _currentHp = maxHp;
    }

    /// <summary>SpecialChunkSettingsLoader에서 maxHp 등을 적용한다.</summary>
    protected virtual void ApplySettings() { }

    // ─── IDiggable ────────────────────────────────────────────────
    public void Dig(Vector2 worldPos, float damage, int toolIndex)
    {
        if (toolIndex != 2) return;
        if (Time.time - _lastHitTime < HitCooldown) return;
        _lastHitTime = Time.time;

        _currentHp -= damage;
        float ratio = Mathf.Max(_currentHp, 0f) / maxHp;

        foreach (var l in _stageListeners)
            l.OnHpRatioChanged(ratio);

        OnHit();

        if (_currentHp <= 0f)
            Die();
    }

    /// <summary>타격 피드백 (VFX 등). 서브클래스에서 구현.</summary>
    protected abstract void OnHit();

    /// <summary>파괴 시 드롭·VFX 등. 서브클래스에서 구현. Destroy는 베이스가 처리.</summary>
    protected abstract void OnDie(Vector3 center);

    protected virtual void Die()
    {
        Vector3 center = _sr != null ? _sr.bounds.center : transform.position;
        OnDie(center);
        Destroy(gameObject);
    }
}
