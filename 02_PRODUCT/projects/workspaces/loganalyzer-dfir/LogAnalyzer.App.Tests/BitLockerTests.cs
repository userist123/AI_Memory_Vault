using System.IO;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using LogAnalyzer.Dfir.Windows.Acquisition;
using LogAnalyzer.Dfir.Windows.Parsers;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Planner #14 E8: BitLocker / device encryption (CFG-DISK-UNENCRYPTED) from a fake WMI source: encrypted volumes give nothing, a volume with protection off gives a finding
/// (OS volume Medium, data volume Low), a namespace that is missing (Windows Home) or access denied gives a gap with the reason and never a finding. Nothing here touches the real machine.
/// </summary>
public sealed class BitLockerTests : IDisposable
{
    private readonly GapsTestKit _k = new();
    public void Dispose() => _k.Dispose();

    private static BitLockerVolume Vol(string letter, int prot, int conv, bool os = false, int? type = null) => new(letter, prot, conv, prot == 1 ? 7 : 0, type ?? (os ? 0 : 1), os);
    private static BitLockerSnapshot Snap(params BitLockerVolume[] v) => new("WS-ALFA-07", "C:", v);

    private (CollectorOutcome Outcome, CaseWorkspace Ws) Collect(BitLockerSnapshot snap, bool elevated = true)
    {
        var ws = _k.Case();
        return (new BitLockerCollector(() => snap).Collect(new CollectorContext { Case = ws, IsElevated = elevated }, default), ws);
    }

    private (List<TimelineEvent> Rows, CollectorOutcome Outcome, CaseWorkspace Ws) Pipeline(BitLockerSnapshot snap)
    {
        var (o, ws) = Collect(snap);
        var sink = new ListSink();
        foreach (var e in o.Evidence) new BitLockerVolumesParser().Parse(e, ws.FullPath(e.StoredPath), sink, default);
        return (sink.Events, o, ws);
    }

    private static ConfigAuditResult Judge(List<TimelineEvent> rows) { int n = 0; return BitLockerAudit.Evaluate(rows, "WS-ALFA-07", () => $"F-{++n:D4}"); }

    [Fact]
    public void Encrypted_volumes_give_no_finding_and_no_gap()
    {
        var (rows, o, _) = Pipeline(Snap(Vol("C:", 1, 1, os: true), Vol("D:", 1, 1)));
        Assert.Equal(EvidenceStatus.Success, o.Status);
        Assert.Equal(2, rows.Count);
        var res = Judge(rows);
        Assert.Empty(res.Findings); Assert.Empty(res.Gaps);
    }

    [Fact]
    public void Protection_off_on_the_os_volume_is_medium_with_the_json_line_as_quote_and_a_data_volume_is_low()
    {
        var (rows, o, ws) = Pipeline(Snap(Vol("C:", 0, 0, os: true), Vol("D:", 0, 0), Vol("E:", 1, 1), Vol("F:", 0, 0, type: 2)));
        var item = Assert.Single(o.Evidence);
        Assert.Equal("bitlocker_volumes_json", item.SourceType);
        Assert.False(string.IsNullOrEmpty(item.Sha256));
        var res = Judge(rows);
        Assert.Empty(res.Gaps);
        var os = Assert.Single(res.Findings, f => f.Severity == Severity.Medium);
        Assert.Equal("CFG-DISK-UNENCRYPTED", os.RuleId);
        Assert.Contains("C:", os.Title);
        Assert.Contains("complet decriptat", os.Description);
        var r = Assert.Single(os.SupportingEvidence);
        Assert.Equal("BitLockerVolume[C:]", r.Locator);
        Assert.Equal(item.EvidenceId, r.EvidenceId);
        Assert.Contains("\"driveLetter\":\"C:\"", r.Quote);
        Assert.Contains("\"protectionStatus\":0", r.Quote);
        Assert.Contains(r.Quote, File.ReadAllText(ws.FullPath(item.StoredPath)).Replace("\r", ""));   // the quote is a line of the stored file
        var data = Assert.Single(res.Findings, f => f.Severity == Severity.Low);
        Assert.Contains("D:", data.Title);
        Assert.DoesNotContain("E:", data.Description);   // protected
        Assert.DoesNotContain("F:", data.Description);   // removable: not judged
        Assert.Equal(2, res.Findings.Count);
    }

    [Fact]
    public void Protection_suspended_on_an_encrypted_os_volume_is_said_so()
    {
        var (rows, _, _) = Pipeline(Snap(Vol("C:", 0, 1, os: true)));
        var f = Assert.Single(Judge(rows).Findings);
        Assert.Contains("suspendată", f.Title);
        Assert.Contains("suspendată", f.Description);
    }

    [Fact]
    public void Unknown_state_gives_nothing()
    {
        var (rows, _, _) = Pipeline(Snap(Vol("C:", 2, 1, os: true)));
        Assert.Empty(Judge(rows).Findings);
    }

    [Fact]
    public void Namespace_missing_is_an_error_with_the_reason_and_stores_nothing()
    {
        var (o, _) = Collect(new BitLockerSnapshot("WS", "C:", null, BitLockerFailure.NamespaceMissing, "Invalid namespace"));
        Assert.Empty(o.Evidence);
        Assert.Equal(EvidenceStatus.Failed, o.Status);
        var err = Assert.Single(o.Errors);
        Assert.Contains("nu există", err);
        Assert.Contains("MicrosoftVolumeEncryption", err);
        Assert.Contains("CFG-DISK-UNENCRYPTED", err);
        Assert.Empty(Judge([]).Findings);
    }

    [Fact]
    public void Access_denied_is_an_error_with_the_reason_and_not_elevated_is_not_available()
    {
        var (o, _) = Collect(new BitLockerSnapshot("WS", "C:", null, BitLockerFailure.AccessDenied, "Access denied"));
        Assert.Empty(o.Evidence);
        Assert.Contains("acces refuzat", Assert.Single(o.Errors));
        var thrown = new BitLockerCollector(() => throw new UnauthorizedAccessException("refuzat")).Collect(new CollectorContext { Case = _k.Case(), IsElevated = true }, default);
        Assert.Contains("acces refuzat", Assert.Single(thrown.Errors));
        var (n, _) = Collect(Snap(), elevated: false);
        Assert.Equal(EvidenceStatus.NotAvailable, n.Status);
        Assert.Empty(n.Evidence);
    }

    [Fact]
    public void A_json_without_the_volume_list_is_a_gap_of_the_parser()
    {
        var ws = _k.Case();
        var path = _k.File("bl.json", "{\"schema\":\"la.bitlocker/1\",\"collectedUtc\":\"2026-11-21T10:00:00Z\",\"machine\":\"WS\"}");
        Assert.True(BitLockerVolumesParser.LooksLikeBitLockerJson(path));
        var item = ws.ImportFile(path, "BitLocker", "bitlocker_volumes_json", TemporalType.CurrentSnapshot, "test", "1");
        var sink = new ListSink();
        var r = new BitLockerVolumesParser().Parse(item, ws.FullPath(item.StoredPath), sink, default);
        Assert.Empty(sink.Events);
        Assert.Contains(r.Gaps, g => g.Reason.Contains("lista de volume"));
    }

    [Fact]
    public void Manual_verification_gives_manage_bde_get_bitlockervolume_and_the_settings_page_with_the_localization_note()
    {
        var (rows, _, ws) = Pipeline(Snap(Vol("C:", 0, 0, os: true)));
        var f = Assert.Single(Judge(rows).Findings);
        var item = ws.LoadEvidence().Single();
        var steps = ManualVerification.For(f.SupportingEvidence[0], item, ws.Root);
        Assert.Contains(steps, s => s.IsCommand && s.Action == "manage-bde -status C:" && s.Compare.Contains("localizate"));
        Assert.Contains(steps, s => s.IsCommand && s.Action.StartsWith("Get-BitLockerVolume -MountPoint C:"));
        Assert.Contains(steps, s => s.Action.Contains("Confidențialitate și securitate → Criptarea dispozitivului") && s.Compare.Contains("limba"));
        Assert.Contains(steps, s => s.Action.StartsWith("Get-FileHash"));
        Assert.All(steps.Where(s => s.IsCommand), s => Assert.DoesNotContain("-Protect", s.Action));   // only reads
    }
}
