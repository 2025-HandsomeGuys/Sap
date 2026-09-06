using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class StaminaManager : MonoBehaviour, IStatProvider
{
    [Header("Player References")]
    [Tooltip("PlayerStat 컴포넌트가 있는 PlayerStat 컴포넌트를 직접 연결하세요.")]
    [SerializeField] private PlayerStat playerStatRef;

    [Tooltip("IPlayerController를 구현한 컴포넌트(예: PlayerController)를 직접 연결하세요.")]
    [SerializeField] private MonoBehaviour playerControllerRef;

    [Header("Stamina Regeneration Settings")]
    [Tooltip("스태미나 회복 시작까지의 대기 시간(초)")]
    public float staminaRegenDelay = 3f;

    [Tooltip("초당 스태미나 회복량의 기준값. 실제 회복량은 최대 스태미나에 비례해 스케일되고, " +
             "이 값은 StatType.StaminaRegen의 기준값(=배율 1배 지점)으로만 쓰인다.")]
    public float staminaRegenRate = 20f;

    [Tooltip("0에서 최대치까지 다 차는 데 걸리는 시간(초). 최대 스태미나가 올라가도 이 시간은 그대로다.")]
    public float staminaFullRegenSeconds = 1.5f;

    [Header("Damage Balance")]
    [Tooltip("부상(injury) 피해 전역 배율. 낙하·함정·용암/냉기지대·구르는 바위·폭발 등 " +
             "AddInjury를 타는 모든 경로에 곱해진다. 방어력 감산 전에 곱하지만 감산이 선형이라 결과는 같다. " +
             "화상·동상·방사선은 각자 배율이 없으므로 영향받지 않는다.")]
    [SerializeField] private float injuryDamageMultiplier = 1.5f;

    [Header("Current Status (Read-Only)")]
    [SerializeField] private float injury;
    [SerializeField] private float burn;
    [SerializeField] private float frostbite;
    [SerializeField] private float radiation;
    [SerializeField] private float diggingReduction;

    public float Injury => injury;
    public float Burn => burn;
    public float Frostbite => frostbite;
    public float Radiation => radiation;
    public float DiggingReduction => diggingReduction;

    private PlayerStat playerStats;
    private IPlayerController playerController;
    private Coroutine regenCoroutine;
    private float lastStaminaValue;

    private List<StatModifier> modifiers = new List<StatModifier>();

    void Start()
    {
        // 1. 인스펙터 직접 할당 우선
        if (playerStatRef != null)
        {
            playerStats = playerStatRef;
        }
        else
        {
            // 2. 직접 할당이 없으면 로컬 PlayerStat 사용
            playerStats = GetComponent<PlayerStat>();
        }

        if (playerControllerRef != null)
            playerController = playerControllerRef as IPlayerController;

        if (playerController == null)
            playerController = GetComponent<IPlayerController>()
                            ?? GetComponentInParent<IPlayerController>()
                            ?? GetComponentInChildren<IPlayerController>();

        if (playerStats == null)
        {
            Debug.LogError("[StaminaManager] PlayerStat을 찾을 수 없습니다. 인스펙터의 'Player Stat Ref' 슬롯에 PlayerStat을 연결해 주세요.");
            this.enabled = false;
            return;
        }

        if (playerController == null)
        {
            Debug.LogError("[StaminaManager] IPlayerController를 찾을 수 없습니다. 인스펙터의 'Player Controller Ref' 슬롯에 PlayerController(Script)를 연결해 주세요.");
            this.enabled = false;
            return;
        }


        playerStats.RegisterProvider(this);
        
        // StaminaRegen 기준값 설정 (기존 20f 유지).
        // 이제 이 값은 "초당 회복량"이 아니라 배율 1배의 기준점이다 — 아이템 버프가 Flat(+10 등)으로
        // 들어오므로 기준값을 1로 낮추면 그 데이터들이 전부 폭주한다. 스케일은 그대로 두고
        // CurrentRegenPerSecond가 최종값/기준값 비율만 배율로 쓴다.
        if (playerStats.GetBaseValue(StatType.StaminaRegen) == 0)
        {
            playerStats.SetBaseValue(StatType.StaminaRegen, staminaRegenRate);
        }

        lastStaminaValue = playerStats.CurrentStamina;
        playerStats.OnStaminaDepleted += OnStaminaDepletedForTelemetry;
        TryStartRegeneration();
    }

    private void OnDestroy()
    {
        if (playerStats != null)
        {
            playerStats.OnStaminaDepleted -= OnStaminaDepletedForTelemetry;
            playerStats.UnregisterProvider(this);
        }
    }

    /// <summary>스태미나 소진 지점 기록 — 일차별 곡선이 학습 여부를 보여준다(설계 §3.2).</summary>
    private void OnStaminaDepletedForTelemetry()
    {
        var encumbrance = FindFirstObjectByType<EncumbranceController>(FindObjectsInactive.Include);

        Telemetry.Log(TelemetryEvents.StaminaDepleted, TelemetryPayload.New()
            .Add("weight", encumbrance != null ? encumbrance.TotalWeight : 0f)
            .Add("encumbered", encumbrance != null && encumbrance.IsEncumbered)
            .Add("injury", Injury)
            .Add("burn", Burn)
            .Add("frostbite", Frostbite)
            .Add("radiation", Radiation));
    }

    void Update()
    {
        if (playerStats.CurrentStamina < lastStaminaValue)
            TryStartRegeneration();

        lastStaminaValue = playerStats.CurrentStamina;
    }

    // ===================================================
    // IStatProvider Implementation
    // ===================================================
    public PlayerStat PlayerStats => playerStats; // StaminaBar 등 외부에서 접근

    /// <summary>MaxStamina에서 깎이는 총량(기준값 스케일). 곱연산 배율은 포함하지 않는다.</summary>
    public float TotalReduction => injury + burn + frostbite + radiation + diggingReduction;

    public IReadOnlyList<StatModifier> GetModifiers()
    {
        modifiers.Clear();
        float totalReduction = TotalReduction;
        if (totalReduction > 0)
            modifiers.Add(new StatModifier(StatType.MaxStamina, ModifierType.Flat, -totalReduction, ModifierSource.Other));
        return modifiers;
    }

    // ===================================================
    // Status Management API
    // ===================================================
    // 무적(PlayerStat.IsInvincible) 중에는 모든 상태 피해(부상/화상/동상/방사선) 누적을 차단한다.
    // 낙하·블랙홀·반중력·마그마·냉기지대 등 모든 호출처가 이 한 곳에서 무적을 존중.
    private bool DamageBlocked => playerStats != null && playerStats.IsInvincible;

    // 방어력·속성 저항이 적용되는 유일한 지점.
    // 낙하(PlayerController)·던전 함정(TrapDamage)·용암/냉기지대(DamageZone)·블랙홀·반중력·
    // 구르는 바위(PlayerStat.ApplyHazardDamage)가 전부 아래 네 메서드로 모이므로,
    // 감산을 여기 한 곳에 두면 모든 피해 경로가 자동으로 커버된다.
    //
    // 채널이 서로 분리돼 있다 — 방어력은 부상만, 각 속성 저항은 자기 속성만 깎는다.
    // 방어력이 화상·동상까지 줄이면 방한복/방열복을 살 이유가 사라진다.
    private float Mitigated(float amount, StatType resistStat, float k)
    {
        if (playerStats == null) return amount;
        return HazardMitigation.Apply(amount, playerStats.GetFinalValue(resistStat), k);
    }

    // 감소는 전부 MarkDirty 뒤에 ClampCurrentStamina를 호출한다.
    // 안 하면 currentStamina가 새 MaxStamina 위에 떠 있게 되고, 그 초과분을 다 쓸 때까지
    // (벽타기·채굴 등) 소모가 바에 안 보인다. MaxStamina가 0이 되면 여기서 쓰러짐이 발화한다.
    public void AddInjury(float amount)      { if (DamageBlocked) return; injury += Mitigated(amount * injuryDamageMultiplier, StatType.Defense, HazardMitigation.DefenseK); playerStats.MarkDirty(); playerStats.ClampCurrentStamina(); }
    public void AddBurn(float amount)        { if (DamageBlocked) return; burn += Mitigated(amount, StatType.HazardBurnResist, HazardMitigation.ResistK); playerStats.MarkDirty(); playerStats.ClampCurrentStamina(); }
    public void AddFrostbite(float amount)
    {
        if (DamageBlocked) return;
        frostbite += Mitigated(amount, StatType.HazardFrostResist, HazardMitigation.ResistK);
        playerStats.MarkDirty();
        playerStats.ClampCurrentStamina();
        FrostbiteOverlayUI.Instance?.UpdateFrostbite(frostbite, playerStats.GetBaseValue(StatType.MaxStamina));
    }
    public void AddRadiation(float amount)   { if (DamageBlocked) return; radiation += Mitigated(amount, StatType.HazardRadiationResist, HazardMitigation.ResistK); playerStats.MarkDirty(); playerStats.ClampCurrentStamina(); }
    public void AddDiggingReduction(float amount)
    {
        diggingReduction += amount;
        playerStats.MarkDirty();
        playerStats.ClampCurrentStamina();
    }

    /// <summary>
    /// 부상·화상·동상만 회복한다. 방사선은 전용 아이템(4인자 오버로드)으로만 빠진다.
    /// </summary>
    public void RecoverStatus(float injuryAmount, float burnAmount, float frostbiteAmount)
        => RecoverStatus(injuryAmount, burnAmount, frostbiteAmount, 0f);

    public void RecoverStatus(float injuryAmount, float burnAmount, float frostbiteAmount, float radiationAmount)
    {
        injury    = Mathf.Max(0, injury - injuryAmount);
        burn      = Mathf.Max(0, burn - burnAmount);
        frostbite = Mathf.Max(0, frostbite - frostbiteAmount);
        radiation = Mathf.Max(0, radiation - radiationAmount);
        playerStats.MarkDirty();
        // 회복으로 MaxStamina가 0 위로 올라왔음을 PlayerStat에 알린다(쓰러짐 1회 발화 래치 해제).
        playerStats.ClampCurrentStamina();
        FrostbiteOverlayUI.Instance?.UpdateFrostbite(frostbite, playerStats.GetBaseValue(StatType.MaxStamina));
    }

    public void ResetDiggingReduction()
    {
        diggingReduction = 0;
        playerStats.MarkDirty();
        playerStats.ClampCurrentStamina(); // 위와 같은 이유 — 래치 해제
    }

    /// <summary>
    /// 현재 스태미나를 (감소분이 반영된) MaxStamina까지 즉시 채운다.
    /// 지상 복귀처럼 "완전히 회복된 상태로 시작"해야 하는 지점에서 호출한다.
    /// 감소 필드를 먼저 초기화한 뒤 호출해야 온전한 최대치까지 찬다.
    /// </summary>
    public void RefillStamina()
    {
        if (playerStats == null) return;
        playerStats.CurrentStamina = playerStats.MaxStamina;
    }

    // ===================================================
    // Regeneration Logic
    // ===================================================
    /// <summary>
    /// 지금 이 순간의 초당 회복량.
    ///
    /// 최대 스태미나에 <b>비례</b>하므로 완충까지 걸리는 시간은 최대치가 얼마든 항상
    /// <see cref="staminaFullRegenSeconds"/>로 고정된다 — 최대 스태미나 업그레이드가
    /// "바가 길어지는 만큼 채우는 데 더 오래 걸리는" 반쪽짜리 강화가 되지 않게 하기 위한 것.
    ///
    /// <see cref="StatType.StaminaRegen"/>은 <b>절대값이 아니라 기준값 대비 배율</b>로만 반영한다
    /// (과적 ×0.5, 아이템 StaminaRegenBoost 등). 최종값을 그대로 초당 회복량으로 쓰면
    /// 최대치 비례가 통째로 깨진다.
    ///
    /// 기준이 되는 최대치는 부상·화상 감소가 반영된 <see cref="PlayerStat.MaxStamina"/>다 —
    /// 즉 다친 상태에서도 (짧아진) 바를 채우는 시간은 같다.
    /// </summary>
    public float CurrentRegenPerSecond
    {
        get
        {
            if (playerStats == null || staminaFullRegenSeconds <= 0f) return 0f;

            float baseRegen = playerStats.GetBaseValue(StatType.StaminaRegen);
            float speedMultiplier = baseRegen > 0f
                ? playerStats.GetFinalValue(StatType.StaminaRegen) / baseRegen
                : 1f;

            return playerStats.MaxStamina / staminaFullRegenSeconds * speedMultiplier;
        }
    }

    private void TryStartRegeneration()
    {
        if (regenCoroutine != null)
            StopCoroutine(regenCoroutine);
        regenCoroutine = StartCoroutine(RegenerateStamina());
    }

    private IEnumerator RegenerateStamina()
    {
        yield return new WaitForSeconds(staminaRegenDelay);

        while (playerStats.CurrentStamina < playerStats.MaxStamina)
        {
            if (!playerController.IsWallClimbing)
                playerStats.RecoverStamina(CurrentRegenPerSecond * Time.deltaTime);
            yield return null;
        }

        regenCoroutine = null;
    }
}
