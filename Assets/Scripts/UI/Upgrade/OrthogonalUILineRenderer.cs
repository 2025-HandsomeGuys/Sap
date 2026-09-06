// @tags: upgrade, ui, tree, line, connection, routing, orthogonal
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 직각(Orthogonal) 방식으로 두 UI 요소 사이를 연결하는 선을 그립니다.
/// 수직/수평 라인으로만 구성되어 더 깔끔한 트리 구조를 만듭니다.
///
/// ⚠ 장애물(노드 사각형)을 넘겨주면 그 위를 지나가지 않게 우회한다.
///   넘기지 않으면 예전처럼 직선으로 관통한다 — 그 상태에서는 부모·자식 사이에
///   낀 형제 노드가 '선행 단계'처럼 보인다(밀착 등반 I 오인 사례).
/// </summary>
public class OrthogonalUILineRenderer : MonoBehaviour
{
    public float lineWidth = 3f;
    public Color lineColor = new Color(0.8f, 0.8f, 0.8f, 1f);
    public Color lockedLineColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    public Color unlockedLineColor = new Color(1f, 0.84f, 0f, 1f); // 노란색

    [Tooltip("우회할 때 옆으로 비켜서는 한 칸 거리")]
    public float detourLane = 110f;

    private List<Image> _lineSegments = new List<Image>();
    private bool _isUnlocked = false;

    /// <summary>
    /// 직각 방식으로 두 점을 연결합니다.
    /// obstacles를 주면 그 사각형을 피해 우회 경로를 찾는다(끝점을 품은 사각형은 자기 노드이므로 무시).
    /// </summary>
    public void DrawOrthogonalLine(Vector2 startPos, Vector2 endPos, bool isUnlocked = false,
                                   IReadOnlyList<Rect> obstacles = null, float trunkY = float.NaN,
                                   IReadOnlyList<Vector2> bends = null)
    {
        _isUnlocked = isUnlocked;
        ClearLines();

        List<Vector2> path = BuildPath(startPos, endPos, obstacles, detourLane, trunkY, bends);
        for (int i = 1; i < path.Count; i++)
        {
            Vector2 a = path[i - 1];
            Vector2 b = path[i];
            if ((a - b).sqrMagnitude < 0.01f) continue;
            CreateLineSegment(a, b, Mathf.Abs(a.x - b.x) < 0.1f);
        }

        UpdateLineColors();
    }

    // ===== 경로 계산 (순수 함수 — UpgradeTreeLineRoutingTests가 실제 트리 좌표로 검사한다) =====

    /// <summary>우회 경로가 시작·끝 행에서 빠져나오는 거리. 세로 간격에 대한 비율이다.</summary>
    private static readonly float[] StubFractions = { 0.25f, 0.4f, 0.12f };

    private const int MaxLaneSteps = 4;

    /// <summary>
    /// 두 점을 잇는 직각 경로의 꺾인점 목록을 만든다.
    /// 기본 경로가 장애물을 지나면 옆 차선으로 우회한 경로를 돌려준다.
    /// 어느 차선으로도 못 피하면 기본 경로를 그대로 돌려준다(선이 사라지는 것보다 낫다).
    ///
    /// ⚠ stub을 고정값으로 두면 안 된다 — 행 간격이 좁은 T1에서는 빠져나오는 지점이
    ///   그대로 이웃 노드 사각형 안이라 어느 차선을 골라도 전부 막힌다. 세로 간격 비율로 잡는다.
    /// </summary>
    public static List<Vector2> BuildPath(Vector2 start, Vector2 end, IReadOnlyList<Rect> obstacles,
                                          float lane = 110f, float trunkY = float.NaN,
                                          IReadOnlyList<Vector2> bends = null)
    {
        // 사람이 그린 모양이 있으면 그게 먼저다. 막히면 아래 자동 경로로 떨어진다 —
        // 선이 사라지는 것보다 낫고, 편집기가 "지정이 무시됐다"고 따로 알려준다.
        if (bends != null && bends.Count > 0)
        {
            List<Vector2> drawn = BuildBendPath(start, end, bends);
            if (!IsBlocked(drawn, obstacles, start, end)) return drawn;
        }

        // trunkY = 가로로 건너갈 높이. UpgradeLaneRouter가 레인 점유를 보고 정한다
        // (살아있는 트렁크에 붙어 내려가다 자식 직전에 갈라진다). 막히면 아래로 떨어진다.
        if (!float.IsNaN(trunkY))
        {
            List<Vector2> trunk = BuildTrunkPath(start, end, trunkY);
            if (!IsBlocked(trunk, obstacles, start, end)) return trunk;

            // 트렁크가 관계없는 노드를 관통한다 — 긴 세로 구간만 반 레인 옆으로 밀어 붙여 지나간다.
            List<Vector2> hug = BuildHuggingTrunkPath(start, end, obstacles, lane, trunkY);
            if (hug != null) return hug;
        }

        List<Vector2> direct = BuildDirectPath(start, end);
        if (!IsBlocked(direct, obstacles, start, end)) return direct;

        float span = Mathf.Abs(end.y - start.y);
        if (span < 1f) return direct;   // 같은 행끼리는 세로 우회가 성립하지 않는다

        // 트리 바깥쪽(중심에서 먼 쪽)으로 먼저 비켜본다 — 안쪽은 트렁크가 붐빈다.
        float mid = (start.x + end.x) * 0.5f;
        float outward = mid >= 0f ? 1f : -1f;

        for (int f = 0; f < StubFractions.Length; f++)
        {
            float stub = span * StubFractions[f];
            for (int step = 1; step <= MaxLaneSteps; step++)
            {
                for (int s = 0; s < 2; s++)
                {
                    float sign = (s == 0) ? outward : -outward;
                    var detour = BuildDetourPath(start, end, mid + sign * lane * step, stub);
                    if (!IsBlocked(detour, obstacles, start, end)) return detour;
                }
            }
        }

        return direct;
    }

    /// <summary>
    /// 사람이 그린 꺾임점으로 경로를 만든다.
    ///
    /// 꺾임점 하나 = "이 높이(y)에서 가로로 건너가 이 레인(x)을 탄다".
    /// 부모 레인에서 세로로 올라와 순서대로 건너가고, **마지막 꺾임점은 자식 레인으로 강제**한다 —
    /// 안 그러면 마지막 세로 구간이 자식에서 어긋나 선이 노드 옆 허공에서 끝난다.
    ///
    /// 좌표는 이미 매핑 좌표다(UpgradeOverlayUI가 ui → 매핑으로 바꿔 넘긴다).
    /// </summary>
    public static List<Vector2> BuildBendPath(Vector2 start, Vector2 end, IReadOnlyList<Vector2> bends)
    {
        var pts = new List<Vector2>(bends.Count * 2 + 2) { start };
        float x = start.x;

        for (int i = 0; i < bends.Count; i++)
        {
            float bx = (i == bends.Count - 1) ? end.x : bends[i].x;
            float by = bends[i].y;
            pts.Add(new Vector2(x, by));      // 지금 레인에서 그 높이까지 세로
            pts.Add(new Vector2(bx, by));     // 그 높이에서 다음 레인까지 가로
            x = bx;
        }

        pts.Add(end);                          // 마지막 레인 = 자식 레인이므로 세로로 들어간다
        return pts;
    }

    /// <summary>
    /// 세로 → trunkY에서 가로 → 세로. trunkY를 한쪽 끝에 붙여 잡으면
    /// 반대쪽 세로 구간이 길어져 그 레인 트렁크와 한 줄로 겹친다 — 그게 '합쳐진 선'이다.
    /// </summary>
    public static List<Vector2> BuildTrunkPath(Vector2 start, Vector2 end, float trunkY)
    {
        return new List<Vector2>(4)
        {
            start,
            new Vector2(start.x, trunkY),
            new Vector2(end.x,   trunkY),
            end,
        };
    }

    /// <summary>
    /// 트렁크(BuildTrunkPath)가 남의 노드를 관통할 때 쓰는 변형.
    /// 두 세로 구간 중 **긴 쪽만** 반 레인 옆으로 밀어, 트렁크에 바짝 붙은 채로 노드를 지나간다.
    /// 합쳐진 한 줄로 읽히면서 관통은 없어진다 — 완전히 다른 차선으로 우회해 버리면
    /// 그 선만 혼자 멀리 떨어져 "합쳐졌다 갈라진다"가 안 보인다.
    ///
    /// 비켜서는 높이는 trunkY가 이미 확보해 둔 여유(짧은 쪽 세로 구간 = clearance)를 그대로 쓴다.
    /// 여기서 새 상수를 만들면 노드 사각형 안에서 옆으로 튀어나온다.
    /// 어느 차선으로도 못 피하면 null — 호출측이 기존 우회 사다리로 떨어진다.
    /// </summary>
    private static List<Vector2> BuildHuggingTrunkPath(Vector2 start, Vector2 end, IReadOnlyList<Rect> obstacles,
                                                       float lane, float trunkY)
    {
        float dy = end.y - start.y;
        if (Mathf.Abs(dy) < 1f) return null;

        float dir = Mathf.Sign(dy);
        float runOut = (trunkY - start.y) * dir;   // 부모 쪽 세로 구간
        float runIn = (end.y - trunkY) * dir;      // 자식 쪽 세로 구간
        if (runOut <= 0f || runIn <= 0f) return null;   // trunkY가 두 노드 사이에 있지 않다

        float clear = Mathf.Min(runOut, runIn);

        bool bulgeOut = runOut >= runIn;               // 긴 쪽을 민다
        if ((bulgeOut ? runOut : runIn) - clear < 2f) return null;   // 밀 구간이 남지 않는다

        float pivotX = bulgeOut ? start.x : end.x;
        float toward = (end.x >= start.x) ? 1f : -1f;

        for (int step = 1; step <= 2; step++)
        {
            for (int s = 0; s < 2; s++)
            {
                float laneX = pivotX + (s == 0 ? toward : -toward) * lane * step;
                var path = TryLanePath(start, end, obstacles, trunkY, dir, clear, laneX, bulgeOut);
                if (path != null) return path;
            }
        }
        return null;
    }

    /// <summary>
    /// laneX를 타는 S자 경로 하나를 만들어 보고, 막히면 null.
    /// bulgeOut이면 부모 쪽에서 빠져나와 laneX를 타고 trunkY에서 자식으로,
    /// 아니면 trunkY에서 laneX로 건너가 자식 앞에서 되돌아온다.
    /// </summary>
    private static List<Vector2> TryLanePath(Vector2 start, Vector2 end, IReadOnlyList<Rect> obstacles,
                                             float trunkY, float dir, float clear, float laneX, bool bulgeOut)
    {
        float run = bulgeOut ? (trunkY - start.y) * dir : (end.y - trunkY) * dir;
        if (run - clear < 2f) return null;             // 밀 구간이 남지 않는다

        float breakY = bulgeOut ? start.y + dir * clear : end.y - dir * clear;
        List<Vector2> path = bulgeOut
            ? new List<Vector2>(6)
              {
                  start,
                  new Vector2(start.x, breakY),
                  new Vector2(laneX,   breakY),
                  new Vector2(laneX,   trunkY),
                  new Vector2(end.x,   trunkY),
                  end,
              }
            : new List<Vector2>(6)
              {
                  start,
                  new Vector2(start.x, trunkY),
                  new Vector2(laneX,   trunkY),
                  new Vector2(laneX,   breakY),
                  new Vector2(end.x,   breakY),
                  end,
              };

        return IsBlocked(path, obstacles, start, end) ? null : path;
    }

    /// <summary>수직 → 수평 → 수직 (또는 그 반대). 예전부터 쓰던 기본 모양.</summary>
    private static List<Vector2> BuildDirectPath(Vector2 start, Vector2 end)
    {
        var pts = new List<Vector2>(4) { start };

        if (Mathf.Abs(end.y - start.y) > Mathf.Abs(end.x - start.x) * 0.5f)
        {
            // 주로 수직 이동 (트리 구조의 일반적인 경우)
            float midY = (start.y + end.y) * 0.5f;
            pts.Add(new Vector2(start.x, midY));
            pts.Add(new Vector2(end.x, midY));
        }
        else
        {
            // 주로 수평 이동
            float midX = (start.x + end.x) * 0.5f;
            pts.Add(new Vector2(midX, start.y));
            pts.Add(new Vector2(midX, end.y));
        }

        pts.Add(end);
        return pts;
    }

    /// <summary>
    /// 끝점에서 곧게 빠져나온 뒤 laneX 차선으로 비켜서 세로로 지나가고, 도착 직전에 돌아온다.
    /// 노드 사이를 '옆으로 지나간다'는 게 눈에 보여야 선행 오인이 사라진다.
    /// </summary>
    private static List<Vector2> BuildDetourPath(Vector2 start, Vector2 end, float laneX, float stub)
    {
        float span = Mathf.Abs(end.y - start.y);
        float s = Mathf.Min(stub, span * 0.45f);          // 짧은 구간에서 스텁이 서로 넘어가지 않게
        float dir = (end.y >= start.y) ? 1f : -1f;

        float y1 = start.y + dir * s;
        float y2 = end.y - dir * s;

        return new List<Vector2>(6)
        {
            start,
            new Vector2(start.x, y1),
            new Vector2(laneX,   y1),
            new Vector2(laneX,   y2),
            new Vector2(end.x,   y2),
            end,
        };
    }

    /// <summary>
    /// 경로가 장애물을 지나는지. 끝점을 품은 사각형은 연결 당사자(부모·자식)이므로 건너뛴다.
    /// </summary>
    public static bool IsBlocked(List<Vector2> path, IReadOnlyList<Rect> obstacles, Vector2 start, Vector2 end)
    {
        if (obstacles == null || obstacles.Count == 0) return false;

        for (int o = 0; o < obstacles.Count; o++)
        {
            Rect box = obstacles[o];
            if (box.Contains(start) || box.Contains(end)) continue;

            for (int i = 1; i < path.Count; i++)
            {
                if (SegmentHits(path[i - 1], path[i], box)) return true;
            }
        }
        return false;
    }

    /// <summary>수직·수평 선분과 사각형의 겹침 판정.</summary>
    private static bool SegmentHits(Vector2 a, Vector2 b, Rect box)
    {
        float minX = Mathf.Min(a.x, b.x);
        float maxX = Mathf.Max(a.x, b.x);
        float minY = Mathf.Min(a.y, b.y);
        float maxY = Mathf.Max(a.y, b.y);

        return maxX > box.xMin && minX < box.xMax && maxY > box.yMin && minY < box.yMax;
    }

    // ===== 그리기 =====

    /// <summary>
    /// 개별 라인 세그먼트(수직 또는 수평)를 생성합니다.
    /// </summary>
    private void CreateLineSegment(Vector2 start, Vector2 end, bool isVertical)
    {
        GameObject lineObj = new GameObject("LineSegment");
        lineObj.transform.SetParent(transform, false);

        RectTransform rectTransform = lineObj.AddComponent<RectTransform>();
        Image lineImage = lineObj.AddComponent<Image>();

        lineImage.color = _isUnlocked ? unlockedLineColor : lineColor;

        if (isVertical)
        {
            float height = Mathf.Abs(end.y - start.y);
            rectTransform.anchoredPosition = new Vector2(start.x, Mathf.Min(start.y, end.y) + height * 0.5f);
            rectTransform.sizeDelta = new Vector2(lineWidth, height);
        }
        else
        {
            // 꺾이는 지점이 이가 빠져 보이지 않게 양 끝을 선 굵기만큼 늘린다
            float width = Mathf.Abs(end.x - start.x) + lineWidth;
            rectTransform.anchoredPosition = new Vector2((start.x + end.x) * 0.5f, start.y);
            rectTransform.sizeDelta = new Vector2(width, lineWidth);
        }

        _lineSegments.Add(lineImage);
    }

    /// <summary>
    /// 선의 색상을 업데이트합니다.
    /// </summary>
    public void UpdateLineColors()
    {
        Color targetColor = _isUnlocked ? unlockedLineColor : lineColor;
        foreach (var segment in _lineSegments)
        {
            if (segment != null)
            {
                segment.color = targetColor;
            }
        }
    }

    /// <summary>
    /// 잠금 상태를 설정합니다.
    /// </summary>
    public void SetLocked(bool locked)
    {
        foreach (var segment in _lineSegments)
        {
            if (segment != null)
            {
                segment.color = locked ? lockedLineColor : lineColor;
            }
        }
    }

    /// <summary>
    /// 해금 상태를 설정합니다.
    /// </summary>
    public void SetUnlocked(bool unlocked)
    {
        _isUnlocked = unlocked;
        UpdateLineColors();
    }

    /// <summary>
    /// 모든 라인 세그먼트를 제거합니다.
    /// </summary>
    private void ClearLines()
    {
        foreach (var segment in _lineSegments)
        {
            if (segment != null)
            {
                Destroy(segment.gameObject);
            }
        }
        _lineSegments.Clear();
    }

    void OnDestroy()
    {
        ClearLines();
    }
}
