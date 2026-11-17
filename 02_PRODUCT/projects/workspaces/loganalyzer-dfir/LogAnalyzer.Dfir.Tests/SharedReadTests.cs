using System.Diagnostics;
using System.Text;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Two app instances share the LIVE case: readers wait for a writer that holds a case file for a moment instead of failing or counting 0.</summary>
public sealed class SharedReadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ladfir_sr_{Guid.NewGuid():N}");

    public SharedReadTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CaseWorkspace NewCase() => CaseWorkspace.Create(Path.Combine(_root, "case"),
        new CaseInfo { CaseId = "CASE-SR", Name = "sr", CreatedAtUtc = DateTimeOffset.UtcNow, Host = "LAB-WS02", Scope = TestScopes.Valid() });

    /// <summary>Holds the file for writing (share Read) for <paramref name="ms"/> ms on another thread; returns once the handle is open.</summary>
    private static Task Hold(string path, int ms)
    {
        var opened = new ManualResetEventSlim();
        var t = Task.Run(() =>
        {
            using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            opened.Set();
            Thread.Sleep(ms);
        });
        opened.Wait();
        return t;
    }

    [Fact]
    public void A_case_opens_while_another_writer_holds_the_evidence_index()
    {
        var ws = NewCase();
        var p = Path.Combine(ws.RawDir("s"), "Security.evtx");
        File.WriteAllText(p, "evtx bytes");
        ws.RegisterStored(p, "Security.evtx", "s", "evtx", TemporalType.Historical, "unit", "1");
        var holder = Hold(ws.EvidenceIndexPath, 300);
        var sw = Stopwatch.StartNew();
        var reopened = CaseWorkspace.Open(ws.Root);
        holder.Wait();
        Assert.True(sw.ElapsedMilliseconds >= 200, "the open should have waited for the writer");
        Assert.Single(reopened.LoadEvidence());
    }

    [Fact]
    public void CountLines_waits_for_a_writer_instead_of_returning_zero()
    {
        var ws = NewCase();
        File.AppendAllText(ws.AppAuditLogPath, "legacy line without chain\n");
        var expected = ws.Recheck().Chains.Audit.LegacyLines;
        Assert.True(expected >= 1);
        var holder = Hold(ws.AppAuditLogPath, 300);
        var got = ws.Recheck().Chains.Audit.LegacyLines;
        holder.Wait();
        Assert.Equal(expected, got);
    }

    [Fact]
    public void SharedRead_does_not_retry_other_errors()
    {
        Assert.Throws<FileNotFoundException>(() => SharedRead.Lines(Path.Combine(_root, "missing.txt")));
        Assert.ThrowsAny<Exception>(() => SharedRead.AllText(_root));
    }

    [Fact]
    public void SharedRead_never_reads_through_an_in_place_rewrite()
    {
        var p = Path.Combine(_root, "rw.txt");
        File.WriteAllText(p, "a\nb\n");
        using var fs = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var reader = Task.Run(() => SharedRead.Lines(p).ToList());
        Thread.Sleep(150);
        Assert.False(reader.IsCompleted);   // still waiting: the handle has write access
        fs.SetLength(0);
        fs.Write("x\ny\nz\n"u8);
        fs.Flush();
        fs.Dispose();
        Assert.Equal(["x", "y", "z"], reader.Result);
    }

    public static IEnumerable<object[]> Inputs() =>
    [
        [new byte[] { }], [Encoding.UTF8.GetBytes("a\r\nb\rc\nd")], [Encoding.UTF8.GetBytes("a\n")], [Encoding.UTF8.GetBytes("\n\r\n")],
        [new byte[] { 0xEF, 0xBB, 0xBF, (byte)'x', (byte)'\r', (byte)'\n', (byte)'y', (byte)'\r' }],
        [Encoding.UTF8.GetBytes("șțî\r\nă")],
        [new byte[] { 0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0 }],
    ];

    [Theory, MemberData(nameof(Inputs))]
    public void SharedRead_lines_split_and_bom_like_File_ReadLines(byte[] bytes)
    {
        var p = Path.Combine(_root, "in.txt");
        File.WriteAllBytes(p, bytes);
        Assert.Equal(File.ReadLines(p).ToList(), SharedRead.Lines(p).ToList());
        Assert.Equal(File.ReadAllText(p), SharedRead.AllText(p));
    }
}
