// @tags: special-chunk, rock, trap, physics, digging, hazard
using UnityEngine;

/// <summary>
/// 매몰 갱도 — 굴러오는 바위 엔티티.
///
/// [Stage 1] 물리 기본: Activate() 시 45도 대각선 impulse.
/// [Stage 2] 지형 파괴: FixedUpdate에서 거리 기반 쓰로틀링으로 ExplodeTerrain 호출.
///
/// SOLID:
///  SRP: 바위 물리·충돌만 담당. 트리거·천장 제거는 RollingRockTrap 담당.
///  DIP: 지형 파괴는 ITerrainManager 인터페이스 통해 처리.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class RollingRockEntity : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────
    [Header("이동 방향")]
    [Tooltip("+1 = 오른쪽, -1 = 왼쪽. 청크 입구 방향에 맞게 설정.")]
    public float rollDirection = 1f;

    [Tooltip("발사 시 초기 속도 크기 (유닛/초). 45도 대각선으로 적용됨.")]
    public float launchSpeed = 8f;

    [Header("지형 파괴 (Stage 2)")]
    [Tooltip("이동 중 지형을 파괴할지 여부. false면 천장 제거만 하고 지형 위를 굴러내려감.")]
    public bool destroyTerrainOnMove = true;

    [Tooltip("마지막 제거 위치에서 이 거리 이상 이동했을 때만 ExplodeTerrain 호출. 작을수록 세밀하지만 무거움.")]
    public float clearInterval = 0.15f;

    [Tooltip("ExplodeTerrain 반경 (유닛). 바위 CircleCollider 반경과 비슷하게 설정.")]
    public float clearRadius = 0.4f;

    // ─── 런타임 ──────────────────────────────────────────────
    private Rigidbody2D _rb;
    private ITerrainManager _terrain;
    private Vector2 _lastClearPos;
    private bool _isActive;

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.bodyType     = RigidbodyType2D.Kinematic;
        _rb.gravityScale = 0f;

        // JSON 설정 적용
        if (SpecialChunkSettingsLoader.Instance != null)
        {
            var s = SpecialChunkSettingsLoader.Instance.Settings.traps.rollingRock;
            launchSpeed    = s.launchSpeed;
            clearInterval  = s.clearInterval;
            clearRadius    = s.clearRadius;
            staminaDamage  = s.staminaDamage;
            knockbackForce = s.knockbackForce;
        }
    }

    /// <summary>RollingRockTrap 또는 TestTrapButton에서 호출.</summary>
    public void Activate()
    {
        if (_isActive) return;
        _isActive = true;

        // FindFirstObjectByType을 Activate() 1회 호출로 한정 — FixedUpdate 반복 탐색 제거
        _terrain = Object.FindFirstObjectByType<StaticChunkTerrainManager>() as ITerrainManager
                ?? Object.FindFirstObjectByType<InfinityMapManager>()        as ITerrainManager;

        if (_terrain == null)
            Debug.LogWarning("[RollingRock] ITerrainManager를 찾을 수 없어 지형 파괴가 비활성화됩니다.");
        else
            Debug.Log($"[RollingRock] TerrainManager 연결: {_terrain.GetType().Name}");

        _rb.bodyType     = RigidbodyType2D.Dynamic;
        _rb.gravityScale = 1f;
        _lastClearPos    = transform.position;

        Vector2 dir = new Vector2(rollDirection, -1f).normalized;
        _rb.AddForce(dir * launchSpeed, ForceMode2D.Impulse);

        Debug.Log($"[RollingRock] Activated — dir=({dir.x:F2},{dir.y:F2}), speed={launchSpeed}");
    }

    private void FixedUpdate()
    {
        if (!_isActive || !destroyTerrainOnMove || _terrain == null) return;

        Vector2 cur = transform.position;
        if (Vector2.Distance(cur, _lastClearPos) >= clearInterval)
        {
            _terrain.ExplodeTerrain(cur, clearRadius);
            _lastClearPos = cur;
        }
    }

    [Header("플레이어 피격 (Stage 3)")]
    [Tooltip("플레이어 충돌 시 스태미나 감소량. DelayedBlast와 동일 패턴.")]
    public float staminaDamage = 30f;

    [Tooltip("플레이어 넉백 힘 크기.")]
    public float knockbackForce = 12f;

    // ─── 충돌 처리 ────────────────────────────────────────────
    private void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Player"))
            ApplyPlayerImpact(col);
    }

    /// <summary>
    /// 플레이어에게 피해 + 넉백을 적용한다.
    /// DIP: IHazardTarget(피해), IPlayerController(넉백) 인터페이스만 참조.
    /// </summary>
    private void ApplyPlayerImpact(Collision2D col)
    {
        // ① 피해 (DIP: IHazardTarget — PlayerStat 구체 타입 미참조)
        IHazardTarget target = col.gameObject.GetComponentInParent<IHazardTarget>();
        if (target == null) target = col.gameObject.GetComponentInChildren<IHazardTarget>();
        if (target != null)
        {
            target.ApplyHazardDamage(staminaDamage);
            Debug.Log($"[RollingRock] 플레이어 피해 -{staminaDamage}");
        }

        // ② 넉백 (DIP: IPlayerController — isDashing 직접 조작 제거)
        IPlayerController controller = col.gameObject.GetComponentInParent<IPlayerController>();
        if (controller != null)
        {
            Vector2 dir = (col.transform.position - transform.position).normalized;
            controller.ApplyExternalKnockback(dir * knockbackForce, 0.3f);
            Debug.Log($"[RollingRock] 넉백 방향: {dir}, 힘: {knockbackForce}");
        }
    }
}
