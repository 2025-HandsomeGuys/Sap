using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 장비별 스탯 수정치 정보 (Inspector에서 설정)
/// </summary>
[System.Serializable]
public class EquipmentStatModData
{
    public StatType statType;
    public ModifierType modifierType;
    public float value;
}

/// <summary>
/// 장비(머리, 옷, 신발, 유물)의 기본 정보를 담는 ScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "EquipmentData", menuName = "Game Data/Equipment SO")]
public class EquipmentSO : ScriptableObject, InterfaceInventoryItem
{
    [Header("기본 정보")]
    public EquipmentID equipmentID;
    public string equipmentNameKey;  // Localization key
    public Sprite icon;
    public string descriptionKey;    // Localization key

    [Header("착용 외형 (인벤토리 프리뷰용)")]
    [Tooltip("가방 화면의 플레이어 프리뷰에 겹쳐 그릴 스프라이트.\n" +
             "기본 플레이어 모습과 같은 캔버스 크기로 그려야 위치가 맞는다.\n" +
             "비워두면 이 장비는 프리뷰에 그려지지 않는다(아트가 준비되면 채우면 즉시 반영).")]
    public Sprite previewSprite;

    [Header("장비 타입")]
    public EquipmentType equipmentType = EquipmentType.None;

    [Header("능력치")]
    public int level;
    public float defense;
    public float durability;
    public int price;

    [Header("스탯 수정치")]
    [Tooltip("이 장비가 장착 시 적용할 스탯 수정치 목록")]
    public List<EquipmentStatModData> statModifiers = new List<EquipmentStatModData>();

    [Header("강화 테이블 (비우면 임시 테스트값 자동)")]
    [Tooltip("레벨별(1→2, 2→3…) 강화 비용·스탯 증가치. 비워두면 EquipmentUpgradeFormula가 임시 테스트 테이블을 만들어 쓰고, 강화 UI에 '⚠ 임시 테스트 값' 배지가 뜬다.")]
    public List<EquipmentUpgradeLevel> upgradeLevels = new List<EquipmentUpgradeLevel>();

    [Header("인벤토리 속성")]
    public float weight = 1f;
    public bool stackable = false;
    public int maxStackSize = 1;

    // ===== InterfaceInventoryItem 구현부 =====
    public string Id => equipmentID.ToString();
    public string DisplayName => LanguageManager.Instance?.L(equipmentNameKey) ?? equipmentNameKey;
    public Sprite Icon => icon;
    public float Weight => weight;
    public bool Stackable => stackable;
    public int MaxStackSize => maxStackSize;
    public string Description => LanguageManager.Instance?.L(descriptionKey) ?? descriptionKey;
}
