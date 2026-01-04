using UnityEngine;

[System.Serializable]
public class ShopItemData
{
    [Header("아이템 정보")]
    public ItemID itemID; // 판매할 아이템 ID
    public int price; // 가격
    
    [Header("판매 설정")]
    public bool isAvailable = true; // 현재 판매 가능한지 여부
    public int stock = -1; // 재고 (-1이면 무제한)
}










