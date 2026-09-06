using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

/// <summary>
/// 프로젝트 내의 SO 파일들을 스캔하여 기존 LocalizedString 형식의 데이터를 CSV로 추출하는 에디터 도구입니다.
/// 주의: 이 도구는 LocalizedString 구조체가 프로젝트에 남아있을 때(마이그레이션 전) 사용해야 정확히 작동합니다.
/// </summary>
public class LocalizationExporter : EditorWindow
{
    private string exportPath = "Assets/StreamingAssets/Data/Exported_Localization.csv";

    [MenuItem("Tools/Localization Exporter")]
    public static void ShowWindow()
    {
        GetWindow<LocalizationExporter>("Localization Exporter");
    }

    private void OnGUI()
    {
        GUILayout.Label("기존 LocalizedString 데이터 추출기", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("프로젝트 내의 모든 ScriptableObject를 스캔하여 'LocalizedString' 타입의 변수들을 찾아 CSV 파일로 저장합니다.", MessageType.Info);
        
        exportPath = EditorGUILayout.TextField("Export Path", exportPath);

        if (GUILayout.Button("추출 시작 (Export to CSV)", GUILayout.Height(40)))
        {
            ExportAllLocalizedStrings();
        }
    }

    private void ExportAllLocalizedStrings()
    {
        // 결과 저장을 위한 리스트: key, kr, en, cn
        List<string[]> rows = new List<string[]>();
        rows.Add(new string[] { "key", "kr", "en", "cn" });

        // 모든 ScriptableObject 애셋 찾기
        string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
        HashSet<string> keysFound = new HashSet<string>();

        int count = 0;
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            Object obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (obj == null) continue;

            SerializedObject serializedObject = new SerializedObject(obj);
            SerializedProperty prop = serializedObject.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = true;

                // LocalizedString 타입의 프로퍼티를 찾음
                // (경고: 구조체 이름이 일치해야 하며, 마이그레이션 후에는 String으로 바뀌어 찾지 못할 수 있음)
                if (prop.type == "LocalizedString")
                {
                    SerializedProperty krProp = prop.FindPropertyRelative("kr");
                    SerializedProperty enProp = prop.FindPropertyRelative("en");
                    SerializedProperty cnProp = prop.FindPropertyRelative("cn");

                    string kr = krProp != null ? krProp.stringValue : "";
                    string en = enProp != null ? enProp.stringValue : "";
                    string cn = cnProp != null ? cnProp.stringValue : "";

                    if (!string.IsNullOrWhiteSpace(kr) || !string.IsNullOrWhiteSpace(en))
                    {
                        // key 생성 (SO이름_프로퍼티이름)
                        string key = $"{obj.name}_{prop.name}".ToLower().Replace(" ", "_");
                        
                        // 중복 방지 (배열 요소인 경우 등)
                        int index = 1;
                        string originalKey = key;
                        while (keysFound.Contains(key))
                        {
                            key = $"{originalKey}_{index}";
                            index++;
                        }
                        keysFound.Add(key);

                        rows.Add(new string[] { key, kr, en, cn });
                        count++;
                    }
                }
            }
        }

        SaveToCsv(rows, exportPath);
        EditorUtility.DisplayDialog("추출 완료", $"총 {count}개의 번역 항목을 추출하여 CSV로 저장했습니다.\n경로: {exportPath}", "확인");
    }

    private void SaveToCsv(List<string[]> rows, string path)
    {
        // 디렉토리가 없으면 생성
        string dirInfo = Path.GetDirectoryName(path);
        if (!Directory.Exists(dirInfo) && !string.IsNullOrEmpty(dirInfo))
        {
            Directory.CreateDirectory(dirInfo);
        }

        StringBuilder sb = new StringBuilder();
        foreach (var row in rows)
        {
            List<string> escapedRow = new List<string>();
            foreach (var field in row)
            {
                // CSV RFC 4180 이스케이프 처리
                string escaped = field ?? "";
                if (escaped.Contains("\"") || escaped.Contains(",") || escaped.Contains("\n") || escaped.Contains("\r"))
                {
                    escaped = escaped.Replace("\"", "\"\"");
                    escaped = $"\"{escaped}\"";
                }
                escapedRow.Add(escaped);
            }
            sb.AppendLine(string.Join(",", escapedRow));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); // BOM 추가
        AssetDatabase.Refresh();
    }
}
