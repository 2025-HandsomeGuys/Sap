// @tags: language, localization, manager, singleton, ui, font
using System;
using UnityEngine;
using TMPro;

public enum LanguageType
{
    Korean,
    English,
    Chinese
}

public class LanguageManager : MonoBehaviour
{
    public static LanguageManager Instance { get; private set; }

    public event Action<LanguageType> OnLanguageChanged;

    [Header("폰트 매핑 설정")]
    [Tooltip("언어별 폰트를 지정한 ScriptableObject")]
    public LanguageFontConfigSO fontConfig;

    [Header("Localization CSV 파일 목록")]
    [Tooltip("DataSheetCache에 등록된 CSV 파일명 중 Localization용 파일들")]
    public string[] localizationCsvFiles;

    private const string LANGUAGE_KEY = "Settings_Language";

    private LanguageType _currentLanguage = LanguageType.Korean;
    public LanguageType CurrentLanguage 
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnLanguageChanged?.Invoke(_currentLanguage);
            }
        }
    }

    private LocalizationProvider _localization = new LocalizationProvider();

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

        LoadLanguage();
        InitializeLocalization();
    }

    private void Start()
    {
        // Awake에서 DataSheetCache가 아직 없었을 경우 재시도
        if (_localization.Count == 0)
        {
            InitializeLocalization();
        }
    }

    private void LoadLanguage()
    {
        int langIndex = PlayerPrefs.GetInt(LANGUAGE_KEY, (int)LanguageType.Korean);
        
        if (langIndex < 0 || langIndex >= Enum.GetValues(typeof(LanguageType)).Length)
        {
            langIndex = (int)LanguageType.Korean;
        }

        _currentLanguage = (LanguageType)langIndex;
    }

    /// <summary>
    /// DataSheetCache에서 Localization CSV 데이터를 로드하여 LocalizationProvider 초기화
    /// </summary>
    public void InitializeLocalization()
    {
        var cache = DataSheetCache.Instance;
        if (cache == null)
        {
            Debug.LogWarning("[LanguageManager] DataSheetCache가 아직 초기화되지 않았습니다.");
            return;
        }

        if (localizationCsvFiles == null || localizationCsvFiles.Length == 0)
        {
            Debug.LogWarning("[LanguageManager] Localization CSV 파일이 지정되지 않았습니다.");
            return;
        }

        var sheets = new System.Collections.Generic.List<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>>();
        
        // Ensure critical localization files are included even if not in the inspector list
        bool hasTips = false;
        bool hasVisuals = false;
        bool hasMainUI = false;
        foreach (string fileName in localizationCsvFiles)
        {
            if (fileName == "LoadingTips.csv") hasTips = true;
            if (fileName == "LoadingVisuals.csv") hasVisuals = true;
            if (fileName == "UI_Localization.csv") hasMainUI = true;
            if (string.IsNullOrEmpty(fileName)) continue;
            sheets.Add(cache.GetSheet(fileName));
        }

        if (!hasTips) AddSheetIfMissing(sheets, cache, "LoadingTips.csv", ref hasTips);
        if (!hasVisuals) AddSheetIfMissing(sheets, cache, "LoadingVisuals.csv", ref hasVisuals);
        if (!hasMainUI) AddSheetIfMissing(sheets, cache, "UI_Localization.csv", ref hasMainUI);
        
        bool hasStock = false;
        AddSheetIfMissing(sheets, cache, "Stock_Localization.csv", ref hasStock);

        bool hasCoin = false;
        AddSheetIfMissing(sheets, cache, "Coin_Localization.csv", ref hasCoin);

        bool hasRelics = false;
        AddSheetIfMissing(sheets, cache, "Relics.csv", ref hasRelics);

        _localization.Load(sheets.ToArray());

        // 로컬라이제이션이 (재)로드되면 이미 배치된 LocalizedTextUI들이 키를 다시 당겨오도록 알린다.
        // 콜드 스타트(메인메뉴 등)에서 LocalizedTextUI.Start가 이 로드보다 먼저 돌면
        // 번역 대신 키가 그대로 남는데, OnEnable에서 이미 이 이벤트를 구독해 두었으므로
        // 여기서 한 번 쏘면 그 자리에서 올바른 언어로 다시 채워진다.
        OnLanguageChanged?.Invoke(_currentLanguage);
    }

    private void AddSheetIfMissing(System.Collections.Generic.List<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>> sheets, DataSheetCache cache, string fileName, ref bool foundFlag)
    {
        var sheet = cache.GetSheet(fileName);
        if (sheet != null && sheet.Count > 0)
        {
            sheets.Add(sheet);
            foundFlag = true;
        }
    }

    public void SetLanguage(int index)
    {
        if (index < 0 || index >= Enum.GetValues(typeof(LanguageType)).Length) return;

        LanguageType newLanguage = (LanguageType)index;
        
        PlayerPrefs.SetInt(LANGUAGE_KEY, index);
        PlayerPrefs.Save();

        CurrentLanguage = newLanguage;
        Debug.Log($"[LanguageManager] Language set to: {CurrentLanguage}");
    }

    /// <summary>
    /// 현재 언어로 Localization key에 해당하는 텍스트 반환
    /// </summary>
    public string L(string key)
    {
        return _localization.Get(key, _currentLanguage);
    }

    /// <summary>
    /// 현재 언어로 포맷팅된 텍스트 반환
    /// </summary>
    public string LF(string key, params object[] args)
    {
        return _localization.GetFormat(key, _currentLanguage, args);
    }

    /// <summary>
    /// 특정 접두사로 시작하는 키들 중 하나를 랜덤하게 선택하여 텍스트 반환
    /// </summary>
    public string GetRandomTextByPrefix(string prefix)
    {
        if (_localization == null || _localization.Count == 0)
        {
            InitializeLocalization();
        }

        var keys = _localization.GetKeysByPrefix(prefix);
        if (keys == null || keys.Count == 0) 
        {
            Debug.LogWarning($"[LanguageManager] '{prefix}'로 시작하는 키를 찾을 수 없습니다. (총 {_localization.Count}개 키 로드됨)");
            return "";
        }

        int randomIndex = UnityEngine.Random.Range(0, keys.Count);
        return L(keys[randomIndex]);
    }

    /// <summary>
    /// 특정 접두사로 시작하는 모든 키 목록을 반환합니다.
    /// </summary>
    public System.Collections.Generic.List<string> GetKeysByPrefix(string prefix)
    {
        if (_localization == null || _localization.Count == 0)
        {
            InitializeLocalization();
        }
        return _localization.GetKeysByPrefix(prefix);
    }

    /// <summary>
    /// 현재 설정된 언어에 맞는 폰트 애셋을 반환합니다.
    /// </summary>
    public TMP_FontAsset GetCurrentFont()
    {
        if (fontConfig != null)
        {
            return fontConfig.GetFont(_currentLanguage);
        }
        return null;
    }
}
