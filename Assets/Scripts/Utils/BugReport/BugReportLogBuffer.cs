// @tags: bug-report, qa, log, ring-buffer
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System.Collections.Generic;
using UnityEngine;

namespace BugReport
{
    /// <summary>
    /// 최근 로그 N줄을 담는 링버퍼. Application.logMessageReceived를 구독한다.
    ///
    /// 세션 누적 Error/Exception 카운트는 버퍼에서 밀려나도 유지된다 —
    /// "이 리포트 이전에 이미 예외가 나 있었다"가 중요한 단서이기 때문.
    /// </summary>
    public static class BugReportLogBuffer
    {
        public const int Capacity = 300;

        private static readonly BugReportLogEntry[] s_ring = new BugReportLogEntry[Capacity];
        private static int s_writeIndex;   // 다음에 쓸 위치
        private static int s_count;        // 채워진 개수 (최대 Capacity)
        private static bool s_installed;

        public static int ErrorCount { get; private set; }
        public static int ExceptionCount { get; private set; }

        /// <summary>로그 콜백 구독. 중복 호출해도 한 번만 걸린다.</summary>
        public static void Install()
        {
            if (s_installed) return;
            s_installed = true;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
            => Add(condition, stackTrace, type, Time.realtimeSinceStartup);

        /// <summary>버퍼에 한 줄 추가. 테스트에서 직접 호출한다.</summary>
        public static void Add(string condition, string stackTrace, LogType type, float time)
        {
            // 스택은 에러 계열만 보관 — 전부 담으면 로그 파일이 수 MB가 된다.
            bool keepStack = type == LogType.Error
                          || type == LogType.Exception
                          || type == LogType.Assert;

            s_ring[s_writeIndex] = new BugReportLogEntry
            {
                condition = condition ?? "",
                stackTrace = keepStack ? (stackTrace ?? "") : "",
                type = type,
                time = time,
            };

            s_writeIndex = (s_writeIndex + 1) % Capacity;
            if (s_count < Capacity) s_count++;

            if (type == LogType.Error) ErrorCount++;
            else if (type == LogType.Exception) ExceptionCount++;
        }

        /// <summary>오래된 것 → 최신 순으로 복사해 반환.</summary>
        public static List<BugReportLogEntry> Snapshot()
        {
            var result = new List<BugReportLogEntry>(s_count);
            int start = (s_writeIndex - s_count + Capacity) % Capacity;
            for (int i = 0; i < s_count; i++)
                result.Add(s_ring[(start + i) % Capacity]);
            return result;
        }

        public static void Clear()
        {
            s_writeIndex = 0;
            s_count = 0;
            ErrorCount = 0;
            ExceptionCount = 0;
        }
    }
}
#endif
