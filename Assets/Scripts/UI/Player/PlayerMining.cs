using UnityEngine;
using System.Collections;

public class PlayerMining : MonoBehaviour
{
    [Header("필수 연결")]
    public PlayerController controller;
    public ToolController toolController;
    public Animator playerAnimator;
    public Transform armPivot;
    public Transform headBone;

    // [New] Stats Reference
    public PlayerStat playerStats;

    [Header("일반 도구 설정")]
    [Tooltip("삽 풀차징까지의 시간(초). 세션 최초 1회 MiningStaminaTuning에 심어지는 기본값이며,\n" +
             "실제 소비는 그쪽 값을 읽는다 — F8 패널에서 바꾼 값이 즉시 반영된다.")]
    public float maxChargeTime = 1.0f;
    public float slowMoveRatio = 0.3f;

    [Header("스테미나 최대치 감소")]
    [Tooltip("곡괭이 타격 성공 시 MaxStamina 감소량 (고정)")]
    [SerializeField] private float pickaxeStaminaReduction = 2f;
    // (미사용) 예전엔 벽타기 중 이 인덱스로 강제 교체했으나, 지금은 들던 도구를 그대로 유지한다.
    // '한 손 채굴'(ClimbMiningUnlockGate)을 사면 매달린 채로 캘 수도 있다.
    public int wallClimbToolIndex = 0;

    [Header("삽 파기 모양 (비우면 기존 타원)")]
    [Tooltip("삽이 파는 구멍 모양 이미지. Import Settings에서 Read/Write Enabled 필수.\n" +
             "삽이 +X(오른쪽)를 향하는 그림으로 그릴 것 — 마우스 방향으로 회전한다.\n" +
             "알파 10 초과 픽셀이 파인다. 비우면 기존 회전 타원으로 판다.")]
    public Texture2D shovelDigMask;

    [Tooltip("마스크 크기 배율. 가로는 이 값 그대로, 세로는 이 값의 0.7배가 적용됩니다.")]
    public float shovelDigMaskScale = 1f;

    [Header("드릴 설정")]
    // 실제 상한은 StatType.DrillBatteryCapacity(업그레이드 '드릴 입수'·'드릴 충전기')에서 온다.
    // 이 필드는 PlayerStat이 없거나, 세이브 로드 전이라 스탯 기본값이 아직 안 깔린 동안의 폴백일 뿐이다.
    [Tooltip("드릴 배터 상한의 폴백값. 실제 상한은 StatType.DrillBatteryCapacity에서 온다.")]
    public float maxBattery = 5.0f;
    public float MaxBattery => _drillStrategy != null ? _drillStrategy.MaxBattery : maxBattery;
    private float dashSpeed = 2.50f;
    [Range(-180f, 180f)]
    public float angleOffset = 0f;

    [Header("머리 회전 제한")]
    public float headMinAngle = -30f; // 아래로 숙이는 최대 각도 (예시)
    public float headMaxAngle = 45f;  // 위로 드는 최대 각도 (예시)

    [Header("공격(채굴) 시 팔 회전 추가 각도")]
    [Range(-180f, 180f)]
    public float attackAngleOffset = 0f;

    [Header("부드러운 회전(스무딩) 설정")]
    public float lookSmoothSpeed = 10f; // 값이 작을수록 느리게 풀리고, 클수록 빨리 풀림
    private float _currentLookWeight = 0f; // 0이면 애니메이션 기본상태, 1이면 마우스 고정 상태
    [Header("채굴 판정 위치 조정")]
    [Tooltip("파기 중심점이 플레이어로부터 얼마나 떨어질지 결정하는 배율.\n" +
             "1.0 = (기존) 파는 반경만큼 앞으로 나감\n" +
             "0.5 = 반경의 절반만큼만 앞으로 나감 (추천)\n" +
             "0.0 = 플레이어 발밑에서 파짐")]
    [Range(0f, 2f)]

    public float digCenterOffsetRatio = 0.5f; // 기본값을 0.5 정도로 확 당겨줍니다.
    // ★ [추가] 타격 후 회복 구간(바라보기 풀기) 여부
    private bool _isRecoveryPhase = false;
    [Header("삽 이펙트")]
    [Tooltip("팔 때 뿅 하고 나타날 이펙트 프리팹")]
    public GameObject shovelEffectPrefab;

    [Tooltip("이펙트 크기 미세 조절용 (스프라이트 PPU 단위가 다를 경우 2.0 등으로 맞춰주세요)")]
    public float shovelEffectScaleMultiplier = 1f;
    public PickaxeEffect attackEffect;
    // Strategies
    private IMiningStrategy _currentStrategy;

    // 제트팩 유물: 드릴 모드에서 드릴을 완전 정지시킨다(파기·대시·에임·드릴 배터 소모 차단).
    // 전략 Handle* 호출만 스킵하고 SwitchStrategy 부기는 유지(제트팩 off 시 올바른 전략 복귀).
    public bool SuppressMining { get; set; }

    /// <summary>
    /// 채굴 전체 정지 조건. 제트팩 억제(<see cref="SuppressMining"/>)와
    /// 스태미나 탈진(<see cref="PlayerStat.IsExhausted"/>)을 한 곳에서 합친다.
    ///
    /// 탈진은 스태미나가 <b>완전히</b> 다시 찰 때까지 풀리지 않으며, 드릴(배터리만 소모)도 함께 멈춘다 —
    /// "지쳐서 아무것도 못 캔다"가 이 상태의 정의라 도구별 예외를 두지 않는다.
    /// </summary>
    private bool MiningBlocked => SuppressMining
                               || (playerStats != null && playerStats.IsExhausted);

    // 탈진 진입 에지 감지 — 차징 중이던 전략을 딱 한 번 리셋하기 위한 것.
    private bool _wasExhausted;

    /// <summary>
    /// 지금 '매달린 채 채굴'이 성립하는가 — 벽타기 상태 + 업그레이드 해금.
    /// 채굴 전략들이 지상/공중 판정을 가를 때 쓰는 단일 기준이다.
    /// </summary>
    public bool IsClimbMining => controller != null
                              && controller.isWallClimbing
                              && ClimbMiningUnlockGate.IsUnlocked;

    // 벽타기 채굴이 아직 안 열린 상태로 매달려 있는가(= 예전 동작으로 잠금). 에지 감지용.
    private bool _climbLocked;

    // 유물(과부하 배터리): 드릴 대시 배터리 효율·방향 무작위 설정. _drillStrategy 생성(Start) 전에
    // 장착돼도 값을 잃지 않도록 여기에 보관했다가 Start에서 전략에 밀어넣는다.
    private float _dashDrainMultiplier = 1f;
    private bool _dashRandomDirection = false;
    private float _dashRandomInterval = 0.5f;
    private float _dashRandomMinDelta = 0f;

    // 유물(mp3+도구스왑): 삽 리듬 모드(차징·쿨다운 없는 즉시 발사). _sapStrategy 생성(Start) 전에
    // 켜져도 값을 잃지 않도록 여기에 보관했다가 Start에서 전략에 밀어넣는다.
    private bool _sapRhythmMode = false;
    private float _sapMinSwingInterval = 0f;   // 리듬 스윙 최소 간격(초) — mp3가 BPM 기준으로 넣어준다
    private float _nextSapSwingTime = 0f;
    private int _sapSwingFrame = -1;

    private SapStrategy _sapStrategy;
    private PickaxeStrategy _pickaxeStrategy;
    private DrillStrategy _drillStrategy;
    private EmptyStrategy _emptyStrategy;

    // 유물 dig 후처리 (전역 참조 — 네임스페이스 충돌 회피)
    private Relic.RelicManager _relicManager;

    // 드릴 대쉬 종료 훅 (유물: 폭발 드릴)
    public event System.Action<Vector2> DrillDashEnded;
    private bool _wasDrillDashing;

    // 배터리 상한을 스탯에서 한 번이라도 받아왔는가 (SyncDrillBattery의 최초 1회 완충 판정)
    private bool _drillBatteryStatApplied;

    // ── 파기 이벤트 허브 ──
    // 모든 채굴 스윙(삽·곡괭이 등)이 실행되는 순간 1회 발행. 유물은 이 이벤트를 개별 구독하지 않고
    // RelicManager가 한 번 구독해 전 슬롯의 OnDigSwing()으로 fan-out한다(파기 관련 유물의 단일 연결점).
    // 파기 파라미터(범위/대상)의 유물 후처리는 GetCurrentDigParameters 단일 choke point에서 적용된다.
    public event System.Action DigSwing;
    public void RaiseDigSwing() => DigSwing?.Invoke();

    [HideInInspector] public bool canCancelAttack = false;

    // [추가] 애니메이션 이벤트(Animation Event)에서 호출할 함수 (캔슬 허용 시점)
    public void AllowAttackCancel()
    {
        canCancelAttack = true;
        ResetControllerState(); // -> controller.speedMultiplier = 1f; & controller.isMiningAction = false;

        // ★ [추가] 파는 모션이 억지로 남지 않도록 트리거/상태 확실히 리셋!
        playerAnimator.ResetTrigger("Mine");
        playerAnimator.SetBool("IsCharging", false);
        playerAnimator.SetBool("IsFullCharge", false);

        if (_currentStrategy is SapStrategy sapStrategy)
        {
            sapStrategy.OnAllowAttackCancelEvent();
        }
    }

    // ★ [추가] 애니메이션 이벤트(Animation Event)에서 호출할 함수 (타격/파기 시점)
    /// <summary>
    /// 애니메이터의 Dig 이벤트에서 호출되어, 내리치는 정확한 프레임에 채굴 판정을 수행합니다.
    /// </summary>
    public void Dig()
    {
        // 곡괭이 전략인 경우 해당 프레임에 PerformDig 실행
        if (_currentStrategy is PickaxeStrategy pickaxeStrategy)
        {
            pickaxeStrategy.PerformDig();
        }
    }
    public void SapDig()
    {
        if (_currentStrategy is SapStrategy sapStrategy)
        {
            sapStrategy.PerformSapDig();
        }
    }

    // [수정됨] 콤보 허용 이벤트 시 회복 구간 플래그를 함께 켭니다.
    public void AllowCombo()
    {
        _isRecoveryPhase = true; // ★ [추가] 콤보 허용 시점부터 바라보기를 풀고 회복 구간으로 진입!

        if (_currentStrategy is PickaxeStrategy pickaxeStrategy)
        {
            pickaxeStrategy.OnAllowComboEvent(); // 콤보 연계 시간 종료 알림
        }
    }
    void Start()
    {
        // Initialize Strategies
        _sapStrategy = new SapStrategy(maxChargeTime, slowMoveRatio);
        _sapStrategy.RhythmMode = _sapRhythmMode;   // mp3+스왑이 Start 전에 켜졌을 수 있다
        _pickaxeStrategy = new PickaxeStrategy(0f, slowMoveRatio, pickaxeStaminaReduction);
        _drillStrategy = new DrillStrategy(maxBattery, dashSpeed);
        // 유물(과부하 배터리)이 Start 전에 장착됐다면 보관된 값을 전략에 적용.
        _drillStrategy.DashDrainMultiplier = _dashDrainMultiplier;
        _drillStrategy.RandomDashDirection = _dashRandomDirection;
        _drillStrategy.RandomDashInterval = _dashRandomInterval;
        _drillStrategy.RandomDashMinDelta = _dashRandomMinDelta;
        _emptyStrategy = new EmptyStrategy();

        // 드릴 배터 상한을 스탯에서 끌어온다. 첫 세팅은 가득 채운 상태로 시작(기존 생성자 동작과 동일).
        SyncDrillBattery(fill: true);

        // ★ [수정됨] 마스크 스케일을 Vector2로 넘김 (가로 1 : 세로 0.7 비율 적용)
        Vector2 digScale = new Vector2(shovelDigMaskScale, shovelDigMaskScale * 0.7f);
        ShovelDigMask.Set(shovelDigMask, digScale);

        // Default to Pickaxe or Empty?
        // Logic will switch based on ToolController
        SwitchStrategy(_sapStrategy); // Default init

        _relicManager = GetComponentInChildren<Relic.RelicManager>();
        if (_relicManager == null) _relicManager = FindFirstObjectByType<Relic.RelicManager>();
    }

    /// <summary>
    /// 인스펙터에서 마스크·배율을 만지면 즉시 반영한다. 이미지 모양을 눈으로 맞춰보는 게
    /// 이 기능의 주 용도라, 플레이를 껐다 켜야 반영되면 쓸모가 없다.
    /// 로그 도배는 ShovelDigMask.Set이 (텍스처, 배율, 결과) 조합으로 억제한다.
    /// </summary>
    private void OnValidate()
    {
        // ★ [수정됨] 인스펙터 변경 시에도 가로 1 : 세로 0.7 비율 적용
        Vector2 digScale = new Vector2(shovelDigMaskScale, shovelDigMaskScale * 0.7f);
        ShovelDigMask.Set(shovelDigMask, digScale);
    }

    public void SwitchStrategy(IMiningStrategy newStrategy)
    {
        if (_currentStrategy == newStrategy) return;

        _currentStrategy?.Exit();
        _currentStrategy = newStrategy;
        _currentStrategy?.Enter(this);
    }

    /// <summary>
    /// 드릴 배터리 상한을 <see cref="StatType.DrillBatteryCapacity"/>에 맞춘다.
    /// 세이브 로드 전에는 PlayerStat의 기본값이 아직 안 깔려 스탯이 0으로 나오므로,
    /// 그동안은 인스펙터 <see cref="maxBattery"/>로 버티고 로드 뒤 프레임에 저절로 따라잡는다.
    /// </summary>
    /// <param name="fill">true면 새 상한까지 가득 채운다(시작 시점 전용).</param>
    private void SyncDrillBattery(bool fill)
    {
        if (_drillStrategy == null) return;

        float capacity = playerStats != null ? playerStats.DrillBatteryCapacity : 0f;
        bool fromStat = capacity > 0f;
        if (!fromStat) capacity = maxBattery;

        _drillStrategy.SetMaxBattery(capacity);

        // 스탯이 처음 도착한 프레임에 한 번은 가득 채운다. Start 시점엔 세이브 로드 전이라
        // 스탯이 0이고, 그 상태로 폴백 상한(5)까지만 채워두면 로드로 상한이 오른 뒤에도
        // 잔량은 그대로라 매 다이브를 덜 찬 배터리로 시작하게 된다.
        if (fill || (fromStat && !_drillBatteryStatApplied))
            _drillStrategy.CurrentBattery = _drillStrategy.MaxBattery;

        if (fromStat) _drillBatteryStatApplied = true;
    }

    /// <summary>
    /// 드릴 배터리를 충전합니다.
    /// </summary>
    public void RefillBattery(float amount)
    {
        if (_drillStrategy != null)
        {
            _drillStrategy.CurrentBattery += amount;
        }
    }

    // 유물(mp3+도구스왑) 훅: 삽을 차징 없는 리듬 모드로 전환. 좌클릭 = 즉시 풀차지 발사.
    // mp3 단독은 일반 차징 유지(뗄 때 판정), 도구스왑 동시 장착 시에만 mp3가 이 모드를 켠다.
    // 삽이 리듬 모드인지(Digger가 파기 입력 시점을 Up→Down으로 바꾸는 데 쓴다).
    public bool IsSapRhythmMode => _sapRhythmMode;

    public void SetSapRhythmMode(bool on, float minSwingInterval = 0f)
    {
        _sapRhythmMode = on;
        _sapMinSwingInterval = minSwingInterval;
        if (_sapStrategy != null) _sapStrategy.RhythmMode = on;
    }

    // 리듬 스윙 승인 게이트. 연타로 Perfect가 얻어걸리는 걸 막는 최소 간격을 여기서 단독 관리한다.
    public bool TrySapRhythmSwing()
    {
        if (_sapSwingFrame == Time.frameCount) return true;   // 이 프레임엔 이미 승인됨
        if (Time.time < _nextSapSwingTime) return false;

        _sapSwingFrame = Time.frameCount;
        _nextSapSwingTime = Time.time + _sapMinSwingInterval;
        return true;
    }

    // 유물(과부하 배터리) 훅: 드릴 대시 배터리 소모 배수. 1=기본, 0.5=효율 2배.
    public void SetDrillDashDrainMultiplier(float mult)
    {
        _dashDrainMultiplier = mult;
        if (_drillStrategy != null) _drillStrategy.DashDrainMultiplier = mult;
    }

    // 유물(과부하 배터리) 훅: 드릴 대시 방향 무작위화 on/off + 재추첨 주기(초) + 최소 회전각(도).
    public void SetDrillDashRandomDirection(bool on, float interval = 0.5f, float minDelta = 0f)
    {
        _dashRandomDirection = on;
        _dashRandomInterval = interval;
        _dashRandomMinDelta = minDelta;
        if (_drillStrategy != null)
        {
            _drillStrategy.RandomDashDirection = on;
            _drillStrategy.RandomDashInterval = interval;
            _drillStrategy.RandomDashMinDelta = minDelta;
        }
    }

    void Update()
    {
        // 드릴 대쉬 종료 엣지 감지 (유물: 폭발 드릴). UI 차단과 무관하게 항상 검사.
        bool dashing = _drillStrategy != null && _drillStrategy.IsDrillDashing;
        if (_wasDrillDashing && !dashing)
        {
            Vector2 endPos = controller != null ? (Vector2)controller.transform.position : (Vector2)transform.position;
            DrillDashEnded?.Invoke(endPos);
        }
        _wasDrillDashing = dashing;

        // 드릴 배터리: 상한 재동기화 + 자연회복.
        // 드릴을 안 들고 있어도 돌아야 한다 — 삽질하는 동안 차야 '드릴 충전기'가 쓸모가 있다.
        // 그래서 전략(HandleUpdate)이 아니라 여기, UI 차단 return보다 위에 둔다.
        SyncDrillBattery(fill: false);
        float drillRegen = playerStats != null ? playerStats.DrillBatteryRegen : 0f;
        if (drillRegen > 0f) RefillBattery(drillRegen * Time.deltaTime);

        // UI가 열려있으면 채굴 입력 전체 차단 (상태 없는 전면 오버레이 포함)
        if (UIStateManager.IsInputBlocked)
        {
            ResetStrategyState();
            return;
        }

        // 탈진에 막 들어간 순간 한 번: 차징·타격 중이던 전략을 UI 차단과 같은 방식으로 되돌린다.
        // 안 하면 삽 차징 게이지가 탈진 내내 붙잡힌 채로 남는다.
        bool exhausted = playerStats != null && playerStats.IsExhausted;
        if (exhausted && !_wasExhausted) ResetStrategyState();
        _wasExhausted = exhausted;

        // 0. 벽타기 채굴이 안 열렸으면 예전대로 — 도구는 유지하되 전략만 비워 입력을 막는다.
        HandleClimbLock();

        // 1. 전략에 따른 로직 수행
        if (!MiningBlocked) _currentStrategy?.HandleUpdate();

        // 2. 도구 변경 잠금 관리
        UpdateToolLockState();

        // 3. Sync Strategy with ToolController
        //    '한 손 채굴'이 열려 있으면 벽타기 중에도 동기화한다 — 매달린 채로 삽질·곡괭이질이
        //    가능해야 하므로 전략을 EmptyStrategy로 비우지 않는다. (드릴은 DrillStrategy가
        //    IsGrounded를 조준 조건으로 걸어서 매달린 상태에선 스스로 발동하지 않는다.)
        if (toolController != null && !_climbLocked)
        {
            int currentTool = toolController.currentToolIndex;

            if (toolController.IsDrillMode && _currentStrategy != _drillStrategy)
            {
                SwitchStrategy(_drillStrategy);
            }
            else if (!toolController.IsDrillMode)
            {
                // 인덱스 1번: 삽 (차징 방식)
                if (currentTool == 1 && _currentStrategy != _sapStrategy)
                {
                    SwitchStrategy(_sapStrategy);
                }
                // 인덱스 2번: 곡괭이 (콤보 방식)
                else if (currentTool == 2 && _currentStrategy != _pickaxeStrategy)
                {
                    SwitchStrategy(_pickaxeStrategy);
                }
                // 인덱스 0번: 맨손 (또는 다른 도구)
                else if (currentTool == 0 && _currentStrategy != _emptyStrategy)
                {
                    SwitchStrategy(_emptyStrategy);
                }
            }
        }
        if (playerAnimator != null)
        {
            // 좌우 이동 키가 눌려 있는지 확인
            bool isMovingInput = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f;

            // 넉백 중이 아니어서 실제로 움직일 수 있는 상태인지 확인
            bool canMove = !controller.isKnockedBack;

            // 차징 중 + 이동 키 누름 + 움직일 수 있는 상태일 때만 true
            playerAnimator.SetBool("ChargingMove", IsCharging && isMovingInput && canMove);
        }
    }

    /// <summary>차징/공격 중이던 전략을 Exit→Enter로 되돌린다(게이지·애니메이션 상태 해제).</summary>
    private void ResetStrategyState()
    {
        if (_currentStrategy == null) return;
        if (!_currentStrategy.IsCharging && !_currentStrategy.IsAttacking) return;

        var saved = _currentStrategy;
        saved.Exit();
        saved.Enter(this);
    }

    void FixedUpdate()
    {
        if (!MiningBlocked) _currentStrategy?.HandleFixedUpdate();
    }
    public bool IsAttacking => _currentStrategy != null && _currentStrategy.IsAttacking;

    void LateUpdate()
    {
        // UI가 열려있으면 시각적 업데이트(회전 등) 차단
        if (UIStateManager.IsInputBlocked)
        {
            return;
        }

        // ★ [추가] 넉백 중일 때는 시선 고정을 풉니다.
        bool canAim = !controller.isKnockedBack;

        // ★ [수정됨] canAim이 true일 때만 회전 가중치를 1로 설정합니다.
        float targetWeight = (canAim && (IsCharging || (IsAttacking && !_isRecoveryPhase))) ? 1f : 0f;

        // 2. 가중치를 부드럽게 보간 (Time.deltaTime을 곱해 서서히 변하게 함)
        _currentLookWeight = Mathf.Lerp(_currentLookWeight, targetWeight, Time.deltaTime * lookSmoothSpeed);

        // 가중치가 완전히 0으로 떨어지면 굳이 연산할 필요 없음 (애니메이터 100% 상태)
        if (_currentLookWeight > 0.01f)
        {
            if (IsCharging)
            {
                // 차징 중: 마우스를 실시간으로 바라봄
                LookAtMouse();
            }
            else
            {
                // 채굴 중이거나, 채굴이 방금 끝나서 서서히 고정이 풀리고 있는 상태
                LookAtLockedTarget();
            }
        }

        // 각 전략의 LateUpdate 처리
        if (!MiningBlocked) _currentStrategy?.HandleLateUpdate();
    }

    /// <summary>
    /// '한 손 채굴'이 잠긴 채로 벽에 붙었을 때의 예전 동작 — 들고 있던 도구(삽/곡괭이/드릴)는
    /// 그대로 유지하고 전략만 비워 채굴 입력을 막는다. 해금돼 있으면 아무 일도 하지 않는다.
    /// 벽에서 내려오면 Update()의 Sync 로직이 현재 도구에 맞는 전략으로 되돌린다.
    /// </summary>
    void HandleClimbLock()
    {
        bool lockedNow = controller != null && controller.isWallClimbing && !ClimbMiningUnlockGate.IsUnlocked;

        if (lockedNow && !_climbLocked)
        {
            _climbLocked = true;
            SwitchStrategy(_emptyStrategy); // 도구는 유지, 로직만 맨손으로
        }
        else if (!lockedNow && _climbLocked)
        {
            _climbLocked = false;
        }
    }

    void UpdateToolLockState()
    {
        if (toolController == null) return;

        // 벽타기 채굴이 잠겨 있으면 예전처럼 도구 교체도 막는다.
        if (_climbLocked)
        {
            toolController.canSwitchTool = false;
            return;
        }

        // 해금 후에는 벽타기 중에도 교체를 허용한다 — 매달린 채 삽/곡괭이를 바꿔 쓸 수 있어야
        // "타면서 캔다"가 성립한다. 잠금 판단은 전략(스윙·대시 중 잠금)에만 맡긴다.

        // 전략에게 물어봄
        bool strategyAllowsSwitch = _currentStrategy != null ? _currentStrategy.CanSwitchTool() : true;
        toolController.canSwitchTool = strategyAllowsSwitch;
    }

    // ============================================================================================================
    //  SHARED VISUAL HELPERS (Public for Strategies)
    // ============================================================================================================

    public void ResetControllerState()
    {
        controller.isMiningAction = false;
        controller.speedMultiplier = 1f;
    }

    public void HandleFlip()
    {
        // ★ [추가] 넉백 중에는 캐릭터의 좌우 반전(Flip)도 막습니다.
        if (controller.isKnockedBack) return;

        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        if (mousePos.x < transform.position.x && !controller.isFacingRight)
            controller.Flip();
        else if (mousePos.x > transform.position.x && controller.isFacingRight)
            controller.Flip();
    }

    private Vector3 _lockedTargetPos;

    // [수정됨] 현재 마우스 위치를 고정(Lock)하고 회복 구간 플래그를 끕니다.
    public void LockTargetPosition()
    {
        _isRecoveryPhase = false; // ★ [추가] 새로운 타격이 시작되면 다시 마우스 방향을 바라봄!
        _lockedTargetPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
    }

    // [추가] 실시간 마우스가 아닌 '특정 좌표'를 바라보는 함수
    public void LookAtTarget(Vector3 targetPos, bool forceDrillDashRot = false, float extraArmOffset = 0f)
    {
        if (forceDrillDashRot)
        {
            RotateBoneToPos(transform, targetPos, -90f);
            if (armPivot != null)
                armPivot.localRotation = Quaternion.Slerp(armPivot.localRotation, Quaternion.identity, _currentLookWeight);
            if (headBone != null)
                headBone.localRotation = Quaternion.Slerp(headBone.localRotation, Quaternion.Euler(0, 0, -90), _currentLookWeight);
        }
        else
        {
            // 몸체 회전도 부드럽게 제자리로
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.identity, _currentLookWeight);

            // 팔 회전
            RotateBoneToPos(armPivot, targetPos, angleOffset + extraArmOffset, false);

            // 머리 회전
            RotateBoneToPos(headBone, targetPos, 180f, true);
        }
    }

    // [수정] 기존 LookAtMouse 함수는 LookAtTarget을 활용하도록 변경
    public void LookAtMouse(bool forceDrillDashRot = false)
    {
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        LookAtTarget(mousePos, forceDrillDashRot, 0f); // <-- 0f 전달
    }

    // [추가] 저장해둔 방향만 바라보는 함수
    public void LookAtLockedTarget(bool forceDrillDashRot = false)
    {
        LookAtTarget(_lockedTargetPos, forceDrillDashRot, attackAngleOffset); // <-- attackAngleOffset 전달
    }

    public void ResetRotations()
    {
        transform.rotation = Quaternion.identity;
        if (armPivot != null) armPivot.localRotation = Quaternion.identity;
        if (headBone != null) headBone.localRotation = Quaternion.identity;
    }

    void RotateBoneToPos(Transform targetTransform, Vector3 targetPos, float offset, bool applyClamp = false)
    {
        Vector2 direction = targetPos - targetTransform.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        if (transform.localScale.x < 0)
        {
            angle = angle + 180;
            offset = offset * -1;
        }

        // 1. 목표 회전값 계산
        Quaternion targetRot = Quaternion.Euler(0, 0, angle + offset);

        // 2. 머리처럼 각도 제한(Clamp)이 필요한 경우
        if (applyClamp)
        {
            // 부모의 회전값을 기준으로 '로컬 회전값'을 계산 (안전한 Clamp를 위함)
            Quaternion parentRot = targetTransform.parent != null ? targetTransform.parent.rotation : Quaternion.identity;
            Quaternion localTargetRot = Quaternion.Inverse(parentRot) * targetRot;

            Vector3 localEuler = localTargetRot.eulerAngles;
            float localZ = localEuler.z;

            if (localZ > 180f) localZ -= 360f; // 0~360 범위를 -180~180으로 변환

            // 인스펙터에 설정한 최소/최대 각도로 제한
            localZ = Mathf.Clamp(localZ, headMinAngle, headMaxAngle);

            // 제한된 로컬 각도를 다시 월드 회전값으로 묶어줌
            targetRot = parentRot * Quaternion.Euler(localEuler.x, localEuler.y, localZ);
        }

        // 3. 애니메이터의 원래 뼈대 회전(현재 프레임)에서 우리가 구한 목표 회전으로 보간(Slerp) 적용!
        targetTransform.rotation = Quaternion.Slerp(targetTransform.rotation, targetRot, _currentLookWeight);
    }

    public float GetCurrentChargeRatio()
    {
        return _currentStrategy != null ? _currentStrategy.GetChargeRatio() : 0f;
    }

    /// <summary>
    /// 드릴 배터리 비율(0~1). 현재 장착 도구와 무관하게 드릴 배터리 상태를 반환합니다.
    /// 드릴 게이지를 상시 표시하는 통합 스태미나 HUD에서 사용.
    /// </summary>
    public float GetDrillBatteryRatio()
    {
        return _drillStrategy != null ? _drillStrategy.GetChargeRatio() : 0f;
    }

    public bool IsCharging => _currentStrategy != null && _currentStrategy.IsCharging;

    public DigParameters GetCurrentDigParameters(float baseRadius, TileType targetTileType)
    {
        // UI가 열려있으면 모든 채굴 파라미터 거부
        if (UIStateManager.IsInputBlocked)
            return new DigParameters { CanDig = false };

        if (_currentStrategy == null) return new DigParameters { CanDig = false };

        if (MiningBlocked) return new DigParameters { CanDig = false };

        var p = _currentStrategy.GetDigParameters(baseRadius, targetTileType);

        // ★ [핵심] 스탯이 합쳐진 p.RadiusMultiplier를 넘기면 스탯까지 역산되어 버립니다!
        // 오직 순수하게 차징된 비율(_currentStrategy.GetChargeRatio())만 빼서 넘깁니다.
        if (_currentStrategy is SapStrategy)
        {
            float chargeProgress = _currentStrategy.GetChargeRatio();
            ShovelDigMask.CurrentShrinkRatio = Mathf.Clamp(chargeProgress, 0.05f, 1f);
        }
        else
        {
            ShovelDigMask.CurrentShrinkRatio = 1f;
        }

        // 유물 dig 후처리 (슬롯 순서대로 순차 적용)
        if (_relicManager != null && p.CanDig)
            _relicManager.ApplyDigModifiers(ref p);

        return p;
    }

    // 지형 파기 오버라이드 relay: Digger → RelicManager. 유물이 true면 기본 원형 파기를 대체한다.
    public bool TryOverrideTerrainDig(Vector2 digPos, Vector2 dir, float radius, int toolIndex)
    {
        return _relicManager != null && _relicManager.TryOverrideTerrainDig(digPos, dir, radius, toolIndex);
    }
}