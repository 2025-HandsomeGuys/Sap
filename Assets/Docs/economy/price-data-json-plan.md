# 가격 데이터 JSON 외부화 구현 계획

> **에이전트 작업자에게:** 이 계획은 `superpowers:subagent-driven-development` 또는 `superpowers:executing-plans`로 태스크 단위 실행한다. 체크박스(`- [ ]`)로 진행을 추적한다.

**설계 문서:** [price-data-json-design.md](price-data-json-design.md) — 근거는 전부 여기 있다. 계획과 문서가 어긋나면 **문서가 정답**이다.

**Goal:** 흩어진 52개 파일의 가격 데이터를 `StreamingAssets/priceData.json` 하나로 모으고, 런타임에 그 값이 ScriptableObject를 덮어쓰게 한다.

**Architecture:** `PriceDataLoader`(실행 순서 -250)가 JSON을 읽어 SO 필드에 직접 써넣는다. 값을 읽는 쪽 코드는 바뀌지 않는다 — `weight`는 프로퍼티 경유, `cost`·`price`는 필드 직접 읽기라 어느 쪽이든 필드에 쓰면 반영된다. 파싱·적용 로직은 `MonoBehaviour`에서 분리한 순수 클래스에 두어 EditMode에서 테스트한다.

**Tech Stack:** Unity 2D / C# / `JsonUtility` / NUnit (EditMode) / StreamingAssets JSON

## Global Constraints

- **UVCS 프로젝트다. `git` 명령을 일절 쓰지 않는다.** 체크인은 UVCS로 한다.
- **Unity Test Runner 실행은 사람이 한다.** Claude는 테스트 파일 작성·수정만 하고 `mcp__mcp-unity__run_tests` 등을 호출하지 않는다.
- **`JsonUtility`는 딕셔너리를 지원하지 않는다.** 배열만 쓴다. `Dictionary<,>` 필드를 DTO에 넣으면 조용히 비어서 돌아온다.
- 모든 C# 스크립트 첫 줄에 `// @tags: ...` 주석을 단다.
- `.meta` 파일을 직접 만들지 않는다. Unity가 생성한다.
- 새 EditMode 테스트는 `Assets/Tests/EditMode/`에 둔다. `EditModeTests.asmdef`는 수정하지 않는다.
- **`isAvailable`·`stock`은 JSON에 넣지 않는다.** 가격만 다룬다(설계 §3.2).
- **장비 `statModifiers`를 채우지 않는다.** 이번 범위가 아니다.
- `PriceDataLoader`의 실행 순서는 **-250**. 현재 가장 이른 것이 `ToolConfigLoader`·`WorldSettingsLoader`의 -200이라 비어 있다.
- JSON 항목 수 (설계 §3.1):

| 절 | 대상 | 개수 |
|---|---|---|
| `minerals` | `MineralID` — `price`와 `weight` | 23 |
| `shopItems` | `ItemID` | 2 |
| `shopEquipment` | `EquipmentID` | 28 |
| `shopRelics` | `RelicID` | 28 |
| `relicUpgrades` | `RelicID` — `gold` 배열 길이 2 | 27 (4001 제외) |
| `upgradeNodes` | `nodeId` 문자열 | 35 |
| | | **143** |

- 불변식: JSON에 없는 ID는 **건너뛴다**(에셋 값 유지). 모르는 ID는 **경고 후 무시**한다(예외를 던지지 않는다).

---

## File Structure

| 파일 | 책임 | 신규/수정 |
|---|---|---|
| `Assets/Scripts/_Core/Data/PriceData.cs` | JSON DTO 6종 + `PriceApplyResult`. 순수 데이터, Unity 의존 최소 | 신규 |
| `Assets/Scripts/_Core/Data/PriceApplier.cs` | 적용 로직 순수 클래스. SO 참조를 받아 필드에 써넣고 결과를 보고한다 | 신규 |
| `Assets/Scripts/_Core/Data/PriceDataLoader.cs` | 파일 읽기 + SO 인스펙터 참조 + `PriceApplier` 호출 | 신규 |
| `Assets/Scripts/Editor/PriceDataExporter.cs` | `Tools/Economy/Export Prices to JSON` | 신규 |
| `Assets/StreamingAssets/priceData.json` | 143개 항목. **툴로 생성** | 신규 |
| `Assets/Scripts/UI/Items/Minerals/Mineral/MineralPriceDatabase.cs` | `ApplyPriceOverrides` 메서드 추가 | 수정 |
| `Assets/Scripts/UI/Upgrade/EquipmentUpgradeOverlayUI.cs` | 유물 강화 골드비를 `upgradeCosts`에서 읽도록 배선 (2곳) | 수정 |
| `Assets/Tests/EditMode/PriceApplierTests.cs` | 파싱·적용 로직 검증 | 신규 |
| `Assets/Tests/EditMode/MineralBalanceTests.cs` | 입력 출처를 에셋 → JSON | 수정 |
| `Assets/Tests/EditMode/ShopPriceTests.cs` | 입력 출처를 에셋 → JSON | 수정 |

**책임 분리가 핵심이다.** `PriceApplier`가 순수 클래스인 이유는 EditMode에서 합성 SO를 만들어 테스트하기 위해서다. `PriceDataLoader`는 파일 I/O와 인스펙터 참조만 담당해 테스트 대상에서 뺀다.

---

### Task 1: JSON DTO와 적용 로직

**Files:**
- Create: `Assets/Scripts/_Core/Data/PriceData.cs`
- Create: `Assets/Scripts/_Core/Data/PriceApplier.cs`
- Test: `Assets/Tests/EditMode/PriceApplierTests.cs`

**Interfaces:**
- Consumes: `MineralID`·`ItemID`·`EquipmentID` (`Assets/Scripts/_Core/Data/Enums.cs`, `Assets/Scripts/UI/Items/Equipments/EquipmentID.cs`), `Relic.Data.RelicID`·`RelicSO`·`RelicDatabase`, `MineralSO`·`MineralDatabase`·`MineralPriceDatabase`, `ShopItemDatabase`·`ShopItemData`·`ShopItemType`, `UpgradeTreeSO`·`UpgradeNodeSO`
- Produces:
  - `[Serializable] class PriceData` — `minerals`·`shopItems`·`shopEquipment`·`shopRelics`·`relicUpgrades`·`upgradeNodes` 배열
  - `class PriceApplyResult` — `applied`(int), `skipped`(int), `warnings`(`List<string>`)
  - `static class PriceApplier` — `PriceApplyResult Apply(PriceData data, PriceApplyTargets targets)`
  - `class PriceApplyTargets` — 적용 대상 SO 묶음

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/Tests/EditMode/PriceApplierTests.cs`:

```csharp
// @tags: test, editmode, price, json, loader, economy
using System.Collections.Generic;
using NUnit.Framework;
using Relic.Data;
using UnityEngine;

/// <summary>
/// PriceApplier의 적용 로직 검증. 합성 ScriptableObject를 만들어 쓰고,
/// 프로젝트의 실제 에셋은 건드리지 않는다.
///
/// 설계: Assets/Docs/economy/price-data-json-design.md §5, §7.2
/// </summary>
public class PriceApplierTests
{
    private readonly List<Object> _created = new List<Object>();

    private T Make<T>() where T : ScriptableObject
    {
        var so = ScriptableObject.CreateInstance<T>();
        _created.Add(so);
        return so;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var o in _created) Object.DestroyImmediate(o);
        _created.Clear();
    }

    /// <summary>광물 2종·상점 3종·유물강화 1종·노드 1개를 가진 최소 타깃.</summary>
    private PriceApplyTargets MakeTargets(
        out MineralSO coal, out MineralSO iron,
        out ShopItemDatabase shop, out RelicSO magnet, out UpgradeNodeSO node)
    {
        coal = Make<MineralSO>();
        coal.mineralID = MineralID.Coal;
        coal.weight = 1f;

        iron = Make<MineralSO>();
        iron.mineralID = MineralID.Iron;
        iron.weight = 1f;

        // InitializeDictionary()를 부르지 않는다 — CreateInstance 시점의 OnEnable이 이미
        // 빈 딕셔너리를 굽고 _initialized를 true로 만들어 재구축이 안 된다.
        // PriceApplier는 그래서 allMinerals 리스트를 직접 본다.
        var mineralDb = Make<MineralDatabase>();
        mineralDb.allMinerals = new List<MineralSO> { coal, iron };

        var priceDb = Make<MineralPriceDatabase>();
        priceDb.ApplyPriceOverrides(new Dictionary<MineralID, int>
        {
            { MineralID.Coal, 1 },
            { MineralID.Iron, 1 },
        });

        shop = Make<ShopItemDatabase>();
        shop.shopItems = new List<ShopItemData>
        {
            new ShopItemData { itemType = ShopItemType.Item,      itemID = ItemID.FrostbiteResist, price = 1 },
            new ShopItemData { itemType = ShopItemType.Equipment, equipmentID = EquipmentID.MinerHelmet, price = 1 },
            new ShopItemData { itemType = ShopItemType.Relic,     relicID = RelicID.Magnet, price = 1 },
        };

        magnet = Make<RelicSO>();
        magnet.id = RelicID.Magnet;
        magnet.maxLevel = 3;
        magnet.upgradeCosts = new[]
        {
            new RelicUpgradeCost { gold = 1 },
            new RelicUpgradeCost { gold = 1 },
        };
        var relicDb = Make<RelicDatabase>();
        relicDb.allRelics = new List<RelicSO> { magnet };

        node = Make<UpgradeNodeSO>();
        node.nodeId = "MiningSpeed_T0_01";
        node.cost = 1;
        var tree = Make<UpgradeTreeSO>();
        tree.allNodes = new List<UpgradeNodeSO> { node };

        return new PriceApplyTargets
        {
            mineralPrices = priceDb,
            minerals      = mineralDb,
            shop          = shop,
            relics        = relicDb,
            upgradeTree   = tree,
        };
    }

    private static PriceData FullData()
    {
        return new PriceData
        {
            minerals = new[]
            {
                new MineralPriceEntry { id = "Coal", price = 18, weight = 0.5f },
                new MineralPriceEntry { id = "Iron", price = 50, weight = 1.2f },
            },
            shopItems     = new[] { new IdPriceEntry { id = "FrostbiteResist", price = 200 } },
            shopEquipment = new[] { new IdPriceEntry { id = "MinerHelmet",     price = 2000 } },
            shopRelics    = new[] { new IdPriceEntry { id = "Magnet",          price = 12000 } },
            relicUpgrades = new[] { new RelicUpgradeEntry { id = "Magnet", gold = new[] { 6000, 18000 } } },
            upgradeNodes  = new[] { new NodeCostEntry { id = "MiningSpeed_T0_01", cost = 700 } },
        };
    }

    [Test]
    public void Apply_WritesMineralWeightAndPrice()
    {
        var t = MakeTargets(out var coal, out var iron, out _, out _, out _);
        PriceApplier.Apply(FullData(), t);

        Assert.AreEqual(0.5f, coal.weight, 0.0001f);
        Assert.AreEqual(1.2f, iron.weight, 0.0001f);
        Assert.AreEqual(18, t.mineralPrices.GetPrice(MineralID.Coal));
        Assert.AreEqual(50, t.mineralPrices.GetPrice(MineralID.Iron));
    }

    [Test]
    public void Apply_WritesShopPricesForAllThreeKinds()
    {
        var t = MakeTargets(out _, out _, out var shop, out _, out _);
        PriceApplier.Apply(FullData(), t);

        Assert.AreEqual(200,   shop.shopItems.Find(i => i.itemType == ShopItemType.Item).price);
        Assert.AreEqual(2000,  shop.shopItems.Find(i => i.itemType == ShopItemType.Equipment).price);
        Assert.AreEqual(12000, shop.shopItems.Find(i => i.itemType == ShopItemType.Relic).price);
    }

    [Test]
    public void Apply_WritesRelicUpgradeGold()
    {
        var t = MakeTargets(out _, out _, out _, out var magnet, out _);
        PriceApplier.Apply(FullData(), t);

        Assert.AreEqual(6000,  magnet.upgradeCosts[0].gold);
        Assert.AreEqual(18000, magnet.upgradeCosts[1].gold);
    }

    [Test]
    public void Apply_WritesUpgradeNodeCost()
    {
        var t = MakeTargets(out _, out _, out _, out _, out var node);
        PriceApplier.Apply(FullData(), t);

        Assert.AreEqual(700, node.cost);
    }

    [Test]
    public void Apply_LeavesAbsentIdsUntouched()
    {
        // JSON에 Iron이 없으면 Iron은 에셋 값 그대로여야 한다
        var t = MakeTargets(out var coal, out var iron, out _, out _, out _);
        var data = new PriceData
        {
            minerals = new[] { new MineralPriceEntry { id = "Coal", price = 18, weight = 0.5f } },
        };

        PriceApplier.Apply(data, t);

        Assert.AreEqual(0.5f, coal.weight, 0.0001f);
        Assert.AreEqual(1f,   iron.weight, 0.0001f, "JSON에 없는 광물은 건드리면 안 된다");
        Assert.AreEqual(1,    t.mineralPrices.GetPrice(MineralID.Iron));
    }

    [Test]
    public void Apply_UnknownIdWarnsButDoesNotThrow()
    {
        var t = MakeTargets(out _, out _, out _, out _, out _);
        var data = new PriceData
        {
            minerals     = new[] { new MineralPriceEntry { id = "NotAMineral", price = 5, weight = 5f } },
            upgradeNodes = new[] { new NodeCostEntry { id = "NoSuchNode", cost = 5 } },
        };

        PriceApplyResult result = null;
        Assert.DoesNotThrow(() => result = PriceApplier.Apply(data, t));
        Assert.AreEqual(2, result.warnings.Count, "모르는 id 2건이 경고로 남아야 한다");
        Assert.AreEqual(0, result.applied);
    }

    [Test]
    public void Apply_NullDataIsSafe()
    {
        var t = MakeTargets(out _, out _, out _, out _, out _);
        PriceApplyResult result = null;
        Assert.DoesNotThrow(() => result = PriceApplier.Apply(null, t));
        Assert.AreEqual(0, result.applied);
    }

    [Test]
    public void Apply_NullSectionsAreSafe()
    {
        // JsonUtility는 JSON에 없는 배열 필드를 null로 남긴다
        var t = MakeTargets(out _, out _, out _, out _, out _);
        Assert.DoesNotThrow(() => PriceApplier.Apply(new PriceData(), t));
    }

    [Test]
    public void Apply_ShortRelicGoldArrayWarnsAndSkips()
    {
        var t = MakeTargets(out _, out _, out _, out var magnet, out _);
        var data = new PriceData
        {
            relicUpgrades = new[] { new RelicUpgradeEntry { id = "Magnet", gold = new[] { 6000 } } },
        };

        PriceApplyResult result = null;
        Assert.DoesNotThrow(() => result = PriceApplier.Apply(data, t));
        Assert.AreEqual(1, magnet.upgradeCosts[0].gold, "길이가 안 맞으면 아무것도 쓰지 않는다");
        Assert.AreEqual(1, magnet.upgradeCosts[1].gold);
        Assert.AreEqual(1, result.warnings.Count);
    }

    [Test]
    public void Apply_MissingTargetsAreSafe()
    {
        // 인스펙터 참조를 안 물린 채 실행돼도 예외가 나면 안 된다
        var empty = new PriceApplyTargets();
        PriceApplyResult result = null;
        Assert.DoesNotThrow(() => result = PriceApplier.Apply(FullData(), empty));
        Assert.Greater(result.warnings.Count, 0);
    }

    [Test]
    public void Apply_CountsAppliedEntries()
    {
        var t = MakeTargets(out _, out _, out _, out _, out _);
        var result = PriceApplier.Apply(FullData(), t);

        // 광물 2 + 소모품 1 + 장비 1 + 유물 1 + 유물강화 1 + 노드 1 = 7
        Assert.AreEqual(7, result.applied);
        Assert.AreEqual(0, result.warnings.Count);
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

사람이 Unity Test Runner(EditMode)에서 실행한다. Claude는 실행하지 않는다.
기대: **컴파일 실패** — `PriceData`, `PriceApplier`, `PriceApplyTargets`, `MineralPriceDatabase.ApplyPriceOverrides` 미정의.

- [ ] **Step 3: DTO를 작성한다**

`Assets/Scripts/_Core/Data/PriceData.cs`:

```csharp
// @tags: price, data, json, dto, economy, balance
using System;
using System.Collections.Generic;

/// <summary>광물 한 종의 가격과 무게. id는 MineralID enum 이름.</summary>
[Serializable]
public class MineralPriceEntry
{
    public string id;
    public int    price;
    public float  weight;
}

/// <summary>id ↔ 가격 한 쌍. 소모품·장비·유물 상점가에 공용.</summary>
[Serializable]
public class IdPriceEntry
{
    public string id;
    public int    price;
}

/// <summary>유물 강화비. gold 길이는 maxLevel - 1 (=2).</summary>
[Serializable]
public class RelicUpgradeEntry
{
    public string id;
    public int[]  gold;
}

/// <summary>업그레이드 노드 비용. id는 enum이 아니라 nodeId 문자열.</summary>
[Serializable]
public class NodeCostEntry
{
    public string id;
    public int    cost;
}

/// <summary>
/// StreamingAssets/priceData.json 의 스키마.
///
/// JsonUtility는 Dictionary를 지원하지 않으므로 전부 배열이다.
/// JSON에 없는 절은 null로 남으므로 소비 측이 null을 견뎌야 한다.
///
/// 설계: Assets/Docs/economy/price-data-json-design.md §3
/// </summary>
[Serializable]
public class PriceData
{
    public MineralPriceEntry[] minerals;
    public IdPriceEntry[]      shopItems;
    public IdPriceEntry[]      shopEquipment;
    public IdPriceEntry[]      shopRelics;
    public RelicUpgradeEntry[] relicUpgrades;
    public NodeCostEntry[]     upgradeNodes;
}

/// <summary>적용 결과 요약. 로그와 테스트가 함께 쓴다.</summary>
public class PriceApplyResult
{
    public int applied;
    public readonly List<string> warnings = new List<string>();
}
```

- [ ] **Step 4: 적용 대상과 적용 로직을 작성한다**

`Assets/Scripts/_Core/Data/PriceApplier.cs`:

```csharp
// @tags: price, apply, json, override, economy, pure-logic
using System;
using System.Collections.Generic;
using Relic.Data;

/// <summary>PriceApplier가 값을 써넣을 대상 묶음. 어느 것이든 null일 수 있다.</summary>
public class PriceApplyTargets
{
    public MineralPriceDatabase mineralPrices;
    public MineralDatabase      minerals;
    public ShopItemDatabase     shop;
    public RelicDatabase        relics;
    public UpgradeTreeSO        upgradeTree;
}

/// <summary>
/// priceData.json 의 값을 ScriptableObject 필드에 써넣는다.
///
/// MonoBehaviour에서 분리한 이유는 EditMode에서 합성 SO로 테스트하기 위해서다.
/// 파일 I/O와 인스펙터 참조는 PriceDataLoader가 담당한다.
///
/// 규칙 (설계 §3.2):
///   - JSON에 없는 id는 건드리지 않는다 (에셋 값 유지)
///   - 모르는 id는 경고만 남기고 넘어간다 (예외를 던지지 않는다)
///
/// 설계: Assets/Docs/economy/price-data-json-design.md §5
/// </summary>
public static class PriceApplier
{
    public static PriceApplyResult Apply(PriceData data, PriceApplyTargets targets)
    {
        var result = new PriceApplyResult();
        if (data == null || targets == null) return result;

        ApplyMinerals(data.minerals, targets, result);
        ApplyShop(data.shopItems,     ShopItemType.Item,      targets, result);
        ApplyShop(data.shopEquipment, ShopItemType.Equipment, targets, result);
        ApplyShop(data.shopRelics,    ShopItemType.Relic,     targets, result);
        ApplyRelicUpgrades(data.relicUpgrades, targets, result);
        ApplyNodes(data.upgradeNodes, targets, result);

        return result;
    }

    private static void ApplyMinerals(MineralPriceEntry[] entries, PriceApplyTargets t, PriceApplyResult r)
    {
        if (entries == null || entries.Length == 0) return;

        var priceOverrides = new Dictionary<MineralID, int>();

        foreach (var e in entries)
        {
            if (!Enum.TryParse(e.id, out MineralID id) || id == MineralID.None)
            {
                r.warnings.Add($"minerals: 알 수 없는 id '{e.id}'");
                continue;
            }

            priceOverrides[id] = e.price;

            if (t.minerals == null || t.minerals.allMinerals == null)
            {
                r.warnings.Add($"minerals: MineralDatabase 참조가 없어 '{e.id}' 무게를 적용 못 함");
                continue;
            }

            // GetMineralByID를 쓰지 않는 이유: MineralDatabase는 OnEnable에서 딕셔너리를 굽는데
            // 그 뒤에 allMinerals가 채워진 인스턴스(테스트의 CreateInstance 등)에서는 딕셔너리가
            // 비어 있어도 _initialized가 true라 재구축을 건너뛴다. 리스트를 직접 본다.
            var so = t.minerals.allMinerals.Find(m => m != null && m.mineralID == id);
            if (so == null)
            {
                r.warnings.Add($"minerals: MineralDatabase에 '{e.id}' 없음");
                continue;
            }

            so.weight = e.weight;
            r.applied++;
        }

        if (priceOverrides.Count == 0) return;

        if (t.mineralPrices == null)
        {
            r.warnings.Add("minerals: MineralPriceDatabase 참조가 없어 가격을 적용 못 함");
            return;
        }
        t.mineralPrices.ApplyPriceOverrides(priceOverrides);
    }

    private static void ApplyShop(IdPriceEntry[] entries, ShopItemType kind, PriceApplyTargets t, PriceApplyResult r)
    {
        if (entries == null || entries.Length == 0) return;

        if (t.shop == null || t.shop.shopItems == null)
        {
            r.warnings.Add($"{kind}: ShopItemDatabase 참조가 없어 적용 못 함");
            return;
        }

        foreach (var e in entries)
        {
            ShopItemData row = null;

            switch (kind)
            {
                case ShopItemType.Item:
                    if (!Enum.TryParse(e.id, out ItemID itemId)) { r.warnings.Add($"shopItems: 알 수 없는 id '{e.id}'"); continue; }
                    row = t.shop.shopItems.Find(i => i.itemType == kind && i.itemID == itemId);
                    break;

                case ShopItemType.Equipment:
                    if (!Enum.TryParse(e.id, out EquipmentID eqId)) { r.warnings.Add($"shopEquipment: 알 수 없는 id '{e.id}'"); continue; }
                    row = t.shop.shopItems.Find(i => i.itemType == kind && i.equipmentID == eqId);
                    break;

                case ShopItemType.Relic:
                    if (!Enum.TryParse(e.id, out RelicID relicId)) { r.warnings.Add($"shopRelics: 알 수 없는 id '{e.id}'"); continue; }
                    row = t.shop.shopItems.Find(i => i.itemType == kind && i.relicID == relicId);
                    break;
            }

            if (row == null)
            {
                r.warnings.Add($"{kind}: 상점 목록에 '{e.id}' 항목 없음");
                continue;
            }

            row.price = e.price;
            r.applied++;
        }
    }

    private static void ApplyRelicUpgrades(RelicUpgradeEntry[] entries, PriceApplyTargets t, PriceApplyResult r)
    {
        if (entries == null || entries.Length == 0) return;

        if (t.relics == null || t.relics.allRelics == null)
        {
            r.warnings.Add("relicUpgrades: RelicDatabase 참조가 없어 적용 못 함");
            return;
        }

        foreach (var e in entries)
        {
            if (!Enum.TryParse(e.id, out RelicID id) || id == RelicID.None)
            {
                r.warnings.Add($"relicUpgrades: 알 수 없는 id '{e.id}'");
                continue;
            }

            // GetRelicByID를 쓰지 않는 이유는 ApplyMinerals의 주석과 같다 — OnEnable 이후에
            // allRelics가 채워진 인스턴스에서 딕셔너리가 비어 있을 수 있다.
            var so = t.relics.allRelics.Find(x => x != null && x.id == id);
            if (so == null)
            {
                r.warnings.Add($"relicUpgrades: RelicDatabase에 '{e.id}' 없음");
                continue;
            }

            if (so.upgradeCosts == null || e.gold == null || e.gold.Length != so.upgradeCosts.Length)
            {
                int want = so.upgradeCosts != null ? so.upgradeCosts.Length : 0;
                int got  = e.gold != null ? e.gold.Length : 0;
                r.warnings.Add($"relicUpgrades: '{e.id}' gold 길이 불일치 (에셋 {want}, JSON {got}) — 건너뜀");
                continue;
            }

            for (int i = 0; i < e.gold.Length; i++)
                so.upgradeCosts[i].gold = e.gold[i];

            r.applied++;
        }
    }

    private static void ApplyNodes(NodeCostEntry[] entries, PriceApplyTargets t, PriceApplyResult r)
    {
        if (entries == null || entries.Length == 0) return;

        if (t.upgradeTree == null)
        {
            r.warnings.Add("upgradeNodes: UpgradeTreeSO 참조가 없어 적용 못 함");
            return;
        }

        foreach (var e in entries)
        {
            var node = t.upgradeTree.GetNodeById(e.id);
            if (node == null)
            {
                r.warnings.Add($"upgradeNodes: 트리에 '{e.id}' 노드 없음");
                continue;
            }

            node.cost = e.cost;
            r.applied++;
        }
    }
}
```

- [ ] **Step 5: `MineralPriceDatabase`에 오버라이드 메서드를 추가한다**

`Assets/Scripts/UI/Items/Minerals/Mineral/MineralPriceDatabase.cs`의 `GetPrice` 아래에 넣는다. **기존 메서드와 `PopulatePrices()`를 건드리지 마라.**

```csharp
    /// <summary>
    /// priceData.json 의 값으로 가격을 덮어쓴다. 목록에 없는 광물은 기존 값을 유지한다.
    /// 설계: Assets/Docs/economy/price-data-json-design.md §5
    /// </summary>
    public void ApplyPriceOverrides(Dictionary<MineralID, int> overrides)
    {
        if (overrides == null) return;
        if (_priceMap == null) BuildMap();

        foreach (var kv in overrides)
            _priceMap[kv.Key] = kv.Value;
    }
```

`BuildMap()`은 직전 광물 가격 작업에서 이미 추가된 private 메서드다. 없으면 `OnEnable`의 맵 구축 코드를 `BuildMap()`으로 추출하고 `OnEnable`이 그것을 호출하게 하라.

- [ ] **Step 6: 테스트가 통과하는지 확인한다**

사람이 Unity Test Runner(EditMode)에서 `PriceApplierTests` 11개를 실행한다.
기대: 전부 PASS.

- [ ] **Step 7: 체크포인트**

`PriceData.cs`, `PriceApplier.cs`, `PriceApplierTests.cs`, `MineralPriceDatabase.cs`를 UVCS에 체크인한다. **`git` 명령을 쓰지 않는다.**

---

### Task 2: 런타임 로더

**Files:**
- Create: `Assets/Scripts/_Core/Data/PriceDataLoader.cs`

**Interfaces:**
- Consumes: Task 1의 `PriceData`·`PriceApplier`·`PriceApplyTargets`·`PriceApplyResult`
- Produces: `PriceDataLoader` — 씬에 배치하는 MonoBehaviour. `public static PriceDataLoader Instance`

- [ ] **Step 1: 로더를 작성한다**

`Assets/Scripts/_Core/Data/PriceDataLoader.cs`:

```csharp
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
```

- [ ] **Step 2: 컴파일을 확인한다**

사람이 Unity에서 컴파일 오류가 없는지 확인한다. `PriceDataLoader`는 파일 I/O와 인스펙터 참조만 담당하므로 EditMode 테스트 대상이 아니다 — 적용 로직은 Task 1의 `PriceApplierTests`가 이미 검증한다.

- [ ] **Step 3: 씬에 배치한다 (사람 몫)**

사람이 Unity에서:
1. 게임 시작 씬(`DatabaseLoader`가 있는 곳)에 빈 GameObject `PriceDataLoader`를 만들고 컴포넌트를 붙인다
2. 인스펙터에 5개 참조를 물린다
   - `mineralPrices` → `Assets/GameData/ShopData/MineralPriceDatabase.asset`
   - `minerals` → `Assets/Resources/MineralDatabase.asset`
   - `shop` → `Assets/GameData/ShopData/ShopItemDatabase.asset`
   - `relics` → `Assets/GameData/Relics/RelicDatabase.asset`
   - `upgradeTree` → `Assets/GameData/UpgradeData/_UpgradeTree.asset`

**참조를 하나라도 빠뜨리면** 그 절이 경고로 남고 해당 가격만 에셋 값을 쓴다. 크래시하지 않는다.

- [ ] **Step 4: 덮어써지는 필드에 경고를 단다**

이제부터 인스펙터에 적힌 값과 게임이 쓰는 값이 다를 수 있다(설계 §2.3). 해당 필드 위에 `[Header]`를 달아 다음에 보는 사람이 속지 않게 한다.

| 파일 | 대상 필드 | 넣을 것 |
|---|---|---|
| `Assets/Scripts/UI/Items/Minerals/Mineral/MineralSO.cs` | `weight` | `[Header("⚠ 런타임에 priceData.json이 덮어씀")]` |
| `Assets/Scripts/UI/Shop/ShopItemData.cs` | `price` | 같음 |
| `Assets/Scripts/UI/Upgrade/UpgradeNodeSO.cs` | `cost` | 같음 |
| `Assets/Scripts/Gameplay/Relics/Data/RelicSO.cs` | `upgradeCosts` | 같음 |
| `Assets/Scripts/UI/Items/Minerals/Mineral/MineralPriceDatabase.cs` | `prices` | 같음 |

`UpgradeNodeSO.cost`에는 이미 `[Tooltip("해금 비용 (골드)")]`이 붙어 있다. **툴팁을 지우지 말고 `[Header]`를 위에 추가**한다.

`ShopItemData.price`에는 `[Header("아이템 정보")]` 그룹이 이미 있다. 그 그룹을 깨지 않도록 `price` 바로 위에 새 `[Header]`를 넣는다.

- [ ] **Step 5: 체크포인트**

`PriceDataLoader.cs`, 위 5개 SO 스크립트, 씬을 UVCS에 체크인한다.

---

### Task 3: JSON 추출 에디터 툴

**Files:**
- Create: `Assets/Scripts/Editor/PriceDataExporter.cs`

**Interfaces:**
- Consumes: Task 1의 `PriceData` 및 DTO 4종
- Produces: 메뉴 `Tools/Economy/Export Prices to JSON`

143개 숫자를 손으로 전사하면 오타가 나는데, 테스트가 JSON을 읽으므로(Task 5) **전사 오류를 테스트가 못 잡는다.** 현재 에셋 값을 그대로 뽑는 툴이 필요한 이유다.

- [ ] **Step 1: 툴을 작성한다**

`Assets/Scripts/Editor/PriceDataExporter.cs`:

```csharp
// @tags: editor, price, export, json, tool, economy
using System.Collections.Generic;
using System.IO;
using Relic.Data;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 현재 에셋의 가격 값을 읽어 StreamingAssets/priceData.json 으로 뽑는다.
///
/// 초기 마이그레이션용이자, 나중에 에셋 쪽에서 값을 만졌을 때 다시 뽑는 용도.
/// 노드 비용은 UpgradeTreeGenerator.cs 가 아니라 생성된 노드 에셋에서 읽는다.
///
/// 설계: Assets/Docs/economy/price-data-json-design.md §6
/// </summary>
public static class PriceDataExporter
{
    private const string OutPath = "Assets/StreamingAssets/priceData.json";

    private const string MineralPriceDbPath = "Assets/GameData/ShopData/MineralPriceDatabase.asset";
    private const string ShopDbPath         = "Assets/GameData/ShopData/ShopItemDatabase.asset";
    private const string MineralDir         = "Assets/GameData/MineralData";
    private const string RelicDir           = "Assets/GameData/Relics";
    private const string NodeDir            = "Assets/GameData/UpgradeData/Node";

    [MenuItem("Tools/Economy/Export Prices to JSON")]
    public static void Export()
    {
        if (File.Exists(OutPath) &&
            !EditorUtility.DisplayDialog(
                "가격 JSON 덮어쓰기",
                $"{OutPath} 를 현재 에셋 값으로 덮어쓴다.\n\n" +
                "JSON이 이미 정답인 상태라면 이 작업은 낡은 에셋 값으로 되돌리는 셈이 된다. 계속할까?",
                "덮어쓴다", "취소"))
        {
            return;
        }

        var data = new PriceData
        {
            minerals      = ExportMinerals(),
            shopItems     = ExportShop(ShopItemType.Item),
            shopEquipment = ExportShop(ShopItemType.Equipment),
            shopRelics    = ExportShop(ShopItemType.Relic),
            relicUpgrades = ExportRelicUpgrades(),
            upgradeNodes  = ExportNodes(),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
        File.WriteAllText(OutPath, JsonUtility.ToJson(data, true));
        AssetDatabase.Refresh();

        Debug.Log($"[PriceDataExporter] 저장 완료: {OutPath}\n" +
                  $"광물 {data.minerals.Length} / 소모품 {data.shopItems.Length} / 장비 {data.shopEquipment.Length} / " +
                  $"유물 {data.shopRelics.Length} / 유물강화 {data.relicUpgrades.Length} / 노드 {data.upgradeNodes.Length}");
    }

    private static MineralPriceEntry[] ExportMinerals()
    {
        var priceDb = AssetDatabase.LoadAssetAtPath<MineralPriceDatabase>(MineralPriceDbPath);
        var list = new List<MineralPriceEntry>();

        foreach (string guid in AssetDatabase.FindAssets("t:MineralSO", new[] { MineralDir }))
        {
            var so = AssetDatabase.LoadAssetAtPath<MineralSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null || so.mineralID == MineralID.None) continue;

            list.Add(new MineralPriceEntry
            {
                id     = so.mineralID.ToString(),
                price  = priceDb != null ? priceDb.GetPrice(so.mineralID) : 0,
                weight = so.weight,
            });
        }

        list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return list.ToArray();
    }

    private static IdPriceEntry[] ExportShop(ShopItemType kind)
    {
        var shop = AssetDatabase.LoadAssetAtPath<ShopItemDatabase>(ShopDbPath);
        var list = new List<IdPriceEntry>();
        if (shop == null || shop.shopItems == null) return list.ToArray();

        foreach (var row in shop.shopItems)
        {
            if (row.itemType != kind) continue;

            string id = kind == ShopItemType.Item      ? row.itemID.ToString()
                      : kind == ShopItemType.Equipment ? row.equipmentID.ToString()
                                                       : row.relicID.ToString();

            list.Add(new IdPriceEntry { id = id, price = row.price });
        }
        return list.ToArray();
    }

    private static RelicUpgradeEntry[] ExportRelicUpgrades()
    {
        var list = new List<RelicUpgradeEntry>();

        foreach (string guid in AssetDatabase.FindAssets("t:RelicSO", new[] { RelicDir }))
        {
            var so = AssetDatabase.LoadAssetAtPath<RelicSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null || so.id == RelicID.None) continue;
            if (so.id == RelicID.TestStatRelic) continue;   // 테스트용 — JSON에 넣지 않는다
            if (so.upgradeCosts == null) continue;

            var gold = new int[so.upgradeCosts.Length];
            for (int i = 0; i < gold.Length; i++) gold[i] = so.upgradeCosts[i].gold;

            list.Add(new RelicUpgradeEntry { id = so.id.ToString(), gold = gold });
        }

        list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return list.ToArray();
    }

    private static NodeCostEntry[] ExportNodes()
    {
        var list = new List<NodeCostEntry>();

        foreach (string guid in AssetDatabase.FindAssets("t:UpgradeNodeSO", new[] { NodeDir }))
        {
            var so = AssetDatabase.LoadAssetAtPath<UpgradeNodeSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null || string.IsNullOrEmpty(so.nodeId)) continue;

            list.Add(new NodeCostEntry { id = so.nodeId, cost = so.cost });
        }

        list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return list.ToArray();
    }
}
```

- [ ] **Step 2: 툴을 실행해 JSON을 만든다 (사람 몫)**

사람이 Unity에서 `Tools/Economy/Export Prices to JSON`을 실행한다.

기대 로그: `광물 23 / 소모품 2 / 장비 28 / 유물 28 / 유물강화 27 / 노드 35`

**개수가 다르면 멈추고 원인을 확인하라.** 특히 노드가 35가 아니면 트리 생성기를 돌리지 않았거나 `NodeDir` 경로가 다른 것이다.

- [ ] **Step 3: 뽑힌 JSON을 눈으로 검증한다 (사람 몫)**

`Assets/StreamingAssets/priceData.json`을 열어 몇 개만 확인한다.

- `ScrapMetal` price 7 / weight 0.3
- `Iron` price 50 / weight 1.2
- `Sapphire` price 242 / weight 2.3
- `MinerHelmet` price 2000, `ScientistHelmet` price 200000
- `Magnet` price 12000, gold `[6000, 18000]`
- `MiningLevel_T0_Final` cost 3500

- [ ] **Step 4: 체크포인트**

`PriceDataExporter.cs`와 `priceData.json`을 UVCS에 체크인한다.

---

### Task 4: 유물 강화 골드비 배선

**Files:**
- Modify: `Assets/Scripts/UI/Upgrade/EquipmentUpgradeOverlayUI.cs` (2곳)

**Interfaces:**
- Consumes: `Relic.Data.RelicSO.upgradeCosts`(`RelicUpgradeCost[]`, 각 원소에 `int gold`), `EquipmentUpgradeFormula.RelicGoldCost(int)`
- Produces: 없음

`RelicSO.upgradeCosts`는 지금 **읽는 코드가 없다.** 실제 강화비는 `EquipmentUpgradeTable.cs:86`의 `RelicGoldCost(int currentLevel) => Math.Max(1, currentLevel) * 2222` 고정 공식이다. 배선하지 않으면 JSON의 `relicUpgrades` 27개 항목이 죽은 데이터가 된다.

- [ ] **Step 1: 헬퍼를 추가한다**

`EquipmentUpgradeOverlayUI.cs`의 클래스 안, `StopUpgradeAnim()` 메서드 근처에 넣는다.

```csharp
    /// <summary>
    /// 유물 강화 골드비. RelicSO.upgradeCosts 를 우선 쓰고, 비어 있으면 기존 공식으로 폴백한다.
    /// upgradeCosts[0] = Lv1→2, [1] = Lv2→3. currentLevel 이 0일 수 있어 Clamp 한다.
    /// 설계: Assets/Docs/economy/price-data-json-design.md §4
    /// </summary>
    private static int RelicUpgradeGold(Relic.Data.RelicSO so, int currentLevel)
    {
        if (so != null && so.upgradeCosts != null && so.upgradeCosts.Length > 0)
        {
            int idx = Mathf.Clamp(currentLevel - 1, 0, so.upgradeCosts.Length - 1);
            return so.upgradeCosts[idx].gold;
        }
        return EquipmentUpgradeFormula.RelicGoldCost(currentLevel);
    }
```

- [ ] **Step 2: 표시 경로를 배선한다**

`RefreshRelicDetail` 안(1156행 부근). `so`는 그 메서드에서 `_selRelicSo`를 받은 지역 변수다.

```csharp
            int goldCost = EquipmentUpgradeFormula.RelicGoldCost(actualLevel);
```

를 아래로 바꾼다.

```csharp
            int goldCost = RelicUpgradeGold(so, actualLevel);
```

**같은 블록의 `RelicMaterialCount`는 건드리지 마라.** 재료 비용은 이번 범위가 아니다.

- [ ] **Step 3: 차감 경로를 배선한다**

강화 적용 메서드 안(1339행 부근).

```csharp
            int goldCost = EquipmentUpgradeFormula.RelicGoldCost(level);
```

를 아래로 바꾼다.

```csharp
            int goldCost = RelicUpgradeGold(_selRelicSo, level);
```

**두 곳이 같은 값을 내야 한다.** 표시액과 차감액이 다르면 플레이어가 속았다고 느낀다. Step 2는 `actualLevel`, Step 3은 `level`을 쓰는데 둘 다 "현재 레벨"을 뜻하는 같은 값이다 — 변수명만 다르다.

- [ ] **Step 4: "임시 테스트 값" 배지를 건드리지 않는다**

`EquipmentUpgradeOverlayUI.cs:1137-1138`의 배지는 **골드만 경고하는 게 아니다.** 같은 화면의 재료 광물도 여전히 임시값이다 — 수량은 `RelicMaterialCount(level) = level`, 종류는 `EquipmentUpgradeStore.ResolveMaterial(null)`이 주는 "DB 첫 광물".

배지를 지우면 아직 미확정인 재료 비용이 확정된 것처럼 보인다. **그대로 둔다**(설계 §4.1).

- [ ] **Step 5: 사람이 확인한다**

플레이 모드에서 유물 강화 화면을 연다.

기대: 차감되는 골드가 티어별 값(1티어 6,000 / 2티어 23,000 / 3티어 100,000)이다. **2,222G가 나오면 배선이 안 된 것이다.**
표시액과 실제 차감액이 같은지도 확인한다.

- [ ] **Step 6: 체크포인트**

`EquipmentUpgradeOverlayUI.cs`를 UVCS에 체크인한다.

---

### Task 5: 밸런스 테스트를 JSON 읽도록 전환

**Files:**
- Modify: `Assets/Tests/EditMode/MineralBalanceTests.cs`
- Modify: `Assets/Tests/EditMode/ShopPriceTests.cs`

**Interfaces:**
- Consumes: Task 1의 `PriceData`·DTO, Task 3이 만든 `Assets/StreamingAssets/priceData.json`, 기존 `MineralEconomy`·`MineralEconomyEntry`
- Produces: 없음

JSON이 정답이 되면 에셋을 읽는 테스트는 **게임이 실제로 쓰는 값을 보지 않게 된다.** 입력 출처만 바꾸고 **불변식은 그대로 유지한다.**

- [ ] **Step 1: 공용 로더 헬퍼를 만든다**

두 테스트 파일이 같은 JSON을 읽으므로 헬퍼를 하나 두고 양쪽에서 쓴다. `MineralBalanceTests.cs` 상단에 넣고 `ShopPriceTests`가 참조한다.

```csharp
/// <summary>
/// EditMode 테스트용 priceData.json 로더. 게임 런타임 경로(PriceDataLoader)와 별개로
/// 파일을 직접 읽는다 — 테스트는 "JSON에 적힌 값"을 검증하지 로더 동작을 검증하지 않는다
/// (로더는 PriceApplierTests 담당).
/// </summary>
public static class PriceDataTestSource
{
    private static PriceData _cached;

    public static PriceData Load()
    {
        if (_cached != null) return _cached;

        string path = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "priceData.json");
        NUnit.Framework.Assert.IsTrue(System.IO.File.Exists(path), $"priceData.json 없음: {path}");

        _cached = UnityEngine.JsonUtility.FromJson<PriceData>(System.IO.File.ReadAllText(path));
        NUnit.Framework.Assert.IsNotNull(_cached, "priceData.json 파싱 실패");
        return _cached;
    }

    public static int MineralPrice(string id)  => Find(id).price;
    public static float MineralWeight(string id) => Find(id).weight;

    private static MineralPriceEntry Find(string id)
    {
        var data = Load();
        NUnit.Framework.Assert.IsNotNull(data.minerals, "priceData.json에 minerals 절이 없다");
        var e = System.Array.Find(data.minerals, m => m.id == id);
        NUnit.Framework.Assert.IsNotNull(e, $"priceData.json에 광물 '{id}' 없음");
        return e;
    }

    public static int ShopPrice(IdPriceEntry[] section, string sectionName, string id)
    {
        NUnit.Framework.Assert.IsNotNull(section, $"priceData.json에 {sectionName} 절이 없다");
        var e = System.Array.Find(section, x => x.id == id);
        NUnit.Framework.Assert.IsNotNull(e, $"priceData.json {sectionName}에 '{id}' 없음");
        return e.price;
    }
}
```

- [ ] **Step 2: `MineralBalanceTests`의 입력 출처를 바꾼다**

`BuildLayer(string tileType)`가 지금 `MineralPriceDatabase`와 `MineralSO` 에셋에서 가격·무게를 읽는다. **`perChunk`는 계속 `tileData.json`에서 읽는다**(가격이 아니라 지형 정의라 이번 이전 대상이 아니다).

`LoadPriceDb()`와 `LoadWeights()` 두 헬퍼를 지우고, `BuildLayer` 안의 항목 생성을 아래로 바꾼다.

```csharp
            float mid = (rule.perChunk[0] + rule.perChunk[1]) * 0.5f;
            entries.Add(new MineralEconomyEntry(
                id,
                PriceDataTestSource.MineralPrice(rule.mineralType),
                PriceDataTestSource.MineralWeight(rule.mineralType),
                mid));
```

`rule.mineralType`이 곧 enum 이름 문자열이라 JSON의 `id`와 그대로 대응한다.

**기대값·허용오차·불변식은 하나도 바꾸지 마라.** 15개 테스트 이름과 단언이 그대로 남아야 한다.

- [ ] **Step 3: `ShopPriceTests`의 입력 출처를 바꾼다**

`LoadShopDb()`·`FindEquipment()`·`FindRelic()`이 지금 `ShopItemDatabase.asset`을 읽는다. JSON을 읽도록 바꾼다.

`EquipmentSets`·`RelicPrices` 기대 데이터와 11개 테스트의 단언은 **그대로 둔다.** ID를 enum 이름 문자열로 조회하도록 바꾼다.

```csharp
    private static int EquipmentPrice(int equipmentId)
    {
        string name = ((EquipmentID)equipmentId).ToString();
        return PriceDataTestSource.ShopPrice(
            PriceDataTestSource.Load().shopEquipment, "shopEquipment", name);
    }

    private static int RelicPrice(int relicId)
    {
        string name = ((RelicID)relicId).ToString();
        return PriceDataTestSource.ShopPrice(
            PriceDataTestSource.Load().shopRelics, "shopRelics", name);
    }
```

**바뀌는 테스트가 셋 있다.** JSON에는 `isAvailable`이 없으므로(설계 §3.2) 판매 여부를 JSON에서 검증할 수 없다.

- `Equipment_AllTwentySevenAreOnSale` — `isAvailable` 검증이라 **에셋을 계속 읽는다.** 그대로 둔다.
- `Relic_TwentySevenAreOnSale_AndTestRelicIsNot` — 같은 이유로 **에셋을 계속 읽는다.**
- `ShopDatabase_StillHasFiftyEightRows` — 에셋 구조 검증이라 **에셋을 계속 읽는다.**

즉 `ShopPriceTests`는 **가격은 JSON, 판매 여부·행 수는 에셋**을 본다. 파일 상단 주석에 이 이유를 남겨라.

`Relic_UpgradeCostsAreHalfAndOneAndHalfOfPrice`와 `Relic_FullUpgradeEqualsOneEquipmentSetOfSameTier`는 `relicUpgrades` 절에서 `gold` 배열을 읽도록 바꾼다.

`Consumables_AreUntouched`는 `shopItems` 절에서 `FrostbiteResist` 200 / `BurnResist` 15를 확인하도록 바꾼다.

- [ ] **Step 4: 사람이 확인한다**

Unity Test Runner(EditMode)에서 `MineralBalanceTests`(15) + `ShopPriceTests`(11) + `PriceApplierTests`(11)를 실행한다.
기대: 37개 전부 PASS.

하나라도 실패하면 **JSON 추출(Task 3)이 잘못됐을 가능성이 가장 크다.** 실패한 항목의 JSON 값을 에셋 값과 대조하라.

- [ ] **Step 5: 체크포인트**

두 테스트 파일을 UVCS에 체크인한다.

---

## 실행 후 남는 것

- 가격 수정 시 건드릴 파일이 **52개 + C# 1개 + 에디터 툴 실행 → `priceData.json` 하나**가 된다
- 업그레이드 노드 비용을 바꿀 때 **트리 생성기를 돌리지 않아도 된다.** GUID 변경과 세이브 해금 리셋이 사라진다
- 유물 강화비가 실제로 게임에 반영된다(2,222G 고정 → 티어별)
- **인스펙터가 거짓말을 하게 된다.** 에셋에 적힌 값과 게임이 쓰는 값이 다를 수 있다(설계 §2.3)
- `priceData.json`은 빌드에 평문으로 실려 나간다(설계 §10)

## 사람이 Unity에서 해야 할 일 (순서대로)

1. Task 1·2 컴파일 확인
2. **⚠ 먼저 `Tools/Upgrade/Upgrade Tree Generator`를 실행한다.** 지금 `Assets/GameData/UpgradeData/Node/`에 노드 에셋이 **34개뿐**이다 — 직전 장비·유물 가격 작업에서 `UpgradeTreeGenerator.cs`를 고쳤지만 생성기를 아직 안 돌렸다. 디스크에는 낡은 `InventoryWeight_T0_01`이 남아 있고, 새로 생겨야 할 `WarehouseCapacity_T0_01`·`InventoryWeight_T1_01`이 없다.
   **이 단계를 건너뛰고 3번을 실행하면 노드 2개가 빠진 JSON이 만들어지고, Task 5의 테스트가 그 잘못된 값을 정답으로 굳힌다.**
3. **`Tools/Economy/Export Prices to JSON` 실행** — 개수가 `23 / 2 / 28 / 28 / 27 / 35`인지 확인
4. 뽑힌 `priceData.json` 눈으로 검증 (Task 3 Step 3의 표본)
5. **씬에 `PriceDataLoader` 배치 + 인스펙터 참조 5개 연결** — 안 하면 JSON이 무시된다
   **⚠ 배선 확인 — 다음 3개는 틀려도 예외도 경고도 없이 조용히 새는 지점이다:**
   - `PriceDataLoader.mineralPrices`와 `ShopManager.priceDatabase`가 **같은 파일**(같은 `.asset`)을 가리키는가
   - `PriceDataLoader.shop`과 `ShopManager.shopItemDatabase`가 같은 파일인가
   - `PriceDataLoader.upgradeTree`와 `UpgradeManager.upgradeTree`가 같은 파일인가

   이 셋은 둘 다 **단순 인스펙터 참조**라, 로더 쪽과 매니저 쪽이 서로 다른 에셋(예: 중복 저장본)을 가리켜도 아무 에러가 안 난다 — 로더는 "적용 143건" 성공 로그를 남기지만 게임은 그 값이 안 꽂힌 원본을 그대로 보게 된다.
   반면 `PriceDataLoader.minerals`(`MineralDatabase`)와 `PriceDataLoader.relics`(`RelicDatabase`)는 이 위험이 없다 — `MineralDatabase`는 `Resources.Load` 싱글턴이라 어느 경로로 로드하든 같은 인스턴스로 수렴하고, `RelicDatabase`는 `OnEnable`에서 자신을 `Instance`로 굳혀 마찬가지로 수렴한다.
6. EditMode 테스트 37개 실행 (+ `PriceDataIntegrationTests` 3개 — 실제 에셋 대조, `priceData.json` 생성 후에만 통과)
7. 플레이 모드에서 유물 강화 골드가 티어별 값인지 확인 (2,222G가 아닌지)
8. JSON 숫자 하나를 바꿔 재실행 — 즉시 반영되는지 확인

## 이번에 일부러 안 하는 것

| 항목 | 근거 | 왜 미뤘나 |
|---|---|---|
| `Tools/Economy/Sync Prices to Assets` (JSON → 에셋 역방향) | 설계 §8 | 인스펙터를 진실로 만드는 편의 기능이다. 수정 속도에 기여하지 않는다 |
| `perChunk`·상태이상을 `priceData.json`에 합치기 | 설계 §8 | 이미 `tileData.json`에 있다. 성격이 다르고 파일이 커지면 찾기 어렵다 |
| 유물 강화 **재료(광물)** 비용 | 설계 §8, §4.1 | 담을 필드(`RelicUpgradeCost.material`·`materialCount`)는 있지만 무엇을 요구할지가 설계되지 않았다. "임시 테스트 값" 배지가 남는 이유 |
| 치트 방지 | 설계 §10 | 출시가 가까워졌을 때 밸런스 JSON 전부를 함께 다룰 사안이다 |
