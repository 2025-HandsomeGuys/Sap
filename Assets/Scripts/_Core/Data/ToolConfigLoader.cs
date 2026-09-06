// @tags: tool, config, loader, json, singleton, manager
using UnityEngine;
using System.IO;

/// <summary>
/// StreamingAssets/toolConfig.json 을 읽어 ToolConfigData 를 제공하는 싱글턴 로더.
/// Script Execution Order: -200 (ToolController 보다 먼저 실행)
/// </summary>
[DefaultExecutionOrder(-200)]
public class ToolConfigLoader : MonoBehaviour
{
    public static ToolConfigLoader Instance { get; private set; }

    public ToolConfigData Config { get; private set; }

    private const string FILE_NAME = "toolConfig.json";

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
            Debug.LogWarning($"[ToolConfigLoader] {FILE_NAME} 없음 — 기본값 사용 (모든 도구 해금).");
            Config = new ToolConfigData();
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            Config = JsonUtility.FromJson<ToolConfigData>(json);
            Debug.Log($"[ToolConfigLoader] 로드 완료: 도구 {Config.unlockNodeIds.Length}개 설정.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ToolConfigLoader] 파싱 실패 — 기본값 사용. 오류: {e.Message}");
            Config = new ToolConfigData();
        }
    }
}
