# 도깨비 가마솥 구현 플랜

> **에이전트 작업자용:** 이 플랜은 task 단위로 구현한다. 각 task는 독립적으로 테스트 가능한 산출물로 끝난다.
> 체크박스(`- [ ]`)로 진행을 추적한다.

**목표:** 땅속 1회성 특수청크 "도깨비 가마솥"을 추가한다. 광물 1개를 투입하면 확률에 따라 상위 광물·재·폭발 광물이 산출되고, 같은 가마솥은 최대 3회 사용 후 비활성(소진) 상태가 된다.

**아키텍처:** 규칙·확률은 Unity 비의존 순수 C#(`MineralUpgradeLadder`, `CauldronResolver`)로 분리해 EditMode로 테스트한다. 연출·스폰·상호작용은 `DokkaebiCauldron`(`InteractableBlockBase` 상속) MonoBehaviour가 담당한다. 사용횟수는 `PlayerData.cauldronSave`(코인·주식 세이브와 동일 패턴)로 영속화한다.

**기술 스택:** Unity 2D, C#, Burst 비의존 순수 로직, NUnit(EditMode), JsonUtility, 단일 `GameScripts.asmdef`.

## Global Constraints

- **버전 관리: UVCS(Unity Version Control). `git` 명령 금지.** "커밋" 단계는 UVCS 체크인을 의미한다(명령어 없이 체크포인트로만 표기).
- **테스트 실행은 사람이 한다.** Claude는 테스트 파일을 작성·수정만 하고 Unity Test Runner를 호출하지 않는다. "테스트 실행" 단계는 *사람이 Unity Editor > Window > General > Test Runner > EditMode에서 실행*을 뜻한다.
- EditMode 테스트는 `Assets/Tests/EditMode/`에 NUnit `[Test]`로 작성한다(네임스페이스 없음, MonoBehaviour 없는 순수 로직). 어셈블리: `Assets/Tests/EditMode/EditModeTests.asmdef`.
- 더티 플래그는 직접 세팅 금지 — `ChunkData.MarkDirty()`/`MarkRenderDirty()` 사용(CLAUDE.md §3).
- 특수청크 초기화 순서 준수: `IChunkInitializer.Initialize()` → `ApplyBorderDataOnly()` → `RestoreSavedPixels()` (CLAUDE.md §11).
- 승급 규칙·결과 광물은 하드코딩 금지 — `tileData.json` 파싱 결과(`TileDatabaseJson`)에서 파생한다.
- 광물 티어 사실: `MineralID` enum의 백의 자리가 층(100/200/300/400), 각 층 마지막 2개가 희귀. `tileData.json`이 권위 데이터.

---

## 파일 구조

| 파일 | 책임 | 신규/수정 |
|---|---|---|
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/MineralUpgradeLadder.cs` | tileData에서 8칸 승급 사다리 구축, 칸 조회·승급 해석 | 신규 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronResult.cs` | 결과 enum + 페이로드 struct | 신규 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronResolver.cs` | 입력+RNG → CauldronResult 확률 추첨 | 신규 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/DokkaebiCauldron.cs` | 상호작용·사용횟수·연출·결과 스폰·소진 | 신규 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronUI.cs` | 인벤토리 광물 선택·투입 UI | 신규 |
| `Assets/Scripts/_Core/Data/CauldronSaveData.cs` | 좌표→남은횟수 저장 DTO | 신규 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronStateStore.cs` | 런타임 좌표→남은횟수 맵 + capture/apply | 신규 |
| `Assets/Scripts/_Core/Data/SpecialChunkSettingsData.cs` | `CauldronSection` 추가 | 수정 |
| `Assets/StreamingAssets/specialChunkSettings.json` | cauldron 섹션 + 스폰 확률 추가 | 수정 |
| `Assets/Scripts/UI/Player/PlayerData.cs` | `cauldronSave` 필드 추가 | 수정 |
| `Assets/Scripts/_Core/Managers/SaveManager.cs` | Save/Load에 cauldronSave 엮기 | 수정 |
| `Assets/Tests/EditMode/MineralUpgradeLadderTests.cs` | 사다리 단위 테스트 | 신규 |
| `Assets/Tests/EditMode/CauldronResolverTests.cs` | 확률·결과 단위 테스트 | 신규 |

---

## Task 1: 승급 사다리 (MineralUpgradeLadder) — 순수 C#, EditMode TDD

`tileData.json`의 층·등급 데이터(`List<TileDataJson>`)에서 8칸 사다리를 구축한다. 각 층마다 [Common 칸][Rare 칸] 2칸. 한 광물은 **가장 얕은 층 첫 등장** 위치에만 들어간다(bleed-over 중복 제거).

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/MineralUpgradeLadder.cs`
- Test: `Assets/Tests/EditMode/MineralUpgradeLadderTests.cs`

**Interfaces:**
- Consumes: `TileDataJson`(필드 `minerals: List<MineralRuleJson>`, 각 `mineralType: string`, `IsRare: bool`), `MineralID`(enum, `Enum.TryParse`로 파싱).
- Produces:
  - `MineralUpgradeLadder(List<TileDataJson> tilesShallowToDeep)` — 생성자(층은 얕은→깊은 순서 가정, tileData.json 순서 그대로).
  - `int RungCount { get; }` (정상 데이터에서 8)
  - `int RungOf(MineralID id)` — 0-based 칸 번호, 없으면 -1
  - `IReadOnlyList<MineralID> Rung(int index)` — 해당 칸 광물 목록
  - `MineralID Resolve(MineralID input, int steps, System.Random rng, out bool clamped)` — input 칸 i 찾고 i+steps 칸(상한 초과 시 마지막 칸으로 clamp, clamped=true)에서 rng로 1개 선택. input이 사다리에 없으면 `MineralID.None` 반환·clamped=false.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/MineralUpgradeLadderTests.cs`:

```csharp
using NUnit.Framework;
using System.Collections.Generic;

/// <summary>
/// MineralUpgradeLadder — tileData 파생 8칸 사다리 + 승급 해석.
/// 순수 로직만 테스트 (MonoBehaviour 없음). 실제 tileData.json 4개 층 구조를 축약 재현.
/// </summary>
public class MineralUpgradeLadderTests
{
    // 실제 tileData.json 구조를 그대로 재현 (bleed-over 포함).
    private static MineralRuleJson R(string type, string rarity)
        => new MineralRuleJson { mineralType = type, rarity = rarity };

    private static TileDataJson Tile(string name, params MineralRuleJson[] minerals)
        => new TileDataJson { tileType = name, minerals = new List<MineralRuleJson>(minerals) };

    private MineralUpgradeLadder MakeLadder()
    {
        var tiles = new List<TileDataJson>
        {
            Tile("Dirt",
                R("ScrapMetal","Common"), R("GarbageBag","Common"), R("PETBottle","Common"), R("Coal","Common"),
                R("Copper","Rare"), R("Iron","Rare")),
            Tile("Ice",
                R("Meteorite","Common"), R("Fossil","Common"), R("Silver","Common"), R("Sapphire","Common"),
                R("Emerald","Rare"), R("Topaz","Rare"),
                R("Copper","Common"), R("Iron","Common")), // bleed-over
            Tile("MagmaRock",
                R("Obsidian","Common"), R("Quartz","Common"), R("Gold","Common"), R("Ruby","Common"),
                R("Diamond","Rare"), R("LavaStone","Rare"),
                R("Emerald","Common"), R("Topaz","Common")), // bleed-over
            Tile("MeteoriteRock",
                R("Mithril","Common"), R("Gravitonium","Common"), R("Uranium","Common"),
                R("VoidStone","Rare"), R("StarFragment","Rare"),
                R("Diamond","Common"), R("LavaStone","Common")), // bleed-over
        };
        return new MineralUpgradeLadder(tiles);
    }

    [Test]
    public void RungCount_FourLayers_IsEight()
    {
        Assert.AreEqual(8, MakeLadder().RungCount);
    }

    [Test]
    public void RungOf_CommonMineral_ReturnsLayerCommonRung()
    {
        Assert.AreEqual(0, MakeLadder().RungOf(MineralID.Coal));      // Dirt-Common
        Assert.AreEqual(2, MakeLadder().RungOf(MineralID.Meteorite)); // Ice-Common
    }

    [Test]
    public void RungOf_RareMineral_ReturnsLayerRareRung()
    {
        Assert.AreEqual(1, MakeLadder().RungOf(MineralID.Copper));  // Dirt-Rare
        Assert.AreEqual(3, MakeLadder().RungOf(MineralID.Emerald)); // Ice-Rare
    }

    [Test]
    public void RungOf_BleedOverMineral_UsesShallowestFirstAppearance()
    {
        // Copper는 Ice에서 Common으로 재등장하지만 Dirt-Rare(rung1)가 우선.
        Assert.AreEqual(1, MakeLadder().RungOf(MineralID.Copper));
        Assert.AreEqual(3, MakeLadder().RungOf(MineralID.Emerald));
        Assert.AreEqual(5, MakeLadder().RungOf(MineralID.Diamond));
    }

    [Test]
    public void Resolve_Success_OneStepUp_ResultInNextRung()
    {
        var ladder = MakeLadder();
        var result = ladder.Resolve(MineralID.Coal, 1, new System.Random(0), out bool clamped);
        Assert.IsFalse(clamped);
        CollectionAssert.Contains((System.Collections.ICollection)ladder.Rung(1), result); // {Copper, Iron}
    }

    [Test]
    public void Resolve_GreatSuccess_TwoStepsUp_ResultInRungPlusTwo()
    {
        var ladder = MakeLadder();
        var result = ladder.Resolve(MineralID.Coal, 2, new System.Random(0), out bool clamped);
        Assert.IsFalse(clamped);
        CollectionAssert.Contains((System.Collections.ICollection)ladder.Rung(2), result); // Ice-Common
    }

    [Test]
    public void Resolve_TopRung_Clamps()
    {
        var ladder = MakeLadder();
        var result = ladder.Resolve(MineralID.StarFragment, 1, new System.Random(0), out bool clamped);
        Assert.IsTrue(clamped);
        CollectionAssert.Contains((System.Collections.ICollection)ladder.Rung(7), result);
    }

    [Test]
    public void Resolve_UnknownMineral_ReturnsNone()
    {
        var result = MakeLadder().Resolve(MineralID.None, 1, new System.Random(0), out bool clamped);
        Assert.AreEqual(MineralID.None, result);
        Assert.IsFalse(clamped);
    }
}
```

- [ ] **Step 2: 테스트 실행해 실패 확인 (사람)**

사람이 Unity Test Runner > EditMode 실행.
기대: 컴파일 에러("MineralUpgradeLadder를 찾을 수 없음") 또는 전체 FAIL.

- [ ] **Step 3: 최소 구현 작성**

`Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/MineralUpgradeLadder.cs`:

```csharp
// @tags: cauldron, mineral, upgrade, ladder, special-chunk
using System;
using System.Collections.Generic;

/// <summary>
/// 도깨비 가마솥 승급 사다리. tileData.json의 층·등급(rarity)을 깊이순으로 펼쳐
/// [층N 일반][층N 희귀] 2칸씩 쌓은 순서표. 한 광물은 가장 얕은 층 첫 등장 위치에만 배치(중복 제거).
/// Unity 비의존 — EditMode 테스트 대상.
/// </summary>
public class MineralUpgradeLadder
{
    private readonly List<List<MineralID>> _rungs = new List<List<MineralID>>();
    private readonly Dictionary<MineralID, int> _rungOf = new Dictionary<MineralID, int>();

    public MineralUpgradeLadder(List<TileDataJson> tilesShallowToDeep)
    {
        if (tilesShallowToDeep == null) return;

        foreach (var tile in tilesShallowToDeep)
        {
            var commonRung = new List<MineralID>();
            var rareRung   = new List<MineralID>();

            if (tile?.minerals != null)
            {
                foreach (var rule in tile.minerals)
                {
                    if (!TryParse(rule?.mineralType, out MineralID id)) continue;
                    if (_rungOf.ContainsKey(id)) continue; // 이미 더 얕은 칸에 배치됨 (bleed-over 무시)

                    var target = rule.IsRare ? rareRung : commonRung;
                    target.Add(id);
                    // 칸 인덱스는 아래에서 일괄 부여하므로 여기선 보류 → 임시로 등록만
                    _rungOf[id] = -1;
                }
            }

            _rungs.Add(commonRung);
            _rungs.Add(rareRung);
        }

        // 칸 인덱스 확정
        for (int i = 0; i < _rungs.Count; i++)
            foreach (var id in _rungs[i])
                _rungOf[id] = i;
    }

    public int RungCount => _rungs.Count;

    public int RungOf(MineralID id)
        => _rungOf.TryGetValue(id, out int i) ? i : -1;

    public IReadOnlyList<MineralID> Rung(int index)
        => (index >= 0 && index < _rungs.Count) ? _rungs[index] : Array.Empty<MineralID>();

    public MineralID Resolve(MineralID input, int steps, System.Random rng, out bool clamped)
    {
        clamped = false;
        int i = RungOf(input);
        if (i < 0) return MineralID.None;

        int target = i + steps;
        if (target >= _rungs.Count)
        {
            target = _rungs.Count - 1;
            clamped = true;
        }

        var rung = _rungs[target];
        if (rung.Count == 0) return input; // 빈 칸 방어: 입력 그대로
        return rung[rng.Next(rung.Count)];
    }

    private static bool TryParse(string typeName, out MineralID id)
    {
        id = MineralID.None;
        if (string.IsNullOrEmpty(typeName)) return false;
        return Enum.TryParse(typeName, true, out id) && id != MineralID.None;
    }
}
```

- [ ] **Step 4: 테스트 실행해 통과 확인 (사람)**

사람이 Unity Test Runner > EditMode 실행. 기대: 8개 테스트 전부 PASS.

- [ ] **Step 5: 체크포인트 (UVCS 체크인)**

메시지: `feat(cauldron): MineralUpgradeLadder + EditMode tests`

---

## Task 2: 결과 페이로드 + 확률 해석 (CauldronResult, CauldronResolver) — 순수 C#, EditMode TDD

확률(15/45/25/15)로 결과를 추첨하고, 성공계열은 사다리로 결과 광물을, 대실패는 폭발 광물 개수를 채운다.

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronResult.cs`
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronResolver.cs`
- Test: `Assets/Tests/EditMode/CauldronResolverTests.cs`

**Interfaces:**
- Consumes: `MineralUpgradeLadder`(Task 1), `MineralID`.
- Produces:
  - `enum CauldronOutcome { GreatSuccess, Success, Fail, GreatFail }`
  - `enum CauldronRewardType { Mineral, Ash, Explosion, EasterEgg }`
  - `struct CauldronResult { CauldronOutcome outcome; CauldronRewardType rewardType; MineralID resultMineral; int explosiveCount; }`
  - `struct CauldronProbabilities { double greatSuccess, success, fail, greatFail; }` (합 1.0 가정)
  - `CauldronResolver(MineralUpgradeLadder ladder, CauldronProbabilities probs, int explosiveMin, int explosiveMax)`
  - `CauldronResult Resolve(MineralID input, System.Random rng)`
    - 추첨 순서: roll=rng.NextDouble(); `< gs` GreatSuccess / `< gs+s` Success / `< gs+s+f` Fail / else GreatFail.
    - GreatSuccess: steps=2 / Success: steps=1 → `ladder.Resolve`. clamped면 rewardType=EasterEgg, 아니면 Mineral. resultMineral 채움.
    - Fail: rewardType=Ash.
    - GreatFail: rewardType=Explosion, explosiveCount=rng.Next(explosiveMin, explosiveMax+1).

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/CauldronResolverTests.cs`:

```csharp
using NUnit.Framework;
using System.Collections.Generic;

public class CauldronResolverTests
{
    private static MineralRuleJson R(string t, string r) => new MineralRuleJson { mineralType = t, rarity = r };
    private static TileDataJson Tile(string n, params MineralRuleJson[] m)
        => new TileDataJson { tileType = n, minerals = new List<MineralRuleJson>(m) };

    private MineralUpgradeLadder MakeLadder() => new MineralUpgradeLadder(new List<TileDataJson>
    {
        Tile("Dirt", R("Coal","Common"), R("Copper","Rare"), R("Iron","Rare")),
        Tile("Ice",  R("Silver","Common"), R("Emerald","Rare")),
        Tile("Magma",R("Gold","Common"), R("Diamond","Rare")),
        Tile("Meteor",R("Mithril","Common"), R("StarFragment","Rare")),
    });

    private CauldronResolver MakeResolver()
    {
        var probs = new CauldronProbabilities { greatSuccess = 0.15, success = 0.45, fail = 0.25, greatFail = 0.15 };
        return new CauldronResolver(MakeLadder(), probs, explosiveMin: 3, explosiveMax: 6);
    }

    // 결정론적 결과를 위해 NextDouble을 제어할 수 없으므로, 경계 분포를 통계로 검증.
    [Test]
    public void Resolve_Distribution_MatchesProbabilities()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(12345);
        var counts = new Dictionary<CauldronOutcome, int>
        {
            { CauldronOutcome.GreatSuccess, 0 }, { CauldronOutcome.Success, 0 },
            { CauldronOutcome.Fail, 0 }, { CauldronOutcome.GreatFail, 0 },
        };
        const int N = 100000;
        for (int i = 0; i < N; i++)
            counts[resolver.Resolve(MineralID.Coal, rng).outcome]++;

        Assert.That(counts[CauldronOutcome.GreatSuccess] / (double)N, Is.EqualTo(0.15).Within(0.02));
        Assert.That(counts[CauldronOutcome.Success]      / (double)N, Is.EqualTo(0.45).Within(0.02));
        Assert.That(counts[CauldronOutcome.Fail]         / (double)N, Is.EqualTo(0.25).Within(0.02));
        Assert.That(counts[CauldronOutcome.GreatFail]    / (double)N, Is.EqualTo(0.15).Within(0.02));
    }

    [Test]
    public void Resolve_Success_GivesMineralReward_OneRungUp()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(1);
        // Coal(rung0) 성공 결과는 항상 rung1(Copper/Iron) 또는 rung2(대성공). 광물 보상이어야.
        for (int i = 0; i < 1000; i++)
        {
            var res = resolver.Resolve(MineralID.Coal, rng);
            if (res.outcome == CauldronOutcome.Success)
            {
                Assert.AreEqual(CauldronRewardType.Mineral, res.rewardType);
                Assert.AreNotEqual(MineralID.None, res.resultMineral);
            }
        }
    }

    [Test]
    public void Resolve_Fail_GivesAsh()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(2);
        for (int i = 0; i < 1000; i++)
        {
            var res = resolver.Resolve(MineralID.Coal, rng);
            if (res.outcome == CauldronOutcome.Fail)
                Assert.AreEqual(CauldronRewardType.Ash, res.rewardType);
        }
    }

    [Test]
    public void Resolve_GreatFail_GivesExplosionWithinRange()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(3);
        for (int i = 0; i < 1000; i++)
        {
            var res = resolver.Resolve(MineralID.Coal, rng);
            if (res.outcome == CauldronOutcome.GreatFail)
            {
                Assert.AreEqual(CauldronRewardType.Explosion, res.rewardType);
                Assert.That(res.explosiveCount, Is.InRange(3, 6));
            }
        }
    }

    [Test]
    public void Resolve_TopMineral_Success_FlagsEasterEgg()
    {
        var resolver = MakeResolver();
        var rng = new System.Random(4);
        for (int i = 0; i < 2000; i++)
        {
            var res = resolver.Resolve(MineralID.StarFragment, rng);
            if (res.outcome == CauldronOutcome.Success || res.outcome == CauldronOutcome.GreatSuccess)
                Assert.AreEqual(CauldronRewardType.EasterEgg, res.rewardType);
        }
    }
}
```

- [ ] **Step 2: 테스트 실행해 실패 확인 (사람)**

사람이 EditMode 실행. 기대: 컴파일 에러 또는 FAIL.

- [ ] **Step 3: 최소 구현 작성**

`Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronResult.cs`:

```csharp
// @tags: cauldron, result, reward, special-chunk
/// <summary>도깨비 가마솥 1회 시도 결과 종류.</summary>
public enum CauldronOutcome { GreatSuccess, Success, Fail, GreatFail }

/// <summary>결과로 무엇이 산출되는지. EasterEgg는 최상위 광물 투입 시 분기(추후 유니크 보상 hook).</summary>
public enum CauldronRewardType { Mineral, Ash, Explosion, EasterEgg }

/// <summary>가마솥 1회 시도의 해석 결과 페이로드.</summary>
public struct CauldronResult
{
    public CauldronOutcome outcome;
    public CauldronRewardType rewardType;
    public MineralID resultMineral; // Mineral / EasterEgg일 때 유효
    public int explosiveCount;      // Explosion일 때 유효
}

/// <summary>확률 묶음. 합은 1.0 가정.</summary>
public struct CauldronProbabilities
{
    public double greatSuccess;
    public double success;
    public double fail;
    public double greatFail;
}
```

`Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronResolver.cs`:

```csharp
// @tags: cauldron, resolver, probability, special-chunk
using System;

/// <summary>
/// 입력 광물 + RNG → CauldronResult. 확률 추첨 후 성공계열은 사다리 승급, 실패는 재, 대실패는 폭발 개수.
/// Unity 비의존 — EditMode 테스트 대상.
/// </summary>
public class CauldronResolver
{
    private readonly MineralUpgradeLadder _ladder;
    private readonly CauldronProbabilities _probs;
    private readonly int _explosiveMin;
    private readonly int _explosiveMax;

    public CauldronResolver(MineralUpgradeLadder ladder, CauldronProbabilities probs, int explosiveMin, int explosiveMax)
    {
        _ladder = ladder;
        _probs = probs;
        _explosiveMin = explosiveMin;
        _explosiveMax = explosiveMax;
    }

    public CauldronResult Resolve(MineralID input, System.Random rng)
    {
        double roll = rng.NextDouble();
        double gs = _probs.greatSuccess;
        double s  = gs + _probs.success;
        double f  = s + _probs.fail;

        if (roll < gs) return Upgrade(input, 2, CauldronOutcome.GreatSuccess, rng);
        if (roll < s)  return Upgrade(input, 1, CauldronOutcome.Success, rng);
        if (roll < f)  return new CauldronResult { outcome = CauldronOutcome.Fail, rewardType = CauldronRewardType.Ash };

        return new CauldronResult
        {
            outcome = CauldronOutcome.GreatFail,
            rewardType = CauldronRewardType.Explosion,
            explosiveCount = rng.Next(_explosiveMin, _explosiveMax + 1)
        };
    }

    private CauldronResult Upgrade(MineralID input, int steps, CauldronOutcome outcome, System.Random rng)
    {
        MineralID result = _ladder.Resolve(input, steps, rng, out bool clamped);
        return new CauldronResult
        {
            outcome = outcome,
            rewardType = clamped ? CauldronRewardType.EasterEgg : CauldronRewardType.Mineral,
            resultMineral = result
        };
    }
}
```

- [ ] **Step 4: 테스트 실행해 통과 확인 (사람)**

사람이 EditMode 실행. 기대: 5개 테스트 전부 PASS.

- [ ] **Step 5: 체크포인트 (UVCS 체크인)**

메시지: `feat(cauldron): CauldronResolver + result payload + tests`

---

## Task 3: 설정 데이터 (CauldronSection + JSON)

확률·폭발 개수·최대 사용횟수를 `specialChunkSettings.json`에 노출하고, 스폰 확률 항목을 추가한다.

**Files:**
- Modify: `Assets/Scripts/_Core/Data/SpecialChunkSettingsData.cs`
- Modify: `Assets/StreamingAssets/specialChunkSettings.json`

**Interfaces:**
- Produces: `SpecialChunkSettingsData.CauldronSection cauldron` (필드: `float greatSuccessChance, successChance, failChance, greatFailChance; int explosiveMin, explosiveMax, maxUses;`)
  + 헬퍼 `CauldronProbabilities ToProbabilities()`.

- [ ] **Step 1: CauldronSection 추가**

`SpecialChunkSettingsData.cs`에 `mineralRock` 섹션 선언 아래(파일 끝 클래스 내부)에 추가:

```csharp
    // ─── Dokkaebi Cauldron (도깨비 가마솥) ───────────────────────
    public CauldronSection cauldron = new CauldronSection();

    [System.Serializable]
    public class CauldronSection
    {
        public float greatSuccessChance = 0.15f;
        public float successChance      = 0.45f;
        public float failChance         = 0.25f;
        public float greatFailChance    = 0.15f;
        public int   explosiveMin       = 3;
        public int   explosiveMax       = 6;
        public int   maxUses            = 3;

        public CauldronProbabilities ToProbabilities() => new CauldronProbabilities
        {
            greatSuccess = greatSuccessChance,
            success      = successChance,
            fail         = failChance,
            greatFail    = greatFailChance,
        };
    }
```

- [ ] **Step 2: JSON에 cauldron 섹션 + 스폰 확률 추가**

`specialChunkSettings.json` — `spawning.spawnChances` 배열에 항목 추가(초기 스폰 확률은 튜닝 전 0.0, 테스트 시 강제 스폰):

```json
      {
        "chunkTypeName": "DokkaebiCauldron",
        "chance": 0.0
      }
```

그리고 최상위 객체(`mineralRock` 뒤)에 섹션 추가:

```json
  "cauldron": {
    "greatSuccessChance": 0.15,
    "successChance": 0.45,
    "failChance": 0.25,
    "greatFailChance": 0.15,
    "explosiveMin": 3,
    "explosiveMax": 6,
    "maxUses": 3
  }
```

- [ ] **Step 3: 검증 (사람)**

Unity 콘솔에서 `SpecialChunkSettingsLoader`가 에러 없이 로드하는지 확인(기존 로더가 JsonUtility로 파싱). 기대: 파싱 에러 없음.

- [ ] **Step 4: 체크포인트 (UVCS 체크인)**

메시지: `feat(cauldron): settings section + spawn entry`

---

## Task 4: 저장 데이터 (CauldronSaveData + CauldronStateStore + SaveManager 연동)

사용횟수를 좌표 기준으로 영속화한다. 코인·주식 세이브(`PlayerData.coinSave`)와 동일 패턴.

**Files:**
- Create: `Assets/Scripts/_Core/Data/CauldronSaveData.cs`
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronStateStore.cs`
- Modify: `Assets/Scripts/UI/Player/PlayerData.cs`
- Modify: `Assets/Scripts/_Core/Managers/SaveManager.cs`

**Interfaces:**
- Consumes: `SaveManager.Instance.playerData`(`PlayerData` 인스턴스), `Vector2Int`(청크 좌표).
- Produces:
  - `CauldronSaveData { List<CauldronEntry> entries; }`, `CauldronEntry { int x, y, remainingUses; }`
  - `static CauldronStateStore`:
    - `int GetRemainingUses(Vector2Int coord, int defaultUses)` — 저장에 없으면 defaultUses 반환
    - `void SetRemainingUses(Vector2Int coord, int uses)` — 런타임 맵 갱신
    - `CauldronSaveData Capture()` / `void Apply(CauldronSaveData data)`
    - `void Clear()` (NewGame용)

- [ ] **Step 1: DTO 작성**

`Assets/Scripts/_Core/Data/CauldronSaveData.cs`:

```csharp
// @tags: cauldron, save, data-container, dto
using System.Collections.Generic;

[System.Serializable]
public class CauldronSaveData
{
    public List<CauldronEntry> entries = new List<CauldronEntry>();
}

[System.Serializable]
public class CauldronEntry
{
    public int x;
    public int y;
    public int remainingUses;
}
```

- [ ] **Step 2: StateStore 작성**

`Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronStateStore.cs`:

```csharp
// @tags: cauldron, save, state, store
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가마솥 좌표→남은횟수 런타임 맵. SaveManager가 Capture/Apply로 PlayerData.cauldronSave와 동기화.
/// 코인·주식 세이브와 동일하게 영속 데이터는 SaveManager 경유.
/// </summary>
public static class CauldronStateStore
{
    private static readonly Dictionary<Vector2Int, int> _remaining = new Dictionary<Vector2Int, int>();

    public static int GetRemainingUses(Vector2Int coord, int defaultUses)
        => _remaining.TryGetValue(coord, out int n) ? n : defaultUses;

    public static void SetRemainingUses(Vector2Int coord, int uses)
        => _remaining[coord] = uses;

    public static CauldronSaveData Capture()
    {
        var data = new CauldronSaveData();
        foreach (var kv in _remaining)
            data.entries.Add(new CauldronEntry { x = kv.Key.x, y = kv.Key.y, remainingUses = kv.Value });
        return data;
    }

    public static void Apply(CauldronSaveData data)
    {
        _remaining.Clear();
        if (data?.entries == null) return;
        foreach (var e in data.entries)
            _remaining[new Vector2Int(e.x, e.y)] = e.remainingUses;
    }

    public static void Clear() => _remaining.Clear();
}
```

- [ ] **Step 3: PlayerData에 필드 추가**

`Assets/Scripts/UI/Player/PlayerData.cs` — `coinSave` 선언 아래에 추가:

```csharp
    [Header("Dokkaebi Cauldron")]
    public CauldronSaveData cauldronSave = new CauldronSaveData();
```

- [ ] **Step 4: SaveManager Save/Load 연동**

`Assets/Scripts/_Core/Managers/SaveManager.cs` — `data.coinSave = ...` 블록(약 403행) 바로 아래에 Capture 추가:

```csharp
            data.cauldronSave = CauldronStateStore.Capture();
```

그리고 Load의 `CoinGameManager...ApplySaveData` 블록(약 507~510행) 아래에 Apply 추가:

```csharp
        if (data.cauldronSave != null)
            CauldronStateStore.Apply(data.cauldronSave);
```

(주의: `NewGame` 경로가 있으면 `CauldronStateStore.Clear()` 호출도 추가 — 기존 코인/주식 초기화와 동일 위치.)

- [ ] **Step 5: 검증 (사람, PlayMode)**

세이브→로드 시 콘솔 에러 없음, JSON 직렬화에 `cauldronSave` 포함 확인.

- [ ] **Step 6: 체크포인트 (UVCS 체크인)**

메시지: `feat(cauldron): coord-keyed use-count persistence via SaveManager`

---

## Task 5: 가마솥 본체 (DokkaebiCauldron MonoBehaviour)

상호작용 → UI 열기 → 결과 적용 → 사용횟수 차감 → 소진 처리. 결과 스폰은 Task 6에서 채우는 메서드를 호출만 한다.

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/DokkaebiCauldron.cs`

**Interfaces:**
- Consumes: `InteractableBlockBase`(상속), `IChunkInitializer`(구현), `CauldronResolver`/`MineralUpgradeLadder`(Task 1·2), `CauldronStateStore`(Task 4), `SpecialChunkSettingsLoader.Instance.Settings.cauldron`(Task 3), `CauldronUI`(Task 6 — `Open(MineralInventory, Action<MineralID>)` 시그니처로 호출), `TileDatabaseLoader`(tileData 접근 — 기존 로더 사용; 정확한 접근자는 구현 시 `SpecialChunkSettingsLoader`와 동일 패턴 확인).
- Produces:
  - `void Initialize(Transform parent)` (IChunkInitializer) — 좌표 기반 남은횟수 복원, 소진 상태 비주얼.
  - `int RemainingUses { get; }`
  - `void SpawnReward(CauldronResult result)` (Task 6에서 본문 구현; Task 5에서는 빈 메서드 + TODO 주석 대신 `partial` 분리 — 아래 참고).

> 결과 스폰은 코드량이 크므로 `DokkaebiCauldron`을 `partial class`로 두고, 스폰 본문은 Task 6의 `DokkaebiCauldron.Spawn.cs`에 둔다. Task 5에서는 스폰 메서드 시그니처만 선언한다.

- [ ] **Step 1: 본체 작성**

`Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/DokkaebiCauldron.cs`:

```csharp
// @tags: cauldron, special-chunk, interactable, mineral, upgrade
using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 도깨비 가마솥 — 광물 1개를 제련해 확률 결과를 산출하는 1회성(최대 maxUses회) 특수청크.
/// 상호작용·횟수관리·연출을 담당하고, 규칙은 CauldronResolver(순수 C#)에 위임한다.
/// 스폰 본문은 partial DokkaebiCauldron.Spawn.cs 참고.
/// </summary>
public partial class DokkaebiCauldron : InteractableBlockBase, IChunkInitializer
{
    [Header("Cauldron Refs")]
    [SerializeField] private CauldronUI cauldronUI;     // 씬/프리팹 내 UI (없으면 런타임 탐색)
    [SerializeField] private Transform spawnPoint;       // 결과가 튀어나오는 위치 (없으면 transform)
    [SerializeField] private SpriteRenderer bodyRenderer; // 부글부글/붉은글로우/소진 색 표현
    [SerializeField] private Color spentColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    private Vector2Int _coord;
    private int _remainingUses;
    private bool _busy;
    private CauldronResolver _resolver;

    public int RemainingUses => _remainingUses;
    public int InitializationOrder => 0;

    public void Initialize(Transform parent)
    {
        _coord = ResolveCoord(parent);

        int maxUses = SettingsMaxUses();
        _remainingUses = CauldronStateStore.GetRemainingUses(_coord, maxUses);

        _resolver = BuildResolver();

        if (_remainingUses <= 0) ApplySpentVisual();
    }

    protected override void HandleInteraction(GameObject interactor)
    {
        if (_busy || _remainingUses <= 0) return;

        var inv = interactor.GetComponentInChildren<MineralInventory>()
                  ?? UnityEngine.Object.FindObjectOfType<MineralInventory>();
        if (inv == null) { Debug.LogWarning("[Cauldron] MineralInventory 없음"); return; }

        var ui = cauldronUI ?? UnityEngine.Object.FindObjectOfType<CauldronUI>(true);
        if (ui == null) { Debug.LogWarning("[Cauldron] CauldronUI 없음"); return; }

        ui.Open(inv, OnMineralChosen);
    }

    private void OnMineralChosen(MineralID input)
    {
        if (_busy || _remainingUses <= 0 || input == MineralID.None) return;
        StartCoroutine(BrewRoutine(input));
    }

    private IEnumerator BrewRoutine(MineralID input)
    {
        _busy = true;

        CauldronResult result = _resolver.Resolve(input, new System.Random(Environment.TickCount));

        yield return PlayBrewAnimation(result); // partial(Task 7) — 연출

        SpawnReward(result); // partial(Task 6) — 스폰

        _remainingUses--;
        CauldronStateStore.SetRemainingUses(_coord, _remainingUses);
        if (_remainingUses <= 0) ApplySpentVisual();

        _busy = false;
    }

    private void ApplySpentVisual()
    {
        if (bodyRenderer != null) bodyRenderer.color = spentColor;
        promptText = "불이 꺼진 가마솥"; // 상호작용은 _remainingUses<=0 가드로 차단됨
    }

    private int SettingsMaxUses()
    {
        var s = SpecialChunkSettingsLoader.Instance?.Settings?.cauldron;
        return s != null ? s.maxUses : 3;
    }

    // 좌표 해석: 부모 청크의 그리드 좌표. 기존 특수청크가 쓰는 방식과 동일하게 구현
    // (예: parent에서 TerrainChunk를 찾아 coord 프로퍼티 사용). 구현 시 실제 접근자 확인.
    private Vector2Int ResolveCoord(Transform parent)
    {
        var tc = parent != null ? parent.GetComponentInParent<TerrainChunk>() : GetComponentInParent<TerrainChunk>();
        return tc != null ? tc.GetChunkCoord() : Vector2Int.zero; // GetChunkCoord: 실제 좌표 접근자명 확인 후 대입
    }
}
```

> **구현 시 확인 필요(2건):** (a) `TerrainChunk`의 청크 좌표 접근자 정확한 이름(`GetChunkCoord()` 가정 — 실제 프로퍼티/메서드로 교체). (b) `tileData.json` 로드 접근자 — Step 2 참고.

- [ ] **Step 2: BuildResolver 구현 (tileData 접근)**

`DokkaebiCauldron`에 추가. `SpecialChunkSettingsLoader`와 동일한 로더 패턴으로 `TileDatabaseJson`을 얻는다. 기존에 tileData를 들고 있는 매니저(`TileDataManager` 등)를 재사용:

```csharp
    private CauldronResolver BuildResolver()
    {
        var settings = SpecialChunkSettingsLoader.Instance.Settings.cauldron;

        // tileData.json의 tiles(층, 얕은→깊은 순서)를 얻는다.
        // TileDataManager가 이미 파싱된 TileDatabaseJson을 보유 → 그 tiles 리스트 사용.
        List<TileDataJson> tiles = TileDataManager.Instance.Database.tiles; // 실제 접근자명 확인 후 대입

        var ladder = new MineralUpgradeLadder(tiles);
        return new CauldronResolver(ladder, settings.ToProbabilities(), settings.explosiveMin, settings.explosiveMax);
    }
```

(상단에 `using System.Collections.Generic;` 추가.)

> **구현 시 확인 필요:** `TileDataManager.Instance.Database.tiles` 경로 — 실제 파싱 결과 보유 위치를 grep(`TileDatabaseJson`)으로 확인 후 정확히 대입. 없으면 `SpecialChunkSettingsLoader`처럼 StreamingAssets에서 직접 로드하는 작은 로더를 추가.

- [ ] **Step 3: 빈 partial 스텁 추가 (컴파일 통과용)**

Task 6·7 본문 전 컴파일을 위해 임시 스텁을 같은 파일 하단 또는 별도 partial에 둔다:

```csharp
public partial class DokkaebiCauldron
{
    private void SpawnReward(CauldronResult result) { /* Task 6에서 구현 */ }
    private IEnumerator PlayBrewAnimation(CauldronResult result) { yield break; /* Task 7에서 구현 */ }
}
```

- [ ] **Step 4: 검증 (사람)**

컴파일 통과. 가마솥 프리팹 없이도 빌드 에러 없음.

- [ ] **Step 5: 체크포인트 (UVCS 체크인)**

메시지: `feat(cauldron): DokkaebiCauldron core (interaction + use-count + spent)`

---

## Task 6: 결과 스폰 (DokkaebiCauldron.Spawn.cs)

결과 종류별로 월드에 산출물을 생성한다. 광물 스폰·폭발은 기존 시스템 재사용.

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/DokkaebiCauldron.Spawn.cs`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/DokkaebiCauldron.cs` (Step 3 스텁의 `SpawnReward` 제거)

**Interfaces:**
- Consumes: `CauldronResult`(Task 2), `MineralDatabase.Instance.GetMineralByID`, `MineralSO.mineralPrefab`, `ExplosiveMineralReactor`의 `projectileHazardPrefab`(Inspector 참조로 가마솥에 직접 연결).
- Produces: `partial void`가 아닌 일반 `private void SpawnReward(CauldronResult)` 본문.

- [ ] **Step 1: Inspector 참조 필드 추가**

`DokkaebiCauldron.cs`(본체)에 폭발/재 프리팹 참조 추가:

```csharp
    [Header("Cauldron Reward Prefabs")]
    [SerializeField] private GameObject explosiveHazardPrefab; // ExplosiveMineralReactor가 쓰는 것과 동일
    [SerializeField] private MineralSO ashMineral;             // 실패 보상 "재" (Task 8에서 에셋 결정)
    [SerializeField] private float spawnScatter = 0.4f;
    [SerializeField] private int explosiveSpawnArc = 60;       // 분산 각도
```

- [ ] **Step 2: 스텁 제거**

Task 5 Step 3 스텁의 `private void SpawnReward(...) { }` 한 줄을 삭제(중복 정의 방지).

- [ ] **Step 3: 스폰 본문 작성**

`DokkaebiCauldron.Spawn.cs`:

```csharp
// @tags: cauldron, spawn, reward, mineral, explosion
using UnityEngine;

public partial class DokkaebiCauldron
{
    private void SpawnReward(CauldronResult result)
    {
        Vector3 origin = (spawnPoint != null ? spawnPoint : transform).position;

        switch (result.rewardType)
        {
            case CauldronRewardType.Mineral:
            case CauldronRewardType.EasterEgg: // 현재는 최상위 광물 그대로 지급 (유니크 보상은 추후)
                SpawnMineral(result.resultMineral, origin);
                break;

            case CauldronRewardType.Ash:
                if (ashMineral != null) SpawnMineralSO(ashMineral, origin);
                else Debug.LogWarning("[Cauldron] ashMineral 미할당 — 재 스폰 생략");
                break;

            case CauldronRewardType.Explosion:
                SpawnExplosives(result.explosiveCount, origin);
                break;
        }
    }

    private void SpawnMineral(MineralID id, Vector3 origin)
    {
        var so = MineralDatabase.Instance?.GetMineralByID(id);
        if (so == null) { Debug.LogWarning($"[Cauldron] MineralSO 없음: {id}"); return; }
        SpawnMineralSO(so, origin);
    }

    private void SpawnMineralSO(MineralSO so, Vector3 origin)
    {
        if (so.mineralPrefab == null) { Debug.LogWarning($"[Cauldron] mineralPrefab 없음: {so.name}"); return; }
        Vector3 pos = origin + (Vector3)(Random.insideUnitCircle * spawnScatter) + Vector3.up * 0.3f;
        var obj = Instantiate(so.mineralPrefab, pos, Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
        // 살짝 튀어오르는 느낌: Rigidbody2D 있으면 위로 임펄스
        var rb = obj.GetComponent<Rigidbody2D>();
        if (rb != null) rb.AddForce(new Vector2(Random.Range(-1.5f, 1.5f), Random.Range(3f, 5f)), ForceMode2D.Impulse);
    }

    private void SpawnExplosives(int count, Vector3 origin)
    {
        if (explosiveHazardPrefab == null) { Debug.LogWarning("[Cauldron] explosiveHazardPrefab 미할당"); return; }
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = origin + (Vector3)(Random.insideUnitCircle * spawnScatter) + Vector3.up * 0.3f;
            var obj = Instantiate(explosiveHazardPrefab, pos, Quaternion.identity);
            var rb = obj.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                float ang = (90f + Random.Range(-explosiveSpawnArc, explosiveSpawnArc)) * Mathf.Deg2Rad;
                rb.AddForce(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(4f, 7f), ForceMode2D.Impulse);
            }
        }
    }
}
```

- [ ] **Step 4: 검증 (사람, PlayMode)**

가마솥 프리팹에 참조 연결 후 각 결과를 임시 강제(디버그 키)로 스폰 확인:
- 광물/재 1개 튀어나옴, 폭발 N개 산란.

- [ ] **Step 5: 체크포인트 (UVCS 체크인)**

메시지: `feat(cauldron): reward spawning (mineral/ash/explosion)`

---

## Task 7: 연출 (부글부글 / 붉은 글로우)

성공계열은 부글부글, 대실패는 붉은 글로우 후 스폰. 코루틴으로 시간차를 준다.

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/DokkaebiCauldron.Anim.cs`
- Modify: `DokkaebiCauldron.cs` (Step 3 스텁의 `PlayBrewAnimation` 제거)

**Interfaces:**
- Consumes: `CauldronResult.outcome`, `bodyRenderer`(본체 필드), 선택적 `ParticleSystem`/`AudioSource`.
- Produces: `private IEnumerator PlayBrewAnimation(CauldronResult result)`.

- [ ] **Step 1: 스텁 제거**

Task 5 Step 3 스텁의 `private IEnumerator PlayBrewAnimation(...)` 제거.

- [ ] **Step 2: 연출 코루틴 작성**

`DokkaebiCauldron.Anim.cs`:

```csharp
// @tags: cauldron, animation, vfx, feedback
using System.Collections;
using UnityEngine;

public partial class DokkaebiCauldron
{
    [Header("Cauldron Anim")]
    [SerializeField] private float brewDuration = 1.5f;
    [SerializeField] private Color brewColor = new Color(0.6f, 1f, 0.6f);   // 부글부글 끓는 빛
    [SerializeField] private Color greatFailColor = new Color(1f, 0.3f, 0.2f); // 붉게 달아오름
    [SerializeField] private ParticleSystem bubbleFx; // 선택 — 없으면 색만

    private IEnumerator PlayBrewAnimation(CauldronResult result)
    {
        bool greatFail = result.outcome == CauldronOutcome.GreatFail;
        Color target = greatFail ? greatFailColor : brewColor;
        Color start  = bodyRenderer != null ? bodyRenderer.color : Color.white;

        if (bubbleFx != null) bubbleFx.Play();

        float t = 0f;
        while (t < brewDuration)
        {
            t += Time.deltaTime;
            // 부글부글: 색을 펄스. 대실패: 점점 붉게 고조.
            float k = greatFail ? (t / brewDuration) : Mathf.PingPong(t * 4f, 1f);
            if (bodyRenderer != null) bodyRenderer.color = Color.Lerp(start, target, k);
            yield return null;
        }

        if (bubbleFx != null) bubbleFx.Stop();
        // 소진 비주얼은 BrewRoutine이 횟수 차감 후 적용하므로 여기선 원복만(소진 아닐 때).
        if (bodyRenderer != null && _remainingUses > 1) bodyRenderer.color = start;
    }
}
```

- [ ] **Step 3: 검증 (사람, PlayMode)**

성공계열 부글부글, 대실패 붉은 고조 후 스폰 타이밍 확인.

- [ ] **Step 4: 체크포인트 (UVCS 체크인)**

메시지: `feat(cauldron): brew/great-fail animation`

---

## Task 8: 투입 UI (CauldronUI)

인벤토리 광물 목록에서 1개 선택 → 투입 → 콜백. 기존 인벤토리 UI 패턴을 따른다.

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronUI.cs`

**Interfaces:**
- Consumes: `MineralInventory`(`ReadonlyItems: IReadOnlyList<InventorySlot>`, `RemoveItem(MineralSO, int)`), `InventorySlot`(`item: InterfaceInventoryItem`, `quantity: int`), `MineralSO`.
- Produces: `void Open(MineralInventory inv, System.Action<MineralID> onChosen)`; 내부에서 선택·투입 시 `inv.RemoveItem(so, 1)` 후 `onChosen(so.mineralID)` 호출하고 UI를 닫는다.

- [ ] **Step 1: UI 컨트롤러 작성**

`CauldronUI.cs`:

```csharp
// @tags: cauldron, ui, inventory, mineral, selection
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 가마솥 투입 UI. 인벤토리 광물을 버튼 그리드로 표시하고, 1개 선택·투입 시 콜백 후 닫는다.
/// 슬롯 버튼 프리팹은 아이콘 Image + 수량 Text + Button을 갖는다.
/// </summary>
public class CauldronUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;     // 전체 패널 (열기/닫기 토글)
    [SerializeField] private Transform slotContainer;  // 버튼이 채워질 부모
    [SerializeField] private CauldronSlotButton slotButtonPrefab; // 아이콘/수량/버튼 컴포넌트
    [SerializeField] private Button closeButton;

    private MineralInventory _inv;
    private Action<MineralID> _onChosen;
    private readonly List<CauldronSlotButton> _spawned = new List<CauldronSlotButton>();

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open(MineralInventory inv, Action<MineralID> onChosen)
    {
        _inv = inv;
        _onChosen = onChosen;
        if (panelRoot != null) panelRoot.SetActive(true);
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var b in _spawned) if (b != null) Destroy(b.gameObject);
        _spawned.Clear();

        if (_inv == null) return;
        foreach (var slot in _inv.ReadonlyItems)
        {
            if (slot?.item is not MineralSO so) continue;
            var btn = Instantiate(slotButtonPrefab, slotContainer);
            btn.Bind(so, slot.quantity, () => Choose(so));
            _spawned.Add(btn);
        }
    }

    private void Choose(MineralSO so)
    {
        if (_inv == null || so == null) return;
        if (!_inv.RemoveItem(so, 1)) return; // 보유 없으면 무시
        var cb = _onChosen;
        Close();
        cb?.Invoke(so.mineralID);
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        _inv = null; _onChosen = null;
    }
}
```

- [ ] **Step 2: 슬롯 버튼 컴포넌트 작성**

`Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Cauldron/CauldronSlotButton.cs`:

```csharp
// @tags: cauldron, ui, slot, button
using System;
using UnityEngine;
using UnityEngine.UI;

public class CauldronSlotButton : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Text quantityText; // TMP 사용 시 TMP_Text로 교체
    [SerializeField] private Button button;

    public void Bind(MineralSO mineral, int quantity, Action onClick)
    {
        if (icon != null) icon.sprite = mineral.Icon;
        if (quantityText != null) quantityText.text = quantity.ToString();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
```

> 프로젝트가 TextMeshPro를 쓰면 `Text`/`using UnityEngine.UI`를 `TMP_Text`/`using TMPro`로 교체. 구현 시 기존 인벤토리 UI(`InventoryUI`)의 텍스트 컴포넌트 종류를 확인해 일치시킬 것.

- [ ] **Step 3: 검증 (사람, PlayMode)**

가마솥 E키 → UI 열림 → 광물 목록 표시 → 클릭 시 1개 차감 + UI 닫힘 + 가마솥 BrewRoutine 진입 확인.

- [ ] **Step 4: 체크포인트 (UVCS 체크인)**

메시지: `feat(cauldron): mineral insertion UI`

---

## Task 9: 에셋·프리팹·스폰 등록 (사람 주도)

코드를 실제 게임에 연결한다. 대부분 Unity 에디터 작업이므로 사람이 수행하고, 체크리스트로 검증한다.

**Files:**
- Create: 가마솥 특수청크 프리팹, CauldronUI 프리팹, "재(Ash)" 광물 에셋(결정 시)
- Modify: `Assets/StreamingAssets/specialChunkSettings.json`(스폰 확률), 특수청크 풀 등록(`SpecialChunkManager` 인스펙터)

- [ ] **Step 1: "재(Ash)" 광물 결정 (CLAUDE.md 설계 보류항목 §6-3)**

택1: (a) `MineralID.Ash` enum 추가 + MineralSO/프리팹/가격(0) 신규, (b) 임시로 기존 잡템(ScrapMetal) 재사용. 결정 후 `DokkaebiCauldron.ashMineral`에 할당. **enum 추가 시** `Enums.cs`의 `MineralID`에 항목 추가하고 MineralDatabase에 등록.

- [ ] **Step 2: 가마솥 프리팹 제작 (CLAUDE.md §8 — 프리팹 직접 편집)**

Project 창에서 특수청크 프리팹을 더블클릭(Prefab Edit 모드)해 자식 배치. `DokkaebiCauldron` + `BoxCollider2D`(파괴불가/상호작용) 부착, `bodyRenderer`/`spawnPoint`/`explosiveHazardPrefab`/`ashMineral`/`cauldronUI` 참조 연결. 청크 1칸=10유닛, 자식 local position 유효범위 X(0~10)/Y(0~10) 준수.

- [ ] **Step 3: CauldronUI 프리팹 + 씬 배치**

UI 캔버스에 CauldronUI 패널 구성(`panelRoot`/`slotContainer`/`slotButtonPrefab`/`closeButton`), `CauldronSlotButton` 프리팹(아이콘·수량·버튼) 제작.

- [ ] **Step 4: 특수청크 풀 + 스폰 등록**

`SpecialChunkManager` 인스펙터의 해당 레이어 풀에 가마솥 프리팹을 `SpecialChunkDef`로 추가(`spawnChance`, `minDepth`/`maxDepth` 설정). `specialChunkSettings.json`의 `DokkaebiCauldron` chance를 테스트값으로 올림.

- [ ] **Step 5: 통합 검증 (사람, PlayMode 체크리스트)**

- [ ] 가마솥이 지형에 스폰되고 파괴 불가
- [ ] E키 → UI → 광물 투입 → 부글부글 → 결과 산출
- [ ] 3회 사용 후 비활성(소진 색), 상호작용 차단
- [ ] 세이브→로드 후 남은횟수 유지, 소진 가마솥은 소진 상태로 복원
- [ ] 대실패 시 폭발 광물 산란 + 붉은 글로우
- [ ] 최상위 광물(별조각) 투입 성공 시 클램프 동작(현재는 별조각 그대로)

- [ ] **Step 6: 체크포인트 (UVCS 체크인)**

메시지: `feat(cauldron): prefab + UI + spawn registration`

---

## 자체 검토 메모

- **스펙 커버리지**: 배치(특수청크 1회성)=Task5/9, 1개투입·3회=Task5+4, 사다리=Task1, 확률결과=Task2, 결과스폰(광물/재/폭발)=Task6, 연출=Task7, UI=Task8, 저장=Task4, 보류항목(유물/이스터에그 hook=EasterEgg rewardType, 재 에셋=Task9-1)=커버됨.
- **구현 중 확인 필요(코드 주석에 명시)**: ① `TerrainChunk` 청크좌표 접근자명 ② `tileData.json` 파싱 결과 보유 위치(`TileDataManager`) ③ UI 텍스트 컴포넌트 종류(Text vs TMP). 세 가지는 구현 직전 grep 1회로 확정 가능하며, 플랜의 알고리즘·구조에는 영향 없음.
- **타입 일관성**: `MineralUpgradeLadder.Resolve(input, steps, rng, out clamped)` 시그니처가 Task1 정의와 Task2 호출에서 동일. `CauldronResult` 필드명이 Task2 정의와 Task5/6 사용에서 동일.
