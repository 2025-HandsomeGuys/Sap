// @tags: debug, console, qa, cheat, facility, unlock, gate
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using System.Text;
using UnityEngine;

namespace DebugTools
{
    /// <summary>
    /// 시설 해금 치트. 잠긴 시설을 조건과 무관하게 열고 닫는다.
    ///
    /// <b>왜 <c>node</c> 명령과 따로 두나</b> — 시설을 여는 조건이 업그레이드 노드 하나가 아니다.
    /// 지상 엘리베이터 입구는 '지하에서 엘리베이터를 직접 찾아간 적 있음'(<see cref="ElevatorStopUnlockStore"/>),
    /// 작업대는 '튜토리얼 완료'(<see cref="TutorialProgress"/>)로 열린다.
    /// QA는 "저 시설 좀 열어줘"가 필요한 것이지 그게 노드인지 플래그인지는 알 바 아니므로,
    /// 여는 창구를 하나로 모으고 <c>facility</c>가 알아서 갈라준다.
    /// </summary>
    internal static class FacilityDebugCommands
    {
        private enum GateKind
        {
            /// <summary>업그레이드 노드 구매로 열린다.</summary>
            Node,
            /// <summary>지하에서 엘리베이터를 발견하면 열린다.</summary>
            ElevatorSeen,
            /// <summary>튜토리얼을 끝내면 열린다.</summary>
            Tutorial,
        }

        private readonly struct Facility
        {
            public readonly string Alias;
            public readonly string Label;
            public readonly GateKind Gate;
            public readonly string NodeId;   // Gate == Node일 때만

            public Facility(string alias, string label, GateKind gate, string nodeId = null)
            {
                Alias = alias; Label = label; Gate = gate; NodeId = nodeId;
            }
        }

        /// <summary>막혀 있는 시설 전부. 새 시설을 잠그면 여기 한 줄을 추가한다.</summary>
        private static readonly Facility[] Facilities =
        {
            new Facility("pc",        "단말기(주식)",      GateKind.Node, "Facility_Computer_T0"),
            new Facility("coin",      "코인 거래",         GateKind.Node, "Facility_Coin_T1"),
            new Facility("map",       "지도(미니맵/M)",    GateKind.Node, "Facility_Map_T0"),
            new Facility("board",     "서브퀘스트 게시판", GateKind.Node, "Facility_Board_T0"),
            new Facility("elevator",  "지상 엘리베이터 입구", GateKind.ElevatorSeen),
            new Facility("workbench", "작업대(강화)",      GateKind.Tutorial),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            DebugCommandRegistry.Register("facility", "facility <이름|all> [off]",
                "시설 강제 해금/잠금. 인자 없으면 시설별 현재 상태. 예: facility workbench / facility all",
                Run, "시설", "fac");

            DebugCommandRegistry.SetArgCompleter("facility", CompleteArg);
        }

        /// <summary>Tab 자동완성 후보. 시설 이름을 외우지 않게 한다(이름이 계속 늘어난다).</summary>
        private static System.Collections.Generic.IEnumerable<string> CompleteArg(string[] argsSoFar, string prefix)
        {
            if (argsSoFar.Length == 0)
            {
                yield return "all";
                for (int i = 0; i < Facilities.Length; i++) yield return Facilities[i].Alias;
                yield break;
            }

            if (argsSoFar.Length == 1) yield return "off";
        }

        private static string Run(string[] args)
        {
            if (args.Length == 0) return Status();

            bool off = args.Length > 1 &&
                       (args[1].Equals("off", System.StringComparison.OrdinalIgnoreCase) ||
                        args[1] == "0" || args[1] == "잠금");

            if (args[0].Equals("all", System.StringComparison.OrdinalIgnoreCase) || args[0] == "전부")
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Facilities.Length; i++)
                    sb.Append(Apply(Facilities[i], off)).Append('\n');
                sb.Append(Status());
                return sb.ToString();
            }

            for (int i = 0; i < Facilities.Length; i++)
            {
                if (!Facilities[i].Alias.Equals(args[0], System.StringComparison.OrdinalIgnoreCase)) continue;
                return Apply(Facilities[i], off) + "\n" + Status();
            }

            return $"그런 시설 없음: {args[0]}\n{Status()}";
        }

        private static string Apply(in Facility f, bool off)
        {
            switch (f.Gate)
            {
                case GateKind.Node:
                {
                    var mgr = UpgradeManager.Instance;
                    if (mgr == null) return $"{f.Label}: UpgradeManager 없음 (지상 씬에서 실행할 것)";

                    // 없는 id를 조용히 해금하면 "켰는데 아무 일도 안 일어난다"가 된다.
                    if (mgr.GetNodeFromCache(f.NodeId) == null)
                        return $"{f.Label}: 노드 '{f.NodeId}'가 트리에 없다 (아직 안 만든 노드)";

                    if (off) mgr.DebugForceLock(f.NodeId);
                    else mgr.DebugForceUnlock(f.NodeId);
                    return $"{f.Label}: {(off ? "잠금" : "해금")} ({f.NodeId})";
                }

                case GateKind.ElevatorSeen:
                    if (off)
                    {
                        // 정류장 해금 기록 전체가 지워진다 — 층 이동 목록도 같이 닫힌다.
                        ElevatorStopUnlockStore.Clear();
                        return $"{f.Label}: 잠금 (엘리베이터 발견 기록을 전부 지웠다)";
                    }
                    // 실제 정류장은 건드리지 않고 '본 적 있다'만 세운다.
                    ElevatorStopUnlockStore.Unlock(ElevatorStopUnlockStore.DebugDiscoveryDepth);
                    return $"{f.Label}: 해금 (정류장 목록은 그대로 — 발견 표식만 세움)";

                case GateKind.Tutorial:
                    return TutorialProgress.DebugSet(!off)
                        ? $"{f.Label}: {(off ? "잠금" : "해금")} (튜토리얼 완료 = {!off})"
                        : $"{f.Label}: 세이브 데이터 없음 (슬롯을 불러온 뒤 실행할 것)";
            }

            return null;
        }

        private static string Status()
        {
            var sb = new StringBuilder("시설 상태:");
            var mgr = UpgradeManager.Instance;

            for (int i = 0; i < Facilities.Length; i++)
            {
                Facility f = Facilities[i];
                string state;

                switch (f.Gate)
                {
                    case GateKind.Node:
                        if (mgr == null) state = "?(매니저 없음)";
                        else if (mgr.GetNodeFromCache(f.NodeId) == null) state = "?(노드 없음)";
                        else state = mgr.IsNodeUnlocked(f.NodeId) ? "[해금]" : "[잠김]";
                        break;

                    case GateKind.ElevatorSeen:
                        state = ElevatorStopUnlockStore.UnlockedCount > 0 ? "[해금]" : "[잠김]";
                        break;

                    default:
                        state = TutorialProgress.IsCompleted ? "[해금]" : "[잠김]";
                        break;
                }

                sb.Append($"\n  <color=#F5C63F>{f.Alias,-10}</color>{f.Label,-22}{state}");
            }

            sb.Append("\n  facility <이름> [off] / facility all");
            return sb.ToString();
        }
    }
}
#endif
