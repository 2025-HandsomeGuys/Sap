using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 업그레이드 효과의 종류를 정의합니다.
/// </summary>
public enum UpgradeEffectType
{
    None = 0,

    // ──── 채굴 (100~) ────
    MiningSpeedMultiplier    = 100,  // 채굴 속도(차징 속도) (곱연산)
    MiningRangeMultiplier    = 102,  // 채굴 범위(사거리) (곱연산)
    MiningLevel              = 103,  // 채광 레벨(채굴할 수 있는 땅의 강인도 제한 해금) (계층 해금 게이트)
    MiningCooldownMultiplier = 104,  // 채굴 쿨다운 (곱연산)
    // 채굴 범위 (합연산). 곱연산과 달리 여러 개를 사도 더해지기만 한다 —
    // 층을 내려갈수록 계속 사는 노드라 곱연산으로 두면 배율이 폭주한다(2026-08-24).
    // 곱연산판(102)은 유물·장비용으로 남겨둔다.
    MiningRangeUp            = 105,
    // 채굴 속도(차징) (합연산). 기준값이 정확히 1.0이라(PlayerStat이 세이브와 무관하게 1f로 고정)
    // +0.1 = '기준의 10%p'로 딱 떨어진다. 곱연산판(100)은 유물·장비용으로 남겨둔다.
    MiningSpeedUp            = 106,

    // ──── 도구 (200~) ────
    ToolRange                = 200,  // 도구 사거리
    ToolChargeTimeReduce     = 201,  // 도구 차징 시간 감소
    PickaxeDamageUp          = 202,  // 곡괭이 데미지 증가
    ShovelStaminaReduce      = 203,  // 삽 스태미나 소모 감소
    PickaxeStaminaReduce     = 204,  // 곡괭이 스태미나 소모 감소 (곱연산, 낮을수록 좋음)

    // ──── 드릴 (250~) ────
    DrillBatteryCapacity     = 250,  // 드릴 배터리 용량
    DrillBatteryRegen        = 251,  // 드릴 배터리 재생
    // 드릴 배터리 소모 배율 (합연산, 낮을수록 좋음). StatType.DrillDrainReduce(기준 1)에 붙어서
    // CSV에 **음수**를 적는다(-0.15 = 15% 싸짐) — ShovelStaminaReduce·StaminaCostReduce와 같은 규칙이다.
    // 곱연산이 아닌 이유: 층을 내려갈수록 계속 사는 노드라 곱으로 두면 소모가 0에 수렴한다.
    DrillDrainReduce         = 252,
    // 드릴 파기 반경 (합연산). StatType.DrillRadius(기준 0.5, 월드 유닛)에 더한다.
    // 드릴 최종 반경 = DrillRadius x 층 감쇠 (대시 선행 파기는 x2). MiningRange는 안 들어온다 —
    // 삽·곡괭이(MiningRange)와 드릴(DrillRadius)은 서로를 키우지 않는다(2026-09-01).
    DrillRadiusUp            = 253,

    // ──── 전투 (300~) ────
    DamageMultiplier         = 300,  // 데미지 (곱연산)
    CritChanceUp             = 301,  // 크리티컬 확률 증가
    // 방어력 (합연산). StatType.Defense로 간다. 소비처는 StaminaManager.AddInjury 한 곳뿐이고,
    // 부상(낙하·던전 함정·용암/냉기지대·구르는 바위·폭발) 피해를 소프트캡 K/(K+방어력)으로 줄인다.
    // 장비 방어력(EquipmentStatProvider)과 같은 스탯에 합산된다.
    // 곱연산 '부상 감소 배율'로 만들지 않은 이유: 층을 내려갈수록 계속 사는 노드라
    // 배율이면 몇 개만 사도 부상이 0에 수렴해 위험지대가 통째로 무의미해진다.
    // 화상·동상·방사선은 각자 저항 스탯이라 이 값이 안 먹는다(HazardMitigation 참고).
    DefenseUp                = 302,

    // ──── 이동 / 체력 (400~) ────
    MoveSpeedMultiplier      = 400,  // 이동 속도 (곱연산)
    ClimbSpeedMultiplier     = 401,  // 벽 타기 속도 (곱연산)
    WallClimbSpeed           = 402,  // 벽 타기 속도 (합연산)
    StaminaCostMultiplier    = 403,  // 스태미나 소모 (곱연산, 낮을수록 좋음)
    MaxStaminaUp             = 404,  // 최대 스태미나 증가
    // 스태미나 소모 감소 (합연산). 403과 달리 StatType.StaminaCostReduce(기준 1)에 붙어서
    // CSV에 **음수**를 적는다(-0.15 = 15% 싸짐) — ShovelStaminaReduce와 같은 규칙이다.
    // 403이 아니라 별도 스탯인 이유: 403은 타일마다 다른 채굴 비용(0.1~2.0)에 직접 곱하는
    // 배율이라, 거기에 고정값을 빼면 싼 타일이 공짜가 된다.
    StaminaCostReduce        = 409,
    FallDamageReduce         = 405,  // 낙하 데미지 감소
    JumpForceMultiplier      = 406,  // 점프력 (곱연산). StatType.JumpForce로 간다
    // 이동 속도 (합연산). 곱연산(400)과 달리 여러 개를 사도 더해지기만 한다 —
    // 층을 내려갈수록 계속 사는 노드라 곱연산으로 두면 배율이 폭주한다(MiningRangeUp과 같은 이유).
    // 곱연산판(400)은 유물·장비용으로 남겨둔다.
    MoveSpeedUp              = 407,  // 이동 속도 (합연산). StatType.MoveSpeed로 간다
    JumpForceUp              = 408,  // 점프력 (합연산). StatType.JumpForce로 간다

    // ──── 자원 / 확률 (500~) ────
    MineralExtraDropChance   = 500,  // 광물 추가 드롭 확률
    RareMineralChance        = 501,  // 레어 광물 획득 확률 (0~100)
    MineralSellBonus         = 502,  // 광물 판매 보너스
    MineralPriceUp           = 503,  // 특정 광물 판매가 상승 (targetMineral 지정, 합연산 골드)
    // 돌 하나를 완파했을 때 나오는 광물 개수 (합연산, 소수 허용).
    // 소수부는 확률로 처리된다 — 0.5 = 50% 확률로 +1개. 정수만 쓰면 소형 돌(1~3개)에
    // +1이 33~100% 증가라 한 노드가 너무 크게 튄다.
    // 곱연산으로 두지 않은 이유: 티어 범위(소 1~3 / 중 2~5 / 대 4~8)에 배율을 곱하면
    // 대형 돌에만 몰려 "큰 돌만 캐는" 단일 최적해가 생긴다. 가산은 소형 돌 쪽 체감이 크다.
    RockMineralCountUp       = 504,

    // ──── 인벤토리 / 시설 (600~) ────
    InventorySlotUp          = 600,  // 인벤토리 슬롯 증가
    InventoryWeightUp        = 601,  // 인벤토리 무게 한도 증가
    WarehouseCapacityUp      = 602,  // 창고 용량 증가
    ConsumableSlotUnlock     = 603,  // 소모품 슬롯 해금
    // 유물 장착 칸 수 (합연산). 기준은 RelicManager.BaseSlotCount(2)이고 여기에 더한다.
    // 슬롯 수는 세이브에도 실리지만 원본은 이 효과다 — RelicManager.SyncUpgradeSlotCount 참고.
    RelicSlotUp              = 604,

    // ──── 시야 / 환경 (700~) ────
    VisionRadiusUp           = 700,  // 시야 반경 증가
    FlashlightRangeUp        = 701,  // 손전등 범위 증가
    EnvironmentResistance    = 702,  // 환경 저항력
    MapExploreRadiusUp       = 703,  // 지도 탐사 반경 증가 (합연산, 셀). 미니맵·전체지도가 공유하는 DigPathTracker 기록 범위

    // ──── 휴식 (750~) ────
    NapCount                 = 750,  // 하루에 가능한 낮잠 횟수(합연산). 침대에서 낮잠 = 주식 1틱만 진행
}

/// <summary>
/// 업그레이드가 제공하는 구체적인 효과를 정의하는 SO입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewUpgradeEffect", menuName = "Upgrade/Effect")]
public class UpgradeEffectSO : ScriptableObject
{
    [Tooltip("효과의 종류")]
    public UpgradeEffectType type;

    [Tooltip("적용 수치 (예: 1.1 = 10% 증가, 5 = +5)")]
    public float value;

    [Tooltip("곱연산(%) 여부. true면 곱연산, false면 합연산으로 처리합니다.")]
    public bool isPercentage;

    [Header("Target (MineralPriceUp 전용)")]
    [Tooltip("이 효과가 겨냥하는 광물. MineralPriceUp일 때만 쓰인다. " +
             "None이면 대상이 없어 아무 일도 하지 않는다.")]
    public MineralID targetMineral = MineralID.None;

    [Tooltip("한 노드가 광물 여러 개의 값을 함께 올릴 때, 두 번째 이후의 대상. " +
             "CSV targetMineral 칸에 'GarbageBag;PETBottle'처럼 ';'로 나열하면 채워진다.")]
    public List<MineralID> extraTargetMinerals = new List<MineralID>();

    /// <summary>이 효과가 그 광물을 겨냥하는가. 대상이 여럿일 수 있어 == 비교를 대신한다.</summary>
    public bool HitsMineral(MineralID id)
    {
        if (id == MineralID.None) return false;
        if (targetMineral == id) return true;
        if (extraTargetMinerals == null) return false;
        for (int i = 0; i < extraTargetMinerals.Count; i++)
            if (extraTargetMinerals[i] == id) return true;
        return false;
    }
}
