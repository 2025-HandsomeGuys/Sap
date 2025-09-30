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
}

public enum MineralID
{
    None = 0,

    // Layer 1: 무른땅
    Garbage = 100,
    ScrapMetal = 101,
    Coal = 102,
    PETBottle = 103,
    HalfCoin = 104,
    GearFragment = 105,
    Magnet = 106,

    // Layer 2: 단단한땅
    Copper = 200,
    Obsidian = 201,

    // Layer 3: 서늘한땅
    IronOre = 300,
    Silver = 301,
    BlueCrystal = 302,
    CashCoin = 303,

    // Layer 4: 빙하기땅
    AncientFish = 400,
    Cryptomoning = 401,
    Dooly = 402,

    // Layer 5: 더운땅
    GoldOre = 500,
    Fossil = 501,
    HumanSkeleton = 502,

    // Layer 6: 마그마땅
    EssenceOfLava = 600,
    Basalt = 601,

    // Layer 7: 최종땅
    MeteoriteFragment = 700,
    Sapphire = 701,
    Ruby = 702,
    Vibranium = 703,
    LightCoin = 704
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