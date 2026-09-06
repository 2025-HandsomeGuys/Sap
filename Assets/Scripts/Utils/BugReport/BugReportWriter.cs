// @tags: bug-report, qa, file-io, writer
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BugReport
{
    public struct BugReportWriteResult
    {
        public bool success;
        public string folderPath;
        public string error;
    }

    /// <summary>
    /// 리포트 폴더 생성 + 파일 쓰기만 담당. 게임 상태를 전혀 모른다.
    /// 이미 만들어진 데이터와 바이트 배열만 받는다.
    /// </summary>
    public static class BugReportWriter
    {
        public static string RootFolder =>
            Path.Combine(Application.persistentDataPath, "BugReports");

        /// <summary>정렬 가능한 타임스탬프 폴더명.</summary>
        public static string FolderName(DateTime when) => when.ToString("yyyy-MM-dd_HHmmss");

        /// <summary>
        /// 리포트 파일 4개를 쓴다.
        /// rootOverride는 테스트가 임시 폴더를 쓰기 위한 것 — null이면 RootFolder를 쓴다.
        /// </summary>
        public static BugReportWriteResult Write(
            BugReportData data, byte[] pngBytes, string saveJson,
            string rootOverride, DateTime when)
        {
            string root = string.IsNullOrEmpty(rootOverride) ? RootFolder : rootOverride;

            try
            {
                string dir = Path.Combine(root, FolderName(when));

                // 같은 초에 두 번 눌러도 앞 리포트를 덮지 않는다.
                if (Directory.Exists(dir))
                {
                    int suffix = 2;
                    string candidate;
                    do { candidate = $"{dir}_{suffix++}"; } while (Directory.Exists(candidate));
                    dir = candidate;
                }
                Directory.CreateDirectory(dir);

                // 스크린샷 실패가 리포트 전체를 막으면 안 된다 — 있으면 쓰고 없으면 넘어간다.
                if (pngBytes != null && pngBytes.Length > 0)
                    File.WriteAllBytes(Path.Combine(dir, "screenshot.png"), pngBytes);

                File.WriteAllText(Path.Combine(dir, "report.json"),
                    JsonUtility.ToJson(data, true), Encoding.UTF8);

                File.WriteAllText(Path.Combine(dir, "log.txt"),
                    FormatLog(), Encoding.UTF8);

                File.WriteAllText(Path.Combine(dir, "save.json"),
                    string.IsNullOrEmpty(saveJson) ? "{}" : saveJson, Encoding.UTF8);

                return new BugReportWriteResult { success = true, folderPath = dir };
            }
            catch (Exception e)
            {
                return new BugReportWriteResult { success = false, error = e.Message };
            }
        }

        private static string FormatLog()
        {
            var sb = new StringBuilder(8192);
            foreach (var entry in BugReportLogBuffer.Snapshot())
            {
                sb.Append('[').Append(entry.time.ToString("F2")).Append("] ")
                  .Append(entry.type).Append(": ").AppendLine(entry.condition);
                if (!string.IsNullOrEmpty(entry.stackTrace))
                    sb.AppendLine(entry.stackTrace);
            }
            return sb.ToString();
        }
    }
}
#endif
