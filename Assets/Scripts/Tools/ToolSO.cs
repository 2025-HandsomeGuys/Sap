using UnityEngine;

[CreateAssetMenu(fileName = "ToolData", menuName = "Game Data/Tool SO")]
public class ToolSO : ScriptableObject, InterfaceInventoryItem
{
    [Header("기본 정보")]
    public ToolID toolID;        // 도구 ID
    public string toolName;       // 이름 (예: "철 곡괭이")
    public Sprite icon;
    [TextArea]
    public string description;   // 설명 (예: "채굴에 사용되는 기본 곡괭이")

    [Header("능력치")]
    public int level;            // 도구 레벨
    public float miningPower;    // 채굴력 (예: 채굴 가능 속도)
    public float miningSpeed;    // 채굴 속도
    //public float durability;     // 내구도
    public int price;            // 구매/판매시 가격

    [Header("인벤토리 속성")]
    public float weight = 0f;        // 도구는 무게 없음
    public bool stackable = false;   // 도구는 스택 불가
    public int maxStackSize = 1;     // 도구는 항상 1개

    // ===== InterfaceInventoryItem 구현부 =====
    public string Id => toolID.ToString();
    public string DisplayName => toolName;
    public Sprite Icon => icon;
    public float Weight => weight;
    public bool Stackable => stackable;
    public int MaxStackSize => maxStackSize;
}
