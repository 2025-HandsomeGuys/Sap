using UnityEngine;

public class Digger : MonoBehaviour
{
    [Header("채굴 설정")]
    public float digRadius = 1.0f;

    /// <summary>
    /// 드릴 전용 파기 반경(월드 유닛, 절대값). <see cref="digRadius"/>(=MiningRange)와 완전히 분리돼 있다 —
    /// '넓은 삽날'(MiningRangeUp)을 사도 드릴은 안 커지고, '드릴 확장 비트'(DrillRadiusUp)를 사도
    /// 삽·곡괭이는 안 커진다. 원본은 StatType.DrillRadius(기준 0.5)이고, 실제 파기는
    /// <see cref="DrillDigRadius"/>가 스탯을 직접 읽는다 — 이 필드는 인스펙터 표시·폴백용이다.
    /// </summary>
    public float drillRadius = DrillStrategy.DefaultDigRadius;

    public float digCooldown = 0.1f;

    /// <summary>
    /// 드릴 차징 파기 반경. 스탯을 매번 읽는다 — 유물처럼 런타임에 붙는 배율도 바로 먹는다.
    /// <see cref="ApplyToolUpgrades"/>가 Start에서 한 번만 도니까 필드만 보면 안 된다.
    /// </summary>
    public float DrillDigRadius => (playerStats != null) ? playerStats.DrillDigRadius : drillRadius;

    /// <summary>
    /// 지금 든 도구의 파기 기준 반경. 드릴(3)만 자기 반경을 쓰고 나머지는 MiningRange를 쓴다.
    /// 파기 경로는 전부 이 값을 거쳐야 한다 — <see cref="digRadius"/>를 직접 읽으면 드릴이 다시
    /// 삽 업그레이드를 따라 커진다.
    /// </summary>
    public float ActiveDigRadius =>
        (_toolController != null && _toolController.currentToolIndex == 3) ? DrillDigRadius : digRadius;

    /// <summary>
    /// 드릴 대시(선행 파기)가 뚫는 반경. 차징 파기의 2배다 —
    /// 예전 (대시=MiningRange) : (차징=MiningRange×0.5) 비율을 그대로 옮긴 값.
    /// </summary>
    public float DrillDashRadius => DrillDigRadius * DrillStrategy.DashRadiusScale;

    // [제거됨] shovelReductionPerRadius — 삽의 MaxStamina 감소량은 MiningStaminaTuning으로 옮겼다.
    // 애초에 이 필드는 죽어 있었다: Update가 tool 1을 걸러내고 PerformShovelDig는 호출자가 없어서
    // 아래 AddDiggingReduction 두 줄에 삽이 도달하지 못했다. 실제 지불은 SapStrategy가 한다.

    [Header("Dependencies")]
    public PlayerStat playerStats;
    [SerializeField] private MonoBehaviour _terrainManagerObject;
    private ITerrainManager mapManager;
    public ITerrainManager TerrainManager => mapManager;

    private float nextDigTime = 0f;

    // 파기 계열 물리 쿼리용 재사용 버퍼(구 CircleCastAll의 프레임당 GC 제거).
    // 드릴 대시가 매 프레임 이 경로를 타므로 여기 할당은 그대로 할당률이 된다.
    private readonly RaycastHit2D[] _castHits = new RaycastHit2D[MiningTargetPicker.MaxHits];
    private ToolController _toolController;
    private StaminaManager _staminaManager;

    private IDigInputHandler _inputHandler;
    private IDigCostCalculator _costCalculator;

    void Awake()
    {
        _inputHandler = new MouseDigInputHandler();

        if (_terrainManagerObject != null)
            mapManager = _terrainManagerObject as ITerrainManager;
        if (mapManager == null)
            mapManager = FindFirstObjectByType<StaticChunkTerrainManager>();
        if (mapManager == null)
            mapManager = FindFirstObjectByType<InfinityMapManager>();
    }

    [Header("Visuals")]
    public float visualDelay = 0.05f;
    public string animationTriggerName = "Mine";
    private Animator _playerAnimator;
    private bool _hasMiningTrigger = false;
    private PlayerMining _playerMining;

    System.Collections.IEnumerator Start()
    {
        if (playerStats == null) playerStats = GetComponent<PlayerStat>();

        // 도구 인덱스는 람다로 넘긴다 — _toolController는 아래에서 대입되고, 도구 교체 시에도 계속 바뀐다.
        _costCalculator = new StaminaDigCostCalculator(
            playerStats,
            () => _toolController != null ? _toolController.currentToolIndex : -1);

        _toolController = GetComponent<ToolController>();
        if (_toolController == null) _toolController = FindFirstObjectByType<ToolController>();

        _playerAnimator = GetComponent<Animator>();
        if (_playerAnimator == null)
        {
            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) _playerAnimator = playerObj.GetComponent<Animator>();
        }

        _playerMining = GetComponent<PlayerMining>();
        if (_playerMining == null) _playerMining = FindFirstObjectByType<PlayerMining>();

        if (_playerAnimator != null)
        {
            foreach (var p in _playerAnimator.parameters)
            {
                if (p.name == animationTriggerName && p.type == AnimatorControllerParameterType.Trigger)
                {
                    _hasMiningTrigger = true;
                    break;
                }
            }
        }

        _staminaManager = FindFirstObjectByType<StaminaManager>();
        yield return null;

        ApplyToolUpgrades();
    }

    void Update()
    {
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState != UIState.None)
        {
            return;
        }

        bool isDigRequested = false;
        int currentTool = (_toolController != null) ? _toolController.currentToolIndex : 0;

        // 1번(삽), 2번(곡괭이), 3번(드릴)은 각 전략 스크립트가 입력을 통제하므로 Digger의 업데이트 무시
        if (currentTool == 1 || currentTool == 2 || currentTool == 3 || (_playerMining != null && _playerMining.IsAttacking))
        {
            return;
        }

        isDigRequested = _inputHandler.IsDigRequested();

        if (isDigRequested)
        {
            if (Time.time >= nextDigTime)
            {
                Vector2 targetPos = _inputHandler.GetTargetPosition();
                TryDig(targetPos);
            }
        }
    }

    public void ApplyToolUpgrades()
    {
        if (playerStats == null) return;

        digRadius = playerStats.MiningRange;
        drillRadius = playerStats.DrillDigRadius;

        if (digRadius <= 0.01f)
        {
            digRadius = 1.0f;
            Debug.LogWarning("[땅파기] 경고: PlayerStat.MiningRange가 0입니다! 기본값 1.0f를 강제 적용합니다.");
        }

        digCooldown = playerStats.MiningCooldown;
    }

    /// <param name="radius">0 이하면 <see cref="ActiveDigRadius"/>. 드릴 대시는 자기 반경을 직접 넘긴다.</param>
    public void ImmediateDig(Vector2 worldPos, float radius = 0f)
    {
        if (mapManager == null) return;

        float r = (radius > 0f) ? radius : ActiveDigRadius;
        Vector2 dir = worldPos - (Vector2)transform.position;
        Vector2 digDirection = (dir == Vector2.zero) ? Vector2.right : dir.normalized;
        int castCount = Physics2D.CircleCast(
            (Vector2)transform.position,
            r,
            digDirection,
            MiningTargetPicker.DefaultFilter,
            _castHits,
            r * TerrainModifier.VERTICAL_SCALE_DEFAULT
        );
        for (int i = 0; i < castCount; i++)
        {
            Collider2D castCollider = _castHits[i].collider;
            if (castCollider != null && castCollider.TryGetComponent(out IIndestructibleHit _))
                return;
        }

        mapManager.ModifyTerrain(worldPos, r, 3, applySmoothing: false);
    }

    /// <param name="radius">0 이하면 <see cref="ActiveDigRadius"/>. 드릴 대시는 자기 반경을 직접 넘긴다.</param>
    public void ImmediateDigRock(Vector2 worldPos, float radius = 0f)
    {
        float r = (radius > 0f) ? radius : ActiveDigRadius;
        // 겹친 돌이 몇 개든 하나만 때린다 — 파기 지점에서 제일 가까운 것.
        IDiggable diggable = MiningTargetPicker.PickNearest(worldPos, r);
        if (diggable == null) return;

        float damage = r;
        diggable.Dig(worldPos, damage, 3);
    }

    public void RequestDig(Vector2 targetPos)
    {
        if (Time.time >= nextDigTime)
            TryDig(targetPos);
    }

    private void TryDig(Vector2 targetPos)
    {
        int currentTool = (_toolController != null) ? _toolController.currentToolIndex : 0;

        if (currentTool != 1 && currentTool != 2 && currentTool != 3)
        {
            if (_hasMiningTrigger) _playerAnimator.SetTrigger(animationTriggerName);
        }

        StartCoroutine(DigRoutine(targetPos));
        nextDigTime = Time.time + digCooldown;
    }

    private System.Collections.IEnumerator DigRoutine(Vector2 targetPos)
    {
        yield return new WaitForSeconds(visualDelay);
        PerformDigAtPosition(targetPos);
    }

    // SapStrategy의 애니메이션 이벤트(SapDig)에서 호출하는 개방형 함수
    public void PerformShovelDig(Vector2 targetPos)
    {
        PerformDigAtPosition(targetPos);
    }

    private void PerformDigAtPosition(Vector2 targetPos)
    {
        Vector2 playerPos = transform.position;
        Vector2 direction = (targetPos - playerPos).normalized;

        float baseRadius = ActiveDigRadius;
        float effectiveRadius = baseRadius;
        if (_playerMining != null && mapManager != null && TileDataManager.Instance != null && mapManager.chunkHeightWorld > 0)
        {
            Vector2 checkPos = playerPos + (direction * baseRadius);
            int chunkY = Mathf.FloorToInt(checkPos.y / mapManager.chunkHeightWorld);
            TileType targetTileType = TileDataManager.Instance.GetTileTypeAtDepth(chunkY);

            var digParams = _playerMining.GetCurrentDigParameters(baseRadius, targetTileType);
            if (digParams.CanDig)
            {
                effectiveRadius = baseRadius * digParams.RadiusMultiplier;
            }
        }
        else if (_playerMining != null)
        {
            var digParams = _playerMining.GetCurrentDigParameters(baseRadius, TileType.Dirt);
            if (digParams.CanDig) effectiveRadius = baseRadius * digParams.RadiusMultiplier;
        }

        Vector2 digPosition = playerPos + (direction * effectiveRadius);
        DigAt(digPosition);
    }

    void TryDigRockOnly(Vector2 position)
    {
        if (_playerMining == null) return;

        float baseRadius = ActiveDigRadius;
        var digParams = _playerMining.GetCurrentDigParameters(baseRadius, TileType.Dirt);
        if (!digParams.CanDig || !digParams.CanDigRock) return;

        float effectiveRadius = baseRadius * digParams.RadiusMultiplier;
        int toolIndex = digParams.ToolIndex;

        // 삽(1번)이면 돌을 파지 못하므로 즉시 종료
        if (toolIndex == 1) return;

        // 겹친 돌이 몇 개든 하나만 때린다 — 파기 지점에서 제일 가까운 것.
        IDiggable diggable = MiningTargetPicker.PickNearest(position, effectiveRadius);
        if (diggable == null) return;

        if (!digParams.IgnoreStaminaCost)
            _costCalculator.PayCost(position, effectiveRadius);
        float damage = (toolIndex == 2 && playerStats != null)
            ? playerStats.GetFinalValue(StatType.PickaxeDamageUp)
            : effectiveRadius;
        diggable.Dig(position, damage, toolIndex);
    }

    void DigAt(Vector2 position)
    {
        if (mapManager == null)
        {
            TryDigRockOnly(position);
            return;
        }

        Vector2 actualHitPos = position;
        TileType targetTileType = TileType.Dirt;

        if (TileDataManager.Instance != null && mapManager.chunkHeightWorld > 0)
        {
            int chunkY = Mathf.FloorToInt(actualHitPos.y / mapManager.chunkHeightWorld);
            targetTileType = TileDataManager.Instance.GetTileTypeAtDepth(chunkY);
        }

        float baseRadius = ActiveDigRadius;
        float effectiveRadius = baseRadius;
        int toolIndex = 0;
        bool miningLevelBlocked = false;   // 유물 지형파기 오버라이드 가드용(아래 TryOverrideTerrainDig)

        if (_playerMining != null)
        {
            var digParams = _playerMining.GetCurrentDigParameters(baseRadius, targetTileType);

            if (!digParams.CanDig) return;
            if (!digParams.CanDigTerrain) return;

            miningLevelBlocked = digParams.MiningLevelBlocked;

            // 이 경로는 맨손(0)·드릴(3)만 탄다(삽·곡괭이는 각자 전략에서 알린다).
            // 맨손은 위 CanDig에서 걸러지므로 실질적으로 드릴의 알림 지점이다.
            MiningLevelGate.Notify(in digParams);

            effectiveRadius = baseRadius * digParams.RadiusMultiplier;
            toolIndex = digParams.ToolIndex;

            // ★ [핵심 수정]: 도구가 1번(삽)이 아닐 때만 돌 파기(CanDigRock)를 시도합니다!
            if (digParams.CanDigRock && toolIndex != 1)
            {
                // 겹친 돌이 몇 개든 하나만 때린다 — 파기 지점에서 제일 가까운 것.
                IDiggable diggable = MiningTargetPicker.PickNearest(actualHitPos, effectiveRadius);
                if (diggable != null)
                {
                    if (!digParams.IgnoreStaminaCost)
                        _costCalculator.PayCost(actualHitPos, effectiveRadius);
                    float damage = (toolIndex == 2 && playerStats != null)
                        ? playerStats.GetFinalValue(StatType.PickaxeDamageUp)
                        : effectiveRadius;
                    diggable.Dig(actualHitPos, damage, toolIndex);
                    return;
                }
            }
        }
        else
        {
            toolIndex = (_toolController != null) ? _toolController.currentToolIndex : 0;
        }

        if (TileDataManager.Instance != null && mapManager.chunkHeightWorld > 0)
        {
            bool ignoreCost = (_playerMining != null) &&
                              _playerMining.GetCurrentDigParameters(baseRadius, targetTileType).IgnoreStaminaCost;

            if (!ignoreCost && !_costCalculator.PayCost(actualHitPos, effectiveRadius))
            {
                Debug.LogError("[땅파기] 5-Fail. 비용 부족 (Stamina)");
                return;
            }
        }

        Vector2 digDirection = ((Vector2)transform.position - actualHitPos == Vector2.zero)
            ? Vector2.right
            : (actualHitPos - (Vector2)transform.position).normalized;
        float sweepDistance = effectiveRadius * TerrainModifier.VERTICAL_SCALE_DEFAULT;
        int castCount = Physics2D.CircleCast(
            (Vector2)transform.position,
            effectiveRadius,
            digDirection,
            MiningTargetPicker.DefaultFilter,
            _castHits,
            sweepDistance
        );
        for (int i = 0; i < castCount; i++)
        {
            Collider2D castCollider = _castHits[i].collider;
            if (castCollider != null && castCollider.TryGetComponent(out IIndestructibleHit indestructible))
            {
                indestructible.OnHitAttempt(actualHitPos, toolIndex);
                return;
            }
        }

        // 채광 면허가 모자란 층에서는 유물이 기본 파기를 대체하지 못한다.
        // 오버라이드는 넘겨받은 반경으로 파는 횟수를 늘리는 식이라(삼지창=3방향),
        // 반경만 게이트를 먹여 놓으면 스윙당 파는 양이 배수로 새어나간다.
        // 여기서 막아도 아래 ModifyTerrain이 게이트 반경으로 한 번은 파므로,
        // '아무 일도 안 일어나는' 상태가 되지는 않는다.
        if (!miningLevelBlocked &&
            _playerMining != null &&
            _playerMining.TryOverrideTerrainDig(actualHitPos, digDirection, effectiveRadius, toolIndex))
            return;

        // 지형 파기 수행
        mapManager.ModifyTerrain(actualHitPos, effectiveRadius, toolIndex);

        // 텔레메트리: 스윙 1회. 이 경로는 맨손(0)·드릴(3)이 탄다
        // (삽·곡괭이는 각자 전략에서 센다). 여기까지 왔으면 지형을 실제로 판 것이다.
        SettlementManager.Instance?.AddSwing(toolIndex, true);

        // [제거됨] 여기 있던 `toolIndex == 1` 삽 최대치 감소 2줄.
        // Update()가 tool 1을 걸러내고 PerformShovelDig는 호출자가 없어 애초에 도달 불가였고,
        // 삽 비용 기준이 '반경'에서 '차징 비율'로 바뀌면서 단위까지 맞지 않게 됐다.
        // 삽의 실제 지불은 SapStrategy.PerformSapDig()가 한다.

        CameraShakeManager.Instance?.ShakeOnDig(toolIndex);
        TerrainParticleManager.Instance?.SpawnImpact(actualHitPos, Color.white, toolIndex);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector2 debugDir = Vector2.right;
        Vector2 debugPos = (Vector2)transform.position + (debugDir * ActiveDigRadius);
        Gizmos.DrawWireSphere(debugPos, ActiveDigRadius);
    }
}