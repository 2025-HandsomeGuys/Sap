using UnityEngine;

[CreateAssetMenu(fileName = "ToolData", menuName = "Game Data/Tool SO")]
public class ToolSO : ScriptableObject
{
    [Header("기본 정보")]
    public ToolID toolID;
    public string toolNameKey;       // Localization key
    public Sprite icon;
    public string descriptionKey;    // Localization key

    [Header("능력치")]
    public int level;
    public float miningPower;
    public float miningSpeed;
    //public float durability;
    public int price;

    /// <summary>
    /// 현재 언어로 도구 이름 반환
    /// </summary>
    public string DisplayName => LanguageManager.Instance?.L(toolNameKey) ?? toolNameKey;

    /// <summary>
    /// 현재 언어로 설명 반환
    /// </summary>
    public string Description => LanguageManager.Instance?.L(descriptionKey) ?? descriptionKey;
}
