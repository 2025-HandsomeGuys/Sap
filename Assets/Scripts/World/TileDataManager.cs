using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class TileDataManager : MonoBehaviour
{
    #region Singleton
    public static TileDataManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        { 
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadTileData();
    }
    #endregion

    [Header("Settings")]
    [Tooltip("Path to the JSON file relative to the StreamingAssets folder.")]
    public string tileDataJsonPath = "tileData.json";

    private Dictionary<TileType, TileDataJson> _tileDataDict = new Dictionary<TileType, TileDataJson>();

    private void LoadTileData()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, tileDataJsonPath);

        if (!File.Exists(filePath))
        {
            Debug.LogError($"Tile data file not found at: {filePath}");
            return;
        }

        try
        {
            string dataAsJson = File.ReadAllText(filePath);
            TileDatabaseJson database = JsonUtility.FromJson<TileDatabaseJson>(dataAsJson);

            _tileDataDict.Clear();
            foreach (var tileData in database.tiles)
            {
                if (Enum.TryParse(tileData.tileType, true, out TileType typeEnum))
                {
                    if (!_tileDataDict.ContainsKey(typeEnum))
                    {
                        _tileDataDict.Add(typeEnum, tileData);
                    }
                    else
                    {
                        Debug.LogWarning($"Duplicate TileType found in JSON data: {tileData.tileType}");
                    }
                }
                else
                {
                    Debug.LogWarning($"Failed to parse TileType from JSON: {tileData.tileType}");
                }
            }
            Debug.Log($"Successfully loaded data for {_tileDataDict.Count} tiles.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading tile data: {e.Message}");
        }
    }

    public TileDataJson GetData(TileType tileType)
    {
        _tileDataDict.TryGetValue(tileType, out TileDataJson data);
        return data;
    }
}
