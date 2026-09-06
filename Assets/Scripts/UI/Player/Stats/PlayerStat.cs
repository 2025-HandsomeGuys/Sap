using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 최종 계산된 스탯을 관리하는 핵심 컴포넌트.
/// 기준값(Base)을 보유하고, 등록된 IStatProvider들의 Modifier를 합산하여 최종값을 계산.
///
/// 계산 공식: FinalValue = (BaseValue + ΣFlat) × ΠPercent
/// </summary>
public class PlayerStat : MonoBehaviour, IHazardTarget
{
    public event Action OnStatChanged;
    public event Action<int> OnGoldChanged;
    public event Action OnStaminaDepleted;

    [Header("기준값 설정 (SO에서 초기화)")]
    [SerializeField] private StatBaseValueTable baseValues = new StatBaseValueTable();

    [Header("현재 소모성 수치")]
    [SerializeField] private float currentStamina;
    [SerializeField] private int gold;

    [Header("피격 무적 시간")]
    [SerializeField] private float invincibilityDuration = 0.8f;

    [Header("탈진(스태미나 소진)")]
    [Tooltip("스태미나를 다 써서 탈진에 들어간 순간, 조작이 얼어붙는 시간(초)")]
    [SerializeField] private float exhaustStunDuration = 0.5f;

    private bool _isInvincible = false;
    public bool IsInvincible => _isInvincible;
    private Coroutine _invCo;

    // MaxStamina가 0인 동안 ClampCurrentStamina가 여러 번 불려도 쓰러짐은 한 번만 발화시킨다.
    private bool _depletedFired;

    // ── 탈진(현재 스태미나 소진) ──
    // MaxStamina 0(=쓰러짐)과는 완전히 별개다. 이쪽은 "지쳐서 잠깐 못 움직인다"는 일시 상태.
    private bool _isExhausted;
    private float _exhaustStunUntil = -1f;

    // 위험물 피해를 부상으로 넘기기 위한 StaminaManager 참조.
    // StaminaManager는 PlayerStat과 다른 GameObject에 있을 수 있어 계층 탐색 후 씬 전체로 폴백한다
    // (Digger·PickaxeStrategy·PlayerController가 쓰는 것과 같은 패턴).
    private StaminaManager _staminaManager;
    private StaminaManager Stamina
    {
        get
        {
            if (_staminaManager == null)
                _staminaManager = GetComponent<StaminaManager>()
                               ?? GetComponentInParent<StaminaManager>()
                               ?? GetComponentInChildren<StaminaManager>()
                               ?? FindFirstObjectByType<StaminaManager>();
            return _staminaManager;
        }
    }

    // 등록된 Provider 목록
    private readonly List<IStatProvider> _providers = new List<IStatProvider>();

    // 계산된 최종값 캐시
    private readonly Dictionary<StatType, float> _finalCache = new Dictionary<StatType, float>();
    // 곱연산 배율(ΠPercent)만 따로 보관 — Flat 감소량을 UI에 최종값과 같은 스케일로 그릴 때 필요
    private readonly Dictionary<StatType, float> _percentCache = new Dictionary<StatType, float>();
    private bool _isDirty = true;

    // ===================================================
    // Provider 등록/해제
    // ===================================================
    public void RegisterProvider(IStatProvider provider)
    {
        if (provider == null || _providers.Contains(provider)) return;

        // 같은 종류의 컴포넌트 Provider가 둘 붙으면 그 출처의 모든 Modifier가 두 번 들어간다.
        // 공식이 (Base + ΣFlat) × ΠPercent라 합연산은 2배, 곱연산은 제곱이 되어
        // 게임 전체 수치가 조용히 부풀고, 로그에도 아무 표시가 남지 않는다.
        // (실제로 FanalPlayer 프리팹에 업그레이드 Provider가 둘 붙어 최대 스태미나가 47.25 → 66.15로 떴던 적이 있다.)
        //
        // MonoBehaviour Provider만 막는다 — ActiveBuff·RelicStatProvider처럼
        // 인스턴스가 여럿인 게 정상인 일반 클래스 Provider는 걸러내면 안 된다.
        if (provider is MonoBehaviour)
        {
            Type incoming = provider.GetType();
            foreach (var existing in _providers)
            {
                if (existing is MonoBehaviour && existing.GetType() == incoming)
                {
                    Debug.LogError($"[PlayerStat] {incoming.Name}가 이미 등록되어 있습니다 — 중복 등록을 거부합니다. " +
                                   "같은 Provider 컴포넌트가 플레이어에 두 개 붙어 있는지 확인하세요.");
                    return;
                }
            }
        }

        _providers.Add(provider);
        MarkDirty();
    }

    public void UnregisterProvider(IStatProvider provider)
    {
        if (_providers.Remove(provider))
        {
            MarkDirty();
        }
    }

    /// <summary>
    /// 캐시 무효화. 다음 GetFinalValue 호출 시 재계산됨
    /// </summary>
    public void MarkDirty()
    {
        _isDirty = true;
    }

    // ===================================================
    // 최종값 조회
    // ===================================================
    /// <summary>
    /// 특정 스탯의 최종 계산된 값을 반환.
    /// 공식: (BaseValue + ΣFlat) × ΠPercent
    /// </summary>
    public float GetFinalValue(StatType type)
    {
        if (_isDirty) RecalculateAll();
        if (_finalCache.TryGetValue(type, out float cached)) return cached;
        return GetBaseValue(type);
    }

    /// <summary>
    /// 정수형 최종값 (MiningPower 등)
    /// </summary>
    public int GetFinalValueInt(StatType type)
    {
        return Mathf.RoundToInt(GetFinalValue(type));
    }

    /// <summary>
    /// 해당 스탯에 걸린 곱연산 배율(ΠPercent)만 반환. 걸린 게 없으면 1.
    /// 공식이 (Base + ΣFlat) × ΠPercent라 Flat 감소분도 이 배율을 먹는다 —
    /// 감소량을 최종값과 같은 스케일로 환산해 그려야 하는 UI(StaminaBar)가 사용한다.
    /// </summary>
    public float GetPercentMultiplier(StatType type)
    {
        if (_isDirty) RecalculateAll();
        return _percentCache.TryGetValue(type, out float p) ? p : 1f;
    }

    // ===================================================
    // 기준값 관련
    // ===================================================
    public float GetBaseValue(StatType type)
    {
        return baseValues.Get(type);
    }

    public void SetBaseValue(StatType type, float value)
    {
        baseValues.Set(type, value);
        MarkDirty();
    }

    // ===================================================
    // 전체 재계산
    // ===================================================
    private void RecalculateAll()
    {
        _finalCache.Clear();
        _percentCache.Clear();

        // 모든 StatType에 대해 기준값으로 초기화
        foreach (StatType type in Enum.GetValues(typeof(StatType)))
        {
            if (type == StatType.None) continue;
            float baseVal = baseValues.Get(type);
            float flatSum = 0f;
            float percentProduct = 1f;

            // 모든 Provider의 Modifier 수집
            foreach (var provider in _providers)
            {
                var modifiers = provider.GetModifiers();
                if (modifiers == null) continue;

                foreach (var mod in modifiers)
                {
                    if (mod.statType != type) continue;

                    if (mod.modifierType == ModifierType.Flat)
                        flatSum += mod.value;
                    else
                        percentProduct *= mod.value;
                }
            }

            float finalValue = (baseVal + flatSum) * percentProduct;
            _finalCache[type] = finalValue;
            _percentCache[type] = percentProduct;
        }

        _isDirty = false;
        OnStatChanged?.Invoke();
    }

    // ===================================================
    // 기존 호환용 프로퍼티 (PlayerStatsController 대체)
    // ===================================================

    // --- Stamina ---
    public float MaxStamina => GetFinalValue(StatType.MaxStamina);
    public float CurrentStamina
    {
        get => currentStamina;
        set
        {
            currentStamina = Mathf.Clamp(value, 0f, MaxStamina);
            RefreshExhaustion();
        }
    }

    /// <summary>
    /// 현재 스태미나를 0까지 다 써버린 상태. <b>완전히 다시 찰 때까지 풀리지 않는다</b> —
    /// 조금 회복하자마자 다시 뛰고 파는 것을 막는 게 이 상태의 목적이다.
    /// 점프(<see cref="PlayerController"/>)와 채굴(<see cref="PlayerMining"/>)이 이 값을 본다.
    ///
    /// MaxStamina가 0이 되는 쓰러짐과 혼동하지 말 것 — 그쪽은 OnStaminaDepleted / 게임오버 경로다.
    /// </summary>
    public bool IsExhausted => _isExhausted;

    /// <summary>탈진에 막 들어간 직후의 짧은 조작 불가 구간(exhaustStunDuration).</summary>
    public bool IsExhaustStunned => _isExhausted && Time.time < _exhaustStunUntil;

    /// <summary>
    /// currentStamina·MaxStamina가 움직인 모든 경로가 여기로 모인다.
    /// 진입은 "0에 닿은 순간 한 번" — 이미 탈진이면 스턴을 다시 걸지 않는다(무한 경직 방지).
    /// 해제는 "완충" 하나뿐이라 부분 회복으로는 절대 안 풀린다.
    /// </summary>
    private void RefreshExhaustion()
    {
        float max = MaxStamina;

        // 기준값이 아직 0인 초기화 전 상태와 쓰러짐(max 0)은 탈진으로 치지 않는다.
        if (max <= 0f) { _isExhausted = false; return; }

        if (!_isExhausted)
        {
            if (currentStamina <= 0f)
            {
                _isExhausted = true;
                _exhaustStunUntil = Time.time + exhaustStunDuration;
            }
            return;
        }

        if (currentStamina >= max - 0.001f)
            _isExhausted = false;
    }

    /// <summary>
    /// 행동 전반의 스태미나 비용 배율(기본 1, 낮을수록 싸다).
    /// '효율적인 호흡' 같은 합연산 노드가 배율을 -0.15씩 깎으므로 0 아래로 내려갈 수 있어
    /// 하한을 둔다 — 없으면 스태미나가 되돌아온다(PickaxeStrategy.MinStaminaScale과 같은 이유).
    /// </summary>
    public const float MinStaminaCostScale = 0.15f;
    public float StaminaCostScale => Mathf.Max(MinStaminaCostScale, GetFinalValue(StatType.StaminaCostReduce));

    public float StaminaCostPerSecond
        => Mathf.Max(0.01f, GetFinalValue(StatType.StaminaCostPerSecond) * StaminaCostScale);

    // --- Movement ---
    public float MoveSpeed => GetFinalValue(StatType.MoveSpeed);
    public float JumpForce => GetFinalValue(StatType.JumpForce);
    public float WallClimbSpeed => GetFinalValue(StatType.WallClimbSpeed);
    public float EncumberedSpeedMultiplier => GetFinalValue(StatType.EncumberedSpeedMultiplier);

    // --- Drill ---
    // 드릴 배터리 상한·자연회복. 소비처는 PlayerMining.SyncDrillBattery()다.
    public float DrillBatteryCapacity => GetFinalValue(StatType.DrillBatteryCapacity);
    public float DrillBatteryRegen => GetFinalValue(StatType.DrillBatteryRegen);

    /// <summary>드릴 배터리 소모 배율. 하한을 두는 이유는 <see cref="StaminaCostScale"/>과 같다 —
    /// 합연산이라 노드를 여러 개 사면 0 아래로 내려가고, 그러면 드릴이 배터리를 되돌려준다.</summary>
    public const float MinDrillDrainScale = 0.2f;
    public float DrillDrainScale => Mathf.Max(MinDrillDrainScale, GetFinalValue(StatType.DrillDrainReduce));

    /// <summary>드릴 파기 반경(월드 유닛, 절대값). MiningRange와 무관하다 — 삽 범위 업그레이드는
    /// 드릴을 키우지 않는다. 기본값이 아직 안 깔린 시점(세이브 로드 전)에는 0이 나오므로,
    /// 그때는 기준값으로 돌려준다 — 0을 그대로 쓰면 드릴이 아무것도 못 판다.</summary>
    public float DrillDigRadius
    {
        get
        {
            float v = GetFinalValue(StatType.DrillRadius);
            return v > 0f ? v : DrillStrategy.DefaultDigRadius;
        }
    }

    // --- Mining ---

    public int MiningLevel => GetFinalValueInt(StatType.MiningLevel);
    public float MiningRange => GetFinalValue(StatType.MiningRange);
    public float MiningCooldown => Mathf.Max(0.05f, GetFinalValue(StatType.MiningCooldown));

    // ===================================================
    // Gold 관련
    // ===================================================
    public int Gold => gold;

    public void AddGold(int amount)
    {
        gold += Mathf.Max(0, amount);
        OnGoldChanged?.Invoke(gold);
    }

    public bool SpendGold(int amount)
    {
        if (gold >= amount)
        {
            gold -= amount;
            OnGoldChanged?.Invoke(gold);
            return true;
        }
        Debug.Log("금화 부족!");
        return false;
    }

    // ===================================================
    // Stamina 관련
    // ===================================================
    /// <summary>
    /// 현재 스태미나를 깎는다. 벽타기·채굴 비용과 위험물 피해가 공유하는 raw 메서드.
    ///
    /// 여기서는 쓰러짐을 발화하지 않는다 — 쓰러짐 조건은 <b>MaxStamina가 0</b> 하나뿐이다
    /// (`Assets/Docs/stamina-max-reduction-plan.md`). 현재 스태미나 0은 "지쳐서 못 움직인다"는
    /// 일시 상태이고 <see cref="RecoverStamina"/> 리젠으로 되돌아온다.
    /// </summary>
    public void UseStamina(float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        RefreshExhaustion();
    }

    /// <summary>
    /// 위험물(구르는 바위·폭발·종유석 등) 피해. IHazardTarget 구현.
    ///
    /// <b>부상(injury)으로 처리해 MaxStamina를 깎는다</b> — 낙하 데미지·던전 함정·블랙홀과 같은 경로다
    /// (<see cref="StaminaManager.AddInjury"/> → IStatProvider → MaxStamina Flat 감소).
    /// 현재 스태미나를 깎는 방식이면 리젠으로 되돌아와 위험물이 위험하지 않게 된다.
    /// </summary>
    public void ApplyHazardDamage(float amount)
    {
        if (_isInvincible) return;

        var stamina = Stamina;
        if (stamina != null) stamina.AddInjury(amount);
        else UseStamina(amount); // 폴백: StaminaManager가 없는 구성에서도 최소한 현재치는 깎는다

        HitFlashUI.Instance?.Flash(0.5f, 0.2f);

        // 위험물 피격 공통음. 무적 체크 뒤라 i-frame 중에는 울리지 않는다
        // → 용암·산화지대처럼 지속 접촉하는 곳에서 소리가 연타되지 않는다.
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.PlayerHurt);

        if (gameObject.activeInHierarchy)
            StartInvincibilityFor(invincibilityDuration);
    }

    // 피격 i-frame용(기존 동작): 이미 무적이면 무시.
    public void StartInvincibility()
    {
        if (_isInvincible || !gameObject.activeInHierarchy) return;
        StartInvincibilityFor(invincibilityDuration);
    }

    // 커스텀 지속시간(유물 등): 진행 중이면 더 긴 무적으로 교체.
    public void StartInvincibility(float duration)
    {
        if (!gameObject.activeInHierarchy) return;
        StartInvincibilityFor(duration);
    }

    private void StartInvincibilityFor(float duration)
    {
        if (_invCo != null) StopCoroutine(_invCo);
        _invCo = StartCoroutine(InvincibilityRoutine(duration));
    }

    private System.Collections.IEnumerator InvincibilityRoutine(float duration)
    {
        _isInvincible = true;
        yield return new WaitForSeconds(duration);
        _isInvincible = false;
        _invCo = null;
    }

    public void RecoverStamina(float amount)
    {
        currentStamina = Mathf.Min(MaxStamina, currentStamina + amount);
        RefreshExhaustion();
    }

    /// <summary>
    /// MaxStamina가 줄었을 때 currentStamina를 새 상한에 맞게 클램프.
    /// <b>MaxStamina가 0 이하가 되면</b> OnStaminaDepleted를 발화한다 — 쓰러짐의 유일한 조건.
    ///
    /// 판정을 currentStamina가 아니라 max로 하는 이유: 이미 지쳐서 currentStamina가 0인 상태에서
    /// 감소가 들어와 max까지 0이 되면 `currentStamina > max`가 false라 예전 코드는 발화하지 못했다.
    /// </summary>
    public void ClampCurrentStamina()
    {
        float max = MaxStamina;
        if (currentStamina > max)
            currentStamina = Mathf.Max(0f, max);

        // MaxStamina가 움직였으니 탈진 판정도 다시 맞춘다(세이브 로드·상태 회복 직후 포함).
        RefreshExhaustion();

        if (max > 0f)
        {
            _depletedFired = false; // 회복되면 다시 쓰러질 수 있다
            return;
        }

        // 기준값이 아직 0인 초기화 전 상태(SO 미적용 등)를 쓰러짐으로 오인하지 않는다.
        if (GetBaseValue(StatType.MaxStamina) <= 0f) return;

        if (_depletedFired) return;
        _depletedFired = true;
        OnStaminaDepleted?.Invoke();
    }

    // ===================================================
    // 유틸리티
    // ===================================================
    public float GetEffectiveMineSpeed(float toolBaseSpeed, float materialBonus = 0f)
    {
        float effective = toolBaseSpeed * GetFinalValue(StatType.MiningSpeed) * (1f + materialBonus);
        return Mathf.Max(0.01f, effective);
    }

    public bool CanBreak(int toolPower, int materialHardness)
    {
        return (toolPower + MiningLevel) >= materialHardness;
    }

    // ===================================================
    // PlayerData 변환 (Save/Load)
    // ===================================================
    public PlayerData ToData(PlayerData data = null)
    {
        if (data == null) data = new PlayerData();

        // 기준값 저장
        data.maxStamina = baseValues.Get(StatType.MaxStamina);
        data.staminaCostPerSecond = baseValues.Get(StatType.StaminaCostPerSecond);
        data.moveSpeed = baseValues.Get(StatType.MoveSpeed);
        data.jumpForce = baseValues.Get(StatType.JumpForce);
        data.wallClimbingSpeed = baseValues.Get(StatType.WallClimbSpeed);
        data.encumberedSpeedMultiplier = baseValues.Get(StatType.EncumberedSpeedMultiplier);

        data.miningLevel = (int)baseValues.Get(StatType.MiningLevel);
        data.miningRange = baseValues.Get(StatType.MiningRange);
        data.miningCooldown = baseValues.Get(StatType.MiningCooldown);

        // 상태값 저장
        data.currentStamina = currentStamina;
        data.gold = gold;

        return data;
    }

    public void FromData(PlayerData data)
    {
        if (data == null) return;

        // 기준값 복원 (SetBaseValue 내부에서 MarkDirty() 호출됨)
        baseValues.Set(StatType.MaxStamina, data.maxStamina);
        baseValues.Set(StatType.StaminaCostPerSecond, data.staminaCostPerSecond);
        baseValues.Set(StatType.MoveSpeed, data.moveSpeed);
        baseValues.Set(StatType.JumpForce, data.jumpForce);
        baseValues.Set(StatType.WallClimbSpeed, data.wallClimbingSpeed);
        baseValues.Set(StatType.EncumberedSpeedMultiplier, data.encumberedSpeedMultiplier);

        baseValues.Set(StatType.MiningLevel, data.miningLevel);
        baseValues.Set(StatType.MiningRange, data.miningRange);
        baseValues.Set(StatType.MiningCooldown, data.miningCooldown);
        baseValues.Set(StatType.MiningSpeed, 1f);

        // 곱연산 업그레이드 대상 스탯 기본값 보장
        SetDefaultMultiplicativeBaseValues();

        // 상태값 복원 (기준값이 설정된 후 적용)
        gold = data.gold;
        currentStamina = data.currentStamina;
        
        // 최종값들은 Provider들이 자동으로 적용되면서 재계산됨
        MarkDirty();

        // 0인 채로 저장된 세이브를 불러오면 그 자리에서 탈진 상태로 시작해야 한다
        // (여기서 안 맞추면 스태미나 0인데 점프·채굴이 되는 한 프레임 구멍이 생긴다).
        //
        // 클램프까지 하는 이유: currentStamina는 위에서 프로퍼티가 아니라 필드에 직접 넣으므로
        // 상한 검사를 거치지 않는다. 저장 당시보다 MaxStamina가 낮아진 세이브
        // (업그레이드 수치 변경·부상 상태 복원 등)를 불러오면 currentStamina가 상한 위에
        // 떠 있게 되고, 그 초과분을 다 쓸 때까지 소모가 스태미나 바에 안 보인다.
        // ClampCurrentStamina가 RefreshExhaustion까지 안에서 부른다.
        ClampCurrentStamina();
    }

    // ===================================================
    // 곱연산 업그레이드 대상 스탯 기본값 보장
    // 이미 세팅된 값이 있으면(> 0) 덮어쓰지 않음
    // ===================================================
    private void SetDefaultMultiplicativeBaseValues()
    {
        // MaxHp: 기본 체력 100
        if (baseValues.Get(StatType.MaxHp) <= 0f)
            baseValues.Set(StatType.MaxHp, 100f);

        // Damage: 기본 공격력 10
        if (baseValues.Get(StatType.Damage) <= 0f)
            baseValues.Set(StatType.Damage, 10f);

        // VisionRadiusUp: 기본 시야 범위 1 (곱연산 기준)
        if (baseValues.Get(StatType.VisionRadiusUp) <= 0f)
            baseValues.Set(StatType.VisionRadiusUp, 1f);

        // FlashlightRangeUp: 기본 손전등 범위 1 (곱연산 기준)
        if (baseValues.Get(StatType.FlashlightRangeUp) <= 0f)
            baseValues.Set(StatType.FlashlightRangeUp, 1f);

        // DrillBatteryCapacity: 기본 드릴 배터리 용량 5
        // 단위는 DrillStrategy의 소모 상수와 같다(차징 0.3/s·대시 3.0/s) — 5면 대시 약 3.3초.
        // 유물(발전기 회복량·제트팩 소모량)과 아이템 충전량도 전부 이 스케일로 적혀 있어,
        // 여기만 다른 숫자(예전 100)를 두면 업그레이드 +N이 유물 대비 20배로 뛴다.
        if (baseValues.Get(StatType.DrillBatteryCapacity) <= 0f)
            baseValues.Set(StatType.DrillBatteryCapacity, 5f);

        // DrillBatteryRegen: 기본 드릴 배터리 회복 0 (기본 상태에선 자연회복 없음)
        if (baseValues.Get(StatType.DrillBatteryRegen) <= 0f)
            baseValues.Set(StatType.DrillBatteryRegen, 0f);

        // DrillDrainReduce: 드릴 배터리 소모 배율 1 (기준 1, 낮을수록 좋음)
        // 이 줄이 없으면 기본값이 0이라 소모가 통째로 사라진다 — 무한 배터리가 된다.
        if (baseValues.Get(StatType.DrillDrainReduce) <= 0f)
            baseValues.Set(StatType.DrillDrainReduce, 1f);

        // DrillRadius: 드릴 파기 반경 0.5 (절대값. MiningRange에 곱하는 배율이 아니다)
        if (baseValues.Get(StatType.DrillRadius) <= 0f)
            baseValues.Set(StatType.DrillRadius, DrillStrategy.DefaultDigRadius);

        // FallDamageReduce: 기본 낙하 피해 배율 1 (곱연산 기준, 낮을수록 좋음)
        if (baseValues.Get(StatType.FallDamageReduce) <= 0f)
            baseValues.Set(StatType.FallDamageReduce, 1f);

        // MineralSellBonus: 기본 판매 보너스 1 (곱연산 기준)
        if (baseValues.Get(StatType.MineralSellBonus) <= 0f)
            baseValues.Set(StatType.MineralSellBonus, 1f);

        // PickaxeDamageUp: 기본 곡괭이 공격력 1 (합산/곱연산 기준)
        if (baseValues.Get(StatType.PickaxeDamageUp) <= 0f)
            baseValues.Set(StatType.PickaxeDamageUp, 1f);

        // ToolRange: 도구 사거리 배율 1 (곱연산 기준)
        if (baseValues.Get(StatType.ToolRange) <= 0f)
            baseValues.Set(StatType.ToolRange, 1f);

        // EnvironmentResistance: 환경 저항 1 = '저항 없음' (곱연산 기준)
        // 기본값이 0이면 (0 + ΣFlat) × ΠPercent라 CSV의 곱연산 노드
        // (EnvironmentResist_T1_01 ×1.2)가 0 × 1.2 = 0으로 죽는다.
        // LayerDigModifier가 이 스탯으로 층 파기 감쇠를 상쇄하므로 1로 깔아야 한다.
        if (baseValues.Get(StatType.EnvironmentResistance) <= 0f)
            baseValues.Set(StatType.EnvironmentResistance, 1f);

        // ShovelStaminaReduce: 삽질 비용 배율 1 (곱연산 기준, 낮을수록 좋음)
        // 이 줄이 없으면 StatBaseValueTable.Get이 0을 돌려주고, 최종값 공식이
        // (Base + ΣFlat) × ΠPercent라 업그레이드를 사도 0 × 배율 = 0이 된다.
        if (baseValues.Get(StatType.ShovelStaminaReduce) <= 0f)
            baseValues.Set(StatType.ShovelStaminaReduce, 1f);

        // PickaxeStaminaReduce: 곡괭이 비용 배율 1 (위와 같은 이유 — 0이면 업그레이드가 죽는다)
        if (baseValues.Get(StatType.PickaxeStaminaReduce) <= 0f)
            baseValues.Set(StatType.PickaxeStaminaReduce, 1f);

        // StaminaCostReduce: 행동 전반 비용 배율 1 (위와 같은 이유)
        if (baseValues.Get(StatType.StaminaCostReduce) <= 0f)
            baseValues.Set(StatType.StaminaCostReduce, 1f);
    }
}
