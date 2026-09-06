// @tags: damage, zone, stamina, player, trigger, special-chunk
using UnityEngine;
using System.Collections;

/// <summary>
/// 데미지 존 (Damage Zone) - 플레이어가 접촉하면 최대 스태미나를 지속적으로 감소시킵니다.
/// Static Special Chunk의 하위 오브젝트로 배치하여 사용합니다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DamageZone : MonoBehaviour
{
    [Header("Damage Settings")]
    [Tooltip("초당 최대 스태미나 감소량")]
    public float damagePerSecond = 10f;

    [Tooltip("데미지 적용 간격 (초)")]
    public float damageInterval = 1.0f;

    [Header("Target Settings")]
    [Tooltip("플레이어 식별 태그")]
    public string targetTag = "Player";

    // 최적화: WaitForSeconds 객체 캐싱 (GC 방지)
    private WaitForSeconds _waitObj;
    
    // 현재 접촉 중인 플레이어 정보
    private GameObject _currentTarget;
    private PlayerStat _targetStats;
    // 부상 누적 경로. PlayerStat과 다른 GameObject에 있을 수 있어 계층 탐색 후 씬 전체로 폴백한다.
    private StaminaManager _targetStamina;

    // 데미지 적용 코루틴 참조
    private Coroutine _damageCoroutine;

    void Start()
    {
        // GC 최적화: WaitForSeconds 미리 캐싱
        _waitObj = new WaitForSeconds(damageInterval);

        // Collider2D가 Trigger인지 검증
        Collider2D col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[DamageZone] '{gameObject.name}'의 Collider2D가 Trigger로 설정되지 않았습니다! isTrigger를 true로 설정합니다.", this);
            col.isTrigger = true;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // 플레이어 태그 확인
        if (!other.CompareTag(targetTag))
            return;

        // PlayerStat 캐싱
        PlayerStat stats = other.GetComponent<PlayerStat>();
        if (stats == null)
        {
            Debug.LogWarning($"[DamageZone] '{other.name}'에 PlayerStat이 없습니다!", this);
            return;
        }

        // 이미 데미지를 받고 있다면 중복 방지
        if (_damageCoroutine != null)
        {
            StopCoroutine(_damageCoroutine);
        }

        _currentTarget = other.gameObject;
        _targetStats = stats;
        _targetStamina = other.GetComponent<StaminaManager>()
                      ?? other.GetComponentInParent<StaminaManager>()
                      ?? other.GetComponentInChildren<StaminaManager>()
                      ?? FindFirstObjectByType<StaminaManager>();

        // 데미지 코루틴 시작
        _damageCoroutine = StartCoroutine(ApplyDamageCoroutine());
        
        Debug.Log($"[DamageZone] '{other.name}' 진입. 초당 {damagePerSecond} 스태미나 감소 시작.");
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(targetTag))
            return;

        // 데미지 중지
        if (_damageCoroutine != null)
        {
            StopCoroutine(_damageCoroutine);
            _damageCoroutine = null;
        }

        _currentTarget = null;
        _targetStats = null;
        _targetStamina = null;

        Debug.Log($"[DamageZone] '{other.name}' 이탈. 데미지 중지.");
    }

    void OnDisable()
    {
        // 청크 언로드 시 코루틴 종료 보장
        if (_damageCoroutine != null)
        {
            StopCoroutine(_damageCoroutine);
            _damageCoroutine = null;
        }

        _currentTarget = null;
        _targetStats = null;
        _targetStamina = null;
    }

    /// <summary>
    /// 주기적으로 플레이어의 스태미나를 감소시키는 코루틴
    /// </summary>
    private IEnumerator ApplyDamageCoroutine()
    {
        while (true)
        {
            // 안전장치: 플레이어가 null이거나 비활성화되었는지 체크
            if (_currentTarget == null || !_currentTarget.activeInHierarchy || _targetStats == null)
            {
                Debug.LogWarning("[DamageZone] 플레이어가 제거되었거나 비활성화되었습니다. 데미지 중지.");
                _damageCoroutine = null;
                yield break;
            }

            // 캐싱된 WaitForSeconds 재사용 (GC 최적화)
            yield return _waitObj;

            // 무적(무적 유물 등) 중에는 이 틱을 건너뛴다. AddInjury 자체도 무적을 존중하지만
            // 여기서 먼저 걸러야 로그가 안 쌓인다. ApplyHazardDamage 라우팅은 매 틱 i-frame/플래시를
            // 만들어 지속 DoT 성격이 깨지므로 사용하지 않는다. 무적 종료 후 다음 틱부터 정상 재개.
            if (_targetStats.IsInvincible)
                continue;

            // 데미지 적용 — 부상(injury)으로 MaxStamina를 깎는다.
            // 현재 스태미나를 깎으면 리젠으로 되돌아와 지속 피해가 무의미해진다.
            float damageAmount = damagePerSecond * damageInterval;
            if (_targetStamina != null)
                _targetStamina.AddInjury(damageAmount);
            else
                _targetStats.UseStamina(damageAmount); // 폴백: StaminaManager를 못 찾은 구성

            Debug.Log($"[DamageZone] {damageAmount} 최대 스태미나 감소 적용. 현재 최대: {_targetStats.MaxStamina}");
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // 에디터에서 데미지 존 영역 시각화
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f); // 빨간색 반투명
            Gizmos.DrawCube(transform.position, col.bounds.size);
        }
    }
#endif
}
