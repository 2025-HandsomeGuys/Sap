using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameScripts.EditorTools
{
    /// <summary>
    /// 로드된 씬 전체를 훑어, "플레이어를 가리켜야 하는데 비어있는(None) / Missing" 오브젝트 참조 필드를 뽑는다.
    /// 프리팹 교체로 끊긴 씬→플레이어 참조를 한 번에 찾을 때 사용.
    ///
    /// 판정 기준(하나라도 해당하면 후보):
    ///   1) Missing 참조 (가리키던 대상이 사라짐) — 가장 확실
    ///   2) 필드 타입이 플레이어 프리팹이 가진 컴포넌트 타입 (예: PlayerController, Digger …)
    ///   3) 필드 이름에 player/target/follow/lookat/owner 키워드 + 타입이 GameObject/Transform/플레이어컴포넌트
    /// </summary>
    public class MissingPlayerRefFinder : EditorWindow
    {
        private GameObject _playerPrefab;   // (선택) 타입 매칭용 — FanalPlayer 드래그
        private bool _includeInactive = true;
        private Vector2 _scroll;

        private static readonly string[] Keywords = { "player", "target", "follow", "lookat", "owner", "hero", "character" };

        private class Row
        {
            public GameObject go;
            public string sceneName;
            public string objPath;
            public string compType;
            public string field;
            public string fieldType;
            public bool missing;
            public int score;   // 정렬용 (높을수록 확실)
        }

        private readonly List<Row> _rows = new List<Row>();
        private HashSet<Type> _playerTypes = new HashSet<Type>();

        [MenuItem("Tools/Prefab/Find Missing Player Refs")]
        private static void Open()
        {
            var w = GetWindow<MissingPlayerRefFinder>("Missing Player Refs");
            w.minSize = new Vector2(560, 440);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "로드된 씬에서 '플레이어를 가리켜야 하는데 비어있는' 참조 필드를 찾습니다.\n" +
                "Player 프리팹을 넣으면 타입 매칭 정확도가 올라갑니다(선택).",
                MessageType.Info);

            _playerPrefab = (GameObject)EditorGUILayout.ObjectField("Player 프리팹 (선택)", _playerPrefab, typeof(GameObject), false);
            _includeInactive = EditorGUILayout.Toggle("비활성 오브젝트 포함", _includeInactive);

            if (GUILayout.Button("씬 스캔", GUILayout.Height(30)))
                Scan();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"결과: {_rows.Count}건", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var r in _rows)
            {
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    var tag = r.missing ? "<color=#ff6060>[MISSING]</color> " : "";
                    var style = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
                    EditorGUILayout.LabelField(
                        $"{tag}{r.objPath}\n<b>{r.compType}</b> . <color=#7fd0ff>{r.field}</color>  ({r.fieldType})  [{r.sceneName}]",
                        style, GUILayout.Height(38));

                    if (GUILayout.Button("선택", GUILayout.Width(60), GUILayout.Height(38)))
                    {
                        Selection.activeGameObject = r.go;
                        EditorGUIUtility.PingObject(r.go);
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            if (_rows.Count > 0 && GUILayout.Button("결과 로그로 출력"))
                DumpLog();
        }

        private void Scan()
        {
            _rows.Clear();
            _playerTypes = CollectPlayerTypes(_playerPrefab);

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                foreach (var comp in root.GetComponentsInChildren<Component>(_includeInactive))
                {
                    if (comp == null) continue;                 // Missing script
                    if (comp is Transform) continue;

                    var so = new SerializedObject(comp);
                    var it = so.GetIterator();
                    while (it.NextVisible(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        // 엔진 공통 노이즈만 제외 (m_Follow/m_LookAt 같은 유의미한 m_ 필드는 남긴다)
                        if (it.propertyPath == "m_GameObject" || it.propertyPath == "m_Script") continue;
                        if (it.objectReferenceValue != null) continue;        // 채워진 건 스킵

                        bool missing = it.objectReferenceInstanceIDValue != 0; // null 인데 instanceID 남음 = Missing
                        Type fieldType = GetFieldType(comp, it.propertyPath);
                        string fieldTypeName = fieldType != null ? fieldType.Name : "?";
                        string lowerName = it.name.ToLowerInvariant();

                        bool typeIsPlayer = fieldType != null &&
                            (fieldType == typeof(GameObject) || fieldType == typeof(Transform) || _playerTypes.Contains(fieldType));
                        bool nameHit = Keywords.Any(k => lowerName.Contains(k));
                        bool typeIsPlayerComponent = fieldType != null && _playerTypes.Contains(fieldType);

                        int score = 0;
                        if (missing) score += 100;
                        if (typeIsPlayerComponent) score += 50;
                        if (nameHit) score += 20;
                        if (fieldType == typeof(GameObject) || fieldType == typeof(Transform)) score += 5;

                        // 포함 규칙: Missing || 플레이어컴포넌트타입 || (키워드 && GO/Transform/플레이어타입)
                        bool include = missing
                                       || typeIsPlayerComponent
                                       || (nameHit && typeIsPlayer);
                        if (!include) continue;

                        _rows.Add(new Row
                        {
                            go = comp.gameObject,
                            sceneName = scene.name,
                            objPath = GetHierarchyPath(comp.transform),
                            compType = comp.GetType().Name,
                            field = it.displayName,
                            fieldType = fieldTypeName,
                            missing = missing,
                            score = score
                        });
                    }
                }
            }

            _rows.Sort((a, b) => b.score.CompareTo(a.score));
            Debug.Log($"[MissingPlayerRefFinder] 스캔 완료 — 후보 {_rows.Count}건 (Player 타입 {_playerTypes.Count}종 매칭).");
        }

        private static HashSet<Type> CollectPlayerTypes(GameObject prefab)
        {
            var set = new HashSet<Type>();
            if (prefab == null) return set;
            foreach (var c in prefab.GetComponentsInChildren<Component>(true))
                if (c != null && !(c is Transform)) set.Add(c.GetType());
            return set;
        }

        /// <summary>propertyPath 로부터 실제 필드 타입을 리플렉션으로 얻는다(단순/1단계 중첩까지).</summary>
        private static Type GetFieldType(object target, string path)
        {
            Type t = target.GetType();
            Type ft = null;
            foreach (var partRaw in path.Split('.'))
            {
                if (partRaw == "Array") return null;   // 컬렉션은 포기
                var fi = GetField(t, partRaw);
                if (fi == null) return null;
                ft = fi.FieldType;
                t = ft;
            }
            return ft;
        }

        private static FieldInfo GetField(Type t, string name)
        {
            while (t != null && t != typeof(object))
            {
                var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (fi != null) return fi;
                t = t.BaseType;
            }
            return null;
        }

        private static string GetHierarchyPath(Transform t)
        {
            var stack = new Stack<string>();
            while (t != null) { stack.Push(t.name); t = t.parent; }
            return string.Join("/", stack);
        }

        private void DumpLog()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Missing Player Refs] {_rows.Count}건");
            foreach (var r in _rows)
                sb.AppendLine($"{(r.missing ? "[MISSING] " : "")}{r.objPath}  ::  {r.compType}.{r.field}  ({r.fieldType})  [{r.sceneName}]");
            Debug.Log(sb.ToString());
        }
    }
}
