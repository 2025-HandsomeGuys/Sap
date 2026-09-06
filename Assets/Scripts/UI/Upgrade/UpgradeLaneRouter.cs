// @tags: upgrade, ui, tree, line, connection, routing, lane, rail
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 업그레이드 트리 연결선이 **어느 높이에서 옆 레인으로 건너갈지**를 레인 점유로 정한다.
///
/// 노드는 uiX가 몇 개 안 되는 값(-350/-175/0/175/350)에만 놓이므로 세로 '레인'이 생긴다.
/// 이 레인에 노드가 있는 구간에서는 선이 그 트렁크에 합쳐져 내려가고,
/// 노드가 없는 구간은 타지 않는다 — 빈 레인을 길게 따라 내려가면
/// "여기 뭔가 있는데 안 보인다"로 읽히고, 부모 바로 아래에서 갈라진 선이
/// 화면 절반을 홀로 내려가 트리가 성기게 보인다.
///
/// **허브가 먼저다.** 분기점(자식 여럿)·합류점(부모 여럿)에 붙은 선은 그 허브의
/// 공유 높이에서 꺾는다 — 한 줄로 나왔다 갈라지고, 한 줄로 모여서 들어간다(BuildHubs).
/// 계단 배치라 자식마다 높이가 달라도 꺾이는 줄은 하나여야 갈래 수가 읽힌다.
///
/// 허브가 아닌 선에만 아래 레인 규칙(부모 P → 자식 C, 둘 사이 구간을 span이라 할 때):
///   - P 레인이 span 안에 노드를 가짐        → **자식 직전**에 건너간다(트렁크에 붙어 내려가다 늦게 분기)
///   - P 레인은 비었고 C 레인은 노드를 가짐  → **부모 직후**에 건너간다(곧장 살아있는 트렁크로 합류)
///   - 그 외(둘 다 비었/둘 다 참)            → 자식 직전(기본)
///
/// 좌표는 UpgradeOverlayUI가 Xmap/Ymap으로 변환한 **매핑 좌표**를 쓴다.
/// 순수 C#이라 UpgradeLaneRouterTests가 SO 없이 그대로 검사한다.
/// </summary>
public class UpgradeLaneRouter
{
    /// <summary>이보다 가까운 x는 같은 레인으로 본다. 레인 간격(매핑 262)에 비해 넉넉히 작다.</summary>
    public const float LaneQuantum = 8f;

    /// <summary>레인 점유 판정에서 양 끝점을 제외하는 여유. 부모·자식 자신이 걸리면 안 된다.</summary>
    private const float EndpointMargin = 1f;

    private readonly Dictionary<int, List<float>> _laneYs = new Dictionary<int, List<float>>();
    private readonly Dictionary<string, float> _forkStub = new Dictionary<string, float>();
    private readonly Dictionary<string, float> _mergeStub = new Dictionary<string, float>();

    /// <summary>연결선 하나. 허브 판정에 부모·자식 id와 갈래 수가 필요해 좌표만으로는 부족하다.</summary>
    public readonly struct Link
    {
        public readonly string ParentId;
        public readonly string ChildId;
        public readonly Vector2 Start;
        public readonly Vector2 End;
        /// <summary>부모의 자식이 여럿인가(분기점).</summary>
        public readonly bool ParentForks;
        /// <summary>자식의 부모가 여럿인가(합류점).</summary>
        public readonly bool ChildMerges;

        public Link(string parentId, Vector2 start, string childId, Vector2 end,
                    bool parentForks, bool childMerges)
        {
            ParentId = parentId; Start = start;
            ChildId = childId; End = end;
            ParentForks = parentForks; ChildMerges = childMerges;
        }
    }


    /// <summary>매핑된 노드 좌표 전부를 받아 레인별 y 목록을 만든다. 선을 만들기 전에 한 번 호출한다.</summary>
    public void Build(IReadOnlyList<Vector2> mappedPositions)
    {
        _laneYs.Clear();
        if (mappedPositions == null) return;

        for (int i = 0; i < mappedPositions.Count; i++)
        {
            int key = LaneKey(mappedPositions[i].x);
            if (!_laneYs.TryGetValue(key, out var ys))
            {
                ys = new List<float>();
                _laneYs.Add(key, ys);
            }
            ys.Add(mappedPositions[i].y);
        }
    }

    /// <summary>laneX 레인이 yA~yB 사이(양 끝 제외)에 노드를 가지는가.</summary>
    public bool IsLaneAlive(float laneX, float yA, float yB)
    {
        if (!_laneYs.TryGetValue(LaneKey(laneX), out var ys)) return false;

        float lo = Mathf.Min(yA, yB) + EndpointMargin;
        float hi = Mathf.Max(yA, yB) - EndpointMargin;
        if (hi <= lo) return false;

        for (int i = 0; i < ys.Count; i++)
        {
            float y = ys[i];
            if (y > lo && y < hi) return true;
        }
        return false;
    }

    /// <summary>
    /// 허브(분기·합류)별 공유 꺾임 거리. 선을 만들기 전에 전체 연결을 한 번 훑어 채운다.
    ///
    /// 같은 허브에 붙은 선이 **전부 같은 값**을 써야 한 줄로 나왔다 갈라지고,
    /// 한 줄로 모여서 들어간다. 선마다 따로 계산하면 계단 배치에서 자식마다 거리가 달라
    /// 꺾이는 높이가 제각각이 되고, 허브 앞뒤가 빗자루처럼 벌어진다.
    /// 허브의 **가장 짧은 선**에 맞춘다 — 긴 선에 맞추면 짧은 선이 뒤로 넘어간다.
    /// </summary>
    public void BuildHubs(IReadOnlyList<Link> links, float clearance)
    {
        _forkStub.Clear();
        _mergeStub.Clear();
        if (links == null) return;

        for (int i = 0; i < links.Count; i++)
        {
            Link l = links[i];
            float span = Mathf.Abs(l.End.y - l.Start.y);
            if (span < 1f) continue;                                  // 같은 행은 가로 직선이라 꺾임이 없다
            if (Mathf.Abs(l.End.x - l.Start.x) < LaneQuantum) continue;   // 같은 레인도 마찬가지

            float stub = Stub(span, clearance);
            if (l.ParentForks) KeepShortest(_forkStub, l.ParentId, stub);
            if (l.ChildMerges) KeepShortest(_mergeStub, l.ChildId, stub);
        }
    }

    /// <summary>
    /// 선이 가로로 건너갈 높이. 같은 행·같은 레인이면 NaN(꺾을 필요가 없다).
    /// clearance = 노드 사각형 반높이 + 여유 — 꺾는 지점이 노드 안에 들어가지 않게 한다.
    ///
    /// 사람이 그린 모양(CSV lineBends)은 여기가 아니라 OrthogonalUILineRenderer가 처리한다 —
    /// 이 함수는 **자동일 때의 높이**만 정한다.
    ///
    /// 우선순위:
    ///   1. **합류**(자식의 부모가 여럿) — 자식 바로 앞에서 한 줄로 모여 함께 들어간다
    ///   2. **분기**(부모의 자식이 여럿) — 부모 바로 뒤에서 한 줄로 나왔다 거기서 갈라진다
    ///   3. 그 외 — 레인 점유(살아있는 트렁크에 붙어 가다 필요한 지점에서 건넌다)
    ///
    /// 합류가 분기보다 먼저다: 둘 다 걸리면 자식 쪽에 붙여야 들어오는 선들이 모인다.
    /// 들어오는 트렁크와 나가는 트렁크는 노드를 사이에 두고 반대편이라 겹치지 않는다.
    /// </summary>
    public float JogY(in Link link, float clearance)
    {
        Vector2 start = link.Start, end = link.End;
        float dy = end.y - start.y;
        if (Mathf.Abs(dy) < 1f) return float.NaN;                        // 같은 행 — 가로 직선
        if (Mathf.Abs(end.x - start.x) < LaneQuantum) return float.NaN;  // 같은 레인 — 세로 직선

        float dir = Mathf.Sign(dy);

        if (link.ChildMerges && _mergeStub.TryGetValue(link.ChildId, out float stubIn))
            return end.y - dir * stubIn;

        if (link.ParentForks && _forkStub.TryGetValue(link.ParentId, out float stubOut))
            return start.y + dir * stubOut;

        float stub = Stub(Mathf.Abs(dy), clearance);

        bool parentLaneAlive = IsLaneAlive(start.x, start.y, end.y);
        bool childLaneAlive = IsLaneAlive(end.x, start.y, end.y);

        if (!parentLaneAlive && childLaneAlive)
            return start.y + dir * stub;   // 부모 레인은 빈 구간 — 곧장 옆 트렁크로 합류

        return end.y - dir * stub;         // 부모 트렁크에 붙어 내려가다 자식 직전에 분기
    }

    /// <summary>
    /// 양쪽 노드 사각형을 다 피할 만큼 벌어져 있으면 clearance, 아니면 절반 지점.
    /// 절반으로 떨어지는 건 배치가 너무 촘촘하다는 뜻이라 트리 쪽에서 간격을 벌려야 한다.
    /// </summary>
    private static float Stub(float span, float clearance)
        => (span >= clearance * 2f) ? clearance : span * 0.5f;

    private static void KeepShortest(Dictionary<string, float> table, string key, float stub)
    {
        if (string.IsNullOrEmpty(key)) return;
        table[key] = table.TryGetValue(key, out float cur) ? Mathf.Min(cur, stub) : stub;
    }

    private static int LaneKey(float x) => Mathf.RoundToInt(x / LaneQuantum);
}
