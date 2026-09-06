# 암시장(Black Market) 유물 구현 계획

> **에이전트 작업자용:** 이 계획은 태스크 단위로 구현한다. 각 태스크는 독립적으로 컴파일 가능한 완성 단위다.
> **이 프로젝트는 UVCS를 쓰므로 git 커밋 스텝이 없다.** Unity 컴파일 / Test Runner / 에셋 셋업은 **사용자가 직접** 수행한다(Claude는 코드·테스트 파일 작성까지).

**Goal:** 지하에서 인벤토리 광물을 전량 즉시 현금화(정가×레벨배율, 지하 1회/하강)하는 액티브 유물 `BlackMarket(4018)`을 추가한다.

**Architecture:** 기존 `RelicBehaviour` 프레임워크에 액티브 유물 1종을 붙인다. 발동 소모 낭비를 막기 위해 코어에 `CanActivate()` 게이트 훅 1개를 신설한다. 가격 DB는 지하 씬에 상점이 없으므로 behaviour가 `MineralPriceDatabase` 에셋 참조를 직접 보유(생성기가 에디터에서 자동 연결).

**Tech Stack:** Unity C#, `Relic.*` 네임스페이스, `[SerializeReference]` behaviour + `RelicSO`.

## Global Constraints

- 네임스페이스: `Relic` (behaviour), `Relic.Data` (RelicID).
- RelicID: `BlackMarket = 4018` (4017 ToolSwap 다음).
- 판매 배율: `sellRatePerLevel = { 0.8f, 0.9f, 1.0f }`.
- 횟수: `usesPerLevel = { 1, 1, 1 }` (구조만 선반영, 현재 전 레벨 1회/하강).
- `maxLevel = 3`.
- 지하 판정 씬 이름 상수: `"DemoUnderground"`.
- 쿨다운 센티넬(소진 락): `999999f`.
- 더티 플래그·직접 골드 조작 금지 — `ctx.stat.AddGold(int)` + `DayEarningsLedger.Report(DayEarningsCategory.MineralSale, int)` 사용(ShopManager와 동일 경로).
- 공유 파일(`RelicID.cs`, `RelicSliceAssetGenerator.cs`, `RelicDebugGranter.cs`, `RelicBehaviour.cs`, `RelicManager.cs`)은 한 태스크씩 끝내고 진행.

---

### Task 1: 코어에 `CanActivate()` 게이트 훅 추가

발동 전 조건 검사 훅. 현재 `ActivateSlot`은 상태기계(`TryActivate`)를 `OnActivate`보다 먼저 돌려 조건 불충족 시 발동을 낭비한다. 이를 막는다.

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Core/RelicBehaviour.cs`
- Modify: `Assets/Scripts/Gameplay/Relics/Core/RelicManager.cs` (`ActivateSlot`, 135–151행 부근)

**Interfaces:**
- Produces: `virtual bool RelicBehaviour.CanActivate()` — 기본 `true`. false면 `RelicManager.ActivateSlot`이 상태기계를 건드리지 않고 즉시 반환.

- [ ] **Step 1: `RelicBehaviour`에 훅 추가**

`Core/RelicBehaviour.cs`의 액티브 생명주기 섹션(현재 25행 `GetCooldown` 위)에 한 줄 추가:

```csharp
        // ── 액티브 생명주기 (액티브 유물만 override) ──
        // 발동 전 게이트. false면 상태기계를 소모하지 않고 발동을 막는다(조건부 액티브용).
        public virtual bool CanActivate() => true;
        public virtual float GetCooldown() => 0f;
```

- [ ] **Step 2: `RelicManager.ActivateSlot`에 게이트 삽입**

`Core/RelicManager.cs`의 `ActivateSlot`에서 `if (b == null || st == null) return;` 다음 줄에 게이트를 추가:

```csharp
        public void ActivateSlot(int slot)
        {
            if (slot < 0 || slot >= _slotBehaviour.Length) return;
            var b = _slotBehaviour[slot];
            var st = _slotActive[slot];
            if (b == null || st == null) return; // 패시브거나 빈 슬롯

            if (!b.CanActivate()) return; // 조건 불충족 → 발동 소모 안 함

            // 토글형: 지속 중 재입력이면 조기 종료
            if (b.IsToggle && st.CancelToCooldown())
            {
                b.OnActiveEnd();
                return;
            }

            if (st.TryActivate(b.GetDuration(), b.GetCooldown()))
                b.OnActivate();
        }
```

- [ ] **Step 3: 컴파일 확인 (사용자)**

Unity로 전환해 컴파일 에러가 없는지 확인. 기존 액티브 유물(무적·발전기 등)은 `CanActivate` 기본 `true`라 동작 불변.

---

### Task 2: `BlackMarketRelic` behaviour 작성

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Behaviours/BlackMarketRelic.cs`

**Interfaces:**
- Consumes: `RelicBehaviour.CanActivate()`(Task 1), `RelicContext.stat`(PlayerStat), `MineralInventory.ReadonlyItems`/`RemoveAllOf`, `MineralPriceDatabase.GetPrice`, `DayEarningsLedger.Report`, `ShopManager.priceDatabase`.
- Produces: `BlackMarketRelic.Configure(MineralPriceDatabase) : BlackMarketRelic` — 생성기(Task 3)가 가격DB 자동 연결에 사용. `static int BlackMarketRelic.ComputeGold(long raw, float rate)` — 판매액 계산(테스트 대상).

- [ ] **Step 1: behaviour 클래스 작성**

`Behaviours/BlackMarketRelic.cs` 신규 생성. 전체 내용:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Relic
{
    // 암시장: 지하에서 인벤토리 광물을 전량 즉시 현금화(정가×레벨배율). 지하 1회/하강.
    // 지상엔 상점이 있으므로 지하 씬(DemoUnderground)에서만 발동 가능.
    [Serializable]
    public class BlackMarketRelic : RelicBehaviour
    {
        private const string UndergroundScene = "DemoUnderground";
        private const float   CooldownSentinel = 999999f; // 소진 후 이번 하강 내내 락

        [Tooltip("레벨별 판매 배율(정가 대비). Lv1=0.8 → Lv3=1.0")]
        [SerializeField] private float[] sellRatePerLevel = { 0.8f, 0.9f, 1.0f };

        [Tooltip("레벨별 하강당 사용 횟수. 지금은 전 레벨 1회.")]
        [SerializeField] private int[] usesPerLevel = { 1, 1, 1 };

        [Tooltip("가격 데이터베이스. 생성기가 자동 연결(런타임 폴백: ShopManager).")]
        [SerializeField] private MineralPriceDatabase priceDb;

        // ── 런타임 전용(비직렬화). 씬 재장착마다 OnEquip에서 리셋 → 하강당 자동 충전. ──
        private MineralInventory _inv;
        private int _usesLeft;

        // 생성기(에디터)에서 가격DB 주입용.
        public BlackMarketRelic Configure(MineralPriceDatabase db)
        {
            priceDb = db;
            return this;
        }

        private float SellRate() => sellRatePerLevel[Mathf.Clamp(level - 1, 0, sellRatePerLevel.Length - 1)];
        private int   UsesForLevel() => usesPerLevel[Mathf.Clamp(level - 1, 0, usesPerLevel.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            ResolveRefs();
            _usesLeft = UsesForLevel();
        }

        public override void OnLevelChanged(int lv)
        {
            base.OnLevelChanged(lv);
            // 하강 중 강화 시 남은 횟수를 새 레벨 최대치로 맞춘다(현재 값 동일이라 무해).
            _usesLeft = Mathf.Max(_usesLeft, UsesForLevel());
        }

        private void ResolveRefs()
        {
            if (_inv == null) _inv = UnityEngine.Object.FindFirstObjectByType<MineralInventory>();
            if (priceDb == null)
            {
                var shop = UnityEngine.Object.FindFirstObjectByType<ShopManager>();
                if (shop != null) priceDb = shop.priceDatabase;
            }
        }

        private static bool IsUnderground() => SceneManager.GetActiveScene().name == UndergroundScene;

        private bool HasAnyMineral()
        {
            if (_inv == null) return false;
            foreach (var slot in _inv.ReadonlyItems)
                if (slot != null && slot.item is MineralSO) return true;
            return false;
        }

        // 발동 게이트: 지하 && 광물 보유 && 남은 횟수 && 가격DB 존재.
        public override bool CanActivate()
        {
            ResolveRefs();
            return IsUnderground() && _usesLeft > 0 && priceDb != null && HasAnyMineral();
        }

        public override float GetDuration() => 0f; // 즉발
        // 이번 발동으로 소진되면 센티넬로 UI 락, 아직 남으면 즉시 재사용(미래 N회 대비).
        public override float GetCooldown() => (_usesLeft <= 1) ? CooldownSentinel : 0f;

        // 판매액 계산(순수 함수, 테스트 대상).
        public static int ComputeGold(long raw, float rate) => Mathf.FloorToInt((float)(raw * rate));

        public override void OnActivate()
        {
            ResolveRefs();
            if (_inv == null || priceDb == null) return;

            long raw = 0;
            var distinct = new HashSet<MineralSO>();
            foreach (var slot in _inv.ReadonlyItems)
            {
                if (slot == null || !(slot.item is MineralSO m)) continue;
                raw += (long)priceDb.GetPrice(m.mineralID) * slot.quantity;
                distinct.Add(m);
            }
            if (raw <= 0 || distinct.Count == 0) return;

            int gold = ComputeGold(raw, SellRate());
            ctx?.stat?.AddGold(gold);
            DayEarningsLedger.Report(DayEarningsCategory.MineralSale, gold);

            foreach (var m in distinct) _inv.RemoveAllOf(m);

            _usesLeft--;
            Debug.Log($"[BlackMarket] 광물 전량 판매 → {gold} gold (rate {SellRate():0.##}, 남은 횟수 {_usesLeft})");
        }
    }
}
```

- [ ] **Step 2: EditMode 테스트 작성 — `ComputeGold` 순수 로직 (사용자 실행)**

가격 합·배율·내림 계산이 유일한 순수 로직이다. `Assets/Scripts/Utils/TestHelpers/` 또는 기존 테스트 asmdef 위치에 EditMode 테스트를 생성. 경로가 프로젝트 테스트 asmdef를 참조해야 함(기존 유물 테스트 폴더가 있으면 거기).

`Assets/Tests/EditMode/BlackMarketRelicTests.cs` (테스트 asmdef가 다른 경로면 그에 맞게 이동):

```csharp
using NUnit.Framework;
using Relic;

public class BlackMarketRelicTests
{
    [Test]
    public void ComputeGold_Lv1_Floors80Percent()
    {
        // raw 1000 * 0.8 = 800
        Assert.AreEqual(800, BlackMarketRelic.ComputeGold(1000, 0.8f));
    }

    [Test]
    public void ComputeGold_FullRate_Unchanged()
    {
        Assert.AreEqual(1000, BlackMarketRelic.ComputeGold(1000, 1.0f));
    }

    [Test]
    public void ComputeGold_FractionFloored()
    {
        // 333 * 0.8 = 266.4 → 266
        Assert.AreEqual(266, BlackMarketRelic.ComputeGold(333, 0.8f));
    }

    [Test]
    public void ComputeGold_Zero_ReturnsZero()
    {
        Assert.AreEqual(0, BlackMarketRelic.ComputeGold(0, 0.8f));
    }
}
```

- [ ] **Step 3: 컴파일 + EditMode 테스트 실행 (사용자)**

Unity Test Runner(EditMode)에서 `BlackMarketRelicTests` 4건 통과 확인. 컴파일 에러 없을 것.
(참고: `slot.quantity`는 비스택 광물도 슬롯당 1로 채워지므로 `price × quantity` 합산이 스택/비스택 모두 정확.)

---

### Task 3: 생성기 등록 + 가격DB 자동 연결 + maxLevel=1

**Files:**
- Modify: `Assets/Scripts/Editor/RelicSliceAssetGenerator.cs`

**Interfaces:**
- Consumes: `BlackMarketRelic.Configure(MineralPriceDatabase)`(Task 2).

- [ ] **Step 1: 가격DB 자동 탐색 + 유물 생성 등록**

`Generate()` 내부, `toolSwap` 생성 줄(38행) 다음에 추가:

```csharp
        var toolSwap = CreateRelic(RelicID.ToolSwap,    "도구 역할 스왑",    RelicType.Passive, new ToolSwapRelic());

        // 암시장: 가격DB 에셋을 자동 탐색해 behaviour에 주입(지하엔 상점이 없어 직접 참조 필요).
        var priceDb = FindFirstAsset<MineralPriceDatabase>();
        if (priceDb == null)
            Debug.LogWarning("[Relic] MineralPriceDatabase 에셋을 찾지 못함 — 암시장은 런타임에 ShopManager 폴백을 시도합니다.");
        var blackMarket = CreateRelic(RelicID.BlackMarket, "암시장", RelicType.Active,
            new BlackMarketRelic().Configure(priceDb));
```

- [ ] **Step 2: `db.allRelics` 리스트에 추가**

41행 리스트 끝에 `blackMarket` 추가:

```csharp
        db.allRelics = new List<RelicSO> { stat, pigeon, magnet, invinc, anvil, plasma, gambler, steroid, drone, spider, genr, spring, gravFlip, dashBomb, furnace, lightning, toolSwap, blackMarket };
```

- [ ] **Step 3: 에셋 탐색 헬퍼 추가**

`FindOrCreate<T>`(78행) 아래에 헬퍼 추가:

```csharp
    private static T FindFirstAsset<T>() where T : Object
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        if (guids == null || guids.Length == 0) return null;
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }
```

> 파일 상단 `using UnityEngine;`에 `Object`(UnityEngine.Object)가 포함됨 — 별도 using 불필요.

- [ ] **Step 4: `BlackMarket` 유물만 `maxLevel` 유지 확인**

`CreateRelic`은 `maxLevel = 3`을 세팅한다. 암시장은 레벨=판매배율 상승이므로 **3레벨 유지가 맞다**(변경 불필요). 강화비용도 기존 기본값(100/250 gold) 사용.

- [ ] **Step 5: 에셋 생성 실행 (사용자)**

Unity에서 `Tools > Relic > Generate Slice Assets` 실행 → `Assets/GameData/Relics/BlackMarket.asset` 생성, `priceDb` 필드 자동 연결, `RelicDatabase.asset`에 등록됨. 인스펙터에서 `BlackMarket.asset`의 behaviour → `priceDb`가 채워졌는지 확인.

---

### Task 4: 디버그 키 등록 (F2)

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs` (`DefaultBindings`, 74–93행)

**Interfaces:**
- Consumes: `RelicID.BlackMarket`(Task 1 이후 존재).

- [ ] **Step 1: 바인딩 추가**

`DefaultBindings()` 배열에 F2 항목 추가(F1 Plasma·F3 Gambler·F5 Anvil 점유 중 → F2 자유). 슬롯은 Anvil(slot 0)과 겹치지 않게 slot 1:

```csharp
            new Binding { key = KeyCode.F5,      relic = RelicID.Anvil,          slot = 0 },
            new Binding { key = KeyCode.F2,      relic = RelicID.BlackMarket,    slot = 1 },
```

- [ ] **Step 2: 씬 인스턴스 폴백 주의 (사용자)**

`RelicDebugGranter`는 씬에 이미 있으면 인스펙터 `bindings`가 우선(코드 `DefaultBindings`는 빈 배열일 때만 폴백). 씬 컴포넌트를 쓰는 경우 인스펙터 bindings에 `F2 → BlackMarket → slot 1`을 수동 추가하거나, 컴포넌트의 bindings를 비워 재초기화.

---

### Task 5: 인게임 통합 검증 (사용자)

**Files:** 없음 (플레이 검증).

- [ ] **Step 1: 지하 정상 판매**
  1. 지하 씬(`DemoUnderground`)에서 광물 몇 종 채굴.
  2. F2로 암시장 지급·장착(slot 1).
  3. 슬롯1 발동 키(기본 R)로 발동.
  4. 확인: 골드 = (정가 합)×0.8 내림만큼 증가, 인벤토리 광물 전부 사라짐, 퀵슬롯 fill이 꽉 참(소진 락).

- [ ] **Step 2: 하강 1회 제한 / 재충전**
  1. Step 1 직후 다시 발동 → 무반응(락).
  2. 지상 복귀 후 다시 지하 진입 → 슬롯1 Ready 복귀, 1회 재사용 가능.

- [ ] **Step 3: 예외 게이트**
  1. 지상 씬에서 발동 시도 → 무반응(발동 소모 없음).
  2. 지하 빈 가방에서 발동 시도 → 무반응.

- [ ] **Step 4: 레벨 배율**
  1. F2 재입력으로 Lv2/Lv3 강화(재장착 없이 `RefreshLevel` 반영).
  2. 각 레벨에서 판매액 비율이 0.9 / 1.0으로 오르는지 확인.

---

## Self-Review 메모

- **Spec 커버리지**: 지하 전용(Task2 `IsUnderground`), 전량 판매(Task2 `OnActivate`), 0.8배+레벨스케일(Task2 `sellRatePerLevel`/`ComputeGold`), 1회/하강(Task2 `_usesLeft`+`GetCooldown`, Task1 게이트), 가격DB 접근(Task3 자동연결+폴백), 디버그(Task4), 검증(Task5) — 전 항목 태스크 존재.
- **타입 일관성**: `CanActivate`(bool), `Configure(MineralPriceDatabase)→BlackMarketRelic`, `ComputeGold(long,float)→int`, `AddGold(int)`, `DayEarningsLedger.Report(DayEarningsCategory,int)` — Task 간 시그니처 일치.
- **UVCS/테스트**: git 커밋 스텝 없음. Unity 컴파일·Test Runner·에셋 생성은 사용자 수행으로 명시.
