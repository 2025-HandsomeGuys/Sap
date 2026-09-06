// @tags: debug, console, qa, command, registry
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DebugTools
{
    /// <summary>명령 하나. 반환 문자열이 콘솔에 출력된다(null이면 출력 없음).</summary>
    public delegate string DebugCommandHandler(string[] args);

    /// <summary>
    /// 인자 자동완성 후보를 내놓는다. <paramref name="argsSoFar"/>는 <b>이미 확정된</b> 인자들
    /// (지금 타이핑 중인 토큰은 빠져 있다) — 몇 번째 인자를 완성할 차례인지 이걸로 안다.
    /// 접두사 걸러내기는 등록소가 하므로 후보를 다 내놔도 된다.
    /// </summary>
    public delegate IEnumerable<string> DebugArgCompleter(string[] argsSoFar, string prefix);

    public class DebugCommand
    {
        public string Name;
        public string Usage;        // 예: "time <배율|reset>"
        public string Description;
        public string[] Aliases;
        public DebugCommandHandler Handler;
        public DebugArgCompleter ArgCompleter;   // 없으면 인자 자동완성 없음
    }

    /// <summary>
    /// 명령 등록소. 새 치트를 추가하는 비용을 "Register 한 줄"로 유지하는 것이 목적이다.
    /// 밸런싱 단계에는 필요한 치트가 계속 늘어나므로, 여기가 무거워지면 아무도 안 쓴다.
    ///
    /// 등록은 어디서든 가능하다 — 시스템별로 자기 파일에서
    /// <c>[RuntimeInitializeOnLoadMethod]</c>로 Register를 호출하면 콘솔이 자동으로 안다.
    /// </summary>
    public static class DebugCommandRegistry
    {
        private static readonly List<DebugCommand> s_commands = new List<DebugCommand>();
        private static readonly Dictionary<string, DebugCommand> s_lookup =
            new Dictionary<string, DebugCommand>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<DebugCommand> All => s_commands;

        public static void Register(string name, string usage, string description,
            DebugCommandHandler handler, params string[] aliases)
        {
            if (string.IsNullOrWhiteSpace(name) || handler == null) return;
            if (s_lookup.ContainsKey(name)) return;   // 도메인 리로드 비활성 시 중복 등록 방지

            var cmd = new DebugCommand
            {
                Name = name,
                Usage = string.IsNullOrEmpty(usage) ? name : usage,
                Description = description,
                Aliases = aliases ?? Array.Empty<string>(),
                Handler = handler,
            };

            s_commands.Add(cmd);
            s_lookup[name] = cmd;
            foreach (var alias in cmd.Aliases)
                if (!string.IsNullOrWhiteSpace(alias)) s_lookup[alias] = cmd;
        }

        public static DebugCommand Find(string name) =>
            name != null && s_lookup.TryGetValue(name, out var cmd) ? cmd : null;

        /// <summary>
        /// 한 줄 실행. 파싱·미등록·예외를 전부 흡수해서 문자열로 돌려준다.
        /// 치트 하나가 던진 예외로 콘솔 자체가 죽으면 안 된다.
        /// </summary>
        public static string Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            string[] parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var cmd = Find(parts[0]);
            if (cmd == null) return $"<color=#E06C6C>알 수 없는 명령: {parts[0]}</color>  (help 로 목록)";

            string[] args = new string[parts.Length - 1];
            Array.Copy(parts, 1, args, 0, args.Length);

            try
            {
                return cmd.Handler(args);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return $"<color=#E06C6C>실행 실패: {e.GetType().Name} — {e.Message}</color>";
            }
        }

        /// <summary>입력 중인 접두사에 맞는 명령 이름들 (Tab 자동완성용).</summary>
        public static List<string> Complete(string prefix)
        {
            var result = new List<string>();
            if (prefix == null) return result;

            foreach (var cmd in s_commands)
                if (cmd.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    result.Add(cmd.Name);

            return result;
        }

        /// <summary>
        /// 명령이 자기 인자 후보를 내놓게 한다. 명령 등록 뒤에 부른다.
        /// 인자 형태는 명령마다 다르므로 등록소는 목록을 모르고, 아는 쪽(명령)이 알려준다.
        /// </summary>
        public static void SetArgCompleter(string commandName, DebugArgCompleter completer)
        {
            DebugCommand cmd = Find(commandName);
            if (cmd != null && completer != null) cmd.ArgCompleter = completer;
        }

        /// <summary>입력 중인 인자의 후보 (Tab 자동완성용). 별칭으로 불러도 된다.</summary>
        public static List<string> CompleteArgs(string commandName, string[] argsSoFar, string prefix)
        {
            var result = new List<string>();

            DebugCommand cmd = Find(commandName);
            if (cmd?.ArgCompleter == null) return result;

            prefix ??= "";
            foreach (string candidate in cmd.ArgCompleter(argsSoFar ?? Array.Empty<string>(), prefix))
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) result.Add(candidate);
            }

            return result;
        }

        public static string BuildHelpText()
        {
            var sb = new StringBuilder();
            sb.Append("<color=#C7CFE2>사용 가능한 명령</color>");
            foreach (var cmd in s_commands)
            {
                sb.Append("\n  <color=#F5C63F>");
                sb.Append(cmd.Usage);
                sb.Append("</color>");
                if (cmd.Aliases.Length > 0)
                {
                    sb.Append("  <color=#7B86A0>[");
                    sb.Append(string.Join(", ", cmd.Aliases));
                    sb.Append("]</color>");
                }
                sb.Append("\n      <color=#7B86A0>");
                sb.Append(cmd.Description);
                sb.Append("</color>");
            }
            return sb.ToString();
        }
    }
}
#endif
