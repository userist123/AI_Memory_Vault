using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Parsing;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

public class CoreTests
{
    [Fact]
    public void Status_names_follow_spec_and_are_distinct()
    {
        Assert.Equal("NOT_AVAILABLE", EvidenceStatus.NotAvailable.ToSpec());
        Assert.Equal("SKIPPED_BY_DESIGN", EvidenceStatus.SkippedByDesign.ToSpec());
        Assert.Equal("BENIGN/KNOWN", Classification.BenignKnown.ToSpec());
        Assert.Equal("CURRENT_SNAPSHOT", TemporalType.CurrentSnapshot.ToSpec());
        Assert.NotEqual(EvidenceStatus.Empty.ToSpec(), EvidenceStatus.Failed.ToSpec());
    }

    [Fact]
    public void Unknown_timestamp_is_never_replaced_by_now()
    {
        var t = Timestamp.Unknown("garbage");
        Assert.False(t.IsKnown);
        Assert.Equal("", t.UtcIso);
        Assert.Equal("garbage", t.Raw);
        Assert.False(Timestamp.FromFileTime(0, "x").IsKnown);
    }

    [Fact]
    public void Timestamp_keeps_utc_and_renders_local_with_offset()
    {
        var t = Timestamp.FromUtc(new DateTime(2026, 9, 19, 15, 0, 26, DateTimeKind.Utc), "raw", "test");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("GTB Standard Time");
        Assert.Equal("2026-09-19T15:00:26.000Z", t.UtcIso);
        Assert.Equal("2026-09-19T18:00:26.000+03:00", t.LocalIso(zone));
    }

    [Theory]
    [InlineData("8.8.8.8", IpScope.External)]
    [InlineData("10.1.2.3", IpScope.Private)]
    [InlineData("192.168.1.1", IpScope.Private)]
    [InlineData("172.20.0.1", IpScope.Private)]
    [InlineData("0.0.0.0", IpScope.Unspecified)]
    [InlineData("127.0.0.1", IpScope.Loopback)]
    [InlineData("255.255.255.255", IpScope.Broadcast)]
    [InlineData("224.0.0.251", IpScope.Multicast)]
    [InlineData("169.254.1.1", IpScope.LinkLocal)]
    [InlineData("192.0.2.10", IpScope.Documentation)]
    [InlineData("fe80::1", IpScope.LinkLocal)]
    [InlineData("2a02:2f0c:8000:3::1", IpScope.External)]
    [InlineData("fd00::1", IpScope.Private)]
    [InlineData("not-an-ip", IpScope.Invalid)]
    public void Ip_classification(string ip, IpScope expected) => Assert.Equal(expected, IpClassifier.Classify(ip));

    [Fact]
    public void Ioc_extraction_suppresses_noise_but_keeps_it_visible()
    {
        var text = "conn to 185.102.217.35 mask 255.255.255.0 local 192.168.1.5 version 5.1.2.3 sha A7B0F70256E6721023C2717050B45754C8063C79899ADF8588913CCE06B92AFC via https://tzd4is.cyou/x";
        var iocs = IocExtractor.Extract(text, "test").ToList();
        Assert.Contains(iocs, i => i is { Type: "ipv4", Value: "185.102.217.35", SuppressedAsNoise: false });
        Assert.Contains(iocs, i => i is { Value: "255.255.255.0", SuppressedAsNoise: true });
        Assert.Contains(iocs, i => i is { Value: "192.168.1.5", SuppressedAsNoise: true });
        Assert.Contains(iocs, i => i is { Value: "5.1.2.3", SuppressedAsNoise: true, SuppressionReason: "version-number context" });
        Assert.Contains(iocs, i => i.Type == "sha256");
        Assert.Contains(iocs, i => i.Type == "url" && i.Value.StartsWith("https://tzd4is.cyou"));
        Assert.DoesNotContain(iocs, i => i.Type == "sha1"); // 40-hex window inside a SHA-256 is not a SHA-1
    }

    [Fact]
    public void Csv_escapes_and_roundtrips_and_blocks_formula_injection()
    {
        var p = Path.Combine(Path.GetTempPath(), $"ladfir_{Guid.NewGuid():N}.csv");
        try
        {
            using (var w = new CsvWriter(p, ["a", "b", "c"])) w.WriteRow(new object?[] { "x,y", "he said \"hi\"\nnext", "=cmd|' /c calc'!A0" });
            var rows = CsvReader.ReadDicts(p).ToList();
            Assert.Single(rows);
            Assert.Equal("x,y", rows[0]["a"]);
            Assert.Equal("he said \"hi\"\nnext", rows[0]["b"]);
            Assert.StartsWith("'=", rows[0]["c"]);
        }
        finally { File.Delete(p); }
    }

    private sealed class ThrowingParser : EvidenceParserBase
    {
        public override ParserDescriptor Descriptor { get; } = TestDescriptor("Throwing");
        protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult r, CancellationToken ct) => throw new InvalidDataException("boom");
    }

    private sealed class ZeroParser : EvidenceParserBase
    {
        public override ParserDescriptor Descriptor { get; } = TestDescriptor("Zero");
        protected override void ParseCore(EvidenceItem item, string fullPath, IEventSink sink, ParseResult r, CancellationToken ct) { }
    }

    private static ParserDescriptor TestDescriptor(string id) => new()
    {
        ParserId = id, Version = "t", Artifact = "test", SourceTypes = ["t"], Fingerprints = ["unknown"], SupportedOs = "any",
        FormatVersions = ["test"], Limitations = ["test only"], Status = ParserMaturity.Experimental, Validation = "none",
    };

    private static EvidenceItem Item() => new() { EvidenceId = "EV-1", CaseId = "C", Source = "s", SourceType = "t" };

    [Fact] // spec §181 Test M
    public void Error_semantics_failed_vs_empty_vs_not_available()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            Assert.Equal(EvidenceStatus.Failed, new ThrowingParser().Parse(Item(), tmp, new ListSink(), default).Status);
            Assert.Equal(EvidenceStatus.Empty, new ZeroParser().Parse(Item(), tmp, new ListSink(), default).Status);
            Assert.Equal(EvidenceStatus.NotAvailable, new ZeroParser().Parse(Item(), tmp + ".missing", new ListSink(), default).Status);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Case_import_hashes_stores_readonly_and_records_custody()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ladfir_case_{Guid.NewGuid():N}");
        var src = Path.GetTempFileName();
        try
        {
            File.WriteAllText(src, "evidence-bytes");
            var ws = CaseWorkspace.Create(root, new CaseInfo { CaseId = "CASE-T", Name = "t", CreatedAtUtc = DateTimeOffset.UtcNow, Timezone = "GTB Standard Time" , Scope = TestScopes.Valid() });
            var ev = ws.ImportFile(src, "Test:Source", "txt", TemporalType.Historical, "unit", "1");
            var stored = ws.FullPath(ev.StoredPath);

            Assert.Equal("EV-000001", ev.EvidenceId);
            Assert.Equal(Hashing.Sha256File(src), ev.Sha256);
            Assert.True(File.GetAttributes(stored).HasFlag(FileAttributes.ReadOnly));
            Assert.True(File.Exists(src)); // original untouched
            Assert.Single(ws.LoadEvidence());
            Assert.Contains(CsvReader.ReadDicts(ws.CustodyCsvPath), r => r["EvidenceId"] == "EV-000001" && r["Sha256"] == ev.Sha256);

            var reopened = CaseWorkspace.Open(root);
            Assert.Equal("EV-000002", reopened.RegisterStored(Write(reopened, "b.txt"), "b", "s", "txt", TemporalType.Derived, "unit", "1").EvidenceId);
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(root, true); File.Delete(src);
        }
    }

    private static string Write(CaseWorkspace ws, string name)
    {
        var p = Path.Combine(ws.RawDir("s"), name);
        File.WriteAllText(p, "x");
        return p;
    }
}
