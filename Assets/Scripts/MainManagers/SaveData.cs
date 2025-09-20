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
    // Using a 1D array for tile states because Unity's JsonUtility doesn't handle 2D arrays.
    public int[] tileStates;

    public SerializableChunkData(int x, int y, int chunkSize)
    {
        chunkX = x;
        chunkY = y;
        tileStates = new int[chunkSize * chunkSize];
    }
}