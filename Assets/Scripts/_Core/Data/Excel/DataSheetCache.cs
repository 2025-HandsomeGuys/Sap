// @tags: csv, excel, cache, manager, singleton, data, localization
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// CSV 데이터 캐시 매니저.
/// StreamingAssets/Data/ 하위 CSV 파일을 파싱하여 메모리에 보관.
/// </summary>
public class DataSheetCache : MonoBehaviour
{
    public static DataSheetCache Instance { get; private set; }

    [Header("로드할 CSV 파일 목록")]
    [Tooltip("StreamingAssets/Data/ 기준 파일명 (확장자 포함)")]
    public string[] csvFileNames;

    private Dictionary<string, List<Dictionary<string, string>>> _cache 
        = new Dictionary<string, List<Dictionary<string, string>>>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        LoadAll();
    }

    /// <summary>
    /// 캐시된 시트 데이터 반환. 없으면 빈 리스트.
    /// </summary>
    public List<Dictionary<string, string>> GetSheet(string fileName)
    {
        if (_cache.TryGetValue(fileName, out var data))
            return data;

        Debug.LogWarning($"[DataSheetCache] '{fileName}' 데이터가 캐시에 없습니다.");
        return new List<Dictionary<string, string>>();
    }

    /// <summary>
    /// 특정 파일 리로드
    /// </summary>
    public void Reload(string fileName)
    {
        string path = GetFilePath(fileName);
        var data = CsvParser.Parse(path);
        _cache[fileName] = data;
    }

    /// <summary>
    /// 전체 리로드
    /// </summary>
    public void ReloadAll()
    {
        _cache.Clear();
        LoadAll();
    }

    private void LoadAll()
    {
        if (csvFileNames == null || csvFileNames.Length == 0)
        {
            Debug.LogWarning("[DataSheetCache] 로드할 CSV 파일이 지정되지 않았습니다.");
            return;
        }

        foreach (string fileName in csvFileNames)
        {
            if (string.IsNullOrEmpty(fileName)) continue;

            string path = GetFilePath(fileName);
            var data = CsvParser.Parse(path);
            _cache[fileName] = data;
        }

        // 어느 씬에서 시작하든 핵심 CSV가 항상 캐시에 존재하도록 보장
        EnsureFileLoaded("LoadingTips.csv");
        EnsureFileLoaded("LoadingVisuals.csv");
        EnsureFileLoaded("UI_Localization.csv");
        EnsureFileLoaded("Items.csv");
        EnsureFileLoaded("Minerals.csv");
        EnsureFileLoaded("Equipments.csv");
        EnsureFileLoaded("Relics.csv");
        EnsureFileLoaded("Quests.csv");
        EnsureFileLoaded("Upgrades.csv");
        EnsureFileLoaded("Dialogues.csv");
        EnsureFileLoaded("Stock_Localization.csv");
        EnsureFileLoaded("Coin_Localization.csv");
        EnsureFileLoaded("Companies.csv");
        EnsureFileLoaded("NewsChains.csv");
        EnsureFileLoaded("NewsTemplates.csv");
    }

    private void EnsureFileLoaded(string fileName)
    {
        if (!_cache.ContainsKey(fileName))
        {
            string path = GetFilePath(fileName);
            if (File.Exists(path))
            {
                var data = CsvParser.Parse(path);
                _cache[fileName] = data;
            }
        }
    }

    private string GetFilePath(string fileName)
    {
        return Path.Combine(Application.streamingAssetsPath, "Data", fileName);
    }
}
