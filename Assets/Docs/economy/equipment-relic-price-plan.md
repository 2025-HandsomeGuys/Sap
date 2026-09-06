# 장비·유물 가격 적용 구현 계획

> **에이전트 작업자에게:** 이 계획은 `superpowers:subagent-driven-development` 또는 `superpowers:executing-plans`로 태스크 단위 실행한다. 체크박스(`- [ ]`)로 진행을 추적한다.

**설계 문서:** [equipment-relic-price-design.md](equipment-relic-price-design.md) — 수치의 근거는 전부 여기 있다. 숫자가 계획과 문서 사이에서 어긋나면 **문서가 정답**이다.
**선행 문서:** [mineral-price-design.md](mineral-price-design.md) — 층별 회당 매출이 여기서 나온다.

**Goal:** 장비 27종과 유물 28종의 가격·강화비를 층 곡선에 맞추고, 그 사다리를 EditMode 테스트로 고정한다.

**Architecture:** 데이터(에셋 YAML)만 바꾼다. C# 로직 변경은 없다. 먼저 가격 사다리를 검증하는 EditMode 테스트를 작성해 실패시키고, 에셋을 고쳐 통과시킨다. 테스트가 가격표 명세서 역할을 하므로 3·4층 수치가 확정될 때 그대로 재사용된다.

**Tech Stack:** Unity 2D / C# / NUnit (EditMode) / ScriptableObject 에셋 YAML

## Global Constraints

- **UVCS 프로젝트다. `git` 명령을 일절 쓰지 않는다.** 체크인은 UVCS로 한다.
- **Unity Test Runner 실행은 사람이 한다.** Claude는 테스트 파일 작성·수정만 하고 `mcp__mcp-unity__run_tests` 등을 호출하지 않는다.
- 새 EditMode 테스트는 `Assets/Tests/EditMode/`에 두고 `EditModeTests.asmdef`(Editor 전용, `GameScripts` 참조)에 자동 포함시킨다. asmdef를 수정하지 않는다.
- 모든 C# 스크립트 첫 줄에 `// @tags: ...` 주석을 단다.
- **`statModifiers`를 채우지 않는다.** 장비 효과는 이번 범위가 아니다(설계 §8).
- **`Assets/GameData/EquipmentData/*.asset`을 건드리지 않는다.** `EquipmentSO.price`는 소비처가 없는 죽은 필드다. 상점 가격은 `ShopItemData.price` 한 곳에서만 관리된다.
- 가격 사다리 (설계 §5·§6):

| 층/티어 | 장비 부위당 | 장비 세트 합 | 유물 구매가 | 유물 Lv1→2 | 유물 Lv2→3 |
|---|---|---|---|---|---|
| 1층 | 2,000 | 6,000 | — | — | — |
| 2층 / 티어1 | 12,000 | 36,000 | 12,000 | 6,000 | 18,000 |
| 3층 / 티어2 | 46,000 | 138,000 | 46,000 | 23,000 | 69,000 |
| 4층 / 티어3 | 200,000 | 600,000 | 200,000 | 100,000 | 300,000 |

- 불변식: 유물 강화비 = 구매가 × 0.5 (Lv1→2), 구매가 × 1.5 (Lv2→3)
- 불변식: 유물 풀강 총액(구매가 × 3) = 같은 티어 장비 한 세트 합
- 장비 27종 총액 **1,728,000G**

---

## File Structure

| 파일 | 책임 | 신규/수정 |
|---|---|---|
| `Assets/Tests/EditMode/ShopPriceTests.cs` | 장비·유물 가격 사다리 검증 | 신규 |
| `Assets/GameData/ShopData/ShopItemDatabase.asset` | 장비 27종 가격 + `isAvailable` 복원 / 유물 27종 가격 + 4001 잠금 | 수정 |
| `Assets/GameData/Relics/*.asset` (27개) | `upgradeCosts` 2개 항목의 `gold` | 수정 |
| `Assets/Docs/economy/mineral-price-design.md` | §8에 T3 예산 상한 1,200,000G 반영 | 수정 |

---

### Task 1: 가격 사다리 검증 테스트 (이 시점에는 실패한다)

**Files:**
- Create: `Assets/Tests/EditMode/ShopPriceTests.cs`

**Interfaces:**
- Consumes: `ShopItemDatabase.shopItems`(`List<ShopItemData>` public), `ShopItemData`(`itemType`·`equipmentID`·`relicID`·`price`·`isAvailable`), `ShopItemType`(`Item`/`Equipment`/`Relic`), `EquipmentID`, `Relic.Data.RelicID`, `Relic.Data.RelicSO`(`id`·`upgradeCosts`), `Relic.Data.RelicUpgradeCost`(`gold`)
- Produces: Task 2·3이 통과시켜야 할 목표 수치. 코드 심볼은 없다.

**주의:** 이 태스크가 끝난 시점에 테스트는 **실패해야 정상**이다. Task 2·3이 데이터를 고쳐 통과시킨다.

- [ ] **Step 1: 테스트를 작성한다**

`Assets/Tests/EditMode/ShopPriceTests.cs`:

```csharp
// @tags: test, editmode, shop, price, equipment, relic, economy, balance
using System.Collections.Generic;
using NUnit.Framework;
using Relic.Data;
using UnityEditor;

/// <summary>
/// 장비·유물 가격 사다리를 고정한다. 이 테스트가 곧 가격표 명세서다 —
/// 수치를 바꾸려면 먼저 Assets/Docs/economy/equipment-relic-price-design.md 를 고치고
/// 여기 기대값을 맞춘다.
///
/// 장비 효과(statModifiers)는 이번 범위가 아니라 검사하지 않는다(설계 §8).
/// </summary>
public class ShopPriceTests
{
    private const string ShopDbPath = "Assets/GameData/ShopData/ShopItemDatabase.asset";
    private const string RelicDir   = "Assets/GameData/Relics";

    // 설계 문서 §5 — 층별 장비 부위 가격 (= §6 유물 티어 구매가)
    private const int Layer1 = 2000;
    private const int Layer2 = 12000;
    private const int Layer3 = 46000;
    private const int Layer4 = 200000;

    // ─────────────────────────────────────────────────────────────
    // 기대 데이터 (설계 §5.1 / §6.1)
    // ─────────────────────────────────────────────────────────────

    /// <summary>세트 이름 → (부위 3개의 equipmentID, 부위당 가격)</summary>
    private static readonly (string set, int[] ids, int price)[] EquipmentSets =
    {
        ("Miner",     new[] { 3004, 3104, 3204 }, Layer1),
        ("Winter",    new[] { 3006, 3106, 3206 }, Layer2),
        ("Climber",   new[] { 3003, 3103, 3203 }, Layer2),
        ("Carrier",   new[] { 3005, 3105, 3205 }, Layer2),
        ("Heat",      new[] { 3007, 3107, 3207 }, Layer3),
        ("Engineer",  new[] { 3008, 3108, 3208 }, Layer3),
        ("Shovel",    new[] { 3010, 3110, 3210 }, Layer3),
        ("Scientist", new[] { 3009, 3109, 3209 }, Layer4),
        ("Marathon",  new[] { 3011, 3111, 3211 }, Layer4),
    };

    /// <summary>relicID → 구매가</summary>
    private static readonly (int id, int price)[] RelicPrices =
    {
        // 티어 1
        (4002, Layer2), (4003, Layer2), (4005, Layer2), (4012, Layer2),
        (4017, Layer2), (4023, Layer2), (4024, Layer2),
        // 티어 2
        (4006, Layer3), (4007, Layer3), (4008, Layer3), (4009, Layer3),
        (4013, Layer3), (4014, Layer3), (4015, Layer3), (4018, Layer3),
        (4021, Layer3), (4022, Layer3), (4027, Layer3), (4028, Layer3),
        // 티어 3
        (4004, Layer4), (4010, Layer4), (4011, Layer4), (4016, Layer4),
        (4019, Layer4), (4020, Layer4), (4025, Layer4), (4026, Layer4),
    };

    /// <summary>테스트용 유물. 상점에서 빼고 강화비도 손대지 않는다.</summary>
    private const int TestRelicId = 4001;

    // ─────────────────────────────────────────────────────────────
    // 로더
    // ─────────────────────────────────────────────────────────────

    private static ShopItemDatabase LoadShopDb()
    {
        var db = AssetDatabase.LoadAssetAtPath<ShopItemDatabase>(ShopDbPath);
        Assert.IsNotNull(db, $"상점 DB를 못 찾음: {ShopDbPath}");
        Assert.IsNotNull(db.shopItems, "shopItems가 null");
        return db;
    }

    private static ShopItemData FindEquipment(ShopItemDatabase db, int equipmentId)
    {
        var row = db.shopItems.Find(i =>
            i.itemType == ShopItemType.Equipment && (int)i.equipmentID == equipmentId);
        Assert.IsNotNull(row, $"equipmentID {equipmentId} 항목이 상점 DB에 없다");
        return row;
    }

    private static ShopItemData FindRelic(ShopItemDatabase db, int relicId)
    {
        var row = db.shopItems.Find(i =>
            i.itemType == ShopItemType.Relic && (int)i.relicID == relicId);
        Assert.IsNotNull(row, $"relicID {relicId} 항목이 상점 DB에 없다");
        return row;
    }

    private static Dictionary<int, RelicSO> LoadRelicAssets()
    {
        var map = new Dictionary<int, RelicSO>();
        string[] guids = AssetDatabase.FindAssets("t:RelicSO", new[] { RelicDir });
        foreach (string guid in guids)
        {
            var so = AssetDatabase.LoadAssetAtPath<RelicSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null) map[(int)so.id] = so;
        }
        Assert.Greater(map.Count, 0, $"{RelicDir}에 RelicSO 에셋이 없다");
        return map;
    }

    // ─────────────────────────────────────────────────────────────
    // 장비 — 설계 §5
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Equipment_PricesMatchLayerLadder()
    {
        var db = LoadShopDb();
        foreach (var (set, ids, price) in EquipmentSets)
            foreach (int id in ids)
                Assert.AreEqual(price, FindEquipment(db, id).price, $"{set} / equipmentID {id}");
    }

    [Test]
    public void Equipment_AllTwentySevenAreOnSale()
    {
        // 가격이 곧 잠금이다 — 해금 시스템을 만들지 않고 전부 노출한다 (설계 §3.1)
        var db = LoadShopDb();
        foreach (var (set, ids, _) in EquipmentSets)
            foreach (int id in ids)
                Assert.IsTrue(FindEquipment(db, id).isAvailable,
                    $"{set} / equipmentID {id}가 잠겨 있다");
    }

    [Test]
    public void Equipment_EachSetHasThreePiecesAtOnePrice()
    {
        // 층 안에서 필수/선택 가격을 구분하지 않는다 (설계 §3.2)
        var db = LoadShopDb();
        foreach (var (set, ids, _) in EquipmentSets)
        {
            Assert.AreEqual(3, ids.Length, $"{set}: 부위가 3개가 아니다");
            int first = FindEquipment(db, ids[0]).price;
            foreach (int id in ids)
                Assert.AreEqual(first, FindEquipment(db, id).price,
                    $"{set}: 부위마다 가격이 다르다 (equipmentID {id})");
        }
    }

    [Test]
    public void Equipment_TotalCostIsOneMillionSevenHundredTwentyEightThousand()
    {
        var db = LoadShopDb();
        int total = 0;
        foreach (var (_, ids, _) in EquipmentSets)
            foreach (int id in ids)
                total += FindEquipment(db, id).price;

        Assert.AreEqual(1_728_000, total, "장비 27종 총액 (설계 §5)");
    }

    // ─────────────────────────────────────────────────────────────
    // 유물 — 설계 §6
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Relic_PricesMatchTierLadder()
    {
        var db = LoadShopDb();
        foreach (var (id, price) in RelicPrices)
            Assert.AreEqual(price, FindRelic(db, id).price, $"relicID {id}");
    }

    [Test]
    public void Relic_TwentySevenAreOnSale_AndTestRelicIsNot()
    {
        var db = LoadShopDb();
        foreach (var (id, _) in RelicPrices)
            Assert.IsTrue(FindRelic(db, id).isAvailable, $"relicID {id}가 잠겨 있다");

        Assert.IsFalse(FindRelic(db, TestRelicId).isAvailable,
            "TestStatRelic(4001)은 테스트용이라 상점에서 빼야 한다");
    }

    [Test]
    public void Relic_UpgradeCostsAreHalfAndOneAndHalfOfPrice()
    {
        // 강화비 = 구매가의 50% / 150% (설계 §3.3)
        var assets = LoadRelicAssets();
        foreach (var (id, price) in RelicPrices)
        {
            Assert.IsTrue(assets.ContainsKey(id), $"relicID {id}의 RelicSO 에셋이 없다");
            var so = assets[id];

            Assert.IsNotNull(so.upgradeCosts, $"relicID {id}: upgradeCosts가 null");
            Assert.AreEqual(2, so.upgradeCosts.Length,
                $"relicID {id}: upgradeCosts 길이는 maxLevel-1 = 2여야 한다");

            Assert.AreEqual(price / 2, so.upgradeCosts[0].gold, $"relicID {id}: Lv1→2");
            Assert.AreEqual(price * 3 / 2, so.upgradeCosts[1].gold, $"relicID {id}: Lv2→3");
        }
    }

    [Test]
    public void Relic_FullUpgradeEqualsOneEquipmentSetOfSameTier()
    {
        // 유물 하나 풀강 = 같은 층 장비 한 세트 (설계 §3.3)
        var db = LoadShopDb();
        var assets = LoadRelicAssets();

        foreach (var (id, price) in RelicPrices)
        {
            var so = assets[id];
            int fullCost = FindRelic(db, id).price + so.upgradeCosts[0].gold + so.upgradeCosts[1].gold;
            Assert.AreEqual(price * 3, fullCost, $"relicID {id}: 풀강 총액은 구매가의 3배");
        }
    }

    [Test]
    public void Relic_TestRelicUpgradeCostsAreUntouched()
    {
        // 4001은 상점에서만 빼고 강화비는 원래 값(100/250)을 유지한다
        var assets = LoadRelicAssets();
        Assert.IsTrue(assets.ContainsKey(TestRelicId), "TestStatRelic 에셋이 없다");
        var so = assets[TestRelicId];
        Assert.AreEqual(100, so.upgradeCosts[0].gold);
        Assert.AreEqual(250, so.upgradeCosts[1].gold);
    }

    // ─────────────────────────────────────────────────────────────
    // 부수 피해 방지
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void ShopDatabase_StillHasFiftyEightRows()
    {
        // 항목을 추가·삭제하지 않는다. 값만 바꾼다.
        Assert.AreEqual(58, LoadShopDb().shopItems.Count);
    }

    [Test]
    public void Consumables_AreUntouched()
    {
        // 광물 가격 작업에서 정한 값이다 — 이번 작업이 건드리면 안 된다
        var db = LoadShopDb();
        var frost = db.shopItems.Find(i => i.itemType == ShopItemType.Item && (int)i.itemID == 1001);
        var burn  = db.shopItems.Find(i => i.itemType == ShopItemType.Item && (int)i.itemID == 1002);
        Assert.IsNotNull(frost, "itemID 1001(동상 저항) 항목이 없다");
        Assert.IsNotNull(burn,  "itemID 1002(화상 저항) 항목이 없다");
        Assert.AreEqual(200, frost.price, "동상 저항 200G");
        Assert.AreEqual(15,  burn.price,  "화상 저항 15G");
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

사람이 Unity Test Runner(EditMode)에서 실행한다. Claude는 실행하지 않는다.

기대: **대부분 FAIL.** 현재 장비는 27종 중 21종이 `isAvailable: 0`이고 가격이 1,000 플랫(광부 2,000·방한 12,000만 조정됨), 유물은 27종이 500G에 강화비 100/250이다. `ShopDatabase_StillHasFiftyEightRows`와 `Consumables_AreUntouched`, `Relic_TestRelicUpgradeCostsAreUntouched`만 PASS한다.

이 실패가 Task 2·3의 작업 지시서다.

- [ ] **Step 3: 체크포인트**

`ShopPriceTests.cs`를 UVCS에 체크인한다. **`git` 명령을 쓰지 않는다.**

---

### Task 2: 상점 가격 적용 (장비 27종 + 유물 28종)

**Files:**
- Modify: `Assets/GameData/ShopData/ShopItemDatabase.asset`

**Interfaces:**
- Consumes: Task 1의 `ShopPriceTests` 중 `Equipment_*`, `Relic_PricesMatchTierLadder`, `Relic_TwentySevenAreOnSale_AndTestRelicIsNot`
- Produces: 없음 (데이터 변경)

**파일 구조.** `shopItems:` 아래에 블록이 반복된다. `itemType: 0`=소모품(`itemID`), `1`=장비(`equipmentID`), `2`=유물(`relicID`).

```
  - itemType: 1
    itemID: 0
    equipmentID: 3003
    price: 1000
    isAvailable: 0
    stock: -1
```

유물 블록에는 `relicID` 줄이 하나 더 있다.

- [ ] **Step 1: 장비 27종의 `price`와 `isAvailable`을 바꾼다**

`equipmentID`로 블록을 찾아 `price`와 `isAvailable`을 함께 바꾼다. **27종 전부 `isAvailable: 1`이다.**

| 세트 | 층 | equipmentID | `price` | `isAvailable` |
|---|---|---|---|---|
| Miner | 1 | 3004, 3104, 3204 | **2000** | **1** |
| Winter | 2 | 3006, 3106, 3206 | **12000** | **1** |
| Climber | 2 | 3003, 3103, 3203 | **12000** | **1** |
| Carrier | 2 | 3005, 3105, 3205 | **12000** | **1** |
| Heat | 3 | 3007, 3107, 3207 | **46000** | **1** |
| Engineer | 3 | 3008, 3108, 3208 | **46000** | **1** |
| Shovel | 3 | 3010, 3110, 3210 | **46000** | **1** |
| Scientist | 4 | 3009, 3109, 3209 | **200000** | **1** |
| Marathon | 4 | 3011, 3111, 3211 | **200000** | **1** |

광부 세트(2000)와 방한 세트(12000)는 **이미 그 값이다** — `isAvailable`도 이미 1이라 손댈 필요가 없다. 나머지 21종이 실제 변경 대상이고, 그중 `isAvailable`을 0에서 1로 되돌리는 것이 21건이다.

`equipmentID: 3001`(레거시, price 15, isAvailable 1)은 **건드리지 않는다.**

- [ ] **Step 2: 유물 27종의 `price`를 바꾸고 4001을 잠근다**

| 티어 | relicID | `price` |
|---|---|---|
| 1 | 4002, 4003, 4005, 4012, 4017, 4023, 4024 | **12000** |
| 2 | 4006, 4007, 4008, 4009, 4013, 4014, 4015, 4018, 4021, 4022, 4027, 4028 | **46000** |
| 3 | 4004, 4010, 4011, 4016, 4019, 4020, 4025, 4026 | **200000** |

그리고 `relicID: 4001`(TestStatRelic) 블록의 **`isAvailable`을 1 → 0으로** 바꾼다. `price`(100)는 그대로 둔다.

나머지 27종의 `isAvailable`은 이미 1이니 그대로 둔다.

- [ ] **Step 3: 하지 말 것을 확인한다**

- 항목을 추가·삭제·재정렬하지 않는다 (총 58개 유지)
- `stock` 필드를 건드리지 않는다
- `itemType: 0` 소모품 2개(1001 price 200, 1002 price 15)를 건드리지 않는다 — 광물 가격 작업에서 정한 값이다
- `equipmentID: 3001` 항목을 건드리지 않는다

**`price: 1000`·`isAvailable: 0` 같은 줄이 파일에 여러 개 있다.** Edit의 `old_string`을 `equipmentID:`/`relicID:` 줄까지 포함해 넉넉히 잡아 유일하게 만들어라. 파일 전체를 Write로 덮어쓰지 마라.

- [ ] **Step 4: 검증한다**

파일을 다시 읽어 확인하고 리포트에 숫자로 적는다.

1. `shopItems` 총 항목 수 = **58**
2. 장비 27종의 `price`가 위 표와 일치하고 **전부 `isAvailable: 1`**
3. 장비 27종 `price` 합계 = **1,728,000**
4. 유물 27종의 `price`가 위 표와 일치, 4001은 `price: 100` / `isAvailable: 0`
5. 소모품 1001=200, 1002=15 / 장비 3001=15 그대로
6. 모든 항목의 `stock` 보존

- [ ] **Step 5: 체크포인트**

`ShopItemDatabase.asset`을 UVCS에 체크인한다.

---

### Task 3: 유물 강화비 적용 (27개 에셋)

**Files:**
- Modify: `Assets/GameData/Relics/` 아래 27개 `.asset`

**Interfaces:**
- Consumes: Task 1의 `Relic_UpgradeCostsAreHalfAndOneAndHalfOfPrice`, `Relic_FullUpgradeEqualsOneEquipmentSetOfSameTier`, `Relic_TestRelicUpgradeCostsAreUntouched`
- Produces: 없음 (데이터 변경)

**파일 구조.** 각 `RelicSO` 에셋에 이 블록이 있다. `gold` 두 값만 바꾼다.

```
  upgradeCosts:
  - gold: 100
    material: 0
    materialCount: 0
  - gold: 250
    material: 0
    materialCount: 0
```

첫 항목이 Lv1→2, 둘째가 Lv2→3이다. **`material`과 `materialCount`는 건드리지 않는다.**

- [ ] **Step 1: 티어 1 — 7개 파일**

`gold`를 **6000** / **18000**으로.

| 파일 | relicID |
|---|---|
| `PigeonFeather.asset` | 4002 |
| `Magnet.asset` | 4003 |
| `SpiderGlove.asset` | 4005 |
| `JunkSpring.asset` | 4012 |
| `ToolSwap.asset` | 4017 |
| `Mp3.asset` | 4023 |
| `Blind.asset` | 4024 |

- [ ] **Step 2: 티어 2 — 12개 파일**

`gold`를 **23000** / **69000**으로.

| 파일 | relicID |
|---|---|
| `Generator.asset` | 4006 |
| `GamblerGlasses.asset` | 4007 |
| `Anvil.asset` | 4008 |
| `Steroid.asset` | 4009 |
| `GravityFlip.asset` | 4013 |
| `DashBomb.asset` | 4014 |
| `Furnace.asset` | 4015 |
| `BlackMarket.asset` | 4018 |
| `DetectionPulse.asset` | 4021 |
| `OverloadBattery.asset` | 4022 |
| `OneWayPortal.asset` | 4027 |
| `Trident.asset` | 4028 |

- [ ] **Step 3: 티어 3 — 8개 파일**

`gold`를 **100000** / **300000**으로.

| 파일 | relicID |
|---|---|
| `Invincibility.asset` | 4004 |
| `PlasmaCutter.asset` | 4010 |
| `DrillDrone.asset` | 4011 |
| `Lightning.asset` | 4016 |
| `MinerDrone.asset` | 4019 |
| `Jetpack.asset` | 4020 |
| `XRay.asset` | 4025 |
| `Hourglass.asset` | 4026 |

- [ ] **Step 4: 건드리면 안 되는 것을 확인한다**

- **`TestStatRelic.asset`(4001)은 그대로 둔다.** `gold` 100 / 250 유지.
- `RelicDatabase.asset`은 `RelicSO`가 아니다. 건드리지 않는다.
- 각 파일의 `id`, `displayNameKey`, `descriptionKey`, `icon`, `type`, `maxLevel`, `behaviour`, `references` 블록을 건드리지 않는다. 특히 `references` 안의 `rid`와 동작 파라미터(예: `Magnet.asset`의 `radiusPerLevel`)는 유물의 실제 동작이므로 손대면 게임이 바뀐다.
- **작업 전에 각 파일의 `id:` 줄을 읽어 relicID를 확인하고 나서 고쳐라.** 파일명만 보고 판단하지 마라.

- [ ] **Step 5: 검증한다**

파일들을 다시 읽어 확인하고 리포트에 적는다.

1. 티어별 파일 수 = 7 / 12 / 8, 합 27
2. 각 파일의 `id` ↔ 티어 대응이 위 표와 일치
3. 각 파일의 `upgradeCosts` 길이가 여전히 **2**
4. `TestStatRelic.asset`이 100 / 250 그대로
5. `material`·`materialCount`가 전부 0 그대로
6. `Assets/GameData/Relics/`의 `RelicSO` 에셋 총 개수가 변경 전과 같은가 (**28개**)

Bash에서 python으로 일괄 파싱해 세는 것을 권한다 — 27개를 눈으로 세면 놓친다.

- [ ] **Step 6: 체크포인트**

바꾼 27개 에셋을 UVCS에 체크인한다.

---

### Task 4: 선행 문서에 T3 예산 상한 반영

**Files:**
- Modify: `Assets/Docs/economy/mineral-price-design.md` (§8)

**Interfaces:**
- Consumes: 없음
- Produces: 없음 (문서 변경)

장비·유물이 전체 예산의 56%를 가져가면서 4층 트리(T3) 예산이 압축됐다. 선행 문서 §8이 아직 옛 스케치(약 2,500,000G)를 담고 있어 4층 작업 때 잘못된 상한으로 설계하게 된다.

- [ ] **Step 1: §8을 고친다**

`Assets/Docs/economy/mineral-price-design.md`의 **§8 "3·4층 — 원칙만 (수치 보류)"** 에서 T3 관련 서술을 찾는다. 현재 "4층 예산이 전체의 74%(약 430만G)라 4층 전용 업그레이드 티어(T3)를 통째로 신설해야 한다"는 취지의 항목이 있다.

여기에 아래 내용을 반영한다. 기존 문장을 지우지 말고 상한을 덧붙이는 방향으로 하라.

- **T3 예산 상한은 약 1,200,000G다.** 옛 스케치 2,500,000G가 아니다.
- 이유: 장비 27종 1,728,000G와 유물 약 1,500,000G가 예산의 상당 부분을 가져간다.
- 근거 문서: `equipment-relic-price-design.md` §7.2
- 120회(=120일) 총 획득 580만G 배분: 트리 1,999,800(34%) / 장비 1,728,000(30%) / 유물 약 1,500,000(26%) / 포션 412,000(7%) / 마켓 약 160,000(3%)

- [ ] **Step 2: 상호 참조를 확인한다**

`mineral-price-design.md`의 §8이나 문서 상단에서 `equipment-relic-price-design.md`를 가리키는 링크가 없으면 추가하라. 두 문서가 예산을 공유하므로 한쪽만 보고 작업하면 어긋난다.

- [ ] **Step 3: 하지 말 것**

- §8 외의 절을 고치지 마라. 광물 수치는 이번 작업과 무관하다.
- 에셋·JSON·C#을 건드리지 마라.

- [ ] **Step 4: 체크포인트**

`mineral-price-design.md`를 UVCS에 체크인한다.

---

## 실행 후 남는 것

- 장비·유물 가격이 EditMode 테스트 **11개**로 고정된다
- **장비 27종은 여전히 아무 효과가 없다** — 설계 §8의 의도된 상태다. 600,000G짜리 Scientist 세트가 빈 `statModifiers`를 가진다
- 3·4층 가격(46,000 / 200,000)은 회당 매출이 미확정인 상태에서 나온 값이라, 3·4층 광물 작업 때 같이 움직인다

## 사람이 Unity에서 해야 할 일

1. **에셋 리임포트** — `ShopData/ShopItemDatabase.asset`, `Relics/` 27개. YAML을 직접 편집했다.
2. **EditMode Test Runner 실행** — `ShopPriceTests` 11개. 전부 PASS여야 한다.
3. **상점 육안 확인** — 장비 27종이 전부 보이고 2,000 / 12,000 / 46,000 / 200,000 네 가격대로 나뉘는지. 유물 27종이 12,000 / 46,000 / 200,000으로 나뉘고 TestStatRelic이 안 보이는지.

## 이번에 일부러 안 하는 것

| 항목 | 근거 | 왜 미뤘나 |
|---|---|---|
| **장비 효과(`statModifiers`)** | 설계 §8 | 가격을 먼저 정해 효과 설계의 기준으로 삼는 접근이다. `StatType`에 `HazardRadiationResist` 추가가 선행 |
| **유물 티어 재검토** | 설계 §6.1 | 이름·주석 기반 추정이다. 실제 강도를 아는 사람이 조정해야 한다 |
| **레거시 장비 3101·3201·3301** | 설계 §10 | 스탯이 있는데 상점에 없다. 노출할지 폐기할지 미정 |
| **유물 슬롯 확장 비용** | 설계 §10 | `RelicSaveData.slotCount`가 확장 가능한데 확장 경로·비용이 없다 |
