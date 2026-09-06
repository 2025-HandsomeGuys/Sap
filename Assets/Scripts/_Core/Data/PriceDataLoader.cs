// @tags: price, loader, json, singleton, streamingassets, economy
using System.IO;
using UnityEngine;

/// <summary>
/// StreamingAssets/priceData.json 을 읽어 가격 ScriptableObject를 덮어쓴다.
///
/// Script Execution Order: -250 — 가격을 읽는 어떤 매니저보다도 먼저 돌아야 한다.
/// (ToolConfigLoader·WorldSettingsLoader가 -200이라 그 앞이 비어 있다.)
///
/// 파일이 없거나 파싱에 실패하면 경고만 남기고 에셋 값을 그대로 쓴다.
///
/// 설계: Assets/Docs/economy/price-data-json-design.md §5
/// </summary>
[DefaultExecutionOrder(-250)]
public class PriceDataLoader : MonoBehaviour
{
    public static PriceDataLoader Instance { get; private set; }

    private const string FILE_NAME = "priceData.json";

    /// <summary>광물 기저 가격표. 돌 드랍이 "비싼 광물"을 판정할 때 참조한다.</summary>
    public MineralPriceDatabase MineralPrices => mineralPrices;

    [Header("적용 대상 (인스펙터에서 물릴 것)")]
    [SerializeField] private MineralPriceDatabase mineralPrices;
    [SerializeField] private MineralDatabase      minerals;
    [SerializeField] private ShopItemDatabase     shop;
    [SerializeField] private Relic.Data.RelicDatabase relics;
    [SerializeField] private UpgradeTreeSO        upgradeTree;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        Load();
    }

    private void Load()
    {
        string path = Path.Combine(Application.streamingAssetsPath, FILE_NAME);

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[PriceDataLoader] {FILE_NAME} 없음 — 에셋에 저장된 가격을 그대로 사용한다.");
            return;
        }

        PriceData data;
        try
        {
            data = JsonUtility.FromJson<PriceData>(File.ReadAllText(path));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PriceDataLoader] 파싱 실패 — 에셋 가격을 그대로 사용한다. 오류: {e.Message}");
            return;
        }

        if (data == null)
        {
            Debug.LogError($"[PriceDataLoader] {FILE_NAME} 파싱 결과가 null — 에셋 가격을 그대로 사용한다.");
            return;
        }

        var targets = new PriceApplyTargets
        {
            mineralPrices = mineralPrices,
            minerals      = minerals,
            shop          = shop,
            relics        = relics,
            upgradeTree   = upgradeTree,
        };

        PriceApplyResult result = PriceApplier.Apply(data, targets);

        foreach (string w in result.warnings)
            Debug.LogWarning($"[PriceDataLoader] {w}");

        Debug.Log($"[PriceDataLoader] 적용 {result.applied}건, 경고 {result.warnings.Count}건.");
    }
}
