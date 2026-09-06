// @tags: telemetry, buffer, queue, flush

using System.Collections.Generic;

/// <summary>
/// 완성된 JSONL 라인을 플러시 전까지 모아 두는 메모리 버퍼.
/// 상한을 두어 플러시가 멈춰도 메모리가 무한정 늘지 않게 한다.
/// 상한 도달 시 새 줄을 버린다 — 세션 초반 이벤트가 분석에 더 중요하기 때문.
/// </summary>
public sealed class TelemetryBuffer
{
    private readonly List<string> _lines;
    private readonly int _maxLines;

    public int Count => _lines.Count;
    public bool IsFull => _lines.Count >= _maxLines;
    /// <summary>상한 초과로 버려진 줄 수(누적).</summary>
    public int DroppedCount { get; private set; }

    public TelemetryBuffer(int maxLines = 512)
    {
        _maxLines = maxLines < 1 ? 1 : maxLines;
        _lines = new List<string>(_maxLines);
    }

    public void Add(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        if (IsFull)
        {
            DroppedCount++;
            return;
        }
        _lines.Add(line);
    }

    /// <summary>현재 내용을 반환하고 버퍼를 비운다.</summary>
    public string[] Drain()
    {
        if (_lines.Count == 0) return System.Array.Empty<string>();

        var result = _lines.ToArray();
        _lines.Clear();
        return result;
    }
}
