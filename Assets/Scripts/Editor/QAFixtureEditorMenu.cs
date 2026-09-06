// @tags: qa, fixture, editor, menu, tool, save, folder

using System.IO;
using System.Text;
using DebugTools;
using UnityEditor;
using UnityEngine;

/// <summary>
/// QA 세이브 픽스처용 에디터 메뉴.
/// 굽고 되돌리는 것은 런타임 콘솔의 <c>fx</c> 명령이 하고, 여기서는 파일을 눈으로 확인한다.
/// </summary>
public static class QAFixtureEditorMenu
{
    [MenuItem("Tools/QA/픽스처 폴더 열기")]
    private static void OpenFixtureFolder()
    {
        string dir = QAFixtureStore.Root;
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir);
    }

    /// <summary>
    /// 실제 세이브가 사는 곳. 픽스처가 제대로 덮였는지 의심될 때 여기를 본다.
    /// (playerData_{슬롯}.json 과 worldData.bin)
    /// </summary>
    [MenuItem("Tools/QA/세이브 폴더 열기")]
    private static void OpenSaveFolder()
    {
        EditorUtility.RevealInFinder(Application.persistentDataPath);
    }

    [MenuItem("Tools/QA/픽스처 목록 출력")]
    private static void PrintFixtures()
    {
        var fixtures = QAFixtureStore.List();
        if (fixtures.Count == 0)
        {
            Debug.Log($"[QAFixture] 픽스처 없음 — {QAFixtureStore.Root}");
            return;
        }

        var sb = new StringBuilder($"[QAFixture] {fixtures.Count}개");
        foreach (var f in fixtures)
        {
            sb.Append($"\n  {f.name}{(f.hasWorld ? "" : "  (지형 없음)")}");
            sb.Append($"\n      {f.memo}   · {f.savedAt:yyyy-MM-dd HH:mm}");
        }
        Debug.Log(sb.ToString());
    }
}
