// @tags: upgrade, tree, editor, tool, generator, node, cost, economy, balance
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 기획된 대규모 업그레이드 트리를 원클릭으로 에셋 생성 및 자동 링킹을 수행하는 에디터 윈도우입니다.
/// </summary>
public class UpgradeTreeGenerator : EditorWindow
{
    // 기본은 증분 갱신이다. 켜면 전부 지우고 새로 만드는 옛 방식으로 돌아간다 —
    // GUID가 새로 발급되므로 다른 에셋의 참조와 인스펙터에서 물린 icon이 전부 끊긴다.
    // 에셋이 꼬였을 때의 마지막 수단으로만 쓴다.
    private bool _forceRecreate = false;
    private string _log = "";

    [MenuItem("Tools/Upgrade/Upgrade Tree Generator")]
    public static void ShowWindow()
    {
        GetWindow<UpgradeTreeGenerator>("Upgrade Tree Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("대규모 업그레이드 트리 자동 생성기", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "바뀐 노드만 갱신한다. 손대지 않은 에셋은 파일도 건드리지 않으므로 " +
            "UVCS에 변경으로 잡히지 않고, GUID와 인스펙터에서 물린 아이콘이 유지된다.",
            MessageType.Info);

        _forceRecreate = EditorGUILayout.Toggle("전체 삭제 후 재생성 (GUID 새로 발급)", _forceRecreate);
        if (_forceRecreate)
        {
            EditorGUILayout.HelpBox(
                "모든 노드·효과 에셋의 GUID가 바뀐다. 이 에셋들을 참조하는 곳과 " +
                "인스펙터에서 지정한 아이콘이 전부 끊긴다. 에셋이 꼬였을 때만 쓸 것.",
                MessageType.Warning);
        }
        EditorGUILayout.Space();

        if (GUILayout.Button("업그레이드 트리 자동 생성 및 링킹 실행", GUILayout.Height(40)))
        {
            GenerateUpgradeTree();
        }

        EditorGUILayout.Space();
        GUILayout.Label("작업 로그:", EditorStyles.boldLabel);
        
        // 스크롤 가능한 로그 영역
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        if (string.IsNullOrEmpty(_log))
        {
            GUILayout.Label("대기 중...");
        }
        else
        {
            GUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
        }
        EditorGUILayout.EndVertical();
    }

    private void GenerateUpgradeTree()
    {
        _log = "업그레이드 트리 생성 시작...\n";

        // 폴더 경로 정의
        string baseDir = "Assets/GameData/UpgradeData";
        string effectDir = $"{baseDir}/Effect";
        string nodeDir = $"{baseDir}/Node";
        string treePath = $"{baseDir}/_UpgradeTree.asset";

        // 폴더 검증 및 생성
        EnsureFolderExists("Assets/GameData");
        EnsureFolderExists(baseDir);
        EnsureFolderExists(effectDir);
        EnsureFolderExists(nodeDir);

        if (_forceRecreate)
        {
            _log += "[전체 재생성] 기존 효과 및 노드 에셋을 삭제합니다 — GUID가 새로 발급됩니다.\n";
            DeleteAssetsInFolder(effectDir);
            DeleteAssetsInFolder(nodeDir);
        }

        // 1. 기획 명세 데이터 — 원본은 UpgradeTree.csv다 (C# 리터럴이 아니다).
        //    설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §4
        var csvErrors = new List<string>();
        List<UpgradeNodeData> planData =
            UpgradeTreeCsvReader.Read(UpgradeTreeCsvReader.DefaultPath, csvErrors);

        foreach (string e in csvErrors) _log += $"  [CSV 오류] {e}\n";

        // 빈 목록에서 반드시 멈춘다. 그냥 진행하면 아래 Pass 4(고아 정리)가
        // 남아 있는 에셋을 전부 '기획에 없는 것'으로 판단해 통째로 지운다.
        if (planData.Count == 0)
        {
            _log += $"\n★ 중단 — {UpgradeTreeCsvReader.DefaultPath} 에서 노드를 하나도 못 읽었다.\n" +
                    "  에셋을 건드리지 않았다. 파일 경로와 헤더를 확인할 것.\n";
            return;
        }

        _log += $"총 {planData.Count}개의 노드를 CSV에서 로드했습니다.\n";

        var nodes = new Dictionary<string, UpgradeNodeSO>();
        var wantedPaths = new HashSet<string>();
        int created = 0, changed = 0, untouched = 0;

        // 2. Pass 1: 효과·노드 에셋을 '있으면 고치고 없으면 만든다'
        //    통째로 다시 만들면 매번 GUID가 바뀌어 참조가 끊기고, 손대지 않은
        //    노드까지 UVCS에 변경으로 잡힌다. 그래서 필드 단위로 비교한다.
        _log += "\n[Pass 1] 효과·노드 에셋 갱신...\n";
        foreach (var data in planData)
        {
            string effectPath = $"{effectDir}/Eff_{data.nodeId}.asset";
            string nodePath = $"{nodeDir}/Node_{data.nodeId}.asset";
            wantedPaths.Add(effectPath);
            wantedPaths.Add(nodePath);

            // A. 효과
            bool effectIsNew = false;
            var effect = AssetDatabase.LoadAssetAtPath<UpgradeEffectSO>(effectPath);
            if (effect == null)
            {
                effect = ScriptableObject.CreateInstance<UpgradeEffectSO>();
                AssetDatabase.CreateAsset(effect, effectPath);
                effectIsNew = true;
            }

            bool effectDirty = false;
            effectDirty |= Assign(ref effect.type, data.effectType);
            effectDirty |= Assign(ref effect.value, data.effectValue);
            effectDirty |= Assign(ref effect.isPercentage, data.isPercentage);
            effectDirty |= Assign(ref effect.targetMineral, data.targetMineral);
            // 대상이 여럿인 노드(예: 재활용 계약 = 쓰레기봉지 + 페트병).
            // 리스트는 Assign(값 비교)으로 안 되므로 내용 비교 후 통째로 갈아 끼운다.
            if (!SameMinerals(effect.extraTargetMinerals, data.extraTargets))
            {
                effect.extraTargetMinerals = new System.Collections.Generic.List<MineralID>(data.extraTargets);
                effectDirty = true;
            }
            // 새 에셋도 반드시 dirty로 표시한다 — CreateAsset은 기본값 상태로 파일을 쓰고
            // 필드는 그 뒤에 채우므로, 표시하지 않으면 채운 값이 저장되지 않는다.
            if (effectDirty) EditorUtility.SetDirty(effect);

            // B. 노드 — icon은 CSV의 `icon` 칸이 **비어 있을 때만** 인스펙터 소유다.
            //    경로가 적혀 있으면 그 스프라이트로 맞춘다(AssignIcon).
            //    아이콘 파일이 아직 없어서 지금은 전 행이 빈 칸이고, 그래서 지금까지의
            //    동작(인스펙터에서 물린 값 유지)이 그대로 남는다.
            //
            //    maxLevel은 반대로 항상 1로 되돌린다. 2026-08-21에 다단계 강화를 접고
            //    "노드를 두 개 두는" 방식으로 정했으므로, 에셋에 남은 maxLevel>1은
            //    폐기된 설계의 잔재다. 인스펙터 소유로 두면 CSV(단일 원본)에 없는 값이
            //    조용히 살아남아 가격 계산(CostForLevel)만 다르게 돌아간다.
            bool nodeIsNew = false;
            var node = AssetDatabase.LoadAssetAtPath<UpgradeNodeSO>(nodePath);
            if (node == null)
            {
                node = ScriptableObject.CreateInstance<UpgradeNodeSO>();
                AssetDatabase.CreateAsset(node, nodePath);
                nodeIsNew = true;
            }

            bool nodeDirty = false;
            nodeDirty |= Assign(ref node.nodeId, data.nodeId);
            nodeDirty |= Assign(ref node.displayNameKey, data.displayNameKey);
            nodeDirty |= Assign(ref node.descriptionKey, data.descriptionKey);
            nodeDirty |= Assign(ref node.tier, data.tier);
            nodeDirty |= Assign(ref node.cost, data.cost);
            nodeDirty |= Assign(ref node.uiPosition, data.uiPosition);
            nodeDirty |= Assign(ref node.effect, effect);
            nodeDirty |= Assign(ref node.maxLevel, 1);   // 다단계 폐기 — 위 주석 참고
            if (node.levelCosts != null && node.levelCosts.Count > 0)
            {
                node.levelCosts.Clear();                 // maxLevel 1에서는 읽히지 않는 죽은 값
                nodeDirty = true;
            }
            if (node.parentNodes == null) { node.parentNodes = new List<UpgradeNodeSO>(); nodeDirty = true; }
            nodeDirty |= AssignLineBends(node, data.lineBends);
            nodeDirty |= AssignIcon(node, data.iconPath);
            if (nodeDirty) EditorUtility.SetDirty(node);

            nodes.Add(data.nodeId, node);

            if (nodeIsNew || effectIsNew) { created++; _log += $"  + 생성 {data.nodeId}\n"; }
            else if (nodeDirty || effectDirty) { changed++; _log += $"  ~ 수정 {data.nodeId}\n"; }
            else untouched++;
        }

        // 3. Pass 2: 선행 노드 링킹
        //    누적을 막으려면 매번 새로 만들어 비교해야 한다. 기존 리스트에 Add만 하면
        //    두 번째 실행부터 부모가 중복으로 쌓인다.
        _log += "\n[Pass 2] 선행 노드 연결...\n";
        int relinked = 0;
        foreach (var data in planData)
        {
            var node = nodes[data.nodeId];
            var desired = new List<UpgradeNodeSO>();

            if (data.parentIds != null)
            {
                foreach (string parentId in data.parentIds)
                {
                    if (nodes.TryGetValue(parentId, out UpgradeNodeSO parent))
                        desired.Add(parent);
                    else
                        _log += $"  [경고] 선행 노드 '{parentId}'를 찾을 수 없습니다! ({data.nodeId})\n";
                }
            }

            if (!SameList(node.parentNodes, desired))
            {
                node.parentNodes = desired;
                EditorUtility.SetDirty(node);
                relinked++;
                _log += $"  ~ 선행 갱신 {data.nodeId} ({desired.Count}개)\n";
            }
        }
        if (relinked == 0) _log += "  변경 없음\n";

        // 4. Pass 3: 트리 컨테이너
        _log += "\n[Pass 3] UpgradeTreeSO 컨테이너...\n";
        var tree = AssetDatabase.LoadAssetAtPath<UpgradeTreeSO>(treePath);
        bool treeIsNew = tree == null;
        if (treeIsNew)
        {
            tree = ScriptableObject.CreateInstance<UpgradeTreeSO>();
            AssetDatabase.CreateAsset(tree, treePath);
            _log += $"  + 생성 {treePath}\n";
        }

        var allNodes = new List<UpgradeNodeSO>();
        var rootNodes = new List<UpgradeNodeSO>();
        foreach (var data in planData)
        {
            allNodes.Add(nodes[data.nodeId]);
            if (data.parentIds == null || data.parentIds.Length == 0)
                rootNodes.Add(nodes[data.nodeId]);
        }

        var tiers = new List<TierInfo>()
            {
                new TierInfo { tierIndex = 0, tierNameKey = "첫 번째 땅", unlockConditionTextKey = "채광레벨 0 달성" },
                new TierInfo { tierIndex = 1, tierNameKey = "두 번째 땅", unlockConditionTextKey = "채광레벨 1 달성" },
                new TierInfo { tierIndex = 2, tierNameKey = "세 번째 땅", unlockConditionTextKey = "채광레벨 2 달성" }
            };

        // 지층 띠의 윗변을 손으로 정해 뒀으면 얹는다. 안 적혀 있으면 그대로 자동.
        var bandErrors = new List<string>();
        var bands = UpgradeTierBandCsvReader.Read(UpgradeTierBandCsvReader.DefaultPath, bandErrors);
        foreach (string e in bandErrors)
            Debug.LogWarning($"[UpgradeTreeGenerator] 지층 경계: {e}");
        foreach (var t in tiers)
            if (bands.TryGetValue(t.tierIndex, out float top))
            {
                t.hasBandTop = true;
                t.bandTop = top;
            }

        bool treeDirty = false;
        if (!SameList(tree.allNodes, allNodes)) { tree.allNodes = allNodes; treeDirty = true; }
        if (!SameList(tree.rootNodes, rootNodes)) { tree.rootNodes = rootNodes; treeDirty = true; }
        if (!SameTiers(tree.tiers, tiers)) { tree.tiers = tiers; treeDirty = true; }

        if (treeDirty)
        {
            EditorUtility.SetDirty(tree);
            if (!treeIsNew) _log += $"  ~ 수정 (노드 {allNodes.Count}개 / 루트 {rootNodes.Count}개)\n";
        }
        else if (!treeIsNew)
        {
            _log += "  변경 없음\n";
        }

        // 5. 고아 에셋 정리 — 기획에서 빠진 노드의 파일이 남아 있으면 테스트가
        //    노드 수에서 걸린다(에셋을 읽어 세기 때문).
        _log += "\n[Pass 4] 기획에 없는 에셋 정리...\n";
        int deleted = DeleteOrphans(effectDir, wantedPaths) + DeleteOrphans(nodeDir, wantedPaths);
        if (deleted == 0) _log += "  없음\n";

        if (created + changed + relinked + deleted > 0 || treeDirty)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        _log += $"\n★ 완료 — 생성 {created} / 수정 {changed} / 선행갱신 {relinked} / 삭제 {deleted} / 그대로 {untouched}\n";
        if (created + changed + relinked + deleted == 0 && !treeDirty)
            _log += "  기획과 에셋이 이미 같습니다. 파일을 하나도 건드리지 않았습니다.\n";
    }

    // ===== 증분 갱신 도우미 =====

    /// <summary>값이 다를 때만 대입하고 true를 돌려준다. 같으면 파일을 건드리지 않는다.</summary>
    private static bool Assign<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        return true;
    }

    /// <summary>
    /// 사람이 지정한 연결선 모양을 CSV에서 에셋으로 옮긴다. 값이 같으면 안 건드린다.
    ///
    /// CSV가 정본이라 **CSV에서 지운 지정은 에셋에서도 사라져야** 한다 —
    /// 남겨 두면 편집기에서 자동으로 되돌렸는데 게임만 옛 모양으로 꺾인다.
    /// </summary>
    private static bool AssignLineBends(UpgradeNodeSO node, UpgradeLineBend[] data)
    {
        if (node.lineBends == null) node.lineBends = new List<UpgradeLineBend>();
        int n = data?.Length ?? 0;

        bool same = node.lineBends.Count == n;
        for (int i = 0; same && i < n; i++)
        {
            var a = node.lineBends[i];
            if (a == null || a.parentId != data[i].parentId ||
                a.points == null || a.points.Count != data[i].points.Count) { same = false; break; }
            for (int k = 0; k < a.points.Count; k++)
                if (a.points[k] != data[i].points[k]) { same = false; break; }
        }
        if (same) return false;

        node.lineBends.Clear();
        for (int i = 0; i < n; i++)
            node.lineBends.Add(new UpgradeLineBend(data[i].parentId, new List<Vector2>(data[i].points)));
        return true;
    }

    /// <summary>대상 광물 목록이 같은가(순서 포함). null·빈 목록은 같은 것으로 본다.</summary>
    private static bool SameMinerals(List<MineralID> a, MineralID[] b)
    {
        int an = a?.Count ?? 0;
        int bn = b?.Length ?? 0;
        if (an != bn) return false;
        for (int i = 0; i < an; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static bool SameList<T>(List<T> a, List<T> b) where T : class
    {
        if (a == null) return b == null || b.Count == 0;
        if (b == null) return a.Count == 0;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    /// <summary>
    /// CSV의 `icon` 경로를 노드에 물린다. 바꿨으면 true.
    ///
    /// 빈 칸은 "정하지 않았다"이지 "지우라"가 아니다 — 그대로 둔다. 안 그러면
    /// 열을 새로 판 순간 인스펙터에서 물려 둔 아이콘이 62개 전부 날아간다.
    /// 지우고 싶으면 인스펙터에서 비우면 된다.
    /// </summary>
    private bool AssignIcon(UpgradeNodeSO node, string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return false;

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath.Trim());
        if (sprite == null)
        {
            _log += $"  ! {node.nodeId}: icon '{iconPath}' 에서 스프라이트를 못 찾았다 — 그대로 둔다\n";
            return false;
        }
        if (node.icon == sprite) return false;
        node.icon = sprite;
        return true;
    }

    private static bool SameTiers(List<TierInfo> a, List<TierInfo> b)
    {
        if (a == null || b == null) return a == b;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] == null || b[i] == null) return false;
            if (a[i].tierIndex != b[i].tierIndex) return false;
            if (a[i].tierNameKey != b[i].tierNameKey) return false;
            if (a[i].unlockConditionTextKey != b[i].unlockConditionTextKey) return false;
            if (a[i].hasBandTop != b[i].hasBandTop) return false;
            if (a[i].hasBandTop && !Mathf.Approximately(a[i].bandTop, b[i].bandTop)) return false;
        }
        return true;
    }

    /// <summary>기획에 없는 .asset을 지운다. 반환값은 지운 개수.</summary>
    private int DeleteOrphans(string folderPath, HashSet<string> wantedPaths)
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("", new[] { folderPath }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".asset") || wantedPaths.Contains(path)) continue;

            AssetDatabase.DeleteAsset(path);
            _log += $"  - 삭제 {System.IO.Path.GetFileNameWithoutExtension(path)}\n";
            count++;
        }
        return count;
    }

    private void EnsureFolderExists(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            string parent = Path.GetDirectoryName(folderPath).Replace('\\', '/');
            string folderName = Path.GetFileName(folderPath);
            AssetDatabase.CreateFolder(parent, folderName);
            _log += $"폴더 생성: {folderPath}\n";
        }
    }

    private void DeleteAssetsInFolder(string folderPath)
    {
        string[] fileGuids = AssetDatabase.FindAssets("", new[] { folderPath });
        foreach (string guid in fileGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".asset"))
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
