using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class LocalizedTextUI : MonoBehaviour
{
    [Header("Localization Key")]
    [Tooltip("CSV 데이터의 key 값을 입력합니다.")]
    public string localizationKey;

    private TextMeshProUGUI _textComponent;

    private void Awake()
    {
        _textComponent = GetComponent<TextMeshProUGUI>();
    }

    private bool _isSubscribed = false;

    private void Start()
    {
        TrySubscribe();
        UpdateText();
    }

    private void OnEnable()
    {
        TrySubscribe();
        UpdateText();
    }

    private void OnDisable()
    {
        if (_isSubscribed && LanguageManager.Instance != null)
        {
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChangedEvent;
            _isSubscribed = false;
        }
    }

    private void TrySubscribe()
    {
        if (!_isSubscribed && LanguageManager.Instance != null)
        {
            LanguageManager.Instance.OnLanguageChanged += OnLanguageChangedEvent;
            _isSubscribed = true;
        }
    }

    private void OnLanguageChangedEvent(LanguageType newLanguage)
    {
        UpdateText();
    }

    public void UpdateText()
    {
        if (_textComponent == null) return;

        string resultString = "";

        if (!string.IsNullOrEmpty(localizationKey) && LanguageManager.Instance != null)
        {
            resultString = LanguageManager.Instance.L(localizationKey);
        }

        _textComponent.text = resultString;

        // 현재 언어에 맞는 폰트 적용
        if (LanguageManager.Instance != null)
        {
            TMP_FontAsset currentFont = LanguageManager.Instance.GetCurrentFont();
            if (currentFont != null)
            {
                _textComponent.font = currentFont;
            }
        }
    }
}
