using UnityEngine;

// CreateAssetMenu 속성을 사용하면 유니티 에디터의 Assets/Create 메뉴에서 쉽게 아이템 에셋을 생성할 수 있습니다.
[CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
public class Item : ScriptableObject
{
    [Header("기본 정보")]
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
    public GameObject itemPrefab; // 월드에 떨어졌을 때 생성될 프리팹
}
