// @tags: settings, loader, json, singleton, world, manager
using UnityEngine;
using System.IO;

/// <summary>
/// StreamingAssets/worldSettings.json 을 읽어 WorldSettingsData 를 제공하는 싱글턴 로더.
/// Script Execution Order: -200 (TileDataManager, InfinityMapManager 보다 먼저 실행)
/// </summary>
[DefaultExecutionOrder(-200)]
public class WorldSettingsLoader : MonoBehaviour
{
    public static WorldSettingsLoader Instance { get; private set; }

    public WorldSettingsData Settings { get; private set; }

    private const string FILE_NAME = "worldSettings.json";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        Load();
    }

    private void Load()
    {
        string path = Path.Combine(Application.streamingAssetsPath, FILE_NAME);

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[WorldSettingsLoader] {FILE_NAME} 없음 — 기본값 사용.");
            Settings = new WorldSettingsData();
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            Settings = JsonUtility.FromJson<WorldSettingsData>(json);
            Debug.Log($"[WorldSettingsLoader] 로드 완료: seed={Settings.world.seed}, viewDistance={Settings.world.viewDistance}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldSettingsLoader] 파싱 실패 — 기본값 사용. 오류: {e.Message}");
            Settings = new WorldSettingsData();
        }
    }
}
