using UnityEngine;
using System.Collections.Generic;

public class SapStrategy : IMiningStrategy
{
    /// <summary>스태미나 비용 배율의 하한. 업그레이드를 다 사도 공짜가 되지는 않는다.</summary>
    private const float MinStaminaScale = 0.15f;

    private PlayerMining _context;

    // 삽질 이펙트는 타격마다 재생된다 — 이 게임에서 가장 자주 도는 생성 경로다.
    // MaterialPropertyBlock은 재사용이 정석이라 static으로 하나만 들고 쓴다(호출당 할당 0).
    private static readonly MaterialPropertyBlock ShovelFxBlock = new MaterialPropertyBlock();
    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");

    /// <summary>삽 이펙트가 살아 있는 시간(초). 기존 Destroy(effect, 1f)와 같은 값.</summary>
    private const float ShovelEffectLifetime = 1f;

    // Configuration
    private float _slowMoveRatio;

    // 풀차징까지의 시간은 MiningStaminaTuning이 들고 있다(F8에서 조절, 즉시 반영).
    // 생성자 인자는 인스펙터 기본값을 심는 용도로만 쓰인다.
    private static float MaxChargeTime => MiningStaminaTuning.SafeShovelMaxChargeTime;

    // 툴스왑으로 삽이 돌을 캘 때 적용할 MaxStamina 고정 감소량(타격당 1회)은
    // MiningStaminaTuning.ShovelRockMaxReduction이 들고 있다(F8에서 조절).

    private StaminaManager _staminaManager;
    private IDigCostCalculator _costCalculator;

    // 파기 지점의 층을 뽑는 데 쓰는 지형 매니저. ResolveTerrainManager가 지연 해석·캐시한다.
    private ITerrainManager _cachedTerrainManager;

    // 지형 청크 오버랩용 재사용 버퍼(구 OverlapCircleAll의 타격마다 GC 제거).
    // 돌 선정은 MiningTargetPicker가 자기 버퍼로 처리하므로 여기서 공유하지 않는다.
    private readonly Collider2D[] _terrainHits = new Collider2D[MiningTargetPicker.MaxHits];

    // State
    private float _currentChargeTimer;
    private bool _isCharging;
    private bool _isAttacking;
    public bool IsAttacking => _isAttacking;

    // Store the charge ratio when firing, so Digger can read it later
    private float _lastChargeRatio;

    public bool RhythmMode { get; set; }

    // 애니메이션 이벤트가 씹혔을 때를 대비한 안전 타이머
    private float _attackSafetyTimer = 0f;
    private const float MaxAttackLockTime = 0.5f; // 0.5초가 지나도 이벤트가 안 오면 강제 해제

    // 우클릭 취소 직후 떼지 않은 마우스로 인해 즉시 재차징되는 것을 막는 플래그
    private bool _requireNewClick = false;

    // ── 벽타기 스윙 ──
    // 매달린 채로는 애니메이터가 Climbing 상태에 머물러 채굴 클립이 돌지 않는다 →
    // 파기 시점(SapDig)과 스윙 종료(AllowAttackCancel)를 알려줄 애니메이션 이벤트가 안 온다.
    // 벽타기 중에만 아래 타이머가 그 두 시점을 대신 잡는다(지상 스윙은 종전 그대로).
    private bool _climbSwinging;
    private float _climbSwingTimer;
    private bool _climbSwingDug;
    private const float ClimbSwingDigTime = 0.12f;
    private const float ClimbSwingEndTime = 0.35f;

    public SapStrategy(float maxChargeTime, float slowMoveRatio)
    {
        MiningStaminaTuning.SeedShovelMaxChargeTime(maxChargeTime);
        _slowMoveRatio = slowMoveRatio;
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
        ResetState();
    }

    public void Exit()
    {
        CancelCharging();
        _context.ResetRotations();
        _context = null;
        _cachedTerrainManager = null;
    }

    public void HandleUpdate()
    {
        if (_context == null) return;

        // 마우스 좌클릭을 떼면 새로운 클릭 요구 상태를 해제함
        if (!Input.GetMouseButton(0))
        {
            _requireNewClick = false;
        }

        // 스윙 중이 아닐 때 무조건 바라보는 것이 아니라, '차징 중'일 때만 마우스를 바라보도록 변경.
        if (_isCharging)
        {
            _context.HandleFlip();
        }

        bool grounded = _context.controller.IsGrounded;
        // '한 손 채굴'(ClimbMiningUnlockGate)이 잠겨 있으면 매달린 상태는 그냥 공중 취급이다.
        bool climbing = _context.IsClimbMining;

        // 벽에 매달린 채 휘두르는 중이면 타이머가 파기·스윙 종료를 대신 처리한다.
        if (climbing) TickClimbSwing();
        else if (_climbSwinging) EndClimbSwing(); // 스윙 도중 벽에서 떨어졌다

        // 안전장치 1: 공격(파기) 중일 때의 예외 탈출 처리
        if (_isAttacking)
        {
            _attackSafetyTimer += Time.deltaTime;

            // 벽타기 스윙은 위 타이머가 끝낸다 — 여기서 강제 해제하면 파기 전에 잘린다.
            if (climbing) return;

            // 바닥을 파서 땅이 꺼졌거나(!grounded), 애니메이터가 이벤트를 씹고 일정 시간이 지났다면 강제 해제!
            if (!grounded || _attackSafetyTimer > MaxAttackLockTime)
            {
                OnAllowAttackCancelEvent(); // 강제로 정상 이동 상태로 복구
                if (_context != null) _context.ResetControllerState();
            }
            // 공격 중에는 입력을 무시하지만, 꾹 누르고 있는 상태는 저장되어 공격이 끝난 뒤 반영됨
            return;
        }

        // 벽타기는 '공중이지만 붙잡고 있는' 상태라 차징·파기를 허용한다.
        if (!grounded && !climbing)
        {
            if (_isCharging) CancelCharging();
            return;
        }

        if (_isCharging)
        {
            // 우클릭(1)을 눌렀을 때만 차징이 취소됩니다.
            if (Input.GetMouseButtonDown(1))
            {
                CancelCharging();
                return;
            }

            ProcessCharging();
        }
        else
        {
            // 리듬모드와 일반모드 분리. 일반 차징 모드에서는 홀딩 대기 지원
            if (RhythmMode)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    _context.HandleFlip();
                    if (_context.TrySapRhythmSwing()) FireMining();
                }
            }
            else if (Input.GetMouseButton(0) && !_requireNewClick)
            {
                StartCharging();
            }
        }
    }

    public void HandleFixedUpdate() { }
    public void HandleLateUpdate() { }

    public bool CanSwitchTool()
    {
        return !(_isCharging || _isAttacking);
    }

    private void StartCharging()
    {
        _isCharging = true;
        _currentChargeTimer = 0f;

        _context.controller.isMiningAction = true;
        _context.controller.speedMultiplier = _slowMoveRatio;

        _context.playerAnimator.ResetTrigger("Mine");
        _context.playerAnimator.SetBool("IsCharging", true);
        _context.playerAnimator.SetBool("IsFullCharge", false);
    }

    private void ProcessCharging()
    {
        // 차징 중인데 이전 모션의 찌꺼기 이벤트가 Animator를 Standing으로 강제 해제해버렸다면 복구
        if (_context != null && !_context.playerAnimator.GetBool("IsCharging"))
        {
            _context.playerAnimator.SetBool("IsCharging", true);
            _context.controller.isMiningAction = true;
            _context.controller.speedMultiplier = _slowMoveRatio;
        }

        if (_currentChargeTimer < MaxChargeTime)
        {
            float speedMultiplier = 1.0f;
            if (_context != null && _context.playerStats != null)
            {
                speedMultiplier = _context.playerStats.GetFinalValue(StatType.MiningSpeed);
            }
            _currentChargeTimer += Time.deltaTime * speedMultiplier;
        }

        // 좌클릭을 떼면 캐기(Mine)로 전환. 최소 차징 미달이면 취소.
        if (Input.GetMouseButtonUp(0))
        {
            if (GetChargeRatio() < MiningStaminaTuning.ShovelMinChargeRatio)
                CancelCharging();
            else
                FireMining();
            return;
        }

        if (_currentChargeTimer >= MaxChargeTime)
        {
            _context.playerAnimator.SetBool("IsFullCharge", true);
        }
        else
        {
            _context.playerAnimator.SetBool("IsFullCharge", false);
        }
    }

    private void CancelCharging()
    {
        _isCharging = false;
        _currentChargeTimer = 0f;
        _climbSwinging = false;
        _climbSwingTimer = 0f;
        _climbSwingDug = false;
        _requireNewClick = true; // 취소 시, 마우스를 뗐다 다시 누르기 전까지는 차징 진입 방지

        if (_context != null)
        {
            _context.ResetControllerState();
            _context.playerAnimator.ResetTrigger("Mine");
            _context.playerAnimator.SetBool("IsCharging", false);
            _context.playerAnimator.SetBool("IsFullCharge", false);
            _context.ResetRotations(); // 완전히 취소했을 때는 고정된 시선을 풀어줍니다.
        }
    }

    private void FireMining()
    {
        _context.LockTargetPosition();
        _lastChargeRatio = RhythmMode ? 1f : Mathf.Clamp01(_currentChargeTimer / MaxChargeTime);

        _context.RaiseDigSwing();

        _isCharging = false;
        _isAttacking = true;
        _attackSafetyTimer = 0f;

        // 파는(내리치는) 동안 이동 속도를 0으로 만들어 이동 차단
        if (_context != null && _context.controller != null)
        {
            _context.controller.isMiningAction = true;
            _context.controller.speedMultiplier = 0f;
        }

        bool climbing = _context.IsClimbMining;
        if (climbing)
        {
            // 벽타기 자세를 강제로 깨지 않는다(Play 금지). 파기 시점은 타이머가 잡는다.
            _climbSwinging = true;
            _climbSwingTimer = 0f;
            _climbSwingDug = false;
            _context.playerAnimator.ResetTrigger("Mine");
        }
        else if (RhythmMode)
        {
            _context.playerAnimator.ResetTrigger("Mine");
            _context.playerAnimator.Play("Digging", 0, 0f);
        }
        else
        {
            _context.playerAnimator.ResetTrigger("Mine");
            _context.playerAnimator.SetTrigger("Mine");
        }

        _context.playerAnimator.SetBool("IsCharging", false);
        _context.playerAnimator.SetBool("IsFullCharge", false);

        _currentChargeTimer = 0f;
    }

    public void OnAllowAttackCancelEvent()
    {
        // 현재 공격 중이 아닐 때 들어오는 지연된 애니메이션 이벤트 무시
        if (!_isAttacking) return;

        _isAttacking = false;
        _currentChargeTimer = 0f;
        _attackSafetyTimer = 0f;

        if (_context != null)
        {
            _context.playerAnimator.ResetTrigger("Mine");
        }
    }

    /// <summary>
    /// 애니메이션 이벤트(SapDig) 진입점. 벽타기 중에는 채굴 클립이 돌지 않아 이 이벤트가
    /// 오지 않으며, 혹시 오더라도 <see cref="TickClimbSwing"/>과 이중 파기가 되지 않도록 무시한다.
    /// </summary>
    public void PerformSapDig()
    {
        if (_context == null) return;
        if (_context.IsClimbMining) return;
        PerformSapDigInternal();
    }

    /// <summary>벽타기 스윙 타이머: 파기 시점과 스윙 종료를 애니메이션 이벤트 대신 잡는다.</summary>
    private void TickClimbSwing()
    {
        if (!_climbSwinging) return;

        _climbSwingTimer += Time.deltaTime;

        if (!_climbSwingDug && _climbSwingTimer >= ClimbSwingDigTime)
        {
            _climbSwingDug = true;
            PerformSapDigInternal();
        }

        if (_climbSwingTimer >= ClimbSwingEndTime) EndClimbSwing();
    }

    /// <summary>벽타기 스윙 종료 — 파기를 못 했으면 여기서 한 번 파고 상태를 푼다.</summary>
    private void EndClimbSwing()
    {
        if (!_climbSwingDug && _isAttacking)
        {
            _climbSwingDug = true;
            PerformSapDigInternal();
        }

        _climbSwinging = false;
        _climbSwingTimer = 0f;
        _climbSwingDug = false;

        OnAllowAttackCancelEvent();
        if (_context != null) _context.ResetControllerState();
    }

    /// <summary>
    /// 삽이 지금 파려는 지점이 속한 층(TileType). 채광 면허 게이트와 층 감쇠가 이 값으로 갈린다.
    ///
    /// 기준점은 곡괭이와 같다 — 플레이어에서 마우스 방향으로 MiningRange만큼 나간 지점.
    /// 실제 파기 중심(digCenter)은 반경이 정해진 뒤에야 나오는데, 그 반경 자체가 이 층 판정에
    /// 달려 있어 순환이 된다. 층 경계는 청크 단위(10유닛)라 이 정도 오차로는 갈리지 않는다.
    ///
    /// 매니저는 캐시한다 — 삽 스윙은 이 게임에서 가장 자주 도는 경로라 스윙마다
    /// FindFirstObjectByType을 돌릴 수 없다. 씬이 바뀌면 파괴된 객체가 남으므로
    /// 인터페이스 참조는 Unity의 == 오버로드를 안 타는 점을 감안해 Component로 확인한다.
    /// </summary>
    private TileType ResolveTargetTileType(Vector2 playerPos, Vector2 direction)
    {
        if (TileDataManager.Instance == null) return TileType.Dirt;

        ITerrainManager mapManager = ResolveTerrainManager();
        if (mapManager == null || mapManager.chunkHeightWorld <= 0) return TileType.Dirt;

        float checkDist = 1.0f;
        if (_context.playerStats != null)
        {
            checkDist = _context.playerStats.MiningRange;
            if (checkDist <= 0.01f) checkDist = 1.0f;
        }

        Vector2 checkPos = playerPos + (direction * checkDist);
        int chunkY = Mathf.FloorToInt(checkPos.y / mapManager.chunkHeightWorld);
        return TileDataManager.Instance.GetTileTypeAtDepth(chunkY);
    }

    private ITerrainManager ResolveTerrainManager()
    {
        if (_cachedTerrainManager is Component cached && cached == null)
            _cachedTerrainManager = null;   // 씬 전환으로 파괴됨

        if (_cachedTerrainManager != null) return _cachedTerrainManager;

        var digger = _context.GetComponent<Digger>();
        if (digger != null) _cachedTerrainManager = digger.TerrainManager;

        if (_cachedTerrainManager == null)
            _cachedTerrainManager = UnityEngine.Object.FindFirstObjectByType<InfinityMapManager>() as ITerrainManager;
        if (_cachedTerrainManager == null)
            _cachedTerrainManager = UnityEngine.Object.FindFirstObjectByType<StaticChunkTerrainManager>() as ITerrainManager;

        return _cachedTerrainManager;
    }

    private void PerformSapDigInternal()
    {
        if (_context == null) return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXJittered(SfxKeys.DigSap);

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 playerPos = _context.transform.position;
        Vector2 direction = (mousePos - playerPos).normalized;

        // 파려는 지점이 속한 층을 넘긴다. 여기서 TileType.Dirt를 넘기면 tier가 항상 0이 되어
        // 채광 면허 게이트(GetDigParameters의 requiredMiningLevel)와 층 감쇠(LayerDigModifier)가
        // 삽에서만 통째로 죽는다 — 곡괭이(PickaxeStrategy.PerformDigInternal)와 같은 해석이다.
        TileType targetTileType = ResolveTargetTileType(playerPos, direction);

        DigParameters p = _context.GetCurrentDigParameters(1.0f, targetTileType);

        if (MiningStaminaTuning.LogDigs)
        {
            string shape = (p.ToolIndex == MiningStaminaTuning.Shovel && ShovelDigMask.IsActive)
                ? "mask" : "ellipse";
            Debug.Log($"[SapDig] f{Time.frameCount} charging={_isCharging} attacking={_isAttacking} " +
                      $"chargeTimer={_currentChargeTimer:F3} ratio={GetChargeRatio():F3} " +
                      $"canDig={p.CanDig} tool={p.ToolIndex} shape={shape} " +
                      $"radius={(p.CanDig ? p.RadiusMultiplier : 0f):F4}");
        }

        if (!p.CanDig) return;

        // 여기가 실제로 한 번 판 지점이다 — 채광 레벨 부족 안내는 여기서만 발화한다.
        MiningLevelGate.Notify(in p);

        float radius = 1.0f * p.RadiusMultiplier;

        float offsetDistance = radius * (_context != null ? _context.digCenterOffsetRatio : 1.0f);
        Vector2 digCenter = playerPos + direction * offsetDistance;

        if (_context.shovelEffectPrefab != null)
        {
            // 1. 파는 방향(마우스 방향)으로 이펙트 회전
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            Quaternion effectRot = Quaternion.Euler(0, 0, angle);

            // 2. 이펙트 생성 — 풀에서 꺼내 쓰고 ShovelEffectLifetime 뒤 자동 반납된다.
            GameObject effect = VfxPool.Play(_context.shovelEffectPrefab, digCenter, effectRot, ShovelEffectLifetime);

            // 3. 이펙트 안의 스프라이트 원본 크기를 가져옵니다.
            SpriteRenderer sr = effect != null ? effect.GetComponentInChildren<SpriteRenderer>() : null;
            if (sr != null && sr.sprite != null)
            {
                float chargeRatio = Mathf.Max(0.05f, GetChargeRatio());

                // 마스크 공식과 똑같이 파지는 실제 월드 지름(Width/Height)을 구합니다.
                float targetWidth = radius * 2f * _context.shovelDigMaskScale;
                float targetHeight = (radius * 2f / chargeRatio) * (_context.shovelDigMaskScale * 0.5f);

                // 목표 지름을 스프라이트 원본 크기로 나누어 완벽한 1:1 맞춤 스케일을 구합니다.
                float scaleX = targetWidth / sr.sprite.bounds.size.x;
                float scaleY = targetHeight / sr.sprite.bounds.size.y;

                effect.transform.localScale = new Vector3(-scaleX, scaleY, 1f) * _context.shovelEffectScaleMultiplier;

                // =====================================================================
                // ★ [완전 수정됨] MaterialPropertyBlock을 사용하여 쉐이더 값을 강제 주입합니다!
                // =====================================================================
                if (sr.sharedMaterial != null)
                {
                    sr.GetPropertyBlock(ShovelFxBlock);

                    // 기본 밀도입니다. 값이 클수록 도트가 촘촘해(작아)집니다.
                    // 도트가 여전히 크면 512, 너무 작으면 128 정도로 조절하세요!
                    float basePixelDensity = 1600f;
                    float finalPixelSize = basePixelDensity * Mathf.Abs(scaleX);

                    ShovelFxBlock.SetFloat(PixelSizeId, finalPixelSize);
                    sr.SetPropertyBlock(ShovelFxBlock);
                }
                // =====================================================================
            }
        }

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
                    _context.playerStats?.UseStamina(staminaCost);
                    MiningStaminaTuning.ReportCost(p.ToolIndex, staminaCost);
                }

                float damage = ((p.ToolIndex == 1 || p.ToolIndex == 2) && _context.playerStats != null)
                    ? _context.playerStats.GetFinalValue(StatType.PickaxeDamageUp) * p.DamageMultiplier
                    : radius;

                diggable.Dig(digCenter, damage, p.ToolIndex);
                if (!isPuzzle) hitAnyRock = true;
            }
        }

        // 지형은 종전대로 걸린 청크를 전부 깎는다(삽 마스크가 반경보다 넓게 퍼지므로
        // 탐색 반경도 그만큼 키운다). 하나로 줄이면 청크 이음매에 안 파인 띠가 남는다.
        if (p.CanDigTerrain)
        {
            float terrainSearchRadius = radius * Mathf.Max(1f, ShovelDigMask.ExtentMultiplier);
            int count = MiningTargetPicker.Overlap(digCenter, terrainSearchRadius, _terrainHits);
            for (int i = 0; i < count; i++)
            {
                Collider2D hitCollider = _terrainHits[i];
                if (hitCollider == null) continue;
                if (!hitCollider.TryGetComponent(out TerrainChunk chunk)) continue;

                if (chunk.Dig(digCenter, radius, p.ToolIndex))
                    hitAnyTerrain = true;
            }
        }

        MiningStaminaTuning.ReportShovelDig(hitAnyRock || hitAnyTerrain);
        SettlementManager.Instance?.AddSwing(p.ToolIndex, hitAnyRock || hitAnyTerrain);

        if (hitAnyRock)
            _staminaManager?.AddDiggingReduction(MiningStaminaTuning.GetRockMaxReduction(p.ToolIndex));

        if (hitAnyTerrain && !p.IgnoreStaminaCost)
        {
            float chargeBasis = GetChargeRatio();
            _costCalculator?.PayCost(digCenter, chargeBasis);

            float maxReduce = MiningStaminaTuning.ShovelTerrainReductionPerCharge * chargeBasis;

            if (_context.playerStats != null)
            {
                // 업그레이드가 배율을 **값으로 깎는다**(노드당 -0.1, 기준 1). 여러 개를 사면
                // 곱이 아니라 뺄셈이라 0 아래로도 갈 수 있다 — 그러면 파기가 스태미나를
                // 되돌려주는 버그가 된다. 하한을 둔다(2026-08-24).
                float scale = Mathf.Max(MinStaminaScale,
                    _context.playerStats.GetFinalValue(StatType.ShovelStaminaReduce));
                maxReduce *= scale;
            }

            _staminaManager?.AddDiggingReduction(maxReduce);

            if (MiningStaminaTuning.LogDigs)
                Debug.Log($"[SapDig] f{Time.frameCount} 지형 실제파임 → 차징 {chargeBasis:F3} " +
                          $"→ 최대치 −{maxReduce:F4}, 현재 소모 {MiningStaminaTuning.LastShovelCost:F4}");
        }
        else if (MiningStaminaTuning.LogDigs)
        {
            Debug.Log($"[SapDig] f{Time.frameCount} 비용 없음 (hitAnyTerrain={hitAnyTerrain}, " +
                      $"ignoreCost={p.IgnoreStaminaCost}, hitAnyRock={hitAnyRock})");
        }
    }

    private void ResetState()
    {
        _isCharging = false;
        _isAttacking = false;
        _currentChargeTimer = 0f;
        _lastChargeRatio = 0f;
        _attackSafetyTimer = 0f;
        _requireNewClick = false;
        _climbSwinging = false;
        _climbSwingTimer = 0f;
        _climbSwingDug = false;
    }

    public float GetChargeRatio()
    {
        if (RhythmMode) return 1f;
        if (_isAttacking) return _lastChargeRatio;
        if (!_isCharging) return 0f;
        return Mathf.Clamp01(_currentChargeTimer / MaxChargeTime);
    }

    public bool IsCharging => _isCharging;

    public DigParameters GetDigParameters(float baseRadius, TileType targetTileType)
    {
        float ratio = GetChargeRatio();

        if (ratio < MiningStaminaTuning.ShovelMinChargeRatio)
            return new DigParameters { CanDig = false };

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

        finalMultiplier *= MiningStaminaTuning.ShovelRadiusMultiplier;

        int currentToolIndex = (_context.toolController != null) ? _context.toolController.currentToolIndex : 0;
        ToolCapabilities.Resolve(currentToolIndex, out bool canDigTerrain, out bool canDigRock, out float toolEfficiency);

        if (!canDigTerrain && !canDigRock)
            return new DigParameters { CanDig = false };

        finalMultiplier *= toolEfficiency;

        // 층 감쇠(얼어붙은/달궈진 땅) — 환경 저항으로 상쇄된 뒤의 계수가 나온다.
        // 스탯이 아니라 여기서만 곱하는 이유는 LayerDigModifier 주석 참고.
        //
        // 하한은 '층 감쇠가 만든 손실'에만 건다. 그냥 MinEffectiveRadius로 클램프하면
        // 맨손(효율 0.1)과 채광레벨 미달(0.05)까지 끌어올려서 못 파야 할 것이 파진다.
        // 감쇠 전에 이미 하한보다 낮았다면 그 값을 그대로 유지한다.
        float beforeLayer = finalMultiplier;
        finalMultiplier *= LayerDigModifier.Resolve(targetTileType, _context.playerStats);

        float floor = Mathf.Min(beforeLayer, LayerDigModifier.MinEffectiveRadius);
        if (finalMultiplier < floor) finalMultiplier = floor;

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