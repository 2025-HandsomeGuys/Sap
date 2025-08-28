using UnityEngine;

public class Mineable : MonoBehaviour
{
    [Tooltip("이 아이템의 고유 ID를 선택하세요.")]
    public ItemID itemID;

    public Item itemData { get; private set; }

    private void Awake()
    {
        if (itemID == ItemID.None)
        {
            Debug.LogError("ItemID가 'None'으로 설정되었습니다. 프리팹을 확인해주세요.", this.gameObject);
            return;
        }

        // 데이터베이스에서 아이템 ID로 실제 아이템 데이터를 찾아 할당합니다.
        itemData = ItemDatabase.Instance.GetItemByID(itemID);

        if (itemData == null)
        {
            Debug.LogError($"'{itemID}'에 해당하는 아이템을 ItemDatabase에서 찾을 수 없습니다. ItemDatabase 에셋에 해당 아이템이 등록되었는지, 그리고 Item 에셋에 ItemID가 올바르게 설정되었는지 확인해주세요.", this.gameObject);
        }
    }
}
