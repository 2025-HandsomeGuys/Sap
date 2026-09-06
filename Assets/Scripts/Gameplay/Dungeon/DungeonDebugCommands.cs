// @tags: dungeon, debug, console, qa, cheat, escape, commands
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using DebugTools;
using UnityEngine;

/// <summary>
/// 던전 중도 탈출 치트 — 콘솔 명령 <c>escape</c>.
///
/// 던전에는 끝 방의 출구 문(<see cref="DungeonExitInteractable"/>) 말고 나가는 경로가 없어서
/// 중간 지점 테스트가 끝나면 죽거나 끝까지 가는 수밖에 없었다. 이 명령이 그 구멍을 메운다.
///
/// 탈출 처리는 <see cref="DungeonEscape.TryLeave"/>에 위임한다 — 출구 문과 완전히 같은 경로라
/// "콘솔로 나갔더니 상태가 다르게 저장되는" 갈래가 생기지 않는다.
/// </summary>
internal static class DungeonDebugCommands
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        DebugCommandRegistry.Register("escape", "escape",
            "던전 중도 탈출. 출구 문과 동일 처리 — 그 던전은 탐험 완료가 되어 재입장 불가",
            Run, "dexit", "탈출");
    }

    private static string Run(string[] args)
    {
        if (!DungeonEscape.TryLeave(out string message)) return message;

        // 정상 플레이로 나간 게 아니므로 이 회차는 밸런스 집계에서 빠져야 한다.
        SaveManager.Instance?.MarkRunAsFixture();

        return message;
    }
}
#endif
