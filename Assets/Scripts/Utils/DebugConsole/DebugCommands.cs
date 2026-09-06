// @tags: debug, console, qa, cheat, commands, balance
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using UnityEngine;

namespace DebugTools
{
    /// <summary>
    /// 기본 제공 치트 명령들.
    ///
    /// 새 명령을 추가할 때 이 파일을 고칠 필요는 없다 —
    /// 어느 시스템에서든 <c>[RuntimeInitializeOnLoadMethod]</c> 안에서
    /// <see cref="DebugCommandRegistry.Register"/>를 부르면 콘솔이 자동으로 잡는다.
    /// 시스템별 치트는 그 시스템 옆에 두는 편이 낫다(여기에 다 모으면 참조가 지저분해진다).
    /// </summary>
    internal static class DebugCommands
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterAll()
        {
            DebugCommandRegistry.Register("help", "help", "명령 목록 출력", Help, "?", "도움말");
            DebugCommandRegistry.Register("clear", "clear", "콘솔 로그 지우기", Clear, "cls");

            DebugCommandRegistry.Register("time", "time <배율|reset>",
                "시간 배속. 0.1 ~ 20. 예: time 4 / time reset", Time_, "ts", "배속");

            DebugCommandRegistry.Register("gold", "gold <±금액>",
                "골드 증감. 예: gold 50000 / gold -10000", Gold, "돈");

            DebugCommandRegistry.Register("day", "day [next|am|pm]",
                "날짜·시간대 조작. 인자 없으면 현재 날짜 출력", Day);

            DebugCommandRegistry.Register("stamina", "stamina",
                "스태미나 가득 + 부상·화상·동상·방사능·굴착피로 제거", Stamina, "st");

            DebugCommandRegistry.Register("nap", "nap [횟수|reset|auto]",
                "하루 낮잠 가능 횟수 조작(업그레이드 노드 전 테스트용). 인자 없으면 현황 출력", Nap, "낮잠");

            DebugCommandRegistry.Register("pos", "pos",
                "플레이어 월드 좌표와 청크 좌표 출력", Pos);
        }

        // ───────────────────────────────────────────────────────────
        // 핸들러
        // ───────────────────────────────────────────────────────────

        private static string Help(string[] args)
        {
            if (args.Length == 0) return DebugCommandRegistry.BuildHelpText();

            var cmd = DebugCommandRegistry.Find(args[0]);
            if (cmd == null) return $"그런 명령 없음: {args[0]}";
            return $"<color=#F5C63F>{cmd.Usage}</color>\n  {cmd.Description}";
        }

        private static string Clear(string[] args)
        {
            DebugConsoleOverlayUI.ClearLog();
            return null;
        }

        private static string Time_(string[] args)
        {
            if (args.Length == 0)
                return $"현재 배속 x{DebugTimeScale.Multiplier:0.##}  (사용법: time <배율|reset>)";

            if (args[0].Equals("reset", System.StringComparison.OrdinalIgnoreCase)
                || args[0] == "1")
            {
                DebugTimeScale.ResetToNormal();
                return "배속 x1 — 정상 속도";
            }

            if (!float.TryParse(args[0], out float m))
                return $"숫자가 아님: {args[0]}";

            DebugTimeScale.Set(m);

            string note = DebugTimeScale.Multiplier > 4f
                ? "\n  <color=#F09A3E>주의: 4배 초과에서는 물리 스텝 간 이동거리가 커져 지형 관통·낙하 판정이 실제와 달라질 수 있다.\n" +
                  "  이동·충돌이 걸린 밸런스는 4배 이하에서 볼 것.</color>"
                : "";

            return $"배속 x{DebugTimeScale.Multiplier:0.##}{note}";
        }

        private static string Gold(string[] args)
        {
            if (args.Length == 0) return "사용법: gold <±금액>";
            if (!int.TryParse(args[0], out int amount)) return $"숫자가 아님: {args[0]}";

            var stat = FindPlayerStat();
            if (stat == null) return "PlayerStat을 찾을 수 없다 (지금 씬에 플레이어가 없음)";

            // DayEarningsLedger.Report를 일부러 부르지 않는다.
            // 장부는 추적 안 된 변동을 '기타'로 흡수하도록 설계돼 있고(설계 주석 참고),
            // 디버그로 넣은 돈이 '광물 판매' 같은 실제 카테고리에 섞이면
            // 하루 정산 화면으로 밸런스를 읽을 때 그 수치가 거짓말이 된다.
            if (amount >= 0) stat.AddGold(amount);
            else if (!stat.SpendGold(-amount)) return $"골드 부족 (보유 {stat.Gold:N0})";

            SaveManager.Instance?.MarkRunAsFixture();

            return $"골드 {(amount >= 0 ? "+" : "")}{amount:N0} → 보유 {stat.Gold:N0}";
        }

        private static string Day(string[] args)
        {
            var dc = DayCycleManager.Instance;
            if (dc == null) return "DayCycleManager 없음";

            if (args.Length == 0) return dc.GetFormattedDayTimeText();

            switch (args[0].ToLowerInvariant())
            {
                case "next": dc.AdvanceToNextDay(); break;
                case "am": case "morning": dc.SetMorning(); break;
                case "pm": case "afternoon": dc.SetAfternoon(); break;
                default: return "사용법: day [next|am|pm]";
            }

            SaveManager.Instance?.MarkRunAsFixture();

            // 하루를 건너뛰는 것이지 '수면 정산'이 아니다 — 침대 정산 연출·장부 리셋은 돌지 않는다.
            return $"{dc.GetFormattedDayTimeText()}  <color=#7B86A0>(정산 연출은 건너뜀)</color>";
        }

        private static string Nap(string[] args)
        {
            var mgr = NapManager.Instance;
            if (mgr == null) return "NapManager 없음 (아직 씬이 로드되지 않았을 수 있음)";

            if (args.Length == 0)
            {
                string ovr = mgr.DebugMaxOverride >= 0
                    ? $"  <color=#F09A3E>(디버그 override = {mgr.DebugMaxOverride})</color>" : "";
                return $"낮잠  최대 {mgr.MaxNapsPerDay}회 / 사용 {mgr.UsedToday}회 / 남음 {mgr.RemainingNaps}회{ovr}" +
                       "\n  사용법: nap <횟수>  (하루 가능 횟수 강제) / nap reset (사용량 0) / nap auto (업그레이드 값 사용)";
            }

            switch (args[0].ToLowerInvariant())
            {
                case "reset":
                    mgr.ResetUsedToday();
                    break;
                case "auto": case "clear":
                    mgr.SetDebugMaxOverride(-1);
                    break;
                default:
                    if (!int.TryParse(args[0], out int n) || n < 0)
                        return "사용법: nap <횟수> | nap reset | nap auto";
                    mgr.SetDebugMaxOverride(n);
                    break;
            }

            SaveManager.Instance?.MarkRunAsFixture();
            return $"낮잠  최대 {mgr.MaxNapsPerDay}회 / 사용 {mgr.UsedToday}회 / 남음 {mgr.RemainingNaps}회";
        }

        private static string Stamina(string[] args)
        {
            var sm = Object.FindFirstObjectByType<StaminaManager>();
            if (sm == null) return "StaminaManager 없음";

            // 순서 중요 — 감소 필드(부상·굴착피로 등)를 먼저 0으로 만들어야
            // RefillStamina가 '깎이지 않은' 최대치까지 채운다. (StaminaManager.RefillStamina 주석)
            sm.RecoverStatus(9999f, 9999f, 9999f, 9999f);
            sm.ResetDiggingReduction();
            sm.RefillStamina();

            SaveManager.Instance?.MarkRunAsFixture();

            return "스태미나 회복 + 상태이상 제거";
        }

        private static string Pos(string[] args)
        {
            var stat = FindPlayerStat();
            if (stat == null) return "플레이어를 찾을 수 없다";

            Vector3 p = stat.transform.position;
            Vector2Int chunk = ChunkCoords.ToChunk(p);
            return $"월드 ({p.x:0.00}, {p.y:0.00})   청크 ({chunk.x}, {chunk.y})";
        }

        // ───────────────────────────────────────────────────────────

        private static PlayerStat s_playerStat;

        /// <summary>씬이 바뀌면 캐시가 죽으므로 null일 때만 다시 찾는다.</summary>
        private static PlayerStat FindPlayerStat()
        {
            if (s_playerStat == null) s_playerStat = Object.FindFirstObjectByType<PlayerStat>();
            return s_playerStat;
        }
    }
}
#endif
