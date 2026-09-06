// @tags: rock, vfx, break, decoration
using UnityEngine;

public class RockBreakVFX : MonoBehaviour
{
    [Header("돌 종류 정의")]
    [Tooltip("이 돌의 크기 티어. 조각 VFX가 티어별 풀을 섞는 기준이 된다.")]
    public RockSizeTier tier = RockSizeTier.Small;

    [Tooltip("이 돌 고유의 조각 스프라이트. 런타임에 평탄화되어 tier 버킷에 합쳐진다.\n" +
             "RockFragmentPool이 프리팹 에셋에서 직접 읽으므로 인스턴스화 없이도 조회된다.")]
    public RockFragmentSet[] fragmentSets;

    // 조각 스프라이트는 RockFragmentPool이 지형 단위로 보유한다.
    // 이 컴포넌트는 풀 조회 키만 들고 있어 청크 풀 재사용 시 stale 배열 참조가 생기지 않는다.
    private TileType _tileType = TileType.HardStone;

    /// <summary>
    /// RockSpawner가 스폰 시 지층을 주입한다.
    /// 티어는 프리팹 자신이 들고 있으므로 인자로 받지 않는다.
    /// </summary>
    public void Setup(TileType tileType)
    {
        _tileType = tileType;
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
        var rule  = RockFragmentPool.GetRule(_tileType, tier);
        int count = RockFragmentPool.ResolveCount(rule, scale);

        int finalLayer = LayerMask.NameToLayer("RockFragment");
        if (finalLayer < 0) finalLayer = gameObject.layer;

        var sr = GetComponent<SpriteRenderer>();
        int sortingLayerID = sr != null ? sr.sortingLayerID : 0;

        // 조각은 돌보다 한 단계 뒤(order-1)에 그린다.
        // 돌과 광물이 같은 order(-1)라 조각을 같은 값으로 두면 부서지는 순간
        // 조각이 광물을 덮어 "무엇이 나왔는지"가 안 보인다. 지형(0)보다는 여전히 뒤,
        // 플레이어(-49~-30)보다는 앞이라 다른 정렬 관계는 그대로다.
        int sortingOrder   = (sr != null ? sr.sortingOrder : 0) - 1;

        float speed = Random.Range(
            Mathf.Min(launchSpeedMin, launchSpeedMax),
            Mathf.Max(launchSpeedMin, launchSpeedMax));

        for (int i = 0; i < count; i++)
        {
            Sprite spr = RockFragmentPool.PickSprite(_tileType, tier);
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
