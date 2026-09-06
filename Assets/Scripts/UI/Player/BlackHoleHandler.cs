// @tags: player, black-hole, gravity, handler, damage
using UnityEngine;

/// <summary>
/// 블랙홀 구역 진입 시 플레이어에게 적용되는 인력 및 중심 도달 시 넉백/데미지 처리.
/// BlackHoleZone이 Activate/Deactivate를 호출한다.
/// </summary>
public class BlackHoleHandler : MonoBehaviour
{
    public bool IsActive { get; private set; }

    private Rigidbody2D _rb;
    private PlayerController _playerController;
    private StaminaManager _staminaManager;

    private Transform _blackHoleCenter;
    private SpecialChunkSettingsData.BlackHoleZoneData _data;
    private BlackHoleZone _currentZone;

    private float _lastDamageTime;
    // 중심 도달 판정 거리 (이 거리 안으로 들어오면 데미지와 넉백을 받음)
    private readonly float _centerThreshold = 0.5f; 

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _playerController = GetComponent<PlayerController>();
        _staminaManager = GetComponent<StaminaManager>();
    }

    public void Activate(BlackHoleZone zone, Transform center, SpecialChunkSettingsData.BlackHoleZoneData data)
    {
        if (IsActive) return;
        
        IsActive = true;
        _currentZone = zone;
        _blackHoleCenter = center;
        _data = data;
        _lastDamageTime = 0f; // 진입 후 중심에 닿으면 즉시 데미지를 주도록 초기화
    }

    public void Deactivate()
    {
        IsActive = false;
        _currentZone = null;
        _blackHoleCenter = null;
        _data = null;
    }

    private void FixedUpdate()
    {
        if (!IsActive || _blackHoleCenter == null || _data == null) return;

        Vector2 playerPos = _rb.position;
        Vector2 centerPos = _blackHoleCenter.position;
        float distance = Vector2.Distance(playerPos, centerPos);
        
        // 방향 벡터 계산 (플레이어 -> 블랙홀 중심)
        Vector2 direction = (centerPos - playerPos).normalized;

        // 1. 지속적인 인력 적용 (빨아들이기)
        _rb.AddForce(direction * _data.pullForce);

        // 2. 중심부 도달 판정 (데미지 및 넉백)
        if (distance <= _centerThreshold)
        {
            if (Time.time - _lastDamageTime >= _data.tickInterval)
            {
                ApplyCenterDamageAndKnockback(direction);
                _lastDamageTime = Time.time;
            }
        }
    }

    private void ApplyCenterDamageAndKnockback(Vector2 pullDirection)
    {
        // 1. 스태미나 감소 (데미지)
        if (_staminaManager != null)
        {
            _staminaManager.AddInjury(_data.damagePerTick);
            // 화면 번쩍임 효과 (기존 추락 데미지와 동일한 피드백)
            HitFlashUI.Instance?.Flash(0.5f, 0.2f);
        }

        // 2. 넉백 처리 (빨려 들어오던 방향의 반대로 튕겨냄)
        Vector2 knockbackDir = -pullDirection;
        
        // 만약 완벽히 중앙에 겹쳐서 벡터가 0이 되었다면, 살짝 대각선 위로 튕겨냅니다.
        if (knockbackDir.magnitude < 0.1f) 
        {
            knockbackDir = new Vector2(Random.Range(-1f, 1f), 1f).normalized;
        }

        // 약간 위쪽으로 솟구치도록 보정 (+ Vector2.up)
        knockbackDir = (knockbackDir + Vector2.up * 0.5f).normalized;

        // 튕겨나가기 전에 현재 속도를 0으로 만들어, 빨려 들어가던 관성과 넉백 힘이 꼬이지 않게 함
        _rb.linearVelocity = Vector2.zero;
        
        // Impulse(순간적인 힘)로 강하게 튕겨냄
        _rb.AddForce(knockbackDir * _data.knockbackForce, ForceMode2D.Impulse);
    }
}
