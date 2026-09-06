/// <summary>
/// 스탯 수정치의 적용 방식
/// </summary>
public enum ModifierType
{
    Flat,       // 합연산 (기본값에 더해짐)
    Percent     // 곱연산 (기본값에 곱해짐, 1.1 = 10% 증가)
}

/// <summary>
/// 스탯 수정치의 출처 구분
/// </summary>
public enum ModifierSource
{
    Upgrade,     // 업그레이드 트리
    Equipment,   // 장비
    Buff,        // 버프 (소모 아이템, 환경 등)
    Drill,       // 드릴 시스템
    Inventory,   // 인벤토리 시스템
    Relic,       // 유물 시스템
    Other        // 기타
}

/// <summary>
/// 각 시스템이 PlayerStat에 제공하는 스탯 수정치 단위.
/// 어떤 스탯에, 어떤 방식으로, 얼마만큼, 어디서 왔는지를 기록.
/// </summary>
[System.Serializable]
public struct StatModifier
{
    public StatType statType;
    public ModifierType modifierType;
    public float value;
    public ModifierSource source;

    public StatModifier(StatType statType, ModifierType modifierType, float value, ModifierSource source)
    {
        this.statType = statType;
        this.modifierType = modifierType;
        this.value = value;
        this.source = source;
    }

    public override string ToString()
    {
        string sign = modifierType == ModifierType.Flat ? "+" : "×";
        return $"[{source}] {statType}: {sign}{value}";
    }
}
