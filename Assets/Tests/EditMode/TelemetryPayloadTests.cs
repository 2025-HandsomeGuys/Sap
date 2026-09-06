using NUnit.Framework;

/// <summary>TelemetryPayload — JSON 조각 생성·이스케이프·타입별 포맷 검증.</summary>
public class TelemetryPayloadTests
{
    [Test]
    public void Empty_ReturnsEmptyObject()
    {
        Assert.AreEqual("{}", TelemetryPayload.New().ToJsonObject());
    }

    [Test]
    public void Add_ProducesKeyValuePairs()
    {
        string json = TelemetryPayload.New()
            .Add("depth", 120)
            .Add("region", "Stone")
            .ToJsonObject();

        Assert.AreEqual("{\"depth\":120,\"region\":\"Stone\"}", json);
    }

    [Test]
    public void Add_BoolIsLowercase()
    {
        Assert.AreEqual("{\"ok\":true,\"bad\":false}",
            TelemetryPayload.New().Add("ok", true).Add("bad", false).ToJsonObject());
    }

    [Test]
    public void Add_FloatUsesInvariantCultureAndTwoDecimals()
    {
        // 한국어 로케일에서도 소수점이 쉼표가 되면 JSON이 깨진다
        Assert.AreEqual("{\"d\":12.34}",
            TelemetryPayload.New().Add("d", 12.339f).ToJsonObject());
    }

    [Test]
    public void Add_NullStringBecomesEmptyString()
    {
        Assert.AreEqual("{\"s\":\"\"}", TelemetryPayload.New().Add("s", (string)null).ToJsonObject());
    }

    [Test]
    public void EscapeJson_HandlesQuotesBackslashesAndControlChars()
    {
        Assert.AreEqual("a\\\"b", TelemetryPayload.EscapeJson("a\"b"));
        Assert.AreEqual("a\\\\b", TelemetryPayload.EscapeJson("a\\b"));
        Assert.AreEqual("a\\nb", TelemetryPayload.EscapeJson("a\nb"));
        Assert.AreEqual("a\\tb", TelemetryPayload.EscapeJson("a\tb"));
    }

    [Test]
    public void EscapeJson_StripsNewlinesFromStackTrace()
    {
        // error 이벤트의 스택트레이스가 JSONL 한 줄을 깨뜨리면 안 된다
        string json = TelemetryPayload.New().Add("trace", "line1\nline2").ToJsonObject();
        Assert.IsFalse(json.Contains("\n"), "직렬화 결과에 실제 개행이 남으면 JSONL이 깨진다");
    }

    [Test]
    public void AddRaw_중첩_오브젝트를_따옴표_없이_넣는다()
    {
        string json = TelemetryPayload.New()
            .Add("result", "return")
            .AddRaw("region_seconds", "{\"Dirt\":12.5,\"Stone\":3}")
            .ToJsonObject();

        Assert.AreEqual("{\"result\":\"return\",\"region_seconds\":{\"Dirt\":12.5,\"Stone\":3}}", json);
    }

    [Test]
    public void AddRaw_null이면_빈_오브젝트를_넣는다()
    {
        string json = TelemetryPayload.New().AddRaw("minerals", null).ToJsonObject();

        Assert.AreEqual("{\"minerals\":{}}", json);
    }
}
