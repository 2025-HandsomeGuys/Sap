// @tags: telemetry, editor, menu, tool, log, folder

using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 텔레메트리 로그 확인용 에디터 메뉴.
/// 로컬 전용(Phase 1)에서는 파일을 직접 열어보는 것이 유일한 확인 수단이라 필요하다.
/// </summary>
public static class TelemetryEditorMenu
{
    private const string EchoMenu = "Tools/Telemetry/콘솔에 이벤트 출력";

    [MenuItem("Tools/Telemetry/로그 폴더 열기")]
    private static void OpenFolder()
    {
        string dir = Telemetry.LogDirectory;
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir);
    }

    [MenuItem("Tools/Telemetry/지금 플러시")]
    private static void FlushNow()
    {
        Telemetry.Flush();
        Debug.Log("[Telemetry] 수동 플러시 완료");
    }

    [MenuItem("Tools/Telemetry/로그 전체 삭제")]
    private static void ClearLogs()
    {
        string dir = Telemetry.LogDirectory;
        if (!Directory.Exists(dir))
        {
            Debug.Log("[Telemetry] 삭제할 로그가 없습니다.");
            return;
        }

        if (!EditorUtility.DisplayDialog("텔레메트리 로그 삭제",
                $"{dir}\n\n안의 모든 .jsonl 파일을 지웁니다. 계속할까요?", "삭제", "취소"))
            return;

        foreach (string f in Directory.GetFiles(dir, "*.jsonl")) File.Delete(f);
        Debug.Log("[Telemetry] 로그 전체 삭제 완료");
    }

    [MenuItem(EchoMenu)]
    private static void ToggleEcho()
    {
        Telemetry.EchoToConsole = !Telemetry.EchoToConsole;
        Menu.SetChecked(EchoMenu, Telemetry.EchoToConsole);
        Debug.Log($"[Telemetry] 콘솔 출력 {(Telemetry.EchoToConsole ? "켬" : "끔")}");
    }

    [MenuItem(EchoMenu, true)]
    private static bool ToggleEchoValidate()
    {
        Menu.SetChecked(EchoMenu, Telemetry.EchoToConsole);
        return true;
    }
}
