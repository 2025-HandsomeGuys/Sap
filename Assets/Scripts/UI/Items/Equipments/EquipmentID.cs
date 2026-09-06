/// <summary>
/// 장비 고유 ID를 정의하는 enum
/// </summary>
public enum EquipmentID
{
    None = 0,

    // 머리 장비 (3000번대)
    LeatherHelmet = 3001,
    IronHelmet = 3002,
    ClimberHelmet = 3003,
    MinerHelmet = 3004,
    CarrierHelmet = 3005,
    WinterHelmet = 3006,
    HeatHelmet = 3007,
    EngineerHelmet = 3008,
    ScientistHelmet = 3009,
    ShovelHelmet = 3010,
    MarathonHelmet = 3011,

    // 옷 장비 (3100번대)
    LeatherArmor = 3101,
    IronArmor = 3102,
    ClimberArmor = 3103,
    MinerArmor = 3104,
    CarrierArmor = 3105,
    WinterArmor = 3106,
    HeatArmor = 3107,
    EngineerArmor = 3108,
    ScientistArmor = 3109,
    ShovelArmor = 3110,
    MarathonArmor = 3111,

    // 신발 장비 (3200번대)
    LeatherBoots = 3201,
    IronBoots = 3202,
    ClimberBoots = 3203,
    MinerBoots = 3204,
    CarrierBoots = 3205,
    WinterBoots = 3206,
    HeatBoots = 3207,
    EngineerBoots = 3208,
    ScientistBoots = 3209,
    ShovelBoots = 3210,
    MarathonBoots = 3211,

    // 유물 장비 (3300번대 - 패시브) — 유물은 RelicManager에서 별도 관리
    WoodRelic = 3301,
    SilverRelic = 3302,
}
