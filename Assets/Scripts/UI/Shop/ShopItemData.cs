using UnityEngine;

[System.Serializable]
public class ShopItemData
{
    [Header("아이템 정보")]
    public ShopItemType itemType = ShopItemType.Item;
    public ItemID itemID; // 판매할 아이템 ID
    public EquipmentID equipmentID; // 판매할 장비 ID
    public Relic.Data.RelicID relicID; // 판매할 유물 ID
    [Header("⚠ 런타임에 priceData.json이 덮어씀")]
    public int price; // 가격
    
    [Header("판매 설정")]
    public bool isAvailable = true; // 현재 판매 가능한지 여부
    public int stock = -1; // 재고 (-1이면 무제한)

    [Header("업그레이드 해금")]
    [Tooltip("이 항목을 상점에 여는 업그레이드 노드 ID. 비워두면 처음부터 판매한다.\n" +
             "트리의 '모이는 노드'(부모가 여럿인 관문)를 쓴다 — 예: PickaxeUnlock_T0_01(곡괭이 입수).\n" +
             "판정은 ShopUnlockGate.")]
    public string unlockNodeId = "";
}










