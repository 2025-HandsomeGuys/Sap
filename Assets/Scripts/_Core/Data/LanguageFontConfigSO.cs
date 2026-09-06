// @tags: language, font, localization, scriptable-object, so, ui
using UnityEngine;
using TMPro;

[CreateAssetMenu(fileName = "LanguageFontConfig", menuName = "ScriptableObjects/LanguageFontConfig")]
public class LanguageFontConfigSO : ScriptableObject
{
    [System.Serializable]
    public struct FontMapping
    {
        public LanguageType language;
        public TMP_FontAsset fontAsset;
    }

    [Header("언어별 폰트 매핑")]
    public FontMapping[] fontMappings;

    /// <summary>
    /// 지정된 언어에 해당하는 폰트를 반환합니다.
    /// </summary>
    public TMP_FontAsset GetFont(LanguageType language)
    {
        if (fontMappings == null) return null;

        foreach (var mapping in fontMappings)
        {
            if (mapping.language == language)
            {
                return mapping.fontAsset;
            }
        }
        
        return null;
    }
}
