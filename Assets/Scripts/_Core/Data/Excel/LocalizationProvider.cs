// @tags: localization, language, csv, excel, provider, ui, text
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CSV 데이터로부터 key → 언어별 텍스트를 관리하는 Localization 제공자.
/// 여러 CSV의 데이터를 하나로 병합합니다.
/// </summary>
public class LocalizationProvider
{
    // key → { "kr" → "한국어", "en" → "English", "cn" → "中文" }
    private Dictionary<string, Dictionary<string, string>> _table
        = new Dictionary<string, Dictionary<string, string>>();

    // LanguageType → CSV 열 이름 매핑
    private static readonly Dictionary<LanguageType, string> _langColumnMap = new Dictionary<LanguageType, string>
    {
        { LanguageType.Korean, "kr" },
        { LanguageType.English, "en" },
        { LanguageType.Chinese, "cn" }
    };

    /// <summary>
    /// 여러 CSV 시트 데이터를 병합하여 로드
    /// </summary>
    public void Load(params List<Dictionary<string, string>>[] sheets)
    {
        _table.Clear();

        foreach (var sheet in sheets)
        {
            if (sheet == null) continue;

            foreach (var row in sheet)
            {
                if (!row.TryGetValue("key", out string key)) continue;
                if (string.IsNullOrEmpty(key)) continue;

                if (_table.ContainsKey(key))
                {
                    Debug.LogWarning($"[LocalizationProvider] 중복 key 발견: '{key}' (덮어쓰기)");
                }

                var langMap = new Dictionary<string, string>();
                foreach (var kvp in row)
                {
                    if (kvp.Key == "key") continue;
                    langMap[kvp.Key.ToLower()] = kvp.Value;
                }
                _table[key] = langMap;
            }
        }
    }

    /// <summary>
    /// 현재 언어로 텍스트 반환. 폴백: 한국어 → key
    /// </summary>
    public string Get(string key, LanguageType language)
    {
        if (string.IsNullOrEmpty(key)) return "";

        if (!_table.TryGetValue(key, out var langMap))
        {
            // key 자체를 반환 (디버깅 시 누락된 key 확인 가능)
            return key;
        }

        string langCol = GetLangColumn(language);

        // 1순위: 요청 언어
        if (langMap.TryGetValue(langCol, out string value) && !string.IsNullOrEmpty(value))
            return value;

        // 2순위: 한국어 폴백
        if (langCol != "kr" && langMap.TryGetValue("kr", out string krValue) && !string.IsNullOrEmpty(krValue))
            return krValue;

        // 3순위: key 반환
        return key;
    }

    /// <summary>
    /// 포맷팅 지원 텍스트 반환
    /// </summary>
    public string GetFormat(string key, LanguageType language, params object[] args)
    {
        string template = Get(key, language);
        try
        {
            return string.Format(template, args);
        }
        catch (System.FormatException e)
        {
            Debug.LogWarning($"[LocalizationProvider] 포맷 에러 (key: '{key}'): {e.Message}");
            return template;
        }
    }

    /// <summary>
    /// key 존재 여부 확인
    /// </summary>
    public bool HasKey(string key)
    {
        return !string.IsNullOrEmpty(key) && _table.ContainsKey(key);
    }

    /// <summary>
    /// 로드된 전체 key 수 반환
    /// </summary>
    public int Count => _table.Count;

    /// <summary>
    /// 특정 접두사로 시작하는 모든 키 목록 반환
    /// </summary>
    public List<string> GetKeysByPrefix(string prefix)
    {
        var result = new List<string>();
        foreach (var key in _table.Keys)
        {
            if (key.StartsWith(prefix))
            {
                result.Add(key);
            }
        }
        return result;
    }

    private string GetLangColumn(LanguageType language)
    {
        if (_langColumnMap.TryGetValue(language, out string col))
            return col;
        return "kr";
    }
}
