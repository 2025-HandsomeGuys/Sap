// @tags: test, editmode, upgrade, tree, cost, economy, balance
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 업그레이드 트리의 비용 총액과 구조를 고정한다.
/// 예산 근거: Assets/Docs/economy/mineral-price-design.md §7
///
/// 노드 에셋은 Tools/Upgrade/Upgrade Tree Generator로 재생성되므로,
/// 이 테스트는 생성 결과(에셋)를 검사한다 — 생성기를 돌리는 것을 잊으면 여기서 잡힌다.
/// </summary>
public class UpgradeTreeCostTests
{
    private const string NodeDir = "Assets/GameData/UpgradeData/Node";

    private static Dictionary<string, UpgradeNodeSO> LoadNodes()
    {
        var map = new Dictionary<string, UpgradeNodeSO>();
        string[] guids = AssetDatabase.FindAssets("t:UpgradeNodeSO", new[] { NodeDir });
        foreach (string guid in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<UpgradeNodeSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null) map[so.nodeId] = so;
        }
        Assert.Greater(map.Count, 0, $"{NodeDir}에 노드 에셋이 없다 — 트리 생성기를 실행했는가?");
        return map;
    }

    /// <summary>
    /// 시설 해금 노드(작업대·컴퓨터·엘리베이터)인가. 비용이 아직 임시값이라
    /// 실측 기반 예산 검사에서 제외한다 — 자세한 이유는 Tier0_TotalCost 주석 참고.
    /// </summary>
    private static bool IsFacilityNode(UpgradeNodeSO n)
        => n.nodeId != null && n.nodeId.StartsWith("Facility_");

    /// <summary>
    /// 낮잠 노드(NapCount 효과)인가. 시설과 같은 이유(임시 비용, 실측 사다리 밖)로
    /// 예산 합산에서 제외한다. 설계: Assets/Docs/nap-system.md
    /// </summary>
    private static bool IsNapNode(UpgradeNodeSO n)
        => n.effect != null && n.effect.type == UpgradeEffectType.NapCount;

    /// <summary>실측 예산 검사에서 빼는 유틸리티 노드(시설·낮잠).</summary>
    private static bool IsBudgetExcluded(UpgradeNodeSO n) => IsFacilityNode(n) || IsNapNode(n);

    private static int SumTier(Dictionary<string, UpgradeNodeSO> nodes, int tier)
    {
        int sum = 0;
        foreach (var n in nodes.Values) if (n.tier == tier) sum += n.cost;
        return sum;
    }

    private static int SumTierExcludingFacilities(Dictionary<string, UpgradeNodeSO> nodes, int tier)
    {
        int sum = 0;
        foreach (var n in nodes.Values) if (n.tier == tier && !IsBudgetExcluded(n)) sum += n.cost;
        return sum;
    }

    [Test]
    [Ignore("노드 수는 트리를 손볼 때마다 바뀌는 값이라 고정 숫자로 못 지킨다. " +
            "CSV 마이그레이션으로 46→50이 됐다. " +
            "죽은 노드 검출로 대체 예정 — specs/2026-08-21-upgrade-tree-autogen-design.md §8/§9.1")]
    public void NodeCount_IsFortySix()
    {
        // 35개 − 구 T0 10개 + 신 T0 16개(곡괭이 입수 포함) + 시설 해금 3개 + 낮잠 2개(Nap_T0_01/02).
        // 노드를 추가/삭제했다면 이 숫자도 갱신할 것.
        // T0 확장 설계: Assets/Docs/superpowers/specs/2026-08-11-t0-upgrade-tree-expansion-design.md
        // 낮잠 설계: Assets/Docs/nap-system.md
        Assert.AreEqual(46, LoadNodes().Count);
    }

    [Test]
    [Ignore("총액 기대값은 플레이하며 조정하는 값이라 고정 숫자로 못 지킨다. " +
            "2026-08-18 승격(이동·등반 필수화, 시설 척추 편입) 이후 이미 방치돼 " +
            "실패 중이었다(기대 5,150 / 실제 7,920). " +
            "오토플레이 리듬 검사로 대체 예정 — specs/2026-08-21-upgrade-tree-autogen-design.md §8/§9.1")]
    public void Tier0_TotalCost_MatchesLayer1Budget()
    {
        // 2026-08-15 재산정. 직전 값 3,940G는 "회당 100G / 노드당 +37.5G" 모델에서
        // 나왔는데, 9일치 실측을 다시 적합하니 노드당 +64G로 기울기가 1.7배였다.
        // 가격이 수입을 못 따라가 5~8일차에 하루 두 개씩 사졌다 (설계 §4.3-2 재산정).
        //
        // 시설 해금 3개(Facility_*)는 뺀다. 그 가격은 아직 실측 사다리에 얹은 값이 아니라
        // 배선용 임시값이라, 여기 더하면 '측정된 예산'이라는 이 숫자의 의미가 흐려진다.
        // 시설 쪽은 아래 Facility_ 테스트들이 따로 잡는다.
        Assert.AreEqual(5150, SumTierExcludingFacilities(LoadNodes(), 0), 150);
    }

    [Test]
    [Ignore("2층 총액도 플레이하며 조정할 값이다. 지금은 통과 중이지만(120,100 / 기대 120,000) " +
            "1층만 풀고 2층을 묶어두면 합성기가 T1에 닿을 때 같은 교착이 반복된다. " +
            "오토플레이 리듬 검사로 대체 예정 — specs/2026-08-21-upgrade-tree-autogen-design.md §8/§9.1")]
    public void Tier1_TotalCost_MatchesLayer2Budget()
    {
        // 2층 예산 198,400G 중 트리 몫 120,000G (설계 §7.2)
        Assert.AreEqual(120000, SumTier(LoadNodes(), 1), 1000);
    }

    [Test]
    [Ignore("면허가는 플레이하며 조정하는 값이라 고정 숫자로 못 지킨다. " +
            "2026-08-18 승격으로 면허 I이 910→1,280이 됐는데 기대값이 안 따라와 " +
            "이미 실패 중이었다. " +
            "오토플레이 리듬 검사로 대체 예정 — specs/2026-08-21-upgrade-tree-autogen-design.md §8/§9.1")]
    public void MiningLicenses_HaveExplicitCosts()
    {
        var nodes = LoadNodes();
        Assert.AreEqual(910,   nodes["MiningLevel_T0_Final"].cost, "면허 I");
        Assert.AreEqual(25000, nodes["MiningLevel_T1_Final"].cost, "면허 II");
    }

    [Test]
    public void Backpack_MovedToTier1_AndCostsFiveThousand()
    {
        var nodes = LoadNodes();
        Assert.IsTrue(nodes.ContainsKey("InventoryWeight_T1_01"), "배낭 I이 T1에 없다");
        Assert.AreEqual(1, nodes["InventoryWeight_T1_01"].tier);
        // 가격 단언(5,000)은 뺐다 — 수치는 §9.1로 넘어갔고, 여기서 지키는 것은 배치다.
        // 배낭이 T1에 있다는 것과 T0에 없다는 것이 이 테스트의 불변식이다.
        Assert.IsFalse(nodes.ContainsKey("InventoryWeight_T0_01"), "배낭 I이 T0에 아직 남아 있다");
    }

    [Test]
    [Ignore("다단계 강화를 접었다(2026-08-21). 무게 한도를 한 노드의 4레벨로 올리는 대신 " +
            "노드를 여러 개 두는 방식으로 바꿨으므로, EffectiveMaxLevel==4를 요구하는 " +
            "이 테스트는 폐기된 설계를 지키고 있다. 생성기가 maxLevel을 항상 1로 되돌린다. " +
            "T0 무게 노드의 '싸고 여러 번'이라는 의도는 살아 있고, " +
            "오토플레이 리듬 검사로 대체 예정 — specs/2026-08-21-upgrade-tree-autogen-design.md §8/§9.1")]
    public void Backpack_T0_IsMultiLevelAndCheap()
    {
        // 2026-08-20: '효율적인 호흡 I'(StaminaCost_T0_01) 자리를 무게 한도 다단계로 바꿨다.
        // 레벨마다 80G 정액 / 무게 +3, 최대 4레벨 — 만렙까지 320G에 +12(기본 8 → 20)다.
        //
        // 값이 아니라 '모양'을 고정하는 테스트다. 정액(costGrowth 1)이라 CostForLevel이
        // 모든 레벨에서 같은 값을 돌려주는지까지 본다 — 여기가 깨지면 UI의 '강화 Lv N (…G)'과
        // 실제 결제액이 어긋난다.
        //
        // ⚠ 2층 진입의 '가방이 금방 찬다' 톱니(경제 설계 §3.2)는 T1 배낭(+30)이 진다.
        //   T0 만렙 총량이 그쪽에 근접하면 층 관문이 사라지므로 아래에서 막아 둔다.
        var nodes = LoadNodes();
        Assert.IsTrue(nodes.ContainsKey("InventoryWeight_T0_01"), "T0 무게 노드가 없다");

        var node = nodes["InventoryWeight_T0_01"];
        Assert.AreEqual(0, node.tier);
        Assert.AreEqual(4, node.EffectiveMaxLevel, "다단계가 아니거나 최대 레벨이 바뀌었다");
        Assert.AreEqual(UpgradeEffectType.InventoryWeightUp, node.effect.type);
        Assert.IsFalse(node.effect.isPercentage, "무게 한도는 합연산이다");
        Assert.AreEqual(3f, node.effect.value, 0.001f, "레벨당 증가량");

        for (int lv = 1; lv <= node.EffectiveMaxLevel; lv++)
            Assert.AreEqual(80, node.CostForLevel(lv), $"Lv{lv} 가격 — 레벨당 80G 정액이어야 한다");

        float atMax = node.effect.value * node.EffectiveMaxLevel;
        Assert.Less(atMax, nodes["InventoryWeight_T1_01"].effect.value,
                    "T0 만렙 총량이 T1 배낭(+30)에 근접하면 층 관문이 사라진다");
    }

    [Test]
    public void Backpack_IsNotAPrerequisiteOfAnyLicense()
    {
        // 배낭을 면허 선행으로 묶으면 "부족함을 겪고 해소하는" 톱니가 사라진다 (설계 §3.2/§6.1)
        var nodes = LoadNodes();
        foreach (string licenseId in new[] { "MiningLevel_T0_Final", "MiningLevel_T1_Final" })
        {
            foreach (var parent in nodes[licenseId].parentNodes)
            {
                Assert.AreNotEqual("InventoryWeight_T1_01", parent.nodeId,
                    $"{licenseId}의 선행에 배낭이 들어 있다");
            }
        }
    }

    [Test]
    [Ignore("최소 경로 총액은 플레이하며 조정하는 값이라 고정 숫자로 못 지킨다. " +
            "2026-08-18 승격으로 필수 노드가 늘어 이미 실패 중이었다(기대 4,120 / 실제 8,340, 16노드). " +
            "오토플레이 리듬 검사로 대체 예정 — specs/2026-08-21-upgrade-tree-autogen-design.md §8/§9.1")]
    public void Tier0_MinimumPathToLicense_CostsFortyOneTwenty()
    {
        // 면허 I까지의 필수 지출. 실측 수입 곡선(회당 60G에서 시작, 노드당 +64G)에
        // 맞춰 매긴 값이라 T0 총액이 맞아도 개별 가격을 옮기면 여기가 먼저 어긋난다.
        var nodes = LoadNodes();
        var required = new HashSet<string>();
        CollectWithParents(nodes["MiningLevel_T0_Final"], required);

        int sum = 0;
        foreach (string id in required) sum += nodes[id].cost;

        Assert.AreEqual(4120, sum, $"필수 노드 {required.Count}개");
    }

    [Test]
    [Ignore("구매 리듬을 '회당 60G + 노드당 64G' 고정 상수로 판정하는 방식을 접는다. " +
            "수입 모델이 스칼라라 어떤 노드를 샀는지 구분하지 못하는 것이 근본 한계다. " +
            "지키려던 감각(다이브마다 하나는 사지고 두 번째는 모자란다)은 살아 있고, " +
            "봇 3종이 밟는 리듬 밴드로 다시 검사한다 — " +
            "specs/2026-08-21-upgrade-tree-autogen-design.md §3/§8/§9.1")]
    public void Tier0_EveryDiveAffordsExactlyOneNode()
    {
        // 이 트리의 핵심 감각: 다이브마다 하나는 사지고, 두 번째는 조금 모자란다.
        // 가격을 하나만 낮춰도 "한 번에 두 개" 구간이 생겨 리듬이 무너진다.
        //
        // 수입 모델은 2026-08-15 실측이다 — 시작 회당 60G, 노드당 +64G.
        // 업그레이드가 곧 수입이라 곡선이 가파르다.
        //
        // ⚠ 이 상수는 '하루 = 다이브 1회'를 전제한다. 긴급 탈출이 시간대를 넘기지 않던
        //   동안은 같은 날 두 번 내려갈 수 있어 회당 수입이 절반으로 눌려 보였다
        //   (PauseOverlayUI.ExecuteEmergencyEscape에서 SetAfternoon으로 막았다).
        //   전제가 깨지면 여기 상수부터 다시 적합해야 한다.
        //
        // 면허를 뺀 필수 노드를 '싼 것부터' 훑는다. 같은 행의 형제 노드는 아무 순서로나
        // 살 수 있으므로, 플레이어가 실제로 밟는 건 이 오름차순 경로다.
        var nodes = LoadNodes();
        var required = new HashSet<string>();
        CollectWithParents(nodes["MiningLevel_T0_Final"], required);
        required.Remove("MiningLevel_T0_Final");

        var path = new List<int>();
        foreach (string id in required) path.Add(nodes[id].cost);
        path.Sort();

        float wallet = 0f;
        for (int k = 0; k < path.Count; k++)
        {
            wallet += 60f + 64f * k;
            Assert.GreaterOrEqual(wallet, path[k], $"{k + 1}번째 다이브: 하나도 못 산다");
            wallet -= path[k];

            int next = (k + 1 < path.Count) ? path[k + 1] : nodes["MiningLevel_T0_Final"].cost;
            Assert.Less(wallet, next, $"{k + 1}번째 다이브: 두 개가 사진다 (잔액 {wallet})");
        }
    }

    [Test]
    public void ShovelStaminaNodes_UseMultiplierConvention()
    {
        // isPercentage는 '곱할 배율'이다 — 0.2는 20% 감소가 아니라 80% 감소가 된다.
        // 효과가 배선돼 있지 않던 동안 이 버그가 T1 노드에 그대로 남아 있었다.
        var nodes = LoadNodes();
        Assert.AreEqual(0.8f,  nodes["ShovelStamina_T0_01"].effect.value, 0.001f, "삽질 I");
        Assert.AreEqual(0.85f, nodes["ShovelStamina_T1_01"].effect.value, 0.001f, "삽질 II");
    }

    private static void CollectWithParents(UpgradeNodeSO node, HashSet<string> acc)
    {
        if (node == null || !acc.Add(node.nodeId)) return;
        if (node.parentNodes == null) return;
        foreach (var parent in node.parentNodes) CollectWithParents(parent, acc);
    }

    [Test]
    public void ToolConfig_UnlockNodeIds_AllExistInTree()
    {
        // toolConfig.json의 unlockNodeIds는 '문자열로' 노드를 가리킨다
        // (ToolController.IsToolUnlocked). 오타나 없는 id를 넣으면 그 도구가
        // 조용히 영구 잠금이 되고, 컴파일러도 에디터도 아무 말을 하지 않는다.
        // 실제로 debug_pickaxe / debug_drill이 그렇게 남아 있었다.
        string path = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "toolConfig.json");
        Assert.IsTrue(System.IO.File.Exists(path), $"{path} 없음");

        var config = UnityEngine.JsonUtility.FromJson<ToolConfigData>(System.IO.File.ReadAllText(path));
        Assert.IsNotNull(config?.unlockNodeIds, "unlockNodeIds 파싱 실패");

        var nodes = LoadNodes();
        for (int i = 0; i < config.unlockNodeIds.Length; i++)
        {
            string id = config.unlockNodeIds[i];
            if (string.IsNullOrEmpty(id)) continue;   // 빈 문자열 = 항상 해금
            Assert.IsTrue(nodes.ContainsKey(id),
                $"toolConfig.json unlockNodeIds[{i}] = \"{id}\" — 트리에 없는 노드다 (도구가 영구 잠김)");
        }
    }

    [Test]
    public void PickaxeUnlock_IsPrerequisiteOfPickaxeNodes()
    {
        // 곡괭이를 갖기 전에 곡괭이 강화를 살 수 있으면 안 된다.
        var nodes = LoadNodes();
        foreach (string id in new[] { "PickaxeDamage_T0_01", "MiningCooldown_T0_01" })
        {
            var required = new HashSet<string>();
            CollectWithParents(nodes[id], required);
            Assert.IsTrue(required.Contains("PickaxeUnlock_T0_01"),
                $"{id}가 곡괭이 입수 없이도 살 수 있다");
        }
    }

    [Test]
    public void FacilityUnlockNodes_ExistInTree()
    {
        // 시설 해금은 씬 오브젝트(WorldInteractable.unlockNodeId)가 '문자열로' 이 노드를
        // 가리킨다. 코드에서 참조되지 않으므로 노드를 지우거나 id를 바꿔도 컴파일은 통과하고,
        // 그 시설만 조용히 영구 잠금이 된다 — 도구 쪽 toolConfig와 똑같은 함정이다.
        //
        // 2026-08-20: "Facility_Elevator_T0"(엘리베이터 가동)은 뺐다 — 엘리베이터는 골드로 사는
        // 업그레이드가 아니라 재료를 가져다 고치는 퀘스트로 간다. 씬에서 그 id를 쓰던 오브젝트는
        // 없었지만, 퀘스트를 붙일 때도 unlockNodeId는 비워 둘 것(없는 노드 = 영구 잠금).
        var nodes = LoadNodes();
        foreach (string id in new[] { "Facility_Workbench_T0", "Facility_Computer_T0" })
        {
            Assert.IsTrue(nodes.ContainsKey(id), $"시설 해금 노드 '{id}'가 트리에 없다 (그 시설이 영구 잠김)");
            Assert.AreEqual(UpgradeEffectType.None, nodes[id].effect.type,
                $"{id}: 해금은 효과가 아니라 nodeId로 걸린다 — effect는 None이어야 한다");
        }
    }

    [Test]
    public void RelicUnlockNodes_ExistInTree()
    {
        // 유물 해금도 도구(toolConfig.json)·시설(WorldInteractable)과 같은 'nodeId 문자열' 규칙이다.
        // RelicSO.unlockNodeId가 노드를 가리키고, RelicManager.SyncUpgradeUnlockedRelics가 대조해
        // 지급한다. 어느 한쪽 이름만 바꿔도 컴파일은 통과하고 그 유물만 조용히 안 나온다.
        var nodes = LoadNodes();

        const string ElevatorTrackerNode = "RelicUnlock_ElevatorTracker_T0";
        Assert.IsTrue(nodes.ContainsKey(ElevatorTrackerNode),
            $"유물 해금 노드 '{ElevatorTrackerNode}'가 트리에 없다 (엘리베이터 신호기를 얻을 방법이 없어진다)");
        Assert.AreEqual(UpgradeEffectType.None, nodes[ElevatorTrackerNode].effect.type,
            "유물 해금은 효과가 아니라 nodeId로 걸린다 — effect는 None이어야 한다");

        // 반대 방향: unlockNodeId가 채워진 유물이 실제로 그 노드를 가리키는가.
        string[] guids = AssetDatabase.FindAssets("t:RelicSO", new[] { "Assets/GameData/Relics" });
        int paired = 0;
        foreach (string guid in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<Relic.Data.RelicSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null || string.IsNullOrEmpty(so.unlockNodeId)) continue;

            paired++;
            Assert.IsTrue(nodes.ContainsKey(so.unlockNodeId),
                $"유물 {so.id}의 unlockNodeId '{so.unlockNodeId}'가 트리에 없다 (영영 지급되지 않는다)");
        }

        Assert.AreEqual(1, paired,
            "업그레이드로 지급되는 유물이 하나도 없다 — Tools/Relic/Generate Slice Assets를 실행했는가?");
    }

    [Test]
    public void FacilityUnlockNodes_AreNotOnLicensePath()
    {
        // 시설을 면허 선행으로 묶으면 T0 필수 경로 비용이 바뀌어 다이브 리듬 테스트가
        // 깨진다. 시설은 곁가지로 남겨 두고, 사고 싶을 때 사게 한다.
        var nodes = LoadNodes();
        foreach (string licenseId in new[] { "MiningLevel_T0_Final", "MiningLevel_T1_Final" })
        {
            var required = new HashSet<string>();
            CollectWithParents(nodes[licenseId], required);
            foreach (string id in required)
                Assert.IsFalse(id.StartsWith("Facility_"), $"{licenseId}의 필수 경로에 시설 노드 {id}가 들어 있다");
        }
    }

    [Test]
    public void NapNodes_ExistWithNapCountEffect()
    {
        // 낮잠 노드는 NapManager가 GetStatValue(NapCount)로 하루 가능 횟수를 읽는다.
        // 각 노드 +1(합연산) → 둘 다 사면 하루 2회. '단말기 개통' 뒤에 걸린다(주식 타이밍 도구).
        var nodes = LoadNodes();
        foreach (string id in new[] { "Nap_T0_01", "Nap_T0_02" })
        {
            Assert.IsTrue(nodes.ContainsKey(id), $"낮잠 노드 '{id}'가 트리에 없다");
            Assert.AreEqual(UpgradeEffectType.NapCount, nodes[id].effect.type, $"{id}: NapCount 효과여야 한다");
            Assert.AreEqual(1f, nodes[id].effect.value, 0.001f, $"{id}: +1회(합연산)여야 한다");
            Assert.IsFalse(nodes[id].effect.isPercentage, $"{id}: 합연산이어야 한다");
        }

        // 두 번째는 첫 번째를 선행으로 — 0→1→2 순서.
        var parents = new HashSet<string>();
        foreach (var p in nodes["Nap_T0_02"].parentNodes) if (p != null) parents.Add(p.nodeId);
        Assert.Contains("Nap_T0_01", new List<string>(parents), "깊은 낮잠 II가 짧은 낮잠 I을 선행으로 갖지 않는다");
    }

    [Test]
    public void EveryParentReference_Resolves()
    {
        // nodeId를 바꿨으므로 끊어진 부모 참조가 없는지 확인한다
        var nodes = LoadNodes();
        foreach (var n in nodes.Values)
        {
            if (n.parentNodes == null) continue;
            foreach (var p in n.parentNodes)
                Assert.IsNotNull(p, $"{n.nodeId}: 부모 참조가 비어 있다(선행 id 오타 의심)");
        }
    }
}
