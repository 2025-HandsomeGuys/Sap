using UnityEngine;

[System.Serializable]
public enum DialogueSide 
{ 
    Left,
    Right
}

[System.Serializable]
public class DialogueLine
{
    [Header("대화 정보")]
    public string speakerNameKey;        // Localization key
    public string textKey;               // Localization key
    public DialogueSide speakerSide;

    [Header("캐릭터 이미지")]
    public Sprite leftSprite;
    public Sprite rightSprite;

    /// <summary>
    /// 현재 언어로 화자 이름 반환
    /// </summary>
    public string SpeakerName => LanguageManager.Instance?.L(speakerNameKey) ?? speakerNameKey;

    /// <summary>
    /// 현재 언어로 대화 텍스트 반환
    /// </summary>
    public string Text => LanguageManager.Instance?.L(textKey) ?? textKey;
}
