// @tags: player, anti-gravity, gravity, handler, phase
using System.Collections;
using UnityEngine;

/// <summary>
/// 반중력 구역 진입 시 플레이어 중력 위상 전환·스프라이트 flip 처리.
/// AntiGravityZone이 SetPhase(phase)/ExitZone()을 호출한다.
/// PlayerController는 IsGravityInverted만 읽어 점프 방향을 결정한다.
///
/// 위상: Normal(강한 아래) → LowNormal(약한 아래·달 저중력) → Inverted(강한 위)
///        → LowInverted(약한 위·달 저중력). 각 방향에서 중력이 약해진 뒤 방향이 뒤집힌다.
///
/// [실행 순서] PlayerController(기본 order 0)는 매 FixedUpdate에서 gravityScale을
/// defaultGravity로 되돌린다. 이 핸들러를 그보다 뒤(order 100)에 실행시켜 FixedUpdate에서
/// gravityScale을 목표값으로 다시 덮어써야, 물리 적분 직전에 저중력/반전이 실제로 적용된다.
/// 덕분에 PlayerController 로직을 수정하지 않아도 된다.
///
/// [flip 방식] Rigidbody2D가 Freeze Rotation Z로 회전을 매 스텝 0으로 되돌리므로
/// transform 회전으로는 뒤집을 수 없다. 대신 localScale.y 부호를 뒤집어(1↔-1) 위아래를
/// 반사한다. 좌우 flip은 PlayerController가 localScale.x로 처리하므로 충돌 없다.
///
/// 전환 연출은 스쿼시&스트레치다. y를 0까지 줄이면(=1→0→-1 lerp) 직교 카메라에서는
/// 회전이 아니라 "납작하게 찌그러짐"으로 보이므로, y는 flipSquashY까지만 줄이고 그
/// 지점에서 부호를 스냅한 뒤 되돌린다. 동시에 x를 flipStretchX까지 넓혀 부피감을 준다.
/// (Z축 회전은 점대칭이라 좌우까지 뒤집혀 캐릭터가 뒤를 보게 되므로 쓰지 않는다.)
///
/// 기준 크기(_baseScaleX/Y)는 Awake에서 한 번만 저장한다. 전환 도중 값(예: 0.35)을
/// Mathf.Abs로 다시 기준 삼으면 중단 시 축소가 영구 고착된다.
/// </summary>
[DefaultExecutionOrder(100)]
public class AntiGravityHandler : MonoBehaviour
{
    [SerializeField] private float antiGravityMultiplier = 0.7f;
    [Tooltip("저중력(달) 위상의 중력 배수. 0=무중력, 1=원래 중력")]
    [SerializeField] private float lowGravityFactor = 0.3f;
    [Tooltip("방향 전환 시 뒤집힘·속도 완화 전환 시간(초)")]
    [SerializeField] private float transitionDuration = 0.25f;
    [Tooltip("뒤집기 중간의 세로 스케일 배율. 0에 가까울수록 납작해진다(0 금지)")]
    [SerializeField, Range(0.1f, 1f)] private float flipSquashY = 0.35f;
    [Tooltip("뒤집기 중간의 가로 스케일 배율. 얇아지는 만큼 넓혀 부피감을 준다")]
    [SerializeField, Range(1f, 2f)] private float flipStretchX = 1.25f;

    public bool IsActive { get; private set; }            // 중력이 위쪽을 향하는 위상(뒤집힘)
    public bool IsGravityInverted => IsActive || _transition != null;

    /// <summary>저중력·역중력으로 중력을 통제 중인지. PlayerController가 세로 속도/바닥밀착을
    /// 건너뛸지 판단하는 데 사용한다. 정상 단계·존 밖에서는 false.</summary>
    public bool OverridesGravity => _overrideGravity;

    private Rigidbody2D _rb;
    private float _defaultGravity;
    private Coroutine _transition;
    private GravityPhase _currentPhase = GravityPhase.Normal;

    // PlayerController의 매 프레임 gravityScale 원복을 이겨내기 위해 FixedUpdate에서 재적용.
    private bool _overrideGravity;
    private float _targetGravityScale;

    private PlayerController _playerController;
    private StaminaManager _staminaManager;
    private PlayerStat _playerStat;
    private Animator _animator;
    private float _peakUpwardSpeed;
    private float _antiGravityAirTime;

    // 스케일 기준값. 전환 도중 값을 기준으로 삼으면 축소가 누적·고착되므로 Awake에서만 잡는다.
    private float _baseScaleX;
    private float _baseScaleY;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _defaultGravity = _rb.gravityScale;
        _baseScaleX = Mathf.Abs(transform.localScale.x);
        _baseScaleY = Mathf.Abs(transform.localScale.y);
        _playerController = GetComponent<PlayerController>();
        _staminaManager = GetComponent<StaminaManager>();
        _playerStat = GetComponent<PlayerStat>();
        _animator = GetComponent<Animator>();
    }

    /// <summary>존이 현재 위상을 전달. 같은 위상 재호출은 무시(idempotent).</summary>
    public void SetPhase(GravityPhase phase)
    {
        if (_rb == null) return;
        if (phase == _currentPhase) return;

        bool wasInverted = GravityPhases.IsInvertedDirection(_currentPhase);
        bool invertedDir = GravityPhases.IsInvertedDirection(phase);
        _currentPhase = phase;
        StopTransition();

        // 중력 목표값 설정. Normal은 override 해제(원래 중력 그대로).
        if (phase == GravityPhase.Normal)
        {
            _overrideGravity = false;
            _rb.gravityScale = _defaultGravity;
        }
        else
        {
            _targetGravityScale = GravityPhases.ScaleFor(phase, _defaultGravity, antiGravityMultiplier, lowGravityFactor);
            _overrideGravity = true;
            _rb.gravityScale = _targetGravityScale;
        }

        IsActive = invertedDir;
        ResetImpactTracking();

        // 카메라도 역중력 방향에 맞춰 180° 롤(플레이어 flip과 동기).
        CameraFollow.Instance?.SetGravityFlip(invertedDir);

        // 방향이 바뀌었으면 부드럽게 뒤집고, 아니면 즉시 현재 방향으로 맞춘다.
        if (invertedDir != wasInverted)
            _transition = StartCoroutine(FlipTransition(invertedDir));
        else
            SetFlip(invertedDir);
    }

    /// <summary>존 이탈/언로드 시 호출. 즉시 정상 복귀.</summary>
    public void ExitZone()
    {
        _currentPhase = GravityPhase.Normal;
        _overrideGravity = false;
        StopTransition();
        if (_rb == null) return;
        _rb.gravityScale = _defaultGravity;
        SetFlip(false);
        CameraFollow.Instance?.SetGravityFlip(false); // 존 이탈 시 카메라 정상 복귀
        IsActive = false;
        ResetImpactTracking();
    }

    // 방향 전환: 세로 속도를 완화하며 스쿼시&스트레치로 뒤집는다.
    private IEnumerator FlipTransition(bool inverted)
    {
        float elapsed = 0f;
        float startVelY = _rb.linearVelocity.y;
        bool startInverted = transform.localScale.y < 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, Mathf.Lerp(startVelY, 0f, t));

            // 전반부: 얇고 넓게 / 후반부: 원복. y가 0을 통과하지 않아 납작해지지 않는다.
            float half = t < 0.5f ? t * 2f : (1f - t) * 2f;
            float squashY = Mathf.Lerp(1f, flipSquashY, half);
            float stretchX = Mathf.Lerp(1f, flipStretchX, half);

            // 가장 얇아지는 중간 지점에서 위아래 부호를 스냅 → 전환이 눈에 띄지 않는다.
            ApplyScale(t < 0.5f ? startInverted : inverted, squashY, stretchX);
            yield return null;
        }

        _transition = null;
        SetFlip(inverted); // 부호·크기 정확히 확정
    }

    // inverted=true → localScale.y 음수(위아래 반전), false → 양수(정상).
    private void SetFlip(bool inverted) => ApplyScale(inverted, 1f, 1f);

    // 기준 크기에 배율을 곱해 스케일을 확정한다. x 부호(좌우 방향)는 PlayerController 소유라 보존한다.
    private void ApplyScale(bool inverted, float squashY, float stretchX)
    {
        Vector3 s = transform.localScale;
        float facing = s.x < 0f ? -1f : 1f;
        s.x = facing * _baseScaleX * stretchX;
        s.y = (inverted ? -1f : 1f) * _baseScaleY * squashY;
        transform.localScale = s;
    }

    private void ResetImpactTracking()
    {
        _peakUpwardSpeed = 0f;
        _antiGravityAirTime = 0f;
    }

    private void StopTransition()
    {
        if (_transition == null) return;
        StopCoroutine(_transition);
        _transition = null;
        SetFlip(transform.localScale.y < 0f); // 중간값(찌그러진 상태)으로 멈추지 않도록 즉시 정규화
    }

    private void FixedUpdate()
    {
        // PlayerController가 같은 FixedUpdate에서 gravityScale을 원복하므로(실행 순서상 먼저),
        // 물리 적분 직전인 여기서 목표 중력으로 다시 덮어쓴다.
        if (_overrideGravity && _rb != null)
            _rb.gravityScale = _targetGravityScale;

        if (!IsActive) return;
        if (_rb.linearVelocity.y > 0f)
            _peakUpwardSpeed = Mathf.Max(_peakUpwardSpeed, _rb.linearVelocity.y);
        if (_playerController != null && !_playerController.IsGrounded)
            _antiGravityAirTime += Time.fixedDeltaTime;
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        if (!IsActive) return;
        foreach (var contact in col.contacts)
        {
            if (contact.normal.y < -0.5f)
            {
                ApplyCeilingImpact();
                break;
            }
        }
    }

    private void ApplyCeilingImpact()
    {
        float speed = _peakUpwardSpeed;
        _peakUpwardSpeed = 0f;
        _antiGravityAirTime = 0f;

        if (_playerController == null) return;

        if (_animator != null)
            _animator.SetTrigger("Land");

        // 차징 슈퍼 점프로 (아래로) 발사된 뒤 천장으로 되돌아온 구간은 낙뎀 면제: 발사 속도만큼 차감
        float effectiveSpeed = Mathf.Max(0f, speed - _playerController.ConsumeChargeJumpLaunchSpeed());
        if (effectiveSpeed > _playerController.fallDamageThreshold && _staminaManager != null)
        {
            float excess = effectiveSpeed - _playerController.fallDamageThreshold;
            float damage = excess * _playerController.fallDamageMultiplier;
            // FallDamageReduce는 '감소율'이 아니라 곱연산 배율(기본 1, 낮을수록 좋음)이다.
            // 스탯 없으면 감면 없음 = 배율 1. PlayerController.OnLanded와 같은 규칙.
            float reduce = _playerStat != null
                ? Mathf.Clamp01(_playerStat.GetFinalValue(StatType.FallDamageReduce))
                : 1f;
            damage *= reduce;

            if (damage > 0f)
            {
                _staminaManager.AddInjury(damage);
                HitFlashUI.Instance?.Flash(0.5f, 0.2f);
                _playerStat?.StartInvincibility();
            }
        }
    }
}