// @tags: telemetry, file, jsonl, sink, quota, persistence

using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// 텔레메트리 라인을 세션별 JSONL 파일에 append한다.
/// Phase 2에서 업로더가 이 디렉토리를 스캔해 '아직 안 보낸 파일'을 그대로 큐로 쓴다(설계 §5).
///
/// 모든 I/O 실패는 삼키고 로그만 남긴다 — 텔레메트리 때문에 게임이 죽으면 안 된다.
/// </summary>
public sealed class TelemetryFileSink
{
    private readonly string _directory;
    private readonly long _maxTotalBytes;

    public string FilePath { get; }

    public TelemetryFileSink(string directory, string fileName, long maxTotalBytes = 50L * 1024 * 1024)
    {
        _directory = directory;
        _maxTotalBytes = maxTotalBytes;

        // 경로 조합조차 던지지 않게 막는다 — 텔레메트리는 어떤 경우에도 게임을 죽이지 않는다
        try { FilePath = Path.Combine(directory, fileName); }
        catch { FilePath = fileName; }
    }

    /// <summary>파일명 규칙: yyyyMMdd-HHmmss_&lt;sessionId 앞 8자&gt;.jsonl</summary>
    public static string BuildFileName(DateTime startedAt, string sessionId)
    {
        string shortId = string.IsNullOrEmpty(sessionId)
            ? "unknown"
            : sessionId.Substring(0, Math.Min(8, sessionId.Length));

        return startedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_" + shortId + ".jsonl";
    }

    public void Append(string[] lines)
    {
        if (lines == null || lines.Length == 0) return;

        try
        {
            Directory.CreateDirectory(_directory);
            File.AppendAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Telemetry] 파일 기록 실패: {e.Message}");
        }
    }

    /// <summary>디렉토리 총합이 상한을 넘으면 오래된 파일부터 지운다. 현재 세션 파일은 남긴다.</summary>
    public void EnforceQuota()
    {
        try
        {
            if (!Directory.Exists(_directory)) return;

            var files = new DirectoryInfo(_directory).GetFiles("*.jsonl");
            long total = 0;
            foreach (var f in files) total += f.Length;
            if (total <= _maxTotalBytes) return;

            Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));

            string currentFull = Path.GetFullPath(FilePath);
            foreach (var f in files)
            {
                if (total <= _maxTotalBytes) break;
                if (string.Equals(f.FullName, currentFull, StringComparison.OrdinalIgnoreCase)) continue;

                long size = f.Length;
                string name = f.Name;
                f.Delete();
                total -= size;
                Debug.Log($"[Telemetry] 용량 상한 초과 — 오래된 로그 삭제: {name}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Telemetry] 용량 정리 실패: {e.Message}");
        }
    }
}
