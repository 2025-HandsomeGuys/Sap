using UnityEngine;

// CreateAssetMenu 속성을 사용하면 유니티 에디터의 Assets/Create 메뉴에서 쉽게 아이템 에셋을 생성할 수 있습니다.
[CreateAssetMenu(fileName = "New Item", menuName = "Game Data/Item SO")]
public class ItemSO : ScriptableObject, InterfaceInventoryItem
{
    [Header("기본 정보")]
    public MineralID itemID; // 아이템을 식별하기 위한 고유 ID
    public string itemName = "새 아이템";
    [TextArea(3, 10)] // 인스펙터에서 여러 줄로 편집할 수 있도록 설정
    public string description = "아이템 설명";
    public Sprite icon = null;
    public float weight = 1f;

    [Header("효과")]
    public float staminaReduction = 0f; // 이 아이템을 획득했을 때 감소할 최대 스태미나 양

    [Header("스택 관련 정보")]
    public bool stackable = false; // 아이템을 겹칠 수 있는지 여부
    [Range(1, 999)]
    public int maxStackSize = 1; // 최대 몇 개까지 겹칠 수 있는지

    [Header("월드 프리팹")]
    public MineralID poolType; // 이 아이템이 ObjectPooler에서 사용하는 타입
    public GameObject itemPrefab; // 월드에 떨어졌을 때 생성될 프리팹

    public string Id => itemID.ToString();        // 변경: 공통 ID로 노출
    public string DisplayName => itemName;        // 변경: 공통 표시 이름
    public Sprite Icon => icon;                   // 변경: 공통 아이콘
    public float Weight => weight;                // 변경: 공통 무게
    public bool Stackable => stackable;           // 변경: 공통 스택 가능 여부
    public int MaxStackSize => maxStackSize;      // 변경: 공통 최대 스택
}
