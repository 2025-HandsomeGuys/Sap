using NUnit.Framework;

/// <summary>TelemetryBuffer — 누적·드레인·상한 초과 처리 검증.</summary>
public class TelemetryBufferTests
{
    [Test]
    public void Drain_ReturnsLinesInOrderAndClears()
    {
        var buf = new TelemetryBuffer();
        buf.Add("a");
        buf.Add("b");

        var drained = buf.Drain();

        Assert.AreEqual(new[] { "a", "b" }, drained);
        Assert.AreEqual(0, buf.Count);
    }

    [Test]
    public void Drain_OnEmptyReturnsEmptyArray()
    {
        var buf = new TelemetryBuffer();
        Assert.AreEqual(0, buf.Drain().Length);
    }

    [Test]
    public void Add_IgnoresNullAndEmpty()
    {
        var buf = new TelemetryBuffer();
        buf.Add(null);
        buf.Add("");
        Assert.AreEqual(0, buf.Count);
    }

    [Test]
    public void Add_BeyondCapacity_DropsNewestAndCounts()
    {
        var buf = new TelemetryBuffer(maxLines: 2);
        buf.Add("a");
        buf.Add("b");
        buf.Add("c");   // 버려짐

        Assert.IsTrue(buf.IsFull);
        Assert.AreEqual(1, buf.DroppedCount);
        // 초반 이벤트가 살아남아야 한다
        Assert.AreEqual(new[] { "a", "b" }, buf.Drain());
    }

    [Test]
    public void Drain_ResetsFullState()
    {
        var buf = new TelemetryBuffer(maxLines: 1);
        buf.Add("a");
        buf.Drain();

        Assert.IsFalse(buf.IsFull);
        buf.Add("b");
        Assert.AreEqual(1, buf.Count);
    }
}
