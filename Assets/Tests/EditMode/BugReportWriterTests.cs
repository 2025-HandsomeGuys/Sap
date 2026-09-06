// @tags: bug-report, test, editmode, file-io
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.IO;
using NUnit.Framework;
using BugReport;

public class BugReportWriterTests
{
    private string _tempRoot;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BugReportTests_" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
    }

    [Test]
    public void FolderName_UsesSortableTimestamp()
    {
        var when = new DateTime(2026, 8, 10, 14, 23, 1);
        Assert.AreEqual("2026-08-10_142301", BugReportWriter.FolderName(when));
    }

    [Test]
    public void Write_CreatesAllFourFiles()
    {
        var data = new BugReportData { memo = "테스트 메모" };
        var when = new DateTime(2026, 8, 10, 14, 23, 1);

        var result = BugReportWriter.Write(data, new byte[] { 1, 2, 3 }, "{}", _tempRoot, when);

        Assert.IsTrue(result.success, result.error);
        string dir = Path.Combine(_tempRoot, "2026-08-10_142301");
        Assert.IsTrue(File.Exists(Path.Combine(dir, "screenshot.png")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "report.json")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "log.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "save.json")));
    }

    [Test]
    public void Write_PreservesKoreanMemoInJson()
    {
        var data = new BugReportData { memo = "벽이 안 막힘" };
        var when = new DateTime(2026, 8, 10, 14, 23, 1);

        BugReportWriter.Write(data, new byte[] { 1 }, "{}", _tempRoot, when);

        string json = File.ReadAllText(
            Path.Combine(_tempRoot, "2026-08-10_142301", "report.json"),
            System.Text.Encoding.UTF8);
        StringAssert.Contains("벽이 안 막힘", json);
    }

    [Test]
    public void Write_WithNullScreenshot_StillWritesOtherFiles()
    {
        var data = new BugReportData();
        var when = new DateTime(2026, 8, 10, 14, 23, 1);

        var result = BugReportWriter.Write(data, null, "{}", _tempRoot, when);

        Assert.IsTrue(result.success, "스크린샷 실패가 리포트 전체를 막으면 안 된다");
        string dir = Path.Combine(_tempRoot, "2026-08-10_142301");
        Assert.IsFalse(File.Exists(Path.Combine(dir, "screenshot.png")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "report.json")));
    }

    [Test]
    public void Write_TwiceInSameSecond_DoesNotOverwrite()
    {
        var when = new DateTime(2026, 8, 10, 14, 23, 1);

        var first = BugReportWriter.Write(new BugReportData { memo = "A" }, null, "{}", _tempRoot, when);
        var second = BugReportWriter.Write(new BugReportData { memo = "B" }, null, "{}", _tempRoot, when);

        Assert.IsTrue(second.success, second.error);
        Assert.AreNotEqual(first.folderPath, second.folderPath,
            "같은 초에 두 번 눌러도 앞 리포트를 덮으면 안 된다");
        Assert.IsTrue(File.Exists(Path.Combine(first.folderPath, "report.json")));
        Assert.IsTrue(File.Exists(Path.Combine(second.folderPath, "report.json")));
    }

    [Test]
    public void Write_WithEmptySaveJson_WritesEmptyObject()
    {
        var when = new DateTime(2026, 8, 10, 14, 23, 1);

        var result = BugReportWriter.Write(new BugReportData(), null, null, _tempRoot, when);

        Assert.IsTrue(result.success, result.error);
        Assert.AreEqual("{}", File.ReadAllText(Path.Combine(result.folderPath, "save.json")));
    }
}
#endif
