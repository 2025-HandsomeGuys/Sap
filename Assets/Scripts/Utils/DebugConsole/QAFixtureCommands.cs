// @tags: qa, fixture, debug, console, commands, balance
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DebugTools
{
    /// <summary>
    /// 세이브 픽스처 콘솔 명령(<c>fx</c>).
    ///
    /// 픽스처 = "이 상황부터 시작" 스냅샷. 세이브 슬롯과 다른 점은
    /// 덮어써지지 않고, 버전관리에 들어가고, 지형 파일까지 한 세트로 묶인다는 것.
    /// 자세한 배경은 <see cref="QAFixtureStore"/> 주석.
    /// </summary>
    internal static class QAFixtureCommands
    {
        /// <summary>복원 후 돌아갈 씬. 지상 허브가 로드 경로가 가장 단순하다.</summary>
        private const string RestartScene = "DemoUpground";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            DebugCommandRegistry.Register("fx", "fx [list|save|load|del|dir] ...",
                "QA 세이브 픽스처. 인자 없으면 목록", Run, "fixture", "픽스처");

            DebugCommandRegistry.SetArgCompleter("fx", CompleteArg);
        }

        /// <summary>Tab 자동완성 후보. load·del 뒤에는 저장된 픽스처 이름을 그대로 내놓는다.</summary>
        private static System.Collections.Generic.IEnumerable<string> CompleteArg(string[] argsSoFar, string prefix)
        {
            if (argsSoFar.Length == 0)
            {
                yield return "list"; yield return "save"; yield return "load";
                yield return "del";  yield return "dir";
                yield break;
            }

            if (argsSoFar.Length != 1) yield break;

            string sub = argsSoFar[0].ToLowerInvariant();
            if (sub != "load" && sub != "del") yield break;

            foreach (var f in QAFixtureStore.List()) yield return f.name;
        }

        private static string Run(string[] args)
        {
            if (args.Length == 0) return List();

            switch (args[0].ToLowerInvariant())
            {
                case "list": case "ls": return List();
                case "save": case "s": return Save(args);
                case "load": case "l": return Load(args);
                case "del": case "rm": return Delete(args);
                case "dir": return QAFixtureStore.Root;
                default: return "사용법: fx [list | save <이름> [메모] | load <이름> [슬롯] | del <이름> | dir]";
            }
        }

        // ───────────────────────────────────────────────────────────

        private static string List()
        {
            var fixtures = QAFixtureStore.List();
            if (fixtures.Count == 0)
                return $"픽스처 없음.  fx save <이름> 으로 현재 상태를 굽는다.\n<color=#7B86A0>{QAFixtureStore.Root}</color>";

            var sb = new StringBuilder();
            sb.Append($"<color=#C7CFE2>픽스처 {fixtures.Count}개</color>");
            foreach (var f in fixtures)
            {
                sb.Append($"\n  <color=#F5C63F>{f.name}</color>");
                if (!f.hasWorld) sb.Append("  <color=#7B86A0>(지형 없음 — 안 판 상태)</color>");
                sb.Append($"\n      <color=#7B86A0>{f.memo}   · {f.savedAt:yy-MM-dd HH:mm}</color>");
            }
            return sb.ToString();
        }

        private static string Save(string[] args)
        {
            if (args.Length < 2) return "사용법: fx save <이름> [메모...]";

            var sm = SaveManager.Instance;
            if (sm == null) return "SaveManager 없음 (메인 메뉴에서는 저장할 상태가 없다)";

            int slot = sm.CurrentSlotIndex;
            string name = args[1];
            string memo = args.Length > 2 ? string.Join(" ", args, 2, args.Length - 2) : "";

            // 픽스처는 '디스크에 있는 파일'을 복사한다 — 메모리 상태를 먼저 내려보내야
            // 방금 캔 광물·방금 판 굴이 스냅샷에 들어간다.
            sm.RefreshReferences();
            sm.Save();
            if (InfinityMapManager.Instance != null) InfinityMapManager.Instance.SaveAllData();

            string error = QAFixtureStore.Capture(name, slot, BuildMeta(memo, slot));
            if (error != null) return $"<color=#E06C6C>{error}</color>";

            return $"픽스처 저장됨: <color=#F5C63F>{name}</color>\n<color=#7B86A0>{QAFixtureStore.PathOf(name)}</color>";
        }

        private static string Load(string[] args)
        {
            if (args.Length < 2) return "사용법: fx load <이름> [슬롯]";

            var sm = SaveManager.Instance;
            if (sm == null) return "SaveManager 없음";

            string name = args[1];
            int slot = sm.CurrentSlotIndex;
            if (args.Length > 2 && int.TryParse(args[2], out int parsed))
            {
                if (parsed < 0 || parsed >= SaveManager.MaxSlots)
                    return $"슬롯 범위는 0~{SaveManager.MaxSlots - 1}";
                slot = parsed;
            }

            string error = QAFixtureStore.Restore(name, slot);
            if (error != null) return $"<color=#E06C6C>{error}</color>";

            // 픽스처로 복원된 회차는 밸런스 분석에서 제외한다(balance-csv-design.md §4).
            // 씬이 재시작되므로 메모리가 아니라 방금 갈아끼운 파일에 직접 쓴다.
            SaveManager.MarkSlotFileAsFixture(slot);

            sm.CurrentSlotIndex = slot;
            PlayerPrefs.SetInt("LastPlayedSlot", slot);
            PlayerPrefs.Save();

            Debug.Log($"[QAFixture] '{name}' → 슬롯 {slot} 복원. " +
                      $"직전 상태는 {QAFixtureStore.BackupPath} 에 백업됨. {RestartScene} 재시작.");

            // 파일만 갈아끼우면 안 먹는다 — 이미 로드된 청크와 매니저 상태가 메모리에 살아 있다.
            // 씬을 다시 올려야 GameManager.OnGeneralSceneLoaded → SaveManager.Load()가 돌고,
            // InfinityMapManager가 새 worldData.bin을 읽는다.
            DebugConsoleOverlayUI.CloseStatic();
            Time.timeScale = 1f;   // 배속이 걸려 있으면 DebugTimeScaleDriver가 다음 프레임에 다시 적용한다
            SceneLoader.LoadScene(RestartScene);

            return null;   // 씬이 갈리므로 콘솔 출력은 의미 없다 (위 Debug.Log로 남긴다)
        }

        private static string Delete(string[] args)
        {
            if (args.Length < 2) return "사용법: fx del <이름>";

            string error = QAFixtureStore.Delete(args[1]);
            if (error != null) return $"<color=#E06C6C>{error}</color>";
            return $"삭제됨: {args[1]}";
        }

        // ───────────────────────────────────────────────────────────

        /// <summary>
        /// meta.txt 본문. 첫 줄이 목록에 표시되는 메모이므로 빈 메모도 한 줄을 차지해야 한다.
        /// </summary>
        private static string BuildMeta(string memo, int slot)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(memo) ? "(메모 없음)" : memo.Trim());
            sb.AppendLine();
            sb.AppendLine($"saved : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"slot  : {slot}");
            sb.AppendLine($"scene : {SceneManager.GetActiveScene().name}");

            var day = DayCycleManager.Instance;
            if (day != null) sb.AppendLine($"day   : {day.GetFormattedDayTimeText()}");

            var stat = UnityEngine.Object.FindFirstObjectByType<PlayerStat>();
            if (stat != null)
            {
                sb.AppendLine($"gold  : {stat.Gold:N0}");
                Vector3 p = stat.transform.position;
                sb.AppendLine($"pos   : ({p.x:0.0}, {p.y:0.0})  청크 {ChunkCoords.ToChunk(p)}");
            }

            return sb.ToString();
        }
    }
}
#endif
