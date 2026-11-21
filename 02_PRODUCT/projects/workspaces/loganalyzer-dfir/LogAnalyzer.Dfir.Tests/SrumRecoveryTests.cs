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

    /// <summary>Lab 2026-11-16: a Windows Server 2025 SRUDB.dat has no Network Data Usage table; JetOpenTable answers -1305. That is a gap, not a failure.</summary>
    [Fact]
    public void Only_a_missing_table_is_treated_as_absent_data()
    {
        Assert.True(SrumNetworkParser.IsTableMissing(new InvalidOperationException($"JetOpenTable {SrumNetworkParser.NetworkTable} failed (JET error -1305).")));
        Assert.False(SrumNetworkParser.IsTableMissing(new InvalidOperationException($"JetOpenTable {SrumNetworkParser.NetworkTable} failed (JET error -1206).")));
        Assert.False(SrumNetworkParser.IsTableMissing(new InvalidOperationException("JetAttachDatabase failed (JET error -1305).")));
        Assert.False(SrumNetworkParser.IsTableMissing(new InvalidOperationException($"JetOpenTable {SrumNetworkParser.NetworkTable} failed (JET error -13050).")));
        var gap = SrumNetworkParser.NetworkTableMissingGap("JetOpenTable x failed (JET error -1305).");
        Assert.Equal(LogAnalyzer.Dfir.Model.EvidenceStatus.Partial, gap.Status);
        Assert.Contains(SrumNetworkParser.NetworkTable, gap.Reason);
        Assert.Contains("-1305", gap.Reason);
    }

    /// <summary>Review 2026-11-16 MED: the user name in the notes comes from the target's manifest; a key planted there must not merge two SRUM sets.</summary>
    [Fact]
    public void The_srum_set_key_is_only_the_last_segment_written_by_the_import()
    {
        var real = Guid.NewGuid().ToString("N");
        var planted = Guid.NewGuid().ToString("N");
        var prefix = LogAnalyzer.Dfir.Windows.Acquisition.RemoteCollection.SrumSetKeyPrefix;
        Assert.Equal(real, InvestigationPipeline.SrumSetKey($"H:C:/x/SRUDB.dat; cererea R; colectat de x {prefix}{planted} (administrator) la t; {prefix}{real}"));
        Assert.Equal(real, InvestigationPipeline.SrumSetKey($"Importat de operator din: f; la importul pachetului: H:p; colectat de x; {prefix}{planted}; {prefix}{real}"));
        Assert.Null(InvestigationPipeline.SrumSetKey($"colectat de x; {prefix}{real} (administrator) la t"));
        Assert.Null(InvestigationPipeline.SrumSetKey($"colectat de x; {prefix}{real}0"));
        Assert.Null(InvestigationPipeline.SrumSetKey($"colectat de x; {prefix}../../{real[..26]}"));
        Assert.Null(InvestigationPipeline.SrumSetKey(""));
    }

    /// <summary>Owner machine only: LADFIR_SRUM_NO_NETWORK = a SRUDB.dat without the Network Data Usage table (Windows Server 2025 lab copy).</summary>
    [SrumNoNetworkFact]
    public void A_database_without_the_network_table_parses_with_an_explained_gap()
    {
        var src = Environment.GetEnvironmentVariable("LADFIR_SRUM_NO_NETWORK")!;
        var copy = Path.Combine(_dir, "SRUDB.dat");
        File.Copy(src, copy);
        var (net, _, _, netError) = SrumNetworkParser.ReadAllWithTables(copy);
        Assert.Empty(net);
        Assert.Contains("-1305", netError);
        var item = new LogAnalyzer.Dfir.Model.EvidenceItem { EvidenceId = "E-1", CaseId = "C-1", Source = copy, SourceType = "srum" };
        var r = new SrumNetworkParser().Parse(item, copy, new LogAnalyzer.Dfir.Model.ListSink(), default);
        Assert.NotEqual(LogAnalyzer.Dfir.Model.EvidenceStatus.Failed, r.Status);
        Assert.Contains(r.Gaps, g => g.Artifact == "SRUM Network Data Usage" && g.Reason.Contains("-1305"));
    }
}

/// <summary>Runs only where LADFIR_SRUM_NO_NETWORK names an existing SRUDB.dat without the network table; elsewhere it is skipped, never passed.</summary>
public sealed class SrumNoNetworkFactAttribute : FactAttribute
{
    public SrumNoNetworkFactAttribute()
    {
        var p = Environment.GetEnvironmentVariable("LADFIR_SRUM_NO_NETWORK");
        if (string.IsNullOrEmpty(p) || !File.Exists(p)) Skip = "SRUM COPY WITHOUT NETWORK TABLE NOT PRESENT (set LADFIR_SRUM_NO_NETWORK)";
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
