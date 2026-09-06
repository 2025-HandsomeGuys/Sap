// @tags: telemetry, payload, json, builder, serialization

using System.Globalization;
using System.Text;

/// <summary>
/// 이벤트별 필드를 JSON 오브젝트 문자열로 누적하는 빌더.
/// 호출 시점에 바로 문자열로 굳혀 Dictionary 박싱·할당을 피한다(설계 §4 성능).
/// 개행은 전부 이스케이프되므로 JSONL 한 줄이 깨지지 않는다.
/// </summary>
public sealed class TelemetryPayload
{
    private readonly StringBuilder _sb = new StringBuilder(128);

    public static TelemetryPayload New() => new TelemetryPayload();

    public TelemetryPayload Add(string key, string value)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":\"")
           .Append(EscapeJson(value ?? string.Empty)).Append('"');
        return this;
    }

    public TelemetryPayload Add(string key, int value)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":")
           .Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public TelemetryPayload Add(string key, float value)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":")
           .Append(value.ToString("0.##", CultureInfo.InvariantCulture));
        return this;
    }

    public TelemetryPayload Add(string key, bool value)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":")
           .Append(value ? "true" : "false");
        return this;
    }

    /// <summary>
    /// 이미 완성된 JSON 조각(오브젝트·배열)을 값 자리에 그대로 넣는다.
    /// 이스케이프하지 않으므로 호출자가 올바른 JSON을 보장해야 한다 —
    /// 중첩 오브젝트(region_seconds, minerals) 전용이다.
    /// </summary>
    public TelemetryPayload AddRaw(string key, string rawJson)
    {
        Separate();
        _sb.Append('"').Append(EscapeJson(key)).Append("\":")
           .Append(string.IsNullOrEmpty(rawJson) ? "{}" : rawJson);
        return this;
    }

    /// <summary>항상 중괄호로 감싼 JSON 오브젝트를 돌려준다. 비어 있으면 "{}".</summary>
    public string ToJsonObject() => "{" + _sb + "}";

    /// <summary>따옴표를 제외한 JSON 문자열 본문 이스케이프.</summary>
    public static string EscapeJson(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length + 8);
        foreach (char c in raw)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    // 그 외 제어문자는 버린다(스택트레이스에 섞여 들어올 수 있다)
                    if (c >= ' ') sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private void Separate()
    {
        if (_sb.Length > 0) _sb.Append(',');
    }
}
