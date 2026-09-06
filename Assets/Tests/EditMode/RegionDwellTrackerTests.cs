// @tags: test, telemetry, region, dwell, editmode

using NUnit.Framework;

public class RegionDwellTrackerTests
{
    [Test]
    public void 지역_전환_없이_누적하면_한_지역에_모인다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(1.5f);
        t.Tick(1.0f);

        Assert.AreEqual("{\"Dirt\":2.5}", t.ToPayloadObject());
    }

    [Test]
    public void 전환하면_이전_지역_누적이_보존된다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.SwitchTo("Stone");
        t.Tick(3f);

        Assert.AreEqual("{\"Dirt\":2,\"Stone\":3}", t.ToPayloadObject());
    }

    [Test]
    public void 같은_지역으로_전환하면_누적이_끊기지_않는다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.SwitchTo("Dirt");
        t.Tick(3f);

        Assert.AreEqual("{\"Dirt\":5}", t.ToPayloadObject());
    }

    [Test]
    public void 되돌아온_지역은_기존_누적에_더해진다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.SwitchTo("Stone");
        t.Tick(1f);
        t.SwitchTo("Dirt");
        t.Tick(4f);

        Assert.AreEqual("{\"Dirt\":6,\"Stone\":1}", t.ToPayloadObject());
    }

    [Test]
    public void 스냅샷_후에도_계속_누적된다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.ToPayloadObject();
        t.Tick(3f);

        Assert.AreEqual("{\"Dirt\":5}", t.ToPayloadObject());
    }

    [Test]
    public void Reset하면_비어_있다()
    {
        var t = new RegionDwellTracker();
        t.SwitchTo("Dirt");
        t.Tick(2f);
        t.Reset();

        Assert.AreEqual("{}", t.ToPayloadObject());
    }

    [Test]
    public void 지역_설정_전_Tick은_버려진다()
    {
        var t = new RegionDwellTracker();
        t.Tick(5f);

        Assert.AreEqual("{}", t.ToPayloadObject());
    }
}
