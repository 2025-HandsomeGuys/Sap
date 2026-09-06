// @tags: telemetry, session, context, anon-id, common-fields

using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// 한 세션의 공통 필드를 소유하고, 이벤트 하나를 JSONL 한 줄로 조립한다.
/// 키 이름은 Phase 2 Supabase 테이블 컬럼과 1:1 대응한다(설계 §5).
/// Unity 의존은 PlayerPrefs 하나뿐이라 EditMode 테스트가 가능하다.
/// </summary>
public sealed class TelemetrySession
{
    private const string AnonIdKey = "telemetry.anonId";

    private readonly string _anonId;
    private readonly string _sessionId;
    private readonly string _buildType;
    private readonly string _version;

    private int _seq;

    /// <summary>현재 일차. 게임이 아직 설정하지 않았으면 0.</summary>
    public int Day { get; set; }
    /// <summary>현재 지역(TileType 이름). 미설정이면 빈 문자열.</summary>
    public string Region { get; set; } = string.Empty;
    /// <summary>현재 깊이(m).</summary>
    public float Depth { get; set; }
    /// <summary>누적 플레이 시간(초).</summary>
    public float PlaytimeTotal { get; set; }
    /// <summary>회차 식별자. 뉴게임마다 새로 발급되어 세이브에 영속한다. 미설정이면 빈 문자열.</summary>
    public string RunId { get; set; } = string.Empty;
    /// <summary>QA 픽스처·디버그 명령이 손댄 회차인지. true면 밸런스 분석에서 제외한다.</summary>
    public bool IsFixture { get; set; }

    public int Seq => _seq;

    public TelemetrySession(string anonId, string sessionId, string buildType, string version)
    {
        _anonId = anonId ?? string.Empty;
        _sessionId = sessionId ?? string.Empty;
        _buildType = buildType ?? string.Empty;
        _version = version ?? string.Empty;
    }

    /// <summary>이벤트 하나를 JSONL 한 줄로 조립한다. 호출마다 seq가 1 증가한다.</summary>
    public string BuildLine(string eventName, string payloadJsonObject)
    {
        var sb = new StringBuilder(256);
        sb.Append('{');
        Str(sb, "anon_id", _anonId);        sb.Append(',');
        Str(sb, "session_id", _sessionId);  sb.Append(',');
        Str(sb, "run_id", RunId);           sb.Append(',');
        Bool(sb, "is_fixture", IsFixture);  sb.Append(',');
        Num(sb, "seq", _seq);               sb.Append(',');
        Str(sb, "event", eventName);        sb.Append(',');
        Str(sb, "build_type", _buildType);  sb.Append(',');
        Str(sb, "version", _version);       sb.Append(',');
        Num(sb, "playtime", Mathf.RoundToInt(PlaytimeTotal)); sb.Append(',');
        Num(sb, "day", Day);                sb.Append(',');
        Str(sb, "region", Region);          sb.Append(',');

        sb.Append("\"depth\":").Append(Depth.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"payload\":").Append(string.IsNullOrEmpty(payloadJsonObject) ? "{}" : payloadJsonObject);
        sb.Append('}');

        _seq++;
        return sb.ToString();
    }

    /// <summary>익명 식별자를 PlayerPrefs에서 읽고, 없으면 새로 만들어 저장한다.</summary>
    public static string LoadOrCreateAnonId()
    {
        string id = PlayerPrefs.GetString(AnonIdKey, string.Empty);
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(AnonIdKey, id);
            PlayerPrefs.Save();
        }
        return id;
    }

    private static void Str(StringBuilder sb, string key, string value)
    {
        sb.Append('"').Append(key).Append("\":\"")
          .Append(TelemetryPayload.EscapeJson(value ?? string.Empty)).Append('"');
    }

    private static void Num(StringBuilder sb, string key, int value)
    {
        sb.Append('"').Append(key).Append("\":")
          .Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Bool(StringBuilder sb, string key, bool value)
    {
        sb.Append('"').Append(key).Append("\":")
          .Append(value ? "true" : "false");
    }
}
