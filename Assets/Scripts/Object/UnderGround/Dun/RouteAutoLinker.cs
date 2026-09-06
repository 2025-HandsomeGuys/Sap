// @tags: dungeon, trap, route, waypoint, linker, spawn
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 루트의 **시작 조각**에 붙어, 이어진 <see cref="RouteSegment"/> 체인을 따라 웨이포인트를 모으고
/// 그 위를 왕복할 장애물 하나를 스폰한다. 어떤 장애물을 태울지는 프리팹 변형마다 고정한다
/// (RouteStart_Blade / _Fire / _Spike) → 던전 임포터는 심볼대로 배치만 하면 되고 배선이 필요 없다.
///
/// 접합 판정은 **양방향**이다. 다음 조각의 StartPoint든 EndPoint든 현재 끝점에 닿으면 잇고,
/// EndPoint에 닿았으면 그 조각의 points를 뒤집어 쓴다. 단방향(StartPoint만 비교)으로 두면
/// 같은 "─" 모양이라도 좌→우용과 우→좌용 심볼을 따로 만들어야 해서 맵 심볼이 6개에서 12개로 늘어난다.
///
/// 진행 방향은 이 조각에서 양쪽으로 각각 걸어보고 **더 긴 쪽**을 택한다.
/// 시작 칸이 루트의 왼쪽 끝인지 오른쪽 끝인지 저작자가 신경 쓰지 않아도 되게 하기 위함.
///
/// 구간별 속도·정지는 각 조각의 <see cref="RouteSegment.speedMultiplier"/>·
/// <see cref="RouteSegment.pauseSeconds"/>에서 읽어 웨이포인트와 나란한 배열로 구워 넘긴다.
/// </summary>
public class RouteAutoLinker : MonoBehaviour
{
    [Header("생성할 장애물 프리팹")]
    public GameObject obstaclePrefab;

    [Header("시작 조각 (비우면 이 오브젝트의 RouteSegment)")]
    public RouteSegment firstSegment;

    [Header("장애물 이동 및 회전 설정")]
    public float obstacleSpeed = 5f;        // 이동 속도(구간 배율이 여기에 곱해진다)
    public float waitTimeAtEnds = 1.5f;     // 양 끝단 대기 시간 (초)
    public float obstacleRotationSpeed = 0f; // 회전 속도 (초당 각도, 마이너스면 시계방향)

    // 이웃 조각의 끝점이 이만큼 안에 있으면 이어진 것으로 본다.
    // 루트 조각의 끝점이 ±0.5이므로 Grid cellSize가 1일 때 이웃 칸 중심과 정확히 맞물린다.
    private const float JointTolerance = 0.1f;

    // 접합부에서 같은 좌표가 두 번 들어가는 것을 막는 간격.
    private const float DuplicateTolerance = 0.05f;

    private const int SafetyLimit = 200;

    /// <summary>체인을 걸으며 모은 결과. 세 리스트는 항상 같은 길이를 유지한다.</summary>
    private class Chain
    {
        public readonly List<Transform> Points = new List<Transform>();
        public readonly List<float> EdgeMultipliers = new List<float>(); // [i] = i-1 → i 구간
        public readonly List<float> Pauses = new List<float>();          // [i] = i에 도착 시 정지
        public int Count => Points.Count;
    }

    private void Start()
    {
        var first = firstSegment != null ? firstSegment : GetComponent<RouteSegment>();
        if (first == null)
        {
            Debug.LogWarning($"[Route] '{name}' 시작 조각(RouteSegment)이 없어 루트를 만들지 못했습니다.", this);
            return;
        }
        if (obstaclePrefab == null)
        {
            Debug.LogWarning($"[Route] '{name}' obstaclePrefab이 비어 있어 장애물을 스폰하지 않았습니다.", this);
            return;
        }

        // 같은 던전 인스턴스 안의 조각만 후보로 둔다. 전역 검색(FindObjectsByType)으로 두면
        // 다른 루트나 다른 던전의 조각이 우연히 좌표가 겹칠 때 체인이 서로 이어붙는다.
        var candidates = transform.root.GetComponentsInChildren<RouteSegment>(true);

        // 시작 조각의 양쪽으로 각각 걸어보고 더 긴 체인을 채택.
        float tol = ToleranceScale(first);
        var forward = Walk(first, false, candidates, tol);
        var backward = Walk(first, true, candidates, tol);
        var chain = forward.Count >= backward.Count ? forward : backward;

        if (chain.Count < 2)
        {
            Debug.LogWarning($"[Route] '{name}' 웨이포인트가 {chain.Count}개뿐입니다. " +
                             "루트 조각이 이어져 있는지(씬 뷰의 시안색 기즈모) 확인하세요.", this);
            return;
        }

        // 부모를 지정하지 않으면 씬 루트로 빠져나가 던전을 나가도 파괴되지 않는다
        // (DungeonOverlayController는 던전 인스턴스만 Destroy한다) → 재입장할 때마다 유령이 쌓인다.
        Transform parent = transform.parent != null ? transform.parent : transform.root;

        var obs = Instantiate(obstaclePrefab, chain.Points[0].position, Quaternion.identity, parent);
        obs.name = $"{obstaclePrefab.name} (route:{name})";

        // 장애물 프리팹도 "한 칸 = 1유닛" 기준이다. 루트 조각이 칸 크기에 맞춰 축소 배치됐으면
        // 같은 비율로 줄여야 경로 폭과 장애물 크기가 맞는다. 부모가 이미 축척을 갖고 있으면 그만큼 뺀다.
        float parentScale = Mathf.Abs(parent.lossyScale.x);
        float segmentScale = Mathf.Abs(first.transform.lossyScale.x);
        if (parentScale > 0.0001f && !Mathf.Approximately(segmentScale, parentScale))
            obs.transform.localScale *= segmentScale / parentScale;

        var mover = obs.GetComponent<MovingObstacle>();
        if (mover == null)
        {
            Debug.LogWarning($"[Route] '{obstaclePrefab.name}'에 MovingObstacle이 없어 제자리에 멈춰 있습니다.", this);
            return;
        }

        mover.waypoints = chain.Points.ToArray();
        mover.edgeSpeedMultipliers = chain.EdgeMultipliers.ToArray();
        mover.waypointPauses = chain.Pauses.ToArray();
        mover.speed = obstacleSpeed;
        mover.waitTimeAtEnds = waitTimeAtEnds;
        mover.rotationSpeed = obstacleRotationSpeed;
    }

    /// <summary>
    /// 플레이 전에 **실제로 이어진 경로**를 그린다.
    /// RouteSegment의 기즈모는 조각마다 제 선을 그을 뿐이라 "붙어 보이는 것"과 "이어진 것"을 구분하지 못한다.
    /// 여기서는 런타임과 같은 체인 탐색을 돌려서, 장애물이 실제로 지나갈 경로만 흰 선으로 잇는다.
    /// </summary>
    private void OnDrawGizmos()
    {
        var first = firstSegment != null ? firstSegment : GetComponent<RouteSegment>();
        if (first == null) return;

        var candidates = transform.root.GetComponentsInChildren<RouteSegment>(true);
        float tol = ToleranceScale(first);
        var forward = Walk(first, false, candidates, tol);
        var backward = Walk(first, true, candidates, tol);
        var chain = forward.Count >= backward.Count ? forward : backward;

        if (chain.Count < 2)
        {
            // 체인이 안 잡힘 — 이웃 조각이 JointTolerance 밖이거나 방향이 끊겼다.
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.45f);
            return;
        }

        Gizmos.color = Color.white;
        for (int i = 1; i < chain.Count; i++)
            Gizmos.DrawLine(chain.Points[i - 1].position, chain.Points[i].position);

        // 장애물이 스폰될 자리(초록)와 왕복의 반대쪽 끝(자홍).
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(chain.Points[0].position, 0.32f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(chain.Points[chain.Count - 1].position, 0.32f);
    }

    /// <summary>
    /// 에디터 진단용 — 런타임과 **똑같은** 탐색을 돌려 결과를 한 줄로 요약한다.
    /// 기즈모는 눈으로 위치를 가늠해야 해서 "코너에서 끊겼는지"를 확답하지 못한다.
    /// </summary>
    public string DescribeChain()
    {
        var first = firstSegment != null ? firstSegment : GetComponent<RouteSegment>();
        if (first == null) return "✗ 시작 조각(RouteSegment) 없음";

        var candidates = transform.root.GetComponentsInChildren<RouteSegment>(true);
        float tol = ToleranceScale(first);
        var forward = Walk(first, false, candidates, tol);
        var backward = Walk(first, true, candidates, tol);
        var chain = forward.Count >= backward.Count ? forward : backward;

        if (chain.Count < 2)
            return $"✗ 체인 실패 — 웨이포인트 {chain.Count}개 (후보 조각 {candidates.Length}개). " +
                   $"이웃 조각이 접합 허용오차({JointTolerance * tol:0.###}) 밖입니다. " +
                   $"조각 스케일 {tol:0.###} — 칸 크기와 맞는지 확인하세요.";

        Vector3 s = chain.Points[0].position;
        Vector3 e = chain.Points[chain.Count - 1].position;

        int pauseCount = 0;
        foreach (float p in chain.Pauses) if (p > 0f) pauseCount++;

        // 구간 배율을 런렝스로 압축: "1×7, 3×3, 1×4"
        var runs = new System.Text.StringBuilder();
        float cur = chain.EdgeMultipliers[1];
        int run = 0;
        for (int i = 1; i < chain.Count; i++)
        {
            if (Mathf.Approximately(chain.EdgeMultipliers[i], cur)) { run++; continue; }
            if (runs.Length > 0) runs.Append(", ");
            runs.Append($"{cur:0.##}×{run}");
            cur = chain.EdgeMultipliers[i];
            run = 1;
        }
        if (runs.Length > 0) runs.Append(", ");
        runs.Append($"{cur:0.##}×{run}");

        return $"웨이포인트 {chain.Count}개  ({s.x:0.##}, {s.y:0.##}) → ({e.x:0.##}, {e.y:0.##})  " +
               $"배율 [{runs}]  정지 {pauseCount}곳  (씬의 조각 {candidates.Length}개)";
    }

    /// <summary>
    /// 접합 허용오차의 배율. 조각이 축소 배치되면(특수 청크는 타일이 0.25유닛) 끝점 간격도 같이 줄어들어,
    /// 고정 허용오차 0.1이 간격(0.125)에 육박해 엉뚱한 조각과 맞물린다.
    /// </summary>
    private static float ToleranceScale(RouteSegment segment)
        => Mathf.Max(0.01f, Mathf.Abs(segment.transform.lossyScale.x));

    /// <summary>시작 조각에서 한 방향으로 체인을 따라가며 웨이포인트와 구간 파라미터를 모은다.</summary>
    private static Chain Walk(RouteSegment first, bool startReversed, RouteSegment[] candidates, float tolScale)
    {
        var chain = new Chain();
        var consumed = new HashSet<RouteSegment>();

        RouteSegment segment = first;
        bool reversed = startReversed;

        for (int i = 0; segment != null && i < SafetyLimit; i++)
        {
            consumed.Add(segment);
            Append(chain, segment, reversed, tolScale);
            if (chain.Count == 0) break;

            segment = FindNext(chain.Points[chain.Count - 1].position, candidates, consumed, tolScale, out reversed);
        }
        return chain;
    }

    // 조각의 points를 방향에 맞춰 이어붙인다. 접합부의 중복 좌표는 건너뛴다.
    private static void Append(Chain chain, RouteSegment segment, bool reversed, float tolScale)
    {
        var src = segment.points;
        if (src == null) return;

        float mul = segment.speedMultiplier > 0f ? segment.speedMultiplier : 1f;
        int lastAdded = -1;

        for (int i = 0; i < src.Length; i++)
        {
            Transform p = reversed ? src[src.Length - 1 - i] : src[i];
            if (p == null) continue;

            if (chain.Count > 0 &&
                Vector2.Distance(chain.Points[chain.Count - 1].position, p.position) < DuplicateTolerance * tolScale)
                continue;

            chain.Points.Add(p);
            chain.EdgeMultipliers.Add(mul); // 이 점으로 들어오는 구간은 이 조각에 속한다
            chain.Pauses.Add(0f);
            lastAdded = chain.Count - 1;
        }

        // 정지는 이 조각을 '빠져나가는' 점에 건다 — 방향이 뒤집혀도 같은 자리에서 쉰다.
        if (lastAdded >= 0 && segment.pauseSeconds > 0f)
            chain.Pauses[lastAdded] = segment.pauseSeconds;
    }

    // 현재 끝점에 닿는 다음 조각을 찾는다. EndPoint 쪽으로 닿았으면 뒤집어 써야 한다.
    private static RouteSegment FindNext(
        Vector3 tip, RouteSegment[] candidates, HashSet<RouteSegment> consumed, float tolScale, out bool reversed)
    {
        reversed = false;
        foreach (var seg in candidates)
        {
            if (seg == null || consumed.Contains(seg)) continue;
            if (seg.StartPoint == null || seg.EndPoint == null) continue;

            if (Vector2.Distance(seg.StartPoint.position, tip) < JointTolerance * tolScale)
            {
                reversed = false;
                return seg;
            }
            if (Vector2.Distance(seg.EndPoint.position, tip) < JointTolerance * tolScale)
            {
                reversed = true;
                return seg;
            }
        }
        return null;
    }
}
