using UnityEngine;

public class DrillStrategy : IMiningStrategy
{
    private PlayerMining _context;
    private Digger _digger;
    private Rigidbody2D _rb;
    public bool IsAttacking => _isDrillDashing;

    // Configuration
    private float _maxBattery;
    private float _dashSpeed;
    private float _slowMoveRatio; // ★ [추가]: 조준(우클릭) 중 이동 속도 배율
    private float _digInterval = 0.2f; // 파기 주기 (초) — 콜라이더 갱신 속도에 맞춤
    private const float IMMEDIATE_DIG_INTERVAL = 0.1f; // 선행 파기 주기 (10Hz) — Job 블로킹 절감

    // 무거운 회전감 설정 — 값이 클수록 더 느리게 돌아감 (단위: 초)
    private const float ROTATION_SMOOTH_TIME = 0.4f;

    /// <summary>
    /// 드릴 파기 반경(월드 유닛)의 기본값. StatType.DrillRadius의 기준값이고
    /// 업그레이드('드릴 확장 비트')가 여기에 더한다.
    /// PlayerStat이 이 상수를 기본값으로 심으므로 두 곳의 숫자가 갈라질 일이 없다.
    ///
    /// ⚠ 배율이 아니라 **절대 반경**이다(2026-09-01). 예전엔 Digger.digRadius(=MiningRange)에
    /// 곱하는 배율이라 '넓은 삽날'(MiningRangeUp)을 살 때마다 드릴까지 같이 커졌다.
    /// 지금은 삽·곡괭이(MiningRange)와 드릴(DrillRadius)이 완전히 분리돼 서로 안 건드린다.
    /// </summary>
    public const float DefaultDigRadius = 0.5f;

    /// <summary>
    /// 대시(선행 파기) 반경 = 드릴 반경 × 이 값.
    /// 분리 전 (대시 = MiningRange) : (차징 = MiningRange × 0.5) 비율을 그대로 옮긴 것이라
    /// 대시 굴의 굵기가 예전과 같다. 굴을 좁히거나 넓히려면 여기만 만지면 된다.
    /// </summary>
    public const float DashRadiusScale = 2f;

    // 배터리 소모율 (초당)
    private const float CHARGE_BATTERY_DRAIN = 0.3f; // 차징(우클릭) 중 소모율
    private const float DASH_BATTERY_DRAIN = 3.0f; // 대시(좌클릭) 중 소모율

    private float _chargingDigInterval = 0.2f; // 차징 중 파기 주기 (초)

    // State
    private float _currentBattery;
    private bool _isDrillDashing;
    private float _digTimer;
    private float _chargingDigTimer;
    private float _immediateDigTimer;
    private bool _infiniteBattery;  // F4 치트: 무한 배터리 모드

    private const string MotorLoopHandle = "drill";
    private bool _motorOn;

    public float CurrentBattery { get => _currentBattery; set => _currentBattery = Mathf.Clamp(value, 0, _maxBattery); }
    public float MaxBattery => _maxBattery;

    /// <summary>
    /// 업그레이드('드릴 절전 모드')발 소모 배율. 기준 1에서 노드가 값을 깎는다.
    /// PlayerStat이 하한(0.2)을 걸어주므로 여기서 다시 클램프하지 않는다.
    /// </summary>
    private float UpgradeDrainScale =>
        _context != null && _context.playerStats != null ? _context.playerStats.DrillDrainScale : 1f;

    /// <summary>
    /// 대시 소모 배율. 유물(과부하 배터리)과 업그레이드를 **곱하지 않고 감소량을 더한다** —
    /// 스탯 쪽 감소 노드가 전부 합연산(기준 1에서 −0.15씩 빼기)인 것과 같은 규칙이다.
    /// 곱으로 두면 둘 다 낀 사람만 복리로 싸져서, 유물을 낀 순간 절전 노드의 체감이 줄어든다.
    /// 예: 유물 0.5 + 절전 I 0.85 → 곱 0.425가 아니라 1 − (0.5 + 0.15) = 0.35.
    /// 합이라 0 아래로 내려갈 수 있어 하한을 건다(안 걸면 대시가 배터리를 채운다).
    /// </summary>
    private float DashDrainScale
    {
        get
        {
            float reduction = (1f - DashDrainMultiplier) + (1f - UpgradeDrainScale);
            return Mathf.Max(PlayerStat.MinDrillDrainScale, 1f - reduction);
        }
    }

    /// <summary>
    /// 배터리 상한을 갈아끼운다. 상한의 원본은 <see cref="StatType.DrillBatteryCapacity"/>이고,
    /// 매 프레임 PlayerMining.SyncDrillBattery()가 이걸 부른다(값이 같으면 즉시 반환).
    /// 늘어난 만큼 자동 충전하지는 않는다 — 업그레이드를 샀다고 배터리가 공짜로 차면 안 된다.
    /// </summary>
    public void SetMaxBattery(float value)
    {
        if (value <= 0f || Mathf.Approximately(value, _maxBattery)) return;
        _maxBattery = value;
        if (_currentBattery > _maxBattery) _currentBattery = _maxBattery;
    }

    // 유물(과부하 배터리) 훅. 기본값은 무효과(소모 1배·무작위 off) → 유물 미장착 시 기존 동작과 동일.
    // 기본 1 = 무효과. PlayerMining.Start가 자기 필드(_dashDrainMultiplier, 기본 1)를 여기에 덮어쓰므로
    // 실효 기본값도 1이다 — 예전 0.5는 그 덮어쓰기에 가려 한 번도 쓰이지 않던 값이었다.
    public float DashDrainMultiplier { get; set; } = 1f;   // 대시 배터리 소모 배수(0.5 = 효율 2배)
    public bool RandomDashDirection { get; set; } = false; // 대시 방향 무작위화 on/off
    public float RandomDashInterval { get; set; } = 0.5f;  // 방향 재추첨 주기(초)
    // 재추첨 시 직전 목표 각도 대비 최소 회전각(도). 클수록 매번 크게 꺾여 더 미쳐날뛴다.
    // 0이면 완전 무작위(직전 방향과 거의 같은 각을 뽑을 수도 있음). 180 미만 권장.
    public float RandomDashMinDelta { get; set; } = 0f;
    private float _randomDashAngle;   // 현재 무작위 대시 목표 각도(degrees)
    private float _randomDashTimer;   // 다음 재추첨까지 남은 시간

    // 드릴 방향 보간 상태
    private float _smoothedAngle;   // 현재 보간된 드릴 방향 각도 (degrees)
    private float _angleVelocity;   // SmoothDamp 내부 속도값
    private bool _wasAiming;       // 이전 프레임 에이밍 여부 (스냅 판정용)
    private bool _isAiming;        // 현재 프레임 에이밍 여부 (LateUpdate에서 참조)

    // ★ [수정]: 생성자에 slowMoveRatio 매개변수 추가 (기본값 0.4f = 속도 40%로 감소)
    public DrillStrategy(float maxBattery, float dashSpeed, float slowMoveRatio = 0.4f)
    {
        _maxBattery = maxBattery;
        _dashSpeed = dashSpeed;
        _slowMoveRatio = slowMoveRatio; // ★ [추가]
        _currentBattery = maxBattery;
    }

    public void Enter(PlayerMining context)
    {
        _context = context;
        _rb = context.controller.GetComponent<Rigidbody2D>();

        // Digger 참조 캐싱 — 같은 GameObject 우선, 없으면 씬 전체 탐색
        _digger = context.GetComponent<Digger>();
        if (_digger == null) _digger = Object.FindFirstObjectByType<Digger>();

        ResetState();
    }

    public void Exit()
    {
        StopDrillDash();
        _context.playerAnimator.SetBool("DrillAiming", false);

        // 전략 교체(툴 스왑) 시 모터 소리가 남으면 안 된다
        if (SoundManager.Instance != null)
            SoundManager.Instance.StopLoop(MotorLoopHandle, 0.1f);
        _motorOn = false;

        // ★ [추가]: 전략 종료(툴 스왑 등) 시 컨트롤러 이동 속도 및 상태 안전 복구
        if (_context != null)
        {
            _context.ResetControllerState();
        }

        _context.ResetRotations();
        _context = null;
    }

    public void HandleUpdate()
    {
        if (_context == null) return;

        // F4: 무한 배터리 토글
        if (Input.GetKeyDown(KeyCode.F4))
        {
            _infiniteBattery = !_infiniteBattery;
            if (_infiniteBattery) _currentBattery = _maxBattery;
            Debug.Log($"[DrillStrategy] 무한 배터리 {(_infiniteBattery ? "ON" : "OFF")}");
        }

        // 공중에서는 에이밍(우클릭) 시작 불가 — 단, 이미 대시 중이면 계속 허용
        bool grounded = _context.controller.IsGrounded;
        bool isAiming = (grounded || _isDrillDashing) && Input.GetMouseButton(1);
        _context.playerAnimator.SetBool("DrillAiming", isAiming);

        // ▼▼▼ ★ [추가]: SapStrategy와 동일한 조준 중 감속 로직 ▼▼▼
        if (isAiming && !_isDrillDashing)
        {
            _context.controller.isMiningAction = true;
            _context.controller.speedMultiplier = _slowMoveRatio; // 이동 속도 감소 적용
        }
        else if (!_isDrillDashing)
        {
            _context.controller.isMiningAction = false;
            _context.controller.speedMultiplier = 1.0f; // 일반 상태로 속도 복구
        }
        // ▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲

        // 방향 보간 계산 (회전 적용은 HandleLateUpdate에서 수행)
        _isAiming = isAiming;
        if (isAiming)
        {
            // 마우스 위치에 따라 즉시 캐릭터를 뒤집던 기존 코드 제거!
            // _context.HandleFlip();

            // 마우스 방향 계산
            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 toMouse = (Vector2)mouseWorld - (Vector2)_context.transform.position;

            // 차징 중(대시 X): 수평 기준 아래로 최대 20도까지만 허용
            if (!_isDrillDashing)
            {
                float minY = -Mathf.Abs(toMouse.x) * Mathf.Tan(20f * Mathf.Deg2Rad);
                if (toMouse.y < minY)
                    toMouse.y = minY;
            }

            float targetAngle = Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;

            // 과부하 배터리 유물: 대시 중에는 마우스 대신 주기적으로 무작위 방향으로 재추첨한다.
            if (RandomDashDirection && _isDrillDashing)
            {
                _randomDashTimer -= Time.deltaTime;
                if (_randomDashTimer <= 0f)
                {
                    float minDelta = Mathf.Clamp(RandomDashMinDelta, 0f, 179f);
                    float offset = Random.Range(minDelta, 360f - minDelta);
                    _randomDashAngle = Mathf.Repeat(_randomDashAngle + offset, 360f);
                    _randomDashTimer = RandomDashInterval;
                }
                targetAngle = _randomDashAngle;
            }

            // 에이밍 시작 첫 프레임: 현재 마우스 방향으로 즉시 스냅 (튀는 현상 방지)
            if (!_wasAiming)
            {
                _smoothedAngle = targetAngle;
                _angleVelocity = 0f;
            }

            // 무거운 회전감: 각도를 서서히 보간 (마우스를 많이 움직여야 돌아감)
            _smoothedAngle = Mathf.SmoothDampAngle(_smoothedAngle, targetAngle, ref _angleVelocity, ROTATION_SMOOTH_TIME);

            // ▼▼▼ [수정됨: 반대 방향으로 플립] ▼▼▼
            float currentDirX = Mathf.Cos(_smoothedAngle * Mathf.Deg2Rad);
            // ±0.1f 데드존 유지

            if (currentDirX < -0.1f) // 드릴이 왼쪽(-)을 향하고 있음
            {
                // **반대:** 캐릭터는 오른쪽(+)을 바라보게 합니다.
                if (!_context.controller.isFacingRight)
                {
                    _context.controller.Flip();
                }
            }
            else if (currentDirX > 0.1f) // 드릴이 오른쪽(+)을 향하고 있음
            {
                // **반대:** 캐릭터는 왼쪽(-)을 바라보게 합니다.
                if (_context.controller.isFacingRight)
                {
                    _context.controller.Flip();
                }
            }
            // ▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲
        }
        else
        {
            _angleVelocity = 0f;
        }

        _wasAiming = isAiming;

        // 차징 중(에이밍 O, 대시 X): 배터리 소모 + 느린 파기
        if (isAiming && !_isDrillDashing)
        {
            if (!_infiniteBattery)
            {
                _currentBattery -= Time.deltaTime * CHARGE_BATTERY_DRAIN * UpgradeDrainScale;
                if (_currentBattery < 0) _currentBattery = 0;
            }

            if (_digger != null)
            {
                _chargingDigTimer += Time.deltaTime;
                if (_chargingDigTimer >= _chargingDigInterval)
                {
                    _chargingDigTimer = 0f;
                    // 클램프된 _smoothedAngle 방향으로 파기 (시각적 드릴 방향과 일치)
                    Vector2 clampedDir = new Vector2(
                        Mathf.Cos(_smoothedAngle * Mathf.Deg2Rad),
                        Mathf.Sin(_smoothedAngle * Mathf.Deg2Rad));
                    Vector2 chargeDigPos = (Vector2)_context.transform.position + clampedDir * _digger.DrillDashRadius * 2f;
                    _digger.RequestDig(chargeDigPos);
                }
            }
        }
        else if (!isAiming)
        {
            _chargingDigTimer = 0f;
        }

        // 대시 시작/종료
        if (isAiming)
        {
            if (Input.GetMouseButton(0) && _currentBattery > 0)
            {
                if (!_isDrillDashing) StartDrillDash();
            }
            else
            {
                if (_isDrillDashing) StopDrillDash();
            }
        }
        else
        {
            if (_isDrillDashing) StopDrillDash();
        }

        if (_isDrillDashing)
        {
            // 배터리 소모 (무한 배터리 모드일 때 스킵)
            if (!_infiniteBattery)
            {
                _currentBattery -= Time.deltaTime * DASH_BATTERY_DRAIN * DashDrainScale;
                if (_currentBattery <= 0)
                {
                    _currentBattery = 0;
                    StopDrillDash();

                    // 파워다운음(SfxKeys.PowerDown)은 현재 사용하지 않는다 — 루프 정리만 수행
                    if (SoundManager.Instance != null)
                        SoundManager.Instance.StopLoop(MotorLoopHandle, 0.1f);
                    _motorOn = false;
                    return;
                }
            }

            // 타이머 기반 자동 파기 — Digger.RequestDig()을 통해 "연속 클릭" 흉내
            _digTimer += Time.deltaTime;
            if (_digTimer >= _digInterval)
            {
                _digTimer = 0f;
                if (_digger != null)
                {
                    Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    _digger.RequestDig(mousePos);
                }
            }
        }

        // 모터 루프 — 차징 중이거나 대시 중이면 돌고, 아니면 멈춘다
        UpdateMotorLoop((isAiming || _isDrillDashing) && _currentBattery > 0f);
    }

    /// <summary>드릴 모터 루프 on/off. 상태가 바뀔 때만 SoundManager를 건드린다.</summary>
    private void UpdateMotorLoop(bool shouldRun)
    {
        if (shouldRun == _motorOn) return;
        _motorOn = shouldRun;

        var sm = SoundManager.Instance;
        if (sm == null) return;

        // 모터 루프음(SfxKeys.DrillMotor)은 현재 사용하지 않는다 — 재생만 끄고 상태 추적은 유지
        // if (shouldRun) sm.Loop(MotorLoopHandle, SfxKeys.DrillMotor);
        if (!shouldRun) sm.StopLoop(MotorLoopHandle);
    }

    public void HandleFixedUpdate()
    {
        if (_rb == null || !_isDrillDashing) return;

        // 보간된 드릴 방향 사용 (시각적 회전과 이동 방향을 일치시킴)
        Vector2 playerPos = _context.transform.position;
        Vector2 direction = new Vector2(
            Mathf.Cos(_smoothedAngle * Mathf.Deg2Rad),
            Mathf.Sin(_smoothedAngle * Mathf.Deg2Rad));

        // 선행 파기: 빈도 제한 (10Hz) — EnsureJobsCompleted 동기 블로킹 횟수 절감
        if (_digger != null)
        {
            _immediateDigTimer += Time.fixedDeltaTime;
            if (_immediateDigTimer >= IMMEDIATE_DIG_INTERVAL)
            {
                _immediateDigTimer = 0f;

                float dashRadius = _digger.DrillDashRadius;
                Vector2 preDigPos = playerPos + direction * dashRadius;
                _digger.ImmediateDig(preDigPos, dashRadius);

                // 암석 데미지: 선행 파기와 동일 빈도로 통합
                _digger.ImmediateDigRock(preDigPos, dashRadius);
            }
        }

        Vector2 targetVel = direction * _dashSpeed;
        _rb.linearVelocity = targetVel;
    }

    // Animator가 실행된 뒤 LateUpdate에서 드릴 방향 회전 적용
    public void HandleLateUpdate()
    {
        if (_context == null) return;

        if (_isAiming)
        {
            Vector2 smoothedDir = new Vector2(
                Mathf.Cos(_smoothedAngle * Mathf.Deg2Rad),
                Mathf.Sin(_smoothedAngle * Mathf.Deg2Rad));
            Vector3 virtualTarget = _context.transform.position + (Vector3)(smoothedDir * 10f);

            // forceDrillDashRot=_isDrillDashing: 차징 중엔 팔만 돌리고, 대시 중엔 몸 전체를 돌진 방향으로 회전!
            _context.LookAtTarget(virtualTarget, forceDrillDashRot: _isDrillDashing);
        }
        else if (!_isDrillDashing)
        {
            _context.ResetRotations();
        }
    }

    public bool CanSwitchTool()
    {
        return !_isDrillDashing;
    }

    private void StartDrillDash()
    {
        _isDrillDashing = true;
        _randomDashAngle = _smoothedAngle;
        _randomDashTimer = RandomDashInterval;
        _digTimer = _digInterval; // 즉시 첫 파기 발동
        _context.playerAnimator.SetBool("DrillDash", true);
        _context.controller.isDashing = true;
        _context.controller.isMiningAction = true;

        // ★ [추가]: 드릴 대시(돌진) 시작 시 감속을 풀고 100% 이동 속도로 복귀!
        _context.controller.speedMultiplier = 1.0f;

        CameraFollow.Instance?.SetDrillZoom(true);
    }

    private void StopDrillDash()
    {
        _isDrillDashing = false;
        _digTimer = 0f;
        if (_context != null)
        {
            _context.playerAnimator.SetBool("DrillDash", false);
            _context.controller.isDashing = false;
            _context.controller.isMiningAction = false;

            // ★ [추가]: 대시 중단 시에도 이동 속도 정상화
            _context.controller.speedMultiplier = 1.0f;
        }
        CameraFollow.Instance?.SetDrillZoom(false);
    }

    private void ResetState()
    {
        _isDrillDashing = false;
        _digTimer = 0f;
        _chargingDigTimer = 0f;
        _immediateDigTimer = 0f;
        _smoothedAngle = 0f;
        _angleVelocity = 0f;
        _wasAiming = false;
    }

    public float GetChargeRatio()
    {
        return Mathf.Clamp01(_currentBattery / _maxBattery);
    }

    public bool IsCharging => _isAiming;
    public bool IsDrillDashing => _isDrillDashing;

    public DigParameters GetDigParameters(float baseRadius, TileType targetTileType)
    {
        if (_currentBattery <= 0 && !_infiniteBattery)
            return new DigParameters { CanDig = false };

        // 층 감쇠는 드릴에도 건다. 드릴 해금(DrillCapacity_T1_01)이 하필 감쇠가 시작되는
        // 2지층 노드라, 여기서 빼면 드릴을 사는 순간 층 감쇠 설계 전체가 무효가 된다.
        // '땅이 얼어서 안 파진다'는 도구가 아니라 지형의 성질이므로 도구를 가리지 않는 게 맞다.
        // 되돌리려면 이 곱셈 한 줄만 지우면 된다.
        //
        // baseRadius는 Digger.ActiveDigRadius가 넘겨준 **드릴 전용 반경**(StatType.DrillRadius)이다.
        // 그래서 여기서 반경 배율을 또 곱하지 않는다 — 곱하면 드릴 반경이 제곱된다.
        // MiningRange는 어느 쪽으로도 안 들어온다 — 삽 범위 업그레이드와 완전 분리(2026-09-01).
        PlayerStat stats = (_context != null) ? _context.playerStats : null;

        // 채광 면허 게이트는 도구를 가리지 않는다 — 드릴만 빼면 드릴을 사는 순간
        // 면허 체계 전체가 무효가 된다(층 감쇠를 드릴에도 거는 것과 같은 이유).
        // 막혔을 때는 층 감쇠 대신 게이트 계수를 쓴다(삽·곡괭이와 같은 해석 — 곱하지 않고 대체).
        bool blocked = MiningLevelGate.IsBlocked(targetTileType, stats,
                                                 out int requiredMiningLevel, out int playerMiningLevel);

        float radius = blocked
            ? MiningLevelGate.BlockedRadiusMultiplier
            : LayerDigModifier.Resolve(targetTileType, stats);

        return new DigParameters
        {
            CanDig = true,
            RadiusMultiplier = radius,
            DamageMultiplier = 1f,
            ToolIndex = 3,
            CanDigTerrain = true,
            CanDigRock = true,
            IgnoreStaminaCost = true,  // 드릴은 배터리만 소모, 스태미나 무시
            MiningLevelBlocked = blocked,
            RequiredMiningLevel = requiredMiningLevel,
            CurrentMiningLevel = playerMiningLevel,
            BlockedTileType = targetTileType
        };
    }
}