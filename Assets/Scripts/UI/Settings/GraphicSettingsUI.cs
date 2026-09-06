using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class GraphicSettingsUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private TMP_Dropdown frameRateDropdown;
    
    [Header("Language Setting (Optional Bind)")]
    [SerializeField] private TMP_Dropdown languageDropdown;

    private List<int> frameRateOptions = new List<int> { 30, 60, 120, 144, 240, -1 }; // -1 is Unlimited

    private void Start()
    {
        InitializeUI();
    }

    private void OnEnable()
    {
        if (GraphicSettingsManager.Instance != null)
        {
            SyncWithManager();
        }
    }

    private void InitializeUI()
    {
        if (GraphicSettingsManager.Instance == null) return;

        // 1. Resolution Dropdown
        resolutionDropdown.ClearOptions();
        List<string> resOptions = new List<string>();
        foreach (var res in GraphicSettingsManager.Instance.SupportResolutions)
        {
            resOptions.Add(res.ToString());
        }
        resolutionDropdown.AddOptions(resOptions);
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        // 2. Fullscreen Toggle
        fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);

        // 3. FrameRate Dropdown
        frameRateDropdown.ClearOptions();
        List<string> fpsOptions = new List<string>();
        foreach (int fps in frameRateOptions)
        {
            fpsOptions.Add(fps == -1 ? "Unlimited" : $"{fps} FPS");
        }
        frameRateDropdown.AddOptions(fpsOptions);
        frameRateDropdown.onValueChanged.AddListener(OnFrameRateChanged);

        // 4. Language Dropdown (if assigned)
        if (languageDropdown != null && LanguageManager.Instance != null)
        {
            languageDropdown.ClearOptions();
            List<string> langOptions = new List<string> { "한국어", "English", "中文(简体)" };
            languageDropdown.AddOptions(langOptions);
            
            // Remove previous listeners to prevent duplicates
            languageDropdown.onValueChanged.RemoveAllListeners();
            languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
        }

        SyncWithManager();
    }

    private void SyncWithManager()
    {
        var manager = GraphicSettingsManager.Instance;

        // Sync Resolution
        resolutionDropdown.value = manager.GetCurrentResolutionIndex();

        // Sync Fullscreen
        fullscreenToggle.isOn = manager.GetIsFullscreen();

        // Sync FrameRate
        int currentLimit = manager.GetCurrentFrameRate();
        int fpsIndex = frameRateOptions.IndexOf(currentLimit);
        if (fpsIndex != -1)
        {
            frameRateDropdown.value = fpsIndex;
        }

        // Sync Language (Temporarily removing listener to prevent infinite loop/immediate trigger)
        if (languageDropdown != null && LanguageManager.Instance != null)
        {
            languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
            languageDropdown.value = (int)LanguageManager.Instance.CurrentLanguage;
            languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
        }
    }

    private void OnResolutionChanged(int index)
    {
        GraphicSettingsManager.Instance.SetResolution(index, fullscreenToggle.isOn);
    }

    private void OnFullscreenChanged(bool isFullscreen)
    {
        GraphicSettingsManager.Instance.SetResolution(resolutionDropdown.value, isFullscreen);
    }

    private void OnFrameRateChanged(int index)
    {
        int limit = frameRateOptions[index];
        GraphicSettingsManager.Instance.SetFrameRate(limit);
    }

    private void OnLanguageChanged(int index)
    {
        if (LanguageManager.Instance != null)
        {
            LanguageManager.Instance.SetLanguage(index);
        }
    }
}
