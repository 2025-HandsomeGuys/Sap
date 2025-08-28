using UnityEngine;

[CreateAssetMenu(fileName = "ToolData", menuName = "Game Data/Tool SO")]
public class ToolSO : ScriptableObject
{
    [Header("기본 정보")]
    public string id;            // 고유 ID (예: "tool_001")
    public string toolName;      // 이름 (예: "철 곡괭이")
    public Sprite icon;
    [TextArea]
    public string description;   // 설명 (예: "철로 만든 기본 곡괭이")

    [Header("능력치")]
    public int level;            // 도구 레벨
    public float miningPower;    // 채굴력 (예: 블록 깨는 속도)
    public float miningSpeed;    // 채굴 속도
    //public float durability;     // 내구도
    public int price;            // 구매/업그레이드 비용
}
