using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GameScripts.EditorTools
{
    /// <summary>
    /// 소스 프리팹에는 있고 타겟 프리팹에는 없는 것을 타겟으로 이식한다.
    ///  1) 빠진 컴포넌트(설정값 포함)를 대응 오브젝트에 추가 (기존 컴포넌트는 유지)
    ///  2) (옵션) 빠진 자식 오브젝트를 소스에서 통째로 복제 생성
    ///  3) (옵션) 붙여넣은/복제한 컴포넌트의 "계층 내 오브젝트 참조"를 이름-경로로 자동 재매핑
    ///
    /// 본(rig)/자식 이름이 서로 같을수록 자동 재매핑 성공률이 높다.
    /// 외부 에셋 참조(머티리얼·스프라이트·다른 프리팹 등)는 GUID 라 항상 그대로 유지된다.
    /// </summary>
    public class PrefabComponentCopier : EditorWindow
    {
        private GameObject _source;   // 완전본 (기준)
        private GameObject _target;   // 이식 대상
        private bool _recurseChildren = true;       // 이름 매칭 자식의 컴포넌트도 채움
        private bool _createMissingChildren = true; // 소스에만 있는 자식 오브젝트를 복제 생성
        private bool _autoRemap = true;             // 계층 참조 이름-경로 자동 재매핑
        private Vector2 _scroll;
        private string _report = "";

        [MenuItem("Tools/Prefab/Copy Missing Components...")]
        private static void Open()
        {
            var win = GetWindow<PrefabComponentCopier>("Prefab Component Copier");
            win.minSize = new Vector2(480, 460);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Source(완전본)에는 있고 Target에는 없는 컴포넌트/자식오브젝트를 Target 프리팹에 이식합니다.\n" +
                "기존 것은 유지합니다. .prefab 에셋을 드래그하세요.",
                MessageType.Info);

            EditorGUILayout.Space();
            _source = (GameObject)EditorGUILayout.ObjectField("Source (기준/완전본)", _source, typeof(GameObject), false);
            _target = (GameObject)EditorGUILayout.ObjectField("Target (채울 대상)", _target, typeof(GameObject), false);

            EditorGUILayout.Space();
            _recurseChildren = EditorGUILayout.Toggle("자식 컴포넌트까지 처리(이름 매칭)", _recurseChildren);
            _createMissingChildren = EditorGUILayout.Toggle("빠진 자식 오브젝트 복제 생성", _createMissingChildren);
            _autoRemap = EditorGUILayout.Toggle("계층 참조 자동 재매핑", _autoRemap);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!IsValid()))
            {
                if (GUILayout.Button("1. 분석 (미리보기)", GUILayout.Height(30)))
                    Run(dryRun: true);

                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("2. 복사 실행", GUILayout.Height(30)))
                    Run(dryRun: false);
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(string.IsNullOrEmpty(_report) ? "(아직 실행 안 함)" : _report,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private bool IsValid()
        {
            if (_source == null || _target == null || _source == _target) return false;
            return PrefabUtility.IsPartOfPrefabAsset(_source) && PrefabUtility.IsPartOfPrefabAsset(_target);
        }

        private void Run(bool dryRun)
        {
            string sourcePath = AssetDatabase.GetAssetPath(_source);
            string targetPath = AssetDatabase.GetAssetPath(_target);
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath))
            {
                _report = "프리팹 에셋 경로를 찾을 수 없습니다. .prefab 에셋을 드래그했는지 확인하세요.";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Source: {sourcePath}");
            sb.AppendLine($"Target: {targetPath}");
            sb.AppendLine($"모드  : {(dryRun ? "분석(미리보기)" : "복사 실행")}");
            sb.AppendLine($"옵션  : 자식컴포넌트={_recurseChildren}, 자식생성={_createMissingChildren}, 자동재매핑={_autoRemap}");
            sb.AppendLine("--------------------------------------------------");

            GameObject srcRoot = PrefabUtility.LoadPrefabContents(sourcePath);
            GameObject tgtRoot = PrefabUtility.LoadPrefabContents(targetPath);

            int copiedComp = 0, createdObj = 0, remappedRefs = 0;
            var unresolved = new List<string>();
            var toRemap = new List<Component>();   // 재매핑 대상 컴포넌트

            try
            {
                // 타겟 원본 상태를 경로→Transform 으로 인덱싱 (루트는 "")
                var tgtMap = new Dictionary<string, Transform> { [""] = tgtRoot.transform };
                foreach (var t in tgtRoot.GetComponentsInChildren<Transform>(true))
                    if (t != tgtRoot.transform)
                        tgtMap[GetRelativePath(tgtRoot.transform, t)] = t;

                // 소스 전체 오브젝트 (얕은 것부터)
                var srcAll = srcRoot.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != srcRoot.transform)
                    .OrderBy(t => GetRelativePath(srcRoot.transform, t).Count(c => c == '/'))
                    .ToList();

                // ── 1) 빠진 자식 오브젝트 복제 생성 ─────────────────────────────
                if (_createMissingChildren)
                {
                    sb.AppendLine("[자식 오브젝트]");
                    foreach (var srcTr in srcAll)
                    {
                        string path = GetRelativePath(srcRoot.transform, srcTr);
                        if (tgtMap.ContainsKey(path)) continue;               // 이미 있음

                        string parentPath = ParentPath(path);
                        if (!tgtMap.TryGetValue(parentPath, out var tgtParent))
                            continue; // 부모가 아직 없음 → 부모가 이번 루프에서 먼저 생성되며 서브트리로 포함됨

                        sb.AppendLine($"    + 생성: {path}");
                        if (!dryRun)
                        {
                            var newGo = Object.Instantiate(srcTr.gameObject);
                            newGo.name = srcTr.name;
                            newGo.transform.SetParent(tgtParent, false);
                            newGo.transform.localPosition = srcTr.localPosition;
                            newGo.transform.localRotation = srcTr.localRotation;
                            newGo.transform.localScale    = srcTr.localScale;

                            // 새 서브트리를 맵에 등록 + 재매핑 대상에 추가
                            foreach (var nt in newGo.GetComponentsInChildren<Transform>(true))
                            {
                                tgtMap[GetRelativePath(tgtRoot.transform, nt)] = nt;
                                toRemap.AddRange(nt.GetComponents<Component>().Where(c => c != null && !(c is Transform)));
                            }
                        }
                        else
                        {
                            // 미리보기: 생성될 것으로 간주하고 맵에 등록
                            tgtMap[path] = null;
                        }
                        createdObj++;
                    }
                    if (createdObj == 0) sb.AppendLine("    (없음)");
                }

                // ── 2) 빠진 컴포넌트 복사 ──────────────────────────────────────
                sb.AppendLine("[컴포넌트]");
                var srcScope = new List<Transform> { srcRoot.transform };
                if (_recurseChildren) srcScope.AddRange(srcAll);

                foreach (var srcTr in srcScope)
                {
                    string relPath = GetRelativePath(srcRoot.transform, srcTr);
                    bool isRoot = srcTr == srcRoot.transform;
                    string label = isRoot ? "[ROOT]" : relPath;

                    // 이 소스 오브젝트에 대응하는 "원래 존재하던" 타겟 오브젝트만 컴포넌트 이식 대상
                    // (새로 복제 생성한 오브젝트는 이미 컴포넌트를 다 갖고 있음)
                    Transform tgtTr = isRoot ? tgtRoot.transform
                                             : (tgtMap.TryGetValue(relPath, out var t) ? t : null);
                    if (tgtTr == null) continue; // 방금 생성됐거나(=이미 완비) 대응 없음

                    var tgtTypes = new HashSet<System.Type>(
                        tgtTr.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType()));

                    var missing = srcTr.GetComponents<Component>()
                        .Where(c => c != null && !(c is Transform) && !tgtTypes.Contains(c.GetType()))
                        .ToList();
                    if (missing.Count == 0) continue;

                    sb.AppendLine($"● {label}");
                    foreach (var comp in missing)
                    {
                        sb.AppendLine($"    + {comp.GetType().Name}");
                        if (!dryRun)
                        {
                            var before = tgtTr.GetComponents<Component>();
                            if (ComponentUtility.CopyComponent(comp) &&
                                ComponentUtility.PasteComponentAsNew(tgtTr.gameObject))
                            {
                                copiedComp++;
                                var pasted = tgtTr.GetComponents<Component>().Except(before).FirstOrDefault();
                                if (pasted != null) toRemap.Add(pasted);
                            }
                            else sb.AppendLine($"      ✗ 복사 실패: {comp.GetType().Name}");
                        }
                    }
                }

                // ── 3) 계층 참조 자동 재매핑 ───────────────────────────────────
                if (_autoRemap && !dryRun)
                {
                    foreach (var comp in toRemap)
                        RemapComponent(comp, srcRoot.transform, tgtRoot.transform, tgtMap, ref remappedRefs, unresolved);
                }
                else if (_autoRemap && dryRun)
                {
                    // 미리보기: 어떤 참조가 해결/미해결 될지 예측
                    foreach (var srcTr in srcScope)
                    {
                        Transform tgtTr = srcTr == srcRoot.transform ? tgtRoot.transform
                            : (tgtMap.ContainsKey(GetRelativePath(srcRoot.transform, srcTr)) ? srcTr : null);
                        if (tgtTr == null) continue;
                        // 소스 컴포넌트 참조를 검사해 미해결 예상 항목만 수집
                        foreach (var comp in srcTr.GetComponents<Component>().Where(c => c != null && !(c is Transform)))
                            PreviewRefs(comp, srcRoot.transform, tgtMap, unresolved);
                    }
                }

                if (!dryRun)
                {
                    PrefabUtility.SaveAsPrefabAsset(tgtRoot, targetPath);
                    AssetDatabase.SaveAssets();
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(srcRoot);
                PrefabUtility.UnloadPrefabContents(tgtRoot);
            }

            sb.AppendLine("--------------------------------------------------");
            if (dryRun)
            {
                sb.AppendLine($"분석 완료 → 자식 생성 {createdObj}개, 컴포넌트 추가 예정.");
                if (unresolved.Count > 0)
                {
                    sb.AppendLine($"\n⚠ 자동 재매핑 불가 예상 참조 {unresolved.Distinct().Count()}건 (수동 연결 필요):");
                    foreach (var u in unresolved.Distinct()) sb.AppendLine($"    - {u}");
                }
                else sb.AppendLine("자동 재매핑으로 모든 계층 참조가 해결될 것으로 보입니다.");
                sb.AppendLine("\n'2. 복사 실행'을 누르세요.");
            }
            else
            {
                sb.AppendLine($"완료 → 자식 {createdObj}개 생성, 컴포넌트 {copiedComp}개 추가, 참조 {remappedRefs}건 재매핑.");
                if (unresolved.Count > 0)
                {
                    sb.AppendLine($"\n⚠ 재매핑 실패 {unresolved.Distinct().Count()}건 (Inspector 수동 연결):");
                    foreach (var u in unresolved.Distinct()) sb.AppendLine($"    - {u}");
                }
                else sb.AppendLine("모든 계층 참조가 자동 연결되었습니다.");
            }

            _report = sb.ToString();
            Debug.Log(_report);
        }

        // ── 참조 재매핑 ────────────────────────────────────────────────────
        private static void RemapComponent(Component comp, Transform srcRoot, Transform tgtRoot,
            Dictionary<string, Transform> tgtMap, ref int remapped, List<string> unresolved)
        {
            if (comp == null || comp is Transform) return;
            var so = new SerializedObject(comp);
            var it = so.GetIterator();
            bool changed = false;
            while (it.NextVisible(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (it.propertyPath == "m_GameObject") continue;
                var val = it.objectReferenceValue;
                if (val == null) continue;

                Transform srcTr = val is GameObject go ? go.transform : (val is Component c ? c.transform : null);
                if (srcTr == null) continue;                 // 에셋 참조 → 유지
                if (srcTr != srcRoot && !srcTr.IsChildOf(srcRoot)) continue; // 외부 → 유지

                string path = GetRelativePath(srcRoot, srcTr);
                string owner = $"{comp.GetType().Name}.{it.propertyPath} → {(path == "" ? "[ROOT]" : path)}";

                if (!tgtMap.TryGetValue(path, out var tgtTr) || tgtTr == null)
                {
                    unresolved.Add(owner);
                    continue;
                }

                Object newVal = null;
                if (val is GameObject) newVal = tgtTr.gameObject;
                else if (val is Component srcComp) newVal = tgtTr.GetComponent(srcComp.GetType());

                if (newVal != null) { it.objectReferenceValue = newVal; changed = true; remapped++; }
                else unresolved.Add(owner + " (컴포넌트 타입 없음)");
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void PreviewRefs(Component comp, Transform srcRoot,
            Dictionary<string, Transform> tgtMap, List<string> unresolved)
        {
            var so = new SerializedObject(comp);
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (it.propertyPath == "m_GameObject") continue;
                var val = it.objectReferenceValue;
                if (val == null) continue;
                Transform srcTr = val is GameObject go ? go.transform : (val is Component c ? c.transform : null);
                if (srcTr == null) continue;
                if (srcTr != srcRoot && !srcTr.IsChildOf(srcRoot)) continue;
                string path = GetRelativePath(srcRoot, srcTr);
                if (!tgtMap.ContainsKey(path))
                    unresolved.Add($"{comp.GetType().Name}.{it.propertyPath} → {(path == "" ? "[ROOT]" : path)}");
            }
        }

        // ── 경로 유틸 ──────────────────────────────────────────────────────
        private static string GetRelativePath(Transform root, Transform t)
        {
            if (t == root) return "";
            var stack = new Stack<string>();
            while (t != null && t != root) { stack.Push(t.name); t = t.parent; }
            return string.Join("/", stack);
        }

        private static string ParentPath(string path)
        {
            int i = path.LastIndexOf('/');
            return i < 0 ? "" : path.Substring(0, i);
        }
    }
}
