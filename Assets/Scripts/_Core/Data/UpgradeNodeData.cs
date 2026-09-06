// @tags: upgrade, tree, node, data, csv
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 부모 하나에서 이 노드로 들어오는 선의 **모양을 사람이 지정한 것**.
///
/// 꺾임점 하나는 "이 높이(uiY)에서 가로로 건너가 이 레인(uiX)을 탄다"는 뜻이다.
/// 시작은 부모 레인에서 세로로 올라오고, 마지막 꺾임점은 반드시 자식 레인으로
/// 건너간다(그리기 시점에 강제한다) — 그래야 선이 노드 위/아래로 들어간다.
///
///   꺾임점 1개  →  Z자 (부모 레인 → 그 높이에서 건너감 → 자식 레인)
///   꺾임점 2개  →  S자 (중간에 다른 레인을 한 번 탄다)
///   그 이상     →  탄 레인 수만큼 계단
///
/// 좌표는 매핑 좌표가 아니라 <b>ui 좌표</b>다 — 매핑은 트리 전체의 중앙(centerY)
/// 기준이라, 노드를 하나만 옮겨도 모든 지정값이 통째로 어긋난다.
///
/// 비어 있으면 UpgradeLaneRouter가 규칙대로 정한다(자동).
/// 편집기(Tools/upgrade_tree_editor.html)에서 선을 더블클릭하면 꺾임점이 늘어난다.
/// </summary>
[System.Serializable]
public class UpgradeLineBend
{
    public string parentId;

    /// <summary>꺾임점 목록. x = 건너가서 탈 레인(uiX), y = 건너가는 높이(uiY).</summary>
    public List<Vector2> points = new List<Vector2>();

    public UpgradeLineBend() { }
    public UpgradeLineBend(string parent, List<Vector2> pts)
    {
        parentId = parent;
        points = pts ?? new List<Vector2>();
    }
}

/// <summary>
/// 업그레이드 노드 1행의 순수 데이터. UpgradeTree.csv 한 줄에 대응한다.
///
/// GameScripts(런타임) 어셈블리에 둔다 — Editor 어셈블리에 있으면
/// EditModeTests.asmdef가 참조하지 못해 테스트를 쓸 수 없다.
/// 런타임 코드는 이 타입을 쓰지 않지만, 그건 배치 이유가 아니라 결과일 뿐이다.
/// </summary>
public class UpgradeNodeData
{
    public string nodeId;
    public string displayNameKey;
    public string descriptionKey;
    public int tier;
    public int cost;
    public Vector2 uiPosition;
    public string[] parentIds;
    public UpgradeEffectType effectType;
    public float effectValue;
    public bool isPercentage;

    /// <summary>
    /// MineralPriceUp 전용 대상 광물. 그 외 효과에서는 None이다.
    /// 효과 타입만으로는 "어느 광물의 값을 올리는지"를 표현할 수 없어 따로 둔다.
    ///
    /// 여러 광물을 한 노드로 올릴 수 있다(CSV targetMineral 칸에 ';'로 나열).
    /// 첫 번째가 이 필드, 나머지가 <see cref="extraTargets"/>다 —
    /// 기존 소비처가 단일 필드만 보므로 첫 번째는 그대로 남긴다.
    /// </summary>
    public MineralID targetMineral = MineralID.None;

    /// <summary>두 번째 이후의 대상 광물. 대부분 비어 있다.</summary>
    public MineralID[] extraTargets = System.Array.Empty<MineralID>();

    /// <summary>
    /// true면 합성기(synth_tree.py)가 이 행의 값을 덮어쓰지 않는다.
    /// 사람이 손으로 확정한 고정점. 1단계에서는 전 행 false다.
    /// </summary>
    public bool locked;

    /// <summary>
    /// 사람이 지정한 연결선 모양(부모별). 비어 있으면 규칙대로 정한다.
    /// CSV `lineBends` 칸(`부모ID=x:y|x:y;부모ID=...`)에서 온다.
    /// </summary>
    public UpgradeLineBend[] lineBends = System.Array.Empty<UpgradeLineBend>();

    /// <summary>
    /// 노드 카드에 띄울 아이콘의 **에셋 경로**(`Assets/.../foo.png`). CSV `icon` 칸.
    ///
    /// **비어 있으면 생성기가 손대지 않는다** — 인스펙터에서 물려 둔 스프라이트가
    /// 그대로 남는다. 경로를 적은 노드만 CSV가 임자가 된다.
    /// 아이콘이 아예 없는 노드는 카드에서 아이콘 자리가 숨겨지고(UpgradeOverlayUI),
    /// 상세 패널은 효과 타입 색으로 대신 칠한다.
    /// </summary>
    public string iconPath = "";

    public UpgradeNodeData(
        string id, string name, string desc, int t, int c, Vector2 pos,
        string[] parents, UpgradeEffectType effType, float effVal, bool isPct,
        bool isLocked = false, MineralID target = MineralID.None,
        MineralID[] extraTargetMinerals = null)
    {
        nodeId = id;
        displayNameKey = name;
        descriptionKey = desc;
        tier = t;
        cost = c;
        uiPosition = pos;
        parentIds = parents;
        effectType = effType;
        effectValue = effVal;
        isPercentage = isPct;
        locked = isLocked;
        targetMineral = target;
        extraTargets = extraTargetMinerals ?? System.Array.Empty<MineralID>();
    }
}
