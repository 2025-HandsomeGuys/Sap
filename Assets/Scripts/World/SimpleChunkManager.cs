using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// move 씬 전용 단순 청크 매니저.
/// GroundChunk 프리팹을 플레이어 주변 청크 좌표에 맞춰 반복 배치한다.
/// WorldManager / WorldGenerator 에 의존하지 않는다.
/// </summary>
public class SimpleChunkManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("청크 배치를 기준으로 삼을 플레이어 Transform")]
    public Transform player;

    [Tooltip("반복 배치할 GroundChunk 프리팹")]
    public GameObject groundChunkPrefab;

    [Header("Chunk Settings")]
    [Tooltip("GroundChunk 하나의 월드 단위 크기 (가로, 세로). TerrainChunk 기본값이면 (10,10) 정도가 자연스럽습니다.")]
    public Vector2 chunkWorldSize = new Vector2(10f, 10f);

    [Tooltip("플레이어를 중심으로 유지할 청크 반경 (X, Y). 예: (2,1) → 좌우 2칸, 위아래 1칸.")]
    public Vector2Int visibleRadius = new Vector2Int(2, 1);

    [Tooltip("청크 원점 오프셋. GroundChunk의 pivot이 중앙이 아닐 경우 보정용으로 사용.")]
    public Vector2 chunkOriginOffset = Vector2.zero;

    [Header("Spawn Limits")]
    [Tooltip("이 월드 Y 좌표보다 위에는 청크를 생성하지 않습니다. (예: 0이면 y<=0 영역에만 생성)")]
    public float maxGroundWorldY = 0f;

    // 현재 활성화된 청크 인스턴스들
    private readonly Dictionary<Vector2Int, GameObject> _activeChunks = new Dictionary<Vector2Int, GameObject>();

    // 마지막으로 기준 삼았던 플레이어의 청크 좌표
    private Vector2Int _currentCenterChunk;
    private bool _initialized;

    private void Start()
    {
        if (!ValidateConfiguration())
        {
            enabled = false;
            return;
        }

        _currentCenterChunk = WorldToChunkCoord(player.position);
        UpdateChunks();
        _initialized = true;
    }

    private void Update()
    {
        if (player == null)
            return;

        if (chunkWorldSize.x <= 0f || chunkWorldSize.y <= 0f)
            return;

        Vector2Int playerChunk = WorldToChunkCoord(player.position);

        // 플레이어가 다른 청크로 넘어갔을 때만 업데이트
        if (!_initialized || playerChunk != _currentCenterChunk)
        {
            _currentCenterChunk = playerChunk;
            UpdateChunks();
            _initialized = true;
        }
    }

    /// <summary>
    /// 월드 좌표를 청크 좌표로 변환.
    /// </summary>
    private Vector2Int WorldToChunkCoord(Vector3 worldPos)
    {
        float sx = chunkWorldSize.x;
        float sy = chunkWorldSize.y <= 0f ? 1f : chunkWorldSize.y;

        int cx = Mathf.FloorToInt(worldPos.x / sx);
        int cy = Mathf.FloorToInt(worldPos.y / sy);

        return new Vector2Int(cx, cy);
    }

    /// <summary>
    /// 현재 중심 청크와 visibleRadius를 기준으로 활성 청크를 갱신.
    /// </summary>
    private void UpdateChunks()
    {
        var needed = new HashSet<Vector2Int>();

        for (int dx = -visibleRadius.x; dx <= visibleRadius.x; dx++)
        {
            for (int dy = -visibleRadius.y; dy <= visibleRadius.y; dy++)
            {
                Vector2Int coord = new Vector2Int(_currentCenterChunk.x + dx, _currentCenterChunk.y + dy);

                // 청크 중심의 월드 Y 좌표가 제한 높이보다 위면 스폰/유지하지 않는다.
                float chunkCenterWorldY = coord.y * chunkWorldSize.y + chunkOriginOffset.y;
                if (chunkCenterWorldY > maxGroundWorldY)
                {
                    continue;
                }

                needed.Add(coord);

                if (!_activeChunks.ContainsKey(coord))
                {
                    SpawnChunkAt(coord);
                }
            }
        }

        // 범위를 벗어난 청크 제거
        var toRemove = new List<Vector2Int>();
        foreach (var kvp in _activeChunks)
        {
            if (!needed.Contains(kvp.Key))
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var key in toRemove)
        {
            _activeChunks.Remove(key);
        }
    }

    /// <summary>
    /// 지정된 청크 좌표에 GroundChunk를 스폰.
    /// </summary>
    private void SpawnChunkAt(Vector2Int coord)
    {
        if (groundChunkPrefab == null)
            return;

        Vector3 worldPos = new Vector3(
            coord.x * chunkWorldSize.x + chunkOriginOffset.x,
            coord.y * chunkWorldSize.y + chunkOriginOffset.y,
            0f
        );

        GameObject instance = Instantiate(groundChunkPrefab, worldPos, Quaternion.identity, transform);
        _activeChunks[coord] = instance;
    }

    /// <summary>
    /// 인스펙터 설정이 유효한지 검사하고, 문제가 있으면 로그를 남긴다.
    /// </summary>
    private bool ValidateConfiguration()
    {
        bool ok = true;

        if (player == null)
        {
            Debug.LogError("[SimpleChunkManager] Player reference is not assigned.", this);
            ok = false;
        }

        if (groundChunkPrefab == null)
        {
            Debug.LogError("[SimpleChunkManager] GroundChunk prefab is not assigned.", this);
            ok = false;
        }

        if (chunkWorldSize.x <= 0f || chunkWorldSize.y <= 0f)
        {
            Debug.LogWarning("[SimpleChunkManager] chunkWorldSize is not set properly. Please assign the GroundChunk world size in inspector.", this);
        }

        return ok;
    }

    /// <summary>
    /// 에디터에서 값 변경 후 강제로 리프레시하고 싶을 때 호출할 수 있는 헬퍼.
    /// </summary>
    public void ForceRefresh()
    {
        foreach (var kvp in _activeChunks)
        {
            if (kvp.Value != null)
            {
                DestroyImmediate(kvp.Value);
            }
        }

        _activeChunks.Clear();

        if (player != null)
        {
            _currentCenterChunk = WorldToChunkCoord(player.position);
            UpdateChunks();
            _initialized = true;
        }
    }
}


