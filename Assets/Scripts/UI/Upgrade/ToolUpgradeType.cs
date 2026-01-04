using UnityEngine;

/// <summary>
/// 도구별 강화 타입
/// </summary>
public enum ToolUpgradeType
{
    // 삽 강화
    ShovelStaminaReduction,      // 스테미나 소모 감소
    
    // 곡괭이 강화
    PickaxeDropRateIncrease,     // 광물 추가 드랍률 증가
    
    // 공통 강화 (삽·곡괭이)
    CommonDigRangeIncrease,      // 파는 범위 증가
    
    // 드릴 강화
    DrillSpeedIncrease,          // 전진 속도 증가
    DrillDurationIncrease,       // 지속 시간 증가
    
    // 강인도 (채굴 파워) - 단일 슬롯에서 레벨업 (레벨 1~7)
    HardnessLevel,               // 강인도 (레벨 1~7)
    
    // 구버전 호환용 (더 이상 사용하지 않음)
    HardnessLevel1,              // 강인도 레벨 1 (구버전)
    HardnessLevel2,              // 강인도 레벨 2 (구버전)
    HardnessLevel3,              // 강인도 레벨 3 (구버전)
    HardnessLevel4,              // 강인도 레벨 4 (구버전)
    HardnessLevel5,              // 강인도 레벨 5 (구버전)
    HardnessLevel6,              // 강인도 레벨 6 (구버전)
    HardnessLevel7               // 강인도 레벨 7 (구버전)
}


