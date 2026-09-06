using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour, IPlayerController
{
    [Header("이동 및 점프 설정")]
    public float moveSpeed = 5f;
    public float jumpForce = 10f;
    public float jumpCooldown = 0.2f;
    private float _lastJumpTime = -100f;
    private const float JUMP_STATE_GRACE = 0.05f;

    [Header("지상/지하 이동속도 배율")]
    [Tooltip("지상 씬에서의 이동속도 배율. 1보다 크면 지상에서 더 빠르다.")]
    [Range(0.1f, 3f)] public float surfaceSpeedMultiplier = 1.38f;

    [Tooltip("지하(그 외 씬)에서의 이동속도 배율. 1보다 작으면 지하에서 더 느리다.")]
    [Range(0.1f, 3f)] public float undergroundSpeedMultiplier = 0.85f;

    [Tooltip("지상으로 취급할 씬 이름 추가분. SurfaceSceneRegistry.Names 는 항상 함께 본다.")]
    public string[] extraSurfaceScenes;

    /// <summary>
    /// 지상/지하 배율. 씬 판정은 <see cref="SurfaceSceneRegistry"/>(단일 출처)에 맡기고,
    /// 결과만 캐시한다 — FixedUpdate마다 Scene.name을 읽으면 매 프레임 문자열이 생긴다.
    /// </summary>
    private float _environmentSpeedMultiplier = 1f;
    public float EnvironmentSpeedMultiplier => _environmentSpeedMultiplier;

    [Header("벽타기 설정")]
    public float wallClimbSpeed = 3f;
    public LayerMask wallLayer;
    public Collider2D bodyCollider;
    public float climbCooldown = 1f;
    private float _lastClimbToggleTime = -100f;

    [Header("지층 벽 미끄러짐(얼음층 등)")]
    [Tooltip("벽에서 미끄러지는 속도의 상한(유닛/초). tileData.json의 wallSlipForce는 여기까지 도달하는 가속도다.")]
    public float maxWallSlipSpeed = 0.8f;

    [Tooltip("위로 오르는 동안 이미 붙은 미끄러짐이 사라지는 속도(유닛/초²). 낮으면 오르기 시작해도 한동안 끌려 내려간다.")]
    public float wallSlipRecovery = 6f;

    // 지금 벽에 붙어 있는 동안 누적된 미끄러짐 속도(양수 = 아래로). 사다리에는 적용하지 않는다.
    private float _wallSlipSpeed = 0f;
    // 층 데이터(wallSlipForce) 조회 캐시. 청크가 바뀔 때만 다시 읽는다.
    private int _wallSlipChunkY = int.MinValue;
    private float _wallSlipAccel = 0f;

    [Header("사다리 설정")]
    public float ladderClimbSpeed = 8f;
    public LayerMask ladderLayer;
    public float ladderAnimSpeed = 1.5f;
    private bool isOverlappingLadder = false;

    [Header("경사면 제한 설정")]
    public float maxSlopeAngle = 55f;
    public float wallAngleMargin = 15f;
    [Range(0.1f, 1f)] public float minSlopeSpeedFactor = 0.5f;

    [Tooltip("평소 이동속도를 넘어선 수평 속도가 초당 이만큼씩 깎여 내려온다.")]
    public float excessSpeedDecay = 8f;

    [Header("바닥 감지")]
    public Collider2D groundCollider;
    public LayerMask groundLayer;

    [Header("마찰력 설정")]
    public float normalFriction = 0.4f;
    public float steepSlopeFriction = 0.4f;
    public float iceFriction = 0.02f;
    public float iceAcceleration = 8f;

    private PhysicsMaterial2D groundFrictionMat;
    private PhysicsMaterial2D airFrictionMat;

    [Header("낙하 설정")]
    public float maxFallSpeed = 20f;
    [Tooltip("이 높이(유닛) 이상에서 떨어져 착지하면 낙하 피해. 속도 환산은 중력에서 자동으로 한다.")]
    public float fallDamageHeight = 1.8f;
    public float fallDamageMultiplier = 2f;

    /// <summary>
    /// fallDamageHeight(유닛)를 착지 속도(u/s)로 환산한 낙뎀 임계값. v = sqrt(2 * g * h).
    /// 인스펙터에는 높이만 적고 속도는 여기서 파생시킨다 — gravityScale이 다른 프리팹끼리
    /// 같은 높이가 같은 체감이 되도록. 기준 중력은 defaultGravity(반중력 등으로 바뀌기 전 값).
    /// </summary>
    public float fallDamageThreshold
    {
        get
        {
            float scale = defaultGravity > 0f ? defaultGravity : (rb != null ? rb.gravityScale : 1f);
            float g = Mathf.Abs(Physics2D.gravity.y) * scale;
            return Mathf.Sqrt(2f * g * Mathf.Max(0f, fallDamageHeight));
        }
    }
    [HideInInspector] public bool isKnockedBack = false;

    private float _airTimeTimer = 0f;

    // ── 애니메이터 전용 접지 판정 ────────────────────────────────────────
    // 물리 isGrounded는 '발 콜라이더가 지형에 닿았나' 하나만 본다. 좁은 대각 굴에서
    // 몸이 벽에 끼면 발 콜라이더만 허공에 떠서 속도 0인데 공중 모션이 나온다.
    // 그래서 그림에 쓰는 접지는 따로 넓게 판정한다. 물리·점프 판정에는 쓰지 말 것.
    private bool _supportedByGround;   // HandleNormalMovement의 hasGroundContact(몸 접촉+아래 레이캐스트 보정 포함)
    private float _stuckHoverTimer;    // 공중 판정인데 사실상 정지해 있는 시간
    private const float STUCK_HOVER_TIME = 0.25f;
    private const float STUCK_HOVER_SPEED = 0.05f;

    [Header("애니메이션 제어 변수")]
    public bool canClimbMove = true;

    [Header("벽타기 애니메이션 위상(Phase) 세분화")]
    public float phase_IdleToMove_1 = 0f;
    public float phase_IdleToMove_2 = 0.5f; // [중요] 반드시 0.5여야 합니다.
    public float phase_AttachToMove_1 = 0f;
    public float phase_AttachToMove_2 = 0.5f; // [중요] 반드시 0.5여야 합니다.

    [Header("벽타기 속도 반영 방식")]
    public float climbAnimSpeedMin = 0.4f;
    public float climbAnimRefSpeed = 2f;
    public float climbAnimSpeedMax = 3f;

    private enum ClimbStartType { Idle, JustAttached, Moving }
    private ClimbStartType _climbStartType = ClimbStartType.Idle;
    private float _wallAttachTime = 0f;

    private bool _isClimbCycleLocked = false;
    private Vector2 _lockedClimbInput = Vector2.zero;
    private float _currentClimbPhase = 0f;
    private bool _isPhaseOne = true;

    // ★ [추가] 이벤트 버그 방어용 변수
    private float _climbStateEnterTime = -1f;
    private float _lastEventTime = -1f;
    private bool _prevClimbingMove = false;

    // 물리 및 상태 변수
    private bool isGrounded;
    private bool isOverlappingWall;
    public bool isWallClimbing;
    public bool IsWallClimbing => isWallClimbing;
    public bool IsGrounded => isGrounded;

    /// 애니메이터에 넘기는 접지 여부. 물리 <see cref="isGrounded"/>보다 넓다 —
    /// 몸이 지형에 지지된 경우와 굴에 끼여 멈춘 경우를 포함한다.
    /// 점프 허용·마찰 재질·OnLanded에는 쓰지 말 것(벽에 몸만 닿아도 참이 된다).
    private bool AnimGrounded => isGrounded || _supportedByGround || _stuckHoverTimer >= STUCK_HOVER_TIME;

    private bool isJumping = false;

    private Rigidbody2D rb;
    private float moveInput;
    private float vInput;
    public bool isFacingRight = true;
    private Animator anim;

    private FootstepPlayer _footstepPlayer;
    private float defaultGravity;
    private bool _jetpackActive;
    private float _jetpackAscend;
    private Vector2 _lastGroundNormal = Vector2.up;
    private float _lastSlopeAngle = 0f;

    [HideInInspector] public bool isMiningAction = false;
    [HideInInspector] public bool isDashing = false;
    [HideInInspector] public float speedMultiplier = 1f;
    [HideInInspector] public float encumbranceMultiplier = 1f;

    // 과적 페널티(EncumbranceController가 세팅). 1f = 정상.
    // 점프는 이동속도와 배율이 달라야 해서(높이는 조금만, 쿨타임은 크게) 따로 둔다.
    [HideInInspector] public float encumbranceJumpForceMultiplier = 1f;
    [HideInInspector] public float encumbranceJumpCooldownMultiplier = 1f;
    [HideInInspector] public Vector2 windVelocity = Vector2.zero;
    [HideInInspector] public bool isOnIce = false;
    private AntiGravityHandler _antiGravityHandler;
    private float _peakFallSpeed;
    private bool _prevGrounded;
    private PlayerStat _playerStat;
    private StaminaManager _staminaManager;

    public event System.Action Landed;
    public System.Func<bool> AirJumpQuery { get; set; }
    public System.Func<bool> GroundJumpQuery { get; set; }
    private float _superJumpCap;
    private float _chargeJumpLaunchSpeed;

    private ContactPoint2D[] contactBuffer = new ContactPoint2D[16];

    void Awake()
    {
        this.enabled = true;
        Rigidbody2D tempRb = GetComponent<Rigidbody2D>();
        if (tempRb != null) tempRb.bodyType = RigidbodyType2D.Dynamic;

        isWallClimbing = false;
        isMiningAction = false;
        isDashing = false;
        isJumping = false;
        canClimbMove = true;
        isKnockedBack = false;

        _isClimbCycleLocked = false;
        _lockedClimbInput = Vector2.zero;
        _wallSlipSpeed = 0f;

        _isPhaseOne = true;
        _currentClimbPhase = phase_IdleToMove_1;
        _climbStartType = ClimbStartType.Idle;

        _climbStateEnterTime = -1f;
        _lastEventTime = -1f;
        _prevClimbingMove = false;
    }

    void OnEnable()
    {
        RefreshEnvironmentSpeedMultiplier();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene from, Scene to) => RefreshEnvironmentSpeedMultiplier();

    /// <summary>정착지 씬처럼 지상 위에 얹히는 오버레이 씬도 지상으로 친다(레지스트리 목록 그대로).</summary>
    public void RefreshEnvironmentSpeedMultiplier()
    {
        bool surface = SurfaceSceneRegistry.IsActiveSceneSurface(extraSurfaceScenes);
        _environmentSpeedMultiplier = surface ? surfaceSpeedMultiplier : undergroundSpeedMultiplier;
    }

#if UNITY_EDITOR
    // 플레이 중 인스펙터에서 배율을 만졌을 때 캐시가 낡지 않도록.
    void OnValidate()
    {
        if (Application.isPlaying) RefreshEnvironmentSpeedMultiplier();
    }
#endif

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        defaultGravity = rb.gravityScale;
        _antiGravityHandler = GetComponent<AntiGravityHandler>();
        RefreshEnvironmentSpeedMultiplier();

        if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
        _playerStat = GetComponent<PlayerStat>();
        _staminaManager = GetComponent<StaminaManager>() ?? FindFirstObjectByType<StaminaManager>();

        groundFrictionMat = new PhysicsMaterial2D("GroundFriction");
        groundFrictionMat.friction = normalFriction;
        groundFrictionMat.bounciness = 0f;

        airFrictionMat = new PhysicsMaterial2D("AirFriction");
        airFrictionMat.friction = 0f;
        airFrictionMat.bounciness = 0f;

        if (bodyCollider != null)
        {
            bodyCollider.sharedMaterial = airFrictionMat;
        }
    }

    void Update()
    {
        if (LoadingData.IsLoading)
        {
            moveInput = 0f;
            vInput = 0f;
            return;
        }

        if (bodyCollider != null)
        {
            isOverlappingWall = bodyCollider.IsTouchingLayers(wallLayer);
            isOverlappingLadder = bodyCollider.IsTouchingLayers(ladderLayer);
        }

        if (_climbStartType == ClimbStartType.JustAttached && Time.time - _wallAttachTime > 0.5f)
        {
            _climbStartType = ClimbStartType.Idle;
        }

        bool shouldBlockInput = UIStateManager.IsInputBlocked
                              || (_playerStat != null && _playerStat.IsExhaustStunned)
                              || isKnockedBack;

        if (shouldBlockInput)
        {
            moveInput = 0f;
            vInput = 0f;
            _lockedClimbInput = Vector2.zero;
        }
        else
        {
            moveInput = Input.GetAxisRaw("Horizontal");
            vInput = Input.GetAxisRaw("Vertical");

            if (isWallClimbing && !isOverlappingLadder)
            {
                if (!_isClimbCycleLocked)
                {
                    if (Mathf.Abs(moveInput) > 0.1f || Mathf.Abs(vInput) > 0.1f)
                    {
                        _lockedClimbInput = new Vector2(moveInput, vInput);
                        _isClimbCycleLocked = true;

                        if (_climbStartType == ClimbStartType.JustAttached)
                            _currentClimbPhase = _isPhaseOne ? phase_AttachToMove_1 : phase_AttachToMove_2;
                        else
                            _currentClimbPhase = _isPhaseOne ? phase_IdleToMove_1 : phase_IdleToMove_2;

                        _climbStartType = ClimbStartType.Moving;
                    }
                    else
                    {
                        _lockedClimbInput = Vector2.zero;
                        if (_climbStartType == ClimbStartType.Moving)
                        {
                            _climbStartType = ClimbStartType.Idle;
                        }
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
            {
                if (Time.time - _lastClimbToggleTime >= climbCooldown)
                {
                    if (isWallClimbing)
                    {
                        ExitWallClimb();
                        _lastClimbToggleTime = Time.time;
                    }
                    else if ((isOverlappingWall || isOverlappingLadder) && !IsClimbStaminaExhausted())
                    {
                        isWallClimbing = true;
                        isMiningAction = false;
                        rb.linearVelocity = Vector2.zero;
                        _lastClimbToggleTime = Time.time;

                        _climbStartType = ClimbStartType.JustAttached;
                        _wallAttachTime = Time.time;
                        _isPhaseOne = true;
                        _currentClimbPhase = phase_AttachToMove_1;

                        if (anim != null) anim.SetTrigger("ClimbStart");

                        if (isOverlappingLadder && bodyCollider != null)
                        {
                            ContactFilter2D filter = new ContactFilter2D();
                            filter.SetLayerMask(ladderLayer);
                            filter.useTriggers = true;
                            Collider2D[] results = new Collider2D[1];
                            if (bodyCollider.Overlap(filter, results) > 0)
                            {
                                float ladderCenterX = results[0].bounds.center.x;
                                transform.position = new Vector3(ladderCenterX, transform.position.y, transform.position.z);
                            }
                        }
                    }
                }
            }

            if (Input.GetButtonDown("Jump") && (_playerStat == null || !_playerStat.IsExhausted))
            {
                bool canJumpByCooldown = Time.time - _lastJumpTime >= jumpCooldown * encumbranceJumpCooldownMultiplier;
                if (canJumpByCooldown)
                {
                    if (isGrounded && !isJumping && !isWallClimbing)
                        if (GroundJumpQuery == null || GroundJumpQuery.Invoke()) Jump();
                        else if (!isGrounded && !isWallClimbing && AirJumpQuery != null && AirJumpQuery.Invoke())
                            Jump();
                }
            }

            if (!isMiningAction && !isWallClimbing)
            {
                if (moveInput < 0 && !isFacingRight) Flip();
                else if (moveInput > 0 && isFacingRight) Flip();
            }
        }

        if (isWallClimbing && !isOverlappingWall && !isOverlappingLadder)
        {
            ExitWallClimb();
        }

        if (anim != null)
        {
            bool isRunning = Mathf.Abs(moveInput) > 0.01f && !isWallClimbing && !isMiningAction;
            anim.SetBool("isRunning", isRunning);
            anim.SetBool("isGrounded", AnimGrounded);
            anim.SetBool("isClimbing", isWallClimbing);

            if (isWallClimbing) anim.SetFloat("yVelocity", 0f);
            else anim.SetFloat("yVelocity", rb.linearVelocity.y);

            bool isClimbingMove = false;
            if (isWallClimbing)
            {
                if (isOverlappingLadder) isClimbingMove = (Mathf.Abs(moveInput) > 0.1f || Mathf.Abs(vInput) > 0.1f);
                else isClimbingMove = _isClimbCycleLocked;
            }
            else
            {
                isClimbingMove = false;
            }

            // ★ [추가] 멈춰있다가 이동을 다시 시작하는 정확한 순간의 시간을 기록합니다.
            if (isClimbingMove && !_prevClimbingMove)
            {
                _climbStateEnterTime = Time.time;
            }
            _prevClimbingMove = isClimbingMove;

            anim.SetFloat("ClimbOffset", _currentClimbPhase);
            anim.SetBool("isClimbingMove", isClimbingMove);

            if (isWallClimbing) anim.speed = (isClimbingMove) ? (isOverlappingLadder ? ladderAnimSpeed : GetClimbAnimSpeed()) : 1f;
            else anim.speed = 1f;
        }
    }

    void FixedUpdate()
    {
        _prevGrounded = isGrounded;
        if (groundCollider != null)
            isGrounded = groundCollider.IsTouchingLayers(groundLayer);

        // HandleNormalMovement가 이 스텝에서 다시 세운다. 벽타기·넉백 경로는 그 함수를 안 타므로
        // 여기서 매 스텝 내려두지 않으면 직전 값이 그대로 남는다.
        _supportedByGround = false;

        bool isWalkableSlope = _lastSlopeAngle > 0.1f && _lastSlopeAngle <= maxSlopeAngle;
        bool isMovingHorizontally = Mathf.Abs(moveInput) > 0.01f;

        bool touchingUnwalkableSlope = false;
        if (groundCollider != null)
        {
            int gCount = groundCollider.GetContacts(contactBuffer);
            for (int i = 0; i < gCount; i++)
            {
                if (Vector2.Angle(Vector2.up, contactBuffer[i].normal) > maxSlopeAngle) touchingUnwalkableSlope = true;
            }
        }
        if (bodyCollider != null)
        {
            int bCount = bodyCollider.GetContacts(contactBuffer);
            for (int i = 0; i < bCount; i++)
            {
                if (Vector2.Angle(Vector2.up, contactBuffer[i].normal) > maxSlopeAngle) touchingUnwalkableSlope = true;
            }
        }

        if (touchingUnwalkableSlope) groundFrictionMat.friction = 0f;
        else if (isOnIce) groundFrictionMat.friction = iceFriction;
        else if (isWalkableSlope && isMovingHorizontally) groundFrictionMat.friction = 0f;
        else groundFrictionMat.friction = normalFriction;

        if (groundCollider != null) groundCollider.sharedMaterial = isGrounded ? groundFrictionMat : airFrictionMat;

        if (rb.linearVelocity.y <= 0f && Time.time - _lastJumpTime > JUMP_STATE_GRACE) isJumping = false;

        if (rb.linearVelocity.y < -maxFallSpeed) rb.linearVelocity = new Vector2(rb.linearVelocity.x, -maxFallSpeed);
        else if (rb.linearVelocity.y > maxFallSpeed) rb.linearVelocity = new Vector2(rb.linearVelocity.x, maxFallSpeed);

        if (!isGrounded && !isWallClimbing)
        {
            _airTimeTimer += Time.fixedDeltaTime;
            if (rb.linearVelocity.y < 0f) _peakFallSpeed = Mathf.Max(_peakFallSpeed, Mathf.Abs(rb.linearVelocity.y));
        }

        if (isGrounded && !_prevGrounded) OnLanded();

        if (isWallClimbing && IsClimbStaminaExhausted())
        {
            ExitWallClimb();
            _lastClimbToggleTime = Time.time;
        }

        if (isKnockedBack)
        {
            if (isWallClimbing) ExitWallClimb();
            rb.gravityScale = defaultGravity;
            if (groundCollider != null) groundCollider.sharedMaterial = airFrictionMat;
            _stuckHoverTimer = 0f;
            return;
        }

        if (isWallClimbing) HandleWallClimbing();
        else HandleNormalMovement();

        UpdateStuckHoverTimer();
    }

    /// 발 콜라이더도 몸도 지형에 안 닿았는데 사실상 멈춰 있으면 '굴에 끼임'으로 본다.
    /// 점프 정점은 vy=0을 1~2프레임만 스치므로 <see cref="STUCK_HOVER_TIME"/> 게이트에 안 걸린다.
    /// 제트팩·안티중력은 의도된 체공이라 제외한다.
    private void UpdateStuckHoverTimer()
    {
        bool intentionalHover = _jetpackActive
                                || (_antiGravityHandler != null && _antiGravityHandler.OverridesGravity);

        bool hovering = !isGrounded && !_supportedByGround && !isWallClimbing && !intentionalHover
                        && rb.linearVelocity.sqrMagnitude < STUCK_HOVER_SPEED * STUCK_HOVER_SPEED;

        if (hovering) _stuckHoverTimer += Time.fixedDeltaTime;
        else _stuckHoverTimer = 0f;
    }

    private bool IsClimbStaminaExhausted()
    {
        if (isOverlappingLadder) return false;
        return _playerStat != null && _playerStat.CurrentStamina <= 0f;
    }

    private void ExitWallClimb()
    {
        isWallClimbing = false;
        canClimbMove = true;
        _isClimbCycleLocked = false;
        _lockedClimbInput = Vector2.zero;
        _wallSlipSpeed = 0f;

        _isPhaseOne = true;
        _currentClimbPhase = phase_IdleToMove_1;
        _climbStartType = ClimbStartType.Idle;

        if (anim != null) anim.SetTrigger("ClimbExit");
    }

    void OnLanded()
    {
        isJumping = false;
        _superJumpCap = 0f;
        Landed?.Invoke();
        PlayMovementSfx(false);

        if (anim != null)
        {
            anim.SetTrigger("Land");
        }

        float effectivePeak = Mathf.Max(0f, _peakFallSpeed - ConsumeChargeJumpLaunchSpeed());
        if (effectivePeak > fallDamageThreshold && _playerStat != null && !_playerStat.IsInvincible)
        {
            float excess = effectivePeak - fallDamageThreshold;
            float damage = excess * fallDamageMultiplier;
            float reduce = Mathf.Clamp01(_playerStat.GetFinalValue(StatType.FallDamageReduce));
            damage *= reduce;

            if (damage > 0f)
            {
                _staminaManager?.AddInjury(damage);
                HitFlashUI.Instance?.Flash(0.5f, 0.2f);
                _playerStat?.StartInvincibility();
            }
        }
        _peakFallSpeed = 0f;
        _airTimeTimer = 0f;
    }

    private float GetClimbAnimSpeed()
    {
        float refSpeed = climbAnimRefSpeed;
        if (refSpeed <= 0.01f && _playerStat != null)
            refSpeed = _playerStat.GetBaseValue(StatType.WallClimbSpeed);
        if (refSpeed <= 0.01f) refSpeed = wallClimbSpeed;
        if (refSpeed <= 0.01f) return 1f;

        float current = (_playerStat != null) ? _playerStat.WallClimbSpeed : wallClimbSpeed;
        float ratio = (current * encumbranceMultiplier) / refSpeed;
        return Mathf.Clamp(ratio, climbAnimSpeedMin, climbAnimSpeedMax);
    }

    void HandleWallClimbing()
    {
        rb.gravityScale = 0f;

        // 매달린 채로 채굴할 수 있으므로 지상 이동과 같은 감속을 벽타기에도 건다.
        // (삽 차징 = 느리게, 곡괭이 스윙 = 정지) — 안 걸면 스윙 중에도 그대로 기어오른다.
        // '한 손 채굴'이 잠겨 있으면 매달린 채로는 아무것도 못 캐므로 예전 그대로 둔다.
        float miningSpeedMultiplier = ClimbMiningUnlockGate.IsUnlocked ? speedMultiplier : 1f;

        if (isOverlappingLadder)
        {
            rb.linearVelocity = new Vector2(0f, vInput * ladderClimbSpeed * encumbranceMultiplier * miningSpeedMultiplier);
        }
        else
        {
            float slip = UpdateWallSlip();

            if (canClimbMove)
            {
                float baseClimbSpeed = (_playerStat != null) ? _playerStat.WallClimbSpeed : wallClimbSpeed;
                float climbSpeed = baseClimbSpeed * encumbranceMultiplier * miningSpeedMultiplier;
                rb.linearVelocity = new Vector2(_lockedClimbInput.x * climbSpeed, _lockedClimbInput.y * climbSpeed - slip);
            }
            else rb.linearVelocity = new Vector2(0f, -slip);

            if (_playerStat != null)
            {
                // 매달려만 있으면 스태미나를 안 쓴다 — 실제로 몸을 끌어올리는(입력이 잠긴)
                // 프레임에만 소모한다. 판정 기준은 애니메이션의 isClimbingMove와 같은
                // _isClimbCycleLocked라 "움직이는 그림 = 소모"가 화면과 어긋나지 않는다.
                bool isClimbMoving = canClimbMove && _isClimbCycleLocked
                                     && _lockedClimbInput.sqrMagnitude > 0.0001f
                                     && miningSpeedMultiplier > 0.0001f;
                float climbCost = isClimbMoving
                    ? _playerStat.StaminaCostPerSecond * Time.fixedDeltaTime
                    : 0f;
                if (climbCost > 0f) _playerStat.UseStamina(climbCost);

                // 벽타기 속도·스태미나 소모율 업그레이드는 수입을 안 움직여서 런 모델로는
                // 값을 매길 수 없다. 매달린 시간과 쓴 스태미나가 그 둘의 유일한 잣대다
                // (Assets/Docs/economy/upgrade-balance-charter.md §7 — 편의성 축 P13).
                // 사다리는 이 분기에 안 들어온다 — 스태미나를 안 쓰므로 제외가 맞다.
                if (SettlementManager.Instance != null)
                    SettlementManager.Instance.AddWallClimb(Time.fixedDeltaTime, climbCost);
            }
        }
    }

    /// <summary>
    /// 이번 FixedUpdate에 벽타기 속도에서 빼줄 미끄러짐 속도(양수 = 아래로).
    ///
    /// 얼음층처럼 tileData.json에 <c>wallSlipForce</c>가 있는 층에서는 벽에 붙어 가만히 있으면
    /// 그 값을 가속도로 삼아 <see cref="maxWallSlipSpeed"/>까지 조금씩 밀려 내려간다.
    /// 위로 오르는 입력을 넣는 동안에는 <see cref="wallSlipRecovery"/>로 다시 0까지 풀린다 —
    /// 오르는 순간 뚝 끊기지 않고 잠깐 끌려 내려가다 붙잡는 느낌을 남기기 위해서다.
    /// </summary>
    private float UpdateWallSlip()
    {
        float accel = ResolveWallSlipAccel();
        if (accel <= 0f)
        {
            _wallSlipSpeed = 0f;
            return 0f;
        }

        bool climbingUp = canClimbMove && _lockedClimbInput.y > 0.01f;
        if (climbingUp) _wallSlipSpeed = Mathf.MoveTowards(_wallSlipSpeed, 0f, wallSlipRecovery * Time.fixedDeltaTime);
        else _wallSlipSpeed = Mathf.MoveTowards(_wallSlipSpeed, maxWallSlipSpeed, accel * Time.fixedDeltaTime);

        return _wallSlipSpeed;
    }

    /// <summary>
    /// 지금 서 있는 층의 <c>wallSlipForce</c>. 지상 씬(InfinityMapManager 없음)이나
    /// 값이 없는 층은 0 — 미끄러짐 없음. 청크 Y가 바뀔 때만 층 데이터를 다시 읽는다.
    /// </summary>
    private float ResolveWallSlipAccel()
    {
        var map = InfinityMapManager.Instance;
        if (map == null || map.chunkHeightWorld <= 0f)
        {
            _wallSlipChunkY = int.MinValue;
            return 0f;
        }

        int chunkY = Mathf.FloorToInt(transform.position.y / map.chunkHeightWorld);
        if (chunkY != _wallSlipChunkY)
        {
            _wallSlipChunkY = chunkY;
            _wallSlipAccel = 0f;

            var mgr = TileDataManager.Instance;
            if (mgr != null)
            {
                var data = mgr.GetData(LayerDigModifier.TileTypeAtWorldY(transform.position.y));
                if (data != null && data.wallSlipForce > 0f) _wallSlipAccel = data.wallSlipForce;
            }
        }

        return _wallSlipAccel;
    }

    void HandleNormalMovement()
    {
        if (isDashing) { rb.gravityScale = 0f; return; }
        rb.gravityScale = defaultGravity;
        if (_jetpackActive) rb.gravityScale = 0f;

        bool antiGravityOverride = _antiGravityHandler != null && _antiGravityHandler.OverridesGravity;
        float currentMaxSlope = 0f;
        Vector2 primaryNormal = Vector2.up;
        bool hasGroundContact = false;

        if (isGrounded && groundCollider != null)
        {
            const float UPRIGHT_EPSILON = 0.05f;
            Vector2 normalSum = Vector2.zero;
            int contactCount = groundCollider.GetContacts(contactBuffer);
            for (int i = 0; i < contactCount; i++)
            {
                if (((1 << contactBuffer[i].collider.gameObject.layer) & groundLayer) == 0) continue;
                hasGroundContact = true;
                Vector2 n = contactBuffer[i].normal;
                if (n.y > UPRIGHT_EPSILON) normalSum += n;
            }
            if (normalSum.sqrMagnitude > 0.0001f)
            {
                primaryNormal = normalSum.normalized;
                currentMaxSlope = Vector2.Angle(Vector2.up, primaryNormal);
            }
        }

        bool bodyTouchesGround = bodyCollider != null && bodyCollider.IsTouchingLayers(groundLayer);
        if (!hasGroundContact && !isJumping && bodyTouchesGround)
        {
            Vector2 rayOrigin = groundCollider.bounds.center;
            float checkDist = groundCollider.bounds.extents.y + 0.1f;
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, checkDist, groundLayer);
            if (hit.collider != null)
            {
                hasGroundContact = true;
                primaryNormal = hit.normal;
                currentMaxSlope = Vector2.Angle(Vector2.up, hit.normal);
            }
        }

        // 몸 접촉+아래 레이캐스트 보정까지 반영된 '지지됨' 결과. 애니메이터 접지 판정이 이걸 쓴다.
        _supportedByGround = hasGroundContact;

        if (hasGroundContact) { _lastGroundNormal = primaryNormal; _lastSlopeAngle = currentMaxSlope; }
        else if (rb.linearVelocity.y <= 0f) { _lastGroundNormal = Vector2.up; _lastSlopeAngle = 0f; }

        bool isWalkingOffLedge = false;
        if (hasGroundContact && !isJumping && Mathf.Abs(moveInput) > 0.01f)
        {
            Collider2D edgeRef = bodyCollider != null ? bodyCollider : groundCollider;
            float dir = Mathf.Sign(moveInput);
            float horizontalStep = Mathf.Abs(rb.linearVelocity.x + windVelocity.x) * Time.fixedDeltaTime;
            float edgeX = edgeRef.bounds.center.x + dir * (edgeRef.bounds.extents.x + horizontalStep);
            Vector2 frontEdgePos = new Vector2(edgeX, groundCollider.bounds.center.y);
            float castDistance = groundCollider.bounds.extents.y + 0.5f;
            RaycastHit2D ledgeHit = Physics2D.Raycast(frontEdgePos, Vector2.down, castDistance, groundLayer);
            if (ledgeHit.collider == null) isWalkingOffLedge = true;
        }

        float speed = (_playerStat != null) ? _playerStat.MoveSpeed : moveSpeed;
        float currentSpeed = speed * speedMultiplier * encumbranceMultiplier * _environmentSpeedMultiplier;
        float slopeSpeedRatio = 1f;
        bool isGoingUphill = (moveInput * _lastGroundNormal.x) < 0f;

        if (isGoingUphill && _lastSlopeAngle > 0.1f && !isWalkingOffLedge)
        {
            float slopePercent = Mathf.Clamp01(_lastSlopeAngle / maxSlopeAngle);
            slopeSpeedRatio = Mathf.Lerp(1f, minSlopeSpeedFactor, slopePercent);
        }

        float effectiveMoveSpeed = currentSpeed * slopeSpeedRatio;
        Vector2 targetVelocity = rb.linearVelocity;

        bool isWalkableSlope = hasGroundContact && (_lastSlopeAngle <= maxSlopeAngle);

        if (isWalkableSlope && !isJumping && !antiGravityOverride)
        {
            if (Mathf.Abs(moveInput) > 0.01f)
            {
                if (isWalkingOffLedge) targetVelocity = new Vector2(moveInput * effectiveMoveSpeed, rb.linearVelocity.y);
                else
                {
                    Vector2 slopeDirection = new Vector2(primaryNormal.y, -primaryNormal.x);
                    float targetY = slopeDirection.y * (moveInput * effectiveMoveSpeed);
                    if (!isGrounded && rb.linearVelocity.y < targetY - 0.5f) targetVelocity = new Vector2(slopeDirection.x * (moveInput * effectiveMoveSpeed), rb.linearVelocity.y);
                    else targetVelocity = slopeDirection * (moveInput * effectiveMoveSpeed);
                }
            }
            else
            {
                targetVelocity.x = 0f;
                if (!isOnIce)
                {
                    targetVelocity.y = 0f;
                    rb.gravityScale = 0f;
                }
            }
        }
        else
        {
            if (Mathf.Abs(moveInput) > 0.01f)
            {
                float normalTargetSpeed = moveInput * effectiveMoveSpeed;
                bool sameDir = Mathf.Sign(moveInput) == Mathf.Sign(rb.linearVelocity.x);
                bool isHighSpeed = Mathf.Abs(rb.linearVelocity.x) > effectiveMoveSpeed;
                if (sameDir && isHighSpeed) targetVelocity.x = rb.linearVelocity.x;
                else
                {
                    float snappyAcceleration = effectiveMoveSpeed * 20f;
                    targetVelocity.x = Mathf.MoveTowards(rb.linearVelocity.x, normalTargetSpeed, snappyAcceleration * Time.fixedDeltaTime);
                }
            }
            else
            {
                float deceleration = effectiveMoveSpeed * 10f;
                targetVelocity.x = Mathf.MoveTowards(rb.linearVelocity.x, 0f, deceleration * Time.fixedDeltaTime);
            }
        }

        bool hitSteepWallRight = false;
        bool hitSteepWallLeft = false;

        void CheckWallContacts(Collider2D col)
        {
            if (col == null) return;
            int count = col.GetContacts(contactBuffer);
            for (int i = 0; i < count; i++)
            {
                Vector2 normal = contactBuffer[i].normal;
                float angle = Vector2.Angle(Vector2.up, normal);
                if (angle > maxSlopeAngle + wallAngleMargin)
                {
                    if (IsOneWayPlatform(contactBuffer[i].collider)) continue;
                    if (normal.x < -0.1f) hitSteepWallRight = true;
                    if (normal.x > 0.1f) hitSteepWallLeft = true;
                }
            }
        }

        static bool IsOneWayPlatform(Collider2D other)
        {
            return other != null && other.usedByEffector && other.TryGetComponent(out PlatformEffector2D effector) && effector.enabled && effector.useOneWay;
        }

        CheckWallContacts(bodyCollider);
        CheckWallContacts(groundCollider);

        if (hitSteepWallRight && moveInput > 0f) targetVelocity.x = Mathf.Min(targetVelocity.x, 0f);
        else if (hitSteepWallLeft && moveInput < 0f) targetVelocity.x = Mathf.Max(targetVelocity.x, 0f);

        if ((hitSteepWallRight || hitSteepWallLeft) && !isJumping)
        {
            if (targetVelocity.y > 0f) targetVelocity.y = 0f;
        }

        if (isOnIce && isGrounded)
        {
            if (Mathf.Abs(moveInput) < 0.01f) targetVelocity.x = rb.linearVelocity.x;
            else targetVelocity.x = Mathf.MoveTowards(rb.linearVelocity.x, targetVelocity.x, iceAcceleration * Time.fixedDeltaTime);
        }

        targetVelocity += windVelocity;

        if (_jetpackActive)
        {
            targetVelocity.y = _jetpackAscend;
            _superJumpCap = Mathf.Max(_superJumpCap, _jetpackAscend);
        }

        float force = (_playerStat != null) ? _playerStat.JumpForce : jumpForce;
        float maxUpSpeed = Mathf.Max(force * 1.2f, _superJumpCap);
        if (targetVelocity.y > maxUpSpeed) targetVelocity.y = maxUpSpeed;

        rb.linearVelocity = targetVelocity;
        if (_superJumpCap > 0f && rb.linearVelocity.y <= 0.01f) _superJumpCap = 0f;
    }

    private float DecayExcessSpeed(float vx, float baseSpeed)
    {
        if (excessSpeedDecay <= 0f) return vx;

        if (baseSpeed <= 0.01f) return vx;

        float abs = Mathf.Abs(vx);
        if (abs <= baseSpeed) return vx;

        float decayed = Mathf.MoveTowards(abs, baseSpeed, excessSpeedDecay * Time.fixedDeltaTime);
        return decayed * Mathf.Sign(vx);
    }

    void Jump()
    {
        isJumping = true;
        _lastJumpTime = Time.time;
        _isClimbCycleLocked = false;

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
        float jumpDir = (_antiGravityHandler != null && _antiGravityHandler.IsGravityInverted) ? -1f : 1f;
        float force = ((_playerStat != null) ? _playerStat.JumpForce : jumpForce) * encumbranceJumpForceMultiplier;
        Vector2 jumpVector = Vector2.up;

        if (_lastSlopeAngle > maxSlopeAngle && _lastGroundNormal != Vector2.up)
        {
            jumpVector = (Vector2.up * 0.7f + _lastGroundNormal * 0.3f).normalized;
        }

        rb.AddForce(jumpVector * force * jumpDir, ForceMode2D.Impulse);
        PlayMovementSfx(true);
    }

    private void PlayMovementSfx(bool isJump)
    {
        if (_footstepPlayer == null) _footstepPlayer = GetComponentInChildren<FootstepPlayer>();
        if (_footstepPlayer != null)
        {
            if (isJump) _footstepPlayer.PlayJump();
            else _footstepPlayer.PlayLand();
            return;
        }
        if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(isJump ? SfxKeys.JumpDirt : SfxKeys.LandDirt);
    }

    public float ConsumeChargeJumpLaunchSpeed()
    {
        float v = _chargeJumpLaunchSpeed;
        _chargeJumpLaunchSpeed = 0f;
        return v;
    }

    public void SetJetpackThrust(bool active, float ascendSpeed)
    {
        _jetpackActive = active;
        _jetpackAscend = ascendSpeed;
    }

    public void SuperJump(float upVelocity)
    {
        isJumping = true;
        _lastJumpTime = Time.time;
        _isClimbCycleLocked = false;
        float jumpDir = (_antiGravityHandler != null && _antiGravityHandler.IsGravityInverted) ? -1f : 1f;
        float v = Mathf.Abs(upVelocity);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, v * jumpDir);
        _superJumpCap = v * 1.05f;
        _chargeJumpLaunchSpeed = Mathf.Min(v, maxFallSpeed);
    }

    public void Flip()
    {
        isFacingRight = !isFacingRight;
        Vector3 scaler = transform.localScale;
        scaler.x *= -1;
        transform.localScale = scaler;
    }

    public void ApplyExternalKnockback(Vector2 velocity, float duration)
    {
        isDashing = true;
        rb.linearVelocity = velocity;
        StartCoroutine(ResetKnockback(duration));
    }

    private System.Collections.IEnumerator ResetKnockback(float duration)
    {
        yield return new WaitForSeconds(duration);
        isDashing = false;
    }

    public void EnableClimbMove()
    {
        canClimbMove = true;
    }

    public void DisableClimbMove()
    {
        canClimbMove = false;
        if (isWallClimbing && !isOverlappingLadder)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }
    private Coroutine knockbackCoroutine;
    public void ApplyKnockback(Vector2 knockbackVel, float minDuration)
    {
        if (knockbackCoroutine != null) StopCoroutine(knockbackCoroutine);
        knockbackCoroutine = StartCoroutine(KnockbackRoutine(knockbackVel, minDuration));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Vector2 knockbackVel, float minDuration)
    {
        isKnockedBack = true;
        rb.linearVelocity = knockbackVel;
        yield return new WaitForSeconds(minDuration);
        yield return new WaitUntil(() => isGrounded);
        isKnockedBack = false;
        knockbackCoroutine = null;
    }

    public void OnClimbHalfCycleComplete()
    {
        // ★ [핵심 해결] 유니티 애니메이터 즉시 발동 버그 방어 
        // 0.5 Offset으로 재생 시작하자마자 0.5위치에 있는 이벤트가 즉각 터지는 현상 무시
        if (Time.time - _climbStateEnterTime < 0.1f) return;

        // ★ 중복 호출 방어 (이중 안전장치)
        if (Time.time - _lastEventTime < 0.1f) return;
        _lastEventTime = Time.time;

        // 다음 애니메이션 페이즈(왼손 <-> 오른손) 정상 전환
        _isPhaseOne = !_isPhaseOne;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        bool canMove = !UIStateManager.IsInputBlocked && !isKnockedBack;

        if (canMove && (Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f))
        {
            _lockedClimbInput = new Vector2(h, v);
        }
        else
        {
            // 키를 뗐을 때만 락을 풀어 확실하게 멈춤
            _isClimbCycleLocked = false;
        }
    }
}