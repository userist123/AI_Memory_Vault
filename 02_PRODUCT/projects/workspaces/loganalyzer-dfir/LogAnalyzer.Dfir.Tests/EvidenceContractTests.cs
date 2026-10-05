using System.Buffers.Binary;
using System.Text;
using DiscUtils.Registry;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Integrity;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// P1 evidence contract: the source hash and parser identity are bound to every derived event and finding; the case is
/// re-verified when a report is produced; preflight states use the spec names.
/// </summary>
public sealed class EvidenceContractTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_contract_" + Guid.NewGuid().ToString("N"));

    public EvidenceContractTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private string SystemHiveWithBam()
    {
        var path = Path.Combine(_dir, "SYSTEM");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var hive = RegistryHive.Create(fs);
        hive.Root.CreateSubKey("Select").SetValue("Current", 1, RegistryValueType.Dword);
        var sid = hive.Root.CreateSubKey(@"ControlSet001\Services\bam\State\UserSettings\S-1-5-21-1-2-3-1001");
        var data = new byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(data, new DateTime(2026, 9, 19, 14, 56, 26, DateTimeKind.Utc).ToFileTimeUtc());
        sid.SetValue(@"\Device\HarddiskVolume3\Users\U\Downloads\SETUP.EXE", data, RegistryValueType.Binary);
        sid.SetValue(@"\Device\HarddiskVolume3\Windows\System32\cmd.exe", data, RegistryValueType.Binary);
        return path;
    }

    private (CaseWorkspace ws, EvidenceItem ev) CaseWith(string file)
    {
        var ws = InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "contract");
        return (ws, Assert.Single(InvestigationPipeline.Import(ws, [file])));
    }

    [Fact]
    public void Imported_evidence_records_acquisition_method_and_read_only()
    {
        var (ws, ev) = CaseWith(SystemHiveWithBam());
        Assert.Equal("imported", ev.AcquisitionMethod);
        Assert.True(ev.ReadOnly);
        Assert.True(new FileInfo(ws.FullPath(ev.StoredPath)).IsReadOnly);
    }

    [Fact]
    public void Every_timeline_event_carries_source_hash_and_parser_identity()
    {
        var (ws, ev) = CaseWith(SystemHiveWithBam());
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);

        Assert.Equal(2, r.Timeline.Count);
        Assert.All(r.Timeline, e =>
        {
            Assert.Equal(ev.Sha256, e.SourceSha256);
            Assert.Equal("SystemHiveExecutionParser", e.ParserId);
            Assert.Equal("1.0", e.ParserVersion);
        });
        var csv = File.ReadAllLines(r.TimelineCsv);
        Assert.Contains("SourceSha256", csv[0]);
        Assert.Contains("Parser", csv[0]);
        Assert.All(csv.Skip(1), l => Assert.Contains(ev.Sha256, l));
    }

    [Fact]
    public void Finding_references_are_bound_to_the_evidence_hash_and_unsupported_findings_are_rejected()
    {
        var index = new Dictionary<string, EvidenceItem>
        {
            ["EV-1"] = new() { EvidenceId = "EV-1", CaseId = "C", Source = "s", SourceType = "evtx", Sha256 = "AA11" },
        };
        var supported = new Finding
        {
            FindingId = "F-1", RuleId = "R", Title = "t", Description = "d",
            SupportingEvidence = [new EvidenceRef("EV-1", "EventRecordID=5", "x")],
        };
        var unsupported = new Finding { FindingId = "F-2", RuleId = "R", Title = "t", Description = "d" };
        var dangling = new Finding
        {
            FindingId = "F-3", RuleId = "R", Title = "t", Description = "d",
            SupportingEvidence = [new EvidenceRef("EV-404", "x", "x")],
        };

        var (kept, rejected) = ProvenanceBinder.BindFindings([supported, unsupported, dangling], index);

        var f = Assert.Single(kept);
        Assert.Equal("AA11", Assert.Single(f.SupportingEvidence).Sha256);
        Assert.Equal(2, rejected.Count);
        Assert.Contains(rejected, x => x.Finding.FindingId == "F-2" && x.Reason.Contains("fără probă"));
        Assert.Contains(rejected, x => x.Finding.FindingId == "F-3" && x.Reason.Contains("EV-404"));
    }

    [Fact]
    public void Report_integrity_check_invalidates_results_when_evidence_changed_after_analysis()
    {
        var (ws, ev) = CaseWith(SystemHiveWithBam());
        var r = new InvestigationPipeline().Run(ws, CollectionProfile.Quick, collect: false);
        Assert.True(ReportIntegrity.Check(r).AllIntact);

        var p = ws.FullPath(ev.StoredPath);
        File.SetAttributes(p, FileAttributes.Normal);
        File.AppendAllText(p, "x");

        var check = ReportIntegrity.Check(r);
        Assert.False(check.AllIntact);
        Assert.Contains(check.Items, i => i.EvidenceId == ev.EvidenceId && i.SpecStatus == "MUTATED");
        Assert.Contains("INVALID", check.Banner);

        var pdf = Path.Combine(_dir, "r.pdf");
        InvestigationReportPdf.Write(r, pdf, "test");
        Assert.True(new FileInfo(pdf).Length > 1000);
    }

    [Fact]
    public void Preflight_uses_spec_status_names()
    {
        var (ws, ev) = CaseWith(SystemHiveWithBam());
        var path = ws.FullPath(ev.StoredPath);

        var ok = EvidencePreflight.Check(ev, path);
        Assert.Equal("AVAILABLE", ok.SpecStatus);
        Assert.Equal(File.GetLastWriteTimeUtc(path), ok.FileTimeUtc);

        var noRef = new EvidenceItem { EvidenceId = "EV-N", CaseId = "C", Source = "s", SourceType = "system_hive", StoredPath = ev.StoredPath };
        var unverified = EvidencePreflight.Check(noRef, path);
        Assert.Equal("UNVERIFIED", unverified.SpecStatus);
        Assert.True(unverified.CanParse);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var locked = EvidencePreflight.Check(ev, path);
            Assert.Equal(PreflightStatus.Locked, locked.Status);
            Assert.Equal("READ_ERROR", locked.SpecStatus);
            Assert.False(locked.CanParse);
        }

        Assert.Equal("NO_EVIDENCE", EvidencePreflight.Check(ev, path + ".missing").SpecStatus);
        File.SetAttributes(path, FileAttributes.Normal);
        File.AppendAllText(path, "x");
        Assert.Equal("MUTATED", EvidencePreflight.Check(ev, path).SpecStatus);
    }
}
