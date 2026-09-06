using UnityEngine;
using System.Collections;

public class PickaxeStrategy : IMiningStrategy
{
    private PlayerMining _context;

    // [페널티 설정] (콤보 시간 관련 타이머 변수 모두 삭제됨!)
    private float _cancelPenaltyTime = 0.5f;

    // [상태]
    private int _comboStep = 0;
    private bool _isAttacking;
    private float _currentPenaltyTimer = 0f;

    // ★ [핵심 상태] AllowCombo 이벤트가 호출되었는지 여부
    private bool _isComboWindowExpired = false;

    // ── 벽타기 스윙 ──
    // 매달린 채로 휘두를 때는 애니메이터가 Climbing 상태에 머물러 채굴 클립이 돌지 않는다.
    // → 파기 시점(Dig)과 스윙 종료(AllowCombo)를 알려줄 애니메이션 이벤트가 아예 안 온다.
    // 그래서 벽타기 중에는 아래 타이머가 그 두 시점을 대신 잡는다. 지상 스윙은 종전대로
    // 애니메이션 이벤트가 주도하므로 이 타이머를 타지 않는다.
    private bool _climbSwinging;
    private float _climbSwingTimer;
    private bool _climbSwingDug;
    private const float ClimbSwingDigTime = 0.22f;   // 스윙 시작 → 파기 판정
    private const float ClimbSwingEndTime = 0.55f;   // 스윙 시작 → 다음 스윙 허용

    public bool IsCharging => false;
    public bool IsAttacking => _isAttacking;

    private float _pickaxeAngleOffset = -10f;
    private float _originalAngleOffset;
    private StaminaManager _staminaManager;
    private IDigCostCalculator _costCalculator;

    private const float TerrainRadiusPerHit = 0.5773503f;
    private const float TerrainStaminaRadiusPerHit = 1f / 3f;

    // 지형 청크 오버랩용 재사용 버퍼(구 OverlapCircleAll의 스윙마다 GC 제거).
    // 돌 선정은 MiningTargetPicker가 자기 버퍼로 처리하므로 여기서 공유하지 않는다.
    private readonly Collider2D[] _terrainHits = new Collider2D[MiningTargetPicker.MaxHits];

    // maxStaminaReduction은 이제 보관하지 않고 MiningStaminaTuning의 세션 기본값으로만 심는다.
    // 이후 실제 소비는 그쪽 값을 읽으므로 F8 패널에서 바꾼 값이 즉시 반영된다.
    public PickaxeStrategy(float dummyParam, float slowMoveRatio, float maxStaminaReduction = 2f)
    {
        MiningStaminaTuning.SeedPickaxeRockMaxReduction(maxStaminaReduction);
    }

    public void Enter(PlayerMining context)
    {
        _context = context;
        _staminaManager = context.playerStats != null
            ? context.playerStats.GetComponent<StaminaManager>()
              ?? UnityEngine.Object.FindFirstObjectByType<StaminaManager>()
            : UnityEngine.Object.FindFirstObjectByType<StaminaManager>();
        _costCalculator = new StaminaDigCostCalculator(
            context.playerStats,
            () => context.toolController != null ? context.toolController.currentToolIndex : -1);
        _originalAngleOffset = _context.angleOffset;
        _context.angleOffset = _originalAngleOffset + _pickaxeAngleOffset;
        ResetCombo();
    }

    public void Exit()
    {
        ResetCombo();
        if (_context != null)
        {
            _context.angleOffset = _originalAngleOffset;
        }
        _context = null;
    }

    public void HandleUpdate()
    {
        if (_context == null) return;

        if (_currentPenaltyTimer > 0f)
        {
            _currentPenaltyTimer -= Time.deltaTime;
        }

        bool grounded = (_context.controller != null && _context.controller.IsGrounded);
        // '한 손 채굴'(ClimbMiningUnlockGate)이 잠겨 있으면 매달린 상태는 그냥 공중 취급이다.
        bool climbing = _context.IsClimbMining;

        // 벽에 매달린 채 휘두르는 중이면 타이머가 파기·스윙 종료를 대신 처리한다.
        if (climbing) TickClimbSwing();
        else if (_climbSwinging) ResetCombo(); // 스윙 도중 벽에서 떨어졌다 — 그냥 취소

        // ★ [핵심 추가]: 공격 중 바닥을 파서 낙하(공중) 상태가 되면 즉시 공격/이동 제어를 풉니다!
        //    단 벽타기는 '공중이지만 붙잡고 있는' 상태라 예외.
        if (_isAttacking && !grounded && !climbing)
        {
            ResetCombo();
            return;
        }

        // 1. 이동에 의한 공격 캔슬 (기존 로직 유지)
        if (_isAttacking && _context.canCancelAttack && Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0)
        {
            ApplyPenaltyAndReset();
            return;
        }

        // 2. 공중에 있을 때(땅에 닿아있지 않을 때)는 마우스 공격 입력 및 애니메이션 실행 완벽 차단!
        if (!grounded && !climbing)
        {
            return;
        }

        // 3. 땅에 있더라도 '착지 애니메이션'이 재생 중이라면 공격 금지! (벽타기 중엔 해당 없음)
        if (!climbing && _context.playerAnimator != null)
        {
            AnimatorStateInfo stateInfo = _context.playerAnimator.GetCurrentAnimatorStateInfo(0);

            if (stateInfo.IsName("Landing") || stateInfo.IsName("WeakLand"))
            {
                return;
            }
        }

        // 4. 공격 입력 처리 (좌클릭)
        if (Input.GetMouseButton(0))
        {
            if (_currentPenaltyTimer > 0f) return;

            // 벽타기 중에는 콤보 연계(애니메이션 이벤트 기반)를 쓸 수 없으므로 단타를 반복한다.
            if (climbing)
            {
                if (!_isAttacking) ExecuteAttack(isNewCombo: true);
                return;
            }

            // [조건 A]: 공격 중이 아니거나, AllowCombo 이벤트가 이미 지나서 콤보 연계가 끝난 경우 -> 1타부터 발동!
            if (!_isAttacking || _isComboWindowExpired)
            {
                ExecuteAttack(isNewCombo: true);
            }
            // [조건 B]: 공격 중이고 AllowAttackCancel ~ AllowCombo 사이 구간인 경우 -> 다음 콤보로 즉시 연계!
            else if (_context.canCancelAttack)
            {
                ExecuteAttack(isNewCombo: false);
            }
            // (주의: canCancelAttack이 켜지기 전(공격 초반)에 마우스를 누르는 것은 완벽히 무시하여 버퍼링 방지)
        }
    }

    public void HandleFixedUpdate() { }
    public void HandleLateUpdate() { }

    public bool CanSwitchTool()
    {
        return !_isAttacking;
    }

    // ★ 1타부터 칠지, 다음 콤보로 이어갈지 결정
    private void ExecuteAttack(bool isNewCombo)
    {
        _isAttacking = true;
        _context.canCancelAttack = false;
        _isComboWindowExpired = false; // 새로운 타격이 시작되므로 콤보 만료 상태 초기화

        _context.controller.isMiningAction = true;
        _context.controller.speedMultiplier = 0f;

        if (isNewCombo)
        {
            _comboStep = 1; // 1타로 초기화
        }
        else
        {
            _comboStep++; // 다음 콤보로 연계
            if (_comboStep > 3) _comboStep = 1;
        }

        _context.LockTargetPosition();
        _context.HandleFlip();

        if (_context.IsClimbMining)
        {
            _climbSwinging = true;
            _climbSwingTimer = 0f;
            _climbSwingDug = false;
        }

        if (_context.attackEffect != null)
        {
            _context.attackEffect.RotateToMouse();
        }

        _context.playerAnimator.SetInteger("ComboStep", _comboStep);
        _context.playerAnimator.ResetTrigger("Mine");
        _context.playerAnimator.SetTrigger("Mine");
    }

    // ★ 애니메이터의 AllowCombo 이벤트에서 호출됨
    public void OnAllowComboEvent()
    {
        if (!_isAttacking) return;

        // 1. 콤보 연계 시간이 끝났음을 표시.
        _isComboWindowExpired = true;

        // ★ [핵심 추가]: 콤보 연계 시간이 끝났으므로 더 이상 공격 중이 아닌 것으로 처리하여 도구 교체를 허용!
        _isAttacking = false;

        if (_context != null)
        {
            // 2. 콤보 입력 창이 닫혔으므로 공격 캔슬 플래그 해제
            _context.canCancelAttack = false;

            // 3. 타격이 끝나고 회복(Recovery) 프레임에 진입하므로 머리와 팔 방향을 원래대로 되돌림!
            _context.ResetRotations();
        }
    }

    /// <summary>
    /// 애니메이션 이벤트(Dig) 진입점. 벽타기 중에는 채굴 클립이 돌지 않아 이 이벤트가
    /// 오지 않으며, 혹시 오더라도 <see cref="TickClimbSwing"/>과 이중 파기가 되지 않도록 무시한다.
    /// </summary>
    public void PerformDig()
    {
        if (_context == null) return;
        if (_context.IsClimbMining) return;
        PerformDigInternal();
    }

    /// <summary>벽타기 스윙 타이머: 파기 시점과 스윙 종료를 애니메이션 이벤트 대신 잡는다.</summary>
    private void TickClimbSwing()
    {
        if (!_climbSwinging) return;

        _climbSwingTimer += Time.deltaTime;

        if (!_climbSwingDug && _climbSwingTimer >= ClimbSwingDigTime)
        {
            _climbSwingDug = true;
            PerformDigInternal();
        }

        if (_climbSwingTimer >= ClimbSwingEndTime) EndClimbSwing();
    }

    /// <summary>벽타기 스윙 종료 — 파기를 못 했으면 여기서 한 번 쳐주고 상태를 푼다.</summary>
    private void EndClimbSwing()
    {
        if (!_climbSwingDug && _isAttacking)
        {
            _climbSwingDug = true;
            PerformDigInternal();
        }

        _climbSwinging = false;
        _climbSwingTimer = 0f;
        ResetCombo();
    }

    private void PerformDigInternal()
    {
        if (_context == null || !_isAttacking) return;

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 playerPos = _context.transform.position;
        Vector2 direction = (mousePos - playerPos).normalized;

        TileType targetTileType = TileType.Dirt;
        ITerrainManager mapManager = null;
        var digger = _context.GetComponent<Digger>();
        if (digger != null)
        {
            mapManager = digger.TerrainManager;
        }
        if (mapManager == null)
        {
            mapManager = UnityEngine.Object.FindFirstObjectByType<InfinityMapManager>() as ITerrainManager;
        }
        if (mapManager == null)
        {
            mapManager = UnityEngine.Object.FindFirstObjectByType<StaticChunkTerrainManager>() as ITerrainManager;
        }

        float checkDist = 1.0f;
        if (_context.playerStats != null)
        {
            checkDist = _context.playerStats.MiningRange;
            if (checkDist <= 0.01f) checkDist = 1.0f;
        }
        Vector2 checkPos = playerPos + (direction * checkDist);

        if (TileDataManager.Instance != null && mapManager != null && mapManager.chunkHeightWorld > 0)
        {
            int chunkY = Mathf.FloorToInt(checkPos.y / mapManager.chunkHeightWorld);
            targetTileType = TileDataManager.Instance.GetTileTypeAtDepth(chunkY);
        }

        _context.RaiseDigSwing();

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXJittered(SfxKeys.DigSwing);

        DigParameters p = _context.GetCurrentDigParameters(1.0f, targetTileType);
        if (!p.CanDig) return;

        // 여기가 실제로 한 번 판 지점이다 — 채광 레벨 부족 안내는 여기서만 발화한다.
        MiningLevelGate.Notify(in p);

        float radius = 1.0f * p.RadiusMultiplier;

        Vector2 digCenter = playerPos + direction * radius;

        bool hitAnyRock = false;
        bool hitAnyTerrain = false;

        // 돌은 한 번에 하나만. 예전엔 원에 걸린 돌을 전부 때렸고, 그래서 스태미나도
        // 겹친 돌 개수만큼 빠졌다. digCenter가 이미 (보는 방향 × 반경)이라
        // 그 점에 제일 가까운 하나 = 보는 방향에서 제일 가까운 돌이다.
        if (p.CanDigRock)
        {
            IDiggable diggable = MiningTargetPicker.PickNearest(digCenter, radius);
            if (diggable != null)
            {
                bool isPuzzle = diggable is IPuzzleDiggable;

                if (!isPuzzle)
                {
                    float staminaCost = 0.5f;
                    if (TileDataManager.Instance != null)
                    {
                        var data = TileDataManager.Instance.GetData(TileType.HardStone);
                        if (data != null && data.maxStaminaReduction > 0)
                            staminaCost = data.maxStaminaReduction;
                    }
                    staminaCost *= MiningStaminaTuning.GetRockCostMultiplier(p.ToolIndex);
                    staminaCost *= PickaxeStaminaScale();
                    _context.playerStats?.UseStamina(staminaCost);
                    MiningStaminaTuning.ReportCost(p.ToolIndex, staminaCost);
                }

                float damage = (p.ToolIndex == 2 && _context.playerStats != null)
                    ? _context.playerStats.GetFinalValue(StatType.PickaxeDamageUp) * p.DamageMultiplier
                    : radius;
                diggable.Dig(digCenter, damage, p.ToolIndex);

                if (!isPuzzle)
                    hitAnyRock = true;
            }
        }

        // 지형은 종전대로 걸린 청크를 전부 깎는다 — 파기 원이 청크 경계에 걸치면
        // 양쪽이 다 깎여야 한다. 여기를 하나로 줄이면 이음매에 안 파인 띠가 남는다.
        if (p.CanDigTerrain)
        {
            int count = MiningTargetPicker.Overlap(digCenter, radius, _terrainHits);
            for (int i = 0; i < count; i++)
            {
                Collider2D hitCollider = _terrainHits[i];
                if (hitCollider == null) continue;
                if (!hitCollider.TryGetComponent(out TerrainChunk chunk)) continue;

                // 삽(SapStrategy)과 같은 이유로 Dig(bool)을 쓴다 — 실제로 깎인 경우에만 비용.
                if (chunk.Dig(digCenter, radius, p.ToolIndex))
                    hitAnyTerrain = true;
            }
        }

        // 뭔가에 맞았을 때만 타격음. 휘두르는 소리(dig_swing)와는 키가 달라
        // SfxThrottle이 서로 억제하지 않는다 — 휘두름 위에 타격이 얹힌다(의도).
        if (hitAnyRock || hitAnyTerrain)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFXJittered(SfxKeys.DigHit);
            }
        }

        // 텔레메트리: 스윙 1회. 맞았든 헛쳤든 휘두른 것은 센다 —
        // 헛스윙 비율이 조준 난이도의 대리 지표가 된다.
        SettlementManager.Instance?.AddSwing(p.ToolIndex, hitAnyRock || hitAnyTerrain);

        if (hitAnyRock)
            _staminaManager?.AddDiggingReduction(
                MiningStaminaTuning.GetRockMaxReduction(p.ToolIndex) * PickaxeStaminaScale());

        if (hitAnyTerrain && !p.IgnoreStaminaCost)
        {
            float staminaRadius = radius * (TerrainStaminaRadiusPerHit / TerrainRadiusPerHit);
            _costCalculator?.PayCost(digCenter, staminaRadius);
            // 곡괭이는 차징이 없으므로(콤보 단계뿐) 종전대로 반경 기준을 유지한다.
            // 삽만 차징 기준으로 바뀌었다 — ShovelTerrainReductionPerCharge 주석 참고.
            _staminaManager?.AddDiggingReduction(
                MiningStaminaTuning.PickaxeTerrainReductionPerRadius * staminaRadius * PickaxeStaminaScale());
        }
    }

    /// <summary>
    /// '가벼운 곡괭이질' 업그레이드 배율(StatType.PickaxeStaminaReduce, 기본 1, 낮을수록 싸다).
    ///
    /// 곡괭이가 스태미나를 쓰는 곳이 세 군데라 세 곳 모두에 곱한다 —
    /// 돌 타격의 현재 스태미나 소모, 돌 타격의 최대치 감소, 지형 파기의 최대치 감소.
    /// (삽은 지형 파기 최대치 한 곳뿐이라 SapStrategy는 한 번만 곱한다.)
    ///
    /// ⚠ 곱하는 위치가 여기(PickaxeStrategy)인 이유: MiningStaminaTuning의 값들은 도구 인덱스로
    ///   갈라지는 공용 테이블이라 거기서 곱하면 툴스왑 유물로 삽이 돌을 캘 때도 할인이 붙는다.
    ///   지형 파기의 '현재 스태미나' 쪽(_costCalculator.PayCost)에는 곱하지 않는다 —
    ///   그 경로는 StaminaDigCostCalculator가 이미 StaminaCostMultiplier(전체 행동 할인)를
    ///   적용하고 있어 두 할인이 겹친다.
    /// </summary>
    private float PickaxeStaminaScale()
        => _context.playerStats != null
            // 노드가 배율을 값으로 깎으므로(노드당 -0.1) 0 아래로 내려갈 수 있다 —
            // 하한이 없으면 곡괭이질이 스태미나를 되돌려준다(2026-08-24).
            ? Mathf.Max(MinStaminaScale,
                _context.playerStats.GetFinalValue(StatType.PickaxeStaminaReduce))
            : 1f;

    /// <summary>스태미나 비용 배율의 하한. 다 사도 공짜는 아니다.</summary>
    private const float MinStaminaScale = 0.15f;

    private void ApplyPenaltyAndReset()
    {
        _currentPenaltyTimer = _cancelPenaltyTime;
        ResetCombo();
    }

    // ★ 애니메이션 종료 또는 콤보 리셋 시 호출
    public void ResetCombo()
    {
        _isAttacking = false;
        _comboStep = 0;
        _isComboWindowExpired = false;
        _climbSwinging = false;
        _climbSwingTimer = 0f;
        _climbSwingDug = false;

        if (_context != null)
        {
            _context.canCancelAttack = false;
            _context.ResetControllerState();
            _context.playerAnimator.SetInteger("ComboStep", 0);
            _context.ResetRotations();
        }
    }

    public float GetChargeRatio()
    {
        if (_comboStep == 1) return 1.0f;
        if (_comboStep == 2) return 1.2f;
        if (_comboStep == 3) return 1.5f;
        return 1.0f;
    }

    public DigParameters GetDigParameters(float baseRadius, TileType targetTileType)
    {
        int currentToolIndex = (_context.toolController != null) ? _context.toolController.currentToolIndex : 0;
        ToolCapabilities.Resolve(currentToolIndex, out bool canDigTerrain, out bool canDigRock, out float toolEfficiency);

        if (!canDigTerrain && !canDigRock) return new DigParameters { CanDig = false };

        float ratio = canDigTerrain ? TerrainRadiusPerHit : GetChargeRatio();

        bool blocked = MiningLevelGate.IsBlocked(targetTileType, _context.playerStats,
                                                 out int requiredMiningLevel, out int playerMiningLevel);

        float finalMultiplier;

        if (blocked)
        {
            finalMultiplier = MiningLevelGate.BlockedRadiusMultiplier;
        }
        else
        {
            float rangeStat = (_context.playerStats != null) ? _context.playerStats.MiningRange : 1.0f;
            float toolRange = (_context.playerStats != null) ? _context.playerStats.GetFinalValue(StatType.ToolRange) : 1.0f;
            finalMultiplier = ratio * Mathf.Max(0.1f, rangeStat) * toolRange;
        }

        finalMultiplier *= toolEfficiency;

        return new DigParameters
        {
            CanDig = true,
            RadiusMultiplier = finalMultiplier,
            DamageMultiplier = 1f,
            ToolIndex = currentToolIndex,
            CanDigTerrain = canDigTerrain,
            CanDigRock = canDigRock,
            MiningLevelBlocked = blocked,
            RequiredMiningLevel = requiredMiningLevel,
            CurrentMiningLevel = playerMiningLevel,
            BlockedTileType = targetTileType
        };
    }
}