// @tags: test, editmode, upgrade, tree, ui, line, routing
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 업그레이드 트리의 연결선이 '관계 없는 노드'를 지나가지 않는지 검사한다.
///
/// 배경: 밀착 등반 I(700, 100)과 낙법 훈련 I(700, -50)은 둘 다 벽 기어오르기 I의
/// 자식(형제)인데 같은 열에 세로로 쌓여 있어서, 벽 기어오르기 → 밀착 등반 선이
/// 낙법 훈련을 정확히 관통했다. 플레이어는 이걸 3단 체인으로 읽고
/// "낙법을 안 샀는데 밀착 등반이 사진다"를 버그로 신고했다. 선행 검사는 정상이었다.
///
/// 좌표를 옮기면 트리 폭이 43% 늘어나므로, 선이 노드를 피해 가도록 고쳤다.
/// 이 테스트는 노드를 추가·이동했을 때 그 관통이 다시 생기는지 잡는다.
/// </summary>
public class UpgradeTreeLineRoutingTests
{
    private const string NodeDir = "Assets/GameData/UpgradeData/Node";

    // UpgradeOverlayUI의 SerializeField 기본값. 인스펙터에서 바꿨다면 여기도 맞춰야 한다.
    private const float GraphScale = 1.5f;
    private const float NodeWidth = 132f;
    private const float NodeHeight = 146f;
    private const float ObstaclePad = 12f;
    private const float TreePadHorizontal = 100f;
    private const float TrunkGap = 22f;
    private static float DetourLane => NodeWidth * 0.5f + 46f;
    private static float Clearance => NodeHeight * 0.5f + TrunkGap;

    private static List<UpgradeNodeSO> LoadNodes()
    {
        var list = new List<UpgradeNodeSO>();
        foreach (string guid in AssetDatabase.FindAssets("t:UpgradeNodeSO", new[] { NodeDir }))
        {
            var so = AssetDatabase.LoadAssetAtPath<UpgradeNodeSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null) list.Add(so);
        }
        Assert.Greater(list.Count, 0, $"{NodeDir}에 노드 에셋이 없다 — 트리 생성기를 실행했는가?");
        return list;
    }

    /// <summary>UpgradeOverlayUI의 Xmap/Ymap과 같은 매핑.</summary>
    private static float CenterY(List<UpgradeNodeSO> nodes)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var n in nodes)
        {
            min = Mathf.Min(min, n.uiPosition.y);
            max = Mathf.Max(max, n.uiPosition.y);
        }
        return (min + max) * 0.5f;
    }

    private static Vector2 Map(UpgradeNodeSO n, float centerY)
        => new Vector2(n.uiPosition.x * GraphScale, (n.uiPosition.y - centerY) * GraphScale);

    /// <summary>
    /// UpgradeOverlayUI가 EnsureTreeBuilt에서 만드는 것과 같은 라우터 — 레인 인덱스 + 허브 거리.
    /// 실제로 그려지는 경로를 보려면 허브까지 같이 채워야 한다(허브가 레인 규칙보다 먼저다).
    /// </summary>
    private static UpgradeLaneRouter BuildRouter(List<UpgradeNodeSO> nodes, float centerY,
                                                 out List<UpgradeLaneRouter.Link> links)
    {
        var mapped = new List<Vector2>(nodes.Count);
        foreach (var n in nodes) mapped.Add(Map(n, centerY));

        var router = new UpgradeLaneRouter();
        router.Build(mapped);

        var childCount = new Dictionary<UpgradeNodeSO, int>();
        foreach (var child in nodes)
        {
            if (child.parentNodes == null) continue;
            foreach (var parent in child.parentNodes)
            {
                if (parent == null) continue;
                childCount.TryGetValue(parent, out int c);
                childCount[parent] = c + 1;
            }
        }

        links = new List<UpgradeLaneRouter.Link>();
        foreach (var child in nodes)
        {
            if (child.parentNodes == null) continue;
            bool merges = IsMergePoint(child);
            foreach (var parent in child.parentNodes)
            {
                if (parent == null) continue;
                childCount.TryGetValue(parent, out int kids);
                links.Add(new UpgradeLaneRouter.Link(
                    parent.nodeId, Map(parent, centerY), child.nodeId, Map(child, centerY),
                    kids > 1, merges));
            }
        }

        router.BuildHubs(links, Clearance);
        return router;
    }

    /// <summary>UpgradeOverlayUI.IsMergePoint와 같은 판정 — 부모가 둘 이상인 합류 노드인가.</summary>
    private static bool IsMergePoint(UpgradeNodeSO child)
    {
        if (child == null || child.parentNodes == null) return false;

        int parents = 0;
        foreach (var p in child.parentNodes)
        {
            if (p == null) continue;
            if (++parents > 1) return true;
        }
        return false;
    }

    private static List<Rect> BuildObstacles(List<UpgradeNodeSO> nodes, float centerY)
    {
        float halfW = NodeWidth * 0.5f + ObstaclePad;
        float halfH = NodeHeight * 0.5f + ObstaclePad;

        var rects = new List<Rect>(nodes.Count);
        foreach (var n in nodes)
        {
            Vector2 c = Map(n, centerY);
            rects.Add(new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f));
        }
        return rects;
    }

    [Test]
    public void EveryConnection_AvoidsUnrelatedNodes()
    {
        var nodes = LoadNodes();
        float centerY = CenterY(nodes);
        var obstacles = BuildObstacles(nodes, centerY);
        var router = BuildRouter(nodes, centerY, out var links);

        var offenders = new List<string>();
        foreach (var link in links)
        {
            var path = OrthogonalUILineRenderer.BuildPath(link.Start, link.End, obstacles, DetourLane,
                                                         router.JogY(link, Clearance));

            if (OrthogonalUILineRenderer.IsBlocked(path, obstacles, link.Start, link.End))
                offenders.Add($"{link.ParentId} → {link.ChildId}");
        }

        Assert.IsEmpty(offenders,
            "연결선이 다른 노드를 관통한다 — 그 노드가 선행 단계로 오인된다:\n  " + string.Join("\n  ", offenders));
    }

    [Test]
    public void DetouredLines_StayInsideContentWidth()
    {
        // 우회선이 콘텐츠 폭을 넘어가면 스크롤 영역 밖으로 잘린다.
        // 폭은 UpgradeOverlayUI가 노드 좌표만 보고 잡으므로 선은 계산에 안 들어간다.
        var nodes = LoadNodes();
        float centerY = CenterY(nodes);
        var obstacles = BuildObstacles(nodes, centerY);
        var router = BuildRouter(nodes, centerY, out var links);

        float maxAbsX = 0f;
        foreach (var n in nodes) maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(n.uiPosition.x));
        float halfContent = maxAbsX * GraphScale + NodeWidth * 0.5f + TreePadHorizontal;

        foreach (var link in links)
        {
            foreach (var p in OrthogonalUILineRenderer.BuildPath(link.Start, link.End, obstacles, DetourLane,
                                                                router.JogY(link, Clearance)))
            {
                Assert.LessOrEqual(Mathf.Abs(p.x), halfContent,
                    $"{link.ParentId} → {link.ChildId} 우회선이 콘텐츠 폭({halfContent})을 벗어난다");
            }
        }
    }

    [Test]
    public void HandDrawnBends_AreUsedAsDrawn()
    {
        // 편집기에서 그린 꺾임점은 그대로 쓰여야 한다.
        // 자동 경로를 먼저 보면 안 막힌 자동 경로가 늘 이겨서 지정이 영영 안 먹는다.
        Vector2 s = new Vector2(0f, 0f);
        Vector2 e = new Vector2(300f, 900f);
        var bends = new List<Vector2>
        {
            new Vector2(-240f, 300f),   // 300 높이에서 -240 레인으로 건너간다
            new Vector2(300f, 700f),    // 700 높이에서 자식 레인으로 돌아온다
        };

        var path = OrthogonalUILineRenderer.BuildBendPath(s, e, bends);

        Assert.AreEqual(6, path.Count, "꺾임점 2개면 꺾인점은 6개다");
        bool ridesLane = false;
        for (int i = 1; i < path.Count; i++)
            if (Mathf.Approximately(path[i - 1].x, -240f) && Mathf.Approximately(path[i].x, -240f) &&
                Mathf.Abs(path[i].y - path[i - 1].y) > 1f) ridesLane = true;
        Assert.IsTrue(ridesLane, "세로 구간이 그린 레인(-240)을 타지 않는다");
        Assert.AreEqual(e.x, path[path.Count - 1].x, 0.01f, "마지막은 자식 레인으로 들어와야 한다");
        Assert.AreEqual(e.x, path[path.Count - 2].x, 0.01f, "자식 앞 세로 구간이 자식 레인에 없다");
    }

    [Test]
    public void HandDrawnBends_LastBendIsForcedOntoTheChildLane()
    {
        // 마지막 꺾임점의 x를 엉뚱하게 적어도 자식 레인으로 끌어당긴다 —
        // 안 그러면 선이 노드 옆 허공에서 끝난다.
        Vector2 s = new Vector2(0f, 0f);
        Vector2 e = new Vector2(300f, 900f);
        var bends = new List<Vector2> { new Vector2(-999f, 700f) };

        var path = OrthogonalUILineRenderer.BuildBendPath(s, e, bends);

        Assert.AreEqual(e.x, path[path.Count - 2].x, 0.01f);
        foreach (var p in path) Assert.AreNotEqual(-999f, p.x, "쓰이지 않아야 할 x가 경로에 남았다");
    }

    [Test]
    public void HandDrawnBends_FallBackWhenTheyHitANode()
    {
        // 그린 모양이 남의 노드를 관통하면 자동 경로로 떨어진다 — 선이 사라지진 않는다.
        Vector2 s = new Vector2(0f, 0f);
        Vector2 e = new Vector2(300f, 900f);
        var bends = new List<Vector2> { new Vector2(-240f, 300f), new Vector2(300f, 700f) };
        var blockers = new List<Rect> { new Rect(-320f, 350f, 160f, 200f) };   // -240 레인 한가운데

        var path = OrthogonalUILineRenderer.BuildPath(s, e, blockers, DetourLane,
                                                     trunkY: 700f, bends: bends);

        Assert.IsFalse(OrthogonalUILineRenderer.IsBlocked(path, blockers, s, e),
            "그린 모양이 막혔는데도 관통하는 경로를 돌려줬다");
    }

    [Test]
    public void SiblingBetweenParentAndChild_IsRoutedAround()
    {
        // 신고된 그 조합. 좌표를 바꾸더라도 이 관계가 유지되는 한 관통은 없어야 한다.
        // 여기서는 일부러 trunkY를 주지 않는다 — 레인 라우팅이 못 풀었을 때 떨어지는
        // 우회 사다리 자체가 살아있는지 검사하는 자리다(실제 경로는 위 스윕 테스트가 본다).
        var byId = new Dictionary<string, UpgradeNodeSO>();
        var nodes = LoadNodes();
        foreach (var n in nodes) byId[n.nodeId] = n;

        foreach (string id in new[] { "ClimbSpeed_T0_01", "FallDamage_T0_01", "WallClimbSpeed_T0_01" })
            Assert.IsTrue(byId.ContainsKey(id), $"{id} 노드가 없다");

        float centerY = CenterY(nodes);
        var obstacles = BuildObstacles(nodes, centerY);

        Vector2 s = Map(byId["ClimbSpeed_T0_01"], centerY);
        Vector2 e = Map(byId["WallClimbSpeed_T0_01"], centerY);
        var path = OrthogonalUILineRenderer.BuildPath(s, e, obstacles, DetourLane);

        Assert.Greater(path.Count, 4, "직선 그대로다 — 낙법 훈련 I을 우회하지 않았다");
        Assert.IsFalse(OrthogonalUILineRenderer.IsBlocked(path, obstacles, s, e));
    }
}
