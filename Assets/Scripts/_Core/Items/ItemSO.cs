// @tags: item, scriptable-object, so, inventory, buff
using System;
using System.Collections.Generic;
using UnityEngine;

public enum ItemActiveType
{
    None,
    Instant,
    Buff,
    Hybrid,
    Utility
}

public enum ItemActiveEffect
{
    RestoreStamina,
    StaminaRegenBoost,
    MaxStaminaBoost,
    FrostResist,
    BurnResist,
    AllEnvResist,
    ChargeTimeReduce,
    RestoreDrillBattery,
    ReturnToBase,
    RestoreMaxStaminaLoss,
    RareMineralDetection,
    // 방사선 전용 회복. RestoreMaxStaminaLoss(부상·화상·동상)로는 방사선이 빠지지 않는다.
    // 기존 에셋의 직렬화 값이 밀리지 않도록 항상 끝에 추가할 것.
    RestoreRadiation,

    // 벽타기 스태미나 소모 절감. StaminaCostPerSecond에 곱연산으로 붙으므로
    // value는 배율(0.5 = 절반)이고 effects의 modifierType을 Percent로 둬야 뜻대로 동작한다.
    ClimbStaminaReduce
}

[Serializable]
public struct ItemEffectData
{
    public ItemActiveEffect effect;
    public float value;
    public float duration; // Buff용

    // 버프가 스탯에 붙는 방식. 기본값이 Flat(=0)이라 이 필드가 없던 기존 에셋도
    // 역직렬화 후 그대로 Flat으로 읽힌다.
    // 배율 스탯(StaminaCostPerSecond 등)에 붙일 때만 Percent로 둔다.
    public ModifierType modifierType;
}

[CreateAssetMenu(fileName = "New Item", menuName = "Game Data/Item SO")]
public class ItemSO : ScriptableObject, InterfaceInventoryItem
{
    [Header("기본 정보")]
    public ItemID itemID;
    public string itemNameKey;       // Localization key
    [TextArea(3, 10)]
    public string descriptionKey;    // Localization key
    public Sprite icon = null;
    public float weight = 1f;

    [Header("효과")]
    public float staminaReduction = 0f;

    [Header("Active Settings")]
    public ItemActiveType activeType = ItemActiveType.None;
    public List<ItemEffectData> effects = new List<ItemEffectData>();
    public float castTime = 0f;

    [Header("스택 관련 정보")]
    public bool stackable = false;
    // 스택 상한은 게임 전역 규칙으로 고정한다(아이템 = 3개). 에셋별 값을 두지 않는다.
    public const int ItemMaxStackSize = 3;

    [Header("월드 프리팹")]
    public ItemID poolType;
    public GameObject itemPrefab;

    public string Id => itemID.ToString();
    public string DisplayName => LanguageManager.Instance?.L(itemNameKey) ?? itemNameKey;
    public Sprite Icon => icon;
    public float Weight => weight;
    public bool Stackable => stackable;
    public int MaxStackSize => ItemMaxStackSize;
    public string Description => LanguageManager.Instance?.L(descriptionKey) ?? descriptionKey;
}
