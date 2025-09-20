using UnityEngine;

[CreateAssetMenu(fileName = "New Mineral", menuName = "Game Data/Mineral SO")]
public class MineralSO : ScriptableObject, InterfaceInventoryItem
{
    [Header("기본 정보")]
    public MineralID mineralID;
    //public string id;            // 고유 ID (예: "mineral_iron")
    public string mineralName;   // 이름 (예: "철광석")
    [TextArea]
    public string description;   // 설명

    [Header("속성")]
    [Range(0, 1)]
    public float spawnRate;      // 채굴 확률 / 등장 빈도 (0~1)
    public int basementLevel;      // 채굴 가능 레벨

    [Header("인벤토리 속성")] // 변경: 인벤토리 호환을 위한 속성 추가
    public float weight = 1f;        // 변경: 무게(추가)
    public bool stackable = true;    // 변경: 광물은 기본 스택 허용(원하는 정책대로 변경)
    [Range(1, 999)]
    public int maxStackSize = 999;   // 변경: 광물 최대 스택(정책에 맞게)

    [Header("UI & 월드")]
    public Sprite icon;
    public GameObject mineralPrefab;

    // ===== IInventoryItem 구현부 =====
    public string Id => mineralID.ToString();     // 변경: 공통 ID로 노출
    public string DisplayName => mineralName;     // 변경: 공통 표시 이름
    public Sprite Icon => icon;                   // 변경: 공통 아이콘
    public float Weight => weight;                // 변경: 공통 무게
    public bool Stackable => stackable;           // 변경: 공통 스택 가능 여부
    public int MaxStackSize => maxStackSize;      // 변경: 공통 최대 스택
}
