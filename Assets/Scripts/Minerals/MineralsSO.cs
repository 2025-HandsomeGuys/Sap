using UnityEngine;

[CreateAssetMenu(fileName = "New Mineral", menuName = "Game Data/Mineral SO")]
public class MineralSO : ScriptableObject
{
    [Header("기본 정보")]
    public string id;            // 고유 ID (예: "mineral_iron")
    public string mineralName;   // 이름 (예: "철광석")
    [TextArea]
    public string description;   // 설명

    [Header("속성")]
    [Range(0, 1)]
    public float spawnRate;      // 채굴 확률 / 등장 빈도 (0~1)
    public int miningLevel;      // 채굴 가능 레벨
    public float hardness;       // 채굴 난이도

    [Header("UI & 월드")]
    public Sprite icon;          // 인벤토리 아이콘
    public GameObject mineralPrefab; // 월드에 놓일 때 사용할 Prefab
}
