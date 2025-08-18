using UnityEngine;

// CreateAssetMenu 속성을 사용하면 유니티 에디터의 Assets/Create 메뉴에서 쉽게 아이템 에셋을 생성할 수 있습니다.
[CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
public class Item : ScriptableObject
{
    [Header("기본 정보")]
    public string itemName = "새 아이템";
    public Sprite icon = null;
    public float weight = 1f;

    [Header("스택 관련 정보")]
    public bool stackable = false; // 아이템을 겹칠 수 있는지 여부
    [Range(1, 999)]
    public int maxStackSize = 1; // 최대 몇 개까지 겹칠 수 있는지
}
