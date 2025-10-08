using System;
using System.Collections.Generic;

/// <summary>
/// Contains serializable classes for saving and loading world data.
/// </summary>

[Serializable]
public class SerializableWorldData
{
    public List<SerializableChunkData> allChunkData;

    public SerializableWorldData()
    {
        allChunkData = new List<SerializableChunkData>();
    }
}

[Serializable]
public class SerializableChunkData
{
    public int chunkX;
    public int chunkY;
    public int[] terrainLayer; // Changed from tileStates
    public int[] mineralLayer; // Added

    public SerializableChunkData(int x, int y, int chunkSize)
    {
        chunkX = x;
        chunkY = y;
        terrainLayer = new int[chunkSize * chunkSize]; // Changed
        mineralLayer = new int[chunkSize * chunkSize]; // Added
    }
}