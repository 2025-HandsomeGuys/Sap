// @tags: telemetry, region, dwell, timer, pure-logic

using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// 층(지역)별 체류 시간을 누적한다. 잠수 1회 단위로 쓰인다(balance-csv-design.md §6).
///
/// 매 프레임 도는 코드에서 Dictionary를 만지지 않도록, 현재 구간은 _pending 필드에 모으고
/// 지역이 바뀔 때만 Dictionary로 옮긴다. 프레임당 비용은 float += 하나다.
///
/// Unity 의존이 없으므로 EditMode 테스트가 가능하다.
/// </summary>
public sealed class RegionDwellTracker
{
    private readonly Dictionary<string, float> _seconds = new Dictionary<string, float>();
    private string _current = string.Empty;
    private float _pending;

    /// <summary>현재 지역에 누적한다. 지역이 설정되기 전 호출은 버려진다.</summary>
    public void Tick(float deltaTime)
    {
        if (string.IsNullOrEmpty(_current) || deltaTime <= 0f) return;
        _pending += deltaTime;
    }

    /// <summary>지역 전환. 같은 지역이면 아무것도 하지 않는다(누적이 끊기지 않는다).</summary>
    public void SwitchTo(string region)
    {
        if (region == _current) return;
        Flush();
        _current = region ?? string.Empty;
    }

    /// <summary>
    /// 현재까지의 누적을 JSON 오브젝트 문자열로 낸다.
    /// 현재 구간을 flush한 스냅샷이며, 호출 후에도 계속 누적할 수 있다.
    /// </summary>
    public string ToPayloadObject()
    {
        Flush();

        var sb = new StringBuilder(64);
        sb.Append('{');
        bool first = true;
        foreach (var kv in _seconds)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(TelemetryPayload.EscapeJson(kv.Key)).Append("\":")
              .Append(kv.Value.ToString("0.##", CultureInfo.InvariantCulture));
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>전부 비운다. 잠수 시작 시 호출한다.</summary>
    public void Reset()
    {
        _seconds.Clear();
        _current = string.Empty;
        _pending = 0f;
    }

    /// <summary>현재 구간의 누적을 Dictionary로 옮긴다.</summary>
    private void Flush()
    {
        if (_pending <= 0f || string.IsNullOrEmpty(_current)) { _pending = 0f; return; }

        _seconds.TryGetValue(_current, out float prev);
        _seconds[_current] = prev + _pending;
        _pending = 0f;
    }
}
