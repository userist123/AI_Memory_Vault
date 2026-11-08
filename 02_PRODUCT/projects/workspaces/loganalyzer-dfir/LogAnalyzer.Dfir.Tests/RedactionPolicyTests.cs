using System.Security.Cryptography;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Privacy;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Master plan 6.15: the redaction policy (categories, reversible or not), the preview, the record, the reveal, and the case left untouched.</summary>
public sealed class RedactionPolicyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ladfir_red_" + Guid.NewGuid().ToString("N"));
    public RedactionPolicyTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static List<TimelineEvent> Timeline() =>
    [
        new()
        {
            Time = Timestamp.FromUtc(new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc), "x", "test"), Source = "EventLog:Security", EvidenceId = "EV-1", EventId = "4624",
            Host = "WS01", User = "ion.popescu", RemoteIp = "10.66.0.21", Summary = "Logon ion.popescu de pe 10.66.0.21 la WS01, SID S-1-5-21-1-2-3-1105", Locator = "EventRecordID=7",
        },
    ];

    private CaseWorkspace Case() => InvestigationPipeline.NewCase(Path.Combine(_dir, "cases"), "red", TestScopes.Valid());

    [Fact]
    public void A_category_the_policy_does_not_mask_stays_readable_while_the_others_are_masked()
    {
        var policy = new RedactionPolicy(RedactionMode.Irreversible, [PiiCategory.Users, PiiCategory.Hosts, PiiCategory.Sids]);
        var m = policy.Apply(PiiMasker.FromTimeline(Timeline())).Mask(Timeline()[0]);
        Assert.Contains("10.66.0.21", m.Summary);                                   // addresses kept
        Assert.Equal("10.66.0.21", m.RemoteIp);
        Assert.DoesNotContain("ion.popescu", m.Summary);
        Assert.DoesNotContain("WS01", m.Summary);
        Assert.DoesNotContain("S-1-5-21-1-2-3-1105", m.Summary);
        Assert.StartsWith("Utilizator-", m.User);

        var all = RedactionPolicy.Default.Apply(PiiMasker.FromTimeline(Timeline())).Mask(Timeline()[0]);
        Assert.DoesNotContain("10.66.0.21", all.Summary);
    }

    [Fact]
    public void An_irreversible_export_keeps_no_table_and_cannot_be_revealed()
    {
        var ws = Case();
        var masker = RedactionPolicy.Default.Apply(PiiMasker.FromTimeline(Timeline()));
        var outFile = Path.Combine(_dir, "out.csv");
        File.WriteAllText(outFile, masker.Mask(Timeline()[0].Summary));
        var rec = Redactions.Record(ws, RedactionPolicy.Default, masker, "cronologie CSV", outFile, "tester");
        Assert.Empty(rec.MappingFile);
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(ws.Root, "Reports", "Redactions")), f => f.EndsWith(".dpapi", StringComparison.Ordinal));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outFile))), rec.OutputSha256);
        Assert.Equal(Redactions.CaseState(ws), rec.CaseStateSha256);
        var (orig, msg) = Redactions.Reveal(ws, rec.Id, "Utilizator-1", "test", "tester");
        Assert.Null(orig);
        Assert.Contains("ireversibil", msg);
        Assert.Contains(File.ReadAllLines(ws.AuditChainPath), l => l.Contains("redaction.recorded") && l.Contains(rec.Id));
    }

    [Fact]
    public void A_reversible_export_reveals_one_value_with_a_reason_and_the_audit_never_holds_the_original()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ws = Case();
        var policy = new RedactionPolicy(RedactionMode.Reversible, RedactionPolicy.All);
        var masker = policy.Apply(PiiMasker.FromTimeline(Timeline()));
        var masked = masker.Mask(Timeline()[0]);
        var outFile = Path.Combine(_dir, "out.csv");
        File.WriteAllText(outFile, masked.Summary);
        var rec = Redactions.Record(ws, policy, masker, "cronologie CSV", outFile, "tester");
        Assert.NotEmpty(rec.MappingFile);
        Assert.DoesNotContain("ion.popescu", File.ReadAllText(Path.Combine(ws.Root, "Reports", "Redactions", rec.MappingFile)));   // encrypted, not plain

        Assert.Null(Redactions.Reveal(ws, rec.Id, masked.User, "  ", "tester").Original);       // a reason is required
        var (orig, _) = Redactions.Reveal(ws, rec.Id, masked.User, "cerere procuror nr. 12", "tester");
        Assert.Equal("ion.popescu", orig);
        var audit = File.ReadAllText(ws.AuditChainPath);
        Assert.Contains("redaction.revealed", audit);
        Assert.DoesNotContain("ion.popescu", audit);
    }

    [Fact]
    public void A_modified_table_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ws = Case();
        var policy = new RedactionPolicy(RedactionMode.Reversible, RedactionPolicy.All);
        var masker = policy.Apply(PiiMasker.FromTimeline(Timeline()));
        var u = masker.Mask(Timeline()[0]).User;
        var outFile = Path.Combine(_dir, "o.csv"); File.WriteAllText(outFile, "x");
        var rec = Redactions.Record(ws, policy, masker, "cronologie CSV", outFile, "tester");
        var map = Path.Combine(ws.Root, "Reports", "Redactions", rec.MappingFile);
        File.SetAttributes(map, FileAttributes.Normal);
        var b = File.ReadAllBytes(map); b[^1] ^= 1; File.WriteAllBytes(map, b);
        var (orig, msg) = Redactions.Reveal(ws, rec.Id, u, "motiv", "tester");
        Assert.Null(orig);
        Assert.Contains("modificată", msg);
    }

    [Fact]
    public void Redacting_never_changes_the_case_evidence_or_analysis_and_the_preview_does_not_touch_the_rows()
    {
        var ws = Case();
        var src = Path.Combine(_dir, "ev.txt"); File.WriteAllText(src, "ion.popescu 10.66.0.21");
        InvestigationPipeline.Import(ws, [src]);
        string Snapshot() => string.Join("\n", Directory.EnumerateFiles(ws.Root, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Reports" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "Logs" + Path.DirectorySeparatorChar))
            .Order(StringComparer.Ordinal).Select(f => f + "=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))));
        var before = Snapshot();
        var tl = Timeline();
        var masker = RedactionPolicy.Default.Apply(PiiMasker.FromTimeline(tl));
        var preview = Redactions.Preview(tl, [], masker);
        var row = Assert.Single(preview);
        Assert.Contains("ion.popescu", row.Before);
        Assert.DoesNotContain("ion.popescu", row.After);
        Assert.Equal("ion.popescu", tl[0].User);                                   // the case rows are not changed by masking
        var outFile = Path.Combine(_dir, "o.csv"); File.WriteAllText(outFile, row.After);
        Redactions.Record(ws, RedactionPolicy.Default, masker, "cronologie CSV", outFile, "tester");
        Assert.Equal(before, Snapshot());
        Assert.Single(Redactions.List(ws));
    }

    [Fact]
    public void Two_records_in_the_same_second_get_distinct_ids_and_a_bad_id_is_refused()
    {
        var ws = Case();
        var masker = RedactionPolicy.Default.Apply(PiiMasker.FromTimeline(Timeline()));
        var outFile = Path.Combine(_dir, "o.csv"); File.WriteAllText(outFile, "x");
        var a = Redactions.Record(ws, RedactionPolicy.Default, masker, "k", outFile, "t");
        var b = Redactions.Record(ws, RedactionPolicy.Default, masker, "k", outFile, "t");
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, Redactions.List(ws).Count);
        Assert.Contains("nu este valid", Redactions.Reveal(ws, "..\\x", "a", "m", "t").Message);
    }
}
