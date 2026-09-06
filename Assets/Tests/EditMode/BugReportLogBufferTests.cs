// @tags: bug-report, test, editmode, log-buffer
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using NUnit.Framework;
using UnityEngine;
using BugReport;

public class BugReportLogBufferTests
{
    [SetUp]
    public void SetUp() => BugReportLogBuffer.Clear();

    [Test]
    public void Snapshot_ReturnsEntriesInInsertionOrder()
    {
        BugReportLogBuffer.Add("first", "", LogType.Log, 1f);
        BugReportLogBuffer.Add("second", "", LogType.Log, 2f);

        var snap = BugReportLogBuffer.Snapshot();

        Assert.AreEqual(2, snap.Count);
        Assert.AreEqual("first", snap[0].condition);
        Assert.AreEqual("second", snap[1].condition);
    }

    [Test]
    public void Snapshot_WhenOverCapacity_KeepsNewestAndDropsOldest()
    {
        int over = BugReportLogBuffer.Capacity + 5;
        for (int i = 0; i < over; i++)
            BugReportLogBuffer.Add($"msg{i}", "", LogType.Log, i);

        var snap = BugReportLogBuffer.Snapshot();

        Assert.AreEqual(BugReportLogBuffer.Capacity, snap.Count);
        Assert.AreEqual("msg5", snap[0].condition, "가장 오래된 5줄이 밀려나야 한다");
        Assert.AreEqual($"msg{over - 1}", snap[snap.Count - 1].condition);
    }

    [Test]
    public void Add_StoresStackTraceOnlyForErrorLevels()
    {
        BugReportLogBuffer.Add("info", "STACK", LogType.Log, 1f);
        BugReportLogBuffer.Add("bad", "STACK", LogType.Error, 2f);

        var snap = BugReportLogBuffer.Snapshot();

        Assert.AreEqual("", snap[0].stackTrace, "일반 로그는 스택을 버린다");
        Assert.AreEqual("STACK", snap[1].stackTrace);
    }

    [Test]
    public void Counts_TrackErrorsAndExceptionsSeparately()
    {
        BugReportLogBuffer.Add("a", "", LogType.Error, 1f);
        BugReportLogBuffer.Add("b", "", LogType.Exception, 2f);
        BugReportLogBuffer.Add("c", "", LogType.Warning, 3f);

        Assert.AreEqual(1, BugReportLogBuffer.ErrorCount);
        Assert.AreEqual(1, BugReportLogBuffer.ExceptionCount);
    }

    [Test]
    public void Counts_SurviveRingBufferOverflow()
    {
        BugReportLogBuffer.Add("early", "", LogType.Error, 0f);
        for (int i = 0; i < BugReportLogBuffer.Capacity + 10; i++)
            BugReportLogBuffer.Add($"msg{i}", "", LogType.Log, i);

        Assert.AreEqual(1, BugReportLogBuffer.ErrorCount,
            "버퍼에서 밀려나도 세션 누적 카운트는 남아야 한다");
    }

    [Test]
    public void Add_WithNullCondition_DoesNotStoreNull()
    {
        BugReportLogBuffer.Add(null, null, LogType.Log, 1f);

        var snap = BugReportLogBuffer.Snapshot();

        Assert.AreEqual("", snap[0].condition);
        Assert.AreEqual("", snap[0].stackTrace);
    }
}
#endif
