// @tags: telemetry, static, entry-point, log, facade

using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 텔레메트리 정적 진입점 (DayEarningsLedger.Report 패턴).
/// 게임플레이 코드는 Telemetry.Log() 한 줄만 알면 되고, 버퍼·파일 기록은 뒤에서 처리한다.
///
/// Phase 1은 로컬 JSONL 기록까지만 한다. 원격 전송은 Phase 2에서
/// 이 디렉토리를 스캔하는 별도 소비자(TelemetryUploader)로 추가된다 — 이 클래스는 바뀌지 않는다.
/// </summary>
public static class Telemetry
{
    private const string FolderName = "telemetry";

    // Application.logMessageReceived는 백그라운드 스레드에서도 올라온다.
    // 버퍼·StringBuilder가 스레드 안전하지 않으므로 진입점에서 잠근다(이벤트 빈도가 낮아 비용은 무시할 수준).
    private static readonly object _gate = new object();

    private static TelemetrySession _session;
    private static TelemetryBuffer _buffer;
    private static TelemetryFileSink _sink;
    private static bool _initialized;

    /// <summary>
    /// 수집 활성화 게이트. Phase 2에서 옵트아웃 토글이 이 값을 끈다(설계 §6).
    /// 꺼져 있으면 Log()가 즉시 반환한다.
    /// </summary>
    public static bool IsEnabled { get; set; } = true;

    /// <summary>에디터에서 이벤트를 콘솔에도 출력한다(기본 꺼짐).</summary>
    public static bool EchoToConsole { get; set; }

    /// <summary>현재 세션 컨텍스트. 초기화 전에는 null.</summary>
    public static TelemetrySession Context => _session;

    public static string LogDirectory => Path.Combine(Application.persistentDataPath, FolderName);

    public static void Init()
    {
        if (_initialized) return;

        DateTime now = DateTime.Now;
        string sessionId = Guid.NewGuid().ToString("N");

        _session = new TelemetrySession(
            TelemetrySession.LoadOrCreateAnonId(),
            sessionId,
            BuildType(),
            Application.version);

        _buffer = new TelemetryBuffer();
        _sink = new TelemetryFileSink(LogDirectory, TelemetryFileSink.BuildFileName(now, sessionId));
        _initialized = true;

        _sink.EnforceQuota();
        Debug.Log($"[Telemetry] 시작 — {_sink.FilePath}");
    }

    /// <summary>이벤트 기록. payload가 null이면 빈 오브젝트로 기록된다.</summary>
    public static void Log(string eventName, TelemetryPayload payload = null)
    {
        if (!IsEnabled || !_initialized || string.IsNullOrEmpty(eventName)) return;

        try
        {
            string line;
            bool full;
            lock (_gate)
            {
                if (!_initialized) return;
                line = _session.BuildLine(eventName, payload?.ToJsonObject() ?? "{}");
                _buffer.Add(line);
                full = _buffer.IsFull;
            }

            if (EchoToConsole) Debug.Log($"[Telemetry] {line}");
            if (full) Flush();
        }
        catch (Exception e)
        {
            // 텔레메트리 예외가 게임플레이로 새어나가면 안 된다.
            // LogWarning은 error 이벤트 필터에 걸리지 않으므로 재귀하지 않는다.
            Debug.LogWarning($"[Telemetry] 기록 실패 ({eventName}): {e.Message}");
        }
    }

    /// <summary>버퍼를 파일로 내린다.</summary>
    public static void Flush()
    {
        string[] lines;
        lock (_gate)
        {
            if (!_initialized) return;
            lines = _buffer.Drain();
        }
        _sink.Append(lines);
    }

    /// <summary>세션 종료 — 마지막 플러시 후 비활성화한다.</summary>
    public static void Shutdown()
    {
        Flush();
        lock (_gate) { _initialized = false; }
    }

    private static string BuildType()
    {
#if DEMO_BUILD
        return "demo";
#else
        return "ea";
#endif
    }
}
