using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public class EvtxRepairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-evtxrepair-" + Guid.NewGuid().ToString("N"));

    public EvtxRepairTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    /// <summary>Exports this machine's System log (readable without elevation) and damages the records of chunk 1.</summary>
    private (string Clean, string Corrupt) Logs()
    {
        var clean = Path.Combine(_dir, "system.evtx");
        using (var p = Process.Start(new ProcessStartInfo("wevtutil.exe", $"epl System \"{clean}\" /ow:true") { UseShellExecute = false, CreateNoWindow = true })!)
            p.WaitForExit(60_000);
        Assert.True(File.Exists(clean));
        Assert.True(new FileInfo(clean).Length > 4096 + 2 * 65536, "System log too small for the test");
        var corrupt = Path.Combine(_dir, "system_corrupt.evtx");
        var b = File.ReadAllBytes(clean);
        for (int i = 0; i < 400; i++) b[4096 + 65536 + 512 + 3000 + i] = 0xCC;
        File.WriteAllBytes(corrupt, b);
        return (clean, corrupt);
    }

    private static int Count(string path)
    {
        int n = 0;
        using var r = new EventLogReader(path, PathType.FilePath);
        try { for (var e = r.ReadEvent(); e is not null; e = r.ReadEvent()) { e.Dispose(); n++; } }
        catch (EventLogException) { }
        return n;
    }

    [Fact]
    public void A_corrupt_chunk_is_found_and_the_rest_of_the_log_is_recovered()
    {
        var (clean, corrupt) = Logs();
        var before = File.ReadAllBytes(corrupt);

        var bad = Assert.Single(EvtxRepair.Inspect(corrupt), c => !c.Valid && !c.Problem.StartsWith("chunk gol"));
        Assert.Equal(1, bad.Index);
        Assert.Contains("CRC", bad.Problem);

        var r = EvtxRepair.Repair(corrupt, Path.Combine(_dir, "repaired.evtx"));
        int total = Count(clean), recovered = Count(r.OutputPath);
        Assert.True(recovered > 0);
        Assert.Equal(total - (bad.LastRecordId - bad.FirstRecordId + 1), recovered);
        Assert.Equal(before, File.ReadAllBytes(corrupt));            // source untouched
    }

    [Fact]
    public void A_clean_log_survives_the_repair_unchanged_in_content()
    {
        var (clean, _) = Logs();
        var r = EvtxRepair.Repair(clean, Path.Combine(_dir, "roundtrip.evtx"));
        Assert.Equal(Count(clean), Count(r.OutputPath));
        Assert.DoesNotContain(r.Chunks, c => !c.Valid && !c.Problem.StartsWith("chunk gol"));
    }

    [Fact]
    public void Parser_uses_the_repaired_copy_and_reports_the_missing_record_range()
    {
        var (_, corrupt) = Logs();
        var item = new EvidenceItem { EvidenceId = "EV-T", CaseId = "C", Source = "System", SourceType = "evtx", StoredPath = corrupt };
        var sink = new ListSink();
        var res = new EvtxParser().Parse(item, corrupt, sink, CancellationToken.None);
        Assert.Equal(EvidenceStatus.Partial, res.Status);
        Assert.True(res.Records > 0);
        var gap = Assert.Single(res.Gaps);
        Assert.Contains("RecordID", gap.Reason);
        Assert.Contains("nu a fost modificat", gap.Notes);
    }

    [Fact]
    public void Not_an_evtx_file_is_rejected()
    {
        var f = Path.Combine(_dir, "x.evtx");
        File.WriteAllText(f, "not an event log");
        Assert.Throws<InvalidDataException>(() => EvtxRepair.Repair(f, Path.Combine(_dir, "y.evtx")));
    }
}
