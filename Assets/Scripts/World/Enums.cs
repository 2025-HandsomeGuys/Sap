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
    // Layer 1
    Garbage,
    ScrapMetal,
    Iron,
    Coal,
    Limestone,

    // Layer 2
    Copper,
    HalfCoin,
    GearFragment,
    Tin,
    Quartz,
    Obsidian,

    // Layer 3
    IronOre,
    Silver,
    BitCoding,
    Jade,
    BlueCrystal,

    // Layer 4
    AncientFishInIce,
    Cryptomoning,
    Dooly,
    MoonlightCrystal,

    // Layer 5
    GoldOre,
    Fossil,
    HumanSkeleton,

    // Layer 6
    EssenceOfLava,
    Basalt,

    // Layer 7
    MeteoriteFragment,
    Sapphire,
    Ruby,
    Vibranium,
    LightCoin
}

public enum ItemID
{
    None = 0,

    // Layer 1
    Garbage = 100,
    ScrapMetal = 101,
    Iron = 102,
    Coal = 103,
    Limestone = 104,

    // Layer 2
    Copper = 200,
    HalfCoin = 201,
    GearFragment = 202,
    Tin = 203,
    Quartz = 204,
    Obsidian = 205,

    // Layer 3
    IronOre = 300,
    Silver = 301,
    BitCoding = 302,
    Jade = 303,
    BlueCrystal = 304,

    // Layer 4
    AncientFishInIce = 400,
    Cryptomoning = 401,
    Dooly = 402,
    MoonlightCrystal = 403,

    // Layer 5
    GoldOre = 500,
    Fossil = 501,
    HumanSkeleton = 502,

    // Layer 6
    EssenceOfLava = 600,
    Basalt = 601,

    // Layer 7
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
