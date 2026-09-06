using UnityEngine;

[CreateAssetMenu(fileName = "New Mineral", menuName = "Game Data/Mineral SO")]
public class MineralSO : ScriptableObject, InterfaceInventoryItem
{
    [Header("기본 정보")]
    public MineralID mineralID;
    public string mineralNameKey;    // Localization key
    public string descriptionKey;    // Localization key

    [Header("속성")]
    [Range(0, 1)]
    public float spawnRate;
    public int basementLevel;

    [Header("인벤토리 속성")]
    [Header("⚠ 런타임에 priceData.json이 덮어씀")]
    public float weight = 1f;
    public bool stackable = true;
    // 스택 상한은 게임 전역 규칙으로 고정한다(광물 = 10개). 에셋별 값을 두지 않는다.
    public const int MineralMaxStackSize = 10;

    [Header("UI & 월드")]
    public Sprite icon;
    public GameObject mineralPrefab;

    // ===== IInventoryItem 구현부 =====
    public string Id => mineralID.ToString();
    public string DisplayName => LanguageManager.Instance?.L(mineralNameKey) ?? mineralNameKey;
    public Sprite Icon => icon;
    public float Weight => weight;
    public bool Stackable => stackable;
    public int MaxStackSize => MineralMaxStackSize;
    public string Description => LanguageManager.Instance?.L(descriptionKey) ?? descriptionKey;
}
