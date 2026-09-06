using System;
using System.IO;
using NUnit.Framework;

/// <summary>TelemetryFileSink — 파일 append·파일명 규칙·용량 상한 정리 검증.</summary>
public class TelemetryFileSinkTests
{
    private string _dir;

    [SetUp]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "telemetry_test_" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public void Append_CreatesDirectoryAndWritesOneLinePerEvent()
    {
        var sink = new TelemetryFileSink(_dir, "s.jsonl");
        sink.Append(new[] { "{\"a\":1}", "{\"b\":2}" });

        string[] lines = File.ReadAllLines(sink.FilePath);
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual("{\"a\":1}", lines[0]);
        Assert.AreEqual("{\"b\":2}", lines[1]);
    }

    [Test]
    public void Append_AppendsRatherThanOverwrites()
    {
        var sink = new TelemetryFileSink(_dir, "s.jsonl");
        sink.Append(new[] { "1" });
        sink.Append(new[] { "2" });

        Assert.AreEqual(2, File.ReadAllLines(sink.FilePath).Length);
    }

    [Test]
    public void Append_EmptyOrNull_DoesNotCreateFile()
    {
        var sink = new TelemetryFileSink(_dir, "s.jsonl");
        sink.Append(null);
        sink.Append(Array.Empty<string>());

        Assert.IsFalse(File.Exists(sink.FilePath));
    }

    [Test]
    public void BuildFileName_UsesTimestampAndShortSessionId()
    {
        string name = TelemetryFileSink.BuildFileName(
            new DateTime(2026, 7, 25, 14, 30, 5), "abcdef0123456789");

        Assert.AreEqual("20260725-143005_abcdef01.jsonl", name);
    }

    [Test]
    public void EnforceQuota_DeletesOldestFilesButKeepsCurrentSession()
    {
        Directory.CreateDirectory(_dir);
        // 각 1KB짜리 오래된 파일 3개
        for (int i = 0; i < 3; i++)
        {
            string p = Path.Combine(_dir, $"old{i}.jsonl");
            File.WriteAllText(p, new string('x', 1024));
            File.SetLastWriteTimeUtc(p, new DateTime(2020, 1, 1).AddDays(i));
        }

        var sink = new TelemetryFileSink(_dir, "current.jsonl", maxTotalBytes: 2048);
        sink.Append(new[] { new string('y', 500) });

        sink.EnforceQuota();

        Assert.IsTrue(File.Exists(sink.FilePath), "현재 세션 파일은 지우면 안 된다");
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "old0.jsonl")), "가장 오래된 파일부터 지워야 한다");

        long total = 0;
        foreach (var f in Directory.GetFiles(_dir)) total += new FileInfo(f).Length;
        Assert.LessOrEqual(total, 2048);
    }

    [Test]
    public void Append_ToUnwritablePath_DoesNotThrow()
    {
        // 텔레메트리 실패가 게임을 죽이면 안 된다 — 생성부터 기록까지 어디서도 던지지 않아야 한다
        Assert.DoesNotThrow(() =>
        {
            var sink = new TelemetryFileSink("\0invalid", "s.jsonl");
            sink.Append(new[] { "{}" });
            sink.EnforceQuota();
        });
    }
}
