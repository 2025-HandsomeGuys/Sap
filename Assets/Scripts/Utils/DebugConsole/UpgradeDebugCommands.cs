// @tags: debug, console, qa, cheat, upgrade, node, unlock, coin
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using System.Text;
using UnityEngine;

namespace DebugTools
{
    /// <summary>
    /// 업그레이드 노드 치트. 노드를 사지 않고 열고 닫는다.
    ///
    /// 잠긴 기능(코인 계열 등)을 확인하려면 그 노드를 사야 하는데, 그러려면
    /// 트리를 밑에서부터 다 사고 골드도 모아야 한다 — QA에선 그게 병목이다.
    /// </summary>
    internal static class UpgradeDebugCommands
    {
        /// <summary>이름 하나로 부르고 싶은 노드들. 긴 id를 외우지 않게 한다.</summary>
        private static readonly (string alias, string nodeId)[] Shortcuts =
        {
            ("coin",     "Facility_Coin_T1"),          // 코인 거래 개통 (2지층 통행권)
            ("computer", "Facility_Computer_T0"),      // 단말기 개통 (주식)
            ("pickaxe",  "PickaxeUnlock_T0_01"),
            ("drill",    "DrillCapacity_T1_01"),
            ("map",      "Facility_Map_T0"),
            ("climbmine", "ClimbMiningUnlock_T0"),   // 매달린 채 삽·곡괭이질
            // 엘리베이터는 업그레이드로 사는 게 아니라 지하에서 직접 발견해서 열린다
            // (WorldInteractable.requireElevatorDiscovered → ElevatorStopUnlockStore).
            // Facility_Elevator_T0 노드는 트리에 없으므로 여기 두면 "그런 노드 없음"만 나온다.
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            DebugCommandRegistry.Register("node", "node <이름|노드id> [off]",
                "업그레이드 노드 강제 해금/잠금. 인자 없으면 단축 이름 목록. 예: node coin / node coin off",
                Node, "unlock", "노드");

            DebugCommandRegistry.Register("upg", "upg [on|off]",
                "연쇄 해금 치트 토글. 켜두면 업그레이드 창에서 잠긴 노드를 눌러 해금할 때 " +
                "그 아래 선행 노드까지 전부 공짜로 열린다.",
                Chain, "연쇄", "chain");

            DebugCommandRegistry.SetArgCompleter("node", CompleteArg);
            DebugCommandRegistry.SetArgCompleter("upg", CompleteOnOff);
        }

        /// <summary>Tab 자동완성 후보 — 단축 이름만. 노드 id 전체는 목록이 너무 길다.</summary>
        private static System.Collections.Generic.IEnumerable<string> CompleteArg(string[] argsSoFar, string prefix)
        {
            if (argsSoFar.Length == 0)
            {
                foreach (var (alias, _) in Shortcuts) yield return alias;
                yield break;
            }

            if (argsSoFar.Length == 1) yield return "off";
        }

        private static System.Collections.Generic.IEnumerable<string> CompleteOnOff(string[] argsSoFar, string prefix)
        {
            if (argsSoFar.Length == 0) { yield return "on"; yield return "off"; }
        }

        /// <summary>
        /// 연쇄 해금 치트 on/off. 인자가 없으면 토글.
        ///
        /// 노드 id를 하나씩 치는 <c>node</c>와 달리, 목표 노드 하나만 트리에서 눌러
        /// 그 아래를 통째로 여는 방식이라 잠긴 기능 확인이 클릭 한 번으로 끝난다.
        /// </summary>
        private static string Chain(string[] args)
        {
            bool on;
            if (args.Length == 0)
            {
                on = !UpgradeManager.DebugChainUnlock;
            }
            else
            {
                string a = args[0];
                on = !(a.Equals("off", System.StringComparison.OrdinalIgnoreCase) ||
                       a == "0" || a == "끄기" || a == "꺼");
            }

            UpgradeManager.DebugChainUnlock = on;
            return on
                ? "연쇄 해금 <color=#7CF57C>ON</color> — 업그레이드 창에서 잠긴 노드를 눌러 해금하면 " +
                  "선행 노드까지 공짜로 전부 열린다. (계층 잠금도 같이 열림)"
                : "연쇄 해금 <color=#F57C7C>OFF</color>";
        }

        private static string Node(string[] args)
        {
            var mgr = UpgradeManager.Instance;
            if (mgr == null) return "UpgradeManager 없음 (지상 씬에서 실행할 것)";

            if (args.Length == 0) return ListShortcuts(mgr);

            string nodeId = Resolve(args[0]);
            bool off = args.Length > 1 &&
                       (args[1].Equals("off", System.StringComparison.OrdinalIgnoreCase) ||
                        args[1] == "0" || args[1] == "잠금");

            if (off)
            {
                return mgr.DebugForceLock(nodeId)
                    ? $"잠금: {nodeId}"
                    : $"이미 잠겨 있음(또는 없는 노드): {nodeId}";
            }

            // 없는 id를 조용히 해금하면 "켰는데 아무 일도 안 일어난다"가 된다 — 먼저 막는다.
            if (mgr.GetNodeFromCache(nodeId) == null)
                return $"그런 노드 없음: {nodeId}\n{ListShortcuts(mgr)}";

            mgr.DebugForceUnlock(nodeId);
            return $"해금: {nodeId}\n(코인은 마켓 씬을 다시 들어가야 탭이 보인다)";
        }

        private static string Resolve(string name)
        {
            foreach (var (alias, nodeId) in Shortcuts)
                if (alias.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return nodeId;
            return name;   // 단축 이름이 아니면 노드 id 그대로 본다
        }

        private static string ListShortcuts(UpgradeManager mgr)
        {
            var sb = new StringBuilder("단축 이름:");
            foreach (var (alias, nodeId) in Shortcuts)
            {
                bool on = mgr.IsNodeUnlocked(nodeId);
                sb.Append($"\n  <color=#F5C63F>{alias,-9}</color>{nodeId} {(on ? "[해금]" : "[잠김]")}");
            }
            sb.Append("\n  그 밖의 노드는 id를 그대로 적는다.");
            return sb.ToString();
        }
    }
}
#endif
