// @tags: chunk, large-static, special-chunk, static-terrain, sprite-renderer, polygon-collider, ichunk
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 거대 특수 청크(예: 4000x1000)의 '기믹 본체' 전용 컴포넌트입니다.
/// 일반 TerrainChunk와 달리 파괴 가능한 픽셀(ChunkData) 배열을 갖지 않아 메모리와 연산을 극도로 절약합니다.
/// 파괴를 원할 경우 이 컴포넌트가 부착된 프리팹의 자식으로 일반 TerrainChunk 파츠를 달아 혼합(Hybrid) 구성합니다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(PolygonCollider2D))]
public class LargeStaticTerrainChunk : MonoBehaviour, IChunk
{
    [Header("Chunk Info")]
    [Tooltip("이 청크가 차지하는 그리드 단위의 폭 (1 = 1000px)")]
    public int chunkGridWidth = 1;
    [Tooltip("이 청크가 차지하는 그리드 단위의 높 (1 = 1000px)")]
    public int chunkGridHeight = 1;

    // IChunk 구현
    public Vector2Int Coord   { get; private set; }
    public int        Width   => chunkGridWidth  * 1000;
    public int        Height  => chunkGridHeight * 1000;

    // IChunk — OCP: SpecialChunkManager가 타입 검사 없이 호출
    public void OnSpawned(Vector2Int coord) => Initialize(coord);
    public bool NeedsDelayedActivation => false;

    public bool IsActive { get; private set; }

    private SpriteRenderer _spriteRenderer;
    private PolygonCollider2D _polygonCollider;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _polygonCollider = GetComponent<PolygonCollider2D>();
    }

    /// <summary>
    /// 매니저(SpecialChunkManager 등)에 의해 생성될 때 호출됩니다.
    /// TerrainChunk의 ReinitializeWithSize 같은 편법 없이 할당된 형태 그대로 나타납니다.
    /// </summary>
    public void Initialize(Vector2Int coord)
    {
        Coord = coord;
        IsActive = true;
        gameObject.SetActive(true);
        
        // Z-Index 등 필요한 렌더링 세팅이 있다면 여기서 수행
        // _spriteRenderer.sortingOrder = 0; // 일반 지형과 맞춤
    }

    /// <summary>
    /// 디스폰 또는 풀링 될 때 호출됩니다.
    /// </summary>
    public void DestroyChunk()
    {
        IsActive = false;
        gameObject.SetActive(false);
        // 만약 자식으로 생성된 일반 TerrainChunk 파츠들이 있다면 여기서 Destroy 파급 처리가 필요할 수도 있음.
    }
}
