// @tags: tool, debug, console, qa, cheat, unlock, commands
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using System.Text;
using DebugTools;
using UnityEngine;

/// <summary>
/// 도구(손·삽·곡괭이·드릴) 해금 치트 — 콘솔 명령 <c>tool</c>.
///
/// 해금 판정은 <see cref="ToolController.IsToolUnlocked"/>가 toolConfig.json의
/// <c>unlockNodeIds</c>를 업그레이드 트리와 대조하는 구조다. 그래서 이 명령도
/// 도구 상태를 따로 두지 않고 <b>그 노드를 업그레이드 트리에 넣고 빼는 것</b>만 한다
/// (별도 플래그를 만들면 세이브·상점 UI와 두 갈래로 갈라진다).
///
/// 도구 이름·영문 별칭도 toolConfig.json에 있다 — 도구가 늘어도 이 파일은 안 고친다.
/// </summary>
internal static class ToolDebugCommands
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        DebugCommandRegistry.Register("tool", "tool [이름|번호|all] / tool lock <이름|번호|all>",
            "도구 해금·잠금. 인자 없으면 현황 출력. 예: tool drill / tool all / tool lock pickaxe",
            Run, "도구", "unlock");
    }

    private static string Run(string[] args)
    {
        var cfg = ToolConfigLoader.Instance?.Config;
        if (cfg == null)
            return "ToolConfigLoader 없음 — 이 씬에는 도구 해금 설정이 로드되지 않았다 (모든 도구가 해금 상태로 취급된다)";

        if (args.Length == 0) return Status(cfg);

        bool lockMode = args[0].Equals("lock", System.StringComparison.OrdinalIgnoreCase)
                        || args[0] == "잠금";
        string target = lockMode ? (args.Length > 1 ? args[1] : null) : args[0];

        if (string.IsNullOrEmpty(target))
            return "사용법: tool lock <이름|번호|all>";

        if (UpgradeManager.Instance == null)
            return "UpgradeManager 없음 (아직 씬이 로드되지 않았을 수 있음)";

        if (target.Equals("all", System.StringComparison.OrdinalIgnoreCase) || target == "전부")
            return ApplyAll(cfg, lockMode);

        int index = Resolve(cfg, target);
        if (index < 0) return $"그런 도구 없음: {target}\n{Status(cfg)}";

        return Apply(cfg, index, lockMode);
    }

    // ───────────────────────────────────────────────────────────

    private static string Status(ToolConfigData cfg)
    {
        var tc = Object.FindFirstObjectByType<ToolController>();

        var sb = new StringBuilder();
        sb.Append("<color=#C7CFE2>도구 해금 현황</color>");

        for (int i = 0; i < cfg.ToolCount; i++)
        {
            string nodeId = cfg.NodeIdOf(i);
            string alias = cfg.AliasOf(i);
            bool unlocked = IsUnlocked(nodeId);

            string mark = unlocked ? "<color=#F5C63F>해금</color>" : "<color=#7B86A0>잠김</color>";
            string equipped = (tc != null && tc.currentToolIndex == i) ? "  <color=#F09A3E>◀ 장착 중</color>" : "";

            sb.Append($"\n  {i}. {cfg.NameOf(i)}");
            if (!string.IsNullOrEmpty(alias)) sb.Append($" <color=#7B86A0>({alias})</color>");
            sb.Append($"  {mark}{equipped}");

            sb.Append(string.IsNullOrEmpty(nodeId)
                ? "\n      <color=#7B86A0>해금 조건 없음 — 항상 사용 가능</color>"
                : $"\n      <color=#7B86A0>{nodeId}</color>");
        }

        return sb.ToString();
    }

    private static string Apply(ToolConfigData cfg, int index, bool lockMode)
    {
        string nodeId = cfg.NodeIdOf(index);
        string name = cfg.NameOf(index);

        if (string.IsNullOrEmpty(nodeId))
            return $"{name}은(는) 해금 조건이 없는 도구다 — 잠그거나 해금할 대상이 아니다";

        string result;
        if (lockMode)
        {
            result = UpgradeManager.Instance.DebugForceLock(nodeId)
                ? $"{name} 잠금  <color=#7B86A0>({nodeId})</color>"
                : $"{name}은(는) 이미 잠겨 있다";
        }
        else
        {
            if (IsUnlocked(nodeId)) return $"{name}은(는) 이미 해금돼 있다";
            UpgradeManager.Instance.DebugForceUnlock(nodeId);
            result = $"{name} 해금  <color=#7B86A0>({nodeId})</color>";
        }

        AfterChange();
        return result;
    }

    private static string ApplyAll(ToolConfigData cfg, bool lockMode)
    {
        int changed = 0;

        for (int i = 0; i < cfg.ToolCount; i++)
        {
            string nodeId = cfg.NodeIdOf(i);
            if (string.IsNullOrEmpty(nodeId)) continue;

            if (lockMode)
            {
                if (UpgradeManager.Instance.DebugForceLock(nodeId)) changed++;
            }
            else if (!IsUnlocked(nodeId))
            {
                UpgradeManager.Instance.DebugForceUnlock(nodeId);
                changed++;
            }
        }

        if (changed == 0) return lockMode ? "잠글 도구가 없다 (이미 전부 잠김)" : "해금할 도구가 없다 (이미 전부 해금)";

        AfterChange();
        return $"도구 {changed}개 {(lockMode ? "잠금" : "해금")}\n{Status(cfg)}";
    }

    /// <summary>
    /// 해금 상태를 바꾼 뒤 처리. 잠근 도구를 든 채로 남으면 그 도구로 계속 팔 수 있으므로
    /// <see cref="ToolController.RefreshSelection"/>으로 손에서 내려놓게 한다.
    /// </summary>
    private static void AfterChange()
    {
        Object.FindFirstObjectByType<ToolController>()?.RefreshSelection();
        SaveManager.Instance?.MarkRunAsFixture();
    }

    private static bool IsUnlocked(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return true;
        return UpgradeManager.Instance != null && UpgradeManager.Instance.IsNodeUnlocked(nodeId);
    }

    /// <summary>번호 → 영문 별칭 → 한글 이름(접두 일치) 순으로 도구를 지목한다.</summary>
    private static int Resolve(ToolConfigData cfg, string token)
    {
        if (int.TryParse(token, out int index))
            return (index >= 0 && index < cfg.ToolCount) ? index : -1;

        for (int i = 0; i < cfg.ToolCount; i++)
        {
            string alias = cfg.AliasOf(i);
            if (!string.IsNullOrEmpty(alias) &&
                alias.Equals(token, System.StringComparison.OrdinalIgnoreCase)) return i;
        }

        for (int i = 0; i < cfg.ToolCount; i++)
        {
            string alias = cfg.AliasOf(i);
            if (!string.IsNullOrEmpty(alias) &&
                alias.StartsWith(token, System.StringComparison.OrdinalIgnoreCase)) return i;

            if (cfg.NameOf(i).StartsWith(token, System.StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }
}
#endif
