using UnityEngine;
using System.Collections.Generic;

// We can reuse the MinableSpawnConfig from the old profile.
// This saves us from redefining it and makes it easy to copy old settings.
using static MineralGenerationProfile;

[CreateAssetMenu(fileName = "TerrainGenerationProfile", menuName = "World/Terrain Generation Profile")]
public class TerrainGenerationProfile : ScriptableObject
{
    [Tooltip("The list of terrain layers, ordered from top to bottom.")]
    public List<TerrainLayer> layers;
}

[System.Serializable]
public class TerrainLayer
{
    [Tooltip("Just for organization in the inspector.")]
    public string description;

    [Tooltip("The Y coordinate where this layer starts. Can be negative.")]
    public int startDepth;

    [Tooltip("The base tile that makes up the majority of this layer.")]
    public TileType baseTileType;

    [Tooltip("A list of all minerals/resources that can spawn in this layer and their generation rules.")]
    public List<MinableSpawnConfig> mineralConfigs;

    [Tooltip("How much max stamina is drained per second while the player is in this layer.")]
    public float periodicMaxStaminaDamage;
}
