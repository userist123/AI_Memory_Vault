using System.IO;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

public sealed class ChainOfCustodyServiceSharedFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LogAnalyzerTests_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Two_custody_services_on_one_file_keep_one_chain()
    {
        var path = Path.Combine(_dir, "custody.log");
        var a = new ChainOfCustodyService(path);
        var b = new ChainOfCustodyService(path);   // both restored GENESIS at construction: stands in for two processes
        for (int k = 0; k < 6; k++) (k % 2 == 0 ? a : b).Append("u", "ACT" + k, "d");
        Assert.True(a.Verify());
        Assert.Equal(6, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void A_garbage_last_line_restarts_from_genesis_and_verify_reports_the_break()
    {
        var path = Path.Combine(_dir, "custody.log");
        var a = new ChainOfCustodyService(path);
        a.Append("u", "ACT", "d");
        File.AppendAllText(path, "{}" + Environment.NewLine);   // a last line without a hash (damaged entry)
        a.Append("u", "ACT2", "d");
        Assert.False(a.Verify());
    }

    [Fact]
    public void A_truncated_last_line_does_not_stop_the_service_and_verify_reports_it_instead_of_throwing()
    {
        var path = Path.Combine(_dir, "custody.log");
        new ChainOfCustodyService(path).Append("u", "ACT", "d");
        File.AppendAllText(path, "{\"Timestamp\":\"2026-11-17T");   // a crash in the middle of a write
        var restarted = new ChainOfCustodyService(path);            // the old constructor threw a JsonException here
        restarted.Append("u", "ACT2", "d");
        Assert.False(restarted.Verify());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
