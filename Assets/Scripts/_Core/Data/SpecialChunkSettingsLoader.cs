// @tags: special-chunk, settings, loader, json, singleton, manager
using UnityEngine;
using System.IO;

/// <summary>
/// StreamingAssets/specialChunkSettings.json 을 읽어 SpecialChunkSettingsData 를 제공하는 싱글턴 로더.
/// Script Execution Order: -150 (SpecialChunkManager, 각 트랩 컴포넌트보다 먼저 실행)
/// </summary>
[DefaultExecutionOrder(-150)]
public class SpecialChunkSettingsLoader : MonoBehaviour
{
    public static SpecialChunkSettingsLoader Instance { get; private set; }

    public SpecialChunkSettingsData Settings { get; private set; }

    private const string FILE_NAME = "specialChunkSettings.json";

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
            Debug.LogWarning($"[SpecialChunkSettingsLoader] {FILE_NAME} 없음 — 기본값 사용.");
            Settings = new SpecialChunkSettingsData();
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            Settings = JsonUtility.FromJson<SpecialChunkSettingsData>(json);
            Debug.Log($"[SpecialChunkSettingsLoader] 로드 완료: minChunkSpacing={Settings.spawning.minChunkSpacing}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SpecialChunkSettingsLoader] 파싱 실패 — 기본값 사용. 오류: {e.Message}");
            Settings = new SpecialChunkSettingsData();
        }
    }
}
