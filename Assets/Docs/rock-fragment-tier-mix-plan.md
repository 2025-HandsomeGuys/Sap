# 돌 조각 크기 티어 믹스 구현 계획

> **작업자 안내:** 태스크 단위로 순서대로 진행한다. 체크박스(`- [ ]`)로 진행 상황을 추적한다.
> 스펙: [rock-fragment-tier-mix.md](rock-fragment-tier-mix.md)

**목표:** 돌이 깨질 때 나오는 조각을 크기 티어(대/중/소)별 풀에서 가중치로 뽑아 섞고, 조각 크기 대신 개수로 돌 크기를 표현한다.

**아키텍처:** `RockSpriteSet`에 티어 필드를 추가하고, 지형(`TileVisualData`) 단위로 티어별 조각 스프라이트 버킷을 static 캐시(`RockFragmentPool`)에 빌드한다. `RockBreakVFX`는 스프라이트 배열 대신 `(TileType, RockSizeTier)`만 들고 있다가 파괴 시 풀에서 개수만큼 뽑아 스폰한다.

**Tech Stack:** Unity 2D, C#, ScriptableObject(`TileVisualSettings`), Unity Test Runner (EditMode)

## Global Constraints

- 버전 관리는 **UVCS**다. `git` 명령어를 사용하지 않는다. 커밋은 사람이 UVCS로 수행한다.
- Unity Test Runner 실행은 **사람이 직접** 한다. 테스트 파일 작성까지만 하고, 테스트 통과를 게이트로 삼지 말고 다음 태스크로 진행한다.
- 조각의 발사 속도·중력·회전 감쇠·수명·페이드·산란·Ghost 레이어 전환 로직은 **변경 금지**. 바뀌는 것은 "어떤 스프라이트를 몇 개 뽑느냐"뿐이다.
- 티어 기본값: Large `totalCount=8, w(L/M/S)=0.6/0.3/0.1` · Medium `6, 0/0.7/0.3` · Small `4, 0/0/1.0`
- 개수 보정 기본값: `countScaleMin=0.7`, `countScaleMax=1.3`, `SizeScale` 범위 `0.5~1.5`
- `RockFragmentSet.cs`는 변경하지 않는다 (그룹핑 컨테이너로 유지).

---

## 파일 구조

| 파일 | 책임 | 상태 |
|---|---|---|
| `Assets/Scripts/_Core/Data/RockSizeTier.cs` | 티어 enum 단독 정의 | 생성 |
| `Assets/Scripts/_Core/Data/TileVisualSettings.cs` | `RockSpriteSet.tier`, `FragmentMixRule`, `TileVisualData.fragmentMixRules` | 수정 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockFragmentPool.cs` | 티어 버킷 빌드·캐시·가중치 룰렛·개수 계산 | 생성 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockBreakVFX.cs` | 풀에서 뽑아 조각 스폰. 조각 스케일 제거 | 수정 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/TerrainDecorator.cs` | `RockData.fragmentSets` 제거, `tier` 추가 | 수정 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockLayoutCalculator.cs` | `RockData`에 `tier` 복사 | 수정 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockSpawner.cs` | `vfx.Setup(tileType, rock.tier)` 주입 | 수정 |
| `Assets/Tests/EditMode/RockFragmentPoolTests.cs` | 룰렛·개수 계산·폴백 검증 | 생성 |

**의존 순서:** Task 1 → 2 → 3 → 4 → 5. Task 1·2가 데이터 계약을 확정하므로 반드시 먼저 끝낸다.

---

### Task 1: 티어 enum과 믹스 규칙 데이터

**Files:**
- Create: `Assets/Scripts/_Core/Data/RockSizeTier.cs`
- Modify: `Assets/Scripts/_Core/Data/TileVisualSettings.cs:12-35`

**Interfaces:**
- Produces: `RockSizeTier { Small, Medium, Large }` · `TileVisualSettings.RockSpriteSet.tier` · `TileVisualSettings.FragmentMixRule` (필드 `tier, totalCount, weightLarge, weightMedium, weightSmall, countScaleMin, countScaleMax`) · `TileVisualData.fragmentMixRules`

- [ ] **Step 1: `RockSizeTier.cs` 생성**

`Assets/Scripts/_Core/Data/RockSizeTier.cs`:

```csharp
// @tags: rock, tier, fragment, decoration, data-container
/// <summary>
/// 돌 종류의 크기 티어. 조각 VFX 풀 분류에 사용된다.
/// 순서를 바꾸면 기존 TileVisualSettings 에셋의 직렬화 값이 어긋난다.
/// </summary>
public enum RockSizeTier
{
    Small  = 0,
    Medium = 1,
    Large  = 2,
}
```

- [ ] **Step 2: `RockSpriteSet`에 `tier` 필드 추가**

`TileVisualSettings.cs`의 `RockSpriteSet` 구조체를 아래로 교체한다:

```csharp
    [System.Serializable]
    public struct RockSpriteSet
    {
        public Sprite normal;   // HP 67%~100%
        public Sprite crack1;   // HP 34%~66%
        public Sprite crack2;   // HP 0%~33%

        [Tooltip("이 돌의 크기 티어. 조각 VFX가 티어별 풀을 섞는 기준이 된다.")]
        public RockSizeTier tier;

        [Tooltip("이 돌 고유의 조각 스프라이트. 런타임에 평탄화되어 tier 버킷에 합쳐진다.")]
        public RockFragmentSet[] fragmentSets;
    }
```

- [ ] **Step 3: `FragmentMixRule` 구조체 추가**

`TileVisualSettings.cs`의 `RockSpriteSet` 바로 아래에 삽입한다:

```csharp
    /// <summary>
    /// 특정 티어의 돌이 깨졌을 때 조각을 어떻게 뽑을지 정하는 규칙.
    /// weight 합은 1일 필요 없다. 룰렛 선택 시 총합으로 정규화된다.
    /// </summary>
    [System.Serializable]
    public struct FragmentMixRule
    {
        [Tooltip("깨진 돌의 티어")]
        public RockSizeTier tier;

        [Tooltip("뽑을 조각 기준 개수 (SizeScale 1.0 기준)")]
        public int totalCount;

        [Tooltip("대 티어 조각 풀에서 뽑을 가중치")]
        public float weightLarge;
        [Tooltip("중 티어 조각 풀에서 뽑을 가중치")]
        public float weightMedium;
        [Tooltip("소 티어 조각 풀에서 뽑을 가중치")]
        public float weightSmall;

        [Tooltip("SizeScale 0.5일 때 개수 배율")]
        public float countScaleMin;
        [Tooltip("SizeScale 1.5일 때 개수 배율")]
        public float countScaleMax;
    }
```

- [ ] **Step 4: `TileVisualData`에 규칙 배열 추가**

`TileVisualData`의 `rockSpacingBuffer` 선언 위에 삽입한다:

```csharp
        [Tooltip("티어별 조각 믹스 규칙. 비워두면 RockFragmentPool의 코드 기본값이 사용된다.")]
        public FragmentMixRule[] fragmentMixRules;
```

- [ ] **Step 5: 컴파일 확인**

Unity 에디터로 돌아가 컴파일 에러가 없는지 확인한다.
`RockSpriteSet`은 struct라 필드 추가만으로는 기존 에셋이 깨지지 않는다. `tier`는 전부 `Small`(0)로 들어간다 — 정상이며 Task 5의 마이그레이션에서 지정한다.

---

### Task 2: `RockFragmentPool` — 티어 버킷·룰렛·개수 계산

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockFragmentPool.cs`
- Test: `Assets/Tests/EditMode/RockFragmentPoolTests.cs`

**Interfaces:**
- Consumes: `RockSizeTier`, `TileVisualSettings.FragmentMixRule`, `TileVisualSettings.TileVisualData` (Task 1)
- Produces:
  - `RockFragmentPool.GetRule(TileType, RockSizeTier) → FragmentMixRule`
  - `RockFragmentPool.PickSprite(TileType, RockSizeTier) → Sprite` (뽑을 수 없으면 `null`)
  - `RockFragmentPool.ResolveCount(in FragmentMixRule, float sizeScale) → int` (static 순수 함수)
  - `RockFragmentPool.PickTier(in FragmentMixRule, float roll01) → RockSizeTier` (static 순수 함수, `roll01`은 0~1)
  - `RockFragmentPool.DefaultRule(RockSizeTier) → FragmentMixRule` (static 순수 함수)
  - `RockFragmentPool.ClearCache()`

- [ ] **Step 1: 테스트 파일 작성**

`Assets/Tests/EditMode/EditModeTests.asmdef`가 이미 `GameScripts`와 `nunit.framework.dll`을 참조하고 있다. asmdef 수정은 필요 없다.

`Assets/Tests/EditMode/RockFragmentPoolTests.cs`:

```csharp
// @tags: rock, fragment, tier, test, editmode
using NUnit.Framework;
using UnityEngine;

public class RockFragmentPoolTests
{
    // --- DefaultRule ---

    [Test]
    public void DefaultRule_Large_Has8CountAndMixedWeights()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large);
        Assert.AreEqual(8, rule.totalCount);
        Assert.AreEqual(0.6f, rule.weightLarge,  0.001f);
        Assert.AreEqual(0.3f, rule.weightMedium, 0.001f);
        Assert.AreEqual(0.1f, rule.weightSmall,  0.001f);
    }

    [Test]
    public void DefaultRule_Small_OnlyDrawsSmall()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Small);
        Assert.AreEqual(4, rule.totalCount);
        Assert.AreEqual(0f,  rule.weightLarge,  0.001f);
        Assert.AreEqual(0f,  rule.weightMedium, 0.001f);
        Assert.AreEqual(1f,  rule.weightSmall,  0.001f);
    }

    // --- ResolveCount ---

    [Test]
    public void ResolveCount_LargeRock_ScalesWithSize()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large); // totalCount 8, 0.7~1.3
        Assert.AreEqual(6,  RockFragmentPool.ResolveCount(rule, 0.5f));
        Assert.AreEqual(8,  RockFragmentPool.ResolveCount(rule, 1.0f));
        Assert.AreEqual(10, RockFragmentPool.ResolveCount(rule, 1.5f));
    }

    [Test]
    public void ResolveCount_SmallRock_ScalesWithSize()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Small); // totalCount 4
        Assert.AreEqual(3, RockFragmentPool.ResolveCount(rule, 0.5f));
        Assert.AreEqual(4, RockFragmentPool.ResolveCount(rule, 1.0f));
        Assert.AreEqual(5, RockFragmentPool.ResolveCount(rule, 1.5f));
    }

    [Test]
    public void ResolveCount_ClampsScaleOutsideExpectedRange()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large);
        Assert.AreEqual(6,  RockFragmentPool.ResolveCount(rule, 0.1f), "0.5 미만은 0.5로 클램프");
        Assert.AreEqual(10, RockFragmentPool.ResolveCount(rule, 9f),   "1.5 초과는 1.5로 클램프");
    }

    [Test]
    public void ResolveCount_NeverReturnsZero()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Small);
        rule.totalCount = 0;
        Assert.AreEqual(1, RockFragmentPool.ResolveCount(rule, 0.5f));
    }

    // --- PickTier ---

    [Test]
    public void PickTier_LargeRule_MapsRollToTierBands()
    {
        // Large 규칙 가중치: L 0.6 / M 0.3 / S 0.1 → 누적 경계 0.6, 0.9, 1.0
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large);
        Assert.AreEqual(RockSizeTier.Large,  RockFragmentPool.PickTier(rule, 0.0f));
        Assert.AreEqual(RockSizeTier.Large,  RockFragmentPool.PickTier(rule, 0.59f));
        Assert.AreEqual(RockSizeTier.Medium, RockFragmentPool.PickTier(rule, 0.61f));
        Assert.AreEqual(RockSizeTier.Medium, RockFragmentPool.PickTier(rule, 0.89f));
        Assert.AreEqual(RockSizeTier.Small,  RockFragmentPool.PickTier(rule, 0.95f));
    }

    [Test]
    public void PickTier_SmallRule_AlwaysSmall()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Small);
        Assert.AreEqual(RockSizeTier.Small, RockFragmentPool.PickTier(rule, 0.0f));
        Assert.AreEqual(RockSizeTier.Small, RockFragmentPool.PickTier(rule, 0.5f));
        Assert.AreEqual(RockSizeTier.Small, RockFragmentPool.PickTier(rule, 0.999f));
    }

    [Test]
    public void PickTier_NonNormalizedWeights_AreNormalized()
    {
        // 가중치 합이 10 → 6/3/1 비율. 경계는 0.6, 0.9
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Large);
        rule.weightLarge  = 6f;
        rule.weightMedium = 3f;
        rule.weightSmall  = 1f;
        Assert.AreEqual(RockSizeTier.Large,  RockFragmentPool.PickTier(rule, 0.5f));
        Assert.AreEqual(RockSizeTier.Medium, RockFragmentPool.PickTier(rule, 0.7f));
        Assert.AreEqual(RockSizeTier.Small,  RockFragmentPool.PickTier(rule, 0.95f));
    }

    [Test]
    public void PickTier_AllWeightsZero_FallsBackToBrokenTier()
    {
        var rule = RockFragmentPool.DefaultRule(RockSizeTier.Medium);
        rule.weightLarge = rule.weightMedium = rule.weightSmall = 0f;
        Assert.AreEqual(RockSizeTier.Medium, RockFragmentPool.PickTier(rule, 0.5f));
    }
}
```

- [ ] **Step 2: `RockFragmentPool.cs` 구현**

`Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockFragmentPool.cs`:

```csharp
// @tags: rock, fragment, tier, vfx, pool, decoration
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지형(TileType)별로 돌 조각 스프라이트를 크기 티어 버킷 3개로 묶어 캐시한다.
/// 큰 돌이 깨질 때 하위 티어 조각을 섞어 뽑기 위한 조회 계층.
///
/// 버킷은 TileType당 최초 1회만 빌드된다. 파괴할 때마다 rockSpriteSets를 순회하지 않는다.
/// </summary>
public static class RockFragmentPool
{
    private const int TierCount = 3;

    private class TerrainPool
    {
        public Sprite[][] buckets;                          // [tier] → 조각 스프라이트
        public Dictionary<RockSizeTier, TileVisualSettings.FragmentMixRule> rules;
    }

    private static readonly Dictionary<TileType, TerrainPool> _pools = new();
    private static readonly HashSet<TileType> _warnedEmpty = new();

    private static TileVisualSettings _settings;

    /// <summary>씬 전환·에셋 리로드 시 캐시를 비운다.</summary>
    public static void ClearCache()
    {
        _pools.Clear();
        _warnedEmpty.Clear();
    }

    /// <summary>조각 풀이 참조할 TileVisualSettings를 주입한다. 주입 시 캐시가 초기화된다.</summary>
    public static void SetSettings(TileVisualSettings settings)
    {
        _settings = settings;
        ClearCache();
    }

    // ------------------------------------------------------------------
    // 순수 함수 — 테스트 대상
    // ------------------------------------------------------------------

    /// <summary>티어별 코드 기본 규칙. TileVisualData.fragmentMixRules가 비었을 때 사용된다.</summary>
    public static TileVisualSettings.FragmentMixRule DefaultRule(RockSizeTier tier)
    {
        var rule = new TileVisualSettings.FragmentMixRule
        {
            tier          = tier,
            countScaleMin = 0.7f,
            countScaleMax = 1.3f,
        };

        switch (tier)
        {
            case RockSizeTier.Large:
                rule.totalCount   = 8;
                rule.weightLarge  = 0.6f;
                rule.weightMedium = 0.3f;
                rule.weightSmall  = 0.1f;
                break;

            case RockSizeTier.Medium:
                rule.totalCount   = 6;
                rule.weightLarge  = 0f;
                rule.weightMedium = 0.7f;
                rule.weightSmall  = 0.3f;
                break;

            default: // Small
                rule.totalCount   = 4;
                rule.weightLarge  = 0f;
                rule.weightMedium = 0f;
                rule.weightSmall  = 1f;
                break;
        }

        return rule;
    }

    /// <summary>
    /// 돌의 SizeScale(0.5~1.5)에 따라 뽑을 조각 개수를 정한다.
    /// 조각 스프라이트 자체는 확대·축소하지 않으므로 돌 크기는 개수로만 표현된다.
    /// </summary>
    public static int ResolveCount(in TileVisualSettings.FragmentMixRule rule, float sizeScale)
    {
        float t     = Mathf.InverseLerp(0.5f, 1.5f, Mathf.Clamp(sizeScale, 0.5f, 1.5f));
        float mul   = Mathf.Lerp(rule.countScaleMin, rule.countScaleMax, t);
        int   count = Mathf.RoundToInt(rule.totalCount * mul);
        return Mathf.Max(1, count);
    }

    /// <summary>
    /// 가중치 룰렛으로 뽑을 티어를 정한다. roll01은 0~1 난수.
    /// 가중치 합이 0이면 깨진 돌 자신의 티어를 반환한다.
    /// </summary>
    public static RockSizeTier PickTier(in TileVisualSettings.FragmentMixRule rule, float roll01)
    {
        float wL = Mathf.Max(0f, rule.weightLarge);
        float wM = Mathf.Max(0f, rule.weightMedium);
        float wS = Mathf.Max(0f, rule.weightSmall);
        float total = wL + wM + wS;

        if (total <= 0f) return rule.tier;

        float r = Mathf.Clamp01(roll01) * total;
        if (r < wL)      return RockSizeTier.Large;
        if (r < wL + wM) return RockSizeTier.Medium;
        return RockSizeTier.Small;
    }

    // ------------------------------------------------------------------
    // 런타임 조회
    // ------------------------------------------------------------------

    public static TileVisualSettings.FragmentMixRule GetRule(TileType tileType, RockSizeTier brokenTier)
    {
        TerrainPool pool = GetOrBuild(tileType);
        if (pool != null && pool.rules.TryGetValue(brokenTier, out var rule))
            return rule;

        return DefaultRule(brokenTier);
    }

    /// <summary>
    /// 규칙의 가중치로 티어를 뽑고 그 버킷에서 스프라이트 1개를 반환한다(중복 허용).
    /// 폴백 순서: 뽑힌 티어 → 깨진 돌 자신의 티어 → 비어있지 않은 아무 티어 → null.
    /// </summary>
    public static Sprite PickSprite(TileType tileType, RockSizeTier brokenTier)
    {
        TerrainPool pool = GetOrBuild(tileType);
        if (pool == null) return null;

        var rule = GetRule(tileType, brokenTier);
        RockSizeTier picked = PickTier(rule, Random.value);

        Sprite spr = TakeFrom(pool, picked);
        if (spr != null) return spr;

        spr = TakeFrom(pool, brokenTier);
        if (spr != null) return spr;

        for (int i = 0; i < TierCount; i++)
        {
            spr = TakeFrom(pool, (RockSizeTier)i);
            if (spr != null) return spr;
        }

        if (_warnedEmpty.Add(tileType))
            Debug.LogWarning($"[RockFragmentPool] TileType '{tileType}'에 조각 스프라이트가 하나도 없습니다. TileVisualSettings의 fragmentSets를 확인하세요.");

        return null;
    }

    private static Sprite TakeFrom(TerrainPool pool, RockSizeTier tier)
    {
        Sprite[] bucket = pool.buckets[(int)tier];
        if (bucket == null || bucket.Length == 0) return null;
        return bucket[Random.Range(0, bucket.Length)];
    }

    // ------------------------------------------------------------------
    // 빌드
    // ------------------------------------------------------------------

    private static TerrainPool GetOrBuild(TileType tileType)
    {
        if (_pools.TryGetValue(tileType, out TerrainPool cached)) return cached;

        if (_settings == null)
        {
            Debug.LogWarning("[RockFragmentPool] TileVisualSettings가 주입되지 않았습니다. RockFragmentPool.SetSettings()를 먼저 호출하세요.");
            return null;
        }

        TileVisualSettings.TileVisualData data = _settings.GetDataForType(tileType);

        var lists = new List<Sprite>[TierCount];
        for (int i = 0; i < TierCount; i++) lists[i] = new List<Sprite>();

        if (data.rockSpriteSets != null)
        {
            foreach (var set in data.rockSpriteSets)
            {
                if (set.fragmentSets == null) continue;

                // fragmentSets는 퍼즐 묶음이 아니라 단순 그룹핑 → 전부 평탄화해서 티어 버킷에 합친다.
                foreach (var fs in set.fragmentSets)
                {
                    if (fs?.sprites == null) continue;
                    foreach (Sprite spr in fs.sprites)
                        if (spr != null) lists[(int)set.tier].Add(spr);
                }
            }
        }

        var pool = new TerrainPool
        {
            buckets = new Sprite[TierCount][],
            rules   = new Dictionary<RockSizeTier, TileVisualSettings.FragmentMixRule>(),
        };

        for (int i = 0; i < TierCount; i++)
            pool.buckets[i] = lists[i].ToArray();

        if (data.fragmentMixRules != null)
        {
            foreach (var rule in data.fragmentMixRules)
                pool.rules[rule.tier] = rule;
        }

        _pools[tileType] = pool;
        return pool;
    }
}
```

- [ ] **Step 3: 컴파일 확인**

Unity 에디터에서 컴파일 에러가 없는지 확인한다. 테스트 실행은 사람이 수행하므로 여기서 기다리지 않는다.

---

### Task 3: `RockBreakVFX` 개편 — 풀에서 뽑고 조각 스케일 제거

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockBreakVFX.cs` (전체 교체)

**Interfaces:**
- Consumes: `RockFragmentPool.GetRule`, `RockFragmentPool.ResolveCount`, `RockFragmentPool.PickSprite` (Task 2)
- Produces: `RockBreakVFX.Setup(TileType tileType, RockSizeTier tier)` — Task 4의 `RockSpawner`가 호출한다. 기존 `SetFragmentSets`·`fragmentSets` 필드는 제거된다.

- [ ] **Step 1: `RockBreakVFX.cs` 전체를 아래로 교체**

```csharp
// @tags: rock, vfx, break, decoration
using UnityEngine;

public class RockBreakVFX : MonoBehaviour
{
    // 조각 스프라이트는 RockFragmentPool이 지형 단위로 보유한다.
    // 이 컴포넌트는 풀 조회 키만 들고 있어 청크 풀 재사용 시 stale 배열 참조가 생기지 않는다.
    private TileType     _tileType = TileType.HardStone;
    private RockSizeTier _tier     = RockSizeTier.Small;

    public void Setup(TileType tileType, RockSizeTier tier)
    {
        _tileType = tileType;
        _tier     = tier;
    }

    [Header("Fragment 물리")]
    [Range(0f, 8f)] public float launchSpeedMin = 2f;
    [Range(0f, 8f)] public float launchSpeedMax = 5f;
    [Tooltip("회전 감쇠. 클수록 빨리 멈춤")]
    [Range(0f, 20f)] public float angularDamping = 6f;

    [Header("Spawn 산란 (광물 방식)")]
    [Tooltip("spawn 위치 X 산란 범위")]
    [Range(0f, 1f)] public float scatterX = 0.1f;
    [Tooltip("spawn 위치 Y 산란 범위")]
    [Range(0f, 1f)] public float scatterY = 0.05f;

    private const float FragmentLifetime = 3f;
    private const float FadeDuration     = 0.4f;

    /// <summary>
    /// scale은 돌의 SizeScale(0.5~1.5). 조각 스프라이트를 확대하지 않고
    /// 뽑을 개수를 정하는 데만 쓴다 — 돌 크기는 개수로만 표현된다.
    /// </summary>
    public void Play(Vector3 position, float scale = 1f)
    {
        var rule  = RockFragmentPool.GetRule(_tileType, _tier);
        int count = RockFragmentPool.ResolveCount(rule, scale);

        int finalLayer = LayerMask.NameToLayer("RockFragment");
        if (finalLayer < 0) finalLayer = gameObject.layer;

        var sr = GetComponent<SpriteRenderer>();
        int sortingLayerID = sr != null ? sr.sortingLayerID : 0;
        int sortingOrder   = sr != null ? sr.sortingOrder   : 0;

        float speed = Random.Range(
            Mathf.Min(launchSpeedMin, launchSpeedMax),
            Mathf.Max(launchSpeedMin, launchSpeedMax));

        for (int i = 0; i < count; i++)
        {
            Sprite spr = RockFragmentPool.PickSprite(_tileType, _tier);
            if (spr == null) return; // 풀이 비어있음 — 풀이 경고를 1회 남긴다

            // 위쪽 편향 랜덤 방향 (광물 방식)
            var velocity = new Vector2(
                Random.Range(-1f, 1f),
                Random.Range(0.3f, 1f)
            ).normalized * speed;

            // 시각 중심에서 산란 (광물 방식)
            Vector3 spawnCenter = position + new Vector3(
                Random.Range(-scatterX, scatterX),
                Random.Range(-scatterY, scatterY),
                0f);

            var go = new GameObject("RockFragment");
            // 조각은 항상 원본 스프라이트 크기로 나온다. localScale을 건드리지 않는다.
            go.transform.position = spawnCenter - (Vector3)spr.bounds.center;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            go.AddComponent<RockFragment>().Init(
                spr, velocity, finalLayer,
                sortingLayerID, sortingOrder,
                FragmentLifetime, FadeDuration, angularDamping);
        }
    }
}
```

- [ ] **Step 2: 변경 확인**

교체 후 아래가 맞는지 눈으로 확인한다:
- `fragmentSets` 필드와 `SetFragmentSets` 메서드가 사라졌다
- `go.transform.localScale = ...` 라인이 사라졌다
- `spr.bounds.center`에 `* scale`이 없다
- 물리·수명·산란 파라미터와 `RockFragment.Init` 호출 인자는 그대로다

이 시점에 `RockSpawner.cs:182`의 `vfx.SetFragmentSets(...)`가 컴파일 에러를 낸다. Task 4에서 고친다.

---

### Task 4: 데이터 전달 경로 — `RockData.tier` 배선

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/TerrainDecorator.cs:22` (구조체 필드)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/TerrainDecorator.cs:109` (`RestoreRocks` — 세이브 복원 경로)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockLayoutCalculator.cs:158`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockSpawner.cs:181-182`

**Interfaces:**
- Consumes: `RockSizeTier` (Task 1), `RockBreakVFX.Setup(TileType, RockSizeTier)` (Task 3)
- Produces: `TerrainDecorator.RockData.tier` — 레이아웃 단계에서 결정되어 spawn까지 전달된다

- [ ] **Step 1: `RockData`의 `fragmentSets`를 `tier`로 교체**

`TerrainDecorator.cs`에서 아래 줄을

```csharp
        public RockFragmentSet[] fragmentSets;
```

이렇게 바꾼다:

```csharp
        public RockSizeTier tier;   // 조각 VFX가 티어별 풀을 뽑는 기준
```

- [ ] **Step 2: `RockLayoutCalculator`에서 티어 복사**

`RockLayoutCalculator.cs:158`의 아래 줄을

```csharp
                    data.fragmentSets  = selectedSet.fragmentSets;
```

이렇게 바꾼다:

```csharp
                    data.tier          = selectedSet.tier;
```

- [ ] **Step 3: `RockSpawner`의 주입 호출 변경**

`RockSpawner.cs:181-182`의 아래 두 줄을

```csharp
        RockBreakVFX vfx = rockObj.GetComponent<RockBreakVFX>();
        if (vfx != null) vfx.SetFragmentSets(rock.fragmentSets);
```

이렇게 바꾼다:

```csharp
        RockBreakVFX vfx = rockObj.GetComponent<RockBreakVFX>();
        if (vfx != null) vfx.Setup(tileType, rock.tier);
```

`tileType`은 `SpawnRockObject`가 이미 파라미터로 받고 있다.

- [ ] **Step 4: 잔여 참조 확인**

`fragmentSets`를 참조하는 곳이 `TileVisualSettings.RockSpriteSet`(authoring 원본)과 `RockFragmentPool.GetOrBuild`(평탄화) 두 곳만 남았는지 검색해 확인한다. 그 외에 남아있으면 컴파일 에러로 드러난다.

- [ ] **Step 5: 컴파일 확인**

Unity 에디터에서 컴파일 에러가 0인지 확인한다.

---

### Task 5: 풀 초기화 배선과 에셋 마이그레이션

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs:235` 직후
- Modify: `Assets/Resources/TileVisualSettings` 에셋 (에디터 작업)

**Interfaces:**
- Consumes: `RockFragmentPool.SetSettings(TileVisualSettings)` (Task 2)

- [ ] **Step 1: `InfinityMapManager` 초기화에 `SetSettings` 주입**

`InfinityMapManager.cs`는 `tileVisualSettings` 필드를 보유하고(`InfinityMapManager.cs:23`),
초기화 코루틴에서 null 가드 후 `Resources.Load`로 폴백한다(`InfinityMapManager.cs:226-235`).

그 null 가드 블록이 **끝난 직후**, `// 1. Initialize Core Systems` 주석 **바로 위**에 한 줄을 추가한다:

```csharp
        // 조각 VFX 풀은 지형별 티어 버킷을 여기서 주입받아 최초 파괴 시 1회 빌드한다.
        RockFragmentPool.SetSettings(tileVisualSettings);
```

가드 뒤에 넣어야 `tileVisualSettings`가 null이 아님이 보장된다. `SetSettings`는 내부에서 `ClearCache()`를 호출하므로 씬 재진입 시 stale 버킷이 남지 않는다.

- [ ] **Step 2: 에셋에 티어 지정**

`TileVisualSettings` 에셋을 열고, 지형(`settings` 항목)마다 `rockSpriteSets`의 각 항목에 `tier`를 지정한다.

- `tier`는 신규 필드라 기존 항목이 **전부 `Small`(0)** 로 들어가 있다. 대·중형 돌을 빠뜨리면 소형 조각만 4개 나온다
- `fragmentMixRules`는 **비워둔다.** 비어있으면 `RockFragmentPool.DefaultRule`의 코드 기본값이 쓰인다. 튜닝이 필요해지면 그때 채운다

- [ ] **Step 3: 플레이 검증**

플레이 모드에서 스펙의 검증 항목을 확인한다:

- 대형 돌 반복 파괴 → 조각에 중·소 티어 스프라이트가 섞여 나오는가
- 소형 돌 파괴 → 소형 조각만 나오는가
- 같은 티어에서 크게/작게 굴러나온 돌 비교 → 조각 개수 차이가 보이는가 (Large 기준 6개 vs 10개)
- 조각이 원본 스프라이트 크기 그대로인가 (확대·축소 없음)
- 얼음 지형 돌에서 돌 조각이 나오지 않는가 (풀 스코프 격리)
- 청크를 멀리 벗어났다 돌아온 뒤 파괴 → 풀 재사용 시에도 정상 동작하는가

- [ ] **Step 4: EditMode 테스트 실행 (사람이 수행)**

Unity Test Runner에서 `RockFragmentPoolTests`를 실행한다.

- [ ] **Step 5: UVCS 체크인 (사람이 수행)**

변경 파일을 UVCS로 체크인한다. `git` 명령어는 사용하지 않는다.

---

## 후속 (이번 범위 아님)

- 티어마다 다른 발사 속도·중력 등 조각별 물리 파라미터
- 티어 4단계 이상 확장
- 조각 오브젝트 풀링 — 현재도 `Destroy` 방식이며 이번 변경으로 개수가 크게 늘지 않는다
