using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Network;
using LogAnalyzer.Dfir.Parsing;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>P2: parser registry with descriptors, content-based selection and no silently skipped evidence.</summary>
public sealed class ParserRegistryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_registry_" + Guid.NewGuid().ToString("N"));

    public ParserRegistryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static EvidenceItem Item(string type, string stored = "x.bin") =>
        new() { EvidenceId = "EV-1", CaseId = "C", Source = "s", SourceType = type, StoredPath = stored };

    [Fact]
    public void Every_registered_parser_describes_itself_completely()
    {
        var all = WindowsParsers.Registry.Descriptors;
        Assert.Equal(6, all.Count);
        Assert.Equal(all.Count, all.Select(d => d.ParserId).Distinct().Count());
        Assert.All(all, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.ParserId));
            Assert.False(string.IsNullOrWhiteSpace(d.Version));
            Assert.False(string.IsNullOrWhiteSpace(d.Artifact));
            Assert.False(string.IsNullOrWhiteSpace(d.SupportedOs));
            Assert.NotEmpty(d.Fingerprints);
            Assert.NotEmpty(d.FormatVersions);
            Assert.NotEmpty(d.Limitations);
            Assert.False(string.IsNullOrWhiteSpace(d.Validation));
        });
        // Maturity is what the tests actually show, not what the parser claims.
        Assert.Equal(ParserMaturity.Validated, all.Single(d => d.ParserId == "EvtxParser").Status);
        Assert.Equal(ParserMaturity.Tested, all.Single(d => d.ParserId == "SystemHiveExecutionParser").Status);
    }

    [Fact]
    public void Registry_refuses_duplicate_parser_ids()
    {
        Assert.Throws<ArgumentException>(() => new ParserRegistry([new EvtxParser(), new EvtxParser()]));
    }

    [Theory]
    [InlineData("EventLog:Security", "Security.evtx", "EvtxParser")]
    [InlineData("evtx", "a.evtx", "EvtxParser")]
    [InlineData("prefetch", "SETUP.EXE-1.pf", "PrefetchParser")]
    [InlineData("srum", "SRUDB.dat", "SrumNetworkParser")]
    [InlineData("pcapng", "c.pcapng", "PcapngParser")]
    [InlineData("system_hive", "SYSTEM", "SystemHiveExecutionParser")]
    [InlineData("amcache", "Amcache.hve", "AmcacheParser")]
    public void Candidates_follow_the_declared_type(string type, string stored, string expected)
    {
        var c = Assert.Single(WindowsParsers.Registry.Candidates(Item(type, stored)));
        Assert.Equal(expected, c.Descriptor.ParserId);
    }

    [Fact]
    public void Unknown_type_has_no_candidate()
    {
        Assert.Empty(WindowsParsers.Registry.Candidates(Item("live_snapshot", "live.json")));
        Assert.Empty(WindowsParsers.Registry.Candidates(Item("amcache_log", "Amcache.hve.LOG1")));
    }

    [Fact]
    public void Parser_preflight_rejects_content_it_does_not_read()
    {
        var f = Path.Combine(_dir, "c.pcapng");
        File.WriteAllBytes(f, Encoding.Latin1.GetBytes("ElfFile\0").Concat(new byte[100]).ToArray());
        var pre = new PcapngParser().Preflight(Item("pcapng", f), f);
        Assert.Equal(PreflightStatus.FormatMismatch, pre.Status);
        Assert.Equal("evtx", pre.Fingerprint);
    }

    [Fact]
    public void Pipeline_records_parser_status_writes_inventory_and_reports_unparsed_evidence()
    {
        var hive = Path.Combine(_dir, "SYSTEM");
        using (var fs = new FileStream(hive, FileMode.Create, FileAccess.ReadWrite))
        using (var h = DiscUtils.Registry.RegistryHive.Create(fs))
            h.Root.CreateSubKey("Select").SetValue("Current", 1, DiscUtils.Registry.RegistryValueType.Dword);
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "registry");
        var ev = Assert.Single(InvestigationPipeline.Import(ws, [hive]));
        var extra = Path.Combine(ws.RawDir("Other"), "notes.txt");
        File.WriteAllText(extra, "free text");
        var other = ws.RegisterStored(extra, extra, "Other", "operator_notes", TemporalType.Unknown, "test", "1");

        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);

        var pr = Assert.Single(r.Parsing, p => p.EvidenceId == ev.EvidenceId);
        Assert.Equal("TESTED", pr.ParserStatus);
        var skipped = Assert.Single(r.Parsing, p => p.EvidenceId == other.EvidenceId);
        Assert.Equal(EvidenceStatus.SkippedByDesign, skipped.Status);
        Assert.Contains("operator_notes", skipped.Error);

        var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(ws.Root, "Analysis", "parsers.json"))).RootElement;
        Assert.Equal(6, inventory.GetArrayLength());
        Assert.Contains(inventory.EnumerateArray(), d => d.GetProperty("ParserId").GetString() == "AmcacheParser");
    }
}
