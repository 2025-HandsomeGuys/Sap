/// <summary>
/// 플레이어의 모든 스탯 종류를 정의하는 enum.
/// 스탯 추가/제거 시 이 enum만 수정하면 됨.
/// </summary>
public enum StatType
{
    None = 0,

    // === 채광 관련 ===
    MiningLevel,                // 채광 레벨
    MiningSpeed,                // 채굴 속도 (차징/공격 속도)
    MiningRange,                // 채굴 범위
    MiningCooldown,             // 채굴 쿨다운
    ToolRange,                  // 곡괭이/삽 범위 증가
    ToolChargeTimeReduce,       // 곡괭이/삽 차징 시간 감소
    PickaxeDamageUp,            // 곡괭이 돌 피해 증가
    RockDamageFlat,             // 암석 데미지 고정 보너스
    RockDamageMultiplier,       // 암석 데미지 배율
    RareMineralChance,          // 희귀 광물 발견 확률 증가
    MineralExtraDropChance,     // 추가 광물 드롭 확률
    RockMineralCountUp,         // 돌 완파 시 광물 개수 증가 (소수부 = 확률)

    // === 이동 관련 ===
    MoveSpeed,                  // 이동 속도
    JumpForce,                  // 점프 힘
    WallClimbSpeed,             // 벽타기 속도
    FallDamageReduce,           // 낙하 피해 감소
    EncumberedSpeedMultiplier,  // 과적 시 속도 배율

    // === 스태미나 관련 ===
    MaxStamina,                 // 최대 스태미나
    StaminaCostPerSecond,       // 스태미나 초당 소모
    ShovelStaminaReduce,        // 삽 스태미나 절약

    // === 드릴 관련 ===
    DrillBatteryCapacity,       // 드릴 배터리 최대치
    DrillBatteryRegen,          // 드릴 배터리 회복

    // === 전투 관련 ===
    CritChanceUp,               // 치명타 확률
    MaxHp,                      // 최대 HP
    Damage,                     // 공격력

    // === 인벤토리/경제 관련 ===
    MineralSellBonus,           // 광물 판매 보너스
    InventorySlotUp,            // 인벤 슬롯 확장
    InventoryWeightUp,          // 무게 한도 증가
    WarehouseCapacityUp,        // 창고 슬롯 확장
    ConsumableSlotUnlock,       // 소모 아이템 슬롯

    // === 환경/탐색 관련 ===
    VisionRadiusUp,             // 주변 시야 증가
    FlashlightRangeUp,          // 손전등 사거리
    EnvironmentResistance,      // 환경 저항
    StaminaRegen,               // 스태미나 재생 속도
    HazardFrostResist,          // 빙결 속성 저항
    HazardBurnResist,           // 화상 속성 저항

    // ⚠ 새 스탯은 여기(맨 끝)에 붙인다. 값이 암묵적이라 중간에 끼워 넣으면 뒤 항목의
    //   번호가 전부 밀리고, StatType을 int로 직렬화한 에셋이 조용히 다른 스탯을 가리킨다
    //   (예: SpiderGlove.asset의 statType: 14). 그래서 곡괭이 스태미나는 삽 옆이 아니라 여기 있다.
    PickaxeStaminaReduce,       // 곡괭이 스태미나 절약 (곱연산 배율, 낮을수록 좋음)

    Defense,                    // 방어력 — 부상(AddInjury) 피해를 줄인다 (Flat, 높을수록 좋음)
    HazardRadiationResist,      // 방사선 속성 저항 (Flat, 높을수록 좋음)

    // 행동 전반의 스태미나 비용 배율 (기본 1, 낮을수록 좋음).
    // ShovelStaminaReduce·PickaxeStaminaReduce와 같은 '기준 1 배율' 스탯이라
    // 업그레이드가 -0.15씩 **더해서** 깎는다 → 노드를 여러 개 사도 복리가 안 붙는다.
    StaminaCostReduce,

    // 드릴 배터리 소모 배율 (기본 1, 낮을수록 좋음). 위 StaminaCostReduce와 같은 규칙으로
    // 업그레이드가 -0.15씩 **더해서** 깎는다. 소비처는 DrillStrategy의 차징·대시 소모 두 곳.
    DrillDrainReduce,

    // 드릴 파기 반경 (기준 0.5, 월드 유닛 절대값, 높을수록 좋음). 드릴만 쓰는 전용 반경이라
    // 예전에 DrillStrategy에 하드코딩돼 있던 0.5가 원본이다. 소비처는 DrillStrategy.GetDigParameters.
    DrillRadius,
}
