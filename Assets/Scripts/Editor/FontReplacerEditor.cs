using UnityEngine;
using UnityEditor;
using TMPro;
using System.Collections.Generic;
using System.IO;

public class FontReplacerEditor : EditorWindow
{
    private TMP_FontAsset targetFont;

    [MenuItem("Tools/Font Replacer")]
    public static void ShowWindow()
    {
        GetWindow<FontReplacerEditor>("Font Replacer");
    }

    private void OnGUI()
    {
        GUILayout.Label("프로젝트 전체 폰트 교체 도구", EditorStyles.boldLabel);
        
        targetFont = (TMP_FontAsset)EditorGUILayout.ObjectField("대상 폰트", targetFont, typeof(TMP_FontAsset), false);

        if (GUILayout.Button("모든 프렙 및 씬의 폰트 교체"))
        {
            if (targetFont == null)
            {
                EditorUtility.DisplayDialog("오류", "대상 폰트를 지정해주세요.", "확인");
                return;
            }

            if (EditorUtility.DisplayDialog("폰트 교체", "프로젝트의 모든 TMP 컴포넌트 폰트를 교체하시겠습니까? 이 작업은 되돌릴 수 없습니다.", "진행", "취소"))
            {
                ReplaceFonts();
            }
        }
    }

    private void ReplaceFonts()
    {
        // 1. 모든 프렙 수정
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        int count = 0;

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            
            if (prefab == null) continue;

            TextMeshProUGUI[] tmps = prefab.GetComponentsInChildren<TextMeshProUGUI>(true);
            bool isModified = false;

            foreach (var tmp in tmps)
            {
                if (tmp.font != targetFont)
                {
                    Undo.RecordObject(tmp, "Replace Font");
                    tmp.font = targetFont;
                    isModified = true;
                }
            }

            if (isModified)
            {
                EditorUtility.SetDirty(prefab);
                count++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FontReplacer] {count}개의 프렙을 수정했습니다.");

        // 2. 현재 열린 씬 수정 (필요 시 모든 씬 스캔 가능하지만, 사용자가 수동으로 수행하는 것이 안전함)
#pragma warning disable 0618
        TextMeshProUGUI[] sceneTmps = GameObject.FindObjectsOfType<TextMeshProUGUI>(true);
#pragma warning restore 0618
        int sceneCount = 0;

        foreach (var tmp in sceneTmps)
        {
            if (tmp.font != targetFont)
            {
                Undo.RecordObject(tmp, "Replace Font");
                tmp.font = targetFont;
                sceneCount++;
            }
        }

        Debug.Log($"[FontReplacer] 현재 씬에서 {sceneCount}개의 텍스트를 수정했습니다.");
        
        EditorUtility.DisplayDialog("완료", $"프렙 {count}개, 씬 {sceneCount}개 수정 완료.", "확인");
    }
}
