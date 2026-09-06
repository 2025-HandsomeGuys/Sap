// @tags: digging, terrain, tracking, event, player
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어가 파고온 월드 위치를 셀 단위로 기록한다.
/// InfinityMapManager.OnTerrainModified 이벤트를 구독하므로
/// 게임오브젝트에 붙이기만 하면 자동으로 동작한다.
///
/// 탐사(ExploreArea)는 플레이어와 '연결된' 빈 공간만 BFS로 밝힌다 —
/// 벽 너머의 빈 공간(특수청크 내부 등)은 직접 도달하기 전까지 맵에 나타나지 않는다.
/// 벽 너머 감지는 탐지 유물 전용 훅인 RevealNearbyVoids()로 분리돼 있다.
/// </summary>
public class DigPathTracker : MonoBehaviour
{
    public static DigPathTracker Instance { get; private set; }

    [Header("셀 크기 (유닛)")]
    [Tooltip("파기 위치를 몇 유닛 단위로 그룹핑할지. 클수록 미니맵 해상도가 낮아지지만 성능이 더 좋다.")]
    public float cellSize = 3f;

    // 파인 셀 좌표 집합 (중복 자동 제거)
    private readonly HashSet<Vector2Int> _diggedCells = new HashSet<Vector2Int>();

    // ExploreArea BFS 재사용 버퍼 (매 호출 alloc 방지)
    private readonly Queue<Vector2Int> _bfsQueue = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> _bfsVisited = new HashSet<Vector2Int>();
    private static readonly Vector2Int[] Neighbors4 =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    // 변경 여부 플래그 — UndergroundMinimap이 매 프레임 재렌더링을 피하기 위해 참조
    public bool IsDirty { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        InfinityMapManager.OnTerrainModified += OnTerrainModified;
    }

    void OnDisable()
    {
        InfinityMapManager.OnTerrainModified -= OnTerrainModified;
    }

    /// <summary>
    /// 플레이어 위치에서 '연결된' 빈 공간만 반경 내에서 밝힌다 (4방향 BFS).
    /// 벽으로 막힌 빈 공간은 반경 안에 있어도 밝혀지지 않는다 —
    /// 특수청크의 큰 빈 공간이 근처만 갔다고 미리 드러나는 것을 방지.
    /// </summary>
    public void ExploreArea(Vector2 centerWorldPos, int radiusCells)
    {
        if (InfinityMapManager.Instance == null) return;

        Vector2Int start = WorldToCell(centerWorldPos);
        _bfsQueue.Clear();
        _bfsVisited.Clear();

        // 시드: 플레이어의 '실제 위치' 지형이 뚫려있으면 그 셀에서 시작.
        // (셀 중심이 아닌 실제 위치 기준 — 좁은 굴 안에서도 시드가 잡힌다)
        if (IsWorldPointOpen(centerWorldPos))
        {
            SeedCell(start);
        }
        else
        {
            foreach (var d in Neighbors4)
                if (IsCellTerrainOpen(start + d))
                    SeedCell(start + d);
        }

        int r2 = radiusCells * radiusCells;
        while (_bfsQueue.Count > 0)
        {
            Vector2Int cell = _bfsQueue.Dequeue();
            foreach (var d in Neighbors4)
            {
                Vector2Int next = cell + d;
                int dx = next.x - start.x;
                int dy = next.y - start.y;
                if (dx * dx + dy * dy > r2) continue;      // 원형 반경 제한

                // 밝히기(탐사 처리): cell과 공유하는 모서리를 여러 점 찍어 하나라도 뚫려 있으면
                // 파온 굴이 next로 이어진 것으로 본다. 셀 '중심'이 아직 흙인 부분 셀
                // (굴이 셀 가장자리·코너만 스친 셀)도 잡아 지나온 길의 검은 네모를 없앤다.
                // 모서리 선(두 셀 경계) 위만 확인하므로 벽 너머 공동은 여전히 새지 않는다.
                if (!_diggedCells.Contains(next) && IsEdgeOpen(cell, next) && _diggedCells.Add(next))
                    IsDirty = true;

                if (!_bfsVisited.Add(next)) continue;       // 전파 판정 1회
                if (!IsCellTerrainOpen(next)) continue;     // 중심이 흙인 셀로는 전파하지 않음
                if (!IsBoundaryOpen(cell, next)) continue;  // 셀 사이 얇은 벽 관통 방지
                _bfsQueue.Enqueue(next);
            }
        }
    }

    private void SeedCell(Vector2Int cell)
    {
        _bfsVisited.Add(cell);
        _bfsQueue.Enqueue(cell);
        if (_diggedCells.Add(cell)) IsDirty = true;
    }

    /// <summary>
    /// [탐지 유물 전용 훅 — 현재 미사용]
    /// 벽 너머를 포함해 반경 내 모든 빈 셀을 밝힌다 (관통 스캔).
    /// 나중에 소나 유물 장착 시 미니맵 Sweep/Ping 주기에 맞춰 호출하는 용도.
    /// </summary>
    public void RevealNearbyVoids(Vector2 centerWorldPos, int radiusCells)
    {
        if (InfinityMapManager.Instance == null) return;

        Vector2Int centerCell = WorldToCell(centerWorldPos);

        for (int y = -radiusCells; y <= radiusCells; y++)
        {
            for (int x = -radiusCells; x <= radiusCells; x++)
            {
                if (x * x + y * y > radiusCells * radiusCells) continue;

                Vector2Int cell = new Vector2Int(centerCell.x + x, centerCell.y + y);
                if (_diggedCells.Contains(cell)) continue;

                if (IsCellTerrainOpen(cell))
                {
                    _diggedCells.Add(cell);
                    IsDirty = true;
                }
            }
        }
    }

    /// <summary>셀 중심의 실제 지형이 뚫려있는지 (탐사 기록 무시, 지형만 판정)</summary>
    private bool IsCellTerrainOpen(Vector2Int cell) => IsWorldPointOpen(CellToWorldCenter(cell));

    /// <summary>두 인접 셀 사이 경계 지점(모서리 중앙)이 뚫려있는지 — 얇은 벽 관통 방지용(전파 판정)</summary>
    private bool IsBoundaryOpen(Vector2Int a, Vector2Int b)
        => IsWorldPointOpen((CellToWorldCenter(a) + CellToWorldCenter(b)) * 0.5f);

    // 공유 모서리를 따라 찍을 샘플 위치(셀 크기 배수, 중앙 0 기준 ±). 코너 쪽까지 넓게 훑는다.
    private static readonly float[] EdgeSampleT = { -0.44f, -0.22f, 0f, 0.22f, 0.44f };

    /// <summary>두 인접 셀의 공유 모서리를 여러 지점 찍어 하나라도 뚫려 있으면 true.
    /// 굴이 모서리 중앙이 아니라 코너 쪽만 지나가는 '부분 셀'을 놓치지 않기 위한 다중 샘플이다.
    /// 모서리 선(경계) 위만 보므로 벽 너머 공동은 밝히지 않는다(누출 방지는 IsBoundaryOpen과 동일).</summary>
    private bool IsEdgeOpen(Vector2Int a, Vector2Int b)
    {
        Vector2 ca = CellToWorldCenter(a);
        Vector2 cb = CellToWorldCenter(b);
        Vector2 mid = (ca + cb) * 0.5f;                                       // 공유 모서리 중앙
        Vector2 along = new Vector2(-(cb.y - ca.y), cb.x - ca.x) / cellSize;  // 모서리 방향 단위벡터
        foreach (float t in EdgeSampleT)
            if (IsWorldPointOpen(mid + along * (cellSize * t)))
                return true;
        return false;
    }

    /// <summary>월드 좌표의 실제 지형이 뚫려있는지 청크 데이터에서 확인</summary>
    private bool IsWorldPointOpen(Vector2 worldPos)
    {
        int cx = Mathf.FloorToInt(worldPos.x / InfinityMapManager.Instance.chunkWidthWorld);
        int cy = Mathf.FloorToInt(worldPos.y / InfinityMapManager.Instance.chunkHeightWorld);

        var chunk = InfinityMapManager.Instance.GetChunk(new Vector2Int(cx, cy));
        if (chunk == null || !chunk.baseData.IsCreated) return false; // 로드 전 청크는 막힌 것으로 취급

        float ppu = chunk.PPU;
        Vector2 chunkPos = chunk.transform.position;
        int px = Mathf.FloorToInt((worldPos.x - chunkPos.x) * ppu);
        int py = Mathf.FloorToInt((worldPos.y - chunkPos.y) * ppu);

        return chunk.IsTransparent(px, py);
    }

    private void OnTerrainModified(Vector2 worldPos, float radius)
    {
        // 파기 원이 걸치는 '모든' 셀을 탐사 처리한다 — 플레이어가 직접 파낸 자리는 무조건 본 곳이다.
        // 중심 셀만 기록하면 돌(DiggableRock)이 드러나며 셀 중심·모서리가 계속 막혀 있는 셀이
        // 탐사 누락돼 지도에 검은 네모로 남는다.
        int minX = Mathf.FloorToInt((worldPos.x - radius) / cellSize);
        int maxX = Mathf.FloorToInt((worldPos.x + radius) / cellSize);
        int minY = Mathf.FloorToInt((worldPos.y - radius) / cellSize);
        int maxY = Mathf.FloorToInt((worldPos.y + radius) / cellSize);
        float r2 = radius * radius;

        for (int cy = minY; cy <= maxY; cy++)
        {
            for (int cx = minX; cx <= maxX; cx++)
            {
                // 원-사각형(셀) 교차 판정: 셀 영역으로 클램프한 최근접점이 반경 안이면 걸친 것
                float nx = Mathf.Clamp(worldPos.x, cx * cellSize, (cx + 1) * cellSize);
                float ny = Mathf.Clamp(worldPos.y, cy * cellSize, (cy + 1) * cellSize);
                float ddx = worldPos.x - nx, ddy = worldPos.y - ny;
                if (ddx * ddx + ddy * ddy > r2) continue;

                if (_diggedCells.Add(new Vector2Int(cx, cy)))
                    IsDirty = true;
            }
        }
    }

    /// <summary>월드 좌표 → 셀 좌표 변환</summary>
    public Vector2Int WorldToCell(Vector2 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / cellSize),
            Mathf.FloorToInt(worldPos.y / cellSize)
        );
    }

    /// <summary>셀 좌표 → 셀 중심 월드 좌표 변환</summary>
    public Vector2 CellToWorldCenter(Vector2Int cell)
    {
        return new Vector2(
            (cell.x + 0.5f) * cellSize,
            (cell.y + 0.5f) * cellSize
        );
    }

    /// <summary>기록된 모든 파인 셀 반환</summary>
    public IReadOnlyCollection<Vector2Int> GetAllDiggedCells() => _diggedCells;

    /// <summary>해당 셀이 탐사(파기·개방 확인)됐는지 여부</summary>
    public bool IsExplored(Vector2Int cell) => _diggedCells.Contains(cell);

    /// <summary>Dirty 플래그 초기화 (미니맵 렌더 후 호출)</summary>
    public void ClearDirty() => IsDirty = false;

    /// <summary>저장된 경로 전체 초기화</summary>
    public void Reset()
    {
        _diggedCells.Clear();
        IsDirty = true;
    }
}
