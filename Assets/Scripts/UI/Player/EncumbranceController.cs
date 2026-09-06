using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 과적(Encumbrance) 상태를 관리하는 컴포넌트.
///
/// 광물 인벤토리 + 아이템 인벤토리의 합산 무게가
/// 최대 무게(maxWeightLimit)의 80% 이상이 되면 과적 상태로 판정.
///
/// 과적 시 효과:
///   - 이동 속도: PlayerStat.EncumberedSpeedMultiplier 배율 적용
///   - 벽타기 속도: 동일 배율 적용
///   - 점프력: encumbranceJumpForceMultiplier 배율 적용 (조금 낮게)
///   - 점프 쿨타임: encumbranceJumpCooldownMultiplier 배율 적용 (더 길게)
///   - 스태미나 회복 속도: encumbranceStaminaRegenMultiplier 배율 적용
///
/// UI 연동: OnEncumbranceChanged 이벤트 구독 또는 IsEncumbered / TotalWeight 프로퍼티 폴링.
/// </summary>
public class EncumbranceController : MonoBehaviour, IStatProvider
{
    [Header("인벤토리 참조 (비워두면 자동 탐색)")]
    [SerializeField] private MineralInventory mineralInventory;
    [SerializeField] private ItemInventory itemInventory;

    [Header("플레이어 참조 (비워두면 자동 탐색)")]
    [SerializeField] private PlayerStat playerStat;
    [SerializeField] private PlayerController playerController;

    [Header("과적 설정")]
    // 과적 비율은 MineralInventory.EncumbranceRatio 하나만 쓴다.
    // 예전엔 여기(0.8)와 MineralInventory(고정 25) 두 곳에 따로 있어서
    // 실제 페널티가 걸리는 값과 가방 UI가 표시하는 값이 서로 달랐다.

    [Tooltip("과적 시 스태미나 회복 속도 배율 (0.5 = 50%)")]
    [SerializeField] [Range(0f, 1f)] private float encumbranceStaminaRegenMultiplier = 0.5f;

    [Tooltip("과적 시 점프력 배율 (0.9 = 90%). 점프 높이는 힘의 제곱에 비례하므로 조금만 내려도 체감이 크다.")]
    [SerializeField] [Range(0.3f, 1f)] private float encumbranceJumpForceMultiplier = 0.9f;

    [Tooltip("과적 시 점프 쿨타임 배율 (2 = 2배). PlayerController.jumpCooldown에 곱해진다.")]
    [SerializeField] [Range(1f, 5f)] private float encumbranceJumpCooldownMultiplier = 2f;

    // ─── UI 연동 인터페이스 ───────────────────────────────────────
    /// <summary>과적 상태가 바뀔 때 발화. true = 과적, false = 정상.</summary>
    public event Action<bool> OnEncumbranceChanged;

    /// <summary>현재 과적 여부.</summary>
    public bool IsEncumbered { get; private set; }

    /// <summary>현재 합산 무게.</summary>
    public float TotalWeight =>
        (mineralInventory != null ? mineralInventory.TotalWeight : 0f) +
        (itemInventory    != null ? itemInventory.TotalWeight    : 0f);

    /// <summary>현재 과적 임계값 (한도 × <see cref="MineralInventory.EncumbranceRatio"/>).</summary>
    public float EncumbranceThreshold =>
        mineralInventory != null ? mineralInventory.encumbranceThreshold : 0f;
    // ─────────────────────────────────────────────────────────────

    private readonly List<StatModifier> _modifiers = new List<StatModifier>();
    private float _baseMineralWeightLimit;

    // ===================================================
    // 초기화
    // ===================================================
    private void Start()
    {
        if (mineralInventory == null)
            mineralInventory = FindFirstObjectByType<MineralInventory>();
        if (itemInventory == null)
            itemInventory = FindFirstObjectByType<ItemInventory>();
        if (playerStat == null)
            playerStat = GetComponent<PlayerStat>();
        if (playerController == null)
            playerController = GetComponent<PlayerController>();

        if (mineralInventory == null)
        {
            Debug.LogError("[EncumbranceController] MineralInventory를 찾을 수 없습니다.");
            enabled = false;
            return;
        }

        // 현재 값을 읽지 않는다 — ApplyWeightLimitStat이 이미 한 번 돌았으면
        // 업그레이드분이 섞인 값을 기준으로 잡아 한도가 계속 부풀어 오른다.
        _baseMineralWeightLimit = MineralInventory.BaseWeightLimit;

        mineralInventory.OnInventoryChanged += EvaluateEncumbrance;
        if (itemInventory != null)
            itemInventory.OnInventoryChanged += EvaluateEncumbrance;

        playerStat?.RegisterProvider(this);
        if (playerStat != null)
            playerStat.OnStatChanged += ApplyWeightLimitStat;

        ApplyWeightLimitStat();
        EvaluateEncumbrance();
    }

    private void OnDestroy()
    {
        if (mineralInventory != null)
            mineralInventory.OnInventoryChanged -= EvaluateEncumbrance;
        if (itemInventory != null)
            itemInventory.OnInventoryChanged -= EvaluateEncumbrance;

        if (playerStat != null)
            playerStat.OnStatChanged -= ApplyWeightLimitStat;

        playerStat?.UnregisterProvider(this);
    }

    // ===================================================
    // 무게 한도 갱신
    // ===================================================
    private void ApplyWeightLimitStat()
    {
        if (mineralInventory == null || playerStat == null) return;
        mineralInventory.maxWeightLimit = _baseMineralWeightLimit + playerStat.GetFinalValue(StatType.InventoryWeightUp);
        // Debug.Log($"[Encumbrance] maxWeightLimit={mineralInventory.maxWeightLimit} (base={_baseMineralWeightLimit}, weightUpStat={playerStat.GetFinalValue(StatType.InventoryWeightUp)})");
        ApplySlotCountStat();
        EvaluateEncumbrance();
    }

    /// <summary>
    /// 아이템 가방 칸 수를 업그레이드 스탯에서 갱신한다.
    ///
    /// InventorySlotUp은 스탯 시스템과 UI에는 배선돼 있었지만 아무도 읽지 않는
    /// 죽은 효과였다 — ItemInventory.maxSlotCount가 상수를 그대로 돌려줬기 때문이다.
    /// 무게 한도와 같은 컨트롤러에 두는 이유는 여기가 이미 itemInventory 참조와
    /// PlayerStat.OnStatChanged 구독을 갖고 있어서다(플레이어 프리팹 재세팅 불필요).
    /// </summary>
    private void ApplySlotCountStat()
    {
        if (itemInventory == null || playerStat == null) return;

        int bonus = Mathf.Max(0, Mathf.RoundToInt(playerStat.GetFinalValue(StatType.InventorySlotUp)));
        if (bonus == itemInventory.bonusSlotCount) return;

        itemInventory.bonusSlotCount = bonus;
        itemInventory.EnsureInitialSlots();
        itemInventory.NotifyChanged();
    }

    // ===================================================
    // 과적 평가
    // ===================================================
    private void EvaluateEncumbrance()
    {
        bool wasEncumbered = IsEncumbered;
        IsEncumbered = TotalWeight > EncumbranceThreshold;
        // Debug.Log($"[Encumbrance] TotalWeight={TotalWeight:F1} Threshold={EncumbranceThreshold:F1} IsEncumbered={IsEncumbered}");

        if (wasEncumbered == IsEncumbered) return;

        ApplySpeedEffect();
        playerStat?.MarkDirty(); // GetModifiers() 재계산 트리거

        // 과적 진입만 기록한다 — 해제는 분석에 쓰이지 않아 노이즈만 늘린다(설계 §3.2)
        if (IsEncumbered)
        {
            Telemetry.Log(TelemetryEvents.EncumberedEnter, TelemetryPayload.New()
                .Add("weight", TotalWeight)
                .Add("threshold", EncumbranceThreshold)
                .Add("over", TotalWeight - EncumbranceThreshold));
        }

        OnEncumbranceChanged?.Invoke(IsEncumbered);
    }

    private void ApplySpeedEffect()
    {
        if (playerController == null) return;

        playerController.encumbranceMultiplier = IsEncumbered
            ? (playerStat != null ? playerStat.EncumberedSpeedMultiplier : 0.5f)
            : 1f;

        // 점프는 이동속도 배율과 따로 간다 — 높이를 속도와 같은 비율로 깎으면
        // 한 칸 턱도 못 넘어 지형에 갇힌다.
        playerController.encumbranceJumpForceMultiplier    = IsEncumbered ? encumbranceJumpForceMultiplier    : 1f;
        playerController.encumbranceJumpCooldownMultiplier = IsEncumbered ? encumbranceJumpCooldownMultiplier : 1f;
    }

    // ===================================================
    // IStatProvider — 스태미나 회복 속도 수정자 제공
    // ===================================================
    public IReadOnlyList<StatModifier> GetModifiers()
    {
        _modifiers.Clear();
        if (IsEncumbered)
        {
            _modifiers.Add(new StatModifier(
                StatType.StaminaRegen,
                ModifierType.Percent,
                encumbranceStaminaRegenMultiplier,
                ModifierSource.Other));
        }
        return _modifiers;
    }
}
