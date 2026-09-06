# 광물 가격 레벨디자인 (1·2층) 구현 계획

> **에이전트 작업자에게:** 이 계획은 `superpowers:subagent-driven-development` 또는 `superpowers:executing-plans`로 태스크 단위 실행한다. 체크박스(`- [ ]`)로 진행을 추적한다.

**설계 문서:** [mineral-price-design.md](mineral-price-design.md) — 수치의 근거는 전부 여기 있다. 숫자가 계획과 문서 사이에서 어긋나면 **문서가 정답**이다.

**Goal:** 1·2층 광물의 가격·무게·산출량을 지수 곡선에 맞춰 재조정하고, 그 밸런스를 EditMode 테스트로 고정한다.

**Architecture:** 밸런스 계산을 순수 C# 정적 클래스(`MineralEconomy`)로 분리하고, EditMode 테스트가 실제 에셋·JSON을 읽어 설계 목표치와 대조한다. 데이터(에셋 YAML·JSON) 변경은 그 테스트를 통과시키는 방향으로 수행한다. 테스트가 밸런스 명세서 역할을 하므로 3·4층 확장 때도 그대로 재사용된다.

**Tech Stack:** Unity 2D / C# / NUnit (EditMode) / ScriptableObject 에셋 YAML / StreamingAssets JSON

## Global Constraints

- **UVCS 프로젝트다. `git` 명령을 일절 쓰지 않는다.** 체크인은 UVCS로 한다.
- **Unity Test Runner 실행은 사람이 한다.** Claude는 테스트 파일 작성·수정만 하고 `mcp__mcp-unity__run_tests` 등을 호출하지 않는다.
- 새 EditMode 테스트는 `Assets/Tests/EditMode/`에 두고 `EditModeTests.asmdef`(Editor 전용, `GameScripts` 참조)에 자동 포함시킨다.
- 모든 스크립트 첫 줄에 `// @tags: ...` 주석을 단다(프로젝트 검색 규약).
- 이번 범위는 **1층(Dirt)·2층(Ice)뿐**이다. 3·4층 수치는 건드리지 않는다.
- 목표 수치 (설계 문서 §4·§5):

| 층 | 가중평균 가격 | 가중평균 무게 | 가방 | 담는 개수 | 회당 매출 | 선별 편향 |
|---|---|---|---|---|---|---|
| 1 (Dirt) | 20.55 | 0.5744 | 40 | 69.6 | 1,430G | 1.165 |
| 2 (Ice) | 95.03 | 1.0733 | 70 | 65.2 | 6,198G | 1.188 |

- 불변식: `최고(가격/무게) ≤ 1.2 × (가중평균 가격 / 가중평균 무게)` (설계 §3.3)
- 불변식: `2층 진입 매출(가방 40) ≥ 2.0 × 1층 만재 매출` (설계 §3.2 안전장치)

---

## File Structure

| 파일 | 책임 | 신규/수정 |
|---|---|---|
| `Assets/Scripts/_Core/Data/MineralEconomy.cs` | 밸런스 계산 순수 로직. Unity 의존 없음 | 신규 |
| `Assets/Tests/EditMode/MineralEconomyTests.cs` | 위 계산식의 단위 테스트(합성 데이터) | 신규 |
| `Assets/Tests/EditMode/MineralBalanceTests.cs` | 실제 에셋·JSON을 읽어 설계 목표치와 대조 | 신규 |
| `Assets/Tests/EditMode/UpgradeTreeCostTests.cs` | 트리 노드 비용·구조 명세 검증 | 신규 |
| `Assets/Scripts/UI/Items/Minerals/Mineral/MineralPriceDatabase.cs` | `_priceMap` null 가드 추가 | 수정 |
| `Assets/GameData/ShopData/MineralPriceDatabase.asset` | 1·2층 12종 가격 | 수정 |
| `Assets/GameData/MineralData/Basement_1/*.asset` | 1층 6종 무게 | 수정 |
| `Assets/GameData/MineralData/Basement_2/*.asset` | 2층 6종 무게 | 수정 |
| `Assets/StreamingAssets/tileData.json` | 2층 `perChunk`·`rarity` | 수정 |
| `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` | T0·T1 비용, 배낭 I을 T0 → T1으로 이동 | 수정 |
| `Assets/GameData/ShopData/ShopItemDatabase.asset` | 장비·포션 가격, 미해금 세트 잠금 | 수정 |

---

### Task 1: 밸런스 계산 순수 로직

**Files:**
- Create: `Assets/Scripts/_Core/Data/MineralEconomy.cs`
- Test: `Assets/Tests/EditMode/MineralEconomyTests.cs`

**Interfaces:**
- Consumes: `MineralID` (`Assets/Scripts/_Core/Data/Enums.cs`)
- Produces:
  - `struct MineralEconomyEntry { MineralID id; float price; float weight; float perChunk; float ValuePerWeight { get; } }`
  - `static class MineralEconomy` — `TotalPerChunk`, `WeightedAveragePrice`, `WeightedAverageWeight`, `AverageValuePerWeight`, `MaxValuePerWeight`, `PickBias`, `ItemsPerTrip`, `RevenuePerTrip`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/Tests/EditMode/MineralEconomyTests.cs`:

```csharp
// @tags: test, editmode, mineral, economy, balance, pure-logic
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// MineralEconomy 계산식의 단위 테스트. 합성 데이터만 쓰고 에셋을 읽지 않는다.
/// 실제 데이터와의 대조는 MineralBalanceTests가 담당한다.
/// </summary>
public class MineralEconomyTests
{
    private static List<MineralEconomyEntry> Sample()
    {
        // perChunk 비중 1:3 → 가중평균은 비싼 쪽으로 끌린다
        return new List<MineralEconomyEntry>
        {
            new MineralEconomyEntry(MineralID.ScrapMetal, 10f, 0.5f, 1f),
            new MineralEconomyEntry(MineralID.Iron,       20f, 1.5f, 3f),
        };
    }

    [Test]
    public void TotalPerChunk_SumsAllEntries()
    {
        Assert.AreEqual(4f, MineralEconomy.TotalPerChunk(Sample()), 0.0001f);
    }

    [Test]
    public void WeightedAveragePrice_WeightsByPerChunk()
    {
        // (10*1 + 20*3) / 4 = 17.5
        Assert.AreEqual(17.5f, MineralEconomy.WeightedAveragePrice(Sample()), 0.0001f);
    }

    [Test]
    public void WeightedAverageWeight_WeightsByPerChunk()
    {
        // (0.5*1 + 1.5*3) / 4 = 1.25
        Assert.AreEqual(1.25f, MineralEconomy.WeightedAverageWeight(Sample()), 0.0001f);
    }

    [Test]
    public void ItemsPerTrip_IsBagDividedByAverageWeight()
    {
        // 40 / 1.25 = 32
        Assert.AreEqual(32f, MineralEconomy.ItemsPerTrip(40f, Sample()), 0.0001f);
    }

    [Test]
    public void RevenuePerTrip_IsItemsTimesAveragePrice()
    {
        // 32 * 17.5 = 560
        Assert.AreEqual(560f, MineralEconomy.RevenuePerTrip(40f, Sample()), 0.001f);
    }

    [Test]
    public void AverageValuePerWeight_IsAvgPriceOverAvgWeight()
    {
        // 17.5 / 1.25 = 14
        Assert.AreEqual(14f, MineralEconomy.AverageValuePerWeight(Sample()), 0.0001f);
    }

    [Test]
    public void MaxValuePerWeight_PicksTheBestEntry()
    {
        // 고철 10/0.5 = 20, 철 20/1.5 = 13.33 → 20
        Assert.AreEqual(20f, MineralEconomy.MaxValuePerWeight(Sample()), 0.0001f);
    }

    [Test]
    public void PickBias_IsMaxOverAverage()
    {
        // 20 / 14 = 1.42857
        Assert.AreEqual(20f / 14f, MineralEconomy.PickBias(Sample()), 0.0001f);
    }

    [Test]
    public void EmptyList_ReturnsZero_AndDoesNotDivideByZero()
    {
        var empty = new List<MineralEconomyEntry>();
        Assert.AreEqual(0f, MineralEconomy.TotalPerChunk(empty));
        Assert.AreEqual(0f, MineralEconomy.WeightedAveragePrice(empty));
        Assert.AreEqual(0f, MineralEconomy.WeightedAverageWeight(empty));
        Assert.AreEqual(0f, MineralEconomy.AverageValuePerWeight(empty));
        Assert.AreEqual(0f, MineralEconomy.MaxValuePerWeight(empty));
        Assert.AreEqual(0f, MineralEconomy.PickBias(empty));
        Assert.AreEqual(0f, MineralEconomy.ItemsPerTrip(40f, empty));
        Assert.AreEqual(0f, MineralEconomy.RevenuePerTrip(40f, empty));
    }

    [Test]
    public void ZeroWeightEntry_IsIgnoredByValuePerWeight()
    {
        var list = new List<MineralEconomyEntry>
        {
            new MineralEconomyEntry(MineralID.Coal, 100f, 0f, 1f),
            new MineralEconomyEntry(MineralID.Iron,  20f, 2f, 1f),
        };
        // 무게 0짜리는 ValuePerWeight 0으로 처리되어 최대값에 끼지 않는다
        Assert.AreEqual(10f, MineralEconomy.MaxValuePerWeight(list), 0.0001f);
    }

    [Test]
    public void NullList_ReturnsZero()
    {
        Assert.AreEqual(0f, MineralEconomy.WeightedAveragePrice(null));
        Assert.AreEqual(0f, MineralEconomy.RevenuePerTrip(40f, null));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

사람이 Unity Test Runner(EditMode)에서 실행한다. Claude는 실행하지 않는다.
기대: **컴파일 실패** — `MineralEconomy`, `MineralEconomyEntry` 미정의.

- [ ] **Step 3: 최소 구현을 작성한다**

`Assets/Scripts/_Core/Data/MineralEconomy.cs`:

```csharp
// @tags: mineral, economy, balance, price, weight, pure-logic
using System.Collections.Generic;

/// <summary>
/// 밸런스 계산 한 항목. 한 층에 속한 광물 1종의 가격·무게·기대 산출량.
/// perChunk는 tileData.json의 [min,max] 중앙값을 쓴다.
/// </summary>
public struct MineralEconomyEntry
{
    public MineralID id;
    public float price;
    public float weight;
    public float perChunk;

    public MineralEconomyEntry(MineralID id, float price, float weight, float perChunk)
    {
        this.id = id;
        this.price = price;
        this.weight = weight;
        this.perChunk = perChunk;
    }

    /// <summary>무게 1당 가치. 무게가 0이면 0(선별 판정에서 제외).</summary>
    public float ValuePerWeight => weight > 0f ? price / weight : 0f;
}

/// <summary>
/// 광물 경제 밸런스 계산. 순수 로직 — Unity 타입에 의존하지 않는다.
///
/// 회당 매출 = (가방 용량 / 가중평균 무게) x 가중평균 가격.
/// 가중치는 perChunk(청크당 기대 산출량)다 — 플레이어가 마주치는 비율이기 때문이다.
///
/// 설계 근거: Assets/Docs/economy/mineral-price-design.md
/// </summary>
public static class MineralEconomy
{
    public static float TotalPerChunk(IReadOnlyList<MineralEconomyEntry> entries)
    {
        if (entries == null) return 0f;
        float sum = 0f;
        for (int i = 0; i < entries.Count; i++) sum += entries[i].perChunk;
        return sum;
    }

    public static float WeightedAveragePrice(IReadOnlyList<MineralEconomyEntry> entries)
    {
        float total = TotalPerChunk(entries);
        if (total <= 0f) return 0f;
        float acc = 0f;
        for (int i = 0; i < entries.Count; i++) acc += entries[i].price * entries[i].perChunk;
        return acc / total;
    }

    public static float WeightedAverageWeight(IReadOnlyList<MineralEconomyEntry> entries)
    {
        float total = TotalPerChunk(entries);
        if (total <= 0f) return 0f;
        float acc = 0f;
        for (int i = 0; i < entries.Count; i++) acc += entries[i].weight * entries[i].perChunk;
        return acc / total;
    }

    /// <summary>층 전체를 나오는 대로 담았을 때의 무게 1당 가치.</summary>
    public static float AverageValuePerWeight(IReadOnlyList<MineralEconomyEntry> entries)
    {
        float avgWeight = WeightedAverageWeight(entries);
        if (avgWeight <= 0f) return 0f;
        return WeightedAveragePrice(entries) / avgWeight;
    }

    /// <summary>그 층에서 가장 효율 좋은 광물의 무게 1당 가치.</summary>
    public static float MaxValuePerWeight(IReadOnlyList<MineralEconomyEntry> entries)
    {
        if (entries == null) return 0f;
        float max = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            float v = entries[i].ValuePerWeight;
            if (v > max) max = v;
        }
        return max;
    }

    /// <summary>
    /// 선별 편향. 최고 효율 광물만 골라 담았을 때 평균 대비 몇 배를 버는가.
    /// 1.2를 넘으면 "골라 담기"가 최적해가 되어 가중평균이 실효값 구실을 못 한다.
    /// </summary>
    public static float PickBias(IReadOnlyList<MineralEconomyEntry> entries)
    {
        float avg = AverageValuePerWeight(entries);
        if (avg <= 0f) return 0f;
        return MaxValuePerWeight(entries) / avg;
    }

    public static float ItemsPerTrip(float bagCapacity, IReadOnlyList<MineralEconomyEntry> entries)
    {
        float avgWeight = WeightedAverageWeight(entries);
        if (avgWeight <= 0f) return 0f;
        return bagCapacity / avgWeight;
    }

    public static float RevenuePerTrip(float bagCapacity, IReadOnlyList<MineralEconomyEntry> entries)
    {
        return ItemsPerTrip(bagCapacity, entries) * WeightedAveragePrice(entries);
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

사람이 Unity Test Runner(EditMode)에서 `MineralEconomyTests` 11개를 실행한다.
기대: 전부 PASS.

- [ ] **Step 5: 체크포인트**

`MineralEconomy.cs`, `MineralEconomyTests.cs`를 UVCS에 체크인한다. **git 명령을 쓰지 않는다.**

---

### Task 2: 실데이터 밸런스 회귀 테스트 (이 시점에는 실패한다)

**Files:**
- Create: `Assets/Tests/EditMode/MineralBalanceTests.cs`
- Modify: `Assets/Scripts/UI/Items/Minerals/Mineral/MineralPriceDatabase.cs`

**Interfaces:**
- Consumes: Task 1의 `MineralEconomy` / `MineralEconomyEntry`, `TileDatabaseJson`·`TileDataJson`·`MineralRuleJson` (`Assets/Scripts/_Core/Data/TileDataModels.cs`), `MineralPriceDatabase.GetPrice(MineralID)`, `MineralSO.mineralID`·`weight`
- Produces: Task 3·4가 통과시켜야 할 목표 수치. 다른 태스크가 소비하는 코드 심볼은 없다.

**주의:** 이 태스크가 끝난 시점에 테스트는 **실패해야 정상**이다. Task 3·4가 데이터를 고쳐서 통과시킨다.

- [ ] **Step 1: `MineralPriceDatabase`에 null 가드를 넣는다**

`OnEnable`이 안 불린 상태(에디터에서 `AssetDatabase.LoadAssetAtPath`로 막 읽은 직후 등)에서 `GetPrice`가 `NullReferenceException`을 던진다. 지연 초기화로 바꾼다.

`Assets/Scripts/UI/Items/Minerals/Mineral/MineralPriceDatabase.cs` — `OnEnable`과 `GetPrice`를 다음으로 교체:

```csharp
    private void OnEnable()
    {
        BuildMap();
    }

    private void BuildMap()
    {
        _priceMap = new Dictionary<MineralID, int>();
        if (prices == null) return;
        foreach (var item in prices)
        {
            if (!_priceMap.ContainsKey(item.mineralID))
            {
                _priceMap.Add(item.mineralID, item.price);
            }
        }
    }

    public int GetPrice(MineralID mineralID)
    {
        // OnEnable이 아직 안 불린 경로(에디터 로드 직후 등)에서도 안전하게.
        if (_priceMap == null) BuildMap();

        if (_priceMap.TryGetValue(mineralID, out int price))
        {
            return price;
        }
        return 0; // Or some default/error value
    }
```

- [ ] **Step 2: 밸런스 테스트를 쓴다**

`Assets/Tests/EditMode/MineralBalanceTests.cs`:

```csharp
// @tags: test, editmode, mineral, economy, balance, price, weight, regression
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 실제 에셋(가격·무게)과 tileData.json(산출량)을 읽어 설계 목표치와 대조한다.
/// 이 테스트가 곧 밸런스 명세서다 — 수치를 바꾸려면 먼저
/// Assets/Docs/economy/mineral-price-design.md 를 고치고 여기 기대값을 맞춘다.
///
/// 3·4층은 아직 재조정 전이라 검사하지 않는다(설계 문서 §8).
/// </summary>
public class MineralBalanceTests
{
    private const string PriceDbPath = "Assets/GameData/ShopData/MineralPriceDatabase.asset";

    // 설계 문서 §3.1 — 층별 가방 용량
    private const float Layer1Bag = 40f;
    private const float Layer2Bag = 70f;

    // 설계 문서 §3.3 — 선별 편향 상한
    private const float MaxPickBias = 1.20f;

    // ─────────────────────────────────────────────────────────────
    // 로더
    // ─────────────────────────────────────────────────────────────

    private static MineralPriceDatabase LoadPriceDb()
    {
        var db = AssetDatabase.LoadAssetAtPath<MineralPriceDatabase>(PriceDbPath);
        Assert.IsNotNull(db, $"가격 DB를 못 찾음: {PriceDbPath}");
        return db;
    }

    private static Dictionary<MineralID, float> LoadWeights()
    {
        var map = new Dictionary<MineralID, float>();
        string[] guids = AssetDatabase.FindAssets("t:MineralSO");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<MineralSO>(path);
            if (so == null) continue;
            map[so.mineralID] = so.weight;
        }
        Assert.Greater(map.Count, 0, "MineralSO 에셋을 하나도 못 찾음");
        return map;
    }

    private static TileDatabaseJson LoadTileDb()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "tileData.json");
        Assert.IsTrue(File.Exists(path), $"tileData.json 없음: {path}");
        var db = JsonUtility.FromJson<TileDatabaseJson>(File.ReadAllText(path));
        Assert.IsNotNull(db?.tiles, "tileData.json 파싱 실패");
        return db;
    }

    /// <summary>tileType(예: "Dirt")의 광물 규칙을 경제 계산 항목으로 변환한다.</summary>
    private static List<MineralEconomyEntry> BuildLayer(string tileType)
    {
        var tileDb = LoadTileDb();
        var priceDb = LoadPriceDb();
        var weights = LoadWeights();

        TileDataJson tile = tileDb.tiles.Find(t => t != null && t.tileType == tileType);
        Assert.IsNotNull(tile, $"tileData.json에 tileType={tileType} 없음");
        Assert.IsNotNull(tile.minerals, $"{tileType}에 minerals 배열 없음");

        var entries = new List<MineralEconomyEntry>();
        foreach (var rule in tile.minerals)
        {
            Assert.IsTrue(System.Enum.TryParse(rule.mineralType, out MineralID id),
                $"{tileType}/{rule.mineralType}: MineralID 파싱 실패");
            Assert.IsTrue(weights.ContainsKey(id), $"{id}: MineralSO 에셋이 없어 무게를 못 읽음");

            float mid = (rule.perChunk[0] + rule.perChunk[1]) * 0.5f;
            entries.Add(new MineralEconomyEntry(id, priceDb.GetPrice(id), weights[id], mid));
        }
        return entries;
    }

    // ─────────────────────────────────────────────────────────────
    // 1층 (Dirt) — 설계 문서 §4
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Layer1_WeightedAveragePrice_MatchesDesign()
    {
        Assert.AreEqual(20.55f, MineralEconomy.WeightedAveragePrice(BuildLayer("Dirt")), 0.5f);
    }

    [Test]
    public void Layer1_WeightedAverageWeight_MatchesDesign()
    {
        Assert.AreEqual(0.5744f, MineralEconomy.WeightedAverageWeight(BuildLayer("Dirt")), 0.01f);
    }

    [Test]
    public void Layer1_ItemsPerTrip_IsAbout70()
    {
        Assert.AreEqual(69.6f, MineralEconomy.ItemsPerTrip(Layer1Bag, BuildLayer("Dirt")), 1.5f);
    }

    [Test]
    public void Layer1_RevenuePerTrip_MatchesDesign()
    {
        Assert.AreEqual(1430f, MineralEconomy.RevenuePerTrip(Layer1Bag, BuildLayer("Dirt")), 60f);
    }

    [Test]
    public void Layer1_PickBias_StaysUnderLimit()
    {
        float bias = MineralEconomy.PickBias(BuildLayer("Dirt"));
        Assert.LessOrEqual(bias, MaxPickBias,
            $"1층 선별 편향 {bias:F3} — 최고 효율 광물의 무게를 올려야 한다 (설계 §3.3)");
    }

    [Test]
    public void Layer1_PerChunkTotal_IsUnchanged()
    {
        // 1층 산출량은 의도된 값이라 이번 작업에서 건드리지 않는다 (설계 §4)
        Assert.AreEqual(249.5f, MineralEconomy.TotalPerChunk(BuildLayer("Dirt")), 1f);
    }

    // ─────────────────────────────────────────────────────────────
    // 2층 (Ice) — 설계 문서 §5
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Layer2_WeightedAveragePrice_MatchesDesign()
    {
        Assert.AreEqual(95.03f, MineralEconomy.WeightedAveragePrice(BuildLayer("Ice")), 1.5f);
    }

    [Test]
    public void Layer2_WeightedAverageWeight_MatchesDesign()
    {
        Assert.AreEqual(1.0733f, MineralEconomy.WeightedAverageWeight(BuildLayer("Ice")), 0.02f);
    }

    [Test]
    public void Layer2_ItemsPerTrip_IsAbout65()
    {
        Assert.AreEqual(65.2f, MineralEconomy.ItemsPerTrip(Layer2Bag, BuildLayer("Ice")), 1.5f);
    }

    [Test]
    public void Layer2_RevenuePerTrip_MatchesDesign()
    {
        Assert.AreEqual(6198f, MineralEconomy.RevenuePerTrip(Layer2Bag, BuildLayer("Ice")), 150f);
    }

    [Test]
    public void Layer2_PickBias_StaysUnderLimit()
    {
        float bias = MineralEconomy.PickBias(BuildLayer("Ice"));
        Assert.LessOrEqual(bias, MaxPickBias,
            $"2층 선별 편향 {bias:F3} — Rare 광물의 무게를 올려야 한다 (설계 §3.3)");
    }

    [Test]
    public void Layer2_PerChunkTotal_IsRaisedTo150()
    {
        Assert.AreEqual(150f, MineralEconomy.TotalPerChunk(BuildLayer("Ice")), 2f);
    }

    [Test]
    public void Layer2_RareMineralsAreTheTwoMostExpensive()
    {
        // Rare인데 저가인 광물이 있으면 만나서 손해가 된다 (설계 §5.1)
        var tileDb = LoadTileDb();
        var priceDb = LoadPriceDb();
        TileDataJson tile = tileDb.tiles.Find(t => t != null && t.tileType == "Ice");

        int cheapestRare = int.MaxValue;
        int dearestCommon = 0;
        foreach (var rule in tile.minerals)
        {
            System.Enum.TryParse(rule.mineralType, out MineralID id);
            int price = priceDb.GetPrice(id);
            if (rule.IsRare) cheapestRare = Mathf.Min(cheapestRare, price);
            else dearestCommon = Mathf.Max(dearestCommon, price);
        }

        Assert.Greater(cheapestRare, dearestCommon,
            $"2층 최저가 Rare({cheapestRare}) <= 최고가 Common({dearestCommon}) — Rare는 그 층 최상위여야 한다");
    }

    // ─────────────────────────────────────────────────────────────
    // 층 간 관계 — 설계 문서 §3.2 / §3.5
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void EnteringLayer2WithoutBackpack_StillBeatsLayer1FullBag()
    {
        // 배낭 I 없이 2층에 내려가도 손해가 아니어야 한다. 이 배수가 1 아래로 떨어지면 설계가 깨진 것.
        float layer1Full = MineralEconomy.RevenuePerTrip(Layer1Bag, BuildLayer("Dirt"));
        float layer2NoBag = MineralEconomy.RevenuePerTrip(Layer1Bag, BuildLayer("Ice"));

        Assert.GreaterOrEqual(layer2NoBag / layer1Full, 2.0f,
            $"2층 진입 매출 {layer2NoBag:F0} vs 1층 만재 {layer1Full:F0} — 2.0배 이상이어야 한다 (설계 §3.2)");
    }

    [Test]
    public void LayerRevenueRatio_IsAboutFourAndAHalf()
    {
        float layer1 = MineralEconomy.RevenuePerTrip(Layer1Bag, BuildLayer("Dirt"));
        float layer2 = MineralEconomy.RevenuePerTrip(Layer2Bag, BuildLayer("Ice"));
        float ratio = layer2 / layer1;

        Assert.GreaterOrEqual(ratio, 4.0f, $"층 배수 {ratio:F2} — 지수 곡선이 너무 완만하다");
        Assert.LessOrEqual(ratio, 5.0f, $"층 배수 {ratio:F2} — 지수 곡선이 너무 가파르다");
    }
}
```

- [ ] **Step 3: 테스트가 실패하는지 확인한다**

사람이 Unity Test Runner(EditMode)에서 `MineralBalanceTests`를 실행한다.
기대: **대부분 FAIL**. 현재 데이터는 1층 평균가 33.0 / 무게 1.0, 2층 평균가 84.5 / 무게 1.0 / `perChunk` 31이라 목표와 어긋난다. `Layer1_PerChunkTotal_IsUnchanged`만 PASS한다.

이 실패가 Task 3·4의 작업 지시서다.

- [ ] **Step 4: 체크포인트**

`MineralBalanceTests.cs`, `MineralPriceDatabase.cs`를 UVCS에 체크인한다.

---

### Task 3: 1층 가격·무게 적용

**Files:**
- Modify: `Assets/GameData/ShopData/MineralPriceDatabase.asset`
- Modify: `Assets/GameData/MineralData/Basement_1/ScrapMetal.asset`, `Garbage.asset`, `WaterBottle.asset`, `Coal.asset`, `Copper.asset`, `Iron.asset`

**Interfaces:**
- Consumes: Task 2의 `MineralBalanceTests` 1층 테스트 6개
- Produces: 없음 (데이터 변경)

**에셋 이름과 광물의 대응이 직관적이지 않다.** `Garbage.asset` = 쓰레기봉지(101), `WaterBottle.asset` = 페트병(102)이다.

- [ ] **Step 1: 가격을 고친다**

`Assets/GameData/ShopData/MineralPriceDatabase.asset`의 `prices` 목록에서 해당 `mineralID` 항목의 `price`만 바꾼다. 목록 순서는 건드리지 않는다.

| mineralID | 광물 | 현재 `price` | 새 `price` |
|---|---|---|---|
| 100 | 고철 ScrapMetal | 10 | **7** |
| 101 | 쓰레기봉지 GarbageBag | 20 | **9** |
| 102 | 페트병 PETBottle | 40 | **12** |
| 103 | 석탄 Coal | 40 | **18** |
| 104 | 구리 Copper | 30 | **32** |
| 105 | 철 Iron | 40 | **50** |

- [ ] **Step 2: 무게를 고친다**

각 에셋의 `weight:` 한 줄만 바꾼다.

| 에셋 | 광물 | 현재 | 새 값 |
|---|---|---|---|
| `Basement_1/ScrapMetal.asset` | 고철 | 1 | **0.3** |
| `Basement_1/Garbage.asset` | 쓰레기봉지 | 1 | **0.35** |
| `Basement_1/WaterBottle.asset` | 페트병 | 1 | **0.4** |
| `Basement_1/Coal.asset` | 석탄 | 1 | **0.5** |
| `Basement_1/Copper.asset` | 구리 | 1 | **0.8** |
| `Basement_1/Iron.asset` | 철 | 1 | **1.2** |

- [ ] **Step 3: 1층 테스트가 통과하는지 확인한다**

사람이 Unity에서 에셋을 리임포트한 뒤 Test Runner(EditMode)에서 `MineralBalanceTests`의 `Layer1_*` 6개를 실행한다.

기대: 6개 전부 PASS.
- `Layer1_WeightedAveragePrice_MatchesDesign` → 20.55
- `Layer1_WeightedAverageWeight_MatchesDesign` → 0.5744
- `Layer1_ItemsPerTrip_IsAbout70` → 69.6
- `Layer1_RevenuePerTrip_MatchesDesign` → 1,431
- `Layer1_PickBias_StaysUnderLimit` → 1.165
- `Layer1_PerChunkTotal_IsUnchanged` → 249.5

`Layer2_*`와 층 간 테스트는 아직 FAIL이다(Task 4에서 해결).

- [ ] **Step 4: 체크포인트**

바꾼 에셋 7개를 UVCS에 체크인한다.

---

### Task 4: 2층 가격·무게·산출량·rarity 적용

**Files:**
- Modify: `Assets/GameData/ShopData/MineralPriceDatabase.asset`
- Modify: `Assets/GameData/MineralData/Basement_2/Fossil.asset`, `Silver.asset`, `Meteorite.asset`, `Topaz.asset`, `Emerald.asset`, `Sapphire.asset`
- Modify: `Assets/StreamingAssets/tileData.json`

**Interfaces:**
- Consumes: Task 2의 `MineralBalanceTests` 2층 테스트 6개 + 층 간 테스트 2개
- Produces: 없음 (데이터 변경)

- [ ] **Step 1: 가격을 고친다**

| mineralID | 광물 | 현재 `price` | 새 `price` |
|---|---|---|---|
| 200 | 운석 Meteorite | 100 | **82** |
| 201 | 화석 Fossil | 70 | **47** |
| 202 | 은 Silver | 80 | **63** |
| 203 | 사파이어 Sapphire | 120 | **242** |
| 204 | 에메랄드 Emerald | 40 | **168** |
| 205 | 토파즈 Topaz | 90 | **100** |

- [ ] **Step 2: 무게를 고친다**

| 에셋 | 광물 | 현재 | 새 값 |
|---|---|---|---|
| `Basement_2/Fossil.asset` | 화석 | 1 | **0.65** |
| `Basement_2/Silver.asset` | 은 | 1 | **0.8** |
| `Basement_2/Meteorite.asset` | 운석 | 1 | **0.95** |
| `Basement_2/Topaz.asset` | 토파즈 | 1 | **1.1** |
| `Basement_2/Emerald.asset` | 에메랄드 | 1 | **1.7** |
| `Basement_2/Sapphire.asset` | 사파이어 | 1 | **2.3** |

에메랄드 1.7 / 사파이어 2.3은 오타가 아니다. Rare의 `perChunk` 비중이 8.7%라 가중평균을 끌어올리지 못하는데 가격/무게는 최고라, 무게를 무겁게 잡지 않으면 선별 편향이 1.4까지 뛴다(설계 §3.3).

- [ ] **Step 3: `tileData.json`의 Ice 층 광물 규칙을 고친다**

`tiles` 배열에서 `"tileType": "Ice"`인 원소의 `minerals` 배열을 통째로 아래로 교체한다. **규칙 개수는 8개 그대로다**(`TileDataMineralRuleTests`가 전체 29개를 세고 있으므로 개수가 바뀌면 그 테스트도 깨진다).

```json
"minerals": [
  {"mineralType": "Meteorite", "minDepth": 20, "maxDepth": 39, "rarity": "Common", "perChunk": [22, 38], "rockDropWeight": 0.5, "rockDropCount": [2, 4]},
  {"mineralType": "Fossil", "minDepth": 20, "maxDepth": 39, "rarity": "Common", "perChunk": [22, 38], "rockDropWeight": 0.5, "rockDropCount": [2, 4]},
  {"mineralType": "Silver", "minDepth": 20, "maxDepth": 39, "rarity": "Common", "perChunk": [22, 38], "rockDropWeight": 0.5, "rockDropCount": [2, 4]},
  {"mineralType": "Topaz", "minDepth": 20, "maxDepth": 39, "rarity": "Common", "perChunk": [22, 38], "rockDropWeight": 0.5, "rockDropCount": [2, 4]},
  {"mineralType": "Emerald", "minDepth": 20, "maxDepth": 39, "rarity": "Rare", "perChunk": [8, 18], "rockDropWeight": 0.5, "rockDropCount": [1, 2]},
  {"mineralType": "Sapphire", "minDepth": 20, "maxDepth": 39, "rarity": "Rare", "perChunk": [8, 18], "rockDropWeight": 0.5, "rockDropCount": [1, 2]},
  {"mineralType": "Copper", "minDepth": 20, "maxDepth": 29, "rarity": "Common", "perChunk": [1, 3], "rockDropWeight": 0.08, "rockDropCount": [1, 2]},
  {"mineralType": "Iron", "minDepth": 20, "maxDepth": 29, "rarity": "Common", "perChunk": [1, 3], "rockDropWeight": 0.08, "rockDropCount": [1, 2]}
]
```

바뀐 것은 셋이다.
- `perChunk` 상향 — 청크당 합계 31 → 150
- `Topaz`: `"rarity": "Rare"` → `"Common"`
- `Sapphire`: `"rarity": "Common"` → `"Rare"`

- [ ] **Step 4: 2층·층간 테스트가 통과하는지 확인한다**

사람이 Unity에서 리임포트 후 Test Runner(EditMode)에서 `MineralBalanceTests` 전체 15개와 `TileDataMineralRuleTests`를 실행한다.

기대: **둘 다 전부 PASS.**
- `Layer2_WeightedAveragePrice_MatchesDesign` → 95.03
- `Layer2_WeightedAverageWeight_MatchesDesign` → 1.0733
- `Layer2_ItemsPerTrip_IsAbout65` → 65.2
- `Layer2_RevenuePerTrip_MatchesDesign` → 6,198
- `Layer2_PickBias_StaysUnderLimit` → 1.188
- `Layer2_PerChunkTotal_IsRaisedTo150` → 150
- `Layer2_RareMineralsAreTheTwoMostExpensive` → 최저 Rare 168 > 최고 Common 100
- `EnteringLayer2WithoutBackpack_StillBeatsLayer1FullBag` → 3,542 / 1,431 = 2.48
- `LayerRevenueRatio_IsAboutFourAndAHalf` → 4.33
- `TileDataMineralRuleTests.EveryRule_HasSpawnAndDropFields` → 규칙 29개 유지

- [ ] **Step 5: 체크포인트**

바꾼 에셋 7개와 `tileData.json`을 UVCS에 체크인한다.

---

### Task 5: 업그레이드 트리 재조정

**Files:**
- Modify: `Assets/Scripts/Editor/UpgradeTreeGenerator.cs:203-380`
- Create: `Assets/Tests/EditMode/UpgradeTreeCostTests.cs`

**Interfaces:**
- Consumes: `UpgradeNodeSO`(`nodeId`, `cost`, `tier`, `parentNodes` — `List<UpgradeNodeSO>`), `UpgradeEffectType`
- Produces: 없음 (데이터 명세 변경)

두 가지를 한꺼번에 한다. 둘 다 `GetUpgradePlanData()` 한 메서드 안이고 같은 재생성 사이클을 공유하기 때문이다.

1. T0·T1 노드 비용을 목표 예산에 맞춘다
2. 배낭 I(`InventoryWeightUp` +30)을 T0 → T1으로 옮긴다 (설계 §6.1)

**설계 §10.1의 무효 노드(`InventorySlot_T1_01`)는 이번에 건드리지 않는다.** 효과가 없는 채로 남지만, 노드를 지우거나 효과를 바꾸는 건 별개 판단이라 뒤로 미룬다. 비용 재조정 대상에는 포함한다.

**이동 방식:**
- T0의 `InventoryWeight_T0_01`(가죽 배낭 I) → `WarehouseCapacity_T0_01`(창고 확장 I, `WarehouseCapacityUp` +6)로 **효과 교체**. 창고는 지상 보관함이라 탐험 매출 곡선에 영향이 없다.
- T1에 `InventoryWeight_T1_01`(가죽 배낭 I, `InventoryWeightUp` +30, 5,000G)을 **신규 추가**.

노드 총 개수가 34 → **35개**가 된다.

**선행 참조 갱신:** T0에서 `nodeId`가 바뀌므로 그것을 부모로 참조하던 노드가 깨진다.
- `VisionRadius_T0_01`의 부모 `"InventoryWeight_T0_01"` → `"WarehouseCapacity_T0_01"`

T1은 `InventorySlot_T1_01`을 그대로 두므로 `WarehouseCapacity_T1_01`의 부모 참조는 **손대지 않는다.**

**세이브 호환:** `UpgradeManager`는 `nodeId` 문자열로 해금 상태를 저장한다(`state.unlockedNodeIds`). `InventoryWeight_T0_01`이 사라지므로 **기존 세이브에서 그 노드 해금이 풀린다.** 밸런스를 통째로 갈아엎는 작업이라 기존 세이브는 어차피 무의미하다 — 테스트 플레이는 새 세이브로 시작한다.

- [ ] **Step 1: 트리 명세 검증 테스트를 쓴다**

`Assets/Tests/EditMode/UpgradeTreeCostTests.cs`:

```csharp
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

    private static int SumTier(Dictionary<string, UpgradeNodeSO> nodes, int tier)
    {
        int sum = 0;
        foreach (var n in nodes.Values) if (n.tier == tier) sum += n.cost;
        return sum;
    }

    [Test]
    public void NodeCount_IsThirtyFive()
    {
        // 34개 + T1에 신규 추가한 배낭 I. 노드를 추가/삭제했다면 이 숫자도 갱신할 것.
        Assert.AreEqual(35, LoadNodes().Count);
    }

    [Test]
    public void Tier0_TotalCost_MatchesLayer1Budget()
    {
        // 1층 예산 22,900G 중 트리 몫 15,000G (설계 §7.1)
        Assert.AreEqual(15000, SumTier(LoadNodes(), 0), 500);
    }

    [Test]
    public void Tier1_TotalCost_MatchesLayer2Budget()
    {
        // 2층 예산 198,400G 중 트리 몫 120,000G (설계 §7.2)
        Assert.AreEqual(120000, SumTier(LoadNodes(), 1), 1000);
    }

    [Test]
    public void MiningLicenses_HaveExplicitCosts()
    {
        var nodes = LoadNodes();
        Assert.AreEqual(3500,  nodes["MiningLevel_T0_Final"].cost, "면허 I");
        Assert.AreEqual(25000, nodes["MiningLevel_T1_Final"].cost, "면허 II");
    }

    [Test]
    public void Backpack_MovedToTier1_AndCostsFiveThousand()
    {
        var nodes = LoadNodes();
        Assert.IsTrue(nodes.ContainsKey("InventoryWeight_T1_01"), "배낭 I이 T1에 없다");
        Assert.AreEqual(1, nodes["InventoryWeight_T1_01"].tier);
        Assert.AreEqual(5000, nodes["InventoryWeight_T1_01"].cost);
        Assert.IsFalse(nodes.ContainsKey("InventoryWeight_T0_01"), "배낭 I이 T0에 아직 남아 있다");
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
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

사람이 Test Runner(EditMode)에서 `UpgradeTreeCostTests`를 실행한다.
기대: `EveryParentReference_Resolves`만 PASS, 나머지 6개 FAIL(`NodeCount_IsThirtyFive`는 현재 34개라 실패).

- [ ] **Step 3: T0 노드를 고친다**

`Assets/Scripts/Editor/UpgradeTreeGenerator.cs`의 `GetUpgradePlanData()` TIER 0 블록에서 `cost` 인자(생성자 5번째)를 바꾼다.

| nodeId | 현재 | 새 값 |
|---|---|---|
| `MiningSpeed_T0_01` | 100 | **700** |
| `MiningPower_T0_01` | 150 | **1100** |
| `MoveSpeed_T0_01` | 150 | **1100** |
| `InventoryWeight_T0_01` → `WarehouseCapacity_T0_01` | 150 | **1100** |
| `PickaxeDamage_T0_01` | 250 | **1800** |
| `MaxStaminaMultiplier_T0_01` | 200 | **1400** |
| `MaxStamina_T0_01` | 200 | **1400** |
| `FallDamage_T0_01` | 200 | **1400** |
| `VisionRadius_T0_01` | 200 | **1400** |
| `MiningLevel_T0_Final` | 500 | **3500** |

합계 14,900G.

배낭 노드(4번)를 통째로 아래로 교체한다:

```csharp
        // 4. 창고 확장 I (선행: 채굴 기본기 I)
        //    원래 여기 있던 '가죽 배낭 I'은 T1으로 옮겼다 — 1층에서 사버리면
        //    2층 진입 시 무게 부족을 겪을 기회가 사라진다(설계 §6.1).
        list.Add(new UpgradeNodeData(
            "WarehouseCapacity_T0_01", "창고 확장 I", "베이스캠프 보관 창고 용량이 +6칸 증가합니다.", 0, 1100, new Vector2(300f, -50f),
            new string[] { "MiningSpeed_T0_01" }, UpgradeEffectType.WarehouseCapacityUp, 6f, false
        ));
```

그리고 9번 `VisionRadius_T0_01`의 선행 배열을 바꾼다:

```csharp
            new string[] { "WarehouseCapacity_T0_01" }, UpgradeEffectType.VisionRadiusUp, 1.15f, true
```

- [ ] **Step 4: T1 노드를 고친다**

기존 13개는 비용만 바꾼다(면허 II는 직접 지정).

| nodeId | 현재 | 새 값 |
|---|---|---|
| `MiningSpeed_T1_01` | 400 | **5200** |
| `DrillCapacity_T1_01` | 500 | **6500** |
| `StaminaCost_T1_01` | 450 | **5900** |
| `InventorySlot_T1_01` | 500 | **6500** |
| `MineralExtraDrop_T1_01` | 500 | **6500** |
| `ClimbSpeed_T1_01` | 450 | **5900** |
| `DrillRegen_T1_01` | 600 | **7800** |
| `ShovelStamina_T1_01` | 550 | **7200** |
| `WarehouseCapacity_T1_01` | 600 | **7800** |
| `MineralSell_T1_01` | 700 | **9100** |
| `WallClimbSpeed_T1_01` | 550 | **7200** |
| `FlashlightRange_T1_01` | 500 | **6500** |
| `MiningCooldown_T1_01` | 600 | **7800** |
| `MiningLevel_T1_Final` | 1200 | **25000** |

`InventorySlot_T1_01`은 효과가 없는 채로 남지만 비용은 함께 올린다 — 안 올리면 트리 안에서 혼자 500G라 눈에 띄게 어색하다. 이 노드의 처리는 뒤로 미룬 사안이다(설계 §10.1).

그리고 **배낭 I을 신규 노드로 추가한다.** TIER 1 블록 끝(23번 `MiningCooldown_T1_01` 뒤, 24번 면허 II 앞)에 넣는다:

```csharp
        // 23-B. 가죽 배낭 I (선행: 심층 채광 숙련) — T0에서 옮겨온 노드
        //     2층 광물은 1층보다 2배 무겁다(평균 0.57 → 1.07). 이걸 사기 전에는
        //     가방 40으로 37개밖에 못 담는다 — 그 부족함을 겪고 사서 해소하는 것이
        //     설계 의도다(설계 §3.2). 그래서 면허의 선행으로 묶지 않는다.
        list.Add(new UpgradeNodeData(
            "InventoryWeight_T1_01", "가죽 배낭 I", "인벤토리 무게 한도가 +30 증가합니다.", 1, 5000, new Vector2(600f, 650f),
            new string[] { "MiningSpeed_T1_01" }, UpgradeEffectType.InventoryWeightUp, 30f, false
        ));
```

`uiPosition`을 `(600, 650)`으로 잡은 이유는 `MiningSpeed_T1_01`의 기존 자식들이 x = −400 / −200 / 0 / 200 / 400의 y = 650에 늘어서 있어서 그 오른쪽 끝이 비어 있기 때문이다. `(600, 800)`에는 `MiningCooldown_T1_01`이 있으므로 겹치지 않는다.

T1 합계 = 기존 13개 89,900 + 배낭 I 5,000 + 면허 II 25,000 = **119,900G**.

**T2(25~34번)는 건드리지 않는다.** 3층 작업 범위다(설계 §8).

- [ ] **Step 5: 트리를 재생성한다**

사람이 Unity에서 `Tools/Upgrade/Upgrade Tree Generator`를 연다. "기존 생성된 에셋 삭제 후 재생성"을 켠 채로 실행한다.

- [ ] **Step 6: 테스트가 통과하는지 확인한다**

사람이 Test Runner(EditMode)에서 `UpgradeTreeCostTests` 7개를 실행한다.
기대: 전부 PASS. 노드 35개 / T0 합계 14,900 / T1 합계 119,900.

- [ ] **Step 7: 체크포인트**

`UpgradeTreeGenerator.cs`, `UpgradeTreeCostTests.cs`, 재생성된 `Assets/GameData/UpgradeData/` 전체를 UVCS에 체크인한다.

---

### Task 6: 상점 가격 — 장비와 포션

**Files:**
- Modify: `Assets/GameData/ShopData/ShopItemDatabase.asset`

**Interfaces:**
- Consumes: 없음
- Produces: 없음 (데이터 변경)

장비 27종이 전부 1,000G 플랫이라 1층에서는 못 사고 2층 이후로는 무의미하다. 1·2층 구간에서 실제로 살 장비만 가격을 매기고, 나머지는 잠근다.

- [ ] **Step 1: 1층·2층 장비 가격을 매긴다**

`shopItems` 목록에서 `equipmentID`로 항목을 찾아 `price`를 바꾼다.

| equipmentID | 장비 | 구간 | 현재 | 새 값 |
|---|---|---|---|---|
| 3004 | 광부의 헬멧 | 1층 | 1000 | **2000** |
| 3104 | 광부의 복장 | 1층 | 1000 | **2000** |
| 3204 | 광부의 부츠 | 1층 | 1000 | **2000** |
| 3006 | 방한 모자 | 2층 | 1000 | **12000** |
| 3106 | 방한복 | 2층 | 1000 | **12000** |
| 3206 | 방한 부츠 | 2층 | 1000 | **12000** |

- [ ] **Step 2: 3·4층 장비를 잠근다**

나머지 7세트는 1,000G 그대로 두면 2층에서 12,000G짜리 방한 장비보다 싸서 밸런스가 뒤집힌다. 가격을 매길 때까지 판매 목록에서 뺀다.

아래 `equipmentID` 항목의 `isAvailable`을 `1` → `0`으로 바꾼다.

```
3003 3103 3203   (Climber)
3005 3105 3205   (Carrier)
3007 3107 3207   (Heat)
3008 3108 3208   (Engineer)
3009 3109 3209   (Scientist)
3010 3110 3210   (Shovel)
3011 3111 3211   (Marathon)
```

3·4층 작업 때 가격과 함께 다시 연다.

- [ ] **Step 3: 2층 포션 가격을 매긴다**

2층은 동상(`zoneStatusType: "Frostbite"`, 초당 0.5)이 쌓이므로 `FrostbiteResist`(itemID 1001)가 입장료 역할을 한다. 효과 지속이 180초라 탐험 1회에 2~3개를 쓴다고 보고, 회당 400~600G가 되도록 잡는다.

| itemID | 아이템 | 현재 `price` | 새 `price` |
|---|---|---|---|
| 1001 | 동상 저항 FrostbiteResist | 10 | **200** |

`BurnResist`(1002, 15G)는 3층(화상)용이라 **이번에는 건드리지 않는다.**

- [ ] **Step 4: 상점에서 눈으로 확인한다**

사람이 플레이 모드로 상점을 연다. 확인할 것:
- 광부 세트 3종이 2,000G로 보인다
- 방한 세트 3종이 12,000G로 보인다
- 나머지 7세트가 목록에 안 보인다
- 동상 저항이 200G로 보인다

- [ ] **Step 5: 체크포인트**

`ShopItemDatabase.asset`을 UVCS에 체크인한다.

---

## 실행 후 남는 것

계획대로 끝나면 이렇게 된다.

- 1·2층 밸런스가 EditMode 테스트 **33개**(경제 11 + 밸런스 15 + 트리 7)로 고정된다
- 3·4층은 손대지 않은 상태로 남는다 — 설계 문서 §8의 원칙만 있고 수치는 미정
- 4층은 여전히 **도달 불가**다(면허 III 노드 없음). 이건 원래 그랬고 이번 범위가 아니다

검증은 설계 문서 §11의 표를 따른다. **핵심 확인 질문은 "층당 4.33배라는 지수 증가가 체감상 적절한가"**이고, 이것을 확인하려고 1·2층만 먼저 만든다.

## 이번에 일부러 안 하는 것

나중에 판단할 사안이라 범위에서 뺐다. 설계 문서에는 남아 있으니 없어지지 않는다.

| 항목 | 근거 | 왜 미뤘나 |
|---|---|---|
| **가방 부족 안내 UI** | 설계 §6.2 | 배낭의 존재를 모른 채 반토막 매출로 노는 걸 막는 안전장치다. 다만 "층 진입 후 첫 만재"를 조건으로 잡아야 정확한데 그러면 층 판정 참조가 붙는다. 남에게 테스트를 시켜봐서 실제로 헤매는지 확인한 뒤 제대로 넣는 게 낫다 |
| **무효 노드 `InventorySlot_T1_01` 처리** | 설계 §10.1 | `InventorySlotUp`은 소비처가 없어 아무 효과가 없다. 슬롯 제한을 실제로 구현할지, 효과를 갈아끼울지가 별개 판단이라 미뤘다. **이번엔 비용만 6,500G로 올려 트리 안에서 튀지 않게만 해둔다** |
| **3층 용암석 Rare 저가 문제** | 설계 §10.2 | 3층 작업 때 그 층 가격표와 함께 고친다 |
| **면허 III · T3 트리** | 설계 §8 | 4층 작업 범위. 4층 예산이 전체의 74%라 티어 하나를 통째로 신설해야 한다 |
