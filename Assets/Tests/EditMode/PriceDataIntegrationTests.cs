// @tags: test, editmode, price, json, integration, economy, real-assets
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Relic.Data;
using UnityEditor;
using UnityEngine;

/// <summary>
/// PriceApplier.Apply()를 "실제 프로젝트 에셋 + 실제 priceData.json"으로 돌려
/// JSON id가 실제 에셋과 매칭되는지를 검증한다.
///
/// 기존 테스트 두 개는 이 공백을 안 본다.
///   - PriceApplierTests   : 합성 ScriptableObject로 적용 로직만 검증 (실제 에셋 안 봄)
///   - MineralBalanceTests / ShopPriceTests : priceData.json을 직접 읽어 밸런스만 검증
///     (로더가 그 값을 실제 에셋에 꽂는지는 안 봄)
/// 즉 JSON에 실제 에셋과 매칭되지 않는 id가 있어도 지금까지는 아무 테스트도 못 잡고
/// 플레이 모드 경고 로그로만 드러났다. 이 파일이 그 공백을 메운다.
///
/// ⚠ priceData.json은 사람이 Tools/Economy/Export Prices to JSON 을 돌려야 생긴다
/// (Assets/Scripts/Editor/PriceDataExporter.cs). **아직 실행 전이라 지금은 이 파일의
/// 모든 테스트가 "priceData.json 없음"으로 실패한다 — 정상이다.** 툴을 실행하면 통과한다.
///
/// ── 실제 에셋 보호 ──────────────────────────────────────────────────────────
/// AssetDatabase로 5개 에셋을 로드하되, PriceApplier.Apply()는 그 원본 위에 직접 돌리지
/// 않는다. Object.Instantiate로 깊은 복사본을 만들어 그 위에 적용한다(LoadClonedRealTargets
/// 참고). 적용 결과(경고·개수)는 원본 에셋의 실제 데이터를 그대로 반영하므로 검증 목적은
/// 달라지지 않는다.
///
/// 이 방식을 고른 이유(대안: 적용 전 값을 백업했다가 [TearDown]에서 되돌리기):
///   - 적용 대상이 143건이나 된다. 필드 하나라도 백업 목록에서 빠뜨리거나, 테스트 도중
///     예외가 나서 TearDown 이전에 흐름이 끊기면 실제 에셋이 조용히 오염된 채로 남는다.
///     이 프로젝트는 UVCS라 "누가 언제 왜 이 값을 바꿨는지"가 diff 밖에서는 안 보인다.
///   - MineralPriceDatabase는 특히 값 저장소가 private `_priceMap`이라 GetPrice()로만
///     조회 가능하고 백업/복원 자체가 불가능하다. (참고: ApplyPriceOverrides는 그 private
///     맵만 바꾸고 직렬화되는 `prices` 리스트는 건드리지 않으므로, 설령 실제 에셋에 직접
///     적용하더라도 다음 도메인 리로드 후에는 맵이 `prices`에서 다시 구워져 원래 값으로
///     돌아온다 — 그러나 "같은 세션 안"에서는 오염된 채로 남고, 그 사이 다른 테스트나
///     사람이 GetPrice()를 호출하면 잘못된 값을 보게 된다. 그래서 이 사실에 기대지 않고
///     복제본을 쓴다.)
///
/// Object.Instantiate(ScriptableObject)는 Unity 직렬화 기반 복사라 임베디드 값
/// (RelicSO.upgradeCosts 구조체 배열, ShopItemDatabase.shopItems 리스트 등)은 원본과
/// 완전히 분리된다. 다만 **다른 에셋을 참조하는 리스트**(MineralDatabase.allMinerals,
/// RelicDatabase.allRelics, UpgradeTreeSO.allNodes)는 얕게 복사되어 원본 SO 에셋
/// 인스턴스를 그대로 가리키므로, 그 리스트에 들어가는 개별 SO도 하나씩 Instantiate해서
/// 복제 리스트로 바꿔치기한다. 이렇게 하면 실제 에셋은 한 바이트도 안 바뀐다 — TearDown은
/// 이 테스트가 만든 런타임 복제 인스턴스만 DestroyImmediate 한다.
///
/// ── 정적 싱글턴 보호 ────────────────────────────────────────────────────────
/// MineralDatabase·RelicDatabase는 OnEnable에서 static Instance를 자기 자신으로 굳힌다
/// (CLAUDE.md 참고). 복제본을 CreateInstance/Instantiate하면 그 OnEnable이 돌면서 이
/// 테스트가 만든 복제본이 전역 싱글턴 자리를 가로챌 수 있고, TearDown에서 복제본을
/// DestroyImmediate 하면 싱글턴이 파괴된 오브젝트를 가리키는 댕글링 참조로 남는다.
/// 그래서 SetUp에서 원래 싱글턴 값을 스냅샷해 두고, TearDown에서 리플렉션으로 되돌린다
/// (둘 다 private set이라 공개 API로는 복원할 수 없다).
/// </summary>
public class PriceDataIntegrationTests
{
    private const string MineralPriceDbPath = "Assets/GameData/ShopData/MineralPriceDatabase.asset";
    private const string ShopDbPath         = "Assets/GameData/ShopData/ShopItemDatabase.asset";
    private const string MineralDbPath      = "Assets/Resources/MineralDatabase.asset";
    private const string RelicDbPath        = "Assets/GameData/Relics/RelicDatabase.asset";
    private const string UpgradeTreePath    = "Assets/GameData/UpgradeData/_UpgradeTree.asset";

    // 설계 문서 §3.1 — 절별 항목 수
    private const int ExpectedMinerals      = 23;
    private const int ExpectedShopItems     = 2;
    private const int ExpectedShopEquipment = 28;
    private const int ExpectedShopRelics    = 28;
    private const int ExpectedRelicUpgrades = 27;
    private const int ExpectedUpgradeNodes  = 35;

    // ── applied 기대값 143의 계산 근거 ──────────────────────────────────────
    // PriceApplier.cs를 읽어 `r.applied++`가 실제로 도는 지점만 센 것이다 (경고로 끝나는
    // 항목은 세지 않는다). 광물 "가격"은 applied를 올리지 않는다는 점이 핵심 함정이다.
    //
    //   ApplyMinerals(45행)       : 광물마다 무게(so.weight) 적용 성공 시에만 1회 (78행).
    //                                가격은 같은 루프에서 priceOverrides 딕셔너리에 모았다가
    //                                루프 밖에서 t.mineralPrices.ApplyPriceOverrides()를
    //                                한 번 호출할 뿐 applied를 올리지 않는다 (81~88행).
    //                                → 광물 23종 전부 매칭되면 23
    //   ApplyShop(91행)           : Item/Equipment/Relic 세 종류로 3번 호출되고, 상점 목록에
    //                                행이 매칭될 때마다 1회 (130행) → 2 + 28 + 28 = 58
    //   ApplyRelicUpgrades(134행) : 유물마다 gold 배열 길이가 맞아 적용될 때 1회 (172행)
    //                                → 27
    //   ApplyNodes(176행)         : 노드마다 cost 적용 성공 시 1회 (196행) → 35
    //
    //   합계: 23 + 58 + 27 + 35 = 143
    //       = (설계 §3.1) 광물 23 + 소모품 2 + 장비 28 + 유물 28 + 유물강화 27 + 노드 35
    private const int ExpectedApplied = 143;

    private readonly List<Object> _created = new List<Object>();

    private static readonly FieldInfo MineralDbInstanceField =
        typeof(MineralDatabase).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly PropertyInfo RelicDbInstanceProperty =
        typeof(RelicDatabase).GetProperty(nameof(RelicDatabase.Instance), BindingFlags.Public | BindingFlags.Static);

    private object _originalMineralDbInstance;
    private object _originalRelicDbInstance;

    [SetUp]
    public void SetUp()
    {
        // MineralDatabase._instance는 private 필드라 리플렉션으로 직접 읽는다 — 공개 getter
        // (MineralDatabase.Instance)를 거치면 null일 때 Resources.Load를 트리거하는 side
        // effect가 있어 피한다. RelicDatabase.Instance는 getter가 필드를 그대로 반환할 뿐이라
        // 공개 getter를 그대로 쓴다.
        _originalMineralDbInstance = MineralDbInstanceField.GetValue(null);
        _originalRelicDbInstance = RelicDatabase.Instance;
    }

    private T CloneAsset<T>(T original) where T : Object
    {
        var clone = Object.Instantiate(original);
        _created.Add(clone);
        return clone;
    }

    [TearDown]
    public void TearDown()
    {
        // 실제 에셋은 건드리지 않았으므로, 여기서 파괴하는 건 전부 이 테스트가 만든
        // 런타임 복제본뿐이다.
        foreach (var o in _created)
            if (o != null) Object.DestroyImmediate(o);
        _created.Clear();

        // OnEnable이 가로챘을 수 있는 정적 싱글턴을 원래 값으로 되돌린다.
        MineralDbInstanceField.SetValue(null, _originalMineralDbInstance);
        RelicDbInstanceProperty.SetValue(null, _originalRelicDbInstance, null);
    }

    private static PriceData LoadRealPriceData(out string path)
    {
        path = Path.Combine(Application.streamingAssetsPath, "priceData.json");
        Assert.IsTrue(File.Exists(path),
            $"priceData.json 없음: {path}\n" +
            "이 테스트는 실제 파일을 검증한다 — 사람이 Tools/Economy/Export Prices to JSON 을 " +
            "먼저 실행해야 한다 (Assets/Scripts/Editor/PriceDataExporter.cs). 아직 안 돌렸다면 " +
            "이 실패는 정상이다.");

        var data = JsonUtility.FromJson<PriceData>(File.ReadAllText(path));
        Assert.IsNotNull(data, $"{path} 파싱 실패");
        return data;
    }

    /// <summary>
    /// 실제 5개 에셋을 로드해 깊은 복사본으로 된 PriceApplyTargets를 만든다.
    /// 원본 에셋은 참조만 하고 수정하지 않는다 — 클래스 상단 주석 참고.
    /// </summary>
    private PriceApplyTargets LoadClonedRealTargets()
    {
        var realMineralPrices = AssetDatabase.LoadAssetAtPath<MineralPriceDatabase>(MineralPriceDbPath);
        var realShop          = AssetDatabase.LoadAssetAtPath<ShopItemDatabase>(ShopDbPath);
        var realMinerals      = AssetDatabase.LoadAssetAtPath<MineralDatabase>(MineralDbPath);
        var realRelics        = AssetDatabase.LoadAssetAtPath<RelicDatabase>(RelicDbPath);
        var realUpgradeTree   = AssetDatabase.LoadAssetAtPath<UpgradeTreeSO>(UpgradeTreePath);

        Assert.IsNotNull(realMineralPrices, $"에셋 없음: {MineralPriceDbPath}");
        Assert.IsNotNull(realShop,          $"에셋 없음: {ShopDbPath}");
        Assert.IsNotNull(realMinerals,      $"에셋 없음: {MineralDbPath}");
        Assert.IsNotNull(realRelics,        $"에셋 없음: {RelicDbPath}");
        Assert.IsNotNull(realUpgradeTree,   $"에셋 없음: {UpgradeTreePath}");

        // ShopItemDatabase.shopItems는 List<ShopItemData>(임베디드 값)라 그냥 Instantiate로
        // 충분히 분리된다. MineralPriceDatabase도 자기 리스트(prices)만 갖고 있어 마찬가지.
        var mineralPricesClone = CloneAsset(realMineralPrices);
        var shopClone          = CloneAsset(realShop);

        // MineralDatabase.allMinerals / RelicDatabase.allRelics / UpgradeTreeSO.allNodes는
        // 다른 에셋(SO)에 대한 참조 리스트라 얕은 Instantiate로는 원본 SO를 그대로 가리킨다.
        // 리스트 안의 SO도 하나씩 복제해서 바꿔치기한다.
        var mineralsClone = ScriptableObject.CreateInstance<MineralDatabase>();
        _created.Add(mineralsClone);
        mineralsClone.allMinerals = new List<MineralSO>();
        if (realMinerals.allMinerals != null)
            foreach (var m in realMinerals.allMinerals)
                if (m != null) mineralsClone.allMinerals.Add(CloneAsset(m));

        var relicsClone = ScriptableObject.CreateInstance<RelicDatabase>();
        _created.Add(relicsClone);
        relicsClone.allRelics = new List<RelicSO>();
        if (realRelics.allRelics != null)
            foreach (var r in realRelics.allRelics)
                if (r != null) relicsClone.allRelics.Add(CloneAsset(r));

        var upgradeTreeClone = ScriptableObject.CreateInstance<UpgradeTreeSO>();
        _created.Add(upgradeTreeClone);
        upgradeTreeClone.allNodes = new List<UpgradeNodeSO>();
        if (realUpgradeTree.allNodes != null)
            foreach (var n in realUpgradeTree.allNodes)
                if (n != null) upgradeTreeClone.allNodes.Add(CloneAsset(n));

        return new PriceApplyTargets
        {
            mineralPrices = mineralPricesClone,
            minerals      = mineralsClone,
            shop          = shopClone,
            relics        = relicsClone,
            upgradeTree   = upgradeTreeClone,
        };
    }

    [Test]
    public void PriceDataJson_ExistsAndParses()
    {
        var data = LoadRealPriceData(out string path);
        Assert.IsNotNull(data, $"{path} 파싱 실패");
    }

    [Test]
    public void PriceDataJson_SectionCountsMatchDesign()
    {
        var data = LoadRealPriceData(out _);

        Assert.AreEqual(ExpectedMinerals,      data.minerals?.Length      ?? 0, "minerals 절 개수 (설계 §3.1)");
        Assert.AreEqual(ExpectedShopItems,     data.shopItems?.Length     ?? 0, "shopItems 절 개수 (설계 §3.1)");
        Assert.AreEqual(ExpectedShopEquipment, data.shopEquipment?.Length ?? 0, "shopEquipment 절 개수 (설계 §3.1)");
        Assert.AreEqual(ExpectedShopRelics,    data.shopRelics?.Length    ?? 0, "shopRelics 절 개수 (설계 §3.1)");
        Assert.AreEqual(ExpectedRelicUpgrades, data.relicUpgrades?.Length ?? 0, "relicUpgrades 절 개수 (설계 §3.1)");
        Assert.AreEqual(ExpectedUpgradeNodes,  data.upgradeNodes?.Length  ?? 0, "upgradeNodes 절 개수 (설계 §3.1)");
    }

    [Test]
    public void Apply_RealDataOnRealAssets_ProducesNoWarnings()
    {
        var data = LoadRealPriceData(out _);
        var targets = LoadClonedRealTargets();

        var result = PriceApplier.Apply(data, targets);

        if (result.warnings.Count > 0)
        {
            string msg = $"PriceApplier.Apply 경고 {result.warnings.Count}건 — " +
                          "priceData.json의 id가 실제 에셋과 매칭되지 않는다:\n" +
                          string.Join("\n", result.warnings);
            Assert.Fail(msg);
        }
    }

    [Test]
    public void Apply_RealDataOnRealAssets_AppliesExpectedCount()
    {
        var data = LoadRealPriceData(out _);
        var targets = LoadClonedRealTargets();

        var result = PriceApplier.Apply(data, targets);

        string warningDump = result.warnings.Count > 0
            ? $" 경고 {result.warnings.Count}건: {string.Join(" | ", result.warnings)}"
            : string.Empty;

        Assert.AreEqual(ExpectedApplied, result.applied,
            $"applied 기대값 {ExpectedApplied} (계산 근거는 클래스 상단 ExpectedApplied 주석 참고).{warningDump}");
    }

    [Test]
    public void UpgradeNodeIds_InPriceJson_AllExistInCsv()
    {
        // 2026-08-21: JSON에 'Facility_Workbench_T0'가 있는데 트리에는 'Facility_Workbench_T1'이라
        // 런타임에 경고만 남기고 작업대 가격이 통째로 적용되지 않고 있었다.
        // 이 검사가 없으면 오타 하나가 조용히 가격을 무력화한다.
        //
        // 트리(에셋)가 아니라 CSV를 기준으로 본다 — CSV가 단일 원본이고,
        // 에셋은 생성기를 안 돌리면 뒤처질 수 있기 때문이다.
        // 설계: specs/2026-08-21-upgrade-tree-autogen-design.md §5.1/§9.3
        var csv = UpgradeTreeCsvReader.Read(UpgradeTreeCsvReader.DefaultPath);
        Assert.Greater(csv.Count, 0, $"{UpgradeTreeCsvReader.DefaultPath}를 못 읽었다");

        var ids = new HashSet<string>();
        foreach (var n in csv) ids.Add(n.nodeId);

        var data = PriceDataTestSource.Load();
        Assert.IsNotNull(data.upgradeNodes, "priceData.json에 upgradeNodes 절이 없다");

        var missing = new List<string>();
        foreach (var e in data.upgradeNodes)
            if (!ids.Contains(e.id)) missing.Add(e.id);

        CollectionAssert.IsEmpty(missing,
            "priceData.json의 이 id들이 UpgradeTree.csv에 없다: " + string.Join(", ", missing));
    }
}
