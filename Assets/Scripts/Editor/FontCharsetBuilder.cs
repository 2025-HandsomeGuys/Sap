// @tags: font, tmp, charset, editor, tool, localization
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 폰트(Static 아틀라스) 굽기용 "고유 문자 목록" txt를 생성/갱신하는 에디터 도구.
///
/// StreamingAssets/Data/*.csv 에 등장하는 모든 문자를 모아
/// Assets/Font/unique_chars.txt 에 코드포인트 순으로 저장한다.
/// Font Asset Creator의 Character Set = "Characters from File" 에 이 txt를 넣어 굽는다.
///
/// 동작 방식:
///  - 기존 txt에 이미 들어있던 문자는 유지(Union)하고, CSV에서 새로 등장한 문자만 추가한다.
///    → 텍스트가 늘어나도 이 메뉴만 다시 실행하면 신규 문자가 자동으로 붙는다.
///  - 제어문자(개행·탭)와 BOM은 제외한다.
///
/// 메뉴: Tools ▸ Font ▸ Rebuild Unique Char Set
/// </summary>
public static class FontCharsetBuilder
{
    // 스캔 대상 CSV 디렉토리 (프로젝트 상대). 다른 텍스트 소스가 생기면 여기에 추가.
    private static readonly string[] ScanDirs =
    {
        "Assets/StreamingAssets/Data",
    };

    // 결과 txt 경로 (프로젝트 상대). Font Asset Creator에서 TextAsset로 지정.
    private const string OutputPath = "Assets/Font/unique_chars.txt";

    private const int CharsPerLine = 80;

    [MenuItem("Tools/Font/Rebuild Unique Char Set")]
    public static void Rebuild()
    {
        var set = new SortedSet<int>();

        // 1) 기존 txt 문자 유지 (Union)
        int existing = 0;
        string absOut = ToAbsolute(OutputPath);
        if (File.Exists(absOut))
        {
            foreach (int code in EnumerateChars(File.ReadAllText(absOut, Encoding.UTF8)))
                if (set.Add(code)) existing++;
        }

        // 2) CSV들에서 문자 수집
        int scannedFiles = 0;
        foreach (string dir in ScanDirs)
        {
            string absDir = ToAbsolute(dir);
            if (!Directory.Exists(absDir)) continue;
            foreach (string file in Directory.GetFiles(absDir, "*.csv", SearchOption.AllDirectories))
            {
                scannedFiles++;
                foreach (int code in EnumerateChars(File.ReadAllText(file, Encoding.UTF8)))
                    set.Add(code);
            }
        }

        int added = set.Count - existing;

        // 3) 코드포인트 순으로 저장 (UTF-8, BOM 없음)
        var sb = new StringBuilder();
        int col = 0;
        foreach (int code in set)
        {
            sb.Append(char.ConvertFromUtf32(code));
            if (++col % CharsPerLine == 0) sb.Append('\n');
        }
        Directory.CreateDirectory(Path.GetDirectoryName(absOut));
        File.WriteAllText(absOut, sb.ToString(), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);

        int ascii = 0, hangul = 0, cjk = 0, other = 0;
        foreach (int c in set)
        {
            if (c >= 32 && c <= 126) ascii++;
            else if (c >= 0xAC00 && c <= 0xD7A3) hangul++;
            else if (c >= 0x4E00 && c <= 0x9FFF) cjk++;
            else other++;
        }

        Debug.Log($"[FontCharsetBuilder] '{OutputPath}' 갱신 완료.\n" +
                  $"  스캔 CSV: {scannedFiles}개 / 총 고유 문자: {set.Count}자 (신규 +{added})\n" +
                  $"  ASCII {ascii} / 한글 {hangul} / 한자 {cjk} / 기타 {other}\n" +
                  $"  → 이 txt로 폰트를 다시 구운 뒤 UVCS에 체크인하세요.");
    }

    private static IEnumerable<int> EnumerateChars(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            int code;
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                code = char.ConvertToUtf32(ch, text[i + 1]);
                i++;
            }
            else
            {
                code = ch;
            }
            if (code < 32) continue;     // 개행·탭 등 제어문자
            if (code == 0xFEFF) continue; // BOM
            yield return code;
        }
    }

    private static string ToAbsolute(string projectRelative)
    {
        // Application.dataPath = ".../Assets"
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, projectRelative.Replace('/', Path.DirectorySeparatorChar));
    }
}
