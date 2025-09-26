public enum TileType
{
    // Special
    Empty = 0,
    Bedrock = 1, // Placeholder for bottom of the world

    // Layer Base Tiles
    Dirt = 10, // Layer 1
    HardStone = 11, // Layer 2
    CoolStone = 12, // Layer 3
    Ice = 13, // Layer 4
    HotStone = 14, // Layer 5
    MagmaRock = 15, // Layer 6
    MeteoriteRock = 16, // Layer 7

    // Resources - Layer 1
    Garbage = 100,
    ScrapMetal = 101,
    Iron = 102,
    Coal = 103,
    Limestone = 104,

    // Resources - Layer 2
    Copper = 200,
    HalfCoin = 201,
    GearFragment = 202,
    Tin = 203,
    Quartz = 204,
    Obsidian = 205,

    // Resources - Layer 3
    IronOre = 300, // Distinct from layer 1 Iron
    Silver = 301,
    BitCoding = 302,
    Jade = 303,
    BlueCrystal = 304,

    // Resources - Layer 4
    AncientFishInIce = 400,
    Cryptomoning = 401,
    Dooly = 402, // :) 
    MoonlightCrystal = 403,

    // Resources - Layer 5
    GoldOre = 500,
    Fossil = 501,
    HumanSkeleton = 502,

    // Resources - Layer 6
    EssenceOfLava = 600,
    Basalt = 601,

    // Resources - Layer 7
    MeteoriteFragment = 700,
    Sapphire = 701,
    Ruby = 702,
    Vibranium = 703,
    LightCoin = 704
}

public enum PoolableType
{
    // Layer 1: 무른땅 - 약한 갈색
    Garbage,           // 쓰레기 - 봉투
    PETBottle,         // 쓰레기 - 페트
    ScrapMetal,        // 고철 - 폐자건거, 폐가전제품
    Iron,              // TileType.Iron (Layer 1)과 일치하도록 추가
    Magnet,  // 자석 - 탐지기 해금용

    // Layer 2: 단단한땅 - 조금 진한 갈색
    Copper,             // 구리
    Coal,               // 석탄
    Limestone,         // TileType.Limestone과 일치하도록 추가

    GearFragment,      // 장비레시피 조각 - 드릴 해금용
    Tin,                // TileType.Tin과 일치하도록 추가
    Quartz,             // TileType.Quartz와 일치하도록 추가
    Obsidian,           // 흑요석

    // Layer 3: 서늘한땅 - 푸른회색
    IronOre,            // 철광석
    Silver,             // 은
    BitCoding,          // TileType.BitCoding과 일치하도록 추가
    Jade,               // TileType.Jade와 일치하도록 추가
    BlueCrystal,        // TileType.BlueCrystal과 일치하도록 추가

    // Layer 4: 빙하기땅 - 얼음, 동굴, 고드름
    AncientFishInIce,   // 얼음 속 고대 물고기
    Cryptomoning,       // 크립토모닝
    Dooly,              // 돌리
    MoonlightCrystal,   // TileType.MoonlightCrystal과 일치하도록 추가

    // Layer 5: 더운땅 - 붉은 흙, 암석
    GoldOre,            // 금광석
    Fossil,             // 화석
    HumanSkeleton,      // 인간 해골 (광부 옷)

    // Layer 6: 마그마땅 - 용암 배경
    EssenceOfLava,      // 마그마의 정수
    Basalt,             // 돌하르방 닮은 현무암

    // Layer 7: 최종땅 - 하얀색
    Sapphire,           // 사파이어
    Ruby,               // 루비
    Vibranium,          // 비브라늄
    MeteoriteFragment,  // 운석 파편 (별가루)
    LightCoin           // 빛코인
}

public enum MineralID
{
    None = 0,

    // Layer 1: 무른땅 - 약한 갈색
    GarbageBag = 100,        // 쓰레기 - 봉투
    PETBottle = 101,         // 쓰레기 - 페트
    ScrapMetal = 102,        // 고철 - 폐자건거, 폐가전제품
    Magnet = 103,  // 자석 - 탐지기 해금용

    // Layer 2: 단단한땅 - 조금 진한 갈색
    Copper = 200,             // 구리
    Coal = 201,               // 석탄
    EquipmentRecipePieces = 202,      // 장비파편 - 드릴 해금용

    // Layer 3: 서늘한땅 - 푸른회색
    IronOre = 300,            // 철광석
    Silver = 301,             // 은
    CashCoin = 302,               // 엽전 (동전 아님)

    // Layer 4: 빙하기땅 - 얼음, 동굴, 고드름
    AncientFish = 400,   // 얼음 속 고대 물고기
    Cryptomorning = 401,       // 크립토모닝
    Doly = 402,              // 돌리

    // Layer 5: 더운땅 - 붉은 흙, 암석
    GoldOre = 500,            // 금광석
    Fossil = 501,             // 화석
    HumanSkeleton = 502,      // 인간 해골 (광부 옷)

    // Layer 6: 마그마땅 - 용암 배경
    EssenceOfLava = 600,      // 마그마의 정수
    Obsidian = 601,           // 흑요석
    Basalt = 602,             // 돌하르방 닮은 현무암

    // Layer 7: 최종땅 - 하얀색
    Sapphire = 700,           // 사파이어
    Ruby = 701,               // 루비
    Vibranium = 702,          // 비브라늄
    MeteoriteFragment = 703,  // 운석 파편 (별가루)
    BikCoin = 704           // 빛코인
}

public enum ItemID
{
    None = 0,

    // 지역 특성 방지(환경저항)
    FrostbiteResist = 1001,
    BurnResist = 1002,

    // 플레이어 도핑제(일시적 버프)
    SpeedBooster = 2001,
    EnergyDrink = 2002
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
