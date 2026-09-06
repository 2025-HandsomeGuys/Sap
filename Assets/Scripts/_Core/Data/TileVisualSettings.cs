// @tags: tile, visual, settings, scriptable-object, so, rock, sprite, prefab
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "TileVisualSettings", menuName = "Sap/Tile Visual Settings")]
public class TileVisualSettings : ScriptableObject
{
    /// <summary>
    /// 특정 티어의 돌이 깨졌을 때 조각을 어떻게 뽑을지 정하는 규칙.
    /// weight 합은 1일 필요 없다. 룰렛 선택 시 총합으로 정규화된다.
    ///
    /// 티어는 돌 프리팹(RockBreakVFX.tier)에 있고, 이 규칙은 지형(TileType) 단위라
    /// 프리팹이 아니라 여기 남는다.
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

    [System.Serializable]
    public struct TileVisualData
    {
        public TileType tileType;
        public Texture2D mainTexture;
        public Texture2D borderTexture;

        [Header("Rock Generation")]
        [Tooltip("이 지형에서 스폰할 돌 프리팹 목록.\n" +
                 "각 프리팹은 SpriteRenderer(정상 스프라이트) + PolygonCollider2D + DiggableRock +\n" +
                 "DamageStagedVisuals(균열 1·2단계) + RockBreakVFX(티어·조각)를 같은 GameObject에 가진다.\n" +
                 "순서를 바꾸면 세이브된 돌의 종류가 뒤바뀐다(RockSaveEntry.spriteSetIndex가 이 리스트의 인덱스).")]
        public List<GameObject> rockPrefabs;

        [Tooltip("티어별 조각 믹스 규칙. 비워두면 RockFragmentPool의 코드 기본값이 사용된다.")]
        public FragmentMixRule[] fragmentMixRules;

        [Tooltip("암석 간 최소 간격 (픽셀 단위)")]
        public float rockSpacingBuffer;

        // ─── LEGACY: 프리팹 이관 전용 ────────────────────────────────────
        // Tools/Rock/Migrate RockSpriteSets → Prefabs 를 실행해 rockPrefabs를 채운 뒤
        // 이 필드와 RockSpriteSet 구조체, RockPrefabMigrator.cs를 함께 삭제한다.
        // 런타임 코드는 이 필드를 더 이상 읽지 않는다.
        [HideInInspector] public List<RockSpriteSet> rockSpriteSets;
    }

    /// <summary>
    /// LEGACY — 프리팹 이관 전용. 마이그레이션 완료 후 삭제할 것.
    /// 필드를 지우면 TileVisualSettings.asset의 기존 스프라이트 참조가 즉시 소실되므로
    /// 반드시 마이그레이션 툴을 먼저 돌린다.
    /// </summary>
    [System.Serializable]
    public struct RockSpriteSet
    {
        public Sprite normal;
        public Sprite crack1;
        public Sprite crack2;
        public RockSizeTier tier;
        public RockFragmentSet[] fragmentSets;
    }

    public List<TileVisualData> settings;

    /// <summary>
    /// Helper to find visual data for a specific tile type.
    /// Returns the first entry (default) if not found.
    /// </summary>
    public TileVisualData GetDataForType(TileType type)
    {
        foreach (var data in settings)
        {
            if (data.tileType == type)
                return data;
        }

        // Fallback to first element if available
        if (settings != null && settings.Count > 0)
            return settings[0];

        return default;
    }
}
