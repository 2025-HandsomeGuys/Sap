// @tags: enum, tile, terrain, layer, mineral, tool, item
public enum TileType
{
    // Special
    Empty = 0,
    Bedrock = 1, // Placeholder for bottom of the world
    Rock = 2,    // DiggableRock 오브젝트 전용 (돌 데미지 배율 설정에 사용)

    // Layer Base Tiles
    Dirt = 10, // Layer 1
    HardStone = 11, // Layer 2
    CoolStone = 12, // Layer 3
    Ice = 13, // Layer 4
    HotStone = 14, // Layer 5
    MagmaRock = 15, // Layer 6
    MeteoriteRock = 16, // Layer 7
}

public enum MineralID
{
    None = 0,

    // 1단계: 잡동사니 및 기초 광물
    ScrapMetal = 100,    // 고철
    GarbageBag = 101,    // 쓰레기봉지
    PETBottle = 102,     // 페트병
    Coal = 103,          // 석탄
    Copper = 104,        // 구리
    Iron = 105,          // 철

    // 2단계: 보석 및 희귀 금속
    Meteorite = 200,     // 운석
    Fossil = 201,        // 화석
    Silver = 202,        // 은
    Sapphire = 203,      // 사파이어
    Emerald = 204,       // 에메랄드
    Topaz = 205,         // 토파즈

    // 3단계: 고가치 광물 및 보석
    Obsidian = 300,      // 흑요석
    Quartz = 301,        // 석영
    Gold = 302,          // 금
    Ruby = 303,          // 루비
    Diamond = 304,       // 다이아몬드
    LavaStone = 305,     // 용암석

    // 4단계: 초희귀 및 우주 물질
    Mithril = 400,       // 미스릴
    Gravitonium = 401,   // 중력석
    Uranium = 402,       // 우라늄
    VoidStone = 403,     // 공허석
    StarFragment = 404,  // 별조각
}

public enum ChunkStatus
{
    Loading,
    Ready,
    Unloaded
}

public enum LayerType
{
    SoftGround,      // 1. 무른땅
    HardGround,      // 2. 단단한땅
    CoolGround,      // 3. 서늘한땅
    IceAgeGround,    // 4. 빙하기땅
    HotGround,       // 5. 더운땅
    MagmaGround,     // 6. 마그마땅
    FinalGround      // 7. 최종땅
}

public enum ItemID
{
    None = 0,
    // 아이템들 (1000번대)
    FrostbiteResist = 1001,
    BurnResist = 1002,
    ClimbPotion = 1003,
    // 필요에 따라 추가
}

public enum ShopItemType
{
    Item,
    Equipment,
    Relic
}

public enum EquipmentType
{
    None = 0,
    Head = 1,
    Clothes = 2,
    Shoes = 3,
    Relic = 4
}

public enum ToolID
{
    None = 0,
    // 도구들 (2000번대)
    Hand = 2000,
    Pickaxe = 2001,
    Shovel = 2002,
    Drill = 2003
    // 필요에 따라 추가
}

public enum MineralRarity
{
    Common,
    Rare
}

public enum TimeOfDay
{
    Morning,
    Afternoon
}

/// <summary>
/// 층에 체류할 때 시간당 누적되는 상태이상 종류.
/// tileData.json의 zoneStatusType 문자열과 대응하며, 파기 비용(maxStaminaReduction)과는 별개다.
/// </summary>
public enum ZoneStatusType
{
    None = 0,
    Frostbite,   // 동상 (2층 Ice)
    Burn,        // 화상 (3층 MagmaRock)
    Radiation    // 방사선 (4층 MeteoriteRock)
}