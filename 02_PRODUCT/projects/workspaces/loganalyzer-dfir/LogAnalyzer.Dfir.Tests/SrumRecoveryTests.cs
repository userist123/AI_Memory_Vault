using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Evaluation 2026-11-05: the SRUM snapshot copy was a "dirty shutdown" database in one run of two (JET -550), SRUM-network was lost and the
/// conclusions changed (63 vs 129 high findings). The ESE logs are now collected beside the database and replayed on the working copy.
/// </summary>
public sealed class SrumRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_srum_" + Guid.NewGuid().ToString("N"));
    public SrumRecoveryTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void The_ese_companions_of_the_database_are_selected_and_nothing_else()
    {
        foreach (var n in new[] { "SRUDB.dat", "SRU.chk", "SRU.log", "SRU01F10.log", "SRUtmp.log", "SRUres00001.jrs", "SRUDB.jfm", "other.log", "SRU.txt" })
            File.WriteAllText(Path.Combine(_dir, n), "x");
        Assert.Equal(["SRU.chk", "SRU.log", "SRU01F10.log", "SRUDB.jfm", "SRUres00001.jrs", "SRUtmp.log"],
            SrumCollector.SrumAuxiliaryFiles(_dir).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Empty(SrumCollector.SrumAuxiliaryFiles(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void Transaction_log_companions_say_why_they_are_not_parsed_on_their_own()
    {
        Assert.Contains("recuperarea", InvestigationPipeline.AuxiliaryNote(SrumCollector.SrumLogSourceType));
        Assert.Contains("Amcache", InvestigationPipeline.AuxiliaryNote("amcache_log"));
        Assert.Null(InvestigationPipeline.AuxiliaryNote("evtx"));
    }

    [Fact]
    public void Review_only_logs_of_the_index_with_their_hash_are_used_and_a_changed_one_stops_the_recovery()
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "srum", TestScopes.Valid());
        var dir = ws.RawDir("SRUM");
        File.WriteAllText(Path.Combine(dir, "SRUDB.dat"), "db");
        foreach (var n in new[] { "SRU.log", "SRU.chk" })
        {
            var f = Path.Combine(dir, n);
            File.WriteAllText(f, n);
            ws.RegisterStored(f, @"C:\Windows\System32\sru\" + n, "SRUM", SrumCollector.SrumLogSourceType, LogAnalyzer.Dfir.Model.TemporalType.Historical, "SrumCollector", "1.0");
        }
        File.WriteAllText(Path.Combine(dir, "SRU00001.log"), "planted, not in the index");
        var (logs, problem) = SrumNetworkParser.VerifiedSrumLogs(dir, default);
        Assert.Equal("", problem);
        Assert.Equal(["SRU.chk", "SRU.log"], logs.Select(l => Path.GetFileName(l.Path)).Order(StringComparer.OrdinalIgnoreCase));

        var log = Path.Combine(dir, "SRU.log");
        File.SetAttributes(log, FileAttributes.Normal);
        File.WriteAllText(log, "changed after collection");
        Assert.Contains("modificat după colectare", SrumNetworkParser.VerifiedSrumLogs(dir, default).Problem);
        Assert.Contains("nu este într-un caz", SrumNetworkParser.VerifiedSrumLogs(_dir, default).Problem);
    }

    /// <summary>Owner machine only: LADFIR_SRUM_DIRTY = a dirty SRUDB.dat copy with no ESE logs beside it. The parser must say it cannot recover and why.</summary>
    [DirtySrumFact]
    public void A_dirty_copy_without_its_logs_fails_with_the_reason()
    {
        var dirty = Environment.GetEnvironmentVariable("LADFIR_SRUM_DIRTY")!;
        var copy = Path.Combine(_dir, "SRUDB.dat");
        File.Copy(dirty, copy);
        var ex = Assert.Throws<InvalidOperationException>(() => SrumNetworkParser.ReadAll(copy));
        Assert.Contains("-550", ex.Message);
        Assert.Contains("nu poate fi recuperată", ex.Message);
    }
}

/// <summary>Runs only where LADFIR_SRUM_DIRTY names an existing dirty SRUDB.dat copy; elsewhere it is reported as skipped, never as passed.</summary>
public sealed class DirtySrumFactAttribute : FactAttribute
{
    public DirtySrumFactAttribute()
    {
        var p = Environment.GetEnvironmentVariable("LADFIR_SRUM_DIRTY");
        if (string.IsNullOrEmpty(p) || !File.Exists(p)) Skip = "SRUM DIRTY COPY NOT PRESENT (set LADFIR_SRUM_DIRTY to a dirty SRUDB.dat copy)";
    }
}
