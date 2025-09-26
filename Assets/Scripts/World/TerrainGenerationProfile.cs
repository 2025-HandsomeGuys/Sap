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

    [Tooltip("The specific type of this layer, used for linking with other systems like Object Pooling.")]
    public LayerType layerType;

    [Tooltip("The Y coordinate where this layer starts. Can be negative.")]
    public int startDepth;

    [Tooltip("The base tile that makes up the majority of this layer.")]
    public TileType baseTileType;

    [Tooltip("A list of all minerals/resources that can spawn in this layer and their generation rules.")]
    public List<MinableSpawnConfig> mineralConfigs;

    [Header("Diagonal Vein Settings")]
    [Tooltip("If true, this layer will have diagonal veins of a different base tile.")]
    public bool hasDiagonalVeins;
    [Tooltip("The tile type to use for the diagonal veins.")]
    public TileType diagonalVeinTile;
    [Tooltip("Controls the thickness and frequency of the veins. Smaller values = thicker, larger veins.")]
    public float veinNoiseScale = 0.1f;

    [Header("Vein Angle Settings")]
    [Tooltip("The min/max angle of the veins in degrees. The angle will vary between these values.")]
    public Vector2 veinAngleRange = new Vector2(30, 60);
    [Tooltip("Controls how quickly the vein angle changes. Smaller values = larger, smoother waves.")]
    public float veinAngleNoiseScale = 0.02f;

    [Tooltip("The cutoff for vein generation. Higher values = less frequent veins.")]
    public float veinThreshold = 0.7f;

    [Tooltip("Controls the scale of the noise used to break up the veins and vary their thickness. Smaller values = larger, smoother variations.")]
    public float veinThicknessNoiseScale = 0.05f;

    [Tooltip("How much max stamina is drained per second while the player is in this layer.")]
    public float periodicMaxStaminaDamage;
}
