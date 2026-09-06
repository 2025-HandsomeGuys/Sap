// @tags: rock, fragment, tier, vfx, pool, decoration
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지형(TileType)별로 돌 조각 스프라이트를 크기 티어 버킷 3개로 묶어 캐시한다.
/// 큰 돌이 깨질 때 하위 티어 조각을 섞어 뽑기 위한 조회 계층.
///
/// 조각과 티어는 돌 프리팹의 RockBreakVFX가 들고 있다.
/// 버킷은 TileType당 최초 1회만 빌드된다. 파괴할 때마다 프리팹 목록을 순회하지 않는다.
/// </summary>
public static class RockFragmentPool
{
    private const int TierCount = 3;

    private class TerrainPool
    {
        public Sprite[][] buckets;                          // [tier] → 조각 스프라이트
        public Dictionary<RockSizeTier, TileVisualSettings.FragmentMixRule> rules;
    }

    private static readonly Dictionary<TileType, TerrainPool> _pools = new Dictionary<TileType, TerrainPool>();
    private static readonly HashSet<TileType> _warnedEmpty = new HashSet<TileType>();

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
            Debug.LogWarning($"[RockFragmentPool] TileType '{tileType}'에 조각 스프라이트가 하나도 없습니다. 돌 프리팹의 RockBreakVFX.fragmentSets를 확인하세요.");

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

        if (data.rockPrefabs != null)
        {
            foreach (GameObject prefab in data.rockPrefabs)
            {
                if (prefab == null) continue;

                // 프리팹 에셋에서 직접 읽는다 — 인스턴스화 없이 GetComponent가 동작한다.
                var vfx = prefab.GetComponent<RockBreakVFX>();
                if (vfx == null || vfx.fragmentSets == null) continue;

                // fragmentSets는 퍼즐 묶음이 아니라 단순 그룹핑 → 전부 평탄화해서 티어 버킷에 합친다.
                foreach (var fs in vfx.fragmentSets)
                {
                    if (fs?.sprites == null) continue;
                    foreach (Sprite spr in fs.sprites)
                        if (spr != null) lists[(int)vfx.tier].Add(spr);
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
