using NUnit.Framework;

/// <summary>TelemetrySession — 공통 필드 주입·seq 증가·JSON 라인 형식 검증.</summary>
public class TelemetrySessionTests
{
    private TelemetrySession NewSession() =>
        new TelemetrySession("anon-1", "sess-1", "ea", "0.1.0");

    [Test]
    public void BuildLine_ContainsAllCommonFields()
    {
        var s = NewSession();
        s.Day = 3;
        s.Region = "Stone";
        s.Depth = 120.5f;
        s.PlaytimeTotal = 3600f;

        string line = s.BuildLine("dive_end", "{\"result\":\"return\"}");

        StringAssert.Contains("\"anon_id\":\"anon-1\"", line);
        StringAssert.Contains("\"session_id\":\"sess-1\"", line);
        StringAssert.Contains("\"event\":\"dive_end\"", line);
        StringAssert.Contains("\"build_type\":\"ea\"", line);
        StringAssert.Contains("\"version\":\"0.1.0\"", line);
        StringAssert.Contains("\"day\":3", line);
        StringAssert.Contains("\"region\":\"Stone\"", line);
        StringAssert.Contains("\"depth\":120.5", line);
        StringAssert.Contains("\"playtime\":3600", line);
        StringAssert.Contains("\"payload\":{\"result\":\"return\"}", line);
    }

    [Test]
    public void BuildLine_SeqIncrementsFromZero()
    {
        var s = NewSession();
        StringAssert.Contains("\"seq\":0", s.BuildLine("a", "{}"));
        StringAssert.Contains("\"seq\":1", s.BuildLine("b", "{}"));
        StringAssert.Contains("\"seq\":2", s.BuildLine("c", "{}"));
        Assert.AreEqual(3, s.Seq);
    }

    [Test]
    public void BuildLine_HasNoNewline()
    {
        // JSONL은 한 이벤트가 정확히 한 줄이어야 한다
        Assert.IsFalse(NewSession().BuildLine("a", "{}").Contains("\n"));
    }

    [Test]
    public void BuildLine_EmptyPayloadIsValidJson()
    {
        StringAssert.Contains("\"payload\":{}", NewSession().BuildLine("heartbeat", "{}"));
    }

    [Test]
    public void BuildLine_EscapesRegionName()
    {
        var s = NewSession();
        s.Region = "a\"b";
        StringAssert.Contains("\"region\":\"a\\\"b\"", s.BuildLine("a", "{}"));
    }

    [Test]
    public void BuildLine_run_id와_is_fixture를_공통_필드로_싣는다()
    {
        var session = new TelemetrySession("anon1", "sess1", "ea", "1.0.0");
        session.RunId = "run-abc";
        session.IsFixture = true;

        string line = session.BuildLine("dive_end", "{}");

        StringAssert.Contains("\"run_id\":\"run-abc\"", line);
        StringAssert.Contains("\"is_fixture\":true", line);
    }

    [Test]
    public void BuildLine_미설정이면_run_id는_빈문자열이고_is_fixture는_false다()
    {
        var session = new TelemetrySession("anon1", "sess1", "ea", "1.0.0");

        string line = session.BuildLine("session_start", "{}");

        StringAssert.Contains("\"run_id\":\"\"", line);
        StringAssert.Contains("\"is_fixture\":false", line);
    }
}
