// @tags: json, loader, singleton, wind, pattern
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// StreamingAssets/windPatterns.json 파일을 로드하여 
/// WindPatternData를 메모리에 캐싱하고 제공하는 유틸리티
/// </summary>
public class WindPatternLoader
{
    private static WindPatternLoader _instance;
    public static WindPatternLoader Instance => _instance ??= new WindPatternLoader();

    private Dictionary<string, WindPatternData> _patternDict;
    private const string FILE_NAME = "windPatterns.json";

    private WindPatternLoader()
    {
        LoadSettings();
    }

    public void LoadSettings()
    {
        _patternDict = new Dictionary<string, WindPatternData>();
        
        string path = Path.Combine(Application.streamingAssetsPath, FILE_NAME);
        if (!File.Exists(path))
        {
            Debug.LogError($"[WindPatternLoader] 파일을 찾을 수 없습니다: {path}");
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            WindPatternDatabase db = JsonUtility.FromJson<WindPatternDatabase>(json);

            if (db != null && db.patterns != null)
            {
                foreach (var pattern in db.patterns)
                {
                    _patternDict[pattern.patternId] = pattern;
                }
                Debug.Log($"[WindPatternLoader] 바람 패턴 로드 성공. 총 {_patternDict.Count}개 패턴 캐싱.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WindPatternLoader] 파일 파싱 중 오류 발생: {e.Message}");
        }
    }

    /// <summary>
    /// 지정된 patternId의 바람 패턴 데이터를 반환합니다.
    /// 없으면 null 반환.
    /// </summary>
    public WindPatternData GetPattern(string patternId)
    {
        if (string.IsNullOrEmpty(patternId)) return null;
        
        if (_patternDict.TryGetValue(patternId, out WindPatternData data))
        {
            return data;
        }
        
        Debug.LogWarning($"[WindPatternLoader] 패턴 ID '{patternId}'를 찾을 수 없습니다.");
        return null;
    }
}
