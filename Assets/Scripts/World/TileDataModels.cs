using System.Collections.Generic;

// This file contains the C# class representations of our JSON data structure.
// These classes are used by JsonUtility to deserialize the JSON file.

[System.Serializable]
public class TileDataJson
{
    public string tileType; // Using string for enum name to be human-readable in JSON
    public float maxStaminaReduction;
}

[System.Serializable]
public class TileDatabaseJson
{
    public List<TileDataJson> tiles;
}
